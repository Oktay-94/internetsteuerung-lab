using System.Collections.Concurrent;
using System.Net;
using Internetsteuerung.Core;
using Internetsteuerung.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Internetsteuerung.Web;

public sealed class WebOptions
{
    public const string Abschnitt = "Web";

    /// <summary>The original was wired to room id 1 ("Raum A"); kept configurable here.</summary>
    public int RaumId { get; set; } = 1;
}

/// <summary>
/// Releases expired blocks independent of any open browser window. This implements the
/// "Hintergrunddienst" from the outlook of the original project (deviation 1.5.3).
/// </summary>
public sealed partial class AutoFreigabeDienst(SperrService sperren, ILogger<AutoFreigabeDienst> log) : BackgroundService
{
    private static readonly TimeSpan Intervall = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var takt = new PeriodicTimer(Intervall);
        do
        {
            try
            {
                var anzahl = await sperren.GebeAbgelaufeneFreiAsync(stoppingToken);
                if (anzahl > 0)
                {
                    LogFreigegeben(log, anzahl);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFehler(log, e);
            }
        }
        while (await takt.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Anzahl} abgelaufene Sperre(n) automatisch freigegeben")]
    private static partial void LogFreigegeben(ILogger logger, int anzahl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Automatische Freigabe fehlgeschlagen, nächster Versuch im nächsten Takt")]
    private static partial void LogFehler(ILogger logger, Exception fehler);
}

/// <summary>Live status of the simulated student PCs, reported by the lab containers.</summary>
public sealed class LabClientStatus
{
    private static readonly IPNetwork LabNetz = IPNetwork.Parse("10.20.0.0/16");
    private readonly ConcurrentDictionary<string, LabClient> _clients = new(StringComparer.Ordinal);

    public sealed record LabClient(string Name, bool Online, string Adresse, DateTimeOffset ZuletztGemeldet, DateTimeOffset? StatusSeit);

    public static bool IstImLabNetz(IPAddress adresse) =>
        LabNetz.Contains(adresse.IsIPv4MappedToIPv6 ? adresse.MapToIPv4() : adresse);

    public void Melde(string name, bool online, string adresse, DateTimeOffset jetzt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40)
        {
            return;
        }

        _clients.AddOrUpdate(name,
            _ => new LabClient(name, online, adresse, jetzt, jetzt),
            (_, alt) => new LabClient(name, online, adresse, jetzt, alt.Online == online ? alt.StatusSeit : jetzt));
    }

    public IReadOnlyList<LabClient> Alle() => _clients.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
}

public sealed class DatenbankHealthCheck(Verbindungsfabrik db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.TesteVerbindungAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("MariaDB nicht erreichbar");
}

public sealed class FirewallHealthCheck(IFirewallClient firewall, IRaumRepository raeume, IOptions<WebOptions> optionen) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var raum = await raeume.GetAktivenAsync(optionen.Value.RaumId, cancellationToken);
        if (raum is null)
        {
            return HealthCheckResult.Degraded("Raum nicht konfiguriert");
        }

        return await firewall.IsRuleEnabledAsync(raum.FirewallRuleUuid, cancellationToken) is null
            ? HealthCheckResult.Unhealthy("Firewall-Regel nicht erreichbar")
            : HealthCheckResult.Healthy();
    }
}

/// <summary>Lab only: waits for MariaDB and creates the room and demo accounts from configuration.</summary>
public sealed partial class LabSeeder(
    Verbindungsfabrik db,
    MariaDbBenutzerRepository benutzer,
    IConfiguration konfiguration,
    IOptions<WebOptions> optionen,
    ILogger<LabSeeder> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var versuch = 1; !await db.TesteVerbindungAsync(cancellationToken); versuch++)
        {
            if (versuch >= 60)
            {
                throw new InvalidOperationException("MariaDB nach 60 Versuchen nicht erreichbar.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var regel = konfiguration["Lab:RegelUuid"] ?? throw new InvalidOperationException("Lab:RegelUuid fehlt.");
        await using (var verbindung = await db.OeffneAsync(cancellationToken))
        await using (var befehl = new MySqlConnector.MySqlCommand(
            "INSERT INTO raum (id, name, firewall_rule_uuid, aktiv) VALUES (@id, 'Raum A', @uuid, 1) " +
            "ON DUPLICATE KEY UPDATE firewall_rule_uuid = @uuid", verbindung))
        {
            befehl.Parameters.AddWithValue("@id", optionen.Value.RaumId);
            befehl.Parameters.AddWithValue("@uuid", regel);
            await befehl.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var eintrag in konfiguration.GetSection("Lab:Benutzer").GetChildren())
        {
            var name = eintrag["Name"];
            var passwort = eintrag["Passwort"];
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(passwort))
            {
                continue;
            }

            var rolle = Enum.TryParse<Rolle>(eintrag["Rolle"], out var r) ? r : Rolle.Lehrer;
            if (await benutzer.LegeAnFallsFehltAsync(name, PasswortHasher.Hashe(passwort), rolle, cancellationToken))
            {
                LogAngelegt(log, name, rolle);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Lab-Benutzer {Name} ({Rolle}) angelegt")]
    private static partial void LogAngelegt(ILogger logger, string name, Rolle rolle);
}
