
using System;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

public class PerfectArmorTierDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override object[] Options => [1, 13];

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        return StartsWithPerfect().And(base.GetExpression(filters, val));
    }

    protected override Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
    {
        return f => GetVal(f);
    }

    private static double GetVal(FlipInstance f)
    {
        return f.Tag != null && double.TryParse(f.Tag.Split("_", 5, StringSplitOptions.None).Last(), out var val) ? val : 0;
    }

    private Expression<Func<FlipInstance, bool>> StartsWithPerfect()
    {
        return flip => flip.Tag != null && flip.Tag.StartsWith("PERFECT_");
    }
}
