
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Adjusts target price based on craft cost of ingredients multiplied by weight")]
public class CraftCostWeightDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override FilterType FilterType => FilterType.RANGE;
    private static Dictionary<string, double> DefaultWeights = new() {
        { "skin", 0.5 },
        { "ultimate_fatal_tempo", 0.65},
        { "rarity_upgrades", 0.5},
        { "upgrade_level", 0.8},
        { "talisman_enrichment", 0.1},
        { "RUNE_MUSIC", 0.5},
        { "RUNE_MEOW_MUSIC", 0.5},
        { "RUNE_DRAGON", 0.5},
        { "RUNE_TIDAL", 0.5},
        { "RUNE_GRAND_SEARING", 0.5},
        { "RUNE_ENCHANT", 0.5},
        { "RUNE_BARK_TUNES", 0.5},
        { "RUNE_BARK_SMITTEN", 0.5},
        { "RUNE_BARK_ICE_SKATES", 0.5},
        { "RUNE_SPELLBOUND", 0.5},
        { "RUNE_GRAND_FREEZING", 0.5},
        { "RUNE_PRIMAL_FEAR", 0.5}
    };

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        Dictionary<string, double> multipliers = ParseMultipliers(val);
        foreach (var item in DefaultWeights)
        {
            multipliers.TryAdd(item.Key, item.Value);
        }
        if (!multipliers.TryGetValue("default", out var defaultMultiplier))
            throw new CoflnetException("missing_argument", "No default multiplier provided, use default:1 to disable");

        filters.filters.TryGetValue("MinProfit", out var minprofitString);
        NumberParser.TryLong(minprofitString, out var target);

        return f => f.Finder == LowPricedAuction.FinderType.CraftCost &&
            f.Context.ContainsKey("breakdown") &&
             f.Context.TryAdd("target", (JsonSerializer.Deserialize<Dictionary<string, long>>(f.Context["breakdown"], (JsonSerializerOptions)null)
                .Select(b => multipliers.GetValueOrDefault(b.Key, defaultMultiplier) * b.Value).Sum() + long.Parse(f.Context["cleanCost"])).ToString())
                && (target == 0 || long.Parse(f.Context["target"]) - f.Auction.StartingBid >= target)
                || f.Context.Remove("target") && false; // clear up temp stored
    }

    private static Dictionary<string, double> ParseMultipliers(string val)
    {
        try
        {
            return val.Split(',').ToDictionary(m => m.Split(':')[0], m => NumberParser.Double(m.Split(':')[1]));
        }
        catch (System.Exception e)
        {
            Console.WriteLine("craftcost filter: " + e);
            throw new CoflnetException("filter_parsing", $"Error in filter CraftCostWeight. Make sure to specify pairs separated by commas of like `modifier:multiplier,sharpness:0.7`");
        }
    }
}
