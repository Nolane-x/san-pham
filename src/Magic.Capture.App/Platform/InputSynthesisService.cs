using System.ComponentModel;
using System.Runtime.InteropServices;
using Magic.Capture.App.Platform.Native;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.Geometry;

namespace Magic.Capture.App.Platform;

internal sealed class InputSynthesisService
{
    public NativePoint GetCursorPosition()
    {
        if (!NativeMethods.GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read cursor position.");
        return point;
    }

    public void SetCursorPosition(PixelPoint point)
    {
        if (!NativeMethods.SetCursorPos(point.X, point.Y)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to position cursor for scrolling capture.");
    }

    public void ScrollVertical(int wheelDelta) => ScrollWheel(wheelDelta, NativeConstants.MouseEventWheel, "vertical");

    public void ScrollHorizontal(int wheelDelta) => ScrollWheel(wheelDelta, NativeConstants.MouseEventHWheel, "horizontal");

    public void Scroll(ScrollVector vector)
    {
        if (vector.HorizontalWheelDelta != 0) ScrollHorizontal(vector.HorizontalWheelDelta);
        if (vector.VerticalWheelDelta != 0) ScrollVertical(vector.VerticalWheelDelta);
    }

    private static void ScrollWheel(int wheelDelta, uint flags, string axis)
    {
        if (wheelDelta == 0) return;
        const int maximumChunk = NativeConstants.WheelDelta * 20;
        var absolute = Math.Abs((long)wheelDelta);
        var chunkCount = checked((int)((absolute + maximumChunk - 1) / maximumChunk));
        if (chunkCount > 64) throw new ArgumentOutOfRangeException(nameof(wheelDelta), "Synthetic scrolling exceeds the bounded input budget.");
        var remaining = (long)wheelDelta;
        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunk = (int)Math.Clamp(remaining, -maximumChunk, maximumChunk);
            var inputs = new[]
            {
                new NativeInput
                {
                    Type = NativeConstants.InputMouse,
                    Mouse = new NativeMouseInput
                    {
                        MouseData = unchecked((uint)chunk),
                        Flags = flags
                    }
                }
            };
            if (NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeInput>()) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows did not accept the {axis} scrolling input.");
            remaining -= chunk;
        }
    }
}
