namespace OopsLayout.Windows;

/// <summary>
/// Settings dialog for managing user exceptions: one word per line in a text
/// box. Saved on "Save" or when the dialog closes; words are routed to the
/// RU/EN keep-list by script. Mirrors ExceptionsWindow.swift.
/// </summary>
internal sealed class ExceptionsForm : Form
{
    private readonly TextBox _text;

    public ExceptionsForm()
    {
        Text = "OopsLayout — Exceptions";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 420);

        var label = new Label
        {
            Text = "Words OopsLayout should never auto-switch — one per line.\r\n" +
                   "Add anything it keeps changing on you (Cyrillic and Latin both work).",
            Location = new Point(12, 12),
            Size = new Size(356, 40),
            ForeColor = SystemColors.GrayText
        };

        _text = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Location = new Point(12, 56),
            Size = new Size(356, 312),
            Font = new Font(FontFamily.GenericMonospace, 10f),
            Text = string.Join(Environment.NewLine, UserExceptions.All)
        };

        var save = new Button
        {
            Text = "Save",
            Location = new Point(280, 380),
            Size = new Size(88, 30),
            DialogResult = DialogResult.OK
        };
        save.Click += (_, _) => Persist();

        AcceptButton = save;
        Controls.Add(label);
        Controls.Add(_text);
        Controls.Add(save);
    }

    private void Persist() =>
        UserExceptions.SetWords(_text.Text.Split('\n'));

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Persist(); // also save if closed via the window's X
        base.OnFormClosing(e);
    }
}
