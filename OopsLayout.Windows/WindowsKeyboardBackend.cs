using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OopsLayout.Core;

namespace OopsLayout.Windows;

/// <summary>
/// Windows implementation of IKeyboardBackend.
/// Uses SetWindowsHookEx (WH_KEYBOARD_LL) for global key monitoring
/// and SendInput + PostMessage for replacement and layout switching.
/// </summary>
public sealed class WindowsKeyboardBackend : IKeyboardBackend
{
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;

    // Captured on the first hook callback (which runs on the UI thread). Used to
    // re-install the hook on the right thread after the machine resumes.
    private System.Threading.SynchronizationContext? _uiContext;

    // A pending replacement set by ReplaceWord while the engine analyses a word.
    private (int count, string text, string original, SwitchDirection dir)? _pending;

    // Injections run one at a time on this chain — two quick replacements on
    // parallel pool threads would interleave their backspaces/text.
    private Task _injectChain = Task.CompletedTask;
    private readonly object _injectLock = new();

    // The last completed replacement, kept so Backspace pressed right after it
    // can undo the whole thing: delete the corrected text, retype the original,
    // and restore the layout the target window had BEFORE we switched it (its
    // HKL is captured at injection time — that's the piece that makes the
    // layout actually come back). Any other meaningful key, or a change of
    // foreground window, forfeits the undo. Class, not struct: written on the
    // injection thread, read on the hook thread, and a volatile reference swap
    // is atomic where a multi-field struct would not be.
    private sealed record UndoInfo(string Text, IntPtr PrevLayout, IntPtr Hwnd);
    private volatile UndoInfo? _undo;

    private const int InjectGapMs = 3;

    // Password-field guard, kept current by UIA focus-changed events so the
    // hook itself never queries UIA (a blocking call there can exceed the
    // hook timeout and get it silently removed).
    private readonly PasswordFocusWatcher _passwordWatcher = new();

    // Reused across keystrokes so VkToChar doesn't allocate on the hot path.
    private readonly byte[] _keyState = new byte[256];
    private readonly char[] _charBuf  = new char[8];

    // Foreground-app exclusion. In code editors / terminals the switcher does
    // more harm than good (short English tokens, symbols, fast typing), so we
    // stay out entirely. The foreground window is cached; the (slightly pricier)
    // process lookup only runs when the active window actually changes.
    private IntPtr _cachedForeground = IntPtr.Zero;
    private bool   _cachedExcluded;

    // Process names (lowercase, without ".exe") to skip. Add your own here.
    private static readonly HashSet<string> ExcludedProcesses = new()
    {
        // editors / IDEs
        "code", "code - insiders", "devenv", "sublime_text",
        "rider64", "idea64", "pycharm64", "webstorm64", "phpstorm64",
        "clion64", "goland64", "rustrover64", "datagrip64", "rubymine64",
        // terminals
        "windowsterminal", "powershell", "pwsh", "cmd", "conhost",
        "alacritty", "wezterm-gui", "hyper", "mintty", "mobaxterm",
    };

    public event Action<char>? CharTyped;
    public event Action? EnterPressed;
    public event Action? BackspacePressed;
    public event Action<char>? WordBreakPressed;

    public void Start()
    {
        InstallHook();
        // Re-arm after the machine wakes: a low-level hook can be silently
        // dropped during the sleep/resume transition (it's removed if the
        // callback ever times out, which the busy resume moment can trigger).
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Registering the UIA focus listener can take a noticeable moment, so
        // do it off the UI thread. Until it lands, FocusIsPassword is false —
        // same behaviour as before this guard existed.
        Task.Run(() =>
        {
            try { _passwordWatcher.Start(); }
            catch (Exception ex) { CrashLog.Write(ex); }
        });
    }

