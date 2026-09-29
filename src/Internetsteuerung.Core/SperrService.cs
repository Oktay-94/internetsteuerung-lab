using Microsoft.Extensions.Logging;

namespace Internetsteuerung.Core;

/// <summary>
/// Block and release logic of the main window (btnToggleInternet_Click, CountdownTimer_Tick in the
/// original), moved out of the UI so it can be tested and shared by the web, the desktop client and
/// the background service.
/// </summary>
public sealed partial class SperrService(
    IFirewallClient firewall,
    ISperreSpeicher speicher,
    IProtokoll protokoll,
    IRaumRepository raeume,
    IClock clock,
    ILogger<SperrService> log)
{
    public const string SystemAkteur = "System (automatische Freigabe)";
    private const string FirewallFehler = "Die Firewall-Regel konnte nicht geändert werden.";

    public async Task<SperrStatus> GetStatusAsync(Raum raum, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(raum);
        var aktiv = await firewall.IsRuleEnabledAsync(raum.FirewallRuleUuid, ct).ConfigureAwait(false);
        var gespeichert = await speicher.GetAsync(raum.Id, ct).ConfigureAwait(false);

        if (aktiv == false && gespeichert is not null)
        {
            // Released directly on the firewall: the stored block is stale.
            await speicher.EntferneAsync(raum.Id, ct).ConfigureAwait(false);
            gespeichert = null;
        }

        return aktiv == true
            ? new SperrStatus(true, gespeichert?.Ende, gespeichert?.GesperrtVon)
            : new SperrStatus(aktiv, null, null);
    }

    /// <param name="dauerMinuten">0 blocks without automatic release.</param>
    public async Task<SperrErgebnis> SperrenAsync(Raum raum, Benutzer benutzer, int dauerMinuten, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(raum);
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!SperrDauer.IstGueltig(dauerMinuten))
        {
            return new SperrErgebnis(false, $"Die Sperrdauer muss zwischen 0 und {SperrDauer.MaximumMinuten} Minuten liegen.");
        }

        if (!await SetzeRegelAsync(raum, aktiv: true, benutzer.Benutzername, ct).ConfigureAwait(false))
        {
            return new SperrErgebnis(false, FirewallFehler);
        }

        var jetzt = clock.Now;
        DateTimeOffset? ende = dauerMinuten > 0 ? jetzt.AddMinutes(dauerMinuten) : null;
        await speicher.SetAsync(new AktiveSperre(raum.Id, jetzt, ende, benutzer.Benutzername), ct).ConfigureAwait(false);

        var dauerText = dauerMinuten == 1 ? "1 Minute" : $"{dauerMinuten} Minuten";
        var details = ende is null ? "ohne automatische Aufhebung" : dauerText;
        await protokoll.SchreibeAsync(new ProtokollEintrag(jetzt, raum.Id, benutzer.Benutzername, ProtokollAktion.Gesperrt, details), ct)
            .ConfigureAwait(false);
        return new SperrErgebnis(true, ende is null ? "Internet gesperrt (ohne automatische Aufhebung)." : $"Internet für {dauerText} gesperrt.");
    }

    public Task<SperrErgebnis> FreigebenAsync(Raum raum, string akteur, CancellationToken ct = default) =>
        FreigebenAsync(raum, akteur, ProtokollAktion.Freigegeben, ct);

    /// <summary>Releases every block whose end time has passed. Called by the background service.</summary>
    /// <returns>Number of released rooms.</returns>
    public async Task<int> GebeAbgelaufeneFreiAsync(CancellationToken ct = default)
    {
        var abgelaufen = await speicher.GetAbgelaufeneAsync(clock.Now, ct).ConfigureAwait(false);
        var anzahl = 0;
        foreach (var sperre in abgelaufen)
        {
            var raum = await raeume.GetAktivenAsync(sperre.RaumId, ct).ConfigureAwait(false);
            if (raum is null)
            {
                LogRaumFehlt(log, sperre.RaumId);
                await speicher.EntferneAsync(sperre.RaumId, ct).ConfigureAwait(false);
                continue;
            }

            var ergebnis = await FreigebenAsync(raum, SystemAkteur, ProtokollAktion.AutomatischFreigegeben, ct).ConfigureAwait(false);
            if (ergebnis.Erfolg)
            {
                anzahl++;
            }
        }

        return anzahl;
    }

    private async Task<SperrErgebnis> FreigebenAsync(Raum raum, string akteur, ProtokollAktion aktion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(raum);
        if (!await SetzeRegelAsync(raum, aktiv: false, akteur, ct).ConfigureAwait(false))
        {
            return new SperrErgebnis(false, FirewallFehler);
        }

        await speicher.EntferneAsync(raum.Id, ct).ConfigureAwait(false);
        await protokoll.SchreibeAsync(new ProtokollEintrag(clock.Now, raum.Id, akteur, aktion, null), ct).ConfigureAwait(false);
        return new SperrErgebnis(true, "Internet freigegeben.");
    }

    /// <summary>Set the rule, then apply. Both must succeed, as in the original (okToggle &amp;&amp; okApply).</summary>
    private async Task<bool> SetzeRegelAsync(Raum raum, bool aktiv, string akteur, CancellationToken ct)
    {
        var ok = await firewall.SetRuleEnabledAsync(raum.FirewallRuleUuid, aktiv, ct).ConfigureAwait(false)
                 && await firewall.ApplyAsync(ct).ConfigureAwait(false);
        if (!ok)
        {
            LogFirewallFehler(log, raum.Name, aktiv);
            await protokoll.SchreibeAsync(
                new ProtokollEintrag(clock.Now, raum.Id, akteur, ProtokollAktion.Fehler, aktiv ? "Sperren fehlgeschlagen" : "Freigabe fehlgeschlagen"),
                ct).ConfigureAwait(false);
        }

        return ok;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firewall-Regel für {Raum} konnte nicht auf aktiv={Aktiv} gesetzt werden")]
    private static partial void LogFirewallFehler(ILogger logger, string raum, bool aktiv);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gespeicherte Sperre verweist auf unbekannten Raum {RaumId}, wird verworfen")]
    private static partial void LogRaumFehlt(ILogger logger, int raumId);
}
