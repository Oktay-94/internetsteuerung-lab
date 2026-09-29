using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Internetsteuerung.Infrastructure;

/// <summary>Certificate validation for the firewall's HTTPS endpoint with optional fingerprint pinning.</summary>
public static class ZertifikatsPruefung
{
    public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> Erzeuge(FirewallOptions optionen)
    {
        ArgumentNullException.ThrowIfNull(optionen);
        return (_, zertifikat, _, fehler) =>
        {
            if (fehler == SslPolicyErrors.None)
            {
                return true;
            }

            if (optionen.UngueltigeZertifikateErlauben)
            {
                return true;
            }

            var erwartet = LeseFingerabdruck(optionen);
            if (zertifikat is null || string.IsNullOrEmpty(erwartet))
            {
                return false;
            }

            var tatsaechlich = Convert.ToHexString(SHA256.HashData(zertifikat.RawData));
            return string.Equals(tatsaechlich, erwartet, StringComparison.OrdinalIgnoreCase);
        };
    }

    private static string? LeseFingerabdruck(FirewallOptions optionen)
    {
        if (!string.IsNullOrWhiteSpace(optionen.ZertifikatSha256))
        {
            return Normalisiere(optionen.ZertifikatSha256);
        }

        // Read on every validation: in the lab the firewall writes this file when it starts.
        return optionen.ZertifikatSha256Datei is { } pfad && File.Exists(pfad)
            ? Normalisiere(File.ReadAllText(pfad))
            : null;
    }

    private static string Normalisiere(string wert) =>
        wert.Trim().Replace(":", "", StringComparison.Ordinal).ToUpperInvariant();
}
