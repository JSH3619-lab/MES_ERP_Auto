# 품목별 BIN 정보 관리 자동화 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `품목별 BIN 정보 관리` 화면을 자동화해, 신규 파트마다 BIN 행 1개를 추가하고 분류·용량에 맞는 값을 채워 저장한다.

**Architecture:** 순수 로직(`BinIdResolver`)은 단위테스트로 못박고, 화면 동작(`RunBinInfoWorkflowAsync`)은 `UnimesApp`에 추가해 기존 팝업/콤보/SendKeys/`Ctrl+S` 헬퍼를 재사용한다. 시작 시 작업선택 다이얼로그로 품목정보/ BIN/둘다를 고른다.

**Tech Stack:** .NET 8 (net8.0-windows), C#, System.Windows.Automation(UIA), WinForms, xUnit(신규 테스트).

> **커밋 정책 (사용자 지정):** 모든 커밋은 사용자가 **live MES에서 정상 동작 확인한 뒤 일괄 진행**한다. 각 Task의 `Commit` 스텝은 그 시점까지 **보류**하고, 검증 후 묶어서 실행한다. (BinIdResolver 단위테스트는 MES 없이 로컬 검증 가능.)

> **설계 출처:** [docs/specs/2026-06-17-bin-info-automation-design.md](../specs/2026-06-17-bin-info-automation-design.md)

---

## File Structure

- Create `src/UnimesAutomation/BinIdResolver.cs` — 파트번호 → (분류, 공정검색키, BIN ID 이름) 순수 로직.
- Create `src/UnimesAutomation/WorkScopeDialog.cs` — 작업선택 WinForms 다이얼로그.
- Create `tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj` — xUnit 테스트 프로젝트.
- Create `tests/UnimesAutomation.Tests/BinIdResolverTests.cs` — BinIdResolver 단위테스트.
- Modify `src/UnimesAutomation/Models.cs` — `WorkScope` enum, `BinInfoConfig`, `Workflow.ShowWorkScopeDialog`/`RuntimeWorkScope`, `RootConfig.BinInfo`.
- Modify `src/UnimesAutomation/Program.cs` — 작업선택 다이얼로그 표시.
- Modify `src/UnimesAutomation/UnimesApp.cs` — 오케스트레이션 분기 + `RunBinInfoWorkflowAsync` + 공유 헬퍼 + 정상 파트 반환.
- Modify `docs/STATUS.md`, `docs/CONFIG.md` — 항목 추가.

---

## Task 1: Config & enums (Models.cs)

**Files:**
- Modify: `src/UnimesAutomation/Models.cs`

- [ ] **Step 1: Add `WorkScope` enum**

`Models.cs` 최상단 namespace 블록 안(다른 enum/클래스 옆)에 추가:

```csharp
public enum WorkScope
{
    ItemInfo,
    BinInfo,
    Both
}
```

- [ ] **Step 2: Add `BinInfoConfig` class** (ItemInfoConfig 바로 아래)

```csharp
public sealed class BinInfoConfig
{
    [JsonPropertyName("menuName")]
    public string MenuName { get; set; } = "품목별 BIN 정보 관리";

    [JsonPropertyName("moduleProcessKey")]
    public string ModuleProcessKey { get; set; } = "M050";

    [JsonPropertyName("compProcessKey")]
    public string CompProcessKey { get; set; } = "C010";

    [JsonPropertyName("binType")]
    public string BinType { get; set; } = "Normal-1";

    [JsonPropertyName("retestNo")]
    public string RetestNo { get; set; } = "0";

    [JsonPropertyName("binComplete")]
    public string BinComplete { get; set; } = "Y";

    [JsonPropertyName("retestTh")]
    public string RetestTh { get; set; } = "H";
}
```

- [ ] **Step 3: Add `Workflow` fields** — `WorkflowConfig`에 추가:

