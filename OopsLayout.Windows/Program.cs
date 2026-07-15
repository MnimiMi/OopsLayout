using OopsLayout.Windows;

// Single instance check
var mutex = new System.Threading.Mutex(true, "OopsLayout_SingleInstance", out bool createdNew);
if (!createdNew)
{
    MessageBox.Show("OopsLayout is already running.", "OopsLayout",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

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
