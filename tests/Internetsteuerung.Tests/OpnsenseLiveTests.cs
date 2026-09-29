using Internetsteuerung.Infrastructure;

namespace Internetsteuerung.Tests;

/// <summary>Runs only when a real OPNsense is configured via environment variables (see docs/opnsense-vm.md).</summary>
public sealed class OpnsenseLiveFactAttribute : FactAttribute
{
    public OpnsenseLiveFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPNSENSE_URL")))
        {
            Skip = "Keine echte OPNsense konfiguriert (OPNSENSE_URL, OPNSENSE_KEY, OPNSENSE_SECRET, OPNSENSE_RULE_UUID, OPNSENSE_CERT_SHA256).";
        }
    }
}

public sealed class OpnsenseLiveTests
{
    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} fehlt");

    /// <summary>Same wiring as production: Basic auth, certificate pinning, no "accept all".</summary>
    private static (OpnsenseClient Client, HttpClient Http) Erzeuge()
    {
        var optionen = new FirewallOptions { ZertifikatSha256 = Env("OPNSENSE_CERT_SHA256") };
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ZertifikatsPruefung.Erzeuge(optionen) };
        var http = new HttpClient(handler) { BaseAddress = new Uri(Env("OPNSENSE_URL").TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
        OpnsenseClient.KonfiguriereAuthentifizierung(http, Env("OPNSENSE_KEY"), Env("OPNSENSE_SECRET"));
        return (new OpnsenseClient(http), http);
    }

    [OpnsenseLiveFact]
    public async Task Regel_laesst_sich_auf_der_echten_OPNsense_an_und_aus_schalten()
    {
        var (client, http) = Erzeuge();
        using (http)
        {
            var uuid = Env("OPNSENSE_RULE_UUID");

            Assert.True(await client.SetRuleEnabledAsync(uuid, true));
            Assert.True(await client.ApplyAsync());
            Assert.True(await client.IsRuleEnabledAsync(uuid));

            Assert.True(await client.SetRuleEnabledAsync(uuid, false));
            Assert.True(await client.ApplyAsync());
            Assert.False(await client.IsRuleEnabledAsync(uuid));
        }
    }

    [OpnsenseLiveFact]
    public async Task Falsches_Zertifikat_wird_abgelehnt()
    {
        var optionen = new FirewallOptions { ZertifikatSha256 = new string('0', 64) };
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ZertifikatsPruefung.Erzeuge(optionen) };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(Env("OPNSENSE_URL").TrimEnd('/') + "/") };
        OpnsenseClient.KonfiguriereAuthentifizierung(http, Env("OPNSENSE_KEY"), Env("OPNSENSE_SECRET"));

        Assert.Null(await new OpnsenseClient(http).IsRuleEnabledAsync(Env("OPNSENSE_RULE_UUID")));
    }
}
