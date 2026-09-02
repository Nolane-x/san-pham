using System.Diagnostics;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Recording;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Magic.Capture.App.Recording;

internal sealed record RecordingProgress(
    RecordingSessionState State,
    TimeSpan ActiveElapsed,
    long FrameCount,
    int CountdownRemaining,
    string Target,
    string OutputPath,
    string? Message = null,
    RecordingAudioStatus? AudioStatus = null,
    RecordingWebcamStatus? WebcamStatus = null);

internal sealed class RecordingSessionService
{
    private readonly RecordingFrameProvider _frames;
    private readonly RecordingRecoveryStore _recovery;
    private readonly LocalLog _log;
    private readonly object _sync = new();
    private CancellationTokenSource? _sessionCts;
    private Task? _runTask;
    private bool _stopRequested;
    private bool _paused;
    private TaskCompletionSource<bool> _resumeSignal = NewSignal();
    private readonly RecordingClock _clock = new();
    private RecordingSessionState _state = RecordingSessionState.Completed;
    private RecordingTarget? _lastRegion;
    private RecordingSessionManifest? _manifest;
    private RecordingAudioPipeline? _activeAudioPipeline;
    private RecordingWebcamSource? _activeWebcamSource;
    private RecordingInputTracker? _activeInputTracker;

    public RecordingSessionService(RecordingFrameProvider frames, RecordingRecoveryStore recovery, LocalLog log)
    {
        _frames = frames;
        _recovery = recovery;
        _log = log;
    }

    public event EventHandler<RecordingProgress>? ProgressChanged;
    public bool IsActive
    {
        get { lock (_sync) return _runTask is { IsCompleted: false }; }
    }
    public RecordingSessionState State { get { lock (_sync) return _state; } }
    public RecordingTarget? LastRegion { get { lock (_sync) return _lastRegion; } }

