using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopPicture.Config;

public sealed class AppSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = false;

    [JsonPropertyName("widgets")]
    public List<WidgetConfig> Widgets { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            SchemaVersion = 1,
            StartWithWindows = false,
            Widgets = new List<WidgetConfig>
            {
                new WidgetConfig
                {
                    Id = Guid.NewGuid().ToString("D"),
                    Name = "照片组件 1",
                    RootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    WidthDip = 480,
                    HeightDip = 270,
                    IntervalSeconds = 60,
                    Paused = false,
                    Visible = true,
                    MonitorId = string.Empty,
                    LeftDip = 40,
                    TopDip = 40,
                    LastShownCatalogId = null
                }
            }
        };
    }
}

public sealed class WidgetConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "照片组件 1";

    [JsonPropertyName("rootPath")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("widthDip")]
    public double WidthDip { get; set; } = 480;

    [JsonPropertyName("heightDip")]
    public double HeightDip { get; set; } = 270;

    [JsonPropertyName("intervalSeconds")]
    public int IntervalSeconds { get; set; } = 60;

    [JsonPropertyName("paused")]
    public bool Paused { get; set; } = false;

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("monitorId")]
    public string MonitorId { get; set; } = string.Empty;

    [JsonPropertyName("leftDip")]
    public double LeftDip { get; set; } = 40;

    [JsonPropertyName("topDip")]
    public double TopDip { get; set; } = 40;

    [JsonPropertyName("lastShownCatalogId")]
    public long? LastShownCatalogId { get; set; }

    [JsonPropertyName("enableCornerRadius")]
    public bool EnableCornerRadius { get; set; } = true;

    [JsonPropertyName("cornerRadius")]
    public int CornerRadius { get; set; } = 16;
}
