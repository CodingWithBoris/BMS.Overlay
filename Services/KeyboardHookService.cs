using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace BMS.Overlay.Services;

/// <summary>
/// Low-level keyboard + mouse hook that observes key presses without consuming them.
/// Unlike RegisterHotKey, the key event still passes through to other applications
/// (e.g., Roblox still receives M to open the map).
/// 
/// Includes Roblox chat detection: when the user presses "/" to open chat,
/// registered key handlers are suppressed until Enter, Escape, or a mouse click
/// closes the chat.
/// </summary>
public class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private readonly LowLevelMouseProc _mouseHookProc;
    private readonly Dictionary<Key, Action> _handlers = new();
    private bool _disposed;

    // Roblox chat state: "/" opens chat, Enter/Escape/click closes it
    private bool _isChatOpen = false;

    public KeyboardHookService()
    {
        // Must keep references to delegates to prevent GC collection
        _keyboardHookProc = KeyboardHookCallback;
        _mouseHookProc = MouseHookCallback;
        InstallHooks();
    }

    private void InstallHooks()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);

        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, moduleHandle, 0);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);

        if (_keyboardHookId == IntPtr.Zero)
            System.Diagnostics.Debug.WriteLine("[KeyboardHook] Failed to install keyboard hook");
        else
            System.Diagnostics.Debug.WriteLine("[KeyboardHook] Keyboard hook installed");

        if (_mouseHookId == IntPtr.Zero)
            System.Diagnostics.Debug.WriteLine("[KeyboardHook] Failed to install mouse hook");
        else
            System.Diagnostics.Debug.WriteLine("[KeyboardHook] Mouse hook installed (for chat-close detection)");
    }

    /// <summary>
    /// Register a key to observe. The key press will NOT be consumed — it still
    /// passes through to other applications.
    /// </summary>
    public void Register(Key key, Action handler)
    {
        _handlers[key] = handler;
        System.Diagnostics.Debug.WriteLine($"[KeyboardHook] Registered passthrough handler for {key}");
    }

    /// <summary>
    /// Unregister a previously registered key.
    /// </summary>
    public void Unregister(Key key)
    {
        _handlers.Remove(key);
        System.Diagnostics.Debug.WriteLine($"[KeyboardHook] Unregistered handler for {key}");
    }

    /// <summary>
    /// Unregister all keys.
    /// </summary>
    public void UnregisterAll()
    {
        _handlers.Clear();
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_KEYDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var key = KeyInterop.KeyFromVirtualKey((int)hookStruct.vkCode);

            // ── Roblox chat detection ──
            // "/" key (OemQuestion) opens chat
            if (key == Key.OemQuestion || key == Key.Divide)
            {
                if (!_isChatOpen)
                {
                    _isChatOpen = true;
                    System.Diagnostics.Debug.WriteLine("[KeyboardHook] Chat opened (/ pressed)");
                }
            }

            // Enter or Escape closes chat
            if (_isChatOpen && (key == Key.Return || key == Key.Escape))
            {
                _isChatOpen = false;
                System.Diagnostics.Debug.WriteLine($"[KeyboardHook] Chat closed ({key} pressed)");
            }

            // Only fire registered handlers when chat is NOT open
            if (!_isChatOpen && _handlers.TryGetValue(key, out var handler))
            {
                try
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(handler);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[KeyboardHook] Handler error: {ex.Message}");
                }
            }
        }

        // Always call next hook — never consume the key
        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            // Left click closes Roblox chat (right click does not)
            if (_isChatOpen && msg == WM_LBUTTONDOWN)
            {
                _isChatOpen = false;
                System.Diagnostics.Debug.WriteLine("[KeyboardHook] Chat closed (left click)");
            }
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }

        _handlers.Clear();
        System.Diagnostics.Debug.WriteLine("[KeyboardHook] All hooks uninstalled");
    }
}
