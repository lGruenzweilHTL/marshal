"""Config flow: Marshal URL and API token, checked before the entry is created."""

from __future__ import annotations

import logging
from typing import Any

import voluptuous as vol
from homeassistant.config_entries import ConfigFlow, ConfigFlowResult
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.selector import TextSelector, TextSelectorConfig, TextSelectorType
from yarl import URL

from .api import MarshalAuthError, MarshalClient, MarshalConnectionError, MarshalError
from .const import API_VERSION, CONF_TOKEN, CONF_URL, DOMAIN

_LOGGER = logging.getLogger(__name__)

SCHEMA = vol.Schema(
    {
        vol.Required(CONF_URL): TextSelector(TextSelectorConfig(type=TextSelectorType.URL)),
        vol.Required(CONF_TOKEN): TextSelector(TextSelectorConfig(type=TextSelectorType.PASSWORD)),
    }
)


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

    async def _check(self, url: str, token: str) -> dict[str, str]:
        client = MarshalClient(async_get_clientsession(self.hass), url, token)
        try:
            if not (await client.get_versions()).supports(API_VERSION):
                return {"base": "unsupported_version"}
            await client.check_token()
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
