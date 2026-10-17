"""Config flow: URL + token, checked against /api/versions and /api/v1/me."""

import json
from collections.abc import Iterator
from pathlib import Path
from unittest.mock import patch

import aiohttp
import pytest
from homeassistant import config_entries
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResultType
from pytest_homeassistant_custom_component.common import MockConfigEntry
from pytest_homeassistant_custom_component.test_util.aiohttp import AiohttpClientMocker

from custom_components.marshal.const import CONF_TOKEN, CONF_URL, DOMAIN

URL = "http://marshal.local:8080"
ME = {"id": "x", "displayName": "HA", "roles": [], "scopes": ["conversation:write"]}
TRANSLATIONS = Path(__file__).parent.parent / "custom_components" / DOMAIN / "translations"


@pytest.fixture(autouse=True)
def skip_entry_setup() -> Iterator[None]:
    with patch("custom_components.marshal.async_setup_entry", return_value=True):
        yield


async def submit(hass: HomeAssistant, url: str = URL + "/", token: str = "mrs_token"):
    result = await hass.config_entries.flow.async_init(DOMAIN, context={"source": config_entries.SOURCE_USER})
    assert result["type"] is FlowResultType.FORM
    return await hass.config_entries.flow.async_configure(result["flow_id"], {CONF_URL: url, CONF_TOKEN: token})


async def test_supported_version_and_valid_token_creates_entry(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0"], "deprecated": []})
    aioclient_mock.get(f"{URL}/api/v1/me", json=ME)

    result = await submit(hass)

    assert result["type"] is FlowResultType.CREATE_ENTRY
    assert result["data"] == {CONF_URL: URL, CONF_TOKEN: "mrs_token"}
    assert result["title"] == "Marshal (marshal.local)"


async def test_unsupported_version_shows_error_and_creates_nothing(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["2.0"], "deprecated": []})

    result = await submit(hass)

    assert result["type"] is FlowResultType.FORM
    assert result["errors"] == {"base": "unsupported_version"}
    assert not hass.config_entries.async_entries(DOMAIN)
    message = json.loads((TRANSLATIONS / "en.json").read_text())["config"]["error"]["unsupported_version"]
    assert message == "Marshal does not support this integration's API version"


async def test_unreachable_shows_cannot_connect(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/versions", exc=aiohttp.ClientConnectionError())

    result = await submit(hass)

    assert result["errors"] == {"base": "cannot_connect"}


@pytest.mark.parametrize("status", [401, 403])
async def test_rejected_token_shows_invalid_auth(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker, status: int) -> None:
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0"], "deprecated": []})
    aioclient_mock.get(f"{URL}/api/v1/me", status=status)

    result = await submit(hass)

    assert result["errors"] == {"base": "invalid_auth"}


async def test_invalid_url_shows_error(hass: HomeAssistant) -> None:
    result = await submit(hass, url="marshal.local")

    assert result["errors"] == {"base": "invalid_url"}


async def test_same_instance_twice_aborts(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    MockConfigEntry(domain=DOMAIN, unique_id=URL, data={CONF_URL: URL, CONF_TOKEN: "t"}).add_to_hass(hass)
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0"], "deprecated": []})
    aioclient_mock.get(f"{URL}/api/v1/me", json=ME)

    result = await submit(hass)

    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "already_configured"


async def start_reauth(hass: HomeAssistant) -> tuple[MockConfigEntry, dict]:
    entry = MockConfigEntry(domain=DOMAIN, unique_id=URL, data={CONF_URL: URL, CONF_TOKEN: "old_token"})
    entry.add_to_hass(hass)
    result = await entry.start_reauth_flow(hass)
    assert result["type"] is FlowResultType.FORM
    assert result["step_id"] == "reauth_confirm"
    return entry, result


async def test_reauth_valid_token_with_scope_updates_entry(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    entry, result = await start_reauth(hass)
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0"], "deprecated": []})
    aioclient_mock.get(f"{URL}/api/v1/me", json={**ME, "scopes": ["conversation:write", "aitasks:write"]})

    result = await hass.config_entries.flow.async_configure(result["flow_id"], {CONF_TOKEN: "new_token"})

    assert result["type"] is FlowResultType.ABORT
    assert result["reason"] == "reauth_successful"
    assert entry.data == {CONF_URL: URL, CONF_TOKEN: "new_token"}
    assert aioclient_mock.mock_calls[-1][3]["Authorization"] == "Bearer new_token"


@pytest.mark.parametrize("status", [401, 403])
async def test_reauth_rejected_token_shows_invalid_auth(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker, status: int) -> None:
    entry, result = await start_reauth(hass)
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0"], "deprecated": []})
    aioclient_mock.get(f"{URL}/api/v1/me", status=status)

    result = await hass.config_entries.flow.async_configure(result["flow_id"], {CONF_TOKEN: "bad_token"})

    assert result["type"] is FlowResultType.FORM
    assert result["errors"] == {"base": "invalid_auth"}
    assert entry.data[CONF_TOKEN] == "old_token"


async def test_reauth_token_without_ai_task_scope_shows_missing_scope(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    entry, result = await start_reauth(hass)
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0"], "deprecated": []})
    aioclient_mock.get(f"{URL}/api/v1/me", json=ME)

    result = await hass.config_entries.flow.async_configure(result["flow_id"], {CONF_TOKEN: "narrow_token"})

    assert result["errors"] == {"base": "missing_scope"}
    assert entry.data[CONF_TOKEN] == "old_token"
