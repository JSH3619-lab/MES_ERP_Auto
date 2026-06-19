using System.Drawing;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class MainForm : Form
{
    private readonly RootConfig _config;
    private readonly RuntimePaths _paths;
    private readonly SimpleLogger _logger;
    private readonly ScreenshotService _screenshots;
    private readonly CommandLineOptions _options;
    private readonly string _appSettingsPath;

    private readonly TextBox _parts = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Label _safety = new() { AutoSize = true };
    private readonly Button _safetyToggle = new() { Text = "변경", AutoSize = true };
    private readonly Button _settings = new() { Text = "설정", Width = 90 };
    private readonly Button _run = new() { Text = "실행", Width = 110 };
    private readonly TextBox _log = new() { Multiline = true, Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private bool _running;

    public MainForm(RootConfig config, RuntimePaths paths, SimpleLogger logger, ScreenshotService screenshots, CommandLineOptions options, string appSettingsPath)
    {
        _config = config;
        _paths = paths;
        _logger = logger;
        _screenshots = screenshots;
        _options = options;
        _appSettingsPath = appSettingsPath;

        Text = "UNIMES 자동화";
        Width = 620;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;

        _scope.Items.AddRange(["통합품목관리", "품목정보관리", "품목 BIN정보 관리"]);
        _scope.SelectedIndex = 0;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 250, ColumnCount = 1 };
        top.Controls.Add(new Label { Text = "진행할 Part No (한 줄에 하나씩 · 쉼표/공백 구분)", Dock = DockStyle.Top, Height = 22 });
        top.Controls.Add(_parts);
        var scopeRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        scopeRow.Controls.Add(new Label { Text = "작업 범위", AutoSize = true, Anchor = AnchorStyles.Left });
        scopeRow.Controls.Add(_scope);
        top.Controls.Add(scopeRow);

        var safetyRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        safetyRow.Controls.Add(_safety);
        safetyRow.Controls.Add(_safetyToggle);
        _safetyToggle.Click += (_, _) => ToggleSafety();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        _run.Click += async (_, _) => await RunAsync();
        _settings.Click += (_, _) => OpenSettings();
        actions.Controls.Add(_run);
        actions.Controls.Add(_settings);

        var logHost = new Panel { Dock = DockStyle.Fill };
        logHost.Controls.Add(_log);
        logHost.Controls.Add(new Label { Text = "실행 로그", Dock = DockStyle.Top, Height = 20 });

        Controls.Add(logHost);
        Controls.Add(safetyRow);
        Controls.Add(top);
        Controls.Add(actions);

        _logger.LineWritten += OnLogLine;
        FormClosed += (_, _) => _logger.LineWritten -= OnLogLine;

        UpdateSafetyLabel();
        UiTheme.Apply(this);
        _log.BackColor = UiTheme.SurfaceDeep;
        _log.ForeColor = UiTheme.TextDim;
        _log.Font = UiTheme.Mono(9f);
    }

    private void OnLogLine(string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (_log.Lines.Length > 500)
                {
                    _log.Lines = _log.Lines.Skip(_log.Lines.Length - 400).ToArray();
                }

                _log.AppendText(line + Environment.NewLine);
            });
        }
        catch (System.Exception)
        {
            // form closing / handle gone
        }
    }

    private void UpdateSafetyLabel()
    {
        var realSave = _config.Safety.SaveEnabled && !_config.Safety.DryRun;
        _safety.Text = realSave
            ? "● 실제 저장 모드 (저장 켜짐)"
            : "안전 모드 · 변경 미리보기 (저장 잠금)";
        _safety.ForeColor = realSave ? UiTheme.Danger : UiTheme.Warn;
    }

    private void ToggleSafety()
    {
        var turningOn = _config.Safety.DryRun || !_config.Safety.SaveEnabled;
        if (turningOn)
        {
            var ok = MessageBox.Show(
                "정말 실제 MES에 저장을 켜시겠습니까? 저장 동작이 실제로 일어납니다.",
                "실제 저장 켜기", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ok != DialogResult.Yes)
            {
                return;
            }

            _config.Safety.DryRun = false;
            _config.Safety.SaveEnabled = true;
        }
        else
        {
            _config.Safety.DryRun = true;
            _config.Safety.SaveEnabled = false;
        }

        UpdateSafetyLabel();
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_appSettingsPath);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            CopyInto(ConfigStore.Load(_appSettingsPath));
            UpdateSafetyLabel();
            _logger.Info("설정이 저장되어 다음 실행부터 반영됩니다.");
        }
    }

    // 재로드한 설정을 기존 인스턴스에 반영(런타임 입력/안전 토글은 보존).
    private void CopyInto(RootConfig src)
    {
        _config.Login = src.Login;
        _config.App = src.App;
        _config.Workflow.InputPartsPath = src.Workflow.InputPartsPath;
        _config.Options = src.Options;
        _config.Categories = src.Categories;
        _config.Global = src.Global;
    }

    private async Task RunAsync()
    {
        if (_running)
        {
            return;
        }

        var parts = PartListParser.Parse(_parts.Text);
        if (parts.Count == 0)
        {
            MessageBox.Show(this, "Part No를 하나 이상 입력하세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config.Workflow.RuntimeWorkScope = _scope.SelectedIndex switch
        {
            1 => WorkScope.ItemInfo,
            2 => WorkScope.BinInfo,
            _ => WorkScope.Both
        };
        _config.Workflow.RuntimePartRequests = parts;

        SetRunning(true);
        try
        {
            await Task.Run(() =>
            {
                var safety = new SafetyGuard(_config.Safety, _logger);
                var app = new UnimesApp(_config, _paths, _logger, _screenshots, safety);
                return app.RunAsync(_options).GetAwaiter().GetResult();
            });
        }
        catch (System.Exception ex)
        {
            _logger.Error(ex, "실행 실패");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _parts.Enabled = !running;
        _scope.Enabled = !running;
        _settings.Enabled = !running;
        _safetyToggle.Enabled = !running;
        _run.Text = running ? "실행 중…" : "실행";
        _run.Enabled = !running;
    }
}
