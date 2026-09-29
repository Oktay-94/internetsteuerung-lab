using Internetsteuerung.Core;

namespace Internetsteuerung.WinForms;

/// <summary>Login dialog, same behaviour as the original LoginForm (btnLogin_Click).</summary>
internal sealed class LoginForm : Form
{
    private readonly AuthService _auth;
    private readonly TextBox _txtBenutzername = new() { Width = 240 };
    private readonly TextBox _txtPasswort = new() { Width = 240, UseSystemPasswordChar = true };
    private readonly Button _btnLogin = new() { Text = "Anmelden", Width = 115 };
    private readonly Button _btnAbbrechen = new() { Text = "Abbrechen", Width = 115, DialogResult = DialogResult.Cancel };

    public LoginForm(AuthService auth)
    {
        _auth = auth;
        Text = "Internetsteuerung · Anmeldung";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        AcceptButton = _btnLogin;
        CancelButton = _btnAbbrechen;

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Benutzername", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_txtBenutzername, 1, 0);
        layout.Controls.Add(new Label { Text = "Passwort", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_txtPasswort, 1, 1);
        var knoepfe = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        knoepfe.Controls.Add(_btnAbbrechen);
        knoepfe.Controls.Add(_btnLogin);
        layout.Controls.Add(knoepfe, 1, 2);
        Controls.Add(layout);

        _btnLogin.Click += async (_, _) => await AnmeldenAsync();
    }

    public Benutzer? AngemeldeterBenutzer { get; private set; }

    private async Task AnmeldenAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtBenutzername.Text) || string.IsNullOrEmpty(_txtPasswort.Text))
        {
            MessageBox.Show("Bitte Benutzername und Passwort eingeben.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnLogin.Enabled = false;
        try
        {
            var benutzer = await _auth.AnmeldenAsync(_txtBenutzername.Text, _txtPasswort.Text);
            if (benutzer is null)
            {
                MessageBox.Show("Benutzername oder Passwort ist falsch oder der Benutzer ist nicht aktiv.", "Anmeldung fehlgeschlagen",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtPasswort.Clear();
                _txtPasswort.Focus();
                return;
            }

            AngemeldeterBenutzer = benutzer;
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            _btnLogin.Enabled = true;
        }
    }
}
