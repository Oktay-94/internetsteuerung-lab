using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>
/// Stands in for the MariaDB tables raum, sperre and protokoll. Lives in memory, so every reload of
/// the page starts with a fresh classroom.
/// </summary>
public sealed class BrowserSpeicher : IRaumRepository, ISperreSpeicher, IProtokoll
{
    public const int RaumId = 1;

    private readonly Raum _raum = new(RaumId, "Schulungsraum A", SimFirewall.RegelUuid);
    private readonly Dictionary<int, AktiveSperre> _sperren = [];
    private readonly List<ProtokollEintrag> _protokoll = [];

    public Task<Raum?> GetAktivenAsync(int raumId, CancellationToken ct = default) =>
        Task.FromResult(raumId == RaumId ? _raum : null);

    public Task<AktiveSperre?> GetAsync(int raumId, CancellationToken ct = default) =>
        Task.FromResult(_sperren.GetValueOrDefault(raumId));

    public Task SetAsync(AktiveSperre sperre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sperre);
        _sperren[sperre.RaumId] = sperre;
        return Task.CompletedTask;
    }

    public Task EntferneAsync(int raumId, CancellationToken ct = default)
    {
        _sperren.Remove(raumId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AktiveSperre>> GetAbgelaufeneAsync(DateTimeOffset jetzt, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AktiveSperre>>(_sperren.Values.Where(s => s.Ende <= jetzt).ToList());

    public Task SchreibeAsync(ProtokollEintrag eintrag, CancellationToken ct = default)
    {
        _protokoll.Add(eintrag);
        return Task.CompletedTask;
    }

    /// <summary>Newest first, like ORDER BY id DESC in the MariaDB implementation.</summary>
    public Task<IReadOnlyList<ProtokollEintrag>> LetzteAsync(int anzahl, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProtokollEintrag>>(_protokoll.AsEnumerable().Reverse().Take(anzahl).ToList());
}
