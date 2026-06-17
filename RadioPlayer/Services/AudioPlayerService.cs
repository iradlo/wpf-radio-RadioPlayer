using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;
using RadioPlayer.Services.Audio;

namespace RadioPlayer.Services;

/// <summary>
/// NAudio-based implementation of <see cref="IAudioPlayerService"/>.
/// MP3 streams are played through a custom ICY-aware pipeline (which also yields
/// song-title metadata); other codecs (AAC, ...) fall back to Media Foundation.
/// Includes automatic reconnect with exponential backoff (max 3 attempts).
/// </summary>
public sealed class AudioPlayerService : IAudioPlayerService
{
    private const int MaxReconnectAttempts = 3;
    private const int DecodeBufferSize = 64 * 1024;
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PlaybackBufferCapacity = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PrebufferDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BufferHeadroom = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StableConnectionThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PreviousTaskStopTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _httpClient;
    private readonly ILogger<AudioPlayerService> _logger;
    private readonly object _lock = new();

    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;
    private int _playbackGeneration;
    private IWavePlayer? _waveOut;
    private double _volume = 0.7;
    private bool _isMuted;
    private bool _disposed;

    /// <summary>
    /// Creates the service. The <paramref name="httpClient"/> must have an infinite
    /// timeout because radio streams never complete.
    /// </summary>
    public AudioPlayerService(HttpClient httpClient, ILogger<AudioPlayerService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        try
        {
            MediaFoundationApi.Startup();
        }
        catch (Exception ex)
        {
            // AAC fallback will be unavailable, but MP3 playback still works.
            _logger.LogWarning(ex, "Media Foundation initialization failed");
        }
    }

    /// <inheritdoc />
    public event EventHandler<PlaybackStatusEventArgs>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<string>? MetadataChanged;

    /// <inheritdoc />
    public PlaybackStatus Status { get; private set; }

