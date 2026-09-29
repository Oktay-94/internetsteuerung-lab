using Internetsteuerung.Core;

namespace Internetsteuerung.Demo.Simulation;

/// <summary>
/// Takes the place of the web app's background service: once per second it releases expired blocks
/// through the real SperrService and lets the student PCs probe. Pages subscribe to redraw.
/// </summary>
public sealed class DemoTakt(SperrService sperren, SimKlassenraum klassenraum) : IDisposable
{
    private readonly PeriodicTimer _takt = new(TimeSpan.FromSeconds(1));

    public event Action? Geaendert;

    public void Starten() => _ = LaufAsync();

    /// <summary>One tick, separated from the timer so tests can drive it with a fake clock.</summary>
    public async Task TickAsync()
    {
        await sperren.GebeAbgelaufeneFreiAsync().ConfigureAwait(false);
        klassenraum.Pruefe();
        Geaendert?.Invoke();
    }

    private async Task LaufAsync()
    {
        while (await _takt.WaitForNextTickAsync().ConfigureAwait(false))
        {
            await TickAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => _takt.Dispose();
}