```csharp
    [JsonPropertyName("showWorkScopeDialog")]
    public bool ShowWorkScopeDialog { get; set; } = true;

    [JsonIgnore]
    public WorkScope RuntimeWorkScope { get; set; } = WorkScope.ItemInfo;
```

- [ ] **Step 4: Add `BinInfo` to `RootConfig`** — `RootConfig`에 프로퍼티 추가하고 `CreateDefault()` 반환 객체에 `BinInfo = new BinInfoConfig()` 추가:

```csharp
    [JsonPropertyName("binInfo")]
    public BinInfoConfig BinInfo { get; set; } = new();
```
`CreateDefault()` 마지막의 `ItemInfo = new ItemInfoConfig()` 다음 줄에 `, BinInfo = new BinInfoConfig()` 추가.

- [ ] **Step 5: Build**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj`
Expected: 경고 0 / 오류 0.

- [ ] **Step 6: Commit (보류)**

```bash
git add src/UnimesAutomation/Models.cs
git commit -m "feat: add BinInfoConfig and WorkScope config"
```

---

## Task 2: BinIdResolver (TDD)

**Files:**
- Create: `tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj`
- Create: `tests/UnimesAutomation.Tests/BinIdResolverTests.cs`
- Create: `src/UnimesAutomation/BinIdResolver.cs`

- [ ] **Step 1: Create test project**

`tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\UnimesAutomation\UnimesAutomation.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing tests**

`tests/UnimesAutomation.Tests/BinIdResolverTests.cs`:

```csharp
using UnimesAutomation;
using Xunit;

public class BinIdResolverTests
{
    private static readonly BinInfoConfig Cfg = new();

    [Theory]
    [InlineData("RMRDAG58A1P-GPWRRWM7", "RAM_Module_Normal_16GB")] // AG=16GB, DDR무관
    [InlineData("RMRD8G58A1P-GPWRRWM7", "RAM_Module_Normal_8GB")]  // 8G=8GB
    [InlineData("RMRDBG58A1P-GPWRRWM7", "RAM_Module_Normal_32GB")] // BG=32GB
    [InlineData("RMRDCG58A1P-GPWRRWM7", "RAM_Module_Normal_64GB")] // CG=64GB(미등록이어도 이름은 계산)
    public void Module_resolves_capacity_only(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, Cfg);
        Assert.NotNull(target);
        Assert.Equal(PartClass.Module, target!.Class);
        Assert.Equal("M050", target.ProcessSearchKey);
        Assert.Equal(expected, target.BinIdName);
    }

    [Theory]
    [InlineData("RCA8G58A1P-XPWRRWM7", "DRAM_Comp_Bin_8Gb")]            // DDR4 8Gb
    [InlineData("RCAAG58A1P-XPWRRWM7", "DRAM_Comp_Bin_16Gb")]          // DDR4 16Gb
    [InlineData("RCRAH58A1P-XPWRRWM7", "DRAM_Comp_D5_XMP72_Bin_16Gb")] // DDR5 16Gb
    public void Comp_resolves_with_ddr(string part, string expected)
    {
        var target = BinIdResolver.Resolve(part, Cfg);
        Assert.NotNull(target);
        Assert.Equal(PartClass.Comp, target!.Class);
        Assert.Equal("C010", target.ProcessSearchKey);
        Assert.Equal(expected, target.BinIdName);
    }

    [Theory]
    [InlineData("XXRDAG58A1P-GPWRRWM7")] // 분류 실패
    [InlineData("RMRDZZ58A1P-GPWRRWM7")] // 모듈 용량코드 미지원
    [InlineData("RC")]                    // 길이 부족
    public void Unresolvable_returns_null(string part)
    {
        Assert.Null(BinIdResolver.Resolve(part, Cfg));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail (compile error: BinIdResolver 없음)**

Run: `dotnet test ./tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj`
Expected: FAIL — `BinIdResolver` / `BinInfoTarget` 미정의로 빌드 실패.

- [ ] **Step 4: Implement BinIdResolver**

`src/UnimesAutomation/BinIdResolver.cs`:

```csharp
namespace UnimesAutomation;

