using System.Net;
using System.Security.Claims;
using Internetsteuerung.Core;
using Internetsteuerung.Infrastructure;
using Internetsteuerung.Web;
using Internetsteuerung.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInternetsteuerung(builder.Configuration);
builder.Services.AddOptions<WebOptions>().Bind(builder.Configuration.GetSection(WebOptions.Abschnitt));
builder.Services.AddHostedService<AutoFreigabeDienst>();
builder.Services.AddSingleton<LabClientStatus>();
if (builder.Configuration.GetValue<bool>("Lab:Aktiv"))
{
    builder.Services.AddHostedService<LabSeeder>();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/anmelden";
        o.Cookie.Name = "internetsteuerung";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHealthChecks()
    .AddCheck<DatenbankHealthCheck>("datenbank")
    .AddCheck<FirewallHealthCheck>("firewall");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/fehler", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/konto/anmelden", async (
    [FromForm] string? benutzername,
    [FromForm] string? passwort,
    AuthService auth,
    HttpContext kontext) =>
{
    var benutzer = await auth.AnmeldenAsync(benutzername ?? "", passwort ?? "", kontext.RequestAborted);
    if (benutzer is null)
    {
        return Results.LocalRedirect("/anmelden?fehler=1");
    }

    var identitaet = new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, benutzer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, benutzer.Benutzername),
        new Claim(ClaimTypes.Role, benutzer.Rolle.ToString()),
    ], CookieAuthenticationDefaults.AuthenticationScheme);
    await kontext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identitaet));
    return Results.LocalRedirect("/");
});

app.MapPost("/konto/abmelden", async (HttpContext kontext) =>
{
    await kontext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/anmelden");
}).RequireAuthorization();

// Heartbeats of the simulated student PCs. Accepted only from the classroom network.
app.MapPost("/api/lab/heartbeat", (LabHeartbeat puls, HttpContext kontext, LabClientStatus status, IClock clock) =>
{
    var absender = kontext.Connection.RemoteIpAddress;
    if (absender is null || !LabClientStatus.IstImLabNetz(absender))
    {
        return Results.StatusCode((int)HttpStatusCode.Forbidden);
    }

    var adresse = absender.IsIPv4MappedToIPv6 ? absender.MapToIPv4() : absender;
    status.Melde(puls.Name, puls.Online, adresse.ToString(), clock.Now);
    return Results.NoContent();
}).DisableAntiforgery();

// Small REST API for scripts and the end-to-end test. Cookie auth plus a custom header:
// browsers cannot send that header cross-site, which blocks CSRF without an antiforgery token.
var raumApi = app.MapGroup("/api/raum").RequireAuthorization().DisableAntiforgery()
    .AddEndpointFilter(async (kontext, weiter) =>
        kontext.HttpContext.Request.Headers.ContainsKey("X-Internetsteuerung")
            ? await weiter(kontext)
            : Results.BadRequest("Header X-Internetsteuerung fehlt."));

raumApi.MapGet("/status", async (SperrService sperren, IRaumRepository raeume, IClock clock,
    Microsoft.Extensions.Options.IOptions<WebOptions> optionen, CancellationToken ct) =>
{
    var raum = await raeume.GetAktivenAsync(optionen.Value.RaumId, ct);
    if (raum is null)
    {
        return Results.NotFound();
    }

    var status = await sperren.GetStatusAsync(raum, ct);
    return Results.Ok(new { raum = raum.Name, status.Gesperrt, status.Ende, status.GesperrtVon, restSekunden = status.Restzeit(clock.Now)?.TotalSeconds });
});

raumApi.MapPost("/sperren", async (SperrAnfrage anfrage, SperrService sperren, IRaumRepository raeume,
    Microsoft.Extensions.Options.IOptions<WebOptions> optionen, ClaimsPrincipal user, CancellationToken ct) =>
{
    var raum = await raeume.GetAktivenAsync(optionen.Value.RaumId, ct);
    if (raum is null)
    {
        return Results.NotFound();
    }

    var benutzer = new Benutzer(
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture),
        user.Identity!.Name!,
        Enum.Parse<Rolle>(user.FindFirstValue(ClaimTypes.Role)!));
    var ergebnis = await sperren.SperrenAsync(raum, benutzer, anfrage.DauerMinuten, ct);
    return ergebnis.Erfolg ? Results.Ok(ergebnis) : Results.UnprocessableEntity(ergebnis);
});

raumApi.MapPost("/freigeben", async (SperrService sperren, IRaumRepository raeume,
    Microsoft.Extensions.Options.IOptions<WebOptions> optionen, ClaimsPrincipal user, CancellationToken ct) =>
{
    var raum = await raeume.GetAktivenAsync(optionen.Value.RaumId, ct);
    if (raum is null)
    {
        return Results.NotFound();
    }

    var ergebnis = await sperren.FreigebenAsync(raum, user.Identity!.Name!, ct);
    return ergebnis.Erfolg ? Results.Ok(ergebnis) : Results.UnprocessableEntity(ergebnis);
});

app.MapHealthChecks("/health");

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

internal sealed record LabHeartbeat(string Name, bool Online);

internal sealed record SperrAnfrage(int DauerMinuten);
