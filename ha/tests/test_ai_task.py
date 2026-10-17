"""AI Task entity: forwards generate-data tasks to Marshal's AI task endpoint."""

import email
import email.policy
import json
from pathlib import Path

import aiohttp
import pytest
import voluptuous as vol
from homeassistant.components import ai_task, conversation
from homeassistant.config_entries import SOURCE_REAUTH
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError
from homeassistant.helpers import selector
from homeassistant.helpers.chat_session import async_get_chat_session
from pytest_homeassistant_custom_component.common import MockConfigEntry
from pytest_homeassistant_custom_component.test_util.aiohttp import AiohttpClientMocker

from custom_components.marshal.const import CONF_TOKEN, CONF_URL, DOMAIN

URL = "http://marshal.local:8080"
AI_TASKS = f"{URL}/api/v1/ai-tasks"
ENTITY = "ai_task.marshal"


@pytest.fixture
async def entry(hass: HomeAssistant, aioclient_mock: AiohttpClientMocker) -> MockConfigEntry:
    """Set up Marshal; depends on aioclient_mock so the shared session is the mocked one."""
    config_entry = MockConfigEntry(domain=DOMAIN, unique_id=URL, data={CONF_URL: URL, CONF_TOKEN: "mrs_token"})
    config_entry.add_to_hass(hass)
    assert await hass.config_entries.async_setup(config_entry.entry_id)
    await hass.async_block_till_done()
    return config_entry


async def generate(hass: HomeAssistant, structure: vol.Schema | None = None, attachments: list[conversation.Attachment] | None = None):
    entity = hass.data[ai_task.DATA_COMPONENT].get_entity(ENTITY)
    with async_get_chat_session(hass) as session:
        return await entity.internal_async_generate_data(
            session,
            ai_task.GenDataTask(name="doorbell", instructions="Who is at the door?", structure=structure, attachments=attachments),
        )


async def parts(aioclient_mock: AiohttpClientMocker) -> list[email.message.Message]:
    writer = aioclient_mock.mock_calls[-1][2]
    body = await writer.as_bytes()
    message = email.message_from_bytes(f"Content-Type: {writer.content_type}\r\n\r\n".encode() + body, policy=email.policy.default)
    return list(message.iter_parts())


async def test_entity_generates_data_and_supports_attachments(hass: HomeAssistant, entry: MockConfigEntry) -> None:
    state = hass.states.get(ENTITY)
    assert state is not None
    entity = hass.data[ai_task.DATA_COMPONENT].get_entity(ENTITY)
    assert entity.supported_features == ai_task.AITaskEntityFeature.GENERATE_DATA | ai_task.AITaskEntityFeature.SUPPORT_ATTACHMENTS


async def test_task_with_structure_and_attachment_sends_one_multipart_request(
    hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker, tmp_path: Path
) -> None:
    hass.config.language = "de"
    image = tmp_path / "snapshot.jpg"
    image.write_bytes(b"\xff\xd8\xff\xe0jpeg")
    aioclient_mock.post(AI_TASKS, json={"data": {"visitor": "courier", "parcel": True}, "runId": "r-1"})
    structure = vol.Schema(
        {
            vol.Required("visitor"): selector.TextSelector(),
            vol.Optional("parcel"): selector.BooleanSelector(),
        }
    )

    result = await generate(hass, structure, [conversation.Attachment("media-source://camera/camera.door", "image/jpeg", image)])

    assert len(aioclient_mock.mock_calls) == 1
    assert aioclient_mock.mock_calls[-1][3]["Authorization"] == "Bearer mrs_token"
    task, attachment = await parts(aioclient_mock)
    assert task.get_param("name", header="content-disposition") == "task"
    sent = json.loads(task.get_content())
    assert sent["taskName"] == "doorbell"
    assert sent["instructions"] == "Who is at the door?"
    assert sent["language"] == "de"
    assert sent["structure"]["type"] == "object"
    assert sent["structure"]["properties"]["visitor"] == {"type": "string"}
    assert sent["structure"]["properties"]["parcel"] == {"type": "boolean"}
    assert sent["structure"]["required"] == ["visitor"]
    assert set(sent) == {"taskName", "instructions", "language", "structure"}
    assert attachment.get_param("name", header="content-disposition") == "attachment"
    assert attachment.get_content_type() == "image/jpeg"
    assert attachment.get_payload(decode=True) == b"\xff\xd8\xff\xe0jpeg"
    assert result.data == {"visitor": "courier", "parcel": True}
    assert result.conversation_id


async def test_text_task_returns_string_without_structure(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(AI_TASKS, json={"data": "Nobody is there.", "runId": "r-2"})

    result = await generate(hass)

    (task,) = await parts(aioclient_mock)
    assert "structure" not in json.loads(task.get_content())
    assert result.data == "Nobody is there."


async def test_forbidden_raises_scope_error_and_starts_reauth(hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker) -> None:
    aioclient_mock.post(AI_TASKS, status=403)

    with pytest.raises(HomeAssistantError) as error:
        await generate(hass)

    assert error.value.translation_key == "missing_scope"
    assert "aitasks:write" in str(error.value)
    await hass.async_block_till_done()
    flows = hass.config_entries.flow.async_progress()
    assert [flow["context"]["source"] for flow in flows] == [SOURCE_REAUTH]


@pytest.mark.parametrize(
    ("mock", "key"),
    [
        ({"exc": aiohttp.ClientConnectionError()}, "cannot_connect"),
        ({"exc": TimeoutError()}, "timeout"),
        ({"status": 404}, "not_supported"),
        ({"status": 429, "json": {"code": "ai_task.daily_cap"}}, "daily_cap"),
        ({"status": 422, "json": {"code": "ai_task.no_vision_model"}}, "no_vision_model"),
        ({"status": 400, "json": {"code": "ai_task.invalid_request"}}, "request_rejected"),
        ({"status": 504, "json": {"code": "ai_task.timeout"}}, "timeout"),
        ({"status": 502, "json": {"code": "ai_task.invalid_output"}}, "invalid_output"),
        ({"status": 500}, "server_error"),
    ],
)
async def test_errors_raise_translated_errors(
    hass: HomeAssistant, entry: MockConfigEntry, aioclient_mock: AiohttpClientMocker, mock: dict, key: str
) -> None:
    aioclient_mock.post(AI_TASKS, **mock)

    with pytest.raises(HomeAssistantError) as error:
        await generate(hass)

    assert error.value.translation_domain == DOMAIN
    assert error.value.translation_key == key
    assert hass.config_entries.flow.async_progress() == []
