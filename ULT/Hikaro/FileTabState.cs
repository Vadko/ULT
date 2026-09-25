using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;

namespace ULT;

public partial class FileTabState : INotifyPropertyChanged, IDisposable
{
    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(Header));
        }
    }
    public string FileType { get; set; } = "";
    public bool HadStatusFileOnLoad { get; set; } = false;

    public JToken JsonData { get; set; }
    public JsonFormatType CurrentFormat { get; set; } = JsonFormatType.Unknown;
    public BaseFormatHandler FormatHandler { get; set; }
    public int OrigIndex { get; set; } = 0;
    public int LangIndex { get; set; } = 1;
    public bool SupportsLanguageManager { get; set; }
    public string OrigLangLabel { get; set; }
    public string TransLangLabel { get; set; }

    public ObservableCollection<DataGridItem> DataRows { get; set; } = [];
    public ICollectionView DataGridView { get; set; }

    private bool _hasUnsavedChanges;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            if (_hasUnsavedChanges == value) return;
            _hasUnsavedChanges = value;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(Header));
        }
    }

    public UndoRedoManager UndoRedo { get; set; } = new();
    public string EditingTranslationBefore { get; set; } = null;

    public SearchManager SearchManager { get; set; } = new();
    public bool SearchWasFilteredSearch { get; set; } = false;

    public GridRowFilter ActiveGridFilter { get; set; } = GridRowFilter.None;

    public int CachedTotalWords { get; set; } = 0;
    public int CachedTranslatedRows { get; set; } = 0;
    public int CachedTranslatedWords { get; set; } = 0;
    public int CachedApprovedRows { get; set; } = 0;
    public int CachedApprovedWords { get; set; } = 0;
    public bool StatsDirty { get; set; } = true;

    public int SelectedRowIndex { get; set; } = -1;
    public double ScrollOffset { get; set; } = 0;
    public bool SearchPanelVisible { get; set; } = false;
    public string SearchStatusText { get; set; } = "";

    public CancellationTokenSource GlossaryCts { get; set; }
    public CancellationTokenSource TranslationMemoryCts { get; set; }

    public string Header
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Новий файл";

            string name = TruncateFileName(Path.GetFileName(FilePath));
            string unsaved = HasUnsavedChanges ? "*" : "";
            return $"{unsaved}{name}";
        }
    }

    public static string TruncateFileName(string fileName, int maxLength = 35)
    {
        if (string.IsNullOrEmpty(fileName))
            return fileName;

        string ext = Path.GetExtension(fileName);
        string nameOnly = Path.GetFileNameWithoutExtension(fileName);

        int cabIndex = nameOnly.IndexOf("-CAB-", StringComparison.OrdinalIgnoreCase);
        if (cabIndex >= 0)
        {
            string prefix = nameOnly[..(cabIndex + "-CAB-".Length)];
            return prefix + "..." + ext;
        }

        if (fileName.Length <= maxLength)
            return fileName;

        int keep = Math.Max(5, maxLength - ext.Length - 3);
        return nameOnly[..Math.Min(keep, nameOnly.Length)] + "..." + ext;
    }

    public string WindowTitle(string toolName)
    {
        if (string.IsNullOrEmpty(FilePath))
            return toolName;

        string fileName = Path.GetFileName(FilePath);
        string unsaved = HasUnsavedChanges ? "*" : "";
        var tmWriteTarget = TranslationMemoryManager.Instance.Memories.FirstOrDefault(m => m.IsActive && m.IsWriteTarget);
        string tm = tmWriteTarget != null ? $" [TM:{tmWriteTarget.Name}]" : "";
        return $"{unsaved}{toolName}{tm} - {fileName}";
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void InitCollectionView()
    {
        DataGridView = CollectionViewSource.GetDefaultView(DataRows);
        DataGridView.MoveCurrentToPosition(-1);
    }

    private void ClearFileState()
    {
        FilePath = "";
        FileType = "";
        JsonData = null;
        FormatHandler = null;
        DataRows.Clear();
        UndoRedo?.Clear();
        SearchManager?.ClearSearch();
        EditingTranslationBefore = null;
    }

    public void Reset()
    {
        ClearFileState();
        CurrentFormat = JsonFormatType.Unknown;
        OrigIndex = 0;
        LangIndex = 1;
        SupportsLanguageManager = false;
        OrigLangLabel = null;
        TransLangLabel = null;
        HasUnsavedChanges = false;
        SearchWasFilteredSearch = false;
        SearchPanelVisible = false;
        SearchStatusText = "";
        ActiveGridFilter = GridRowFilter.None;
        CachedTotalWords = 0;
        CachedTranslatedRows = 0;
        CachedTranslatedWords = 0;
        CachedApprovedRows = 0;
        CachedApprovedWords = 0;
        SelectedRowIndex = -1;
        ScrollOffset = 0;
        DataGridView?.Filter = null;
        InitCollectionView();
        OnPropertyChanged(nameof(Header));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        PropertyChanged = null;

        foreach (var row in DataRows)
            row.OriginalStringData = null;

        ClearFileState();
        DataGridView = null;
        GlossaryCts?.Cancel();
        GlossaryCts?.Dispose();
        GlossaryCts = null;
        TranslationMemoryCts?.Cancel();
        TranslationMemoryCts?.Dispose();
        TranslationMemoryCts = null;
    }
}