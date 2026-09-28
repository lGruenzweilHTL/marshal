using System.ComponentModel.DataAnnotations;

namespace Marshal.Api.Hosting;

/// <summary>
/// Profile of the instance owner created on first start (<c>owner</c>). Its time zone is <c>instance.timeZone</c>.
/// Later changes are made per user.
/// </summary>
public sealed class OwnerOptions
{
    [Required(ErrorMessage = "owner.displayName is required.")]
    public string DisplayName { get; set; } = "Owner";

    [RegularExpression("^(de|en)$", ErrorMessage = "owner.language is invalid.")]
    public string Language { get; set; } = "en";
}
