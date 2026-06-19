# MES GUI 메인/설정 창 + 다크 HUD 테마 Implementation Plan — Plan 3 / 3

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 콘솔 + 순차 다이얼로그를 **상시 메인 창**(파트 입력·작업범위·안전모드·실행·창 안 로그)으로 대체하고, **설정 창**(로그인·분류별 설정·고급)에서 값을 직접 편집·저장(DPAPI)하게 한다. 전체를 다크 HUD 테마로 입힌다.

**Architecture:** `Program`은 GUI를 기본 진입점으로 띄운다(`--dump-only`/`--help`만 콘솔). `MainForm`이 입력을 모아 백그라운드 스레드에서 `UnimesApp.RunAsync`를 돌리고, `SimpleLogger.LineWritten` 이벤트를 구독해 로그를 창 안 패널에 스트리밍한다(Approach 2). `SettingsForm`은 `ConfigStore`로 `appsettings.json`을 읽고/쓰며, 비밀번호는 `SecretProtector`로 암호화. 설정 변경은 다음 실행부터 반영(실행마다 현재 config로 `UnimesApp` 새로 생성).

**Tech Stack:** .NET 8 (`net8.0-windows`, WinForms), C#, xUnit(순수 로직만).

**전제:** Plan 1(분류 설정·DPAPI·ConfigStore)·Plan 2(엑셀 리포트) 완료·머지됨.

**스펙 참조:** [docs/.../2026-06-19-mes-main-settings-design.md](../specs/2026-06-19-mes-main-settings-design.md) §3, §6, §7, §10, §14.

---

## 현재 코드 사실

- 진입 `Program.Main`: 인자 파싱 → `LoadConfig`(ConfigStore.Load) → `ScreenshotService`/`SafetyGuard`/`UnimesApp` 생성 → (`!DumpOnly`) `WorkScopeDialog.ShowDialog()` + `PartInputDialog.ShowDialog()` → `app.RunAsync(options)`.
- 런타임 입력은 `config.Workflow.RuntimeWorkScope`(WorkScope) / `config.Workflow.RuntimePartRequests`(List<PartRequest>)에 담겨 `RunAsync`가 사용.
- 생성자: `new UnimesApp(config, paths, logger, screenshots, safety)`, `new ScreenshotService(paths, logger)`, `new SafetyGuard(config.Safety, logger)`. `RunAsync(CommandLineOptions)` → `Task<int>`.
- `SafetyGuard`는 `config.Safety`(SafetyConfig) **참조**를 들고 `SaveEnabled`를 본다 → 같은 인스턴스를 바꾸면 게이트도 바뀜.
- `SimpleLogger.Write`는 콘솔+파일에 기록(이벤트 없음).
- `PartInputDialog.Parse(text)`: 줄/쉼표/세미콜론/탭/공백 분리 + 중복 제거 → `List<PartRequest>`.
- `WorkScopeDialog`: 둘 다/품목정보관리만/BIN만 → `WorkScope?`.
- 설정 모델: `RootConfig{ Login, Safety, App, Workflow, Options, Categories{DramModule,DramComp}, Global }`. `Options{DefectWarehouses,BinTypes,RetestThs,BinCompletes}`. `CategoryConfig{ItemInfo{BinManage,TurnKey,AssemblyIn,DefectWarehouse}, BinInfo{ProcessSearchKey,Rows:List<BinRowConfig>{ProcessName,BinType,RetestNo,BinComplete,RetestTh}}}`. `LoginConfig{UserId,PasswordMode,Password,PasswordEncrypted,...,Language,System}`.
- `ConfigStore.Load(path)` / `ConfigStore.Save(path, config)`. `SecretProtector.Encrypt/Decrypt`.

---

## File Structure

생성:
- `src/UnimesAutomation/UiTheme.cs` — 다크 HUD 팔레트 + `Apply(Control)` 헬퍼.
- `src/UnimesAutomation/PartListParser.cs` — 파트 입력 파싱 순수 함수.
- `src/UnimesAutomation/MainForm.cs` — 메인 창.
- `src/UnimesAutomation/SettingsForm.cs` — 설정 창(좌측 메뉴 + 로그인/고급 패널).
- `src/UnimesAutomation/CategorySettingsControl.cs` — 분류별 설정 패널(품목/BIN 테이블).
- `tests/UnimesAutomation.Tests/PartListParserTests.cs`
- `tests/UnimesAutomation.Tests/LoggerEventTests.cs`

