namespace Marshal.Abstractions.Extraction;

/// <summary>The type of an extraction answer. Every type but <see cref="Text"/> leaves the calling run untainted.</summary>
public enum AnswerType
{
    Boolean,
    Number,

    /// <summary>A whole number. Named like the JSON Schema type the model answers in.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Mirrors the JSON Schema type name.")]
    Integer,

    /// <summary>ISO 8601 date (<c>2026-10-18</c>).</summary>
    Date,

    /// <summary>ISO 8601 date and time with offset.</summary>
    DateTime,

    /// <summary>One of the caller's values.</summary>
    Enum,

    /// <summary>Free text of at most <see cref="ExtractionAnswer.MaxTextLength"/> characters; taints the run.</summary>
    Text,
}

/// <summary>The answer a question asks for.</summary>
/// <param name="Type">Answer type.</param>
/// <param name="Values">The allowed values of an <see cref="AnswerType.Enum"/> answer; ignored otherwise.</param>
public sealed record AnswerSpec(AnswerType Type, IReadOnlyList<string>? Values = null)
{
    /// <summary>True when an answer of this type taints the calling run.</summary>
    public bool Taints => Type == AnswerType.Text;
}
