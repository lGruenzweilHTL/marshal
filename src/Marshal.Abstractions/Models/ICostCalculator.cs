using Microsoft.Extensions.AI;

namespace Marshal.Abstractions.Models;

/// <summary>
/// Computes the cost of a model call from the configured price table.
/// </summary>
public interface ICostCalculator
{
    /// <summary>Cost of one call; 0 for models without a price entry or without usage data.</summary>
    decimal Calculate(string modelKey, UsageDetails? usage);
}
