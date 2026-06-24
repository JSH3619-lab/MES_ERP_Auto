using System.Drawing;
using System.Windows.Forms;

namespace UnimesAutomation;

// RAmos 브랜드 라이트 테마. 밝은 코퍼레이트 톤 + 실행 로그만 다크 콘솔.
public static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(255, 255, 255);    // 본문/입력 화이트
    public static readonly Color Surface = Color.FromArgb(244, 246, 250);       // #F4F6FA 연한 스트립(헤더 하단/상태/푸터)
    public static readonly Color SurfaceDeep = Color.FromArgb(255, 255, 255);   // 입력칸 화이트
    public static readonly Color Border = Color.FromArgb(216, 222, 233);        // #D8DEE9 경계선
    public static readonly Color Navy = Color.FromArgb(24, 56, 149);            // #183895 브랜드 네이비(주색)
    public static readonly Color Accent = Color.FromArgb(24, 56, 149);          // 주색 = 네이비
    public static readonly Color AccentBright = Color.FromArgb(46, 91, 208);    // #2E5BD0 스우시/진행바
    public static readonly Color Gray = Color.FromArgb(127, 127, 127);          // #7F7F7F 브랜드 그레이(보조)
    public static readonly Color Warn = Color.FromArgb(224, 165, 46);           // #E0A52E
    public static readonly Color Danger = Color.FromArgb(210, 59, 59);          // #D23B3B
    public static readonly Color Success = Color.FromArgb(31, 157, 85);         // #1F9D55
    public static readonly Color Text = Color.FromArgb(27, 34, 48);             // #1B2230 본문 텍스트
    public static readonly Color TextDim = Color.FromArgb(91, 102, 117);        // #5B6675
    public static readonly Color TextFaint = Color.FromArgb(138, 147, 163);     // #8A93A3

    // 실행 로그(다크 콘솔) 전용 색
    public static readonly Color LogBackground = Color.FromArgb(14, 19, 32);    // #0E1320
    public static readonly Color LogText = Color.FromArgb(174, 183, 199);       // #AEB7C7
    public static readonly Color LogFaint = Color.FromArgb(90, 100, 120);       // #5A6478
    public static readonly Color LogInfo = Color.FromArgb(108, 160, 240);       // #6CA0F0
    public static readonly Color LogWarn = Color.FromArgb(224, 165, 46);        // #E0A52E
    public static readonly Color LogDanger = Color.FromArgb(255, 107, 107);     // #FF6B6B

    public static Font Mono(float size = 9f, FontStyle style = FontStyle.Regular)
        => new("Consolas", size, style);

    public static Font Ui(float size = 9f, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", size, style);

    // 컨트롤 트리에 브랜드 라이트 색을 일괄 적용한다(설정 창 등).
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
                    b.Font = Ui(9f);
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
                    combo.Font = Ui(9f);
                    break;
                case Label l:
                    l.ForeColor = TextDim;
                    l.Font = Ui(9f);
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
        grid.BackgroundColor = Background;
        grid.GridColor = Border;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.DefaultCellStyle.BackColor = SurfaceDeep;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Navy;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Font = Ui(9f);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
        grid.ColumnHeadersDefaultCellStyle.Font = Ui(9f);
        grid.RowHeadersVisible = false;
    }
}
