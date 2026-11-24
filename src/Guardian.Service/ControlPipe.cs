using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Guardian.Core.Config;
using Guardian.Core.Security;
using Guardian.Core.Storage;

namespace Guardian.Service;

public sealed record ControlRequest(string Command, string Password, string? Payload = null, string? Arg = null);
public sealed record ControlResponse(bool Ok, string Message);

/// <summary>
/// Local control channel. Every privileged operation (uninstall, later: config changes)
/// goes through the service, which runs as SYSTEM and checks the admin/recovery password.
/// The pipe is reachable by local interactive users only; the password is the real gate.
/// </summary>
public sealed class ControlPipeServer(ILogger<ControlPipeServer> log, EncryptedConfigStore store, IHostApplicationLifetime life, Enforcers.TrustedClock clock,
    Guardian.Service.Enforcers.LockoutExemptions exemptions, Guardian.Service.Enforcers.NetworkEnforcer network, AdminGate gate)
    : BackgroundService
{
    public const string PipeName = "GuardianControl";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

            await using var pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
            await pipe.WaitForConnectionAsync(ct);
            try
            {
                var reader = new StreamReader(pipe);
                var writer = new StreamWriter(pipe) { AutoFlush = true };
                var req = JsonSerializer.Deserialize<ControlRequest>(await reader.ReadLineAsync(ct) ?? "");
                var resp = req is null ? new ControlResponse(false, "bad request") : Handle(req);
                await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Control request failed");
            }
        }
    }

    private ControlResponse Handle(ControlRequest req)
    {
        var cfg = store.Load();
        var auth = gate.Authenticate(cfg, req.Password);
        log.LogInformation("Control '{Cmd}': {Auth}", req.Command, auth);
        if (auth == AuthResult.LockedOut) return new(false, "Too many wrong passwords. Try again later.");
        if (auth == AuthResult.Denied) return new(false, "Access denied.");

        switch (req.Command)
        {
            case "verify":
                return new(true, auth.ToString());
            case "export":
                return new(true, ConfigEditing.Export(cfg));
            case "import" or "unlock" when cfg.Cooling.Hours > 0:
                return new(false, "לא זמין כש\"השהיית כיבוי\" פעילה. אפשר לשנות דרך חלון הניהול.");
            case "uninstall" when cfg.Cooling.Hours > 0 && !(cfg.Cooling.UninstallDueUtc is { } due && due <= clock.UtcNow):
                return new(false, cfg.Cooling.UninstallDueUtc is { } d
                    ? $"ההסרה מושהית. היא תתאפשר ב-{d.ToLocalTime():dd/MM/yyyy HH:mm}."
                    : $"ההסרה מושהית ב-{cfg.Cooling.Hours} שעות. בקשו הסרה בחלון הניהול (הגדרות > השהיית כיבוי), ואחרי הזמן הזה אפשר יהיה להסיר.");
            case "import":
                var oldUsers = cfg.UserLockouts.Select(u => u.UserName).ToList();
                try { ConfigEditing.Import(cfg, req.Payload ?? ""); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { return new(false, "Invalid config: " + ex.Message); }
                store.Save(cfg);
                foreach (var gone in oldUsers.Where(o => !cfg.UserLockouts.Any(u => u.UserName.Equals(o, StringComparison.OrdinalIgnoreCase))))
                    Guardian.Service.Native.NetUser.SetLogonHours(gone, Guardian.Service.Native.NetUser.AllHours);
                return new(true, "Policy updated.");
            case "set-password":
                if (!ConfigEditing.TrySetPassword(cfg, auth, req.Arg ?? "", req.Payload ?? "", out var err)) return new(false, err);
                store.Save(cfg);
                return new(true, "Password changed.");
            case "unlock":
                if (string.IsNullOrWhiteSpace(req.Arg) || !int.TryParse(req.Payload, out var mins) || mins is < 1 or > 1440)
                    return new(false, "usage: unlock USER MINUTES");
                exemptions.Until[req.Arg] = clock.LocalNow.AddMinutes(mins);
                return new(true, $"{req.Arg} unlocked until {clock.LocalNow.AddMinutes(mins):HH:mm}.");
            case "uninstall":
            case "uninstall-update": // an update: the same settings are installed again right after
            {
                // Running as SYSTEM: lift every protection and undo system changes;
                // the caller then stops and deletes both services.
                var services = Uninstaller.LiftProtections(cfg, network);
                _ = Task.Delay(500).ContinueWith(_ => life.StopApplication());
                return new(true, string.Join(",", services));
            }
            default:
                return new(false, "Unknown command.");
        }
    }

    public static ControlResponse Send(ControlRequest req, int timeoutMs = 10_000)
    {
        // Pipes do not support ReadTimeout (it throws), so the wait for the answer is bounded with an async read.
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(timeoutMs);
        var writer = new StreamWriter(pipe) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(req));
        string? line;
        try { line = new StreamReader(pipe).ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult(); }
        catch (TimeoutException) { throw new IOException("The service did not answer in time."); }
        return JsonSerializer.Deserialize<ControlResponse>(line ?? "") ?? new(false, "no response");
    }
}