수정:
- `src/UnimesAutomation/LoggerSetup.cs` — `SimpleLogger.LineWritten` 이벤트.
- `src/UnimesAutomation/Program.cs` — GUI 기본 진입.

제거:
- `src/UnimesAutomation/WorkScopeDialog.cs`, `src/UnimesAutomation/PartInputDialog.cs`(메인 창으로 흡수).
- `appsettings.save-test.json`, `run_unimes_automation_save_test.cmd`(안전 토글로 대체).

문서: `CLAUDE.md`, `docs/ARCHITECTURE.md`, `docs/CONFIG.md`, `docs/STATUS.md`.

---

## Task 1: SimpleLogger 라인 이벤트 — TDD

**Files:** Modify `src/UnimesAutomation/LoggerSetup.cs`; Test `tests/UnimesAutomation.Tests/LoggerEventTests.cs`

- [ ] **Step 1: 실패 테스트**

`tests/UnimesAutomation.Tests/LoggerEventTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnimesAutomation;
using Xunit;

public class LoggerEventTests
{
    [Fact]
    public void LineWritten_fires_with_formatted_line()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unimes_log_{Guid.NewGuid():N}.log");
        var captured = new List<string>();
        using (var logger = new SimpleLogger(path))
        {
            logger.LineWritten += line => captured.Add(line);
            logger.Info("hello");
            logger.Warn("careful");
        }
        try
        {
            Assert.Equal(2, captured.Count);
            Assert.Contains("[INFO] hello", captured[0]);
            Assert.Contains("[WARN] careful", captured[1]);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: 실패 확인** — `dotnet test ... --filter LoggerEventTests` (컴파일 실패: LineWritten 없음).

- [ ] **Step 3: 구현** — `LoggerSetup.cs`의 `SimpleLogger`에 이벤트 추가, `Write`에서 발생:

클래스에 필드 추가(예: `_writer` 아래):
```csharp
    public event Action<string>? LineWritten;
```
`Write` 메서드를 다음으로 교체:
```csharp
    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_lock)
        {
            Console.WriteLine(line);
            _writer.WriteLine(line);
        }

        LineWritten?.Invoke(line);
    }
```

- [ ] **Step 4: 통과 확인** — `dotnet test ... --filter LoggerEventTests` PASS; 전체 회귀 없음.

- [ ] **Step 5: Commit** — `git commit -m "feat: add SimpleLogger.LineWritten event for UI log streaming"` (+ Co-Authored-By trailer; 이하 동일).

---

## Task 2: PartListParser — TDD

`PartInputDialog.Parse` 로직을 순수 함수로 추출(메인 창에서 재사용, 다이얼로그는 Task 8에서 제거).

**Files:** Create `src/UnimesAutomation/PartListParser.cs`; Test `tests/UnimesAutomation.Tests/PartListParserTests.cs`

- [ ] **Step 1: 실패 테스트**
```csharp
using System.Linq;
using UnimesAutomation;
using Xunit;

public class PartListParserTests
{
    [Fact]
    public void Splits_on_newline_comma_space_and_dedupes()
    {
        var parts = PartListParser.Parse("RM1, RM2\nRM3 RM2");
        Assert.Equal(new[] { "RM1", "RM2", "RM3" }, parts.Select(p => p.PartNo).ToArray());
    }

    [Fact]
    public void Empty_input_yields_empty()
    {
        Assert.Empty(PartListParser.Parse("   \n  "));
    }
}
```

- [ ] **Step 2: 실패 확인.**

- [ ] **Step 3: 구현** — `src/UnimesAutomation/PartListParser.cs`:
```csharp
namespace UnimesAutomation;