    public async Task StartAsync(RecordingTarget target, string finalPath, RecordingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Path.IsPathFullyQualified(finalPath)) throw new ArgumentException("Recording output path must be fully qualified.", nameof(finalPath));
        options = RecordingRules.Normalize(options);
        RecordingOutputPolicy.ValidateCompatibility(options);
        var extension = RecordingOutputPolicy.Extension(options.OutputFormat);
        if (!finalPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Recording output must use the {extension} extension for {RecordingOutputPolicy.DisplayName(options.OutputFormat)}.", nameof(finalPath));

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_runTask is { IsCompleted: false }) throw new InvalidOperationException("A recording is already active.");
            _sessionCts?.Dispose();
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stopRequested = false;
            _paused = false;
            _resumeSignal.TrySetResult(true);
            _resumeSignal = NewSignal();
            _clock.Reset();
            _state = RecordingSessionState.Preparing;
            if (target.Kind == RecordingTargetKind.Region) _lastRegion = target;
            _runTask = RunAsync(target, finalPath, options, started, _sessionCts.Token);
        }
        await started.Task;
    }

    public async Task StartAudioOnlyAsync(string finalPath, RecordingOptions options, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(finalPath)) throw new ArgumentException("Recording output path must be fully qualified.", nameof(finalPath));
        options = RecordingRules.Normalize(options);
        RecordingOutputPolicy.ValidateCompatibility(options);
        if (!RecordingOutputPolicy.IsAudioOnly(options.OutputFormat))
            throw new ArgumentException("Audio-only start requires the M4A output format.", nameof(options));
        if (!finalPath.EndsWith(RecordingOutputPolicy.Extension(options.OutputFormat), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Audio-only recording output must use .m4a.", nameof(finalPath));

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_runTask is { IsCompleted: false }) throw new InvalidOperationException("A recording is already active.");
            _sessionCts?.Dispose();
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stopRequested = false;
            _paused = false;
            _resumeSignal.TrySetResult(true);
            _resumeSignal = NewSignal();
            _clock.Reset();
            _state = RecordingSessionState.Preparing;
            _runTask = RunAudioOnlyAsync(finalPath, options, started, _sessionCts.Token);
        }
        await started.Task;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        RecordingSessionManifest? manifest;
        RecordingAudioPipeline? audio;
        RecordingInputTracker? input;
        lock (_sync)
        {
            if (_state != RecordingSessionState.Recording || _paused) return;
            _paused = true;
            _clock.Pause();
            TransitionUnsafe(RecordingSessionState.Paused);
            audio = _activeAudioPipeline;
            input = _activeInputTracker;
            manifest = SnapshotManifestUnsafe();
        }
        audio?.SetPaused(true);
        input?.SetPaused(true);
        if (manifest is not null) await _recovery.SaveAsync(manifest, cancellationToken);
        Publish();
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        RecordingSessionManifest? manifest;
        RecordingAudioPipeline? audio;
        RecordingInputTracker? input;
        TaskCompletionSource<bool> signal;
        lock (_sync)
        {
            if (_state != RecordingSessionState.Paused || !_paused) return;
            _paused = false;
            _clock.Resume();
            TransitionUnsafe(RecordingSessionState.Recording);
            audio = _activeAudioPipeline;
            input = _activeInputTracker;
            signal = _resumeSignal;
            _resumeSignal = NewSignal();
            manifest = SnapshotManifestUnsafe();
        }
        audio?.SetPaused(false);
        input?.SetPaused(false);
        signal.TrySetResult(true);
        if (manifest is not null) await _recovery.SaveAsync(manifest, cancellationToken);
        Publish();
    }

    public void Stop()
    {
        TaskCompletionSource<bool> signal;
        lock (_sync)
        {
            _stopRequested = true;
            signal = _resumeSignal;
        }
        signal.TrySetResult(true);
    }

    public async Task<RecordingRecoveryResult> LoadRecoveryAsync(CancellationToken cancellationToken = default) =>
        await _recovery.LoadUnfinishedAsync(cancellationToken);

    public async Task ClearRecoveryAsync(CancellationToken cancellationToken = default) =>
        await _recovery.ClearAsync(cancellationToken);

    private async Task RunAsync(
        RecordingTarget target,
        string finalPath,
        RecordingOptions options,
        TaskCompletionSource<bool> started,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var directory = Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException("Recording output has no directory.");
        Directory.CreateDirectory(directory);
        var baseName = Path.GetFileNameWithoutExtension(finalPath);
        var tempPath = Path.Combine(directory, $".{baseName}.{sessionId:N}{RecordingOutputPolicy.PartialSuffix(options.OutputFormat)}");
        var outputWidth = RecordingRules.ScaleDimension(target.Bounds.Width, options.ScalePercent);
        var outputHeight = RecordingRules.ScaleDimension(target.Bounds.Height, options.ScalePercent);
        long frameCount = 0;
        var lastJournalFrame = -1L;
        RecordingAudioPipeline? audio = null;
        RecordingWebcamSource? webcam = null;
        RecordingInputTracker? input = null;

        try
        {
            lock (_sync)
            {
                _manifest = new RecordingSessionManifest(
                    RecordingManifestPolicy.CurrentSchemaVersion,
                    sessionId,
                    RecordingSessionState.Preparing,
                    target.Kind,
                    target.DisplayName,
                    finalPath,
                    tempPath,
                    options,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0,
                    0);
            }
            await _recovery.SaveAsync(_manifest, cancellationToken);
            Publish(countdownRemaining: options.CountdownSeconds);

            for (var remaining = options.CountdownSeconds; remaining > 0; remaining--)
            {
                if (StopRequested()) throw new OperationCanceledException("Recording was cancelled during countdown.");
                Publish(countdownRemaining: remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            if (options.IncludeWebcam)
            {
                webcam = new RecordingWebcamSource();
                await webcam.StartAsync(options.WebcamDeviceId, cancellationToken);
            }

            if (options.IncludeSystemAudio || options.IncludeMicrophone)
            {
                audio = RecordingAudioPipeline.Create(options);
                await audio.StartAndWarmUpAsync(cancellationToken);
            }

            if (RecordingEffectsPolicy.HasAnyEffect(options))
                input = new RecordingInputTracker(target, options, () => _clock.ActiveElapsed);

            lock (_sync)
            {
                _activeAudioPipeline = audio;
                _activeWebcamSource = webcam;
                _activeInputTracker = input;
                TransitionUnsafe(RecordingSessionState.Recording);
                _clock.Start();
                _manifest = SnapshotManifestUnsafe();
            }
            input?.Start();
            if (_manifest is not null) await _recovery.SaveAsync(_manifest, cancellationToken);
            started.TrySetResult(true);
            Publish();

            async Task CommitFrameProgressAsync(long index, CancellationToken token)
            {
                frameCount = checked(index + 1);
                if (frameCount - lastJournalFrame >= options.FramesPerSecond)
                {
                    lastJournalFrame = frameCount;
                    RecordingSessionManifest? periodic;
                    lock (_sync)
                    {
                        if (_manifest is not null)
                            _manifest = _manifest with { FrameCount = frameCount, ActiveElapsedTicks = _clock.ActiveElapsed.Ticks, UpdatedUtc = DateTimeOffset.UtcNow };
                        periodic = _manifest;
                    }
                    if (periodic is not null) await _recovery.SaveAsync(periodic, token);
                }
                Publish(frameCount: frameCount);
            }

            async Task<bool> PrepareVisualFrameAsync(long index, CancellationToken token)
            {
                await WaitUntilResumedAsync(token);
                if (StopRequested() || RecordingStopPolicy.ShouldStop(_clock.ActiveElapsed, options.StopAfterMinutes)) return false;
                await PaceFrameAsync(index, options.FramesPerSecond, token);
                await WaitUntilResumedAsync(token);
                return !(StopRequested() || RecordingStopPolicy.ShouldStop(_clock.ActiveElapsed, options.StopAfterMinutes));
            }

            async Task<RecordingFramePixels> CaptureProcessedPixelsAsync(CancellationToken token)
            {
                var captured = await Task.Run(() => _frames.Capture(target, options.IncludeCursor), token);
                var decoded = await RecordingFrameDecoder.DecodeBgra8PixelsAsync(captured.PngBytes, outputWidth, outputHeight, token);
                var now = _clock.ActiveElapsed;
                var snapshot = input?.Snapshot(now) ?? RecordingInputSnapshot.Empty;
                if (input is not null)
                    RecordingEffectsCompositor.ApplyZoomInPlace(decoded, snapshot, options, target.Bounds.Width, target.Bounds.Height);
                if (webcam is not null)
                {
                    webcam.ThrowIfFailed();
                    RecordingWebcamCompositor.CompositeInPlace(decoded, webcam.GetLatestFrame(), options);
                }
                if (input is not null)
                    RecordingEffectsCompositor.ApplyOverlaysInPlace(decoded, snapshot, options, target.Bounds.Width, target.Bounds.Height, now);
                return decoded;
            }

            if (options.OutputFormat == RecordingOutputFormat.Mp4)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(directory);
                var tempFile = await folder.CreateFileAsync(Path.GetFileName(tempPath), CreationCollisionOption.ReplaceExisting);
                var encoder = new Mp4RecordingEncoder();
                await encoder.EncodeAsync(
                    tempFile,
                    outputWidth,
                    outputHeight,
                    options,
                    async (index, token) =>
                    {
                        if (!await PrepareVisualFrameAsync(index, token)) return null;
                        IBuffer videoBuffer;
                        if (webcam is null && input is null)
                        {
                            var captured = await Task.Run(() => _frames.Capture(target, options.IncludeCursor), token);
                            var decoded = await RecordingFrameDecoder.DecodeBgra8Async(captured.PngBytes, outputWidth, outputHeight, token);
                            videoBuffer = decoded.Buffer;
                        }
                        else
                        {
                            var pixels = await CaptureProcessedPixelsAsync(token);
                            videoBuffer = RecordingFrameDecoder.ToBuffer(pixels.BgraBytes);
                        }
                        await CommitFrameProgressAsync(index, token);
                        return videoBuffer;
                    },
                    audio is null
                        ? null
                        : async (index, token) =>
                        {
                            await WaitUntilResumedAsync(token);
                            if (StopRequested() || RecordingStopPolicy.ShouldStop(_clock.ActiveElapsed, options.StopAfterMinutes)) return null;
                            await PaceAudioBlockAsync(index, token);
                            await WaitUntilResumedAsync(token);
                            if (StopRequested() || RecordingStopPolicy.ShouldStop(_clock.ActiveElapsed, options.StopAfterMinutes)) return null;
                            audio.ThrowIfFailed();
                            return await audio.ReadMixedBlockAsync(token);
                        },
                    cancellationToken);
            }
            else
            {
                async Task<RecordingFramePixels?> AnimatedFrameFactory(long index, CancellationToken token)
                {
                    if (!await PrepareVisualFrameAsync(index, token)) return null;
                    var pixels = await CaptureProcessedPixelsAsync(token);
                    await CommitFrameProgressAsync(index, token);
                    return pixels;
                }

                if (options.OutputFormat == RecordingOutputFormat.Gif)
                {
                    var encoder = new GifRecordingEncoder();
                    await encoder.EncodeAsync(tempPath, outputWidth, outputHeight, options.FramesPerSecond, AnimatedFrameFactory, cancellationToken);
                }
                else if (options.OutputFormat == RecordingOutputFormat.Apng)
                {
                    var encoder = new ApngRecordingEncoder();
                    await encoder.EncodeAsync(tempPath, outputWidth, outputHeight, options.FramesPerSecond, AnimatedFrameFactory, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported recording output format: {options.OutputFormat}.");
                }
            }

            lock (_sync)
            {
                _clock.Stop();
                TransitionUnsafe(RecordingSessionState.Finalizing);
                if (_manifest is not null)
                    _manifest = _manifest with { State = RecordingSessionState.Finalizing, FrameCount = frameCount, ActiveElapsedTicks = _clock.ActiveElapsed.Ticks, UpdatedUtc = DateTimeOffset.UtcNow };
            }
            if (_manifest is not null) await _recovery.SaveAsync(_manifest, cancellationToken);
            Publish(frameCount: frameCount);

            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(tempPath);
            if (!info.Exists || info.Length <= 0) throw new InvalidDataException($"Recording temporary {RecordingOutputPolicy.DisplayName(options.OutputFormat)} file is missing or empty.");
            File.Move(tempPath, finalPath, overwrite: true);

            lock (_sync)
            {
                TransitionUnsafe(RecordingSessionState.Completed);
                if (_manifest is not null)
                    _manifest = _manifest with { State = RecordingSessionState.Completed, FrameCount = frameCount, ActiveElapsedTicks = _clock.ActiveElapsed.Ticks, UpdatedUtc = DateTimeOffset.UtcNow };
            }
            await _recovery.ClearAsync(cancellationToken);
            Publish(frameCount: frameCount, message: $"Saved: {finalPath}");
        }
        catch (OperationCanceledException ex)
        {
            started.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
            await FailAsync(ex, frameCount);
        }
        catch (Exception ex)
        {
            started.TrySetException(ex);
            await FailAsync(ex, frameCount);
        }
        finally
        {
            lock (_sync)
            {
                _activeAudioPipeline = null;
                _activeWebcamSource = null;
                _activeInputTracker = null;
            }
            if (input is not null)
            {
                try { input.Dispose(); }
                catch (Exception disposeError) { _log.Error("RecordingInputDispose", disposeError); }
            }
            if (webcam is not null)
            {
                try { await webcam.DisposeAsync(); }
                catch (Exception disposeError) { _log.Error("RecordingWebcamDispose", disposeError); }
            }
            if (audio is not null)
            {
                try { await audio.DisposeAsync(); }
                catch (Exception disposeError) { _log.Error("RecordingAudioDispose", disposeError); }
            }
        }
    }

    private async Task FailAsync(Exception ex, long frameCount, long audioBlockCount = 0)
    {
        _log.Error("Recording", ex);
        RecordingSessionManifest? failed;
        lock (_sync)
        {
            _state = RecordingSessionState.Failed;
            _clock.Stop();
            if (_manifest is not null)
                _manifest = _manifest with
                {
                    State = RecordingSessionState.Failed,
                    FrameCount = frameCount,
                    AudioBlockCount = audioBlockCount,
                    ActiveElapsedTicks = _clock.ActiveElapsed.Ticks,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Failure = ex.Message.Length <= 400 ? ex.Message : ex.Message[..400]
                };
            failed = _manifest;
        }
        if (failed is not null)
        {
            try { await _recovery.SaveAsync(failed); }
            catch (Exception journalEx) { _log.Error("RecordingRecovery", journalEx); }
        }
        Publish(frameCount: frameCount, message: ex.Message);
    }

    private async Task RunAudioOnlyAsync(
        string finalPath,
        RecordingOptions options,
        TaskCompletionSource<bool> started,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var directory = Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException("Recording output has no directory.");
        Directory.CreateDirectory(directory);
        var baseName = Path.GetFileNameWithoutExtension(finalPath);
        var tempPath = Path.Combine(directory, $".{baseName}.{sessionId:N}{RecordingOutputPolicy.PartialSuffix(options.OutputFormat)}");
        long audioBlockCount = 0;
        var lastJournalBlock = -1L;
        RecordingAudioPipeline? audio = null;

        try
        {
            lock (_sync)
            {
                _manifest = new RecordingSessionManifest(
                    RecordingManifestPolicy.CurrentSchemaVersion,
                    sessionId,
                    RecordingSessionState.Preparing,
                    RecordingTargetKind.AudioOnly,
                    "Audio only",
                    finalPath,
                    tempPath,
                    options,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0,
                    0,
                    AudioBlockCount: 0);
            }
            await _recovery.SaveAsync(_manifest, cancellationToken);
            Publish(countdownRemaining: options.CountdownSeconds);

            for (var remaining = options.CountdownSeconds; remaining > 0; remaining--)
            {
                if (StopRequested()) throw new OperationCanceledException("Recording was cancelled during countdown.");
                Publish(countdownRemaining: remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            audio = RecordingAudioPipeline.Create(options);
            await audio.StartAndWarmUpAsync(cancellationToken);
            lock (_sync)
            {
                _activeAudioPipeline = audio;
                _activeWebcamSource = null;
                _activeInputTracker = null;
                TransitionUnsafe(RecordingSessionState.Recording);
                _clock.Start();
                _manifest = SnapshotManifestUnsafe();
            }
            if (_manifest is not null) await _recovery.SaveAsync(_manifest, cancellationToken);
            started.TrySetResult(true);
            Publish(message: "Recording audio only…");

            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var tempFile = await folder.CreateFileAsync(Path.GetFileName(tempPath), CreationCollisionOption.ReplaceExisting);
            var encoder = new M4aAudioRecordingEncoder();
            await encoder.EncodeAsync(
                tempFile,
                options,
                async (index, token) =>
                {
                    await WaitUntilResumedAsync(token);
                    if (StopRequested() || RecordingStopPolicy.ShouldStop(_clock.ActiveElapsed, options.StopAfterMinutes)) return null;
                    await PaceAudioBlockAsync(index, token);
                    await WaitUntilResumedAsync(token);
                    if (StopRequested() || RecordingStopPolicy.ShouldStop(_clock.ActiveElapsed, options.StopAfterMinutes)) return null;
                    audio.ThrowIfFailed();
                    var block = await audio.ReadMixedBlockAsync(token);
                    if (block is null) return null;
                    audioBlockCount = checked(index + 1);
                    if (audioBlockCount - lastJournalBlock >= RecordingAudioPolicy.SampleRate / RecordingAudioPolicy.FramesPerBlock)
                    {
                        lastJournalBlock = audioBlockCount;
                        RecordingSessionManifest? periodic;
                        lock (_sync)
                        {
                            if (_manifest is not null)
                                _manifest = _manifest with { AudioBlockCount = audioBlockCount, ActiveElapsedTicks = _clock.ActiveElapsed.Ticks, UpdatedUtc = DateTimeOffset.UtcNow };
                            periodic = _manifest;
                        }
                        if (periodic is not null) await _recovery.SaveAsync(periodic, token);
                    }
                    Publish(message: $"Recording audio only · {audioBlockCount:N0} block(s)");
                    return block;
                },
                cancellationToken);

            lock (_sync)
            {
                _clock.Stop();
                TransitionUnsafe(RecordingSessionState.Finalizing);
                if (_manifest is not null)
                    _manifest = _manifest with { State = RecordingSessionState.Finalizing, AudioBlockCount = audioBlockCount, ActiveElapsedTicks = _clock.ActiveElapsed.Ticks, UpdatedUtc = DateTimeOffset.UtcNow };
            }
            if (_manifest is not null) await _recovery.SaveAsync(_manifest, cancellationToken);
            Publish(message: "Finalizing M4A…");

            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(tempPath);
            if (!info.Exists || info.Length <= 0) throw new InvalidDataException("Recording temporary M4A file is missing or empty.");
            File.Move(tempPath, finalPath, overwrite: true);

            lock (_sync)
            {
                TransitionUnsafe(RecordingSessionState.Completed);
                if (_manifest is not null)
                    _manifest = _manifest with { State = RecordingSessionState.Completed, AudioBlockCount = audioBlockCount, ActiveElapsedTicks = _clock.ActiveElapsed.Ticks, UpdatedUtc = DateTimeOffset.UtcNow };
            }
            await _recovery.ClearAsync(cancellationToken);
            Publish(message: $"Saved: {finalPath}");
        }
        catch (OperationCanceledException ex)
        {
            started.TrySetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
            await FailAsync(ex, 0, audioBlockCount);
        }
        catch (Exception ex)
        {
            started.TrySetException(ex);
            await FailAsync(ex, 0, audioBlockCount);
        }
        finally
        {
            lock (_sync) { _activeAudioPipeline = null; }
            if (audio is not null)
            {
                try { await audio.DisposeAsync(); }
                catch (Exception disposeError) { _log.Error("RecordingAudioOnlyDispose", disposeError); }
            }
        }
    }

    private async Task WaitUntilResumedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (_sync)
            {
                if (!_paused || _stopRequested) return;
                signal = _resumeSignal.Task;
            }
            await signal.WaitAsync(cancellationToken);
        }
    }

    private async Task PaceFrameAsync(long frameIndex, int fps, CancellationToken cancellationToken)
    {
        while (true)
        {
            var due = RecordingCadence.TimestampForFrame(frameIndex, fps);
            var remaining = due - _clock.ActiveElapsed;
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining > TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : remaining, cancellationToken);
            if (StopRequested()) return;
        }
    }

    private async Task PaceAudioBlockAsync(long blockIndex, CancellationToken cancellationToken)
    {
        while (true)
        {
            var due = RecordingAudioPolicy.TimestampForBlock(checked(blockIndex + 1));
            var remaining = due - _clock.ActiveElapsed;
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining > TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(20) : remaining, cancellationToken);
            if (StopRequested()) return;
        }
    }

    private bool StopRequested() { lock (_sync) return _stopRequested; }

    private RecordingSessionManifest? SnapshotManifestUnsafe() => _manifest is null ? null : _manifest with
    {
        State = _state,
        ActiveElapsedTicks = _clock.ActiveElapsed.Ticks,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    private void TransitionUnsafe(RecordingSessionState next)
    {
        if (_state == next) return;
        if (!RecordingStateMachine.CanTransition(_state, next))
            throw new InvalidOperationException($"Invalid recording state transition: {_state} → {next}.");
        _state = next;
    }

    private void Publish(long? frameCount = null, int countdownRemaining = 0, string? message = null)
    {
        RecordingProgress progress;
        lock (_sync)
        {
            var manifest = _manifest;
            progress = new RecordingProgress(
                _state,
                _clock.ActiveElapsed,
                frameCount ?? manifest?.FrameCount ?? 0,
                countdownRemaining,
                manifest?.TargetSummary ?? "Recording",
                manifest?.FinalPath ?? string.Empty,
                message,
                _activeAudioPipeline?.Status,
                _activeWebcamSource?.Status);
        }
        ProgressChanged?.Invoke(this, progress);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingClock
    {
        private readonly Stopwatch _watch = new();
        private readonly object _gate = new();
        private TimeSpan _pausedAccumulated;
        private TimeSpan? _pauseStarted;

        public TimeSpan ActiveElapsed
        {
            get
            {
                lock (_gate)
                {
                    var elapsed = _watch.Elapsed;
                    var currentPause = _pauseStarted is { } started ? elapsed - started : TimeSpan.Zero;
                    var active = elapsed - _pausedAccumulated - currentPause;
                    return active < TimeSpan.Zero ? TimeSpan.Zero : active;
                }
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _watch.Reset();
                _pausedAccumulated = TimeSpan.Zero;
                _pauseStarted = null;
            }
        }

        public void Start() { lock (_gate) _watch.Start(); }
        public void Stop() { lock (_gate) _watch.Stop(); }

        public void Pause()
        {
            lock (_gate)
            {
                if (!_watch.IsRunning || _pauseStarted is not null) return;
                _pauseStarted = _watch.Elapsed;
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                if (_pauseStarted is not { } started) return;
                _pausedAccumulated += _watch.Elapsed - started;
                _pauseStarted = null;
            }
        }
    }
}
