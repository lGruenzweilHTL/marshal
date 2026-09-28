"""Shared fixtures."""

import pytest
from homeassistant.core import HomeAssistant
from homeassistant.setup import async_setup_component


@pytest.fixture(autouse=True)
def auto_enable_custom_integrations(enable_custom_integrations: None) -> None:
    """Load custom_components/marshal in every test."""


@pytest.fixture(autouse=True)
async def setup_homeassistant(hass: HomeAssistant) -> None:
    """Set up the core `homeassistant` component (exposed entities), which `conversation` needs."""
    assert await async_setup_component(hass, "homeassistant", {})
