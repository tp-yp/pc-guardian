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
public sealed class ControlPipeServer(ILogger<ControlPipeServer> log, EncryptedConfigStore store, IHostApplicationLifetime life)
    : BackgroundService
{
    public const string PipeName = "GuardianControl";
    private readonly AdminGate _gate = new();

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
        var auth = _gate.Authenticate(cfg, req.Password);
        log.LogInformation("Control '{Cmd}': {Auth}", req.Command, auth);
        if (auth == AuthResult.LockedOut) return new(false, "Too many wrong passwords. Try again later.");
        if (auth == AuthResult.Denied) return new(false, "Access denied.");

        switch (req.Command)
        {
            case "verify":
                return new(true, auth.ToString());
            case "export":
                return new(true, ConfigEditing.Export(cfg));
            case "import":
                try { ConfigEditing.Import(cfg, req.Payload ?? ""); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { return new(false, "Invalid config: " + ex.Message); }
                store.Save(cfg);
                return new(true, "Policy updated.");
            case "set-password":
                if (!ConfigEditing.TrySetPassword(cfg, auth, req.Arg ?? "", req.Payload ?? "", out var err)) return new(false, err);
                store.Save(cfg);
                return new(true, "Password changed.");
            case "uninstall":
                // Running as SYSTEM: restore the normal DACL and remove restart actions,
                // then remove config and let the CLI stop/delete the service.
                ServiceHardening.Unharden(cfg.ServiceName);
                ServiceHardening.DisableRecovery(cfg.ServiceName);
                File.Delete(Path.Combine(Paths.DataDir, "config.bin"));
                File.Delete(Path.Combine(Paths.DataDir, "key.bin"));
                _ = Task.Delay(500).ContinueWith(_ => life.StopApplication());
                return new(true, cfg.ServiceName);
            default:
                return new(false, "Unknown command.");
        }
    }

    public static ControlResponse Send(ControlRequest req, int timeoutMs = 10_000)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        pipe.Connect(timeoutMs);
        var writer = new StreamWriter(pipe) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(req));
        var line = new StreamReader(pipe).ReadLine();
        return JsonSerializer.Deserialize<ControlResponse>(line ?? "") ?? new(false, "no response");
    }
}
