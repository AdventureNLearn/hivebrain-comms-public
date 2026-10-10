using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Shell;

public partial class Form1 : Form
{
    private static readonly Color Paper = Color.FromArgb(0x0f, 0x11, 0x15);
    private static readonly Color Panel = Color.FromArgb(0x17, 0x1a, 0x21);
    private static readonly Color Ink = Color.FromArgb(0xf2, 0xf4, 0xf8);
    private static readonly Color Green = Color.FromArgb(0x3d, 0xdc, 0x84);
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private readonly string statePath;
    private Panel tabBar = null!;
    private readonly List<Button> tabButtons = new();
    private readonly List<Control> pages = new();
    private int activeTab;
    private Label dot = null!;
    private Button hubToggle = null!;
    private CheckBox autoStart = null!;
    private CheckBox startWithWindows = null!;
    private readonly string settingsPath;
    private bool autoStartHub = true;
    private bool hubBusy;
    private System.Windows.Forms.Timer hubTimer = null!;

    public Form1()
    {
        statePath = Path.Combine(root, "window-state.txt");
        settingsPath = Path.Combine(root, "shell-settings.txt");
        Text = "HiveBrainComms";
        Width = 1280;
        Height = 860;
        BackColor = Paper;
        ForeColor = Ink;
        StartPosition = FormStartPosition.CenterScreen;
        RestoreWindow();

        UseDarkTitleBar();
        var bar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Panel };
        var title = new Label { Text = "HiveBrainComms", AutoSize = true, ForeColor = Ink, Location = new Point(12, 12), Font = new Font(Font, FontStyle.Bold) };
        var bot = new Button { Text = "Grok Bot", FlatStyle = FlatStyle.Flat, ForeColor = Paper, BackColor = Green, Location = new Point(180, 8), AutoSize = true };
        bot.Click += (_, _) => OpenBot();
        dot = new Label { Text = "hub", AutoSize = true, ForeColor = Ink, Location = new Point(290, 14) };
        hubToggle = new Button { Text = "Start", FlatStyle = FlatStyle.Flat, ForeColor = Ink, BackColor = Paper, Location = new Point(360, 8), AutoSize = true };
        hubToggle.Click += async (_, _) => await ToggleHub();
        var startAll = new Button { Text = "Start All", FlatStyle = FlatStyle.Flat, ForeColor = Paper, BackColor = Green, Location = new Point(440, 8), AutoSize = true };
        var about = new Button { Text = "About", FlatStyle = FlatStyle.Flat, ForeColor = Ink, BackColor = Paper, Location = new Point(1040, 8), AutoSize = true };
        about.Click += (_, _) => ShowAbout();
        startAll.Click += async (_, _) => await StartAll();
        autoStart = new CheckBox { Text = "Auto-start hub", ForeColor = Ink, BackColor = Panel, AutoSize = true, Location = new Point(560, 12) };
        startWithWindows = new CheckBox { Text = "Start with Windows", ForeColor = Ink, BackColor = Panel, AutoSize = true, Location = new Point(700, 12) };
        LoadSettings();
        if (!File.Exists(settingsPath)) SaveSettings();
        autoStart.Checked = autoStartHub;
        startWithWindows.Checked = StartupShortcutExists();
        autoStart.CheckedChanged += (_, _) => { autoStartHub = autoStart.Checked; SaveSettings(); };
        startWithWindows.CheckedChanged += (_, _) => SetStartup(startWithWindows.Checked);
        notice = new Label { Text = "", AutoSize = false, ForeColor = Ink, BackColor = Panel, Dock = DockStyle.Bottom, Height = 24 };
        bar.Controls.Add(title);
        bar.Controls.Add(bot);
        bar.Controls.Add(dot);
        bar.Controls.Add(hubToggle);
        bar.Controls.Add(startAll);
        bar.Controls.Add(about);
        bar.Controls.Add(autoStart);
        bar.Controls.Add(startWithWindows);

        tabBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Paper, Padding = new Padding(8, 4, 0, 0) };
        Controls.Add(notice);
        Controls.Add(tabBar);
        Controls.Add(bar);

        tabBar.BringToFront();
        ResizeEnd += (_, _) => SaveWindow();
        FormClosing += (_, e) => { SaveWindow(); hubTimer.Stop(); Program.Log("closing " + e.CloseReason); };
        Load += async (_, _) =>
        {
            try
            {
                if (autoStartHub && !await HubUp()) await StartHub();
                await RefreshHub();
                hubTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                hubTimer.Tick += async (_, _) => await RefreshHub();
                hubTimer.Start();
                await BuildTabs();
                Program.Log("tabs ready");
            }
            catch (Exception ex)
            {
                Program.Log("tabs " + ex);
            }
        };
    }

    private async Task RefreshHub()
    {
        if (IsDisposed) return;
        var up = await HubUp();
        dot.Text = up ? "hub  on" : "hub  off";
        dot.ForeColor = up ? Green : Color.FromArgb(0xc4, 0x56, 0x4a);
        hubToggle.Text = up ? "Stop" : "Start";
    }

    private async Task ToggleHub()
    {
        if (hubBusy) return;
        hubBusy = true;
        try
        {
            if (await HubUp()) StopHub();
            else await StartHub();
            await RefreshHub();
        }
        catch (Exception ex)
        {
            Program.Log("hub toggle " + ex.Message);
        }
        finally
        {
            hubBusy = false;
        }
    }

    private async Task StartHub()
    {
        var starter = Path.Combine(root, "Start-HiveBrainComms.ps1");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + starter + "\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        });
        var deadline = DateTime.Now.AddSeconds(15);
        while (!await HubUp() && DateTime.Now < deadline) await Task.Delay(250);
    }

    private void StopHub()
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("python"))
        {
            try
            {
                var command = proc.MainModule?.FileName ?? "";
                if (!ListeningOnHub(proc.Id)) continue;
                proc.Kill();
                Program.Log("hub stopped " + proc.Id + " " + command);
            }
            catch (Exception ex)
            {
                Program.Log("hub stop " + ex.Message);
            }
        }
    }

    private static bool ListeningOnHub(int pid)
    {
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netstat.exe",
            Arguments = "-ano",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var probe = System.Diagnostics.Process.Start(info);
        if (probe is null) return false;
        var text = probe.StandardOutput.ReadToEnd();
        probe.WaitForExit(2000);
        foreach (var line in text.Split('\n'))
        {
            if (line.Contains("127.0.0.1:8107") && line.Contains("LISTENING") && line.TrimEnd().EndsWith(pid.ToString()))
                return true;
        }
        return false;
    }

    private Label notice = null!;

    private void Say(string text)
    {
        notice.Text = text;
        Program.Log("notice " + text);
    }

    private async Task StartAll()
    {
        try
        {
            if (!await HubUp())
            {
                await StartHub();
                if (!await HubUp()) Say("Hub is down. Start did not get a 200 from 127.0.0.1:8107.");
            }
            await RefreshHub();
            if (BuildSessionOpen())
            {
                FocusBuild();
                Say("Build is already open. Focused it.");
            }
            else
            {
                SelectTab(IndexOf("Build"));
                Say("Build tab is selected. Auto-watch is off unless HBC_BUILD_WATCH is set.");
            }
            FocusOrStartBot();
            Program.Log("start all");
        }
        catch (Exception ex)
        {
            Say("Start All failed. " + ex.Message);
        }
    }

    private int IndexOf(string title)
    {
        for (var i = 0; i < tabButtons.Count; i++)
            if (tabButtons[i].Text == title) return i;
        return 0;
    }

    private static bool BuildSessionOpen()
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("WindowsTerminal"))
        {
            var command = CommandLineOf(proc.Id);
            if (command.Contains("--title \"Grok Build\"", StringComparison.Ordinal) && proc.MainWindowHandle != IntPtr.Zero)
                return true;
        }
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("grok"))
        {
            var command = CommandLineOf(proc.Id);
            if (command.Contains("--continue", StringComparison.Ordinal) || command.Contains("--cwd", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void FocusBuild()
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("WindowsTerminal"))
        {
            var command = CommandLineOf(proc.Id);
            if (!command.Contains("--title \"Grok Build\"", StringComparison.Ordinal)) continue;
            if (proc.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(proc.MainWindowHandle, 9);
            SetForegroundWindow(proc.MainWindowHandle);
            return;
        }
    }

    private static void FocusOrStartBuild(string root)
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("WindowsTerminal"))
        {
            try
            {
                var command = CommandLineOf(proc.Id);
                if (!command.Contains("--title \"Grok Build\"", StringComparison.Ordinal)) continue;
                if (proc.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(proc.MainWindowHandle, 9);
                SetForegroundWindow(proc.MainWindowHandle);
                return;
            }
            catch (Exception ex)
            {
                Program.Log("build focus " + ex.Message);
            }
        }
        var link = FindBuildLink(root);
        if (link is not null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
        else
            Program.Log("Grok Build shortcut was not found beside the app or on the Desktop.");
    }

    private static string? FindBuildLink(string root)
    {
        var local = Path.Combine(root, "Grok Build.lnk");
        if (File.Exists(local)) return local;
        var desks = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop"),
        };
        foreach (var desk in desks)
        {
            if (string.IsNullOrEmpty(desk)) continue;
            var link = Path.Combine(desk, "Grok Build.lnk");
            if (File.Exists(link)) return link;
        }
        return null;
    }

    private static string CommandLineOf(int pid)
    {
        var handle = OpenProcess(0x0410, false, pid);
        if (handle == IntPtr.Zero) return "";
        try
        {
            var pbi = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0) return "";
            var pointerBuffer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                if (!ReadProcessMemory(handle, pbi.PebBaseAddress + 0x20, pointerBuffer, IntPtr.Size, out _)) return "";
                var parameters = Marshal.ReadIntPtr(pointerBuffer);
                if (!ReadProcessMemory(handle, parameters + 0x70, pointerBuffer, IntPtr.Size * 2, out _)) return "";
                var length = Marshal.ReadInt16(pointerBuffer);
                var buffer = Marshal.ReadIntPtr(pointerBuffer, IntPtr.Size);
                var bytes = new byte[length];
                var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    if (!ReadProcessMemory(handle, buffer, pinned.AddrOfPinnedObject(), length, out _)) return "";
                }
                finally { pinned.Free(); }
                return System.Text.Encoding.Unicode.GetString(bytes);
            }
            finally { Marshal.FreeHGlobal(pointerBuffer); }
        }
        finally { CloseHandle(handle); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1, PebBaseAddress, Reserved2_0, Reserved2_1, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int length, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, IntPtr buffer, int size, out IntPtr read);

    private static void FocusOrStartBot()
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Grok Bot"))
        {
            if (proc.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(proc.MainWindowHandle, 9);
            SetForegroundWindow(proc.MainWindowHandle);
            return;
        }
        var exe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Grok Bot", "Grok Bot.exe");
        if (!File.Exists(exe))
        {
            Program.Log("Grok Bot.exe was not found");
            return;
        }
        var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized,
        });
        if (started is not null) ShowWindow(started.MainWindowHandle, 6);
    }

    private void LoadSettings()
    {
        autoStartHub = true;
        if (!File.Exists(settingsPath)) return;
        foreach (var line in File.ReadAllLines(settingsPath))
        {
            if (line.StartsWith("auto-start-hub=", StringComparison.Ordinal))
                autoStartHub = line.EndsWith("on", StringComparison.Ordinal);
        }
    }

    private void SaveSettings()
    {
        File.WriteAllText(settingsPath, "auto-start-hub=" + (autoStartHub ? "on" : "off") + Environment.NewLine);
    }

    private static string StartupLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "Startup", "HiveBrainComms.lnk");

    private static bool StartupShortcutExists() => File.Exists(StartupLink);

    private void SetStartup(bool enabled)
    {
        if (enabled)
        {
            var target = Path.Combine(root, "Open-HiveBrainComms-Shell.cmd");
            var script = "$s = New-Object -ComObject WScript.Shell; $l = $s.CreateShortcut('" + StartupLink + "'); $l.TargetPath = '" + target + "'; $l.WorkingDirectory = '" + root + "'; $l.Save()";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else if (File.Exists(StartupLink))
        {
            File.Delete(StartupLink);
        }
        Program.Log("startup " + (enabled ? "on" : "off"));
    }

    private static IntPtr FindWindowByTitle(string title)
    {
        var found = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            var buffer = new System.Text.StringBuilder(512);
            GetWindowText(handle, buffer, buffer.Capacity);
            if (buffer.ToString().Contains(title, StringComparison.Ordinal))
            {
                found = handle;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private async Task BuildTabs()
    {
        await AddWeb("SuperGrok", "https://grok.com/", "supergrok");
        await AddWeb("Imagine", "https://grok.com/imagine", "imagine");
        await AddWeb("X", "https://x.com/", "x");
        await AddWeb("Freebots", "https://freebots.lol/", "freebots");
        await AddWeb("Hub", "http://127.0.0.1:8107/", "lab");
        await AddBuild();
        AddRouting();
        RestoreTab();
    }

    private static async Task<bool> HubUp()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync("http://127.0.0.1:8107/");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RestoreWindow()
    {
        if (!File.Exists(statePath)) return;
        var parts = File.ReadAllText(statePath).Split(',');
        if (parts.Length < 4 || !int.TryParse(parts[0], out var x)) return;
        int.TryParse(parts[1], out var y);
        int.TryParse(parts[2], out var w);
        int.TryParse(parts[3], out var h);
        if (w > 400 && h > 300) { StartPosition = FormStartPosition.Manual; Bounds = new Rectangle(x, y, w, h); }
    }

    private void RestoreTab()
    {
        if (!File.Exists(statePath)) return;
        var parts = File.ReadAllText(statePath).Split(',');
        if (parts.Length > 4 && int.TryParse(parts[4], out var index) && index >= 0 && index < pages.Count)
            SelectTab(index);
        else
            SelectTab(0);
    }

    private void SaveWindow()
    {
        if (WindowState != FormWindowState.Normal) return;
        var b = Bounds;
        File.WriteAllText(statePath, $"{b.X},{b.Y},{b.Width},{b.Height},{activeTab}");
    }

    private void UseDarkTitleBar()
    {
        var preference = 1;
        DwmSetWindowAttribute(Handle, 20, ref preference, sizeof(int));
    }

    private Button MakeTab(string title)
    {
        var button = new Button
        {
            Text = title,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Ink,
            BackColor = Paper,
            AutoSize = true,
            Height = 28,
            Margin = new Padding(4, 0, 4, 0),
            Font = new Font("Segoe UI", 11),
        };
        button.FlatAppearance.BorderSize = 0;
        var index = tabButtons.Count;
        button.Click += (_, _) => SelectTab(index);
        tabButtons.Add(button);
        tabBar.Controls.Add(button);
        return button;
    }

    private void SelectTab(int index)
    {
        activeTab = index;
        for (var i = 0; i < pages.Count; i++) pages[i].Visible = i == index;
        foreach (var button in tabButtons) button.FlatAppearance.BorderSize = 0;
        tabButtons[index].FlatAppearance.BorderColor = Green;
        tabButtons[index].FlatAppearance.BorderSize = 1;
        SaveWindow();
    }

    private async Task AddWeb(string title, string url, string profile)
    {
        MakeTab(title);
        var view = new WebView2 { Dock = DockStyle.Fill };
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Paper, Visible = false };
        page.Controls.Add(view);
        pages.Add(page);
        Controls.Add(page);
        page.BringToFront();
        try
        {
            var folder = Path.Combine(root, "profiles", profile);
            Directory.CreateDirectory(folder);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, folder);
            await view.EnsureCoreWebView2Async(env);
            view.CoreWebView2.NewWindowRequested += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try { await OpenChild(title, env, e); }
                finally { deferral.Complete(); }
            };
            view.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                var current = view.Source;
                if (current is null) return;
                Program.Log("nav " + title + " " + current.Host + current.AbsolutePath);
                if (profile != "x" && current.Host.EndsWith("x.com", StringComparison.OrdinalIgnoreCase)
                    && current.AbsolutePath.StartsWith("/home", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRetry(page, view, url);
                }
            };
            view.Source = new Uri(url);
            Program.Log("loaded " + title + " profile " + profile);
        }
        catch (Exception ex)
        {
            Program.Log("panel " + title + " " + ex.Message);
            page.Controls.Clear();
            page.Controls.Add(new Label { Text = title + " did not load. " + ex.Message, Dock = DockStyle.Fill, ForeColor = Ink });
            Say(title + " did not load. " + ex.Message);
        }
    }

    private async Task AddBuild()
    {
        MakeTab("Build");
        var view = new WebView2 { Dock = DockStyle.Fill };
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Paper, Visible = false };
        var banner = new Label
        {
            Text = "Build auto-watch is off. This tab is a shell; it does not watch the bus until you opt in.",
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Ink,
            BackColor = Panel,
        };
        page.Controls.Add(view);
        page.Controls.Add(banner);
        pages.Add(page);
        Controls.Add(page);
        page.BringToFront();
        try
        {
            var folder = Path.Combine(root, "profiles", "build");
            Directory.CreateDirectory(folder);
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await view.EnsureCoreWebView2Async(env);
            var tokenPath = Path.Combine(root, ".session-token");
            var token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : "";
            if (token.Length == 0) Say("Hub token is missing, so the Build tab cannot open the terminal.");
            view.Source = new Uri("http://127.0.0.1:8107/build.html?token=" + Uri.EscapeDataString(token));
            view.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                banner.Text = e.IsSuccess
                    ? "Build tab is connected. Auto-watch stays off unless HBC_BUILD_WATCH is set."
                    : "Build tab failed to load. The hub may be down.";
                if (!e.IsSuccess) Say("Build tab failed to load.");
            };
        }
        catch (Exception ex)
        {
            Say("Build tab did not load. " + ex.Message);
        }
    }

    private void AddRouting()
    {
        MakeTab("Grok Bot");
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Paper, Visible = false };
        var view = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Paper,
            ForeColor = Ink,
            Font = new Font("Consolas", 10),
        };
        page.Controls.Add(view);
        pages.Add(page);
        Controls.Add(page);
        page.BringToFront();
        void RefreshRoute()
        {
            var busDir = Path.Combine(root, "bus");
            var inbound = Path.Combine(busDir, "GROKBOT-TO-BUILD.md");
            var returns = Path.Combine(busDir, "BUILD-TO-GROKBOT.md");
            view.Text = TailOf(inbound, "routed:") + Environment.NewLine + "---- returns ----" + Environment.NewLine + TailOf(returns, "## ");
        }
        RefreshRoute();
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) => RefreshRoute();
        timer.Start();
    }

    private static string TailOf(string path, string marker)
    {
        if (!File.Exists(path)) return path + " was not found.";
        var lines = File.ReadAllLines(path);
        var start = Math.Max(0, lines.Length - 40);
        var kept = new List<string>();
        for (var i = start; i < lines.Length; i++)
            if (lines[i].Contains(marker) || marker == "## ") kept.Add(lines[i]);
        return string.Join(Environment.NewLine, kept);
    }

    private async Task OpenChild(string title, CoreWebView2Environment env, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var allowed = new[] { "grok.com", "x.com", "accounts.x.ai", "freebots.lol" };
        var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) ? target.Host : "";
        var ok = allowed.Any(name => host == name || host.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
        if (!ok && host.Length > 0)
        {
            e.Handled = true;
            Program.Log("popup blocked " + host);
            return;
        }
        var child = new Form { Text = title + " sign-in", Width = 900, Height = 720, StartPosition = FormStartPosition.CenterParent };
        var popup = new WebView2 { Dock = DockStyle.Fill };
        child.Controls.Add(popup);
        child.Show(this);
        await popup.EnsureCoreWebView2Async(env);
        e.NewWindow = popup.CoreWebView2;
        popup.CoreWebView2.WindowCloseRequested += (_, _) => child.Close();
        Program.Log("popup for " + title);
    }

    private void ShowRetry(Panel page, WebView2 view, string url)
    {
        if (page.Controls.OfType<Panel>().Any(panel => panel.Tag as string == "retry")) return;
        var bar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Green, Tag = "retry" };
        var label = new Label { Text = "Signed in to X. Click Retry to return.", ForeColor = Paper, AutoSize = true, Location = new Point(8, 8) };
        var retry = new Button { Text = "Retry sign-in", Left = 280, Top = 4, FlatStyle = FlatStyle.Flat };
        retry.Click += (_, _) => { page.Controls.Remove(bar); view.Source = new Uri(url); };
        bar.Controls.Add(label);
        bar.Controls.Add(retry);
        page.Controls.Add(bar);
        Program.Log("retry bar shown");
    }

    private delegate bool EnumProc(IntPtr handle, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ShowAbout()
    {
        MessageBox.Show(
            "HiveBrainComms by AdventureNLearn\r\nBuilt on HiveBrainComms",
            "About",
            MessageBoxButtons.OK);
    }

    private void OpenBot()
    {
        var link = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", "Grok Bot.lnk");
        if (File.Exists(link))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
        }
    }
}
