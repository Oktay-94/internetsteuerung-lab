using Internetsteuerung.Core;
using Internetsteuerung.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Internetsteuerung.WinForms;

internal static class Program
{
    /// <summary>Same start sequence as the original: DB check, login dialog, load room, main window.</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var konfiguration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("INTERNETSTEUERUNG_")
            .Build();

        using var dienste = new ServiceCollection()
            .AddLogging()
            .AddInternetsteuerung(konfiguration)
            .BuildServiceProvider();

        var db = dienste.GetRequiredService<Verbindungsfabrik>();
        if (!db.TesteVerbindungAsync().GetAwaiter().GetResult())
        {
            MessageBox.Show("Die Datenbank ist nicht erreichbar. Die Anwendung wird beendet.", "DB-Fehler",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var anmeldung = new LoginForm(dienste.GetRequiredService<AuthService>());
        if (anmeldung.ShowDialog() != DialogResult.OK || anmeldung.AngemeldeterBenutzer is null)
        {
            return;
        }

        var raumId = konfiguration.GetValue("Web:RaumId", 1);
        var raum = dienste.GetRequiredService<IRaumRepository>().GetAktivenAsync(raumId).GetAwaiter().GetResult();
        if (raum is null)
        {
            MessageBox.Show($"Für Raum {raumId} ist keine aktive Konfiguration hinterlegt.", "DB-Raum-Fehler",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new HauptForm(
            dienste.GetRequiredService<SperrService>(),
            dienste.GetRequiredService<IClock>(),
            raum,
            anmeldung.AngemeldeterBenutzer));
    }
}
