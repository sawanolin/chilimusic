using System.Runtime.InteropServices;
namespace ChiliMusic;
public sealed class FunctionKeyService : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct KeyData { public uint Vk, Scan, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    public static bool KeyAvailable(int key) { const int id = 0x4C43; if (!RegisterHotKey(IntPtr.Zero, id, 0x4000, (uint)key)) return false; UnregisterHotKey(IntPtr.Zero, id); return true; }
    private readonly HookProc _proc; private IntPtr _hook; private readonly PlayerViewModel _vm; private bool _winHeld; private readonly HashSet<uint> _blocked = [];
    private static readonly UIntPtr MaskTag = new(0x4e4d50);
    public bool Active => _hook != IntPtr.Zero;
    public FunctionKeyService(PlayerViewModel vm) { _vm = vm; _proc = OnKey; _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0); if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    private IntPtr OnKey(int code, IntPtr message, IntPtr ptr)
    {
        if (code < 0) return CallNextHookEx(_hook, code, message, ptr); var key = Marshal.PtrToStructure<KeyData>(ptr);
        if (key.Extra == MaskTag) return CallNextHookEx(_hook, code, message, ptr);
        bool down = message.ToInt64() is 0x100 or 0x104, up = message.ToInt64() is 0x101 or 0x105;
        if (key.Vk is 0x5b or 0x5c) _winHeld = down;
        if (up && _blocked.Remove(key.Vk)) return new IntPtr(1);
        bool otherModifier = (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x10) & 0x8000) != 0;
        if (!_vm.Settings.HotkeysEnabled) return CallNextHookEx(_hook, code, message, ptr);
        bool play = !otherModifier && (key.Vk == _vm.Settings.PlayKey && !_winHeld || (_vm.Settings.RemapHardwareShortcuts && _winHeld && key.Vk == 0x50));
        bool pause = !otherModifier && (key.Vk == _vm.Settings.PauseKey && !_winHeld || (_vm.Settings.RemapHardwareShortcuts && _winHeld && key.Vk == 0xbe));
        if (down && (play || pause))
        {
            if (_blocked.Add(key.Vk))
            {
                if (_winHeld) MaskWindowsMenu();
                Application.Current.Dispatcher.BeginInvoke(() => { if (play) _vm.Run(PlayAsync); else { _vm.PausePlayback(); _vm.Changed(nameof(_vm.PlayGlyph)); _vm.Status = "已暂停"; } });
            }
            return new IntPtr(1);
        }
        return CallNextHookEx(_hook, code, message, ptr);
    }
    private Task PlayAsync() => _vm.ResumePlaybackAsync();
    private static void MaskWindowsMenu() { var input = new[] { new Input { Type = 1, Data = new() { Key = new() { Vk = 0x11, Extra = MaskTag } } }, new Input { Type = 1, Data = new() { Key = new() { Vk = 0x11, Flags = 2, Extra = MaskTag } } } }; SendInput(2, input, Marshal.SizeOf<Input>()); }
    public void Dispose() { if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } _blocked.Clear(); }
}
