namespace Internetsteuerung.Core;

/// <summary>Duration rules for a timed block, taken over from the original main window (cmbDauer, numDauer).</summary>
public static class SperrDauer
{
    /// <summary>Predefined durations in minutes, as offered in the original combo box.</summary>
    public static IReadOnlyList<int> Vordefiniert { get; } = [15, 30, 45, 60, 90];

    /// <summary>Default of the custom-duration field in the original (numDauer).</summary>
    public const int StandardBenutzerdefiniert = 45;

    /// <summary>Upper bound for a custom duration: one school day.</summary>
    public const int MaximumMinuten = 480;

    /// <summary>0 means "no automatic release" (option "Keine Zeitsteuerung").</summary>
    public static bool IstGueltig(int minuten) => minuten is >= 0 and <= MaximumMinuten;

    /// <summary>Formats like the original countdown label: total minutes and seconds, "MM:SS".</summary>
    public static string FormatiereRestzeit(TimeSpan rest)
    {
        if (rest < TimeSpan.Zero)
        {
            rest = TimeSpan.Zero;
        }

        var minuten = (int)rest.TotalMinutes;
        return $"{minuten:D2}:{rest.Seconds:D2}";
    }
}
