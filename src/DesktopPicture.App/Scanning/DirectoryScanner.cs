using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopPicture.Logging;

namespace DesktopPicture.Scanning;

public sealed class DirectoryScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public static bool IsSupportedImage(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public async Task<List<string>> ScanAsync(
        string rootDirectory,
        Action<string>? onFirstCandidateFound = null,
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<string>(1024);
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                AppLogger.Instance.Warn($"DirectoryScanner: Root directory does not exist: '{rootDirectory}'");
                return results;
            }

            var dirStack = new Stack<string>();
            dirStack.Push(rootDirectory);

            bool reportedFirstCandidate = false;
            int countSinceProgress = 0;

            while (dirStack.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var currentDir = dirStack.Pop();

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if (!dirInfo.Exists) continue;

                    // Skip reparse points (symlinks / junctions) to prevent cycles & out-of-root traversal
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0 && !string.Equals(currentDir, rootDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Enumerate subdirectories
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        try
                        {
                            var subInfo = new DirectoryInfo(subDir);
                            if ((subInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                dirStack.Push(subDir);
                            }
                        }
                        catch
                        {
                            // Skip inaccessible subdirectories
                        }
                    }

                    // Enumerate files
                    foreach (var file in Directory.EnumerateFiles(currentDir))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (IsSupportedImage(file))
                        {
                            results.Add(file);
                            countSinceProgress++;

                            if (!reportedFirstCandidate)
                            {
                                reportedFirstCandidate = true;
                                onFirstCandidateFound?.Invoke(file);
                            }

                            if (countSinceProgress >= 500)
                            {
                                onProgress?.Invoke(results.Count);
                                countSinceProgress = 0;
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Silently ignore permission denied directories per SPEC 7.1
                }
                catch (PathTooLongException)
                {
                    // Ignore path too long
                }
                catch (Exception ex)
                {
                    AppLogger.Instance.Warn($"DirectoryScanner: Skipping unreadable folder '{currentDir}': {ex.Message}");
                }
            }

            onProgress?.Invoke(results.Count);
            AppLogger.Instance.Info($"DirectoryScanner: Completed scanning '{rootDirectory}'. Found {results.Count} candidates.");
            return results;
        }, cancellationToken);
    }
}
