using System.Drawing;
using System.Windows.Forms;

namespace UnimesAutomation;

// JARVIS/HUD 풍 다크 테마. 색/폰트와 컨트롤 트리 일괄 적용 헬퍼.
public static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(10, 14, 23);     // #0A0E17
    public static readonly Color Surface = Color.FromArgb(12, 18, 31);        // #0C121F
    public static readonly Color SurfaceDeep = Color.FromArgb(6, 10, 17);     // #060A11
    public static readonly Color Border = Color.FromArgb(22, 59, 71);         // #163B47
    public static readonly Color Accent = Color.FromArgb(56, 233, 238);       // bright cyan
    public static readonly Color Warn = Color.FromArgb(255, 196, 77);         // bright gold
    public static readonly Color Danger = Color.FromArgb(255, 107, 107);      // #FF6B6B
    public static readonly Color Text = Color.FromArgb(223, 251, 253);        // bright text
    public static readonly Color TextDim = Accent;                            // 실행 버튼과 동일한 시안
    public static readonly Color TextFaint = Color.FromArgb(120, 205, 216);   // 밝은 보조 시안

    public static Font Mono(float size = 9f, FontStyle style = FontStyle.Regular)
        => new("Consolas", size, style);

    // 컨트롤 트리에 다크 색을 일괄 적용한다(버튼/텍스트박스/그리드 등 기본 스타일을 다크로).
    public static void Apply(Control root)
    {
        if (root is Form)
        {
            root.BackColor = Background;
        }

        ApplyRecursive(root);
    }

    private static void ApplyRecursive(Control control)
    {
        foreach (Control c in control.Controls)
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Border;
                    b.BackColor = Surface;
                    b.ForeColor = Text;
                    b.Font = Mono(9f);
                    break;
                case TextBox t:
                    t.BackColor = SurfaceDeep;
                    t.ForeColor = Text;
                    t.BorderStyle = BorderStyle.FixedSingle;
                    t.Font = Mono(9f);
                    break;
                case ComboBox combo:
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = SurfaceDeep;
                    combo.ForeColor = Text;
                    combo.Font = Mono(9f);
                    break;
                case Label l:
                    l.ForeColor = TextDim;
                    l.Font = Mono(9f);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case Panel p:
                    p.BackColor = Surface;
                    break;
            }

            if (c.HasChildren)
            {
                ApplyRecursive(c);
            }
        }
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = SurfaceDeep;
        grid.GridColor = Border;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.DefaultCellStyle.BackColor = SurfaceDeep;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Border;
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Font = Mono(9f);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
        grid.ColumnHeadersDefaultCellStyle.Font = Mono(9f);
        grid.RowHeadersVisible = false;
    }
}
