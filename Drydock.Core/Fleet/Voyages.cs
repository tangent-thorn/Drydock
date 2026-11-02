// THIRD-PARTY: voyage duration, EXP bonus prediction and the per-sector breakpoint table below are ported from
// Submarine Tracker by Infiziert90 (https://github.com/Infiziert90/SubmarineTracker), MIT licence, commit
// 04c54d770f815cd416bbade1ac5b178b551a32b5: Data/Voyage.cs (CalculateDuration), SubExplPretty.cs (CalcTime),
// Data/Sectors.cs (MapBreakpoints, PredictBonusExp, CalculateBonusExp, CalculateExpForSectors) and Data/Build.cs
// (stat sums, FuelCost, SubRank.From). The breakpoints themselves are Submarine Tracker's, taken there from a
// community sheet (https://docs.google.com/spreadsheets/d/1eiuGRrQTjJ5n4ogybKOBxJRT7EiJLlX93fge-Z8MmD4).
//
// MIT License
//
// Copyright (c) 2023 Infi <infiziert@protonmail.ch>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of
// the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
// THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.

using System.Numerics;

namespace Drydock.Core.Fleet;

/// <summary>EXP at the sector's bonus tiers: T2 and T3 against Surveillance, Normal and Optimal against Retrieval, Favor against Favor.</summary>
public sealed record Breakpoint(int T2, int T3, int Normal, int Optimal, int Favor);

/// <param name="Base">Sum of the sectors' ExpReward, no bonus.</param>
/// <param name="Worst">Every sector at its guaranteed tier.</param>
/// <param name="Best">Every sector at its maximum tier.</param>
public sealed record ExpRange(long Base, long Worst, long Best);

/// <param name="DurationSeconds">12 h fixed plus travel and survey of every sector.</param>
/// <param name="Fuel">Ceruleum tanks, the sum of the sectors' CeruleumTankReq.</param>
public sealed record VoyageEstimate(uint DurationSeconds, int Fuel, ExpRange Exp)
{
    public double Hours => DurationSeconds / 3600.0;
}

public static class VoyageMath
{
    public const int FixedVoyageSeconds = 43200; // 12 h

