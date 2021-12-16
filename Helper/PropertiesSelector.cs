using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using hypixel;

namespace Coflnet.Sky.Commands.Helper
{
    /// <summary>
    /// Takes care of selecting interesting/relevant properties from a flip
    /// </summary>
    public class PropertiesSelector
    {
        [DataContract]
        public class Property
        {
            [DataMember(Name = "val")]
            public string Value;
            /// <summary>
            /// how important is this?
            /// </summary>
            [IgnoreDataMember]
            public int Rating;

            public Property()
            { }
            public Property(string value, int rating)
            {
                Value = value;
                Rating = rating;
            }

        }

        private static Dictionary<Enchantment.EnchantmentType, byte> RelEnchantLookup = null;

        public static IEnumerable<Property> GetProperties(SaveAuction auction)
        {
            var properties = new List<Property>();

            if (RelEnchantLookup == null)
                RelEnchantLookup = Sky.Constants.RelevantEnchants.ToDictionary(r => r.Type, r => r.Level);


            var data = auction.FlatenedNBT;

            if (data.ContainsKey("winning_bid"))
            {
                properties.Add(new Property("Top Bid: " + string.Format("{0:n0}", long.Parse(data["winning_bid"])), 20));
            }
            if (data.ContainsKey("hpc"))
                properties.Add(new Property("HPB: " + data["hpc"], 12));
            if (data.ContainsKey("rarity_upgrades"))
                properties.Add(new Property("Recombobulated ", 12));
            if (auction.Count > 1)
                properties.Add(new Property($"Count x{auction.Count}", 12));
            if (data.ContainsKey("heldItem"))
                properties.Add(new Property($"Holds {ItemDetails.TagToName(data["heldItem"])}", 12));
            if (data.ContainsKey("candyUsed"))
                properties.Add(new Property($"Candy Used {data["candyUsed"]}", 11));
            if (data.ContainsKey("farming_for_dummies_count"))
                properties.Add(new Property($"Farming for dummies {data["farming_for_dummies_count"]}", 11));
            if (data.ContainsKey("skin"))
                properties.Add(new Property($"Skin: {ItemDetails.TagToName(data["skin"])}", 15));
            if (data.ContainsKey("spider_kills"))
                properties.Add(new Property($"Kills: {ItemDetails.TagToName(data["spider_kills"])}", 15));
            if (data.ContainsKey("zombie_kills"))
                properties.Add(new Property($"Kills: {ItemDetails.TagToName(data["zombie_kills"])}", 15));

            properties.AddRange(data.Where(p => p.Value == "PERFECT" || p.Value == "FLAWLESS").Select(p => new Property($"{p.Value} gem", p.Value == "PERFECT" ? 14 : 7)));

            var isBook = auction.Tag == "ENCHANTED_BOOK";

            properties.AddRange(auction.Enchantments?.Where(e => (!RelEnchantLookup.ContainsKey(e.Type) && e.Level >= 6) || (RelEnchantLookup.TryGetValue(e.Type, out byte lvl)) && e.Level >= lvl).Select(e => new Property()
            {
                Value = $"{ItemDetails.TagToName(e.Type.ToString())}: {e.Level}",
                Rating = 2 + e.Level + (e.Type.ToString().StartsWith("ultimate") ? 5 : 0) + (e.Type == Enchantment.EnchantmentType.infinite_quiver ? -3 : 0)
            }));

            return properties;
        }
    }
}