// 파트 입력 텍스트(줄/쉼표/세미콜론/탭/공백 구분)를 중복 없는 PartRequest 목록으로 만든다.
public static class PartListParser
{
    public static List<PartRequest> Parse(string text)
    {
        return (text ?? "")
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(partNo => new PartRequest { PartNo = partNo })
            .ToList();
    }
}
```

- [ ] **Step 4: 통과 확인(전체 테스트).**
- [ ] **Step 5: Commit** — `"refactor: extract PartListParser pure helper"`.

---

## Task 3: UiTheme (다크 HUD 팔레트 + 적용 헬퍼)

**Files:** Create `src/UnimesAutomation/UiTheme.cs`

스펙 §14 팔레트. 글로우는 WinForms 한계로 생략(평면 색). 표준 폼 보더는 유지(1차).

- [ ] **Step 1: 구현** — `src/UnimesAutomation/UiTheme.cs`:
```csharp
using System.Drawing;
using System.Windows.Forms;

namespace UnimesAutomation;

// JARVIS/HUD 풍 다크 테마. 색/폰트와 컨트롤 트리 일괄 적용 헬퍼.
public static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(10, 14, 23);     // #0A0E17
    public static readonly Color Surface = Color.FromArgb(12, 18, 31);        // #0C121F
    public static readonly Color SurfaceDeep = Color.FromArgb(6, 10, 17);     // #060A11
    public static readonly Color Border = Color.FromArgb(22, 59, 71);         // #163B47
    public static readonly Color Accent = Color.FromArgb(43, 212, 216);       // #2BD4D8 cyan
    public static readonly Color Warn = Color.FromArgb(245, 182, 66);         // #F5B642 gold
    public static readonly Color Danger = Color.FromArgb(255, 107, 107);      // #FF6B6B
    public static readonly Color Text = Color.FromArgb(191, 239, 243);        // #BFEFF3
    public static readonly Color TextDim = Color.FromArgb(127, 185, 194);     // #7FB9C2
    public static readonly Color TextFaint = Color.FromArgb(78, 107, 120);    // #4E6B78

    public static Font Mono(float size = 9f, FontStyle style = FontStyle.Regular)
        => new("Consolas", size, style);

    // 컨트롤 트리에 다크 색을 일괄 적용한다(버튼/텍스트박스/그리드 등 기본 스타일을 다크로).
    public static void Apply(Control root)
    {
        root.BackColor = root is Form ? Background : root.BackColor;
        ApplyRecursive(root);
    }

    private static void ApplyRecursive(Control control)
    {
        foreach (Control c in control.Controls)
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Border;
                    b.BackColor = Surface;
                    b.ForeColor = Text;
                    b.Font = Mono(9f);
                    break;
                case TextBox t:
                    t.BackColor = SurfaceDeep;
                    t.ForeColor = Text;
                    t.BorderStyle = BorderStyle.FixedSingle;
                    t.Font = Mono(9f);
                    break;
                case ComboBox combo:
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = SurfaceDeep;
                    combo.ForeColor = Text;
                    combo.Font = Mono(9f);
                    break;
                case Label l:
                    l.ForeColor = TextDim;
                    l.Font = Mono(9f);
                    break;
                case Panel p:
                    p.BackColor = Surface;
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
            }

            if (c.HasChildren)
            {
                ApplyRecursive(c);
            }
        }
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = SurfaceDeep;
        grid.GridColor = Border;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.DefaultCellStyle.BackColor = SurfaceDeep;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Border;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Font = Mono(9f);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
        grid.ColumnHeadersDefaultCellStyle.Font = Mono(9f);
        grid.RowHeadersVisible = false;
    }
}
```

- [ ] **Step 2: 빌드 + Commit** — `"feat: add dark HUD UiTheme palette and apply helper"`.

---

## Task 4: SettingsForm 골격 + 로그인/고급 패널

**Files:** Create `src/UnimesAutomation/SettingsForm.cs`

설계: SettingsForm은 `appsettings.json` 경로를 받아 **자체 작업본**을 `ConfigStore.Load`로 읽어 편집한다. `저장` 시 DPAPI 암호화 + `ConfigStore.Save` 후 `DialogResult.OK`. `취소`면 변경 폐기. 좌측 메뉴로 패널 전환(로그인 / DRAM Module / DRAM Comp / 고급). 분류 패널은 Task 5의 `CategorySettingsControl` 2개.

- [ ] **Step 1: 구현** — `src/UnimesAutomation/SettingsForm.cs`:
```csharp
using System.Drawing;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class SettingsForm : Form
{
    private readonly string _path;
    private readonly RootConfig _config;

    // 로그인
    private readonly TextBox _userId = new() { Width = 200 };
    private readonly TextBox _password = new() { Width = 200, UseSystemPasswordChar = true, PlaceholderText = "(변경하려면 입력)" };
    private readonly ComboBox _language = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _system = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    // 고급
    private readonly TextBox _launchPath = new() { Width = 460 };
    private readonly TextBox _recoveryPart = new() { Width = 300 };

    private readonly CategorySettingsControl _modulePanel;
    private readonly CategorySettingsControl _compPanel;
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    public SettingsForm(string appSettingsPath)
    {
        _path = appSettingsPath;
        _config = ConfigStore.Load(appSettingsPath);
        _modulePanel = new CategorySettingsControl(_config.Categories.DramModule, _config.Options) { Dock = DockStyle.Fill };
        _compPanel = new CategorySettingsControl(_config.Categories.DramComp, _config.Options) { Dock = DockStyle.Fill };

        Text = "설정";
        Width = 760; Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false;

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

        Controls.Add(_host);
        Controls.Add(nav);
        Controls.Add(bottom);
        CancelButton = cancel;
        ShowPanel(BuildLoginPanel());

        UiTheme.Apply(this);
        foreach (Control c in nav.Controls) { c.Width = 130; }
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
        void Row(string label, Control field) { p.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }); p.Controls.Add(field); }
        Row("아이디", _userId);
        Row("비밀번호", _password);
        Row("언어", _language);
        Row("시스템", _system);
        var note = new Label { Text = "비밀번호는 Windows 계정으로 암호화되어 이 PC에만 저장됩니다 (DPAPI).", AutoSize = true };
        p.Controls.Add(new Label { Text = "" }); p.Controls.Add(note);
        return p;
    }

    private Control BuildAdvancedPanel()
    {
        var p = new TableLayoutPanel { ColumnCount = 2, Padding = new Padding(16), AutoSize = true };
        p.Controls.Add(new Label { Text = "MES 실행 경로", AutoSize = true }); p.Controls.Add(_launchPath);
        p.Controls.Add(new Label { Text = "복구용 기파트", AutoSize = true }); p.Controls.Add(_recoveryPart);
        return p;
    }

    private void LoadLoginFields()
    {
        _userId.Text = _config.Login.UserId;
        _language.Items.AddRange(["한국어", "English"]); _language.SelectedItem = _config.Login.Language; if (_language.SelectedIndex < 0) _language.Text = _config.Login.Language;
        _system.Items.AddRange(["UNIMES"]); _system.SelectedItem = _config.Login.System; if (_system.SelectedIndex < 0) _system.Text = _config.Login.System;
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
        if (!string.IsNullOrEmpty(_password.Text))
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
}
```

- [ ] **Step 2: 빌드** — `CategorySettingsControl`가 아직 없으면 Task 5와 함께 빌드. (이 태스크와 Task 5는 같은 빌드 단위로 묶어도 됨.)

- [ ] **Step 3: Commit**(Task 5와 함께) — 아래 Task 5 Step 4 참고.

---

## Task 5: CategorySettingsControl (품목/BIN 테이블)

**Files:** Create `src/UnimesAutomation/CategorySettingsControl.cs`

품목정보관리 1행 + BIN 정보관리 테이블(드롭다운). 드롭다운 목록은 `OptionsConfig`. BIN ID는 자동 산출이라 컬럼에 두지 않는다(스펙: 자동). `ApplyTo(CategoryConfig)`로 편집값을 모델에 반영.

- [ ] **Step 1: 구현** — `src/UnimesAutomation/CategorySettingsControl.cs`:
```csharp
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class CategorySettingsControl : UserControl
{
    private readonly OptionsConfig _options;
    private readonly DataGridView _item = new() { Dock = DockStyle.Top, Height = 64, AllowUserToAddRows = false, AllowUserToResizeRows = false };
    private readonly DataGridView _bin = new() { Dock = DockStyle.Fill, AllowUserToResizeRows = false };

    public CategorySettingsControl(CategoryConfig category, OptionsConfig options)
    {
        _options = options;
        Padding = new Padding(12);

        BuildItemGrid();
        BuildBinGrid();

        var binHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        binHeader.Controls.Add(new Label { Text = "BIN 정보관리", AutoSize = true });
        var add = new Button { Text = "행 추가", AutoSize = true };
        var del = new Button { Text = "행 삭제", AutoSize = true };
        add.Click += (_, _) => _bin.Rows.Add();
        del.Click += (_, _) => { if (_bin.CurrentRow is { IsNewRow: false } r) _bin.Rows.Remove(r); };
        binHeader.Controls.Add(add); binHeader.Controls.Add(del);

        Controls.Add(_bin);
        Controls.Add(binHeader);
        Controls.Add(new Label { Text = "품목정보관리", Dock = DockStyle.Top, Height = 22 });
        Controls.Add(_item);

        LoadFrom(category);
    }

    private void BuildItemGrid()
    {
        _item.Columns.Add(YesNoCol("binManage", "BIN 관리"));
        _item.Columns.Add(YesNoCol("turnKey", "Turn Key"));
        _item.Columns.Add(YesNoCol("assemblyIn", "조립입고"));
        _item.Columns.Add(ComboCol("defectWarehouse", "불량창고", _options.DefectWarehouses));
        _item.AllowUserToAddRows = false;
    }

    private void BuildBinGrid()
    {
        _bin.Columns.Add(new DataGridViewTextBoxColumn { Name = "processName", HeaderText = "공정명" });
        _bin.Columns.Add(ComboCol("binType", "BIN Type", _options.BinTypes));
        _bin.Columns.Add(new DataGridViewTextBoxColumn { Name = "retestNo", HeaderText = "Retest No" });
        _bin.Columns.Add(ComboCol("binComplete", "BIN 완료여부", _options.BinCompletes));
        _bin.Columns.Add(ComboCol("retestTh", "Retest TH", _options.RetestThs));
    }

    private static DataGridViewComboBoxColumn YesNoCol(string name, string header)
        => ComboCol(name, header, ["Y", "N"]);

    private static DataGridViewComboBoxColumn ComboCol(string name, string header, IEnumerable<string> items)
    {
        var col = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, FlatStyle = FlatStyle.Flat };
        foreach (var i in items) col.Items.Add(i);
        return col;
    }

    private void LoadFrom(CategoryConfig category)
    {
        _item.Rows.Add(category.ItemInfo.BinManage, category.ItemInfo.TurnKey, category.ItemInfo.AssemblyIn, category.ItemInfo.DefectWarehouse);
        foreach (var r in category.BinInfo.Rows)
        {
            _item.ClearSelection();
            _bin.Rows.Add(r.ProcessName, r.BinType, r.RetestNo, r.BinComplete, r.RetestTh);
        }
        if (category.BinInfo.Rows.Count == 0) _bin.Rows.Add();
    }

    public void ApplyTo(CategoryConfig category)
    {
        var row = _item.Rows[0];
        category.ItemInfo.BinManage = Cell(row, "binManage");
        category.ItemInfo.TurnKey = Cell(row, "turnKey");
        category.ItemInfo.AssemblyIn = Cell(row, "assemblyIn");
        category.ItemInfo.DefectWarehouse = Cell(row, "defectWarehouse");

        var rows = new List<BinRowConfig>();
        foreach (DataGridViewRow r in _bin.Rows)
        {
            if (r.IsNewRow) continue;
            var process = Cell(r, "processName");
            rows.Add(new BinRowConfig
            {
                ProcessName = process,
                BinType = Cell(r, "binType"),
                RetestNo = Cell(r, "retestNo"),
                BinComplete = Cell(r, "binComplete"),
                RetestTh = Cell(r, "retestTh")
            });
        }
        if (rows.Count > 0)
        {
            category.BinInfo.Rows = rows;
            category.BinInfo.ProcessSearchKey = rows[0].ProcessName;
        }
    }

    private static string Cell(DataGridViewRow row, string col)
        => row.Cells[col].Value?.ToString() ?? "";
}
```

- [ ] **Step 2: 빌드** — `dotnet build` (Task 4 + 5 함께). 0 오류.
- [ ] **Step 3: 수동 확인(선택)** — 임시로 `SettingsForm`을 띄워보거나, 빌드만으로 진행하고 Task 6/7 후 실제로 띄워 확인.
- [ ] **Step 4: Commit** — `git add SettingsForm.cs CategorySettingsControl.cs` → `"feat: add SettingsForm and CategorySettingsControl"`.

> **수동 검증 메모:** WinForms UI는 단위테스트가 어렵다. 저장 왕복(값 변경→저장→`appsettings.json` 반영→재오픈 시 로드)은 Task 7 후 실제 실행으로 확인한다.

---

## Task 6: MainForm (입력·안전토글·실행·창 안 로그 / Approach 2)

**Files:** Create `src/UnimesAutomation/MainForm.cs`

설계: MainForm은 `RootConfig`, `RuntimePaths`, `SimpleLogger`, `ScreenshotService`, `CommandLineOptions`, `appSettingsPath`를 받는다. `실행`은 입력을 `config.Workflow`에 담고 **백그라운드 스레드**에서 `new UnimesApp(config,...).RunAsync(options)`를 돌린다. `logger.LineWritten` 구독 → `BeginInvoke`로 로그 패널에 append(줄 수 제한). 실행 중 입력 비활성화, `실행`→`중지`(중지는 창 닫기 안내 — 강제 중단은 범위 밖, 버튼은 비활성 표시). 안전 토글은 `config.Safety` 변경, 실제 저장 켤 때 확인 다이얼로그.

- [ ] **Step 1: 구현** — `src/UnimesAutomation/MainForm.cs`:
```csharp
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
        _config = config; _paths = paths; _logger = logger; _screenshots = screenshots; _options = options; _appSettingsPath = appSettingsPath;

        Text = "UNIMES 자동화";
        Width = 620; Height = 640; StartPosition = FormStartPosition.CenterScreen;

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
        _log.BackColor = UiTheme.SurfaceDeep; _log.ForeColor = UiTheme.TextDim; _log.Font = UiTheme.Mono(9f);
    }

    private void OnLogLine(string line)
    {
        if (IsDisposed) return;
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
        catch (System.Exception) { /* form closing */ }
    }

    private void UpdateSafetyLabel()
    {
        _safety.Text = _config.Safety.SaveEnabled && !_config.Safety.DryRun
            ? "● 실제 저장 모드 (저장 켜짐)"
            : "안전 모드 · 변경 미리보기 (저장 잠금)";
        _safety.ForeColor = _config.Safety.SaveEnabled && !_config.Safety.DryRun ? UiTheme.Danger : UiTheme.Warn;
    }

    private void ToggleSafety()
    {
        var turningOn = _config.Safety.DryRun || !_config.Safety.SaveEnabled;
        if (turningOn)
        {
            var ok = MessageBox.Show("정말 실제 MES에 저장을 켜시겠습니까? 저장 동작이 실제로 일어납니다.",
                "실제 저장 켜기", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ok != DialogResult.Yes) return;
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
            var reloaded = ConfigStore.Load(_appSettingsPath);
            CopyInto(reloaded);
            UpdateSafetyLabel();
            _logger.Info("설정이 저장되어 다음 실행부터 반영됩니다.");
        }
    }

    // 재로드한 설정을 기존 인스턴스에 복사(참조를 들고 있는 곳과의 일관성 위해 필드 단위 반영).
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
        if (_running) return;
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
```

- [ ] **Step 2: 빌드** — 0 오류.
- [ ] **Step 3: Commit** — `"feat: add MainForm with in-window log streaming (Approach 2)"`.

---

## Task 7: Program GUI 진입

**Files:** Modify `src/UnimesAutomation/Program.cs`

`--dump-only`/`--help`는 콘솔 유지. 그 외에는 MainForm을 띄운다. 다이얼로그 호출 제거.

- [ ] **Step 1: Main 수정** — `Main`의 본문에서, `if (options.Help) { PrintHelp(); return 0; }` 다음, 설정/서비스 생성 후의 **`WorkScopeDialog`/`PartInputDialog` 블록과 `return app.RunAsync(...)` 부분**을 다음으로 교체:
```csharp
            var config = LoadConfig(options.ConfigPath, rootDirectory, logger);
            var screenshots = new ScreenshotService(paths, logger);

            if (options.DumpOnly)
            {
                var safety = new SafetyGuard(config.Safety, logger);
                var app = new UnimesApp(config, paths, logger, screenshots, safety);
                return app.RunAsync(options).GetAwaiter().GetResult();
            }

            var appSettingsPath = Path.Combine(rootDirectory, "appsettings.json");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(config, paths, logger, screenshots, options, appSettingsPath));
            return 0;
