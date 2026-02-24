using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace BMS.Overlay.Services;

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private int _idCounter = 1;
    private readonly Dictionary<int, Action> _handlers = new();
    private IntPtr _windowHandle;

    public HotkeyService(Window window)
    {
        _window = window;
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
    }

    public void Register(Key key, Action handler, ModifierKeys modifiers = ModifierKeys.None)
    {
        int id = _idCounter++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint mods = (uint)modifiers;

        if (RegisterHotKey(_windowHandle, id, mods, vk))
        {
            _handlers[id] = handler;
        }
    }

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == 0x0312) // WM_HOTKEY
        {
            int id = (int)msg.wParam;
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler();
                handled = true;
            }
        }
    }

    public void Dispose()
    {
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_windowHandle, id);
        }
    }
}