public sealed record BinInfoTarget(PartClass Class, string ProcessSearchKey, string BinIdName);

// 파트번호에서 BIN 정보 처리에 필요한 파생값을 계산한다. 화면 동작과 분리된 순수 로직.
public static class BinIdResolver
{
    // 모듈 용량코드 → GB (DDR 무관)
    private static readonly Dictionary<string, int> ModuleDensityGb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1G"] = 1, ["2G"] = 2, ["4G"] = 4, ["8G"] = 8, ["AG"] = 16, ["BG"] = 32, ["CG"] = 64
    };

    // Comp 용량코드 → (Gb, DDR5 여부)
    private static readonly Dictionary<string, (int Gb, bool Ddr5)> CompDensity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4G"] = (4, false), ["8G"] = (8, false), ["AG"] = (16, false),
        ["AH"] = (16, true), ["HE"] = (24, true), ["BH"] = (32, true)
    };

    public static BinInfoTarget? Resolve(string partNo, BinInfoConfig config)
    {
        var code = (partNo ?? "").Trim();
        var cls = PartClassifier.Classify(code);

        if (cls == PartClass.Module)
        {
            // [소싱2][DRAM1][DIMM1][용량2] → 용량 = index 4..5
            if (code.Length < 6 || !ModuleDensityGb.TryGetValue(code.Substring(4, 2), out var gb))
            {
                return null;
            }

            return new BinInfoTarget(cls, config.ModuleProcessKey, $"RAM_Module_Normal_{gb}GB");
        }

        if (cls == PartClass.Comp)
        {
            // [소싱2][DRAM1][용량2] → 용량 = index 3..4
            if (code.Length < 5 || !CompDensity.TryGetValue(code.Substring(3, 2), out var info))
            {
                return null;
            }

            var name = info.Ddr5
                ? $"DRAM_Comp_D5_XMP72_Bin_{info.Gb}Gb"
                : $"DRAM_Comp_Bin_{info.Gb}Gb";
            return new BinInfoTarget(cls, config.CompProcessKey, name);
        }

        return null;
    }
}
```

- [ ] **Step 5: Run tests to verify pass**

Run: `dotnet test ./tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj`
Expected: PASS (10 tests).

- [ ] **Step 6: Commit (보류)**

```bash
git add tests/UnimesAutomation.Tests src/UnimesAutomation/BinIdResolver.cs
git commit -m "feat: add BinIdResolver with unit tests"
```

---

## Task 3: WorkScopeDialog (WinForms)

**Files:**
- Create: `src/UnimesAutomation/WorkScopeDialog.cs`

- [ ] **Step 1: Create dialog** (PartInputDialog 패턴 동형)

`src/UnimesAutomation/WorkScopeDialog.cs`:

```csharp
using System.Windows.Forms;

namespace UnimesAutomation;

