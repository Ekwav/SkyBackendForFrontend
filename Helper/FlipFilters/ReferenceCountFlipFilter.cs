
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Count of references used for flip, together with high volume indicates that item is new and may be volatilie")]
public class ReferenceCountDetailedFlipFilter : NumberDetailedFlipFilter
{
    protected override Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
    {
        return flip => flip.Context.ContainsKey("refCount") ? int.Parse(flip.Context["refCount"]) : 0;
    }
}