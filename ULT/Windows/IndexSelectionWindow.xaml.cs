using System.Windows;
using System.Windows.Controls;

namespace ULT;

public partial class IndexSelectionWindow : Window
{
    public int SelectedIndex { get; private set; } = -1;

    public int SelectedOrigIndex { get; private set; } = -1;
    public int SelectedTransIndex { get; private set; } = -1;

    public bool ApplyToAll => ChkApplyToAll.IsChecked == true;

    private readonly bool _isDual;

    public IndexSelectionWindow(string prompt, IEnumerable<string> items, bool isBatchOpen = false)
    {
        InitializeComponent();
        _isDual = false;

        LblPromptOrig.Text = prompt;

        ColHeaderSep.Width = new GridLength(0);
        ColHeaderTrans.Width = new GridLength(0);
        ColListSep.Width = new GridLength(0);
        ColListTrans.Width = new GridLength(0);
        ListBoxTrans.Visibility = Visibility.Collapsed;
        LblPromptTrans.Visibility = Visibility.Collapsed;

        Width = 350;

        if (isBatchOpen)
        {
            ChkApplyToAll.Visibility = Visibility.Visible;
            Height = 300;
        }

        ListBoxOrig.ItemsSource = items.ToList();
    }

    public IndexSelectionWindow(string promptOrig, string promptTrans, IEnumerable<string> items, bool isBatchOpen = false)
    {
        InitializeComponent();
        _isDual = true;

        LblPromptOrig.Text = promptOrig;
        LblPromptTrans.Text = promptTrans;

        if (isBatchOpen)
        {
            ChkApplyToAll.Visibility = Visibility.Visible;
            Height = 420;
        }

        var itemList = items.ToList();
        ListBoxOrig.ItemsSource = itemList;
        ListBoxTrans.ItemsSource = itemList;
    }

    private void ListBoxOrig_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxOrig.SelectedItem is string selected)
        {
            int idx = ParseIndex(selected);
            if (_isDual)
                SelectedOrigIndex = idx;
            else
                SelectedIndex = idx;
        }
        UpdateOkButton();
    }

    private void ListBoxTrans_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxTrans.SelectedItem is string selected)
            SelectedTransIndex = ParseIndex(selected);

        UpdateOkButton();
    }

    private void UpdateOkButton()
    {
        BtnOk.IsEnabled = _isDual
            ? (SelectedOrigIndex >= 0 && SelectedTransIndex >= 0)
            : (SelectedIndex >= 0);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;

    private static int ParseIndex(string item)
    {
        var colonIndex = item.IndexOf(':');
        if (colonIndex > 0 && int.TryParse(item.Substring(0, colonIndex).Trim(), out int index))
            return index;
        return -1;
    }
}