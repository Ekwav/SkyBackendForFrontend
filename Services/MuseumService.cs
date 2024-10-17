using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Commands.Shared;

public class MuseumService
{
    private IAuctionApi sniperApi;
    private ILogger<MuseumService> logger;
    private HypixelItemService hypixelItemService;

    public MuseumService(IAuctionApi sniperApi, ILogger<MuseumService> logger, HypixelItemService hypixelItemService)
    {
        this.sniperApi = sniperApi;
        this.logger = logger;
        this.hypixelItemService = hypixelItemService;
    }

    public async Task<IEnumerable<Cheapest>> GetBestMuseumPrices()
    {
        var items = await hypixelItemService.GetItemsAsync();
        var prices = await sniperApi.ApiAuctionLbinsGetAsync();

        var donateableItems = items.Where(i => i.Value.MuseumData != null);
        var single = donateableItems.Where(i => i.Value.MuseumData.DonationXp > 0).ToDictionary(i => i.Key, i => i.Value.MuseumData.DonationXp);

        var result = new Dictionary<string, (long pricePerExp, long auctionid)>();
        foreach (var item in single)
        {
            if (prices.TryGetValue(item.Key, out var price))
            {
                result.Add(item.Key, (price.Price / item.Value, price.AuctionId));
            }
        }
        var best10 = result.OrderBy(i => i.Value.Item1).Take(10).ToDictionary(i => i.Key, i => i.Value);
        var ids = best10.Select(i => i.Value.auctionid).ToList();
        using (var db = new HypixelContext())
        {
            var auctions = await db.Auctions.Where(a => ids.Contains(a.UId)).ToListAsync();
            var byUid = auctions.ToDictionary(a => a.UId);
            return best10.Where(b=>byUid.ContainsKey(b.Value.auctionid)).Select(a => new Cheapest
            {
                AuctuinUuid = byUid[a.Value.auctionid].Uuid,
                ItemName = byUid[a.Value.auctionid].ItemName,
                PricePerExp = a.Value.pricePerExp
            });
        }
    }

    public class Cheapest
    {
        public string AuctuinUuid { get; set; }
        public string ItemName { get; set; }
        public long PricePerExp { get; set; }
    }
}