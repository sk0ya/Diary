using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DrawingIcon = System.Drawing.Icon;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using Screen = System.Windows.Forms.Screen;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using TrayContextMenuStrip = System.Windows.Forms.ContextMenuStrip;

namespace Dialy.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan EdgeHoldDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan RevealCooldown = TimeSpan.FromMilliseconds(900);

    private MainWindow? _hiddenMainWindow;
    private DispatcherTimer? _edgeWatcher;
    private NotifyIcon? _trayIcon;
    private CursorPoint? _lastCursor;
    private ArmedEdge? _armedEdge;
    private DateTimeOffset _lastRevealAt = DateTimeOffset.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _hiddenMainWindow = new MainWindow();
        MainWindow = _hiddenMainWindow;
        _hiddenMainWindow.Closed += (_, _) => Shutdown();

        InitializeTrayIcon();

        _edgeWatcher = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _edgeWatcher.Tick += EdgeWatcher_OnTick;
        _edgeWatcher.Start();
    }

    private void EdgeWatcher_OnTick(object? sender, EventArgs e)
    {
        if (_hiddenMainWindow is null || !GetCursorPos(out var cursor))
        {
            return;
        }

        if (_hiddenMainWindow.IsVisible)
        {
            ResetEdgeState(cursor);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastRevealAt < RevealCooldown)
        {
            _lastCursor = cursor;
            return;
        }

        var screen = Screen.FromPoint(new DrawingPoint(cursor.X, cursor.Y));
        var edge = DetectEdge(screen.Bounds, cursor);
        if (edge is null)
        {
            ResetEdgeState(cursor);
            return;
        }

        if (_armedEdge is { } armed &&
            armed.Edge == edge &&
            string.Equals(armed.ScreenDeviceName, screen.DeviceName, StringComparison.Ordinal))
        {
            if (now - armed.ArmedAt >= EdgeHoldDuration)
            {
                _lastRevealAt = now;
                RevealOnScreen(screen, cursor);
                ResetEdgeState(cursor);
                return;
            }
        }
        else if (IsMovingTowardEdge(_lastCursor, cursor, edge.Value))
        {
            _armedEdge = new ArmedEdge(edge.Value, screen.DeviceName, now);
        }

        _lastCursor = cursor;
    }

    private void InitializeTrayIcon()
    {
        var menu = new TrayContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("表示", null, (_, _) => Dispatcher.Invoke(RevealAtCursor)));
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => Dispatcher.Invoke(ExitApplication)));

        _trayIcon = new NotifyIcon
        {
            Text = "Dialy",
            Icon = DrawingIcon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RevealAtCursor);
    }

    private void RevealAtCursor()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var screen = Screen.FromPoint(new DrawingPoint(cursor.X, cursor.Y));
        RevealOnScreen(screen, cursor);
    }

    private void RevealOnScreen(Screen screen, CursorPoint cursor)
    {
        _hiddenMainWindow?.Reveal(screen.WorkingArea, cursor.X);
    }

    private void ExitApplication()
    {
        _edgeWatcher?.Stop();
        _hiddenMainWindow?.Close();
        if (_hiddenMainWindow is null)
        {
            Shutdown();
        }
    }

    private void ResetEdgeState(CursorPoint cursor)
    {
        _armedEdge = null;
        _lastCursor = cursor;
    }

    private static ScreenEdge? DetectEdge(DrawingRectangle bounds, CursorPoint cursor)
    {
        if (cursor.Y <= bounds.Top)
        {
            return ScreenEdge.Top;
        }

        if (cursor.Y >= bounds.Bottom - 1)
        {
            return ScreenEdge.Bottom;
        }

        if (cursor.X <= bounds.Left)
        {
            return ScreenEdge.Left;
        }

        if (cursor.X >= bounds.Right - 1)
        {
            return ScreenEdge.Right;
        }

        return null;
    }

    private static bool IsMovingTowardEdge(CursorPoint? previous, CursorPoint current, ScreenEdge edge)
    {
        if (previous is null)
        {
            return true;
        }

        return edge switch
        {
            ScreenEdge.Top => current.Y <= previous.Value.Y,
            ScreenEdge.Bottom => current.Y >= previous.Value.Y,
            ScreenEdge.Left => current.X <= previous.Value.X,
            ScreenEdge.Right => current.X >= previous.Value.X,
            _ => false
        };
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint lpPoint);

    private readonly record struct ArmedEdge(ScreenEdge Edge, string ScreenDeviceName, DateTimeOffset ArmedAt);

    private enum ScreenEdge
    {
        Top,
        Right,
        Bottom,
        Left
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _edgeWatcher?.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        base.OnExit(e);
    }
}
