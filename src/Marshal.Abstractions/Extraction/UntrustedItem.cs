namespace Marshal.Abstractions.Extraction;

/// <summary>Third-party content resolved for the extractor; it never reaches the main model.</summary>
/// <param name="Reference">The typed reference it was resolved from, e.g. <c>mail:private:4711</c>.</param>
/// <param name="Text">The content as text.</param>
/// <param name="TaintKind">Taint source a <c>text</c> answer about it marks the run with (<c>mail</c>, <c>rss</c>, <c>notes</c>, ...).</param>
public sealed record UntrustedItem(string Reference, string Text, string TaintKind);
