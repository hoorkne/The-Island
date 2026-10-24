using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.Media.Control;

namespace RioIsland;

public partial class MainWindow : Window
{
    private const double CompactWidth = 238, CompactHeight = 46;
    private const double ExpandedWidth = 490, ExpandedHeight = 402;
    private readonly NativeMetrics _metrics = new();
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromSeconds(1) };
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private System.Windows.Forms.NotifyIcon? _tray;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _expanded;
    private bool _refreshing;
    private int _tick;
    private string? _clipboardValue;
    private List<LyricLine> _lyrics = new();
    private TimeSpan _remaining = TimeSpan.FromMinutes(25);
    private DateTime _deadline;
    private bool _timerRunning;

    private const int HotKeyId = 0x5249;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001, ModControl = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => PositionAtTop();
        _pulse.Tick += Pulse_Tick;
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(HandleMessages);
        RegisterHotKey(_handle, HotKeyId, ModControl | ModAlt, 0x49); // Ctrl+Alt+I
    }

    private IntPtr HandleMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotKeyId)
        {
            ToggleIsland();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTop();
        InitializeTray();
        UpdateMetrics();
        _pulse.Start();
        try
        {
            _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            await RefreshMediaAsync();
        }
        catch (Exception)
        {
            SongTitle.Text = "Media unavailable";
            SongArtist.Text = "System media access could not start";
        }
    }

    private void PositionAtTop()
    {
        var workArea = SystemParameters.WorkArea; // DPI-aware, primary display
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + 8;
    }

    private void ToggleIsland(bool? expand = null)
    {
        _expanded = expand ?? !_expanded;
        ExpandedView.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        CompactView.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        AnimateSize(_expanded ? ExpandedWidth : CompactWidth, _expanded ? ExpandedHeight : CompactHeight);
    }

    private void AnimateSize(double width, double height)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
    }

    private void CompactView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ToggleIsland(true);
    private void ClosePanel_Click(object sender, RoutedEventArgs e) => ToggleIsland(false);

    private async void Pulse_Tick(object? sender, EventArgs e)
    {
        _tick++;
        UpdateMetrics();
        UpdateTimer();
        UpdateLyrics();
        if (_tick % 3 == 0 && !_refreshing)
            await RefreshMediaAsync();
        if (_tick % 10 == 0 && !_expanded)
            CompactStatus.Text = $"{DateTime.Now:HH:mm} · Click to expand";
    }

    private void UpdateMetrics()
    {
        double cpu = _metrics.CpuPercent();
        var ram = _metrics.RamUsage();
        CpuText.Text = $"CPU  {cpu:0}%";
        CpuBar.Value = cpu;
        RamText.Text = $"RAM  {ram.UsedGb:0.0}/{ram.TotalGb:0.0} GB";
        RamBar.Value = ram.Percent;
    }

    private async Task RefreshMediaAsync()
    {
        if (_mediaManager is null || _refreshing) return;
        _refreshing = true;
        try
        {
            _session = _mediaManager.GetCurrentSession();
            if (_session is null)
            {
                SongTitle.Text = "No media playing";
                SongArtist.Text = "Play something in Spotify or browser";
                PlayButton.Content = "▶";
                return;
            }
            var media = await _session.TryGetMediaPropertiesAsync();
            SongTitle.Text = string.IsNullOrWhiteSpace(media?.Title) ? "Unknown track" : media.Title;
            SongArtist.Text = string.IsNullOrWhiteSpace(media?.Artist) ? "Windows Media Session" : media.Artist;
            var playback = _session.GetPlaybackInfo();
            bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PlayButton.Content = playing ? "⏸" : "▶";
            CompactStatus.Text = SongTitle.Text;
        }
        catch (Exception)
        {
            SongTitle.Text = "Media not available";
            SongArtist.Text = "This app may not expose media controls";
            _session = null;
        }
        finally { _refreshing = false; }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        try { if (_session is not null) await _session.TrySkipPreviousAsync(); }
        catch (Exception) { /* Unsupported by active player */ }
        await RefreshMediaAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); }
        catch (Exception) { /* Unsupported by active player */ }
        await RefreshMediaAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        try { if (_session is not null) await _session.TrySkipNextAsync(); }
        catch (Exception) { /* Unsupported by active player */ }
        await RefreshMediaAsync();
    }

    private void Timer_Click(object sender, RoutedEventArgs e)
    {
        if (_timerRunning)
        {
            _remaining = _deadline - DateTime.UtcNow;
            if (_remaining < TimeSpan.Zero) _remaining = TimeSpan.Zero;
            _timerRunning = false;
        }
        else
        {
            if (_remaining <= TimeSpan.Zero) _remaining = TimeSpan.FromMinutes(25);
            _deadline = DateTime.UtcNow + _remaining;
            _timerRunning = true;
        }
        TimerButton.Content = _timerRunning ? "Pause" : "Start";
        UpdateTimer();
    }

    private void ResetTimer_Click(object sender, RoutedEventArgs e)
    {
        _timerRunning = false;
        _remaining = TimeSpan.FromMinutes(25);
        TimerButton.Content = "Start";
        TimerText.Text = "25:00";
    }

    private void UpdateTimer()
    {
        if (_timerRunning)
        {
            _remaining = _deadline - DateTime.UtcNow;
            if (_remaining <= TimeSpan.Zero)
            {
                _timerRunning = false;
                _remaining = TimeSpan.Zero;
                TimerButton.Content = "Start";
                CompactStatus.Text = "✓ Focus session complete!";
                _tray?.ShowBalloonTip(3000, "RIO Island", "Focus session complete!", System.Windows.Forms.ToolTipIcon.Info);
            }
        }
        int totalSeconds = Math.Max(0, (int)Math.Ceiling(_remaining.TotalSeconds));
        TimerText.Text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void LoadLyrics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load synced LRC lyrics",
            Filter = "LRC lyrics (*.lrc)|*.lrc|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _lyrics = LrcLyrics.Parse(File.ReadAllText(dialog.FileName));
            LyricsText.Text = _lyrics.Count == 0 ? "No timed lyric lines found" : $"✓ Loaded {_lyrics.Count} lyric lines";
        }
        catch (Exception ex)
        {
            LyricsText.Text = $"Cannot read lyrics: {ex.Message}";
        }
    }

    private void UpdateLyrics()
    {
        if (_lyrics.Count == 0 || _session is null) return;
        try { LyricsText.Text = LrcLyrics.AtTime(_lyrics, _session.GetTimelineProperties().Position); }
        catch (Exception) { /* Some players don't share position */ }
    }

    private void CaptureClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText()) { ClipboardText.Text = "No text in clipboard"; return; }
            _clipboardValue = Clipboard.GetText();
            ClipboardText.Text = _clipboardValue.Length > 72 ? _clipboardValue[..72].Replace('\n', ' ') + "…" : _clipboardValue.Replace('\n', ' ');
        }
        catch (Exception) { ClipboardText.Text = "Clipboard is busy or unavailable"; }
    }

    private void RestoreClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboardValue is null) return;
        try { Clipboard.SetText(_clipboardValue); }
        catch (Exception) { ClipboardText.Text = "Clipboard is busy or unavailable"; }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            ShelfText.Text = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} files dropped";
            ToggleIsland(true);
        }
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e) => ShelfText.Text = "No file dropped";

    private void InitializeTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "RIO Island",
            Visible = true
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Expand island", null, (_, _) => Dispatcher.Invoke(() => ToggleIsland(true)));
        var autoStart = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = IsAutoStartEnabled(),
            CheckOnClick = false
        };
        autoStart.Click += (_, _) =>
        {
            try
            {
                bool newValue = !IsAutoStartEnabled();
                SetAutoStart(newValue);
                autoStart.Checked = IsAutoStartEnabled();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "RIO Island startup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        menu.Items.Add(autoStart);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => ToggleIsland(true));
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("RIOIsland") is string;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
            ?? throw new InvalidOperationException("Cannot open Startup registry key.");
        if (!enable) { key.DeleteValue("RIOIsland", false); return; }
        string exe = Environment.ProcessPath ?? "";
        if (!string.Equals(Path.GetFileName(exe), "RioIsland.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publish the app to RioIsland.exe before enabling startup.");
        key.SetValue("RIOIsland", $"\"{exe}\"");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _pulse.Stop();
        if (_handle != IntPtr.Zero) UnregisterHotKey(_handle, HotKeyId);
        if (_source is not null) _source.RemoveHook(HandleMessages);
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
    }
}
