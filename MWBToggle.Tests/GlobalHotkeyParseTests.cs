namespace MWBToggle.Tests;

[TestClass]
public class GlobalHotkeyParseTests
{
    // Modifier prefix decoding

    [TestMethod]
    public void ModifierPlusLetter_Parses()
    {
        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("#^+f", out uint mods, out uint vk));
        Assert.AreEqual((uint)'F', vk);
        // MOD_WIN (0x08) | MOD_CONTROL (0x02) | MOD_SHIFT (0x04) = 0x0E
        Assert.AreEqual(0x0Eu, mods);
    }

    [TestMethod]
    public void NoModifier_LetterAlone_Parses()
    {
        // Bare letter is parseable here — the gate against bare hotkeys lives in
        // the registration path, not the parser.
        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("a", out uint mods, out uint vk));
        Assert.AreEqual((uint)'A', vk);
        Assert.AreEqual(0u, mods);
    }

    // Lowercase / uppercase handling

    [TestMethod]
    public void LowercaseLetter_NormalizesToVk()
    {
        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("#a", out _, out uint vk));
        Assert.AreEqual((uint)'A', vk);

        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("#A", out _, out uint vk2));
        Assert.AreEqual((uint)'A', vk2);
    }

    // Modifier-only — no key

    [TestMethod]
    public void ModifierWithoutKey_Rejected()
    {
        Assert.IsFalse(GlobalHotkey.ParseAhkHotkey("#", out _, out _));
        Assert.IsFalse(GlobalHotkey.ParseAhkHotkey("#^!+", out _, out _));
    }

    // Unknown key name

    [TestMethod]
    public void UnknownKeyName_Rejected()
    {
        Assert.IsFalse(GlobalHotkey.ParseAhkHotkey("#totally-not-a-key", out _, out _));
    }

    // Special-key names

    [TestMethod]
    public void SpaceKey_Parses()
    {
        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("#space", out _, out uint vk));
        Assert.AreEqual((uint)System.Windows.Forms.Keys.Space, vk);
    }

    [TestMethod]
    public void FunctionKeys_AllParse()
    {
        for (int n = 1; n <= 12; n++)
        {
            string hk = $"#F{n}";
            Assert.IsTrue(GlobalHotkey.ParseAhkHotkey(hk, out _, out uint vk),
                $"Expected '{hk}' to parse");
            Assert.IsTrue(vk >= (uint)System.Windows.Forms.Keys.F1 &&
                          vk <= (uint)System.Windows.Forms.Keys.F12,
                $"Expected F-key VK for '{hk}', got 0x{vk:X}");
        }
    }

    [TestMethod]
    public void ArrowKeys_Parse()
    {
        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("#up", out _, out uint up));
        Assert.AreEqual((uint)System.Windows.Forms.Keys.Up, up);

        Assert.IsTrue(GlobalHotkey.ParseAhkHotkey("#down", out _, out uint down));
        Assert.AreEqual((uint)System.Windows.Forms.Keys.Down, down);
    }

    // Empty input

    [TestMethod]
    public void Empty_Rejected()
    {
        Assert.IsFalse(GlobalHotkey.ParseAhkHotkey("", out _, out _));
    }
}
