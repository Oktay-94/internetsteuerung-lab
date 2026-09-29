using System.Net;
using System.Text;
using Internetsteuerung.Infrastructure;

namespace Internetsteuerung.Tests;

public sealed class OpnsenseClientTests
{
    private const string Uuid = "3f2b1c9e-7a4d-4e8b-9c1a-2d5e6f7a8b9c";

    private sealed class AufzeichnenderHandler(Func<HttpRequestMessage, HttpResponseMessage> antwort) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Anfragen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Anfragen.Add(request);
            return Task.FromResult(antwort(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OpnsenseClient Client, AufzeichnenderHandler Handler) Erzeuge(Func<HttpRequestMessage, HttpResponseMessage> antwort)
    {
        var handler = new AufzeichnenderHandler(antwort);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://10.20.0.1/") };
        OpnsenseClient.KonfiguriereAuthentifizierung(http, "key", "secret");
        return (new OpnsenseClient(http), handler);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Status_wird_aus_search_rule_gelesen(string enabled, bool erwartet)
    {
        var (client, handler) = Erzeuge(_ => Json($$"""{"rows":[{"uuid":"andere","enabled":"1"},{"uuid":"{{Uuid}}","enabled":"{{enabled}}"}]}"""));

        Assert.Equal(erwartet, await client.IsRuleEnabledAsync(Uuid));
        Assert.Equal("/api/firewall/filter/search_rule", handler.Anfragen[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Unbekannte_Regel_liefert_null() =>
        Assert.Null(await Erzeuge(_ => Json("""{"rows":[]}""")).Client.IsRuleEnabledAsync(Uuid));

    [Fact]
    public async Task Kaputte_Antwort_liefert_null() =>
        Assert.Null(await Erzeuge(_ => Json("kein json")).Client.IsRuleEnabledAsync(Uuid));

    [Fact]
    public async Task Jede_Anfrage_traegt_Basic_Auth_aus_Key_und_Secret()
    {
        var (client, handler) = Erzeuge(_ => Json("""{"rows":[]}"""));

        await client.IsRuleEnabledAsync(Uuid);

        var header = handler.Anfragen[0].Headers.Authorization!;
        Assert.Equal("Basic", header.Scheme);
        Assert.Equal("key:secret", Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!)));
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public async Task Regel_wird_auf_expliziten_Zustand_gesetzt(bool aktiv, string pfadEnde)
    {
        var (client, handler) = Erzeuge(_ => Json("""{"result":"Enabled","changed":true}"""));

        Assert.True(await client.SetRuleEnabledAsync(Uuid, aktiv));
        Assert.Equal(HttpMethod.Post, handler.Anfragen[0].Method);
        Assert.Equal($"/api/firewall/filter/toggleRule/{Uuid}/{pfadEnde}", handler.Anfragen[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Unbekannte_Regel_beim_Setzen_ist_ein_Fehlschlag_obwohl_HTTP_200()
    {
        // OPNsense answers an unknown uuid with 200 and {"result":"failed"}.
        var (client, _) = Erzeuge(_ => Json("""{"result":"failed"}"""));

        Assert.False(await client.SetRuleEnabledAsync(Uuid, true));
    }

    [Fact]
    public async Task Apply_ist_nur_bei_status_ok_erfolgreich()
    {
        Assert.True(await Erzeuge(_ => Json("""{"status":"ok"}""")).Client.ApplyAsync());
        Assert.False(await Erzeuge(_ => Json("""{"status":"failed"}""")).Client.ApplyAsync());
        Assert.False(await Erzeuge(_ => Json("{}", HttpStatusCode.Unauthorized)).Client.ApplyAsync());
    }

    [Fact]
    public async Task Netzwerkfehler_wird_als_Fehlschlag_gemeldet()
    {
        var (client, _) = Erzeuge(_ => throw new HttpRequestException("Verbindung abgelehnt"));

        Assert.False(await client.SetRuleEnabledAsync(Uuid, true));
        Assert.Null(await client.IsRuleEnabledAsync(Uuid));
    }
}
