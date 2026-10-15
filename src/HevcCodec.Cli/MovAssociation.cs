using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Diagnostics;

internal static class MovAssociation
{
    private const string ShellPath = @"Software\Classes\WMP11.AssocFile.MOV\shell";
    private const string BackupPath = @"Software\UniversalVideoCodecForWindows\MovAssociation";
    private const string Verb = "UniversalVideoCodecPlay";

    internal static void RegisterLauncher(string directory)
    {
        Restore();
        string cli = Path.Combine(directory, "HevcCodec.Cli.exe");
        string launch = $"\"{cli}\" openvideo \"%1\"";
        using RegistryKey progid = Registry.CurrentUser.CreateSubKey(@"Software\Classes\UniversalVideoCodec.MOV");
        progid.SetValue("", "QuickTime video (Universal Video Codec for Windows)");
        progid.SetValue("FriendlyTypeName", "QuickTime video");
        using (RegistryKey details = progid.CreateSubKey("Application"))
        {
            details.SetValue("ApplicationName", "Universal Video Codec for Windows");
            details.SetValue("ApplicationDescription", "Open original MOV video in Windows Media Player Legacy with HEVC support.");
            details.SetValue("ApplicationIcon", $"\"{cli}\",0");
        }
        using (RegistryKey icon = progid.CreateSubKey("DefaultIcon"))
            icon.SetValue("", $"\"{cli}\",0");
        using (RegistryKey command = progid.CreateSubKey(@"shell\open\command")) command.SetValue("", launch);
        using RegistryKey application = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\HevcCodec.Cli.exe");
        application.SetValue("FriendlyAppName", "Universal Video Codec for Windows");
        using (RegistryKey command = application.CreateSubKey(@"shell\open\command")) command.SetValue("", launch);
        using (RegistryKey supported = application.CreateSubKey("SupportedTypes")) supported.SetValue(".mov", "");
        using (RegistryKey openWith = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.mov\OpenWithProgids"))
            openWith.SetValue("UniversalVideoCodec.MOV", Array.Empty<byte>(), RegistryValueKind.None);
        using RegistryKey capabilities = Registry.CurrentUser.CreateSubKey(@"Software\UniversalVideoCodecForWindows\Capabilities");
        capabilities.SetValue("ApplicationName", "Universal Video Codec for Windows");
        capabilities.SetValue("ApplicationDescription", "Open original MOV videos in Windows Media Player Legacy with DirectShow codec support.");
        using (RegistryKey associations = capabilities.CreateSubKey("FileAssociations")) associations.SetValue(".mov", "UniversalVideoCodec.MOV");
        using RegistryKey registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registered.SetValue("Universal Video Codec for Windows", @"Software\UniversalVideoCodecForWindows\Capabilities");
        NotifyAssociationChanged();
    }

    internal static int OpenVideo(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Video not found", fullPath);
        string player = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Windows Media Player\wmplayer.exe");
        Process.Start(new ProcessStartInfo(player) { UseShellExecute = true, ArgumentList = { "/Open", fullPath } });
        return 0;
    }

    internal static void UnregisterLauncher()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\UniversalVideoCodec.MOV", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\HevcCodec.Cli.exe", throwOnMissingSubKey: false);
        using (RegistryKey? openWith = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.mov\OpenWithProgids", writable: true))
            openWith?.DeleteValue("UniversalVideoCodec.MOV", throwOnMissingValue: false);
        using (RegistryKey? registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            registered?.DeleteValue("Universal Video Codec for Windows", throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\UniversalVideoCodecForWindows\Capabilities", throwOnMissingSubKey: false);
        NotifyAssociationChanged();
    }

    internal static void Restore()
    {
        using RegistryKey? shell = Registry.CurrentUser.OpenSubKey(ShellPath, writable: true);
        using RegistryKey? backup = Registry.CurrentUser.OpenSubKey(BackupPath);
        if (shell is null || backup is null) return;
        if (string.Equals(shell.GetValue("") as string, Verb, StringComparison.Ordinal))
        {
            if (backup.GetValue("HadUserDefault") is int had && had != 0)
                shell.SetValue("", backup.GetValue("PreviousUserDefault") as string ?? "");
            else shell.DeleteValue("", throwOnMissingValue: false);
        }
        shell.DeleteSubKeyTree(Verb, throwOnMissingSubKey: false);
        using (RegistryKey? play = shell.OpenSubKey(@"Play\command", writable: true))
        {
            if (play is not null && (play.GetValue("") as string)?.EndsWith(" /Open \"%1\"", StringComparison.Ordinal) == true &&
                play.GetValue("DelegateExecute") as string == "")
            {
                RestoreValue(play, backup, "", "HadPlayCommand", "PreviousPlayCommand");
                RestoreValue(play, backup, "DelegateExecute", "HadPlayDelegate", "PreviousPlayDelegate");
            }
        }
        backup.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(BackupPath, throwOnMissingSubKey: false);
        NotifyAssociationChanged();
    }

    private static void RestoreValue(RegistryKey target, RegistryKey backup, string name, string hadName, string previousName)
    {
        if (backup.GetValue(hadName) is int had && had != 0)
            target.SetValue(name, backup.GetValue(previousName) as string ?? "");
        else target.DeleteValue(name, throwOnMissingValue: false);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    private static void NotifyAssociationChanged() => SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
}
