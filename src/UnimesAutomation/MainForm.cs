using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    private readonly RootConfig _config;
    private readonly RuntimePaths _paths;
    private readonly SimpleLogger _logger;
    private readonly ScreenshotService _screenshots;
    private readonly CommandLineOptions _options;
    private readonly string _appSettingsPath;

    private readonly TextBox _parts = new();
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _counterLabel = new() { Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, Width = 110 };
    private readonly Panel _progressTrack = new() { Dock = DockStyle.Fill };
    private readonly Panel _progressFill = new() { Dock = DockStyle.Left, Width = 0 };
    private readonly Label _safetyLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _safetyToggle = new() { Text = "변경", Width = 70, Dock = DockStyle.Right };
    private readonly Button _settings = new() { Text = "CONFIG", Width = 120, Dock = DockStyle.Left };
    private readonly Button _run = new() { Text = "실행", Width = 130, Dock = DockStyle.Right };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None };
    private bool _running;

    public MainForm(RootConfig config, RuntimePaths paths, SimpleLogger logger, ScreenshotService screenshots, CommandLineOptions options, string appSettingsPath)
    {
        _config = config;
        _paths = paths;
        _logger = logger;
        _screenshots = screenshots;
        _options = options;
        _appSettingsPath = appSettingsPath;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 586);
        BackColor = UiTheme.Border;          // 1px 외곽선
        Padding = new Padding(1);
        Font = UiTheme.Mono(12f);

        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());

        _logger.LineWritten += OnLogLine;
        FormClosed += (_, _) => _logger.LineWritten -= OnLogLine;

        UpdateSafetyLabel();
        UpdateStatus();
    }

    // ── 헤더 (커스텀 타이틀바 + 코너 브래킷 + 드래그) ───────────────────────
    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UiTheme.Surface };
        header.Paint += (_, e) => DrawBrackets(e.Graphics, header.Width, header.Height, top: true);
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(); };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(14, 11),
            Text = "▌ UNIMES // AUTOMATION",
            ForeColor = UiTheme.Text,
            Font = UiTheme.Mono(14.5f, FontStyle.Bold)
        };
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(); };

        var rightBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 230,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0, 8, 8, 0)
        };
        rightBar.Controls.Add(HeaderButton("✕", UiTheme.Danger, Close));
        rightBar.Controls.Add(HeaderButton("—", UiTheme.TextDim, () => WindowState = FormWindowState.Minimized));
        rightBar.Controls.Add(new Label { Text = "● LINK: MES", AutoSize = true, ForeColor = UiTheme.TextFaint, Font = UiTheme.Mono(12f), Margin = new Padding(8, 4, 8, 0) });

        header.Controls.Add(title);
        header.Controls.Add(rightBar);
        return header;
    }

    private Label HeaderButton(string text, Color color, Action onClick)
    {
        var b = new Label { Text = text, AutoSize = true, ForeColor = color, Font = UiTheme.Mono(12f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(6, 2, 6, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    // ── 본문 ──────────────────────────────────────────────────────────────
    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Background,
            Padding = new Padding(14, 10, 14, 10),
            ColumnCount = 1,
            RowCount = 9
        };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // part label
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); // part box
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); // scope
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // status
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));  // progress
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // log header
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 168)); // log (약 절반)
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); // banner
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); // buttons

        body.Controls.Add(Dim("PART NO  ·  한 줄에 하나씩 / 쉼표·공백 구분"), 0, 0);

        _parts.Multiline = true;
        _parts.Dock = DockStyle.Fill;
        _parts.ScrollBars = ScrollBars.Vertical;
        _parts.AcceptsReturn = true;
        _parts.BorderStyle = BorderStyle.FixedSingle;
        _parts.BackColor = UiTheme.SurfaceDeep;
        _parts.ForeColor = UiTheme.Text;
        _parts.Font = UiTheme.Mono(12.5f);
        body.Controls.Add(_parts, 0, 1);

        var scopeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Margin = new Padding(0) };
        scopeRow.Controls.Add(new Label { Text = "작업 범위", AutoSize = true, ForeColor = UiTheme.TextDim, Font = UiTheme.Mono(12f), Margin = new Padding(0, 8, 8, 0) });
        _scope.Items.AddRange(["통합품목관리", "품목정보관리", "품목 BIN정보 관리"]);
        _scope.SelectedIndex = 0;
        _scope.FlatStyle = FlatStyle.Flat;
        _scope.BackColor = UiTheme.SurfaceDeep;
        _scope.ForeColor = UiTheme.Text;
        _scope.Font = UiTheme.Mono(12.5f);
        scopeRow.Controls.Add(_scope);
        body.Controls.Add(scopeRow, 0, 2);

        var statusRow = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
        _statusLabel.ForeColor = UiTheme.Accent;
        _statusLabel.Font = UiTheme.Mono(12.5f);
        _counterLabel.ForeColor = UiTheme.TextDim;
        _counterLabel.Font = UiTheme.Mono(12.5f);
        statusRow.Controls.Add(_statusLabel);
        statusRow.Controls.Add(_counterLabel);
        body.Controls.Add(statusRow, 0, 3);

        _progressTrack.BackColor = UiTheme.SurfaceDeep;
        _progressFill.BackColor = UiTheme.Accent;
        _progressTrack.Controls.Add(_progressFill);
        body.Controls.Add(_progressTrack, 0, 4);

        var logHeader = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
        logHeader.Controls.Add(new Label { Text = Path.GetFileName(_paths.RunLogPath), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.TextFaint, Font = UiTheme.Mono(10.5f), Width = 240 });
        logHeader.Controls.Add(new Label { Text = "EXEC LOG", Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextDim, Font = UiTheme.Mono(12f), Width = 120 });
        body.Controls.Add(logHeader, 0, 5);

        var logHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.SurfaceDeep, Padding = new Padding(8, 6, 8, 6) };
        _log.BackColor = UiTheme.SurfaceDeep;
        _log.ForeColor = UiTheme.TextDim;
        _log.Font = UiTheme.Mono(12f);
        logHost.Controls.Add(_log);
        body.Controls.Add(logHost, 0, 6);

        var banner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(26, 20, 10), Padding = new Padding(10, 0, 8, 0) };
        banner.Paint += (_, e) => { using var pen = new Pen(Color.FromArgb(92, 68, 16)); e.Graphics.DrawRectangle(pen, 0, 0, banner.Width - 1, banner.Height - 1); };
        _safetyLabel.Font = UiTheme.Mono(12.5f);
        _safetyToggle.FlatStyle = FlatStyle.Flat;
        _safetyToggle.BackColor = Color.FromArgb(26, 20, 10);
        _safetyToggle.ForeColor = UiTheme.Warn;
        _safetyToggle.Font = UiTheme.Mono(12f);
        _safetyToggle.FlatAppearance.BorderColor = Color.FromArgb(92, 68, 16);
        _safetyToggle.Click += (_, _) => ToggleSafety();
        banner.Controls.Add(_safetyLabel);
        banner.Controls.Add(_safetyToggle);
        body.Controls.Add(banner, 0, 7);

        var buttonBar = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(0, 12, 0, 0) };
        buttonBar.Paint += (_, e) => DrawBrackets(e.Graphics, buttonBar.Width, buttonBar.Height, top: false);
        StyleButton(_settings, UiTheme.TextDim, UiTheme.Border, "CONFIG");
        StyleButton(_run, UiTheme.Accent, UiTheme.Accent, "실행");
        _settings.Click += (_, _) => OpenSettings();
        _run.Click += async (_, _) => await RunAsync();
        buttonBar.Controls.Add(_settings);
        buttonBar.Controls.Add(_run);
        body.Controls.Add(buttonBar, 0, 8);

        return body;
    }

    private static Label Dim(string text)
        => new() { Text = text, Dock = DockStyle.Fill, ForeColor = UiTheme.TextDim, Font = UiTheme.Mono(12f), TextAlign = ContentAlignment.MiddleLeft };

    private static void StyleButton(Button b, Color fg, Color border, string text)
    {
        b.Text = text;
        b.Height = 34;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = UiTheme.Surface;
        b.ForeColor = fg;
        b.Font = UiTheme.Mono(12.5f, FontStyle.Bold);
        b.FlatAppearance.BorderColor = border;
    }

    private void StartDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
    }

    private static void DrawBrackets(Graphics g, int w, int h, bool top)
    {
        const int n = 14;
        using var pen = new Pen(top ? UiTheme.Accent : UiTheme.Border, 2);
        if (top)
        {
            g.DrawLine(pen, 3, 3, 3 + n, 3);
            g.DrawLine(pen, 3, 3, 3, 3 + n);
            g.DrawLine(pen, w - 3 - n, 3, w - 3, 3);
            g.DrawLine(pen, w - 3, 3, w - 3, 3 + n);
        }
        else
        {
            g.DrawLine(pen, 3, h - 3, 3 + n, h - 3);
            g.DrawLine(pen, 3, h - 3, 3, h - 3 - n);
            g.DrawLine(pen, w - 3 - n, h - 3, w - 3, h - 3);
            g.DrawLine(pen, w - 3, h - 3, w - 3, h - 3 - n);
        }
    }

    // ── 로그 스트리밍 (색 구분 + 진행률 파싱) ──────────────────────────────
    private void OnLogLine(string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => AppendLog(line));
        }
        catch (System.Exception)
        {
            // form closing
        }
    }

    private void AppendLog(string line)
    {
        if (_log.TextLength > 80000)
        {
            _log.Clear();
        }

        var color = UiTheme.Accent;
        var token = "[INFO]";
        if (line.Contains("[WARN]"))
        {
            color = UiTheme.Warn;
            token = "[WARN]";
        }
        else if (line.Contains("[ERROR]"))
        {
            color = UiTheme.Danger;
            token = "[ERROR]";
        }

        var idx = line.IndexOf(token, System.StringComparison.Ordinal);
        if (idx < 0)
        {
            AppendSegment(line + System.Environment.NewLine, UiTheme.TextDim);
        }
        else
        {
            AppendSegment(line[..idx], UiTheme.TextFaint);
            AppendSegment(token, color);
            AppendSegment(line[(idx + token.Length)..] + System.Environment.NewLine, UiTheme.TextDim);
        }

        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();

        var m = Regex.Match(line, @"\[(\d+)/(\d+)\]");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var k) && int.TryParse(m.Groups[2].Value, out var total) && total > 0)
        {
            _counterLabel.Text = $"[ {k:00} / {total:00} ]";
            _progressFill.Width = (int)(_progressTrack.Width * (double)k / total);
        }
    }

    private void AppendSegment(string text, Color color)
    {
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = color;
        _log.AppendText(text);
    }

    // ── 상태/안전 ─────────────────────────────────────────────────────────
    private void UpdateStatus()
    {
        if (_running)
        {
            _statusLabel.Text = $"▸ RUNNING · {_scope.Text}";
            _statusLabel.ForeColor = UiTheme.Accent;
        }
        else
        {
            _statusLabel.Text = "▸ READY";
            _statusLabel.ForeColor = UiTheme.TextDim;
        }
    }

    private void UpdateSafetyLabel()
    {
        var realSave = _config.Safety.SaveEnabled && !_config.Safety.DryRun;
        _safetyLabel.Text = realSave
            ? "⚠  REAL SAVE MODE · 실제 저장 켜짐"
            : "⛨  SAFE MODE · 변경 미리보기 (저장 잠금)";
        _safetyLabel.ForeColor = realSave ? UiTheme.Danger : UiTheme.Warn;
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

    private void CopyInto(RootConfig src)
    {
        _config.Login = src.Login;
        _config.App = src.App;
        _config.Workflow.InputPartsPath = src.Workflow.InputPartsPath;
        _config.Options = src.Options;
        _config.Categories = src.Categories;
        _config.Global = src.Global;
    }

    // ── 실행 (Approach 2: 백그라운드 스레드) ───────────────────────────────
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
        if (running)
        {
            StyleButton(_run, UiTheme.Danger, UiTheme.Danger, "ABORT");
            _run.Enabled = false;
        }
        else
        {
            StyleButton(_run, UiTheme.Accent, UiTheme.Accent, "실행");
            _run.Enabled = true;
            _progressFill.Width = 0;
            _counterLabel.Text = "";
        }

        UpdateStatus();
    }
}
