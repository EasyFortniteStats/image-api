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
    SharedAssets assets)
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
    public async Task<IActionResult> Post(Locker locker, [FromQuery] bool? lossless)
    {
        Console.WriteLine(
            $"Locker image request | Name = {locker.PlayerName} | Locale = {locker.Locale} | Items = {locker.Items.Length}");
        var lockKey = $"locker_{locker.RequestId}";
        using (await namedLock.LockAsync(lockKey).ConfigureAwait(false))
        {
            await GenerateItemCards(locker);
        }

        using var lockerBitmap = await GenerateImage(locker);

        // Determine the quality of the image based on quality mapping and locker.Items.Length
        var quality = lossless == true ? 100 : QualityMapping.FirstOrDefault(x => locker.Items.Length <= x.Count).Quality;
        return File(lockerBitmap.Encode(SKEncodedImageFormat.Jpeg, quality).AsStream(true), "image/jpeg");
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

        using var backgroundPaint = new SKPaint();
        backgroundPaint.IsAntialias = true;
        backgroundPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint((float)imageInfo.Width / 2, 0),
            new SKPoint((float)imageInfo.Width / 2, imageInfo.Height),
            [new SKColor(44, 154, 234), new SKColor(14, 53, 147)],
            [0.0f, 1.0f],
            SKShaderTileMode.Repeat);

        canvas.DrawRect(0, 0, imageInfo.Width, imageInfo.Height, backgroundPaint);

        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf"); // don't dispose

        var icon = await assets.GetBitmap("Assets/Images/Locker/Icon.png"); // don't dispose
        var resize = (int)(50 * uiResizingFactor);
        using var resizeIcon = icon!.Resize(new SKImageInfo(resize, resize), SKSamplingOptions.Default);
        canvas.DrawBitmap(resizeIcon, 50, 50);

        using var splitPaint = new SKPaint();
        splitPaint.IsAntialias = true;
        splitPaint.Color = SKColors.White;

        var splitWidth = 5 * uiResizingFactor;
        var splitR = 3 * uiResizingFactor;
        canvas.DrawRoundRect(50 + resizeIcon.Width + splitWidth, 57, splitWidth, 50 * uiResizingFactor, splitR, splitR,
            splitPaint);

        using var namePaint = new SKPaint();
        using var nameFont = new SKFont(segoeFont, nameFontSize);
        namePaint.IsAntialias = true;
        namePaint.Color = SKColors.White;

        nameFont.MeasureText(locker.PlayerName, out var textBounds);
        canvas.DrawText(locker.PlayerName, 50 + resizeIcon.Width + splitWidth * 3, 58 - textBounds.Top, nameFont, namePaint);

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
            item.Image?.Dispose();
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

    private async Task GenerateItemCards(Locker locker)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2
        };
        await Parallel.ForEachAsync(locker.Items, options, async (item, token) =>
        {
            var filePath = Path.Combine(BASE_ITEM_IMAGE_PATH, $"{item.Id}.png");
            SKBitmap? itemImage = null;
            if (!System.IO.File.Exists(filePath) && item.ImageUrl is not null)
            {
                using var client = clientFactory.CreateClient();
                byte[]? itemImageBytes;
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
                        Console.WriteLine(
                            $"Failed to download image with status {e2.StatusCode} for {item.Name} ({item.ImageUrl}) ");
                        itemImageBytes = null;
                    }
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine(
                        $"Failed to download image with status {e.StatusCode} for {item.Name} ({item.ImageUrl}) ");
                    itemImageBytes = null;
                }

                if (itemImageBytes is not null)
                {
                    var itemImageRaw = SKBitmap.Decode(itemImageBytes);
                    if (itemImageRaw.Width != 256 || itemImageRaw.Height != 256)
                    {
                        itemImage = itemImageRaw.Resize(new SKImageInfo(256, 256), SKSamplingOptions.Default);
                    }
                    else
                    {
                        itemImage = itemImageRaw;
                    }

                    Directory.CreateDirectory(BASE_ITEM_IMAGE_PATH);
                    await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 4096, true);
                    using var data = itemImage.Encode(SKEncodedImageFormat.Png, 100);
                    data.SaveTo(fileStream);
                }
            }
            else if (System.IO.File.Exists(filePath))
            {
                itemImage = SKBitmap.Decode(filePath);
            }

            item.Image = await GenerateItemCard(item, itemImage);
            itemImage?.Dispose();
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
            using var questionmarkFont = new SKFont(await assets.GetFont("Assets/Fonts/Fortnite-86Bold.otf"), 256.0f);
            questionmarkPaint.IsAntialias = true;
            questionmarkPaint.Color = SKColors.White;
            
            questionmarkFont.MeasureText("?", out var questionmarkTextBounds);
            
            canvas.DrawText("?", (float)bitmap.Width / 2, (float)bitmap.Height / 2 + questionmarkTextBounds.Height / 2, SKTextAlign.Center, questionmarkFont, questionmarkPaint);
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
        using var nameFont = new SKFont(fortniteFont, 18.0f);
        namePaint.IsAntialias = true;
        namePaint.Color = SKColors.White;

        nameFont.MeasureText(lockerItem.Name, out var entryNameTextBounds);
        canvas.DrawText(lockerItem.Name, (float)bitmap.Width / 2, bitmap.Height - 59 + entryNameTextBounds.Height, SKTextAlign.Center, nameFont, namePaint);

        using var descriptionPaint = new SKPaint();
        using var descriptionFont = new SKFont(fortniteFont, 15.0f);
        descriptionPaint.IsAntialias = true;
        descriptionPaint.Color = SKColor.Parse(lockerItem.RarityColor);

        descriptionFont.MeasureText(lockerItem.Description, out entryNameTextBounds);
        canvas.DrawText(lockerItem.Description, (float)bitmap.Width / 2,
            bitmap.Height - 42 + entryNameTextBounds.Height, SKTextAlign.Center, descriptionFont, descriptionPaint);

        using var sourcePaint = new SKPaint();
        using var sourceFont = new SKFont(fortniteFont, 15.0f);
        sourcePaint.IsAntialias = true;
        sourcePaint.Color = SKColors.White;

        var fontOffset = lockerItem.SourceType == SourceType.Other ? 10 : 42;

        sourceFont.MeasureText(lockerItem.Source, out entryNameTextBounds);
        canvas.DrawText(lockerItem.Source, bitmap.Width - fontOffset, bitmap.Height - entryNameTextBounds.Height + 8, SKTextAlign.Right, sourceFont, sourcePaint);

        return bitmap;
    }

    private async Task<SKBitmap> GenerateFooter(float resizeFactor)
    {
        var poppinsFont = await assets.GetFont("Assets/Fonts/Poppins.ttf"); // don't dispose

        using var textPaint = new SKPaint();
        using var textFont = new SKFont(poppinsFont, 40.0f * resizeFactor);
        textPaint.IsAntialias = true;
        textPaint.Color = SKColors.White;

        //var text = "EasyFnStats.com".ToUpper();
        const string text = "EASYFNSTATS.COM";

        textFont.MeasureText(text, out var textBounds);

        var imageInfo = new SKImageInfo((int)((50 + 10 + 5 + 10) * resizeFactor + textBounds.Width),
            (int)(50 * resizeFactor));
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var logoBitmap = await assets.GetBitmap("Assets/Images/Logo.png"); // don't dispose
        var logoBitmapResize =
            logoBitmap!.Resize(new SKImageInfo(imageInfo.Height, imageInfo.Height), SKSamplingOptions.Default);
        canvas.DrawBitmap(logoBitmapResize, new SKPoint(0, 0));

        var splitR = 3 * resizeFactor;

        using var splitPaint = new SKPaint();
        splitPaint.IsAntialias = true;
        splitPaint.Color = SKColors.White;
        canvas.DrawRoundRect((50 + 10) * resizeFactor, (imageInfo.Height - 40 * resizeFactor) / 2, 5 * resizeFactor,
            40 * resizeFactor, splitR, splitR, splitPaint);

        canvas.DrawText(text, (50 + 10 + 5 + 10) * resizeFactor, (imageInfo.Height + textBounds.Height) / 2, textFont, textPaint);

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