using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Extraction;

/// <summary>
/// Answers one typed question about untrusted content with a model that has no tools. The content is data for the
/// extractor only; its calls are recorded as model-call steps of the calling run.
/// </summary>
public interface IQuarantinedExtractor
{
    Task<Result<ExtractionAnswer>> ExtractAsync(UntrustedItem item, string question, AnswerSpec answer, MarshalExecutionContext context, CancellationToken cancellationToken);
}
