namespace EasyFortniteStats_ImageApi.Models;

// String Enum StatsType
public enum StatsType
{
    Normal = 0,
    Competitive = 1,
}

public class Stats
{
    public string PlayerName { get; set; }
    public string InputType { get; set; }
    public bool IsVerified { get; set; }
    public string? UserName { get; set; }
    public string? BackgroundImagePath { get; set; }
    public Playtime Playtime { get; set; }
    public float BattlePassLevel { get; set; }
    public string[] BattlePassLevelBarColors { get; set; }
    public CompetitiveStats? Competitive { get; set; }
    public StatsEntry Overall { get; set; }
    public StatsEntry Solo { get; set; }
    public StatsEntry Duos { get; set; }
    public StatsEntry Trios { get; set; }
    public StatsEntry Squads { get; set; }
    public StatsEntry? Teams { get; set; }
}

public class CompetitiveStats
{
    public RankedStatsEntry[] RankedStatsEntries { get; set; }
    public string Earnings { get; set; }
    public string PowerRanking { get; set; }
}

public class RankedStatsEntry
{
    public RankedType RankingType { get; set; }
    public int CurrentDivision { get; set; }
    public string CurrentDivisionName { get; set; }
    public int HighestDivision { get; set; }
    public string HighestDivisionName { get; set; }
    public float Progress { get; set; }
    public string? Ranking { get; set; }

    public bool isUnranked()
    {
        return CurrentDivision == 0 && HighestDivision == 0 && Progress == 0.0;
    }
}

public enum RankedType
{
    BatteRoyale = 0,
    ZeroBuild = 1,
}


public class StatsEntry
{
    public string MatchesPlayed { get; set; }
    public string Wins { get; set; }
    public string WinRatio { get; set; }
    public string Kills { get; set; }
    public string KD { get; set; }
    public string? Top25 { get; set; }
    public string? Top12 { get; set; }
    public string? Top6 { get; set; }
}

public class Playtime
{
    public string Days { get; set; }
    public string Hours { get; set; }
    public string Minutes { get; set; }
}