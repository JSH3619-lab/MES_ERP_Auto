# MES 엑셀 결과 리포트(Excel Report) Implementation Plan — Plan 2 / 3

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 실행 결과를 `output/result_<timestamp>.xlsx` **한 파일·시트 2개**(품목정보관리 / BIN 정보관리)로 남긴다. MES 폼과 같은 한글 컬럼 + 행별 처리일시. 기존 분리 CSV 출력을 대체.

**Architecture:** `BinResult` 모델을 신설해 BIN 결과에 공정명·BIN Type·Retest No·BIN 완료여부·Retest TH·BIN ID를 담는다. `ResultWorkbook`(ClosedXML)이 품목/ BIN 결과를 받아 단일 xlsx를 쓴다. 워크플로우는 각자 CSV를 쓰지 않고 결과만 반환하고, `RunAsync`가 범위(품목/BIN/둘다)에 맞춰 xlsx를 1회 쓰고 완료창을 1회 띄운다.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, ClosedXML(순수 관리형 xlsx, Excel 설치 불필요), xUnit.

**전제(Plan 1 완료):** 설정은 분류 구조. 이 플랜은 결과 리포트만 다룬다. GUI/테마는 Plan 3.

**스펙 참조:** [docs/superpowers/specs/2026-06-19-mes-main-settings-design.md](../specs/2026-06-19-mes-main-settings-design.md) §8.

---

## 현재 코드 사실 (구현자 참고)

- `CsvFiles.WriteResults(outputDir, timestamp, IReadOnlyList<PartResult>)` → `result_<ts>.csv`. **입력 읽기 `CsvFiles.ReadPartRequests`는 유지.**
- `RunItemInfoWorkflowAsync`(약 198~462행): `List<PartResult>` 수집 → 내부에서 `CsvFiles.WriteResults` 호출 + `ShowCompletionDialog`. `WorkflowRunResult(ValidParts, Results, OutputPath)` 반환.
- `RunBinInfoWorkflowAsync`(약 465~681행): `List<PartResult>` 수집(로컬 `RecordResult(status,saved,message)` 클로저, `Classification="BIN"`). BIN 전용 셀값(공정/타입/TH/ID)은 **기록 안 함.** 내부에서 `WriteResults($"{ts}_bin")` + 완료창. 반환 `WorkflowRunResult`.
- `RunAsync`(약 160~192행): 범위별로 워크플로우 호출. `Both`면 결과 합쳐 완료창 1회(`combinedResults`).
- `ShowCompletionDialog(IReadOnlyList<PartResult>, ...)`(약 1754~1813행): Saved/Status/PartNo/Message로 요약. "결과 CSV: ..." 표기.
- BIN 루프에서 `target`(약 508행, `ProcessSearchKey`/`BinIdName`)과 `binRow`(약 622행, `BinType/RetestNo/BinComplete/RetestTh`)가 성공·후반 실패 시 스코프에 있음. 초기 실패(약 512/530/539행)에는 없음.

---

## File Structure

생성:
- `src/UnimesAutomation/ResultWorkbook.cs` — ClosedXML 2시트 xlsx writer.
- `tests/UnimesAutomation.Tests/ResultWorkbookTests.cs`

수정:
- `src/UnimesAutomation/Models.cs` — `PartResult.ProcessedAt` 추가, `BinResult` 신설.
- `src/UnimesAutomation/UnimesApp.cs` — BIN 결과를 `BinResult`로 수집, `RunAsync`에서 xlsx 1회 쓰기 + 완료창 1회, 워크플로우 내부 CSV/완료창 제거.
- `src/UnimesAutomation/CsvFiles.cs` — `WriteResults` 제거(입력 읽기만 유지).
- `src/UnimesAutomation/UnimesAutomation.csproj` — ClosedXML 패키지.
- `docs/STATUS.md` — 결과물이 xlsx로 바뀐 점 1줄.

---

## Task 1: ClosedXML 패키지 추가

