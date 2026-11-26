using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Security.Cryptography;
using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Policy;
using Guardian.Service.Native;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Blocks executables by terminating them as soon as they start.
/// Primary signal: WMI Win32_ProcessStartTrace (kernel process-start events, needs SYSTEM).
/// Backup: a full process scan on every tick, which also catches processes already running
/// when a schedule window opens and anything WMI missed.
/// Limitation: without a kernel driver the process exists for a few milliseconds before it is killed.
/// </summary>
public sealed class AppBlocker : IDisposable
{
    private readonly ILogger<AppBlocker> _log;
    private readonly ConcurrentDictionary<string, (long Size, DateTime Mtime, string Hash)> _hashCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (long Size, DateTime Mtime, string? Name)> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private readonly ConcurrentDictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManagementEventWatcher? _watcher;
    private volatile GuardianConfig? _cfg;
    private List<string> _trustedSelf = [];  // Guardian's own folder only

    private readonly AccessLog _access;
    private readonly TrustedClock _clock;
    private readonly Lazy<string?> _selfHash = new(() => HashFile(Environment.ProcessPath!));

    private readonly AdminToolsExemptions _toolsExempt;

    public AppBlocker(ILogger<AppBlocker> log, AccessLog access, TrustedClock clock, AdminToolsExemptions toolsExempt)
    {
        _toolsExempt = toolsExempt;
        _log = log;
        _access = access;
        _clock = clock;
        try
        {
            _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID FROM Win32_ProcessStartTrace"));
            _watcher.EventArrived += (_, e) =>
            {
                try { Check(Convert.ToInt32(e.NewEvent["ProcessID"])); }
                catch (Exception ex) { _log.LogDebug(ex, "start-trace check failed"); }
            };
            _watcher.Start();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Process start events unavailable; falling back to scanning only");
            _watcher = null;
        }
    }

