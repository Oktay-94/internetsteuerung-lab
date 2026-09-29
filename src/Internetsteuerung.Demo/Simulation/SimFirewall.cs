using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>One simulated REST call, shown in the demo so visitors can follow the OPNsense API.</summary>
public sealed record ApiAufruf(DateTimeOffset Zeitpunkt, string Methode, string Pfad, string Antwort);

/// <summary>
/// OPNsense firewall simulated in the browser. Like the real one, toggleRule only changes the
/// configuration; the packet filter follows after apply.
/// </summary>
public sealed class SimFirewall(IClock clock) : IFirewallClient
{
    public const string RegelUuid = "3f9b2c61-8d4e-4a7b-9c15-6e2d0a8f4b27";
    private const int MaxAufrufe = 12;

    private readonly List<ApiAufruf> _aufrufe = [];
    private bool _konfiguriert;

    /// <summary>True once an enabled rule has been applied, i.e. the block is effective in the packet filter.</summary>
    public bool FilterAktiv { get; private set; }

    /// <summary>Simulated network round trip, so the UI shows its busy state as against the real API.</summary>
    public TimeSpan Latenz { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Changing calls, newest first. Status reads (search_rule every 3 seconds) are left out.</summary>
    public IReadOnlyList<ApiAufruf> Aufrufe => _aufrufe;

    public Task<bool?> IsRuleEnabledAsync(string ruleUuid, CancellationToken ct = default) =>
        Task.FromResult<bool?>(ruleUuid == RegelUuid ? _konfiguriert : null);

    public async Task<bool> SetRuleEnabledAsync(string ruleUuid, bool enabled, CancellationToken ct = default)
    {
        await Task.Delay(Latenz, ct).ConfigureAwait(false);
        var pfad = $"/api/firewall/filter/toggleRule/{ruleUuid}/{(enabled ? 1 : 0)}";
        if (ruleUuid != RegelUuid)
        {
            // Same quirk as OPNsense: HTTP 200 with result "failed" for an unknown uuid.
            Merke("POST", pfad, """{"result":"failed"}""");
            return false;
        }

        _konfiguriert = enabled;
        Merke("POST", pfad, enabled ? """{"result":"Enabled"}""" : """{"result":"Disabled"}""");
        return true;
    }

    public async Task<bool> ApplyAsync(CancellationToken ct = default)
    {
        await Task.Delay(Latenz, ct).ConfigureAwait(false);
        FilterAktiv = _konfiguriert;
        Merke("POST", "/api/firewall/filter/apply", """{"status":"OK"}""");
        return true;
    }

    private void Merke(string methode, string pfad, string antwort)
    {
        _aufrufe.Insert(0, new ApiAufruf(clock.Now, methode, pfad, antwort));
        if (_aufrufe.Count > MaxAufrufe)
        {
            _aufrufe.RemoveAt(_aufrufe.Count - 1);
        }
    }
}
