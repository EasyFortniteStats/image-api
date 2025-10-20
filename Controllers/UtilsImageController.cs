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
            using var barPaint = new SKPaint();
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
        using var textFont = new SKFont(segoeFont, 20);
        textPaint.IsAntialias = true;
        textPaint.Color = SKColors.White;

        textFont.MeasureText(progressBar.Text, out var textBounds);
        canvas.DrawText(progressBar.Text, 500 + 5, (float)bitmap.Height / 2 - textBounds.MidY, textFont, textPaint);

        if (progressBar.BarText != null)
        {
            using var barTextPaint = new SKPaint();
            using var barTextFont = new SKFont(segoeFont, 15);
            barTextPaint.IsAntialias = true;
            barTextPaint.Color = SKColors.White;

            barTextFont.MeasureText(progressBar.BarText, out textBounds);
            canvas.DrawText(progressBar.BarText, (500 - textBounds.Width) / 2,
                 bitmap.Height / 2f - textBounds.MidY, barTextFont, barTextPaint);
        }

        var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    [HttpPost("drop")]
    public async Task<IActionResult> GenerateDropImage(Drop drop)
    {
        logger.LogInformation("Drop Image request received");
        var mapBitmap =
            await assets.GetBitmap(
                $"data/images/map/{drop.Locale}.png"); // don't dispose TODO: Clear caching on bg change

        if (mapBitmap == null)
            return BadRequest("Map file doesn't exist.");

        var bitmap = new SKBitmap(mapBitmap.Width, mapBitmap.Height);
        using var canvas = new SKCanvas(bitmap);

        canvas.DrawBitmap(mapBitmap, 0, 0);

        var markerAmount = Directory.EnumerateFiles("Assets/Images/Map/Markers", "*.png").Count();
        var markerBitmap =
            await assets.GetBitmap(
                $"Assets/Images/Map/Markers/{RandomNumberGenerator.GetInt32(markerAmount - 1)}.png"); // don't dispose

        const int worldRadius = 150_000;
        const int xOffset = -60;
        const int yOffset = 0;

        var mx = (drop.X + worldRadius) / (worldRadius * 2f) * bitmap.Width + xOffset;
        var my = (drop.Y + worldRadius) / (worldRadius * 2f) * bitmap.Height + yOffset;

        canvas.DrawBitmap(markerBitmap, mx - (float)markerBitmap!.Width / 2, my - markerBitmap.Height);

        var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        return File(data.AsStream(true), "image/jpeg");
    }
}