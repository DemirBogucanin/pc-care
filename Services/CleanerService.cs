using System.IO;
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
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue")
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
            }
        ];
    }

    public CleanupScanResult ScanCategory(CleanupDefinition definition, CancellationToken cancellationToken = default)
    {
        var files = CollectCandidates(definition, cancellationToken);
        return new CleanupScanResult(files.Sum(file => file.Length), files.Count);
    }

    public CleanupCategoryResult CleanCategory(
        CleanupDefinition definition,
        CleanupScanResult scanned,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
}
