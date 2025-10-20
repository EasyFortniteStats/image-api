using AsyncKeyedLock;
using EasyFortniteStats_ImageApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

// ReSharper disable InconsistentNaming
namespace EasyFortniteStats_ImageApi.Controllers;

[ApiController]
[Route("stats")]
public class StatsImageController(IMemoryCache cache, AsyncKeyedLocker<string> namedLock, SharedAssets assets, ILogger<StatsImageController> logger)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(Stats stats, StatsType type = StatsType.Normal)
    {
        logger.LogInformation("Stats image request received | Name = {PlayerName} | Type = {Type}", stats.PlayerName, type);
        if (type == StatsType.Normal && stats.Teams == null)
            return BadRequest("Normal stats type requested but no team stats were provided.");
        if (type == StatsType.Competitive && stats.Competitive == null)
            return BadRequest("Competitive stats type requested but no competitive stats were provided.");

        var backgroundHash = stats.BackgroundImagePath is not null ? $"_{stats.BackgroundImagePath.GetHashCode()}" : "";

        var lockName = $"stats_{type}{backgroundHash}_template_mutex";
        SKBitmap? templateBitmap;
        using (await namedLock.LockAsync(lockName).ConfigureAwait(false))
        {
            cache.TryGetValue($"stats_{type}{backgroundHash}_template_image", out templateBitmap);
            if (templateBitmap == null)
            {
                templateBitmap = await GenerateTemplate(stats, type);
                cache.Set($"stats_{type}{backgroundHash}_template_image", templateBitmap);
            }
        }

        using var templateCopy = templateBitmap.Copy();
        using var bitmap = await GenerateImage(stats, type, templateCopy);
        var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    private async Task<SKBitmap> GenerateTemplate(Stats stats, StatsType type)
    {
        var imageInfo = type == StatsType.Competitive ? new SKImageInfo(1505, 624) : new SKImageInfo(1505, 777);

        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var customBackgroundBitmap =
            await assets.GetBitmap("data/images/{0}",
                stats.BackgroundImagePath); // don't dispose TODO: Clear caching on bg change
        if (customBackgroundBitmap is null)
        {
            using var backgroundPaint = new SKPaint();
            backgroundPaint.IsAntialias = true;
            backgroundPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(imageInfo.Rect.MidX, imageInfo.Rect.MidY),
                MathF.Sqrt(MathF.Pow(imageInfo.Rect.MidX, 2) + MathF.Pow(imageInfo.Rect.MidY, 2)),
                [new SKColor(41, 165, 224), new SKColor(9, 66, 180)],
                SKShaderTileMode.Clamp);

            canvas.DrawRoundRect(0, 0, imageInfo.Width, imageInfo.Height, 50, 50, backgroundPaint);
        }
        else
        {
            using var backgroundImagePaint = new SKPaint();
            backgroundImagePaint.IsAntialias = true;
            backgroundImagePaint.FilterQuality = SKFilterQuality.Medium;

            if (customBackgroundBitmap.Width != imageInfo.Width || customBackgroundBitmap.Height != imageInfo.Height)
            {
                using var resizedCustomBackgroundBitmap =
                    customBackgroundBitmap.Resize(imageInfo, SKFilterQuality.Medium);
                backgroundImagePaint.Shader = SKShader.CreateBitmap(resizedCustomBackgroundBitmap,
                    SKShaderTileMode.Clamp, SKShaderTileMode.Repeat);
            }
            else
                backgroundImagePaint.Shader = SKShader.CreateBitmap(customBackgroundBitmap, SKShaderTileMode.Clamp,
                    SKShaderTileMode.Repeat);

            canvas.DrawRoundRect(0, 0, imageInfo.Width, imageInfo.Height, 50, 50, backgroundImagePaint);
        }

        using var nameSplit = new SKPaint();
        nameSplit.IsAntialias = true;
        nameSplit.Color = SKColors.Gray;

        canvas.DrawRoundRect(134, 57, 5, 50, 3, 3, nameSplit);

        using var boxPaint = new SKPaint();
        boxPaint.IsAntialias = true;
        boxPaint.Color = SKColors.White.WithAlpha((int)(.2 * 255));

        var fortniteFont = await assets.GetFont("Assets/Fonts/Fortnite.ttf"); // don't dispose
        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf"); // don't dispose

        using var competitiveBoxTitlePaint = new SKPaint();
        competitiveBoxTitlePaint.IsAntialias = true;
        competitiveBoxTitlePaint.Color = SKColors.White;
        competitiveBoxTitlePaint.Typeface = fortniteFont;
        competitiveBoxTitlePaint.TextSize = 25;

        using var boxTitlePaint = new SKPaint();
        boxTitlePaint.IsAntialias = true;
        boxTitlePaint.Color = SKColors.White;
        boxTitlePaint.Typeface = fortniteFont;
        boxTitlePaint.TextSize = 50;

        using var titlePaint = new SKPaint();
        titlePaint.IsAntialias = true;
        titlePaint.Color = SKColors.LightGray;
        titlePaint.Typeface = segoeFont;
        titlePaint.TextSize = 20;

        var textBounds = new SKRect();

        if (type == StatsType.Competitive)
        {
            var overallBoxRect = new SKRoundRect(SKRect.Create(50, 159, 437, 415), 30);
            DrawBlurredRoundRect(bitmap, overallBoxRect);
            canvas.DrawRoundRect(overallBoxRect, boxPaint);

            using var overlayBoxPaint = new SKPaint();
            overlayBoxPaint.IsAntialias = true;
            overlayBoxPaint.Color = SKColors.White.WithAlpha((int)(.2 * 255));

            var upperBoxRect = SKRect.Create(49, 159, 437, 158);
            var upperBox = new SKRoundRect(upperBoxRect);
            upperBox.SetRectRadii(upperBoxRect,
                [new SKPoint(30, 30), new SKPoint(30, 30), new SKPoint(0, 0), new SKPoint(0, 0)]);
            canvas.DrawRoundRect(upperBox, overlayBoxPaint);

            using var splitPaint = new SKPaint();
            splitPaint.IsAntialias = true;
            splitPaint.Color = SKColors.White.WithAlpha((int)(.5 * 255));
            canvas.DrawRoundRect(267, 192, 1, 77, 1, 1, splitPaint);

            var buildLogo = await assets.GetBitmap("Assets/Images/Stats/BuildLogo.png"); // don't dispose
            canvas.DrawBitmap(buildLogo, new SKPoint(115, 277));

            var zeroBuildLogo = await assets.GetBitmap("Assets/Images/Stats/ZeroBuildLogo.png"); // don't dispose
            canvas.DrawBitmap(zeroBuildLogo, new SKPoint(317, 277));

            competitiveBoxTitlePaint.MeasureText("OVERALL", ref textBounds);
            canvas.DrawText("OVERALL", 211, 305 - textBounds.Top, competitiveBoxTitlePaint);

            titlePaint.MeasureText("Earnings", ref textBounds);
            canvas.DrawText("Earnings", 70, 338 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Power Ranking", ref textBounds);
            canvas.DrawText("Power Ranking", 250, 338 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Games", ref textBounds);
            canvas.DrawText("Games", 70, 414 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Wins", ref textBounds);
            canvas.DrawText("Wins", 231, 414 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Win%", ref textBounds);
            canvas.DrawText("Win%", 370, 414 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Kills", ref textBounds);
            canvas.DrawText("Kills", 70, 491 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("K/D", ref textBounds);
            canvas.DrawText("K/D", 231, 491 - textBounds.Top, titlePaint);
        }
        else
        {
            var overallBoxRect = new SKRoundRect(SKRect.Create(50, 159, 437, 568), 30);
            DrawBlurredRoundRect(bitmap, overallBoxRect);
            canvas.DrawRoundRect(overallBoxRect, boxPaint);

            boxTitlePaint.MeasureText("OVERALL", ref textBounds);
            canvas.DrawText("OVERALL", 60, 134 - textBounds.Top, boxTitlePaint);

            titlePaint.MeasureText("Games", ref textBounds);
            canvas.DrawText("Games", 70, 184 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Wins", ref textBounds);
            canvas.DrawText("Wins", 231, 184 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Win%", ref textBounds);
            canvas.DrawText("Win%", 370, 184 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Kills", ref textBounds);
            canvas.DrawText("Kills", 70, 261 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("K/D", ref textBounds);
            canvas.DrawText("K/D", 231, 261 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Playtime since Season 7", ref textBounds);
            canvas.DrawText("Playtime since Season 7", 70, 338 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("days", ref textBounds);
            canvas.DrawText("days", 70, 397 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("hours", ref textBounds);
            canvas.DrawText("hours", 147, 397 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("minutes", ref textBounds);
            canvas.DrawText("minutes", 231, 397 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("BattlePass Level", ref textBounds);
            canvas.DrawText("BattlePass Level", 70, 442 - textBounds.Top, titlePaint);

            using var battlePassBarBackgroundPaint = new SKPaint();
            battlePassBarBackgroundPaint.IsAntialias = true;
            battlePassBarBackgroundPaint.Color = SKColors.White.WithAlpha((int)(.3 * 255));
            canvas.DrawRoundRect(158, 483, 309, 20, 10, 10, battlePassBarBackgroundPaint);
        }

        // Solo
        var soloBoxRect = new SKRoundRect(SKRect.Create(517, 159, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, soloBoxRect);
        canvas.DrawRoundRect(soloBoxRect, boxPaint);

        boxTitlePaint.MeasureText("SOLO", ref textBounds);
        canvas.DrawText("SOLO", 527, 134 - textBounds.Top, boxTitlePaint);

        var soloIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/solo.png"); // don't dispose
        canvas.DrawBitmap(soloIcon, new SKPoint(648, 134));

        titlePaint.MeasureText("Games", ref textBounds);
        canvas.DrawText("Games", 537, 184 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Wins", ref textBounds);
        canvas.DrawText("Wins", 698, 184 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Win%", ref textBounds);
        canvas.DrawText("Win%", 837, 184 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Kills", ref textBounds);
        canvas.DrawText("Kills", 537, 261 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("K/D", ref textBounds);
        canvas.DrawText("K/D", 698, 261 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Top 25", ref textBounds);
        canvas.DrawText("Top 25", 837, 261 - textBounds.Top, titlePaint);

        // Duos
        var duosBoxRect = new SKRoundRect(SKRect.Create(996, 159, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, duosBoxRect);
        canvas.DrawRoundRect(duosBoxRect, boxPaint);

        boxTitlePaint.MeasureText("DUOS", ref textBounds);
        canvas.DrawText("DUOS", 1006, 134 - textBounds.Top, boxTitlePaint);

        var duosIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/duos.png"); // don't dispose
        canvas.DrawBitmap(duosIcon, new SKPoint(1133, 134));

        titlePaint.MeasureText("Games", ref textBounds);
        canvas.DrawText("Games", 1016, 184 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Wins", ref textBounds);
        canvas.DrawText("Wins", 1177, 184 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Win%", ref textBounds);
        canvas.DrawText("Win%", 1316, 184 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Kills", ref textBounds);
        canvas.DrawText("Kills", 1016, 261 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("K/D", ref textBounds);
        canvas.DrawText("K/D", 1177, 261 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Top 12", ref textBounds);
        canvas.DrawText("Top 12", 1316, 261 - textBounds.Top, titlePaint);

        // Trios
        var triosBoxRect = new SKRoundRect(SKRect.Create(517, 389, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, triosBoxRect);
        canvas.DrawRoundRect(triosBoxRect, boxPaint);

        boxTitlePaint.MeasureText("TRIOS", ref textBounds);
        canvas.DrawText("TRIOS", 527, 364 - textBounds.Top, boxTitlePaint);

        var triosIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/trios.png"); // don't dispose
        canvas.DrawBitmap(triosIcon, new SKPoint(663, 364));

        titlePaint.MeasureText("Games", ref textBounds);
        canvas.DrawText("Games", 537, 414 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Wins", ref textBounds);
        canvas.DrawText("Wins", 698, 414 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Win%", ref textBounds);
        canvas.DrawText("Win%", 837, 414 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Kills", ref textBounds);
        canvas.DrawText("Kills", 537, 491 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("K/D", ref textBounds);
        canvas.DrawText("K/D", 698, 491 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Top 6", ref textBounds);
        canvas.DrawText("Top 6", 837, 491 - textBounds.Top, titlePaint);

        // Squads
        var squadsBoxRect = new SKRoundRect(SKRect.Create(996, 389, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, squadsBoxRect);
        canvas.DrawRoundRect(squadsBoxRect, boxPaint);

        boxTitlePaint.MeasureText("SQUADS", ref textBounds);
        canvas.DrawText("SQUADS", 1006, 364 - textBounds.Top, boxTitlePaint);

        var squadsIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/squads.png"); // don't dispose
        canvas.DrawBitmap(squadsIcon, new SKPoint(1191, 364));

        titlePaint.MeasureText("Games", ref textBounds);
        canvas.DrawText("Games", 1016, 414 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Wins", ref textBounds);
        canvas.DrawText("Wins", 1177, 414 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Win%", ref textBounds);
        canvas.DrawText("Win%", 1316, 414 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Kills", ref textBounds);
        canvas.DrawText("Kills", 1016, 491 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("K/D", ref textBounds);
        canvas.DrawText("K/D", 1177, 491 - textBounds.Top, titlePaint);

        titlePaint.MeasureText("Top 6", ref textBounds);
        canvas.DrawText("Top 6", 1316, 491 - textBounds.Top, titlePaint);

        if (type == StatsType.Normal)
        {
            // Teams
            var teamsBoxRect = new SKRoundRect(SKRect.Create(517, 619, 938, 108), 30);
            DrawBlurredRoundRect(bitmap, teamsBoxRect);
            canvas.DrawRoundRect(teamsBoxRect, boxPaint);

            boxTitlePaint.MeasureText("TEAMS", ref textBounds);
            canvas.DrawText("TEAMS", 527, 594 - textBounds.Top, boxTitlePaint);

            var teamsIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/teams.png"); // don't dispose
            canvas.DrawBitmap(teamsIcon, new SKPoint(683, 594));

            titlePaint.MeasureText("Games", ref textBounds);
            canvas.DrawText("Games", 537, 644 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Wins", ref textBounds);
            canvas.DrawText("Wins", 698, 644 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Win%", ref textBounds);
            canvas.DrawText("Win%", 837, 644 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("Kills", ref textBounds);
            canvas.DrawText("Kills", 954, 644 - textBounds.Top, titlePaint);

            titlePaint.MeasureText("K/D", ref textBounds);
            canvas.DrawText("K/D", 1115, 644 - textBounds.Top, titlePaint);
        }

        return bitmap;
    }

    private async Task<SKBitmap> GenerateImage(Stats stats, StatsType type, SKBitmap templateBitmap)
    {
        var imageInfo = new SKImageInfo(templateBitmap.Width, templateBitmap.Height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

        var fortniteFont = await assets.GetFont("Assets/Fonts/Fortnite.ttf"); // don't dispose
        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf"); // don't dispose

        using var namePaint = new SKPaint();
        namePaint.IsAntialias = true;
        namePaint.Color = SKColors.White;
        namePaint.Typeface = segoeFont;
        namePaint.TextSize = 64;
        namePaint.FilterQuality = SKFilterQuality.Medium;

        using var titlePaint = new SKPaint();
        titlePaint.IsAntialias = true;
        titlePaint.Color = SKColors.LightGray;
        titlePaint.Typeface = segoeFont;
        titlePaint.TextSize = 20;
        titlePaint.FilterQuality = SKFilterQuality.Medium;

        using var valuePaint = new SKPaint();
        valuePaint.IsAntialias = true;
        valuePaint.Color = SKColors.White;
        valuePaint.Typeface = fortniteFont;
        valuePaint.TextSize = 35;
        valuePaint.FilterQuality = SKFilterQuality.Medium;

        using var divisionPaint = new SKPaint();
        divisionPaint.IsAntialias = true;
        divisionPaint.Color = SKColors.White;
        divisionPaint.Typeface = fortniteFont;
        divisionPaint.TextSize = 35;
        divisionPaint.FilterQuality = SKFilterQuality.Medium;

        using var rankProgressPaint = new SKPaint();
        rankProgressPaint.IsAntialias = true;
        rankProgressPaint.Color = SKColors.White.WithAlpha((int)(255 * 0.7));
        rankProgressPaint.Typeface = segoeFont;
        rankProgressPaint.TextSize = 16;
        rankProgressPaint.FilterQuality = SKFilterQuality.Medium;

        using var rankingPaint = new SKPaint();
        rankingPaint.IsAntialias = true;
        rankingPaint.Color = SKColors.White;
        rankingPaint.Typeface = fortniteFont;
        rankingPaint.TextSize = 20;
        rankingPaint.FilterQuality = SKFilterQuality.Medium;

        var textBounds = new SKRect();

        var inputIcon =
            await assets.GetBitmap($"Assets/Images/Stats/InputTypes/{stats.InputType}.png"); // don't dispose
        canvas.DrawBitmap(inputIcon, 50, 50);

        namePaint.MeasureText(stats.PlayerName, ref textBounds);
        canvas.DrawText(stats.PlayerName, 159, 58 - textBounds.Top, namePaint);

        if (stats.IsVerified)
        {
            var verifiedIcon = await assets.GetBitmap("Assets/Images/Stats/Verified.png"); // don't dispose
            canvas.DrawBitmap(verifiedIcon, 159 + textBounds.Width + 5, 47);

            using var discordBoxBitmap = await ImageUtils.GenerateDiscordBox(assets, stats.UserName ?? "???#0000");
            canvas.DrawBitmap(discordBoxBitmap, imageInfo.Width - 50 - discordBoxBitmap.Width, 39);
        }

        if (type == StatsType.Competitive)
        {
            var rankedTypeX = new Dictionary<RankedType, int>
            {
                {RankedType.BatteRoyale, 151},
                {RankedType.ZeroBuild, 379},
            };
            foreach (var rankedStatsEntry in stats.Competitive!.RankedStatsEntries)
            {
                var x = rankedTypeX[rankedStatsEntry.RankingType];
                var divisionAssetName = rankedStatsEntry.isUnranked()
                    ? "Unranked"
                    : rankedStatsEntry.CurrentDivision.ToString();
                var divisionIconBitmap =
                    await assets.GetBitmap(
                        $"Assets/Images/Stats/DivisionIcons/{divisionAssetName}.png"); // don't dispose
                canvas.DrawBitmap(divisionIconBitmap, x - divisionIconBitmap!.Width / 2f, 109);

                divisionPaint.MeasureText(rankedStatsEntry.CurrentDivisionName, ref textBounds);
                canvas.DrawText(rankedStatsEntry.CurrentDivisionName, x - (int)(textBounds.Width / 2),
                    206 - textBounds.Top, divisionPaint);

                if (rankedStatsEntry.Ranking == null)
                {
                    const int maxBarWidth = 130, barHeight = 6;
                    var progressText = $"{(int)(rankedStatsEntry.Progress * 100)}%";
                    rankProgressPaint.MeasureText(progressText, ref textBounds);
                    var barX = x - textBounds.Width / 2f - maxBarWidth / 2f;

                    using var barBackgroundPaint = new SKPaint();
                    barBackgroundPaint.IsAntialias = true;
                    barBackgroundPaint.Color = SKColors.White.WithAlpha((int)(.2 * 255));
                    canvas.DrawRoundRect(barX, 250, maxBarWidth, barHeight, 10, 10, barBackgroundPaint);

                    var rankProgressBarWidth = (int)(maxBarWidth * rankedStatsEntry.Progress);
                    if (rankProgressBarWidth > 0)
                    {
                        rankProgressBarWidth = Math.Max(rankProgressBarWidth, barHeight);
                        using var battlePassBarPaint = new SKPaint();
                        battlePassBarPaint.IsAntialias = true;
                        battlePassBarPaint.Shader = SKShader.CreateLinearGradient(
                            new SKPoint(barX, 0),
                            new SKPoint(barX + rankProgressBarWidth, 0),
                            [
                                SKColor.Parse(stats.BattlePassLevelBarColors[0]),
                                SKColor.Parse(stats.BattlePassLevelBarColors[1])
                            ],
                            [0, 1],
                            SKShaderTileMode.Repeat);
                        canvas.DrawRoundRect(barX, 250, rankProgressBarWidth, barHeight, 10, 10, battlePassBarPaint);
                    }

                    canvas.DrawText(progressText, barX + maxBarWidth + 7, 247 - textBounds.Top, rankProgressPaint);
                }
                else
                {
                    rankingPaint.MeasureText(rankedStatsEntry.Ranking, ref textBounds);
                    canvas.DrawText(rankedStatsEntry.Ranking, x - (int)(textBounds.Width / 2), 245 - textBounds.Top,
                        rankingPaint);
                }
            }

            valuePaint.MeasureText(stats.Competitive.Earnings, ref textBounds);
            canvas.DrawText(stats.Competitive.Earnings, 70, 365 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Competitive.PowerRanking, ref textBounds);
            canvas.DrawText(stats.Competitive.PowerRanking, 250, 365 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.MatchesPlayed, ref textBounds);
            canvas.DrawText(stats.Overall.MatchesPlayed, 70, 441 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.Wins, ref textBounds);
            canvas.DrawText(stats.Overall.Wins, 231, 441 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.WinRatio, ref textBounds);
            canvas.DrawText(stats.Overall.WinRatio, 370, 441 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.Kills, ref textBounds);
            canvas.DrawText(stats.Overall.Kills, 70, 518 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.KD, ref textBounds);
            canvas.DrawText(stats.Overall.KD, 231, 518 - textBounds.Top, valuePaint);
        }
        else
        {
            valuePaint.MeasureText(stats.Overall.MatchesPlayed, ref textBounds);
            canvas.DrawText(stats.Overall.MatchesPlayed, 70, 211 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.Wins, ref textBounds);
            canvas.DrawText(stats.Overall.Wins, 231, 211 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.WinRatio, ref textBounds);
            canvas.DrawText(stats.Overall.WinRatio, 370, 211 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.Kills, ref textBounds);
            canvas.DrawText(stats.Overall.Kills, 70, 288 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Overall.KD, ref textBounds);
            canvas.DrawText(stats.Overall.KD, 231, 288 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Playtime.Days, ref textBounds);
            canvas.DrawText(stats.Playtime.Days, 70, 369 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Playtime.Hours, ref textBounds);
            canvas.DrawText(stats.Playtime.Hours, 147, 369 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Playtime.Minutes, ref textBounds);
            canvas.DrawText(stats.Playtime.Minutes, 213, 369 - textBounds.Top, valuePaint);

            var battlePassLevel = ((int)stats.BattlePassLevel).ToString();
            valuePaint.MeasureText(battlePassLevel, ref textBounds);
            canvas.DrawText(battlePassLevel, 70, 479 - textBounds.Top, valuePaint);

            const int maxBarWidth = 309, barHeight = 20;

            var battlePassBarWidth = (int)(maxBarWidth * (stats.BattlePassLevel - (int)stats.BattlePassLevel));
            if (battlePassBarWidth > 0)
            {
                battlePassBarWidth = Math.Max(battlePassBarWidth, barHeight);
                using var battlePassBarPaint = new SKPaint();
                battlePassBarPaint.IsAntialias = true;
                battlePassBarPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(158, 0),
                    new SKPoint(158 + battlePassBarWidth, 0),
                    [
                        SKColor.Parse(stats.BattlePassLevelBarColors[0]),
                        SKColor.Parse(stats.BattlePassLevelBarColors[1])
                    ],
                    [0, 1],
                    SKShaderTileMode.Repeat);

                canvas.DrawRoundRect(158, 483, battlePassBarWidth, barHeight, 10, 10, battlePassBarPaint);
            }
        }

        valuePaint.MeasureText(stats.Solo.MatchesPlayed, ref textBounds);
        canvas.DrawText(stats.Solo.MatchesPlayed, 537, 211 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Solo.Wins, ref textBounds);
        canvas.DrawText(stats.Solo.Wins, 698, 211 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Solo.WinRatio, ref textBounds);
        canvas.DrawText(stats.Solo.WinRatio, 837, 211 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Solo.Kills, ref textBounds);
        canvas.DrawText(stats.Solo.Kills, 537, 288 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Solo.KD, ref textBounds);
        canvas.DrawText(stats.Solo.KD, 698, 288 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Solo.Top25, ref textBounds);
        canvas.DrawText(stats.Solo.Top25, 837, 288 - textBounds.Top, valuePaint);


        valuePaint.MeasureText(stats.Duos.MatchesPlayed, ref textBounds);
        canvas.DrawText(stats.Duos.MatchesPlayed, 1016, 211 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Duos.Wins, ref textBounds);
        canvas.DrawText(stats.Duos.Wins, 1177, 211 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Duos.WinRatio, ref textBounds);
        canvas.DrawText(stats.Duos.WinRatio, 1316, 211 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Duos.Kills, ref textBounds);
        canvas.DrawText(stats.Duos.Kills, 1016, 288 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Duos.KD, ref textBounds);
        canvas.DrawText(stats.Duos.KD, 1177, 288 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Duos.Top12, ref textBounds);
        canvas.DrawText(stats.Duos.Top12, 1316, 288 - textBounds.Top, valuePaint);


        valuePaint.MeasureText(stats.Trios.MatchesPlayed, ref textBounds);
        canvas.DrawText(stats.Trios.MatchesPlayed, 537, 441 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Trios.Wins, ref textBounds);
        canvas.DrawText(stats.Trios.Wins, 698, 441 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Trios.WinRatio, ref textBounds);
        canvas.DrawText(stats.Trios.WinRatio, 837, 441 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Trios.Kills, ref textBounds);
        canvas.DrawText(stats.Trios.Kills, 537, 518 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Trios.KD, ref textBounds);
        canvas.DrawText(stats.Trios.KD, 698, 518 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Trios.Top6, ref textBounds);
        canvas.DrawText(stats.Trios.Top6, 837, 518 - textBounds.Top, valuePaint);


        valuePaint.MeasureText(stats.Squads.MatchesPlayed, ref textBounds);
        canvas.DrawText(stats.Squads.MatchesPlayed, 1016, 441 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Squads.Wins, ref textBounds);
        canvas.DrawText(stats.Squads.Wins, 1177, 441 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Squads.WinRatio, ref textBounds);
        canvas.DrawText(stats.Squads.WinRatio, 1316, 441 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Squads.Kills, ref textBounds);
        canvas.DrawText(stats.Squads.Kills, 1016, 518 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Squads.KD, ref textBounds);
        canvas.DrawText(stats.Squads.KD, 1177, 518 - textBounds.Top, valuePaint);

        valuePaint.MeasureText(stats.Squads.Top6, ref textBounds);
        canvas.DrawText(stats.Squads.Top6, 1316, 518 - textBounds.Top, valuePaint);

        if (type == StatsType.Normal && stats.Teams != null)
        {
            valuePaint.MeasureText(stats.Teams.MatchesPlayed, ref textBounds);
            canvas.DrawText(stats.Teams.MatchesPlayed, 537, 671 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Teams.Wins, ref textBounds);
            canvas.DrawText(stats.Teams.Wins, 698, 671 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Teams.WinRatio, ref textBounds);
            canvas.DrawText(stats.Teams.WinRatio, 837, 671 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Teams.Kills, ref textBounds);
            canvas.DrawText(stats.Teams.Kills, 954, 671 - textBounds.Top, valuePaint);

            valuePaint.MeasureText(stats.Teams.KD, ref textBounds);
            canvas.DrawText(stats.Teams.KD, 1115, 671 - textBounds.Top, valuePaint);
        }

        return bitmap;
    }

    private static void DrawBlurredRoundRect(SKBitmap bitmap, SKRoundRect rect)
    {
        using var canvas = new SKCanvas(bitmap);

        canvas.ClipRoundRect(rect, antialias: true);

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.ImageFilter = SKImageFilter.CreateBlur(5, 5);

        canvas.DrawBitmap(bitmap, 0, 0, paint);
    }
}