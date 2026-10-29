using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hive.Infrastructure;

internal static class RobloxWindowSizer
{
    private const uint NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;

    internal static bool ApplyCompactBounds(IntPtr window, int width = 800, int height = 500)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window))
            return false;
        if (IsZoomed(window))
            ShowWindowAsync(window, 9);
        return SetWindowPos(window, IntPtr.Zero, 0, 0, Math.Clamp(width, 320, 7680), Math.Clamp(height, 240, 4320), NoMove | NoZOrder | NoActivate);
    }

    internal static async Task ResizeWhenReadyAsync(int processId, CancellationToken cancellationToken, int width = 800, int height = 500)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var started = process.StartTime.ToUniversalTime();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                process.Refresh();
                if (process.HasExited || process.StartTime.ToUniversalTime() != started)
                    return;
                var window = process.MainWindowHandle;
                if (window != IntPtr.Zero && IsWindowVisible(window))
                {
                    ApplyCompactBounds(window, width, height);
                    await Task.Delay(2000, cancellationToken);
                    process.Refresh();
                    if (!process.HasExited && process.StartTime.ToUniversalTime() == started && process.MainWindowHandle == window)
                        ApplyCompactBounds(window, width, height);
                    return;
                }
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
