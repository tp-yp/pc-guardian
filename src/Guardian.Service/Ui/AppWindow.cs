using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Guardian.Service.Ui;

/// <summary>
/// A normal desktop window that shows Guardian's screens with WebView2 – the browser engine built
/// into Windows (Edge's engine, installed with Windows 10/11). It does not depend on any browser
/// the person uses, so browsers can be blocked without losing the management window.
/// Plain Win32 (no WinForms/WPF) to keep the single exe small and self-contained.
/// </summary>
internal sealed class AppWindow
{
    public sealed record Options(string Title, int Width, int Height, bool Popup = false, bool TopMost = false, int AutoCloseSeconds = 0);

    private const int WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_CLOSE = 0x0010, WM_APP_RUN = 0x8001, WM_TIMER = 0x0113;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_BORDER = 0x00800000, WS_CAPTION = 0x00C00000;
    private const uint WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080;

    private readonly Options _opt;
    private readonly Action<CoreWebView2> _load;
    private IntPtr _hwnd;
    private CoreWebView2Controller? _controller;
    private readonly WndProcDelegate _proc;
    private readonly ConcurrentQueue<(SendOrPostCallback, object?)> _queue = new();

    private AppWindow(Options opt, Action<CoreWebView2> load)
    {
        _opt = opt;
        _load = load;
        _proc = WndProc;
    }

    /// <summary>Shows a window and blocks until it is closed. Returns false if WebView2 is not available.</summary>
    public static bool Show(Options opt, Action<CoreWebView2> load, Action<AppWindow, string>? onMessage = null)
    {
        try { CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (Exception) { return false; }

        var ok = true;
        var t = new Thread(() =>
        {
            try { new AppWindow(opt, load) { _onMessage = onMessage }.Run(); }
            catch (Exception) { ok = false; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return ok;
    }

    private Action<AppWindow, string>? _onMessage;

    public void Close() => PostMessageW(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    private void Run()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2: sharp text
        var hInstance = GetModuleHandleW(null);
        var cls = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512)),
            hIcon = ExtractIconW(hInstance, Environment.ProcessPath!, 0),
            hbrBackground = new IntPtr(6), // COLOR_WINDOW + 1
            lpszClassName = "GuardianWindow" + Environment.CurrentManagedThreadId,
        };
        RegisterClassExW(ref cls);

        var scale = GetDpiForSystem() / 96.0;
        int w = (int)(_opt.Width * scale), h = (int)(_opt.Height * scale);
        // Fit inside the visible work area (screen minus taskbar), so the title bar and X are always reachable.
        if (!SystemParametersInfoW(0x30 /* SPI_GETWORKAREA */, 0, out var wa, 0))
            wa = new RECT { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        int areaW = wa.Right - wa.Left, areaH = wa.Bottom - wa.Top, margin = (int)(16 * scale);
        w = Math.Max(Math.Min(w, areaW - 2 * margin), Math.Min(areaW, (int)(480 * scale)));
        h = Math.Max(Math.Min(h, areaH - 2 * margin), Math.Min(areaH, (int)(400 * scale)));
        int x = wa.Left + Math.Max(0, (areaW - w) / 2), y = wa.Top + Math.Max(0, (areaH - h) / 2);
        var style = _opt.Popup ? WS_POPUP | WS_BORDER | WS_VISIBLE : WS_OVERLAPPEDWINDOW | WS_VISIBLE;
        var ex = (_opt.TopMost ? WS_EX_TOPMOST : 0) | (_opt.Popup ? WS_EX_TOOLWINDOW : 0);
        _hwnd = CreateWindowExW(ex, cls.lpszClassName, _opt.Title, style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindow failed");
        SetForegroundWindow(_hwnd);
        if (_opt.AutoCloseSeconds > 0) SetTimer(_hwnd, new IntPtr(1), (uint)_opt.AutoCloseSeconds * 1000, IntPtr.Zero);

        SynchronizationContext.SetSynchronizationContext(new Win32Context(this));
        SynchronizationContext.Current!.Post(_ => _ = InitAsync(), null);

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private async Task InitAsync()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
        _controller = await env.CreateCoreWebView2ControllerAsync(_hwnd);
        var wv = _controller.CoreWebView2;
        wv.Settings.AreDevToolsEnabled = false;
        wv.Settings.IsStatusBarEnabled = false;
        wv.Settings.AreDefaultContextMenusEnabled = false;
        wv.Settings.IsZoomControlEnabled = false;
        wv.DocumentTitleChanged += (_, _) => { if (!_opt.Popup) SetWindowTextW(_hwnd, wv.DocumentTitle); };
        wv.WebMessageReceived += (_, e) =>
        {
            var m = e.TryGetWebMessageAsString();
            if (m == "close") Close(); else _onMessage?.Invoke(this, m);
        };
        // Links that open a new window go to the default browser.
        wv.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
        };
        Resize();
        _load(wv);
    }

    private void Resize()
    {
        if (_controller is null) return;
        GetClientRect(_hwnd, out var r);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, r.Right - r.Left, r.Bottom - r.Top);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SIZE: Resize(); return IntPtr.Zero;
            case WM_APP_RUN:
                while (_queue.TryDequeue(out var item)) item.Item1(item.Item2);
                return IntPtr.Zero;
            case WM_TIMER: DestroyWindow(hwnd); return IntPtr.Zero;
            case WM_DESTROY:
                _controller?.Close();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private sealed class Win32Context(AppWindow w) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            w._queue.Enqueue((d, state));
            PostMessageW(w._hwnd, WM_APP_RUN, IntPtr.Zero, IntPtr.Zero);
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);
        public override SynchronizationContext CreateCopy() => this;
    }

    // ---- Win32 ----
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(IntPtr h, string text);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(uint action, uint param, out RECT r, uint winIni);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr fn);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIconW(IntPtr inst, string file, uint index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr h, string text, string caption, uint type);

    /// <summary>Simple RTL message box (used when WebView2 is missing).</summary>
    public static void Message(string text) => MessageBoxW(IntPtr.Zero, text, "Geder", 0x40 | 0x00080000 | 0x00100000);
}
