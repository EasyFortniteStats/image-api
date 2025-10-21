using System.Text.Json.Serialization;
using SkiaSharp;

namespace EasyFortniteStats_ImageApi.Models;

public class Shop
{
    public string Date { get; set; }
    public string Title { get; set; }
    public string? CreatorCodeTitle { get; set; }
    public string? CreatorCode { get; set; }
    public string? BackgroundImagePath { get; set; }
    public ShopSection[] Sections { get; set; }
}

public class ShopSection
{
    public string Id { get; set; }
    public string? Name { get; set; }
    public ShopEntry[] Entries { get; set; }
}

public class ShopEntry
{
    public string Id { get; set; }
    public string RegularPrice { get; set; }
    public string FinalPrice { get; set; }
    public ShopEntryBanner? Banner { get; set; }
    public float Size { get; set; }
    public string[]? BackgroundColors { get; set; }
    public string? TextBackgroundColor { get; set; }
    public string Name { get; set; }
    public string? ImageType { get; set; }
    public string? ImageUrl { get; set; }
    public string FallbackImageUrl { get; set; }
    public bool IsSpecial { get; set; }

    [JsonIgnore] public SKBitmap? Image { get; set; }
}

public class ShopEntryBanner
{
    public string Text { get; set; }
    public string[] Colors { get; set; }
}

public class ShopSectionLocationData
{
    public ShopSectionLocationData(string id, ShopLocationDataEntry? name, ShopEntryLocationData[] entries)
    {
        Id = id;
        Name = name;
        Entries = entries;
    }

    public string Id { get; }
    public ShopLocationDataEntry? Name { get; }
    public ShopEntryLocationData[] Entries { get; }
}

public class ShopEntryLocationData(
    string id,
    ShopLocationDataEntry name,
    ShopLocationDataEntry price,
    ShopLocationDataEntry? banner)
{
    public string Id { get; } = id;
    public ShopLocationDataEntry Name { get; } = name;
    public ShopLocationDataEntry Price { get; } = price;
    public ShopLocationDataEntry? Banner { get; } = banner;
}

public class ShopLocationDataEntry
{
    public ShopLocationDataEntry(int x, int y)
    {
        X = x;
        Y = y;
    }

    public ShopLocationDataEntry(int x, int y, int maxWidth)
    {
        X = x;
        Y = y;
        MaxWidth = maxWidth;
    }

    public int X { get; }
    public int Y { get; }
    public int? MaxWidth { get; }
}