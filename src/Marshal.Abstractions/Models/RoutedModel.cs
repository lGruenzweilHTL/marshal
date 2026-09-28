using Microsoft.Extensions.AI;

namespace Marshal.Abstractions.Models;

/// <summary>
/// A configured model selected by the router.
/// </summary>
/// <param name="Key">Configuration key of the model (used for prices and run records).</param>
/// <param name="Tier">Tier of the model.</param>
/// <param name="Client">Provider-neutral chat client.</param>
public sealed record RoutedModel(string Key, ModelTier Tier, IChatClient Client);
