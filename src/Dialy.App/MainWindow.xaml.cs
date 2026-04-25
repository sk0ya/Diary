using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Editor.Controls;
using Editor.Controls.Themes;
using DrawingRectangle = System.Drawing.Rectangle;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace Dialy.App;

public partial class MainWindow : Window
{
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");

    private readonly DailyNoteService _dailyNoteService = new();
    private readonly DispatcherTimer _autoSaveTimer;

    private DateOnly? _loadedDate;
    private DateTimeOffset? _lastSavedAt;
    private DateTimeOffset _ignoreDeactivateUntil = DateTimeOffset.MinValue;
    private bool _isClosing;
    private bool _isHiding;

    public MainWindow()
    {
        InitializeComponent();

        EditorHost.SetTheme(EditorTheme.Nord.WithAccent(MediaColor.FromRgb(0xDA, 0x8A, 0x67)));
        EditorHost.BufferChanged += EditorHost_OnBufferChanged;
        EditorHost.SaveRequested += EditorHost_OnSaveRequested;
        EditorHost.QuitRequested += EditorHost_OnQuitRequested;

        Deactivated += MainWindow_OnDeactivated;
        Closing += MainWindow_OnClosing;

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_OnTick;

        EntryDateText.Text = "今日の日記";
        UpdateStatusChrome();
    }

    public void Reveal(DrawingRectangle workingArea, int cursorX)
    {
        EnsureTodayNoteLoaded();
        PositionWindow(workingArea, cursorX);

        _ignoreDeactivateUntil = DateTimeOffset.UtcNow.AddMilliseconds(700);

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        FocusEditor();
    }

    private void EnsureTodayNoteLoaded()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayPath = _dailyNoteService.EnsureEntry(today);
        var currentPath = EditorHost.FilePath;

        if (!string.Equals(currentPath, todayPath, StringComparison.OrdinalIgnoreCase))
        {
            SaveCurrentEntry();
            EditorHost.LoadFile(todayPath);
        }

        _loadedDate = today;
        _lastSavedAt = File.Exists(todayPath)
            ? new DateTimeOffset(File.GetLastWriteTime(todayPath))
            : DateTimeOffset.Now;

        UpdateEntryChrome(today, todayPath);
        UpdateStatusChrome();
    }

    private void PositionWindow(DrawingRectangle workingArea, int cursorX)
    {
        Width = Math.Clamp(workingArea.Width * 0.28, 360, 460);
        Height = Math.Clamp(workingArea.Height * 0.9, 620, 1040);
        Top = workingArea.Top + Math.Max(12, workingArea.Height * 0.03);

        var alignRight = cursorX >= workingArea.Left + (workingArea.Width / 2);
        Left = alignRight
            ? workingArea.Right - Width - 18
            : workingArea.Left + 18;
    }

    private void FocusEditor()
    {
        Dispatcher.BeginInvoke(() =>
        {
            EditorHost.Focus();
            Keyboard.Focus(EditorHost);
        }, DispatcherPriority.Input);
    }

    private void AutoSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        SaveCurrentEntry();
    }

    private void EditorHost_OnBufferChanged(object? sender, EventArgs e)
    {
        UpdateStatusChrome();
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void EditorHost_OnSaveRequested(object? sender, SaveRequestedEventArgs e)
    {
        var targetPath = e.FilePath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = EditorHost.Engine.CurrentBuffer.FilePath ??
                         _dailyNoteService.EnsureEntry(DateOnly.FromDateTime(DateTime.Now));
        }

        SaveBufferTo(targetPath);
    }

    private void EditorHost_OnQuitRequested(object? sender, QuitRequestedEventArgs e)
    {
        HideToStandby();
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        if (_isClosing || _isHiding || DateTimeOffset.UtcNow < _ignoreDeactivateUntil)
        {
            return;
        }

        HideToStandby();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _autoSaveTimer.Stop();

        if (!SaveCurrentEntry())
        {
            _isClosing = false;
            e.Cancel = true;
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            HideToStandby();
            return;
        }

        DragMove();
    }

    private void HideToStandby()
    {
        if (_isClosing || _isHiding || !IsVisible)
        {
            return;
        }

        _autoSaveTimer.Stop();
        if (!SaveCurrentEntry())
        {
            return;
        }

        _isHiding = true;
        try
        {
            Hide();
        }
        finally
        {
            _isHiding = false;
        }
    }

    private bool SaveCurrentEntry()
    {
        var filePath = EditorHost.Engine.CurrentBuffer.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return true;
        }

        if (!EditorHost.Engine.CurrentBuffer.Text.IsModified)
        {
            UpdateStatusChrome();
            return true;
        }

        return SaveBufferTo(filePath);
    }

    private bool SaveBufferTo(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EditorHost.OnSaveStarted();
            try
            {
                EditorHost.Engine.CurrentBuffer.Save(path);
            }
            finally
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => EditorHost.OnSaveFinished());
            }

            _lastSavedAt = DateTimeOffset.Now;
            if (_loadedDate is null)
            {
                _loadedDate = DateOnly.FromDateTime(DateTime.Now);
            }

            UpdateEntryChrome(_loadedDate.Value, EditorHost.Engine.CurrentBuffer.FilePath ?? path);
            UpdateStatusChrome();
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"保存に失敗しました。{Environment.NewLine}{ex.Message}", "Dialy", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateStatusChrome();
            return false;
        }
    }

    private void UpdateEntryChrome(DateOnly date, string path)
    {
        var dateText = date.ToDateTime(TimeOnly.MinValue).ToString("yyyy年M月d日 dddd", JapaneseCulture);
        Title = $"Dialy | {date:yyyy-MM-dd}";
        EntryDateText.Text = dateText;
        LastSavedText.ToolTip = path;
    }

    private void UpdateStatusChrome()
    {
        var isModified = EditorHost.Engine.CurrentBuffer.Text.IsModified;
        StateBadge.Background = (MediaBrush)FindResource(isModified ? "WarningBrush" : "SuccessBrush");
        StateBadgeText.Text = isModified ? "編集中" : "保存済み";
        LastSavedText.Text = _lastSavedAt is null
            ? "最終保存なし"
            : $"最終保存 {_lastSavedAt.Value.LocalDateTime:HH:mm:ss}";
    }
}
