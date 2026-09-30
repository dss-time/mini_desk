using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MiniDesk.Models;

namespace MiniDesk.Services;

public static class ShellItemActionsService
{
    public static string GetFolder(DesktopItem item)
    {
        // Managed shortcut storage is an implementation detail; show its original desktop folder.
        var path = item.OriginalDesktopPath ?? item.Path;
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null || !Directory.Exists(directory))
            throw new DirectoryNotFoundException(LocalizationService.Current.Get("Message_ItemMissing"));
        return directory;
    }

    public static void OpenFolder(DesktopItem item) => Process.Start(new ProcessStartInfo(GetFolder(item)) { UseShellExecute = true });

    public static void ShowProperties(DesktopItem item)
    {
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
            throw new FileNotFoundException(LocalizationService.Current.Get("Message_ItemMissing"), item.Path);
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x0000000C, Verb = "properties", File = item.Path, Show = 1
        };
        if (!ShellExecuteEx(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        public string? Verb;
        public string? File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public IntPtr Instance;
        public IntPtr IdList;
        public string? Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr Icon;
        public IntPtr Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
}
