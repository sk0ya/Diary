using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;

namespace Diary.App;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    private bool _browseOpen;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        FolderTextBox.Text = current.RootDirectory ?? DailyNoteService.DefaultRootDirectory;
        AlwaysVisibleCheckBox.IsChecked = current.AlwaysVisible;
        Result = current;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_browseOpen) return;
        Dispatcher.BeginInvoke(CommitAndClose);
    }

    private void CommitAndClose()
    {
        if (!IsVisible) return;
        Result = new AppSettings
        {
            RootDirectory = string.IsNullOrWhiteSpace(FolderTextBox.Text)
                ? null
                : FolderTextBox.Text.Trim(),
            AlwaysVisible = AlwaysVisibleCheckBox.IsChecked == true
        };
        Close();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        _browseOpen = true;
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "日記の保存フォルダを選択してください",
                SelectedPath = FolderTextBox.Text,
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                FolderTextBox.Text = dialog.SelectedPath;
        }
        finally
        {
            _browseOpen = false;
            Activate();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CommitAndClose();
}
