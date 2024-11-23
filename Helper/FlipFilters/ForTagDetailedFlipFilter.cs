
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;
public class ForTagDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => new object[] { };
    public FilterType FilterType => FilterType.RANGE;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        return null;
    }
}
