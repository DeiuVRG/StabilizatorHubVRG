using System.ComponentModel.DataAnnotations;

namespace StabilizatorHub.Web.Contracts;

public sealed record ClaimRequest
{
    [Required, MinLength(4), MaxLength(16)]
    public string PairingCode { get; init; } = string.Empty;
}

public sealed record RenameRequest
{
    [Required, MinLength(1), MaxLength(60)]
    public string Name { get; init; } = string.Empty;
}

public sealed record ControlRequest
{
    [Required]
    public bool? On { get; init; }
}
