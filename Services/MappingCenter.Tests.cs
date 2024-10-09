using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Core;

public class MappingCenterTests
{
    private MappingCenter mappingCenter;
    [SetUp]
    public void Setup()
    {
        mappingCenter = new MappingCenter(new HypixelItemService(new(), NullLogger<HypixelItemService>.Instance), (tag)
            => Task.FromResult(new Dictionary<DateTime, long> { { DateTime.UtcNow.Date.AddDays(-200), 0 } }));
    }

    [Test]
    public async Task TestGetIngredients()
    {
        var value = await mappingCenter.GetPriceForItemOn("test", DateTime.UtcNow.Date.AddDays(-2));
        Assert.That(value, Is.EqualTo(1));
        value = await mappingCenter.GetPriceForItemOn("test", DateTime.UtcNow.Date.AddDays(-50));
        Assert.That(value, Is.EqualTo(1));
    }
}