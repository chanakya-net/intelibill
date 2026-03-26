using System.ComponentModel.DataAnnotations;

namespace Intelibill.Api.Options;

public sealed class AppOptions
{
    public const string SectionName = "App";

    [Required]
    public string BaseUrl { get; init; } = string.Empty;
}
