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

        if (itemOnly.Checked)
        {
            return WorkScope.ItemInfo;
        }

        if (binOnly.Checked)
        {
            return WorkScope.BinInfo;
        }

        return WorkScope.Both;
    }
}
