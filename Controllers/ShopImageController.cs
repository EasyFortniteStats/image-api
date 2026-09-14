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
    SharedAssets assets,
    ILogger<ShopImageController> logger)
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
    public async Task<IActionResult> Shop([FromBody] Shop shop, [FromQuery] bool? forceNew, CancellationToken cancellationToken)
    {
        var _forceNew = forceNew ?? false;
        logger.LogInformation("Item Shop image request received");
        var templateHash = shop.GetTemplateHash();
        var localeTemplateHash = shop.GetLocaleTemplateHash();

        SKBitmap templateBitmapCopy;
        ShopSectionLocationData[]? locationData;
        using (await namedLock.LockAsync($"shop_template_{templateHash}", cancellationToken).ConfigureAwait(false))
        {
            logger.LogDebug("Acquired shop template lock");
            var templateBitmap = cache.Get<SKBitmap?>($"shop_template_bmp_{templateHash}");
            locationData = cache.Get<ShopSectionLocationData[]?>($"shop_location_data_{templateHash}");
            if (_forceNew || templateBitmap is null)
            {
                logger.LogDebug("Generating new shop template");
                await PrefetchImages(shop, cancellationToken);
                var templateGenerationResult = await GenerateTemplate(shop);
                templateBitmap = templateGenerationResult.Item2;
                locationData = templateGenerationResult.Item1;
                cache.Set($"shop_template_bmp_{templateHash}", templateBitmap, ShopImageCacheOptions);
                cache.Set($"shop_location_data_{templateHash}", locationData, TimeSpan.FromMinutes(10));
            }
            templateBitmapCopy = templateBitmap.Copy();
            logger.LogDebug("Releasing shop template lock");
        }

        using (templateBitmapCopy)
        {
            SKBitmap localeTemplateBitmapCopy;
            using (await namedLock.LockAsync($"shop_template_{localeTemplateHash}", cancellationToken).ConfigureAwait(false))
            {
                logger.LogDebug("Acquired locale shop template lock");
                var localeTemplateBitmap = cache.Get<SKBitmap?>($"shop_template_{localeTemplateHash}_bmp");
                if (_forceNew || localeTemplateBitmap == null)
                {
                    logger.LogDebug("Generating new locale shop template");
                    localeTemplateBitmap = await GenerateLocaleTemplate(shop, templateBitmapCopy, locationData!);
                    cache.Set($"shop_template_{localeTemplateHash}_bmp", localeTemplateBitmap, ShopImageCacheOptions);
                }
                localeTemplateBitmapCopy = localeTemplateBitmap.Copy();
                logger.LogDebug("Releasing locale shop template lock");
            }

            logger.LogDebug("Generating final shop image");
            using var localeCopy = localeTemplateBitmapCopy;
            using var shopImage = await GenerateShopImage(shop, localeCopy);
            var data = shopImage.Encode(SKEncodedImageFormat.Png, 100);
            return File(data.AsStream(true), "image/png");
        }
    }

    [HttpPost("section")]
    public async Task<IActionResult> ShopSection(
        [FromBody] ShopSection section, [FromQuery] string? locale, [FromQuery] bool? isNewShop,
        CancellationToken cancellationToken)
    {
        locale ??= "en";
        var _isNewShop = isNewShop ?? false;
        logger.LogInformation("Item Shop section image request received | Locale = {Locale} | New Shop = {SectionId}", locale, section.Id);

        SKBitmap templateBitmapCopy;
        ShopSectionLocationData? shopSectionLocationData;

        using (await namedLock.LockAsync($"shop_section_template_{section.Id}", cancellationToken).ConfigureAwait(false))
        {
            var templateBitmap = cache.Get<SKBitmap?>($"shop_section_template_bmp_{section.Id}");
            shopSectionLocationData = cache.Get<ShopSectionLocationData?>($"shop_section_location_data_{section.Id}");
            if (_isNewShop || templateBitmap is null)
            {
                await PrefetchImages([section], cancellationToken);
                var templateGenerationResult = await GenerateSectionTemplate(section);
                templateBitmap = templateGenerationResult.Item2;
                shopSectionLocationData = templateGenerationResult.Item1;
                cache.Set($"shop_section_template_bmp_{section.Id}", templateBitmap, ShopImageCacheOptions);
                cache.Set($"shop_section_location_data_{section.Id}", shopSectionLocationData,
                    TimeSpan.FromMinutes(10));
            }
            templateBitmapCopy = templateBitmap.Copy();
        }

        using var templateCopy = templateBitmapCopy;
        SKBitmap localeTemplateBitmapCopy;

        var lockName = $"shop_section_template_{locale}_{section.Id}";
        using (await namedLock.LockAsync(lockName, cancellationToken).ConfigureAwait(false))
        {
            var localeTemplateBitmap = cache.Get<SKBitmap?>($"shop_section_template_{locale}_bmp_{section.Id}");
            if (_isNewShop || localeTemplateBitmap == null)
            {
                localeTemplateBitmap =
                    await GenerateSectionLocaleTemplate(section, templateCopy, shopSectionLocationData!);
                cache.Set($"shop_section_template_{locale}_bmp_{section.Id}", localeTemplateBitmap,
                    ShopImageCacheOptions);
            }
            localeTemplateBitmapCopy = localeTemplateBitmap.Copy();
        }

        using var localeCopy = localeTemplateBitmapCopy;
        using var image = await GenerateShopSectionImage(section, localeCopy);
        var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    private Task PrefetchImages(Shop shop, CancellationToken cancellationToken)
    {
        return PrefetchImages(shop.Sections, cancellationToken);
    }

    private async Task PrefetchImages(IReadOnlyList<ShopSection> sections, CancellationToken cancellationToken)
    {
        var entries = sections.SelectMany(x => x.Entries);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = cancellationToken
        };
        using var client = clientFactory.CreateClient();
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

                var url = entry.ImageUrl ?? entry.FallbackImageUrl;
                SKBitmap bitmap;

                try
                {
                    var imageBytes = await client.GetByteArrayAsync(url, token);
                    bitmap = SKBitmap.Decode(imageBytes)
                        ?? throw new InvalidDataException($"Upstream image for shop entry '{entry.Id}' is invalid.");
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
            using var paint = new SKPaintSafe();
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

            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(imageInfo.Rect, 50), antialias: true);
            canvas.DrawBitmap(backgroundBitmap, imageInfo.Rect, SKSamplingOptions.Default, backgroundImagePaint);
            canvas.Restore();
        }

        canvas.DrawBitmap(templateBitmap, 0, 0, SKSamplingOptions.Default);

        if (shop is { CreatorCode: not null, CreatorCodeTitle: not null })
        {
            using var shopTitlePaint = new SKPaint();
            using var shopTitlePaintFont = new SKFont();
            shopTitlePaint.IsAntialias = true;
            shopTitlePaintFont.Size = TITLE_FONT_SIZE;
            shopTitlePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-86Bold.otf");


            var shopTitleWidth = shopTitlePaintFont.MeasureText(shop.Title, shopTitlePaint);


            var maxBoxWidth = imageInfo.Width - 3 * HORIZONTAL_PADDING - shopTitleWidth;
            if (maxBoxWidth > 0)
            {
                using var creatorCodeBoxBitmap =
                    await GenerateCreatorCodeBox(shop.CreatorCodeTitle, shop.CreatorCode, maxBoxWidth);
                canvas.DrawBitmap(creatorCodeBoxBitmap, imageInfo.Width - 100 - creatorCodeBoxBitmap.Width, 100, SKSamplingOptions.Default);
            }

            var adBannerBitmap = await assets.GetBitmap("Assets/Images/Shop/ad_banner.png"); // don't dispose
            canvas.DrawBitmap(adBannerBitmap, imageInfo.Width - 100 - 50 - adBannerBitmap!.Width,
                100 - adBannerBitmap.Height / 2f, SKSamplingOptions.Default);
        }

        return bitmap;
    }

    private async Task<SKBitmap> GenerateLocaleTemplate(Shop shop, SKBitmap templateBitmap,
        IEnumerable<ShopSectionLocationData> shopSectionLocationData)
    {
        var imageInfo = new SKImageInfo(templateBitmap.Width, templateBitmap.Height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        canvas.DrawBitmap(templateBitmap, SKPoint.Empty, SKSamplingOptions.Default);

        // Drawing the shop title
        using var shopTitlePaint = new SKPaint();
        using var shopTitlePaintFont = new SKFont();
        shopTitlePaint.IsAntialias = true;
        shopTitlePaintFont.Size = TITLE_FONT_SIZE;
        shopTitlePaint.Color = SKColors.White;
        shopTitlePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-86Bold.otf");

        var shopTitleWidth = shopTitlePaintFont.MeasureText(shop.Title, shopTitlePaint);
        canvas.DrawAlignedText(shop.Title, new SKPoint(100, 50), shopTitlePaintFont, shopTitlePaint);

        // Drawing the date
        using var datePaint = new SKPaint();
        using var datePaintFont = new SKFont();
        datePaint.IsAntialias = true;
        datePaintFont.Size = DATE_FONT_SIZE;
        datePaint.Color = SKColors.White;
        datePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-86BoldItalic.otf");

        var datePoint = new SKPoint(
            Math.Max(HORIZONTAL_PADDING + shopTitleWidth / 2f,
                HORIZONTAL_PADDING + datePaintFont.MeasureText(shop.Date, datePaint) / 2),
            313);
        canvas.DrawAlignedText(shop.Date, datePoint, datePaintFont, datePaint, SKTextAlign.Center);

        foreach (var sectionLocationData in shopSectionLocationData)
        {
            var shopSection = shop.Sections.FirstOrDefault(x => x.Id == sectionLocationData.Id);

            // Draw the section name if it exists
            if (sectionLocationData.Name != null && shopSection?.Name != null)
            {
                using var sectionNamePaint = new SKPaint();
                using var sectionNamePaintFont = new SKFont();
                sectionNamePaint.IsAntialias = true;
                sectionNamePaintFont.Size = SECTION_NAME_FONT_SIZE;
                sectionNamePaint.Color = SKColors.White;
                sectionNamePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-86BoldItalic.otf");

                var sectionNamePoint = new SKPoint(sectionLocationData.Name.X, sectionLocationData.Name.Y);
                canvas.DrawAlignedText(shopSection.Name, sectionNamePoint, sectionNamePaintFont, sectionNamePaint);
            }

            foreach (var entryLocationData in sectionLocationData.Entries)
            {
                var shopEntry = shopSection?.Entries?.FirstOrDefault(x => x.Id == entryLocationData.Id);
                if (shopEntry is null)
                    continue;

                using var entryNamePaint = new SKPaint();
                using var entryNamePaintFont = new SKFont();
                entryNamePaint.IsAntialias = true;
                entryNamePaintFont.Size = ENTRY_NAME_FONT_SIZE;
                entryNamePaint.Color = SKColors.White;
                entryNamePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-75Medium.otf");

                var nameLines = SplitNameText(shopEntry.Name, entryLocationData.Name.MaxWidth ?? 0,
                    entryNamePaintFont, entryNamePaint);
                if (nameLines.Length > 1)
                {
                    canvas.DrawAlignedText(
                        nameLines[0],
                        new SKPoint(entryLocationData.Name.X, entryLocationData.Name.Y - 33), entryNamePaintFont,
                        entryNamePaint);
                }

                canvas.DrawAlignedText(
                    nameLines.Last(),
                    new SKPoint(entryLocationData.Name.X, entryLocationData.Name.Y), entryNamePaintFont,
                    entryNamePaint);

                // Draw the shop entry price
                using var pricePaint = new SKPaint();
                using var pricePaintFont = new SKFont();
                pricePaint.IsAntialias = true;
                pricePaintFont.Size = ENTRY_PRICE_FONT_SIZE;
                pricePaint.Color = SKColors.White;
                pricePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-75Medium.otf");

                var priceTextWidth = pricePaintFont.MeasureText(shopEntry.FinalPrice, pricePaint);
                var pricePoint = new SKPoint(
                    entryLocationData.Price.X,
                    entryLocationData.Price.Y - pricePaintFont.Metrics.Descent);
                canvas.DrawAlignedText(
                    shopEntry.FinalPrice,
                    pricePoint, pricePaintFont,
                    pricePaint,
                    verticalAlignment: VerticalTextAlignment.Baseline);

                // Draw strikeout old price if item is discounted
                if (shopEntry.FinalPrice != shopEntry.RegularPrice)
                {
                    using var oldPricePaint = new SKPaint();
                    using var oldPricePaintFont = new SKFont();
                    oldPricePaint.IsAntialias = true;
                    oldPricePaintFont.Size = ENTRY_PRICE_FONT_SIZE;
                    oldPricePaint.Color = SKColors.White.WithAlpha((int)(.6 * 255));
                    oldPricePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-75Medium.otf");

                    var oldPriceTextWidth = oldPricePaintFont.MeasureText(shopEntry.RegularPrice, oldPricePaint);
                    var oldPricePoint = new SKPoint(
                        entryLocationData.Price.X + priceTextWidth + 9,
                        entryLocationData.Price.Y - oldPricePaintFont.Metrics.Descent);
                    canvas.DrawAlignedText(
                        shopEntry.RegularPrice,
                        oldPricePoint, oldPricePaintFont,
                        oldPricePaint,
                        verticalAlignment: VerticalTextAlignment.Baseline);

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
                    canvas.DrawBitmap(bannerBitmap, entryLocationData.Banner!.X, entryLocationData.Banner.Y, SKSamplingOptions.Default);
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
                    using var itemCardPaint = new SKPaintSafe();
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

                canvas.DrawBitmap(sectionBitmap, new SKPoint(sectionX, sectionY), SKSamplingOptions.Default);
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
        using var creatorCodeTitlePaintFont = new SKFont();
        creatorCodeTitlePaint.IsAntialias = true;
        creatorCodeTitlePaintFont.Size = 100f;
        creatorCodeTitlePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-76Bold.otf");
        creatorCodeTitlePaint.Color = SKColors.Black;

        using var creatorCodePaint = new SKPaint();
        using var creatorCodePaintFont = new SKFont();
        creatorCodePaint.IsAntialias = true;
        creatorCodePaintFont.Size = 100f;
        creatorCodePaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-76Bold.otf");
        creatorCodePaint.Color = new SKColor(178, 165, 255);

        float width =
                creatorCodeTitlePaintFont.MeasureText(creatorCodeTitle, creatorCodeTitlePaint) + creatorCodeTitlePaintFont.MeasureText(creatorCode, creatorCodeTitlePaint),
            height = 150f;
        while (width > maxWidth)
        {
            creatorCodeTitlePaintFont.Size--;
            creatorCodePaintFont.Size--;
            width = creatorCodeTitlePaintFont.MeasureText(creatorCodeTitle, creatorCodeTitlePaint) + creatorCodePaintFont.MeasureText(creatorCode, creatorCodePaint);
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

        var y = (imageInfo.Height - creatorCodePaintFont.Spacing) / 2 - creatorCodePaintFont.Metrics.Ascent;

        canvas.DrawText(creatorCodeTitle, 0, y, SKTextAlign.Left, creatorCodeTitlePaintFont, creatorCodeTitlePaint);
        canvas.DrawText(creatorCode, imageInfo.Width, y, SKTextAlign.Right, creatorCodePaintFont, creatorCodePaint);

        return bitmap;
    }

    private async Task<SKBitmap> GenerateBanner(string text, IReadOnlyList<string> colors, int maxWidth)
    {
        using var bannerPaint = new SKPaint();
        using var bannerPaintFont = new SKFont();
        bannerPaint.IsAntialias = true;
        bannerPaintFont.Size = 17.0f;
        bannerPaintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-76BoldItalic.otf");
        bannerPaint.Color = SKColor.Parse(colors[1]);

        bannerPaintFont.MeasureText(text, out var textBounds, bannerPaint);
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
                bannerPaintFont.MeasureText(text + "...", out textBounds, bannerPaint);
            }

            text += "...";
        }


        canvas.DrawAlignedText(
            text,
            new SKPoint(13, imageInfo.Height / 2f), bannerPaintFont,
            bannerPaint,
            verticalAlignment: VerticalTextAlignment.Center);

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
            using var backgroundGradientPaint = new SKPaintSafe();
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
            using var gradientPaint = new SKPaintSafe();
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
            var coverRect = SKRect.Create(10, 10, 236, 236);

            using var coverPaint = new SKPaint();
            coverPaint.IsAntialias = true;
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(coverRect, 10), antialias: true);
            canvas.DrawBitmap(shopEntry.Image, coverRect, SKSamplingOptions.Default, coverPaint);
            canvas.Restore();
        }
        else
        {
            int resizeWidth, resizeHeight;
            var aspectRatio = (float)shopEntry.Image.Width / shopEntry.Image.Height;

            if (imageInfo.Width > imageInfo.Height)
            {
                resizeWidth = imageInfo.Width;
                resizeHeight = (int)(imageInfo.Width / aspectRatio);
            }
            else
            {
                resizeWidth = (int)(imageInfo.Height / aspectRatio);
                resizeHeight = imageInfo.Height;
            }

            using var imagePaint = new SKPaint();

            // Car bundles get centered in the middle of the card vertically
            if (shopEntry.ImageType == "car-bundle")
            {
                var cropY = (resizeHeight - imageInfo.Height) / 2f;
                var sourceScaleY = shopEntry.Image.Height / (float)resizeHeight;
                var sourceRect = new SKRect(0, cropY * sourceScaleY, shopEntry.Image.Width,
                    (cropY + imageInfo.Height) * sourceScaleY);
                canvas.DrawBitmap(shopEntry.Image, sourceRect,
                    new SKRect(0, 0, resizeWidth, imageInfo.Height), SKSamplingOptions.Default, imagePaint);
            }
            // Center image in the middle of the card, if width is bigger than the image
            else if (resizeWidth > imageInfo.Width)
            {
                var cropX = (resizeWidth - imageInfo.Width) / 2f;
                var sourceScaleX = shopEntry.Image.Width / (float)resizeWidth;
                var sourceRect = new SKRect(cropX * sourceScaleX, 0,
                    (cropX + imageInfo.Width) * sourceScaleX, shopEntry.Image.Height);
                canvas.DrawBitmap(shopEntry.Image, sourceRect,
                    new SKRect(0, 0, imageInfo.Width, resizeHeight), SKSamplingOptions.Default, imagePaint);
            }
            else
            {
                var offsetMulti = shopEntry.Size >= 3f ? 0.08f : 0f;
                canvas.DrawBitmap(shopEntry.Image,
                    new SKRect(0, resizeHeight * -offsetMulti, resizeWidth,
                        resizeHeight * (1 - offsetMulti)), SKSamplingOptions.Default, imagePaint);
            }
        }


        if (shopEntry.TextBackgroundColor != null)
        {
            var textBackgroundColor = ImageUtils.ParseColor(shopEntry.TextBackgroundColor);
            using var shadowPaint = new SKPaintSafe();
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
            using var shadowPaint = new SKPaintSafe();
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
        canvas.DrawBitmap(vbucksBitmap, 13, imageInfo.Height - vbucksBitmap!.Height - 11, SKSamplingOptions.Default);

        if (shopEntry.IsSpecial)
        {
            using var paint = new SKPaint();
            using var paintFont = new SKFont();
            using var font = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-74Regular.otf"), 35.0f);
            paint.IsAntialias = true;
            paintFont.Size = 35.0f;
            paint.Color = SKColors.White;
            paintFont.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-74Regular.otf");

            canvas.DrawText("+", imageInfo.Width - 18, imageInfo.Height - paintFont.Metrics.Descent + 3, SKTextAlign.Right, paintFont, paint);
        }

        return bitmap;
    }

    private static string[] SplitNameText(string text, int maxWidth, SKFont paintFont, SKPaint paint)
    {
        var regex = NameSplitRegex();
        var matches = regex.Matches(text);

        var currentLine = 0;
        var lines = new StringBuilder[] { new(), new() };
        foreach (Match match in matches)
        {
            var line = lines[currentLine];
            paintFont.MeasureText(line + match.Value, out var bounds, paint);
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
            paintFont.MeasureText(line.ToString(), out var textBounds, paint);
            if (textBounds.Width <= maxWidth) continue;

            while (textBounds.Width > maxWidth)
            {
                line.Remove(line.Length - 1, 1);
                paintFont.MeasureText(line + "...", out textBounds, paint);
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
