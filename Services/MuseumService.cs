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

    public async Task<IEnumerable<Cheapest>> GetBestMuseumPrices(HashSet<string> alreadyDonated, int amount = 30)
    {
        var items = await hypixelItemService.GetItemsAsync();
        var prices = await sniperApi.ApiAuctionLbinsGetAsync();

        var donateableItems = items.Where(i => i.Value.MuseumData != null);
        var single = donateableItems.Where(i => i.Value.MuseumData.DonationXp > 0).ToDictionary(i => i.Key, i => i.Value.MuseumData.DonationXp);

        var set = donateableItems.Where(i => i.Value.MuseumData.ArmorSetDonationXp != null && i.Value.MuseumData.ArmorSetDonationXp?.Count != 0)
                .GroupBy(i=>i.Value.MuseumData.ArmorSetDonationXp.First().Key)
                .ToDictionary(i => i.First().Value.MuseumData.ArmorSetDonationXp.First(), 
                    i => (i.First().Value.MuseumData.ArmorSetDonationXp.First().Value, i.Select(j => j.Key).ToArray()));

        var result = new Dictionary<string, (long pricePerExp, long[] auctionid)>();
        foreach (var item in single)
        {
            if (prices.TryGetValue(item.Key, out var price))
            {
                result.Add(item.Key, (price.Price / item.Value, new[] { price.AuctionId }));
            }
        }
        var best10 = result.Where(r => !alreadyDonated.Contains(r.Key))
            .OrderBy(i => i.Value.Item1)
            .Take(amount).ToDictionary(i => i.Key, i => i.Value);
        var ids = best10.SelectMany(i => i.Value.auctionid).ToList();
        using (var db = new HypixelContext())
        {
            var auctions = await db.Auctions.Where(a => ids.Contains(a.UId)).ToListAsync();
            var byUid = auctions.ToDictionary(a => a.UId);
            return best10.Where(b => b.Value.auctionid.All(x=>byUid.ContainsKey(x))).Select(a => {
                if(a.Value.auctionid.Length > 1)
                {
                    return new Cheapest
                    {
                        Uuids = a.Value.auctionid.Select(x=>byUid[x].Uuid).ToArray(),
                        ItemName = byUid[a.Value.auctionid[0]].ItemName,
                        PricePerExp = a.Value.pricePerExp
                    };
                }
                return new Cheapest
                {
                    AuctuinUuid = byUid[a.Value.auctionid.First()].Uuid,
                    ItemName = byUid[a.Value.auctionid.First()].ItemName,
                    PricePerExp = a.Value.pricePerExp
                };
            });
        }
    }

    public class Cheapest
    {
        public string AuctuinUuid { get; set; }
        public string[] Uuids { get; set; }
        public string ItemName { get; set; }
        public long PricePerExp { get; set; }
    }
}