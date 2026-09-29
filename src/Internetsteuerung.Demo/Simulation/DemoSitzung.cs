using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>
/// Login for the demo. There is no user database in the browser, so it accepts one publicly shown
/// demo account and writes the same audit entries as AuthService. Password hashing (PBKDF2) is
/// covered by the web app and its unit tests.
/// </summary>
public sealed class DemoSitzung(IProtokoll protokoll, IClock clock)
{
    public const string DemoBenutzername = "lehrer.demo";
    public const string DemoPasswort = "demo";

    public Benutzer? Benutzer { get; private set; }

    public async Task<bool> AnmeldenAsync(string benutzername, string passwort)
    {
        var name = (benutzername ?? string.Empty).Trim();
        var gueltig = name == DemoBenutzername && passwort == DemoPasswort;
        Benutzer = gueltig ? new Benutzer(1, DemoBenutzername, Rolle.Lehrer) : null;

        var aktion = gueltig ? ProtokollAktion.Anmeldung : ProtokollAktion.AnmeldungFehlgeschlagen;
        await protokoll.SchreibeAsync(new ProtokollEintrag(clock.Now, null, name, aktion, null)).ConfigureAwait(false);
        return gueltig;
    }

    public void Abmelden() => Benutzer = null;
}
