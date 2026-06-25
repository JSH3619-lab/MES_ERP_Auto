using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed partial class UnimesApp
{
    private async Task<List<BinResult>> RunBinInfoWorkflowAsync(
        AutomationElement mainWindow,
        IReadOnlyList<PartRequest> requests)
    {
        if (requests.Count == 0)
        {
            _logger.Info("BIN 정보 관리 대상 Part 없음. 건너뜀.");
            return [];
        }

        _logger.Info($"품목별 BIN 정보 관리 workflow started. count={requests.Count}, dryRun={_config.Safety.DryRun}, saveEnabled={_config.Safety.SaveEnabled}");
        await NavigateToMenuByF3Async(mainWindow, _config.Global.BinInfoMenuName);
        BringToFront(mainWindow);
        ThrowIfCancellationRequested("BIN 정보 관리 화면 준비");

        var results = new List<BinResult>();
        var useProductLookup = _config.Workflow.RuntimeWorkScope == WorkScope.BinInfo;
        var binControlsStopwatch = Stopwatch.StartNew();
        AutomationElement binWindow = FindNamedWindow(mainWindow, _config.Global.BinInfoMenuName) ?? mainWindow;
        AutomationElement? partIdEdit = FindBinPartIdEdit(binWindow);
        _logger.Info($"BIN stable controls cached. partIdEdit={partIdEdit is not null}, productLookup={useProductLookup}, elapsed={binControlsStopwatch.Elapsed.TotalSeconds:0.000}s");

        foreach (var request in requests)
        {
            ThrowIfCancellationRequested("BIN 정보 관리 남은 Part 처리");

            var resultRecorded = false;
            var cls = PartClassifier.Classify(request.PartNo);
            void RecordResult(BinInfoRowTarget? rowTarget, string status, string saved, string message)
            {
                var rowConfig = rowTarget?.Row;
                results.Add(new BinResult
                {
                    PartNo = request.PartNo,
                    Classification = cls.ToString(),
                    ProcessName = rowTarget?.ProcessSearchKey ?? "",
                    BinType = rowConfig?.BinType ?? "",
                    RetestNo = rowConfig?.RetestNo ?? "",
                    BinComplete = rowConfig?.BinComplete ?? "",
                    RetestTh = rowConfig?.RetestTh ?? "",
                    BinId = rowTarget?.BinIdName ?? "",
                    Saved = saved,
                    Status = status,
                    Message = message,
                    ProcessedAt = DateTime.Now
                });
                resultRecorded = true;
            }

            try
            {
                _logger.Info($"BIN part started. part='{request.PartNo}'");
                BringToFront(mainWindow);
                ThrowIfCancellationRequested("BIN Part 처리 시작");

                var target = BinIdResolver.Resolve(request.PartNo, _config);
                if (target is null)
                {
                    _logger.Warn($"BIN 분류/용량 파싱 실패로 건너뜀. part='{request.PartNo}'");
                    RecordResult(null, "SKIPPED", "NO", "BIN 분류/용량 파싱 실패");
                    continue;
                }

                if (!IsElementUsable(binWindow))
                {
                    binWindow = FindNamedWindow(mainWindow, _config.Global.BinInfoMenuName) ?? mainWindow;
                }

                if (!IsElementUsable(partIdEdit))
                {
                    partIdEdit = FindBinPartIdEdit(binWindow);
                }

                if (partIdEdit is null)
                {
                    _logger.Error($"BIN 품목 ID 입력칸 미발견. part='{request.PartNo}'");
                    _screenshots.CaptureElement(binWindow, $"bin_part_id_not_found_{MakeSafeToken(request.PartNo)}");
                    RecordResult(null, "ERROR", "NO", "BIN 품목 ID 입력칸 미발견");
                    continue;
                }

                if (useProductLookup)
                {
                    if (!await SelectBinProductFromLookupAsync(binWindow, partIdEdit, request.PartNo))
                    {
                        _logger.Warn($"BIN 품목 코드 미존재로 건너뜀. part='{request.PartNo}'");
                        RecordResult(null, "SKIPPED", "NO", "BIN 품목 코드 미존재");
                        continue;
                    }
                }
                else
                {
                    SetElementText(partIdEdit, request.PartNo, "BIN 품목 ID");
                }

                if (!IsElementUsable(partIdEdit))
                {
                    partIdEdit = FindBinPartIdEdit(binWindow);
                }

                if (partIdEdit is not null)
                {
                    SendEnter(partIdEdit, "BIN 품목 ID 조회");
                }
                else
                {
                    _logger.Warn("BIN 품목 ID 입력칸 재탐색 실패. 조회 버튼 fallback 사용.");
                    ClickToolbarSearchFallback(mainWindow);
                }

                _logger.Info($"BIN 조회 실행. part='{request.PartNo}'");
                var binQueryStopwatch = Stopwatch.StartNew();
                await WaitForBinQuerySettledAsync(binWindow, request.PartNo, TimeSpan.FromMilliseconds(_config.Workflow.SearchDelayMilliseconds));
                ThrowIfCancellationRequested("BIN 조회 대기");
                _logger.Info($"BIN 조회 대기 완료. part='{request.PartNo}', elapsed={binQueryStopwatch.Elapsed.TotalSeconds:0.000}s");

                // 조회 결과가 없으면 '[900014]검색된 Data가 없습니다.' 모달이 뜬다.
                // 모달이 떠 있는 동안 행 스캔/행추가가 막히므로, 뜨면 Enter로 먼저 닫는다.
                await ConfirmNoDataPopupAsync(request.PartNo, TimeSpan.FromMilliseconds(2000));

                if (!IsElementUsable(binWindow))
                {
                    binWindow = FindNamedWindow(mainWindow, _config.Global.BinInfoMenuName) ?? mainWindow;
                }

                var existingRowsStopwatch = Stopwatch.StartNew();
                var existingRows = FindBinRowsForPart(binWindow, request.PartNo).Count;
                _logger.Info($"BIN 기존 행 탐색 완료. part='{request.PartNo}', rows={existingRows}, elapsed={existingRowsStopwatch.Elapsed.TotalSeconds:0.000}s, queryElapsed={binQueryStopwatch.Elapsed.TotalSeconds:0.000}s");
                var existingTargetRows = Math.Min(existingRows, target.Rows.Count);
                for (var i = 0; i < existingTargetRows; i++)
                {
                    RecordResult(target.Rows[i], "OK", "UNCHANGED", $"기존 BIN 등록 행 {existingRows}건 발견");
                }

                if (existingRows >= target.Rows.Count)
                {
                    _logger.Info($"BIN 기존 등록 행 충분. 신규 행추가 없이 건너뜀. part='{request.PartNo}', rows={existingRows}, targetRows={target.Rows.Count}, elapsed={binQueryStopwatch.Elapsed.TotalSeconds:0.000}s");
                    continue;
                }

                if (!IsElementUsable(binWindow))
                {
                    binWindow = FindNamedWindow(mainWindow, _config.Global.BinInfoMenuName) ?? mainWindow;
                }

                var stopPart = false;
                for (var targetIndex = existingRows; targetIndex < target.Rows.Count; targetIndex++)
                {
                    var rowTarget = target.Rows[targetIndex];
                    var rowNo = targetIndex + 1;
                    _logger.Info($"BIN 신규 행 처리 시작. part='{request.PartNo}', row={rowNo}/{target.Rows.Count}, process='{rowTarget.ProcessSearchKey}', binId='{rowTarget.BinIdName}'");

                    if (!IsElementUsable(binWindow))
                    {
                        binWindow = FindNamedWindow(mainWindow, _config.Global.BinInfoMenuName) ?? mainWindow;
                    }

                    var row = await InsertBinRowAsync(mainWindow, binWindow);
                    if (row is null)
                    {
                        _logger.Error($"BIN 추가 행 미발견. part='{request.PartNo}', row={rowNo}");
                        _screenshots.CaptureElement(binWindow, $"bin_new_row_not_found_{MakeSafeToken(request.PartNo)}_{rowNo}");
                        RecordResult(rowTarget, "ERROR", "NO", "BIN 추가 행 미발견");
                        stopPart = true;
                        break;
                    }

                    if (!await SelectProcessPopupAsync(row, rowTarget.ProcessSearchKey))
                    {
                        _logger.Error($"BIN 공정명 입력 실패. part='{request.PartNo}', key='{rowTarget.ProcessSearchKey}', row={rowNo}");
                        RecordResult(rowTarget, "ERROR", "NO", $"BIN 공정명 입력 실패. key='{rowTarget.ProcessSearchKey}'");
                        stopPart = true;
                        break;
                    }

                    if (!IsElementUsable(row))
                    {
                        row = FindNewBinRow(binWindow);
                    }

                    if (row is null)
                    {
                        _logger.Error($"BIN 공정명 선택 후 행 재탐색 실패. part='{request.PartNo}', row={rowNo}");
                        RecordResult(rowTarget, "ERROR", "NO", "BIN 공정명 선택 후 행 재탐색 실패");
                        stopPart = true;
                        break;
                    }

                    FillFixedBinCells(row, rowTarget.Row);

                    if (!await SelectBinIdPopupExactAsync(row, rowTarget.BinIdName))
                    {
                        _logger.Warn($"BIN ID 미설정으로 저장 건너뜀. part='{request.PartNo}', binId='{rowTarget.BinIdName}', row={rowNo}");
                        RecordResult(rowTarget, "ERROR", "NO", $"BIN ID 미설정. binId='{rowTarget.BinIdName}'");
                        stopPart = true;
                        break;
                    }

                    if (SaveItemInfo(mainWindow))
                    {
                        await DelayAsync(300);
                        if (await ConfirmBinSaveValidationPopupAsync(request.PartNo))
                        {
                            _logger.Error($"BIN 저장 검증 경고로 저장 실패 처리. part='{request.PartNo}', row={rowNo}");
                            RecordResult(rowTarget, "ERROR", "NO", "BIN 저장 검증 경고 발생");
                            stopPart = true;
                            break;
                        }

                        _screenshots.CaptureElement(binWindow, $"bin_after_save_{MakeSafeToken(request.PartNo)}_{rowNo}");
                        _logger.Info($"BIN saved. part='{request.PartNo}', binId='{rowTarget.BinIdName}', row={rowNo}, elapsed={binQueryStopwatch.Elapsed.TotalSeconds:0.000}s");
                        RecordResult(rowTarget, "OK", "YES", $"BIN 저장 완료. binId='{rowTarget.BinIdName}'");
                    }
                    else
                    {
                        _logger.Warn($"BIN 저장 게이트로 저장 생략. part='{request.PartNo}', binId='{rowTarget.BinIdName}', row={rowNo}");
                        RecordResult(rowTarget, "DRYRUN", "NO", $"BIN 저장 게이트로 저장 생략. binId='{rowTarget.BinIdName}'");
                    }
                }

                if (stopPart)
                {
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"BIN 처리 실패. part='{request.PartNo}'");
                _screenshots.CaptureElement(binWindow, $"bin_exception_{MakeSafeToken(request.PartNo)}");
                if (!resultRecorded)
                {
                    RecordResult(null, "ERROR", "NO", ex.Message);
                }

                if (_config.Workflow.StopOnFirstFailure)
                {
                    break;
                }
            }
        }

        _logger.Info("품목별 BIN 정보 관리 workflow finished.");
        return results;
    }

    private AutomationElement? FindBinPartIdEdit(AutomationElement binWindow)
    {
        return FindByAutomationId(binWindow, "2953814")
               ?? FindEditNextToLabel(binWindow, "품목 ID");
    }

    private async Task<bool> SelectBinProductFromLookupAsync(AutomationElement binWindow, AutomationElement partIdEdit, string partNo)
    {
        var popup = FindPopupWindow(IsBinProductLookupPopupWindow);
        if (popup is null && !OpenBinProductLookupPopup(binWindow, partIdEdit))
        {
            _logger.Warn($"BIN 품목 코드 검색 팝업을 열지 못했습니다. part='{partNo}'");
            return false;
        }

        // 팝업 창은 떠도 내부 '품목 코드' 라벨이 늦게 렌더돼 단발 확인이 실패할 수 있다.
        // 라벨까지 보일 때까지 폴링한다.
        popup = await WaitForPopupWindowAsync(IsBinProductLookupPopupWindow, TimeSpan.FromMilliseconds(2000));
        if (popup is null)
        {
            _logger.Warn($"BIN 품목 코드 검색 팝업 미감지. part='{partNo}'");
            return false;
        }

        var input = FindBinProductLookupCodeEdit(popup);
        if (input is null)
        {
            _logger.Warn($"BIN 품목 코드 검색 입력칸 미발견. part='{partNo}'");
            return false;
        }

        var popupHandle = GetNativeHandle(popup);

        SetElementText(input, partNo, "BIN 품목 코드 검색키");
        TryFocus(input, "BIN 품목 코드 검색키");
        await DelayAsync(100);
        SendKeys.SendWait("{ENTER}");
        _logger.Info($"BIN 품목 코드 팝업 조회 Enter 전송. part='{partNo}'");

        var searchState = await WaitForBinProductLookupSearchResultAsync(popup, partNo, TimeSpan.FromMilliseconds(1800));
        if (searchState == BinProductLookupSearchState.Missing)
        {
            _logger.Warn($"BIN 품목 코드 미존재. 검색 팝업 유지 후 다음 Part 진행. part='{partNo}'");
            return false;
        }

        if (searchState != BinProductLookupSearchState.Found)
        {
            _logger.Warn($"BIN 품목 코드 검색 결과 없음. 검색 팝업 유지 후 다음 Part 진행. part='{partNo}'");
            return false;
        }

        SendKeys.SendWait("{ENTER}");
        _logger.Info($"BIN 품목 코드 팝업 확인 Enter 전송. part='{partNo}'");
        await DelayAsync(300);

        if (await TryDismissBinProductMissingWarningAsync(popup, partNo))
        {
            _logger.Warn($"BIN 품목 코드 미존재. 검색 팝업 유지 후 다음 Part 진행. part='{partNo}'");
            return false;
        }

        if (popupHandle != IntPtr.Zero)
        {
            if (!await WaitForNativeWindowClosedAsync(popupHandle, TimeSpan.FromMilliseconds(1200)))
            {
                _logger.Warn($"BIN 품목 코드 검색 결과 없음. 검색 팝업 유지 후 다음 Part 진행. part='{partNo}'");
                return false;
            }
        }
        else
        {
            popup = FindPopupWindow(IsBinProductLookupPopupWindow);
            if (popup is not null)
            {
                _logger.Warn($"BIN 품목 코드 검색 결과 없음. 검색 팝업 유지 후 다음 Part 진행. part='{partNo}'");
                return false;
            }
        }

        _logger.Info($"BIN 품목 코드 검색 선택 완료. part='{partNo}'");
        return true;
    }

    private enum BinProductLookupSearchState
    {
        None,
        Found,
        Missing
    }

    private AutomationElement? FindBinProductLookupCodeEdit(AutomationElement popup)
    {
        return FindEditNextToLabel(popup, "품목 코드")
               ?? FindDescendants(popup, ControlType.Edit)
                   .Select(edit => new
                   {
                       Edit = edit,
                       Rect = SafeReadRect(() => edit.Current.BoundingRectangle)
                   })
                   .Where(x => x.Rect.HasValue && !x.Rect.Value.IsEmpty)
                   .OrderBy(x => x.Rect!.Value.Top)
                   .ThenBy(x => x.Rect!.Value.Left)
                   .FirstOrDefault()?.Edit;
    }

    private bool OpenBinProductLookupPopup(AutomationElement binWindow, AutomationElement partIdEdit)
    {
        // 검색 팝업은 열릴 때 품목 ID 칸에 남아 있는 값을 먼저 자동 조회한다.
        // 이전 Part/실행에서 남은 값이 조회돼 엉뚱한 미존재 경고가 뜨지 않도록 열기 전에 비운다.
        SetElementText(partIdEdit, "", "BIN 품목 ID 초기화");

        TryFocus(partIdEdit, "BIN 품목 ID");

        var button = FindBinProductLookupButton(binWindow, partIdEdit);
        if (button is not null)
        {
            ClickElementCenterByMouse(button);
            Thread.Sleep(250);
            _logger.Info("BIN 품목 코드 검색 버튼 클릭.");
            return true;
        }

        var rect = SafeReadRect(() => partIdEdit.Current.BoundingRectangle);
        if (!rect.HasValue || rect.Value.IsEmpty)
        {
            return false;
        }

        Cursor.Position = new System.Drawing.Point((int)(rect.Value.Right + 10), (int)(rect.Value.Top + rect.Value.Height / 2));
        MouseClick();
        Thread.Sleep(250);
        _logger.Info("BIN 품목 코드 검색 버튼 좌표 fallback 클릭.");
        return true;
    }

    private AutomationElement? FindBinProductLookupButton(AutomationElement binWindow, AutomationElement partIdEdit)
    {
        var editRect = SafeReadRect(() => partIdEdit.Current.BoundingRectangle);
        if (!editRect.HasValue || editRect.Value.IsEmpty)
        {
            return null;
        }

        return FindDescendants(binWindow, ControlType.Button)
            .Where(button => string.Equals(
                SafeRead(() => button.Current.AutomationId) ?? "",
                "uniButton_OpenPopup",
                StringComparison.Ordinal))
            .Select(button => new
            {
                Button = button,
                Rect = SafeReadRect(() => button.Current.BoundingRectangle)
            })
            .Where(x => x.Rect.HasValue && !x.Rect.Value.IsEmpty)
            .Where(x => Math.Abs(CenterY(x.Rect!.Value) - CenterY(editRect.Value)) <= 10)
            .Where(x => x.Rect!.Value.Left >= editRect.Value.Right - 2)
            .OrderBy(x => x.Rect!.Value.Left)
            .FirstOrDefault()?.Button;
    }

    private async Task<BinProductLookupSearchState> WaitForBinProductLookupSearchResultAsync(
        AutomationElement lookupPopup,
        string partNo,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await TryDismissBinProductMissingWarningAsync(lookupPopup, partNo))
            {
                return BinProductLookupSearchState.Missing;
            }

            if (!HasForeignSmallForegroundWindow(GetNativeHandle(lookupPopup)) &&
                HasPopupDataRows(lookupPopup))
            {
                return BinProductLookupSearchState.Found;
            }

            await DelayAsync(80);
        }

        if (await TryDismissBinProductMissingWarningAsync(lookupPopup, partNo))
        {
            return BinProductLookupSearchState.Missing;
        }

        if (HasForeignSmallForegroundWindow(GetNativeHandle(lookupPopup)))
        {
            return BinProductLookupSearchState.None;
        }

        return HasPopupDataRows(lookupPopup)
            ? BinProductLookupSearchState.Found
            : BinProductLookupSearchState.None;
    }

    // [971001] 경고는 룩업 팝업 위에 모달로 떠서 포커스를 가져간다.
    // 버튼을 UIA로 찾아 클릭하지 않고(=SafetyGuard도 안 탐), 떠 있는 걸 감지하면 Enter로 닫는다(확인이 기본 버튼).
    private async Task<bool> TryDismissBinProductMissingWarningAsync(AutomationElement lookupPopup, string partNo)
    {
        var lookupHandle = GetNativeHandle(lookupPopup);

        // 룩업 팝업 위에 뜬 다른 작은 모달 창 = [971001] 경고.
        if (!HasForeignSmallForegroundWindow(lookupHandle))
        {
            return false;
        }

        SendKeys.SendWait("{ENTER}");
        await DelayAsync(300);

        if (HasForeignSmallForegroundWindow(lookupHandle))
        {
            _logger.Warn($"BIN 품목 코드 미존재 경고 Enter 후에도 모달 잔존. part='{partNo}'");
            return false;
        }

        _logger.Warn($"BIN 품목 코드 미존재 경고 Enter 처리. part='{partNo}'");
        return true;
    }

    private bool HasPopupDataRows(AutomationElement popup)
    {
        return FindDescendants(popup, ControlType.DataItem).Any();
    }

    private bool HasForeignSmallForegroundWindow(IntPtr lookupHandle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == lookupHandle)
        {
            return false;
        }

        var foregroundWindow = TryFromHandle(foreground);
        return foregroundWindow is not null && IsSmallPopupCandidate(foregroundWindow);
    }

    private async Task<bool> HandleOpenPartIdPopupFastAsync(string originalPart)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        AutomationElement? popup = null;
        while (DateTime.UtcNow < deadline)
        {
            popup = FindPartIdPopupFast();
            if (popup is not null)
            {
                break;
            }

            await DelayAsync(50);
        }

        if (popup is null)
        {
            return false;
        }

        _logger.Warn($"BIN 품목 ID 입력 후 PartID 팝업 감지. part='{originalPart}'");
        var rows = FindDescendants(popup, ControlType.DataItem).ToList();
        if (rows.Count > 0)
        {
            var row = FindPopupRowByProductCode(popup, originalPart) ?? rows[0];
            await SelectPartIdPopupRowAsync(popup, row, originalPart);
            return false;
        }

        await DismissMissingWarningAsync(originalPart, forceEnterFallback: true);
        await RecoverPartIdPopupByKeyboardAsync(originalPart);
        return true;
    }

    private AutomationElement? FindPartIdPopupFast()
    {
        foreach (var window in FindTopLevelWindows())
        {
            if (IsPartIdPopupWindow(window))
            {
                return window;
            }

            if (!IsUnimesCandidate(window))
            {
                continue;
            }

            AutomationElementCollection children;
            try
            {
                children = window.FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement child in children)
            {
                if (IsPartIdPopupWindow(child))
                {
                    return child;
                }
            }
        }

        return null;
    }

    // 900014는 메인 ShellForm 위 owned 모달이라 ShellForm descendants를 훑으면 UIA가 오래 막힌다.
    // 메인 창은 direct child Window까지만 보고, 작은 메시지 창 후보 안에서만 텍스트를 확인한다.
    private async Task<bool> ConfirmNoDataPopupAsync(string partNo, TimeSpan timeout)
    {
        string[] tokens = ["900014", "검색된 Data", "검색된 데이터", "Data가 없습니다"];
        var warning = await WaitForOwnedMessageDialogAsync(tokens, timeout, allowAnyMessageBoxForm: true);
        if (warning is null)
        {
            return false;
        }

        var message = ReadMessageText(warning);
        var ok = FindButtonByAnyName(warning, ["확인", "OK"]);
        if (ok is not null)
        {
            ClickElement(ok, "BIN no-data warning confirm");
            _logger.Info($"BIN 900014 경고 [확인] 처리. part='{partNo}', message='{message}'");
        }
        else
        {
            SendKeys.SendWait("{ENTER}");
            _logger.Info($"BIN 900014 경고 Enter 처리. part='{partNo}', message='{message}'");
        }

        if (await WaitForOwnedMessageDialogClosedAsync(tokens, TimeSpan.FromMilliseconds(1000), allowAnyMessageBoxForm: true))
        {
            return true;
        }

        _logger.Warn($"BIN 900014 경고가 닫히지 않음. part='{partNo}'");
        return false;
    }

    private async Task<bool> ConfirmBinSaveValidationPopupAsync(string partNo)
    {
        string[] tokens = ["970029", "BIN Type", "확인하"];
        var warning = await WaitForOwnedMessageDialogAsync(tokens, TimeSpan.FromMilliseconds(1200));
        if (warning is null)
        {
            return false;
        }

        var message = ReadMessageText(warning);
        var ok = FindButtonByAnyName(warning, ["확인", "OK"]);
        if (ok is not null)
        {
            ClickElement(ok, "BIN save validation warning confirm");
        }
        else
        {
            SendKeys.SendWait("{ENTER}");
        }

        await WaitForOwnedMessageDialogClosedAsync(tokens, TimeSpan.FromMilliseconds(1000));
        _logger.Warn($"BIN 저장 검증 경고 처리. part='{partNo}', message='{message}'");
        return true;
    }

    private async Task<AutomationElement?> WaitForOwnedMessageDialogAsync(
        IReadOnlyCollection<string> tokens,
        TimeSpan timeout,
        bool allowAnyMessageBoxForm = false)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var dialog = FindOwnedMessageDialog(tokens, allowAnyMessageBoxForm);
            if (dialog is not null)
            {
                return dialog;
            }

            await DelayAsync(100);
        }

        return FindOwnedMessageDialog(tokens, allowAnyMessageBoxForm);
    }

    private async Task<bool> WaitForOwnedMessageDialogClosedAsync(
        IReadOnlyCollection<string> tokens,
        TimeSpan timeout,
        bool allowAnyMessageBoxForm = false)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FindOwnedMessageDialog(tokens, allowAnyMessageBoxForm) is null)
            {
                return true;
            }

            await DelayAsync(100);
        }

        return FindOwnedMessageDialog(tokens, allowAnyMessageBoxForm) is null;
    }

    private AutomationElement? FindOwnedMessageDialog(IReadOnlyCollection<string> tokens, bool allowAnyMessageBoxForm = false)
    {
        foreach (var window in FindTopLevelWindows())
        {
            if (!IsUnimesCandidate(window))
            {
                continue;
            }

            if (IsMainShellWindow(window))
            {
                foreach (var childWindow in FindDirectChildWindows(window))
                {
                    if (IsOwnedMessageDialogCandidate(childWindow, tokens, allowAnyMessageBoxForm))
                    {
                        return childWindow;
                    }
                }

                continue;
            }

            if (IsOwnedMessageDialogCandidate(window, tokens, allowAnyMessageBoxForm))
            {
                return window;
            }

            foreach (var childWindow in FindDirectChildWindows(window))
            {
                if (IsOwnedMessageDialogCandidate(childWindow, tokens, allowAnyMessageBoxForm))
                {
                    return childWindow;
                }
            }
        }

        return null;
    }

    private bool IsOwnedMessageDialogCandidate(
        AutomationElement window,
        IReadOnlyCollection<string> tokens,
        bool allowAnyMessageBoxForm)
    {
        if (!IsSmallPopupCandidate(window))
        {
            return false;
        }

        if (IsMesMessageDialog(window, tokens))
        {
            return true;
        }

        return allowAnyMessageBoxForm && IsMessageBoxFormWithConfirm(window);
    }

    private bool IsMessageBoxFormWithConfirm(AutomationElement window)
    {
        var automationId = SafeRead(() => window.Current.AutomationId) ?? "";
        var name = SafeRead(() => window.Current.Name) ?? "";

        return (string.Equals(automationId, "MessageBoxForm", StringComparison.Ordinal) ||
                string.Equals(name, "MessageBoxForm", StringComparison.Ordinal)) &&
               FindButtonByAnyName(window, ["확인", "OK"]) is not null;
    }

    private List<AutomationElement> FindBinRowsForPart(AutomationElement binWindow, string partNo)
    {
        return FindDescendants(binWindow, ControlType.DataItem)
            .Where(IsBinInfoRow)
            .Where(row =>
            {
                var product = FindGridCell(row, "품목ID", ControlType.Edit);
                var value = product is null ? "" : ReadValue(product).Trim();
                return string.Equals(value, partNo, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private async Task<AutomationElement?> InsertBinRowAsync(AutomationElement mainWindow, AutomationElement binWindow)
    {
        var before = CountBinInfoRows(binWindow);
        var stopwatch = Stopwatch.StartNew();

        if (TrySendBinInsertShortcut(binWindow, mainWindow))
        {
            var row = await WaitForNewBinRowAsync(binWindow, before, TimeSpan.FromMilliseconds(1200));
            if (row is not null)
            {
                _logger.Info($"BIN 행추가 Ctrl+Insert 성공. elapsed={stopwatch.Elapsed.TotalSeconds:0.000}s");
                return row;
            }

            _logger.Warn($"BIN 행추가 Ctrl+Insert 후 새 행 미감지. 버튼 fallback 사용. elapsed={stopwatch.Elapsed.TotalSeconds:0.000}s");
        }

        if (ClickInsertRow(mainWindow))
        {
            var row = await WaitForNewBinRowAsync(binWindow, before, TimeSpan.FromMilliseconds(2000));
            if (row is not null)
            {
                _logger.Info($"BIN 행추가 버튼 fallback 성공. elapsed={stopwatch.Elapsed.TotalSeconds:0.000}s");
                return row;
            }

            _logger.Warn($"BIN 행추가 버튼 fallback 후 새 행 미감지. elapsed={stopwatch.Elapsed.TotalSeconds:0.000}s");
        }

        return null;
    }

    private bool ClickInsertRow(AutomationElement mainWindow)
    {
        var insert = FindButtonByAutomationIdContains(mainWindow, "Tool : InsertRow")
            ?? FindButtonByAnyName(mainWindow, ["행추가"]);
        if (insert is not null)
        {
            _safety.EnsureCanClick(insert, "BIN insert row");
            BringToFront(GetContainingWindow(insert));
            ClickElementCenterByMouse(insert);
            _logger.Info("BIN 행추가 버튼 클릭.");
            return true;
        }

        return false;
    }

    private bool TrySendBinInsertShortcut(AutomationElement? binWindow, AutomationElement mainWindow)
    {
        try
        {
            BringToFront(mainWindow);
            if (binWindow is not null)
            {
                FocusBinSelectionGrid(binWindow);
            }

            SendKeys.SendWait("^{INSERT}");
            _logger.Info("BIN 행추가 Ctrl+Insert 전송.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"BIN 행추가 Ctrl+Insert 실패: {ex.Message}");
            return false;
        }
    }

    private async Task<AutomationElement?> WaitForNewBinRowAsync(AutomationElement binWindow, int before, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var rows = FindBinInfoRows(binWindow);
            if (rows.Count > before)
            {
                return rows
                    .Select(row => new
                    {
                        Row = row,
                        Rect = SafeReadRect(() => row.Current.BoundingRectangle)
                    })
                    .Where(x => x.Rect.HasValue && !x.Rect.Value.IsEmpty)
                    .OrderBy(x => x.Rect!.Value.Top)
                    .LastOrDefault()?.Row;
            }

            if (before == 0 && rows.Count > 0)
            {
                return rows[0];
            }

            await DelayAsync(100);
        }

        return null;
    }

    private int CountBinInfoRows(AutomationElement binWindow) => FindBinInfoRows(binWindow).Count;

    private List<AutomationElement> FindBinInfoRows(AutomationElement binWindow)
    {
        return FindDescendants(binWindow, ControlType.DataItem)
            .Where(IsBinInfoRow)
            .ToList();
    }

    private AutomationElement? FindNewBinRow(AutomationElement binWindow)
    {
        return FindBinInfoRows(binWindow)
            .Select(row => new
            {
                Row = row,
                Rect = SafeReadRect(() => row.Current.BoundingRectangle)
            })
            .Where(x => x.Rect.HasValue && !x.Rect.Value.IsEmpty)
            .OrderBy(x => x.Rect!.Value.Top)
            .LastOrDefault()?.Row;
    }

    private bool IsBinInfoRow(AutomationElement row)
    {
        var editNames = FindDescendants(row, ControlType.Edit)
            .Select(edit => SafeRead(() => edit.Current.Name) ?? "")
            .ToHashSet(StringComparer.Ordinal);

        return editNames.Contains("품목ID") &&
               editNames.Contains("공정명") &&
               editNames.Contains("BIN ID");
    }

    private void FocusBinSelectionGrid(AutomationElement binWindow)
    {
        var group = FindDescendants(binWindow, ControlType.Group)
            .FirstOrDefault(element => string.Equals(
                SafeRead(() => element.Current.Name) ?? "",
                "BIN 정보 선택",
                StringComparison.Ordinal));
        var rect = group is null
            ? SafeReadRect(() => binWindow.Current.BoundingRectangle)
            : SafeReadRect(() => group.Current.BoundingRectangle);

        if (!rect.HasValue || rect.Value.IsEmpty)
        {
            return;
        }

        Cursor.Position = new System.Drawing.Point(
            (int)(rect.Value.Left + Math.Min(rect.Value.Width - 10, 40)),
            (int)(rect.Value.Top + Math.Min(rect.Value.Height - 10, 70)));
        MouseClick();
        Thread.Sleep(100);
    }

    private async Task<bool> SelectProcessPopupAsync(AutomationElement row, string processKey)
    {
        var cell = FindGridCell(row, "공정명", ControlType.Edit);
        if (cell is null)
        {
            _logger.Warn("BIN 공정명 셀 미발견.");
            return false;
        }

        OpenCellPopup(cell, "공정명");
        var popup = await WaitForPopupWindowAsync(IsProcessPopupWindow, TimeSpan.FromMilliseconds(1500));
        if (popup is null)
        {
            _logger.Warn("공정명 검색 팝업 미감지.");
            return false;
        }

        var input = FindEditNextToLabel(popup, "Segment ID");
        if (input is not null)
        {
            SetElementText(input, processKey, "공정명 Segment ID");
            await DelayAsync(150);
            ClickPopupSearch(popup, "공정명");
            await WaitForPopupRowAsync(popup, processKey, TimeSpan.FromMilliseconds(1500));
        }

        var refreshed = FindPopupWindow(IsProcessPopupWindow) ?? popup;
        var resultRow = FindPopupRowByExactValue(refreshed, processKey);
        if (resultRow is null)
        {
            _logger.Warn($"공정명 검색 결과 미발견. key='{processKey}'");
            await CancelGenericPopupAsync(refreshed, "공정명");
            return false;
        }

        await SelectGenericPopupRowAsync(refreshed, resultRow, "공정명", processKey);
        _logger.Info($"공정명 선택 완료. key='{processKey}'");
        return true;
    }

    private async Task<bool> SelectBinIdPopupExactAsync(AutomationElement row, string binIdName)
    {
        var cell = FindGridCell(row, "BIN ID", ControlType.Edit);
        if (cell is null)
        {
            _logger.Warn("BIN ID 셀 미발견.");
            return false;
        }

        OpenCellPopup(cell, "BIN ID");
        var popup = await WaitForPopupWindowAsync(IsBinIdPopupWindow, TimeSpan.FromMilliseconds(1500));
        if (popup is null)
        {
            _logger.Warn("BIN ID 검색 팝업 미감지.");
            return false;
        }

        var input = FindEditNextToLabel(popup, "BINID");
        if (input is not null)
        {
            SetElementText(input, binIdName, "BIN ID 검색키");
            await DelayAsync(150);
            ClickPopupSearch(popup, "BIN ID");
            await WaitForPopupRowAsync(popup, binIdName, TimeSpan.FromMilliseconds(2000));
        }

        var refreshed = FindPopupWindow(IsBinIdPopupWindow) ?? popup;
        var resultRow = FindPopupRowByExactValue(refreshed, binIdName);
        if (resultRow is null)
        {
            _logger.Warn($"BIN ID 미발견(미등록 가능). binId='{binIdName}'");
            await CancelGenericPopupAsync(refreshed, "BIN ID");
            return false;
        }

        await SelectGenericPopupRowAsync(refreshed, resultRow, "BIN ID", binIdName);
        _logger.Info($"BIN ID 선택 완료. binId='{binIdName}'");
        return true;
    }

    private AutomationElement? FindGridCell(AutomationElement row, string columnName, ControlType controlType)
    {
        return FindDescendants(row, controlType)
            .FirstOrDefault(cell =>
                string.Equals(SafeRead(() => cell.Current.Name) ?? "", columnName, StringComparison.Ordinal));
    }

    private void OpenCellPopup(AutomationElement cell, string columnName)
    {
        TryFocus(cell, $"BIN {columnName} cell");
        var rect = SafeReadRect(() => cell.Current.BoundingRectangle)
            ?? throw new InvalidOperationException($"BIN {columnName} 셀 위치를 읽지 못했습니다.");
        if (rect.IsEmpty)
        {
            throw new InvalidOperationException($"BIN {columnName} 셀 위치가 비어 있습니다.");
        }

        Cursor.Position = new System.Drawing.Point((int)(rect.Right - 8), (int)(rect.Top + rect.Height / 2));
        MouseClick();
        Thread.Sleep(250);
        _logger.Info($"BIN {columnName} 셀 팝업 버튼 클릭.");
    }

    private void FillFixedBinCells(AutomationElement row, BinRowConfig binRow)
    {
        SetBinComboCell(row, "BIN Type", binRow.BinType, expectedStoredValue: "0");

        var retestNo = FindGridCell(row, "Retest No", ControlType.Edit)
            ?? throw new InvalidOperationException("BIN Retest No 셀을 찾지 못했습니다.");
        SetElementText(retestNo, binRow.RetestNo, "BIN Retest No");
        CommitField();

        SetBinComboCell(row, "Bin완료여부", binRow.BinComplete);
        SetBinComboCell(row, "Retest TH", binRow.RetestTh);
    }

    private void SetBinComboCell(AutomationElement row, string columnName, string targetValue, string? expectedStoredValue = null)
    {
        var combo = FindGridCell(row, columnName, ControlType.ComboBox)
            ?? throw new InvalidOperationException($"BIN {columnName} 셀을 찾지 못했습니다.");

        var current = GetComboCurrentText(combo);
        if (IsExpectedBinComboValue(current, targetValue, expectedStoredValue))
        {
            _logger.Info($"BIN cell already set. column='{columnName}', value='{current}'");
            return;
        }

        TryFocus(combo, $"BIN {columnName}");
        var target = FindDescendants(combo, ControlType.ListItem)
            .FirstOrDefault(li => string.Equals(SafeRead(() => li.Current.Name) ?? "", targetValue, StringComparison.Ordinal));
        if (target is null)
        {
            throw new InvalidOperationException($"BIN {columnName} 목록에서 '{targetValue}' 항목을 찾지 못했습니다.");
        }

        // 품목정보관리 콤보(ApplyComboCell)와 동일한 다중 전략: 리스트 항목 직접 선택 → 키보드 → ValuePattern.
        // 새 행 콤보는 ExpandCollapse가 'current state' 예외로 막혀 키보드 단독으론 드롭다운이 안 열려 커밋되지 않는다.
        // 리스트 항목 직접 선택은 드롭다운 펼침이 필요 없다.
        TryExpandCombo(combo);
        if (TrySelectListItem(target))
        {
            CommitComboEdit(columnName);
            if (IsExpectedBinComboValue(GetComboCurrentText(combo), targetValue, expectedStoredValue))
            {
                _logger.Info($"BIN cell set via list item. column='{columnName}', '{current}'->'{targetValue}'");
                return;
            }
        }

        if (TrySelectComboByKeyboard(combo, targetValue, columnName) &&
            IsExpectedBinComboValue(GetComboCurrentText(combo), targetValue, expectedStoredValue))
        {
            _logger.Info($"BIN cell set via keyboard. column='{columnName}', '{current}'->'{targetValue}'");
            return;
        }

        if (TrySetBinComboValuePattern(combo, columnName, targetValue, targetValue, expectedStoredValue, current))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedStoredValue) &&
            !string.Equals(expectedStoredValue, targetValue, StringComparison.Ordinal) &&
            TrySetBinComboValuePattern(combo, columnName, expectedStoredValue, targetValue, expectedStoredValue, current))
        {
            return;
        }

        var actual = GetComboCurrentText(combo);
        throw new InvalidOperationException($"BIN {columnName} 값을 '{targetValue}'로 설정하지 못했습니다. actual='{actual}'");
    }

    private bool TrySetBinComboValuePattern(
        AutomationElement combo,
        string columnName,
        string valueToSet,
        string targetValue,
        string? expectedStoredValue,
        string previousValue)
    {
        if (!combo.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern) ||
            rawPattern is not ValuePattern valuePattern ||
            valuePattern.Current.IsReadOnly)
        {
            return false;
        }

        try
        {
            TryFocus(combo, $"BIN {columnName}");
            valuePattern.SetValue(valueToSet);
            CommitComboEdit(columnName);
            var updated = GetComboCurrentText(combo);
            if (IsExpectedBinComboValue(updated, targetValue, expectedStoredValue))
            {
                _logger.Info($"BIN cell set via ValuePattern. column='{columnName}', '{previousValue}'->'{updated}', input='{valueToSet}'");
                return true;
            }

            _logger.Warn($"BIN ValuePattern did not commit. column='{columnName}', input='{valueToSet}', actual='{updated}'");
        }
        catch (Exception ex)
        {
            _logger.Warn($"BIN ValuePattern failed. column='{columnName}', input='{valueToSet}', reason={ex.Message}");
        }

        return false;
    }

    private static bool IsExpectedBinComboValue(string actual, string targetValue, string? expectedStoredValue)
    {
        return string.Equals(actual, targetValue, StringComparison.Ordinal) ||
               (!string.IsNullOrWhiteSpace(expectedStoredValue) &&
                string.Equals(actual, expectedStoredValue, StringComparison.Ordinal));
    }

    private void ClickPopupSearch(AutomationElement popup, string label)
    {
        var search = FindButtonByAnyName(popup, ["조회", "Search", "Find"]);
        if (search is not null)
        {
            ClickElement(search, $"{label} popup search");
            return;
        }

        SendKeys.SendWait("{ENTER}");
        _logger.Info($"{label} 팝업 조회 버튼 미발견. Enter fallback 전송.");
    }

    private async Task SelectGenericPopupRowAsync(AutomationElement popup, AutomationElement row, string label, string value)
    {
        ClickElement(row, $"{label} popup row");
        await DelayAsync(150);

        var ok = FindButtonByAnyName(popup, ["확인", "OK"]);
        if (ok is not null)
        {
            ClickElement(ok, $"{label} popup confirm");
            await WaitForPopupClosedAsync(popup, TimeSpan.FromMilliseconds(1500));
            return;
        }

        SendKeys.SendWait("{ENTER}");
        await WaitForPopupClosedAsync(popup, TimeSpan.FromMilliseconds(1500));
        _logger.Info($"{label} 팝업 확인 Enter fallback 전송. value='{value}'");
    }

    private async Task CancelGenericPopupAsync(AutomationElement popup, string label)
    {
        var cancel = FindButtonByAnyName(popup, ["취소", "Cancel"]);
        if (cancel is not null)
        {
            ClickElement(cancel, $"{label} popup cancel");
            await WaitForPopupClosedAsync(popup, TimeSpan.FromMilliseconds(1000));
            return;
        }

        SendKeys.SendWait("{ESC}");
        await WaitForPopupClosedAsync(popup, TimeSpan.FromMilliseconds(1000));
    }

    private async Task WaitForPopupRowAsync(AutomationElement popup, string exactValue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var refreshed = FindPopupWindow(candidate => IsSamePopup(candidate, popup)) ?? popup;
            if (FindPopupRowByExactValue(refreshed, exactValue) is not null)
            {
                return;
            }

            await DelayAsync(100);
        }
    }

    private AutomationElement? FindPopupRowByExactValue(AutomationElement popup, string exactValue)
    {
        var rows = FindDescendants(popup, ControlType.DataItem).ToList();
        foreach (var row in rows)
        {
            var name = SafeRead(() => row.Current.Name) ?? "";
            if (string.Equals(name.Trim(), exactValue, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }

            var values = FindDescendants(row, ControlType.Edit).Select(ReadValue);
            if (values.Any(value => string.Equals(value.Trim(), exactValue, StringComparison.OrdinalIgnoreCase)))
            {
                return row;
            }
        }

        if (rows.Count == 1)
        {
            _logger.Warn($"팝업 행을 정확히 식별하지 못했지만 결과가 1건이라 해당 행을 선택합니다. value='{exactValue}'");
            return rows[0];
        }

        return null;
    }

    private async Task<AutomationElement?> WaitForPopupWindowAsync(Func<AutomationElement, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var popup = FindPopupWindow(predicate);
            if (popup is not null)
            {
                return popup;
            }

            await DelayAsync(100);
        }

        return FindPopupWindow(predicate);
    }

    private AutomationElement? FindPopupWindow(Func<AutomationElement, bool> predicate)
    {
        var candidates = new List<AutomationElement>();
        foreach (var window in FindTopLevelWindows())
        {
            if (IsUnimesCandidate(window))
            {
                if (IsSmallPopupCandidate(window) && predicate(window))
                {
                    candidates.Add(window);
                }

                AutomationElementCollection children;
                try
                {
                    children = window.FindAll(
                        TreeScope.Children,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
                }
                catch
                {
                    continue;
                }

                foreach (AutomationElement child in children)
                {
                    if (predicate(child))
                    {
                        candidates.Add(child);
                    }
                }

                continue;
            }

            if (IsSmallPopupCandidate(window) && predicate(window))
            {
                candidates.Add(window);
            }
        }

        return candidates
            .Where(window =>
            {
                var rect = SafeReadRect(() => window.Current.BoundingRectangle);
                return rect.HasValue && !rect.Value.IsEmpty;
            })
            .LastOrDefault();
    }

    private async Task WaitForPopupClosedAsync(AutomationElement popup, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FindPopupWindow(candidate => IsSamePopup(candidate, popup)) is null)
            {
                return;
            }

            await DelayAsync(100);
        }
    }

    private static bool IsSamePopup(AutomationElement candidate, AutomationElement popup)
    {
        var left = SafeRead(() => candidate.Current.NativeWindowHandle);
        var right = SafeRead(() => popup.Current.NativeWindowHandle);
        return left != 0 && left == right;
    }

    private bool IsProcessPopupWindow(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        return string.Equals(name, "Undefined", StringComparison.Ordinal) &&
               FindFirstByNameContains(window, "Segment ID") is not null;
    }

    private bool IsBinProductLookupPopupWindow(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        return string.Equals(name, "Undefined", StringComparison.Ordinal) &&
               FindFirstByNameContains(window, "품목 코드") is not null &&
               FindFirstByNameContains(window, "Segment ID") is null &&
               FindFirstByNameContains(window, "BINID") is null;
    }

    private bool IsBinIdPopupWindow(AutomationElement window)
    {
        var name = SafeRead(() => window.Current.Name) ?? "";
        return name.Contains("BINID Popup", StringComparison.OrdinalIgnoreCase) ||
               (name.Contains("BIN", StringComparison.OrdinalIgnoreCase) &&
                FindFirstByNameContains(window, "BINID") is not null);
    }
}
