using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace RioIsland;

// All state and Changed notifications belong to the WPF dispatcher.
// Windows callbacks only enqueue work; no periodic metadata/artwork polling.
internal sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed, _reading, _pending;
    private int _generation;
    private TimeSpan _position, _start, _end;
    private DateTimeOffset _updated;
    private double _rate = 1;
    private bool _hasTimeline;
    private DateTimeOffset _timelineStamp;
    private TimeSpan _rawPosition;

    public event EventHandler? Changed;
    public string Title { get; private set; } = "No media playing";
    public string Artist { get; private set; } = "Play music in a Windows media app";
    public string TrackKey { get; private set; } = "";
    public BitmapImage? Artwork { get; private set; }
    public bool HasSession => _session is not null;
    public bool IsPlaying { get; private set; }
    public bool CanPrevious { get; private set; }
    public bool CanToggle { get; private set; }
    public bool CanNext { get; private set; }
    public TimeSpan Duration => _end > _start ? _end - _start : TimeSpan.Zero;

    public MediaSessionService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public async Task StartAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            SwitchSession();
        }
        catch (Exception)
        {
            if (_disposed) return;
            Title = "Media unavailable";
            Artist = "Windows media access could not start";
            Notify();
        }
    }

    private void Post(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => Post(SwitchSession);
    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => Post(() =>
        {
            if (sender != _session) return;
            // Invalidate an in-flight read before starting the latest read.
            _generation++;
            Artwork = null;
            QueueProperties();
        });
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => Post(() =>
        {
            if (sender != _session) return;
            ReadPlayback();
            Notify();
        });
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => Post(() =>
        {
            if (sender != _session) return;
            ReadTimeline();
            Notify();
        });

    private void SwitchSession()
    {
        try
        {
            var next = _manager?.GetCurrentSession();
            if (next == _session) return;
            Unsubscribe();
            _session = next;
            _generation++;
            ClearTrack();
            if (next is not null)
            {
                next.MediaPropertiesChanged += OnPropertiesChanged;
                next.PlaybackInfoChanged += OnPlaybackChanged;
                next.TimelinePropertiesChanged += OnTimelineChanged;
            }
            ReadPlayback();
            Notify();
            QueueProperties();
        }
        catch (Exception)
        {
            Unsubscribe();
            _session = null;
            _generation++;
            ClearTrack();
            ReadPlayback();
            Notify();
        }
    }

    private void ClearTrack()
    {
        TrackKey = "";
        Title = _session is null ? "No media playing" : "Loading track";
        Artist = "Play music in a Windows media app";
        Artwork = null;
        _position = _start = _end = TimeSpan.Zero;
        _hasTimeline = false;
    }

    private async void QueueProperties()
    {
        _pending = true;
        if (_reading) return;
        _reading = true;
        try
        {
            while (_pending && !_disposed)
            {
                _pending = false;
                var session = _session;
                int generation = _generation;
                if (session is null) continue;
                try
                {
                    var media = await session.TryGetMediaPropertiesAsync();
                    if (_disposed || session != _session || generation != _generation) continue;
                    Title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown track" : media.Title;
                    Artist = string.IsNullOrWhiteSpace(media.Artist) ? "Windows media session" : media.Artist;
                    TrackKey = session.SourceAppUserModelId + "\n" + media.Title + "\n" +
                        media.Artist + "\n" + media.AlbumTitle + "\n" + media.TrackNumber;
                    ReadPlayback();
                    Notify();
                    // A small, frozen bitmap is retained only for the current track.
                    var artwork = await ReadArtworkAsync(media.Thumbnail);
                    if (_disposed || session != _session || generation != _generation) continue;
                    Artwork = artwork;
                    Notify();
                }
                catch (Exception)
                {
                    if (_disposed || session != _session || generation != _generation) continue;
                    Title = "Media not available";
                    Artist = "This player may not expose media information";
                    Artwork = null;
                    Notify();
                }
            }
        }
        finally { _reading = false; }
    }

    private static async Task<BitmapImage?> ReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 4 * 1024 * 1024) return null;
            using var reader = new DataReader(stream);
            uint length = (uint)stream.Size;
            if (await reader.LoadAsync(length) != length) return null;
            byte[] bytes = new byte[length];
            reader.ReadBytes(bytes);
            using var memory = new MemoryStream(bytes, false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception) { return null; }
    }

    private void ReadPlayback()
    {
        bool wasPlaying = IsPlaying;
        double oldRate = _rate;
        var oldPosition = Position;
        var oldUpdated = _timelineStamp;
        IsPlaying = CanPrevious = CanToggle = CanNext = false;
        try
        {
            var info = _session?.GetPlaybackInfo();
            if (info is not null)
            {
                IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                CanPrevious = info.Controls.IsPreviousEnabled;
                CanToggle = info.Controls.IsPlayPauseToggleEnabled;
                CanNext = info.Controls.IsNextEnabled;
                _rate = info.PlaybackRate is double rate && double.IsFinite(rate) ? rate : 1;
            }
        }
        catch (Exception) { }
        ReadTimeline();
        // Some players change playback state without publishing a new timeline.
        if ((wasPlaying != IsPlaying || oldRate != _rate) && oldUpdated == _timelineStamp)
        {
            _position = oldPosition + _start;
            _updated = DateTimeOffset.UtcNow;
        }
    }

    private void ReadTimeline()
    {
        try
        {
            var timeline = _session?.GetTimelineProperties();
            if (timeline is null) { _hasTimeline = false; _position = _start = _end = TimeSpan.Zero; return; }
            if (_hasTimeline && _timelineStamp == timeline.LastUpdatedTime &&
                _rawPosition == timeline.Position && _start == timeline.StartTime && _end == timeline.EndTime) return;
            _hasTimeline = true;
            _timelineStamp = timeline.LastUpdatedTime;
            _rawPosition = timeline.Position;
            _position = timeline.Position;
            _start = timeline.StartTime;
            _end = timeline.EndTime;
            _updated = timeline.LastUpdatedTime;
        }
        catch (Exception) { _hasTimeline = false; _position = _start = _end = TimeSpan.Zero; }
    }

    public TimeSpan Position
    {
        get
        {
            if (Duration <= TimeSpan.Zero) return TimeSpan.Zero;
            double elapsed = IsPlaying && _updated != default
                ? Math.Max(0, (DateTimeOffset.UtcNow - _updated).TotalSeconds) * _rate : 0;
            return TimeSpan.FromSeconds(Math.Clamp((_position - _start).TotalSeconds + elapsed,
                0, Duration.TotalSeconds));
        }
    }

    // Explicit values avoid binding commands to unsupported player capabilities.
    public async Task<bool> CommandAsync(string command)
    {
        var session = _session;
        if (_disposed || session is null) return false;
        try
        {
            return command switch
            {
                "previous" when CanPrevious => await session.TrySkipPreviousAsync(),
                "toggle" when CanToggle => await session.TryTogglePlayPauseAsync(),
                "next" when CanNext => await session.TrySkipNextAsync(),
                _ => false
            };
        }
        catch (Exception) { return false; }
    }

    private void Notify() { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    private void Unsubscribe()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
    }

    public void Dispose()
    {
        _disposed = true;
        _generation++;
        if (_manager is not null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        Unsubscribe();
        _session = null;
        _manager = null;
        Artwork = null;
        Changed = null;
    }
}
