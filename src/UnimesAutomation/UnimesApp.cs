using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed partial class UnimesApp
{
    private readonly RootConfig _config;
    private readonly RuntimePaths _paths;
    private readonly SimpleLogger _logger;
    private readonly ScreenshotService _screenshots;
    private readonly SafetyGuard _safety;

    private sealed record WorkflowRunResult(
        List<PartRequest> ValidParts,
        List<PartResult> Results);

    public UnimesApp(
        RootConfig config,
        RuntimePaths paths,
        SimpleLogger logger,
        ScreenshotService screenshots,
        SafetyGuard safety)
    {
        _config = config;
        _paths = paths;
        _logger = logger;
        _screenshots = screenshots;
        _safety = safety;
    }

    public bool HasExistingLoggedInMainWindow() => FindExistingMainWindow() is not null;

    private CancellationToken _cancel;

    private void ThrowIfCancellationRequested(string context)
    {
        if (!_cancel.IsCancellationRequested)
        {
            return;
        }

        _logger.Info($"정지 요청 감지. {context} 중단.");
        _cancel.ThrowIfCancellationRequested();
    }

    private Task DelayAsync(int millisecondsDelay) => Task.Delay(millisecondsDelay, _cancel);

    private Task DelayAsync(TimeSpan delay) => Task.Delay(delay, _cancel);

    private void SendEnter(AutomationElement element, string reason)
    {
        TryFocus(element, reason);
        SendKeys.SendWait("{ENTER}");
        _logger.Info($"{reason} Enter 전송.");
    }

    public async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancel = default)
    {
        _cancel = cancel;
        _logger.Info("===== UNIMES automation bootstrap started =====");
        _logger.Info($"Options: noLaunch={options.NoLaunch}, dumpOnly={options.DumpOnly}");
        _logger.Info($"Safety: dryRun={_config.Safety.DryRun}, saveEnabled={_config.Safety.SaveEnabled}");

        var launchMode = ResolveLaunchMode(options);
        _logger.Info($"Launch mode: {launchMode}");

        if (options.DumpOnly)
        {
            _logger.Info("Dump-only mode. Launch skipped.");
        }
        else if (launchMode == LaunchMode.Launch)
        {
            LaunchUnimes();
        }
        else
        {
            var existing = FindExistingMainWindow();
            if (existing is not null)
            {
                _logger.Info("Existing logged-in UNIMES detected. Skipping launch and login.");
            }
            else if (FindUnimesWindow() is not null)
            {
                _logger.Info("Existing UNIMES login/startup window detected. Skipping launch.");
            }
            else if (launchMode == LaunchMode.Attach)
            {
                throw new InvalidOperationException(
                    "launchMode=attach 인데 로그인된 UNIMES 창을 찾지 못했습니다. UNIMES에 먼저 로그인하세요.");
            }
            else
            {
                _logger.Info("No running UNIMES detected. Launching.");
                LaunchUnimes();
            }
        }

        var window = await WaitForUnimesWindowAsync(TimeSpan.FromSeconds(_config.App.LaunchTimeoutSeconds));
        if (window is null)
        {
            throw new TimeoutException("UNIMES window was not found.");
        }

        LogWindowIdentity(window, "Initial UNIMES window");
        _screenshots.CaptureElement(window, "initial_window");

        if (options.DumpOnly)
        {
            UiDump.DumpToFile(window, _paths.UiDumpPath, _config.App.UiDumpMaxDepth, _logger);
            return 0;
        }

        var loginPerformed = false;
        AutomationElement? mainWindow = null;
        if (IsLoginScreen(window))
        {
            _logger.Info("Login screen detected.");
            await HandleLoginScreenAsync(window);
            loginPerformed = true;
        }
        else if (IsProbablyMainWindow(window))
        {
            _logger.Info("Initial window is main UNIMES window.");
            mainWindow = window;
        }
        else
        {
            _logger.Info("Initial window is not login/main. Waiting for login or main window.");
            var nextWindow = await WaitForLoginOrMainWindowAsync(TimeSpan.FromSeconds(_config.App.LoginTimeoutSeconds));
            if (nextWindow is null)
            {
                _screenshots.CaptureDesktop("login_or_main_window_timeout");
                throw new TimeoutException("Login or main UNIMES window was not detected after startup wait.");
            }

            LogWindowIdentity(nextWindow, "Login/main UNIMES window");
            if (IsLoginScreen(nextWindow))
            {
                _logger.Info("Login screen detected after startup window.");
                await HandleLoginScreenAsync(nextWindow);
                loginPerformed = true;
            }
            else
            {
                _logger.Info("Main UNIMES window detected after startup window.");
                mainWindow = nextWindow;
            }
        }

        if (loginPerformed)
        {
            _logger.Info("로그인 후 Continue 팝업 자동 처리는 생략. 메인 화면 감지로 진행.");
        }
        else
        {
            _logger.Info("Login was not performed by automation. Waiting for main window.");
        }

        ThrowIfCancellationRequested("메인 창 대기 전");
        var mainWindowTimeout = loginPerformed
            ? TimeSpan.FromSeconds(12)
            : TimeSpan.FromSeconds(_config.App.LoginTimeoutSeconds);
        mainWindow ??= await WaitForMainWindowAsync(mainWindowTimeout);
        if (mainWindow is null)
        {
            _screenshots.CaptureDesktop("main_window_timeout");
            throw new TimeoutException("Main UNIMES window was not detected after login wait.");
        }

        LogWindowIdentity(mainWindow, "Main UNIMES window");
        _screenshots.CaptureElement(mainWindow, "main_window");

        _logger.Info("Main window detected. Starting workflow.");

        UiDump.DumpToFile(mainWindow, _paths.UiDumpPath, _config.App.UiDumpMaxDepth, _logger);

        if (_config.Workflow.Enabled)
        {
            ThrowIfCancellationRequested("워크플로우 시작 전");
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

        _logger.Info("Bootstrap completed.");
        return 0;
    }

    private async Task<WorkflowRunResult> RunItemInfoWorkflowAsync(AutomationElement mainWindow)
    {
        var inputPath = "";
        IReadOnlyList<PartRequest> requests;
        if (_config.Workflow.RuntimePartRequests.Count > 0)
        {
            requests = _config.Workflow.RuntimePartRequests;
            _logger.Info($"Using Part No values from startup input dialog. count={requests.Count}");
        }
        else
        {
            inputPath = ResolveInputPartsPath();
            requests = CsvFiles.ReadPartRequests(inputPath);
        }

        if (requests.Count == 0)
        {
            _logger.Warn($"No Part No entries found. input='{inputPath}'");
            return new WorkflowRunResult([], []);
        }

        _logger.Info($"품목정보관리 workflow started. count={requests.Count}, dryRun={_config.Safety.DryRun}, saveEnabled={_config.Safety.SaveEnabled}");
        _logger.Info("처리 예정 Part 목록:");
        for (var index = 0; index < requests.Count; index++)
        {
            var partNo = requests[index].PartNo;
            var cls = PartClassifier.Classify(partNo);
            var itemInfo = _config.ResolveItemInfo(cls);
            var warehouse = itemInfo?.DefectWarehouse ?? "(분류 실패 → 미존재 여부만 확인)";
            _logger.Info($"  [{index + 1}/{requests.Count}] {partNo} → class={cls}, 불량창고={warehouse}");
        }

        await NavigateToMenuByF3Async(mainWindow, _config.Global.ItemInfoMenuName);

        var results = new List<PartResult>();
        var validParts = new List<PartRequest>();
        // 품목명 입력칸·조회 버튼·품목정보관리 자식 창은 Part가 바뀌어도 동일하다.
        // UIA 전체 탐색이 느려 Part마다 다시 찾으면 조회까지 8초+ 걸리므로 한 번만 찾아 재사용한다.
        AutomationElement itemInfoWindow = FindItemInfoWindow(mainWindow) ?? mainWindow;
        AutomationElement? partNameEdit = null;
        foreach (var request in requests)
        {
            ThrowIfCancellationRequested("품목정보관리 남은 Part 처리");

            var classification = PartClassifier.Classify(request.PartNo);
            var categoryItem = _config.ResolveItemInfo(classification) ?? new ItemInfoValues();

            var result = new PartResult
            {
                PartNo = request.PartNo,
                Classification = classification.ToString(),
                BinManage = categoryItem.BinManage,
                TurnKey = categoryItem.TurnKey,
                AssemblyIn = categoryItem.AssemblyIn,
                DefectWarehouse = categoryItem.DefectWarehouse,
                Saved = "NO"
            };

            try
            {
                _logger.Info($"품목정보관리 part started. part='{request.PartNo}', class={classification}");
                BringToFront(mainWindow);
                ThrowIfCancellationRequested("품목정보관리 화면 준비");

                if (!IsElementUsable(itemInfoWindow))
                {
                    itemInfoWindow = FindItemInfoWindow(mainWindow) ?? mainWindow;
                }

                if (!IsElementUsable(partNameEdit))
                {
                    partNameEdit = FindEditNextToLabel(itemInfoWindow, "품목명");
                }

                if (partNameEdit is null)
                {
                    _screenshots.CaptureElement(itemInfoWindow, $"item_info_part_name_not_found_{request.PartNo}");
                    result.Status = "ERROR";
                    result.Message = "품목명 input field was not found.";
                    results.Add(result);
                    if (_config.Workflow.StopOnFirstFailure) break;
                    continue;
                }

                SetElementText(partNameEdit, request.PartNo, "품목정보관리 품목명");
                SendEnter(partNameEdit, "품목정보관리 품목명 조회");
                var itemQueryStopwatch = Stopwatch.StartNew();
                ThrowIfCancellationRequested("품목정보관리 품목명 입력");

                if (await HandleOpenPartIdPopupAsync(request.PartNo))
                {
                    result.Status = "SKIPPED";
                    result.Saved = "NO";
                    result.Message = $"품목 코드 미존재 → 경고 확인 후 기파트 키보드 복구. part='{request.PartNo}'";
                    _logger.Info($"품목정보관리 skipped missing part. part='{request.PartNo}'");
                    results.Add(result);
                    if (_config.Workflow.StopOnFirstFailure) break;
                    continue;
                }

                var pid = PartClassifier.ExtractPid(request.PartNo);
                _logger.Info($"품목정보관리 그리드 행 탐색 시작. part='{request.PartNo}', pid='{pid}'");
                var row = await WaitForItemGridRowAsync(itemInfoWindow, pid, TimeSpan.FromMilliseconds(_config.Workflow.SearchDelayMilliseconds));
                if (row is null)
                {
                    _logger.Warn($"품목정보관리 그리드 행 1차 미발견. 0.7초 후 재시도. part='{request.PartNo}', pid='{pid}'");
                    await DelayAsync(700);
                    row = FindGridRowByProductId(itemInfoWindow, pid);
                }

                if (row is null)
                {
                    // 조회 결과 행이 없으면 '존재하지 않습니다' 경고가 떠 있다(미존재).
                    // blind 재조회(전체조회→MES 멈춤)를 하지 않고, 경고를 닫고 검색 팝업을 취소한 뒤
                    // 이 파트는 건너뛴다.
                    if (await HandleMissingPartAsync(mainWindow, request.PartNo))
                    {
                        result.Status = "SKIPPED";
                        result.Saved = "NO";
                        result.Message = $"품목 코드 미존재 → 경고 확인 후 기파트 키보드 복구. part='{request.PartNo}'";
                        _logger.Info($"품목정보관리 skipped missing part after row search. part='{request.PartNo}'");
                        results.Add(result);
                        if (_config.Workflow.StopOnFirstFailure) break;
                        continue;
                    }

                    _screenshots.CaptureElement(itemInfoWindow, $"item_info_pid_row_not_found_{request.PartNo}");
                    result.Status = "ERROR";
                    result.Message = $"PID row not found in grid. pid='{pid}'";
                    _logger.Error($"품목정보관리 row not found. part='{request.PartNo}', pid='{pid}'");
                    results.Add(result);
                    if (_config.Workflow.StopOnFirstFailure) break;
                    continue;
                }

                _logger.Info($"품목정보관리 그리드 행 발견. part='{request.PartNo}', pid='{pid}'");
                _logger.Info($"품목정보관리 조회 행 확인 완료. part='{request.PartNo}', elapsed={itemQueryStopwatch.Elapsed.TotalSeconds:0.000}s");
                validParts.Add(request);
                if (classification == PartClass.Unknown)
                {
                    _logger.Warn($"Part exists or returned a row, but classification failed. Skipping value changes. part='{request.PartNo}'");
                    _screenshots.CaptureElement(itemInfoWindow, $"classification_failed_{request.PartNo}");
                    result.Status = "SKIPPED";
                    result.Saved = "NO";
                    result.Message = "Part exists, but prefix is neither DRAM Module/Comp nor SSD(DA/DE).";
                    results.Add(result);
                    if (_config.Workflow.StopOnFirstFailure) break;
                    continue;
                }

                // dryRun 또는 saveEnabled=false 이면 화면을 바꾸지 않고 '변경 예정'만 판별한다.
                var readOnlyMode = _config.Safety.DryRun || !_config.Safety.SaveEnabled;
                _logger.Info($"품목정보관리 셀 비교 시작. part='{request.PartNo}', readOnly={readOnlyMode}");

                var detail = new List<string>();
                var changeCount = 0;
                var wouldCount = 0;
                foreach (var (column, value) in new[]
                {
                    ("BIN 관리", result.BinManage),
                    ("Turn Key", result.TurnKey),
                    ("조립입고 공정이동여부", result.AssemblyIn),
                    ("불량창고", result.DefectWarehouse)
                })
                {
                    // SSD는 조립입고 공정이동여부를 설정값과 무관하게 절대 건드리지 않는다.
                    if (classification == PartClass.Ssd && column == "조립입고 공정이동여부")
                    {
                        _logger.Info($"품목정보관리 SSD 조립입고 미처리(고정). part='{request.PartNo}'");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        _logger.Info($"품목정보관리 셀 비교 제외. part='{request.PartNo}', column='{column}'");
                        continue;
                    }

                    var action = ApplyComboCell(row, column, value, readOnlyMode);
                    if (action == CellAction.Changed)
                    {
                        changeCount++;
                        detail.Add($"{column}={value}");
                    }
                    else if (action == CellAction.WouldChange)
                    {
                        wouldCount++;
                        detail.Add($"{column}→{value}");
                    }
                }

                _screenshots.CaptureElement(itemInfoWindow, $"item_info_before_save_{request.PartNo}");

                if (changeCount == 0 && wouldCount == 0)
                {
                    result.Status = "OK";
                    result.Saved = "UNCHANGED";
                    result.Message = "모든 값이 이미 일치 (변경 없음).";
                    _logger.Info($"품목정보관리 no change. part='{request.PartNo}', pid='{pid}', class={classification}, elapsed={itemQueryStopwatch.Elapsed.TotalSeconds:0.000}s");
                }
                else if (wouldCount > 0)
                {
                    result.Status = "DRYRUN";
                    result.Saved = "NO";
                    result.Message = "변경 예정(저장 안 함): " + string.Join(", ", detail);
                    _logger.Info($"품목정보관리 dryRun. part='{request.PartNo}', elapsed={itemQueryStopwatch.Elapsed.TotalSeconds:0.000}s, {result.Message}");
                }
                else if (SaveItemInfo(mainWindow))
                {
                    await DelayAsync(300);
                    _screenshots.CaptureElement(mainWindow, $"item_info_after_save_{request.PartNo}");
                    result.Status = "OK";
                    result.Saved = "YES";
                    result.Message = "변경 저장: " + string.Join(", ", detail);
                    _logger.Info($"품목정보관리 saved. part='{request.PartNo}', elapsed={itemQueryStopwatch.Elapsed.TotalSeconds:0.000}s, {result.Message}");
                }
                else
                {
                    result.Status = "ERROR";
                    result.Saved = "NO";
                    result.Message = "값을 변경했으나 저장 게이트에 막힘: " + string.Join(", ", detail);
                    _logger.Error($"품목정보관리 changed but save blocked. part='{request.PartNo}', {result.Message}");
                }

                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"품목정보관리 processing failed. part='{request.PartNo}'");
                _screenshots.CaptureElement(mainWindow, $"item_info_exception_{request.PartNo}");
                try
                {
                    var dumpPath = Path.Combine(
                        _paths.LogsDirectory,
                        $"ui_dump_iteminfo_{MakeSafeToken(request.PartNo)}_{_paths.Timestamp}.txt");
                    UiDump.DumpToFile(mainWindow, dumpPath, _config.App.UiDumpMaxDepth, _logger);
                }
                catch
                {
                    // diagnostic dump is best-effort
                }

                result.Status = "ERROR";
                result.Message = ex.Message;
                results.Add(result);
                if (_config.Workflow.StopOnFirstFailure) break;
            }
        }

        return new WorkflowRunResult(validParts, results);
    }


    private void CommitField()
    {
        try
        {
            SendKeys.SendWait("{TAB}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Field commit (Tab) failed: {ex.Message}");
        }
    }

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

        var title = errors > 0 || skipped > 0 ? "UNIMES 자동화 완료 - 확인 필요" : "UNIMES 자동화 완료";
        try
        {
            var kind = errors > 0 || skipped > 0 ? NativeMessage.Kind.Warning : NativeMessage.Kind.Information;
            NativeMessage.Show(builder.ToString(), title, kind);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Completion dialog failed: {ex.Message}");
        }
    }


    private enum CellAction
    {
        Unchanged,
        WouldChange,
        Changed
    }

    private AutomationElement? FindGridRowByProductId(AutomationElement mainWindow, string productId)
    {
        foreach (var row in FindDescendants(mainWindow, ControlType.DataItem))
        {
            var idEdit = FindDescendants(row, ControlType.Edit)
                .FirstOrDefault(edit => string.Equals(SafeRead(() => edit.Current.Name) ?? "", "품목ID", StringComparison.Ordinal));
            if (idEdit is null)
            {
                continue;
            }

            var value = ReadValue(idEdit);
            if (string.Equals(value.Trim(), productId, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    private async Task<AutomationElement?> WaitForItemGridRowAsync(AutomationElement itemInfoWindow, string productId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ThrowIfCancellationRequested("품목정보관리 조회 결과 확인");

            var row = FindGridRowByProductId(itemInfoWindow, productId);
            if (row is not null)
            {
                return row;
            }

            if (FindWarningDialog() is not null || FindPartIdPopup() is not null)
            {
                return null;
            }

            await DelayAsync(80);
        }

        return FindGridRowByProductId(itemInfoWindow, productId);
    }

    private async Task WaitForBinQuerySettledAsync(AutomationElement binWindow, string partNo, TimeSpan timeout)
    {
        string[] noDataTokens = ["900014", "검색된 Data", "검색된 데이터", "Data가 없습니다"];
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ThrowIfCancellationRequested("BIN 조회 결과 확인");

            if (FindBinRowsForPart(binWindow, partNo).Count > 0 ||
                FindOwnedMessageDialog(noDataTokens, allowAnyMessageBoxForm: true) is not null)
            {
                return;
            }

            await DelayAsync(80);
        }
    }

    private AutomationElement? FindNamedWindow(AutomationElement mainWindow, string name)
    {
        return FindDescendants(mainWindow, ControlType.Window)
            .Where(window => string.Equals(
                SafeRead(() => window.Current.Name) ?? "",
                name,
                StringComparison.Ordinal))
            .Where(window =>
            {
                var rect = SafeReadRect(() => window.Current.BoundingRectangle);
                return rect.HasValue && !rect.Value.IsEmpty;
            })
            .LastOrDefault();
    }

    private AutomationElement? FindItemInfoWindow(AutomationElement mainWindow)
        => FindNamedWindow(mainWindow, _config.Global.ItemInfoMenuName);

    private CellAction ApplyComboCell(AutomationElement row, string columnName, string targetValue, bool readOnlyMode)
    {
        var combo = FindDescendants(row, ControlType.ComboBox)
            .FirstOrDefault(c => string.Equals(SafeRead(() => c.Current.Name) ?? "", columnName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Grid cell not found: column='{columnName}'");

        TryFocus(combo, $"grid cell '{columnName}'");

        var target = FindDescendants(combo, ControlType.ListItem)
            .FirstOrDefault(li => string.Equals(SafeRead(() => li.Current.Name) ?? "", targetValue, StringComparison.Ordinal));

        var current = GetComboCurrentText(combo);
        var already = string.Equals(current, targetValue, StringComparison.Ordinal) ||
                      (target is not null && IsListItemSelected(target));
        if (already)
        {
            _logger.Info($"Cell already set. column='{columnName}', value='{targetValue}'");
            return CellAction.Unchanged;
        }

        if (readOnlyMode)
        {
            _logger.Info($"[readOnly] Would change cell. column='{columnName}', '{current}'->'{targetValue}'");
            return CellAction.WouldChange;
        }

        TryExpandCombo(combo);
        if (target is not null && TrySelectListItem(target))
        {
            CommitComboEdit(columnName);
            var updated = GetComboCurrentText(combo);
            if (string.Equals(updated, targetValue, StringComparison.Ordinal))
            {
                _logger.Info($"Cell set via list item. column='{columnName}', '{current}'->'{targetValue}'");
                return CellAction.Changed;
            }

            _logger.Warn($"List item select did not commit target value. column='{columnName}', expected='{targetValue}', actual='{updated}'");
        }

        if (target is not null && TrySelectComboByKeyboard(combo, targetValue, columnName))
        {
            var updated = GetComboCurrentText(combo);
            if (string.Equals(updated, targetValue, StringComparison.Ordinal))
            {
                _logger.Info($"Cell set via keyboard. column='{columnName}', '{current}'->'{targetValue}'");
                return CellAction.Changed;
            }

            _logger.Warn($"Keyboard select did not commit target value. column='{columnName}', expected='{targetValue}', actual='{updated}'");
        }

        if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern) &&
            rawPattern is ValuePattern valuePattern && !valuePattern.Current.IsReadOnly)
        {
            TryFocus(combo, $"grid cell '{columnName}'");
            valuePattern.SetValue(targetValue);
            CommitComboEdit(columnName);
            var updated = GetComboCurrentText(combo);
            if (!string.Equals(updated, targetValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Grid cell value did not commit. column='{columnName}', expected='{targetValue}', actual='{updated}'");
            }

            _logger.Info($"Cell set via ValuePattern. column='{columnName}', '{current}'->'{targetValue}'");
            return CellAction.Changed;
        }

        throw new InvalidOperationException($"Failed to set grid cell. column='{columnName}', target='{targetValue}'");
    }

    private void CommitComboEdit(string columnName)
    {
        try
        {
            SendKeys.SendWait("{TAB}");
            Thread.Sleep(180);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Combo edit commit failed. column='{columnName}', reason={ex.Message}");
        }
    }

    private string GetComboCurrentText(AutomationElement combo)
    {
        var selected = FindDescendants(combo, ControlType.ListItem).FirstOrDefault(IsListItemSelected);
        if (selected is not null)
        {
            var name = SafeRead(() => selected.Current.Name) ?? "";
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return ReadValue(combo);
    }

    private void TryExpandCombo(AutomationElement combo)
    {
        try
        {
            if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var raw) &&
                raw is ExpandCollapsePattern pattern &&
                pattern.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
            {
                pattern.Expand();
                Thread.Sleep(150);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Combo expand failed: {ex.Message}");
        }
    }

    private bool TrySelectListItem(AutomationElement item)
    {
        try
        {
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw) && raw is SelectionItemPattern pattern)
            {
                pattern.Select();
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"SelectionItemPattern.Select failed: {ex.Message}");
        }

        try
        {
            if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var raw) && raw is InvokePattern pattern)
            {
                pattern.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"InvokePattern.Invoke failed on list item: {ex.Message}");
        }

        return false;
    }

    private bool TrySelectComboByKeyboard(AutomationElement combo, string targetValue, string columnName)
    {
        var items = FindDescendants(combo, ControlType.ListItem).ToList();
        var index = items.FindIndex(li =>
            string.Equals(SafeRead(() => li.Current.Name) ?? "", targetValue, StringComparison.Ordinal));
        if (index < 0)
        {
            _logger.Warn($"Keyboard select skipped: list item not found. column='{columnName}', target='{targetValue}'");
            return false;
        }

        TryFocus(combo, $"grid cell '{columnName}'");
        EnsureComboExpanded(combo, columnName);

        // 한글 항목은 SendKeys 타이핑이 IME 때문에 불가하므로, 맨 위로 올린 뒤 인덱스만큼 내려가 고른다.
        for (var i = 0; i < items.Count; i++)
        {
            SendKeys.SendWait("{UP}");
            Thread.Sleep(40);
        }

        for (var i = 0; i < index; i++)
        {
            SendKeys.SendWait("{DOWN}");
            Thread.Sleep(40);
        }

        SendKeys.SendWait("{ENTER}");
        Thread.Sleep(180);
        _logger.Info($"Keyboard combo navigation done. column='{columnName}', index={index}, items={items.Count}");
        return true;
    }

    private void EnsureComboExpanded(AutomationElement combo, string columnName)
    {
        try
        {
            if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var raw) &&
                raw is ExpandCollapsePattern pattern)
            {
                if (pattern.Current.ExpandCollapseState != ExpandCollapseState.Expanded)
                {
                    pattern.Expand();
                    Thread.Sleep(200);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Combo expand (keyboard) failed. column='{columnName}', reason={ex.Message}");
        }

        // ExpandCollapse가 cold 상태(편집모드 아님)로 던지면 드롭다운이 안 열린다.
        // 직전 인접 셀을 건너뛴 경우(예: SSD 불량창고)에 발생하므로, 드롭다운 버튼을 좌표 클릭해 강제로 연다.
        if (TryOpenComboByClick(combo, columnName))
        {
            return;
        }

        SendKeys.SendWait("%{DOWN}");
        Thread.Sleep(200);
    }

    private bool TryOpenComboByClick(AutomationElement combo, string columnName)
    {
        var rect = SafeReadRect(() => combo.Current.BoundingRectangle);
        if (rect is null || rect.Value.IsEmpty)
        {
            return false;
        }

        TryFocus(combo, $"grid cell '{columnName}'");
        Cursor.Position = new System.Drawing.Point((int)(rect.Value.Right - 8), (int)(rect.Value.Top + rect.Value.Height / 2));
        MouseClick();
        Thread.Sleep(250);
        _logger.Info($"콤보 드롭다운 좌표 클릭으로 강제 확장. column='{columnName}'");
        return true;
    }

    private static bool IsListItemSelected(AutomationElement item)
    {
        try
        {
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw) && raw is SelectionItemPattern pattern)
            {
                return pattern.Current.IsSelected;
            }
        }
        catch
        {
            // selection state is best-effort
        }

        return false;
    }

    private static string ReadValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) && raw is ValuePattern pattern)
            {
                return pattern.Current.Value ?? "";
            }
        }
        catch
        {
            // value read is best-effort
        }

        return "";
    }

    private bool SaveItemInfo(AutomationElement mainWindow)
    {
        if (!_config.Safety.SaveEnabled || _config.Safety.DryRun)
        {
            _logger.Warn($"Save skipped by safety gate. saveEnabled={_config.Safety.SaveEnabled}, dryRun={_config.Safety.DryRun}");
            return false;
        }

        BringToFront(mainWindow);
        SendKeys.SendWait("^s");
        _logger.Info("Ctrl+S sent (save).");
        return true;
    }

    private string ResolveInputPartsPath()
    {
        var configured = Environment.ExpandEnvironmentVariables(_config.Workflow.InputPartsPath);
        var path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_paths.RootDirectory, configured);

        if (File.Exists(path))
        {
            return path;
        }

        var examplePath = Path.Combine(_paths.RootDirectory, "input_parts.example.csv");
        if (File.Exists(examplePath))
        {
            _logger.Warn($"input_parts.csv not found. Using example input for normal-flow test. path='{examplePath}'");
            return examplePath;
        }

        throw new FileNotFoundException("Part input file was not found.", path);
    }

    private AutomationElement? FindEditNextToLabel(AutomationElement mainWindow, string labelText)
    {
        var label = FindFirstByNameContains(mainWindow, labelText);
        if (label is not null)
        {
            var labelRect = SafeReadRect(() => label.Current.BoundingRectangle);
            if (labelRect.HasValue && !labelRect.Value.IsEmpty)
            {
                var candidates = FindDescendants(mainWindow, ControlType.Edit)
                    .Select(element => new
                    {
                        Element = element,
                        Rect = SafeReadRect(() => element.Current.BoundingRectangle)
                    })
                    .Where(x => x.Rect.HasValue && !x.Rect.Value.IsEmpty)
                    .Where(x => Math.Abs(CenterY(x.Rect!.Value) - CenterY(labelRect.Value)) <= 22)
                    .Where(x => x.Rect!.Value.Left > labelRect.Value.Right)
                    .OrderBy(x => x.Rect!.Value.Left)
                    .ToList();

                var editable = candidates.FirstOrDefault(x => IsWritableValueControl(x.Element));
                if (editable is not null)
                {
                    return editable.Element;
                }

                if (candidates.Count > 0)
                {
                    return candidates[0].Element;
                }
            }
        }

        _logger.Warn($"{labelText} label-based search failed. Falling back to top-right Edit heuristic.");
        var windowRect = SafeReadRect(() => mainWindow.Current.BoundingRectangle);
        if (!windowRect.HasValue || windowRect.Value.IsEmpty)
        {
            return null;
        }

        return FindDescendants(mainWindow, ControlType.Edit)
            .Select(element => new
            {
                Element = element,
                Rect = SafeReadRect(() => element.Current.BoundingRectangle)
            })
            .Where(x => x.Rect.HasValue && !x.Rect.Value.IsEmpty)
            .Where(x => x.Rect!.Value.Top >= windowRect.Value.Top + 80 && x.Rect.Value.Top <= windowRect.Value.Top + 240)
            .Where(x => x.Rect!.Value.Left >= windowRect.Value.Left + windowRect.Value.Width * 0.45)
            .OrderBy(x => x.Rect!.Value.Top)
            .ThenBy(x => x.Rect!.Value.Left)
            .FirstOrDefault()?.Element;
    }
    private static bool IsElementUsable(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        try
        {
            // 캐시한 요소가 stale이면 프로퍼티 접근에서 ElementNotAvailableException이 난다.
            _ = element.Current.ControlType;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> HandleOpenPartIdPopupAsync(string originalPart)
    {
        var popup = await WaitForPartIdPopupAsync(TimeSpan.FromMilliseconds(700));
        if (popup is null)
        {
            return false;
        }

        _logger.Warn($"고객사PartID 팝업 자동 감지. 메인 조회를 누르지 않고 팝업을 먼저 처리합니다. part='{originalPart}'");

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
        while (DateTime.UtcNow < deadline)
        {
            var rows = FindDescendants(popup, ControlType.DataItem).ToList();
            if (rows.Count > 0)
            {
                var row = FindPopupRowByProductCode(popup, originalPart) ?? rows[0];
                await SelectPartIdPopupRowAsync(popup, row, originalPart);
                return false;
            }

            await DelayAsync(80);
        }

        _logger.Warn($"고객사PartID 팝업에 결과가 없어 미존재로 보고 경고 확인 후 기파트로 키보드 복구합니다. part='{originalPart}'");
        await DismissMissingWarningAsync(originalPart, forceEnterFallback: true);
        await RecoverPartIdPopupByKeyboardAsync(originalPart);
        return true;
    }

    private async Task<AutomationElement?> WaitForPartIdPopupAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var popup = FindPartIdPopup(logDuplicates: false);
            if (popup is not null)
            {
                return popup;
            }

            await DelayAsync(100);
        }

        return FindPartIdPopup(logDuplicates: false);
    }

    private AutomationElement? FindButtonByAutomationIdContains(AutomationElement root, string idFragment)
    {
        return FindDescendants(root, ControlType.Button)
            .FirstOrDefault(button => (SafeRead(() => button.Current.AutomationId) ?? "")
                .Contains(idFragment, StringComparison.OrdinalIgnoreCase));
    }

    private void ClickToolbarSearchFallback(AutomationElement mainWindow)
    {
        BringToFront(mainWindow);
        var rect = SafeReadRect(() => mainWindow.Current.BoundingRectangle);
        if (!rect.HasValue || rect.Value.IsEmpty)
        {
            throw new InvalidOperationException("Main window rectangle is unavailable for search fallback.");
        }

        var x = (int)(rect.Value.Left + 208);
        var y = (int)(rect.Value.Top + 30);
        Cursor.Position = new System.Drawing.Point(x, y);
        MouseClick();
    }

    // 미존재 파트 처리. 조회 직후:
    //  1) '[971001] 존재하지 않습니다' 경고가 떴으면 닫는다(=미존재 확정 신호).
    //  2) 자동으로 열린 고객사PartID 팝업에 기파트를 넣고 Enter, Enter로 정상값을 다시 선택한다.
    // 미존재로 처리했으면 true, 경고가 없으면(다른 원인) false.
    private async Task<bool> HandleMissingPartAsync(AutomationElement? mainWindow, string originalPart)
    {
        AutomationElement? warning = null;
        for (var attempt = 0; attempt < 2 && warning is null; attempt++)
        {
            warning = FindWarningDialog();
            if (warning is null)
            {
                await DelayAsync(80);
            }
        }

        if (warning is null)
        {
            var names = string.Join(" | ", FindTopLevelWindows()
                .Select(window => SafeRead(() => window.Current.Name) ?? "")
                .Where(name => !string.IsNullOrWhiteSpace(name)));
            _logger.Info($"미존재 경고 미감지(다른 원인 가능). 현재 top-level 창: [{names}]");

            if (FindPartIdPopup() is null)
            {
                return false;
            }

            _logger.Warn($"고객사PartID 팝업이 열려 있어 UIA 미감지 경고로 보고 Enter 후 기파트 복구 처리합니다. part='{originalPart}'");
            await DismissMissingWarningAsync(originalPart, forceEnterFallback: true);
            await RecoverPartIdPopupByKeyboardAsync(originalPart);
            return true;
        }

        _logger.Warn($"미존재 경고 감지. part='{originalPart}'");
        _screenshots.CaptureDesktop($"missing_part_{MakeSafeToken(originalPart)}");

        var ok = FindButtonByAnyName(warning, ["확인", "OK"]);
        if (ok is not null)
        {
            ClickElement(ok, "missing-part warning confirm");
            _logger.Info("미존재 경고창 [확인] 처리.");
            await DelayAsync(300);
        }

        await RecoverPartIdPopupByKeyboardAsync(originalPart);

        return true;
    }

    private async Task DismissMissingWarningAsync(string originalPart, bool forceEnterFallback = false)
    {
        if (!forceEnterFallback)
        {
            var warning = FindWarningDialog();
            if (warning is not null)
            {
                var ok = FindButtonByAnyName(warning, ["확인", "OK"]);
                if (ok is not null)
                {
                    ClickElement(ok, "missing-part warning confirm");
                    _logger.Info("미존재 경고창 [확인] 처리.");
                    await DelayAsync(500);
                    return;
                }
            }
        }

        _logger.Warn($"미존재 경고창을 UIA로 찾지 못해 Enter fallback으로 확인 처리합니다. part='{originalPart}'");
        try
        {
            SendKeys.SendWait("{ENTER}");
            _logger.Info("미존재 경고창 Enter fallback 전송 완료.");
            await DelayAsync(600);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"미존재 경고 Enter fallback 실패: {ex.Message}");
        }
    }

    private async Task CancelPartIdPopupAsync(string originalPart)
    {
        var popup = FindPartIdPopup();
        if (popup is null)
        {
            _logger.Warn($"고객사PartID 팝업이 없어 취소 처리를 생략합니다. part='{originalPart}'");
            return;
        }

        var cancel = FindByAutomationId(popup, "1769868")
            ?? FindByAutomationId(popup, "4655312")
            ?? FindButtonByAnyName(popup, ["취소", "Cancel"]);
        if (cancel is null)
        {
            throw new InvalidOperationException("고객사PartID 팝업의 취소 버튼을 찾지 못했습니다.");
        }

        ClickElement(cancel, "고객사PartID popup cancel after missing part");
        await DelayAsync(300);
        _logger.Info($"고객사PartID 팝업 [취소] 처리. part='{originalPart}'");
    }

    private async Task RecoverPartIdPopupByKeyboardAsync(string originalPart)
    {
        var recoveryPart = _config.Global.RecoveryPart;
        if (string.IsNullOrWhiteSpace(recoveryPart))
        {
            _logger.Warn("itemInfo.recoveryPart가 비어 있어 기파트 키보드 복구를 생략하고 팝업을 취소합니다.");
            await CancelPartIdPopupAsync(originalPart);
            return;
        }

        var popup = FindPartIdPopup()
            ?? throw new InvalidOperationException("고객사PartID 팝업을 찾지 못해 기파트 키보드 복구를 진행할 수 없습니다.");

        var productCodeEdit = FindPopupProductCodeEdit(popup)
            ?? throw new InvalidOperationException("고객사PartID 팝업의 품목 코드 입력칸을 찾지 못했습니다.");

        SetElementText(productCodeEdit, recoveryPart, "고객사PartID 팝업 품목 코드(복구)");
        TryFocus(productCodeEdit, "고객사PartID 팝업 품목 코드(복구)");
        await DelayAsync(300);

        _logger.Info($"기파트 복구 조회 Enter 전송. recovery='{recoveryPart}'");
        SendKeys.SendWait("{ENTER}");
        await WaitForPartIdPopupResultAsync(recoveryPart, TimeSpan.FromMilliseconds(2000));
        await DelayAsync(200);
        _logger.Info($"기파트 복구 선택 Enter 전송. recovery='{recoveryPart}'");
        SendKeys.SendWait("{ENTER}");
        await WaitForPartIdPopupClosedAsync(TimeSpan.FromMilliseconds(1500));

        if (FindPartIdPopup() is not null)
        {
            var refreshedPopup = FindPartIdPopup();
            var row = refreshedPopup is null ? null : FindPopupRowByProductCode(refreshedPopup, recoveryPart);
            if (refreshedPopup is not null && row is not null)
            {
                await SelectPartIdPopupRowAsync(refreshedPopup, row, recoveryPart);
            }
            else
            {
                _logger.Warn($"기파트 키보드 복구 후에도 팝업이 남아 있어 취소합니다. recovery='{recoveryPart}'");
                await CancelPartIdPopupAsync(originalPart);
            }
        }

        _logger.Info($"기파트 키보드 복구 완료. original='{originalPart}', recovery='{recoveryPart}'");
    }

    private async Task WaitForPartIdPopupResultAsync(string productCode, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var popup = FindPartIdPopup();
            if (popup is null)
            {
                return;
            }

            if (FindPopupRowByProductCode(popup, productCode) is not null ||
                FindDescendants(popup, ControlType.DataItem).Any())
            {
                return;
            }

            await DelayAsync(100);
        }
    }

    private async Task WaitForPartIdPopupClosedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FindPartIdPopup() is null)
            {
                return;
            }

            await DelayAsync(100);
        }
    }

    private AutomationElement? FindPopupProductCodeEdit(AutomationElement popup)
    {
        var edits = FindDescendants(popup, ControlType.Edit).ToList();
        return edits.FirstOrDefault(edit =>
                   (SafeRead(() => edit.Current.AutomationId) ?? "")
                   .Contains("txtCd", StringComparison.OrdinalIgnoreCase))
               ?? edits.FirstOrDefault(edit =>
                   (SafeRead(() => edit.Current.AutomationId) ?? "")
                   .Equals("1441912", StringComparison.OrdinalIgnoreCase))
               ?? edits.FirstOrDefault(edit =>
                   (SafeRead(() => edit.Current.AutomationId) ?? "")
                   .Equals("2427784", StringComparison.OrdinalIgnoreCase))
               ?? edits.FirstOrDefault();
    }

    private async Task SelectPartIdPopupRowAsync(AutomationElement popup, AutomationElement row, string part)
    {
        ClickElement(row, "고객사PartID popup row");
        await DelayAsync(150);

        var ok = FindByAutomationId(popup, "3542176") ?? FindButtonByAnyName(popup, ["확인", "OK"]);
        if (ok is null)
        {
            throw new InvalidOperationException("고객사PartID 팝업의 확인 버튼을 찾지 못했습니다.");
        }

        ClickElement(ok, "고객사PartID popup confirm");
        await DelayAsync(300);
        _logger.Info($"고객사PartID 팝업 행 선택 완료. part='{part}'");
    }

    private AutomationElement? FindPopupRowByProductCode(AutomationElement popup, string productCode)
    {
        var productId = PartClassifier.ExtractPid(productCode);
        var candidates = new[] { productCode, productId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = FindDescendants(popup, ControlType.DataItem).ToList();
        foreach (var row in rows)
        {
            var name = SafeRead(() => row.Current.Name) ?? "";
            if (candidates.Any(candidate => string.Equals(name.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return row;
            }

            var editValues = FindDescendants(row, ControlType.Edit)
                .Select(ReadValue)
                .Where(value => !string.IsNullOrWhiteSpace(value));
            if (editValues.Any(value => candidates.Any(candidate =>
                    string.Equals(value.Trim(), candidate, StringComparison.OrdinalIgnoreCase))))
            {
                return row;
            }
        }

        if (rows.Count == 1)
        {
            _logger.Warn($"팝업 행을 정확히 식별하지 못했지만 결과가 1건이라 해당 행을 선택합니다. part='{productCode}'");
            return rows[0];
        }

        _logger.Warn($"팝업 행 식별 실패. part='{productCode}', popupRows={rows.Count}");
        return null;
    }

    private AutomationElement? FindPartIdPopup(bool logDuplicates = true)
    {
        var popups = new List<AutomationElement>();

        foreach (var window in FindTopLevelWindows())
        {
            if (IsPartIdPopupWindow(window))
            {
                popups.Add(window);
                continue;
            }

            if (!IsUnimesCandidate(window))
            {
                continue;
            }

            popups.AddRange(FindDescendants(window, ControlType.Window)
                .Where(IsPartIdPopupWindow));
        }

        popups = popups
            .GroupBy(popup => SafeRead(() => popup.Current.NativeWindowHandle))
            .Select(group => group.Last())
            .ToList();

        if (logDuplicates && popups.Count > 1)
        {
            _logger.Warn($"고객사PartID 팝업이 {popups.Count}개 감지되었습니다. 가장 최근 후보를 사용합니다.");
        }

        return popups
            .Where(popup =>
            {
                var rect = SafeReadRect(() => popup.Current.BoundingRectangle);
                return rect.HasValue && !rect.Value.IsEmpty;
            })
            .LastOrDefault()
            ?? popups.LastOrDefault();
    }

    private static bool IsPartIdPopupWindow(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        if (!name.Contains("PartID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var controlType = SafeRead(() => window.Current.ControlType);
        return controlType == ControlType.Window;
    }


    private void LaunchUnimes()
    {
        var path = Environment.ExpandEnvironmentVariables(_config.App.LaunchPath);
        _logger.Info($"Launching UNIMES target: {path}");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("UNIMES launch target was not found.", path);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };

        Process.Start(startInfo);
        _logger.Info("Launch request submitted.");
    }

    private async Task HandleLoginScreenAsync(AutomationElement loginWindow)
    {
        BringToFront(loginWindow);
        await DelayAsync(500);

        await RestoreLoginTryAgainStateAsync(loginWindow);
        ThrowIfCancellationRequested("로그인 화면 준비");

        var credentials = ResolveLoginCredentials();

        var fields = FindLoginEditFields(loginWindow);
        if (credentials is not null && (fields.UserId is null || fields.Password is null))
        {
            _logger.Warn("Login Edit controls were not fully exposed by UIA. Falling back to coordinate-based login input.");
            _screenshots.CaptureElement(loginWindow, "login_coordinate_input_fallback");
            FillLoginCredentialsByCoordinates(loginWindow, credentials.Value);
            await ClickLoginSubmitAsync(loginWindow);
            return;
        }

        if (fields.UserId is null && fields.Password is null)
        {
            _logger.Warn("No Edit controls found on login screen. UIA may not expose login fields.");
            _screenshots.CaptureElement(loginWindow, "login_no_edit_controls");
            return;
        }

        _logger.Info($"Login Edit controls selected. userId={DescribeElementForLog(fields.UserId)}, password={DescribeElementForLog(fields.Password)}");

        if (credentials is not null && !string.IsNullOrWhiteSpace(credentials.Value.UserId))
        {
            if (fields.UserId is null)
            {
                throw new InvalidOperationException("Login user id edit control was not found.");
            }

            SetElementText(fields.UserId, credentials.Value.UserId, "login user id");
            _logger.Info($"Login user id filled. source={credentials.Value.Source}");
        }

        if (credentials is not null)
        {
            if (fields.Password is null)
            {
                throw new InvalidOperationException("Password edit control was not found.");
            }

            SetElementText(fields.Password, credentials.Value.Password, "login password");
            _logger.Info($"Login password filled. source={credentials.Value.Source}. Password value is not logged.");

            await ClickLoginSubmitAsync(loginWindow);
        }
        else
        {
            if (fields.Password is not null)
            {
                TryFocus(fields.Password, "password field");
            }

            _screenshots.CaptureElement(loginWindow, "login_waiting_for_manual_password");
            _logger.Info("No stored login credentials were configured. Enter password and submit login in UNIMES. Waiting for main window.");
        }
    }

    private (AutomationElement? UserId, AutomationElement? Password) FindLoginEditFields(AutomationElement loginWindow)
    {
        var edits = FindDescendants(loginWindow, ControlType.Edit)
            .Select(element => new
            {
                Element = element,
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Enabled = SafeRead(() => element.Current.IsEnabled),
                Offscreen = SafeRead(() => element.Current.IsOffscreen)
            })
            .Where(candidate => candidate.Rect.HasValue &&
                                !candidate.Rect.Value.IsEmpty &&
                                candidate.Enabled &&
                                !candidate.Offscreen)
            .OrderBy(candidate => candidate.Rect!.Value.Top)
            .ThenBy(candidate => candidate.Rect!.Value.Left)
            .ToList();

        _logger.Info($"Login visible Edit controls found: {edits.Count}");
        for (var index = 0; index < edits.Count; index++)
        {
            _logger.Info($"  login edit[{index}]: {DescribeElementForLog(edits[index].Element)}");
        }

        if (edits.Count == 0)
        {
            return (null, null);
        }

        if (edits.Count == 1)
        {
            return (null, edits[0].Element);
        }

        return (edits[0].Element, edits[1].Element);
    }

    private void FillLoginCredentialsByCoordinates(
        AutomationElement loginWindow,
        (string UserId, string Password, string Source) credentials)
    {
        // The UNIMES login form has ID and password on the same row:
        // left field = user id, right field = password. The rows below are language/server combo boxes.
        SetLoginTextByCoordinates(loginWindow, 0.625, 0.529, credentials.UserId);
        _logger.Info($"Login user id filled by coordinates. source={credentials.Source}");

        SetLoginTextByCoordinates(loginWindow, 0.785, 0.529, credentials.Password);
        _logger.Info($"Login password filled by coordinates. source={credentials.Source}. Password value is not logged.");
    }

    private static void SetLoginTextByCoordinates(AutomationElement loginWindow, double relativeX, double relativeY, string text)
    {
        ClickLoginPoint(loginWindow, relativeX, relativeY);
        Thread.Sleep(100);
        SendKeys.SendWait("^a");
        SendKeys.SendWait("{BACKSPACE}");
        SendKeys.SendWait(EscapeForSendKeys(text));
    }

    private async Task ClickLoginSubmitAsync(AutomationElement loginWindow)
    {
        var loginButton = await WaitForLoginButtonAsync(loginWindow, TimeSpan.FromSeconds(8));
        if (loginButton is not null)
        {
            ClickElement(loginButton, "login submit");
            _logger.Info("Login button clicked.");
            return;
        }

        _screenshots.CaptureElement(loginWindow, "login_button_not_found");
        ClickLoginSubmitByCoordinates(loginWindow);
        _logger.Warn("Login button UIA element was not found. Coordinate fallback clicked.");
    }

    private void ThrowIfLoginFailureDetected()
    {
        var dialog = FindLoginFailureDialog();
        if (dialog is null)
        {
            return;
        }

        var message = ReadMessageText(dialog);
        _screenshots.CaptureElement(dialog, "login_failed");
        var ok = FindButtonByAnyName(dialog, ["확인", "OK"]);
        if (ok is not null)
        {
            ClickElement(ok, "login failure confirm");
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(message)
                ? "UNIMES 로그인 실패가 감지되었습니다. 설정 창에서 아이디/비밀번호를 다시 확인하세요."
                : $"UNIMES 로그인 실패가 감지되었습니다. 설정 창에서 아이디/비밀번호를 다시 확인하세요. message='{message}'");
    }

    private AutomationElement? FindLoginFailureDialog()
    {
        string[] tokens = ["비밀번호", "패스워드", "Password", "password", "incorrect", "invalid", "틀렸", "잘못", "실패", "오류", "인증"];

        foreach (var window in FindTopLevelWindows())
        {
            if (!IsUnimesCandidate(window))
            {
                continue;
            }

            if (IsLoginFailureDialogCandidate(window, tokens))
            {
                return window;
            }

            foreach (var childWindow in FindDirectChildWindows(window))
            {
                if (IsLoginFailureDialogCandidate(childWindow, tokens))
                {
                    return childWindow;
                }
            }
        }

        return null;
    }

    private bool IsLoginFailureDialogCandidate(AutomationElement window, IReadOnlyCollection<string> tokens)
    {
        return IsSmallPopupCandidate(window) &&
               !IsLoginScreen(window) &&
               FindButtonByAnyName(window, ["확인", "OK"]) is not null &&
               WindowContainsAnyText(window, tokens);
    }

    private (string UserId, string Password, string Source)? ResolveLoginCredentials()
    {
        var mode = (_config.Login.PasswordMode ?? "").Trim().ToLowerInvariant();
        if (mode == "dpapi")
        {
            var userId = string.IsNullOrWhiteSpace(_config.Login.UserId)
                ? GetEnvironmentValue(_config.Login.UserIdEnvironmentVariable)
                : _config.Login.UserId;
            var password = SecretProtector.Decrypt(_config.Login.PasswordEncrypted);

            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrEmpty(password))
            {
                return (userId, password, "dpapi");
            }

            throw new InvalidOperationException(
                "login.passwordMode=dpapi 이지만 복호화된 비밀번호가 비어 있습니다. 설정 창에서 비밀번호를 다시 입력하세요.");
        }

        if (mode == "env")
        {
            var userId = GetEnvironmentValue(_config.Login.UserIdEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(userId))
            {
                userId = _config.Login.UserId;
            }

            var password = GetEnvironmentValue(_config.Login.PasswordEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrEmpty(password))
            {
                return (userId, password, "env");
            }

            throw new InvalidOperationException(
                $"login.passwordMode=env 이지만 환경변수 '{_config.Login.PasswordEnvironmentVariable}' 값이 없습니다.");
        }

        if (_config.Login.UseConfigPassword)
        {
            if (!string.IsNullOrWhiteSpace(_config.Login.UserId) && !string.IsNullOrEmpty(_config.Login.Password))
            {
                return (_config.Login.UserId, _config.Login.Password, "config");
            }

            throw new InvalidOperationException("login.passwordMode=config 이지만 login.userId 또는 login.password 값이 비어 있습니다.");
        }

        return null;
    }

    private static string GetEnvironmentValue(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? ""
            : Environment.GetEnvironmentVariable(name) ?? "";
    }

    private async Task RestoreLoginTryAgainStateAsync(AutomationElement loginWindow)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (!IsLoginTryAgainState(loginWindow))
            {
                if (attempt > 1)
                {
                    _logger.Info("Login Try again state cleared.");
                }

                return;
            }

            _logger.Warn($"Login Try again state detected. Clicking Try again. attempt={attempt}");
            _screenshots.CaptureElement(loginWindow, $"login_try_again_{attempt}");

            if (!TryClickLoginTryAgain(loginWindow))
            {
                ClickLoginTryAgainByCoordinates(loginWindow);
                _logger.Warn("Login Try again coordinate fallback clicked.");
            }

            await DelayAsync(1800);
        }

        if (IsLoginTryAgainState(loginWindow))
        {
            _screenshots.CaptureElement(loginWindow, "login_try_again_not_cleared");
            _logger.Warn("Login Try again 상태가 해소되지 않은 것으로 감지됐지만 로그인 입력을 계속 시도합니다.");
        }
    }

    private bool IsLoginTryAgainState(AutomationElement loginWindow)
    {
        return FindVisibleLoginTryAgainElement(loginWindow) is not null;
    }

    private bool TryClickLoginTryAgain(AutomationElement loginWindow)
    {
        var target = FindVisibleLoginTryAgainElement(loginWindow);
        if (target is null)
        {
            return false;
        }

        BringToFront(loginWindow);
        ClickElementCenterByMouse(target);
        return true;
    }

    private AutomationElement? FindVisibleLoginTryAgainElement(AutomationElement loginWindow)
    {
        var loginRect = SafeReadRect(() => loginWindow.Current.BoundingRectangle);
        if (!loginRect.HasValue || loginRect.Value.IsEmpty)
        {
            return null;
        }

        if (!HasVisibleLoginServerError(loginWindow, loginRect.Value))
        {
            return null;
        }

        if (HasVisibleLoginServerSelection(loginWindow, loginRect.Value))
        {
            return null;
        }

        return FindDescendants(loginWindow, null)
            .Select(element => new
            {
                Element = element,
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Offscreen = SafeRead(() => element.Current.IsOffscreen),
                ContainsTryAgain = ElementContainsAnyText(element, ["Try again"])
            })
            .Where(candidate => candidate.ContainsTryAgain &&
                                candidate.Rect.HasValue &&
                                !candidate.Rect.Value.IsEmpty &&
                                !candidate.Offscreen &&
                                IsLoginTryAgainLinkRect(candidate.Rect.Value, loginRect.Value))
            .OrderBy(candidate => candidate.Rect!.Value.Top)
            .ThenBy(candidate => candidate.Rect!.Value.Left)
            .FirstOrDefault()?.Element;
    }

    private bool HasVisibleLoginServerError(AutomationElement loginWindow, System.Windows.Rect loginRect)
    {
        return FindDescendants(loginWindow, null)
            .Select(element => new
            {
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Offscreen = SafeRead(() => element.Current.IsOffscreen),
                ContainsError = ElementContainsAnyText(element, ["서버가 응답", "응답하지 않습니다"])
            })
            .Any(candidate => candidate.ContainsError &&
                              candidate.Rect.HasValue &&
                              !candidate.Rect.Value.IsEmpty &&
                              !candidate.Offscreen &&
                              IsLoginTopWarningRect(candidate.Rect.Value, loginRect));
    }

    private bool HasVisibleLoginServerSelection(AutomationElement loginWindow, System.Windows.Rect loginRect)
    {
        return FindDescendants(loginWindow, null)
            .Select(element => new
            {
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Offscreen = SafeRead(() => element.Current.IsOffscreen),
                ContainsServer = ElementContainsAnyText(element, ["UNIMES"])
            })
            .Any(candidate => candidate.ContainsServer &&
                              candidate.Rect.HasValue &&
                              !candidate.Rect.Value.IsEmpty &&
                              !candidate.Offscreen &&
                              IsLoginServerSelectionRect(candidate.Rect.Value, loginRect));
    }

    private static bool IsLoginTryAgainLinkRect(System.Windows.Rect rect, System.Windows.Rect loginRect)
    {
        return IsLoginTopWarningRect(rect, loginRect) &&
               rect.Left >= loginRect.Left + loginRect.Width * 0.25 &&
               rect.Left <= loginRect.Left + loginRect.Width * 0.50 &&
               rect.Width <= loginRect.Width * 0.20;
    }

    private static bool IsLoginTopWarningRect(System.Windows.Rect rect, System.Windows.Rect loginRect)
    {
        return rect.Top >= loginRect.Top &&
               rect.Top <= loginRect.Top + loginRect.Height * 0.18 &&
               rect.Left >= loginRect.Left + loginRect.Width * 0.05 &&
               rect.Left <= loginRect.Left + loginRect.Width * 0.55 &&
               rect.Height <= loginRect.Height * 0.08;
    }

    private static bool IsLoginServerSelectionRect(System.Windows.Rect rect, System.Windows.Rect loginRect)
    {
        return rect.Left >= loginRect.Left + loginRect.Width * 0.50 &&
               rect.Left <= loginRect.Left + loginRect.Width * 0.90 &&
               rect.Top >= loginRect.Top + loginRect.Height * 0.60 &&
               rect.Top <= loginRect.Top + loginRect.Height * 0.70;
    }

    private static void ClickLoginTryAgainByCoordinates(AutomationElement loginWindow)
    {
        ClickLoginPoint(loginWindow, 0.375, 0.098);
    }

    private static void ClickLoginSubmitByCoordinates(AutomationElement loginWindow)
    {
        ClickLoginPoint(loginWindow, 0.604, 0.710);
    }

    private static void ClickLoginPoint(AutomationElement loginWindow, double relativeX, double relativeY)
    {
        var rect = loginWindow.Current.BoundingRectangle;
        if (rect.IsEmpty)
        {
            return;
        }

        Cursor.Position = new System.Drawing.Point(
            (int)(rect.Left + rect.Width * relativeX),
            (int)(rect.Top + rect.Height * relativeY));
        MouseClick();
    }

    private async Task<AutomationElement?> WaitForLoginButtonAsync(AutomationElement loginWindow, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var button = FindButtonByAnyName(loginWindow, ["확인", "?뺤씤", "Login", "OK"]);
            if (button is not null && SafeRead(() => button.Current.IsEnabled))
            {
                return button;
            }

            await DelayAsync(250);
        }

        var finalButton = FindButtonByAnyName(loginWindow, ["확인", "?뺤씤", "Login", "OK"]);
        return finalButton is not null && SafeRead(() => finalButton.Current.IsEnabled)
            ? finalButton
            : null;
    }

    private async Task<AutomationElement?> WaitForUnimesWindowAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            ThrowIfCancellationRequested("UNIMES 창 탐색");
            attempt++;
            var window = FindUnimesWindow();
            if (window is not null)
            {
                return window;
            }

            if (attempt == 1 || attempt % 10 == 0)
            {
                LogTopLevelWindowSnapshot($"UNIMES window scan attempt {attempt}");
            }

            await DelayAsync(500);
        }

        return null;
    }

    private async Task<AutomationElement?> WaitForLoginOrMainWindowAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            ThrowIfCancellationRequested("로그인/메인 창 탐색");
            ThrowIfLoginFailureDetected();

            attempt++;
            var candidates = FindTopLevelWindows()
                .Where(IsUnimesCandidate)
                .ToList();

            var login = candidates.FirstOrDefault(IsLoginScreen);
            if (login is not null)
            {
                return login;
            }

            var main = candidates.FirstOrDefault(candidate => !IsLoginScreen(candidate) && IsProbablyMainWindow(candidate));
            if (main is not null)
            {
                return main;
            }

            if (attempt == 1 || attempt % 10 == 0)
            {
                LogTopLevelWindowSnapshot($"Login/main window scan attempt {attempt}");
            }

            await DelayAsync(500);
        }

        return null;
    }

    private async Task<AutomationElement?> WaitForMainWindowAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ThrowIfCancellationRequested("메인 창 탐색");
            ThrowIfLoginFailureDetected();

            var candidates = FindTopLevelWindows()
                .Where(IsUnimesCandidate)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (!IsLoginScreen(candidate) && IsProbablyMainWindow(candidate))
                {
                    return candidate;
                }
            }

            await DelayAsync(1000);
        }

        return null;
    }

    private enum LaunchMode
    {
        Launch,
        Attach,
        AttachOrLaunch
    }

    private LaunchMode ResolveLaunchMode(CommandLineOptions options)
    {
        if (options.NoLaunch)
        {
            return LaunchMode.Attach;
        }

        return (_config.App.LaunchMode ?? "").Trim().ToLowerInvariant() switch
        {
            "launch" => LaunchMode.Launch,
            "attach" => LaunchMode.Attach,
            _ => LaunchMode.AttachOrLaunch
        };
    }

    private AutomationElement? FindExistingMainWindow()
    {
        return FindTopLevelWindows()
            .Where(IsUnimesCandidate)
            .FirstOrDefault(window => !IsLoginScreen(window) && IsProbablyMainWindow(window));
    }

    private AutomationElement? FindUnimesWindow()
    {
        var candidates = FindTopLevelWindows()
            .Where(IsUnimesCandidate)
            .ToList();

        if (candidates.Count == 0)
        {
            LogProcessHints();
            return null;
        }

        var login = candidates.FirstOrDefault(IsLoginScreen);
        if (login is not null)
        {
            return login;
        }

        return candidates.FirstOrDefault(IsProbablyMainWindow) ?? candidates[0];
    }

    private IEnumerable<AutomationElement> FindTopLevelWindows()
    {
        AutomationElementCollection windows;
        try
        {
            windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        }
        catch (Exception ex)
        {
            _logger.Warn($"Top-level window scan failed: {ex.Message}");
            yield break;
        }

        foreach (AutomationElement window in windows)
        {
            yield return window;
        }
    }

    private IEnumerable<AutomationElement> FindDirectChildWindows(AutomationElement root)
    {
        AutomationElementCollection windows;
        try
        {
            windows = root.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
        }
        catch
        {
            yield break;
        }

        foreach (AutomationElement window in windows)
        {
            yield return window;
        }
    }

    private static bool IsMainShellWindow(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        var automationId = SafeRead(() => window.Current.AutomationId) ?? "";

        return name.Contains("UNIMES -", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(automationId, "ShellForm", StringComparison.Ordinal);
    }

    private bool IsUnimesCandidate(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        var className = SafeRead(() => window.Current.ClassName) ?? "";
        var processId = SafeReadInt(() => window.Current.ProcessId);
        var processName = GetProcessName(processId);

        if (string.Equals(processName, "(unavailable)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsOwnProcess(processId, processName))
        {
            return false;
        }

        if (IsShellPropertiesWindow(name, className, processName))
        {
            return false;
        }

        if (IsOwnConsoleWindow(name, className))
        {
            return false;
        }

        // 같은 플랫폼(Bizentro.App.MAIN.Shell)에서 MES와 ERP가 동일 프로세스/클래스로 떠서
        // 타이틀로만 구분된다(MES='UNIMES - ...', ERP='UNIERP - ...'). 제외 토큰이 타이틀에
        // 있으면 프로세스명 힌트보다 우선해 후보에서 뺀다(예: ERP 창을 잡지 않도록).
        if (_config.App.WindowTitleExcludes.Any(token =>
                !string.IsNullOrWhiteSpace(token) &&
                name.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (_config.App.WindowTitleContains.Any(token =>
                !string.IsNullOrWhiteSpace(token) &&
                name.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (className.Contains("UNIMES", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (processName.StartsWith("Bizentro.App.MAIN.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Some ClickOnce windows expose weak titles during startup. Process hints are
        // a secondary signal only; title/class name still has priority.
        if (processId.HasValue && processId.Value > 0)
        {
            return _config.App.ProcessNameHints.Any(hint =>
                !string.IsNullOrWhiteSpace(hint) &&
                processName.Contains(hint, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private bool IsLoginScreen(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        var processId = SafeReadInt(() => window.Current.ProcessId);
        var processName = GetProcessName(processId);

        if (processName.Equals("Bizentro.App.MAIN.Shell", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!name.Contains("UNIMES", StringComparison.OrdinalIgnoreCase) &&
            !processName.Equals("Bizentro.App.MAIN.ClientAgent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var editCount = CountDescendants(window, ControlType.Edit, 4);
        var comboCount = CountDescendants(window, ControlType.ComboBox, 4);
        var hasConfirm = FindButtonByAnyName(window, ["확인", "Login", "OK"]) is not null;
        var hasCancelOrSetting = FindButtonByAnyName(window, ["취소", "설정", "Cancel", "Setting"]) is not null;

        if (processName.Equals("Bizentro.App.MAIN.ClientAgent", StringComparison.OrdinalIgnoreCase))
        {
            return editCount >= 1;
        }

        return editCount >= 1 && (comboCount >= 1 || hasConfirm) && hasCancelOrSetting;
    }

    private bool IsProbablyMainWindow(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        var processId = SafeReadInt(() => window.Current.ProcessId);
        var processName = GetProcessName(processId);

        if (processName.Equals("Bizentro.App.MAIN.Shell", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!name.Contains("UNIMES", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (FindFirstByNameContains(window, "Home Page") is not null)
        {
            return true;
        }

        if (FindFirstByNameContains(window, "기준정보") is not null)
        {
            return true;
        }

        return !IsLoginScreen(window);
    }

    private AutomationElement? FindButtonByAnyName(AutomationElement root, IReadOnlyCollection<string> names)
    {
        foreach (var button in FindDescendants(root, ControlType.Button))
        {
            var name = SafeRead(() => button.Current.Name) ?? "";
            if (names.Any(candidate => name.Contains(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return button;
            }
        }

        return null;
    }

    private AutomationElement? FindFirstByNameContains(AutomationElement root, string text)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, text));
        }
        catch
        {
            // Exact property search can fail on disappearing elements; fallback below.
        }

        return FindDescendants(root, null)
            .FirstOrDefault(element =>
            {
                var name = SafeRead(() => element.Current.Name) ?? "";
                return name.Contains(text, StringComparison.OrdinalIgnoreCase);
            });
    }

    private IEnumerable<AutomationElement> FindDescendants(AutomationElement root, ControlType? controlType)
    {
        Condition condition = controlType is null
            ? Condition.TrueCondition
            : new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);

        AutomationElementCollection elements;
        try
        {
            elements = root.FindAll(TreeScope.Descendants, condition);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Control search failed. controlType={controlType?.ProgrammaticName ?? "any"}, reason={ex.Message}");
            yield break;
        }

        foreach (AutomationElement element in elements)
        {
            yield return element;
        }
    }

    private int CountDescendants(AutomationElement root, ControlType controlType, int stopAt)
    {
        var count = 0;
        foreach (var _ in FindDescendants(root, controlType))
        {
            count++;
            if (count >= stopAt)
            {
                return count;
            }
        }

        return count;
    }

    private void SetElementText(AutomationElement element, string text, string fieldName)
    {
        TryFocus(element, fieldName);

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern) &&
            rawPattern is ValuePattern valuePattern &&
            !valuePattern.Current.IsReadOnly)
        {
            try
            {
                valuePattern.SetValue(text);
                return;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ElementNotAvailableException)
            {
                _logger.Warn($"ValuePattern.SetValue failed for {fieldName}. Falling back to SendKeys. reason={ex.Message}");
            }
        }

        _logger.Warn($"ValuePattern unavailable for {fieldName}. Falling back to SendKeys.");
        SendKeys.SendWait("^a");
        SendKeys.SendWait("{BACKSPACE}");
        SendKeys.SendWait(EscapeForSendKeys(text));
    }

    private void ClickElement(AutomationElement element, string reason)
    {
        _safety.EnsureCanClick(element, reason);
        BringToFront(GetContainingWindow(element));

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var rawPattern) &&
            rawPattern is InvokePattern invokePattern)
        {
            invokePattern.Invoke();
            return;
        }

        _logger.Debug($"InvokePattern unavailable. 좌표 기반 fallback 사용. reason='{reason}'");
        ClickElementCenterByMouse(element);
    }

    private static void TryFocus(AutomationElement element, string fieldName)
    {
        try
        {
            element.SetFocus();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unable to focus {fieldName}.", ex);
        }
    }

    private void LogWindowIdentity(AutomationElement window, string label)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        var className = SafeRead(() => window.Current.ClassName) ?? "";
        var automationId = SafeRead(() => window.Current.AutomationId) ?? "";
        var processId = SafeReadInt(() => window.Current.ProcessId);
        var processName = GetProcessName(processId);
        var rect = SafeReadRect(() => window.Current.BoundingRectangle);
        var enabled = SafeRead(() => window.Current.IsEnabled);
        var visible = !SafeRead(() => window.Current.IsOffscreen);
        var rectText = rect.HasValue
            ? $"L={rect.Value.Left:0},T={rect.Value.Top:0},R={rect.Value.Right:0},B={rect.Value.Bottom:0},W={rect.Value.Width:0},H={rect.Value.Height:0}"
            : "(unavailable)";

        _logger.Info($"{label}: name='{name}', class='{className}', automationId='{automationId}', processId={processId}, processName='{processName}', rect='{rectText}', enabled={enabled}, visible={visible}");
    }

    private static string DescribeElementForLog(AutomationElement? element)
    {
        if (element is null)
        {
            return "(null)";
        }

        var name = SafeRead(() => element.Current.Name) ?? "";
        var automationId = SafeRead(() => element.Current.AutomationId) ?? "";
        var controlType = SafeRead(() => element.Current.ControlType)?.ProgrammaticName ?? "";
        var rect = SafeReadRect(() => element.Current.BoundingRectangle);
        var rectText = rect.HasValue
            ? $"L={rect.Value.Left:0},T={rect.Value.Top:0},R={rect.Value.Right:0},B={rect.Value.Bottom:0},W={rect.Value.Width:0},H={rect.Value.Height:0}"
            : "(unavailable)";

        return $"type='{controlType}', name='{name}', automationId='{automationId}', rect='{rectText}'";
    }

    private void LogProcessHints()
    {
        foreach (var hint in _config.App.ProcessNameHints.Where(h => !string.IsNullOrWhiteSpace(h)))
        {
            var matches = Process.GetProcesses()
                .Where(process =>
                {
                    try
                    {
                        if (IsOwnProcess(process.Id, process.ProcessName))
                        {
                            return false;
                        }

                        return process.ProcessName.Contains(hint, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .Select(process => $"{process.ProcessName}({process.Id})")
                .ToList();

            if (matches.Count > 0)
            {
                _logger.Info($"Process hint '{hint}' matched: {string.Join(", ", matches)}");
            }
        }
    }

    private void LogTopLevelWindowSnapshot(string label)
    {
        var windows = FindTopLevelWindows()
            .Select(window =>
            {
                var name = SafeRead(() => window.Current.Name) ?? "";
                var className = SafeRead(() => window.Current.ClassName) ?? "";
                var processId = SafeReadInt(() => window.Current.ProcessId);
                var processName = GetProcessName(processId);
                return $"name='{name}', class='{className}', pid={processId}, process='{processName}'";
            })
            .Where(line =>
                line.Contains("UNIMES", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("MES", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Bizentro", StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        if (windows.Count == 0)
        {
            _logger.Info($"{label}: no MES-like top-level windows found.");
            return;
        }

        _logger.Info($"{label}: {windows.Count} MES-like top-level window(s): {string.Join(" || ", windows)}");
    }

    private static bool IsShellPropertiesWindow(string name, string className, string processName)
    {
        if (!string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("?띿꽦", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Properties", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnProcess(int? processId, string processName)
    {
        if (processId == Environment.ProcessId)
        {
            return true;
        }

        return processName.Equals("UnimesAutomation", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    }

    // exe를 실행한 콘솔/터미널 호스트(WindowsTerminal·conhost 등)는 자기 자신의 실행 경로
    // (...\UnimesAutomation.exe)를 창 제목으로 표시한다. 그 제목엔 "Unimes"가 들어가 있어
    // WindowTitleContains=["UNIMES"] 매칭에 걸려 MES로 오탐되므로 후보에서 제외한다.
    private static bool IsOwnConsoleWindow(string name, string className)
    {
        if (name.Contains("UnimesAutomation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return className.Equals("CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProcessName(int? processId)
    {
        if (!processId.HasValue || processId.Value <= 0)
        {
            return "";
        }

        try
        {
            return Process.GetProcessById(processId.Value).ProcessName;
        }
        catch
        {
            return "(unavailable)";
        }
    }

    private static AutomationElement? GetContainingWindow(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = element;

        while (current is not null)
        {
            try
            {
                if (current.Current.ControlType == ControlType.Window)
                {
                    return current;
                }

                current = walker.GetParent(current);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static void BringToFront(AutomationElement? window)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            var handle = new IntPtr(window.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(handle))
            {
                ShowWindow(handle, 9); // SW_RESTORE only when minimized.
            }

            var foreground = GetForegroundWindow();
            if (foreground == handle)
            {
                return;
            }

            // 다른 세션의 MES처럼 포그라운드가 아닌 창은 SetForegroundWindow가 무시된다.
            // 현재 포그라운드 창의 입력 스레드에 잠시 붙어(AttachThreadInput) 포커스 전환을 강제한다.
            var targetThread = GetWindowThreadProcessId(handle, out _);
            var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);

            if (foregroundThread != 0 && foregroundThread != targetThread)
            {
                AttachThreadInput(foregroundThread, targetThread, true);
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
                AttachThreadInput(foregroundThread, targetThread, false);
            }
            else
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
            }

            Thread.Sleep(150); // 포커스 안정화 후 SendKeys/클릭 신뢰도 확보.
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void ClickElementCenterByMouse(AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
        {
            throw new InvalidOperationException("Cannot click element because its bounding rectangle is empty.");
        }

        var x = (int)(rect.Left + rect.Width / 2);
        var y = (int)(rect.Top + rect.Height / 2);
        Cursor.Position = new System.Drawing.Point(x, y);
        MouseClick();
    }

    private static void ClickElementCenterByMouseDouble(AutomationElement element)
    {
        ClickElementCenterByMouse(element);
        Thread.Sleep(80);
        MouseClick();
    }

    private static string EscapeForSendKeys(string text)
    {
        var special = new HashSet<char> { '+', '^', '%', '~', '(', ')', '{', '}', '[', ']' };
        var escaped = new System.Text.StringBuilder();

        foreach (var ch in text)
        {
            if (special.Contains(ch))
            {
                escaped.Append('{').Append(ch).Append('}');
            }
            else
            {
                escaped.Append(ch);
            }
        }

        return escaped.ToString();
    }

    private static T? SafeRead<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }

    private static int? SafeReadInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static System.Windows.Rect? SafeReadRect(Func<System.Windows.Rect> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr GetNativeHandle(AutomationElement element)
    {
        var handle = SafeReadInt(() => element.Current.NativeWindowHandle);
        return handle.HasValue && handle.Value != 0 ? new IntPtr(handle.Value) : IntPtr.Zero;
    }

    private static AutomationElement? TryFromHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return AutomationElement.FromHandle(handle);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNativeWindowAlive(IntPtr handle)
    {
        return handle != IntPtr.Zero && IsWindow(handle) && IsWindowVisible(handle);
    }

    private static async Task<bool> WaitForNativeWindowClosedAsync(IntPtr handle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsNativeWindowAlive(handle))
            {
                return true;
            }

            await Task.Delay(80);
        }

        return !IsNativeWindowAlive(handle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private static void MouseClick()
    {
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTDOWN
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTUP
    }
}