public static class WorkScopeDialog
{
    // 사용자가 작업 범위를 고른다. 취소 시 null.
    public static WorkScope? ShowDialog(IWin32Window? owner = null)
    {
        using var form = new Form
        {
            Text = "작업 선택",
            Width = 360,
            Height = 230,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var both = new RadioButton { Text = "둘 다 (품목정보관리 → BIN 정보 관리)", Left = 24, Top = 20, Width = 300, Checked = true };
        var itemOnly = new RadioButton { Text = "품목정보관리만", Left = 24, Top = 52, Width = 300 };
        var binOnly = new RadioButton { Text = "BIN 정보 관리만", Left = 24, Top = 84, Width = 300 };

        var ok = new Button { Text = "시작", Left = 150, Top = 140, Width = 85, Height = 30, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", Left = 245, Top = 140, Width = 85, Height = 30, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange([both, itemOnly, binOnly, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
        if (result != DialogResult.OK)
        {
            return null;
        }

        if (itemOnly.Checked) return WorkScope.ItemInfo;
        if (binOnly.Checked) return WorkScope.BinInfo;
        return WorkScope.Both;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj`
Expected: 경고 0 / 오류 0.

- [ ] **Step 3: Commit (보류)**

```bash
git add src/UnimesAutomation/WorkScopeDialog.cs
git commit -m "feat: add WorkScopeDialog"
```

---

## Task 4: 오케스트레이션 wiring

**Files:**
- Modify: `src/UnimesAutomation/Program.cs:28-40`
- Modify: `src/UnimesAutomation/UnimesApp.cs` (RunAsync 분기, RunItemInfoWorkflowAsync 반환, FindNamedWindow 헬퍼)

- [ ] **Step 1: Program.cs — 작업선택 다이얼로그**

`Program.cs`에서 `if (config.Workflow.Enabled && config.Workflow.ShowPartInputDialog && !options.DumpOnly)` 블록 **바로 앞**에 추가:

```csharp
            if (config.Workflow.Enabled && config.Workflow.ShowWorkScopeDialog && !options.DumpOnly)
            {
                var scope = WorkScopeDialog.ShowDialog();
                if (scope is null)
                {
                    logger.Info("Work scope selection cancelled by user.");
                    return 0;
                }

                config.Workflow.RuntimeWorkScope = scope.Value;
                logger.Info($"Work scope selected: {scope.Value}");
            }
```

- [ ] **Step 2: UnimesApp — RunItemInfoWorkflowAsync가 정상 파트 반환**

`RunItemInfoWorkflowAsync` 시그니처를 `private async Task<List<PartRequest>> RunItemInfoWorkflowAsync(AutomationElement mainWindow)` 로 변경.
메서드 안에 `var validParts = new List<PartRequest>();` 선언(요청 루프 시작 전). 각 Part 처리에서 **조회 성공(행 발견)** 한 경우 — `result.Status`가 `SKIPPED`/`ERROR`가 아닌 경로 — 에서 `validParts.Add(request);` 추가. 메서드 끝에서 `return validParts;`.
(미존재 SKIP / classification 실패 SKIP / ERROR continue 경로에는 추가하지 않는다.)

- [ ] **Step 3: UnimesApp — RunAsync 분기**

`RunAsync`의 `if (_config.Workflow.Enabled) { await RunItemInfoWorkflowAsync(mainWindow); }` (line ~124-127) 을 교체:

```csharp
        if (_config.Workflow.Enabled)
        {
            var scope = _config.Workflow.RuntimeWorkScope;
            var requests = _config.Workflow.RuntimePartRequests;

            List<PartRequest> binParts = requests.ToList();
            if (scope == WorkScope.ItemInfo || scope == WorkScope.Both)
            {
                var valid = await RunItemInfoWorkflowAsync(mainWindow);
                if (scope == WorkScope.Both)
                {
                    binParts = valid;
                }
            }

            if (scope == WorkScope.BinInfo || scope == WorkScope.Both)
            {
                await RunBinInfoWorkflowAsync(mainWindow, binParts);
            }
        }
```

- [ ] **Step 4: UnimesApp — FindNamedWindow 헬퍼 추가 + FindItemInfoWindow 위임**

기존 `FindItemInfoWindow` 본문을 일반화한 헬퍼로 바꾼다:

```csharp
    private AutomationElement? FindNamedWindow(AutomationElement mainWindow, string name)
    {
        return FindDescendants(mainWindow, ControlType.Window)
            .Where(window => string.Equals(SafeRead(() => window.Current.Name) ?? "", name, StringComparison.Ordinal))
            .Where(window =>
            {
                var rect = SafeReadRect(() => window.Current.BoundingRectangle);
                return rect.HasValue && !rect.Value.IsEmpty;
            })
            .LastOrDefault();
    }

    private AutomationElement? FindItemInfoWindow(AutomationElement mainWindow)
        => FindNamedWindow(mainWindow, _config.ItemInfo.MenuName);
```
(기존 `FindItemInfoWindow` 본문 블록을 위 2개로 교체.)

- [ ] **Step 5: UnimesApp — RunBinInfoWorkflowAsync 임시 stub (Task 6에서 구현)**

컴파일 통과용 최소 stub 추가:

```csharp
    private async Task RunBinInfoWorkflowAsync(AutomationElement mainWindow, IReadOnlyList<PartRequest> requests)
    {
        _logger.Warn($"RunBinInfoWorkflowAsync stub. count={requests.Count} (구현 예정)");
        await Task.CompletedTask;
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj`
Expected: 경고 0 / 오류 0.

- [ ] **Step 7: Commit (보류)**

```bash
git add src/UnimesAutomation/Program.cs src/UnimesAutomation/UnimesApp.cs
git commit -m "feat: wire work-scope orchestration with bin workflow stub"
```

---

## Task 5: UI 디스커버리 (live MES + 덤프) — **체크포인트**

코드 작성 전, BIN 화면의 automation id/구조를 덤프로 확정한다. **사용자가 실행**하고 결과 덤프를 분석한다.

**Files:** 없음(조사). 산출물: 아래 값들을 Task 6 구현에 사용.

- [ ] **Step 1: BIN 화면 덤프 수집**

품목별 BIN 정보 관리 탭을 연 상태(파트 1개 조회 후 행추가까지 해 둔 상태 포함)에서:
Run: `dotnet run --project ./src/UnimesAutomation/UnimesAutomation.csproj -- --dump-only --no-launch`
Expected: `logs/ui_dump_*.txt` 생성.

- [ ] **Step 2: 다음 항목 식별·기록**

- `품목 ID` 입력칸: label `품목 ID` 인접 Edit (FindEditNextToLabel로 가능한지 확인).
- 900014 경고창: 창 이름/`확인` 버튼 automation id (없으면 Enter fallback).
- 행추가 결과 행의 편집 셀: `공정명`/`BIN Type`/`Retest No`/`Bin완료여부`/`Retest TH` 의 ControlType(ComboBox/Edit)·name.
- `공정명` 우측 검색버튼 automation id, 검색 팝업의 `Segment ID` 입력칸·조회/확인 버튼·결과 그리드 열.
- `BIN ID` 검색버튼 automation id, 팝업 필터 입력칸·결과 그리드 열(이름 매칭에 쓸 열).

- [ ] **Step 3: 기록**

확정한 id/이름을 이 plan의 Task 6 코드 내 `// 디스커버리:` 주석 자리에 채운다.

---

## Task 6: RunBinInfoWorkflowAsync 구현

**Files:**
- Modify: `src/UnimesAutomation/UnimesApp.cs` (stub 교체 + 헬퍼 추가)

> Task 5에서 확정한 selector를 사용한다. 가능한 곳은 기존 헬퍼(`FindEditNextToLabel`, `FindButtonByAnyName`, `FindByAutomationId`, `ApplyComboCell`, `SetElementText`, `CommitField`, `SaveItemInfo`, `HandleOpenPartIdPopupAsync`)를 재사용한다.

- [ ] **Step 1: 900014 경고 확인 헬퍼**

```csharp
    // [900014] 검색된 Data가 없습니다 등 메시지 팝업을 확인 처리한다(신규 파트의 정상 신호).
    private async Task ConfirmNoDataPopupAsync()
    {
        var warning = FindWarningDialog();
        var ok = warning is null ? null : FindButtonByAnyName(warning, ["확인", "OK"]);
        if (ok is not null)
        {
            ClickElement(ok, "no-data(900014) confirm");
            _logger.Info("BIN 900014 경고창 [확인] 처리.");
            await Task.Delay(300);
            return;
        }

        SendKeys.SendWait("{ENTER}");
        _logger.Info("BIN 900014 경고창 Enter fallback 전송.");
        await Task.Delay(400);
    }
```

- [ ] **Step 2: 검색 팝업에서 검색→선택 헬퍼**

```csharp
    // 셀 옆 검색 버튼 → 팝업 → 검색키 입력 → Enter(조회) → Enter(선택).
    // 공정명(Segment ID에 M050/C010)용. 성공 추정 시 true.
    private async Task<bool> SearchPopupSelectByEnterAsync(AutomationElement searchButton, string searchKey, string label)
    {
        ClickElement(searchButton, $"{label} 검색 버튼");
        var popup = await WaitForPartIdPopupAsync(TimeSpan.FromMilliseconds(1500)); // 디스커버리: 동일 팝업 식별자 재사용 가능여부 확인
        if (popup is null)
        {
            _logger.Warn($"{label} 검색 팝업 미감지.");
            return false;
        }

        var input = FindPopupProductCodeEdit(popup); // 디스커버리: Segment ID 입력칸 id로 교체 가능
        if (input is null)
        {
            _logger.Warn($"{label} 검색 입력칸 미발견.");
            return false;
        }

        SetElementText(input, searchKey, $"{label} 검색키");
        TryFocus(input, $"{label} 검색키");
        await Task.Delay(200);
        SendKeys.SendWait("{ENTER}"); // 조회
        await Task.Delay(500);
        SendKeys.SendWait("{ENTER}"); // 선택
        await WaitForPartIdPopupClosedAsync(TimeSpan.FromMilliseconds(1500));
        _logger.Info($"{label} 검색 선택 완료. key='{searchKey}'");
        return true;
    }
```

- [ ] **Step 3: BIN ID 검색 — 정확 일치 행 선택 헬퍼**

```csharp
    // BIN ID 팝업에서 binIdName 정확 일치 행을 선택. 미발견 시 false(저장 금지 신호).
    private async Task<bool> SearchPopupSelectExactAsync(AutomationElement searchButton, string binIdName)
    {
        ClickElement(searchButton, "BIN ID 검색 버튼");
        var popup = await WaitForPartIdPopupAsync(TimeSpan.FromMilliseconds(1500));
        if (popup is null)
        {
            _logger.Warn("BIN ID 검색 팝업 미감지.");
            return false;
        }

        var input = FindPopupProductCodeEdit(popup); // 디스커버리: BIN ID 팝업 필터 입력칸으로 교체 가능
        if (input is not null)
        {
            SetElementText(input, binIdName, "BIN ID 검색키");
            TryFocus(input, "BIN ID 검색키");
            await Task.Delay(200);
            SendKeys.SendWait("{ENTER}");
            await WaitForPartIdPopupResultAsync(binIdName, TimeSpan.FromMilliseconds(2000));
        }

        var refreshed = FindPartIdPopup();
        var row = refreshed is null ? null : FindPopupRowByProductCode(refreshed, binIdName);
        if (refreshed is null || row is null)
        {
            _logger.Warn($"BIN ID 미발견(미등록 가능). binId='{binIdName}'");
            if (refreshed is not null)
            {
                await CancelPartIdPopupAsync(binIdName);
            }

            return false;
        }

        await SelectPartIdPopupRowAsync(refreshed, row, binIdName);
        _logger.Info($"BIN ID 선택 완료. binId='{binIdName}'");
        return true;
    }
```

- [ ] **Step 4: 고정 셀 입력 헬퍼**

```csharp
    // 추가된 BIN 행의 고정 셀을 채운다. 디스커버리에서 셀이 ComboBox면 ApplyComboCell,
    // Edit면 SetElementText. 아래는 ComboBox 가정(실제 타입은 Task 5로 확정).
    private void FillFixedBinCells(AutomationElement row)
    {
        ApplyComboCell(row, "BIN Type", _config.BinInfo.BinType, readOnlyMode: false);
        ApplyComboCell(row, "Retest No", _config.BinInfo.RetestNo, readOnlyMode: false);
        ApplyComboCell(row, "Bin완료여부", _config.BinInfo.BinComplete, readOnlyMode: false);
        ApplyComboCell(row, "Retest TH", _config.BinInfo.RetestTh, readOnlyMode: false);
    }
```
> 디스커버리에서 `Retest No`가 Edit면 해당 줄을 `SetElementText(FindGridCell(row, "Retest No"), _config.BinInfo.RetestNo, "Retest No")` 로 교체. 컬럼명은 Task 5 확정값 사용.

- [ ] **Step 5: 워크플로 본문 (stub 교체)**

```csharp
    private async Task RunBinInfoWorkflowAsync(AutomationElement mainWindow, IReadOnlyList<PartRequest> requests)
    {
        if (requests.Count == 0)
        {
            _logger.Info("BIN 정보 관리 대상 Part 없음. 건너뜀.");
            return;
        }

        _logger.Info($"품목별 BIN 정보 관리 workflow started. count={requests.Count}, dryRun={_config.Safety.DryRun}, saveEnabled={_config.Safety.SaveEnabled}");
        await NavigateToMenuByF3Async(mainWindow, _config.BinInfo.MenuName);

        AutomationElement binWindow = FindNamedWindow(mainWindow, _config.BinInfo.MenuName) ?? mainWindow;
        AutomationElement? partIdEdit = null;

        foreach (var request in requests)
        {
            try
            {
                _logger.Info($"BIN part started. part='{request.PartNo}'");
                BringToFront(mainWindow);

                var target = BinIdResolver.Resolve(request.PartNo, _config.BinInfo);
                if (target is null)
                {
                    _logger.Warn($"BIN 분류/용량 파싱 실패로 건너뜀. part='{request.PartNo}'");
                    continue;
                }

                if (!IsElementUsable(binWindow)) binWindow = FindNamedWindow(mainWindow, _config.BinInfo.MenuName) ?? mainWindow;
                if (!IsElementUsable(partIdEdit)) partIdEdit = FindEditNextToLabel(binWindow, "품목 ID"); // 디스커버리: 라벨 텍스트 확정
                if (partIdEdit is null)
                {
                    _logger.Error($"BIN 품목 ID 입력칸 미발견. part='{request.PartNo}'");
                    continue;
                }

                SetElementText(partIdEdit, request.PartNo, "BIN 품목 ID");
                CommitField();
                await Task.Delay(200);
                await HandleOpenPartIdPopupAsync(request.PartNo); // 자동 PartID 팝업 뜨면 기존 처리 재사용

                // 데이터 없음(900014) 확인 → 신규 등록 진행
                await ConfirmNoDataPopupAsync();

                // 행추가
                BringToFront(mainWindow);
                SendKeys.SendWait("^{INSERT}"); // Ctrl+Insert
                await Task.Delay(300);

                var row = FindNewBinRow(binWindow); // 디스커버리: 추가된 입력 행 식별
                if (row is null)
                {
                    _logger.Error($"BIN 추가 행 미발견. part='{request.PartNo}'");
                    continue;
                }

                // 공정명 검색
                var processSearchButton = FindProcessSearchButton(binWindow, row); // 디스커버리: 공정명 셀 우측 버튼
                if (processSearchButton is null || !await SearchPopupSelectByEnterAsync(processSearchButton, target.ProcessSearchKey, "공정명"))
                {
                    _logger.Error($"공정명 입력 실패. part='{request.PartNo}'");
                    continue;
                }

                // 고정값
                if (!_config.Safety.DryRun && _config.Safety.SaveEnabled)
                {
                    FillFixedBinCells(row);
                }

                // BIN ID 검색(정확 일치). 미발견 시 저장 금지 + 스킵.
                var binIdSearchButton = FindBinIdSearchButton(binWindow, row); // 디스커버리
                if (binIdSearchButton is null || !await SearchPopupSelectExactAsync(binIdSearchButton, target.BinIdName))
                {
                    _logger.Warn($"BIN ID 미설정으로 저장 건너뜀. part='{request.PartNo}', binId='{target.BinIdName}'");
                    continue;
                }

                // 저장
                SaveItemInfo(mainWindow); // Ctrl+S + 안전게이트(기존 메서드 재사용)
                _logger.Info($"BIN saved(or gated). part='{request.PartNo}', binId='{target.BinIdName}'");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"BIN 처리 실패. part='{request.PartNo}'");
                _screenshots.CaptureElement(binWindow, $"bin_exception_{MakeSafeToken(request.PartNo)}");
                if (_config.Workflow.StopOnFirstFailure) break;
            }
        }

        _logger.Info("품목별 BIN 정보 관리 workflow finished.");
    }
```

- [ ] **Step 6: 디스커버리 의존 헬퍼 3개 추가** (Task 5 값으로 본문 채움)

`FindNewBinRow(binWindow)`, `FindProcessSearchButton(binWindow,row)`, `FindBinIdSearchButton(binWindow,row)` — Task 5에서 확정한 automation id/구조로 구현. 예시 골격:

```csharp
    private AutomationElement? FindNewBinRow(AutomationElement binWindow)
        => FindDescendants(binWindow, ControlType.DataItem).LastOrDefault(); // 디스커버리: 입력 가능한 신규 행 선택 규칙 확정

    private AutomationElement? FindProcessSearchButton(AutomationElement binWindow, AutomationElement row)
        => FindByAutomationId(row, "디스커버리:공정명검색버튼id")
           ?? FindDescendants(row, ControlType.Button).FirstOrDefault();

    private AutomationElement? FindBinIdSearchButton(AutomationElement binWindow, AutomationElement row)
        => FindByAutomationId(row, "디스커버리:BINID검색버튼id");
```

- [ ] **Step 7: Build**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj`
Expected: 경고 0 / 오류 0.

- [ ] **Step 8: live 검증 (dryRun=true 먼저)**

`appsettings.save-test.json`을 복사해 `dryRun=true,saveEnabled=false`로 BIN-only 실행 → 로그로 공정명/BIN ID 검색·매칭 흐름 확인. 그다음 save-test로 실제 저장.

- [ ] **Step 9: Commit (보류)**

```bash
git add src/UnimesAutomation/UnimesApp.cs
git commit -m "feat: implement bin info workflow"
```

---

## Task 7: 문서 갱신

**Files:**
- Modify: `docs/CONFIG.md` (binInfo 섹션 + showWorkScopeDialog)
- Modify: `docs/STATUS.md` (BIN 자동화 항목)
- Modify: `appsettings.save-test.json` / `appsettings.example.json` (binInfo 블록, showWorkScopeDialog)

- [ ] **Step 1: CONFIG.md에 `binInfo` 표 + `showWorkScopeDialog` 추가**
- [ ] **Step 2: STATUS.md 한눈에 표 + 새 섹션 추가**
- [ ] **Step 3: appsettings 두 파일에 `binInfo` 블록 추가**
- [ ] **Step 4: Commit (보류)**

```bash
git add docs/ appsettings.save-test.json appsettings.example.json
git commit -m "docs: bin info automation config and status"
```

---

## Self-Review (작성자 점검 결과)

- **스펙 커버리지:** 작업선택(Task3,4) / BIN ID 도출(Task2) / 900014(Task6 S1) / 행추가 Ctrl+Insert(Task6 S5) / 공정명·BIN ID 검색(Task6 S2,3) / 고정값(Task6 S4) / 저장·안전게이트(Task6 S5) / 유효파트 공유(Task4 S2,3) / 테스트(Task2) / 덤프 확정(Task5) — 모두 매핑됨.
- **플레이스홀더:** 순수로직/설정/다이얼로그/오케스트레이션은 완전 코드. UIA selector는 "디스커버리:" 로 명시한 **실측 의존**(게으른 TBD 아님) — Task 5가 산출, Task 6가 소비.
- **타입 일관성:** `BinInfoTarget(Class, ProcessSearchKey, BinIdName)`, `BinInfoConfig` 프로퍼티명, `WorkScope` 값이 전 Task에서 일치.
- **커밋:** 사용자 지시대로 전 Task commit 보류 → live 검증 후 일괄.
