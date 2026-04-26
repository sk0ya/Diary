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
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");

    private AppSettings _settings = AppSettings.Load();
    private DailyNoteService _dailyNoteService;
    private readonly DispatcherTimer _autoSaveTimer;
    private VimEditorControl EditorHost { get; }

    private DateOnly? _loadedDate;
    private DateTimeOffset _ignoreDeactivateUntil = DateTimeOffset.MinValue;
    private bool _isClosing;
    private bool _isHiding;
    private DateOnly _calendarMonth = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);

    public MainWindow()
    {
        InitializeComponent();

        _dailyNoteService = new DailyNoteService(_settings.RootDirectory);

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
        _calendarMonth = new DateOnly(today.Year, today.Month, 1);
        UpdateEntryChrome(today, todayPath);

        BuildCalendar();
    }

    private void PositionWindow(DrawingRectangle workingArea, int cursorX)
    {
        Width = Math.Clamp(workingArea.Width * 0.28, 360, 460);
        Height = workingArea.Height;
        Top = workingArea.Top;

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

    private void HideToStandby()
    {
        if (_isClosing || _isHiding || !IsVisible || _settings.AlwaysVisible)
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

            if (_loadedDate is null)
            {
                _loadedDate = DateOnly.FromDateTime(DateTime.Now);
            }

            UpdateEntryChrome(_loadedDate.Value, EditorHost.Engine.CurrentBuffer.FilePath ?? path);
    
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

        var path = _dailyNoteService.EnsureEntry(date);
        EditorHost.LoadFile(path);
        _loadedDate = date;

        UpdateEntryChrome(date, path);

        BuildCalendar();
        FocusEditor();
    }

    private void CalPrevMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(-1);
        BuildCalendar();
    }

    private void CalNextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_calendarMonth < new DateOnly(today.Year, today.Month, 1))
        {
            _calendarMonth = _calendarMonth.AddMonths(1);
            BuildCalendar();
        }
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

    private void BuildCalendar()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        CalMonthText.Text = _calendarMonth.ToString("yyyy年M月", JapaneseCulture);
        CalNextMonthButton.IsEnabled = _calendarMonth < new DateOnly(today.Year, today.Month, 1);

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
        var subtleBrush = (SolidColorBrush)FindResource("TextSubtleBrush");

        var number = new TextBlock
        {
            Text                = date.Day.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily          = new FontFamily("Cascadia Code"),
            FontSize            = 12,
            FontWeight          = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground          = isToday ? (SolidColorBrush)FindResource("SuccessBrush") : hasEntry ? accent : (isFuture ? subtleBrush : textBrush),
            Opacity             = isFuture ? 0.25 : (isToday || hasEntry ? 1.0 : 0.35)
        };

        FrameworkElement content;
        if (hasEntry)
        {
            var dot = new Border
            {
                Width               = 4, Height = 4,
                Background          = accent,
                CornerRadius        = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 2, 0, 0)
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
            IsEnabled  = isToday || hasEntry,
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

}
