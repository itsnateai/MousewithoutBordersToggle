using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace MWBToggle;

/// <summary>
/// Manual update checker — no telemetry, no background requests.
/// User clicks the button, we check GitHub once, download if needed.
/// </summary>
internal sealed class UpdateDialog : Form
{
    private static readonly HttpClient _http = CreateHttpClient();

    private readonly Label _lblStatus;
    private readonly Label _lblDetail;
    private readonly Panel _progressOuter;
    private readonly Panel _progressFill;
    private readonly Button _btnAction;
    private readonly Button _btnCancel;
    private CancellationTokenSource? _cts;

    private string? _remoteVersion;
    private string? _downloadUrl;
    private string? _hashFileUrl;

    private readonly Font _boldFont;
    private readonly Font _italicFont;

    private readonly System.Windows.Forms.Timer _marqueeTimer;
    private int _marqueePos;
    private bool _marqueeForward = true;

    private const string AppName = "MWBToggle";
    private const string GitHubRepo = "itsnateai/MousewithoutBordersToggle";

    // First version tag that emits a SHA256SUMS release asset (commit 6f7f1db).
    // For any remote version >= this, a missing SHA256SUMS is treated as a
    // supply-chain error and the update is aborted. Older releases are
    // grandfathered so existing users on pre-2.5.0 can still self-update.
    private static readonly Version FIRST_HASH_EMITTING_VERSION = new Version(2, 5, 0);

    public UpdateDialog()
    {
        Text = $"{AppName} — Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(420, 180);

        _boldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _italicFont = new Font("Segoe UI", 7.5f, FontStyle.Italic);

        _lblStatus = new Label
        {
            Text = "Checking GitHub for new version...",
            Location = new Point(20, 20),
            Size = new Size(370, 24),
            Font = _boldFont,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_lblStatus);

        _lblDetail = new Label
        {
            Text = "",
            Location = new Point(20, 48),
            Size = new Size(370, 20),
            ForeColor = SystemColors.GrayText,
            Font = _italicFont,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_lblDetail);

        _progressOuter = new Panel
        {
            Location = new Point(30, 80),
            Size = new Size(350, 18),
            BackColor = SystemColors.ControlDark,
            BorderStyle = BorderStyle.None
        };
        _progressFill = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(0, 18),
            BackColor = Color.FromArgb(76, 175, 80)
        };
        _progressOuter.Controls.Add(_progressFill);
        Controls.Add(_progressOuter);

        _btnAction = new Button
        {
            Text = "Upgrade Now",
            Location = new Point(155, 112),
            Size = new Size(110, 32),
            Visible = false,
            AccessibleName = "Download and install the latest version"
        };
        _btnAction.Click += OnActionClick;
        Controls.Add(_btnAction);

