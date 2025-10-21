using System.Net;
using AsyncKeyedLock;
using EasyFortniteStats_ImageApi.Models;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;

// ReSharper disable InconsistentNaming
namespace EasyFortniteStats_ImageApi.Controllers;

[ApiController]
[Route("locker")]
public class AccountImageController(
    IHttpClientFactory clientFactory,
    AsyncKeyedLocker<string> namedLock,
    SharedAssets assets,
    ILogger<AccountImageController> logger)
    : ControllerBase
{
    private const string BASE_ITEM_IMAGE_PATH = "data/images/locker/items";

    private static readonly IReadOnlyList<(int Count, int Quality)> QualityMapping = new List<(int, int)>
    {
        (100, 100),
        (150, 95),
        (200, 90),
        (250, 85),
        (300, 80),
        (325, 75),
        (350, 70),
        (400, 65),
        (425, 60),
        (450, 55),
        (475, 50),
        (500, 45),
    };

    [HttpPost]
    public async Task<IActionResult> Post(Locker locker, [FromQuery] bool? lossless, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Locker image request received | Name = {PlayerName} | Locale = {Locale} | Items = {Items}",
            locker.PlayerName, locker.Locale, locker.Items.Length);

        using (await namedLock.LockAsync($"locker_{locker.RequestId}", cancellationToken).ConfigureAwait(false))
        {
            await GenerateItemCards(locker, cancellationToken);
        }

        using (locker)
        {
            using var lockerBitmap = await GenerateImage(locker);

            // Determine the quality of the image based on quality mapping and locker.Items.Length
            var quality = lossless == true
                ? 100
                : QualityMapping.FirstOrDefault(x => locker.Items.Length <= x.Count).Quality;
            var data = lockerBitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
            return File(data.AsStream(true), "image/jpeg");
        }
    }

    private async Task<SKBitmap> GenerateImage(Locker locker)
    {
        // Calculate rows and columns based on locker.Items count
        // Columns and Rows should be equal
        // min value is 5
        var columns = Math.Max((int)Math.Ceiling(Math.Sqrt(locker.Items.Length)), 5);
        var rows = locker.Items.Length / columns + (locker.Items.Length % columns == 0 ? 0 : 1);

        var uiResizingFactor = (float)(1 + rows * 0.15);
        var nameFontSize = (int)(64 * uiResizingFactor);
        var footerSpace = (int)(80 * uiResizingFactor);
        var imageInfo = new SKImageInfo(
            50 + 256 * columns + 25 * (columns - 1) + 50,
            50 + nameFontSize + 50 + rows * 313 + (rows - 1) * 25 + footerSpace);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        using var backgroundPaint = new SKPaintSafe();
        backgroundPaint.IsAntialias = true;
        backgroundPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint((float)imageInfo.Width / 2, 0),
            new SKPoint((float)imageInfo.Width / 2, imageInfo.Height),
            [new SKColor(44, 154, 234), new SKColor(14, 53, 147)],
            [0.0f, 1.0f],
            SKShaderTileMode.Repeat);

        canvas.DrawRect(0, 0, imageInfo.Width, imageInfo.Height, backgroundPaint);

        var textBounds = new SKRect();
        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf"); // don't dispose

        var iconBitmap = await assets.GetBitmap("Assets/Images/Locker/Icon.png"); // don't dispose
        var resize = (int)(50 * uiResizingFactor);
        using var drawIconPaint = new SKPaint();
        drawIconPaint.IsAntialias = true;
        drawIconPaint.FilterQuality = SKFilterQuality.High;
        canvas.DrawBitmap(iconBitmap, SKRect.Create(50, 50, resize, resize), drawIconPaint);

        using var splitPaint = new SKPaint();
        splitPaint.IsAntialias = true;
        splitPaint.Color = SKColors.White;

        var splitWidth = 5 * uiResizingFactor;
        var splitR = 3 * uiResizingFactor;
        canvas.DrawRoundRect(50 + resize + splitWidth, 57, splitWidth, 50 * uiResizingFactor, splitR, splitR,
            splitPaint);

        using var namePaint = new SKPaint();
        namePaint.IsAntialias = true;
        namePaint.Color = SKColors.White;
        namePaint.Typeface = segoeFont;
        namePaint.TextSize = nameFontSize;
        namePaint.FilterQuality = SKFilterQuality.Medium;

        namePaint.MeasureText(locker.PlayerName, ref textBounds);
        canvas.DrawText(locker.PlayerName, 50 + resize + splitWidth * 3, 58 - textBounds.Top, namePaint);

        using var discordBoxBitmap = await ImageUtils.GenerateDiscordBox(assets, locker.UserName, uiResizingFactor);
        canvas.DrawBitmap(discordBoxBitmap, imageInfo.Width - 50 - discordBoxBitmap.Width, 39);

        var column = 0;
        var row = 0;
        foreach (var item in locker.Items)
        {
            canvas.DrawBitmap(
                item.Image,
                50 + 256 * column + 25 * column,
                50 + nameFontSize + 50 + row * 313 + row * 25);
            column++;
            if (column != columns) continue;
            column = 0;
            row++;
        }

        // Load Footer.svg file as a stream
        using var footerBitmap = await GenerateFooter(uiResizingFactor);
        canvas.DrawBitmap(footerBitmap, (imageInfo.Width - footerBitmap.Width) / 2.0f,
            imageInfo.Height - (footerSpace + footerBitmap.Height) / 2.0f);

        return bitmap;
    }

    private async Task GenerateItemCards(Locker locker, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2,
            CancellationToken = cancellationToken
        };
        using var client = clientFactory.CreateClient();
        await Parallel.ForEachAsync(locker.Items, options, async (item, token) =>
        {
            var filePath = Path.Combine(BASE_ITEM_IMAGE_PATH, $"{item.Id}.png");
            var fileExists = System.IO.File.Exists(filePath);

            byte[]? itemImageBytes = null;
            if (fileExists)
            {
                itemImageBytes = await System.IO.File.ReadAllBytesAsync(filePath, token);
            }
            else if (item.ImageUrl is not null)
            {
                try
                {
                    var imageUrl = changeUrlImageSize(item.ImageUrl, 256);
                    itemImageBytes = await client.GetByteArrayAsync(imageUrl, token);
                }
                catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound)
                {
                    try
                    {
                        itemImageBytes = await client.GetByteArrayAsync(item.ImageUrl, token);
                    }
                    catch (HttpRequestException e2)
                    {
                        logger.LogWarning(
                            "Failed to download image with status {HttpStatusCode} for {ItemName} ({ItemImageUrl}) ", 
                            e2.StatusCode, item.Name, item.ImageUrl);
                    }
                }
                catch (HttpRequestException e)
                {
                    logger.LogWarning(
                        "Failed to download image with status {HttpStatusCode} for {ItemName} ({ItemImageUrl}) ", 
                        e.StatusCode, item.Name, item.ImageUrl);
                }
            }

            SKBitmap? itemImage = null;

            if (itemImageBytes is not null)
            {
                var itemImageRaw = SKBitmap.Decode(itemImageBytes);
                if (itemImageRaw.Width != 256 || itemImageRaw.Height != 256)
                {
                    fileExists = false;
                    var resized = itemImageRaw.Resize(new SKImageInfo(256, 256), SKFilterQuality.Medium);
                    itemImageRaw.Dispose();
                    itemImage = resized;
                }
                else
                {
                    itemImage = itemImageRaw;
                }

                if (!fileExists)
                {
                    Directory.CreateDirectory(BASE_ITEM_IMAGE_PATH);
                    using var data = itemImage.Encode(SKEncodedImageFormat.Png, 100);
                    var dataBytes = data.AsSpan().ToArray();
                    await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 4096, true);
                    fileStream.SetLength(dataBytes.LongLength);
                    await fileStream.WriteAsync(dataBytes, token);
                }
            }

            using (itemImage)
            {
                item.Image = await GenerateItemCard(item, itemImage);
            }
        });
    }

    private async Task<SKBitmap> GenerateItemCard(LockerItem lockerItem, SKBitmap? itemImage)
    {
        var imageInfo = new SKImageInfo(256, 313);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var rarityBackground =
            await assets.GetBitmap($"Assets/Images/Locker/RarityBackgrounds/{lockerItem.Rarity}.png");
        canvas.DrawBitmap(rarityBackground, SKPoint.Empty);

        if (itemImage is not null)
        {
            canvas.DrawBitmap(itemImage, SKPoint.Empty);
        }
        else
        {
            using var questionmarkPaint = new SKPaint();
            questionmarkPaint.IsAntialias = true;
            questionmarkPaint.Color = SKColors.White;
            questionmarkPaint.Typeface = await assets.GetFont("Assets/Fonts/Fortnite-86Bold.otf");
            questionmarkPaint.TextSize = 256.0f;
            questionmarkPaint.TextAlign = SKTextAlign.Center;

            var questionmarkTextBounds = new SKRect();
            questionmarkPaint.MeasureText("?", ref questionmarkTextBounds);

            canvas.DrawText("?", (float)bitmap.Width / 2, (float)bitmap.Height / 2 + questionmarkTextBounds.Height / 2,
                questionmarkPaint);
        }

        var typeIcon = lockerItem.SourceType != SourceType.Other
            ? await assets.GetBitmap($"Assets/Images/Locker/Source/{lockerItem.SourceType}.png")
            : null;
        using var overlayImage = ImageUtils.GenerateItemCardOverlay(imageInfo.Width, typeIcon);
        canvas.DrawBitmap(overlayImage, new SKPoint(0, imageInfo.Height - overlayImage.Height));

        using var rarityStripe =
            ImageUtils.GenerateRarityStripe(imageInfo.Width, SKColor.Parse(lockerItem.RarityColor));
        canvas.DrawBitmap(rarityStripe,
            new SKPoint(0, imageInfo.Height - overlayImage.Height - rarityStripe.Height + 5));
        // TODO: Fix Transparency issues

        var fortniteFont = await assets.GetFont("Assets/Fonts/Fortnite.ttf"); // don't dispose

        using var namePaint = new SKPaint();
        namePaint.IsAntialias = true;
        namePaint.TextSize = 18.0f;
        namePaint.Color = SKColors.White;
        namePaint.Typeface = fortniteFont;
        namePaint.TextAlign = SKTextAlign.Center;

        var entryNameTextBounds = new SKRect();
        namePaint.MeasureText(lockerItem.Name, ref entryNameTextBounds);
        canvas.DrawText(lockerItem.Name, (float)bitmap.Width / 2, bitmap.Height - 59 + entryNameTextBounds.Height,
            namePaint);

        using var descriptionPaint = new SKPaint();
        descriptionPaint.IsAntialias = true;
        descriptionPaint.TextSize = 15.0f;
        descriptionPaint.Color = SKColor.Parse(lockerItem.RarityColor);
        descriptionPaint.Typeface = fortniteFont;
        descriptionPaint.TextAlign = SKTextAlign.Center;

        descriptionPaint.MeasureText(lockerItem.Description, ref entryNameTextBounds);
        canvas.DrawText(lockerItem.Description, (float)bitmap.Width / 2,
            bitmap.Height - 42 + entryNameTextBounds.Height, descriptionPaint);

        using var sourcePaint = new SKPaint();
        sourcePaint.IsAntialias = true;
        sourcePaint.TextSize = 15.0f;
        sourcePaint.Color = SKColors.White;
        sourcePaint.Typeface = fortniteFont;
        sourcePaint.TextAlign = SKTextAlign.Right;

        var fontOffset = lockerItem.SourceType == SourceType.Other ? 10 : 42;

        sourcePaint.MeasureText(lockerItem.Source, ref entryNameTextBounds);
        canvas.DrawText(lockerItem.Source, bitmap.Width - fontOffset, bitmap.Height - entryNameTextBounds.Height + 8,
            sourcePaint);

        return bitmap;
    }

    private async Task<SKBitmap> GenerateFooter(float resizeFactor)
    {
        var poppinsFont = await assets.GetFont("Assets/Fonts/Poppins.ttf"); // don't dispose

        using var textPaint = new SKPaint();
        textPaint.IsAntialias = true;
        textPaint.TextSize = 40.0f * resizeFactor;
        textPaint.Color = SKColors.White;
        textPaint.Typeface = poppinsFont;

        //var text = "EasyFnStats.com".ToUpper();
        const string text = "EASYFNSTATS.COM";

        var textBounds = new SKRect();
        textPaint.MeasureText(text, ref textBounds);

        var imageInfo = new SKImageInfo((int)((50 + 10 + 5 + 10) * resizeFactor + textBounds.Width),
            (int)(50 * resizeFactor));
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var logoBitmap = await assets.GetBitmap("Assets/Images/Logo.png"); // don't dispose

        using var drawLogoPaint = new SKPaint();
        drawLogoPaint.IsAntialias = true;
        drawLogoPaint.FilterQuality = SKFilterQuality.High;
        canvas.DrawBitmap(logoBitmap, SKRect.Create(0, 0, imageInfo.Height, imageInfo.Height), drawLogoPaint);

        var splitR = 3 * resizeFactor;

        using var splitPaint = new SKPaint();
        splitPaint.IsAntialias = true;
        splitPaint.Color = SKColors.White;
        canvas.DrawRoundRect((50 + 10) * resizeFactor, (imageInfo.Height - 40 * resizeFactor) / 2, 5 * resizeFactor,
            40 * resizeFactor, splitR, splitR, splitPaint);

        canvas.DrawText(text, (50 + 10 + 5 + 10) * resizeFactor, (imageInfo.Height + textBounds.Height) / 2, textPaint);

        return bitmap;
    }

    private static string changeUrlImageSize(string originalUrl, int size)
    {
        var uri = new Uri(originalUrl);

        if (!uri.Host.Equals("fortnite-api.com", StringComparison.OrdinalIgnoreCase)) return originalUrl;

        var basePart = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        var filePart = uri.Segments.Last();
        var modifiedFileName = filePart.Insert(filePart.LastIndexOf('.'), $"_{size}");
        return $"{basePart}{modifiedFileName}";
    }
}