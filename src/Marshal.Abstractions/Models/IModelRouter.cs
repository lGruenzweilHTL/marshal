namespace Marshal.Abstractions.Models;

/// <summary>
/// Selects a configured model by tier and required capabilities.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Returns a model of the requested tier that has every required capability, or a failure with code
    /// <c>model.not_available</c>. No model call is made.
    /// </summary>
    Result<RoutedModel> Route(ModelRequest request);
}
