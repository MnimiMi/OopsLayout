using System.Runtime.InteropServices;

namespace OopsLayout.Windows;

/// <summary>
/// Tracks whether the focused UI element is a password field, via UI Automation
/// focus-changed events. Uses the modern COM UIA client (what Narrator uses),
/// not the legacy WPF one: an event subscription from this client is what makes
/// Chromium browsers expose their web content tree (and thus IsPassword) at all
/// — the WPF client only ever saw the page as one opaque Document.
/// </summary>
internal sealed class PasswordFocusWatcher : IDisposable
{
    // Written on UIA's callback thread, read on the keyboard-hook thread.
    public volatile bool FocusIsPassword;

    private IUIAutomation? _uia;
    private Handler? _handler;

    /// <summary>
    /// Call from a background thread: creating the client and subscribing are
    /// cross-process COM calls that can take a noticeable moment.
    /// </summary>
    public void Start()
    {
        _uia = (IUIAutomation)new CUIAutomation();
        _handler = new Handler(this);
        FocusIsPassword = IsPassword(_uia.GetFocusedElement());
        _uia.AddFocusChangedEventHandler(IntPtr.Zero, _handler);
    }

    public void Dispose()
    {
        if (_uia is not null && _handler is not null)
        {
            try { _uia.RemoveFocusChangedEventHandler(_handler); }
            catch { /* UIA teardown can be flaky; we're exiting anyway */ }
        }
        _uia = null;
        _handler = null;
    }

    private const int UIA_IsPasswordPropertyId = 30019;

    private static bool IsPassword(IUIAutomationElement element)
    {
        try
        {
            return element.GetCurrentPropertyValue(UIA_IsPasswordPropertyId) is true;
        }
        catch
        {
            return false; // element vanished mid-query — treat as not a password
        }
    }

    private sealed class Handler : IUIAutomationFocusChangedEventHandler
    {
        private readonly PasswordFocusWatcher _owner;
        public Handler(PasswordFocusWatcher owner) => _owner = owner;

        public void HandleFocusChangedEvent(IUIAutomationElement sender) =>
            _owner.FocusIsPassword = IsPassword(sender);
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
