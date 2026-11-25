using System.Reflection;
using Xunit;

namespace OopsLayout.Windows.Tests;

public class ProgramTests
{
    // Without [STAThread] the UI thread is MTA and Apps… → Browse (a shell COM
    // file dialog) hangs. Top-level statements silently lose the attribute.
    [Fact]
    public void Main_RunsOnStaThread()
    {
        var main = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(main);
        Assert.NotNull(main!.GetCustomAttribute<STAThreadAttribute>());
    }
}
