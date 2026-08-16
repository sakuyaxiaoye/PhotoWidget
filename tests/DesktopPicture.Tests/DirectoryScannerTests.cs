using System;
using System.IO;
using System.Threading.Tasks;
using DesktopPicture.Scanning;
using Xunit;

namespace DesktopPicture.Tests;

public class DirectoryScannerTests : IDisposable
{
    private readonly string _tempDir;

    public DirectoryScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScannerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Create directory hierarchy with images and non-image files
        var subDir1 = Path.Combine(_tempDir, "Sub1");
        var subDir2 = Path.Combine(_tempDir, "Sub2", "Nested");
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        File.WriteAllText(Path.Combine(_tempDir, "img1.jpg"), "fake_data");
        File.WriteAllText(Path.Combine(_tempDir, "img2.PNG"), "fake_data");
        File.WriteAllText(Path.Combine(_tempDir, "ignore.txt"), "text");
        File.WriteAllText(Path.Combine(subDir1, "img3.webp"), "fake_data");
        File.WriteAllText(Path.Combine(subDir1, "ignore.exe"), "binary");
        File.WriteAllText(Path.Combine(subDir2, "img4.GIF"), "fake_data");
        File.WriteAllText(Path.Combine(subDir2, "img5.jpeg"), "fake_data");
    }

    [Fact]
    public async Task Test_ScanAsync_FindsAllImages_AndCallsFirstCandidate()
    {
        var scanner = new DirectoryScanner();
        string? firstCandidate = null;
        int progressCount = 0;

        var results = await scanner.ScanAsync(
            _tempDir,
            onFirstCandidateFound: first => firstCandidate = first,
            onProgress: count => progressCount = count);

        Assert.Equal(5, results.Count);
        Assert.NotNull(firstCandidate);
        Assert.True(File.Exists(firstCandidate));
        Assert.True(DirectoryScanner.IsSupportedImage(firstCandidate));
    }

    [Fact]
    public void Test_IsSupportedImage_Extensions()
    {
        Assert.True(DirectoryScanner.IsSupportedImage("photo.jpg"));
        Assert.True(DirectoryScanner.IsSupportedImage("photo.JPEG"));
        Assert.True(DirectoryScanner.IsSupportedImage("photo.png"));
        Assert.True(DirectoryScanner.IsSupportedImage("photo.webp"));
        Assert.True(DirectoryScanner.IsSupportedImage("photo.gif"));

        Assert.False(DirectoryScanner.IsSupportedImage("video.mp4"));
        Assert.False(DirectoryScanner.IsSupportedImage("text.txt"));
        Assert.False(DirectoryScanner.IsSupportedImage("photo"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }
}
