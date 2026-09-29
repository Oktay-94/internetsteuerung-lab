using System.Net;
using System.Net.Sockets;

namespace Internetsteuerung.Core;

/// <summary>
/// Blocks and releases single student PCs, in addition to the room-wide block of SperrService.
/// Every change is written to the audit log with the PC's name and address.
/// </summary>
public sealed class PcSperrService(IHostSperreClient firewall, IProtokoll protokoll, IClock clock)
{
    private const string FirewallFehler = "Die Firewall hat die Änderung nicht übernommen.";

    public Task<SperrErgebnis> SperrenAsync(Raum raum, Arbeitsplatz pc, Benutzer benutzer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        return SetzeAsync(raum, pc, sperren: true, benutzer.Benutzername, ct);
    }

    public Task<SperrErgebnis> FreigebenAsync(Raum raum, Arbeitsplatz pc, string akteur, CancellationToken ct = default) =>
        SetzeAsync(raum, pc, sperren: false, akteur, ct);

    public Task<IReadOnlyCollection<string>> GesperrteAsync(CancellationToken ct = default) =>
        firewall.GesperrteHostsAsync(ct);

    /// <summary>Only plain dotted IPv4 addresses; IPAddress.TryParse alone would also accept "5" as 0.0.0.5.</summary>
    public static bool IstGueltigeAdresse(string? adresse) =>
        adresse is not null
        && adresse.Split('.').Length == 4
        && IPAddress.TryParse(adresse, out var ip)
        && ip.AddressFamily == AddressFamily.InterNetwork;

    private async Task<SperrErgebnis> SetzeAsync(Raum raum, Arbeitsplatz pc, bool sperren, string akteur, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(raum);
        ArgumentNullException.ThrowIfNull(pc);
        if (!IstGueltigeAdresse(pc.Adresse))
        {
            return new SperrErgebnis(false, $"„{pc.Adresse}“ ist keine gültige IPv4-Adresse.");
        }

        var ok = sperren
            ? await firewall.SperreHostAsync(pc.Adresse, ct).ConfigureAwait(false)
            : await firewall.GibHostFreiAsync(pc.Adresse, ct).ConfigureAwait(false);
        var details = $"{pc.Name} ({pc.Adresse})";

        if (!ok)
        {
            await protokoll.SchreibeAsync(
                new ProtokollEintrag(clock.Now, raum.Id, akteur, ProtokollAktion.Fehler, details + (sperren ? ": Sperren fehlgeschlagen" : ": Freigabe fehlgeschlagen")),
                ct).ConfigureAwait(false);
            return new SperrErgebnis(false, FirewallFehler);
        }

        var aktion = sperren ? ProtokollAktion.Gesperrt : ProtokollAktion.Freigegeben;
        await protokoll.SchreibeAsync(new ProtokollEintrag(clock.Now, raum.Id, akteur, aktion, details), ct).ConfigureAwait(false);
        return new SperrErgebnis(true, sperren ? $"{pc.Name} gesperrt." : $"{pc.Name} freigegeben.");
    }
}