    public void Tick(GuardianConfig cfg)
    {
        if (!ReferenceEquals(cfg, _cfg))
        {
            _trustedSelf = AppPolicyEvaluator.TrustedPrefixes(AppContext.BaseDirectory);
            _cfg = cfg;
        }
        if (!UserScopes.AnyAppBlocking(cfg)) return;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.SessionId == 0) continue; // services / system processes
                Check(p.Id);
            }
        }
    }

    private void Check(int pid)
    {
        var cfg = _cfg;
        if (cfg is null || !UserScopes.AnyAppBlocking(cfg) || pid <= 4 || pid == Environment.ProcessId) return;
        using var proc = TryGet(pid);
        if (proc is null || proc.SessionId == 0) return;
        var path = ProcessPath.Get(pid);
        if (string.IsNullOrEmpty(path)) return;

        // Never block Guardian itself (e.g. the temporary copy that runs the uninstaller):
        // identified by identical file contents, not by name.
        if ((Path.GetFileName(path).StartsWith("guardian", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith("geder", StringComparison.OrdinalIgnoreCase)) &&
            _selfHash.Value is { } self && Hash(path) == self) return;
        // Nor any other Geder build, such as an update that is being installed from a Downloads folder.
        if (IsGederFile(path)) return;
        // Each Windows account may have its own rules; unknown accounts follow the general ones.
        var user = cfg.UserPolicies.Count > 0 ? ProcessUser.Of(pid) : null;
        var rules = UserScopes.Effective(cfg, user).Apps;
        var scopeName = UserScopes.Find(cfg, user)?.UserName;

        // Admin tools are checked before the protected-parts rule: copies of them also live in WinSxS.
        // (Not while Guardian is being removed or updated: its config is already gone and it runs sc.exe itself.)
        if (rules.BlockAdminTools && AdminTools.Is(path, OriginalName(path)) && File.Exists(Path.Combine(Paths.DataDir, "config.bin"))
            && AdminTools.StartedByHand(ProcessParent.PathOf(pid)) && !_toolsExempt.IsExempt(user ?? ProcessUser.Of(pid), _clock.LocalNow))
        {
            Kill(proc, pid, path, Reasons.AdminTool, user, scopeName);
            return;
        }

        // Essential parts of Windows (and Guardian's folder) are never closed, whatever the rules say.
        if (ProtectedApps.IsProtected(path, WindowsDir, Paths.InstallDir)) return;
        // "Block video files": video players, and any program opened with a video file.
        if (rules.BlockVideoFiles && (VideoFiles.IsPlayer(path, OriginalName(path)) || VideoFiles.InCommandLine(ProcessCommandLine.Get(pid))))
        {
            Kill(proc, pid, path, Reasons.Video, user, scopeName);
            return;
        }
        // Programs inside one account's profile folder (its Downloads, Desktop, AppData) are that account's only.
        // This holds whenever program locking is on for anyone, whatever the owner's own settings are.
        // A program an administrator started with elevation (the admin password was typed) is left alone.
        if (UserScopes.AnyProgramLocking(cfg) && UserPrograms.OwnerOf(path, UserProfiles.Current()) != null &&
            UserPrograms.IsOtherUsers(path, user ?? ProcessUser.Of(pid), UserProfiles.Current()) && !ProcessUser.IsElevated(pid))
        {
            Kill(proc, pid, path, Reasons.OtherAccount, user, scopeName);
            return;
        }
        var hashNeeded = rules.Rules.Any(r => !string.IsNullOrEmpty(r.Sha256));
        var nameNeeded = rules.Rules.Any(r => r.Action == RuleAction.Block && (!string.IsNullOrEmpty(r.FileName) || !string.IsNullOrEmpty(r.OriginalName)));
        var info = new ProcessInfo(path, hashNeeded ? Hash(path) : null, nameNeeded ? OriginalName(path) : null);
        var decision = AppPolicyEvaluator.Evaluate(rules, info, _clock.LocalNow, _trustedSelf);
        // "Block VPN and proxies" (whole computer): known VPN / proxy programs, also when renamed.
        if (decision.Action == RuleAction.Allow && cfg.Vpn.Block && Guardian.Core.Network.VpnLists.IsVpnProgram(path, info.OriginalName ?? OriginalName(path)))
        {
            decision = new AppDecision(RuleAction.Block, Reasons.Vpn);
            scopeName = null;
        }
        if (decision.Action == RuleAction.Allow) return;
        Kill(proc, pid, path, decision.Reason, user, scopeName);
    }

    private void Kill(Process proc, int pid, string path, string reason, string? user, string? scopeName)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
            _log.LogInformation("Blocked {Path} (pid {Pid}): {Reason}", path, pid, reason);
            _access.Add("app", path, allowed: false, user ?? ProcessUser.Of(pid), path);
            NotifyOnce(proc.SessionId, path, reason + (reason is Reasons.Vpn or Reasons.OtherAccount ? "" : scopeName is null ? " · הגדרות המחשב כולו" : $" · הגדרות של {scopeName}"));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not terminate {Path}", path); }
    }

    private void NotifyOnce(int sessionId, string path, string reason)
    {
        var key = sessionId + "|" + path;
        if (_lastNotified.TryGetValue(key, out var t) && DateTime.UtcNow - t < TimeSpan.FromMinutes(1)) return;
        _lastNotified[key] = DateTime.UtcNow;
        var name = DisplayName(path);
        // A styled Hebrew screen on the user's desktop; a plain message box if that fails.
        var arg = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name));
        var why = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(reason));
        if (!UserLauncher.Launch(sessionId, Environment.ProcessPath!, $"blocked {arg} {why}"))
            Wts.Notify(sessionId, "Geder", $"התוכנה \"{name}\" חסומה במחשב הזה.\nהסיבה: {reason}");
    }

    private string? Hash(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (_hashCache.TryGetValue(path, out var c) && c.Size == fi.Length && c.Mtime == fi.LastWriteTimeUtc) return c.Hash;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var h = Convert.ToHexString(SHA256.HashData(fs));
            _hashCache[path] = (fi.Length, fi.LastWriteTimeUtc, h);
            return h;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The original file name written inside the program, which renaming the file does not change.</summary>
    private string? OriginalName(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (_nameCache.TryGetValue(path, out var c) && c.Size == fi.Length && c.Mtime == fi.LastWriteTimeUtc) return c.Name;
            var name = ReadOriginalName(path);
            _nameCache[path] = (fi.Length, fi.LastWriteTimeUtc, name);
            return name;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Size, DateTime Mtime, bool Geder)> _selfCache = new(StringComparer.OrdinalIgnoreCase);

    private bool IsGederFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (_selfCache.TryGetValue(path, out var c) && c.Size == fi.Length && c.Mtime == fi.LastWriteTimeUtc) return c.Geder;
            var v = FileVersionInfo.GetVersionInfo(path);
            var geder = SelfIdentity.IsGeder(!string.IsNullOrWhiteSpace(v.OriginalFilename) ? v.OriginalFilename : v.InternalName, v.ProductName);
            _selfCache[path] = (fi.Length, fi.LastWriteTimeUtc, geder);
            return geder;
        }
        catch (Exception) { return false; }
    }

    public static string? ReadOriginalName(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            var n = !string.IsNullOrWhiteSpace(v.OriginalFilename) ? v.OriginalFilename : v.InternalName;
            return string.IsNullOrWhiteSpace(n) ? null : n.Trim();
        }
        catch (Exception) { return null; }
    }

    private static string DisplayName(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            var n = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription : v.ProductName;
            if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
        }
        catch { /* fall back to the file name */ }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string? HashFile(string path)
    {
        try { using var fs = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(fs)); }
        catch (Exception) { return null; }
    }

    private static Process? TryGet(int pid)
    {
        try { return Process.GetProcessById(pid); } catch (ArgumentException) { return null; }
    }

    public void Dispose() { _watcher?.Stop(); _watcher?.Dispose(); }
}
