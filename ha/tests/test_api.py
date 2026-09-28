"""MarshalClient: version discovery, token check, turn streaming."""

import asyncio
from collections.abc import AsyncIterator

import aiohttp
import pytest
from homeassistant.core import HomeAssistant
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from pytest_homeassistant_custom_component.test_util.aiohttp import AiohttpClientMocker

from custom_components.marshal.api import (
    Final,
    MarshalAuthError,
    MarshalClient,
    MarshalConnectionError,
    MarshalResponseError,
    Status,
    TextDelta,
    parse_sse,
)

URL = "http://marshal.local:8080"
TURNS = f"{URL}/api/v1/conversation/turns"


def client(hass: HomeAssistant) -> MarshalClient:
    return MarshalClient(async_get_clientsession(hass), URL + "/", "mrs_token")


async def chunks(*parts: bytes) -> AsyncIterator[bytes]:
    for part in parts:
        yield part


async def collect(stream: AsyncIterator) -> list:
    return [item async for item in stream]


async def test_versions_lists_supported_and_deprecated(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/versions", json={"supported": ["1.0", "2.0"], "deprecated": ["1.0"]})

    versions = await client(hass).get_versions()

    assert versions.supported == ["1.0", "2.0"]
    assert versions.deprecated == ["1.0"]
    assert versions.supports("1.0")
    assert not versions.supports("3.0")


async def test_versions_unreachable_is_connection_error(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/versions", exc=aiohttp.ClientConnectionError())

    with pytest.raises(MarshalConnectionError):
        await client(hass).get_versions()


async def test_versions_non_json_is_response_error(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/versions", text="<html>proxy</html>")

    with pytest.raises(MarshalResponseError):
        await client(hass).get_versions()


@pytest.mark.parametrize("status", [401, 403])
async def test_check_token_rejected_is_auth_error(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker, status: int) -> None:
    aioclient_mock.get(f"{URL}/api/v1/me", status=status)

    with pytest.raises(MarshalAuthError):
        await client(hass).check_token()


async def test_check_token_sends_bearer(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.get(f"{URL}/api/v1/me", json={"id": "x", "displayName": "HA", "roles": [], "scopes": ["conversation:write"]})

    await client(hass).check_token()

    assert aioclient_mock.mock_calls[-1][3]["Authorization"] == "Bearer mrs_token"


async def test_stream_turn_yields_deltas_status_and_final(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    body = (
        'event: status\ndata: {"text":"Checking the printer"}\n\n'
        'event: delta\ndata: {"text":"It is "}\n\n'
        'event: delta\ndata: {"text":"at 40%."}\n\n'
        'event: final\ndata: {"speech":"It is at 40%.","continueConversation":false,"conversationId":"c-1"}\n\n'
    )
    aioclient_mock.post(TURNS, text=body, headers={"Content-Type": "text/event-stream"})

    events = await collect(client(hass).stream_turn({"text": "printer?"}))

    assert events == [
        Status("Checking the printer"),
        TextDelta("It is "),
        TextDelta("at 40%."),
        Final("It is at 40%.", False, "c-1"),
    ]
    method, url, data, headers = aioclient_mock.mock_calls[-1]
    assert data == {"text": "printer?"}
    assert headers["Authorization"] == "Bearer mrs_token"
    assert headers["Accept"] == "text/event-stream"


@pytest.mark.parametrize("status", [401, 403])
async def test_stream_turn_rejected_token_is_auth_error(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker, status: int) -> None:
    aioclient_mock.post(TURNS, status=status)

    with pytest.raises(MarshalAuthError):
        await collect(client(hass).stream_turn({"text": "hi"}))


async def test_stream_turn_problem_carries_code(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, status=400, json={"code": "conversation.text_required", "detail": "text is required."})

    with pytest.raises(MarshalResponseError) as error:
        await collect(client(hass).stream_turn({"text": ""}))

    assert error.value.status == 400
    assert error.value.code == "conversation.text_required"


async def test_stream_turn_timeout_is_connection_error(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, exc=asyncio.TimeoutError())

    with pytest.raises(MarshalConnectionError):
        await collect(client(hass).stream_turn({"text": "hi"}))


async def test_stream_turn_without_final_is_response_error(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text='event: delta\ndata: {"text":"cut"}\n\n')

    with pytest.raises(MarshalResponseError):
        await collect(client(hass).stream_turn({"text": "hi"}))


async def test_parse_sse_joins_split_lines_crlf_and_multiline_data() -> None:
    events = await collect(parse_sse(chunks(b"event: del", b"ta\r\ndata: {\"te", b"xt\":\"a\"}\r\n\r\n", b": ping\n\ndata: one\ndata: two\n\n")))

    assert events == [("delta", '{"text":"a"}'), ("message", "one\ntwo")]


async def test_parse_sse_decodes_utf8_split_across_chunks() -> None:
    encoded = 'data: {"text":"für"}\n\n'.encode()
    split = encoded.index("ü".encode()) + 1

    events = await collect(parse_sse(chunks(encoded[:split], encoded[split:])))

    assert events == [("message", '{"text":"für"}')]