    public void Stop()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _passwordWatcher.Dispose();
        UninstallHook();
    }

    private void InstallHook()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _hookProc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module  = process.MainModule!;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
    }

    private void UninstallHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        // Re-install on the UI thread (where the hook's message loop lives).
        _uiContext?.Post(_ =>
        {
            UninstallHook();
            InstallHook();
        }, null);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Captured here because this callback always runs on the UI thread, where
        // the WinForms SynchronizationContext is installed.
        _uiContext ??= System.Threading.SynchronizationContext.Current;
        try
        {
            if (nCode >= 0 &&
                (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                // Skip input we injected ourselves (our backspaces/retype).
                if ((kb.flags & NativeMethods.LLKHF_INJECTED) == 0)
                {
                    // If a replacement was triggered we swallow this key (the
                    // break char / Enter) so it never reaches the app, then
                    // re-emit it ourselves as part of the corrected text.
                    if (HandleVirtualKey((uint)kb.vkCode, kb.scanCode))
                        return (IntPtr)1;
                }
            }
        }
        catch (Exception ex)
        {
            // An exception must never escape into the native hook chain — that
            // would crash the entire process. Log it and keep the hook alive.
            CrashLog.Write(ex);
        }
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>Returns true if this key was consumed (a replacement happened).</summary>
    private bool HandleVirtualKey(uint vk, uint scanCode)
    {
        // Stay out of code editors / terminals entirely — don't even buffer.
        if (IsExcludedForegroundApp())
            return false;

        // Never touch password fields: nothing gets buffered while one has
        // focus, and anything buffered before focus landed there is dropped
        // (Backspace is the engine's existing "clear the buffer" path).
        if (_passwordWatcher.FocusIsPassword)
        {
            BackspacePressed?.Invoke();
            return false;
        }

        // Any meaningful key other than Backspace forfeits a pending undo —
        // typing continued, or the caret moved (arrows, Delete, …), so
        // re-injecting old text would land in the wrong place. Bare modifiers
        // (holding Shift, etc.) don't count.
        if (_undo is not null && vk != NativeMethods.VK_BACK && !IsModifierKey(vk))
            _undo = null;

        // Backspace: right after a correction it means "put it back".
        if (vk == NativeMethods.VK_BACK)
        {
            if (TryUndo()) return true; // swallow — the undo replays everything
            BackspacePressed?.Invoke();
            return false;
        }

        // Enter
        if (vk == 0x0D)
            return FlushAndMaybeReplace(() => EnterPressed?.Invoke(), trailing: '\r');

        // Convert VK to character using current keyboard layout
        var c = VkToChar(vk, scanCode);
        if (c == '\0') return false;

        if (c == '\r' || c == '\n')
            return FlushAndMaybeReplace(() => EnterPressed?.Invoke(), trailing: '\r');

        // A character belongs to the word if it maps between layouts. This
        // crucially includes the EN punctuation keys that produce Russian
        // letters (' → э, [ → х, ] → ъ, ; → ж, , → б, . → ю); treating them as
        // breakers would split words like "это" (typed ' n j) before conversion.
        if (char.IsLetterOrDigit(c) || KeyMap.IsEnChar(c) || KeyMap.IsRuChar(c))
        {
            CharTyped?.Invoke(c);
            return false;
        }

        // Anything else (space, !, ?, -, (, ), …) ends the word.
        return FlushAndMaybeReplace(() => WordBreakPressed?.Invoke(c), trailing: c);
    }

    /// <summary>
    /// Runs the word-end notification (which may set <see cref="_pending"/> via
    /// ReplaceWord). If a replacement is pending, schedules it on the UI thread
    /// with <paramref name="trailing"/> appended, and reports that the original
    /// key should be swallowed.
    /// </summary>
    private bool FlushAndMaybeReplace(Action notify, char trailing)
    {
        _pending = null;
        notify();
        if (_pending is not { } p)
            return false;

        _pending = null;
        var text = p.text + (trailing == '\r' ? string.Empty : trailing.ToString());
        var sendEnter = trailing == '\r';

        // Enter-triggered replacements can't be undone (the line/message is
        // already submitted), so no original is kept for them.
        var undoText = sendEnter ? null : p.original + trailing;

        // Run off the hook thread, pacing keys so the target app keeps up.
        EnqueueInjection(() => ExecuteReplace(p.count, text, p.dir, sendEnter, undoText));
        return true;
    }

    /// <summary>
    /// Queues an injection on the serial chain — injections must never overlap
    /// each other (interleaved backspaces/text), so no Task.Run here.
    /// </summary>
    private void EnqueueInjection(Action action)
    {
        lock (_injectLock)
        {
            _injectChain = _injectChain.ContinueWith(_ =>
            {
                try { action(); }
                catch (Exception ex) { CrashLog.Write(ex); } // a faulted task would be silent
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private static bool IsModifierKey(uint vk) =>
        vk is NativeMethods.VK_SHIFT or NativeMethods.VK_CONTROL or NativeMethods.VK_MENU
           or NativeMethods.VK_CAPITAL
           or 0x5B or 0x5C            // Win keys
           or (>= 0xA0 and <= 0xA5);  // L/R Shift, Ctrl, Alt

    /// <summary>
    /// Undo the last replacement if one is still eligible. Returns true when
    /// the undo was queued (the triggering Backspace must then be swallowed).
    /// </summary>
    private bool TryUndo()
    {
        var u = _undo;
        if (u is null) return false;
        _undo = null; // one shot
        if (NativeMethods.GetForegroundWindow() != u.Hwnd)
            return false; // user moved elsewhere — let Backspace act normally

        EnqueueInjection(() => ExecuteUndo(u));
        return true;
    }

    // Deletes the corrected text we injected, retypes what the user actually
    // typed, and hands the window back its pre-correction layout.
    private static void ExecuteUndo(UndoInfo u)
    {
        // Restore the layout FIRST so the user's next keystrokes land in the
        // layout they were typing in before we interfered.
        NativeMethods.PostMessage(u.Hwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST,
            IntPtr.Zero, u.PrevLayout);

        for (int i = 0; i < u.Text.Length; i++)
        {
            SendKeyTap(NativeMethods.VK_BACK);
            Thread.Sleep(InjectGapMs);
        }

        foreach (var ch in u.Text)
        {
            SendUnicodeChar(ch);
            Thread.Sleep(InjectGapMs);
        }
    }

    /// <summary>For the tray: is the switcher currently dormant (excluded app)?</summary>
    public bool IsForegroundExcluded() => IsExcludedForegroundApp();

    /// <summary>True when the active window is a code editor / terminal we skip.</summary>
    private bool IsExcludedForegroundApp()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg != _cachedForeground)
        {
            _cachedForeground = fg;
            _cachedExcluded = ComputeExcluded(fg);
        }
        return _cachedExcluded;
    }

    private static bool ComputeExcluded(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return ExcludedProcesses.Contains(p.ProcessName.ToLowerInvariant());
        }
        catch
        {
            return false; // process gone / access denied — treat as not excluded
        }
    }

    private char VkToChar(uint vk, uint scanCode)
    {
        // Ctrl/Alt combinations are shortcuts, not text — don't buffer them.
        bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        bool alt  = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU)    & 0x8000) != 0;
        if (ctrl || alt) return '\0';

        var hwnd   = NativeMethods.GetForegroundWindow();
        var thread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var layout = NativeMethods.GetKeyboardLayout(thread);

        // Reuse _keyState; only the modifier bytes ever change, so set them each
        // call (the rest of the 256 stay zero for the lifetime of the buffer).
        _keyState[NativeMethods.VK_SHIFT]   =
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0 ? (byte)0x80 : (byte)0;
        _keyState[NativeMethods.VK_CAPITAL] =
            (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0 ? (byte)0x01 : (byte)0;

        int rc = ToUnicodeEx(vk, scanCode, _keyState, _charBuf, _charBuf.Length, 0, layout);
        if (rc < 0)
        {
            // Dead key — e.g. the ' / [ keys on US-International, which are also
            // the Russian letters э / х. _charBuf[0] holds the dead-key character,
            // so capture it; then call again to flush the armed state so it
            // doesn't combine with (corrupt) the next keystroke.
            char dead = _charBuf[0];
            ToUnicodeEx(vk, scanCode, _keyState, _charBuf, _charBuf.Length, 0, layout);
            return dead;
        }
        return rc >= 1 ? _charBuf[0] : '\0';
    }

    // CharSet.Unicode is essential: ToUnicodeEx writes 2-byte WCHARs. Without it
    // the char[] buffer is marshalled as 1-byte ANSI and the native write
    // overruns it, corrupting the heap (crash 0xc0000374).
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode,
        byte[] lpKeyState, [Out] char[] pwszBuff,
        int cchBuff, uint wFlags, IntPtr dwhkl);

    // ── ReplaceWord ───────────────────────────────────────────────────────────

    public void ReplaceWord(int count, string newText, string originalText, SwitchDirection direction)
    {
        // Called by the engine from within the hook callback. We only *record*
        // the request here; FlushAndMaybeReplace schedules the actual injection
        // on the UI thread once the triggering key has been swallowed.
        _pending = (count, newText, originalText, direction);
    }

    // Runs on the injection chain. Keys are sent one at a time with small pauses
    // so apps that read input asynchronously (browsers, Electron) don't drop any
    // of the rapid backspaces — sending them as one big batch loses some.
    // The inter-key gap is kept short, because every millisecond here is a
    // millisecond the user's next keystrokes can race against this injection.
    private void ExecuteReplace(int count, string newText, SwitchDirection direction,
        bool sendEnter, string? undoText)
    {
        // Capture what we're about to change, so Backspace can put it back:
        // the target window and the layout it currently types in.
        var hwnd = NativeMethods.GetForegroundWindow();
        var thread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var prevLayout = NativeMethods.GetKeyboardLayout(thread);

        // Switch the layout FIRST, so it has the whole (short) injection window to
        // take effect before the user types the next word.
        SwitchLayout(direction);

        for (int i = 0; i < count; i++)
        {
            SendKeyTap(NativeMethods.VK_BACK);
            Thread.Sleep(InjectGapMs);
        }

        foreach (var ch in newText)
        {
            SendUnicodeChar(ch);
            Thread.Sleep(InjectGapMs);
        }

        if (sendEnter)
            SendKeyTap(NativeMethods.VK_RETURN);

        // Only now does the undo become available — before the injection has
        // finished there is nothing consistent to roll back.
        _undo = undoText is null ? null : new UndoInfo(undoText, prevLayout, hwnd);
    }

    private static void SendKeyTap(ushort vk)
    {
        var inputs = new[]
        {
            MakeKeyInput(vk, 0),
            MakeKeyInput(vk, NativeMethods.KEYEVENTF_KEYUP)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendUnicodeChar(char c)
    {
        var inputs = new[]
        {
            MakeUnicodeInput(c, 0),
            MakeUnicodeInput(c, 0x0002) // KEYUP
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT MakeKeyInput(ushort vk, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = flags }
        }
    };

    private static NativeMethods.INPUT MakeUnicodeInput(char c, uint extraFlags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk    = 0,
                wScan  = c,
                dwFlags = 0x0004 | extraFlags // KEYEVENTF_UNICODE
            }
        }
    };

    private static void SwitchLayout(SwitchDirection direction)
    {
        int primary = direction == SwitchDirection.EnToRu
            ? (WordBuffer.Target == Cyrillic.Ukrainian
                ? NativeMethods.LANG_PRIMARY_UK
                : NativeMethods.LANG_PRIMARY_RU)
            : NativeMethods.LANG_PRIMARY_EN;

        // Switch only to a layout the user already has installed (e.g. UK
        // English). If none matches we do NOTHING — never load/force a new one
        // (the old fallback to US English actually added it to the user's list).
        var layout = FindInstalledLayout(primary);
        if (layout == IntPtr.Zero) return;

        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST,
            IntPtr.Zero, layout);
    }

    private static IntPtr FindInstalledLayout(int primaryLangId)
    {
        var list = new IntPtr[32];
        int n = NativeMethods.GetKeyboardLayoutList(list.Length, list);
        for (int i = 0; i < n; i++)
        {
            // The low word of an HKL is the layout's LANGID; mask to the primary id.
            int langId = (int)((long)list[i] & 0xFFFF);
            if ((langId & 0x3FF) == primaryLangId)
                return list[i];
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Stop();
}
