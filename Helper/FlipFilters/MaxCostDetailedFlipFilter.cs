
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;
[FilterDescription("Maximum cost of an auction")]
public class MaxCostDetailedFlipFilter : NoRangeBase
{
    public override object[] Options => new string[] { "0", 50_000_000_000.ToString() };

    public override FilterType FilterType => FilterType.NUMERICAL;

    public override Expression<Func<FlipInstance, bool>> GetNumExpression(long val)
    {
        return flip => Math.Max(flip.Auction.StartingBid, flip.Auction.HighestBidAmount) < val;
    }
}