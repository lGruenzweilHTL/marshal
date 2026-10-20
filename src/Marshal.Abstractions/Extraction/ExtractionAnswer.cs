using System.Text.Json;

namespace Marshal.Abstractions.Extraction;

/// <summary>A validated extraction answer.</summary>
/// <param name="Value">The answer as JSON (<c>true</c>, <c>42</c>, <c>"2026-10-18"</c>, ...), or the string <c>"unknown"</c>.</param>
/// <param name="Unknown">The model could not answer from the content.</param>
/// <param name="Taints">The answer is free text and taints the calling run.</param>
public sealed record ExtractionAnswer(JsonElement Value, bool Unknown, bool Taints)
{
    /// <summary>Longest <see cref="AnswerType.Text"/> answer.</summary>
    public const int MaxTextLength = 500;

    /// <summary>Error code of an answer that does not match its type after the repair attempt.</summary>
    public const string InvalidAnswerCode = "extract.invalid_answer";

    /// <summary>Error code of a reference that names nothing the owner can read.</summary>
    public const string NotFoundCode = "extract.not_found";
}
