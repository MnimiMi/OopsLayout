using System.Runtime.InteropServices;

namespace OopsLayout.Windows;

/// <summary>
/// Tracks whether the focused UI element is a password field, via UI Automation
/// focus-changed events. Uses the modern COM UIA client (what Narrator uses),
/// not the legacy WPF one: an event subscription from this client is what makes
/// Chromium browsers expose their web content tree (and thus IsPassword) at all
/// — the WPF client only ever saw the page as one opaque Document.
///
/// Everything that touches UIA runs on ONE dedicated MTA thread we own. The
/// focus-changed callback (which UIA raises on its own thread) does the absolute
/// minimum — it just wakes our thread — and never calls back into UIA. Calling
/// UIA synchronously from inside its own event callback is the documented way to
/// deadlock or crash it, and that reentrancy (plus creating the client on a
/// throwaway pool thread) is the likeliest cause of the earlier hard crash when
/// focus entered a password field.
/// </summary>
internal sealed class PasswordFocusWatcher : IDisposable
{
    // Written on the UIA worker thread, read on the keyboard-hook thread.
    public volatile bool FocusIsPassword;

    private Thread? _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _disposed;

    // Touched only on the worker thread.
    private IUIAutomation? _uia;
    private Handler? _handler;

    private const int UIA_IsPasswordPropertyId = 30019;

    // The worker re-reads focus on every focus-changed event, but also at least
    // this often as a safety net: if a focus event is ever missed, the flag
    // can't stay stale (e.g. stuck "password") longer than this.
    private const int PollIntervalMs = 400;

    public void Start()
    {
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "OopsLayout-UIA",
        };
        // MTA: UIA client calls and the client's own callback threads are all
        // MTA, so cross-thread calls need no marshalling and no message pump.
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public bool IsAlive => _thread?.IsAlive == true;

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        // Best effort — never block app shutdown on a stuck cross-process call.
        _thread?.Join(TimeSpan.FromMilliseconds(500));
    }

    private void ThreadProc()
    {
        try
        {
            _uia = (IUIAutomation)new CUIAutomation();
            RefreshFocus();                 // seed the flag with the current focus
            _handler = new Handler(this);
            _uia.AddFocusChangedEventHandler(IntPtr.Zero, _handler);

            while (true)
            {
                // Event-driven (instant response) with a timeout that also
                // re-reads focus, so a missed focus event can't leave the flag
                // stuck. The event subscription above is what keeps Chromium's
                // accessibility tree awake; this timed re-read is just a net.
                _wake.WaitOne(PollIntervalMs);
                if (_disposed) break;
                RefreshFocus();
            }
        }
        catch (Exception ex)
        {
            // Managed failures during setup/teardown. A native access violation
            // is NOT catchable here — the whole point of this thread model is to
            // stop one from happening, not to swallow it.
            CrashLog.Write(ex);
        }
        finally
        {
            try
            {
                if (_uia is not null && _handler is not null)
                    _uia.RemoveFocusChangedEventHandler(_handler);
            }
            catch { /* UIA teardown can be flaky; we're exiting anyway */ }
            _uia = null;
            _handler = null;
            ActivityLog.Write("UIA watcher: focus handler removed, thread exited");
        }
    }

    /// <summary>Read the currently focused element and update the flag. Worker thread only.</summary>
    private void RefreshFocus()
    {
        try
        {
            var focused = _uia?.GetFocusedElement();
            FocusIsPassword = focused is not null
                && focused.GetCurrentPropertyValue(UIA_IsPasswordPropertyId) is true;
        }
        catch
        {
            // Element vanished mid-query / provider busy — not a password.
            FocusIsPassword = false;
        }
    }

    private sealed class Handler : IUIAutomationFocusChangedEventHandler
    {
        private readonly PasswordFocusWatcher _owner;
        public Handler(PasswordFocusWatcher owner) => _owner = owner;

        // Runs on a UIA-owned thread. Do the minimum and NEVER call back into
        // UIA or throw here — just nudge the worker to re-read focus. The
        // `sender` element is deliberately ignored (touching it would be a
        // cross-process call on UIA's own callback thread).
        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        {
            try { _owner._wake.Set(); }
            catch { /* disposed during shutdown — nothing to do */ }
        }
    }
}

// ── Minimal COM interop for the UIA client ─────────────────────────────────
// Placeholders only occupy vtable slots (order from UIAutomationClient.idl);
// they are never called. Verified live against Chrome and Win32 edits.

[ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
internal class CUIAutomation { }

[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    // CompareElements, CompareRuntimeIds, GetRootElement, ElementFromHandle,
    // ElementFromPoint
    void _00(); void _01(); void _02(); void _03(); void _04();
    IUIAutomationElement GetFocusedElement();                       // slot 6
    // GetRootElementBuildCache, ElementFromHandleBuildCache,
    // ElementFromPointBuildCache, GetFocusedElementBuildCache, CreateTreeWalker,
    // get_ControlViewWalker, get_ContentViewWalker, get_RawViewWalker,
    // get_RawViewCondition, get_ControlViewCondition, get_ContentViewCondition,
    // CreateCacheRequest, CreateTrueCondition, CreateFalseCondition,
    // CreatePropertyCondition, CreatePropertyConditionEx, CreateAndCondition,
    // CreateAndConditionFromArray, CreateAndConditionFromNativeArray,
    // CreateOrCondition, CreateOrConditionFromArray,
    // CreateOrConditionFromNativeArray, CreateNotCondition,
    // AddAutomationEventHandler, RemoveAutomationEventHandler,
    // AddPropertyChangedEventHandlerNativeArray, AddPropertyChangedEventHandler,
    // RemovePropertyChangedEventHandler, AddStructureChangedEventHandler,
    // RemoveStructureChangedEventHandler
    void _06(); void _07(); void _08(); void _09(); void _10();
    void _11(); void _12(); void _13(); void _14(); void _15();
    void _16(); void _17(); void _18(); void _19(); void _20();
    void _21(); void _22(); void _23(); void _24(); void _25();
    void _26(); void _27(); void _28(); void _29(); void _30();
    void _31(); void _32(); void _33(); void _34(); void _35();
    void AddFocusChangedEventHandler(IntPtr cacheRequest,           // slot 37
        IUIAutomationFocusChangedEventHandler handler);
    void RemoveFocusChangedEventHandler(                            // slot 38
        IUIAutomationFocusChangedEventHandler handler);
}

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    // SetFocus, GetRuntimeId, FindFirst, FindAll, FindFirstBuildCache,
    // FindAllBuildCache, BuildUpdatedCache
    void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06();
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCurrentPropertyValue(int propertyId);                 // slot 8
}

[ComImport, Guid("c270f6b5-5c69-4290-9745-7a7f97169468"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationFocusChangedEventHandler
{
    void HandleFocusChangedEvent(IUIAutomationElement sender);
}
