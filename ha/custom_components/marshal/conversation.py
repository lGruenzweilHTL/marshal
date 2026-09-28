"""Conversation entity: forwards each Assist turn to Marshal and streams the reply into HA's chat log."""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from typing import Literal, override

from homeassistant.components import conversation
from homeassistant.core import HomeAssistant
from homeassistant.helpers import intent
from homeassistant.helpers.device_registry import DeviceEntryType, DeviceInfo
from homeassistant.helpers.entity_platform import AddConfigEntryEntitiesCallback

from . import MarshalConfigEntry
from .api import Final, MarshalAuthError, MarshalClient, MarshalConnectionError, MarshalError, TextDelta
from .const import DOMAIN, SUPPORTED_LANGUAGES
from .turn import TurnMapper

_LOGGER = logging.getLogger(__name__)

# Short spoken errors; the log has the details.
_ERRORS: dict[str, dict[str, str]] = {
    "unreachable": {"de": "Marshal ist gerade nicht erreichbar.", "en": "Marshal is not reachable right now."},
    "auth": {
        "de": "Marshal hat den Zugriff abgelehnt. Prüfe das API-Token der Integration.",
        "en": "Marshal refused access. Check the integration's API token.",
    },
    "failed": {"de": "Marshal konnte das gerade nicht bearbeiten.", "en": "Marshal could not handle that right now."},
}


async def async_setup_entry(
    hass: HomeAssistant, entry: MarshalConfigEntry, async_add_entities: AddConfigEntryEntitiesCallback
) -> None:
    """Set up the Marshal conversation agent."""
    async_add_entities([MarshalConversationEntity(entry)])


class MarshalConversationEntity(conversation.ConversationEntity, conversation.AbstractConversationAgent):
    """Marshal as an Assist conversation agent."""

    _attr_has_entity_name = True
    _attr_name = None
    _attr_supports_streaming = True

    def __init__(self, entry: MarshalConfigEntry) -> None:
        """Initialize for one config entry."""
        self._entry = entry
        self._attr_unique_id = entry.entry_id
        self._attr_device_info = DeviceInfo(
            identifiers={(DOMAIN, entry.entry_id)}, name="Marshal", manufacturer="Marshal", entry_type=DeviceEntryType.SERVICE
        )

    @property
    @override
    def supported_languages(self) -> list[str] | Literal["*"]:
        """Marshal replies in German and English."""
        return SUPPORTED_LANGUAGES

    @override
    async def async_added_to_hass(self) -> None:
        """Register as agent of the config entry."""
        await super().async_added_to_hass()
        conversation.async_set_agent(self.hass, self._entry, self)

    @override
    async def async_will_remove_from_hass(self) -> None:
        """Unregister the agent."""
        conversation.async_unset_agent(self.hass, self._entry)
        await super().async_will_remove_from_hass()

    @override
    async def _async_handle_message(
        self, user_input: conversation.ConversationInput, chat_log: conversation.ChatLog
    ) -> conversation.ConversationResult:
        """Forward the turn and stream Marshal's reply into the chat log."""
        client: MarshalClient = self._entry.runtime_data
        request = TurnMapper(self.hass).map(user_input, chat_log.conversation_id)
        outcome: list[Final] = []
        streamed = False
        try:
            async for content in chat_log.async_add_delta_content_stream(
                self.entity_id, _deltas(client.stream_turn(request), outcome)
            ):
                streamed = streamed or bool(getattr(content, "content", None))
        except MarshalError as err:
            return self._error(user_input, chat_log, err)

        final = outcome[0]
        if not streamed:
            chat_log.async_add_assistant_content_without_tools(
                conversation.AssistantContent(agent_id=self.entity_id, content=final.speech)
            )
        response = intent.IntentResponse(language=user_input.language)
        response.async_set_speech(final.speech)
        # Marshal decides whether it asked something; HA's chat-log heuristic (trailing "?") is not used.
        return conversation.ConversationResult(
            response=response, conversation_id=chat_log.conversation_id, continue_conversation=final.continue_conversation
        )

    def _error(
        self, user_input: conversation.ConversationInput, chat_log: conversation.ChatLog, err: MarshalError
    ) -> conversation.ConversationResult:
        kind = "unreachable" if isinstance(err, MarshalConnectionError) else "auth" if isinstance(err, MarshalAuthError) else "failed"
        _LOGGER.warning("Marshal turn failed (%s): %s", kind, err)
        language = user_input.language.split("-")[0].lower()
        message = _ERRORS[kind].get(language, _ERRORS[kind]["en"])
        chat_log.async_add_assistant_content_without_tools(conversation.AssistantContent(agent_id=self.entity_id, content=message))
        response = intent.IntentResponse(language=user_input.language)
        response.async_set_error(intent.IntentResponseErrorCode.UNKNOWN, message)
        return conversation.ConversationResult(response=response, conversation_id=chat_log.conversation_id)


async def _deltas(events: AsyncIterator, outcome: list[Final]) -> AsyncIterator[conversation.AssistantContentDeltaDict]:
    """Turn Marshal's events into chat-log deltas; status events are dropped, the final event is kept aside."""
    yield {"role": "assistant"}
    async for event in events:
        if isinstance(event, TextDelta) and event.text:
            yield {"content": event.text}
        elif isinstance(event, Final):
            outcome.append(event)
