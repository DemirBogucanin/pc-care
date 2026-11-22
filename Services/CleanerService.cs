using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PCCare.Models;

namespace PCCare.Services;

public sealed class CleanerService
{
    private static readonly string WindowsFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string ProgramData =
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public IReadOnlyList<CleanupDefinition> CreateDefinitions()
    {
        var errorReportPaths = new[]
        {
            Path.Combine(LocalAppData, "CrashDumps"),
            Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(WindowsFolder, "MEMORY.DMP"),
            Path.Combine(WindowsFolder, "Minidump")
        };

        return
        [
            new CleanupDefinition
            {
                Id = "user-temp",
                Name = "User temporary files",
                Description = "Temporary data from apps and Windows. Files currently in use are skipped.",
                Glyph = "\uE74D",
                AccentHex = "#5EE0A8",
                Paths = [Path.GetTempPath()],
                Recursive = true
            },
            new CleanupDefinition
            {
                Id = "windows-temp",
                Name = "Windows temporary files",
                Description = "Stale system temporary data. Windows recreates anything it needs.",
                Glyph = "\uE7BA",
                AccentHex = "#6EA8FF",
                Paths = [Path.Combine(WindowsFolder, "Temp")],
                Recursive = true,
                RequiresAdministrator = true
            },
            new CleanupDefinition
            {
                Id = "prefetch",
                Name = "Prefetch traces",
                Description = "Prefetch files older than 7 days. Windows rebuilds them automatically.",
                Glyph = "\uE945",
                AccentHex = "#F2B968",
                Paths = [Path.Combine(WindowsFolder, "Prefetch")],
                Patterns = ["*.pf"],
                Recursive = false,
                MinimumAge = TimeSpan.FromDays(7),
                RequiresAdministrator = true
            },
            new CleanupDefinition
            {
                Id = "thumbnails",
                Name = "Thumbnail & icon cache",
                Description = "Cached previews that Windows regenerates when you browse your files.",
                Glyph = "\uEB9F",
                AccentHex = "#B795FF",
                Paths = [Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer")],
                Patterns = ["thumbcache_*.db", "iconcache_*.db"],
                Recursive = false
            },
            new CleanupDefinition
            {
                Id = "shader-cache",
                Name = "DirectX shader cache",
                Description = "Old compiled shaders. Games and graphics apps rebuild them on demand.",
                Glyph = "\uE7FC",
                AccentHex = "#64D8E8",
                Paths = [Path.Combine(LocalAppData, "D3DSCache")],
                Recursive = true
            },
            new CleanupDefinition
            {
                Id = "error-reports",
                Name = "Error reports & crash dumps",
                Description = "Diagnostic reports and crash dumps that are no longer needed.",
                Glyph = "\uE7BA",
                AccentHex = "#F08A96",
                Paths = errorReportPaths,
                Recursive = true,
                RequiresAdministrator = true
            },
            new CleanupDefinition
            {
                Id = "update-cache",
                Name = "Windows Update cache",
                Description = "Downloaded update packages older than 24 hours. Windows re-downloads them if needed.",
                Glyph = "\uE895",
                AccentHex = "#7FB0FF",
                Paths = [Path.Combine(WindowsFolder, @"SoftwareDistribution\Download")],
                Recursive = true,
                MinimumAge = TimeSpan.FromDays(1),
                RequiresAdministrator = true
            },
            new CleanupDefinition
            {
                Id = "recycle-bin",
                Name = "Recycle Bin",
                Description = "Permanently removes files you already deleted on every fixed drive. They cannot be restored afterwards.",
                Glyph = "\uE74D",
                AccentHex = "#F08A96",
                Paths = [],
                Kind = CleanupKind.RecycleBin,
                SelectedByDefault = false
            },
            new CleanupDefinition
            {
                Id = "delivery-optimization",
                Name = "Delivery Optimization cache",
                Description = "Update packages kept for sharing with other PCs. Windows downloads them again when needed.",
                Glyph = "\uE896",
                AccentHex = "#7FB0FF",
                Paths = [Path.Combine(WindowsFolder, @"SoftwareDistribution\DeliveryOptimization")],
                Recursive = true,
                RequiresAdministrator = true
            },
            new CleanupDefinition
            {
                Id = "search-index",
                Name = "Windows Search index",
                Description = "Deletes the search database so Windows rebuilds a fresh, smaller one. Indexing continues in the background.",
                Glyph = "\uE721",
                AccentHex = "#B795FF",
                Paths = [Path.Combine(ProgramData, @"Microsoft\Search\Data\Applications\Windows")],
                Kind = CleanupKind.SearchIndex,
                SelectedByDefault = false,
                RequiresAdministrator = true
            }
        ];
    }

    public CleanupScanResult ScanCategory(CleanupDefinition definition, CancellationToken cancellationToken = default)
    {
        return definition.Kind switch
        {
            CleanupKind.RecycleBin => ScanRecycleBin(cancellationToken),
            CleanupKind.SearchIndex => ScanSearchIndex(definition, cancellationToken),
            _ => ScanFiles(definition, cancellationToken)
        };
    }

    public CleanupCategoryResult CleanCategory(
        CleanupDefinition definition,
        CleanupScanResult scanned,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return definition.Kind switch
        {
            CleanupKind.RecycleBin => EmptyRecycleBin(scanned, progress, cancellationToken),
            CleanupKind.SearchIndex => RebuildSearchIndex(definition, scanned, progress, cancellationToken),
            _ => CleanFiles(definition, scanned, progress, cancellationToken)
        };
    }

    public bool FlushDns()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "ipconfig.exe"),
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            return process is not null && process.WaitForExit(15_000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static CleanupScanResult ScanFiles(CleanupDefinition definition, CancellationToken cancellationToken)
    {
        var files = CollectCandidates(definition, cancellationToken);
        return new CleanupScanResult(files.Sum(file => file.Length), files.Count);
    }

    private static CleanupCategoryResult CleanFiles(
        CleanupDefinition definition,
        CleanupScanResult scanned,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = CollectCandidates(definition, cancellationToken);
        long freedBytes = 0;
        var deletedCount = 0;
        var total = files.Count;

        for (var index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            try
            {
                var attributes = File.GetAttributes(file.Path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file.Path, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(file.Path);
                freedBytes += file.Length;
                deletedCount++;
            }
            catch
            {
                // Locked or protected files are intentionally left untouched.
            }

            if (index == total - 1 || index % 25 == 0)
            {
                var percent = total == 0 ? 100 : (index + 1) * 100.0 / total;
                progress?.Report(new CleanupProgress(
                    percent,
                    $"Cleaning {definition.Name.ToLowerInvariant()} · {index + 1:N0} / {total:N0}",
                    freedBytes,
                    deletedCount));
            }
        }

        if (definition.Recursive)
        {
            RemoveEmptyDirectories(definition, cancellationToken);
        }

        progress?.Report(new CleanupProgress(100, $"{definition.Name} cleaned", freedBytes, deletedCount));

        var remainingBytes = Math.Max(0, scanned.SizeBytes - freedBytes);
        var remainingCount = Math.Max(0, scanned.ItemCount - deletedCount);
        return new CleanupCategoryResult(freedBytes, deletedCount, remainingBytes, remainingCount);
    }

    private static IReadOnlyList<string> GetFixedDriveRoots()
    {
        var roots = new List<string>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch
            {
                // A drive can disappear while the list is being built.
            }
        }

        return roots;
    }

    private static CleanupScanResult ScanRecycleBin(CancellationToken cancellationToken)
    {
        long size = 0;
        var items = 0;

        foreach (var root in GetFixedDriveRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = QueryRecycleBin(root);
            size += info.SizeBytes;
            items += info.ItemCount;
        }

        return new CleanupScanResult(size, items);
    }

    private static CleanupCategoryResult EmptyRecycleBin(
        CleanupScanResult scanned,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var roots = GetFixedDriveRoots();
        long freedBytes = 0;
        var deletedCount = 0;

        for (var index = 0; index < roots.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[index];
            progress?.Report(new CleanupProgress(
                roots.Count == 0 ? 100 : (index + 1) * 100.0 / roots.Count,
                $"Emptying Recycle Bin on {root.TrimEnd(Path.DirectorySeparatorChar)}...",
                freedBytes,
                deletedCount));

            var before = QueryRecycleBin(root);
            if (before.SizeBytes <= 0 && before.ItemCount <= 0)
            {
                continue;
            }

            try
            {
                // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
                if (SHEmptyRecycleBin(IntPtr.Zero, root, 0x7) == 0)
                {
                    freedBytes += before.SizeBytes;
                    deletedCount += before.ItemCount;
                }
            }
            catch
            {
                // The Recycle Bin is busy; leave it untouched.
            }
        }

        progress?.Report(new CleanupProgress(100, "Recycle Bin emptied", freedBytes, deletedCount));

        var remainingBytes = Math.Max(0, scanned.SizeBytes - freedBytes);
        var remainingCount = Math.Max(0, scanned.ItemCount - deletedCount);
        return new CleanupCategoryResult(freedBytes, deletedCount, remainingBytes, remainingCount);
    }

    private static (long SizeBytes, int ItemCount) QueryRecycleBin(string root)
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(root, ref info) == 0 && info.i64Size > 0)
            {
                return (info.i64Size, (int)Math.Min(int.MaxValue, info.i64NumItems));
            }
        }
        catch
        {
            // No Recycle Bin on this volume or access was denied.
        }

        return (0, 0);
    }

    private static CleanupScanResult ScanSearchIndex(
        CleanupDefinition definition,
        CancellationToken cancellationToken)
    {
        long size = 0;
        var count = 0;

        foreach (var file in GetSearchIndexFiles(definition))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if (info.Exists)
                {
                    size += info.Length;
                    count++;
                }
            }
            catch
            {
                // The index file may be locked or disappear during the scan.
            }
        }

        return new CleanupScanResult(size, count);
    }

    private static CleanupCategoryResult RebuildSearchIndex(
        CleanupDefinition definition,
        CleanupScanResult scanned,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = GetSearchIndexFiles(definition);
        if (files.Count == 0)
        {
            progress?.Report(new CleanupProgress(100, "Search index is already compact"));
            return new CleanupCategoryResult(0, 0, 0, 0);
        }

        progress?.Report(new CleanupProgress(5, "Stopping Windows Search to release the index..."));
        var serviceStopped = false;
        long freedBytes = 0;
        var deletedCount = 0;

        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[index];

                long length;
                try
                {
                    length = new FileInfo(file).Length;
                }
                catch
                {
                    length = 0;
                }

                var removed = TryDeleteFile(file);
                if (!removed && !serviceStopped)
                {
                    serviceStopped = RunServiceCommand("stop WSearch");
                    Thread.Sleep(1500);
                    removed = TryDeleteFile(file);
                }

                if (removed)
                {
                    freedBytes += length;
                    deletedCount++;
                }

                progress?.Report(new CleanupProgress(
                    10 + (index + 1) * 80.0 / files.Count,
                    "Rebuilding the Windows Search index...",
                    freedBytes,
                    deletedCount));
            }
        }
        finally
        {
            if (serviceStopped)
            {
                progress?.Report(new CleanupProgress(95, "Restarting Windows Search...", freedBytes, deletedCount));
                RunServiceCommand("start WSearch");
            }
        }

        progress?.Report(new CleanupProgress(
            100,
            deletedCount > 0
                ? "Search index will rebuild in the background"
                : "Search index could not be released",
            freedBytes,
            deletedCount));

        var remainingBytes = Math.Max(0, scanned.SizeBytes - freedBytes);
        var remainingCount = Math.Max(0, scanned.ItemCount - deletedCount);
        return new CleanupCategoryResult(freedBytes, deletedCount, remainingBytes, remainingCount);
    }

    private static IReadOnlyList<string> GetSearchIndexFiles(CleanupDefinition definition)
    {
        var files = new List<string>();

        foreach (var root in definition.Paths)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "Windows.*"))
                {
                    var extension = Path.GetExtension(file);
                    if (extension.Equals(".edb", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".db", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(file);
                    }
                }
            }
            catch
            {
                // The index folder can be protected while Windows Search runs.
            }
        }

        return files;
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RunServiceCommand(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "net.exe"),
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(120_000))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // The process already exited.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<CandidateFile> CollectCandidates(
        CleanupDefinition definition,
        CancellationToken cancellationToken)
    {
        var result = new List<CandidateFile>();
        var protectedRoots = new List<string>
        {
            NormalizeDirectory(AppContext.BaseDirectory),
            NormalizeDirectory(Path.Combine(Path.GetTempPath(), ".net"))
        };

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                protectedRoots.Add(NormalizeDirectory(processDirectory));
            }
        }

        var minimumDate = definition.MinimumAge > TimeSpan.Zero
            ? DateTime.UtcNow - definition.MinimumAge
            : DateTime.MinValue;

        foreach (var rootPath in definition.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try
            {
                root = NormalizeDirectory(rootPath);
            }
            catch
            {
                continue;
            }

            if (File.Exists(root))
            {
                if (IsSafeFile(root, root, protectedRoots, definition, minimumDate))
                {
                    try
                    {
                        var info = new FileInfo(root);
                        if (info.Exists)
                        {
                            result.Add(new CandidateFile(info.FullName, info.Length));
                        }
                    }
                    catch
                    {
                        // The file may disappear while scanning.
                    }
                }

                continue;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                List<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(directory).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (definition.Recursive)
                        {
                            pending.Push(entry);
                        }

                        continue;
                    }

                    if (!IsSafeFile(entry, root, protectedRoots, definition, minimumDate))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(entry);
                        if (info.Exists)
                        {
                            result.Add(new CandidateFile(info.FullName, info.Length));
                        }
                    }
                    catch
                    {
                        // The file may disappear while scanning. That is safe to ignore.
                    }
                }
            }
        }

        return result;
    }

    private static bool IsSafeFile(
        string path,
        string root,
        IReadOnlyList<string> protectedRoots,
        CleanupDefinition definition,
        DateTime minimumDate)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsWithin(fullPath, root))
            {
                return false;
            }

            if (protectedRoots.Any(protectedRoot => IsWithin(fullPath, protectedRoot)))
            {
                return false;
            }

            var fileName = Path.GetFileName(fullPath);
            if (!definition.Patterns.Any(pattern => WildcardMatch(fileName, pattern)))
            {
                return false;
            }

            return definition.MinimumAge <= TimeSpan.Zero ||
                   File.GetLastWriteTimeUtc(fullPath) <= minimumDate;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveEmptyDirectories(CleanupDefinition definition, CancellationToken cancellationToken)
    {
        foreach (var rootPath in definition.Paths)
        {
            string root;
            try
            {
                root = NormalizeDirectory(rootPath);
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            var directories = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                List<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(directory).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.Directory) != 0 &&
                            (attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            directories.Add(entry);
                            pending.Push(entry);
                        }
                    }
                    catch
                    {
                        // Ignore entries that disappear during cleanup.
                    }
                }
            }

            foreach (var directory in directories.OrderByDescending(path => path.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (Directory.Exists(directory) &&
                        !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, false);
                    }
                }
                catch
                {
                    // An app may still be using the folder.
                }
            }
        }
    }

    private static string NormalizeDirectory(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithin(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        var textIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = 0;

        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length &&
                (char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(text[textIndex]) ||
                 pattern[patternIndex] == '?'))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                matchIndex = textIndex;
                patternIndex++;
            }
            else if (starIndex != -1)
            {
                patternIndex = starIndex + 1;
                matchIndex++;
                textIndex = matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private sealed record CandidateFile(string Path, long Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