        _btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(295, 112),
            Size = new Size(80, 32),
            AccessibleName = "Cancel update check"
        };
        _btnCancel.Click += (_, _) =>
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { /* rapid double-click race with Dispose */ }
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(_btnCancel);

        // Enter triggers the currently-primary action (Upgrade Now once visible, nothing
        // before that), Esc always cancels and disposes the in-flight HTTP request.
        AcceptButton = _btnAction;
        CancelButton = _btnCancel;

        _marqueeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _marqueeTimer.Tick += (_, _) =>
        {
            const int step = 4, barW = 80;
            if (_marqueeForward) _marqueePos += step; else _marqueePos -= step;
            if (_marqueePos + barW >= _progressOuter.Width) _marqueeForward = false;
            if (_marqueePos <= 0) _marqueeForward = true;
            _progressFill.Location = new Point(_marqueePos, 0);
            _progressFill.Size = new Size(barW, 18);
        };

        Shown += async (_, _) => await CheckForUpdateAsync();
    }

    private static HttpClient CreateHttpClient()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        // Disable auto-redirect: the default handler follows redirects WITHOUT re-checking
        // each hop against the allowlist, which would let an allowlisted origin (e.g. the
        // GitHub API) hand off to an attacker-controlled CDN via a crafted 3xx. We follow
        // manually below, validating each hop.
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppName, version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // URL allowlist for every HTTP request (including post-redirect). The prior code
    // only validated the first URL before `HttpClient` silently followed redirects.
    private static readonly string[] UrlAllowlist =
    {
        "https://api.github.com/",
        "https://github.com/itsnateai/",
        // GitHub release-asset CDN. Both hosts are seen in the wild — GitHub
        // rolled `release-assets.githubusercontent.com` alongside the legacy
        // `objects.githubusercontent.com` and either can be the redirect target
        // for a `github.com/.../releases/download/...` GET. Keeping both keeps
        // self-update working through the rollout.
        "https://objects.githubusercontent.com/",
        "https://release-assets.githubusercontent.com/",
    };

    internal static bool IsAllowlisted(string? url) =>
        !string.IsNullOrEmpty(url) &&
        UrlAllowlist.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Issue a GET and follow up to 5 redirects manually. Every hop's URL — including
    /// the initial one — is validated against <see cref="UrlAllowlist"/> before the
    /// request is sent. Throws if any hop lands off-list or if the redirect chain
    /// exceeds the hop limit.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAllowlistedAsync(
        string url, HttpCompletionOption completion, CancellationToken ct)
    {
        const int maxHops = 5;
        for (int hop = 0; hop < maxHops; hop++)
        {
            if (!IsAllowlisted(url))
                throw new HttpRequestException($"URL not in allowlist: {url}");

            var response = await _http.GetAsync(url, completion, ct);

            int status = (int)response.StatusCode;
            if (status >= 300 && status < 400 && response.Headers.Location != null)
            {
                var next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location.ToString()
                    : new Uri(new Uri(url), response.Headers.Location).ToString();
                response.Dispose();
                url = next;
                continue;
            }

            return response;
        }
        throw new HttpRequestException($"Too many redirects (>{maxHops}) starting from initial URL.");
    }

    // ─── Check GitHub ───────────────────────────────────────────

    private async Task CheckForUpdateAsync()
    {
        _cts = new CancellationTokenSource();

        // Winget-managed installs should use `winget upgrade` instead of self-update
        if (IsWingetManaged())
        {
            _marqueeTimer.Stop();
            _progressOuter.Visible = false;
            _lblStatus.Text = "This installation is managed by winget.";
            _lblDetail.Text = "Use: winget upgrade itsnateai.MWBToggle";
            _btnAction.Visible = false;
            _btnCancel.Text = "OK";
            _btnCancel.Size = new Size(64, 26);
            _btnCancel.Location = new Point((ClientSize.Width - 64) / 2, 112);
            return;
        }

        _marqueeTimer.Start();

        try
        {
            var response = await SendAllowlistedAsync(
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest",
                HttpCompletionOption.ResponseContentRead,
                _cts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var vals)
                    ? vals.FirstOrDefault() : null;
                ShowError(remaining == "0"
                    ? "GitHub API rate limit reached." : "GitHub API access denied (403).",
                    remaining == "0" ? "Try again in a few minutes." : "Check your network connection.");
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ShowError("No releases found on GitHub.", "The repository may not have any published releases.");
                return;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(_cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _remoteVersion = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals("MWBToggle.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        _downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    }
                    if (name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        _hashFileUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    }
                }
            }

            if (string.IsNullOrEmpty(_downloadUrl))
            {
                ShowError("No update package found in the latest release.", "The release may be incomplete.");
                return;
            }

            ShowVersionComparison();
        }
        catch (TaskCanceledException)
        {
            if (_cts?.IsCancellationRequested != true)
                ShowError("Request timed out.", "Check your internet connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            ShowError("Could not reach GitHub.", ex.Message);
        }
        catch (JsonException)
        {
            ShowError("Unexpected response from GitHub.", "The API response format may have changed.");
        }
        catch (Exception ex)
        {
            ShowError("Update check failed.", ex.Message);
        }
    }

    // ─── Compare Versions ───────────────────────────────────────

    private void ShowVersionComparison()
    {
        _marqueeTimer.Stop();
        _progressFill.Size = new Size(0, 18);
        _progressFill.Location = new Point(0, 0);

        var localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var isNewer = Version.TryParse(_remoteVersion, out var remote)
                   && Version.TryParse(localVersion, out var local)
                   && remote > local;

        _lblDetail.Text = $"Current: {localVersion}  →  GitHub: {_remoteVersion}";
        _progressOuter.Visible = false;

        if (isNewer)
        {
            _lblStatus.Text = "A new version is available!";
            _btnAction.Text = "Upgrade Now";
            _btnAction.Visible = true;
            _btnCancel.Text = "Cancel";
        }
        else
        {
            _lblStatus.Text = "You're on the latest version!";
            _btnAction.Visible = false;
            _btnCancel.Text = "OK";
            // Shrink the OK button for this acknowledgment-only state — a large
            // Cancel-sized button makes the simple "you're up to date" popup feel
            // heavier than it needs to be. Re-center inside the 420-wide dialog.
            _btnCancel.Size = new Size(64, 26);
            _btnCancel.Location = new Point((ClientSize.Width - 64) / 2, 112);
        }
    }

    // ─── Download & Apply ───────────────────────────────────────

    private async void OnActionClick(object? sender, EventArgs e)
    {
        _btnAction.Enabled = false;
        _btnCancel.Text = "Cancel";
        _progressOuter.Visible = true;
        _progressFill.Location = new Point(0, 0);
        _lblStatus.Text = $"Downloading {AppName} {_remoteVersion}...";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path.");
        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";

        try
        {
            // Validate download URL origin before fetching
            if (!_downloadUrl!.StartsWith("https://github.com/itsnateai/", StringComparison.OrdinalIgnoreCase) &&
                !_downloadUrl!.StartsWith("https://objects.githubusercontent.com/", StringComparison.OrdinalIgnoreCase) &&
                !_downloadUrl!.StartsWith("https://release-assets.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("Update failed: download URL is not from the expected source.", _downloadUrl!);
                return;
            }

            if (!await DownloadFileAsync(_downloadUrl!, newPath))
                return;

            // Verify SHA256 hash if the release includes a SHA256SUMS file
            if (!string.IsNullOrEmpty(_hashFileUrl))
            {
                // Tighten the hash-URL origin check to match _downloadUrl at line ~341.
                // The general UrlAllowlist would also let through api.github.com paths,
                // but a release asset must come from github.com/itsnateai/… or the CDN.
                if (!_hashFileUrl.StartsWith("https://github.com/itsnateai/", StringComparison.OrdinalIgnoreCase) &&
                    !_hashFileUrl.StartsWith("https://objects.githubusercontent.com/", StringComparison.OrdinalIgnoreCase) &&
                    !_hashFileUrl.StartsWith("https://release-assets.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
                {
                    ShowError("Update failed: hash URL is not from the expected source.", _hashFileUrl);
                    return;
                }

                _lblStatus.Text = "Verifying integrity...";
                try
                {
                    using var hashResponse = await SendAllowlistedAsync(
                        _hashFileUrl, HttpCompletionOption.ResponseContentRead, _cts!.Token);
                    hashResponse.EnsureSuccessStatusCode();
                    var hashContent = await hashResponse.Content.ReadAsStringAsync(_cts.Token);
                    string? expectedHash = null;
                    foreach (var line in hashContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Format: "hexhash  filename" or "hexhash *filename"
                        var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 &&
                            parts[1].Trim().TrimStart('*').Equals("MWBToggle.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            expectedHash = parts[0].Trim();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(expectedHash))
                    {
                        var actualHash = ComputeFileHash(newPath);
                        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            TryDelete(newPath);
                            ShowError("Hash verification failed.",
                                "The downloaded file doesn't match the expected SHA256 checksum.");
                            return;
                        }
                    }
                    else
                    {
                        TryDelete(newPath);
                        ShowError("Hash verification failed.",
                            "SHA256SUMS file found but contains no entry for MWBToggle.exe.");
                        return;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // SHA256SUMS fetch failed — fail closed. The hash is the primary integrity
                    // control (the exe is not Authenticode-signed), so we never ship an update
                    // we couldn't verify.
                    Logger.Warn($"SHA256SUMS fetch failed: {ex.Message}");
                    TryDelete(newPath);
                    ShowError("Hash verification failed.",
                        "Could not fetch SHA256SUMS. Try again, or run `winget upgrade`.");
                    return;
                }
            }
            else
            {
                // Version-gated fail-closed: if the remote release is >= FIRST_HASH_EMITTING_VERSION
                // and no SHA256SUMS asset was found, abort rather than installing unverified.
                // Grandfathered older releases (<v2.5.0) keep the skip-with-log behavior so users
                // upgrading from very old builds can still reach a hash-emitting version safely.
                bool isGrandfathered = Version.TryParse(_remoteVersion, out var remoteVer)
                                    && remoteVer < FIRST_HASH_EMITTING_VERSION;
                if (isGrandfathered)
                {
                    Logger.Warn($"Update verify SKIPPED (grandfathered release {_remoteVersion} < {FIRST_HASH_EMITTING_VERSION})");
                    // continue to apply
                }
                else
                {
                    TryDelete(newPath);
                    Logger.Error($"Update aborted: SHA256SUMS missing for release {_remoteVersion} (>= {FIRST_HASH_EMITTING_VERSION}). Fail-closed.");
                    ShowError("Update integrity file missing.",
                        $"SHA256SUMS was not found in release {_remoteVersion}. Aborting for security — try `winget upgrade` or download manually.");
                    return;
                }
            }

            _lblStatus.Text = "Applying update...";
            _progressOuter.Visible = false;

            TryDelete(oldPath);
            if (File.Exists(exePath))
                File.Move(exePath, oldPath);
            File.Move(newPath, exePath);

            // nosemgrep: gitlab.security_code_scan.SCS0001-1 -- exePath is Environment.ProcessPath; the replacement binary was SHA256-verified above against a SHA256SUMS asset from the github.com/itsnateai/ allowlisted origin (fail-closed on missing sums for >= v2.5.0)
            using var _ = Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = "--after-update",
                UseShellExecute = true
            });
            Application.Exit();
        }
        catch (IOException ex)
        {
            // Rollback: restore old exe if possible
            if (File.Exists(oldPath))
            {
                TryDelete(exePath);
                try { File.Move(oldPath, exePath); } catch { }
            }
            TryDelete(newPath);

            ShowError(
                ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                    ? "Cannot replace the executable." : "Failed to apply update.",
                ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                    ? "Your antivirus may be locking the file. Try again." : ex.Message);
        }
        catch (TaskCanceledException)
        {
            if (File.Exists(oldPath))
            {
                TryDelete(exePath);
                try { File.Move(oldPath, exePath); } catch { }
            }
            TryDelete(newPath);
            if (!IsDisposed) ShowVersionComparison();
        }
        catch (Exception ex)
        {
            if (File.Exists(oldPath))
            {
                TryDelete(exePath);
                try { File.Move(oldPath, exePath); } catch { }
            }
            TryDelete(newPath);
            if (!IsDisposed) ShowError("Update failed.", ex.Message);
        }
    }

    private async Task<bool> DownloadFileAsync(string url, string destPath)
    {
        using var response = await SendAllowlistedAsync(url, HttpCompletionOption.ResponseHeadersRead, _cts!.Token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var contentStream = await response.Content.ReadAsStreamAsync(_cts.Token);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, _cts.Token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
            downloaded += read;

            if (totalBytes > 0 && !IsDisposed) BeginInvoke(() =>
            {
                if (IsDisposed) return;
                int pct = (int)(downloaded * 100 / totalBytes);
                _progressFill.Size = new Size(
                    (int)(_progressOuter.Width * downloaded / totalBytes), 18);
                var dlMB = downloaded / (1024.0 * 1024.0);
                var totalMB = totalBytes / (1024.0 * 1024.0);
                _lblDetail.Text = totalMB < 1
                    ? $"{pct}% ({downloaded / 1024.0:F0} / {totalBytes / 1024.0:F0} KB)"
                    : $"{pct}% ({dlMB:F0} / {totalMB:F0} MB)";
            });
        }

        if (totalBytes > 0 && downloaded != totalBytes)
        {
            TryDelete(destPath);
            ShowError("Download was incomplete.",
                      $"Expected {totalBytes:N0} bytes, got {downloaded:N0}.");
            return false;
        }

        // Minimum size sanity check — reject truncated/empty downloads
        if (downloaded < 100_000)
        {
            TryDelete(destPath);
            ShowError("Downloaded file is too small.",
                      $"Got {downloaded:N0} bytes — expected a valid executable.");
            return false;
        }

        return true;
    }

    // ─── Error ──────────────────────────────────────────────────

    private void ShowError(string message, string detail)
    {
        _marqueeTimer.Stop();
        _progressOuter.Visible = false;
        _lblStatus.Text = message;
        _lblStatus.ForeColor = Color.FromArgb(255, 152, 0);
        _lblDetail.Text = detail;
        _btnAction.Visible = false;
        _btnCancel.Text = "OK";
        _btnCancel.Location = new Point(170, 112);
    }

    // ─── Static Helpers (called from Program.cs) ────────────────

    /// <summary>Detect whether the app was installed via winget (lives under WinGet\Packages).</summary>
    internal static bool IsWingetManaged() =>
        (Environment.ProcessPath ?? "").Contains(@"Microsoft\WinGet\Packages", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clean up .old/.new artifacts from a previous update.</summary>
    /// <remarks>
    /// Rollback safety: .old is kept until the new version proves itself by writing
    /// a .ok sentinel (see <see cref="WriteStartupSentinel"/>). If the new exe crashes
    /// before the sentinel is written, .old survives for manual recovery.
    /// </remarks>
    internal static void CleanupUpdateArtifacts()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        // Torn-state recovery: if update was interrupted between moving exe→.old
        // and .new→exe, the exe is gone but .old still has the previous version.
        if (!File.Exists(exePath))
        {
            var oldPath = exePath + ".old";
            if (File.Exists(oldPath))
            {
                try { File.Move(oldPath, exePath); }
                catch (Exception ex) { Logger.Warn($"Torn-state restore failed: {ex.Message}"); }
            }
            return;
        }

        // Always safe to remove a stray .new (half-downloaded from a cancelled update).
        TryDelete(exePath + ".new");

        // Only remove .old once the new version has successfully started once (sentinel present).
        var okSentinel = exePath + ".ok";
        if (File.Exists(okSentinel))
        {
            TryDelete(exePath + ".old");
        }
    }

    /// <summary>
    /// Write a .ok sentinel next to the exe once the new version has successfully
    /// reached its running state. CleanupUpdateArtifacts uses this to decide whether
    /// it's safe to remove .old — if the new exe crashes before the sentinel is
    /// written, .old persists and the user can rename it to recover manually.
    /// </summary>
    internal static void WriteStartupSentinel()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            var sentinel = exePath + ".ok";
            if (!File.Exists(sentinel))
                File.WriteAllText(sentinel, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Logger.Warn($"WriteStartupSentinel: {ex.Message}");
        }
    }

    /// <summary>Show a brief floating toast near the system tray after a successful update.</summary>
    internal static void ShowUpdateToast()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();

            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.FromArgb(240, 240, 240),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 8, 12, 8)
            };
            var toastFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            var lbl = new Label
            {
                Text = $"{AppName} updated to v{version}",
                AutoSize = true,
                Font = toastFont,
                ForeColor = Color.FromArgb(30, 30, 30)
            };
            toast.Controls.Add(lbl);
            toast.FormClosed += (_, _) => toastFont.Dispose();

            var screen = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            toast.Load += (_, _) =>
                toast.Location = new Point(screen.Right - toast.Width - 20, screen.Bottom - toast.Height - 20);
            toast.Show();

            var dismiss = new System.Windows.Forms.Timer { Interval = 5000 };
            dismiss.Tick += (_, _) =>
            {
                dismiss.Stop();
                dismiss.Dispose();
                toast.Close();
            };
            dismiss.Start();
        };
        timer.Start();
    }

    // ─── Helpers ────────────────────────────────────────────────

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _italicFont?.Dispose();
            _marqueeTimer.Stop();
            _marqueeTimer.Dispose();
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            try { _cts?.Dispose(); } catch (ObjectDisposedException) { }
        }
        base.Dispose(disposing);
    }
}
