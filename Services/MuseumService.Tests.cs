using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using System;
using NUnit.Framework;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace Coflnet.Sky.Commands.Shared;

public class MuseumServiceTests
{
    Core.Services.Item baseItem = new("xy", "111", "Tes Item", "b", "LEGENDARY", 0, null, null, 0, null, false, "TEST", null, null, null, null, null, "none", null, null, null,
            new(0, "ARMOR_SETS", new(), null, new() { { "XY", 4 } }, "INTERMEDIATE"), false, null, null, null, null);
    [SetUp]
    public void Setup()
    {
    }
    [Test]
    public async Task ArmrorSetExclude()
    {
        var auctionMock = new Mock<IAuctionApi>();
        var itemService = new Mock<IHypixelItemStore>();
        itemService.Setup(service => service.GetItemsAsync()).ReturnsAsync(() => new Dictionary<string, Core.Services.Item>(){
            {"1", new CopyAble(baseItem){MuseumData=new(0, "ARMOR_SETS", new(), null, new() { { "GOBLIN", 4 } }, "INTERMEDIATE")}}
        });
        auctionMock.Setup(service => service.ApiAuctionLbinsGetAsync(0, default)).ReturnsAsync(() => new Dictionary<string, Sniper.Client.Model.ReferencePrice>(){
            {"1", new (){Price=100, AuctionId=1234}}
        });
        var service = new MuseumService(auctionMock.Object, NullLogger<MuseumService>.Instance, itemService.Object);
        var result = await service.GetBestOptions(new HashSet<string>(), 30);
        result.Count.Should().Be(1);

        result = await service.GetBestOptions(new HashSet<string>() { "GOBLIN" }, 30);
        result.Count.Should().Be(0);
    }

    [Test]
    public async Task ExtendSets()
    {
        var auctionMock = new Mock<IAuctionApi>();
        var itemService = new Mock<IHypixelItemStore>();
        itemService.Setup(service => service.GetItemsAsync()).ReturnsAsync(() => new Dictionary<string, Core.Services.Item>(){
            {"1", new CopyAble(baseItem){MuseumData=new(0, "ARMOR_SETS", new(), null, new() { { "CRIMSON_HUNTER", 4 } }, "INTERMEDIATE")}}
        });
        auctionMock.Setup(service => service.ApiAuctionLbinsGetAsync(0, default)).ReturnsAsync(() => new Dictionary<string, Sniper.Client.Model.ReferencePrice>(){
            {"1", new (){Price=100, AuctionId=1234}},
            {"BLAZE_BELT", new (){Price=100, AuctionId=1235}}
        });
        var service = new MuseumService(auctionMock.Object, NullLogger<MuseumService>.Instance, itemService.Object);
        var result = await service.GetBestOptions(new HashSet<string>(), 30);
        result.First().Value.auctionid.Length.Should().Be(2);
    }

    public record CopyAble : Core.Services.Item
    {
        public CopyAble(Core.Services.Item item) : base(item)
        {
        }


    }
}