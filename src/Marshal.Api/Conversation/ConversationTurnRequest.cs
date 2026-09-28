using Marshal.Abstractions.Execution;

namespace Marshal.Api.Conversation;

/// <summary>
/// A conversation turn.
/// </summary>
/// <param name="ConversationId">Token clients: their own conversation id (required). Browser sessions: Marshal's session id
/// from an earlier final event; omitted starts a new conversation.</param>
/// <param name="Text">What the user said or typed.</param>
/// <param name="Language">Request language (<c>de</c> or <c>en</c>); the reply uses it.</param>
/// <param name="DeviceId">Home Assistant device id.</param>
/// <param name="SatelliteId">Satellite entity id, e.g. <c>assist_satellite.kitchen</c>.</param>
/// <param name="AreaId">Area id of the satellite.</param>
/// <param name="HaUserId">Home Assistant user id, if known.</param>
/// <param name="InputMode"><c>voice</c> when the turn went through speech-to-text, otherwise <c>text</c>.</param>
/// <param name="FollowupRef">Opaque reference from a voice follow-up; unknown values are ignored.</param>
public sealed record ConversationTurnRequest(
    string? ConversationId,
    string Text,
    string? Language,
    string? DeviceId,
    string? SatelliteId,
    string? AreaId,
    string? HaUserId,
    InputMode? InputMode,
    string? FollowupRef);
