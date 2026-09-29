namespace Internetsteuerung.Core;

/// <summary>Role of a user account, mirrors the ENUM column benutzer.rolle.</summary>
public enum Rolle
{
    Lehrer,
    Admin,
}

/// <summary>An authenticated teacher or admin account.</summary>
public sealed record Benutzer(int Id, string Benutzername, Rolle Rolle);

/// <summary>A user row including the stored password hash, only used inside the login flow.</summary>
public sealed record BenutzerMitHash(Benutzer Benutzer, string PasswortHash);

/// <summary>
/// Network configuration of a classroom. In the original project the API key, secret and base URL
/// were stored here as well; in this rebuild they live in configuration (see FirewallOptions).
/// </summary>
public sealed record Raum(int Id, string Name, string FirewallRuleUuid);

/// <summary>A student PC in a classroom, identified by its fixed IPv4 address.</summary>
public sealed record Arbeitsplatz(string Name, string Adresse);

/// <summary>A running block, persisted so the automatic release survives restarts.</summary>
public sealed record AktiveSperre(int RaumId, DateTimeOffset GesperrtSeit, DateTimeOffset? Ende, string GesperrtVon);

/// <summary>Current state of a room as shown in the UI.</summary>
/// <param name="Gesperrt">True if the block rule is enabled, false if not, null if the rule was not found.</param>
public sealed record SperrStatus(bool? Gesperrt, DateTimeOffset? Ende, string? GesperrtVon)
{
    public TimeSpan? Restzeit(DateTimeOffset jetzt) =>
        Gesperrt == true && Ende is { } ende ? (ende > jetzt ? ende - jetzt : TimeSpan.Zero) : null;
}

/// <summary>Result of a block or release request.</summary>
public sealed record SperrErgebnis(bool Erfolg, string Meldung);

public enum ProtokollAktion
{
    Anmeldung,
    AnmeldungFehlgeschlagen,
    Gesperrt,
    Freigegeben,
    AutomatischFreigegeben,
    Fehler,
}

/// <summary>Audit entry: who did what and when (item "Protokollierung" from the original outlook).</summary>
public sealed record ProtokollEintrag(
    DateTimeOffset Zeitpunkt,
    int? RaumId,
    string Akteur,
    ProtokollAktion Aktion,
    string? Details);