    // Submarine Tracker's Sectors.MapBreakpoints, keyed by the SubmarineExploration row id.
    public static IReadOnlyDictionary<uint, Breakpoint> Breakpoints { get; } = new Dictionary<uint, Breakpoint>
    {
        { 1, new(20, 80, 50, 80, 70) },
        { 2, new(20, 80, 50, 80, 70) },
        { 3, new(20, 85, 55, 85, 70) },
        { 4, new(20, 85, 55, 85, 70) },
        { 5, new(25, 90, 60, 90, 80) },
        { 6, new(25, 90, 60, 90, 80) },
        { 7, new(30, 95, 65, 95, 90) },
        { 8, new(30, 100, 70, 100, 90) },
        { 9, new(35, 110, 75, 105, 90) },
        { 10, new(50, 115, 80, 110, 90) },
        { 11, new(50, 90, 80, 110, 70) },
        { 12, new(55, 95, 90, 120, 80) },
        { 13, new(60, 100, 100, 130, 75) },
        { 14, new(60, 100, 100, 130, 85) },
        { 15, new(80, 115, 120, 160, 90) },
        { 16, new(60, 100, 100, 130, 85) },
        { 17, new(65, 105, 110, 140, 90) },
        { 18, new(85, 120, 135, 175, 95) },
        { 19, new(75, 110, 120, 155, 95) },
        { 20, new(90, 125, 140, 180, 100) },
        { 21, new(90, 120, 135, 175, 95) },
        { 22, new(105, 130, 140, 180, 100) },
        { 23, new(110, 140, 140, 180, 105) },
        { 24, new(120, 130, 145, 190, 105) },
        { 25, new(120, 135, 145, 190, 105) },
        { 26, new(135, 140, 150, 195, 110) },
        { 27, new(130, 145, 150, 195, 110) },
        { 28, new(130, 150, 155, 200, 120) },
        { 29, new(135, 150, 160, 200, 130) },
        { 30, new(140, 155, 170, 215, 135) },

        { 32, new(135, 150, 165, 205, 140) },
        { 33, new(140, 155, 170, 205, 140) },
        { 34, new(140, 160, 175, 210, 145) },
        { 35, new(145, 165, 180, 220, 145) },
        { 36, new(145, 160, 185, 220, 150) },
        { 37, new(145, 165, 180, 220, 145) },
        { 38, new(150, 170, 180, 220, 140) },
        { 39, new(160, 175, 190, 225, 150) },
        { 40, new(155, 170, 190, 220, 140) },
        { 41, new(160, 175, 190, 225, 150) },
        { 42, new(155, 170, 185, 230, 160) },
        { 43, new(160, 175, 185, 235, 165) },
        { 44, new(160, 170, 190, 240, 175) },
        { 45, new(165, 190, 195, 245, 170) },
        { 46, new(170, 185, 205, 250, 175) },
        { 47, new(165, 180, 185, 235, 165) },
        { 48, new(165, 180, 185, 235, 165) },
        { 49, new(170, 185, 190, 240, 165) },
        { 50, new(175, 190, 200, 250, 175) },
        { 51, new(180, 190, 200, 250, 175) },

        { 53, new(180, 190, 200, 250, 175) },
        { 54, new(180, 190, 200, 250, 175) },
        { 55, new(180, 190, 200, 250, 175) },
        { 56, new(180, 195, 205, 260, 178) },
        { 57, new(180, 195, 210, 260, 185) },
        { 58, new(180, 195, 210, 265, 185) },
        { 59, new(180, 195, 215, 270, 185) },
        { 60, new(180, 195, 220, 270, 185) },
        { 61, new(180, 195, 220, 270, 185) },
        { 62, new(180, 195, 220, 270, 185) },
        { 63, new(185, 200, 225, 275, 190) },
        { 64, new(185, 200, 230, 280, 190) },
        { 65, new(185, 200, 230, 280, 190) },
        { 66, new(190, 205, 235, 285, 195) },
        { 67, new(195, 210, 240, 290, 200) },
        { 68, new(195, 210, 245, 295, 200) },
        { 69, new(200, 215, 255, 300, 205) },
        { 70, new(205, 220, 255, 300, 210) },
        { 71, new(205, 220, 260, 305, 210) },
        { 72, new(205, 220, 260, 305, 210) },

        { 74, new(205, 220, 260, 305, 210) },
        { 75, new(205, 220, 260, 305, 210) },
        { 76, new(205, 220, 260, 305, 210) },
        { 77, new(210, 225, 265, 310, 215) },
        { 78, new(210, 225, 265, 310, 215) },
        { 79, new(210, 225, 265, 310, 215) },
        { 80, new(210, 225, 265, 310, 215) },
        { 81, new(215, 230, 270, 315, 220) },
        { 82, new(215, 230, 270, 315, 220) },
        { 83, new(215, 230, 270, 315, 220) },
        { 84, new(215, 230, 270, 315, 220) },
        { 85, new(215, 230, 270, 315, 220) },
        { 86, new(215, 230, 270, 315, 220) },
        { 87, new(220, 235, 275, 320, 225) },
        { 88, new(220, 235, 275, 320, 225) },
        { 89, new(220, 235, 275, 320, 225) },
        { 90, new(220, 235, 275, 320, 225) },
        { 91, new(220, 235, 275, 320, 225) },
        { 92, new(220, 235, 275, 320, 225) },
        { 93, new(220, 235, 275, 320, 225) },

        { 95, new(220, 235, 275, 320, 225) },
        { 96, new(220, 235, 275, 320, 225) },
        { 97, new(220, 235, 275, 320, 225) },
        { 98, new(225, 240, 280, 325, 230) },
        { 99, new(225, 237, 280, 325, 227) },
        { 100, new(225, 238, 280, 325, 230) },
        { 101, new(225, 240, 280, 325, 230) },
        { 102, new(226, 241, 281, 326, 231) },
        { 103, new(227, 242, 282, 327, 232) },
        { 104, new(228, 243, 283, 328, 233) },
        { 105, new(229, 244, 284, 329, 234) },
        { 106, new(230, 245, 285, 330, 235) },
        { 107, new(230, 245, 285, 330, 235) },
        { 108, new(231, 246, 286, 331, 236) },
        { 109, new(232, 247, 287, 332, 237) },
        { 110, new(233, 248, 288, 333, 238) },
        { 111, new(234, 249, 289, 334, 239) },
        { 112, new(234, 249, 289, 334, 239) },
        { 113, new(235, 250, 290, 335, 240) },
        { 114, new(235, 250, 290, 335, 240) },

        { 116, new(235, 250, 290, 335, 240) },
        { 117, new(235, 250, 290, 335, 240) },
        { 118, new(235, 250, 290, 335, 240) },
        { 119, new(236, 251, 291, 336, 241) },
        { 120, new(237, 252, 292, 337, 242) },
        { 121, new(238, 253, 293, 338, 243) },
        { 122, new(240, 255, 295, 340, 245) },
        { 123, new(241, 256, 296, 341, 246) },
        { 124, new(242, 257, 297, 342, 247) },
        { 125, new(243, 258, 298, 343, 248) },
        { 126, new(244, 259, 299, 344, 249) },
        { 127, new(245, 260, 300, 345, 250) },
        { 128, new(245, 260, 300, 345, 250) },
        { 129, new(246, 261, 301, 346, 251) },
        { 130, new(247, 262, 302, 347, 252) },
        { 131, new(248, 263, 303, 348, 253) },
        { 132, new(249, 264, 304, 349, 254) },
        { 133, new(249, 264, 304, 349, 254) },
        { 134, new(250, 265, 305, 350, 255) },
        { 135, new(250, 266, 305, 350, 255) },

        { 137, new(251, 266, 306, 351, 256) },
        { 138, new(252, 267, 307, 352, 257) },
        { 139, new(253, 268, 308, 353, 258) },
        { 140, new(254, 269, 309, 354, 259) },
        { 141, new(254, 269, 309, 354, 259) },
        { 142, new(255, 270, 310, 355, 260) },
        { 143, new(255, 270, 310, 355, 260) },
        { 144, new(256, 271, 311, 356, 261) },
        { 145, new(257, 272, 312, 357, 262) },
        { 146, new(258, 273, 313, 358, 263) },
        { 147, new(258, 273, 313, 358, 263) },
        { 148, new(259, 274, 314, 359, 264) },
        { 149, new(260, 275, 315, 360, 265) },
    };

