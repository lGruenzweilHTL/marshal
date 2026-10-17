"""HTTP client for Marshal's public API: version discovery, token check, streamed conversation turns and AI tasks."""

from __future__ import annotations

import codecs
import json
from collections.abc import AsyncIterable, AsyncIterator
from dataclasses import dataclass
from http import HTTPStatus
from typing import Any

import aiohttp

from .const import API_PREFIX

# Marshal answers a text turn within its latency cap (60 s by default); allow a little more before giving up.
TIMEOUT = aiohttp.ClientTimeout(total=75, connect=5)
CHECK_TIMEOUT = aiohttp.ClientTimeout(total=10, connect=5)
# Marshal cancels an AI task after aiTasks.timeoutSeconds (60 s by default) and answers 504; wait a little longer.
AI_TASK_TIMEOUT = aiohttp.ClientTimeout(total=75, connect=5)


class MarshalError(Exception):
    """Base error of the Marshal client."""


class MarshalConnectionError(MarshalError):
    """Marshal is unreachable or did not answer in time."""


class MarshalTimeoutError(MarshalConnectionError):
    """Marshal did not answer in time."""


class MarshalAuthError(MarshalError):
    """The API token was rejected (401) or lacks a scope (403)."""

    def __init__(self, message: str, status: int | None = None) -> None:
        """Initialize with the HTTP status."""
        super().__init__(message)
        self.status = status


class MarshalResponseError(MarshalError):
    """Marshal answered with an error or an unreadable response."""

    def __init__(self, message: str, status: int | None = None, code: str | None = None) -> None:
        """Initialize with the HTTP status and Marshal's problem code, if any."""
        super().__init__(message)
        self.status = status
        self.code = code


@dataclass(frozen=True, slots=True)
class ApiVersions:
    """Versions from `GET /api/versions`."""

    supported: list[str]
    deprecated: list[str]

    def supports(self, version: str) -> bool:
        """Return whether Marshal serves `version`."""
        return version in self.supported


@dataclass(frozen=True, slots=True)
class TextDelta:
    """A reply text chunk (`delta` event)."""

    text: str


@dataclass(frozen=True, slots=True)
class Status:
    """A progress message (`status` event); never spoken."""

    text: str


@dataclass(frozen=True, slots=True)
class Final:
    """The last event of a turn."""

    speech: str
    continue_conversation: bool
    conversation_id: str


type TurnEvent = TextDelta | Status | Final


@dataclass(frozen=True, slots=True)
class AiTaskFile:
    """An attachment of an AI task."""

    filename: str
    mime_type: str
    content: bytes


