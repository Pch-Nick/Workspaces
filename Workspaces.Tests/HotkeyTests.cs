using System.Windows.Input;
using Xunit;

namespace Workspaces.Tests
{
    public class HotkeyTests
    {
        [Fact]
        public void Parse_CtrlAlt1()
        {
            uint mod, vk;
            Assert.True(HotkeyManager.Parse("Ctrl+Alt+1", out mod, out vk));
            Assert.Equal(3u, mod);      // Ctrl(2) | Alt(1)
            Assert.Equal(0x31u, vk);
        }

        [Fact]
        public void Format_ThenParse_RoundTrips()
        {
            string s = HotkeyManager.Format(ModifierKeys.Control | ModifierKeys.Shift, Key.F5);
            Assert.Equal("Ctrl+Shift+F5", s);
            uint mod, vk;
            Assert.True(HotkeyManager.Parse(s, out mod, out vk));
            Assert.Equal(0x74u, vk);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Ctrl+")]
        [InlineData("Ctrl+Alt")]
        [InlineData("Ctrl+Banana")]
        public void Parse_Invalid_ReturnsFalse(string s)
        {
            uint mod, vk;
            Assert.False(HotkeyManager.Parse(s, out mod, out vk));
        }
    }
}