    /// <summary>
    /// The guaranteed and the maximum bonus tier (0 to 4) of one sector for a hull's stats, Tracker's PredictBonusExp.
    /// A sector without a breakpoint row gives no bonus.
    /// </summary>
    public static (int Guaranteed, int Maximum) Tier(uint sector, BuildStats stats)
    {
        if (!Breakpoints.TryGetValue(sector, out var br)) return (0, 0);

        var guaranteed = br.Optimal <= stats.Retrieval ? 1 : 0;

        var maximum = guaranteed;
        maximum += br.T2 <= stats.Surveillance ? 1 : 0;
        maximum += br.T3 <= stats.Surveillance ? 1 : 0;

        if (br.Favor <= stats.Favor)
        {
            maximum += 1;
            maximum += br.T2 <= stats.Surveillance ? 1 : 0;
            maximum += br.T3 <= stats.Surveillance ? 1 : 0;
        }

        return (guaranteed, Math.Clamp(maximum, 0, 4));
    }

    /// <summary>Tracker's CalculateBonusExp: the multiplier 1.00 to 2.00 in quarters, truncated.</summary>
    public static long BonusExp(int tier, long exp) => tier switch
    {
        1 => (long)(exp * 1.25),
        2 => (long)(exp * 1.50),
        3 => (long)(exp * 1.75),
        4 => (long)(exp * 2.00),
        _ => exp,
    };

    /// <summary>
    /// The sector rows of a route, or null when it is empty, longer than five, names a sector the sheet lacks,
    /// names a start point, or mixes maps.
    /// </summary>
    public static IReadOnlyList<SectorData>? Resolve(SubmarineGameData data, IReadOnlyList<uint> route)
    {
        if (route.Count is 0 or > 5) return null;
        var list = new List<SectorData>(route.Count);
        foreach (var id in route)
        {
            if (!data.Sectors.TryGetValue(id, out var s) || s.StartingPoint) return null;
            list.Add(s);
        }
        return list.All(s => s.Map == list[0].Map) ? list : null;
    }

    /// <summary>Tracker's Voyage.CalculateDuration, in seconds; the first leg starts at the map's start point.</summary>
    public static uint? DurationSeconds(SubmarineGameData data, IReadOnlyList<uint> route, int speed)
    {
        if (Resolve(data, route) is not { } sectors) return null;
        if (data.StartOf(sectors[0].Map) is not { } start) return null;

        var duration = CalcTime(start, sectors[0], speed);
        for (var i = 1; i < sectors.Count; i++)
            duration += CalcTime(sectors[i - 1], sectors[i], speed);
        return duration + FixedVoyageSeconds;
    }

    public static int? Fuel(SubmarineGameData data, IReadOnlyList<uint> route) =>
        Resolve(data, route) is { } sectors ? sectors.Sum(s => s.CeruleumTankReq) : null;

    public static ExpRange? Exp(SubmarineGameData data, IReadOnlyList<uint> route, BuildStats stats)
    {
        if (Resolve(data, route) is not { } sectors) return null;
        long total = 0, worst = 0, best = 0;
        foreach (var s in sectors)
        {
            var (guaranteed, maximum) = Tier(s.Id, stats);
            total += s.ExpReward;
            worst += BonusExp(guaranteed, s.ExpReward);
            best += BonusExp(maximum, s.ExpReward);
        }
        return new ExpRange(total, worst, best);
    }

    /// <summary>Duration, fuel and EXP of one voyage; null when the route can't be resolved.</summary>
    public static VoyageEstimate? Estimate(SubmarineGameData data, IReadOnlyList<uint> route, BuildStats stats)
    {
        if (DurationSeconds(data, route, stats.Speed) is not { } seconds) return null;
        if (Fuel(data, route) is not { } fuel || Exp(data, route, stats) is not { } exp) return null;
        return new VoyageEstimate(seconds, fuel, exp);
    }

    // SubExplPretty.cs CalcTime: travel to the sector plus its survey, in single precision like Tracker's.
    private static uint CalcTime(SectorData from, SectorData to, float speed) =>
        TravelTime(from, to, speed) + SurveyTime(to, speed);

    private static uint TravelTime(SectorData from, SectorData to, float speed)
    {
        if (speed < 1) speed = 1;
        return (uint)Math.Floor(Vector3.Distance(new Vector3(from.X, from.Y, from.Z), new Vector3(to.X, to.Y, to.Z)) * 3990 / (speed * 100) * 60);
    }

    private static uint SurveyTime(SectorData sector, float speed)
    {
        if (speed < 1) speed = 1;
        return (uint)Math.Floor(sector.SurveyDurationMin * 7000 / (speed * 100) * 60);
    }
}