```
파일 상단에 `using System.Windows.Forms;`가 없으면 추가. 사용하지 않게 된 `HasExistingLoggedInMainWindow` 사전 호출/로그가 Main에 있으면 제거(중복). `[STAThread]`는 이미 있음(유지).

- [ ] **Step 2: 빌드 + dump-only 스모크** — `dotnet build`; `dotnet run -- --config ./appsettings.example.json --dump-only --no-launch` → config 로드 후 종료(파싱 에러 없음, MES 없으면 timeout만).
- [ ] **Step 3: Commit** — `"feat: launch MainForm as default GUI entry point"`.

> **라이브 수동 검증(이 태스크 후):** GUI를 띄워 파트 입력→작업범위→실행 시 창 안 로그 스트리밍, 설정 저장 왕복, 안전 토글 확인. 실제 MES 연동은 사용자가 확인.

---

## Task 8: 정리(구 다이얼로그·save-test 제거) + 문서

**Files:** Delete `WorkScopeDialog.cs`, `PartInputDialog.cs`, `appsettings.save-test.json`, `run_unimes_automation_save_test.cmd`; Modify docs + `run_unimes_automation.cmd`

- [ ] **Step 1: 파일 제거**
```bash
git rm src/UnimesAutomation/WorkScopeDialog.cs src/UnimesAutomation/PartInputDialog.cs
git rm run_unimes_automation_save_test.cmd
rm -f appsettings.save-test.json   # gitignored, 추적 안 됨 → 로컬 삭제만
```

- [ ] **Step 2: 빌드 + 전체 테스트** — 0 오류, 모든 테스트 PASS. (`WorkScopeDialog`/`PartInputDialog` 참조가 남아 있으면 제거 — Program에서 이미 제거됨.)

- [ ] **Step 3: run_unimes_automation.cmd 안내 문구 갱신** — "Part No input dialog will open first" → "메인 창이 열립니다" 류로 수정(선택, 1줄).

- [ ] **Step 4: 문서 갱신**
  - `CLAUDE.md` 빌드/실행 섹션: GUI 진입을 반영(예: 인자 없이 실행하면 메인 창). 저장 게이트 설명에 "메인 창 안전 토글" 추가.
  - `docs/ARCHITECTURE.md`: 진입 흐름을 MainForm 기준으로, 파일 맵에 MainForm/SettingsForm/CategorySettingsControl/UiTheme 추가, WorkScopeDialog/PartInputDialog 제거.
  - `docs/CONFIG.md`: `passwordMode` 에 `dpapi` 설명 추가, 구조가 `categories`/`options`/`global` 기준임을 반영(이미 Plan 1에서 일부). save-test 언급 제거.
  - `docs/STATUS.md`: 진행/주의점에 GUI·안전 토글 반영, `appsettings.save-test.json` 줄 제거.

- [ ] **Step 5: Commit** — `"chore: remove legacy dialogs/save-test; update docs for GUI"`.

---

## Self-Review (작성자 체크 — 완료)

- **스펙 커버리지**: §3 Approach 2 실행(Task 1,6) / §6 메인 창(Task 6) / §7 설정 창(Task 4,5) / §10 제거·진입(Task 7,8) / §14 다크 테마(Task 3, 전 폼 적용).
- **타입 일관성**: `UiTheme`, `PartListParser.Parse`, `SimpleLogger.LineWritten`, `SettingsForm(appSettingsPath)`, `CategorySettingsControl(category, options)`+`ApplyTo`, `MainForm(config, paths, logger, screenshots, options, appSettingsPath)` 명칭 일치.
- **플레이스홀더 없음**: 신규 파일 전체 코드 포함. 테스트 가능한 순수 로직(Logger 이벤트·PartListParser)만 TDD, WinForms는 빌드+라이브 수동 검증으로 명시.
- **리스크/한계**: WinForms는 단위테스트 불가 → 저장 왕복·실행·테마는 라이브 확인 필요. 안전 토글은 같은 `config.Safety` 인스턴스 변경으로 `SafetyGuard`에 반영. 강제 중단(중지) 미구현(창 닫기/프로세스 종료로 대체) — 필요 시 후속.

## 검증

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
dotnet test  .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj
```
그리고 GUI 실 실행으로 입력/실행/설정 저장/안전 토글/창 안 로그를 사용자가 확인.
