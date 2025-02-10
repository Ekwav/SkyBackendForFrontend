using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;
public class ExpressionParser
{
    static Dictionary<string, Func<FlipSettings, double>> mapping = new(){
        {"{{MIN_PROFIT}}", s=>s.MinProfit},
        {"{{MAX_COST}}", s=>s.MaxCost},
        {"{{MIN_VOLUME}}", s=>s.MinVolume},
        {"{{MIN_PROFIT_PERCENT}}", s=>s.MinProfitPercent}
    };
    public static string Evaluate(string expression, FlipSettings settings)
    {
        if (!expression.Contains("{{"))
        {
            return expression;
        }
        var breakdown = Regex.Match(expression, @"(.*?)(\{{.*?\}})");
        var variable = breakdown.Groups[2].Value;
        if (!mapping.TryGetValue(variable, out var item))
        {
            throw new CoflnetException("unknown_variable", $"Invalid variable in filter: {variable}");
        }
        var parts = expression.Split('-').Select(part =>
        {
            if (!part.Contains(variable))
                return part;
            var hasMultiplier = part.Contains("*");
            if (!hasMultiplier)
                return expression.Replace(part, item(settings).ToString());
            var parts = part.Split('*');
            var multiplier = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var value = item(settings) * multiplier;
            var prefix = breakdown.Groups[1].Value;
            if (value < 10)
                return prefix + value.ToString();
            return prefix + ((long)value).ToString();
        });
        expression = string.Join("-", parts);
        return expression;
    }
}