**Files:** Modify `src/UnimesAutomation/UnimesAutomation.csproj`

- [ ] **Step 1: 패키지 추가**

기존 ItemGroup(ProtectedData) 안에 줄 추가하거나 새 ItemGroup으로:
```xml
    <PackageReference Include="ClosedXML" Version="0.104.2" />
```
(만약 복원이 `0.104.2` 없음을 보고하면, `dotnet add src/UnimesAutomation/UnimesAutomation.csproj package ClosedXML` 로 최신 안정 버전을 넣는다.)

- [ ] **Step 2: 빌드/복원 확인**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj`
Expected: 성공, ClosedXML 복원됨.

- [ ] **Step 3: Commit**
```bash
git add src/UnimesAutomation/UnimesAutomation.csproj
git commit -m "build: add ClosedXML for xlsx result report"
```
(commit 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` 한 줄 추가. 이하 모든 커밋 동일.)

---

## Task 2: 결과 모델 — PartResult.ProcessedAt + BinResult

**Files:** Modify `src/UnimesAutomation/Models.cs`

- [ ] **Step 1: `PartResult`에 처리일시 추가**

`PartResult` 클래스에 프로퍼티 추가:
```csharp
    public DateTime ProcessedAt { get; set; } = DateTime.Now;
```

- [ ] **Step 2: `BinResult` 추가**

`PartResult` 클래스 바로 아래에 추가:
```csharp
public sealed class BinResult
{
    public required string PartNo { get; init; }
    public string Classification { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string BinType { get; set; } = "";
    public string RetestNo { get; set; } = "";
    public string BinComplete { get; set; } = "";
    public string RetestTh { get; set; } = "";
    public string BinId { get; set; } = "";
    public string Saved { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.Now;
}
```
(`System` 네임스페이스의 `DateTime`은 Models.cs의 ImplicitUsings로 이미 사용 가능.)

- [ ] **Step 3: 빌드 확인 + Commit**
```bash
dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj
git add src/UnimesAutomation/Models.cs
git commit -m "feat: add BinResult model and PartResult.ProcessedAt"
```

---

## Task 3: ResultWorkbook (ClosedXML 2시트) — TDD

**Files:**
- Create `src/UnimesAutomation/ResultWorkbook.cs`
- Test `tests/UnimesAutomation.Tests/ResultWorkbookTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`tests/UnimesAutomation.Tests/ResultWorkbookTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using UnimesAutomation;
using Xunit;

