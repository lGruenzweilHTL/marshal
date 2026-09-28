"""Conversation entity: forwards turns to Marshal and streams the reply into HA's chat log."""

import asyncio
import json

import aiohttp
import pytest
from homeassistant.components import conversation
from homeassistant.core import Context, HomeAssistant
from homeassistant.helpers import chat_session
from homeassistant.helpers import intent
from pytest_homeassistant_custom_component.common import MockConfigEntry
from pytest_homeassistant_custom_component.test_util.aiohttp import AiohttpClientMocker

from custom_components.marshal.const import CONF_TOKEN, CONF_URL, DOMAIN

URL = "http://marshal.local:8080"
TURNS = f"{URL}/api/v1/conversation/turns"
AGENT = "conversation.marshal"


def sse(event: str, data: dict) -> str:
    return f"event: {event}\ndata: {json.dumps(data)}\n\n"


def final(speech: str, conversation_id: str = "c-1", continue_conversation: bool = False) -> str:
    return sse("final", {"speech": speech, "continueConversation": continue_conversation, "conversationId": conversation_id})


@pytest.fixture
async def entry(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> MockConfigEntry:
    """Set up Marshal; depends on aioclient_mock so the shared session is the mocked one."""
    config_entry = MockConfigEntry(domain=DOMAIN, unique_id=URL, data={CONF_URL: URL, CONF_TOKEN: "mrs_token"})
    config_entry.add_to_hass(hass)
    assert await hass.config_entries.async_setup(config_entry.entry_id)
    await hass.async_block_till_done()
    return config_entry


async def converse(hass: HomeAssistant, text: str = "wie weit ist der Druck?", language: str = "de", conversation_id: str | None = None, **kwargs):
    return await conversation.async_converse(hass, text, conversation_id, Context(user_id="ha-user"), language, agent_id=AGENT, **kwargs)


def sent(aioclient_mock: AiohttpClientMocker) -> dict:
    return aioclient_mock.mock_calls[-1][2]


async def test_entity_supports_german_english_and_streaming(hass: HomeAssistant, entry: MockConfigEntry) -> None:
    state = hass.states.get(AGENT)
    assert state is not None
    agent = conversation.async_get_agent_info(hass, AGENT)
    assert agent is not None
    assert agent.supports_streaming
    entity = hass.data["entity_components"]["conversation"].get_entity(AGENT)
    assert entity.supported_languages == ["de", "en"]


async def test_turn_forwarded_with_turn_fields_and_final_speech(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text=final("Der Druck ist bei 40 Prozent.", "conv-7"))

    result = await converse(hass, conversation_id="conv-7", satellite_id="assist_satellite.kitchen")

    body = sent(aioclient_mock)
    assert body["text"] == "wie weit ist der Druck?"
    assert body["language"] == "de"
    assert body["conversationId"] == "conv-7"
    assert body["satelliteId"] == "assist_satellite.kitchen"
    assert body["haUserId"] == "ha-user"
    assert body["inputMode"] == "voice"
    assert set(body) == {"conversationId", "text", "language", "deviceId", "satelliteId", "areaId", "haUserId", "inputMode", "followupRef"}
    assert result.response.speech["plain"]["speech"] == "Der Druck ist bei 40 Prozent."
    assert result.conversation_id == "conv-7"
    assert result.continue_conversation is False


async def test_generated_conversation_id_is_sent_and_returned(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text=final("ok"))

    result = await converse(hass, conversation_id=None)

    assert result.conversation_id
    assert sent(aioclient_mock)["conversationId"] == result.conversation_id


async def test_deltas_streamed_in_order_and_status_not_spoken(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    body = sse("status", {"text": "Frage den Drucker"}) + "".join(sse("delta", {"text": part}) for part in ["Der Druck ", "ist bei ", "40 %."]) + final("Der Druck ist bei 40 %.")
    aioclient_mock.post(TURNS, text=body)
    deltas: list[dict] = []

    with (
        chat_session.async_get_chat_session(hass) as session,
        conversation.async_get_chat_log(hass, session, chat_log_delta_listener=lambda _, delta: deltas.append(delta)) as chat_log,
    ):
        result = await converse(hass, conversation_id=session.conversation_id)

    assert [delta.get("content") for delta in deltas if "content" in delta] == ["Der Druck ", "ist bei ", "40 %."]
    assert result.response.speech["plain"]["speech"] == "Der Druck ist bei 40 %."
    assert "Frage den Drucker" not in json.dumps([content.as_dict() for content in chat_log.content], default=str)
    assert chat_log.content[-1].content == "Der Druck ist bei 40 %."


async def test_continue_conversation_follows_marshal(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text=final("Soll ich die Haustür aufsperren? Sie ist dann offen.", continue_conversation=True))

    result = await converse(hass, "sperr die Haustür auf")

    assert result.continue_conversation is True


async def test_question_mark_alone_does_not_continue(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text=final("Wusstest du das?", continue_conversation=False))

    result = await converse(hass)

    assert result.continue_conversation is False


async def test_final_without_deltas_is_recorded_in_the_chat_log(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text=final("Erledigt."))

    with chat_session.async_get_chat_session(hass) as session, conversation.async_get_chat_log(hass, session) as chat_log:
        await converse(hass, conversation_id=session.conversation_id)

    assert chat_log.content[-1].content == "Erledigt."


async def test_followup_marker_becomes_followup_ref(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, text=final("ok"))

    await converse(hass, "ja", extra_system_prompt="marshal-followup:abc123")

    assert sent(aioclient_mock)["followupRef"] == "abc123"


@pytest.mark.parametrize(
    ("error", "language", "expected"),
    [
        (aiohttp.ClientConnectionError(), "de", "Marshal ist gerade nicht erreichbar."),
        (aiohttp.ClientConnectionError(), "en", "Marshal is not reachable right now."),
        (asyncio.TimeoutError(), "de", "Marshal ist gerade nicht erreichbar."),
    ],
)
async def test_unreachable_is_spoken_in_the_turn_language(
    hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker, error: Exception, language: str, expected: str
) -> None:
    aioclient_mock.post(TURNS, exc=error)

    result = await converse(hass, language=language)

    assert result.response.response_type is intent.IntentResponseType.ERROR
    assert result.response.speech["plain"]["speech"] == expected


@pytest.mark.parametrize(
    ("status", "expected"),
    [
        (401, "Marshal hat den Zugriff abgelehnt. Prüfe das API-Token der Integration."),
        (500, "Marshal konnte das gerade nicht bearbeiten."),
    ],
)
async def test_error_response_is_spoken_briefly(
    hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker, status: int, expected: str
) -> None:
    aioclient_mock.post(TURNS, status=status, json={"code": "x"})

    result = await converse(hass)

    assert result.response.speech["plain"]["speech"] == expected


async def test_unknown_language_errors_fall_back_to_english(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(TURNS, exc=aiohttp.ClientConnectionError())

    result = await converse(hass, language="fr")

    assert result.response.speech["plain"]["speech"] == "Marshal is not reachable right now."


async def test_unload_removes_the_agent(hass: HomeAssistant, entry: MockConfigEntry) -> None:
    assert await hass.config_entries.async_unload(entry.entry_id)
    await hass.async_block_till_done()

    assert hass.states.get(AGENT).state == "unavailable"
