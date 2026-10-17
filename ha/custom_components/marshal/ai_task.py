"""AI Task entity: forwards generate-data tasks (instructions, structure, attachments) to Marshal."""

from __future__ import annotations

import logging
from http import HTTPStatus
from typing import Any, override

from homeassistant.components import ai_task, conversation
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError
from homeassistant.helpers import llm
from homeassistant.helpers.device_registry import DeviceEntryType, DeviceInfo
from homeassistant.helpers.entity_platform import AddConfigEntryEntitiesCallback
from probatio import to_openapi

from . import MarshalConfigEntry
from .api import AiTaskFile, MarshalAuthError, MarshalConnectionError, MarshalError, MarshalResponseError, MarshalTimeoutError
from .const import AI_TASK_SCOPE, DOMAIN

_LOGGER = logging.getLogger(__name__)

# Marshal's problem codes with their own messages; other 4xx are "request_rejected", 5xx "server_error".
_CODES: dict[str, str] = {
    "ai_task.timeout": "timeout",
    "ai_task.daily_cap": "daily_cap",
    "ai_task.no_vision_model": "no_vision_model",
    "ai_task.invalid_output": "invalid_output",
}


async def async_setup_entry(
    hass: HomeAssistant, entry: MarshalConfigEntry, async_add_entities: AddConfigEntryEntitiesCallback
) -> None:
    """Set up the Marshal AI Task entity."""
    async_add_entities([MarshalAITaskEntity(entry)])


class MarshalAITaskEntity(ai_task.AITaskEntity):
    """Marshal as an AI Task provider: one headless Marshal run per task."""

    _attr_has_entity_name = True
    _attr_name = None
    _attr_supported_features = ai_task.AITaskEntityFeature.GENERATE_DATA | ai_task.AITaskEntityFeature.SUPPORT_ATTACHMENTS

    def __init__(self, entry: MarshalConfigEntry) -> None:
        """Initialize for one config entry."""
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_ai_task"
        self._attr_device_info = DeviceInfo(
            identifiers={(DOMAIN, entry.entry_id)}, name="Marshal", manufacturer="Marshal", entry_type=DeviceEntryType.SERVICE
        )

    @override
    async def _async_generate_data(self, task: ai_task.GenDataTask, chat_log: conversation.ChatLog) -> ai_task.GenDataTaskResult:
        """Send only the task's fields and files; Marshal answers with a string or an object matching the structure."""
        request: dict[str, Any] = {"taskName": task.name, "instructions": task.instructions, "language": self.hass.config.language}
        if task.structure is not None:
            request["structure"] = to_openapi(task.structure, custom_serializer=llm.selector_serializer)
        files = await self.hass.async_add_executor_job(_read_files, task.attachments or [])
        try:
            data = await self._entry.runtime_data.generate_data(request, files)
        except MarshalError as err:
            raise self._translated(err) from err
        return ai_task.GenDataTaskResult(conversation_id=chat_log.conversation_id, data=data)

    def _translated(self, err: MarshalError) -> HomeAssistantError:
        key, placeholders = _error_key(err)
        if key in ("missing_scope", "invalid_auth"):
            self._entry.async_start_reauth(self.hass)
        if key in ("server_error", "request_rejected"):
            _LOGGER.warning("Marshal AI task failed: %s", err)
        return HomeAssistantError(translation_domain=DOMAIN, translation_key=key, translation_placeholders=placeholders)


def _error_key(err: MarshalError) -> tuple[str, dict[str, str]]:
    match err:
        case MarshalAuthError(status=HTTPStatus.FORBIDDEN):
            return "missing_scope", {"scope": AI_TASK_SCOPE}
        case MarshalAuthError():
            return "invalid_auth", {}
        case MarshalTimeoutError():
            return "timeout", {}
        case MarshalConnectionError():
            return "cannot_connect", {}
        case MarshalResponseError(status=HTTPStatus.NOT_FOUND):
            return "not_supported", {}
        case MarshalResponseError(code=code) if code in _CODES:
            return _CODES[code], {}
        case MarshalResponseError(status=status) if status is not None and status < HTTPStatus.INTERNAL_SERVER_ERROR:
            return "request_rejected", {"status": str(status), "code": err.code or "-"}
        case _:
            status = getattr(err, "status", None)
            return "server_error", {"status": str(status or "-")}


def _read_files(attachments: list[conversation.Attachment]) -> list[AiTaskFile]:
    return [AiTaskFile(attachment.path.name, attachment.mime_type, attachment.path.read_bytes()) for attachment in attachments]
