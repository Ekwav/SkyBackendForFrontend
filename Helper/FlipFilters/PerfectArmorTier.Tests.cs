using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class PerfectArmorTierTests
{
    [TestCase("PERFECT_HELMET_1", "1", true)]
    [TestCase("PERFECT_HELMET_1", "1-10", true)]
    [TestCase("PERFECT_HELMET_1", ">2", false)]
    [TestCase("PERFECT_HELMET_11", ">2", true)]
    [TestCase("KEVIN", "1-13", false)]
    [TestCase("SO_KEVIN", "1", false)]
    [TestCase("THEORETICAL_HOE_WARTS_3", "1-12", false)]
    public void Match(string tag, string selector, bool expected)
    {
        var filter = new PerfectArmorTierDetailedFlipFilter() as DetailedFlipFilter;
        var flip = new FlipInstance()
        {
            Tag = tag,
        };
        Assert.That(expected, Is.EqualTo(filter.GetExpression(new FilterContext(new(), null),selector).Compile()(flip)));
    }
}