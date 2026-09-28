"""Integration metadata."""

import json
from pathlib import Path

from homeassistant.core import HomeAssistant
from homeassistant.loader import async_get_integration

from custom_components.marshal.const import DOMAIN

COMPONENT = Path(__file__).parent.parent / "custom_components" / DOMAIN


async def test_integration_loads_with_config_flow(hass: HomeAssistant) -> None:
    integration = await async_get_integration(hass, DOMAIN)

    assert integration.config_flow
    assert "conversation" in integration.dependencies


def test_translations_cover_the_same_keys() -> None:
    def keys(node: object, prefix: str = "") -> set[str]:
        if isinstance(node, dict):
            return {key for name, child in node.items() for key in keys(child, f"{prefix}{name}.")}
        return {prefix.rstrip(".")}

    strings = keys(json.loads((COMPONENT / "strings.json").read_text()))
    for language in ("en", "de"):
        assert keys(json.loads((COMPONENT / "translations" / f"{language}.json").read_text())) == strings, language
