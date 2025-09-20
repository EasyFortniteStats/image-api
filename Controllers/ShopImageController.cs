using System.Text;
using System.Text.RegularExpressions;
using AsyncKeyedLock;
using EasyFortniteStats_ImageApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

// ReSharper disable InconsistentNaming
namespace EasyFortniteStats_ImageApi.Controllers;

[ApiController]
[Route("shop")]
public partial class ShopImageController(
    IMemoryCache cache,
    IHttpClientFactory clientFactory,
    AsyncKeyedLocker<string> namedLock,
    SharedAssets assets)
    : ControllerBase
{
    // Constants
    private const int HORIZONTAL_PADDING = 100;
    private const int BOTTOM_PADDING = 100;
    private const int HEADER_HEIGHT = 450;
    private const int COLUMN_SPACE = 100;
    private const int CARDS_PER_SECTION = 4;
    private const int CARD_WIDTH = 256;
    private const int CARD_HEIGHT = 408;
    private const int CARD_SPACE = 24;
    private const int CARD_PADDING = 12;
    private const int SECTION_WIDTH = CARDS_PER_SECTION * CARD_WIDTH + (CARDS_PER_SECTION - 1) * CARD_SPACE;
    private const int SECTION_HEIGHT = CARD_HEIGHT + 57;

    private const float TITLE_FONT_SIZE = 200f;
    private const float DATE_FONT_SIZE = 50f;
    private const float SECTION_NAME_FONT_SIZE = 43f;
    private const float ENTRY_NAME_FONT_SIZE = 27f;
    private const float ENTRY_PRICE_FONT_SIZE = 21f;


    private static readonly MemoryCacheEntryOptions ShopImageCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        PostEvictionCallbacks =
        {
            new PostEvictionCallbackRegistration
            {
                EvictionCallback = ImageUtils.BitmapPostEvictionCallback
            }
        }
    };

    [HttpPost]
    public async Task<IActionResult> Shop([FromBody] Shop shop, [FromQuery] string? locale, [FromQuery] bool? isNewShop)
    {
        locale ??= "en";
        var _isNewShop = isNewShop ?? false;
        Console.WriteLine($"Item Shop image request | Locale = {locale} | New Shop = {_isNewShop}");
        // Hash the section ids
        var templateHash = string.Join('-', shop.Sections.Select(x => x.Id)).GetHashCode().ToString();

        SKBitmap? templateBitmap;
        ShopSectionLocationData[]? locationData;

        using (await namedLock.LockAsync("shop_template").ConfigureAwait(false))
        {
            templateBitmap = cache.Get<SKBitmap?>($"shop_template_bmp_{templateHash}");
            locationData = cache.Get<ShopSectionLocationData[]?>($"shop_location_data_{templateHash}");
            if (_isNewShop || templateBitmap is null)
            {
                await PrefetchImages(shop);
                var templateGenerationResult = await GenerateTemplate(shop);
                templateBitmap = templateGenerationResult.Item2;
                locationData = templateGenerationResult.Item1;
                cache.Set($"shop_template_bmp_{templateHash}", templateBitmap, ShopImageCacheOptions);
                cache.Set($"shop_location_data_{templateHash}", locationData, TimeSpan.FromMinutes(10));
            }
        }

        SKBitmap? localeTemplateBitmap;

        var lockName = $"shop_template_{locale}";
        using (await namedLock.LockAsync(lockName).ConfigureAwait(false))
        {
            localeTemplateBitmap = cache.Get<SKBitmap?>($"shop_template_{locale}_bmp");
            if (_isNewShop || localeTemplateBitmap == null)
            {
                localeTemplateBitmap = await GenerateLocaleTemplate(shop, templateBitmap, locationData!);
                cache.Set($"shop_template_{locale}_bmp", localeTemplateBitmap, ShopImageCacheOptions);
            }
        }

        using var localeTemplateBitmapCopy = localeTemplateBitmap.Copy();
        using var shopImage = await GenerateShopImage(shop, localeTemplateBitmapCopy);
        var data = shopImage.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    [HttpPost("section")]
    public async Task<IActionResult> ShopSection([FromBody] ShopSection section, [FromQuery] string? locale,
        [FromQuery] bool? isNewShop)
    {
        locale ??= "en";
        var _isNewShop = isNewShop ?? false;
        Console.WriteLine($"Item Shop section image request | Locale = {locale} | New Shop = {section.Id}");

        SKBitmap? templateBitmap;
        ShopSectionLocationData? shopSectionLocationData;

        using (await namedLock.LockAsync($"shop_section_template_{section.Id}").ConfigureAwait(false))
        {
            templateBitmap = cache.Get<SKBitmap?>($"shop_section_template_bmp_{section.Id}");
            shopSectionLocationData = cache.Get<ShopSectionLocationData?>($"shop_section_location_data_{section.Id}");
            if (_isNewShop || templateBitmap is null)
            {
                await PrefetchImages([section]);
                var templateGenerationResult = await GenerateSectionTemplate(section);
                templateBitmap = templateGenerationResult.Item2;
                shopSectionLocationData = templateGenerationResult.Item1;
                cache.Set($"shop_section_template_bmp_{section.Id}", templateBitmap, ShopImageCacheOptions);
                cache.Set($"shop_section_location_data_{section.Id}", shopSectionLocationData,
                    TimeSpan.FromMinutes(10));
            }
        }

        SKBitmap? localeTemplateBitmap;

        var lockName = $"shop_section_template_{locale}_{section.Id}";
        using (await namedLock.LockAsync(lockName).ConfigureAwait(false))
        {
            localeTemplateBitmap = cache.Get<SKBitmap?>($"shop_section_template_{locale}_bmp_{section.Id}");
            if (_isNewShop || localeTemplateBitmap == null)
            {
                localeTemplateBitmap =
                    await GenerateSectionLocaleTemplate(section, templateBitmap, shopSectionLocationData!);
                cache.Set($"shop_section_template_{locale}_bmp_{section.Id}", localeTemplateBitmap,
                    ShopImageCacheOptions);
            }
        }

        using var localeTemplateBitmapCopy = localeTemplateBitmap.Copy();
        using var image = await GenerateShopSectionImage(section, localeTemplateBitmapCopy);
        var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    private async Task PrefetchImages(Shop shop)
    {
        await PrefetchImages(shop.Sections);
    }

    private async Task PrefetchImages(IEnumerable<ShopSection> sections)
    {
        var entries = sections.SelectMany(x => x.Entries);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2
        };
        await Parallel.ForEachAsync(entries, options, async (entry, token) =>
        {
            var cacheKey = $"shop_image_{entry.Id}";
            using (await namedLock.LockAsync(cacheKey, token).ConfigureAwait(false))
            {
                var cachedBitmap = cache.Get<SKBitmap?>(cacheKey);
                if (cachedBitmap is not null)
                {
                    entry.Image = cachedBitmap;
                    return;
                }

                using var client = clientFactory.CreateClient();
                var url = entry.ImageUrl ?? entry.FallbackImageUrl;
                SKBitmap bitmap;

                try
                {
                    var imageBytes = await client.GetByteArrayAsync(url, token);
                    bitmap = SKBitmap.Decode(imageBytes);
                }
                catch (Exception)
                {
                    bitmap = new SKBitmap(512, 512);
                }

                entry.Image = bitmap;
                // cache image for 10 minutes & make sure it gets disposed after the period
                cache.Set(cacheKey, bitmap, ShopImageCacheOptions);
            }
        });
    }

    private async Task<SKBitmap> GenerateShopImage(Shop shop, SKBitmap templateBitmap)
    {
        var imageInfo = new SKImageInfo(templateBitmap.Width, templateBitmap.Height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var backgroundBitmap = await assets.GetBitmap("data/images/{0}", shop.BackgroundImagePath); // don't dispose
        if (backgroundBitmap is null)
        {
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.IsDither = true;
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint((float)imageInfo.Width / 2, 0),
                new SKPoint((float)imageInfo.Width / 2, imageInfo.Height),
                [new SKColor(44, 154, 234), new SKColor(14, 53, 147)],
                [0.0f, 1.0f],
                SKShaderTileMode.Repeat);

            canvas.DrawRoundRect(0, 0, imageInfo.Width, imageInfo.Height, 50, 50, paint);
        }
        else
        {
            using var backgroundImagePaint = new SKPaint();
            backgroundImagePaint.IsAntialias = true;

            using var resizedCustomBackgroundBitmap = backgroundBitmap.Resize(imageInfo, SKSamplingOptions.Default);
            backgroundImagePaint.Shader = SKShader.CreateBitmap(resizedCustomBackgroundBitmap, SKShaderTileMode.Clamp,
                SKShaderTileMode.Repeat);

            canvas.DrawRoundRect(0, 0, imageInfo.Width, imageInfo.Height, 50, 50, backgroundImagePaint);
        }

        canvas.DrawBitmap(templateBitmap, 0, 0);

        if (shop is { CreatorCode: not null, CreatorCodeTitle: not null })
        {
            using var shopTitlePaint = new SKPaint();
            using var shopTitleFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-86Bold.otf"), TITLE_FONT_SIZE);
            shopTitlePaint.IsAntialias = true;

            var shopTitleWidth = shopTitleFont.MeasureText(shop.Title);

            var maxBoxWidth = imageInfo.Width - 3 * HORIZONTAL_PADDING - shopTitleWidth;
            using var creatorCodeBoxBitmap =
                await GenerateCreatorCodeBox(shop.CreatorCodeTitle, shop.CreatorCode, maxBoxWidth);
            canvas.DrawBitmap(creatorCodeBoxBitmap, imageInfo.Width - 100 - creatorCodeBoxBitmap.Width, 100);

            var adBannerBitmap = await assets.GetBitmap("Assets/Images/Shop/ad_banner.png"); // don't dispose
            canvas.DrawBitmap(adBannerBitmap, imageInfo.Width - 100 - 50 - adBannerBitmap!.Width,
                100 - adBannerBitmap.Height / 2f);
        }

        return bitmap;
    }

    private async Task<SKBitmap> GenerateLocaleTemplate(Shop shop, SKBitmap templateBitmap,
        IEnumerable<ShopSectionLocationData> shopSectionLocationData)
    {
        var imageInfo = new SKImageInfo(templateBitmap.Width, templateBitmap.Height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

        // Drawing the shop title
        using var shopTitlePaint = new SKPaint();
        using var shopTitleFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-86Bold.otf"), TITLE_FONT_SIZE);
        shopTitlePaint.IsAntialias = true;
        shopTitlePaint.Color = SKColors.White;

        var shopTitleWidth = shopTitleFont.MeasureText(shop.Title);
        canvas.DrawText(shop.Title, 100, 50 - shopTitleFont.Metrics.Ascent, shopTitleFont, shopTitlePaint);

        // Drawing the date
        using var datePaint = new SKPaint();
        using var dateFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-86BoldItalic.otf"), DATE_FONT_SIZE);
        datePaint.IsAntialias = true;
        datePaint.Color = SKColors.White;
        var datePoint = new SKPoint(
            Math.Max(HORIZONTAL_PADDING + shopTitleWidth / 2f, HORIZONTAL_PADDING + dateFont.MeasureText(shop.Date) / 2),
            313 - dateFont.Metrics.Ascent);
        canvas.DrawText(shop.Date, datePoint, SKTextAlign.Center, dateFont, datePaint);

        foreach (var sectionLocationData in shopSectionLocationData)
        {
            var shopSection = shop.Sections.FirstOrDefault(x => x.Id == sectionLocationData.Id);

            // Draw the section name if it exists
            if (sectionLocationData.Name != null)
            {
                using var sectionNamePaint = new SKPaint();
                using var sectionNameFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-86BoldItalic.otf"), SECTION_NAME_FONT_SIZE);
                sectionNamePaint.IsAntialias = true;
                sectionNamePaint.Color = SKColors.White;

                var sectionNamePoint = new SKPoint(sectionLocationData.Name.X,
                    sectionLocationData.Name.Y - sectionNameFont.Metrics.Ascent);
                canvas.DrawText(shopSection?.Name, sectionNamePoint, sectionNameFont, sectionNamePaint);
            }

            foreach (var entryLocationData in sectionLocationData.Entries)
            {
                var shopEntry = shopSection?.Entries?.FirstOrDefault(x => x.Id == entryLocationData.Id);
                if (shopEntry is null)
                    continue;

                using var entryNamePaint = new SKPaint();
                using var entryNameFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-75Medium.otf"), ENTRY_NAME_FONT_SIZE);
                entryNamePaint.Color = SKColors.White;

                SKRect entryNameTextBounds;
                var nameLines = SplitNameText(shopEntry.Name, entryLocationData.Name.MaxWidth ?? 0, entryNameFont, entryNamePaint); 
                if (nameLines.Length > 1)
                {
                    entryNameFont.MeasureText(nameLines[0], out entryNameTextBounds, entryNamePaint); 
                    canvas.DrawText(nameLines[0], entryLocationData.Name.X, entryLocationData.Name.Y + entryNameTextBounds.Height - 33, entryNameFont, entryNamePaint);
                }

                entryNameFont.MeasureText(nameLines.Last(), out entryNameTextBounds, entryNamePaint); 
                canvas.DrawText(nameLines.Last(), entryLocationData.Name.X, entryLocationData.Name.Y + entryNameTextBounds.Height, entryNameFont, entryNamePaint);

                // Draw the shop entry price
                using var pricePaint = new SKPaint();
                using var priceFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-75Medium.otf"), ENTRY_PRICE_FONT_SIZE);
                pricePaint.IsAntialias = true;
                pricePaint.Color = SKColors.White;

                var priceTextWidth = priceFont.MeasureText(shopEntry.FinalPrice);
                var pricePoint = new SKPoint(entryLocationData.Price.X,
                    entryLocationData.Price.Y - priceFont.Metrics.Descent);
                canvas.DrawText(shopEntry.FinalPrice, pricePoint, priceFont, pricePaint);

                // Draw strikeout old price if item is discounted
                if (shopEntry.FinalPrice != shopEntry.RegularPrice)
                {
                    using var oldPricePaint = new SKPaint();
                    using var oldPriceFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-75Medium.otf"), ENTRY_PRICE_FONT_SIZE);
                    oldPricePaint.IsAntialias = true;
                    oldPricePaint.Color = SKColors.White.WithAlpha((int)(.6 * 255));

                    var oldPriceTextWidth = oldPriceFont.MeasureText(shopEntry.RegularPrice);
                    var oldPricePoint = new SKPoint(entryLocationData.Price.X + priceTextWidth + 9,
                        entryLocationData.Price.Y - priceFont.Metrics.Descent);
                    canvas.DrawText(shopEntry.RegularPrice, oldPricePoint, oldPriceFont, oldPricePaint);

                    // Draw the strikeout line
                    using var strikePaint = new SKPaint();
                    strikePaint.IsAntialias = true;
                    strikePaint.StrokeWidth = 2f;
                    strikePaint.Color = SKColors.White.WithAlpha((int)(.6 * 255));

                    var strikeStart = new SKPoint(oldPricePoint.X - 4, oldPricePoint.Y - 9);
                    var strikeEnd = new SKPoint(oldPricePoint.X + oldPriceTextWidth + 2, oldPricePoint.Y - 6);
                    canvas.DrawLine(strikeStart, strikeEnd, strikePaint);
                }

                if (shopEntry.Banner != null)
                {
                    using var bannerBitmap = await GenerateBanner(shopEntry.Banner.Text, shopEntry.Banner.Colors,
                        (int)entryLocationData.Banner!.MaxWidth!);
                    canvas.DrawBitmap(bannerBitmap, entryLocationData.Banner!.X, entryLocationData.Banner.Y);
                }
            }
        }

        return bitmap;
    }

    private async Task<(ShopSectionLocationData[], SKBitmap)> GenerateTemplate(Shop shop)
    {
        var columnCount = 2;
        var bestAspectRatioDiff = float.MaxValue;
        int width = 0, height = 0, sectionsPerColumn = 0;
        for (var curColumnCount = columnCount; curColumnCount <= 15; curColumnCount++)
        {
            var curWidth = HORIZONTAL_PADDING * 2 + curColumnCount * SECTION_WIDTH +
                           (curColumnCount - 1) * COLUMN_SPACE;
            var curSectionsPerColumn = (int)Math.Ceiling((double)shop.Sections.Length / curColumnCount);
            var curHeight = HEADER_HEIGHT + curSectionsPerColumn * SECTION_HEIGHT +
                            (curSectionsPerColumn - 1) * CARD_SPACE + BOTTOM_PADDING;

            // The goal is reaching a 1:1 aspect ratio
            var aspectRatio = (float)curWidth / curHeight;
            var aspectRatioDiff = Math.Abs(aspectRatio - 1);
            if (aspectRatioDiff >= bestAspectRatioDiff) break;

            width = curWidth;
            height = curHeight;
            sectionsPerColumn = curSectionsPerColumn;
            bestAspectRatioDiff = aspectRatioDiff;
            columnCount = curColumnCount;
        }

        var imageInfo = new SKImageInfo(width, height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var shopLocationData = new ShopSectionLocationData[shop.Sections.Length];
        var iSec = 0;
        for (var i = 0; i < columnCount; i++)
        {
            var sections = shop.Sections.Skip(i * sectionsPerColumn).Take(sectionsPerColumn).ToList();
            for (var j = 0; j < sections.Count; j++)
            {
                var section = sections[j];
                var sectionImageInfo = new SKImageInfo(
                    SECTION_WIDTH, SECTION_HEIGHT);
                using var sectionBitmap = new SKBitmap(sectionImageInfo);
                using var sectionCanvas = new SKCanvas(sectionBitmap);

                var sectionX = HORIZONTAL_PADDING + i * SECTION_WIDTH + i * COLUMN_SPACE;
                var sectionY = HEADER_HEIGHT + j * SECTION_HEIGHT + j * CARD_SPACE;

                var position = 0f;
                var shopEntryData = new List<ShopEntryLocationData>();
                foreach (var entry in section.Entries)
                {
                    // If the next card is full height, we can't fit it in the current column
                    if (!MathF.Floor(position).Equals(position) && entry.Size >= 1) position = MathF.Ceiling(position);
                    var entryX = (int)position * CARD_WIDTH + (int)position * CARD_SPACE;
                    var entryY = SECTION_HEIGHT - CARD_HEIGHT +
                                 (MathF.Floor(position).Equals(position) ? 0 : (CARD_HEIGHT + CARD_SPACE) / 2);
                    position += entry.Size;

                    using var itemCardBitmap = await GenerateItemCard(entry);
                    using var itemCardPaint = new SKPaint();
                    itemCardPaint.IsAntialias = true;
                    itemCardPaint.Shader = SKShader.CreateBitmap(itemCardBitmap, SKShaderTileMode.Clamp,
                        SKShaderTileMode.Clamp, SKMatrix.CreateTranslation(entryX, entryY));
                    sectionCanvas.DrawRoundRect(entryX, entryY, itemCardBitmap.Width, itemCardBitmap.Height, 20, 20,
                        itemCardPaint);

                    var nameLocationData = new ShopLocationDataEntry(sectionX + entryX + 13,
                        sectionY + entryY + itemCardBitmap.Height - 72, itemCardBitmap.Width - 2 * CARD_PADDING);
                    var priceLocationData = new ShopLocationDataEntry(sectionX + entryX + 13 + 22 + 8,
                        sectionY + entryY + itemCardBitmap.Height - 8);
                    ShopLocationDataEntry? bannerLocationData = null;
                    if (entry.Banner != null)
                        bannerLocationData = new ShopLocationDataEntry(sectionX + entryX + 8, sectionY + entryY + 8,
                            itemCardBitmap.Width - 2 * 8);
                    shopEntryData.Add(new ShopEntryLocationData(entry.Id, nameLocationData, priceLocationData,
                        bannerLocationData));
                }

                ShopLocationDataEntry? sectionNameLocationData = null;
                if (section.Name != null)
                    sectionNameLocationData = new ShopLocationDataEntry(sectionX, sectionY);
                shopLocationData[iSec] =
                    new ShopSectionLocationData(section.Id, sectionNameLocationData, shopEntryData.ToArray());

                canvas.DrawBitmap(sectionBitmap, new SKPoint(sectionX, sectionY));
                iSec++;
            }
        }

        return (shopLocationData, bitmap);
    }

    private async Task<SKBitmap> GenerateCreatorCodeBox(string creatorCodeTitle, string creatorCode, float maxWidth)
    {
        creatorCodeTitle = $" {creatorCodeTitle} · ";
        creatorCode = $"{creatorCode} ";

        using var creatorCodeTitlePaint = new SKPaint();
        using var creatorCodeTitleFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-76Bold.otf"), 100f);
        creatorCodeTitlePaint.IsAntialias = true;
        creatorCodeTitlePaint.Color = SKColors.Black;

        using var creatorCodePaint = new SKPaint();
        using var creatorCodeFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-76Bold.otf"), 100f);
        creatorCodePaint.IsAntialias = true;
        creatorCodePaint.Color = new SKColor(178, 165, 255);

        float width = creatorCodeTitleFont.MeasureText(creatorCodeTitle) + creatorCodeTitleFont.MeasureText(creatorCode), height = 150f;
        while (width > maxWidth)
        {
            creatorCodeTitleFont.Size--;
            creatorCodeFont.Size--;
            width = creatorCodeTitleFont.MeasureText(creatorCodeTitle) + creatorCodeFont.MeasureText(creatorCode);
            height--;
        }

        var imageInfo = new SKImageInfo((int)width, (int)height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        using var boxPaint = new SKPaint();
        boxPaint.IsAntialias = true;
        boxPaint.Color = SKColors.White;
        boxPaint.Style = SKPaintStyle.Fill;
        canvas.DrawRoundRect(new SKRect(0, 0, imageInfo.Width, imageInfo.Height), 100, 100, boxPaint);

        var y = (imageInfo.Height - creatorCodeFont.Spacing) / 2 - creatorCodeFont.Metrics.Ascent;

        canvas.DrawText(creatorCodeTitle, 0, y, creatorCodeTitleFont, creatorCodeTitlePaint);
        canvas.DrawText(creatorCode, imageInfo.Width, y, SKTextAlign.Right, creatorCodeFont, creatorCodePaint);

        return bitmap;
    }

    private async Task<SKBitmap> GenerateBanner(string text, IReadOnlyList<string> colors, int maxWidth)
    {
        using var bannerPaint = new SKPaint();
        using var bannerFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-76BoldItalic.otf"), 17.0f);
        bannerPaint.IsAntialias = true;
        bannerPaint.Color = SKColor.Parse(colors[1]);

        bannerFont.MeasureText(text, out var textBounds, bannerPaint);
        var maxTextWidth = maxWidth - 2 * 13;

        var imageInfo = new SKImageInfo(Math.Min(2 * 13 + (int)textBounds.Width, maxWidth), 34);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        using var backgroundPaint = new SKPaint();
        backgroundPaint.IsAntialias = true;
        backgroundPaint.Color = SKColor.Parse(colors[0]);
        backgroundPaint.Style = SKPaintStyle.Fill;

        canvas.DrawRoundRect(new SKRect(0, 0, imageInfo.Width, imageInfo.Height), 20, 20, backgroundPaint);

        if (textBounds.Width > maxTextWidth)
        {
            while (textBounds.Width > maxTextWidth)
            {
                text = text.Remove(text.Length - 1, 1);
                bannerFont.MeasureText(text + "...", out textBounds);
            }

            text += "...";
        }


        // 6 + textBounds.Top
        canvas.DrawText(text, 13, (float)imageInfo.Height / 2 - textBounds.MidY, bannerFont, bannerPaint);

        return bitmap;
    }

    private async Task<SKBitmap> GenerateItemCard(ShopEntry shopEntry)
    {
        var imageInfo = new SKImageInfo(
            (int)Math.Ceiling(shopEntry.Size) * CARD_WIDTH + ((int)Math.Ceiling(shopEntry.Size) - 1) * CARD_SPACE,
            Math.Floor(shopEntry.Size).Equals(shopEntry.Size) ? CARD_HEIGHT : CARD_HEIGHT / 2 - CARD_SPACE / 2);
        var bitmap = new SKBitmap(imageInfo);

        if (shopEntry.Image is null)
            return bitmap;

        using var canvas = new SKCanvas(bitmap);

        if (shopEntry.BackgroundColors != null)
        {
            using var backgroundGradientPaint = new SKPaint();
            backgroundGradientPaint.IsAntialias = true;
            backgroundGradientPaint.IsDither = true;
            switch (shopEntry.BackgroundColors.Length)
            {
                case 1:
                    backgroundGradientPaint.Color = ImageUtils.ParseColor(shopEntry.BackgroundColors[0]);
                    break;
                case 2:
                    backgroundGradientPaint.Shader = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(0, imageInfo.Height),
                        [
                            ImageUtils.ParseColor(shopEntry.BackgroundColors[0]),
                            ImageUtils.ParseColor(shopEntry.BackgroundColors[1])
                        ],
                        [0.0f, 1.0f],
                        SKShaderTileMode.Clamp);
                    break;
                case 3:
                    backgroundGradientPaint.Shader = SKShader.CreateLinearGradient(
                        new SKPoint(0, 0),
                        new SKPoint(0, imageInfo.Height),
                        [
                            ImageUtils.ParseColor(shopEntry.BackgroundColors[0]),
                            ImageUtils.ParseColor(shopEntry.BackgroundColors[2]), // maybe fix this order in payload?
                            ImageUtils.ParseColor(shopEntry.BackgroundColors[1]),
                        ],
                        [0.0f, 0.5f, 1.0f],
                        SKShaderTileMode.Clamp);
                    break;
            }
            canvas.DrawRect(0, 0, imageInfo.Width, imageInfo.Height, backgroundGradientPaint);
        }
        else if (shopEntry.ImageType == "track")
        {
            using var backgroundPaint = new SKPaint();
            backgroundPaint.Color = SKColors.Black.WithAlpha((int)(.3f * 255));
            canvas.DrawRect(0, 0, imageInfo.Width, imageInfo.Height, backgroundPaint);
        }
        else if (shopEntry.ImageUrl == null)
        {
            // Draw radial gradient and paste resizedImageBitmap on it
            using var gradientPaint = new SKPaint();
            gradientPaint.IsAntialias = true;
            gradientPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(imageInfo.Rect.MidX, imageInfo.Rect.MidY),
                MathF.Sqrt(MathF.Pow(imageInfo.Rect.MidX, 2) + MathF.Pow(imageInfo.Rect.MidY, 2)),
                [new SKColor(129, 207, 250), new SKColor(52, 136, 217)],
                SKShaderTileMode.Clamp);

            canvas.DrawRect(0, 0, imageInfo.Width, imageInfo.Height, gradientPaint);
        }

        // Scale image down to fit the card
        if (shopEntry is { ImageType: "track", ImageUrl: null })
        {
            using var coverBitmap = shopEntry.Image.Resize(new SKImageInfo(236, 236), SKSamplingOptions.Default);

            using var roundedCoverBitmap = new SKBitmap(236, 236);
            using var roundedCoverCanvas = new SKCanvas(roundedCoverBitmap);
            roundedCoverCanvas.ClipRoundRect(
                new SKRoundRect(new SKRect(0, 0, coverBitmap.Width, coverBitmap.Height), 10), antialias: true);
            roundedCoverCanvas.DrawBitmap(coverBitmap, 0, 0);

            canvas.DrawBitmap(roundedCoverBitmap, 10, 10);
        }
        else
        {
            int resizeWidth, resizeHeight;
            var aspectRatio = shopEntry.Image.Width / shopEntry.Image.Height;

            if (imageInfo.Width > imageInfo.Height)
            {
                resizeWidth = imageInfo.Width;
                resizeHeight = imageInfo.Width / aspectRatio;
            }
            else
            {
                resizeWidth = imageInfo.Height * aspectRatio;
                resizeHeight = imageInfo.Height;
            }

            using var resizedImageBitmap =
                shopEntry.Image.Resize(new SKImageInfo(resizeWidth, resizeHeight), SKSamplingOptions.Default);

            // Car bundles get centered in the middle of the card vertically
            if (shopEntry.ImageType == "car-bundle")
            {
                var cropY = (resizedImageBitmap.Height - imageInfo.Height) / 2;
                var cropRect = new SKRect(0, cropY, resizedImageBitmap.Width, cropY + imageInfo.Height);
                canvas.DrawBitmap(resizedImageBitmap, cropRect,
                    new SKRect(0, 0, resizedImageBitmap.Width, imageInfo.Height));
            }
            // Center image in the middle of the card, if width is bigger than the image
            else if (resizedImageBitmap.Width > imageInfo.Width)
            {
                var cropX = (resizedImageBitmap.Width - imageInfo.Width) / 2;
                var cropRect = new SKRect(cropX, 0, cropX + imageInfo.Width, resizedImageBitmap.Height);
                canvas.DrawBitmap(resizedImageBitmap, cropRect,
                    new SKRect(0, 0, imageInfo.Width, resizedImageBitmap.Height));
            }
            else
            {
                var offsetMulti = shopEntry.Size >= 3f ? 0.08f : 0f;
                canvas.DrawBitmap(resizedImageBitmap, new SKPoint(0, resizedImageBitmap.Height * -offsetMulti));
            }
        }


        if (shopEntry.TextBackgroundColor != null)
        {
            var textBackgroundColor = ImageUtils.ParseColor(shopEntry.TextBackgroundColor);
            using var shadowPaint = new SKPaint();
            shadowPaint.IsAntialias = true;
            shadowPaint.IsDither = true;
            shadowPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(imageInfo.Width / 2f, imageInfo.Height),
                new SKPoint(imageInfo.Width / 2f, imageInfo.Height * .7f),
                [
                    textBackgroundColor,
                    textBackgroundColor.WithAlpha(0)
                ],
                [0.0f, 1.0f],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(imageInfo.Rect, shadowPaint);
        }
        else if (shopEntry.ImageType == "track")
        {
            using var shadowPaint = new SKPaint();
            shadowPaint.IsAntialias = true;
            shadowPaint.IsDither = true;
            shadowPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(imageInfo.Width / 2f, imageInfo.Height),
                new SKPoint(imageInfo.Width / 2f, imageInfo.Height * .6f),
                [
                    SKColors.Black.WithAlpha((int)(.8 * 255)),
                    SKColors.Black.WithAlpha(0)
                ],
                [0.0f, 1.0f],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(imageInfo.Rect, shadowPaint);
        }

        // Draw V-Bucks icon
        var vbucksBitmap = await assets.GetBitmap("Assets/Images/Shop/vbucks_icon.png"); // don't dispose
        canvas.DrawBitmap(vbucksBitmap, 13, imageInfo.Height - vbucksBitmap!.Height - 11);

        if (shopEntry.IsSpecial)
        {
            using var paint = new SKPaint();
            using var font = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-74Regular.otf"), 35.0f);
            paint.IsAntialias = true;
            paint.Color = SKColors.White;

            canvas.DrawText("+", imageInfo.Width - 18, imageInfo.Height - font.Metrics.Descent + 3, SKTextAlign.Right, font, paint);
        }

        return bitmap;
    }

    private static string[] SplitNameText(string text, int maxWidth, SKFont font, SKPaint paint)
    {
        var regex = NameSplitRegex();
        var matches = regex.Matches(text);
        
        var currentLine = 0;
        var lines = new StringBuilder[] { new(), new() };
        foreach (Match match in matches)
        {
            var line = lines[currentLine];
            font.MeasureText(line + match.Value, out var bounds, paint);
            if (bounds.Width > maxWidth) currentLine++;
            if (currentLine >= 2)
            {
                lines[1].Append(match.Value);
                break;
            }

            lines[currentLine].Append(match.Value);
        }

        // Adjust lines that are too long and add ellipsis
        foreach (var line in lines)
        {
            font.MeasureText(line.ToString(), out var textBounds, paint);
            if (textBounds.Width <= maxWidth) continue;

            while (textBounds.Width > maxWidth)
            {
                line.Remove(line.Length - 1, 1);
                font.MeasureText(line + "...", out textBounds);
            }

            line.Append("...");
        }

        // Return not empty lines
        return lines.Select(x => x.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
    }

    [GeneratedRegex("([a-z0-9]+|[^a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex NameSplitRegex();

    private async Task<SKBitmap> GenerateShopSectionImage(ShopSection section, SKBitmap templateBitmap)
    {
        return null;
    }

    private async Task<SKBitmap> GenerateSectionLocaleTemplate(ShopSection section, SKBitmap templateBitmap,
        ShopSectionLocationData sectionLocationData)
    {
        return null;
    }

    private async Task<(ShopSectionLocationData, SKBitmap)> GenerateSectionTemplate(ShopSection section)
    {
        return (null, null);
    }
}