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

    public string GetTemplateHash()
    {
        var hash = new HashCode();
        foreach (var section in Sections)
            hash.Add(section.GetTemplateHash());
        return hash.ToHashCode().ToString();
    }

    public string GetLocaleTemplateHash()
    {
        var hash = new HashCode();
        hash.Add(Date);
        foreach (var section in Sections)
            hash.Add(section.GetLocaleTemplateHash());
        return hash.ToHashCode().ToString();
    }
}

public class ShopSection
{
    public string Id { get; set; }
    public string? Name { get; set; }
    public ShopEntry[] Entries { get; set; }

    public int GetTemplateHash()
    {
        var hash = new HashCode();
        hash.Add(Id);
        foreach (var entry in Entries)
            hash.Add(entry.GetTemplateHash());
        return hash.ToHashCode();
    }

    public int GetLocaleTemplateHash()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        foreach (var entry in Entries)
            hash.Add(entry.GetLocaleTemplateHash());
        return hash.ToHashCode();
    }
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

    public int GetTemplateHash()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(RegularPrice);
        hash.Add(FinalPrice);
        hash.Add(Size);
        if (BackgroundColors != null)
        {
            foreach (var color in BackgroundColors)
                hash.Add(color);
        }
        hash.Add(TextBackgroundColor);
        hash.Add(ImageType);
        hash.Add(ImageUrl);
        hash.Add(FallbackImageUrl);
        hash.Add(IsSpecial);
        return hash.ToHashCode();
    }

    public int GetLocaleTemplateHash()
    {
        var hash = new HashCode();
        hash.Add(GetTemplateHash());
        hash.Add(Name);
        return hash.ToHashCode();
    }
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