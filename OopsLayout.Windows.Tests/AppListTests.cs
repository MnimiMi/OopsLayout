using Xunit;

namespace OopsLayout.Windows.Tests;

public class AppListTests
{
    [Theory]
    [InlineData("Wow", "wow")]
    [InlineData("Wow.exe", "wow")]
    [InlineData("  WOW.EXE \r", "wow")]
    [InlineData("\"C:\\Program Files (x86)\\World of Warcraft\\_retail_\\Wow.exe\"", "wow")]
    [InlineData("PathOfExile_x64", "pathofexile_x64")]
    [InlineData("   ", "")]
    [InlineData("\"\"", "")]
    public void Normalize_GivesLowercaseProcessName(string raw, string expected) =>
        Assert.Equal(expected, UserApps.Normalize(raw));

    [Fact]
    public void Parse_DropsBlanksAndDuplicates()
    {
        // As it comes out of the multiline TextBox: "\r\n"-separated, split on '\n'.
        var set = UserApps.Parse("Wow\r\n\r\nwow.exe\r\nDiscord\r\n".Split('\n'));
        Assert.Equal(new[] { "discord", "wow" }, set.Order());
    }

    [Fact]
    public void IsExcludedName_UsesBuiltInAndUserLists_CaseInsensitive()
    {
        var saved = WindowsKeyboardBackend.UserExcludedProcesses;
        try
        {
            WindowsKeyboardBackend.UserExcludedProcesses = UserApps.Parse(new[] { "Wow.exe" });

            Assert.True(WindowsKeyboardBackend.IsExcludedName("Wow"));      // user list
            Assert.True(WindowsKeyboardBackend.IsExcludedName("WindowsTerminal")); // built-in
            Assert.False(WindowsKeyboardBackend.IsExcludedName("notepad"));

            WindowsKeyboardBackend.UserExcludedProcesses = new();
            Assert.False(WindowsKeyboardBackend.IsExcludedName("Wow"));     // removed again
        }
        finally
        {
            WindowsKeyboardBackend.UserExcludedProcesses = saved;
        }
    }
}
