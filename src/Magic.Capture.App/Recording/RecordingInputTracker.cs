using System.ComponentModel;
using System.Runtime.InteropServices;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Recording;

namespace Magic.Capture.App.Recording;

internal sealed class RecordingInputTracker : IDisposable
{
    private const int MaximumClicks = 16;
    private const int MaximumStrokes = 128;
    private readonly object _gate = new();
    private readonly RecordingRect _target;
    private readonly RecordingOptions _options;
    private readonly Func<TimeSpan> _activeElapsed;
    private readonly NativeMethods.LowLevelHookProc _mouseProc;
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;
    private readonly List<RecordingClickEvent> _clicks = new(MaximumClicks);
    private readonly List<RecordingStroke> _strokes = new(MaximumStrokes);
    private List<RecordingPoint>? _activeStroke;
    private TimeSpan _activeStrokeStarted;
    private RecordingPoint _cursor = new(-1, -1);
    private RecordingKeyOverlay? _key;
    private bool _control;
    private bool _alt;
    private bool _shift;
    private bool _win;
    private bool _paused;
    private bool _zoomActive;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _disposed;

    public RecordingInputTracker(RecordingTarget target, RecordingOptions options, Func<TimeSpan> activeElapsed)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(activeElapsed);
        _target = new RecordingRect(target.Bounds.X, target.Bounds.Y, target.Bounds.Width, target.Bounds.Height);
        _options = RecordingRules.Normalize(options);
        _activeElapsed = activeElapsed;
        _mouseProc = MouseHook;
        _keyboardProc = KeyboardHook;
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero) return;
        var needsMouse = _options.CursorHighlight || _options.ClickVisualization || _options.DrawWhileRecording || _options.LiveZoom;
        var needsKeyboard = _options.SafeKeyOverlay || _options.DrawWhileRecording || _options.LiveZoom;
        if (needsMouse)
        {
            _mouseHook = NativeMethods.SetWindowsHookExW(NativeConstants.WhMouseLl, _mouseProc, IntPtr.Zero, 0);
            if (_mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the recording mouse hook.");
        }
        if (needsKeyboard)
        {
            _keyboardHook = NativeMethods.SetWindowsHookExW(NativeConstants.WhKeyboardLl, _keyboardProc, IntPtr.Zero, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
                throw new Win32Exception(error, "Unable to install the recording keyboard hook.");
            }
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
            _activeStroke = null;
            if (paused)
            {
                _control = _alt = _shift = _win = false;
                _key = null;
            }
        }
    }

    public RecordingInputSnapshot Snapshot(TimeSpan now)
    {
        lock (_gate)
        {
            _clicks.RemoveAll(click => now - click.Timestamp >= RecordingEffectsPolicy.RippleLifetime);
            var key = _key is not null && now - _key.Timestamp < RecordingEffectsPolicy.KeyOverlayLifetime ? _key : null;
            var strokes = new List<RecordingStroke>(_strokes.Count + (_activeStroke is null ? 0 : 1));
            strokes.AddRange(_strokes);
            if (_activeStroke is { Count: > 1 })
                strokes.Add(new RecordingStroke(RecordingEffectsPolicy.BoundStroke(_activeStroke), _activeStrokeStarted, now));
            return new RecordingInputSnapshot(_cursor, _clicks.ToArray(), strokes.ToArray(), key, _zoomActive);
        }
    }

    private IntPtr MouseHook(int code, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<LowLevelMouseHookStruct>(lParam);
                lock (_gate)
                {
                    _cursor = RecordingEffectsPolicy.MapDesktopPointToTarget(data.Point.X, data.Point.Y, _target);
                    if (!_paused)
                        ProcessMouseMessage(unchecked((uint)wParam.ToUInt64()), _cursor, _activeElapsed());
                }
            }
        }
        catch (Exception) when (!_disposed)
        {
            // Hook callbacks must never break the system input chain. The recorder's bounded state is best-effort.
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void ProcessMouseMessage(uint message, RecordingPoint point, TimeSpan now)
    {
        if (message == NativeConstants.WmLButtonDown)
        {
            if (_options.ClickVisualization && InsideTarget(point)) AddClick(new RecordingClickEvent(point, RecordingMouseButton.Left, now));
            if (_options.DrawWhileRecording && _control && _alt && InsideTarget(point))
            {
                _activeStrokeStarted = now;
                _activeStroke = new List<RecordingPoint>(128) { point };
            }
        }
        else if (message == NativeConstants.WmRButtonDown)
        {
            if (_options.ClickVisualization && InsideTarget(point)) AddClick(new RecordingClickEvent(point, RecordingMouseButton.Right, now));
        }
        else if (message == NativeConstants.WmMouseMove && _activeStroke is not null && InsideTarget(point))
        {
            if (_activeStroke.Count < RecordingEffectsPolicy.MaximumStrokePoints && (_activeStroke.Count == 0 || _activeStroke[^1] != point))
                _activeStroke.Add(point);
        }
        else if (message == NativeConstants.WmLButtonUp && _activeStroke is { Count: > 1 } stroke)
        {
            var bounded = RecordingEffectsPolicy.BoundStroke(stroke);
            if (_strokes.Count == MaximumStrokes) _strokes.RemoveAt(0);
            _strokes.Add(new RecordingStroke(bounded, _activeStrokeStarted, now));
            _activeStroke = null;
        }
    }

    private void AddClick(RecordingClickEvent click)
    {
        if (_clicks.Count == MaximumClicks) _clicks.RemoveAt(0);
        _clicks.Add(click);
    }

    private IntPtr KeyboardHook(int code, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var message = unchecked((uint)wParam.ToUInt64());
                var data = Marshal.PtrToStructure<LowLevelKeyboardHookStruct>(lParam);
                lock (_gate)
                {
                    UpdateModifier(data.VirtualKey, message is NativeConstants.WmKeyDown or NativeConstants.WmSysKeyDown);
                    if (!_paused && message is NativeConstants.WmKeyDown or NativeConstants.WmSysKeyDown)
                    {
                        var now = _activeElapsed();
                        var label = RecordingSafeKeyFormatter.Format(data.VirtualKey, _control, _alt, _shift, _win);
                        if (_options.SafeKeyOverlay && label is not null) _key = new RecordingKeyOverlay(label, now);
                        if (_options.LiveZoom && _control && _alt && data.VirtualKey == 0x5A) _zoomActive = !_zoomActive;
                    }
                }
            }
        }
        catch (Exception) when (!_disposed)
        {
            // Never interfere with the keyboard hook chain because an overlay feature failed.
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private bool InsideTarget(RecordingPoint point) =>
        point.X >= 0 && point.Y >= 0 && point.X < _target.Width && point.Y < _target.Height;

    private void UpdateModifier(uint key, bool down)
    {
        if (key is NativeConstants.VkControl or NativeConstants.VkLControl or NativeConstants.VkRControl) _control = down;
        else if (key is NativeConstants.VkMenu or NativeConstants.VkLMenu or NativeConstants.VkRMenu) _alt = down;
        else if (key is NativeConstants.VkShift or NativeConstants.VkLShift or NativeConstants.VkRShift) _shift = down;
        else if (key is NativeConstants.VkLWin or NativeConstants.VkRWin) _win = down;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }
}