public class ResultWorkbookTests
{
    [Fact]
    public void Write_creates_two_sheets_with_headers_and_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unimes_xlsx_{Guid.NewGuid():N}");
        var item = new PartResult
        {
            PartNo = "RMRDAG58A1B-GPWRRWM7", Classification = "Module",
            BinManage = "Y", TurnKey = "N", AssemblyIn = "Y", DefectWarehouse = "제품 폐기창고",
            Saved = "YES", Status = "OK", Message = "ok",
            ProcessedAt = new DateTime(2026, 6, 19, 10, 0, 0)
        };
        var bin = new BinResult
        {
            PartNo = "RCAH18AG-XPWRRWM7", Classification = "Comp", ProcessName = "C010",
            BinType = "Normal-1", RetestNo = "0", BinComplete = "Y", RetestTh = "H",
            BinId = "DRAM_Comp_D5_XMP72_Bin_16Gb", Saved = "YES", Status = "OK", Message = "ok",
            ProcessedAt = new DateTime(2026, 6, 19, 10, 1, 0)
        };
        try
        {
            var path = ResultWorkbook.Write(dir, "20260619_100000", [item], [bin]);
            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToList();
            Assert.Contains("품목정보관리", names);
            Assert.Contains("BIN 정보관리", names);

            var binWs = wb.Worksheet("BIN 정보관리");
            Assert.Equal("공정명", binWs.Cell(1, 3).GetString());
            Assert.Equal("BIN ID", binWs.Cell(1, 8).GetString());
            Assert.Equal("C010", binWs.Cell(2, 3).GetString());
            Assert.Equal("DRAM_Comp_D5_XMP72_Bin_16Gb", binWs.Cell(2, 8).GetString());

            var itemWs = wb.Worksheet("품목정보관리");
            Assert.Equal("불량창고", itemWs.Cell(1, 6).GetString());
            Assert.Equal("제품 폐기창고", itemWs.Cell(2, 6).GetString());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_bin_only_omits_item_sheet()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unimes_xlsx_{Guid.NewGuid():N}");
        var bin = new BinResult { PartNo = "RC...", Classification = "Comp", ProcessName = "C010",
            BinId = "x", Saved = "NO", Status = "DRYRUN", Message = "", ProcessedAt = DateTime.Now };
        try
        {
            var path = ResultWorkbook.Write(dir, "ts2", [], [bin]);
            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToList();
            Assert.DoesNotContain("품목정보관리", names);
            Assert.Contains("BIN 정보관리", names);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: 실패 확인** — `dotnet test ./tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj --filter ResultWorkbookTests` → 컴파일 실패(`ResultWorkbook` 미정의).

- [ ] **Step 3: 구현 작성**

`src/UnimesAutomation/ResultWorkbook.cs`:
```csharp
using ClosedXML.Excel;

namespace UnimesAutomation;

// 실행 결과를 단일 xlsx(시트 2개: 품목정보관리 / BIN 정보관리)로 쓴다. 비어 있는 시트는 만들지 않는다.
public static class ResultWorkbook
{
    public static string Write(
        string outputDirectory,
        string timestamp,
        IReadOnlyList<PartResult> itemResults,
        IReadOnlyList<BinResult> binResults)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"result_{timestamp}.xlsx");

        using var workbook = new XLWorkbook();

        if (itemResults.Count > 0)
        {
            var ws = workbook.Worksheets.Add("품목정보관리");
            string[] headers = ["품목", "분류", "BIN 관리", "Turn Key", "조립입고", "불량창고", "저장", "상태", "메시지", "처리일시"];
            for (var c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
            }

            var row = 2;
            foreach (var r in itemResults)
            {
                ws.Cell(row, 1).Value = r.PartNo;
                ws.Cell(row, 2).Value = r.Classification;
                ws.Cell(row, 3).Value = r.BinManage;
                ws.Cell(row, 4).Value = r.TurnKey;
                ws.Cell(row, 5).Value = r.AssemblyIn;
                ws.Cell(row, 6).Value = r.DefectWarehouse;
                ws.Cell(row, 7).Value = r.Saved;
                ws.Cell(row, 8).Value = r.Status;
                ws.Cell(row, 9).Value = r.Message;
                ws.Cell(row, 10).Value = r.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss");
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        if (binResults.Count > 0)
        {
            var ws = workbook.Worksheets.Add("BIN 정보관리");
            string[] headers = ["품목", "분류", "공정명", "BIN Type", "Retest No", "BIN 완료여부", "Retest TH", "BIN ID", "저장", "상태", "메시지", "처리일시"];
            for (var c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
            }

            var row = 2;
            foreach (var r in binResults)
            {
                ws.Cell(row, 1).Value = r.PartNo;
                ws.Cell(row, 2).Value = r.Classification;
                ws.Cell(row, 3).Value = r.ProcessName;
                ws.Cell(row, 4).Value = r.BinType;
                ws.Cell(row, 5).Value = r.RetestNo;
                ws.Cell(row, 6).Value = r.BinComplete;
                ws.Cell(row, 7).Value = r.RetestTh;
                ws.Cell(row, 8).Value = r.BinId;
                ws.Cell(row, 9).Value = r.Saved;
                ws.Cell(row, 10).Value = r.Status;
                ws.Cell(row, 11).Value = r.Message;
                ws.Cell(row, 12).Value = r.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss");
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        if (!workbook.Worksheets.Any())
        {
            workbook.Worksheets.Add("결과 없음");
        }

        workbook.SaveAs(path);
        return path;
    }
}
```

- [ ] **Step 4: 통과 확인** — `dotnet test ... --filter ResultWorkbookTests`(2 PASS), 이어서 전체 `dotnet test ...`(회귀 없음).

- [ ] **Step 5: Commit**
```bash
git add src/UnimesAutomation/ResultWorkbook.cs tests/UnimesAutomation.Tests/ResultWorkbookTests.cs
git commit -m "feat: add ResultWorkbook (ClosedXML two-sheet writer)"
```

---

## Task 4: BIN 결과 수집 + 단일 xlsx 출력 일원화 (UnimesApp)

`Models`/`ResultWorkbook` 변경에 맞춰 `UnimesApp`을 한 번에 바꾼다(빌드+테스트 그린 단위). 자동화 제어흐름(UI 조작)은 건드리지 않고, **결과 기록·출력·완료창**만 바꾼다.

**Files:** Modify `src/UnimesAutomation/UnimesApp.cs`, `src/UnimesAutomation/CsvFiles.cs`

- [ ] **Step 1: WorkflowRunResult에서 OutputPath 제거**

`record WorkflowRunResult(List<PartRequest> ValidParts, List<PartResult> Results, string OutputPath);`
→ `record WorkflowRunResult(List<PartRequest> ValidParts, List<PartResult> Results);`

- [ ] **Step 2: 품목 워크플로우 — 내부 CSV/완료창 제거, 반환 정리**

`RunItemInfoWorkflowAsync`의 시그니처에서 `bool showCompletionDialog` 파라미터 제거. 끝부분
```csharp
        var outputPath = CsvFiles.WriteResults(_paths.OutputDirectory, _paths.Timestamp, results);
        _logger.Info($"품목정보관리 result CSV saved: {outputPath}");
        if (showCompletionDialog) { ShowCompletionDialog(results, outputPath); }
        return new WorkflowRunResult(validParts, results, outputPath);
```
을 다음으로 교체:
```csharp
        return new WorkflowRunResult(validParts, results);
```
조기 반환 `return new WorkflowRunResult([], [], "");`도 `return new WorkflowRunResult([], []);`로 수정.

- [ ] **Step 3: BIN 워크플로우 — BinResult 수집, 반환 List<BinResult>**

`RunBinInfoWorkflowAsync` 시그니처에서 `bool showCompletionDialog` 제거, 반환형을 `Task<List<BinResult>>`로 변경. 조기 반환 `return new WorkflowRunResult([], [], "");` → `return [];`.

`var results = new List<PartResult>();` → `var results = new List<BinResult>();`

per-part 루프 시작부에 분류와 진행 컨텍스트 로컬을 둔다(기존 `var resultRecorded = false;` 부근):
```csharp
            var cls = PartClassifier.Classify(request.PartNo);
            var rowProcess = "";
            var rowBinType = "";
            var rowRetestNo = "";
            var rowBinComplete = "";
            var rowRetestTh = "";
            var rowBinId = "";
```
`RecordResult` 클로저를 BinResult 생성으로 교체:
```csharp
            void RecordResult(string status, string saved, string message)
            {
                results.Add(new BinResult
                {
                    PartNo = request.PartNo,
                    Classification = cls.ToString(),
                    ProcessName = rowProcess,
                    BinType = rowBinType,
                    RetestNo = rowRetestNo,
                    BinComplete = rowBinComplete,
                    RetestTh = rowRetestTh,
                    BinId = rowBinId,
                    Saved = saved,
                    Status = status,
                    Message = message,
                    ProcessedAt = DateTime.Now
                });
                resultRecorded = true;
            }
```
`target` 해석 직후(약 508행, null 체크 통과 뒤)에 채운다:
```csharp
                rowProcess = target.ProcessSearchKey;
                rowBinId = target.BinIdName;
```
`binRow` 해석 직후(약 622행, `FillFixedBinCells` 호출 부근)에 채운다:
```csharp
                rowBinType = binRow.BinType;
                rowRetestNo = binRow.RetestNo;
                rowBinComplete = binRow.BinComplete;
                rowRetestTh = binRow.RetestTh;
```
끝부분의 `outputPath = CsvFiles.WriteResults(...)`/완료창/`return new WorkflowRunResult(...)`를 제거하고 `return results;`로 끝낸다. (catch 블록의 `RecordResult` 호출은 그대로 — 이제 BinResult를 만든다.)

- [ ] **Step 4: 오케스트레이션 — RunAsync에서 xlsx 1회 + 완료창 1회**

`RunAsync`의 `if (_config.Workflow.Enabled)` 블록(약 160~192행)을 다음으로 교체:
```csharp
        if (_config.Workflow.Enabled)
        {
            var scope = _config.Workflow.RuntimeWorkScope;
            List<PartRequest> binParts = _config.Workflow.RuntimePartRequests.ToList();
            var itemResults = new List<PartResult>();
            var binResults = new List<BinResult>();

            if (scope == WorkScope.ItemInfo || scope == WorkScope.Both)
            {
                var itemRun = await RunItemInfoWorkflowAsync(mainWindow);
                itemResults = itemRun.Results;
                if (scope == WorkScope.Both)
                {
                    binParts = itemRun.ValidParts;
                }
            }

            if (scope == WorkScope.BinInfo || scope == WorkScope.Both)
            {
                binResults = await RunBinInfoWorkflowAsync(mainWindow, binParts);
            }

            if (itemResults.Count > 0 || binResults.Count > 0)
            {
                var outputPath = ResultWorkbook.Write(_paths.OutputDirectory, _paths.Timestamp, itemResults, binResults);
                _logger.Info($"결과 리포트 저장: {outputPath}");
                ShowCompletionDialog(itemResults, binResults, outputPath);
            }
        }
```
사용하지 않게 된 `AddOutputPath` 헬퍼가 있으면(다른 곳에서 안 쓰면) 제거한다.

- [ ] **Step 5: 완료창을 두 결과 합산으로**

`ShowCompletionDialog`를 다음 형태로 교체(두 오버로드를 이 하나로 대체). 요약은 품목+BIN 합산:
```csharp
    private readonly record struct ResultLine(string PartNo, string Saved, string Status, string Message);

    private void ShowCompletionDialog(
        IReadOnlyList<PartResult> itemResults,
        IReadOnlyList<BinResult> binResults,
        string outputPath)
    {
        if (!_config.Workflow.ShowCompletionDialog)
        {
            return;
        }

        var lines = new List<ResultLine>();
        foreach (var r in itemResults) lines.Add(new ResultLine(r.PartNo, r.Saved, r.Status, r.Message));
        foreach (var r in binResults) lines.Add(new ResultLine(r.PartNo, r.Saved, r.Status, r.Message));

        var saved = lines.Count(r => string.Equals(r.Saved, "YES", StringComparison.Ordinal));
        var unchanged = lines.Count(r => string.Equals(r.Saved, "UNCHANGED", StringComparison.Ordinal));
        var dryRun = lines.Count(r => string.Equals(r.Status, "DRYRUN", StringComparison.Ordinal));
        var skipped = lines.Count(r => string.Equals(r.Status, "SKIPPED", StringComparison.Ordinal));
        var errors = lines.Count(r => string.Equals(r.Status, "ERROR", StringComparison.Ordinal));

        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"작업 완료 (총 {lines.Count}건)");
        builder.AppendLine();
        builder.AppendLine($"저장: {saved}    변경없음: {unchanged}    변경예정(dryRun): {dryRun}");
        builder.AppendLine($"건너뜀: {skipped}    오류: {errors}");

        var problems = lines.Where(r => r.Status is "ERROR" or "SKIPPED").ToList();
        if (problems.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("확인 필요:");
            foreach (var problem in problems)
            {
                builder.AppendLine($" - {problem.PartNo} [{problem.Status}] {problem.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"결과 파일: {outputPath}");

        var icon = errors > 0 || skipped > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
        var title = errors > 0 || skipped > 0 ? "UNIMES 자동화 완료 - 확인 필요" : "UNIMES 자동화 완료";
        try
        {
            MessageBox.Show(builder.ToString(), title, MessageBoxButtons.OK, icon);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Completion dialog failed: {ex.Message}");
        }
    }
```
(기존 `ShowCompletionDialog(IReadOnlyList<PartResult>, string)` / `(..., IReadOnlyList<string>)` 두 메서드는 삭제. 다른 호출처가 남지 않게 빌드로 확인.)

- [ ] **Step 6: CsvFiles.WriteResults 제거**

`CsvFiles.cs`에서 `WriteResults` 메서드와, 그 메서드만 쓰던 private 헬퍼(`Escape` 등)가 **다른 곳에서 안 쓰이면** 함께 제거. `ReadPartRequests`와 그것이 쓰는 헬퍼(`ParseCsvLine`/`FindColumn`/`GetCell`/`Normalize`)는 유지. 빌드 경고/오류로 미사용 여부 확인.

- [ ] **Step 7: 빌드 + 전체 테스트**

Run: `dotnet build ./src/UnimesAutomation/UnimesAutomation.csproj` (0 오류)
Run: `dotnet test ./tests/UnimesAutomation.Tests/UnimesAutomation.Tests.csproj` (전체 PASS)
`git grep -n "WriteResults\|WorkflowRunResult(\[\], \[\], " src/` 로 잔존 참조 없음 확인.

- [ ] **Step 8: Commit**
```bash
git add src/UnimesAutomation/UnimesApp.cs src/UnimesAutomation/CsvFiles.cs
git commit -m "feat: write single xlsx report with item/bin sheets"
```

---

## Task 5: 문서 갱신 + 스모크 (가벼움)

**Files:** Modify `docs/STATUS.md`

- [ ] **Step 1: STATUS.md 한 줄 갱신** — 결과물이 `output/result_<timestamp>.xlsx`(시트 2개)임을 반영. (기존 CSV 언급이 있으면 xlsx로 수정.)

- [ ] **Step 2: 파싱/빌드 스모크** — `dotnet build` + `dotnet test` 전체 그린 재확인.

- [ ] **Step 3: Commit**
```bash
git add docs/STATUS.md
git commit -m "docs: note xlsx result report output"
```

> **라이브 검증(플랜 후):** 실제 MES에서 `둘 다` 1건 실행 → `output/result_*.xlsx`에 두 시트·한글 컬럼·BIN 전용 값(공정/타입/TH/ID)·처리일시가 채워지는지 확인.

---

## Self-Review (작성자 체크 — 완료)

- **스펙 §8 커버리지**: 단일 xlsx 2시트(Task 3) / 한글 컬럼·처리일시(Task 3) / BIN 전용 값 수집(Task 4) / CSV 대체(Task 4 Step 6).
- **타입 일관성**: `BinResult`, `ResultWorkbook.Write(...)`, `WorkflowRunResult(ValidParts, Results)`, `ResultLine`, `RunBinInfoWorkflowAsync : Task<List<BinResult>>` 명칭이 태스크 전반 일치.
- **플레이스홀더 없음**: 신규 파일·테스트 전체 코드 포함. UnimesApp는 앵커+정확한 교체로 명시.
- **리스크**: BIN 결과 기록 변경은 관찰용(자동화 제어흐름 불변). 출력/완료창 일원화는 빌드+테스트로 검증, 실동작은 라이브 확인.

## 다음 플랜
- **Plan 3 — GUI 메인/설정 창 + 다크 HUD 테마 + Approach 2 실행** (스펙 §3,6,7,10,14).
