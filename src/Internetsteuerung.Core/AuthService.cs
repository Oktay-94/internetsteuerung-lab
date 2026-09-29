namespace Internetsteuerung.Core;

/// <summary>Login against the benutzer table (LoginAsync in the original Database class).</summary>
public sealed class AuthService(IBenutzerRepository benutzer, IProtokoll protokoll, IClock clock)
{
    /// <returns>The user on success, otherwise null. Callers show one neutral message for all failures.</returns>
    public async Task<Benutzer?> AnmeldenAsync(string benutzername, string passwort, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(benutzername) || string.IsNullOrEmpty(passwort))
        {
            return null;
        }

        var name = benutzername.Trim();
        var eintrag = await benutzer.FindeAktivenAsync(name, ct).ConfigureAwait(false);
        var ergebnis = eintrag is null
            ? default
            : PasswortHasher.Pruefe(passwort, eintrag.PasswortHash);

        if (eintrag is null || !ergebnis.Gueltig)
        {
            await protokoll.SchreibeAsync(
                new ProtokollEintrag(clock.Now, null, name, ProtokollAktion.AnmeldungFehlgeschlagen, null), ct).ConfigureAwait(false);
            return null;
        }

        if (ergebnis.NeuHashen)
        {
            await benutzer.AktualisiereHashAsync(eintrag.Benutzer.Id, PasswortHasher.Hashe(passwort), ct).ConfigureAwait(false);
        }

        await protokoll.SchreibeAsync(
            new ProtokollEintrag(clock.Now, null, eintrag.Benutzer.Benutzername, ProtokollAktion.Anmeldung, null), ct).ConfigureAwait(false);
        return eintrag.Benutzer;
    }
}
