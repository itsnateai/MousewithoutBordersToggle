namespace MWBToggle.Tests;

[TestClass]
public class UpdateDialogAllowlistTests
{
    // Empty / null

    [TestMethod]
    public void NullOrEmpty_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowlisted(null));
        Assert.IsFalse(UpdateDialog.IsAllowlisted(""));
    }

    // HTTP scheme — must be HTTPS

    [TestMethod]
    public void HttpScheme_Rejected()
    {
        // Allowlist prefixes are all https://, so any http:// URL fails the StartsWith.
        Assert.IsFalse(UpdateDialog.IsAllowlisted(
            "http://github.com/itsnateai/MousewithoutBordersToggle/releases/download/v1/MWBToggle.exe"));
    }

    // github.com — owner-scoped to itsnateai

    [TestMethod]
    public void GitHubCom_ItsnateaiOwner_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowlisted(
            "https://github.com/itsnateai/MousewithoutBordersToggle/releases/download/v2/MWBToggle.exe"));
    }

    [TestMethod]
    public void GitHubCom_DifferentOwner_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowlisted(
            "https://github.com/evil/MousewithoutBordersToggle/releases/download/v1/MWBToggle.exe"));
    }

    // api.github.com — broad allowance, but redirects re-validated per-hop downstream

    [TestMethod]
    public void ApiGitHub_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowlisted(
            "https://api.github.com/repos/itsnateai/MousewithoutBordersToggle/releases/latest"));
    }

    // GitHub release-asset CDNs

    [TestMethod]
    public void ObjectsCdn_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowlisted(
            "https://objects.githubusercontent.com/anything-token-and-path"));
    }

    [TestMethod]
    public void ReleaseAssetsCdn_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowlisted(
            "https://release-assets.githubusercontent.com/anything-token-and-path"));
    }

    // Host-confusion attacks

    [TestMethod]
    public void HostConfusion_GitHubAsSubdomain_Rejected()
    {
        // The trailing slash in the prefix is what defeats this — `github.com.evil.example`
        // doesn't have `/` at position 18 where the prefix expects it.
        Assert.IsFalse(UpdateDialog.IsAllowlisted(
            "https://github.com.evil.example/itsnateai/MousewithoutBordersToggle/releases/download/v1/x.exe"));
    }

    [TestMethod]
    public void HostConfusion_LookalikeCdnSuffix_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowlisted(
            "https://objects.githubusercontent.com.evil.example/anything"));
    }

    [TestMethod]
    public void DifferentGitHubProperty_Rejected()
    {
        // raw.githubusercontent.com and itsnateai.github.io are GitHub-owned but not
        // on the allowlist — must fail.
        Assert.IsFalse(UpdateDialog.IsAllowlisted(
            "https://raw.githubusercontent.com/itsnateai/MousewithoutBordersToggle/main/CHANGELOG.md"));
        Assert.IsFalse(UpdateDialog.IsAllowlisted(
            "https://itsnateai.github.io/MousewithoutBordersToggle/index.html"));
    }

    // Case-insensitivity

    [TestMethod]
    public void HostCaseInsensitive_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowlisted(
            "https://GITHUB.COM/itsnateai/MousewithoutBordersToggle/releases/latest"));
    }
}
