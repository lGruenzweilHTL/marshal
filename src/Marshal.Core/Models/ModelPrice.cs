namespace Marshal.Core.Models;

/// <summary>
/// Price of a model per million tokens.
/// </summary>
public sealed class ModelPrice
{
    public decimal InputPerMTok { get; set; }

    public decimal OutputPerMTok { get; set; }

    /// <summary>Price of cached input tokens; the input price applies when not set.</summary>
    public decimal? CachedPerMTok { get; set; }
}
