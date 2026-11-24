using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Security.Cryptography;
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
    private readonly ConcurrentDictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManagementEventWatcher? _watcher;
    private volatile GuardianConfig? _cfg;
    private List<string> _trusted = [];

    private readonly AccessLog _access;
    private readonly Lazy<string?> _selfHash = new(() => HashFile(Environment.ProcessPath!));

    public AppBlocker(ILogger<AppBlocker> log, AccessLog access)
    {
        _log = log;
        _access = access;
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
            _trusted = AppPolicyEvaluator.TrustedPrefixes(cfg.Apps,
                [Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)],
                AppContext.BaseDirectory);
            _cfg = cfg;
        }
        if (!cfg.Apps.Enabled) return;
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
        if (cfg is null || !cfg.Apps.Enabled || pid <= 4 || pid == Environment.ProcessId) return;
        using var proc = TryGet(pid);
        if (proc is null || proc.SessionId == 0) return;
        var path = ProcessPath.Get(pid);
        if (string.IsNullOrEmpty(path)) return;

        // Never block Guardian itself (e.g. the temporary copy that runs the uninstaller):
        // identified by identical file contents, not by name.
        if (Path.GetFileName(path).StartsWith("guardian", StringComparison.OrdinalIgnoreCase) &&
            _selfHash.Value is { } self && Hash(path) == self) return;

        // Each Windows account may have its own rules; unknown accounts follow the general ones.
        var user = cfg.UserPolicies.Count > 0 ? ProcessUser.Of(pid) : null;
        var rules = UserScopes.Effective(cfg, user).Apps;
        var hashNeeded = rules.Rules.Any(r => !string.IsNullOrEmpty(r.Sha256));
        var info = new ProcessInfo(path, hashNeeded ? Hash(path) : null);
        var decision = AppPolicyEvaluator.Evaluate(rules, info, DateTime.Now, _trusted);
        if (decision.Action == RuleAction.Allow) return;

        try
        {
            proc.Kill(entireProcessTree: true);
            _log.LogInformation("Blocked {Path} (pid {Pid}): {Reason}", path, pid, decision.Reason);
            _access.Add("app", path, allowed: false, user ?? ProcessUser.Of(pid));
            NotifyOnce(proc.SessionId, path);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not terminate {Path}", path); }
    }

    private void NotifyOnce(int sessionId, string path)
    {
        var key = sessionId + "|" + path;
        if (_lastNotified.TryGetValue(key, out var t) && DateTime.UtcNow - t < TimeSpan.FromMinutes(1)) return;
        _lastNotified[key] = DateTime.UtcNow;
        var name = DisplayName(path);
        // A styled Hebrew screen on the user's desktop; a plain message box if that fails.
        var arg = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name));
        if (!UserLauncher.Launch(sessionId, Environment.ProcessPath!, $"blocked {arg}"))
            Wts.Notify(sessionId, "Guardian", $"התוכנה \"{name}\" חסומה במחשב הזה.");
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
