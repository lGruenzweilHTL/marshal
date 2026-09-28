"""Constants for the Marshal integration."""

from typing import Final

DOMAIN: Final = "marshal"

CONF_URL: Final = "url"
CONF_TOKEN: Final = "token"

# The Marshal API version this component speaks. Marshal lists supported versions at /api/versions.
API_VERSION: Final = "1.0"
API_PREFIX: Final = "/api/v1"

SUPPORTED_LANGUAGES: Final = ["de", "en"]
