using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace PcHub.Agent.Modules;

/// <summary>
/// Lee y controla lo que suena en el PC usando la sesión multimedia de Windows (la misma que muestra
/// el panel de volumen). Funciona con YouTube Music en Chrome/Edge, Spotify, VLC, etc.
/// </summary>
public sealed class MediaModule : IDisposable
{
    private readonly Action<JsonObject> _onChange;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Timer? _timer;
    private int _busy;
    private string _lastKey = "";
    private DateTime _lastSent = DateTime.MinValue;
    private string _artKey = "";
    private string _lastBroadcastArtKey = "\u0000";
    private string? _artBase64;

    public JsonObject Current { get; private set; } = new() { ["status"] = "none" };

    public MediaModule(Action<JsonObject> onChange) => _onChange = onChange;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo abrir la sesión multimedia de Windows", ex);
        }
        _timer = new Timer(_ => _ = TickAsync(), null, 500, 1000);
    }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var state = await ReadStateAsync();
            var hasArt = state["art"] != null;
            var key = $"{state["title"]}|{state["artist"]}|{state["status"]}|{state["app"]}|{hasArt}";
            bool playing = (string?)state["status"] == "playing";
            // Se envía si cambió algo, o cada 5 s mientras suena (para resincronizar la barra de progreso)
            if (key != _lastKey || (playing && DateTime.UtcNow - _lastSent > TimeSpan.FromSeconds(5)))
            {
                _lastKey = key;
                _lastSent = DateTime.UtcNow;
                Current = state;
                // La carátula (~30 KB) solo viaja cuando cambia la canción; el teléfono conserva la anterior
                var toSend = (JsonObject)state.DeepClone();
                var artKey = $"{state["title"]}|{state["artist"]}|{hasArt}";
                if (artKey == _lastBroadcastArtKey) toSend.Remove("art");
                else _lastBroadcastArtKey = artKey;
                _onChange(toSend);
            }
            else
            {
                Current = state;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Media tick", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private GlobalSystemMediaTransportControlsSession? Session => _manager?.GetCurrentSession();

    public async Task<JsonObject> ReadStateAsync()
    {
        var s = Session;
        if (s == null) return new JsonObject { ["status"] = "none" };

        var info = s.GetPlaybackInfo();
        var timeline = s.GetTimelineProperties();
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { props = await s.TryGetMediaPropertiesAsync(); } catch { /* algunas apps fallan aquí */ }

        string status = info?.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "paused",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "stopped",
            _ => "unknown",
        };

        double position = timeline.Position.TotalSeconds;
        if (status == "playing" && timeline.LastUpdatedTime.Year > 2000)
            position += (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds;
        double duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
        if (duration > 0) position = Math.Clamp(position, 0, duration);

        string title = props?.Title ?? "";
        string artist = props?.Artist ?? "";

        var artKey = $"{title}|{artist}";
        if (props?.Thumbnail != null && (artKey != _artKey || _artBase64 == null))
        {
            // Algunas apps publican la carátula un poco después del título: se reintenta mientras falte
            _artKey = artKey;
            _artBase64 = await ReadThumbnailAsync(props);
        }
        else if (props?.Thumbnail == null && artKey != _artKey)
        {
            _artKey = artKey;
            _artBase64 = null;
        }

        return new JsonObject
        {
            ["title"] = title,
            ["artist"] = artist,
            ["album"] = props?.AlbumTitle ?? "",
            ["app"] = s.SourceAppUserModelId ?? "",
            ["status"] = status,
            ["position"] = Math.Round(position, 1),
            ["duration"] = Math.Round(Math.Max(duration, 0), 1),
            ["canNext"] = info?.Controls.IsNextEnabled ?? false,
            ["canPrev"] = info?.Controls.IsPreviousEnabled ?? false,
            ["art"] = _artBase64,
        };
    }

    private static async Task<string?> ReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        try
        {
            using var ras = await props.Thumbnail.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            const int max = 320;
            double scale = Math.Min(1.0, (double)max / Math.Max(img.Width, img.Height));
            int w = Math.Max(1, (int)(img.Width * scale)), h = Math.Max(1, (int)(img.Height * scale));
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, w, h);
            }
            using var outMs = new MemoryStream();
            bmp.Save(outMs, ImageFormat.Jpeg);
            return Convert.ToBase64String(outMs.ToArray());
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer la carátula", ex);
            return null;
        }
    }

    public async Task CommandAsync(string action, double? pos)
    {
        var s = Session;
        if (s == null)
        {
            // Sin sesión: usar teclas multimedia como respaldo
            switch (action)
            {
                case "playpause": case "play": case "pause": InputModule.KeyCombo("playpause"); break;
                case "next": InputModule.KeyCombo("next"); break;
                case "prev": InputModule.KeyCombo("prev"); break;
            }
            return;
        }

        switch (action)
        {
            case "playpause": await s.TryTogglePlayPauseAsync(); break;
            case "play": await s.TryPlayAsync(); break;
            case "pause": await s.TryPauseAsync(); break;
            case "next": await s.TrySkipNextAsync(); break;
            case "prev": await s.TrySkipPreviousAsync(); break;
            case "seek" when pos.HasValue:
                await s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(pos.Value).Ticks);
                break;
            default: throw new ArgumentException($"Acción de media desconocida: {action}");
        }
        // Forzar un envío de estado pronto
        _lastKey = "";
        _ = Task.Delay(300).ContinueWith(_ => TickAsync());
    }

    public void Dispose() => _timer?.Dispose();
}
