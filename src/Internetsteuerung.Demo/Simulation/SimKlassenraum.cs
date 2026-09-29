using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>A simulated student PC with its last probe result.</summary>
public sealed class SchuelerPc(string name, string adresse, DateTimeOffset erstePruefung)
{
    public string Name { get; } = name;

    public string Adresse { get; } = adresse;

    public bool Online { get; internal set; } = true;

    public DateTimeOffset NaechstePruefung { get; internal set; } = erstePruefung;
}

/// <summary>
/// Student PCs of the classroom. Like the lab containers, each one probes the internet every
/// 2 seconds, so a block shows up with a short delay and only after the firewall applied it.
/// </summary>
public sealed class SimKlassenraum
{
    public static readonly TimeSpan Pruefintervall = TimeSpan.FromSeconds(2);
    private const int AnzahlPcs = 6;

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
            _pcs.Add(new SchuelerPc($"schueler-pc-{i}", $"10.20.10.{100 + i}", start + versatz));
        }
    }

    public IReadOnlyList<SchuelerPc> Pcs => _pcs;

    /// <summary>Lets every PC whose probe is due check its connection.</summary>
    public void Pruefe()
    {
        var jetzt = _clock.Now;
        foreach (var pc in _pcs.Where(pc => jetzt >= pc.NaechstePruefung))
        {
            pc.Online = !_firewall.FilterAktiv;
            pc.NaechstePruefung = jetzt + Pruefintervall;
        }
    }
}
