using System.Globalization;
using System.IO;
using Microsoft.Win32;
using PCCare.Models;

namespace PCCare.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledFileSuffix = ".pccare-disabled";

    private static readonly string[] AccentPalette =
    [
        "#5EE0A8", "#6EA8FF", "#B795FF", "#F2B968", "#64D8E8", "#F08A96", "#91D36F"
    ];

    private static readonly byte[] ApprovedEnabled =
        [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] ApprovedDisabled =
        [0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    public IReadOnlyList<StartupEntry> LoadStartupEntries()
    {
        var entries = new List<StartupEntry>();

        AddCurrentUserRunEntries(entries);
        AddMachineRunEntries(entries);
        AddStartupFolderEntries(entries);

        return entries
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(entry => entry.IsEnabled)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            if (entry.SourceKind == StartupSourceKind.RegistryRun)
            {
                SetRegistryEntryEnabled(entry, enabled);
            }
            else
            {
                SetFolderEntryEnabled(entry, enabled);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                "Windows denied access to this startup item. Run PC Care as administrator and try again.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "This startup item is in use and could not be changed right now.", ex);
        }
    }

    private static void AddCurrentUserRunEntries(List<StartupEntry> entries)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        AddRunEntries(entries, baseKey, RegistryHive.CurrentUser, false);
    }

    private static void AddMachineRunEntries(List<StartupEntry> entries)
    {
        if (Environment.Is64BitOperatingSystem)
        {
            using (var key64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                AddRunEntries(entries, key64, RegistryHive.LocalMachine, false);
            }

            using var key32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            AddRunEntries(entries, key32, RegistryHive.LocalMachine, true);
        }
        else
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            AddRunEntries(entries, key, RegistryHive.LocalMachine, false);
        }
    }

    private static void AddRunEntries(
        List<StartupEntry> entries,
        RegistryKey baseKey,
        RegistryHive hive,
        bool is32Bit)
    {
        using var runKey = baseKey.OpenSubKey(RunKeyPath);
        if (runKey is null)
        {
            return;
        }

        foreach (var valueName in runKey.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(valueName))
            {
                continue;
            }

            var command = runKey.GetValue(valueName)?.ToString();
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var enabled = ReadApprovedState(baseKey, valueName, is32Bit);
            var userLabel = hive == RegistryHive.CurrentUser ? "Current user" : "All users";
            var architecture = is32Bit ? " · 32-bit" : string.Empty;
            var hiveLabel = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
            var idSuffix = is32Bit ? "32" : "64";

            entries.Add(new StartupEntry
            {
                Id = $"registry:{hiveLabel}:{idSuffix}:{valueName}",
                Name = valueName,
                Command = command.Trim(),
                Location = $"Registry · {userLabel}{architecture}",
                AccentHex = AccentFor(valueName),
                SourceKind = StartupSourceKind.RegistryRun,
                RegistryHive = hiveLabel,
                RegistryKeyPath = RunKeyPath,
                RegistryValueName = valueName,
                Registry32Bit = is32Bit,
                IsEnabled = enabled
            });
        }
    }

    private static bool ReadApprovedState(RegistryKey baseKey, string valueName, bool is32Bit)
    {
        var path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" +
                   (is32Bit ? "Run32" : "Run");

        using var key = baseKey.OpenSubKey(path);
        if (key?.GetValue(valueName) is byte[] data && data.Length > 0)
        {
            return data[0] != 0x03 && data[0] != 0x07;
        }

        return true;
    }

    private static void SetRegistryEntryEnabled(StartupEntry entry, bool enabled)
    {
        var hive = entry.RegistryHive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        var view = entry.Registry32Bit ? RegistryView.Registry32 : RegistryView.Default;
        var approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" +
                           (entry.Registry32Bit ? "Run32" : "Run");

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var approvedKey = baseKey.CreateSubKey(approvedPath, true);
        approvedKey.SetValue(
            entry.RegistryValueName!,
            enabled ? ApprovedEnabled : ApprovedDisabled,
            RegistryValueKind.Binary);
    }

    private static void AddStartupFolderEntries(List<StartupEntry> entries)
    {
        AddStartupFolderEntries(
            entries,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "Current user");

        AddStartupFolderEntries(
            entries,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "All users");
    }

    private static void AddStartupFolderEntries(List<StartupEntry> entries, string folder, string userLabel)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var enabled = !fileName.EndsWith(DisabledFileSuffix, StringComparison.OrdinalIgnoreCase);
            var displayFile = enabled
                ? fileName
                : fileName[..^DisabledFileSuffix.Length];
            var name = Path.GetFileNameWithoutExtension(displayFile);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var command = enabled && displayFile.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                ? ResolveShortcut(path)
                : path;

            entries.Add(new StartupEntry
            {
                Id = $"folder:{path}",
                Name = name,
                Command = command,
                Location = $"Startup folder · {userLabel}",
                AccentHex = AccentFor(name),
                SourceKind = StartupSourceKind.StartupFolder,
                FilePath = path,
                IsEnabled = enabled
            });
        }
    }

    private static void SetFolderEntryEnabled(StartupEntry entry, bool enabled)
    {
        var path = entry.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new IOException("The startup shortcut no longer exists.");
        }

        if (enabled)
        {
            if (!path.EndsWith(DisabledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var original = path[..^DisabledFileSuffix.Length];
            if (File.Exists(original))
            {
                throw new IOException("A startup item with the same name already exists.");
            }

            File.Move(path, original);
        }
        else
        {
            if (path.EndsWith(DisabledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var disabledPath = path + DisabledFileSuffix;
            if (File.Exists(disabledPath))
            {
                throw new IOException("This startup item is already disabled.");
            }

            File.Move(path, disabledPath);
        }
    }

    private static string ResolveShortcut(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return path;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            string target = shortcut.TargetPath as string ?? string.Empty;
            string arguments = shortcut.Arguments as string ?? string.Empty;
            return string.IsNullOrWhiteSpace(target)
                ? path
                : string.IsNullOrWhiteSpace(arguments) ? target : $"{target} {arguments}";
        }
        catch
        {
            return path;
        }
    }

    private static string AccentFor(string name)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(name);
        var index = (int)((uint)hash % (uint)AccentPalette.Length);
        return AccentPalette[index];
    }
}
