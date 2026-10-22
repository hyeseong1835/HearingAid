using System.Globalization;
using System.Reflection;
using HearingAid.Core.Audio;

namespace HearingAid;

/// <summary>VSCode 설정 화면 스타일의 개별 설정 행: 강조바 + 이름/설명 + 입력 컨트롤 + 기본값 복원.</summary>
class AudioSourceSettingRow : Panel
{
    public AudioSourceSettingSpec Spec { get; }
    public object? Value { get; private set; }
    public event Action? Changed;

    static readonly Font BodyFont = new("Segoe UI", 10);

    readonly Panel accent = new();
    readonly LinkLabel resetLink = new();
    Control input = null!;
    bool suppressCommit;

    public AudioSourceSettingRow(AudioSourceSettingSpec spec, object? initialValue, int rowWidth)
    {
        Spec = spec;
        Width = rowWidth;
        BackColor = Color.FromArgb(24, 24, 28);
        BuildUi(rowWidth);
        ApplyValue(initialValue);
    }

    void BuildUi(int rowWidth)
    {
        accent.Location = new Point(0, 0);
        accent.Size = new Size(3, 70);
        accent.BackColor = Color.Transparent;
        Controls.Add(accent);

        var nameLabel = new Label
        {
            Text = Spec.Name, Location = new Point(14, 6), AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.Gainsboro
        };
        Controls.Add(nameLabel);
        var descLabel = new Label
        {
            Text = string.IsNullOrEmpty(Spec.Description) ? $"({Spec.ClrType.Name})" : $"{Spec.Description}  ({Spec.ClrType.Name})",
            Location = new Point(14, 26),
            MaximumSize = new Size(Math.Max(rowWidth - 40, 100), 0),
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Silver
        };
        Controls.Add(descLabel);

        input = CreateInputControl(Spec.ClrType);
        input.Location = new Point(14, descLabel.Bottom + 6);
        Controls.Add(input);

        int nextX = input.Right + 12;
        if (Spec.IsSecret && input is TextBox secretBox)
        {
            // 화면 공유·녹화 중에 토큰이 그대로 보이지 않도록 기본은 가리고, 필요할 때만 잠깐 보이게 한다.
            secretBox.UseSystemPasswordChar = true;
            var reveal = new CheckBox
            {
                Text = "보기", AutoSize = true, Font = BodyFont,
                Location = new Point(nextX, input.Top + 2)
            };
            reveal.CheckedChanged += (s, e) => secretBox.UseSystemPasswordChar = !reveal.Checked;
            Controls.Add(reveal);
            nextX = reveal.Right + 12;
        }

        resetLink.Text = "기본값으로 재설정";
        resetLink.Font = BodyFont;
        resetLink.Location = new Point(nextX, input.Top + 3);
        resetLink.AutoSize = true;
        resetLink.LinkColor = Color.FromArgb(0, 122, 204);
        resetLink.LinkClicked += (s, e) => ApplyValue(Spec.DefaultValue);
        Controls.Add(resetLink);

        Height = input.Bottom + 12;
        accent.Height = Height;
    }

