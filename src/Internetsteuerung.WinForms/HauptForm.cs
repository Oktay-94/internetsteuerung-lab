using Internetsteuerung.Core;

namespace Internetsteuerung.WinForms;

/// <summary>
/// Main window of the original (Form1): status label, traffic light, block/release button,
/// duration selection (cmbDauer, numDauer) and the countdown. The logic lives in SperrService.
/// </summary>
internal sealed class HauptForm : Form
{
    private const string KeineZeitsteuerung = "Keine Zeitsteuerung";
    private const string Benutzerdefiniert = "Benutzerdefiniert";

    private readonly SperrService _sperren;
    private readonly IClock _clock;
    private readonly Raum _raum;
    private readonly Benutzer _benutzer;

    private readonly Label _lblStatusIcon = new() { Text = "●", Font = new Font("Segoe UI", 28f), AutoSize = true, ForeColor = Color.Gray };
    private readonly Label _lblStatus = new() { Text = "Status: wird geladen ...", Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = true };
    private readonly Button _btnToggleInternet = new() { Text = "Status wird geladen ...", Width = 360, Height = 48, Enabled = false };
    private readonly ComboBox _cmbDauer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _numDauer = new() { Minimum = 1, Maximum = SperrDauer.MaximumMinuten, Value = SperrDauer.StandardBenutzerdefiniert, Width = 80, Enabled = false };
    private readonly Label _lblCountdown = new() { Text = "Keine Sperre aktiv", Font = new Font("Segoe UI", 12f), AutoSize = true };
    private readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 1000 };

    private SperrStatus? _status;
    private bool _beschaeftigt;

    public HauptForm(SperrService sperren, IClock clock, Raum raum, Benutzer benutzer)
    {
        _sperren = sperren;
        _clock = clock;
        _raum = raum;
        _benutzer = benutzer;

        Text = $"Internetsteuerung · {raum.Name} · angemeldet als {benutzer.Benutzername} ({benutzer.Rolle})";
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20);

        _cmbDauer.Items.Add(KeineZeitsteuerung);
        foreach (var minuten in SperrDauer.Vordefiniert)
        {
            _cmbDauer.Items.Add($"{minuten} Minuten");
        }

        _cmbDauer.Items.Add(Benutzerdefiniert);
        _cmbDauer.SelectedIndex = 0;
        _cmbDauer.SelectedIndexChanged += (_, _) => _numDauer.Enabled = Equals(_cmbDauer.SelectedItem, Benutzerdefiniert);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        var kopf = new FlowLayoutPanel { AutoSize = true };
        kopf.Controls.Add(_lblStatusIcon);
        kopf.Controls.Add(_lblStatus);
        layout.Controls.Add(kopf);
        var dauer = new FlowLayoutPanel { AutoSize = true };
        dauer.Controls.Add(new Label { Text = "Sperrdauer:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        dauer.Controls.Add(_cmbDauer);
        dauer.Controls.Add(_numDauer);
        dauer.Controls.Add(new Label { Text = "Minuten", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        layout.Controls.Add(dauer);
        layout.Controls.Add(_btnToggleInternet);
        layout.Controls.Add(_lblCountdown);
        Controls.Add(layout);

        _btnToggleInternet.Click += async (_, _) => await UmschaltenAsync();
        _countdownTimer.Tick += async (_, _) => await CountdownTimerTickAsync();
        Load += async (_, _) =>
        {
            await InitStatusAsync();
            _countdownTimer.Start();
        };
        FormClosed += (_, _) => _countdownTimer.Dispose();
    }

    /// <summary>InitStatusAsync of the original: reads the real rule state from the firewall.</summary>
    private async Task InitStatusAsync()
    {
        try
        {
            _status = await _sperren.GetStatusAsync(_raum);
            if (_status.Gesperrt is null)
            {
                MessageBox.Show("Die Firewall-Regel konnte nicht gefunden werden.", "Firewall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (HttpRequestException e)
        {
            MessageBox.Show($"Firewall nicht erreichbar: {e.Message}", "Firewall", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status = new SperrStatus(null, null, null);
        }

        UpdateStatusUi();
    }

    private async Task UmschaltenAsync()
    {
        if (_status?.Gesperrt is null || _beschaeftigt)
        {
            return;
        }

        _beschaeftigt = true;
        _btnToggleInternet.Enabled = false;
        _btnToggleInternet.Text = "Bitte warten ...";
        try
        {
            var ergebnis = _status.Gesperrt == true
                ? await _sperren.FreigebenAsync(_raum, _benutzer.Benutzername)
                : await _sperren.SperrenAsync(_raum, _benutzer, GewaehlteDauerInMinuten());
            if (!ergebnis.Erfolg)
            {
                MessageBox.Show(ergebnis.Meldung, "Firewall", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _beschaeftigt = false;
            await InitStatusAsync();
        }
    }

    /// <summary>GetSelectedDurationInMinutes of the original: 0 means no automatic release.</summary>
    private int GewaehlteDauerInMinuten() => _cmbDauer.SelectedItem switch
    {
        KeineZeitsteuerung => 0,
        Benutzerdefiniert => (int)_numDauer.Value,
        string text => int.Parse(text.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture),
        _ => 0,
    };

    /// <summary>Every second: countdown; also triggers the automatic release like the original timer.</summary>
    private async Task CountdownTimerTickAsync()
    {
        if (_beschaeftigt || _status?.Gesperrt != true || _status.Ende is null)
        {
            return;
        }

        if (_status.Restzeit(_clock.Now) is { } rest && rest > TimeSpan.Zero)
        {
            _lblCountdown.Text = $"Restzeit: {SperrDauer.FormatiereRestzeit(rest)}";
            return;
        }

        _lblCountdown.Text = "Sperre wird automatisch beendet ...";
        await _sperren.GebeAbgelaufeneFreiAsync();
        await InitStatusAsync();
    }

    private void UpdateStatusUi()
    {
        var gesperrt = _status?.Gesperrt;
        _lblStatus.Text = gesperrt switch
        {
            true => "Status: Internet GESPERRT",
            false => "Status: Internet AKTIV",
            _ => "Status: unbekannt",
        };
        _lblStatusIcon.ForeColor = gesperrt switch { true => Color.Firebrick, false => Color.ForestGreen, _ => Color.Gray };
        _btnToggleInternet.Text = gesperrt == true ? "Internet freigeben" : "Internet sperren";
        _btnToggleInternet.Enabled = gesperrt is not null;
        _cmbDauer.Enabled = gesperrt == false;
        _lblCountdown.Text = gesperrt switch
        {
            true when _status!.Ende is null => "Sperre aktiv (ohne automatische Aufhebung)",
            true => $"Restzeit: {SperrDauer.FormatiereRestzeit(_status!.Restzeit(_clock.Now) ?? TimeSpan.Zero)}",
            _ => "Keine Sperre aktiv",
        };
    }
}
