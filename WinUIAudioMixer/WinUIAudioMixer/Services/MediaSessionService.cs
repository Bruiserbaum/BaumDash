using System.Drawing;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinUIAudioMixer.Services;

public sealed class MediaInfo
{
    public string Title    { get; init; } = "";
    public string Artist   { get; init; } = "";
    public string Album    { get; init; } = "";
    public bool   IsPlaying         { get; init; }
    public Bitmap? Thumbnail        { get; init; }
    public bool   CanPlay           { get; init; }
    public bool   CanPause          { get; init; }
    public bool   CanSkipNext       { get; init; }
    public bool   CanSkipPrevious   { get; init; }
    public bool   HasSession        { get; init; }
}

/// <summary>
/// Wraps the Windows System Media Transport Controls (SMTC) session manager.
/// Must be created on a thread that has a WinRT dispatcher context (use CreateAsync from UI thread).
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    public event Action<MediaInfo>? MediaChanged;

    private MediaSessionService() { }

    public static async Task<MediaSessionService> CreateAsync()
    {
        var svc = new MediaSessionService();
        svc._manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        svc._manager.CurrentSessionChanged += svc.OnCurrentSessionChanged;
        svc.AttachSession(svc._manager.GetCurrentSession());
        return svc;
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }
        _currentSession = session;
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged    += OnPlaybackInfoChanged;
        }
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager s, CurrentSessionChangedEventArgs _)
    {
        AttachSession(s.GetCurrentSession());
        await RefreshAsync();
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs _)
        => await RefreshAsync();

    private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs _)
        => await RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_currentSession == null)
        {
            MediaChanged?.Invoke(new MediaInfo());
            return;
        }

        try
        {
            var props    = await _currentSession.TryGetMediaPropertiesAsync();
            var playback = _currentSession.GetPlaybackInfo();
            var thumb    = props.Thumbnail != null ? await LoadThumbnailAsync(props.Thumbnail) : null;

            MediaChanged?.Invoke(new MediaInfo
            {
                Title           = props.Title       ?? "",
                Artist          = props.Artist      ?? "",
                Album           = props.AlbumTitle  ?? "",
                IsPlaying       = playback.PlaybackStatus ==
                                  GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Thumbnail       = thumb,
                CanPlay         = playback.Controls.IsPlayEnabled,
                CanPause        = playback.Controls.IsPauseEnabled,
                CanSkipNext     = playback.Controls.IsNextEnabled,
                CanSkipPrevious = playback.Controls.IsPreviousEnabled,
                HasSession      = true,
            });
        }
        catch { MediaChanged?.Invoke(new MediaInfo()); }
    }

    private static async Task<Bitmap?> LoadThumbnailAsync(IRandomAccessStreamReference streamRef)
    {
        try
        {
            using var stream = await streamRef.OpenReadAsync();
            var ms     = new MemoryStream();
            var reader = new DataReader(stream);
            var size   = (uint)stream.Size;
            await reader.LoadAsync(size);
            var buf = new byte[size];
            reader.ReadBytes(buf);
            ms.Write(buf);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch { return null; }
    }

    public async Task PlayAsync()           => await TryAsync(s => s.TryPlayAsync());
    public async Task PauseAsync()          => await TryAsync(s => s.TryPauseAsync());
    public async Task TogglePlayPauseAsync() => await TryAsync(s => s.TryTogglePlayPauseAsync());
    public async Task NextAsync()           => await TryAsync(s => s.TrySkipNextAsync());
    public async Task PreviousAsync()       => await TryAsync(s => s.TrySkipPreviousAsync());

    private async Task TryAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> fn)
    {
        if (_currentSession == null) return;
        try { await fn(_currentSession); } catch { }
    }

    public void Dispose()
    {
        AttachSession(null);
        if (_manager != null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
    }
}
