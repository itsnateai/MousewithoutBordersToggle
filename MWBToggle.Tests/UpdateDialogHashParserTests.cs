using Microsoft.VisualStudio.TestTools.UnitTesting;
using MWBToggle;

namespace MWBToggleTests;

[TestClass]
public class UpdateDialogHashParserTests
{
    private const string Filename = "MWBToggle.exe";
    private const string Hash = "abc1234567890def1234567890abcdef1234567890abcdef1234567890abcdef";

    // ─── Format coverage ──────────────────────────────────────────

    [TestMethod]
    public void GnuCoreutils_TwoSpaces_Parses()
    {
        var body = $"{Hash}  {Filename}\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void GnuCoreutils_BinaryStarPrefix_Parses()
    {
        // sha256sum --binary on Windows runners emits this form.
        var body = $"{Hash} *{Filename}\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void TabSeparator_Parses()
    {
        var body = $"{Hash}\t{Filename}\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void BsdTag_Parses()
    {
        var body = $"SHA256 ({Filename}) = {Hash}\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    // ─── Line-ending edge cases (Windows runners default to CRLF) ──

    [TestMethod]
    public void Crlf_LineEndings_Parses()
    {
        var body = $"{Hash}  {Filename}\r\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void NoTrailingNewline_Parses()
    {
        var body = $"{Hash}  {Filename}";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void BlankLines_Skipped()
    {
        var body = $"\n\n{Hash}  {Filename}\n\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    // ─── Multi-entry / wrong-file ────────────────────────────────

    [TestMethod]
    public void MultiEntry_FindsCorrectFile()
    {
        var body =
            $"deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef  OtherTool.exe\n" +
            $"{Hash}  {Filename}\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void MultiEntry_FirstEntryWins_WhenSameFile()
    {
        // Defensive: if a malicious SHA256SUMS includes two entries for the same file,
        // we lock to the first hit so an attacker can't append-and-override.
        var body =
            $"{Hash}  {Filename}\n" +
            $"deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef  {Filename}\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void OnlyOtherFile_ReturnsNull()
    {
        var body =
            $"deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef  Sibling.exe\n" +
            $"feedfacefeedfacefeedfacefeedfacefeedfacefeedfacefeedfacefeedface  OtherTool.exe\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    // ─── Empty / null inputs ─────────────────────────────────────

    [TestMethod]
    public void EmptyBody_ReturnsNull() => Assert.IsNull(UpdateDialog.ParseHashFor("", Filename));

    [TestMethod]
    public void NullBody_ReturnsNull() => Assert.IsNull(UpdateDialog.ParseHashFor(null, Filename));

    [TestMethod]
    public void WhitespaceOnly_ReturnsNull() => Assert.IsNull(UpdateDialog.ParseHashFor("   \n\t\r\n", Filename));

    // ─── Filename matching ───────────────────────────────────────

    [TestMethod]
    public void Filename_CaseInsensitive()
    {
        var body = $"{Hash}  mwbtoggle.exe\n";
        Assert.AreEqual(Hash, UpdateDialog.ParseHashFor(body, "MWBToggle.exe"));
    }

    [TestMethod]
    public void Filename_PrefixCollisionDoesNotMatch()
    {
        // "MWBToggle.exe.old" should NOT match "MWBToggle.exe".
        var body = $"{Hash}  MWBToggle.exe.old\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void Filename_SuffixCollisionDoesNotMatch()
    {
        // "OldMWBToggle.exe" should NOT match "MWBToggle.exe".
        var body = $"{Hash}  OldMWBToggle.exe\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    // ─── Hash validation (length + hex) ──────────────────────────

    [TestMethod]
    public void Hash_NonHexChars_ReturnsNull()
    {
        // 64 chars but contains non-hex `xyz`. Must not be returned as the trust hash.
        var body = $"xyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyzx  {Filename}\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void Hash_TooShort_ReturnsNull()
    {
        var body = $"deadbeef  {Filename}\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void Hash_TooLong_ReturnsNull()
    {
        // 65 chars — sneakily wrong length that survived a stale digest spec.
        var body = $"{Hash}a  {Filename}\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void BsdTag_EmptyAfterEquals_ReturnsNull()
    {
        var body = $"SHA256 ({Filename}) = \n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, Filename));
    }

    [TestMethod]
    public void NullFilename_ReturnsNull()
    {
        // Defense against a null-filename programming error reaching the parser.
        var body = $"{Hash}  {Filename}\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, null!));
    }

    [TestMethod]
    public void EmptyFilename_ReturnsNull()
    {
        var body = $"{Hash}  {Filename}\n";
        Assert.IsNull(UpdateDialog.ParseHashFor(body, ""));
    }
}
