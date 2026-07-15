using System.Drawing.Imaging;
using OopsLayout.Core;

namespace OopsLayout.Windows;

/// <summary>
/// WinForms tray application. No main window — lives entirely in the system tray.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly WindowsKeyboardBackend _backend;
    private readonly SwitcherEngine _engine;
    private readonly Icon _activeIcon;     // full-colour: switcher on
    private readonly Icon _inactiveIcon;   // greyed: switcher paused / dormant
    private readonly System.Windows.Forms.Timer _statusTimer;
    private IntPtr _inactiveHIcon;         // backing HICON to free on exit
    private bool _enabled = true;
    private bool? _lastActive;             // last icon state, to avoid needless updates

    public TrayApp()
    {
        UserExceptions.Load(); // feed user keep-words into Core before we start
        Settings.Load();       // restore the chosen Cyrillic target (RU / UK)

        _backend = new WindowsKeyboardBackend();
        _engine = new SwitcherEngine(_backend);

        _activeIcon   = LoadIcon();
        _inactiveIcon = MakeGrayscale(_activeIcon, out _inactiveHIcon);

        _trayIcon = new NotifyIcon
        {
            Text    = "OopsLayout (active)",
            Icon    = _activeIcon,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ToggleEnabled();

        // Grey the icon while the switcher is dormant (paused, or in a code
        // editor / terminal). Foreground app can change without any keypress, so
        // we poll — cheap, since the backend caches per foreground window.
        _statusTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        _engine.Start();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Enabled", null, (_, _) => ToggleEnabled())
        {
            Checked = true,
            CheckOnClick = true
        };

        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildTargetMenu());
        menu.Items.Add("Exceptions…", null, (_, _) => ShowExceptions());
        menu.Items.Add("About", null, (_, _) =>
            MessageBox.Show(
                "OopsLayout\nAutomatic keyboard layout switcher\n\ngithub.com/MnimiMi/OopsLayout",
                "OopsLayout",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        return menu;
    }

    private void ToggleEnabled()
    {
        _enabled = !_enabled;
        _engine.Enabled = _enabled;

        // Keep the menu checkmark in sync.
        if (_trayIcon.ContextMenuStrip?.Items[0] is ToolStripMenuItem item)
            item.Checked = _enabled;

        RefreshStatus();
    }

    private static void ShowExceptions()
    {
        using var form = new ExceptionsForm();
        form.ShowDialog();
    }

    /// <summary>Submenu to pick which Cyrillic language the switcher targets.</summary>
    private static ToolStripMenuItem BuildTargetMenu()
    {
        var menu = new ToolStripMenuItem("Cyrillic target");
        var ru = new ToolStripMenuItem("Russian");
        var uk = new ToolStripMenuItem("Ukrainian");

        void Select(Cyrillic t)
        {
            Settings.SaveTarget(t);
            ru.Checked = t == Cyrillic.Russian;
            uk.Checked = t == Cyrillic.Ukrainian;
        }

        ru.Click += (_, _) => Select(Cyrillic.Russian);
        uk.Click += (_, _) => Select(Cyrillic.Ukrainian);
        ru.Checked = WordBuffer.Target == Cyrillic.Russian;
        uk.Checked = WordBuffer.Target == Cyrillic.Ukrainian;

        menu.DropDownItems.Add(ru);
        menu.DropDownItems.Add(uk);
        return menu;
    }

    /// <summary>Greys the icon and updates the tooltip to match the live state.</summary>
    private void RefreshStatus()
    {
        bool excluded = _backend.IsForegroundExcluded();
        bool active   = _enabled && !excluded;

        if (_lastActive != active)
        {
            _trayIcon.Icon = active ? _activeIcon : _inactiveIcon;
            _lastActive = active;
        }

        _trayIcon.Text = !_enabled    ? "OopsLayout (paused)"
                       : excluded     ? "OopsLayout (off in this app)"
                                      : "OopsLayout (active)";
    }

    private static Icon LoadIcon()
    {
        // The icon is an embedded resource, so it works from a single-file exe.
        var asm  = System.Reflection.Assembly.GetExecutingAssembly();
        var name = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is not null) return new Icon(stream);
        }
        return SystemIcons.Application;
    }

    /// <summary>Builds a desaturated (grey) copy of <paramref name="source"/>.</summary>
    private static Icon MakeGrayscale(Icon source, out IntPtr hIcon)
    {
        using var src  = source.ToBitmap();
        using var gray = new Bitmap(src.Width, src.Height);

        // Standard luminance weights — turns colour into perceptual grey.
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.30f, 0.30f, 0.30f, 0f, 0f },
            new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
            new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
            new[] { 0f,    0f,    0f,    1f, 0f },
            new[] { 0f,    0f,    0f,    0f, 1f },
        });

        using (var g = Graphics.FromImage(gray))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(matrix);
            g.DrawImage(src,
                new Rectangle(0, 0, src.Width, src.Height),
                0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }

        hIcon = gray.GetHicon();
        return Icon.FromHandle(hIcon); // handle freed via DestroyIcon in DisposeIcons
    }

    private void DisposeIcons()
    {
        _inactiveIcon.Dispose();
        if (_inactiveHIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_inactiveHIcon);
            _inactiveHIcon = IntPtr.Zero;
        }
        _activeIcon.Dispose();
    }

    private void ExitApp()
    {
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _engine.Stop();
        _engine.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        DisposeIcons();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Dispose();
            _engine.Dispose();
            _trayIcon.Dispose();
            DisposeIcons();
        }
        base.Dispose(disposing);
    }
}
