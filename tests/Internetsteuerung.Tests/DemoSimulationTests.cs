using Internetsteuerung.Core;
using Internetsteuerung.Demo.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Internetsteuerung.Tests;

public class DemoSimulationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    private sealed class Aufbau
    {
        public FakeClock Uhr { get; } = new(Start);

        public SimFirewall Firewall { get; }

        public BrowserSpeicher Speicher { get; } = new();

        public SimKlassenraum Klassenraum { get; }

        public SperrService Sperren { get; }

        public DemoTakt Takt { get; }

        public Raum Raum { get; }

        public Benutzer Lehrer { get; } = new(1, DemoSitzung.DemoBenutzername, Rolle.Lehrer);

        public Aufbau()
        {
            Firewall = new SimFirewall(Uhr) { Latenz = TimeSpan.Zero };
            Klassenraum = new SimKlassenraum(Firewall, Uhr);
            Sperren = new SperrService(Firewall, Speicher, Speicher, Speicher, Uhr, NullLogger<SperrService>.Instance);
            Takt = new DemoTakt(Sperren, Klassenraum);
            Raum = Speicher.GetAktivenAsync(BrowserSpeicher.RaumId).Result!;
        }

        /// <summary>Advances the clock second by second, like the one-second timer in the browser.</summary>
        public async Task LaufeAsync(TimeSpan dauer)
        {
            for (var i = 0; i < (int)dauer.TotalSeconds; i++)
            {
                Uhr.Vorspulen(TimeSpan.FromSeconds(1));
                await Takt.TickAsync();
            }
        }
    }

    [Fact]
    public async Task Toggle_wirkt_erst_nach_apply()
    {
        var a = new Aufbau();

        Assert.True(await a.Firewall.SetRuleEnabledAsync(SimFirewall.RegelUuid, true));

        Assert.True(await a.Firewall.IsRuleEnabledAsync(SimFirewall.RegelUuid));
        Assert.False(a.Firewall.FilterAktiv);
        Assert.True(await a.Firewall.ApplyAsync());
        Assert.True(a.Firewall.FilterAktiv);
    }

    [Fact]
    public async Task Unbekannte_uuid_liefert_failed_wie_opnsense()
    {
        var a = new Aufbau();

        Assert.False(await a.Firewall.SetRuleEnabledAsync("unbekannt", true));

        Assert.Null(await a.Firewall.IsRuleEnabledAsync("unbekannt"));
        Assert.Equal("""{"result":"failed"}""", a.Firewall.Aufrufe[0].Antwort);
    }

    [Fact]
    public async Task Sperre_nimmt_alle_pcs_innerhalb_eines_pruefintervalls_offline()
    {
        var a = new Aufbau();
        await a.LaufeAsync(TimeSpan.FromSeconds(2));
        Assert.All(a.Klassenraum.Pcs, pc => Assert.True(pc.Online));

        var ergebnis = await a.Sperren.SperrenAsync(a.Raum, a.Lehrer, 15);
        await a.LaufeAsync(SimKlassenraum.Pruefintervall);

        Assert.True(ergebnis.Erfolg);
        Assert.All(a.Klassenraum.Pcs, pc => Assert.False(pc.Online));
        Assert.Equal(["/api/firewall/filter/apply", $"/api/firewall/filter/toggleRule/{SimFirewall.RegelUuid}/1"],
            a.Firewall.Aufrufe.Select(x => x.Pfad));
    }

    [Fact]
    public async Task Einminuetige_sperre_wird_automatisch_freigegeben()
    {
        var a = new Aufbau();
        await a.Sperren.SperrenAsync(a.Raum, a.Lehrer, 1);

        await a.LaufeAsync(TimeSpan.FromSeconds(59));
        Assert.True(a.Firewall.FilterAktiv);

        await a.LaufeAsync(TimeSpan.FromSeconds(1) + SimKlassenraum.Pruefintervall);
        Assert.False(a.Firewall.FilterAktiv);
        Assert.All(a.Klassenraum.Pcs, pc => Assert.True(pc.Online));
        var letzte = (await a.Speicher.LetzteAsync(1))[0];
        Assert.Equal(ProtokollAktion.AutomatischFreigegeben, letzte.Aktion);
    }

    [Fact]
    public async Task Protokoll_liefert_neueste_zuerst_und_begrenzt()
    {
        var speicher = new BrowserSpeicher();
        for (var i = 0; i < 3; i++)
        {
            await speicher.SchreibeAsync(new ProtokollEintrag(Start.AddMinutes(i), 1, $"akteur{i}", ProtokollAktion.Gesperrt, null));
        }

        var letzte = await speicher.LetzteAsync(2);

        Assert.Equal(["akteur2", "akteur1"], letzte.Select(e => e.Akteur));
    }

    [Fact]
    public async Task Api_verlauf_ist_auf_zwoelf_eintraege_begrenzt()
    {
        var a = new Aufbau();
        for (var i = 0; i < 10; i++)
        {
            await a.Firewall.SetRuleEnabledAsync(SimFirewall.RegelUuid, i % 2 == 0);
            await a.Firewall.ApplyAsync();
        }

        Assert.Equal(12, a.Firewall.Aufrufe.Count);
    }

    [Theory]
    [InlineData(DemoSitzung.DemoBenutzername, DemoSitzung.DemoPasswort, true, ProtokollAktion.Anmeldung)]
    [InlineData("  " + DemoSitzung.DemoBenutzername + " ", DemoSitzung.DemoPasswort, true, ProtokollAktion.Anmeldung)]
    [InlineData(DemoSitzung.DemoBenutzername, "falsch", false, ProtokollAktion.AnmeldungFehlgeschlagen)]
    [InlineData("", "", false, ProtokollAktion.AnmeldungFehlgeschlagen)]
    public async Task Demo_anmeldung_prueft_und_protokolliert(string name, string passwort, bool erwartet, ProtokollAktion aktion)
    {
        var speicher = new BrowserSpeicher();
        var sitzung = new DemoSitzung(speicher, new FakeClock(Start));

        var ok = await sitzung.AnmeldenAsync(name, passwort);

        Assert.Equal(erwartet, ok);
        Assert.Equal(erwartet, sitzung.Benutzer is not null);
        Assert.Equal(aktion, (await speicher.LetzteAsync(1))[0].Aktion);
    }
}
