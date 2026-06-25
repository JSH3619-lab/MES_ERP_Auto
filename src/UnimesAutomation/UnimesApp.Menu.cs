using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed partial class UnimesApp
{
    private async Task NavigateToMenuByF3Async(AutomationElement mainWindow, string menuName)
    {
        if (FindNamedWindow(mainWindow, menuName) is not null)
        {
            _logger.Info($"{menuName} screen already detected. F3 navigation skipped.");
            return;
        }

        _logger.Info($"Navigating to '{menuName}' via F3 menu search.");
        var navigationStopwatch = Stopwatch.StartNew();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ThrowIfCancellationRequested($"'{menuName}' 메뉴 탐색");

            if (!await EnsureMainWindowForegroundAsync(mainWindow, menuName, attempt))
            {
                _logger.Warn($"MES 메인 창 foreground 확보 실패. menu='{menuName}', attempt={attempt}");
                _screenshots.CaptureDesktop($"menu_foreground_failed_attempt_{attempt}");
                await DelayAsync(300);
                continue;
            }

            AutomationElement? menuSearchButton = null;
            SendKeys.SendWait("{F3}");
            _logger.Info($"F3 메뉴찾기 입력. attempt={attempt}, elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");

            await DelayAsync(150);
            LogFocusedElement(mainWindow, $"메뉴찾기 동작 직후. menu='{menuName}', attempt={attempt}");

            var menuTextSet = await TrySetMenuSearchTextAsync(mainWindow, menuSearchButton, menuName, attempt);
            if (!menuTextSet)
            {
                menuSearchButton = FindButtonByAutomationIdContains(mainWindow, "Tool : GoSearch")
                    ?? FindButtonByAnyName(mainWindow, ["메뉴찾기"]);
                if (menuSearchButton is not null)
                {
                    ClickElement(menuSearchButton, "menu search");
                    _logger.Info($"F3 실패 후 메뉴찾기 버튼 클릭. attempt={attempt}, elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");
                    await DelayAsync(150);
                    LogFocusedElement(mainWindow, $"메뉴찾기 버튼 클릭 직후. menu='{menuName}', attempt={attempt}");
                    menuTextSet = await TrySetMenuSearchTextAsync(mainWindow, menuSearchButton, menuName, attempt);
                }
            }

            if (menuTextSet)
            {
                _logger.Info($"메뉴찾기 입력칸 직접 활성화. menu='{menuName}', attempt={attempt}, elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");
            }
            else
            {
                _logger.Warn($"메뉴찾기 입력칸 직접 활성화 실패. 메뉴명 SendKeys fallback 생략. menu='{menuName}', attempt={attempt}");
                _screenshots.CaptureElement(mainWindow, $"menu_search_input_not_found_attempt_{attempt}");
                await DelayAsync(300);
                continue;
            }

            SendKeys.SendWait("{ENTER}");

            if (await WaitForMenuScreenAsync(mainWindow, menuName, TimeSpan.FromSeconds(1)))
            {
                _logger.Info($"{menuName} screen confirmed. elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");
                return;
            }

            if (TryClickMenuSearchGoButton(mainWindow))
            {
                _logger.Info($"메뉴찾기 [가기] 버튼 클릭. menu='{menuName}', attempt={attempt}");
                if (await WaitForMenuScreenAsync(mainWindow, menuName, TimeSpan.FromSeconds(2)))
                {
                    _logger.Info($"{menuName} screen confirmed. elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");
                    return;
                }
            }

            _logger.Warn($"{menuName} screen was not confirmed after menu search attempt {attempt}.");
            _screenshots.CaptureElement(mainWindow, $"menu_f3_not_confirmed_attempt_{attempt}");

            if (TryOpenMenuFromTree(mainWindow, menuName))
            {
                _logger.Info($"트리 메뉴 직접 더블클릭. menu='{menuName}', attempt={attempt}");
                if (await WaitForMenuScreenAsync(mainWindow, menuName, TimeSpan.FromSeconds(2)))
                {
                    _logger.Info($"{menuName} screen confirmed by tree menu. elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");
                    return;
                }
            }

            await DelayAsync(200);
        }

        if (FindNamedWindow(mainWindow, menuName) is null)
        {
            throw new InvalidOperationException($"{menuName} 화면 진입을 확인하지 못했습니다. Home Page 등 다른 화면에서 입력하지 않도록 중단합니다.");
        }

        _logger.Info($"{menuName} screen confirmed. elapsed={navigationStopwatch.Elapsed.TotalSeconds:0.000}s");
    }

    private async Task<bool> EnsureMainWindowForegroundAsync(AutomationElement mainWindow, string menuName, int attempt)
    {
        var handle = GetNativeHandle(mainWindow);
        if (handle == IntPtr.Zero)
        {
            _logger.Warn($"MES 메인 창 handle 확인 실패. menu='{menuName}', attempt={attempt}");
            return false;
        }

        for (var retry = 1; retry <= 3; retry++)
        {
            BringToFront(mainWindow);
            await DelayAsync(150);

            var foreground = GetForegroundWindow();
            if (foreground == handle)
            {
                if (retry > 1)
                {
                    _logger.Info($"MES 메인 창 foreground 확보. menu='{menuName}', attempt={attempt}, retry={retry}");
                }

                return true;
            }

            var foregroundWindow = TryFromHandle(foreground);
            var name = foregroundWindow is null ? "" : SafeRead(() => foregroundWindow.Current.Name) ?? "";
            var processId = foregroundWindow is null ? null : SafeReadInt(() => foregroundWindow.Current.ProcessId);
            var processName = GetProcessName(processId);
            _logger.Warn($"MES 메인 창 foreground 대기. menu='{menuName}', attempt={attempt}, retry={retry}, foreground='{name}', process='{processName}'");
        }

        return false;
    }

    private async Task<bool> TrySetMenuSearchTextAsync(
        AutomationElement mainWindow,
        AutomationElement? menuSearchButton,
        string menuName,
        int attempt)
    {
        var input = await WaitForMenuSearchInputAsync(mainWindow, menuSearchButton, TimeSpan.FromMilliseconds(700));
        if (input is null)
        {
            input = TryUseFocusedMenuSearchEdit(mainWindow, menuName, attempt, "메뉴찾기 입력칸 후보 미발견");
        }

        if (input is null && TryActivateHomePageTab(mainWindow))
        {
            await DelayAsync(150);
            SendKeys.SendWait("{F3}");
            _logger.Info($"Home Page 탭 전환 후 F3 메뉴찾기 재입력. menu='{menuName}', attempt={attempt}");
            await DelayAsync(150);
            LogFocusedElement(mainWindow, $"Home Page 전환 후 메뉴찾기 동작 직후. menu='{menuName}', attempt={attempt}");
            input = await WaitForMenuSearchInputAsync(mainWindow, menuSearchButton, TimeSpan.FromMilliseconds(700));
            if (input is null)
            {
                input = TryUseFocusedMenuSearchEdit(mainWindow, menuName, attempt, "Home Page 전환 후 메뉴찾기 입력칸 후보 미발견");
            }
        }

        if (input is null)
        {
            return false;
        }

        try
        {
            TryFocus(input, "메뉴찾기 입력칸");
        }
        catch
        {
            ClickElementCenterByMouse(input);
            await DelayAsync(100);
        }

        if (input.TryGetCurrentPattern(ValuePattern.Pattern, out var rawPattern) &&
            rawPattern is ValuePattern valuePattern &&
            !valuePattern.Current.IsReadOnly)
        {
            try
            {
                valuePattern.SetValue(menuName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"메뉴찾기 ValuePattern.SetValue 실패. attempt={attempt}, reason={ex.Message}");
            }
        }

        SendKeys.SendWait("^a");
        SendKeys.SendWait("{BACKSPACE}");
        SendKeys.SendWait(EscapeForSendKeys(menuName));
        return true;
    }

    private AutomationElement? TryUseFocusedMenuSearchEdit(AutomationElement mainWindow, string menuName, int attempt, string context)
    {
        var focused = LogFocusedElement(mainWindow, $"{context}. menu='{menuName}', attempt={attempt}");
        if (!IsWritableEditInside(mainWindow, focused))
        {
            return null;
        }

        _logger.Info($"메뉴찾기 focused Edit 사용. menu='{menuName}', attempt={attempt}, input={DescribeElementForLog(focused)}");
        return focused;
    }

    private bool TryActivateHomePageTab(AutomationElement mainWindow)
    {
        var tab = FindDescendants(mainWindow, ControlType.TabItem)
            .Select(element => new
            {
                Element = element,
                Name = SafeRead(() => element.Current.Name) ?? "",
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Enabled = SafeRead(() => element.Current.IsEnabled),
                Offscreen = SafeRead(() => element.Current.IsOffscreen)
            })
            .Where(candidate => string.Equals(candidate.Name, "Home Page", StringComparison.OrdinalIgnoreCase) &&
                                candidate.Rect.HasValue &&
                                !candidate.Rect.Value.IsEmpty &&
                                candidate.Enabled &&
                                !candidate.Offscreen)
            .OrderBy(candidate => candidate.Rect!.Value.Top)
            .ThenBy(candidate => candidate.Rect!.Value.Left)
            .FirstOrDefault()?.Element;

        if (tab is null)
        {
            _logger.Warn("Home Page 탭을 찾지 못해 메뉴찾기 포커스 복구를 생략합니다.");
            return false;
        }

        BringToFront(mainWindow);
        if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var rawPattern) &&
            rawPattern is SelectionItemPattern selectionItemPattern)
        {
            try
            {
                selectionItemPattern.Select();
                _logger.Info("Home Page 탭 선택으로 메뉴찾기 포커스 복구 시도.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Home Page 탭 SelectionItemPattern 실패. 좌표 클릭 fallback 사용. reason={ex.Message}");
            }
        }

        try
        {
            ClickElementCenterByMouse(tab);
            _logger.Info("Home Page 탭 클릭으로 메뉴찾기 포커스 복구 시도.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Home Page 탭 클릭 실패. reason={ex.Message}");
            return false;
        }
    }

    private AutomationElement? LogFocusedElement(AutomationElement mainWindow, string context)
    {
        var focused = TryGetFocusedElement();
        var insideMain = focused is not null && IsInsideElement(mainWindow, focused);
        var writableEdit = IsWritableEdit(focused);
        _logger.Info($"{context} FocusedElement: insideMain={insideMain}, writableEdit={writableEdit}, {DescribeElementForLog(focused)}");
        return focused;
    }

    private static AutomationElement? TryGetFocusedElement()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWritableEditInside(AutomationElement mainWindow, AutomationElement? element)
    {
        return element is not null &&
               IsInsideElement(mainWindow, element) &&
               IsWritableEdit(element);
    }

    private static bool IsWritableEdit(AutomationElement? element)
    {
        if (element is null)
        {
            return false;
        }

        var controlType = SafeRead(() => element.Current.ControlType);
        return controlType == ControlType.Edit && IsWritableValueControl(element);
    }

    private static bool IsInsideElement(AutomationElement root, AutomationElement element)
    {
        try
        {
            if (Automation.Compare(root, element))
            {
                return true;
            }

            var walker = TreeWalker.ControlViewWalker;
            var current = element;
            while (current is not null)
            {
                var parent = walker.GetParent(current);
                if (parent is null)
                {
                    return false;
                }

                if (Automation.Compare(root, parent))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private async Task<AutomationElement?> WaitForMenuSearchInputAsync(
        AutomationElement mainWindow,
        AutomationElement? menuSearchButton,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var input = FindMenuSearchInput(mainWindow, menuSearchButton);
            if (input is not null)
            {
                return input;
            }

            await DelayAsync(100);
        }

        return FindMenuSearchInput(mainWindow, menuSearchButton);
    }

    private AutomationElement? FindMenuSearchInput(AutomationElement mainWindow, AutomationElement? menuSearchButton)
    {
        var mainRect = SafeReadRect(() => mainWindow.Current.BoundingRectangle);
        var buttonRect = menuSearchButton is null ? null : SafeReadRect(() => menuSearchButton.Current.BoundingRectangle);
        if (mainRect is null || mainRect.Value.IsEmpty)
        {
            return null;
        }

        return FindDescendants(mainWindow, ControlType.Edit)
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
                                !candidate.Offscreen &&
                                IsMenuSearchPanelRect(candidate.Rect.Value, mainRect.Value, buttonRect))
            .OrderByDescending(candidate => candidate.Rect!.Value.Left)
            .ThenBy(candidate => candidate.Rect!.Value.Top)
            .FirstOrDefault()?.Element;
    }

    private static bool IsMenuSearchPanelRect(
        System.Windows.Rect rect,
        System.Windows.Rect mainRect,
        System.Windows.Rect? menuSearchButtonRect)
    {
        var inTopRightPanel = rect.Top >= mainRect.Top &&
                              rect.Bottom <= mainRect.Top + 180 &&
                              rect.Left >= mainRect.Right - 430;
        if (inTopRightPanel)
        {
            return true;
        }

        return menuSearchButtonRect.HasValue &&
               rect.Left >= menuSearchButtonRect.Value.Left - 80 &&
               rect.Top >= menuSearchButtonRect.Value.Top &&
               rect.Top <= menuSearchButtonRect.Value.Bottom + 70;
    }

    private bool TryClickMenuSearchGoButton(AutomationElement mainWindow)
    {
        var mainRect = SafeReadRect(() => mainWindow.Current.BoundingRectangle);
        if (mainRect is null || mainRect.Value.IsEmpty)
        {
            return false;
        }

        var button = FindDescendants(mainWindow, ControlType.Button)
            .Select(element => new
            {
                Element = element,
                Name = SafeRead(() => element.Current.Name) ?? "",
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Enabled = SafeRead(() => element.Current.IsEnabled),
                Offscreen = SafeRead(() => element.Current.IsOffscreen)
            })
            .Where(candidate => candidate.Name.Contains("가기", StringComparison.OrdinalIgnoreCase) &&
                                candidate.Rect.HasValue &&
                                !candidate.Rect.Value.IsEmpty &&
                                candidate.Enabled &&
                                !candidate.Offscreen &&
                                candidate.Rect.Value.Top >= mainRect.Value.Top &&
                                candidate.Rect.Value.Bottom <= mainRect.Value.Top + 140 &&
                                candidate.Rect.Value.Left >= mainRect.Value.Right - 220)
            .OrderByDescending(candidate => candidate.Rect!.Value.Left)
            .FirstOrDefault()?.Element;

        if (button is null)
        {
            return false;
        }

        ClickElement(button, "menu search go");
        return true;
    }

    private bool TryOpenMenuFromTree(AutomationElement mainWindow, string menuName)
    {
        var item = FindDescendants(mainWindow, ControlType.DataItem)
            .Select(element => new
            {
                Element = element,
                Name = SafeRead(() => element.Current.Name) ?? "",
                Rect = SafeReadRect(() => element.Current.BoundingRectangle),
                Enabled = SafeRead(() => element.Current.IsEnabled),
                Offscreen = SafeRead(() => element.Current.IsOffscreen)
            })
            .Where(candidate => string.Equals(candidate.Name, menuName, StringComparison.Ordinal) &&
                                candidate.Rect.HasValue &&
                                !candidate.Rect.Value.IsEmpty &&
                                candidate.Enabled &&
                                !candidate.Offscreen)
            .OrderBy(candidate => candidate.Rect!.Value.Top)
            .FirstOrDefault()?.Element;

        if (item is null)
        {
            return false;
        }

        BringToFront(mainWindow);
        ClickElementCenterByMouseDouble(item);
        return true;
    }

    private async Task<bool> WaitForMenuScreenAsync(AutomationElement mainWindow, string menuName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FindNamedWindow(mainWindow, menuName) is not null)
            {
                return true;
            }

            await DelayAsync(250);
        }

        return false;
    }

    private static string MakeSafeToken(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }
}
