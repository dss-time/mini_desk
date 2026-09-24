using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace MiniDesk.Setup;

internal static class Program
{
    private const string AppName = "MiniDesk";
    private static readonly string DefaultInstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    internal static string InstallDir => Environment.GetEnvironmentVariable("MINIDESK_TEST_INSTALL_ROOT") ?? DefaultInstallDir;
    internal static bool TestMode => Environment.GetEnvironmentVariable("MINIDESK_TEST_INSTALL_ROOT") is not null;
    internal static string DataDir => Environment.GetEnvironmentVariable("MINIDESK_TEST_DATA_ROOT") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--silent-install")) { Install(false); return 0; }
            if (args.Contains("--silent-uninstall-keep")) { Uninstall(false); return 0; }
            if (args.Contains("--silent-uninstall-delete")) { Uninstall(true); return 0; }
            if (args.Contains("--uninstall")) Application.Run(new UninstallForm());
            else Application.Run(new InstallForm());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, InstallerStrings.Get("Installer_FailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static void Install(bool startWithWindows)
    {
        Directory.CreateDirectory(InstallDir);
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniDesk.Payload.zip")
            ?? throw new InvalidOperationException(InstallerStrings.Get("Installer_MissingPayload"));
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false))
        {
            var installRoot = Path.GetFullPath(InstallDir) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var destination = Path.GetFullPath(Path.Combine(InstallDir, entry.FullName));
                if (!destination.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid installer payload path.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
        File.Copy(Environment.ProcessPath!, Path.Combine(InstallDir, "Uninstall.exe"), true);

        if (!TestMode)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            Shortcut.Create(Path.Combine(desktop, "MiniDesk.lnk"), Path.Combine(InstallDir, "MiniDesk.exe"));
            Shortcut.Create(Path.Combine(startMenu, "MiniDesk.lnk"), Path.Combine(InstallDir, "MiniDesk.exe"));
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (startWithWindows) run.SetValue(AppName, $"\"{Path.Combine(InstallDir, "MiniDesk.exe")}\" --background");
            else run.DeleteValue(AppName, false);
            using var uninstall = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MiniDesk");
            uninstall.SetValue("DisplayName", AppName);
            uninstall.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1");
            uninstall.SetValue("Publisher", AppName);
            uninstall.SetValue("DisplayIcon", Path.Combine(InstallDir, "MiniDesk.exe"));
            uninstall.SetValue("InstallLocation", InstallDir);
            uninstall.SetValue("UninstallString", $"\"{Path.Combine(InstallDir, "Uninstall.exe")}\" --uninstall");
            uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
        }
    }

    internal static void Uninstall(bool deleteConfiguration)
    {
        foreach (var process in Process.GetProcessesByName("MiniDesk"))
            try { process.CloseMainWindow(); if (!process.WaitForExit(1200)) process.Kill(); } catch { }
        RestoreManagedDesktopItems();
        if (!TestMode)
        {
            SafeDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MiniDesk.lnk"));
            SafeDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MiniDesk.lnk"));
            Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)?.DeleteValue(AppName, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MiniDesk", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\MiniDesk", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\MiniDesk", false);
        }
        if (deleteConfiguration)
        {
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
        }
        var cleanup = Path.Combine(Path.GetTempPath(), $"MiniDesk-cleanup-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(cleanup, $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\nrmdir /s /q \"{InstallDir}\"\r\ndel /q \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cleanup}\"") { CreateNoWindow = true, UseShellExecute = false });
    }

    private static void SafeDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static void RestoreManagedDesktopItems()
    {
        var configPath = Path.Combine(DataDir, "workspace.json");
        if (!File.Exists(configPath)) return;
        var failures = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("Groups", out var groups)) return;
            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("Items", out var items)) continue;
                foreach (var item in items.EnumerateArray())
                {
                    var mode = item.TryGetProperty("VisibilityMode", out var modeValue) ? modeValue.GetString() : null;
                    var path = item.TryGetProperty("Path", out var pathValue) ? pathValue.GetString() : null;
                    try
                    {
                        if (mode == "ManagedShortcut" && !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                            item.TryGetProperty("OriginalDesktopPath", out var originalValue))
                        {
                            var original = originalValue.GetString();
                            if (string.IsNullOrWhiteSpace(original) || File.Exists(original)) throw new IOException("桌面恢复位置已被占用");
                            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                            File.Move(path, original, false);
                            NotifyShell(original);
                        }
                        else if (mode == "HiddenAttribute" && !string.IsNullOrWhiteSpace(path) &&
                                 (File.Exists(path) || Directory.Exists(path)) &&
                                 item.TryGetProperty("OriginalFileAttributes", out var attributes))
                        {
                            File.SetAttributes(path, (FileAttributes)attributes.GetInt32());
                            NotifyShell(path);
                        }
                    }
                    catch { if (!string.IsNullOrWhiteSpace(path)) failures.Add(path); }
                }
            }
        }
        catch (JsonException) { return; }
        if (failures.Count > 0)
            throw new InvalidOperationException(InstallerStrings.Format("Installer_UninstallBlocked", failures.Count));
    }

    private static void NotifyShell(string path) => SHChangeNotify(0x00002000, 0x2005, path, IntPtr.Zero);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
}

