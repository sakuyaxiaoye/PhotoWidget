using System;
using System.IO;
using DesktopPicture.Config;
using Xunit;

namespace DesktopPicture.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DesktopPicture_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Test_DefaultSettings_Created_When_NoFileExists()
    {
        using var service = new SettingsService(_tempDir);
        var settings = service.Current;

        Assert.NotNull(settings);
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Single(settings.Widgets);
        Assert.Equal(480, settings.Widgets[0].WidthDip);
        Assert.Equal(270, settings.Widgets[0].HeightDip);
        Assert.True(File.Exists(Path.Combine(_tempDir, "settings.json")));
    }

    [Fact]
    public void Test_Settings_SaveAndReload()
    {
        using (var service = new SettingsService(_tempDir))
        {
            service.Update(s =>
            {
                s.Widgets[0].Name = "测试组件A";
                s.Widgets[0].WidthDip = 640;
                s.Widgets[0].HeightDip = 360;
            }, immediate: true);
        }

        using (var service2 = new SettingsService(_tempDir))
        {
            var reloaded = service2.Current;
            Assert.Equal("测试组件A", reloaded.Widgets[0].Name);
            Assert.Equal(640, reloaded.Widgets[0].WidthDip);
            Assert.Equal(360, reloaded.Widgets[0].HeightDip);
        }
    }

    [Fact]
    public void Test_Backup_Recovery_When_SettingsCorrupted()
    {
        // 1. Save valid settings to generate backup
        using (var service = new SettingsService(_tempDir))
        {
            service.Update(s =>
            {
                s.Widgets[0].Name = "备份正常数据";
            }, immediate: true);
            service.Update(s =>
            {
                s.Widgets[0].Name = "第二轮数据";
            }, immediate: true);
        }

        var mainFile = Path.Combine(_tempDir, "settings.json");
        var backupFile = Path.Combine(_tempDir, "settings.json.bak");
        Assert.True(File.Exists(backupFile));

        // Corrupt main file
        File.WriteAllText(mainFile, "{ invalid_json_syntax: true, corrupted");

        using (var service2 = new SettingsService(_tempDir))
        {
            var reloaded = service2.Current;
            Assert.NotNull(reloaded);
            // Should recover from backup or default
            Assert.NotEmpty(reloaded.Widgets);
        }
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
