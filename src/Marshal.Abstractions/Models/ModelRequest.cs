namespace Marshal.Abstractions.Models;

/// <summary>
/// What a model call needs: a tier and optional capabilities.
/// </summary>
/// <param name="Tier">Requested tier.</param>
/// <param name="Vision">Requires image input.</param>
/// <param name="Tools">Requires tool calling.</param>
/// <param name="MinContextLength">Minimum context length in tokens (0 = any).</param>
public sealed record ModelRequest(ModelTier Tier, bool Vision = false, bool Tools = false, int MinContextLength = 0);
