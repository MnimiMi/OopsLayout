namespace OopsLayout.Windows;

internal static class Program
{
    // STA is required on the UI thread: shell COM dialogs (OpenFileDialog in
    // Apps… → Browse) hang on an MTA thread. Top-level statements can't carry
    // this attribute — the main thread then defaults to MTA — hence a real Main.
    [STAThread]
    private static void Main()
    {
        // Single instance: if another copy already holds the mutex, just bow out
        // quietly. The running copy is already in the tray, so a modal "already running"
        // dialog only gets in the way (and blocks when launched from a script).
        var mutex = new Mutex(true, "OopsLayout_SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        ActivityLog.Write($"App start, version {Application.ProductVersion}");

        // Record anything that slips through so a crash leaves a trace instead of
        // vanishing silently. Log path: %TEMP%\oopslayout-error.log
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write(e.ExceptionObject as Exception);

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
