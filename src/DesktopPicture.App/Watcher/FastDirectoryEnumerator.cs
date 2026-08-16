using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DesktopPicture.Interop;
using DesktopPicture.Logging;
using DesktopPicture.Storage;

namespace DesktopPicture.Watcher;

public sealed record DiscoveredDirectory(
    string FullPath,
    string RelativePath,
    long LastWriteUtcTicks);

public static class FastDirectoryEnumerator
{
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    public static long FileTimeToUtcTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        long fileTime = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        return fileTime + 504911232000000000L;
    }

    public static ImageExtensionType GetExtensionType(string filename)
    {
        int lastDot = filename.LastIndexOf('.');
        if (lastDot < 0 || lastDot >= filename.Length - 1) return ImageExtensionType.Unknown;

        var ext = filename.AsSpan(lastDot);
        if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Jpg;

        if (ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Jpeg;

        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Png;

        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Webp;

        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Gif;

        if (ext.Equals(".avif", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Avif;

        if (ext.Equals(".heic", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Heic;

        if (ext.Equals(".heif", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Heif;

        if (ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Bmp;

        if (ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".tif", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Tiff;

        if (ext.Equals(".jfif", StringComparison.OrdinalIgnoreCase))
            return ImageExtensionType.Jfif;

        return ImageExtensionType.Unknown;
    }

    /// <summary>
    /// Performs an ultra-fast sequential DFS enumeration optimized for mechanical hard drives (zero secondary seeks).
    /// </summary>
    public static void EnumerateImagesAndDirectories(
        string rootPath,
        Action<ImageUpsertEntry> onImageDiscovered,
        Action<DiscoveredDirectory> onDirectoryDiscovered,
        Func<string, long, bool>? shouldSkipDirectory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return;

        var dirStack = new Stack<(string FullPath, string RelPath)>();
        dirStack.Push((rootPath, string.Empty));

        while (dirStack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (currentDir, relDir) = dirStack.Pop();

            string searchPattern = Path.Combine(currentDir, "*");
            IntPtr hFind = NativeMethods.FindFirstFileEx(
                searchPattern,
                NativeMethods.FindExInfoBasic,
                out var findData,
                NativeMethods.FindExSearchNameMatch,
                IntPtr.Zero,
                NativeMethods.FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == INVALID_HANDLE_VALUE)
                continue;

            try
            {
                do
                {
                    ct.ThrowIfCancellationRequested();
                    string name = findData.cFileName;

                    // Skip self and parent pseudo-directories
                    if (name == "." || name == "..")
                        continue;

                    // Skip hidden / system / reparse points (symlinks/junctions to prevent infinite loops)
                    if ((findData.dwFileAttributes & (NativeMethods.FILE_ATTRIBUTE_HIDDEN |
                                                      NativeMethods.FILE_ATTRIBUTE_SYSTEM |
                                                      NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT)) != 0)
                    {
                        continue;
                    }

                    if ((findData.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        // Skip common Windows trash / internal folders
                        if (name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string childFullPath = Path.Combine(currentDir, name);
                        string childRelPath = string.IsNullOrEmpty(relDir) ? name : Path.Combine(relDir, name);
                        long dirLastWriteTicks = FileTimeToUtcTicks(findData.ftLastWriteTime);

                        onDirectoryDiscovered(new DiscoveredDirectory(childFullPath, childRelPath, dirLastWriteTicks));

                        // Check if directory can be pruned based on unchanged timestamp
                        if (shouldSkipDirectory != null && shouldSkipDirectory(childRelPath, dirLastWriteTicks))
                        {
                            continue;
                        }

                        dirStack.Push((childFullPath, childRelPath));
                    }
                    else
                    {
                        var extType = GetExtensionType(name);
                        if (extType == ImageExtensionType.Unknown)
                            continue;

                        long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                        long fileLastWriteTicks = FileTimeToUtcTicks(findData.ftLastWriteTime);
                        string fileRelPath = string.IsNullOrEmpty(relDir) ? name : Path.Combine(relDir, name);

                        onImageDiscovered(new ImageUpsertEntry(
                            fileRelPath,
                            extType,
                            fileSize,
                            fileLastWriteTicks));
                    }
                } while (NativeMethods.FindNextFile(hFind, out findData));
            }
            finally
            {
                NativeMethods.FindClose(hFind);
            }
        }
    }
}