class MarshalClient:
    """Talks to one Marshal instance with one API token."""

    def __init__(self, session: aiohttp.ClientSession, base_url: str, token: str) -> None:
        """Initialize with HA's shared session."""
        self._session = session
        self._base_url = base_url.rstrip("/")
        self._token = token

    async def get_versions(self) -> ApiVersions:
        """Read the supported API versions (no token needed)."""
        body = await self._get_json("/api/versions", authenticated=False)
        try:
            return ApiVersions(list(body["supported"]), list(body.get("deprecated", [])))
        except (KeyError, TypeError) as err:
            raise MarshalResponseError("Unexpected /api/versions response") from err

    async def check_token(self) -> list[str]:
        """Return the token's scopes; raise MarshalAuthError unless Marshal accepts the token."""
        body = await self._get_json(f"{API_PREFIX}/me", authenticated=True)
        scopes = body.get("scopes") if isinstance(body, dict) else None
        return [str(scope) for scope in scopes] if isinstance(scopes, list) else []

    async def stream_turn(self, request: dict[str, Any]) -> AsyncIterator[TurnEvent]:
        """Send one turn and yield its events; the last one is always a Final."""
        try:
            async with self._session.post(
                f"{self._base_url}{API_PREFIX}/conversation/turns",
                json=request,
                headers={**self._auth(), "Accept": "text/event-stream"},
                timeout=TIMEOUT,
            ) as response:
                await self._raise_for_status(response)
                async for event, data in parse_sse(response.content.iter_any()):
                    parsed = _turn_event(event, data)
                    if parsed is None:
                        continue
                    yield parsed
                    if isinstance(parsed, Final):
                        return
        except (aiohttp.ClientError, TimeoutError) as err:
            raise MarshalConnectionError(str(err) or type(err).__name__) from err
        raise MarshalResponseError("The turn stream ended without a final event")

    async def generate_data(self, task: dict[str, Any], files: list[AiTaskFile]) -> Any:
        """Run one AI task (`task` JSON part plus `attachment` parts) and return Marshal's `data`."""
        with aiohttp.MultipartWriter("form-data") as writer:
            writer.append_json(task).set_content_disposition("form-data", name="task")
            for file in files:
                part = writer.append(file.content, {"Content-Type": file.mime_type})
                part.set_content_disposition("form-data", name="attachment", filename=file.filename)
        try:
            async with self._session.post(
                f"{self._base_url}{API_PREFIX}/ai-tasks", data=writer, headers=self._auth(), timeout=AI_TASK_TIMEOUT
            ) as response:
                await self._raise_for_status(response)
                try:
                    body = await response.json(content_type=None)
                    return body["data"]
                except (ValueError, KeyError, TypeError) as err:
                    raise MarshalResponseError("Unexpected AI task response", response.status) from err
        except TimeoutError as err:
            raise MarshalTimeoutError("Marshal did not answer in time") from err
        except aiohttp.ClientError as err:
            raise MarshalConnectionError(str(err) or type(err).__name__) from err

    async def _get_json(self, path: str, *, authenticated: bool) -> Any:
        try:
            async with self._session.get(
                f"{self._base_url}{path}", headers=self._auth() if authenticated else {}, timeout=CHECK_TIMEOUT
            ) as response:
                await self._raise_for_status(response)
                try:
                    return await response.json(content_type=None)
                except ValueError as err:
                    raise MarshalResponseError(f"{path} did not return JSON", response.status) from err
        except (aiohttp.ClientError, TimeoutError) as err:
            raise MarshalConnectionError(str(err) or type(err).__name__) from err

    def _auth(self) -> dict[str, str]:
        return {"Authorization": f"Bearer {self._token}"}

    @staticmethod
    async def _raise_for_status(response: aiohttp.ClientResponse) -> None:
        if response.status in (HTTPStatus.UNAUTHORIZED, HTTPStatus.FORBIDDEN):
            raise MarshalAuthError(f"Marshal rejected the token ({response.status})", response.status)
        if response.status >= HTTPStatus.BAD_REQUEST:
            code = None
            try:
                problem = await response.json(content_type=None)
                code = problem.get("code") if isinstance(problem, dict) else None
            except ValueError:
                pass
            raise MarshalResponseError(f"Marshal returned {response.status}", response.status, code)


def _turn_event(event: str, data: str) -> TurnEvent | None:
    try:
        payload = json.loads(data)
        match event:
            case "delta":
                return TextDelta(str(payload["text"]))
            case "status":
                return Status(str(payload["text"]))
            case "final":
                return Final(str(payload["speech"]), bool(payload["continueConversation"]), str(payload["conversationId"]))
    except (ValueError, KeyError, TypeError) as err:
        raise MarshalResponseError(f"Malformed {event} event") from err
    return None


async def parse_sse(chunks: AsyncIterable[bytes]) -> AsyncIterator[tuple[str, str]]:
    """Yield `(event, data)` for each server-sent event (unnamed events are `message`)."""
    decoder = codecs.getincrementaldecoder("utf-8")()
    buffer = ""
    event = ""
    data: list[str] = []
    async for chunk in chunks:
        buffer += decoder.decode(chunk)
        # A trailing "\r" may be the first half of "\r\n"; keep it until the next chunk.
        lines = buffer.replace("\r\n", "\n").replace("\r", "\n").split("\n") if not buffer.endswith("\r") else None
        if lines is None:
            continue
        buffer = lines.pop()
        for line in lines:
            if line == "":
                if data:
                    yield event or "message", "\n".join(data)
                event, data = "", []
            elif not line.startswith(":"):
                field, _, value = line.partition(":")
                value = value.removeprefix(" ")
                if field == "event":
                    event = value
                elif field == "data":
                    data.append(value)
