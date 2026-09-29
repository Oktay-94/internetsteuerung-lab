using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>A simulated student PC with its last probe result.</summary>
public sealed class SchuelerPc(Arbeitsplatz arbeitsplatz, DateTimeOffset erstePruefung)
{
    public Arbeitsplatz Arbeitsplatz { get; } = arbeitsplatz;

    public string Name => Arbeitsplatz.Name;

    public string Adresse => Arbeitsplatz.Adresse;

    public bool Online { get; internal set; } = true;

    public DateTimeOffset NaechstePruefung { get; internal set; } = erstePruefung;
}

/// <summary>
/// The 20 student PCs of the classroom. Like the lab containers, each one probes the internet
/// every 2 seconds, so a block shows up with a short delay and only once the firewall enforces it.
/// </summary>
public sealed class SimKlassenraum
{
    public const int AnzahlPcs = 20;
    public static readonly TimeSpan Pruefintervall = TimeSpan.FromSeconds(2);

    private readonly SimFirewall _firewall;
    private readonly IClock _clock;
    private readonly List<SchuelerPc> _pcs = [];

    public SimKlassenraum(SimFirewall firewall, IClock clock)
    {
        _firewall = firewall;
        _clock = clock;
        var start = clock.Now;
        for (var i = 1; i <= AnzahlPcs; i++)
        {
            // Staggered probes, so the PCs go offline one after another as in the lab.
            var versatz = TimeSpan.FromMilliseconds(Pruefintervall.TotalMilliseconds * (i - 1) / AnzahlPcs);
            var arbeitsplatz = new Arbeitsplatz($"schueler-pc-{i:D2}", $"10.20.10.{100 + i}");
            _pcs.Add(new SchuelerPc(arbeitsplatz, start + versatz));
        }
    }

    public IReadOnlyList<SchuelerPc> Pcs => _pcs;

    /// <summary>Lets every PC whose probe is due check its connection.</summary>
    public void Pruefe()
    {
        var jetzt = _clock.Now;
        foreach (var pc in _pcs.Where(pc => jetzt >= pc.NaechstePruefung))
        {
            pc.Online = !_firewall.FilterAktiv && !_firewall.IstHostGesperrt(pc.Adresse);
            pc.NaechstePruefung = jetzt + Pruefintervall;
        }
    }
}