    Control CreateInputControl(Type t)
    {
        if (Spec.IsProcessPicker)
        {
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 320, Font = BodyFont };
            foreach (var name in AudioSessionProcesses.ListActiveAudioProcessNames()) picker.Items.Add(name);
            picker.TextChanged += (s, e) => Commit(picker.Text);
            return picker;
        }
        if (t == typeof(bool))
        {
            var cb = new CheckBox { AutoSize = true, Font = BodyFont };
            cb.CheckedChanged += (s, e) => Commit(cb.Checked);
            return cb;
        }
        if (t.IsEnum)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = BodyFont };
            combo.Items.AddRange(Enum.GetValues(t).Cast<object>().ToArray());
            combo.SelectedIndexChanged += (s, e) => Commit(combo.SelectedItem);
            return combo;
        }
        if (IsIntegral(t))
        {
            var (min, max) = IntegralRange(t);
            var nud = new NumericUpDown { Width = 200, DecimalPlaces = 0, Minimum = min, Maximum = max, Font = BodyFont };
            nud.ValueChanged += (s, e) => Commit(Convert.ChangeType(nud.Value, t));
            return nud;
        }
        if (t == typeof(float) || t == typeof(double))
        {
            var nud = new NumericUpDown
            {
                Width = 140, DecimalPlaces = 3, Increment = 0.01m,
                Minimum = -1_000_000, Maximum = 1_000_000,
                Font = BodyFont
            };
            // 삼항 연산자로 쓰면 C#이 두 분기를 공통 타입(double)으로 통합해버려 float를 선택해도
            // boxed double이 되어버린다(이후 JsonSerializer가 ClrType=float로 직렬화할 때 타입 불일치로 실패).
            // 분기마다 별도로 박싱하도록 if/else로 분리한다.
            nud.ValueChanged += (s, e) =>
            {
                if (t == typeof(float)) Commit((float)nud.Value);
                else Commit((double)nud.Value);
            };
            return nud;
        }

        if (t.IsArray && IsStructRecordArray(t.GetElementType()))
            return CreateStructArrayGrid(t.GetElementType()!);

        // string 및 그 외 알 수 없는 타입은 텍스트 상자로 편집 (문자열이 아니면 타입 변환 시도)
        var tb = new TextBox { Width = 320, Font = BodyFont };
        tb.TextChanged += (s, e) => Commit(t == typeof(string) ? tb.Text : SafeConvert(tb.Text, t));
        return tb;
    }

    /// <summary>배열 요소가 (userId/gain 같은 public 필드를 가진) 순수 값 타입 구조체인지 —
    /// 이런 배열은 전용 그리드 편집기로 표시한다(예: NetCordAudioSource.UserConfig[]).</summary>
    static bool IsStructRecordArray(Type? elementType) =>
        elementType is { IsValueType: true, IsEnum: false, IsPrimitive: false } && elementType != typeof(decimal);

    /// <summary>구조체 배열 설정을, 요소 타입의 public 필드마다 한 열을 갖는 그리드로 편집한다.
    /// 행 추가/삭제는 DataGridView 기본 기능(AllowUserToAddRows/AllowUserToDeleteRows)을 그대로 쓴다.</summary>
    Control CreateStructArrayGrid(Type elementType)
    {
        var fields = elementType.GetFields(BindingFlags.Public | BindingFlags.Instance);

        var grid = new DataGridView
        {
            Width = Math.Max(Width - 160, 300),
            Height = 140,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.FromArgb(30, 30, 34),
            Font = BodyFont,
        };
        foreach (var f in fields)
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = f.Name, HeaderText = f.Name });

        void CommitFromGrid()
        {
            var items = grid.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r =>
                {
                    object item = Activator.CreateInstance(elementType)!;
                    foreach (var f in fields)
                        f.SetValue(item, ConvertFieldValue(r.Cells[f.Name].Value?.ToString() ?? "", f.FieldType));
                    return item;
                })
                .ToArray();

            var typedArray = Array.CreateInstance(elementType, items.Length);
            Array.Copy(items, typedArray, items.Length);
            Commit(typedArray);
        }

        grid.CellEndEdit += (s, e) => CommitFromGrid();
        grid.UserDeletedRow += (s, e) => CommitFromGrid();

        return grid;
    }

    /// <summary>DataGridView는 셀 편집이 끝나야(CellEndEdit) 값이 커밋된다. 저장/적용 직전에
    /// 호출해, 막 편집 중인 셀이 있어도 그 값이 반영되도록 강제로 편집을 끝낸다.</summary>
    public void CommitPending()
    {
        if (input is DataGridView grid && grid.IsCurrentCellInEditMode)
            grid.EndEdit();
    }

    static object? ConvertFieldValue(string text, Type fieldType)
    {
        if (fieldType == typeof(string)) return text;
        if (string.IsNullOrWhiteSpace(text)) return TypeDefault(fieldType);
        try { return Convert.ChangeType(text, fieldType, CultureInfo.InvariantCulture); }
        catch { return TypeDefault(fieldType); }
    }

    void PopulateGrid(DataGridView grid, object? v)
    {
        grid.Rows.Clear();
        if (v is not System.Collections.IEnumerable seq) return;

        var fields = Spec.ClrType.GetElementType()!.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var item in seq)
        {
            int rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            foreach (var f in fields)
                row.Cells[f.Name].Value = f.GetValue(item)?.ToString() ?? "";
        }
    }

    static bool IsIntegral(Type t) => t == typeof(sbyte) || t == typeof(byte) || t == typeof(short)
        || t == typeof(ushort) || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong);

    static (decimal Min, decimal Max) IntegralRange(Type t) => t switch
    {
        _ when t == typeof(ulong) => (0, ulong.MaxValue),
        _ when t == typeof(uint) => (0, uint.MaxValue),
        _ when t == typeof(ushort) => (0, ushort.MaxValue),
        _ when t == typeof(byte) => (0, byte.MaxValue),
        _ when t == typeof(long) => (long.MinValue, long.MaxValue),
        _ => (int.MinValue, int.MaxValue) // int/short/sbyte
    };

    static object? SafeConvert(string text, Type t)
    {
        try { return Convert.ChangeType(text, t); }
        catch { return TypeDefault(t); }
    }

    static object? TypeDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

    void Commit(object? v)
    {
        if (suppressCommit) return;
        Value = v;
        UpdateAccent();
        Changed?.Invoke();
    }

    void ApplyValue(object? v)
    {
        // 컨트롤에 값을 밀어넣으면 컨트롤 자신의 변경 이벤트가 Commit()을 호출해 Value를 동기화한다.
        // 단, ValueChanged/TextChanged가 값이 그대로일 때는 발생하지 않을 수 있으므로 먼저 직접 반영해둔다.
        suppressCommit = true;
        try
        {
            switch (input)
            {
                case CheckBox cb: cb.Checked = v is bool b && b; break;
                case ComboBox combo when Spec.IsProcessPicker: combo.Text = v?.ToString() ?? ""; break;
                case ComboBox combo: combo.SelectedItem = v; break;
                case NumericUpDown nud: nud.Value = Convert.ToDecimal(v ?? 0); break;
                case DataGridView grid: PopulateGrid(grid, v); break;
                case TextBox tb: tb.Text = v?.ToString() ?? ""; break;
            }
        }
        finally { suppressCommit = false; }

        Value = v;
        UpdateAccent();
        Changed?.Invoke();
    }

    void UpdateAccent()
    {
        bool modified = !Equals(Value, Spec.DefaultValue);
        accent.BackColor = modified ? Color.FromArgb(0, 122, 204) : Color.Transparent;
        resetLink.Visible = modified;
    }
}
