using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Internetsteuerung.Core;

namespace Internetsteuerung.Infrastructure;

/// <summary>
/// Encapsulates the OPNsense REST API, like OpnsenseClient in the original project.
/// Endpoints: GET  api/firewall/filter/search_rule
///            POST api/firewall/filter/toggleRule/{uuid}/{0|1}
///            POST api/firewall/filter/apply
/// Connection data comes from configuration, never from source code.
/// </summary>
public sealed class OpnsenseClient(HttpClient http) : IFirewallClient
{
    public static void KonfiguriereAuthentifizierung(HttpClient client, string apiKey, string apiSecret)
    {
        ArgumentNullException.ThrowIfNull(client);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<bool?> IsRuleEnabledAsync(string ruleUuid, CancellationToken ct = default)
    {
        try
        {
            using var antwort = await http.GetAsync(new Uri("api/firewall/filter/search_rule?show_all=1&interface=lan", UriKind.Relative), ct)
                .ConfigureAwait(false);
            if (!antwort.IsSuccessStatusCode)
            {
                return null;
            }

            var json = JsonNode.Parse(await antwort.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var zeile = json?["rows"]?.AsArray()
                .FirstOrDefault(r => string.Equals(r?["uuid"]?.GetValue<string>(), ruleUuid, StringComparison.OrdinalIgnoreCase));
            return zeile?["enabled"]?.ToString() switch
            {
                "1" or "true" or "True" => true,
                "0" or "false" or "False" => false,
                _ => null,
            };
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<bool> SetRuleEnabledAsync(string ruleUuid, bool enabled, CancellationToken ct = default)
    {
        var pfad = $"api/firewall/filter/toggleRule/{Uri.EscapeDataString(ruleUuid)}/{(enabled ? 1 : 0)}";

        // OPNsense answers 200 even for an unknown uuid, with {"result":"failed"}; only Enabled/Disabled count.
        return await PostAsync(pfad, json => json?["result"]?.ToString() is "Enabled" or "Disabled", ct).ConfigureAwait(false);
    }

    public Task<bool> ApplyAsync(CancellationToken ct = default) =>
        PostAsync("api/firewall/filter/apply", json => string.Equals(json?["status"]?.ToString()?.Trim(), "ok", StringComparison.OrdinalIgnoreCase), ct);

    private async Task<bool> PostAsync(string pfad, Func<JsonNode?, bool> istErfolg, CancellationToken ct)
    {
        try
        {
            // OPNsense expects a JSON body on POST, the original sent "{}" as well.
            using var body = new StringContent("{}", Encoding.UTF8, "application/json");
            using var antwort = await http.PostAsync(new Uri(pfad, UriKind.Relative), body, ct).ConfigureAwait(false);
            if (!antwort.IsSuccessStatusCode)
            {
                return false;
            }

            var text = await antwort.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return istErfolg(string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return false;
        }
    }
}
