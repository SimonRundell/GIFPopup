using System.Windows.Forms;

namespace GifPopup;

/// <summary>
/// Decides whether a given key should be swallowed, given the current set of
/// "armed" hotkeys. Pure logic, no Win32 — kept separate so it can be unit
/// tested without an actual OS hook.
/// </summary>
internal static class KeyMatcher
{
    /// <summary>
    /// Returns true if <paramref name="key"/> is one of the currently armed
    /// keys (the active GIF hotkeys, plus Space) and should therefore be
    /// swallowed and handled by GifPopup rather than passed on.
    /// </summary>
    public static bool ShouldSwallow(Keys key, IReadOnlySet<Keys> armedKeys)
    {
        return armedKeys.Contains(key);
    }
}

/// <summary>
/// Wraps a system-wide WH_KEYBOARD_LL hook. Fires <see cref="KeyPressed"/> for
/// any key in the currently armed set and swallows it; every other key is
/// passed straight through to whichever window actually has focus (e.g.
/// PowerPoint), so this never interferes with normal keyboard use elsewhere.
/// </summary>
internal sealed class GlobalKeyHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private HashSet<Keys> _armedKeys = new();

    /// <summary>Raised on the hook thread when an armed key is pressed.</summary>
    public event Action<Keys>? KeyPressed;

    public GlobalKeyHook()
    {
        // Keep a reference to the delegate for the lifetime of the hook —
        // otherwise the GC can collect it while native code still holds a
        // pointer to it.
        _proc = HookCallback;
    }

    /// <summary>Replaces the set of keys that will be intercepted.</summary>
    public void SetArmedKeys(IEnumerable<Keys> keys)
    {
        _armedKeys = new HashSet<Keys>(keys);
    }

    /// <summary>Installs the low-level keyboard hook. No admin rights required.</summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(currentModule.ModuleName!),
            0);
    }

    /// <summary>Removes the hook if installed.</summary>
    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var key = (Keys)data.vkCode;

            if (KeyMatcher.ShouldSwallow(key, _armedKeys))
            {
                KeyPressed?.Invoke(key);
                return (IntPtr)1; // swallow — do not call CallNextHookEx
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }
}
