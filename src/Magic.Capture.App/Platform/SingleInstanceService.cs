using System.IO.Pipes;
using System.Text.Json;
using Magic.Capture.Core.Platform;

namespace Magic.Capture.App.Platform;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\Magic.Capture.Desktop.Singleton";
    private const string ShowEventName = @"Local\Magic.Capture.Desktop.Show";
    private const string CommandPipeName = "Magic.Capture.Desktop.Command";
    private const int MaximumCommandPayloadChars = 64 * 1024;

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly EventWaitHandle? _stopEvent;
    private Thread? _listener;
    private Thread? _commandListener;
    private volatile bool _disposed;

    public SingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        IsPrimary = _ownsMutex;
        if (IsPrimary)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
        }
    }

    public bool IsPrimary { get; }

    public void StartListening(Action showPrimary, Action<IReadOnlyList<string>>? commandReceived = null)
    {
        ArgumentNullException.ThrowIfNull(showPrimary);
        if (!IsPrimary || _listener is not null || _showEvent is null || _stopEvent is null) return;

        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _stopEvent };
            while (true)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 1) return;
                try { showPrimary(); }
                catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
                {
                    // A transient UI callback failure must not kill the resident listener.
                    // Fatal memory/native failures are deliberately allowed to escape.
                }
            }
        })
        {
            IsBackground = true,
            Name = "Magic Capture Desktop single-instance listener"
        };
        _listener.Start();

        if (commandReceived is not null)
        {
            _commandListener = new Thread(() => CommandLoop(commandReceived))
            {
                IsBackground = true,
                Name = "Magic Capture Desktop command listener"
            };
            _commandListener.Start();
        }
    }

    private void CommandLoop(Action<IReadOnlyList<string>> commandReceived)
    {
        while (!_disposed)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    CommandPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly);
                pipe.WaitForConnection();
                if (_disposed) return;
                using var reader = new StreamReader(pipe);
                var payload = ReadBoundedPayload(reader);
                var args = JsonSerializer.Deserialize<string[]>(payload) ?? [];
                if (args.Length > 0) commandReceived(args);
            }
            catch (IOException) when (_disposed) { return; }
            catch (ObjectDisposedException) when (_disposed) { return; }
            catch (JsonException) { }
            catch (InvalidDataException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (Exception ex) when (!FatalExceptionPolicy.IsFatal(ex))
            {
                // Keep the listener alive for recoverable callback/runtime failures, but never
                // hide fatal memory/native corruption behind the IPC resilience boundary.
            }
        }
    }

    private static string ReadBoundedPayload(StreamReader reader)
    {
        var buffer = new char[4096];
        var builder = new System.Text.StringBuilder(Math.Min(MaximumCommandPayloadChars, 8192));
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0) return builder.ToString();
            if (builder.Length + read > MaximumCommandPayloadChars)
                throw new InvalidDataException("Single-instance command payload is too large.");
            builder.Append(buffer, 0, read);
        }
    }

    public static bool SignalPrimary()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ShowEventName);
            return existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public static bool SendCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return SignalPrimary();
        try
        {
            var payload = JsonSerializer.Serialize(args);
            if (payload.Length > MaximumCommandPayloadChars) return false;
            using var client = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out);
            client.Connect(750);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(payload);
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopEvent?.Set();
        try
        {
            using var unblock = new NamedPipeClientStream(".", CommandPipeName, PipeDirection.Out);
            unblock.Connect(100);
        }
        catch (TimeoutException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _listener?.Join(TimeSpan.FromMilliseconds(500));
        _commandListener?.Join(TimeSpan.FromMilliseconds(500));
        _showEvent?.Dispose();
        _stopEvent?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }

}
