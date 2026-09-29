using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

// Minimal OPNsense-compatible filter API for the lab. It implements exactly the three endpoints the
// client uses and enforces the rule with a real nftables drop rule inside the firewall container,
// so a blocked room really loses its internet connection.

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration(); // the slim builder leaves HTTPS out, OPNsense serves the API via HTTPS
builder.Services.AddSingleton(Filter.AusKonfiguration(builder.Configuration));
var app = builder.Build();

var filter = app.Services.GetRequiredService<Filter>();
await filter.WendeAnAsync(app.Logger);

app.Use(async (kontext, weiter) =>
{
    if (!filter.IstAutorisiert(kontext.Request.Headers.Authorization.ToString()))
    {
        kontext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await kontext.Response.WriteAsJsonAsync(new { status = 401, message = "Authentication Failed" });
        return;
    }

    await weiter(kontext);
});

app.MapMethods("/api/firewall/filter/search_rule", ["GET", "POST"], () => Results.Json(filter.SucheRegeln()));

app.MapPost("/api/firewall/filter/toggleRule/{uuid}", (string uuid) => Results.Json(filter.Schalte(uuid, null)));
app.MapPost("/api/firewall/filter/toggleRule/{uuid}/{enabled}", (string uuid, string enabled) =>
    Results.Json(filter.Schalte(uuid, enabled == "1")));

app.MapPost("/api/firewall/filter/apply", async () =>
{
    var ok = await filter.WendeAnAsync(app.Logger);
    return Results.Json(new { status = ok ? "ok" : "failed" });
});

await app.RunAsync();

/// <summary>Holds the configured and the applied state, like OPNsense ("change, then apply").</summary>
internal sealed partial class Filter(string apiKey, string apiSecret, string regelUuid, string beschreibung, string lanNetz, bool nftAktiv)
{
    private readonly object _sperre = new();
    private bool _konfiguriert;

    public static Filter AusKonfiguration(IConfiguration konfiguration) => new(
        Pflicht(konfiguration, "SIM_API_KEY"),
        Pflicht(konfiguration, "SIM_API_SECRET"),
        Pflicht(konfiguration, "SIM_RULE_UUID"),
        konfiguration["SIM_RULE_DESCRIPTION"] ?? "Internet-Sperre Raum A",
        konfiguration["SIM_LAN_NET"] ?? "10.20.0.0/16",
        string.Equals(konfiguration["SIM_NFT"], "true", StringComparison.OrdinalIgnoreCase));

    public bool IstAutorisiert(string header)
    {
        const string Praefix = "Basic ";
        if (!header.StartsWith(Praefix, StringComparison.Ordinal))
        {
            return false;
        }

        var erwartet = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}")));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[Praefix.Length..]), erwartet);
    }

    public object SucheRegeln()
    {
        lock (_sperre)
        {
            var zeile = new Dictionary<string, string>
            {
                ["uuid"] = regelUuid,
                ["enabled"] = _konfiguriert ? "1" : "0",
                ["action"] = "block",
                ["interface"] = "lan",
                ["source_net"] = lanNetz,
                ["destination_net"] = "any",
                ["ipprotocol"] = "inet46",
                ["description"] = beschreibung,
            };
            return new { total = 1, rowCount = 1, current = 1, rows = new[] { zeile } };
        }
    }

    public object Schalte(string uuid, bool? ziel)
    {
        if (!string.Equals(uuid, regelUuid, StringComparison.OrdinalIgnoreCase))
        {
            return new { result = "failed" };
        }

        lock (_sperre)
        {
            var neu = ziel ?? !_konfiguriert;
            var geaendert = neu != _konfiguriert;
            _konfiguriert = neu;
            return new { result = neu ? "Enabled" : "Disabled", changed = geaendert };
        }
    }

    /// <summary>Writes the configured state into nftables: chain "sperre" is either empty or drops the LAN.</summary>
    public async Task<bool> WendeAnAsync(ILogger log)
    {
        bool gesperrt;
        lock (_sperre)
        {
            gesperrt = _konfiguriert;
        }

        if (!nftAktiv)
        {
            LogSimuliert(log, gesperrt);
            return true;
        }

        var ok = await NftAsync(log, "flush", "chain", "inet", "lab", "sperre");
        if (ok && gesperrt)
        {
            ok = await NftAsync(log, "add", "rule", "inet", "lab", "sperre", "ip", "saddr", lanNetz, "counter", "drop",
                "comment", $"\"{beschreibung}\"");
        }

        LogAngewendet(log, gesperrt, ok);
        return ok;
    }

    private static async Task<bool> NftAsync(ILogger log, params string[] argumente)
    {
        var start = new ProcessStartInfo("nft") { RedirectStandardError = true };
        foreach (var argument in argumente)
        {
            start.ArgumentList.Add(argument);
        }

        using var prozess = Process.Start(start)!;
        var fehler = await prozess.StandardError.ReadToEndAsync();
        await prozess.WaitForExitAsync();
        if (prozess.ExitCode != 0)
        {
            LogNftFehler(log, string.Join(' ', argumente), fehler.Trim());
        }

        return prozess.ExitCode == 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "nftables deaktiviert, Zustand nur simuliert: gesperrt={Gesperrt}")]
    private static partial void LogSimuliert(ILogger logger, bool gesperrt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Regel angewendet: gesperrt={Gesperrt}, erfolgreich={Ok}")]
    private static partial void LogAngewendet(ILogger logger, bool gesperrt, bool ok);

    [LoggerMessage(Level = LogLevel.Error, Message = "nft {Argumente} fehlgeschlagen: {Fehler}")]
    private static partial void LogNftFehler(ILogger logger, string argumente, string fehler);

    private static string Pflicht(IConfiguration konfiguration, string schluessel) =>
        konfiguration[schluessel] is { Length: > 0 } wert
            ? wert
            : throw new InvalidOperationException($"Umgebungsvariable {schluessel} fehlt.");
}
