using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.FlipTracker.Client.Api;
using Confluent.Kafka;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.FlipTracker.Client.Model;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Payments.Client.Api;
using System.Collections.Concurrent;
using Coflnet.Kafka;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Leaderboard.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands
{
    public interface IFlipTrackingService : IFlipReceiveTracker
    {
        Task<int> ActiveFlipperCount();
        Task ClickFlip(string auctionId, string playerId);
        Task DownVote(string auctionId, string playerId);
        Task<List<FlipDetails>> GetFlipsForFinder(LowPricedAuction.FinderType type, DateTime start, DateTime end);
        Task<FlipSumary> GetPlayerFlips(string uuid, TimeSpan timeSpan, DateTime endTime = default);
        Task<FlipSumary> GetPlayerFlips(IEnumerable<string> uuids, TimeSpan timeSpan, DateTime endTime = default);
        Task<TierSumary> GetPreApiProfit();
        Task<(TimeSpan, int)> GetRecommendedPenalty(IEnumerable<string> playerIds);
        Task<SpeedCompResult> GetSpeedComp(IEnumerable<string> playerIds);
        Task NewFlip(LowPricedAuction flip, DateTime foundAt = default);
        Task PurchaseConfirm(string auctionId, string playerId);
        Task PurchaseStart(string auctionId, string playerId);
        Task Sold(string auctionId, string playerId);
        Task UpVote(string auctionId, string playerId);
    }

    public interface IFlipReceiveTracker
    {
        Task ReceiveFlip(string auctionId, string playerId, DateTime when = default);
    }

    public partial class FlipTrackingService : IFlipTrackingService
    {
        public TrackerApi flipTracking;
        private AnalyseApi flipAnalyse;

        //public static FlipTrackingService Instance = new FlipTrackingService();

        private static string ProduceTopic;
        private UpgradePriceService priceService;
        private ActivitySource tracer;
        private IConnectApi connectApi;
        private IProductsApi productApi;
        private IScoresApi scoresApi;
        private ILogger<FlipTrackingService> logger;


        IProducer<string, FlipEvent> producer;

        public FlipTrackingService(
            UpgradePriceService priceService,
            ActivitySource tracer,
            IConfiguration config,
            IConnectApi connectApi,
            IProductsApi productApi,
            Kafka.KafkaCreator kafkaCreator,
            IScoresApi scoresApi,
            ILogger<FlipTrackingService> logger)
        {
            producer = kafkaCreator?.BuildProducer<string, FlipEvent>();

            var url = config["FLIPTRACKER_BASE_URL"] ?? "http://" + config["FLIPTRACKER_HOST"];
            ProduceTopic = config["TOPICS:FLIP_EVENT"];
            flipTracking = new TrackerApi(url);
            flipAnalyse = new AnalyseApi(url);
            this.priceService = priceService;
            this.tracer = tracer;
            this.connectApi = connectApi;
            this.productApi = productApi;
            _ = kafkaCreator?.CreateTopicIfNotExist(ProduceTopic, 6);
            this.scoresApi = scoresApi;
            this.logger = logger;
        }


        public async Task ReceiveFlip(string auctionId, string playerId, DateTime when = default)
        {
            try
            {
                await SendEvent(auctionId, playerId, FlipEventType.FLIPRECEIVE, when);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }
        public async Task ClickFlip(string auctionId, string playerId)
        {
            await SendEvent(auctionId, playerId, FlipEventType.FLIPCLICK);
        }
        public async Task PurchaseStart(string auctionId, string playerId)
        {
            await SendEvent(auctionId, playerId, FlipEventType.PURCHASESTART);
        }
        public async Task PurchaseConfirm(string auctionId, string playerId)
        {
            await SendEvent(auctionId, playerId, FlipEventType.PURCHASECONFIRM);
        }
        public async Task Sold(string auctionId, string playerId)
        {
            await SendEvent(auctionId, playerId, FlipEventType.AUCTIONSOLD);
        }
        public async Task UpVote(string auctionId, string playerId)
        {
            await SendEvent(auctionId, playerId, FlipEventType.UPVOTE);
        }
        public async Task DownVote(string auctionId, string playerId)
        {
            await SendEvent(auctionId, playerId, FlipEventType.DOWNVOTE);
        }

        private Task SendEvent(string auctionId, string playerId, FlipEventType type, DateTime when = default)
        {
            var flipEvent = new FlipEvent()
            {
                Type = type,
                PlayerId = AuctionService.Instance.GetId(playerId),
                AuctionId = AuctionService.Instance.GetId(auctionId),
                Timestamp = when == default ? DateTime.UtcNow : when
            };

            producer.Produce(ProduceTopic, new Message<string, FlipEvent>() { Value = flipEvent, Key = flipEvent.AuctionId.ToString() });
            return Task.CompletedTask;
        }

        public async Task NewFlip(LowPricedAuction flip, DateTime foundAt = default)
        {
            await flipTracking.TrackerFlipAuctionIdPostAsync(flip.Auction.Uuid, new Flip()
            {
                FinderType = Enum.TryParse<FinderType>(flip.Finder.ToString(), true, out var finder) ? finder : FinderType.UNKOWN,
                TargetPrice = (int)flip.TargetPrice,
                Timestamp = foundAt,
                AuctionId = flip.UId
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Recomended delay for a given player
        /// </summary>
        /// <param name="playerIds">The uuid of the player to test</param>
        /// <returns></returns>
        public async Task<(TimeSpan, int)> GetRecommendedPenalty(IEnumerable<string> playerIds)
        {
            var breakdown = await GetSpeedComp(playerIds);
            var hourCount = breakdown?.Times?.Where(t => t.TotalSeconds > 1).GroupBy(t => TimeSpan.Parse(t.Age).Hours).Count() ?? 0;
            return (TimeSpan.FromSeconds(breakdown?.Penalty ?? 0), hourCount);
        }

        public async Task<TierSumary> GetPreApiProfit()
        {
            var ownedAt = await productApi.ProductsServiceServiceSlugOwnedGetAsync("pre_api", DateTime.UtcNow - TimeSpan.FromDays(1), DateTime.UtcNow);
            var minecraftConnnectionResponse = await connectApi.ConnectUsersIdsGetAsync(ownedAt.Select(o => o.UserId).Distinct().ToList());
            var includeMap = new ConcurrentDictionary<Guid, List<(DateTime start, DateTime end)>>();
            foreach (var user in minecraftConnnectionResponse)
            {
                foreach (var ownership in ownedAt)
                {
                    if (ownership.UserId != user.ExternalId)
                    {
                        continue;
                    }
                    foreach (var account in user.Accounts)
                    {
                        includeMap.GetOrAdd(Guid.Parse(account.AccountUuid), (k) => new()).Add((ownership.Start, ownership.End));
                    }
                }
            }
            var accountsToCheck = minecraftConnnectionResponse.SelectMany(m => m.Accounts).Where(a => a.LastRequestedAt > DateTime.UtcNow - TimeSpan.FromDays(3)).ToList();
            var sentRequest = ownedAt.SelectMany(o => minecraftConnnectionResponse.Where(m => m.ExternalId == o.UserId)
                    .SelectMany(m => m.Accounts.Where(a => a.LastRequestedAt > DateTime.UtcNow - TimeSpan.FromDays(3)).Select(a => new FlipTimeSelection(a.AccountUuid, o.Start, o.End)))).ToList();
            var multiRequests = Task.WhenAll(accountsToCheck.Select(async a =>
            {
                try
                {
                    return await flipTracking.FlipsPlayerIdGetAsync(Guid.Parse(a.AccountUuid), DateTime.UtcNow - TimeSpan.FromDays(1), DateTime.UtcNow);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            ));

            long totalProfitSent = await LoadProfitOfSentFlips(sentRequest);

            var result = await multiRequests;
            Console.WriteLine($"Checked {accountsToCheck.Count} accounts");
            Console.WriteLine($"Owned {ownedAt.Count} accounts, including {includeMap.Count} accounts");
            Console.WriteLine($"Found total {result.Sum(r => r.Count)} flips");
            var flipsBoughtWhile = new List<PastFlip>();
            foreach (var flip in result.Where(r => r != null).SelectMany(r => r))
            {
                if (!includeMap.TryGetValue(flip.Flipper, out var include))
                    continue;
                if (include.Any(i => flip.PurchaseTime > i.start && flip.PurchaseTime < i.end))
                {
                    flipsBoughtWhile.Add(flip);
                }
            }
            var profitPerAccount = flipsBoughtWhile.GroupBy(f => f.Flipper).ToDictionary(g => g.Key, g => g.Sum(f => f.Profit));
            var profitPerUser = minecraftConnnectionResponse.Select(m => new
            {
                m.ExternalId,
                Profit = m.Accounts.Sum(a => profitPerAccount.GetValueOrDefault(Guid.Parse(a.AccountUuid), 0))
            }).ToList();
            var totalProfit = flipsBoughtWhile.Sum(f => f.Profit);
            Console.WriteLine($"Found {flipsBoughtWhile.Count} preapi flips");
            return new()
            {
                AverageProfit = totalProfit / Math.Max(1, includeMap.Count),
                HourCount = ownedAt.Count,
                FlipCount = flipsBoughtWhile.Count,
                PlayerCount = minecraftConnnectionResponse.Count(),
                BestProfit = flipsBoughtWhile.Select(f => f.Profit).DefaultIfEmpty(0L).Max(),
                BestProfitName = flipsBoughtWhile.MaxBy(f => f.Profit)?.ItemName,
                Profit = totalProfit,
                ProfitSent = totalProfitSent,
                MostUserProfit = profitPerUser.Select(f => f.Profit).DefaultIfEmpty(0L).Max()
            };
        }

        private async Task<long> LoadProfitOfSentFlips(List<FlipTimeSelection> sentRequest)
        {
            var totalSent = await flipAnalyse.FlipsSentPostAsync(sentRequest);
            var sentAuctionids = totalSent.Select(f => long.Parse(f.AuctionId)).ToHashSet();
            var sentMap = new Dictionary<string, (string, long)>();
            using (var context = new HypixelContext())
            {
                var auctions = await context.Auctions.Include(a => a.Bids).Where(a => sentAuctionids.Contains(a.UId))
                    .Select(a => new
                    {
                        a.UId,
                        a.HighestBidAmount,
                        Buyer = a.Bids.OrderByDescending(b => b.Amount).Select(b => b.Bidder).FirstOrDefault()
                    })
                    .ToListAsync();
                foreach (var auction in auctions)
                {
                    sentMap[auction.UId.ToString()] = (auction.Buyer, auction.HighestBidAmount);
                }
            }
            var totalProfitSent = totalSent.Where(f => sentMap.TryGetValue(f.AuctionId, out var sent)).Sum(f => f.Worth - sentMap[f.AuctionId].Item2);
            Console.WriteLine($"Sent {sentMap.Count} flips value {totalProfitSent}");
            return totalProfitSent;
        }

        /// <summary>
        /// Get the speed data for a given player
        /// </summary>
        public virtual async Task<SpeedCompResult> GetSpeedComp(IEnumerable<string> playerIds)
        {
            return await flipAnalyse.PlayersSpeedPostAsync(new SpeedCheckRequest(playerIds.ToList())).ConfigureAwait(false);
        }

        public async Task<int> ActiveFlipperCount()
        {
            return await flipAnalyse.UsersActiveCountGetAsync().ConfigureAwait(false);
        }

        public async Task<List<FlipDetails>> GetFlipsForFinder(LowPricedAuction.FinderType type, DateTime start, DateTime end)
        {
            if (start > end)
            {
                var tmp = end;
                end = start;
                start = tmp;
            }
            if (start < end - TimeSpan.FromDays(1))
                throw new CoflnetException("span_to_large", "Querying for more than a day is not supported");

            var idTask = flipAnalyse.AnalyseFinderFinderTypeGetAsync(Enum.Parse<FinderType>(type.ToString(), true), start, end).ConfigureAwait(false);
            using (var context = new HypixelContext())
            {
                var receivedFlips = await idTask;
                if (receivedFlips == null)
                    throw new CoflnetException("retrieve_failed", "Could not retrieve data from the flip tracker");
                var flips = receivedFlips.GroupBy(r => r.AuctionId).Select(g => g.First()).ToDictionary(f => f.AuctionId);
                var ids = flips.Keys;
                var buyList = await context.Auctions.Where(a => ids.Contains(a.UId) && a.HighestBidAmount > 0)
                    .Include(a => a.NBTLookup)
                    .AsNoTracking()
                    .ToListAsync().ConfigureAwait(false);
                // only include flips that were bought shortly after being reported
                //buyList = buyList.Where(a => !flips.TryGetValue(a.UId, out Flip f) || f.Timestamp < a.End && f.Timestamp > a.End - TimeSpan.FromSeconds(50)).ToList();

                var uidKey = NBT.Instance.GetKeyId("uid");
                var buyLookup = buyList
                    .Where(a => a.NBTLookup.Where(l => l.KeyId == uidKey).Any())
                    .GroupBy(a =>
                    {
                        return a.NBTLookup.Where(l => l.KeyId == uidKey).FirstOrDefault().Value;
                    }).ToDictionary(b => b.Key);
                var buyUidLookup = buyLookup.Select(a => a.Key).ToHashSet();
                var sellIds = await context.NBTLookups.Where(b => b.KeyId == uidKey && buyUidLookup.Contains(b.Value)).AsNoTracking().Select(n => n.AuctionId).ToListAsync();
                var buyAuctionUidLookup = buyLookup.Select(a => a.Value.First().UId).ToHashSet();
                var sells = await context.Auctions.Where(b => sellIds.Contains(b.Id) && !buyAuctionUidLookup.Contains(b.UId) && b.End > start && b.HighestBidAmount > 0 && b.End < DateTime.UtcNow)
                                        .Select(s => new { s.End, s.HighestBidAmount, s.NBTLookup, s.Uuid }).AsNoTracking().ToListAsync().ConfigureAwait(false);

                return sells.Select(s =>
                {
                    var uid = s.NBTLookup.Where(b => b.KeyId == uidKey).FirstOrDefault().Value;
                    var buy = buyLookup.GetValueOrDefault(uid)?.OrderBy(b => b.End).Where(b => b.Uuid != s.Uuid).FirstOrDefault();
                    if (buy == null)
                        return null;
                    // make sure that this is the correct sell of this flip
                    if (buy.End > s.End)
                        return null;
                    if (buy.HighestBidAmount == 0)
                        return null;

                    var profit = s.HighestBidAmount * 98 / 100
                                - buy.HighestBidAmount;


                    return new FlipDetails()
                    {
                        BuyTime = buy.End,
                        Finder = type,
                        ItemName = buy.ItemName,
                        ItemTag = buy.Tag,
                        OriginAuction = buy.Uuid,
                        PricePaid = buy.HighestBidAmount,
                        SellTime = s.End,
                        SoldAuction = s.Uuid,
                        SoldFor = s.HighestBidAmount,
                        Tier = buy.Tier.ToString(),
                        uId = uid,
                        Profit = profit
                    };
                }).Where(f => f != null).GroupBy(s => s.OriginAuction)
                .Select(s => s.Where(s => s.SellTime > s.BuyTime).OrderBy(s => s.SellTime).FirstOrDefault())
                .Where(f => f != null)
                .ToList();
            }

        }
        public async Task<FlipSumary> GetPlayerFlips(string uuid, TimeSpan timeSpan, DateTime endTime = default)
        {
            return await GetPlayerFlips(new string[] { uuid }, timeSpan, endTime);
        }

        public async Task<FlipSumary> GetPlayerFlips(IEnumerable<string> uuids, TimeSpan timeSpan, DateTime endTime = default)
        {
            if (endTime == default)
                endTime = DateTime.UtcNow;
            var startTime = endTime.Subtract(timeSpan);
            var playerGuids = uuids.Select(u => Guid.Parse(u)).ToHashSet();
            Activity.Current.Log("uuids " + JsonConvert.SerializeObject(uuids));
            // use flipTracking.FlipsPlayerIdGetAsync(uuid)
            var allSoldFlips = await Task.WhenAll(uuids.Select(async uuid =>
            {
                var response = await flipTracking.FlipsPlayerIdGetWithHttpInfoAsync(Guid.Parse(uuid), startTime, endTime);
                var list = JsonConvert.DeserializeObject<List<PastFlip>>(response.RawContent);
                if (list == null)
                    throw new CoflnetException("load_failed", $"Loading flips for {uuid} failed");
                return (list, uuid);
            }));

            var relevantFlips = allSoldFlips.Where(f => f.Item1 != null).SelectMany(f => f.Item1.Select(fl => (Flip: fl, f.uuid)))
                .Where(f => f.Flip != null && f.Flip.Profit != 0)
                .GroupBy(f => f.Flip.PurchaseAuctionId)
                .Select(g => g.Last()).ToList();
            var newFlips = relevantFlips.Select(e =>
            {
                var f = e.Flip;
                return new FlipDetails()
                {
                    BuyTime = f.PurchaseTime,
                    Finder = Enum.TryParse<LowPricedAuction.FinderType>(f.FinderType.ToString().Replace("SNIPERMEDIAN", "SNIPER_MEDIAN"), true, out var finder) ? finder : LowPricedAuction.FinderType.UNKOWN,
                    ItemName = f.ItemName,
                    ItemTag = f.ItemTag,
                    OriginAuction = f.PurchaseAuctionId.ToString("N"),
                    PricePaid = f.PurchaseCost,
                    SellTime = f.SellTime,
                    SoldAuction = f.SellAuctionId.ToString("N"),
                    SoldFor = f.SellPrice,
                    Tier = f.ItemTier.ToString(),
                    uId = f.Uid,
                    PropertyChanges = f.ProfitChanges.Select(c => new PropertyChange(c.Label, c.Amount)).ToList(),
                    Profit = f.Profit,
                    // flag enum converted to array
                    Flags = (Shared.FlipFlags)f.Flags,
                    Seller = e.uuid
                };
            }).ToArray();
            foreach (var uuid in uuids)
            {
                if (timeSpan < TimeSpan.FromDays(6.999) || endTime <= DateTime.UtcNow - TimeSpan.FromSeconds(10))
                    continue;
                var accountProfit = relevantFlips
                    .Where(f => f.uuid == uuid)
                    .Where(f => f.Flip.SellTime > DateTime.UtcNow - TimeSpan.FromDays(7))
                    .Sum(f => f.Flip.Profit);
                logger.LogInformation($"Player {uuid} made {accountProfit} profit in {timeSpan} {relevantFlips.Count} flips checked ({allSoldFlips.Count()})");
                if (accountProfit == 0 && relevantFlips.Count() == 0)
                    continue;
                SaveProfitToLeaderboard(uuids.First(), accountProfit);
            }
            var profit = newFlips.Sum(r => r.Profit);
            return new FlipSumary()
            {
                Flips = newFlips,
                TotalProfit = profit
            };

            using var context = new HypixelContext();
            var playerIds = await context.Players.Where(p => uuids.Contains(p.UuId)).AsNoTracking().Select(p => p.Id).ToListAsync();
            var uidKey = NBT.Instance.GetKeyId("uid");
            var sellList = await context.Auctions.Where(a => playerIds.Contains(a.SellerId))
                .Where(a => a.End > endTime && a.End < DateTime.UtcNow && a.HighestBidAmount > 0)
                .Include(a => a.NBTLookup)
                .Include(a => a.Enchantments)
                .AsNoTracking()
                .ToListAsync().ConfigureAwait(false);

            var sells = sellList
                .Where(a => a.NBTLookup.Where(l => l.KeyId == uidKey).Any())
                .GroupBy(a =>
                {
                    return a.NBTLookup.Where(l => l.KeyId == uidKey).FirstOrDefault().Value;
                }).ToList();
            var SalesUidLookup = sells.Select(a => a.Key).ToHashSet();
            var sales = await context.NBTLookups.Where(b => b.KeyId == uidKey && SalesUidLookup.Contains(b.Value)).AsNoTracking().Select(n => n.AuctionId).ToListAsync();
            var playerBids = await context.Bids.Where(b => playerIds.Contains(b.BidderId) && sales.Contains(b.Auction.Id) && b.Timestamp > endTime.Subtract(TimeSpan.FromDays(14)))
                .AsNoTracking()
                // filtering
                .OrderByDescending(bid => bid.Id)
                .Select(b => new
                {
                    b.Auction.Uuid,
                    b.Auction.End,
                    b.Auction.Tag,
                    b.Auction.Tier,
                    b.Amount,
                    Enchants = b.Auction.Enchantments,
                    Nbt = b.Auction.NBTLookup
                }).GroupBy(b => b.Uuid)
                .Select(bid => new BidQuery(
                    bid.Key,
                    bid.Max(b => b.Amount),
                    bid.Max(b => b.Amount),
                    bid.Max(b => b.End),
                    bid.First().Tag,
                    bid.First().Tier,
                    bid.OrderByDescending(b => b.Amount).First().Nbt.ToArray(),
                    bid.First().Enchants
                ))
                //.ThenInclude (b => b.Auction)
                .ToListAsync().ConfigureAwait(false);

            var flipStats = (await flipTracking.TrackerBatchFlipsPostAsync(playerBids.Select(b => AuctionService.Instance.GetId(b.Key)).ToList()))
                    ?.GroupBy(t => t.AuctionId)
                    ?.ToDictionary(t => t.Key, v => v.AsEnumerable());
            var flips = playerBids.Where(a => SalesUidLookup.Contains(a.Nbt.Where(b => b.KeyId == uidKey).FirstOrDefault().Value)).Select(b =>
            {

                return ToFlipDetails(b, uidKey, sells, flipStats);

            }).OrderByDescending(f => f.Profit).ToArray();

            return new FlipSumary()
            {
                Flips = flips,
                TotalProfit = flips.Sum(r => r.Profit)
            };

        }

        private void SaveProfitToLeaderboard(string uuid, long totalProfit)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // postfix week
                    var weekStartDate = DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd");
                    var boardSlug = $"sky-flippers-{weekStartDate}";
                    var looserBoard = $"sky-flippers-loosers-{weekStartDate}";
                    await scoresApi.ScoresLeaderboardSlugPostAsync(boardSlug, new ScoreCreate(uuid, totalProfit, 100));
                    await scoresApi.ScoresLeaderboardSlugPostAsync(looserBoard, new ScoreCreate(uuid, -totalProfit, 100));
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to post flip score");
                }
            });
        }

        private FlipDetails ToFlipDetails(BidQuery b, short uidKey, List<IGrouping<long, SaveAuction>> sells, Dictionary<long, IEnumerable<Flip>> flipStats)
        {
            Flip first = flipStats?.GetValueOrDefault(AuctionService.Instance.GetId(b.Key))?.OrderBy(b => b.Timestamp).FirstOrDefault();
            var uId = b.Nbt.Where(b => b.KeyId == uidKey).FirstOrDefault().Value;
            var sell = sells.Where(s => s.Key == uId)?
                    .FirstOrDefault()
                    ?.OrderByDescending(b => b.End)
                    .FirstOrDefault();
            try
            {
                return ToFlipDetails(b, first, uId, sell);
            }
            catch
            {
                Activity.Current?.AddTag("buy", JsonConvert.SerializeObject(b));
                Activity.Current?.AddTag("sell", JsonConvert.SerializeObject(sell));
                throw;
            }
        }

        private FlipDetails ToFlipDetails(BidQuery b, Flip first, long uId, SaveAuction sell)
        {
            var soldFor = sell
                                ?.HighestBidAmount;

            var enchantsBad = b.Tag == "ENCHANTED_BOOK" && (b.Enchants.Count == 1 && sell.Enchantments.Count != 1 || b.Enchants.First().Level != sell.Enchantments.First().Level)
                                && (sell.HighestBidAmount - b.HighestOwnBid) > 1_000_000;
            var profit = 1L;
            var changeSumary = new List<PropertyChange>();
            if (b.Tag == sell.Tag
                && !enchantsBad)
            {
                try
                {
                    changeSumary.AddRange(GetChanges(b, sell));
                }
                catch (Exception e)
                {
                    Activity.Current?.AddTag("exception", e.ToString());
                    changeSumary.Add(new("Error occured " + Activity.Current.TraceId, -1));
                }
                var tax = sell.HighestBidAmount - sell.HighestBidAmount * 98 / 100;
                changeSumary.Add(new PropertyChange()
                {
                    Description = $"2% AH tax for sell",
                    Effect = -tax
                });

                profit = changeSumary.Sum(g => g.Effect)
                + sell.HighestBidAmount
                - b.HighestOwnBid;
            }


            return new FlipDetails()
            {
                Finder = (first == null ? LowPricedAuction.FinderType.UNKOWN : Enum.Parse<LowPricedAuction.FinderType>(
                    first.FinderType.ToString().Replace("SNIPERMEDIAN", "SNIPER_MEDIAN"), true)),
                OriginAuction = b.Key,
                ItemTag = sell.Tag,
                Tier = sell.Tier.ToString(),
                SoldAuction = sell?.Uuid,
                PricePaid = b.HighestOwnBid,
                SoldFor = soldFor ?? 0,
                uId = uId,
                ItemName = sell?.ItemName,
                BuyTime = b.End,
                SellTime = sell.End,
                Profit = profit,
                PropertyChanges = changeSumary
            };
        }

        private IEnumerable<PropertyChange> GetChanges(BidQuery b, SaveAuction sell)
        {
            if (b == null || sell.Tag == null)
                yield break;
            if (b.Tier < sell.Tier)
                if (sell.Tag.StartsWith("PET_"))
                {
                    if (sell.NBTLookup.Where(l => l.KeyId == NBT.Instance.GetKeyId("heldItem") && l.Value == ItemDetails.Instance.GetItemIdForTag("TIER_BOOST")).Any())
                        yield return new("Tier Boost cost", priceService.GetTierBoostCost());
                    else
                    {
                        var cost = priceService.GetKatPrice(sell.Tag, sell.Tier);
                        yield return new("Kat upgrade", (long)-cost.UpgradeCost);
                        if (cost.MaterialCost > 0)
                            yield return new("Kat materials", (long)-cost.MaterialCost);
                    }
                }
                else
                    yield return new("Recombobulator", (long)-priceService.GetPrice("RECOMBOBULATOR_3000"));
        }
    }
}