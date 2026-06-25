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
    private readonly Button _settings = new() { Text = "CONFIG", Width = 120, Dock = DockStyle.Left };
    private readonly Button _run = new() { Text = "실행", Width = 130, Dock = DockStyle.Right };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None };
    private bool _running;
    private CancellationTokenSource? _cts;
    private readonly Image _logo = LoadLogo();

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
        ClientSize = new Size(600, 640);
        BackColor = UiTheme.Border;          // 1px 외곽선
        Padding = new Padding(1);
        Font = UiTheme.Ui(10.5f);

        Controls.Add(BuildBody());
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeader());

        _logger.LineWritten += OnLogLine;
        FormClosed += (_, _) => _logger.LineWritten -= OnLogLine;

        ForceRealSaveMode();
        UpdateSafetyLabel();
        UpdateStatus();
    }

    // ── 헤더 (브랜드 로고 + 커스텀 타이틀바 + 드래그) ───────────────────────
    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = UiTheme.Background };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Navy, 2);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(); };

        var logo = new PictureBox
        {
            Image = _logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(14, 9),
            Size = new Size(140, 38),
            BackColor = UiTheme.Background
        };
        logo.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(); };

        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(168, 21),
            Text = "MES AUTOMATION",
            ForeColor = UiTheme.TextFaint,
            Font = UiTheme.Ui(9f)
        };
        subtitle.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartDrag(); };

        var rightBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 120,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = UiTheme.Background,
            Padding = new Padding(0, 10, 10, 0)
        };
        rightBar.Controls.Add(HeaderButton("✕", UiTheme.Danger, Close));
        rightBar.Controls.Add(HeaderButton("—", UiTheme.TextDim, () => WindowState = FormWindowState.Minimized));

        header.Controls.Add(subtitle);
        header.Controls.Add(logo);
        header.Controls.Add(rightBar);
        return header;
    }

    private static Image LoadLogo()
    {
        var asm = typeof(MainForm).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("ramos_wordmark.png", System.StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return new Bitmap(1, 1);
        }

        using var stream = asm.GetManifestResourceStream(name)!;
        return Image.FromStream(stream);
    }

    // ── 푸터 (회사명 + 버전) ───────────────────────────────────────────────
    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = UiTheme.Surface };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        footer.Controls.Add(new Label { Text = "  RAMOS Technology co., Ltd.", Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Navy, Font = UiTheme.Ui(9f, FontStyle.Bold), Width = 260 });
        footer.Controls.Add(new Label { Text = "UNIMES Automation · v1.0  ", Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.TextFaint, Font = UiTheme.Mono(9f), Width = 220 });
        return footer;
    }

    private Label HeaderButton(string text, Color color, Action onClick)
    {
        var b = new Label { Text = text, AutoSize = true, ForeColor = color, Font = UiTheme.Ui(13f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(6, 2, 6, 0) };
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
            Padding = new Padding(16, 12, 16, 12),
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
        var partsHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Navy, Padding = new Padding(3, 0, 0, 0) };
        partsHost.Controls.Add(_parts);
        body.Controls.Add(partsHost, 0, 1);

        var scopeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Margin = new Padding(0) };
        scopeRow.Controls.Add(new Label { Text = "작업 범위", AutoSize = true, ForeColor = UiTheme.TextDim, Font = UiTheme.Ui(10.5f), Margin = new Padding(0, 8, 8, 0) });
        _scope.Items.AddRange(["통합품목관리", "품목정보관리", "품목 BIN정보 관리"]);
        _scope.SelectedIndex = 0;
        _scope.FlatStyle = FlatStyle.Flat;
        _scope.BackColor = UiTheme.SurfaceDeep;
        _scope.ForeColor = UiTheme.Text;
        _scope.Font = UiTheme.Ui(10.5f);
        scopeRow.Controls.Add(_scope);
        body.Controls.Add(scopeRow, 0, 2);

        var statusRow = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
        _statusLabel.ForeColor = UiTheme.Success;
        _statusLabel.Font = UiTheme.Ui(11f, FontStyle.Bold);
        _counterLabel.ForeColor = UiTheme.Navy;
        _counterLabel.Font = UiTheme.Mono(12.5f);
        statusRow.Controls.Add(_statusLabel);
        statusRow.Controls.Add(_counterLabel);
        body.Controls.Add(statusRow, 0, 3);

        _progressTrack.BackColor = UiTheme.Border;
        _progressFill.BackColor = UiTheme.Navy;
        _progressTrack.Controls.Add(_progressFill);
        body.Controls.Add(_progressTrack, 0, 4);

        var logHeader = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
        logHeader.Controls.Add(new Label { Text = Path.GetFileName(_paths.RunLogPath), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.TextFaint, Font = UiTheme.Mono(9.5f), Width = 240 });
        logHeader.Controls.Add(new Label { Text = "실행 로그", Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextDim, Font = UiTheme.Ui(10.5f), Width = 120 });
        body.Controls.Add(logHeader, 0, 5);

        var logHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.LogBackground, Padding = new Padding(8, 6, 8, 6) };
        _log.BackColor = UiTheme.LogBackground;
        _log.ForeColor = UiTheme.LogText;
        _log.Font = UiTheme.Mono(11f);
        logHost.Controls.Add(_log);
        body.Controls.Add(logHost, 0, 6);

        var banner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(251, 239, 239), Padding = new Padding(12, 0, 8, 0) };
        banner.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Danger);
            e.Graphics.DrawRectangle(pen, 0, 0, banner.Width - 1, banner.Height - 1);
            using var bar = new SolidBrush(UiTheme.Danger);
            e.Graphics.FillRectangle(bar, 0, 0, 3, banner.Height);
        };
        _safetyLabel.Font = UiTheme.Ui(11f, FontStyle.Bold);
        banner.Controls.Add(_safetyLabel);
        body.Controls.Add(banner, 0, 7);

        var buttonBar = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(0, 14, 0, 0) };
        StyleButton(_settings, UiTheme.Gray, UiTheme.Background, UiTheme.Gray, "설정");
        StyleButton(_run, Color.White, UiTheme.Navy, UiTheme.Navy, "▶ 실행");
        _settings.Click += (_, _) => OpenSettings();
        _run.Click += async (_, _) =>
        {
            if (_running)
            {
                RequestAbort();
            }
            else
            {
                await RunAsync();
            }
        };
        buttonBar.Controls.Add(_settings);
        buttonBar.Controls.Add(_run);
        body.Controls.Add(buttonBar, 0, 8);

        return body;
    }

    private static Label Dim(string text)
        => new() { Text = text, Dock = DockStyle.Fill, ForeColor = UiTheme.TextDim, Font = UiTheme.Ui(10.5f), TextAlign = ContentAlignment.MiddleLeft };

    private static void StyleButton(Button b, Color fg, Color bg, Color border, string text)
    {
        b.Text = text;
        b.Height = 40;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = bg;
        b.ForeColor = fg;
        b.Font = UiTheme.Ui(12f, FontStyle.Bold);
        b.FlatAppearance.BorderColor = border;
    }

    private void StartDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
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

        var color = UiTheme.LogInfo;
        var token = "[INFO]";
        if (line.Contains("[WARN]"))
        {
            color = UiTheme.LogWarn;
            token = "[WARN]";
        }
        else if (line.Contains("[ERROR]"))
        {
            color = UiTheme.LogDanger;
            token = "[ERROR]";
        }

        var idx = line.IndexOf(token, System.StringComparison.Ordinal);
        if (idx < 0)
        {
            AppendSegment(line + System.Environment.NewLine, UiTheme.LogText);
        }
        else
        {
            AppendSegment(line[..idx], UiTheme.LogFaint);
            AppendSegment(token, color);
            AppendSegment(line[(idx + token.Length)..] + System.Environment.NewLine, UiTheme.LogText);
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
            _statusLabel.Text = $"● RUNNING · {_scope.Text}";
            _statusLabel.ForeColor = UiTheme.Navy;
        }
        else
        {
            _statusLabel.Text = "● READY · 대기";
            _statusLabel.ForeColor = UiTheme.Success;
        }
    }

    private void UpdateSafetyLabel()
    {
        _safetyLabel.Text = "⚠  REAL SAVE MODE · 실제 MES 저장";
        _safetyLabel.ForeColor = UiTheme.Danger;
    }

    private void ForceRealSaveMode()
    {
        _config.Safety.DryRun = false;
        _config.Safety.SaveEnabled = true;
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_appSettingsPath);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            CopyInto(ConfigStore.Load(_appSettingsPath));
            ForceRealSaveMode();
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
        ForceRealSaveMode();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetRunning(true);
        try
        {
            await Task.Run(() =>
            {
                var safety = new SafetyGuard(_config.Safety, _logger);
                var app = new UnimesApp(_config, _paths, _logger, _screenshots, safety);
                return app.RunAsync(_options, token).GetAwaiter().GetResult();
            }, token);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("실행이 사용자 요청으로 중단되었습니다.");
        }
        catch (System.Exception ex)
        {
            _logger.Error(ex, "실행 실패");
            ShowFailureDialog(ex);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    private void ShowFailureDialog(System.Exception ex)
    {
        var reason = SummarizeFailure(ex);
        var message =
            "작업 실패" + System.Environment.NewLine +
            System.Environment.NewLine +
            $"원인: {reason}" + System.Environment.NewLine +
            System.Environment.NewLine +
            $"로그: {_paths.RunLogPath}";

        try
        {
            NativeMessage.Show(message, "UNIMES 자동화 실패", NativeMessage.Kind.Error);
        }
        catch
        {
            MessageBox.Show(this, message, "UNIMES 자동화 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SummarizeFailure(System.Exception ex)
    {
        var message = ex.GetBaseException().Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = ex.GetType().Name;
        }

        message = Regex.Replace(message, @"\s+", " ").Trim();
        return message.Length <= 180 ? message : message[..180] + "...";
    }

    private void RequestAbort()
    {
        if (_cts is null || _cts.IsCancellationRequested)
        {
            return;
        }

        _logger.Info("사용자 정지 요청. 현재 Part의 안전 지점에서 멈춥니다.");
        _cts.Cancel();
        _run.Text = "정지 중…";
        _run.Enabled = false;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _parts.Enabled = !running;
        _scope.Enabled = !running;
        _settings.Enabled = !running;
        if (running)
        {
            StyleButton(_run, Color.White, UiTheme.Danger, UiTheme.Danger, "■ 정지");
            _run.Enabled = true;
        }
        else
        {
            StyleButton(_run, Color.White, UiTheme.Navy, UiTheme.Navy, "▶ 실행");
            _run.Enabled = true;
            _progressFill.Width = 0;
            _counterLabel.Text = "";
        }

        UpdateStatus();
    }
}
