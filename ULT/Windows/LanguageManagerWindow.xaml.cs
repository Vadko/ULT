using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;

namespace ULT;

public class LanguageItem
{
    public string Name { get; set; } = "";
    public bool IsChecked { get; set; } = false;
}

public partial class LanguageManagerWindow : Window
{
    public bool FileWasModified { get; private set; } = false;

    private string _filePath = "";
    private JObject _jsonObj = null;

    private ObservableCollection<LanguageItem> Languages { get; } = new ObservableCollection<LanguageItem>();

    public LanguageManagerWindow()
    {
        InitializeComponent();
        LanguagesListBox.ItemsSource = Languages;
    }

    public void PreloadFile(string filePath, JToken jsonData)
    {
        _filePath = filePath;
        _jsonObj = jsonData as JObject;
        RefreshUI();
    }

    private void RefreshUI()
    {
        Languages.Clear();
        StatusLabel.Text = "";
        DeleteCountLabel.Text = "";

        bool hasData = _jsonObj != null;

        if (hasData)
        {
            var langs = LanguageManagerService.GetLanguages(_jsonObj);
            foreach (var l in langs)
                Languages.Add(new LanguageItem { Name = l, IsChecked = false });

            var cfg = LanguageManagerService.GetInputFieldConfig(_jsonObj);
            NameInput.IsEnabled = cfg.name;
            CodeInput.IsEnabled = cfg.code;
            FlagsInput.IsEnabled = cfg.flags;
            UpdateAddHint(cfg);
        }
        else
        {
            NameInput.IsEnabled = false;
            CodeInput.IsEnabled = false;
            FlagsInput.IsEnabled = false;
            AddHintLabel.Text = "";
        }

        BtnAdd.IsEnabled = hasData;
        BtnRemove.IsEnabled = false;
        UpdateDeleteCount();
    }

    private void UpdateAddHint((bool name, bool code, bool flags) cfg)
    {
        if (cfg.code && !cfg.name && !cfg.flags)
            AddHintLabel.Text = "Для цієї структури вказується лише Code (напр. uk).";
        else if (cfg.name && !cfg.code && !cfg.flags)
            AddHintLabel.Text = "Для цієї структури вказується лише Name.";
        else if (cfg.name && cfg.code && cfg.flags)
            AddHintLabel.Text = "Потрібні Name, Code та Flags (ціле число).";
        else if (!cfg.name && !cfg.code && !cfg.flags)
            AddHintLabel.Text = "Натисніть Додати — нова мова буде створена з порожніми значеннями.";
        else
            AddHintLabel.Text = "";
    }

    public void DeleteCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDeleteCount();
    }

    private void UpdateDeleteCount()
    {
        int toDelete = Languages.Count(l => l.IsChecked);
        int total = Languages.Count;

        if (toDelete == 0)
        {
            DeleteCountLabel.Text = "";
            BtnRemove.IsEnabled = false;
        }
        else if (toDelete >= total)
        {
            DeleteCountLabel.Text = $"Не можна видаляти всі мови!";
            BtnRemove.IsEnabled = false;
        }
        else
        {
            DeleteCountLabel.Text = $"Буде видалено {toDelete} з {total} {PluralizationHelper.GetLangsWord(total)}";
            BtnRemove.IsEnabled = true;
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_jsonObj == null) return;
        StatusLabel.Text = "";

        string name = NameInput.Text.Trim();
        string code = CodeInput.Text.Trim();
        string flagsText = FlagsInput.Text.Trim();

        bool ok = LanguageManagerService.AddLanguage(_jsonObj, name, code, flagsText);
        if (!ok) return;

        if (SaveCurrentJson())
        {
            FileWasModified = true;
            StatusLabel.Text = "Мову додано й файл збережено.";
            RefreshUI();
        }
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_jsonObj == null) return;
        StatusLabel.Text = "";

        var toDeleteNames = Languages.Where(l => l.IsChecked).Select(l => l.Name).ToList();
        var indicesToKeep = Languages
            .Select((item, idx) => new { item, idx })
            .Where(x => !x.item.IsChecked)
            .Select(x => x.idx)
            .ToList();

        var confirm = MessageBox.Show(this, $"Видалити {toDeleteNames.Count} {PluralizationHelper.GetLangsWord(toDeleteNames.Count)}?\n{string.Join(", ", toDeleteNames)}\nЦю дію не можна скасувати!", "Підтвердження", MessageBoxButton.YesNo);

        if (confirm != MessageBoxResult.Yes) return;

        bool ok = LanguageManagerService.RemoveLanguages(_jsonObj, indicesToKeep);
        if (!ok) return;

        if (SaveCurrentJson())
        {
            FileWasModified = true;
            RefreshUI();
        }
    }

    private bool SaveCurrentJson()
    {
        try
        {
            File.WriteAllText(
                _filePath,
                JsonConvert.SerializeObject(_jsonObj, Formatting.Indented),
                new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{ex.Message}", "Не вдалося зберегти файл", MessageBoxButton.OK);
            return false;
        }
    }
}