using StandDesk.Core;
namespace StandDesk.Tests;
public sealed class HotkeyTests
{
    [Fact] public void ParsesProposedStandShortcut() => Assert.Equal(new HotkeySpec(7, 0x26), HotkeySpec.Parse("Ctrl+Alt+Shift+Up"));
    [Theory] [InlineData("F12")] [InlineData("Ctrl+Alt+F12")] [InlineData("Ctrl+Alt")] [InlineData("Ctrl+Alt+Ctrl+S")]
    public void RejectsInvalidOrReservedKeys(string text) => Assert.Throws<ArgumentException>(() => HotkeySpec.Parse(text));
}
