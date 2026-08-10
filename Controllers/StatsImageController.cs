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
    private static readonly MemoryCacheEntryOptions TemplateCacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(10)
    };

    [HttpPost]
    public async Task<IActionResult> Post(Stats stats, StatsType type = StatsType.Normal)
    {
        logger.LogInformation("Stats image request received | Name = {PlayerName} | Type = {Type}", stats.PlayerName, type);
        if (type == StatsType.Normal && stats.Teams is null)
            return BadRequest("Normal stats type requested but no team stats were provided.");
        if (type == StatsType.Competitive && stats.Competitive is null)
            return BadRequest("Competitive stats type requested but no competitive stats were provided.");

        var backgroundHash = stats.BackgroundImagePath is not null
            ? $"_{stats.BackgroundImagePath.GetHashCode()}"
            : "";

        var lockName = $"stats_{type}{backgroundHash}_template_mutex";
        byte[]? templateData;
        using (await namedLock.LockAsync(lockName).ConfigureAwait(false))
        {
            var cacheKey = $"stats_{type}{backgroundHash}_template_image";
            cache.TryGetValue(cacheKey, out templateData);
            if (templateData is null)
            {
                using var templateBitmap = await GenerateTemplate(stats, type);
                using var encodedTemplate = templateBitmap.Encode(SKEncodedImageFormat.Png, 100);
                templateData = encodedTemplate.ToArray();
                cache.Set(cacheKey, templateData, TemplateCacheOptions);
            }
        }

        // The cache owns managed encoded data, while this request exclusively owns the decoded bitmap.
        using var templateCopy = SKBitmap.Decode(templateData)
            ?? throw new InvalidOperationException("The cached stats template could not be decoded.");
        using var bitmap = await GenerateImage(stats, type, templateCopy);
        var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return File(data.AsStream(true), "image/png");
    }

    private async Task<SKBitmap> GenerateTemplate(Stats stats, StatsType type)
    {
        var imageInfo = type == StatsType.Competitive
            ? new SKImageInfo(1505, 624)
            : new SKImageInfo(1505, 777);

        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        var customBackgroundBitmap =
            await assets.GetBitmap("data/images/{0}",
                stats.BackgroundImagePath); // don't dispose TODO: Clear caching on bg change
        if (customBackgroundBitmap is null)
        {
            using var backgroundPaint = new SKPaintSafe();
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

            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(imageInfo.Rect, 50), antialias: true);
            canvas.DrawBitmap(customBackgroundBitmap, imageInfo.Rect, SKSamplingOptions.Default, backgroundImagePaint);
            canvas.Restore();
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
        using var competitiveBoxTitlePaintFont = new SKFont();
        competitiveBoxTitlePaint.IsAntialias = true;
        competitiveBoxTitlePaint.Color = SKColors.White;
        competitiveBoxTitlePaintFont.Typeface = fortniteFont;
        competitiveBoxTitlePaintFont.Size = 25;

        using var boxTitlePaint = new SKPaint();
        using var boxTitlePaintFont = new SKFont();
        boxTitlePaint.IsAntialias = true;
        boxTitlePaint.Color = SKColors.White;
        boxTitlePaintFont.Typeface = fortniteFont;
        boxTitlePaintFont.Size = 50;

        using var titlePaint = new SKPaint();
        using var titlePaintFont = new SKFont();
        titlePaint.IsAntialias = true;
        titlePaint.Color = SKColors.LightGray;
        titlePaintFont.Typeface = segoeFont;
        titlePaintFont.Size = 20;

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
            canvas.DrawBitmap(buildLogo, new SKPoint(115, 277), SKSamplingOptions.Default);

            var zeroBuildLogo = await assets.GetBitmap("Assets/Images/Stats/ZeroBuildLogo.png"); // don't dispose
            canvas.DrawBitmap(zeroBuildLogo, new SKPoint(317, 277), SKSamplingOptions.Default);

            canvas.DrawAlignedText("OVERALL", new SKPoint(211, 305), competitiveBoxTitlePaintFont, competitiveBoxTitlePaint);

            canvas.DrawAlignedText("Earnings", new SKPoint(70, 338), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Power Ranking", new SKPoint(250, 338), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Games", new SKPoint(70, 414), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Wins", new SKPoint(231, 414), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Win%", new SKPoint(370, 414), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Kills", new SKPoint(70, 491), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("K/D", new SKPoint(231, 491), titlePaintFont, titlePaint);
        }
        else
        {
            var overallBoxRect = new SKRoundRect(SKRect.Create(50, 159, 437, 568), 30);
            DrawBlurredRoundRect(bitmap, overallBoxRect);
            canvas.DrawRoundRect(overallBoxRect, boxPaint);

            canvas.DrawAlignedText("OVERALL", new SKPoint(60, 134), boxTitlePaintFont, boxTitlePaint);

            canvas.DrawAlignedText("Games", new SKPoint(70, 184), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Wins", new SKPoint(231, 184), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Win%", new SKPoint(370, 184), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Kills", new SKPoint(70, 261), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("K/D", new SKPoint(231, 261), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Playtime since Season 7", new SKPoint(70, 338), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("days", new SKPoint(70, 397), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("hours", new SKPoint(147, 397), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("minutes", new SKPoint(231, 397), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("BattlePass Level", new SKPoint(70, 442), titlePaintFont, titlePaint);

            using var battlePassBarBackgroundPaint = new SKPaint();
            battlePassBarBackgroundPaint.IsAntialias = true;
            battlePassBarBackgroundPaint.Color = SKColors.White.WithAlpha((int)(.3 * 255));
            canvas.DrawRoundRect(158, 483, 309, 20, 10, 10, battlePassBarBackgroundPaint);
        }

        // Solo
        var soloBoxRect = new SKRoundRect(SKRect.Create(517, 159, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, soloBoxRect);
        canvas.DrawRoundRect(soloBoxRect, boxPaint);

        canvas.DrawAlignedText("SOLO", new SKPoint(527, 134), boxTitlePaintFont, boxTitlePaint);

        var soloIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/solo.png"); // don't dispose
        canvas.DrawBitmap(soloIcon, new SKPoint(648, 134), SKSamplingOptions.Default);

        canvas.DrawAlignedText("Games", new SKPoint(537, 184), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Wins", new SKPoint(698, 184), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Win%", new SKPoint(837, 184), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Kills", new SKPoint(537, 261), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("K/D", new SKPoint(698, 261), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Top 25", new SKPoint(837, 261), titlePaintFont, titlePaint);

        // Duos
        var duosBoxRect = new SKRoundRect(SKRect.Create(996, 159, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, duosBoxRect);
        canvas.DrawRoundRect(duosBoxRect, boxPaint);

        canvas.DrawAlignedText("DUOS", new SKPoint(1006, 134), boxTitlePaintFont, boxTitlePaint);

        var duosIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/duos.png"); // don't dispose
        canvas.DrawBitmap(duosIcon, new SKPoint(1133, 134), SKSamplingOptions.Default);

        canvas.DrawAlignedText("Games", new SKPoint(1016, 184), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Wins", new SKPoint(1177, 184), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Win%", new SKPoint(1316, 184), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Kills", new SKPoint(1016, 261), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("K/D", new SKPoint(1177, 261), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Top 12", new SKPoint(1316, 261), titlePaintFont, titlePaint);

        // Trios
        var triosBoxRect = new SKRoundRect(SKRect.Create(517, 389, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, triosBoxRect);
        canvas.DrawRoundRect(triosBoxRect, boxPaint);

        canvas.DrawAlignedText("TRIOS", new SKPoint(527, 364), boxTitlePaintFont, boxTitlePaint);

        var triosIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/trios.png"); // don't dispose
        canvas.DrawBitmap(triosIcon, new SKPoint(663, 364), SKSamplingOptions.Default);

        canvas.DrawAlignedText("Games", new SKPoint(537, 414), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Wins", new SKPoint(698, 414), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Win%", new SKPoint(837, 414), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Kills", new SKPoint(537, 491), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("K/D", new SKPoint(698, 491), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Top 6", new SKPoint(837, 491), titlePaintFont, titlePaint);

        // Squads
        var squadsBoxRect = new SKRoundRect(SKRect.Create(996, 389, 459, 185), 30);
        DrawBlurredRoundRect(bitmap, squadsBoxRect);
        canvas.DrawRoundRect(squadsBoxRect, boxPaint);

        canvas.DrawAlignedText("SQUADS", new SKPoint(1006, 364), boxTitlePaintFont, boxTitlePaint);

        var squadsIcon = await assets.GetBitmap(@"Assets/Images/Stats/PlaylistIcons/squads.png"); // don't dispose
        canvas.DrawBitmap(squadsIcon, new SKPoint(1191, 364), SKSamplingOptions.Default);

        canvas.DrawAlignedText("Games", new SKPoint(1016, 414), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Wins", new SKPoint(1177, 414), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Win%", new SKPoint(1316, 414), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Kills", new SKPoint(1016, 491), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("K/D", new SKPoint(1177, 491), titlePaintFont, titlePaint);

        canvas.DrawAlignedText("Top 6", new SKPoint(1316, 491), titlePaintFont, titlePaint);

        if (type == StatsType.Normal)
        {
            // Teams
            var teamsBoxRect = new SKRoundRect(SKRect.Create(517, 619, 938, 108), 30);
            DrawBlurredRoundRect(bitmap, teamsBoxRect);
            canvas.DrawRoundRect(teamsBoxRect, boxPaint);

            canvas.DrawAlignedText("TEAMS", new SKPoint(527, 594), boxTitlePaintFont, boxTitlePaint);

            var teamsIcon = await assets.GetBitmap("Assets/Images/Stats/PlaylistIcons/teams.png"); // don't dispose
            canvas.DrawBitmap(teamsIcon, new SKPoint(683, 594), SKSamplingOptions.Default);

            canvas.DrawAlignedText("Games", new SKPoint(537, 644), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Wins", new SKPoint(698, 644), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Win%", new SKPoint(837, 644), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("Kills", new SKPoint(954, 644), titlePaintFont, titlePaint);

            canvas.DrawAlignedText("K/D", new SKPoint(1115, 644), titlePaintFont, titlePaint);
        }

        return bitmap;
    }

    private async Task<SKBitmap> GenerateImage(Stats stats, StatsType type, SKBitmap templateBitmap)
    {
        var imageInfo = new SKImageInfo(templateBitmap.Width, templateBitmap.Height);
        var bitmap = new SKBitmap(imageInfo);
        using var canvas = new SKCanvas(bitmap);

        canvas.DrawBitmap(templateBitmap, SKPoint.Empty, SKSamplingOptions.Default);

        var fortniteFont = await assets.GetFont("Assets/Fonts/Fortnite.ttf"); // don't dispose
        var segoeFont = await assets.GetFont("Assets/Fonts/Segoe.ttf"); // don't dispose

        using var namePaint = new SKPaint();
        using var namePaintFont = new SKFont();
        namePaint.IsAntialias = true;
        namePaint.Color = SKColors.White;
        namePaintFont.Typeface = segoeFont;
        namePaintFont.Size = 64;

        using var titlePaint = new SKPaint();
        using var titlePaintFont = new SKFont();
        titlePaint.IsAntialias = true;
        titlePaint.Color = SKColors.LightGray;
        titlePaintFont.Typeface = segoeFont;
        titlePaintFont.Size = 20;

        using var valuePaint = new SKPaint();
        using var valuePaintFont = new SKFont();
        valuePaint.IsAntialias = true;
        valuePaint.Color = SKColors.White;
        valuePaintFont.Typeface = fortniteFont;
        valuePaintFont.Size = 35;

        using var divisionPaint = new SKPaint();
        using var divisionPaintFont = new SKFont();
        divisionPaint.IsAntialias = true;
        divisionPaint.Color = SKColors.White;
        divisionPaintFont.Typeface = fortniteFont;
        divisionPaintFont.Size = 35;

        using var rankProgressPaint = new SKPaint();
        using var rankProgressPaintFont = new SKFont();
        rankProgressPaint.IsAntialias = true;
        rankProgressPaint.Color = SKColors.White.WithAlpha((int)(255 * 0.7));
        rankProgressPaintFont.Typeface = segoeFont;
        rankProgressPaintFont.Size = 16;

        using var rankingPaint = new SKPaint();
        using var rankingPaintFont = new SKFont();
        rankingPaint.IsAntialias = true;
        rankingPaint.Color = SKColors.White;
        rankingPaintFont.Typeface = fortniteFont;
        rankingPaintFont.Size = 20;

        var inputIcon =
            await assets.GetBitmap($"Assets/Images/Stats/InputTypes/{stats.InputType}.png"); // don't dispose
        canvas.DrawBitmap(inputIcon, 50, 50, SKSamplingOptions.Default);

        var playerNameWidth = namePaintFont.MeasureText(stats.PlayerName, namePaint);
        canvas.DrawAlignedText(stats.PlayerName, new SKPoint(159, 58), namePaintFont, namePaint);

        if (stats.IsVerified)
        {
            var verifiedIcon = await assets.GetBitmap("Assets/Images/Stats/Verified.png"); // don't dispose
            canvas.DrawBitmap(verifiedIcon, 159 + playerNameWidth + 5, 47, SKSamplingOptions.Default);

            using var discordBoxBitmap = await ImageUtils.GenerateDiscordBox(assets, stats.UserName ?? "???#0000");
            canvas.DrawBitmap(discordBoxBitmap, imageInfo.Width - 50 - discordBoxBitmap.Width, 39, SKSamplingOptions.Default);
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
                canvas.DrawBitmap(divisionIconBitmap, x - divisionIconBitmap!.Width / 2f, 109, SKSamplingOptions.Default);

                canvas.DrawAlignedText(
                    rankedStatsEntry.CurrentDivisionName,
                    new SKPoint(x, 206), divisionPaintFont,
                    divisionPaint,
                    SKTextAlign.Center);

                if (rankedStatsEntry.Ranking is null)
                {
                    const int maxBarWidth = 130, barHeight = 6;
                    var progressText = $"{(int)(rankedStatsEntry.Progress * 100)}%";
                    var progressTextWidth = rankProgressPaintFont.MeasureText(progressText, rankProgressPaint);
                    var barX = x - progressTextWidth / 2f - maxBarWidth / 2f;

                    using var barBackgroundPaint = new SKPaint();
                    barBackgroundPaint.IsAntialias = true;
                    barBackgroundPaint.Color = SKColors.White.WithAlpha((int)(.2 * 255));
                    canvas.DrawRoundRect(barX, 250, maxBarWidth, barHeight, 10, 10, barBackgroundPaint);

                    var rankProgressBarWidth = (int)(maxBarWidth * rankedStatsEntry.Progress);
                    if (rankProgressBarWidth > 0)
                    {
                        rankProgressBarWidth = Math.Max(rankProgressBarWidth, barHeight);
                        using var battlePassBarPaint = new SKPaintSafe();
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

                    canvas.DrawAlignedText(progressText, new SKPoint(barX + maxBarWidth + 7, 247), rankProgressPaintFont, rankProgressPaint);
                }
                else
                {
                    canvas.DrawAlignedText(
                        rankedStatsEntry.Ranking,
                        new SKPoint(x, 245), rankingPaintFont,
                        rankingPaint,
                        SKTextAlign.Center);
                }
            }

            canvas.DrawAlignedText(stats.Competitive.Earnings, new SKPoint(70, 365), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Competitive.PowerRanking, new SKPoint(250, 365), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.MatchesPlayed, new SKPoint(70, 441), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.Wins, new SKPoint(231, 441), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.WinRatio, new SKPoint(370, 441), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.Kills, new SKPoint(70, 518), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.KD, new SKPoint(231, 518), valuePaintFont, valuePaint);
        }
        else
        {
            canvas.DrawAlignedText(stats.Overall.MatchesPlayed, new SKPoint(70, 211), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.Wins, new SKPoint(231, 211), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.WinRatio, new SKPoint(370, 211), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.Kills, new SKPoint(70, 288), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Overall.KD, new SKPoint(231, 288), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Playtime.Days, new SKPoint(70, 369), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Playtime.Hours, new SKPoint(147, 369), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Playtime.Minutes, new SKPoint(213, 369), valuePaintFont, valuePaint);

            var battlePassLevel = ((int)stats.BattlePassLevel).ToString();
            canvas.DrawAlignedText(battlePassLevel, new SKPoint(70, 479), valuePaintFont, valuePaint);

            const int maxBarWidth = 309, barHeight = 20;

            var battlePassBarWidth = (int)(maxBarWidth * (stats.BattlePassLevel - (int)stats.BattlePassLevel));
            if (battlePassBarWidth > 0)
            {
                battlePassBarWidth = Math.Max(battlePassBarWidth, barHeight);
                using var battlePassBarPaint = new SKPaintSafe();
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

        canvas.DrawAlignedText(stats.Solo.MatchesPlayed, new SKPoint(537, 211), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Solo.Wins, new SKPoint(698, 211), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Solo.WinRatio, new SKPoint(837, 211), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Solo.Kills, new SKPoint(537, 288), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Solo.KD, new SKPoint(698, 288), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Solo.Top25!, new SKPoint(837, 288), valuePaintFont, valuePaint);


        canvas.DrawAlignedText(stats.Duos.MatchesPlayed, new SKPoint(1016, 211), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Duos.Wins, new SKPoint(1177, 211), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Duos.WinRatio, new SKPoint(1316, 211), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Duos.Kills, new SKPoint(1016, 288), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Duos.KD, new SKPoint(1177, 288), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Duos.Top12!, new SKPoint(1316, 288), valuePaintFont, valuePaint);


        canvas.DrawAlignedText(stats.Trios.MatchesPlayed, new SKPoint(537, 441), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Trios.Wins, new SKPoint(698, 441), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Trios.WinRatio, new SKPoint(837, 441), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Trios.Kills, new SKPoint(537, 518), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Trios.KD, new SKPoint(698, 518), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Trios.Top6!, new SKPoint(837, 518), valuePaintFont, valuePaint);


        canvas.DrawAlignedText(stats.Squads.MatchesPlayed, new SKPoint(1016, 441), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Squads.Wins, new SKPoint(1177, 441), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Squads.WinRatio, new SKPoint(1316, 441), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Squads.Kills, new SKPoint(1016, 518), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Squads.KD, new SKPoint(1177, 518), valuePaintFont, valuePaint);

        canvas.DrawAlignedText(stats.Squads.Top6!, new SKPoint(1316, 518), valuePaintFont, valuePaint);

        if (type == StatsType.Normal && stats.Teams is not null)
        {
            canvas.DrawAlignedText(stats.Teams.MatchesPlayed, new SKPoint(537, 671), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Teams.Wins, new SKPoint(698, 671), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Teams.WinRatio, new SKPoint(837, 671), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Teams.Kills, new SKPoint(954, 671), valuePaintFont, valuePaint);

            canvas.DrawAlignedText(stats.Teams.KD, new SKPoint(1115, 671), valuePaintFont, valuePaint);
        }

        return bitmap;
    }

    private static readonly SKImageFilter _blurredFilter = SKImageFilter.CreateBlur(5, 5);

    private static void DrawBlurredRoundRect(SKBitmap bitmap, SKRoundRect rect)
    {
        using var canvas = new SKCanvas(bitmap);

        canvas.ClipRoundRect(rect, antialias: true);

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.ImageFilter = _blurredFilter;

        canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default, paint);
    }
}
