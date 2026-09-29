using System.ComponentModel.DataAnnotations;

namespace Internetsteuerung.Infrastructure;

/// <summary>Firewall connection. Set via environment variables or user secrets, never in code.</summary>
public sealed class FirewallOptions
{
    public const string Abschnitt = "Firewall";

    [Required, Url]
    public string BaseUrl { get; set; } = "";

    [Required]
    public string ApiKey { get; set; } = "";

    [Required]
    public string ApiSecret { get; set; } = "";

    /// <summary>
    /// SHA-256 fingerprint (hex) of the firewall's self-signed certificate. The original accepted
    /// every certificate; pinning keeps HTTPS protection with a self-signed certificate.
    /// </summary>
    public string? ZertifikatSha256 { get; set; }

    /// <summary>Alternative to ZertifikatSha256: file that contains the fingerprint (used in the lab).</summary>
    public string? ZertifikatSha256Datei { get; set; }

    /// <summary>Only for local experiments. Logged as a warning when enabled.</summary>
    public bool UngueltigeZertifikateErlauben { get; set; }

    [Range(1, 60)]
    public int TimeoutSekunden { get; set; } = 10;
}

public sealed class DatenbankOptions
{
    public const string Abschnitt = "Datenbank";

    [Required]
    public string ConnectionString { get; set; } = "";
}
