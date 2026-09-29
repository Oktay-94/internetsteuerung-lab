using Internetsteuerung.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Internetsteuerung.Tests;

public sealed class SperrServiceTests
{
    private const string RegelUuid = "3f2b1c9e-7a4d-4e8b-9c1a-2d5e6f7a8b9c";
    private static readonly Raum RaumA = new(1, "Raum A", RegelUuid);
    private static readonly Benutzer Lehrer = new(1, "lehrer", Rolle.Lehrer);
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Start);
    private readonly FakeFirewall _firewall = new FakeFirewall().MitRegel(RegelUuid, aktiv: false);
    private readonly InMemorySperreSpeicher _speicher = new();
    private readonly InMemoryProtokoll _protokoll = new();

    private SperrService Erzeuge() =>
        new(_firewall, _speicher, _protokoll, new InMemoryRaumRepository(RaumA), _clock, NullLogger<SperrService>.Instance);

    [Fact]
    public async Task Sperren_mit_Dauer_aktiviert_Regel_und_merkt_sich_das_Ende()
    {
        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Lehrer, dauerMinuten: 45);

        Assert.True(ergebnis.Erfolg);
        Assert.True(_firewall.Aktiv[RegelUuid]);
        var sperre = Assert.Single(_speicher.Eintraege.Values);
        Assert.Equal(Start.AddMinutes(45), sperre.Ende);
        Assert.Equal("lehrer", sperre.GesperrtVon);
        Assert.Contains(_protokoll.Eintraege, e => e.Aktion == ProtokollAktion.Gesperrt);
    }

    [Fact]
    public async Task Sperren_ohne_Zeitsteuerung_hat_kein_Ende_und_wird_nie_automatisch_frei()
    {
        var service = Erzeuge();
        await service.SperrenAsync(RaumA, Lehrer, dauerMinuten: 0);

        _clock.Vorspulen(TimeSpan.FromDays(1));
        var freigegeben = await service.GebeAbgelaufeneFreiAsync();

        Assert.Equal(0, freigegeben);
        Assert.True(_firewall.Aktiv[RegelUuid]);
        Assert.Null(_speicher.Eintraege[RaumA.Id].Ende);
    }

    [Fact]
    public async Task Nach_Ablauf_gibt_der_Hintergrundlauf_die_Sperre_automatisch_frei()
    {
        var service = Erzeuge();
        await service.SperrenAsync(RaumA, Lehrer, dauerMinuten: 15);

        _clock.Vorspulen(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.Equal(0, await service.GebeAbgelaufeneFreiAsync());
        Assert.True(_firewall.Aktiv[RegelUuid]);

        _clock.Vorspulen(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await service.GebeAbgelaufeneFreiAsync());

        Assert.False(_firewall.Aktiv[RegelUuid]);
        Assert.Empty(_speicher.Eintraege);
        Assert.Contains(_protokoll.Eintraege, e => e.Aktion == ProtokollAktion.AutomatischFreigegeben);
    }

    [Fact]
    public async Task Automatische_Freigabe_funktioniert_auch_nach_einem_Neustart()
    {
        // The original version kept the end time only in memory (deviation 1.5.3).
        // Here a new service instance, as after a restart, still finds the persisted block.
        await Erzeuge().SperrenAsync(RaumA, Lehrer, dauerMinuten: 30);
        _clock.Vorspulen(TimeSpan.FromMinutes(31));

        var nachNeustart = Erzeuge();
        Assert.Equal(1, await nachNeustart.GebeAbgelaufeneFreiAsync());
        Assert.False(_firewall.Aktiv[RegelUuid]);
    }

    [Fact]
    public async Task Fehler_der_Firewall_aendert_keinen_Zustand()
    {
        _firewall.FehlerBeimApply = true;

        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Lehrer, dauerMinuten: 45);

        Assert.False(ergebnis.Erfolg);
        Assert.Equal("Die Firewall-Regel konnte nicht geändert werden.", ergebnis.Meldung);
        Assert.False(_firewall.Aktiv[RegelUuid]);
        Assert.Empty(_speicher.Eintraege);
        Assert.Contains(_protokoll.Eintraege, e => e.Aktion == ProtokollAktion.Fehler);
    }

    [Fact]
    public async Task Status_meldet_null_wenn_die_Regel_nicht_existiert()
    {
        var unbekannterRaum = RaumA with { FirewallRuleUuid = "00000000-0000-0000-0000-000000000000" };

        var status = await Erzeuge().GetStatusAsync(unbekannterRaum);

        Assert.Null(status.Gesperrt);
    }

    [Fact]
    public async Task Status_raeumt_gespeicherte_Sperre_auf_wenn_die_Regel_auf_der_Firewall_aus_ist()
    {
        // Someone released the room directly on the firewall: the stored end time is stale.
        _speicher.Eintraege[RaumA.Id] = new AktiveSperre(RaumA.Id, Start, Start.AddMinutes(30), "lehrer");

        var status = await Erzeuge().GetStatusAsync(RaumA);

        Assert.False(status.Gesperrt);
        Assert.Empty(_speicher.Eintraege);
    }

    [Fact]
    public async Task Status_zeigt_Restzeit_waehrend_einer_Sperre()
    {
        var service = Erzeuge();
        await service.SperrenAsync(RaumA, Lehrer, dauerMinuten: 90);
        _clock.Vorspulen(TimeSpan.FromSeconds(30));

        var status = await service.GetStatusAsync(RaumA);

        Assert.True(status.Gesperrt);
        Assert.Equal(TimeSpan.FromMinutes(89) + TimeSpan.FromSeconds(30), status.Restzeit(_clock.Now));
    }

    [Fact]
    public async Task Freigeben_deaktiviert_Regel_und_loescht_Sperre()
    {
        var service = Erzeuge();
        await service.SperrenAsync(RaumA, Lehrer, dauerMinuten: 45);

        var ergebnis = await service.FreigebenAsync(RaumA, "lehrer");

        Assert.True(ergebnis.Erfolg);
        Assert.False(_firewall.Aktiv[RegelUuid]);
        Assert.Empty(_speicher.Eintraege);
    }

    [Theory]
    [InlineData(1, "Internet für 1 Minute gesperrt.", "1 Minute")]
    [InlineData(45, "Internet für 45 Minuten gesperrt.", "45 Minuten")]
    public async Task Meldung_und_Protokoll_verwenden_Einzahl_und_Mehrzahl_korrekt(int minuten, string meldung, string details)
    {
        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Lehrer, minuten);

        Assert.Equal(meldung, ergebnis.Meldung);
        Assert.Equal(details, _protokoll.Eintraege.Single(e => e.Aktion == ProtokollAktion.Gesperrt).Details);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(481)]
    public async Task Ungueltige_Dauer_wird_abgelehnt(int minuten)
    {
        var ergebnis = await Erzeuge().SperrenAsync(RaumA, Lehrer, minuten);

        Assert.False(ergebnis.Erfolg);
        Assert.Equal(0, _firewall.ApplyAufrufe);
    }
}