    /// <inheritdoc />
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            ApplyVolume();
        }
    }

    /// <inheritdoc />
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            ApplyVolume();
        }
    }

    /// <inheritdoc />
    public async Task PlayAsync(string streamUrl, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Task? previousTask;
        int myGeneration;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock)
        {
            _playbackCts?.Cancel();
            previousTask = _playbackTask;
            _playbackCts = cts;
            myGeneration = ++_playbackGeneration;
        }

        if (previousTask is not null)
        {
            try
            {
                await previousTask.WaitAsync(PreviousTaskStopTimeout);
            }
            catch (TimeoutException)
            {
                // Previous task is still blocked (e.g. MediaFoundationReader probing a stalled
                // stream). Its CTS is already cancelled so it will exit as soon as the probe
                // returns; we don't block the new station on that.
                _logger.LogWarning("Previous playback task did not stop within {Timeout}; proceeding", PreviousTaskStopTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Previous playback task ended with an error");
            }
        }

        lock (_lock)
        {
            if (_playbackGeneration != myGeneration)
            {
                cts.Dispose();
                return;
            }

            _playbackTask = RunPlaybackAsync(streamUrl, cts.Token);
        }

        SetStatus(PlaybackStatus.Connecting);
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_lock)
        {
            _playbackCts?.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        try
        {
            _playbackTask?.Wait(PreviousTaskStopTimeout);
        }
        catch (AggregateException)
        {
            // Playback task failures were already reported via StatusChanged.
        }

        lock (_lock)
        {
            _playbackCts?.Dispose();
            _playbackCts = null;
        }
    }

    private async Task RunPlaybackAsync(string streamUrl, CancellationToken ct)
    {
        var attempt = 0;
        var delay = InitialReconnectDelay;
        while (true)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await PlayOnceAsync(streamUrl, ct);
                SetStatus(PlaybackStatus.Stopped);
                return;
            }
            catch (OperationCanceledException)
            {
                SetStatus(PlaybackStatus.Stopped);
                return;
            }
            catch (NotSupportedException ex)
            {
                // Unsupported format: retrying cannot help.
                SetStatus(PlaybackStatus.Error, ex.Message);
                return;
            }
            catch (Exception ex)
            {
                // A user stop can surface as an I/O error from the closing stream.
                if (ct.IsCancellationRequested)
                {
                    SetStatus(PlaybackStatus.Stopped);
                    return;
                }

                _logger.LogWarning(ex, "Playback of {Url} failed", streamUrl);

                // A connection that survived a while was healthy: restart the attempt budget.
                if (DateTimeOffset.UtcNow - startedAt > StableConnectionThreshold)
                {
                    attempt = 0;
                    delay = InitialReconnectDelay;
                }

                attempt++;
                if (attempt > MaxReconnectAttempts)
                {
                    SetStatus(PlaybackStatus.Error, "The station is currently unavailable.");
                    return;
                }

                SetStatus(
                    PlaybackStatus.Reconnecting,
                    $"Connection lost, retrying ({attempt}/{MaxReconnectAttempts})...");
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(PlaybackStatus.Stopped);
                    return;
                }

                delay *= 2;
            }
        }
    }

    private async Task PlayOnceAsync(string streamUrl, CancellationToken ct)
    {
        var response = await SendFollowingRedirectsAsync(streamUrl, ct);
        try
        {
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var finalPath = response.RequestMessage?.RequestUri?.AbsolutePath ?? streamUrl;
            if (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
                finalPath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("This station uses HLS streaming, which is not supported yet.");
            }

            if (mediaType.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
            {
                await PlayIcyMp3Async(response, ct);
            }
            else
            {
                // Non-MP3 codec: let Media Foundation handle it (no ICY metadata).
                // Use the post-redirect URL; Media Foundation chokes on redirect chains.
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? streamUrl;
                response.Dispose();
                await PlayWithMediaFoundationAsync(finalUrl, ct);
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Sends the stream request following redirects manually: the built-in handler
    /// refuses HTTPS-to-HTTP downgrades, which are common with radio streams.
    /// </summary>
    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(string url, CancellationToken ct)
    {
        const int maxRedirects = 5;
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || hop >= maxRedirects)
            {
                throw new HttpRequestException($"Too many redirects or missing Location for {url}.");
            }

            url = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(url), location).ToString();
        }
    }

    private async Task PlayIcyMp3Async(HttpResponseMessage response, CancellationToken ct)
    {
        var metadataInterval = 0;
        if (response.Headers.TryGetValues("icy-metaint", out var values))
        {
            _ = int.TryParse(values.FirstOrDefault(), out metadataInterval);
        }

        var networkStream = await response.Content.ReadAsStreamAsync(ct);
        using var icyStream = new IcyReadFullyStream(networkStream, metadataInterval);
        icyStream.StreamTitleChanged += title => MetadataChanged?.Invoke(this, title);

        // DecodeLoop calls synchronous Stream.Read which ignores the CancellationToken.
        // Disposing the network stream on cancellation forces the blocked Read to throw,
        // unblocking the decode thread so the previous playback task can complete promptly.
        using var streamCancelReg = ct.Register(networkStream.Dispose);
        await Task.Run(() => DecodeLoop(icyStream, ct), ct);
    }

    private void DecodeLoop(Stream mp3Stream, CancellationToken ct)
    {
        IMp3FrameDecompressor? decompressor = null;
        BufferedWaveProvider? playbackBuffer = null;
        IWavePlayer? waveOut = null;
        var decodeBuffer = new byte[DecodeBufferSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = Mp3Frame.LoadFromStream(mp3Stream)
                    ?? throw new EndOfStreamException("The radio stream ended.");

                if (decompressor is null)
                {
                    var format = new Mp3WaveFormat(
                        frame.SampleRate,
                        frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                        frame.FrameLength,
                        frame.BitRate);
                    decompressor = new AcmMp3FrameDecompressor(format);
                    playbackBuffer = new BufferedWaveProvider(decompressor.OutputFormat)
                    {
                        BufferDuration = PlaybackBufferCapacity,
                    };
                }

                var decoded = decompressor.DecompressFrame(frame, decodeBuffer, 0);

                // Apply backpressure instead of discarding audio when the buffer is full.
                while (!ct.IsCancellationRequested &&
                       playbackBuffer!.BufferDuration - playbackBuffer.BufferedDuration < BufferHeadroom)
                {
                    Thread.Sleep(100);
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                playbackBuffer!.AddSamples(decodeBuffer, 0, decoded);

                if (waveOut is null && playbackBuffer.BufferedDuration >= PrebufferDuration)
                {
                    waveOut = AttachWaveOut(playbackBuffer);
                    SetStatus(PlaybackStatus.Playing);
                }
            }

            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            DetachWaveOut(waveOut);
            decompressor?.Dispose();
        }
    }

    private async Task PlayWithMediaFoundationAsync(string streamUrl, CancellationToken ct)
    {
        await Task.Run(async () =>
        {
            // The reader constructor blocks while it probes the stream, hence Task.Run.
            using var reader = new MediaFoundationReader(streamUrl);
            ct.ThrowIfCancellationRequested();

            var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waveOut = AttachWaveOut(reader, beforePlay: w =>
                w.PlaybackStopped += (_, args) => stopped.TrySetResult(args.Exception));
            try
            {
                SetStatus(PlaybackStatus.Playing);
                await using var registration = ct.Register(() => stopped.TrySetResult(null));
                var error = await stopped.Task;
                ct.ThrowIfCancellationRequested();
                throw error ?? new IOException("The radio stream ended.");
            }
            finally
            {
                DetachWaveOut(waveOut);
            }
        }, ct);
    }

    private IWavePlayer AttachWaveOut(IWaveProvider provider, Action<IWavePlayer>? beforePlay = null)
    {
        var waveOut = new WaveOutEvent();
        waveOut.Init(provider);
        beforePlay?.Invoke(waveOut);
        lock (_lock)
        {
            _waveOut = waveOut;
        }

        ApplyVolume();
        waveOut.Play();
        return waveOut;
    }

    private void DetachWaveOut(IWavePlayer? waveOut)
    {
        if (waveOut is null)
        {
            return;
        }

        lock (_lock)
        {
            if (ReferenceEquals(_waveOut, waveOut))
            {
                _waveOut = null;
            }
        }

        try
        {
            waveOut.Stop();
            waveOut.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while disposing wave output");
        }
    }

    private void ApplyVolume()
    {
        lock (_lock)
        {
            if (_waveOut is WaveOutEvent waveOut)
            {
                waveOut.Volume = (float)(_isMuted ? 0.0 : _volume);
            }
        }
    }

    private void SetStatus(PlaybackStatus status, string? message = null)
    {
        Status = status;
        StatusChanged?.Invoke(this, new PlaybackStatusEventArgs { Status = status, Message = message });
    }
}
