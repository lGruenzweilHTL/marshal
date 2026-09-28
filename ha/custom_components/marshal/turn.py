"""Maps HA's conversation input (snake_case) to a Marshal turn request (camelCase)."""

from __future__ import annotations

import logging
import re
from typing import Any, Literal

from homeassistant.components.conversation import ConversationInput
from homeassistant.core import HomeAssistant
from homeassistant.helpers import device_registry as dr
from homeassistant.helpers import entity_registry as er

_LOGGER = logging.getLogger(__name__)

# A whitespace-delimited `marshal-followup:<nonce>` token. Only the nonce leaves HA; the rest of the extra system
# prompt is never forwarded.
_FOLLOWUP = re.compile(r"(?<!\S)marshal-followup:([A-Za-z0-9_-]{6,64})(?!\S)")

type InputMode = Literal["voice", "text"]


def followup_ref(extra_system_prompt: str | None) -> str | None:
    """Return the nonce of a `marshal-followup:<nonce>` marker, or None."""
    if not extra_system_prompt:
        return None
    match = _FOLLOWUP.search(extra_system_prompt)
    return match.group(1) if match else None


class TurnMapper:
    """Builds the body of `POST /api/v1/conversation/turns` from the turn fields only (no HA state)."""

    def __init__(self, hass: HomeAssistant) -> None:
        """Initialize with the registries of `hass`."""
        self._hass = hass

    def map(self, user_input: ConversationInput, conversation_id: str) -> dict[str, Any]:
        """Return the request for one turn of the conversation `conversation_id`."""
        return {
            "conversationId": conversation_id,
            "text": user_input.text,
            "language": user_input.language.split("-")[0].lower(),
            "deviceId": user_input.device_id,
            "satelliteId": user_input.satellite_id,
            "areaId": self._area_id(user_input),
            "haUserId": user_input.context.user_id,
            "inputMode": self._input_mode(user_input),
            "followupRef": followup_ref(user_input.extra_system_prompt),
        }

    def _area_id(self, user_input: ConversationInput) -> str | None:
        devices = dr.async_get(self._hass)
        if user_input.device_id and (device := devices.async_get(user_input.device_id)) and device.area_id:
            return device.area_id
        if user_input.satellite_id and (entity := er.async_get(self._hass).async_get(user_input.satellite_id)):
            if entity.area_id:
                return entity.area_id
            if entity.device_id and (device := devices.async_get(entity.device_id)):
                return device.area_id
        return None

    def _input_mode(self, user_input: ConversationInput) -> InputMode:
        """Voice when the pipeline run behind this turn started with wake word or speech-to-text.

        ConversationInput has no speech-to-text flag. The pipeline run that calls the agent shares its Context, so
        the running run is found by context id in assist_pipeline's run registry. That registry is internal: when it
        cannot be read (or the turn did not come from a pipeline), a satellite id means voice.
        """
        stage = self._pipeline_start_stage(user_input.context.id)
        if stage is not None:
            return "voice" if stage in ("wake_word", "stt") else "text"
        return "voice" if user_input.satellite_id else "text"

    def _pipeline_start_stage(self, context_id: str) -> str | None:
        try:
            from homeassistant.components.assist_pipeline.pipeline import KEY_ASSIST_PIPELINE  # noqa: PLC0415

            if (pipeline_data := self._hass.data.get(KEY_ASSIST_PIPELINE)) is None:
                return None
            for runs in pipeline_data.pipeline_runs._pipeline_runs.values():  # noqa: SLF001
                for run in runs.values():
                    if run.context.id == context_id:
                        return str(run.start_stage)
        except (ImportError, AttributeError, TypeError):
            _LOGGER.debug("assist_pipeline run registry not readable; using the satellite heuristic")
        return None
