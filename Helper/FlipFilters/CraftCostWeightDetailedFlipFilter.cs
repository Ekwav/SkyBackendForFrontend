
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Adjusts target price based on craft cost of ingredients multiplied by weight")]
public class CraftCostWeightDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override FilterType FilterType => FilterType.RANGE;
    private static Dictionary<string, double> DefaultWeights = new() {
        { "skin", 0.5 },
        { "ultimate_fatal_tempo", 0.65},
        { "rarity_upgrades", 0.5},
        { "upgrade_level", 0.8},
        { "talisman_enrichment", 0.1},
        { "RUNE_MUSIC", 0.5},
        { "RUNE_MEOW_MUSIC", 0.5},
        { "RUNE_DRAGON", 0.5},
        { "RUNE_TIDAL", 0.5},
        { "RUNE_GRAND_SEARING", 0.5},
        { "RUNE_ENCHANT", 0.5},
        { "RUNE_BARK_TUNES", 0.5},
        { "RUNE_BARK_SMITTEN", 0.5},
        { "RUNE_BARK_ICE_SKATES", 0.5},
        { "RUNE_SPELLBOUND", 0.5},
        { "RUNE_GRAND_FREEZING", 0.5},
        { "RUNE_PRIMAL_FEAR", 0.5}
    };
    public CraftCostWeightDetailedFlipFilter()
    {
        foreach (var item in Enum.GetNames<ItemReferences.Reforge>())
        {
            validModifiers.Add(item); // technically not all reforges can show up because of the threshold
        }
    }

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        Dictionary<string, double> multipliers = ParseMultipliers(val);
        foreach (var item in multipliers)
        {
            var targetAttrib = item.Key.Split('.')[0];
            if (validModifiers.Contains(targetAttrib, StringComparer.OrdinalIgnoreCase))
                continue;
            var mostSimilar = validModifiers.OrderBy(m => Fastenshtein.Levenshtein.Distance(targetAttrib, m)).First();
            throw new CoflnetException("invalid_argument", $"Invalid modifier `{item.Key}` provided, did you mean `{mostSimilar}`?");
        }
        foreach (var item in DefaultWeights)
        {
            multipliers.TryAdd(item.Key, item.Value);
        }
        if (!multipliers.TryGetValue("default", out var defaultMultiplier))
            throw new CoflnetException("missing_argument", "No default multiplier provided, use default:0.9 to disable");

        filters.filters.TryGetValue("MinProfit", out var minprofitString);
        NumberParser.TryLong(minprofitString, out var target);
        var anyDot = multipliers.Keys.Any(k => k.Contains('.'));

        return f => f.Finder == LowPricedAuction.FinderType.CraftCost &&
            CalculateCraftCostWithMultipliers(f, multipliers, defaultMultiplier, target, anyDot); // clear up temp stored
    }

    private static bool CalculateCraftCostWithMultipliers(FlipInstance f, Dictionary<string, double> multipliers, double defaultMultiplier, long target, bool anyDot)
    {
        if (!f.Context.TryGetValue("breakdown", out var breakdownSerialized))
            return false;
        var breakdown = JsonSerializer.Deserialize<Dictionary<string, long>>(breakdownSerialized);
        var lookup = new Dictionary<string, string>(f.Auction.FlatenedNBT, StringComparer.OrdinalIgnoreCase);
        if (anyDot)
            foreach (var item in f.Auction.Enchantments)
            {
                lookup.TryAdd(item.Type.ToString(), item.Level.ToString());
            }
        var valueSum = breakdown.Select(b => GetMultiplier(multipliers, defaultMultiplier, b, lookup) * b.Value).Sum()
            + long.Parse(f.Context["cleanCost"]);

        if (target > valueSum * 0.98 - f.Auction.StartingBid)
            return false;
        f.Context["target"] = valueSum.ToString();
        return true;
    }

    private static double GetMultiplier(Dictionary<string, double> multipliers, double defaultMultiplier, KeyValuePair<string, long> b, Dictionary<string, string> lookup)
    {
        if (multipliers.TryGetValue($"{b.Key}.{lookup.GetValueOrDefault(b.Key)}", out var multiplier))
            return multiplier;
        return multipliers.GetValueOrDefault(b.Key, defaultMultiplier);
    }

    private static Dictionary<string, double> ParseMultipliers(string val)
    {
        if(val.Contains('&'))
        {
            throw new CoflnetException("invalid_argument", "Use commas to separate multipliers, not `&`, also don't put unnecessary spaces anywhere");
        }
        try
        {
            return val.Split(',').ToDictionary(m => m.Split(':')[0], m => NumberParser.Double(m.Split(':')[1]), StringComparer.OrdinalIgnoreCase);
        }
        catch (System.Exception e)
        {
            Console.WriteLine("craftcost filter: " + e);
            throw new CoflnetException("filter_parsing", $"Error in filter CraftCostWeight. Make sure to specify pairs separated by commas of like `modifier:multiplier,sharpness:0.7`");
        }
    }

    private readonly HashSet<string> validModifiers = ["default",
    "rarity_upgrades", "color", "upgrade_level", "protection", "ultimate_wisdom", "mana_vampire", "hotpc", "growth", "ultimate_last_stand",
        "pesterminator", "champion", "ultimate_wise", "ultimate_soul_eater", "art_of_war_count", "fire_aspect", "ultimate_combo", "unlocked_slots",
        "ultimate_one_for_all", "tabasco", "sharpness", "ultimate_swarm", "bane_of_arthropods", "smite", "dragon_hunter", "titan_killer",
        "thunderbolt", "thunderlord", "blazing_fortune", "mana_pool", "fishing_experience", "dominance", "breeze", "life_regeneration",
        "mana_regeneration", "virtual", "infection", "ender_resistance", "veteran", "undead_resistance", "lifeline", "fortitude",
        "blazing_resistance", "speed", "mending", "experience", "arachno_resistance", "prosperity", "ferocious_mana", "skin", "ultimate_legion",
        "hardened_mana", "blast_protection", "hecatomb", "fire_protection", "ultimate_bobbin_time", "strong_mana", "feather_falling", "dye_item",
        "execute", "syphon", "smoldering", "critical", "cleave", "ultimate_chimera", "COMBAT_0", "OPAL_0", "giant_killer", "scavenger",
        "triple_strike", "divine_gift", "prosecute", "venomous", "RUNE_MUSIC", "first_strike", "luck", "looting", "RUNE_MEOW_MUSIC", "vicious",
        "magic_find", "reflection", "ultimate_refrigerate", "ultimate_the_one", "cayenne", "candyUsed", "exp", "baseStatBoostPercentage",
        "baseStatBoost", "lure", "piscary", "blessing", "expertise_kills", "RUNE_RAINY_DAY", "efficiency", "paleontologist", "pristine",
        "expertise", "ultimate_flash", "double_hook", "fishing_speed", "fisherman", "trophy_hunter", "charm", "frail", "petItem", "smarty_pants",
        "edition", "ultimate_reiterate", "ultimate_fatal_tempo", "RUNE_BARK_TUNES", "toxophilite", "overload", "cubism", "power", "snipe",
        "ultimate_rend", "chance", "artOfPeaceApplied", "RUNE_TIDAL", "talisman_enrichment", "AMBER_0", "compact", "life_steal",
        "ultimate_inferno", "ender_slayer", "farming_for_dummies_count", "harvesting", "cultivating", "sunder", "dedication",
        "new_years_cake", "JASPER_0", "JASPER_1", "ice_cold", "winning_bid", "full_bid", "RUNE_SLIMY", "SAPPHIRE_0", "ethermerge", "pgems",
        "divan_powder_coating", "AMBER_1", "TOPAZ_0", "RUNE_CLOUDS", "scroll_count", "is_shiny", "eman_kills", "COMBAT_1",
        "projectile_protection", "raider_kills", "RUNE_REDSTONE", "green_thumb", "mined_crops", "model", "ultimate_no_pain_no_gain",
        "RUNE_ENCHANT", "RUNE_PRIMAL_FEAR", "lapidary", "drill_part_fuel_tank", "drill_part_upgrade_module", "ultimate_flowstate",
        "drill_part_engine", "RUNE_LAVATEARS", "handles_found", "RUNE_GRAND_FREEZING", "counter_strike", "wood_singularity_count",
        "RUNE_SPELLBOUND", "big_brain", "RUNE_GRAND_SEARING", "DEFENSIVE_0", "JADE_0", "UNIVERSAL_0", "blocksBroken", "RUNE_ZOMBIE_SLAYER",
        "spider_kills", "RUNE_SMITTEN", "AMETHYST_0", "sword_kills", "RUNE_ICE_SKATES", "zombie_kills", "mana_steal", "ender", "warrior",
        "blazing", "undead", "ignition", "midas_touch", "attack_speed", "arachno", "elite", "life_recovery", "combo", "JADE_1", "SAPPHIRE_1",
        "AQUAMARINE_0", "RUNE_SNOW", "deadeye", "thunder_charge", "MASTER_CRYPT_UNDEAD_MAGICBOYS_25", "MASTER_CRYPT_TANK_ZOMBIE_80",
        "MASTER_CRYPT_TANK_ZOMBIE_60", "MASTER_CRYPT_UNDEAD_HYPIXEL_25", "MASTER_CRYPT_UNDEAD_THEMGRF_25", "MINOS_CHAMPION_310",
        "MASTER_CRYPT_UNDEAD_CECER_25", "MASTER_CRYPT_TANK_ZOMBIE_70", "MASTER_CRYPT_UNDEAD__ONAH_25", "MASTER_CRYPT_UNDEAD_DUECES_25",
        "MASTER_CRYPT_UNDEAD_THORLON_25", "MASTER_CRYPT_UNDEAD_WILLIAMTIGER_25", "MASTER_CRYPT_UNDEAD_DONPIRESO_25", "MASTER_CRYPT_UNDEAD_FLAMEBOY101_25",
        "MASTER_CRYPT_UNDEAD_CHILYNN_25", "MASTER_CRYPT_UNDEAD_REZZUS_25", "MASTER_CRYPT_UNDEAD_EXTERNALIZABLE_25", "MASTER_CRYPT_UNDEAD_SYLENT_25",
        "MASTER_CRYPT_UNDEAD_BEMBO_25", "MASTER_CRYPT_UNDEAD_PLANCKE_25", "MASTER_CRYPT_UNDEAD_CODENAME_B_25", "MASTER_CRYPT_UNDEAD_RELENTER_25",
        "MASTER_CRYPT_UNDEAD_SFARNHAM_25", "MASTER_CRYPT_UNDEAD_JUDG3_25", "MASTER_CRYPT_UNDEAD_REVENGEEE_25", "MASTER_CRYPT_UNDEAD_SKYERZZ_25",
        "MASTER_CRYPT_UNDEAD_DCTR_25", "MASTER_CRYPT_UNDEAD_JAYAVARMEN_25", "MASTER_CRYPT_UNDEAD_LIKAOS_25", "MASTER_CRYPT_UNDEAD_NITROHOLIC__25",
        "MASTER_CRYPT_UNDEAD_AGENTK_25", "MASTER_CRYPT_UNDEAD_JAMIETHEGEEK_25", "MASTER_CRYPT_UNDEAD_LADYBLEU_25", "MASTER_CRYPT_UNDEAD_ORANGEMARSHALL_25",
        "MASTER_CRYPT_UNDEAD_MINIKLOON_25", "MASTER_CRYPT_UNDEAD_BLOOZING_25", "MASTER_CRYPT_UNDEAD_APUNCH_25", "MASTER_CRYPT_UNDEAD_CONNORLINFOOT_25",
        "MINOS_INQUISITOR_750", "collected_coins", "RUNE_SPARKLING", "RUNE_SNAKE", "RUNE_JERRY", "RUNE_LAVA", "yogsKilled", "blood_god_kills",
        "RUNE_SOULTWIST", "MINING_0", "RUNE_GEM", "RUNE_DRAGON", "gemstone_slots", "RUNE_ENDERSNAKE", "party_hat_color", "hunter", "RUNE_FIERY_BURST",
        "RUNE_WHITE_SPIRAL", "bow_kills", "RUNE_RAINBOW", "RUNE_ZAP", "RUNE_LIGHTNING", "RUNE_SMOKEY", "RUNE_GOLDEN_CARPET", "RUNE_MAGIC", "RUNE_HOT",
        "RUNE_COUTURE", "party_hat_emoji", "RUNE_SUPER_PUMPKIN", "RUNE_ICE", "RUNE_BLOOD_2", "RUNE_WAKE", "RUNE_FIRE_SPIRAL", "RUNE_BITE",
        "RUNE_SPIRIT", "RUNE_HEARTS", "RUNE_GOLDEN"];
}
