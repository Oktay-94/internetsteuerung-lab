using Internetsteuerung.Core;

namespace Internetsteuerung.Tests;

public sealed class SperrDauerTests
{
    [Theory]
    [InlineData(90 * 60, "90:00")]
    [InlineData(45 * 60, "45:00")]
    [InlineData(61, "01:01")]
    [InlineData(9, "00:09")]
    [InlineData(0, "00:00")]
    public void Restzeit_wird_wie_im_Original_als_MM_SS_formatiert(int sekunden, string erwartet) =>
        Assert.Equal(erwartet, SperrDauer.FormatiereRestzeit(TimeSpan.FromSeconds(sekunden)));

    [Fact]
    public void Negative_Restzeit_wird_als_null_angezeigt() =>
        Assert.Equal("00:00", SperrDauer.FormatiereRestzeit(TimeSpan.FromSeconds(-5)));

    [Fact]
    public void Vordefinierte_Dauern_entsprechen_der_Dokumentation() =>
        Assert.Equal([15, 30, 45, 60, 90], SperrDauer.Vordefiniert);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(480, true)]
    [InlineData(481, false)]
    [InlineData(-1, false)]
    public void Gueltigkeit_der_Dauer(int minuten, bool gueltig) =>
        Assert.Equal(gueltig, SperrDauer.IstGueltig(minuten));
}
