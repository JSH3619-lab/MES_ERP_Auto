using System.Runtime.InteropServices;

namespace UnimesAutomation;

// 완료/실패 알림창. 자동화 중 MES 위로 떠야 하므로 TopMost|SetForeground|TaskModal 보존.
public static class NativeMessage
{
    public enum Kind
    {
        Information,
        Warning,
        Error
    }

    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbTaskModal = 0x00002000;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTopMost = 0x00040000;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(string text, string caption, Kind kind)
    {
        var icon = kind switch
        {
            Kind.Error => MbIconError,
            Kind.Warning => MbIconWarning,
            _ => MbIconInformation
        };
        MessageBoxW(IntPtr.Zero, text, caption, MbOk | icon | MbTaskModal | MbSetForeground | MbTopMost);
    }
}