internal sealed class InstallForm : Form
{
    private readonly CheckBox _startup = new() { Text = InstallerStrings.Get("Installer_Startup"), AutoSize = true, Left = 42, Top = 188, Checked = true };
    public InstallForm()
    {
        Text = InstallerStrings.Get("Installer_InstallTitle"); Width = 600; Height = 340; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(new Label { Text = "MiniDesk", Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, Left = 40, Top = 38 });
        Controls.Add(new Label { Text = InstallerStrings.Get("Installer_Tagline"), Font = new Font("Segoe UI", 11), ForeColor = Color.DimGray, AutoSize = true, Left = 43, Top = 86 });
        Controls.Add(new Label { Text = InstallerStrings.Format("Installer_LocationFormat", Program.InstallDir), AutoSize = true, Left = 43, Top = 142 });
        Controls.Add(_startup);
        var install = new Button { Text = InstallerStrings.Get("Installer_InstallAction"), Width = 150, Height = 40, Left = 400, Top = 245, BackColor = Color.FromArgb(23, 105, 247), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        install.Click += (_, _) => { Program.Install(_startup.Checked); MessageBox.Show(InstallerStrings.Get("Installer_Installed"), InstallerStrings.Get("Installer_InstalledTitle")); Close(); };
        Controls.Add(install);
    }
}

internal sealed class UninstallForm : Form
{
    private readonly RadioButton _keep = new() { Text = InstallerStrings.Get("Installer_KeepData"), Checked = true, AutoSize = true, Left = 42, Top = 125 };
    private readonly RadioButton _delete = new() { Text = InstallerStrings.Get("Installer_DeleteData"), AutoSize = true, Left = 42, Top = 160 };
    public UninstallForm()
    {
        Text = InstallerStrings.Get("Installer_UninstallTitle"); Width = 600; Height = 310; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(new Label { Text = InstallerStrings.Get("Installer_UninstallTitle"), Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Left = 40, Top = 35 });
        Controls.Add(new Label { Text = InstallerStrings.Get("Installer_UninstallSafety"), AutoSize = true, Left = 43, Top = 82, ForeColor = Color.DimGray });
        Controls.Add(_keep); Controls.Add(_delete);
        var uninstall = new Button { Text = InstallerStrings.Get("Installer_UninstallAction"), Width = 140, Height = 38, Left = 410, Top = 215, BackColor = Color.MistyRose, ForeColor = Color.DarkRed, FlatStyle = FlatStyle.Flat };
        uninstall.Click += (_, _) => { Program.Uninstall(_delete.Checked); MessageBox.Show(InstallerStrings.Get("Installer_Uninstalled"), InstallerStrings.Get("Installer_UninstalledTitle")); Close(); };
        Controls.Add(uninstall);
    }
}

internal static class Shortcut
{
    public static void Create(string shortcutPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var link = (IShellLinkW)(object)new ShellLink();
        link.SetPath(targetPath); link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
        ((IPersistFile)link).Save(shortcutPath, false);
    }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink { }
    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown), System.Runtime.InteropServices.Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int maxPath, IntPtr data, uint flags); void GetIDList(out IntPtr idList); void SetIDList(IntPtr idList); void GetDescription(IntPtr name, int maxName); void SetDescription(string name); void GetWorkingDirectory(IntPtr dir, int maxPath); void SetWorkingDirectory(string dir); void GetArguments(IntPtr args, int maxPath); void SetArguments(string args); void GetHotkey(out short hotkey); void SetHotkey(short hotkey); void GetShowCmd(out int showCmd); void SetShowCmd(int showCmd); void GetIconLocation(IntPtr iconPath, int iconPathLength, out int iconIndex); void SetIconLocation(string iconPath, int iconIndex); void SetRelativePath(string path, uint reserved); void Resolve(IntPtr hwnd, uint flags); void SetPath(string path);
    }
    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown), System.Runtime.InteropServices.Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile { void GetClassID(out Guid classId); void IsDirty(); void Load(string fileName, uint mode); void Save(string fileName, bool remember); void SaveCompleted(string fileName); void GetCurFile(out string fileName); }
}
