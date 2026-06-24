using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class SsdSettingsControl : UserControl
{
    private readonly OptionsConfig _options;
    private readonly DataGridView _item = new() { Dock = DockStyle.Top, Height = 64, AllowUserToAddRows = false, AllowUserToResizeRows = false };
    private readonly DataGridView _b0Bin = new() { Dock = DockStyle.Fill, AllowUserToResizeRows = false };
    private readonly DataGridView _r0Bin = new() { Dock = DockStyle.Fill, AllowUserToResizeRows = false };

    public SsdSettingsControl(SsdCategoryConfig category, OptionsConfig options)
    {
        _options = options;
        Padding = new Padding(12);

        BuildItemGrid();
        BuildBinGrid(_b0Bin);
        BuildBinGrid(_r0Bin);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildBinPage("B0 BIN 정보관리", _b0Bin));
        tabs.TabPages.Add(BuildBinPage("R0 BIN 정보관리", _r0Bin));

        Controls.Add(tabs);
        Controls.Add(new Label { Text = "품목정보관리", Dock = DockStyle.Top, Height = 22 });
        Controls.Add(_item);

        LoadFrom(category);
    }

    private void BuildItemGrid()
    {
        _item.Columns.Add(YesNoCol("binManage", "BIN 관리"));
        _item.Columns.Add(YesNoCol("turnKey", "Turn Key"));
        _item.Columns.Add(ComboCol("defectWarehouse", "불량창고", _options.DefectWarehouses));
    }

    private void BuildBinGrid(DataGridView grid)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "processName", HeaderText = "공정명" });
        grid.Columns.Add(ComboCol("binType", "BIN Type", _options.BinTypes));
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "retestNo", HeaderText = "Retest No" });
        grid.Columns.Add(ComboCol("binComplete", "BIN 완료여부", _options.BinCompletes));
        grid.Columns.Add(ComboCol("retestTh", "Retest TH", _options.RetestThs));

        var binId = new DataGridViewTextBoxColumn { Name = "binId", HeaderText = "BIN ID", ReadOnly = true };
        binId.DefaultCellStyle.ForeColor = UiTheme.TextFaint;
        grid.Columns.Add(binId);
    }

    private TabPage BuildBinPage(string title, DataGridView grid)
    {
        var page = new TabPage(title);
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        var add = new Button { Text = "행 추가", AutoSize = true };
        var del = new Button { Text = "행 삭제", AutoSize = true };
        add.Click += (_, _) => AddBinRow(grid, "", "", "", "", "");
        del.Click += (_, _) => { if (grid.CurrentRow is { IsNewRow: false } r) grid.Rows.Remove(r); };
        header.Controls.Add(add);
        header.Controls.Add(del);

        page.Controls.Add(grid);
        page.Controls.Add(header);
        return page;
    }

    private static DataGridViewComboBoxColumn YesNoCol(string name, string header)
        => ComboCol(name, header, ["Y", "N"]);

    private static DataGridViewComboBoxColumn ComboCol(string name, string header, IEnumerable<string> items)
    {
        var col = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, FlatStyle = FlatStyle.Flat };
        foreach (var i in items)
        {
            col.Items.Add(i);
        }

        return col;
    }

    private void LoadFrom(SsdCategoryConfig category)
    {
        _item.Rows.Add(category.ItemInfo.BinManage, category.ItemInfo.TurnKey, category.ItemInfo.DefectWarehouse);
        LoadBinRows(_b0Bin, category.B0BinInfo.Rows);
        LoadBinRows(_r0Bin, category.R0BinInfo.Rows);
    }

    private void LoadBinRows(DataGridView grid, IReadOnlyList<BinRowConfig> rows)
    {
        foreach (var r in rows)
        {
            AddBinRow(grid, r.ProcessName, r.BinType, r.RetestNo, r.BinComplete, r.RetestTh);
        }

        if (rows.Count == 0)
        {
            AddBinRow(grid, "", "", "", "", "");
        }
    }

    private static void AddBinRow(DataGridView grid, string process, string binType, string retestNo, string binComplete, string retestTh)
    {
        grid.Rows.Add(process, binType, retestNo, binComplete, retestTh, "자동 산출");
    }

    public void ApplyTo(SsdCategoryConfig category)
    {
        var row = _item.Rows[0];
        category.ItemInfo.BinManage = Cell(row, "binManage");
        category.ItemInfo.TurnKey = Cell(row, "turnKey");
        category.ItemInfo.AssemblyIn = "";
        category.ItemInfo.DefectWarehouse = Cell(row, "defectWarehouse");

        ApplyBinRows(_b0Bin, category.B0BinInfo);
        ApplyBinRows(_r0Bin, category.R0BinInfo);
    }

    private static void ApplyBinRows(DataGridView grid, BinInfoValues binInfo)
    {
        var rows = new List<BinRowConfig>();
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow)
            {
                continue;
            }

            rows.Add(new BinRowConfig
            {
                ProcessName = Cell(r, "processName"),
                BinType = Cell(r, "binType"),
                RetestNo = Cell(r, "retestNo"),
                BinComplete = Cell(r, "binComplete"),
                RetestTh = Cell(r, "retestTh")
            });
        }

        if (rows.Count > 0)
        {
            binInfo.Rows = rows;
            binInfo.ProcessSearchKey = rows[0].ProcessName;
        }
    }

    private static string Cell(DataGridViewRow row, string col)
        => row.Cells[col].Value?.ToString() ?? "";
}
