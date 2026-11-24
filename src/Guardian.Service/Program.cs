using Guardian.Service.Ui;
using Guardian.Core.Security;
using Guardian.Core.Storage;
using Guardian.Service;
using Guardian.Service.Enforcers;

// (no arguments, double-click)         -> install wizard, or open the management page if installed
// guardian run                         -> service entry point (started by the SCM)
// guardian watchdog                    -> companion service entry point
// guardian install [--name X]          -> first-time setup: set passwords, install hardened service
// guardian uninstall                   -> requires admin or recovery password
// guardian status
// guardian export FILE.json            -> write current policy (no secrets) to a file
// guardian import FILE.json            -> replace policy from a file
// guardian set-password admin|recovery
// guardian ui                          -> open the management page (http://127.0.0.1:8898)
// guardian hash FILE.exe               -> print SHA-256 for an app rule
// guardian unlock USER MINUTES         -> temporarily lift the schedule lock for a user

var cmd = args.FirstOrDefault() ?? "gui";
var store = new EncryptedConfigStore(Paths.DataDir, new DpapiKeyProtector());

switch (cmd)
{
    case "gui":
    {
        // Double-click: open the app if this build is installed; otherwise install or update (needs admin).
        Setup.HideOwnConsole();
        if (Setup.IsInstalled() && !Setup.IsDifferentBuildThanInstalled()) return Gui.App();
        if (!Setup.IsElevated()) { Setup.RelaunchElevated(["setup"]); return 0; }
        return Gui.Setup(store, Setup.IsInstalled() ? Guardian.Service.Ui.SetupMode.Update : Guardian.Service.Ui.SetupMode.Install);
    }

    case "setup":
        Setup.HideOwnConsole();
        return Gui.Setup(store, Setup.IsInstalled() ? Guardian.Service.Ui.SetupMode.Update : Guardian.Service.Ui.SetupMode.Install);

    case "app":
        Setup.HideOwnConsole();
        return Gui.App();

    case "blocked":
        Setup.HideOwnConsole();
        return Gui.Blocked(args.ElementAtOrDefault(1) ?? "");

    case "uninstall-gui":
    {
        Setup.HideOwnConsole();
        if (!Setup.IsElevated()) { Setup.RelaunchElevated([cmd]); return 0; }
        if (Path.GetDirectoryName(Environment.ProcessPath!)!.StartsWith(Paths.InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            // Run from a temporary copy so the install folder can be deleted.
            var tmp = Path.Combine(Path.GetTempPath(), "guardian-uninstall.exe");
            File.Copy(Environment.ProcessPath!, tmp, true);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tmp, "uninstall-gui") { UseShellExecute = true });
            return 0;
        }
        return Gui.Setup(store, Guardian.Service.Ui.SetupMode.Uninstall);
    }

    case "uninstall-system":
        return Uninstaller.SystemTaskMain(args.ElementAtOrDefault(1) ?? "", store);

    case "run":
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<AdminGate>();
        builder.Services.AddHostedService<Guardian.Service.Ui.ManagementUi>();
        builder.Services.AddSingleton<LockoutExemptions>();
        builder.Services.AddSingleton<LockoutEnforcer>();
        builder.Services.AddSingleton<AppBlocker>();
        builder.Services.AddSingleton<AccessLog>();
        builder.Services.AddSingleton<NetworkEnforcer>();
        builder.Services.AddSingleton<DirectConnectionWatcher>();
        builder.Services.AddSingleton<AppCatalogScanner>();
        builder.Services.AddSingleton<DnsFilterServer>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DnsFilterServer>());
        builder.Services.AddSingleton<ProxyHost>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ProxyHost>());
        builder.Services.AddHostedService<EnforcementWorker>();
        builder.Services.AddHostedService<ControlPipeServer>();
        builder.Build().Run();
        return 0;

    case "watchdog":
    {
        var wb = Host.CreateApplicationBuilder(args);
        wb.Services.AddWindowsService();
        wb.Services.AddSingleton(store);
        wb.Services.AddHostedService<WatchdogWorker>();
        wb.Build().Run();
        return 0;
    }

    case "install":
    {
        if (store.Exists) { Console.Error.WriteLine("Already installed. Uninstall first."); return 1; }
        try
        {
            var admin = Cli.ReadNewPassword("Admin password");
            var recovery = Cli.ReadNewPassword("Recovery (rescue) password - store it somewhere safe");
            var name = Setup.Install(store, admin, recovery, Arg("--name"));
            Console.WriteLine($"Installed as '{name}'. All blocking features are OFF; turn them on in the management page: guardian ui");
            return 0;
        }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    case "uninstall":
    {
        var (ok, message) = Setup.Uninstall(store, Cli.ReadPassword("Password"));
        (ok ? Console.Out : Console.Error).WriteLine(message);
        return ok ? 0 : 2;
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

    case "ui":
        return Gui.App();

    case "hash":
    {
        var f = args.ElementAtOrDefault(1);
        if (f is null || !File.Exists(f)) { Console.Error.WriteLine("usage: guardian hash FILE.exe"); return 1; }
        using var fs = File.OpenRead(f);
        Console.WriteLine(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)));
        return 0;
    }

    case "status":
        if (!store.Exists) { Console.WriteLine("Not installed."); return 0; }
        var st = store.Load();
        Console.WriteLine($"Installed. Service: {st.ServiceName}, watchdog: {st.WatchdogServiceName}.");
        return 0;

    default:
        Console.WriteLine("usage: guardian install [--name NAME] | uninstall | status | export FILE | import FILE | set-password admin|recovery | unlock USER MINUTES | hash FILE | ui");
        return cmd is "help" or "--help" or "/?" ? 0 : 1;
}

string? Arg(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
