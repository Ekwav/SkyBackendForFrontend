
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace Coflnet.Sky.Commands.Shared;
public class ArmorSetDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => ItemDetails.Instance.TagLookup
        .Where(t => t.Key.EndsWith("_LEGGINGS") && IsOnAh(t))
        .Select(t => (object)t.Key.Replace("_LEGGINGS", "")).Append(ExtraArmorSets.Keys).ToArray();


    public static ReadOnlyDictionary<string, string[]> ExtraArmorSets = new(new Dictionary<string, string[]>
    {
        { "seymour", new []{ "OXFORD_SHOES", "SATIN_TROUSERS", "CASHMERE_JACKET", "VELVET_TOP_HAT" } },
    });
    private static bool IsOnAh(KeyValuePair<string, int> t)
    {
        return !t.Key.Contains("_TERROR_") && !t.Key.Contains("_CRIMSON_") && !t.Key.Contains("_AURORA_") && !t.Key.Contains("_FERVOR_") && !t.Key.Contains("_HOLLOW_");
    }

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        if (ExtraArmorSets.TryGetValue(val, out var set))
            return flip => set.Contains(flip.Auction.Tag);
        var regex = new Regex('^' + Regex.Escape(val) + "_(CHESTPLATE|HELMET|BOOTS|LEGGINGS)");
        return flip => regex.Match(flip.Auction.Tag).Success;
    }
}
