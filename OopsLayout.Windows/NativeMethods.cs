using System.Runtime.InteropServices;

namespace OopsLayout.Windows;

internal static class NativeMethods
{
    // ── Keyboard hook ────────────────────────────────────────────────────────
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN     = 0x0100;
    public const int WM_SYSKEYDOWN  = 0x0104;

    // Set in KBDLLHOOKSTRUCT.flags when the event was injected (e.g. by our SendInput).
    public const uint LLKHF_INJECTED = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint   vkCode;
        public uint   scanCode;
        public uint   flags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                  IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
                                               IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Frees an HICON created by Bitmap.GetHicon (used for the runtime grey icon).
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── Keyboard state ────────────────────────────────────────────────────────
    public const int VK_SHIFT   = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU    = 0x12; // Alt
    public const int VK_CAPITAL = 0x14; // CapsLock

    // Reads the real, physical key state regardless of which thread has focus —
    // unlike GetKeyState, which only reflects our own (unfocused) message queue.
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    // ── Input simulation ─────────────────────────────────────────────────────
    public const uint INPUT_KEYBOARD  = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_BACK       = 0x08;
    public const ushort VK_RETURN     = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int    dx;
        public int    dy;
        public uint   mouseData;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        // MOUSEINPUT is the largest variant; including it makes the union (and
        // thus INPUT) the size Windows expects. Without it, INPUT is too small
        // and SendInput silently fails the cbSize check and injects nothing.
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── Layout switching ──────────────────────────────────────────────────────
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    // Primary language IDs (LANGID & 0x3FF) — same for any sub-variant, so this
    // matches US *and* UK English, etc.
    public const int LANG_PRIMARY_EN = 0x09;
    public const int LANG_PRIMARY_RU = 0x19;
    public const int LANG_PRIMARY_UK = 0x22; // Ukrainian

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // Language IDs
    public const string LANG_EN = "00000409"; // English US
    public const string LANG_RU = "00000419"; // Russian
}
