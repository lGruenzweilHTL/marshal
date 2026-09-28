using System.ComponentModel.DataAnnotations;

namespace Marshal.Api.Hosting;

/// <summary>
/// Profile of the instance owner created on first start (<c>owner</c>). Later changes are made per user.
/// </summary>
public sealed class OwnerOptions
{
    [Required(ErrorMessage = "owner.displayName is required.")]
    public string DisplayName { get; set; } = "Owner";

    [RegularExpression("^(de|en)$", ErrorMessage = "owner.language is invalid.")]
    public string Language { get; set; } = "en";

    /// <summary>IANA time zone id.</summary>
    [Required(ErrorMessage = "owner.timeZone is required.")]
    public string TimeZone { get; set; } = "UTC";
}
