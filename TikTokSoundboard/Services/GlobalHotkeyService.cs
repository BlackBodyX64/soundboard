using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TikTokSoundboard.Services;

/// <summary>
/// Low-level global keyboard hook for hotkey support.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private readonly Dictionary<int, string> _vkToKey = new();
    private readonly HashSet<string> _registeredKeys = new();
    private bool _active = true;

    public event Action<string>? KeyTriggered;

    public GlobalHotkeyService()
    {
        _proc = HookCallback;
        BuildVkMap();
    }

    public bool IsActive
    {
        get => _active;
        set => _active = value;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    public void SetRegisteredKeys(IEnumerable<string> keys)
    {
        _registeredKeys.Clear();
        foreach (var k in keys)
            _registeredKeys.Add(k);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_KEYDOWN && _active)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (_vkToKey.TryGetValue(hookStruct.vkCode, out var key) && _registeredKeys.Contains(key))
            {
                KeyTriggered?.Invoke(key);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void BuildVkMap()
    {
        // F1-F12
        for (int i = 1; i <= 12; i++)
            _vkToKey[0x6F + i] = $"F{i}"; // VK_F1=0x70 .. VK_F12=0x7B

        // 0-9
        for (int i = 0; i <= 9; i++)
            _vkToKey[0x30 + i] = i.ToString();

        // A-Z
        for (int i = 0; i < 26; i++)
            _vkToKey[0x41 + i] = ((char)('A' + i)).ToString();

        // Numpad 0-9
        for (int i = 0; i <= 9; i++)
            _vkToKey[0x60 + i] = $"Num{i}";

        // Numpad special
        _vkToKey[0x6E] = "Num.";  // VK_DECIMAL
        _vkToKey[0x6B] = "Num+";  // VK_ADD
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
