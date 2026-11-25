using System.Diagnostics;

namespace OopsLayout.Windows;

/// <summary>
/// Settings dialog for the app list: OopsLayout neither buffers nor injects
/// anything while one of these apps is in front. One process name per line;
/// add from a picker of apps with a window open right now, or browse to any
/// .exe (e.g. a game that isn't running). Saved when the dialog closes.
/// Laid out with a TableLayoutPanel so text wraps and nothing clips at any
/// display scale.
/// </summary>
internal sealed class AppsForm : Form
{
    private readonly TextBox _text;
    private readonly ComboBox _running;

    public AppsForm()
    {
        Text = "OopsLayout — Apps";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi; // sizes below are at 100% scale
        ClientSize = new Size(420, 500);
        Padding = new Padding(10);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var info = WrappingLabel(
            "Apps where OopsLayout does nothing (no switching, no typing), one per line. " +
            "Pick a running app, or Browse… to its .exe (e.g. a game that isn't running). " +
            "Code editors and terminals are always skipped.",
            SystemColors.GrayText);

        var warning = WrappingLabel(
            "The keyboard hook stays installed. For games whose anti-cheat reacts to " +
            "the hook itself, pause or exit OopsLayout before starting them.",
            Color.DarkRed);

        _running = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        _running.Items.AddRange(RunningWindowApps().Cast<object>().ToArray());
        if (_running.Items.Count > 0) _running.SelectedIndex = 0;

        var add = new Button { Text = "Add", AutoSize = true };
        add.Click += (_, _) => { if (_running.SelectedItem is string n) AddName(n); };

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => BrowseExe();

        _text = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 10f),
            Text = string.Join(Environment.NewLine, UserApps.All)
        };

        var save = new Button
        {
            Text = "Save",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK // closes the form → OnFormClosing saves
        };

        table.Controls.Add(info, 0, 0);
        table.SetColumnSpan(info, 3);
        table.Controls.Add(warning, 0, 1);
        table.SetColumnSpan(warning, 3);
        table.Controls.Add(_running, 0, 2);
        table.Controls.Add(add, 1, 2);
        table.Controls.Add(browse, 2, 2);
        table.Controls.Add(_text, 0, 3);
        table.SetColumnSpan(_text, 3);
        table.Controls.Add(save, 0, 4);
        table.SetColumnSpan(save, 3);

        AcceptButton = save;
        Controls.Add(table);
    }

    private static Label WrappingLabel(string text, Color color) => new()
    {
        Text = text,
        ForeColor = color,
        AutoSize = true, // with Left|Right anchoring the table hands it a width → wraps
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(3, 0, 3, 8),
    };

    private void BrowseExe()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose the app's .exe",
            Filter = "Programs (*.exe)|*.exe",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            AddName(Path.GetFileNameWithoutExtension(dlg.FileName));
    }

    private void AddName(string name)
    {
        var lines = _text.Text.Split('\n').Select(UserApps.Normalize);
        if (lines.Contains(UserApps.Normalize(name))) return;
        if (_text.Text.Length > 0 && !_text.Text.EndsWith('\n'))
            _text.AppendText(Environment.NewLine);
        _text.AppendText(name);
    }

    /// <summary>
    /// Process names of apps with a visible top-level window. Uses only window
    /// enumeration and the system process list — no handle is opened to any
    /// process (a running game's anti-cheat may watch for that).
    /// </summary>
    private static List<string> RunningWindowApps()
    {
        var pids = new HashSet<uint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) &&
                NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == IntPtr.Zero &&
                NativeMethods.GetWindowTextLength(hwnd) > 0)
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                pids.Add(pid);
            }
            return true;
        }, IntPtr.Zero);

        var own = Environment.ProcessId;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id != own && pids.Contains((uint)p.Id) && !WindowsKeyboardBackend.IsExcludedName(p.ProcessName))
                    names.Add(p.ProcessName);
            }
        }
        return names.ToList();
    }

    private void Persist() => UserApps.SetApps(_text.Text.Split('\n'));

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Persist(); // also save if closed via the window's X
        base.OnFormClosing(e);
    }
}
