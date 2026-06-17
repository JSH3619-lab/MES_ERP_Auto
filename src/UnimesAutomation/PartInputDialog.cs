using System.Windows.Forms;

namespace UnimesAutomation;

public static class PartInputDialog
{
    public static IReadOnlyList<PartRequest>? ShowDialog(IWin32Window? owner = null)
    {
        using var form = new Form
        {
            Text = "UNIMES Part No 입력",
            Width = 560,
            Height = 430,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog
        };

        var label = new Label
        {
            Text = "진행할 Part No를 입력하세요. 한 줄에 하나씩 입력하거나 쉼표/공백으로 구분할 수 있습니다.",
            Left = 16,
            Top = 14,
            Width = 510,
            Height = 36
        };

        var textBox = new TextBox
        {
            Left = 16,
            Top = 56,
            Width = 510,
            Height = 280,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            AcceptsTab = false
        };

        var okButton = new Button
        {
            Text = "시작",
            Left = 346,
            Top = 350,
            Width = 85,
            Height = 30,
            DialogResult = DialogResult.OK
        };

        var cancelButton = new Button
        {
            Text = "취소",
            Left = 441,
            Top = 350,
            Width = 85,
            Height = 30,
            DialogResult = DialogResult.Cancel
        };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        while (true)
        {
            var result = owner is null ? form.ShowDialog() : form.ShowDialog(owner);
            if (result != DialogResult.OK)
            {
                return null;
            }

            var parts = Parse(textBox.Text);
            if (parts.Count > 0)
            {
                return parts;
            }

            MessageBox.Show(form, "Part No를 하나 이상 입력하세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static List<PartRequest> Parse(string text)
    {
        return text
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(partNo => new PartRequest { PartNo = partNo })
            .ToList();
    }
}

