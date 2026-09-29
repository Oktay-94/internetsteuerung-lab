using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Internetsteuerung.Core;

/// <summary>
/// Salted PBKDF2 password hashing. The original project compared an unsalted SHA-256 hash inside
/// MariaDB (SHA2(@passwort, 256)); such legacy hashes are still accepted and upgraded on next login.
/// Format: pbkdf2-sha256$iterations$salt(base64)$hash(base64)
/// </summary>
public static class PasswortHasher
{
    private const string Praefix = "pbkdf2-sha256";
    private const int Iterationen = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public readonly record struct PruefErgebnis(bool Gueltig, bool NeuHashen);

    public static string Hashe(string passwort)
    {
        ArgumentNullException.ThrowIfNull(passwort);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(passwort, salt, Iterationen, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Praefix, Iterationen.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static PruefErgebnis Pruefe(string passwort, string gespeichert)
    {
        ArgumentNullException.ThrowIfNull(passwort);
        if (string.IsNullOrEmpty(gespeichert))
        {
            return new(false, false);
        }

        if (IstLegacySha256(gespeichert))
        {
            var berechnet = SHA256.HashData(Encoding.UTF8.GetBytes(passwort));
            var erwartet = Convert.FromHexString(gespeichert);
            var gueltig = CryptographicOperations.FixedTimeEquals(berechnet, erwartet);
            return new(gueltig, gueltig);
        }

        var teile = gespeichert.Split('$');
        if (teile.Length != 4 || teile[0] != Praefix
            || !int.TryParse(teile[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterationen)
            || !TryBase64(teile[2], out var salt) || !TryBase64(teile[3], out var erwarteterHash))
        {
            return new(false, false);
        }

        var hash = Rfc2898DeriveBytes.Pbkdf2(passwort, salt, iterationen, HashAlgorithmName.SHA256, erwarteterHash.Length);
        var ok = CryptographicOperations.FixedTimeEquals(hash, erwarteterHash);
        return new(ok, ok && iterationen < Iterationen);
    }

    private static bool IstLegacySha256(string wert) =>
        wert.Length == 64 && wert.All(Uri.IsHexDigit);

    private static bool TryBase64(string wert, out byte[] bytes)
    {
        bytes = new byte[wert.Length];
        if (Convert.TryFromBase64String(wert, bytes, out var laenge))
        {
            bytes = bytes[..laenge];
            return laenge > 0;
        }

        return false;
    }
}
