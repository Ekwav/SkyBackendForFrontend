using Coflnet.Sky.Core;
using FluentAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class CraftCostWeightTests
{
    [TestCase("2", "ethermerge:1,default:0.8", true, 50908000)]
    [TestCase("50000000", "ethermerge:1,default:0.8", false, 0)]
    [TestCase("2", "ethermerge:1,aoteStone:0.1,default:0.8", true, 43908000)]
    [TestCase("2", "art_of_war_count:0.5,default:0.8", true, 45228000)]
    [TestCase("2", "art_of_war_count:0.5,art_of_war_count.1:1.5,default:0.8", true, 53428000)]
    public void Test(string minProfit, string filterVal, bool expected, int target)
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        var expression = filter.GetExpression(new(new() { { "MinProfit", minProfit } }, null), filterVal);
        var compiled = expression.Compile();
        FlipInstance flip = GetSampleFlip();
        compiled.Invoke(flip).Should().Be(expected);
        if (target != 0)
            flip.Context["target"].Should().Be(target.ToString());
    }

    [TestCase("2000", "art_of_war_count:0.5,default:0.8", false, 0)]
    [TestCase("2", "art_of_war_count:0.5,default:0.8", true, 45228000)]
    public void TestProfitPerent(string minProfitPercent, string filterVal, bool expected, int target)
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        var expression = filter.GetExpression(new(new() { { "MinProfitPercentage", minProfitPercent } }, null), filterVal);
        var compiled = expression.Compile();
        FlipInstance flip = GetSampleFlip();
        compiled.Invoke(flip).Should().Be(expected);
        if (target != 0)
            flip.Context["target"].Should().Be(target.ToString());
    }

    private static FlipInstance GetSampleFlip()
    {
        return new FlipInstance()
        {
            Finder = LowPricedAuction.FinderType.CraftCost,
            Auction = new Core.SaveAuction() { StartingBid = 10000000, FlatenedNBT = new() { { "art_of_war_count", "1" } }, Enchantments = [] },
            Context = new() {
                {"cleanCost", "10000000"},
                { "breakdown", "{\"ethermerge\":16100000,\"aoteStone\":10000000,\"rarity_upgrades\":8200000,\"art_of_war_count\":8200000,\"hotpc\":7685000}" } }
        };
    }

    [TestCase("default:.6 & hpc:0.6 & ultimate_wisdom:.4 & ultimate_legion:.4 & renowned:.5 & pesterminator:.3 & hecatomb:.3", "Use commas to separate multipliers, not `&`, also don't put unnecessary spaces anywhere")]
    [TestCase("hpc:0.6", "Invalid modifier `hpc` provided, did you mean `hotpc`?")]
    [TestCase("hotpc:0.6", "No default multiplier provided, use default:0.9 to disable")]
    public void ExpectedErrors(string filterVal, string expected)
    {
        var filter = new CraftCostWeightDetailedFlipFilter();
        filter.Invoking(f => f.GetExpression(new(new() { { "MinProfit", "2" } }, null), filterVal)).Should().Throw<CoflnetException>().WithMessage(expected);
    }

}