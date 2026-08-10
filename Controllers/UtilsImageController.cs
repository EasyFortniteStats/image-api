using System.Security.Cryptography;
using EasyFortniteStats_ImageApi.Models;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;

// ReSharper disable InconsistentNaming
namespace EasyFortniteStats_ImageApi.Controllers;

[ApiController]
[Route("utils")]
public class UtilsImageController(SharedAssets assets, ILogger<UtilsImageController> logger) : ControllerBase
{
    [HttpGet("collectGarbage")]
    public IActionResult CollectGarbage()
    {
        GC.Collect();
        return NoContent();
    }

    [HttpPost("progressBar")]
    public async Task<IActionResult> GenerateProgressBar(ProgressBar progressBar)
    {
        logger.LogInformation("Progress Bar request received");
        using var bitmap = new SKBitmap(568, 30);
        using var canvas = new SKCanvas(bitmap);

        using var barBackgroundPaint = new SKPaint();
        barBackgroundPaint.IsAntialias = true;
        barBackgroundPaint.Color = SKColors.White.WithAlpha((int)(.3 * 255));

        canvas.DrawRoundRect(0, bitmap.Height / 2f - 20 / 2f, 500, 20, 10, 10, barBackgroundPaint);

        var barWidth = (int)(500 * progressBar.Progress);
        if (barWidth > 0)
        {
            barWidth = barWidth < 20 ? 20 : barWidth;
            using var barPaint = new SKPaintSafe();
            barPaint.IsAntialias = true;
            barPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(barWidth, 0),
                [SKColor.Parse(progressBar.GradientColors[0]), SKColor.Parse(progressBar.GradientColors[1])],
                [0, 1],
                SKShaderTileMode.Repeat);

            canvas.DrawRoundRect(0, (bitmap.Height - 20) / 2f, barWidth, 20, 10, 10, barPaint);
        }

        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf");
        using var textPaint = new SKPaint();
        textPaint.IsAntialias = true;
        textPaint.Color = SKColors.White;
        textPaint.TextSize = 20;
        textPaint.Typeface = segoeFont;

        canvas.DrawAlignedText(
            progressBar.Text,
            new SKPoint(500 + 5, bitmap.Height / 2f),
            textPaint,
            verticalAlignment: VerticalTextAlignment.Center);

        if (progressBar.BarText != null)
        {
            using var barTextPaint = new SKPaint();
            barTextPaint.IsAntialias = true;
            barTextPaint.Color = SKColors.White;
            barTextPaint.TextSize = 15;
            barTextPaint.Typeface = segoeFont;

            canvas.DrawAlignedText(
                progressBar.BarText,
                SKRect.Create(0, 0, 500, bitmap.Height),
                barTextPaint,
                SKTextAlign.Center,
                VerticalTextAlignment.Center);
        }

        var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    [HttpPost("drop")]
    public async Task<IActionResult> GenerateDropImage(Drop drop)
    {
        logger.LogInformation("Drop Image request received | Locale = {DropLocale}", drop.Locale);

        var filePath = $"data/images/map/{drop.Locale}.png";
        if (!System.IO.File.Exists(filePath))
            return BadRequest("Map file doesn't exist.");

        using var bitmap = SKBitmap.Decode(filePath);
        using var canvas = new SKCanvas(bitmap);

        var markerAmount = Directory.EnumerateFiles("Assets/Images/Map/Markers", "*.png").Count();
        var markerBitmap =
            await assets.GetBitmap(
                $"Assets/Images/Map/Markers/{RandomNumberGenerator.GetInt32(markerAmount)}.png"); // don't dispose

        const int worldRadius = 80_000;
        const int xOffset = -12_200;
        const int yOffset = 3_200;

        var mx = (drop.X + worldRadius) / (worldRadius * 2f) * bitmap.Width + xOffset;
        var my = (drop.Y + worldRadius) / (worldRadius * 2f) * bitmap.Height + yOffset;

        canvas.DrawBitmap(markerBitmap, mx - markerBitmap!.Width / 2f, my - markerBitmap.Height);

        var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        return File(data.AsStream(true), "image/jpeg");
    }
}
