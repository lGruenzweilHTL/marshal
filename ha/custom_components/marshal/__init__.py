"""The Marshal integration: forwards Assist conversations to a Marshal instance."""

from __future__ import annotations

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant
from homeassistant.helpers.aiohttp_client import async_get_clientsession

from .api import MarshalClient
from .const import CONF_TOKEN, CONF_URL

PLATFORMS: list[Platform] = [Platform.CONVERSATION]

type MarshalConfigEntry = ConfigEntry[MarshalClient]


async def async_setup_entry(hass: HomeAssistant, entry: MarshalConfigEntry) -> bool:
    """Set up Marshal from a config entry."""
    entry.runtime_data = MarshalClient(async_get_clientsession(hass), entry.data[CONF_URL], entry.data[CONF_TOKEN])
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    return True


async def async_unload_entry(hass: HomeAssistant, entry: MarshalConfigEntry) -> bool:
    """Unload a config entry."""
    return await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
