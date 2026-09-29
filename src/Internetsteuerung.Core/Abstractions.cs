namespace Internetsteuerung.Core;

/// <summary>Abstraction of the time source so the countdown logic can be tested without waiting.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

/// <summary>
/// REST access to the firewall. Implemented by OpnsenseClient against the OPNsense API
/// (search_rule, toggleRule, apply), exactly the three calls used in the original project.
/// </summary>
public interface IFirewallClient
{
    /// <returns>True if the rule is enabled, false if disabled, null if the rule does not exist.</returns>
    Task<bool?> IsRuleEnabledAsync(string ruleUuid, CancellationToken ct = default);

    /// <summary>Sets the rule to an explicit state instead of blindly toggling it.</summary>
    Task<bool> SetRuleEnabledAsync(string ruleUuid, bool enabled, CancellationToken ct = default);

    /// <summary>Activates pending configuration changes on the firewall.</summary>
    Task<bool> ApplyAsync(CancellationToken ct = default);
}

public interface IBenutzerRepository
{
    Task<BenutzerMitHash?> FindeAktivenAsync(string benutzername, CancellationToken ct = default);

    Task AktualisiereHashAsync(int benutzerId, string neuerHash, CancellationToken ct = default);
}

public interface IRaumRepository
{
    Task<Raum?> GetAktivenAsync(int raumId, CancellationToken ct = default);
}

public interface ISperreSpeicher
{
    Task<AktiveSperre?> GetAsync(int raumId, CancellationToken ct = default);

    Task SetAsync(AktiveSperre sperre, CancellationToken ct = default);

    Task EntferneAsync(int raumId, CancellationToken ct = default);

    Task<IReadOnlyList<AktiveSperre>> GetAbgelaufeneAsync(DateTimeOffset jetzt, CancellationToken ct = default);
}

public interface IProtokoll
{
    Task SchreibeAsync(ProtokollEintrag eintrag, CancellationToken ct = default);

    Task<IReadOnlyList<ProtokollEintrag>> LetzteAsync(int anzahl, CancellationToken ct = default);
}
