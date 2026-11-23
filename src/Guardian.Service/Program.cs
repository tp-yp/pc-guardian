using Guardian.Core.Config;
using Guardian.Core.Naming;
using Guardian.Core.Security;
using Guardian.Core.Storage;
using Guardian.Service;

// guardian run                         -> service entry point (started by the SCM)
// guardian install [--name X]          -> first-time setup: set passwords, install hardened service
// guardian uninstall                   -> requires admin or recovery password
// guardian status

var cmd = args.FirstOrDefault() ?? "help";
var store = new EncryptedConfigStore(Paths.DataDir, new DpapiKeyProtector());

switch (cmd)
{
    case "run":
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService();
        builder.Services.AddSingleton(store);
        builder.Services.AddHostedService<EnforcementWorker>();
        builder.Services.AddHostedService<ControlPipeServer>();
        builder.Build().Run();
        return 0;

    case "install":
    {
        if (store.Exists) { Console.Error.WriteLine("Already installed. Uninstall first."); return 1; }
        var name = Arg("--name") ?? ServiceNameGenerator.Generate();
        if (!ServiceNameGenerator.IsValid(name)) { Console.Error.WriteLine("Invalid service name (letters/digits only)."); return 1; }
        var admin = Cli.ReadNewPassword("Admin password");
        var recovery = Cli.ReadNewPassword("Recovery (rescue) password - store it somewhere safe");
        if (admin == recovery) { Console.Error.WriteLine("Recovery password must differ from admin password."); return 1; }

        Directory.CreateDirectory(Paths.InstallDir);
        var exe = Path.Combine(Paths.InstallDir, name + ".exe");
        File.Copy(Environment.ProcessPath!, exe, overwrite: true);

        store.Save(new GuardianConfig
        {
            ServiceName = name,
            AdminPasswordHash = PasswordHasher.Hash(admin),
            RecoveryPasswordHash = PasswordHasher.Hash(recovery),
        });
        ServiceHardening.Install(name, exe);
        Console.WriteLine($"Installed and started service '{name}'.");
        return 0;
    }

    case "uninstall":
    {
        // The service (running as SYSTEM) checks the password and lifts its own protection.
        var resp = ControlPipeServer.Send(new ControlRequest("uninstall", Cli.ReadPassword("Password")));
        if (!resp.Ok) { Console.Error.WriteLine(resp.Message); return 2; }
        Thread.Sleep(1500);
        ServiceHardening.StopAndDelete(resp.Message);
        try { File.Delete(Path.Combine(Paths.InstallDir, resp.Message + ".exe")); } catch (IOException) { /* removed on reboot */ }
        Console.WriteLine("Uninstalled.");
        return 0;
    }

    case "status":
        Console.WriteLine(store.Exists ? $"Installed as '{store.Load().ServiceName}'." : "Not installed.");
        return 0;

    default:
        Console.WriteLine("usage: guardian install [--name NAME] | uninstall | status");
        return cmd == "help" ? 0 : 1;
}

string? Arg(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
