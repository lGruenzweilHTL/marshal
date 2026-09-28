"""TurnMapper: HA conversation input -> Marshal turn request."""

from types import SimpleNamespace

import pytest
from homeassistant.components.assist_pipeline.pipeline import KEY_ASSIST_PIPELINE, PipelineStage
from homeassistant.components.conversation import ConversationInput
from homeassistant.core import Context, HomeAssistant
from homeassistant.helpers import area_registry as ar
from homeassistant.helpers import device_registry as dr
from homeassistant.helpers import entity_registry as er
from pytest_homeassistant_custom_component.common import MockConfigEntry

from custom_components.marshal.turn import TurnMapper, followup_ref

CONVERSATION_ID = "01JCONVERSATION"


def user_input(
    *,
    text: str = "wie weit ist der Druck?",
    language: str = "de",
    device_id: str | None = None,
    satellite_id: str | None = None,
    extra_system_prompt: str | None = None,
    context: Context | None = None,
) -> ConversationInput:
    return ConversationInput(
        text=text,
        context=context or Context(user_id="ha-user-1"),
        conversation_id=None,
        device_id=device_id,
        satellite_id=satellite_id,
        language=language,
        agent_id="conversation.marshal",
        extra_system_prompt=extra_system_prompt,
    )


def satellite_device(hass: HomeAssistant, area: str | None = "Küche") -> str:
    entry = MockConfigEntry(domain="esphome")
    entry.add_to_hass(hass)
    area_id = ar.async_get(hass).async_create(area).id if area else None
    device = dr.async_get(hass).async_get_or_create(config_entry_id=entry.entry_id, identifiers={("esphome", "sat-1")})
    if area_id:
        dr.async_get(hass).async_update_device(device.id, area_id=area_id)
    return device.id


def pipeline_run(hass: HomeAssistant, context: Context, start_stage: PipelineStage) -> None:
    run = SimpleNamespace(context=context, start_stage=start_stage)
    hass.data[KEY_ASSIST_PIPELINE] = SimpleNamespace(pipeline_runs=SimpleNamespace(_pipeline_runs={"pipeline": {"run": run}}))


async def test_maps_every_turn_field_to_camel_case(hass: HomeAssistant) -> None:
    device_id = satellite_device(hass)
    area_id = ar.async_get(hass).async_get_area_by_name("Küche").id

    request = TurnMapper(hass).map(user_input(device_id=device_id, satellite_id="assist_satellite.kitchen"), CONVERSATION_ID)

    assert request == {
        "conversationId": CONVERSATION_ID,
        "text": "wie weit ist der Druck?",
        "language": "de",
        "deviceId": device_id,
        "satelliteId": "assist_satellite.kitchen",
        "areaId": area_id,
        "haUserId": "ha-user-1",
        "inputMode": "voice",
        "followupRef": None,
    }


async def test_area_falls_back_to_the_satellite_entity(hass: HomeAssistant) -> None:
    area_id = ar.async_get(hass).async_create("Wohnzimmer").id
    er.async_get(hass).async_get_or_create("assist_satellite", "esphome", "sat-2", suggested_object_id="living")
    er.async_get(hass).async_update_entity("assist_satellite.living", area_id=area_id)

    request = TurnMapper(hass).map(user_input(satellite_id="assist_satellite.living"), CONVERSATION_ID)

    assert request["areaId"] == area_id


async def test_device_without_area_has_no_area(hass: HomeAssistant) -> None:
    device_id = satellite_device(hass, area=None)

    request = TurnMapper(hass).map(user_input(device_id=device_id), CONVERSATION_ID)

    assert request["areaId"] is None


@pytest.mark.parametrize(("language", "expected"), [("de", "de"), ("en-US", "en"), ("DE-at", "de")])
async def test_language_is_the_primary_subtag(hass: HomeAssistant, language: str, expected: str) -> None:
    assert TurnMapper(hass).map(user_input(language=language), CONVERSATION_ID)["language"] == expected


async def test_typed_input_without_pipeline_or_satellite_is_text(hass: HomeAssistant) -> None:
    assert TurnMapper(hass).map(user_input(), CONVERSATION_ID)["inputMode"] == "text"


@pytest.mark.parametrize(("stage", "expected"), [(PipelineStage.STT, "voice"), (PipelineStage.WAKE_WORD, "voice"), (PipelineStage.INTENT, "text")])
async def test_input_mode_follows_the_pipeline_run_start_stage(hass: HomeAssistant, stage: PipelineStage, expected: str) -> None:
    context = Context(user_id="ha-user-1")
    pipeline_run(hass, context, stage)

    assert TurnMapper(hass).map(user_input(context=context), CONVERSATION_ID)["inputMode"] == expected


async def test_app_microphone_without_satellite_is_voice(hass: HomeAssistant) -> None:
    context = Context(user_id="ha-user-1")
    pipeline_run(hass, context, PipelineStage.STT)

    assert TurnMapper(hass).map(user_input(context=context, satellite_id=None), CONVERSATION_ID)["inputMode"] == "voice"


async def test_typed_on_a_satellite_device_follows_the_pipeline(hass: HomeAssistant) -> None:
    context = Context()
    pipeline_run(hass, context, PipelineStage.INTENT)

    request = TurnMapper(hass).map(user_input(context=context, satellite_id="assist_satellite.kitchen"), CONVERSATION_ID)

    assert request["inputMode"] == "text"


async def test_other_runs_do_not_count(hass: HomeAssistant) -> None:
    pipeline_run(hass, Context(), PipelineStage.STT)

    assert TurnMapper(hass).map(user_input(context=Context()), CONVERSATION_ID)["inputMode"] == "text"


async def test_unknown_pipeline_internals_fall_back_to_the_satellite_heuristic(hass: HomeAssistant) -> None:
    hass.data[KEY_ASSIST_PIPELINE] = SimpleNamespace()

    assert TurnMapper(hass).map(user_input(satellite_id="assist_satellite.kitchen"), CONVERSATION_ID)["inputMode"] == "voice"
    assert TurnMapper(hass).map(user_input(), CONVERSATION_ID)["inputMode"] == "text"


@pytest.mark.parametrize(
    ("prompt", "expected"),
    [
        ("marshal-followup:abc123", "abc123"),
        ("Ask the user.\nmarshal-followup:K7x_9-QzLm2 ", "K7x_9-QzLm2"),
        ("The print finished. Do you want the camera snapshot?", None),
        (None, None),
        ("marshal-followup:", None),
        ("marshal-followup:abc", None),
        ("marshal-followup:" + "a" * 65, None),
        ("marshal-followup:abc123!", None),
        ("xmarshal-followup:abc123", None),
    ],
)
def test_followup_ref(prompt: str | None, expected: str | None) -> None:
    assert followup_ref(prompt) == expected


async def test_only_the_marker_is_forwarded(hass: HomeAssistant) -> None:
    request = TurnMapper(hass).map(user_input(extra_system_prompt="secret context\nmarshal-followup:abc123"), CONVERSATION_ID)

    assert request["followupRef"] == "abc123"
    assert "secret context" not in str(request)
