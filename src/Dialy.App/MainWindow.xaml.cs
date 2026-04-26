using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button            = System.Windows.Controls.Button;
using FontFamily        = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Editor.Controls;
using Editor.Controls.Themes;
using DrawingRectangle = System.Drawing.Rectangle;
using MediaColor = System.Windows.Media.Color;

namespace Dialy.App;

public partial class MainWindow : Window
{
    private enum TodoListMode
    {
        Daily,
        Backlog
    }

    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");
    private const double ScreenEdgeInset = 8;

    private AppSettings _settings = AppSettings.Load();
    private DailyNoteService _dailyNoteService;
    private BacklogTodoService _backlogTodoService;
    private readonly DispatcherTimer _autoSaveTimer;
    private VimEditorControl EditorHost { get; }

    private DateOnly? _loadedDate;
    private string? _backlogMarkdown;
    private DateTimeOffset _ignoreDeactivateUntil = DateTimeOffset.MinValue;
    private bool _isClosing;
    private bool _isHiding;
    private DateOnly _calendarMonth = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
    private TodoListMode _todoListMode = TodoListMode.Daily;

    public MainWindow()
    {
        InitializeComponent();

        _dailyNoteService = new DailyNoteService(_settings.RootDirectory);
        _backlogTodoService = new BacklogTodoService(_settings.RootDirectory);

        EditorHost = new VimEditorControl(VimEditorControlDefaults.CreateOptions());
        EditorHostContainer.Content = EditorHost;

        EditorHost.SetTheme(EditorTheme.Nord.WithAccent(MediaColor.FromRgb(0xDA, 0x8A, 0x67)));
        EditorHost.BufferChanged += EditorHost_OnBufferChanged;
        EditorHost.SaveRequested += EditorHost_OnSaveRequested;
        EditorHost.QuitRequested += EditorHost_OnQuitRequested;

        Closing += MainWindow_OnClosing;

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_OnTick;

        EntryDateText.Text = "今日の日記";

    }

    public void Reveal(DrawingRectangle workingArea, int cursorX)
    {
        if (_loadedDate is null || string.IsNullOrWhiteSpace(EditorHost.FilePath))
        {
            EnsureTodayNoteLoaded();
        }

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
        var todayPath = _dailyNoteService.PrepareEntry(today);
        var currentPath = EditorHost.FilePath;

        if (!string.Equals(currentPath, todayPath, StringComparison.OrdinalIgnoreCase))
        {
            SaveCurrentEntry();
            LoadEntryIntoEditor(today);
        }

        _loadedDate = today;
        _calendarMonth = new DateOnly(today.Year, today.Month, 1);
        UpdateEntryChrome(today, todayPath);
        RefreshTodoView();

        BuildCalendar();
    }

    private void PositionWindow(DrawingRectangle workingArea, int cursorX)
    {
        var shellMargin = ShellBorder.Margin;
        var shellWidth = Math.Clamp(workingArea.Width * 0.28, 360, 460);

        Width = shellWidth + shellMargin.Left + shellMargin.Right;
        Height = workingArea.Height + shellMargin.Top + shellMargin.Bottom;
        Top = workingArea.Top - shellMargin.Top;

        var alignRight = cursorX >= workingArea.Left + (workingArea.Width / 2);
        Left = alignRight
            ? workingArea.Right - ScreenEdgeInset - shellMargin.Left - shellWidth
            : workingArea.Left + ScreenEdgeInset - shellMargin.Left;
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

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
        RefreshTodoView();
    }

    private void EditorHost_OnSaveRequested(object? sender, SaveRequestedEventArgs e)
    {
        var targetPath = e.FilePath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = EditorHost.Engine.CurrentBuffer.FilePath ??
                         _dailyNoteService.GetEntryPath(DateOnly.FromDateTime(DateTime.Now));
        }

