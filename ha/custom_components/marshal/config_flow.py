"""Config flow: Marshal URL and API token, checked before the entry is created; reauth for a new token."""

from __future__ import annotations

import logging
from collections.abc import Mapping
from typing import Any

import voluptuous as vol
from homeassistant.config_entries import ConfigFlow, ConfigFlowResult
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.selector import TextSelector, TextSelectorConfig, TextSelectorType
from yarl import URL

from .api import MarshalAuthError, MarshalClient, MarshalConnectionError, MarshalError
from .const import AI_TASK_SCOPE, API_VERSION, CONF_TOKEN, CONF_URL, DOMAIN

_LOGGER = logging.getLogger(__name__)

SCHEMA = vol.Schema(
    {
        vol.Required(CONF_URL): TextSelector(TextSelectorConfig(type=TextSelectorType.URL)),
        vol.Required(CONF_TOKEN): TextSelector(TextSelectorConfig(type=TextSelectorType.PASSWORD)),
    }
)

REAUTH_SCHEMA = vol.Schema({vol.Required(CONF_TOKEN): TextSelector(TextSelectorConfig(type=TextSelectorType.PASSWORD))})


class MarshalConfigFlow(ConfigFlow, domain=DOMAIN):
    """Set up a connection to one Marshal instance."""

    VERSION = 1

    async def async_step_user(self, user_input: dict[str, Any] | None = None) -> ConfigFlowResult:
        """Ask for URL and token, then check version support and the token."""
        errors: dict[str, str] = {}
        if user_input is not None:
            url = _normalize(user_input[CONF_URL])
            if url is None:
                errors["base"] = "invalid_url"
            else:
                await self.async_set_unique_id(url)
                self._abort_if_unique_id_configured()
                errors = await self._check(url, user_input[CONF_TOKEN])
                if not errors:
                    return self.async_create_entry(
                        title=f"Marshal ({URL(url).host})", data={CONF_URL: url, CONF_TOKEN: user_input[CONF_TOKEN]}
                    )

        return self.async_show_form(
            step_id="user", data_schema=self.add_suggested_values_to_schema(SCHEMA, user_input), errors=errors
        )

    async def async_step_reauth(self, entry_data: Mapping[str, Any]) -> ConfigFlowResult:
        """Start reauthentication when Marshal rejects the token or it lacks a scope."""
        return await self.async_step_reauth_confirm()

    async def async_step_reauth_confirm(self, user_input: dict[str, Any] | None = None) -> ConfigFlowResult:
        """Ask for a new token; it must carry the AI task scope, since that is what reauth is for."""
        errors: dict[str, str] = {}
        entry = self._get_reauth_entry()
        if user_input is not None:
            errors = await self._check(entry.data[CONF_URL], user_input[CONF_TOKEN], required_scope=AI_TASK_SCOPE)
            if not errors:
                return self.async_update_reload_and_abort(entry, data_updates={CONF_TOKEN: user_input[CONF_TOKEN]})

        return self.async_show_form(step_id="reauth_confirm", data_schema=REAUTH_SCHEMA, errors=errors)

    async def _check(self, url: str, token: str, required_scope: str | None = None) -> dict[str, str]:
        client = MarshalClient(async_get_clientsession(self.hass), url, token)
        try:
            if not (await client.get_versions()).supports(API_VERSION):
                return {"base": "unsupported_version"}
            scopes = await client.check_token()
            if required_scope is not None and required_scope not in scopes:
                return {"base": "missing_scope"}
        except MarshalAuthError:
            return {"base": "invalid_auth"}
        except MarshalConnectionError:
            return {"base": "cannot_connect"}
        except MarshalError:
            _LOGGER.exception("Unexpected answer from Marshal at %s", url)
            return {"base": "unknown"}
        return {}


def _normalize(value: str) -> str | None:
    """Return `scheme://host[:port][/path]` without a trailing slash, or None when it is not an http(s) URL."""
    try:
        url = URL(value.strip())
    except ValueError:
        return None
    if url.scheme not in ("http", "https") or not url.host:
        return None
    return str(url.with_query(None).with_fragment(None)).rstrip("/")
