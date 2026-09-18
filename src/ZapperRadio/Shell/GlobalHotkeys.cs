using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using VirtualKey = Windows.System.VirtualKey;

namespace ZapperRadio.Shell;

/// <summary>
/// Keyboard shortcuts that work while another app has focus. Windows delivers them as WM_HOTKEY to the window
/// they were registered for, so the window procedure is chained to watch for that one message.
/// A combination another app already holds cannot be registered; that is reported instead of thrown, because
/// which shortcuts are free depends on whatever else the machine is running.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int GwlpWndProc = -4;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;

    /// <summary>Holding the keys down should not repeat the action; one press is one zap.</summary>
    private const uint ModNoRepeat = 0x4000;

    /// <summary>Well clear of the identifiers any other component of the app might use.</summary>
    private const int FirstId = 0xA100;

    private readonly nint _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<int, Action> _actions = [];

    /// <summary>Kept in a field: Windows calls this delegate long after <see cref="Register"/> returns.</summary>
    private readonly WindowProc _windowProc;

    private nint _previousProc;
    private int _nextId = FirstId;
    private bool _disposed;

    public GlobalHotkeys(nint hwnd, DispatcherQueue dispatcher)
    {
        _hwnd = hwnd;
        _dispatcher = dispatcher;
        _windowProc = OnMessage;
    }

    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    /// <summary>
    /// Claims Ctrl+Alt+<paramref name="key"/> system wide and runs <paramref name="action"/> on the UI thread
    /// when it is pressed. Returns false when another app got there first, leaving the other shortcuts working.
    /// </summary>
    public bool Register(VirtualKey key, Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Attach();

        var id = _nextId;
        if (!RegisterHotKey(_hwnd, id, ModControl | ModAlt | ModNoRepeat, (uint)key))
        {
            return false;
        }

        _nextId++;
        _actions[id] = action;
        return true;
    }

    /// <summary>Gives every claimed combination back to Windows, so another app can use it again.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }

        _actions.Clear();
        _nextId = FirstId;
        Detach();
    }

    /// <summary>Chains this window procedure in front of the one the window already has.</summary>
    private void Attach()
    {
        if (_previousProc == 0)
        {
            _previousProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_windowProc));
        }
    }

    private void Detach()
    {
        if (_previousProc != 0)
        {
            SetWindowLongPtr(_hwnd, GwlpWndProc, _previousProc);
            _previousProc = 0;
        }
    }

    private nint OnMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotkey && _actions.TryGetValue((int)wParam, out var action))
        {
            // Queued rather than run here, so playback never starts from inside a window message.
            _dispatcher.TryEnqueue(() => action());
            return 0;
        }

        return CallWindowProc(_previousProc, window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint previous, nint hwnd, uint message, nint wParam, nint lParam);
}