        SaveBufferTo(targetPath);
    }

    private void EditorHost_OnQuitRequested(object? sender, QuitRequestedEventArgs e)
    {
        HideToStandby();
    }

    private const int WM_ACTIVATEAPP = 0x001C;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ACTIVATEAPP && wParam == IntPtr.Zero &&
            !_isClosing && !_isHiding && !_settings.AlwaysVisible &&
            DateTimeOffset.UtcNow >= _ignoreDeactivateUntil)
        {
            Dispatcher.BeginInvoke(HideToStandby);
        }

        return IntPtr.Zero;
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

    private void EntryDateButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCalendarVisibility();
    }

    private void HideToStandby(bool force = false)
    {
        if (_isClosing || _isHiding || !IsVisible || (!force && _settings.AlwaysVisible))
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

        if (_loadedDate is { } loadedDate &&
            IsDailyEntryPath(filePath, loadedDate))
        {
            if (_dailyNoteService.ContentMatchesStored(loadedDate, EditorHost.Text))
            {
                EditorHost.Engine.CurrentBuffer.Text.MarkSaved();
                return true;
            }

            return SaveBufferTo(filePath);
        }

        if (!EditorHost.Engine.CurrentBuffer.Text.IsModified)
        {
            return true;
        }

        return SaveBufferTo(filePath);
    }

    private bool SaveBufferTo(string path)
    {
        try
        {
            if (_loadedDate is { } loadedDate &&
                IsDailyEntryPath(path, loadedDate) &&
                _dailyNoteService.IsTemplateContent(loadedDate, EditorHost.Text))
            {
                _dailyNoteService.DeleteEntry(loadedDate);
                EditorHost.Engine.CurrentBuffer.Text.MarkSaved();
                UpdateEntryChrome(loadedDate, path);
                BuildCalendar();
                return true;
            }

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

            if (_loadedDate is null)
            {
                _loadedDate = DateOnly.FromDateTime(DateTime.Now);
            }

            UpdateEntryChrome(_loadedDate.Value, EditorHost.Engine.CurrentBuffer.FilePath ?? path);
            BuildCalendar();
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"保存に失敗しました。{Environment.NewLine}{ex.Message}", "Dialy", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void NavigateToEntry(DateOnly date)
    {
        if (_loadedDate == date) return;

        _autoSaveTimer.Stop();
        SaveCurrentEntry();

        var path = _dailyNoteService.PrepareEntry(date);
        LoadEntryIntoEditor(date);
        _loadedDate = date;

        UpdateEntryChrome(date, path);
        RefreshTodoView();

        BuildCalendar();
        FocusEditor();
    }

    private void TodoAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddTodoFromInput();
    }

    private void TodoInputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        AddTodoFromInput();
    }

    private void AddTodoFromInput()
    {
        var todoText = TodoInputTextBox.Text.Trim();
        if (todoText.Length == 0)
        {
            return;
        }

        if (_todoListMode == TodoListMode.Daily)
        {
            EnsureDailyEntryLoaded();
            var updatedText = TodoSectionService.AddTodo(EditorHost.Text, todoText);
            ApplyDailyTodoDocumentChange(updatedText);
        }
        else
        {
            var updatedBacklog = TodoSectionService.AddTodo(LoadBacklogMarkdown(), todoText);
            SaveBacklogMarkdown(updatedBacklog);
            RefreshTodoView();
        }

        TodoInputTextBox.Clear();
    }

    private void TodoCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox checkBox ||
            checkBox.Tag is not int lineIndex ||
            checkBox.IsChecked is not bool isCompleted)
        {
            return;
        }

        if (!TodoSectionService.TrySetCompletion(EditorHost.Text, lineIndex, isCompleted, out var updatedText))
        {
            return;
        }

        ApplyDailyTodoDocumentChange(updatedText);
    }

    private void DailyModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetTodoListMode(TodoListMode.Daily);
    }

    private void BacklogModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetTodoListMode(TodoListMode.Backlog);
    }

    private void SetTodoListMode(TodoListMode mode)
    {
        if (_todoListMode == mode)
        {
            return;
        }

        _todoListMode = mode;
        RefreshTodoView();
    }

    private void BacklogMoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int lineIndex })
        {
            return;
        }

        EnsureDailyEntryLoaded();

        if (!TodoSectionService.TryRemoveTodo(LoadBacklogMarkdown(), lineIndex, out var updatedBacklog, out var todoText))
        {
            return;
        }

        SaveBacklogMarkdown(updatedBacklog);

        var updatedDailyText = TodoSectionService.AddTodo(EditorHost.Text, todoText);
        ApplyDailyTodoDocumentChange(updatedDailyText);
    }

    private void ApplyDailyTodoDocumentChange(string updatedText)
    {
        var currentText = EditorHost.Text ?? string.Empty;
        if (string.Equals(currentText, updatedText, StringComparison.Ordinal))
        {
            return;
        }

        var cursor = EditorHost.Engine.Cursor;
        EditorHost.SetText(updatedText);
        EditorHost.Engine.SetCursorPosition(cursor);

        _autoSaveTimer.Stop();
        RefreshTodoView();
        SaveCurrentEntry();
    }

    private void RefreshTodoView()
    {
        var dailyItems = TodoSectionService.Parse(EditorHost.Text);
        var backlogItems = TodoSectionService.Parse(LoadBacklogMarkdown());
        TodoListPanel.Children.Clear();

        UpdateTodoModeChrome(dailyItems, backlogItems);

        if (_todoListMode == TodoListMode.Daily)
        {
            RenderDailyItems(dailyItems);
            TodoEmptyText.Text = "Daily の TODO はまだありません";
            TodoEmptyText.Visibility = dailyItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            RenderBacklogItems(backlogItems);
            TodoEmptyText.Text = "Backlog はまだありません";
            TodoEmptyText.Visibility = backlogItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RenderDailyItems(IReadOnlyList<TodoItem> items)
    {
        foreach (var item in items)
        {
            var label = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = item.IsCompleted
                    ? (SolidColorBrush)FindResource("TextSubtleBrush")
                    : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null
            };

            var contentHost = new Border
            {
                MaxHeight = 32,
                ClipToBounds = true,
                Child = label
            };

            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content = contentHost,
                Tag = item.LineIndex,
                IsChecked = item.IsCompleted,
                Foreground = (SolidColorBrush)FindResource("AccentBrush"),
                Margin = new Thickness(0, 0, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            checkBox.Checked += TodoCheckBox_Changed;
            checkBox.Unchecked += TodoCheckBox_Changed;

            TodoListPanel.Children.Add(checkBox);
        }
    }

    private void RenderBacklogItems(IReadOnlyList<TodoItem> items)
    {
        foreach (var item in items)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = item.IsCompleted
                    ? (SolidColorBrush)FindResource("TextSubtleBrush")
                    : (SolidColorBrush)FindResource("TextBrush"),
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var contentHost = new Border
            {
                MaxHeight = 32,
                ClipToBounds = true,
                Child = label
            };

            var moveButton = new Button
            {
                Content = "Dailyへ",
                Tag = item.LineIndex,
                Style = (Style)FindResource("TodoRowActionButtonStyle"),
                Height = 24,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0)
            };
            moveButton.Click += BacklogMoveButton_Click;

            row.Children.Add(contentHost);
            Grid.SetColumn(moveButton, 1);
            row.Children.Add(moveButton);
            TodoListPanel.Children.Add(row);
        }
    }

    private void UpdateTodoModeChrome(IReadOnlyList<TodoItem> dailyItems, IReadOnlyList<TodoItem> backlogItems)
    {
        var completedCount = dailyItems.Count(static item => item.IsCompleted);
        DailyModeButton.Content = dailyItems.Count == 0
            ? "Daily 0/0"
            : $"Daily {completedCount}/{dailyItems.Count}";
        BacklogModeButton.Content = $"Backlog {backlogItems.Count}";

        UpdateTodoModeButtonState(DailyModeButton, _todoListMode == TodoListMode.Daily);
        UpdateTodoModeButtonState(BacklogModeButton, _todoListMode == TodoListMode.Backlog);
    }

    private void UpdateTodoModeButtonState(Button button, bool isActive)
    {
        button.Background = isActive
            ? new SolidColorBrush(MediaColor.FromArgb(0x30, 0xE0, 0x92, 0x6E))
            : new SolidColorBrush(Colors.Transparent);
        button.BorderBrush = (SolidColorBrush)FindResource(isActive ? "AccentBrush" : "StrokeBrush");
        button.Foreground = (SolidColorBrush)FindResource(isActive ? "TextBrush" : "TextSubtleBrush");
    }

    private void EnsureDailyEntryLoaded()
    {
        if (!string.IsNullOrWhiteSpace(EditorHost.FilePath))
        {
            return;
        }

        EnsureTodayNoteLoaded();
    }

    private string LoadBacklogMarkdown()
    {
        _backlogMarkdown ??= _backlogTodoService.Load();
        return _backlogMarkdown;
    }

    private void SaveBacklogMarkdown(string markdown)
    {
        _backlogMarkdown = markdown;
        _backlogTodoService.Save(markdown);
    }

    private void CalPrevMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void CalNextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(1);
        BuildCalendar();
    }

    private void TodayJumpButton_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _calendarMonth = new DateOnly(today.Year, today.Month, 1);
        if (_loadedDate == today)
            BuildCalendar();
        else
            NavigateToEntry(today);
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly date })
            NavigateToEntry(date);
    }

    private void ToggleCalendarVisibility()
    {
        var willShow = CalendarPanel.Visibility != Visibility.Visible;
        CalendarPanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;

        if (willShow)
        {
            BuildCalendar();
        }
    }

    private void BuildCalendar()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        CalMonthText.Text = _calendarMonth.ToString("yyyy年M月", JapaneseCulture);
        CalNextMonthButton.IsEnabled = true;

        var entries = _dailyNoteService.GetExistingEntries(_calendarMonth.Year, _calendarMonth.Month);
        CalDayGrid.Children.Clear();

        var firstDay = new DateOnly(_calendarMonth.Year, _calendarMonth.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_calendarMonth.Year, _calendarMonth.Month);
        var startDow = (int)firstDay.DayOfWeek;

        for (int i = 0; i < startDow; i++)
            CalDayGrid.Children.Add(new TextBlock());

        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateOnly(_calendarMonth.Year, _calendarMonth.Month, d);
            CalDayGrid.Children.Add(CreateDayButton(
                date,
                hasEntry:   entries.Contains(date),
                isFuture:   date > today,
                isToday:    date == today,
                isSelected: date == _loadedDate));
        }
    }

    private Button CreateDayButton(DateOnly date, bool hasEntry, bool isFuture, bool isToday, bool isSelected)
    {
        var accent     = new SolidColorBrush(MediaColor.FromRgb(0xE0, 0x92, 0x6E));
        var textBrush  = (SolidColorBrush)FindResource("TextBrush");

        var number = new TextBlock
        {
            Text                = date.Day.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily          = new FontFamily("Cascadia Code"),
            FontSize            = 12,
            FontWeight          = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground          = isToday ? (SolidColorBrush)FindResource("SuccessBrush") : hasEntry ? accent : textBrush,
            Opacity             = isFuture ? 0.62 : 1.0
        };

        FrameworkElement content;
        if (hasEntry)
        {
            var dot = new Border
            {
                Width               = 3, Height = 3,
                Background          = accent,
                CornerRadius        = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 1, 0, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(number);
            stack.Children.Add(dot);
            content = stack;
        }
        else
        {
            content = number;
        }

        var background = isSelected
            ? new SolidColorBrush(MediaColor.FromArgb(55, 0xE0, 0x92, 0x6E))
            : new SolidColorBrush(Colors.Transparent);

        var btn = new Button
        {
            Content    = content,
            Tag        = date,
            IsEnabled  = true,
            Background = background,
            Style      = (Style)FindResource("CalDayButtonStyle")
        };
        btn.Click += DayButton_Click;
        return btn;
    }

    private void UpdateEntryChrome(DateOnly date, string path)
    {
        var dateText = date.ToDateTime(TimeOnly.MinValue).ToString("yyyy年M月d日 dddd", JapaneseCulture);
        Title = $"Dialy | {date:yyyy-MM-dd}";
        EntryDateText.Text = dateText;
    }

    private void ApplySettings(AppSettings newSettings)
    {
        newSettings.Save();

        var folderChanged = !string.Equals(
            newSettings.RootDirectory ?? DailyNoteService.DefaultRootDirectory,
            _settings.RootDirectory ?? DailyNoteService.DefaultRootDirectory,
            StringComparison.OrdinalIgnoreCase);

        _settings = newSettings;

        if (folderChanged)
        {
            _autoSaveTimer.Stop();
            SaveCurrentEntry();
            _dailyNoteService = new DailyNoteService(_settings.RootDirectory);
            _backlogTodoService = new BacklogTodoService(_settings.RootDirectory);
            _backlogMarkdown = null;
            _loadedDate = null;
            EnsureTodayNoteLoaded();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _ignoreDeactivateUntil = DateTimeOffset.UtcNow.AddSeconds(5);

        var dialog = new SettingsWindow(_settings) { Owner = this };
        dialog.Closed += (_, _) => ApplySettings(dialog.Result);
        dialog.Show();
    }

    private void CloseViewButton_Click(object sender, RoutedEventArgs e)
    {
        HideToStandby(force: true);
    }

    private void LoadEntryIntoEditor(DateOnly date)
    {
        var path = _dailyNoteService.PrepareEntry(date);
        if (File.Exists(path))
        {
            EditorHost.LoadFile(path);
            return;
        }

        EditorHost.Engine.LoadFile(path);
        EditorHost.SetText(_dailyNoteService.LoadEntry(date));
    }

    private bool IsDailyEntryPath(string path, DateOnly date)
    {
        return string.Equals(
            path,
            _dailyNoteService.GetEntryPath(date),
            StringComparison.OrdinalIgnoreCase);
    }

}
