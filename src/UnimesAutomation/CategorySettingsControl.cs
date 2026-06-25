using System.Windows.Forms;

namespace UnimesAutomation;

public sealed class CategorySettingsControl : UserControl
{
    private readonly OptionsConfig _options;
    private readonly DataGridView _item = new() { Dock = DockStyle.Top, Height = 64, AllowUserToAddRows = false, AllowUserToResizeRows = false };
    private readonly DataGridView _bin = new() { Dock = DockStyle.Fill, AllowUserToResizeRows = false };

    public CategorySettingsControl(CategoryConfig category, OptionsConfig options)
    {
        _options = options;
        Padding = new Padding(12);

        BuildItemGrid();
        BuildBinGrid();

        // 콤보 목록에 없는 값(예: SIP 1번행 Bin완료여부 Blank)이 와도 오류 대화상자 없이 그대로 둔다.
        _item.DataError += (_, e) => e.ThrowException = false;
        _bin.DataError += (_, e) => e.ThrowException = false;

        var binHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        binHeader.Controls.Add(new Label { Text = "BIN 정보관리", AutoSize = true });
        var add = new Button { Text = "행 추가", AutoSize = true };
        var del = new Button { Text = "행 삭제", AutoSize = true };
        add.Click += (_, _) => AddBinRow("", "", "", "", "");
        del.Click += (_, _) => { if (_bin.CurrentRow is { IsNewRow: false } r) _bin.Rows.Remove(r); };
        binHeader.Controls.Add(add);
        binHeader.Controls.Add(del);

        Controls.Add(_bin);
        Controls.Add(binHeader);
        Controls.Add(new Label { Text = "품목정보관리", Dock = DockStyle.Top, Height = 22 });
        Controls.Add(_item);

        LoadFrom(category);
    }

    private void BuildItemGrid()
    {
        _item.AllowUserToAddRows = false;
        _item.Columns.Add(YesNoCol("binManage", "BIN 관리"));
        _item.Columns.Add(YesNoCol("turnKey", "Turn Key"));
        _item.Columns.Add(YesNoCol("assemblyIn", "조립입고"));
        _item.Columns.Add(ComboCol("defectWarehouse", "불량창고", _options.DefectWarehouses));
    }

    private void BuildBinGrid()
    {
        _bin.Columns.Add(new DataGridViewTextBoxColumn { Name = "processName", HeaderText = "공정명" });
        _bin.Columns.Add(ComboCol("binType", "BIN Type", _options.BinTypes));
        _bin.Columns.Add(new DataGridViewTextBoxColumn { Name = "retestNo", HeaderText = "Retest No" });
        _bin.Columns.Add(ComboCol("binComplete", "BIN 완료여부", _options.BinCompletes));
        _bin.Columns.Add(ComboCol("retestTh", "Retest TH", _options.RetestThs));

        // BIN ID는 파트 용량코드로 자동 산출(편집 불가). 표시만 한다.
        var binId = new DataGridViewTextBoxColumn { Name = "binId", HeaderText = "BIN ID", ReadOnly = true };
        binId.DefaultCellStyle.ForeColor = UiTheme.TextFaint;
        _bin.Columns.Add(binId);
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

    private void AddBinRow(string process, string binType, string retestNo, string binComplete, string retestTh)
    {
        _bin.Rows.Add(process, binType, retestNo, binComplete, retestTh, "자동 산출");
    }

    private void LoadFrom(CategoryConfig category)
    {
        _item.Rows.Add(category.ItemInfo.BinManage, category.ItemInfo.TurnKey, category.ItemInfo.AssemblyIn, category.ItemInfo.DefectWarehouse);
        foreach (var r in category.BinInfo.Rows)
        {
            AddBinRow(r.ProcessName, r.BinType, r.RetestNo, r.BinComplete, r.RetestTh);
        }

        if (category.BinInfo.Rows.Count == 0)
        {
            AddBinRow("", "", "", "", "");
        }
    }

    public void ApplyTo(CategoryConfig category)
    {
        var row = _item.Rows[0];
        category.ItemInfo.BinManage = Cell(row, "binManage");
        category.ItemInfo.TurnKey = Cell(row, "turnKey");
        category.ItemInfo.AssemblyIn = Cell(row, "assemblyIn");
        category.ItemInfo.DefectWarehouse = Cell(row, "defectWarehouse");

        var rows = new List<BinRowConfig>();
        foreach (DataGridViewRow r in _bin.Rows)
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
            category.BinInfo.Rows = rows;
            category.BinInfo.ProcessSearchKey = rows[0].ProcessName;
        }
    }

    private static string Cell(DataGridViewRow row, string col)
        => row.Cells[col].Value?.ToString() ?? "";
}
