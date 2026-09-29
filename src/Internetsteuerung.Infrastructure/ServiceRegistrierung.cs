using Internetsteuerung.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Internetsteuerung.Infrastructure;

public static class ServiceRegistrierung
{
    /// <summary>Registers database, firewall client and the core services.</summary>
    public static IServiceCollection AddInternetsteuerung(this IServiceCollection services, IConfiguration konfiguration)
    {
        services.AddOptions<FirewallOptions>().Bind(konfiguration.GetSection(FirewallOptions.Abschnitt))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<DatenbankOptions>().Bind(konfiguration.GetSection(DatenbankOptions.Abschnitt))
            .ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<Verbindungsfabrik>();
        services.AddSingleton<MariaDbBenutzerRepository>();
        services.AddSingleton<IBenutzerRepository>(sp => sp.GetRequiredService<MariaDbBenutzerRepository>());
        services.AddSingleton<IRaumRepository, MariaDbRaumRepository>();
        services.AddSingleton<ISperreSpeicher, MariaDbSperreSpeicher>();
        services.AddSingleton<IProtokoll, MariaDbProtokoll>();

        services.AddHttpClient<IFirewallClient, OpnsenseClient>((sp, http) =>
            {
                var optionen = sp.GetRequiredService<IOptions<FirewallOptions>>().Value;
                http.BaseAddress = new Uri(optionen.BaseUrl.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(optionen.TimeoutSekunden);
                OpnsenseClient.KonfiguriereAuthentifizierung(http, optionen.ApiKey, optionen.ApiSecret);
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    ZertifikatsPruefung.Erzeuge(sp.GetRequiredService<IOptions<FirewallOptions>>().Value),
            });

        services.AddSingleton<AuthService>();
        services.AddSingleton<SperrService>();
        return services;
    }
}
