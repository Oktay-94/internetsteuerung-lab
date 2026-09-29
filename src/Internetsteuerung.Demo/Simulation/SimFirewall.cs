using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>One simulated REST call, shown in the demo so visitors can follow the OPNsense API.</summary>
public sealed record ApiAufruf(DateTimeOffset Zeitpunkt, string Methode, string Pfad, string? Anfrage, string Antwort);

/// <summary>
/// OPNsense firewall simulated in the browser. Like the real one, toggleRule only changes the
/// configuration and the packet filter follows after apply. Single PCs are blocked through a host
/// alias, whose changes (alias_util) take effect at once.
/// </summary>
public sealed class SimFirewall(IClock clock) : IFirewallClient, IHostSperreClient
{
    public const string RegelUuid = "3f9b2c61-8d4e-4a7b-9c15-6e2d0a8f4b27";
    public const string Alias = "einzelsperre_raum_a";
    private const int MaxAufrufe = 12;

    private readonly List<ApiAufruf> _aufrufe = [];
    private readonly HashSet<string> _gesperrteHosts = new(StringComparer.Ordinal);
    private bool _konfiguriert;

    /// <summary>True once an enabled rule has been applied, i.e. the room block is effective in the packet filter.</summary>
    public bool FilterAktiv { get; private set; }

    /// <summary>Simulated network round trip, so the UI shows its busy state as against the real API.</summary>
    public TimeSpan Latenz { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Changing calls, newest first. Status reads (search_rule every second) are left out.</summary>
    public IReadOnlyList<ApiAufruf> Aufrufe => _aufrufe;

    public bool IstHostGesperrt(string adresse) => _gesperrteHosts.Contains(adresse);

    public Task<bool?> IsRuleEnabledAsync(string ruleUuid, CancellationToken ct = default) =>
        Task.FromResult<bool?>(ruleUuid == RegelUuid ? _konfiguriert : null);

    public async Task<bool> SetRuleEnabledAsync(string ruleUuid, bool enabled, CancellationToken ct = default)
    {
        await Task.Delay(Latenz, ct).ConfigureAwait(false);
        var pfad = $"/api/firewall/filter/toggleRule/{ruleUuid}/{(enabled ? 1 : 0)}";
        if (ruleUuid != RegelUuid)
        {
            // Same quirk as OPNsense: HTTP 200 with result "failed" for an unknown uuid.
            Merke(pfad, null, """{"result":"failed"}""");
            return false;
        }

        _konfiguriert = enabled;
        Merke(pfad, null, enabled ? """{"result":"Enabled"}""" : """{"result":"Disabled"}""");
        return true;
    }

    public async Task<bool> ApplyAsync(CancellationToken ct = default)
    {
        await Task.Delay(Latenz, ct).ConfigureAwait(false);
        FilterAktiv = _konfiguriert;
        Merke("/api/firewall/filter/apply", null, """{"status":"OK"}""");
        return true;
    }

    public Task<bool> SperreHostAsync(string adresse, CancellationToken ct = default) =>
        AliasAsync("add", adresse, () => _gesperrteHosts.Add(adresse), ct);

    public Task<bool> GibHostFreiAsync(string adresse, CancellationToken ct = default) =>
        AliasAsync("delete", adresse, () => _gesperrteHosts.Remove(adresse), ct);

    public Task<IReadOnlyCollection<string>> GesperrteHostsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<string>>(_gesperrteHosts.ToList());

    private async Task<bool> AliasAsync(string aktion, string adresse, Action aendere, CancellationToken ct)
    {
        await Task.Delay(Latenz, ct).ConfigureAwait(false);
        aendere();
        Merke($"/api/firewall/alias_util/{aktion}/{Alias}", $$"""{"address":"{{adresse}}"}""", """{"status":"done"}""");
        return true;
    }

    private void Merke(string pfad, string? anfrage, string antwort)
    {
        _aufrufe.Insert(0, new ApiAufruf(clock.Now, "POST", pfad, anfrage, antwort));
        if (_aufrufe.Count > MaxAufrufe)
        {
            _aufrufe.RemoveAt(_aufrufe.Count - 1);
        }
    }
}
