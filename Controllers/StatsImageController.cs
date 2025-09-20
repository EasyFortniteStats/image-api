using AsyncKeyedLock;
using EasyFortniteStats_ImageApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

// ReSharper disable InconsistentNaming
namespace EasyFortniteStats_ImageApi.Controllers;

[ApiController]
[Route("stats")]
public class StatsImageController(IMemoryCache cache, AsyncKeyedLocker<string> namedLock, SharedAssets assets)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(Stats stats, StatsType type = StatsType.Normal)
    {
        Console.WriteLine($"Stats image request | Name = {stats.PlayerName} | Type = {type}");
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

            if (customBackgroundBitmap.Width != imageInfo.Width || customBackgroundBitmap.Height != imageInfo.Height)
            {
                using var resizedCustomBackgroundBitmap =
                    customBackgroundBitmap.Resize(imageInfo, SKSamplingOptions.Default);
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
        boxPaint.Color = SKColors.White.WithAlpha((int) (.2 * 255));

        var fortniteFont = await assets.GetFont("Assets/Fonts/Fortnite.ttf"); // don't dispose
        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf"); // don't dispose

        using var competitiveBoxTitlePaint = new SKPaint();
        using var competitiveBoxTitleFont = new SKFont(fortniteFont, 25);
        competitiveBoxTitlePaint.IsAntialias = true;
        competitiveBoxTitlePaint.Color = SKColors.White;

        using var boxTitlePaint = new SKPaint();
        using var boxTitleFont = new SKFont(fortniteFont, 50);
        boxTitlePaint.IsAntialias = true;
        boxTitlePaint.Color = SKColors.White;

        using var titlePaint = new SKPaint();
        using var titleFont = new SKFont(segoeFont, 20);
        titlePaint.IsAntialias = true;
        titlePaint.Color = SKColors.LightGray;

        SKRect textBounds;

        if (type == StatsType.Competitive)
        {
            var overallBoxRect = new SKRoundRect(SKRect.Create(50, 159, 437, 415), 30);
            DrawBlurredRoundRect(bitmap, overallBoxRect);
            canvas.DrawRoundRect(overallBoxRect, boxPaint);

            using var overlayBoxPaint = new SKPaint();
            overlayBoxPaint.IsAntialias = true;
            overlayBoxPaint.Color = SKColors.White.WithAlpha((int) (.2 * 255));

            var upperBoxRect = SKRect.Create(49, 159, 437, 158);
            var upperBox = new SKRoundRect(upperBoxRect);
            upperBox.SetRectRadii(upperBoxRect,
                [new SKPoint(30, 30), new SKPoint(30, 30), new SKPoint(0, 0), new SKPoint(0, 0)]);
            canvas.DrawRoundRect(upperBox, overlayBoxPaint);

            using var splitPaint = new SKPaint();
            splitPaint.IsAntialias = true;
            splitPaint.Color = SKColors.White.WithAlpha((int) (.5 * 255));
            canvas.DrawRoundRect(267, 192, 1, 77, 1, 1, splitPaint);

            var buildLogo = await assets.GetBitmap("Assets/Images/Stats/BuildLogo.png"); // don't dispose
            canvas.DrawBitmap(buildLogo, new SKPoint(115, 277));

            var zeroBuildLogo = await assets.GetBitmap("Assets/Images/Stats/ZeroBuildLogo.png"); // don't dispose
            canvas.DrawBitmap(zeroBuildLogo, new SKPoint(317, 277));

            competitiveBoxTitleFont.MeasureText("OVERALL", out textBounds);
            canvas.DrawText("OVERALL", 211, 305 - textBounds.Top, competitiveBoxTitleFont, competitiveBoxTitlePaint);

            titleFont.MeasureText("Earnings", out textBounds);
            canvas.DrawText("Earnings", 70, 338 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Power Ranking", out textBounds);
            canvas.DrawText("Power Ranking", 250, 338 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Games", out textBounds);
            canvas.DrawText("Games", 70, 414 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Wins", out textBounds);
            canvas.DrawText("Wins", 231, 414 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Win%", out textBounds);
            canvas.DrawText("Win%", 370, 414 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Kills", out textBounds);
            canvas.DrawText("Kills", 70, 491 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("K/D", out textBounds);
            canvas.DrawText("K/D", 231, 491 - textBounds.Top, titleFont, titlePaint);
        }
        else
        {
            var overallBoxRect = new SKRoundRect(SKRect.Create(50, 159, 437, 568), 30);
            DrawBlurredRoundRect(bitmap, overallBoxRect);
            canvas.DrawRoundRect(overallBoxRect, boxPaint);

            boxTitleFont.MeasureText("OVERALL", out textBounds);
            canvas.DrawText("OVERALL", 60, 134 - textBounds.Top, boxTitleFont, boxTitlePaint);

            titleFont.MeasureText("Games", out textBounds);
            canvas.DrawText("Games", 70, 184 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Wins", out textBounds);
            canvas.DrawText("Wins", 231, 184 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Win%", out textBounds);
            canvas.DrawText("Win%", 370, 184 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Kills", out textBounds);
            canvas.DrawText("Kills", 70, 261 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("K/D", out textBounds);
            canvas.DrawText("K/D", 231, 261 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Playtime since Season 7", out textBounds);
            canvas.DrawText("Playtime since Season 7", 70, 338 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("days", out textBounds);
            canvas.DrawText("days", 70, 397 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("hours", out textBounds);
            canvas.DrawText("hours", 147, 397 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("minutes", out textBounds);
            canvas.DrawText("minutes", 231, 397 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("BattlePass Level", out textBounds);
            canvas.DrawText("BattlePass Level", 70, 442 - textBounds.Top, titleFont, titlePaint);

            using var battlePassBarBackgroundPaint = new SKPaint();
            battlePassBarBackgroundPaint.IsAntialias = true;
            battlePassBarBackgroundPaint.Color = SKColors.White.WithAlpha((int) (.3 * 255));
            canvas.DrawRoundRect(158, 483, 309, 20, 10, 10, battlePassBarBackgroundPaint);
        }

        // Solo
        var soloBoxRect = new SKRoundRect(SKRect.Create(517, 159, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, soloBoxRect);
        canvas.DrawRoundRect(soloBoxRect, boxPaint);

        boxTitleFont.MeasureText("SOLO", out textBounds);
        canvas.DrawText("SOLO", 527, 134 - textBounds.Top, boxTitleFont, boxTitlePaint);

        var soloIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/solo.png"); // don't dispose
        canvas.DrawBitmap(soloIcon, new SKPoint(648, 134));

        titleFont.MeasureText("Games", out textBounds);
        canvas.DrawText("Games", 537, 184 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Wins", out textBounds);
        canvas.DrawText("Wins", 698, 184 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Win%", out textBounds);
        canvas.DrawText("Win%", 837, 184 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Kills", out textBounds);
        canvas.DrawText("Kills", 537, 261 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("K/D", out textBounds);
        canvas.DrawText("K/D", 698, 261 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Top 25", out textBounds);
        canvas.DrawText("Top 25", 837, 261 - textBounds.Top, titleFont, titlePaint);

        // Duos
        var duosBoxRect = new SKRoundRect(SKRect.Create(996, 159, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, duosBoxRect);
        canvas.DrawRoundRect(duosBoxRect, boxPaint);

        boxTitleFont.MeasureText("DUOS", out textBounds);
        canvas.DrawText("DUOS", 1006, 134 - textBounds.Top, boxTitleFont, boxTitlePaint);

        var duosIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/duos.png"); // don't dispose
        canvas.DrawBitmap(duosIcon, new SKPoint(1133, 134));

        titleFont.MeasureText("Games", out textBounds);
        canvas.DrawText("Games", 1016, 184 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Wins", out textBounds);
        canvas.DrawText("Wins", 1177, 184 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Win%", out textBounds);
        canvas.DrawText("Win%", 1316, 184 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Kills", out textBounds);
        canvas.DrawText("Kills", 1016, 261 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("K/D", out textBounds);
        canvas.DrawText("K/D", 1177, 261 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Top 12", out textBounds);
        canvas.DrawText("Top 12", 1316, 261 - textBounds.Top, titleFont, titlePaint);

        // Trios
        var triosBoxRect = new SKRoundRect(SKRect.Create(517, 389, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, triosBoxRect);
        canvas.DrawRoundRect(triosBoxRect, boxPaint);

        boxTitleFont.MeasureText("TRIOS", out textBounds);
        canvas.DrawText("TRIOS", 527, 364 - textBounds.Top, boxTitleFont, boxTitlePaint);

        var triosIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/trios.png"); // don't dispose
        canvas.DrawBitmap(triosIcon, new SKPoint(663, 364));

        titleFont.MeasureText("Games", out textBounds);
        canvas.DrawText("Games", 537, 414 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Wins", out textBounds);
        canvas.DrawText("Wins", 698, 414 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Win%", out textBounds);
        canvas.DrawText("Win%", 837, 414 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Kills", out textBounds);
        canvas.DrawText("Kills", 537, 491 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("K/D", out textBounds);
        canvas.DrawText("K/D", 698, 491 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Top 6", out textBounds);
        canvas.DrawText("Top 6", 837, 491 - textBounds.Top, titleFont, titlePaint);

        // Squads
        var squadsBoxRect = new SKRoundRect(SKRect.Create(996, 389, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, squadsBoxRect);
        canvas.DrawRoundRect(squadsBoxRect, boxPaint);

        boxTitleFont.MeasureText("SQUADS", out textBounds);
        canvas.DrawText("SQUADS", 1006, 364 - textBounds.Top, boxTitleFont, boxTitlePaint);

        var squadsIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/squads.png"); // don't dispose
        canvas.DrawBitmap(squadsIcon, new SKPoint(1191, 364));

        titleFont.MeasureText("Games", out textBounds);
        canvas.DrawText("Games", 1016, 414 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Wins", out textBounds);
        canvas.DrawText("Wins", 1177, 414 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Win%", out textBounds);
        canvas.DrawText("Win%", 1316, 414 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Kills", out textBounds);
        canvas.DrawText("Kills", 1016, 491 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("K/D", out textBounds);
        canvas.DrawText("K/D", 1177, 491 - textBounds.Top, titleFont, titlePaint);

        titleFont.MeasureText("Top 6", out textBounds);
        canvas.DrawText("Top 6", 1316, 491 - textBounds.Top, titleFont, titlePaint);

        if (type == StatsType.Normal)
        {
            // Teams
            var teamsBoxRect = new SKRoundRect(SKRect.Create(517, 619, 938, 108), 30);
            DrawBlurredRoundRect(bitmap, teamsBoxRect);
            canvas.DrawRoundRect(teamsBoxRect, boxPaint);

            boxTitleFont.MeasureText("TEAMS", out textBounds);
            canvas.DrawText("TEAMS", 527, 594 - textBounds.Top, boxTitleFont, boxTitlePaint);

            var teamsIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/teams.png"); // don't dispose
            canvas.DrawBitmap(teamsIcon, new SKPoint(683, 594));

            titleFont.MeasureText("Games", out textBounds);
            canvas.DrawText("Games", 537, 644 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Wins", out textBounds);
            canvas.DrawText("Wins", 698, 644 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Win%", out textBounds);
            canvas.DrawText("Win%", 837, 644 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("Kills", out textBounds);
            canvas.DrawText("Kills", 954, 644 - textBounds.Top, titleFont, titlePaint);

            titleFont.MeasureText("K/D", out textBounds);
            canvas.DrawText("K/D", 1115, 644 - textBounds.Top, titleFont, titlePaint);
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
        using var nameFont = new SKFont(segoeFont, 64);
        namePaint.IsAntialias = true;
        namePaint.Color = SKColors.White;

        using var titlePaint = new SKPaint();
        using var titleFont = new SKFont(segoeFont, 20);
        titlePaint.IsAntialias = true;
        titlePaint.Color = SKColors.LightGray;

        using var valuePaint = new SKPaint();
        using var valueFont = new SKFont(fortniteFont, 35);
        valuePaint.IsAntialias = true;
        valuePaint.Color = SKColors.White;

        using var divisionPaint = new SKPaint();
        using var divisionFont = new SKFont(fortniteFont, 35);
        divisionPaint.IsAntialias = true;
        divisionPaint.Color = SKColors.White;

        using var rankProgressPaint = new SKPaint();
        using var rankProgressFont = new SKFont(segoeFont, 16);
        rankProgressPaint.IsAntialias = true;
        rankProgressPaint.Color = SKColors.White.WithAlpha((int) (255 * 0.7));

        using var rankingPaint = new SKPaint();
        using var rankingFont = new SKFont(fortniteFont, 20);
        rankingPaint.IsAntialias = true;
        rankingPaint.Color = SKColors.White;

        var inputIcon =
            await assets.GetBitmap($"Assets/Images/Stats/InputTypes/{stats.InputType}.png"); // don't dispose
        canvas.DrawBitmap(inputIcon, 50, 50);

        nameFont.MeasureText(stats.PlayerName, out var textBounds);
        canvas.DrawText(stats.PlayerName, 159, 58 - textBounds.Top, nameFont, namePaint);

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

                divisionFont.MeasureText(rankedStatsEntry.CurrentDivisionName, out textBounds);
                canvas.DrawText(rankedStatsEntry.CurrentDivisionName, x - (int) (textBounds.Width / 2), 206 - textBounds.Top, divisionFont, divisionPaint);
                if (rankedStatsEntry.Ranking == null)
                {
                    const int maxBarWidth = 130, barHeight = 6;
                    var progressText = $"{(int) (rankedStatsEntry.Progress * 100)}%";
                    rankProgressFont.MeasureText(progressText, out textBounds);
                    var barX = x - textBounds.Width / 2f - maxBarWidth / 2f;

                    using var barBackgroundPaint = new SKPaint();
                    barBackgroundPaint.IsAntialias = true;
                    barBackgroundPaint.Color = SKColors.White.WithAlpha((int) (.2 * 255));
                    canvas.DrawRoundRect(barX, 250, maxBarWidth, barHeight, 10, 10, barBackgroundPaint);

                    var rankProgressBarWidth = (int) (maxBarWidth * rankedStatsEntry.Progress);
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

                    canvas.DrawText(progressText, barX + maxBarWidth + 7, 247 - textBounds.Top, rankProgressFont, rankProgressPaint);
                }
                else
                {
                    rankingFont.MeasureText(rankedStatsEntry.Ranking, out textBounds);
                    canvas.DrawText(rankedStatsEntry.Ranking, x - (int) (textBounds.Width / 2), 245 - textBounds.Top, rankingFont, rankingPaint);
                }
            }

            valueFont.MeasureText(stats.Competitive.Earnings, out textBounds);
            canvas.DrawText(stats.Competitive.Earnings, 70, 365 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Competitive.PowerRanking, out textBounds);
            canvas.DrawText(stats.Competitive.PowerRanking, 250, 365 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.MatchesPlayed, out textBounds);
            canvas.DrawText(stats.Overall.MatchesPlayed, 70, 441 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.Wins, out textBounds);
            canvas.DrawText(stats.Overall.Wins, 231, 441 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.WinRatio, out textBounds);
            canvas.DrawText(stats.Overall.WinRatio, 370, 441 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.Kills, out textBounds);
            canvas.DrawText(stats.Overall.Kills, 70, 518 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.KD, out textBounds);
            canvas.DrawText(stats.Overall.KD, 231, 518 - textBounds.Top, valueFont, valuePaint);
        }
        else
        {
            valueFont.MeasureText(stats.Overall.MatchesPlayed, out textBounds);
            canvas.DrawText(stats.Overall.MatchesPlayed, 70, 211 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.Wins, out textBounds);
            canvas.DrawText(stats.Overall.Wins, 231, 211 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.WinRatio, out textBounds);
            canvas.DrawText(stats.Overall.WinRatio, 370, 211 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.Kills, out textBounds);
            canvas.DrawText(stats.Overall.Kills, 70, 288 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Overall.KD, out textBounds);
            canvas.DrawText(stats.Overall.KD, 231, 288 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Playtime.Days, out textBounds);
            canvas.DrawText(stats.Playtime.Days, 70, 369 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Playtime.Hours, out textBounds);
            canvas.DrawText(stats.Playtime.Hours, 147, 369 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Playtime.Minutes, out textBounds);
            canvas.DrawText(stats.Playtime.Minutes, 213, 369 - textBounds.Top, valueFont, valuePaint);

            var battlePassLevel = ((int) stats.BattlePassLevel).ToString();
            valueFont.MeasureText(battlePassLevel, out textBounds);
            canvas.DrawText(battlePassLevel, 70, 479 - textBounds.Top, valueFont, valuePaint);

            const int maxBarWidth = 309, barHeight = 20;

            var battlePassBarWidth = (int) (maxBarWidth * (stats.BattlePassLevel - (int) stats.BattlePassLevel));
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

        valueFont.MeasureText(stats.Solo.MatchesPlayed, out textBounds);
        canvas.DrawText(stats.Solo.MatchesPlayed, 537, 211 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Solo.Wins, out textBounds);
        canvas.DrawText(stats.Solo.Wins, 698, 211 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Solo.WinRatio, out textBounds);
        canvas.DrawText(stats.Solo.WinRatio, 837, 211 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Solo.Kills, out textBounds);
        canvas.DrawText(stats.Solo.Kills, 537, 288 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Solo.KD, out textBounds);
        canvas.DrawText(stats.Solo.KD, 698, 288 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Solo.Top25, out textBounds);
        canvas.DrawText(stats.Solo.Top25, 837, 288 - textBounds.Top, valueFont, valuePaint);


        valueFont.MeasureText(stats.Duos.MatchesPlayed, out textBounds);
        canvas.DrawText(stats.Duos.MatchesPlayed, 1016, 211 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Duos.Wins, out textBounds);
        canvas.DrawText(stats.Duos.Wins, 1177, 211 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Duos.WinRatio, out textBounds);
        canvas.DrawText(stats.Duos.WinRatio, 1316, 211 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Duos.Kills, out textBounds);
        canvas.DrawText(stats.Duos.Kills, 1016, 288 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Duos.KD, out textBounds);
        canvas.DrawText(stats.Duos.KD, 1177, 288 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Duos.Top12, out textBounds);
        canvas.DrawText(stats.Duos.Top12, 1316, 288 - textBounds.Top, valueFont, valuePaint);


        valueFont.MeasureText(stats.Trios.MatchesPlayed, out textBounds);
        canvas.DrawText(stats.Trios.MatchesPlayed, 537, 441 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Trios.Wins, out textBounds);
        canvas.DrawText(stats.Trios.Wins, 698, 441 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Trios.WinRatio, out textBounds);
        canvas.DrawText(stats.Trios.WinRatio, 837, 441 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Trios.Kills, out textBounds);
        canvas.DrawText(stats.Trios.Kills, 537, 518 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Trios.KD, out textBounds);
        canvas.DrawText(stats.Trios.KD, 698, 518 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Trios.Top6, out textBounds);
        canvas.DrawText(stats.Trios.Top6, 837, 518 - textBounds.Top, valueFont, valuePaint);


        valueFont.MeasureText(stats.Squads.MatchesPlayed, out textBounds);
        canvas.DrawText(stats.Squads.MatchesPlayed, 1016, 441 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Squads.Wins, out textBounds);
        canvas.DrawText(stats.Squads.Wins, 1177, 441 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Squads.WinRatio, out textBounds);
        canvas.DrawText(stats.Squads.WinRatio, 1316, 441 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Squads.Kills, out textBounds);
        canvas.DrawText(stats.Squads.Kills, 1016, 518 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Squads.KD, out textBounds);
        canvas.DrawText(stats.Squads.KD, 1177, 518 - textBounds.Top, valueFont, valuePaint);

        valueFont.MeasureText(stats.Squads.Top6, out textBounds);
        canvas.DrawText(stats.Squads.Top6, 1316, 518 - textBounds.Top, valueFont, valuePaint);

        if (type == StatsType.Normal && stats.Teams != null)
        {
            valueFont.MeasureText(stats.Teams.MatchesPlayed, out textBounds);
            canvas.DrawText(stats.Teams.MatchesPlayed, 537, 671 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Teams.Wins, out textBounds);
            canvas.DrawText(stats.Teams.Wins, 698, 671 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Teams.WinRatio, out textBounds);
            canvas.DrawText(stats.Teams.WinRatio, 837, 671 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Teams.Kills, out textBounds);
            canvas.DrawText(stats.Teams.Kills, 954, 671 - textBounds.Top, valueFont, valuePaint);

            valueFont.MeasureText(stats.Teams.KD, out textBounds);
            canvas.DrawText(stats.Teams.KD, 1115, 671 - textBounds.Top, valueFont, valuePaint);
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