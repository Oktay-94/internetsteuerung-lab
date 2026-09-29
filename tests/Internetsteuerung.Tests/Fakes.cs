using Internetsteuerung.Core;

namespace Internetsteuerung.Tests;

internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Vorspulen(TimeSpan dauer) => Now += dauer;
}

/// <summary>In-memory firewall with the same "change, then apply" semantics as OPNsense.</summary>
internal sealed class FakeFirewall : IFirewallClient
{
    private readonly Dictionary<string, bool> _konfiguriert = new(StringComparer.Ordinal);

    public Dictionary<string, bool> Aktiv { get; } = new(StringComparer.Ordinal);

    public bool FehlerBeimSetzen { get; set; }

    public bool FehlerBeimApply { get; set; }

    public int ApplyAufrufe { get; private set; }

    public FakeFirewall MitRegel(string uuid, bool aktiv)
    {
        _konfiguriert[uuid] = aktiv;
        Aktiv[uuid] = aktiv;
        return this;
    }

    public Task<bool?> IsRuleEnabledAsync(string ruleUuid, CancellationToken ct = default) =>
        Task.FromResult<bool?>(Aktiv.TryGetValue(ruleUuid, out var aktiv) ? aktiv : null);

    public Task<bool> SetRuleEnabledAsync(string ruleUuid, bool enabled, CancellationToken ct = default)
    {
        if (FehlerBeimSetzen || !_konfiguriert.ContainsKey(ruleUuid))
        {
            return Task.FromResult(false);
        }

        _konfiguriert[ruleUuid] = enabled;
        return Task.FromResult(true);
    }

    public Task<bool> ApplyAsync(CancellationToken ct = default)
    {
        ApplyAufrufe++;
        if (FehlerBeimApply)
        {
            return Task.FromResult(false);
        }

        foreach (var (uuid, wert) in _konfiguriert)
        {
            Aktiv[uuid] = wert;
        }

        return Task.FromResult(true);
    }
}

internal sealed class InMemorySperreSpeicher : ISperreSpeicher
{
    public Dictionary<int, AktiveSperre> Eintraege { get; } = [];

    public Task<AktiveSperre?> GetAsync(int raumId, CancellationToken ct = default) =>
        Task.FromResult(Eintraege.GetValueOrDefault(raumId));

    public Task SetAsync(AktiveSperre sperre, CancellationToken ct = default)
    {
        Eintraege[sperre.RaumId] = sperre;
        return Task.CompletedTask;
    }

    public Task EntferneAsync(int raumId, CancellationToken ct = default)
    {
        Eintraege.Remove(raumId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AktiveSperre>> GetAbgelaufeneAsync(DateTimeOffset jetzt, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AktiveSperre>>(
            Eintraege.Values.Where(s => s.Ende is { } ende && ende <= jetzt).ToList());
}

internal sealed class FakeHostSperre : IHostSperreClient
{
    public List<string> Gesperrt { get; } = [];

    public bool Fehler { get; set; }

    public int Aufrufe { get; private set; }

    public Task<bool> SperreHostAsync(string adresse, CancellationToken ct = default) => Setze(adresse, sperren: true);

    public Task<bool> GibHostFreiAsync(string adresse, CancellationToken ct = default) => Setze(adresse, sperren: false);

    public Task<IReadOnlyCollection<string>> GesperrteHostsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<string>>(Gesperrt.ToList());

    private Task<bool> Setze(string adresse, bool sperren)
    {
        Aufrufe++;
        if (Fehler)
        {
            return Task.FromResult(false);
        }

        Gesperrt.Remove(adresse);
        if (sperren)
        {
            Gesperrt.Add(adresse);
        }

        return Task.FromResult(true);
    }
}

internal sealed class InMemoryProtokoll : IProtokoll
{
    public List<ProtokollEintrag> Eintraege { get; } = [];

    public Task SchreibeAsync(ProtokollEintrag eintrag, CancellationToken ct = default)
    {
        Eintraege.Add(eintrag);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProtokollEintrag>> LetzteAsync(int anzahl, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProtokollEintrag>>(Eintraege.AsEnumerable().Reverse().Take(anzahl).ToList());
}

internal sealed class InMemoryRaumRepository(params Raum[] raeume) : IRaumRepository
{
    public Task<Raum?> GetAktivenAsync(int raumId, CancellationToken ct = default) =>
        Task.FromResult(raeume.FirstOrDefault(r => r.Id == raumId));
}

internal sealed class InMemoryBenutzerRepository : IBenutzerRepository
{
    public Dictionary<string, BenutzerMitHash> Benutzer { get; } = new(StringComparer.Ordinal);

    public Task<BenutzerMitHash?> FindeAktivenAsync(string benutzername, CancellationToken ct = default) =>
        Task.FromResult(Benutzer.GetValueOrDefault(benutzername));

    public Task AktualisiereHashAsync(int benutzerId, string neuerHash, CancellationToken ct = default)
    {
        var eintrag = Benutzer.Values.Single(b => b.Benutzer.Id == benutzerId);
        Benutzer[eintrag.Benutzer.Benutzername] = eintrag with { PasswortHash = neuerHash };
        return Task.CompletedTask;
    }
}
