using System.Drawing;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class SettingsForm : Form
{
    private const string SavedPasswordMask = "********";

    private readonly string _path;
    private readonly RootConfig _config;

    private readonly TextBox _userId = new() { Width = 200 };
    private readonly TextBox _password = new() { Width = 200, UseSystemPasswordChar = true, PlaceholderText = "(변경하려면 입력)" };
    private readonly ComboBox _language = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _system = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _launchPath = new() { Width = 460 };
    private readonly TextBox _recoveryPart = new() { Width = 300 };

    private readonly CategorySettingsControl _modulePanel;
    private readonly CategorySettingsControl _compPanel;
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private bool _hasSavedPassword;
    private bool _passwordEdited;
    private bool _suppressPasswordChanged;

    public SettingsForm(string appSettingsPath)
    {
        _path = appSettingsPath;
        _config = ConfigStore.Load(appSettingsPath);
        _modulePanel = new CategorySettingsControl(_config.Categories.DramModule, _config.Options) { Dock = DockStyle.Fill };
        _compPanel = new CategorySettingsControl(_config.Categories.DramComp, _config.Options) { Dock = DockStyle.Fill };

        Text = "설정";
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        var nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 150, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        nav.Controls.Add(NavButton("로그인 정보", BuildLoginPanel));
        nav.Controls.Add(NavButton("DRAM Module", () => _modulePanel));
        nav.Controls.Add(NavButton("DRAM Comp", () => _compPanel));
        nav.Controls.Add(NavButton("고급", BuildAdvancedPanel));

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "저장", Width = 90 };
        save.Click += (_, _) => Save();
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(save);

        LoadLoginFields();
        LoadAdvancedFields();
        _password.TextChanged += (_, _) =>
        {
            if (!_suppressPasswordChanged)
            {
                _passwordEdited = true;
            }
        };
        _password.Enter += (_, _) =>
        {
            if (_hasSavedPassword && !_passwordEdited && _password.Text == SavedPasswordMask)
            {
                SetPasswordText("");
            }
        };
        _password.Leave += (_, _) =>
        {
            if (_hasSavedPassword && !_passwordEdited && string.IsNullOrEmpty(_password.Text))
            {
                SetPasswordText(SavedPasswordMask);
            }
        };

        Controls.Add(_host);
        Controls.Add(nav);
        Controls.Add(bottom);
        CancelButton = cancel;
        ShowPanel(BuildLoginPanel());

        UiTheme.Apply(this);
        foreach (Control c in nav.Controls)
        {
            c.Width = 130;
        }
    }

    private Button NavButton(string text, Func<Control> factory)
    {
        var b = new Button { Text = text, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
        b.Click += (_, _) => ShowPanel(factory());
        return b;
    }

    private void ShowPanel(Control panel)
    {
        _host.Controls.Clear();
        panel.Dock = DockStyle.Fill;
        _host.Controls.Add(panel);
        UiTheme.Apply(panel);
    }

    private Control BuildLoginPanel()
    {
        var p = new TableLayoutPanel { ColumnCount = 2, Padding = new Padding(16), AutoSize = true };
        void Row(string label, Control field)
        {
            p.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
            p.Controls.Add(field);
        }

        Row("아이디", _userId);
        Row("비밀번호", _password);
        Row("언어", _language);
        Row("시스템", _system);
        p.Controls.Add(new Label { Text = "" });
        p.Controls.Add(new Label { Text = "비밀번호는 Windows 계정으로 암호화되어 이 PC에만 저장됩니다 (DPAPI).", AutoSize = true });
        return p;
    }

    private Control BuildAdvancedPanel()
    {
        var p = new TableLayoutPanel { ColumnCount = 2, Padding = new Padding(16), AutoSize = true };
        p.Controls.Add(new Label { Text = "MES 실행 경로", AutoSize = true });
        p.Controls.Add(_launchPath);
        p.Controls.Add(new Label { Text = "복구용 기파트", AutoSize = true });
        p.Controls.Add(_recoveryPart);
        return p;
    }

    private void LoadLoginFields()
    {
        _userId.Text = _config.Login.UserId;
        _hasSavedPassword =
            _config.Login.UseDpapiPassword &&
            !string.IsNullOrEmpty(_config.Login.PasswordEncrypted) &&
            !string.IsNullOrEmpty(SecretProtector.Decrypt(_config.Login.PasswordEncrypted));
        if (_hasSavedPassword)
        {
            SetPasswordText(SavedPasswordMask);
            _password.PlaceholderText = "저장됨 (변경하려면 새로 입력)";
        }

        _language.Items.AddRange(["한국어", "English"]);
        _language.SelectedItem = _config.Login.Language;
        if (_language.SelectedIndex < 0)
        {
            _language.Text = _config.Login.Language;
        }

        _system.Items.AddRange(["UNIMES"]);
        _system.SelectedItem = _config.Login.System;
        if (_system.SelectedIndex < 0)
        {
            _system.Text = _config.Login.System;
        }
    }

    private void LoadAdvancedFields()
    {
        _launchPath.Text = _config.App.LaunchPath;
        _recoveryPart.Text = _config.Global.RecoveryPart;
    }

    private void Save()
    {
        _config.Login.UserId = _userId.Text.Trim();
        _config.Login.Language = _language.Text;
        _config.Login.System = _system.Text;
        if (_passwordEdited && !string.IsNullOrEmpty(_password.Text))
        {
            _config.Login.PasswordMode = "dpapi";
            _config.Login.PasswordEncrypted = SecretProtector.Encrypt(_password.Text);
            _config.Login.Password = "";
        }

        _config.App.LaunchPath = _launchPath.Text.Trim();
        _config.Global.RecoveryPart = _recoveryPart.Text.Trim();
        _modulePanel.ApplyTo(_config.Categories.DramModule);
        _compPanel.ApplyTo(_config.Categories.DramComp);

        ConfigStore.Save(_path, _config);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetPasswordText(string value)
    {
        _suppressPasswordChanged = true;
        try
        {
            _password.Text = value;
        }
        finally
        {
            _suppressPasswordChanged = false;
        }
    }
}
