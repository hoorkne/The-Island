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


namespace RioIsland;

public partial class MainWindow : Window
{
    private const double CompactWidth = 260, CompactHeight = 56;
    private const double ExpandedWidth = 460, ExpandedHeight = 472;
    private readonly NativeMetrics _metrics = new();
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromSeconds(1) };
    private System.Windows.Forms.NotifyIcon? _tray;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _expanded;

    private int _tick;
    private string? _clipboardValue;
    private List<LyricLine> _lyrics = new();
    private TimeSpan _remaining = TimeSpan.FromMinutes(25);
    private DateTime _deadline;
    private bool _timerRunning;

    private readonly MediaSessionService _media;
    private readonly DispatcherTimer _positionPulse = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _closing;
    private int _animationVersion;
    private string _lyricTrackKey = "";
    private DateTime _lastInteraction = DateTime.UtcNow;
    private DateTime _nextBlink = DateTime.UtcNow.AddSeconds(5);
    private bool _sleeping;
    private static bool MotionEnabled => SystemParameters.ClientAreaAnimation &&
        (System.Windows.Media.RenderCapability.Tier >> 16) > 0 && !SystemParameters.IsRemoteSession;

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
        _media = new MediaSessionService(Dispatcher);
        _media.Changed += Media_Changed;
        _positionPulse.Tick += (_, _) => UpdatePosition();
        PreviewMouseDown += (_, _) => { if (_sleeping) WakeMascot(true); else _lastInteraction = DateTime.UtcNow; };
        PreviewKeyDown += (_, e) => { _lastInteraction = DateTime.UtcNow; if (e.Key == Key.Escape) ToggleIsland(false); };
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
        WakeMascot(true);
        await _media.StartAsync();
    }

    private void PositionAtTop()
    {
        var workArea = SystemParameters.WorkArea; // DPI-aware, primary display
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + 8;
    }

    private void ToggleIsland(bool? expand = null)
    {
        if (_closing) return;
        _expanded = expand ?? !_expanded;
        WakeMascot(true);
        CollapseButton.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        int version = ++_animationVersion;
        ExpandedView.IsHitTestVisible = _expanded;
        if (_expanded)
        {
            ExpandedView.Visibility = Visibility.Visible;
            UpdateMetrics();
            UpdatePosition();
        }
        double targetOpacity = _expanded ? 1 : 0;
        if (MotionEnabled)
        {
            var fade = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(_expanded ? 240 : 120));
            fade.Completed += (_, _) =>
            {
                if (version != _animationVersion || _closing) return;
                ExpandedView.BeginAnimation(OpacityProperty, null);
                ExpandedView.Opacity = targetOpacity;
                if (!_expanded) ExpandedView.Visibility = Visibility.Collapsed;
            };
            ExpandedView.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            ExpandedView.BeginAnimation(OpacityProperty, null);
            ExpandedView.Opacity = targetOpacity;
            ExpandedView.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        var area = SystemParameters.WorkArea;
        AnimateSize(Math.Min(_expanded ? ExpandedWidth : CompactWidth, area.Width - 16),
            Math.Min(_expanded ? ExpandedHeight : CompactHeight, area.Height - 16));
        UpdatePositionTimer();
    }

    private void AnimateSize(double width, double height)
    {
        if (!MotionEnabled)
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Width = width; Height = height;
            return;
        }
        var ease = new BackEase { Amplitude = 0.12, EasingMode = EasingMode.EaseOut };
        AnimateDimension(WidthProperty, width, ease);
        AnimateDimension(HeightProperty, height, ease);
    }

    private void AnimateDimension(DependencyProperty property, double target, IEasingFunction ease)
    {
        double current = (double)GetValue(property);
        BeginAnimation(property, null);
        SetValue(property, target);
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(320))
        { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, 60);
        BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void CompactView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded) ToggleIsland(true);
    }
    private void ClosePanel_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ToggleIsland(false);
    }

    private void Pulse_Tick(object? sender, EventArgs e)
    {
        _tick++;
        if (_expanded && _tick % 2 == 0) UpdateMetrics();
        if (_timerRunning) UpdateTimer();
        UpdateMascot();
        if (!_media.HasSession && _tick % 10 == 0)
            CompactStatus.Text = _sleeping ? "Resting · click to wake" : $"{DateTime.Now:HH:mm} · Click to expand";
    }

    private void WakeMascot(bool greet)
    {
        _lastInteraction = DateTime.UtcNow;
        _sleeping = false;
        Mascot.ToolTip = greet ? "Hello. Make yourself at home." : "The Island is awake";
        AnimateEyes(greet ? 1.2 : 1, greet);
        _nextBlink = DateTime.UtcNow.AddSeconds(Random.Shared.Next(4, 9));
        if (greet && !_media.HasSession)
            CompactStatus.Text = DateTime.Now.Hour < 12 ? "Good morning" :
                DateTime.Now.Hour < 18 ? "Good afternoon" : "Good evening";
    }

    private void UpdateMascot()
    {
        bool sleep = !_media.IsPlaying && !_timerRunning && !_expanded &&
            DateTime.UtcNow - _lastInteraction > TimeSpan.FromSeconds(45);
        if (sleep != _sleeping)
        {
            _sleeping = sleep;
            AnimateEyes(sleep ? 0.16 : 1, false);
            Mascot.ToolTip = sleep ? "Resting · click to wake" : "The Island is awake";
        }
        if (!sleep && DateTime.UtcNow >= _nextBlink)
        {
            AnimateEyes(0.1, true);
            _nextBlink = DateTime.UtcNow.AddSeconds(Random.Shared.Next(4, 9));
        }
    }

    private void AnimateEyes(double scale, bool returnToOpen)
    {
        foreach (var eye in new[] { LeftEye, RightEye })
        {
            eye.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            eye.ScaleY = returnToOpen ? 1 : scale;
            if (!MotionEnabled) continue;
            var animation = new DoubleAnimation(returnToOpen ? 1 : (scale < 1 ? 1 : 0.16), scale,
                TimeSpan.FromMilliseconds(returnToOpen ? 120 : 240))
            { AutoReverse = returnToOpen, FillBehavior = FillBehavior.Stop,
              EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            eye.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animation);
        }
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

    private void Media_Changed(object? sender, EventArgs e)
    {
        if (_closing) return;
        SongTitle.Text = _media.Title;
        SongArtist.Text = _media.Artist;
        TrackArtwork.Source = _media.Artwork;
        ArtworkFallback.Visibility = _media.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Content = _media.IsPlaying ? "\uE769" : "\uE768";
        PreviousButton.IsEnabled = _media.CanPrevious;
        PlayButton.IsEnabled = _media.CanToggle;
        NextButton.IsEnabled = _media.CanNext;
        CompactStatus.Text = _media.HasSession ? _media.Title : "Click to expand";
        if (_lyrics.Count > 0 && _lyricTrackKey != _media.TrackKey)
        {
            _lyrics.Clear();
            _lyricTrackKey = "";
            LyricsText.Text = "Track changed · load its .lrc";
        }
        UpdatePosition();
        UpdatePositionTimer();
        UpdateMascot();
    }

    private void UpdatePositionTimer()
    {
        if (_expanded && _media.IsPlaying && _media.Duration > TimeSpan.Zero && !_closing)
            _positionPulse.Start();
        else _positionPulse.Stop();
    }

    private void UpdatePosition()
    {
        var duration = _media.Duration;
        var position = _media.Position;
        TrackProgress.Value = duration > TimeSpan.Zero ? position.TotalSeconds / duration.TotalSeconds : 0;
        ElapsedText.Text = duration > TimeSpan.Zero ? FormatTime(position) : "--:--";
        DurationText.Text = duration > TimeSpan.Zero ? FormatTime(duration) : "--:--";
        if (_lyrics.Count > 0)
            LyricsText.Text = duration > TimeSpan.Zero
                ? LrcLyrics.AtTime(_lyrics, position) : "This player does not share a usable timeline";
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private async Task RunMediaCommandAsync(string command)
    {
        if (!await _media.CommandAsync(command) && !_closing)
            CompactStatus.Text = "This player could not complete the command";
    }
    private async void Previous_Click(object sender, RoutedEventArgs e) => await RunMediaCommandAsync("previous");
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await RunMediaCommandAsync("toggle");
    private async void Next_Click(object sender, RoutedEventArgs e) => await RunMediaCommandAsync("next");

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
                _tray?.ShowBalloonTip(3000, "The Island", "Focus session complete!", System.Windows.Forms.ToolTipIcon.Info);
            }
        }
        int totalSeconds = Math.Max(0, (int)Math.Ceiling(_remaining.TotalSeconds));
        TimerText.Text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private async void LoadLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_media.TrackKey))
        {
            LyricsText.Text = "Start a track before loading its .lrc";
            return;
        }
        string track = _media.TrackKey;
        var dialog = new OpenFileDialog
        {
            Title = "Load synced LRC lyrics for the current track",
            Filter = "LRC lyrics (*.lrc)|*.lrc"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024)
            {
                LyricsText.Text = "Choose an LRC file smaller than 1 MB";
                return;
            }
            string text = await File.ReadAllTextAsync(dialog.FileName);
            var parsed = LrcLyrics.Parse(text);
            if (_closing) return;
            if (track != _media.TrackKey)
            {
                LyricsText.Text = "Track changed · load its .lrc";
                return;
            }
            _lyrics = parsed;
            _lyricTrackKey = track;
            LyricsText.Text = parsed.Count == 0 ? "No timed lyric lines found" : "Waiting for the first line";
            UpdatePosition();
        }
        catch (Exception ex) { if (!_closing) LyricsText.Text = $"Cannot read lyrics: {ex.Message}"; }
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
            Text = "The Island",
            Visible = true
        };
        // Preserve an existing startup opt-in after the executable rename.
        try { if (IsAutoStartEnabled()) SetAutoStart(true); }
        catch (Exception) { /* A locked registry must not prevent launching the island. */ }
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
                System.Windows.MessageBox.Show(ex.Message, "The Island startup", MessageBoxButton.OK, MessageBoxImage.Information);
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
        return key?.GetValue("TheIsland") is string || key?.GetValue("RIOIsland") is string;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
            ?? throw new InvalidOperationException("Cannot open Startup registry key.");
        if (!enable)
        {
            key.DeleteValue("TheIsland", false);
            key.DeleteValue("RIOIsland", false);
            return;
        }
        string exe = Environment.ProcessPath ?? "";
        if (!string.Equals(Path.GetFileName(exe), "TheIsland.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publish the app to TheIsland.exe before enabling startup.");
        key.SetValue("TheIsland", $"\"{exe}\"");
        key.DeleteValue("RIOIsland", false); // Migrate an existing opt-in; never create a second entry.
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _animationVersion++;
        _pulse.Stop();
        _positionPulse.Stop();
        _media.Changed -= Media_Changed;
        _media.Dispose();
        if (_handle != IntPtr.Zero) UnregisterHotKey(_handle, HotKeyId);
        if (_source is not null) _source.RemoveHook(HandleMessages);
        if (_tray is not null)
        {
            _tray.Visible = false;
            var menu = _tray.ContextMenuStrip;
            _tray.Dispose();
            menu?.Dispose();
        }
    }
}
