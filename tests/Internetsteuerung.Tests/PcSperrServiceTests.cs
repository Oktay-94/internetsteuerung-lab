using Internetsteuerung.Core;

namespace Internetsteuerung.Tests;

public sealed class PcSperrServiceTests
{
    private static readonly Raum RaumA = new(1, "Raum A", "regel");
    private static readonly Benutzer Lehrer = new(1, "lehrer", Rolle.Lehrer);
    private static readonly Arbeitsplatz Pc5 = new("schueler-pc-05", "10.20.10.105");

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeHostSperre _firewall = new();
    private readonly InMemoryProtokoll _protokoll = new();

    private PcSperrService Erzeuge() => new(_firewall, _protokoll, _clock);

    [Fact]
    public async Task Sperren_setzt_nur_diese_Adresse_auf_den_Alias_und_protokolliert_den_PC()
    {
        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Pc5, Lehrer);

        Assert.True(ergebnis.Erfolg);
        Assert.Equal(["10.20.10.105"], _firewall.Gesperrt);
        var eintrag = Assert.Single(_protokoll.Eintraege);
        Assert.Equal(ProtokollAktion.Gesperrt, eintrag.Aktion);
        Assert.Equal("schueler-pc-05 (10.20.10.105)", eintrag.Details);
    }

    [Fact]
    public async Task Freigeben_entfernt_die_Adresse_wieder()
    {
        var service = Erzeuge();
        await service.SperrenAsync(RaumA, Pc5, Lehrer);

        var ergebnis = await service.FreigebenAsync(RaumA, Pc5, "lehrer");

        Assert.True(ergebnis.Erfolg);
        Assert.Empty(await service.GesperrteAsync());
        Assert.Equal(ProtokollAktion.Freigegeben, _protokoll.Eintraege[^1].Aktion);
    }

    [Fact]
    public async Task Fehler_der_Firewall_wird_gemeldet_und_protokolliert()
    {
        _firewall.Fehler = true;

        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Pc5, Lehrer);

        Assert.False(ergebnis.Erfolg);
        Assert.Equal(ProtokollAktion.Fehler, Assert.Single(_protokoll.Eintraege).Aktion);
    }

    [Theory]
    [InlineData("10.20.10.105", true)]
    [InlineData("5", false)]
    [InlineData("10.20.10", false)]
    [InlineData("10.20.10.256", false)]
    [InlineData("::1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Nur_IPv4_Adressen_sind_gueltig(string? adresse, bool erwartet) =>
        Assert.Equal(erwartet, PcSperrService.IstGueltigeAdresse(adresse));

    [Fact]
    public async Task Ungueltige_Adresse_erreicht_die_Firewall_nicht()
    {
        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Pc5 with { Adresse = "5" }, Lehrer);

        Assert.False(ergebnis.Erfolg);
        Assert.Equal(0, _firewall.Aufrufe);
        Assert.Empty(_protokoll.Eintraege);
    }
}
