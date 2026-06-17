using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class UnimesApp
{
    private readonly RootConfig _config;
    private readonly RuntimePaths _paths;
    private readonly SimpleLogger _logger;
    private readonly ScreenshotService _screenshots;
    private readonly SafetyGuard _safety;

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

    public async Task<int> RunAsync(CommandLineOptions options)
    {
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
        if (IsLoginScreen(window))
        {
            _logger.Info("Login screen detected.");
            await HandleLoginScreenAsync(window);
            loginPerformed = true;
        }
        else
        {
            _logger.Info("Login screen was not detected. Assuming UNIMES is already logged in or still loading.");
        }

        if (loginPerformed)
        {
            // 로그인을 수행한 경우 Continue 팝업이 수 초~10초 뒤 늦게 뜰 수 있어 길게 대기한다.
            await HandleContinuePopupsAsync(TimeSpan.FromSeconds(_config.App.PopupTimeoutSeconds));
        }
        else
        {
            _logger.Info("Login was not performed by automation. Skipping pre-main Continue popup scan.");
        }

        var mainWindow = await WaitForMainWindowAsync(TimeSpan.FromSeconds(_config.App.LoginTimeoutSeconds), loginPerformed);
        if (mainWindow is null)
        {
            _screenshots.CaptureDesktop("main_window_timeout");
            throw new TimeoutException("Main UNIMES window was not detected after login wait.");
        }

        LogWindowIdentity(mainWindow, "Main UNIMES window");
        _screenshots.CaptureElement(mainWindow, "main_window");

        if (loginPerformed)
        {
            await HandleContinuePopupsAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            _logger.Info("Skipping post-main Continue popup scan.");
        }

        UiDump.DumpToFile(mainWindow, _paths.UiDumpPath, _config.App.UiDumpMaxDepth, _logger);

        if (_config.Workflow.Enabled)
        {
            await RunItemInfoWorkflowAsync(mainWindow);
        }

        _logger.Info("Bootstrap completed.");
        return 0;
    }

    private async Task RunItemInfoWorkflowAsync(AutomationElement mainWindow)
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
            return;
        }

        _logger.Info($"품목정보관리 workflow started. count={requests.Count}, dryRun={_config.Safety.DryRun}, saveEnabled={_config.Safety.SaveEnabled}");
        _logger.Info("처리 예정 Part 목록:");
        for (var index = 0; index < requests.Count; index++)
        {
            var partNo = requests[index].PartNo;
            var cls = PartClassifier.Classify(partNo);
            var warehouse = cls switch
            {
                PartClass.Module => _config.ItemInfo.ModuleDefectWarehouse,
                PartClass.Comp => _config.ItemInfo.CompDefectWarehouse,
                _ => "(분류 실패 → 미존재 여부만 확인)"
            };
            _logger.Info($"  [{index + 1}/{requests.Count}] {partNo} → class={cls}, 불량창고={warehouse}");
        }

        await NavigateToMenuByF3Async(mainWindow, _config.ItemInfo.MenuName);

        var results = new List<PartResult>();
        foreach (var request in requests)
        {
            var classification = PartClassifier.Classify(request.PartNo);
            var defectWarehouse = classification switch
            {
                PartClass.Module => _config.ItemInfo.ModuleDefectWarehouse,
                PartClass.Comp => _config.ItemInfo.CompDefectWarehouse,
                _ => ""
            };

            var result = new PartResult
            {
                PartNo = request.PartNo,
                Classification = classification.ToString(),
                BinManage = _config.ItemInfo.BinManage,
                TurnKey = _config.ItemInfo.TurnKey,
                AssemblyIn = _config.ItemInfo.AssemblyIn,
                DefectWarehouse = defectWarehouse,
                Saved = "NO"
            };

            try
            {
                _logger.Info($"품목정보관리 part started. part='{request.PartNo}', class={classification}");
                BringToFront(mainWindow);

                var itemInfoWindow = FindItemInfoWindow(mainWindow) ?? mainWindow;
                var partNameEdit = FindEditNextToLabel(itemInfoWindow, "품목명");
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
                // 품목명은 팝업 조회 필드라 값 입력 후 commit(Tab)+검증 대기 후 조회해야
                // 그리드가 새 Part로 갱신된다. commit 전에 조회하면 직전 Part 결과가 남는다.
                CommitField();
                await Task.Delay(200);

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

                ClickSearch(mainWindow);
                _logger.Info($"품목정보관리 조회 실행. part='{request.PartNo}'");
                await Task.Delay(_config.Workflow.SearchDelayMilliseconds);

                var pid = PartClassifier.ExtractPid(request.PartNo);
                itemInfoWindow = FindItemInfoWindow(mainWindow) ?? itemInfoWindow;
                _logger.Info($"품목정보관리 그리드 행 탐색 시작. part='{request.PartNo}', pid='{pid}'");
                var row = FindGridRowByProductId(itemInfoWindow, pid);
                if (row is null)
                {
                    _logger.Warn($"품목정보관리 그리드 행 1차 미발견. 1초 후 재시도. part='{request.PartNo}', pid='{pid}'");
                    await Task.Delay(1000);
                    itemInfoWindow = FindItemInfoWindow(mainWindow) ?? itemInfoWindow;
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
                if (classification == PartClass.Unknown)
                {
                    _logger.Warn($"Part exists or returned a row, but classification failed. Skipping value changes. part='{request.PartNo}'");
                    _screenshots.CaptureElement(itemInfoWindow, $"classification_failed_{request.PartNo}");
                    result.Status = "SKIPPED";
                    result.Saved = "NO";
                    result.Message = "Part exists, but prefix is neither Module(RM/TM/BM/CM) nor Comp(RC/TC/BC/CC).";
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
                    _logger.Info($"품목정보관리 no change. part='{request.PartNo}', pid='{pid}', class={classification}");
                }
                else if (wouldCount > 0)
                {
                    result.Status = "DRYRUN";
                    result.Saved = "NO";
                    result.Message = "변경 예정(저장 안 함): " + string.Join(", ", detail);
                    _logger.Info($"품목정보관리 dryRun. part='{request.PartNo}', {result.Message}");
                }
                else if (SaveItemInfo(mainWindow))
                {
                    await Task.Delay(800);
                    _screenshots.CaptureElement(mainWindow, $"item_info_after_save_{request.PartNo}");
                    result.Status = "OK";
                    result.Saved = "YES";
                    result.Message = "변경 저장: " + string.Join(", ", detail);
                    _logger.Info($"품목정보관리 saved. part='{request.PartNo}', {result.Message}");
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

        var outputPath = CsvFiles.WriteResults(_paths.OutputDirectory, _paths.Timestamp, results);
        _logger.Info($"품목정보관리 result CSV saved: {outputPath}");

        ShowCompletionDialog(results, outputPath);
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

    private void ShowCompletionDialog(IReadOnlyList<PartResult> results, string outputPath)
    {
        if (!_config.Workflow.ShowCompletionDialog)
        {
            return;
        }

        var saved = results.Count(r => string.Equals(r.Saved, "YES", StringComparison.Ordinal));
        var unchanged = results.Count(r => string.Equals(r.Saved, "UNCHANGED", StringComparison.Ordinal));
        var dryRun = results.Count(r => string.Equals(r.Status, "DRYRUN", StringComparison.Ordinal));
        var skipped = results.Count(r => string.Equals(r.Status, "SKIPPED", StringComparison.Ordinal));
        var errors = results.Count(r => string.Equals(r.Status, "ERROR", StringComparison.Ordinal));

        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"작업 완료 (총 {results.Count}건)");
        builder.AppendLine();
        builder.AppendLine($"저장: {saved}    변경없음: {unchanged}    변경예정(dryRun): {dryRun}");
        builder.AppendLine($"건너뜀: {skipped}    오류: {errors}");

        var problems = results.Where(r => r.Status is "ERROR" or "SKIPPED").ToList();
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
        builder.AppendLine($"결과 CSV: {outputPath}");

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

    private async Task NavigateToMenuByF3Async(AutomationElement mainWindow, string menuName)
    {
        if (FindFirstByNameContains(mainWindow, menuName) is not null)
        {
            _logger.Info($"{menuName} screen already detected. F3 navigation skipped.");
            return;
        }

        _logger.Info($"Navigating to '{menuName}' via F3 menu search.");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            BringToFront(mainWindow);
            await Task.Delay(250);

            var menuSearchButton = FindButtonByAutomationIdContains(mainWindow, "Tool : GoSearch")
                ?? FindButtonByAnyName(mainWindow, ["메뉴찾기"]);
            if (menuSearchButton is not null)
            {
                ClickElement(menuSearchButton, "menu search");
                _logger.Info($"메뉴찾기 버튼 클릭. attempt={attempt}");
            }
            else
            {
                SendKeys.SendWait("{F3}");
                _logger.Info($"F3 메뉴찾기 입력. attempt={attempt}");
            }

            await Task.Delay(700);
            SendKeys.SendWait("^a");
            SendKeys.SendWait(EscapeForSendKeys(menuName));
            await Task.Delay(350);
            SendKeys.SendWait("{ENTER}");

            if (await WaitForMenuScreenAsync(mainWindow, menuName, TimeSpan.FromSeconds(4)))
            {
                _logger.Info($"{menuName} screen confirmed.");
                return;
            }

            _logger.Warn($"{menuName} screen was not confirmed after menu search attempt {attempt}.");
            _screenshots.CaptureElement(mainWindow, $"menu_f3_not_confirmed_attempt_{attempt}");
            SendKeys.SendWait("{ESC}");
            await Task.Delay(500);
        }

        if (FindFirstByNameContains(mainWindow, menuName) is null)
        {
            throw new InvalidOperationException($"{menuName} 화면 진입을 확인하지 못했습니다. Home Page 등 다른 화면에서 입력하지 않도록 중단합니다.");
        }

        _logger.Info($"{menuName} screen confirmed.");
    }

    private async Task<bool> WaitForMenuScreenAsync(AutomationElement mainWindow, string menuName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FindFirstByNameContains(mainWindow, menuName) is not null)
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static string MakeSafeToken(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
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

    private AutomationElement? FindItemInfoWindow(AutomationElement mainWindow)
    {
        return FindDescendants(mainWindow, ControlType.Window)
            .Where(window => string.Equals(
                SafeRead(() => window.Current.Name) ?? "",
                _config.ItemInfo.MenuName,
                StringComparison.Ordinal))
            .Where(window =>
            {
                var rect = SafeReadRect(() => window.Current.BoundingRectangle);
                return rect.HasValue && !rect.Value.IsEmpty;
            })
            .LastOrDefault();
    }

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
            _logger.Info($"Cell set via list item. column='{columnName}', '{current}'->'{targetValue}'");
            return CellAction.Changed;
        }

        if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern) &&
            rawPattern is ValuePattern valuePattern && !valuePattern.Current.IsReadOnly)
        {
            valuePattern.SetValue(targetValue);
            _logger.Info($"Cell set via ValuePattern. column='{columnName}', '{current}'->'{targetValue}'");
            return CellAction.Changed;
        }

        throw new InvalidOperationException($"Failed to set grid cell. column='{columnName}', target='{targetValue}'");
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
            _logger.Warn($"Combo expand failed: {ex.Message}");
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
            _logger.Warn($"SelectionItemPattern.Select failed: {ex.Message}");
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
            _logger.Warn($"InvokePattern.Invoke failed on list item: {ex.Message}");
        }

        return false;
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
    private void ClickSearch(AutomationElement mainWindow)
    {
        // 팝업에도 '조회' 버튼이 있으므로 메인 툴바 Query automation id를 먼저 사용한다.
        var searchButton = FindButtonByAutomationIdContains(mainWindow, "Tool : Query")
            ?? FindButtonByAnyName(mainWindow, ["조회", "Search", "Find"]);
        if (searchButton is not null)
        {
            ClickElement(searchButton, "search query");
            return;
        }

        _logger.Warn("Search button was not found by name/id. 좌표 기반 fallback 사용: toolbar search icon.");
        ClickToolbarSearchFallback(mainWindow);
    }

    private async Task<bool> HandleOpenPartIdPopupAsync(string originalPart)
    {
        var popup = await WaitForPartIdPopupAsync(TimeSpan.FromMilliseconds(1200));
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

            await Task.Delay(80);
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

            await Task.Delay(100);
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
                await Task.Delay(80);
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
            await Task.Delay(300);
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
                    await Task.Delay(500);
                    return;
                }
            }
        }

        _logger.Warn($"미존재 경고창을 UIA로 찾지 못해 Enter fallback으로 확인 처리합니다. part='{originalPart}'");
        try
        {
            SendKeys.SendWait("{ENTER}");
            _logger.Info("미존재 경고창 Enter fallback 전송 완료.");
            await Task.Delay(600);
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
        await Task.Delay(300);
        _logger.Info($"고객사PartID 팝업 [취소] 처리. part='{originalPart}'");
    }

    private async Task RecoverPartIdPopupByKeyboardAsync(string originalPart)
    {
        var recoveryPart = _config.ItemInfo.RecoveryPart;
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
        await Task.Delay(300);

        _logger.Info($"기파트 복구 조회 Enter 전송. recovery='{recoveryPart}'");
        SendKeys.SendWait("{ENTER}");
        await WaitForPartIdPopupResultAsync(recoveryPart, TimeSpan.FromMilliseconds(2000));
        await Task.Delay(200);
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

            await Task.Delay(100);
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

            await Task.Delay(100);
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
        await Task.Delay(150);

        var ok = FindByAutomationId(popup, "3542176") ?? FindButtonByAnyName(popup, ["확인", "OK"]);
        if (ok is null)
        {
            throw new InvalidOperationException("고객사PartID 팝업의 확인 버튼을 찾지 못했습니다.");
        }

        ClickElement(ok, "고객사PartID popup confirm");
        await Task.Delay(300);
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

    // 미존재 경고('존재하지' 텍스트 + [확인])를 가진 창을 찾는다. 별도 모달이든 메인 자식이든
    // 내용 기준으로 잡는다(이름으로 제외하지 않음 — 경고창 제목이 비거나 UNIMES일 수 있어서).
    private AutomationElement? FindWarningDialog()
    {
        foreach (var window in FindTopLevelWindows())
        {
            if (IsMissingPartWarningWindow(window))
            {
                return window;
            }

            foreach (var childWindow in FindDescendants(window, ControlType.Window))
            {
                if (IsMissingPartWarningWindow(childWindow))
                {
                    return childWindow;
                }
            }
        }

        return null;
    }

    private bool IsMissingPartWarningWindow(AutomationElement window)
    {
        return FindFirstByNameContains(window, "존재하지") is not null &&
               FindButtonByAnyName(window, ["확인", "OK"]) is not null;
    }

    private AutomationElement? FindByAutomationId(AutomationElement root, string automationId)
    {
        try
        {
            var found = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (found is not null)
            {
                return found;
            }
        }
        catch
        {
            // 사라지는 요소에서 예외 가능 — 아래 fallback.
        }

        return FindDescendants(root, null)
            .FirstOrDefault(element =>
                string.Equals(SafeRead(() => element.Current.AutomationId) ?? "", automationId, StringComparison.Ordinal));
    }

    private static bool IsWritableValueControl(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern) &&
                   rawPattern is ValuePattern valuePattern &&
                   !valuePattern.Current.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private static double CenterY(System.Windows.Rect rect) => rect.Top + rect.Height / 2.0;

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
        await Task.Delay(500);

        var edits = FindDescendants(loginWindow, ControlType.Edit).ToList();
        if (edits.Count == 0)
        {
            _logger.Warn("No Edit controls found on login screen. UIA may not expose login fields.");
            _screenshots.CaptureElement(loginWindow, "login_no_edit_controls");
            return;
        }

        _logger.Info($"Login Edit controls found: {edits.Count}");

        if (!string.IsNullOrWhiteSpace(_config.Login.UserId))
        {
            SetElementText(edits[0], _config.Login.UserId, "login user id");
            _logger.Info("Login user id filled.");
        }

        if (_config.Login.UseConfigPassword)
        {
            if (string.IsNullOrEmpty(_config.Login.Password))
            {
                throw new InvalidOperationException("passwordMode=config but password is empty.");
            }

            if (edits.Count < 2)
            {
                throw new InvalidOperationException("Password edit control was not found.");
            }

            SetElementText(edits[1], _config.Login.Password, "login password");
            _logger.Info("Login password filled from local config. Password value is not logged.");

            var loginButton = FindButtonByAnyName(loginWindow, ["?뺤씤", "Login", "OK"]);
            if (loginButton is null)
            {
                _screenshots.CaptureElement(loginWindow, "login_button_not_found");
                throw new InvalidOperationException("Login button was not found.");
            }

            ClickElement(loginButton, "login submit");
            _logger.Info("Login button clicked.");
        }
        else
        {
            if (edits.Count >= 2)
            {
                TryFocus(edits[1], "password field");
            }

            _screenshots.CaptureElement(loginWindow, "login_waiting_for_manual_password");
            _logger.Info("Manual password mode. Enter password and submit login in UNIMES. Waiting for main window.");
        }
    }

    private async Task HandleContinuePopupsAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var clickedAny = false;
        _logger.Info($"Checking Continue popup for up to {timeout.TotalSeconds:0.#}s.");

        while (DateTime.UtcNow < deadline)
        {
            var button = FindContinueButton();
            if (button is not null)
            {
                _screenshots.CaptureDesktop("continue_popup_before_click");
                ClickElement(button, "post-login Continue popup");
                _logger.Info("Continue popup clicked.");
                clickedAny = true;
                await Task.Delay(1000);
                continue;
            }

            if (clickedAny)
            {
                return;
            }

            await Task.Delay(500);
        }

        _logger.Info("No Continue popup detected in wait window.");
    }

    private async Task<AutomationElement?> WaitForUnimesWindowAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
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

            await Task.Delay(500);
        }

        return null;
    }

    private async Task<AutomationElement?> WaitForMainWindowAsync(TimeSpan timeout, bool handleContinuePopups)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (handleContinuePopups)
            {
                await HandleContinuePopupsAsync(TimeSpan.FromMilliseconds(500));
            }

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

            await Task.Delay(1000);
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

    private AutomationElement? FindTopLevelButtonByAnyName(IReadOnlyCollection<string> names)
    {
        foreach (var window in FindTopLevelWindows())
        {
            var button = FindButtonByAnyName(window, names);
            if (button is not null)
            {
                return button;
            }
        }

        return null;
    }

    private AutomationElement? FindContinueButton()
    {
        foreach (var window in FindTopLevelWindows().Where(IsContinuePopupCandidate))
        {
            var button = FindButtonByAnyName(window, ["Continue"]);
            if (button is not null)
            {
                return button;
            }
        }

        return null;
    }

    private bool IsContinuePopupCandidate(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        var className = SafeRead(() => window.Current.ClassName) ?? "";
        var processId = SafeReadInt(() => window.Current.ProcessId);
        var processName = GetProcessName(processId);
        var rect = SafeReadRect(() => window.Current.BoundingRectangle);

        if (IsOwnProcess(processId, processName))
        {
            return false;
        }

        if (_config.App.WindowTitleExcludes.Any(token =>
                !string.IsNullOrWhiteSpace(token) &&
                name.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (name.Contains("Continue", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!processName.StartsWith("Bizentro.App.MAIN.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (name.Contains("UNIMES - ", StringComparison.OrdinalIgnoreCase) &&
            className.Contains("Window", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rect is null)
        {
            return false;
        }

        return rect.Value.Width is > 0 and <= 700 && rect.Value.Height is > 0 and <= 500;
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

        _logger.Warn($"InvokePattern unavailable. 좌표 기반 fallback 사용. reason='{reason}'");
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
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private static void MouseClick()
    {
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTDOWN
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTUP
    }
}
