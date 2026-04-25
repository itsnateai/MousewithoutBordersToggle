namespace MWBToggle.Tests;

[TestClass]
public class UpdateDialogDownloadCapTests
{
    // The legitimate self-contained release is ~150 MB. The cap at 200 MB
    // (~33% headroom) is the "compromised-CDN-can't-fill-the-user's-disk"
    // gate. These tests pin the boundary so a future cleanup can't silently
    // bump the cap to int.MaxValue or remove it.

    [TestMethod]
    public void Cap_IsExactly200MB()
    {
        // 200 * 1024 * 1024 = 209715200. Pinned because changing this without
        // updating the comment in DownloadFileAsync will silently weaken the
        // network-attacker defense.
        Assert.AreEqual(209_715_200L, UpdateDialog.MaxDownloadBytes);
    }

    [TestMethod]
    public void Zero_Allowed()
    {
        // Caller treats Content-Length:0/missing as "stream and count" — the
        // size-check gate must let zero through so chunked-transfer downloads
        // reach the mid-stream check inside the read loop.
        Assert.IsTrue(UpdateDialog.IsAllowedDownloadSize(0));
    }

    [TestMethod]
    public void NegativeContentLength_Allowed()
    {
        // Negative Content-Length is non-canonical but defensively allowed
        // through the header gate (mid-stream check still runs).
        Assert.IsTrue(UpdateDialog.IsAllowedDownloadSize(-1));
    }

    [TestMethod]
    public void TypicalReleaseSize_Allowed()
    {
        // ~150 MB self-contained build.
        Assert.IsTrue(UpdateDialog.IsAllowedDownloadSize(150L * 1024 * 1024));
    }

    [TestMethod]
    public void AtCap_Allowed()
    {
        // 200 MB exact — boundary is inclusive.
        Assert.IsTrue(UpdateDialog.IsAllowedDownloadSize(UpdateDialog.MaxDownloadBytes));
    }

    [TestMethod]
    public void OneByteOverCap_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedDownloadSize(UpdateDialog.MaxDownloadBytes + 1));
    }

    [TestMethod]
    public void Hostile50GB_Rejected()
    {
        // The agent's exact attacker scenario from the audit.
        Assert.IsFalse(UpdateDialog.IsAllowedDownloadSize(50L * 1024 * 1024 * 1024));
    }

    [TestMethod]
    public void LongMaxValue_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedDownloadSize(long.MaxValue));
    }
}
