using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UnimesAutomation;

public sealed partial class UnimesApp
{
    // 미존재 경고('존재하지' 텍스트 + [확인])를 가진 창을 찾는다. 별도 모달이든 메인 자식이든
    // 내용 기준으로 잡는다(이름으로 제외하지 않음 — 경고창 제목이 비거나 UNIMES일 수 있어서).
    private AutomationElement? FindWarningDialog() => FindMesMessageDialog(["존재하지"]);

    // 971001 경고 탐지(검증됨)를 텍스트 토큰만 바꿔 재사용한다. 900014 등 동종 'Message' 모달 공통.
    private AutomationElement? FindMesMessageDialog(IReadOnlyCollection<string> textTokens)
    {
        foreach (var window in FindTopLevelWindows())
        {
            // 경고창은 항상 MES 프로세스 소속이다. 이 화면 밖 에디터/터미널 창에도
            // 같은 텍스트가 보일 수 있어, MES 후보 창으로 한정해 오탐을 막는다.
            if (!IsUnimesCandidate(window))
            {
                continue;
            }

            if (IsMesMessageDialog(window, textTokens))
            {
                return window;
            }

            foreach (var childWindow in FindDescendants(window, ControlType.Window))
            {
                if (IsMesMessageDialog(childWindow, textTokens))
                {
                    return childWindow;
                }
            }
        }

        return null;
    }

    private AutomationElement? FindWarningDialogByText(IReadOnlyCollection<string> tokens)
    {
        foreach (var window in FindTopLevelWindows())
        {
            if (WindowContainsAnyText(window, tokens) && FindButtonByAnyName(window, ["확인", "OK"]) is not null)
            {
                return window;
            }

            foreach (var childWindow in FindDescendants(window, ControlType.Window))
            {
                if (WindowContainsAnyText(childWindow, tokens) &&
                    FindButtonByAnyName(childWindow, ["확인", "OK"]) is not null)
                {
                    return childWindow;
                }
            }
        }

        return null;
    }

    private static bool IsSmallPopupCandidate(AutomationElement window)
    {
        var rect = SafeReadRect(() => window.Current.BoundingRectangle);
        if (!rect.HasValue || rect.Value.IsEmpty)
        {
            return false;
        }

        return rect.Value.Width is > 0 and <= 900 &&
               rect.Value.Height is > 0 and <= 700;
    }

    private bool WindowContainsAnyText(AutomationElement window, IReadOnlyCollection<string> tokens)
    {
        return FindDescendants(window, null)
            .Any(element => ElementContainsAnyText(element, tokens));
    }

    private bool IsMesMessageDialog(AutomationElement window, IReadOnlyCollection<string> textTokens)
    {
        return WindowContainsAnyText(window, textTokens) &&
               FindButtonByAnyName(window, ["확인", "OK"]) is not null;
    }

    private static bool ElementContainsAnyText(AutomationElement element, IReadOnlyCollection<string> tokens)
    {
        var name = SafeRead(() => element.Current.Name) ?? "";
        var value = ReadValue(element);

        return tokens.Any(token =>
            !string.IsNullOrWhiteSpace(token) &&
            (name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
             value.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ElementContainsAllText(AutomationElement element, IReadOnlyCollection<string> tokens)
    {
        var name = SafeRead(() => element.Current.Name) ?? "";
        var value = ReadValue(element);

        return tokens.All(token =>
            !string.IsNullOrWhiteSpace(token) &&
            (name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
             value.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private string ReadMessageText(AutomationElement window)
    {
        return FindDescendants(window, null)
            .Select(element => ReadValue(element).Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
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
}
