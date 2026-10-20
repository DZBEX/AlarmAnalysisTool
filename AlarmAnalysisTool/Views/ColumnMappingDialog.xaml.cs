using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Windows;
using AlarmAnalysisTool.Models;

namespace AlarmAnalysisTool.Views;

public partial class ColumnMappingDialog : Window
{
    private readonly List<string> _headers;
    private ObservableCollection<MappingItem> _mappingItems = new();

    public ColumnMappingDialog(List<string> headers, ColumnMapping autoMapping, List<List<string>> previewRows)
    {
        InitializeComponent();
        _headers = headers;

        // 构建映射项
        foreach (var field in ColumnMapping.StandardFields)
        {
            var item = new MappingItem
            {
                FieldName = field.Key,
                DisplayName = field.Value,
                AvailableColumns = new ObservableCollection<string>(GetAvailableOptions(field.Key))
            };

            var mapped = autoMapping.GetColumn(field.Key);
            if (!string.IsNullOrEmpty(mapped) && headers.Contains(mapped))
                item.SelectedColumn = mapped;

            _mappingItems.Add(item);
        }

        MappingList.ItemsSource = _mappingItems;

        // 预览数据
        if (previewRows.Count > 0)
        {
            var dataTable = new System.Data.DataTable();
            foreach (var h in headers)
                dataTable.Columns.Add(h);
            foreach (var row in previewRows.Take(5))
            {
                var dr = dataTable.NewRow();
                for (int i = 0; i < Math.Min(row.Count, headers.Count); i++)
                    dr[i] = row[i];
                dataTable.Rows.Add(dr);
            }
            PreviewGrid.ItemsSource = dataTable.DefaultView;
        }
    }

    private List<string> GetAvailableOptions(string fieldName)
    {
        var options = new List<string> { "(不映射)" };
        options.AddRange(_headers);
        return options;
    }

    private void AutoDetectBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _mappingItems)
        {
            foreach (var h in _headers)
            {
                var detected = ColumnMapping.AutoDetectField(h);
                if (detected == item.FieldName)
                {
                    item.SelectedColumn = h;
                    break;
                }
            }
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public ColumnMapping GetMapping()
    {
        var mapping = new ColumnMapping();
        foreach (var item in _mappingItems)
        {
            if (string.IsNullOrEmpty(item.SelectedColumn) || item.SelectedColumn == "(不映射)")
                continue;

            var prop = typeof(ColumnMapping).GetProperty(item.FieldName);
            prop?.SetValue(mapping, item.SelectedColumn);
        }
        return mapping;
    }
}

public class MappingItem : INotifyPropertyChanged
{
    public string FieldName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ObservableCollection<string> AvailableColumns { get; set; } = new();

    private string _selectedColumn = "";
    public string SelectedColumn
    {
        get => _selectedColumn;
        set { _selectedColumn = value; OnPropertyChanged(nameof(SelectedColumn)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
