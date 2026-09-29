using System.Security.Cryptography;
using System.Text;
using Internetsteuerung.Core;

namespace Internetsteuerung.Tests;

public sealed class PasswortHasherTests
{
    [Fact]
    public void Hash_und_Pruefung_passen_zusammen()
    {
        var hash = PasswortHasher.Hashe("Korrekt-Pferd-Batterie");

        var ergebnis = PasswortHasher.Pruefe("Korrekt-Pferd-Batterie", hash);

        Assert.True(ergebnis.Gueltig);
        Assert.False(ergebnis.NeuHashen);
        Assert.StartsWith("pbkdf2-sha256$", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Falsches_Passwort_wird_abgelehnt() =>
        Assert.False(PasswortHasher.Pruefe("falsch", PasswortHasher.Hashe("richtig")).Gueltig);

    [Fact]
    public void Gleiches_Passwort_ergibt_wegen_Salt_unterschiedliche_Hashes() =>
        Assert.NotEqual(PasswortHasher.Hashe("gleich"), PasswortHasher.Hashe("gleich"));

    [Fact]
    public void Alter_SHA256_Hash_aus_dem_Original_wird_erkannt_und_zum_Neuhashen_markiert()
    {
        var alterHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("altes-passwort"))).ToLowerInvariant();

        var ergebnis = PasswortHasher.Pruefe("altes-passwort", alterHash);

        Assert.True(ergebnis.Gueltig);
        Assert.True(ergebnis.NeuHashen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kaputt")]
    [InlineData("pbkdf2-sha256$abc$def")]
    public void Unbekanntes_Hashformat_ist_ungueltig(string gespeichert) =>
        Assert.False(PasswortHasher.Pruefe("egal", gespeichert).Gueltig);
}

public sealed class AuthServiceTests
{
    private readonly InMemoryBenutzerRepository _repo = new();
    private readonly InMemoryProtokoll _protokoll = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero));

    private AuthService Erzeuge() => new(_repo, _protokoll, _clock);

    [Fact]
    public async Task Gueltige_Anmeldung_liefert_Benutzer_und_wird_protokolliert()
    {
        _repo.Benutzer["lehrer"] = new(new Benutzer(1, "lehrer", Rolle.Lehrer), PasswortHasher.Hashe("geheim"));

        var benutzer = await Erzeuge().AnmeldenAsync("lehrer", "geheim");

        Assert.Equal(1, benutzer?.Id);
        Assert.Contains(_protokoll.Eintraege, e => e.Aktion == ProtokollAktion.Anmeldung);
    }

    [Theory]
    [InlineData("lehrer", "falsch")]
    [InlineData("unbekannt", "geheim")]
    [InlineData("", "")]
    public async Task Ungueltige_Anmeldung_liefert_null(string name, string passwort)
    {
        _repo.Benutzer["lehrer"] = new(new Benutzer(1, "lehrer", Rolle.Lehrer), PasswortHasher.Hashe("geheim"));

        Assert.Null(await Erzeuge().AnmeldenAsync(name, passwort));
    }

    [Fact]
    public async Task Alter_Hash_wird_beim_Anmelden_auf_PBKDF2_umgestellt()
    {
        var alterHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("geheim"))).ToLowerInvariant();
        _repo.Benutzer["lehrer"] = new(new Benutzer(1, "lehrer", Rolle.Lehrer), alterHash);

        await Erzeuge().AnmeldenAsync("lehrer", "geheim");

        Assert.StartsWith("pbkdf2-sha256$", _repo.Benutzer["lehrer"].PasswortHash, StringComparison.Ordinal);
    }
}
