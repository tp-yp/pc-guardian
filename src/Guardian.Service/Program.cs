using Guardian.Core.Config;
using Guardian.Core.Naming;
using Guardian.Core.Security;
using Guardian.Core.Storage;
using Guardian.Service;
using Guardian.Service.Enforcers;

// guardian run                         -> service entry point (started by the SCM)
// guardian install [--name X]          -> first-time setup: set passwords, install hardened service
// guardian uninstall                   -> requires admin or recovery password
// guardian status
// guardian export FILE.json            -> write current policy (no secrets) to a file
// guardian import FILE.json            -> replace policy from a file
// guardian set-password admin|recovery
// guardian hash FILE.exe               -> print SHA-256 for an app rule
// guardian unlock USER MINUTES         -> temporarily lift the schedule lock for a user

var cmd = args.FirstOrDefault() ?? "help";
var store = new EncryptedConfigStore(Paths.DataDir, new DpapiKeyProtector());

switch (cmd)
{
    case "run":
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<LockoutExemptions>();
        builder.Services.AddSingleton<LockoutEnforcer>();
        builder.Services.AddSingleton<AppBlocker>();
        builder.Services.AddSingleton<AccessLog>();
        builder.Services.AddSingleton<NetworkEnforcer>();
        builder.Services.AddSingleton<DnsFilterServer>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DnsFilterServer>());
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
        DataDirSecurity.Lock(Paths.DataDir);
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

    case "export":
    case "import":
    {
        var file = args.ElementAtOrDefault(1);
        if (file is null) { Console.Error.WriteLine($"usage: guardian {cmd} FILE.json"); return 1; }
        var payload = cmd == "import" ? File.ReadAllText(file) : null;
        var resp = ControlPipeServer.Send(new ControlRequest(cmd, Cli.ReadPassword("Password"), payload));
        if (!resp.Ok) { Console.Error.WriteLine(resp.Message); return 2; }
        if (cmd == "export") { File.WriteAllText(file, resp.Message); Console.WriteLine($"Policy written to {file}."); }
        else Console.WriteLine(resp.Message);
        return 0;
    }

    case "set-password":
    {
        var which = args.ElementAtOrDefault(1);
        if (which is not ("admin" or "recovery")) { Console.Error.WriteLine("usage: guardian set-password admin|recovery"); return 1; }
        var current = Cli.ReadPassword(which == "recovery" ? "Current recovery password" : "Current admin or recovery password");
        var resp = ControlPipeServer.Send(new ControlRequest("set-password", current, Cli.ReadNewPassword("New password"), which));
        Console.WriteLine(resp.Message);
        return resp.Ok ? 0 : 2;
    }

    case "unlock":
    {
        var user = args.ElementAtOrDefault(1);
        if (user is null || !int.TryParse(args.ElementAtOrDefault(2), out var minutes) || minutes is < 1 or > 1440)
        { Console.Error.WriteLine("usage: guardian unlock USER MINUTES (1-1440)"); return 1; }
        var resp = ControlPipeServer.Send(new ControlRequest("unlock", Cli.ReadPassword("Password"), minutes.ToString(), user));
        Console.WriteLine(resp.Message);
        return resp.Ok ? 0 : 2;
    }

    case "hash":
    {
        var f = args.ElementAtOrDefault(1);
        if (f is null || !File.Exists(f)) { Console.Error.WriteLine("usage: guardian hash FILE.exe"); return 1; }
        using var fs = File.OpenRead(f);
        Console.WriteLine(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)));
        return 0;
    }

    case "status":
        Console.WriteLine(store.Exists ? $"Installed as '{store.Load().ServiceName}'." : "Not installed.");
        return 0;

    default:
        Console.WriteLine("usage: guardian install [--name NAME] | uninstall | status | export FILE | import FILE | set-password admin|recovery | unlock USER MINUTES | hash FILE");
        return cmd == "help" ? 0 : 1;
}

string? Arg(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
