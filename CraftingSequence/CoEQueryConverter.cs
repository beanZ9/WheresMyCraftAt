using ExileCore;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace WheresMyCraftAt.CraftingSequence;

public static class CoEQueryConverter
{
    public static string GenerateLinqQuery(string coeId, string cleanModName, long? threshold, JObject coeData)
    {
        DebugWindow.LogMsg($"[GenerateLinqQuery] Generating query for coeId {coeId} and modName {cleanModName}");
        var lowerId = coeId.ToLowerInvariant();
        
        if (lowerId == "open_prefix") return threshold > 1 ? $"ModsInfo.OpenPrefixCount >= {threshold}" : "ModsInfo.HasOpenPrefix";
        if (lowerId == "open_suffix") return threshold > 1 ? $"ModsInfo.OpenSuffixCount >= {threshold}" : "ModsInfo.HasOpenSuffix";
        if (lowerId == "open_affix") return threshold > 1 ? $"(ModsInfo.OpenPrefixCount + ModsInfo.OpenSuffixCount) >= {threshold}" : "ModsInfo.HasOpenPrefix || ModsInfo.HasOpenSuffix";

        if (lowerId == "count_prefix") return $"ModsInfo.Prefixes.Count >= {threshold ?? 1}";
        if (lowerId == "count_suffix") return $"ModsInfo.Suffixes.Count >= {threshold ?? 1}";
        if (lowerId == "count_affix") return $"(ModsInfo.Prefixes.Count + ModsInfo.Suffixes.Count) >= {threshold ?? 1}";

        if (lowerId == "count_iprefix") return $"ModsInfo.Prefixes.Count(e => e.ModRecord.InfluenceType != \"None\") >= {threshold ?? 1}";
        if (lowerId == "count_isuffix") return $"ModsInfo.Suffixes.Count(e => e.ModRecord.InfluenceType != \"None\") >= {threshold ?? 1}";
        if (lowerId == "count_iaffix") return $"(ModsInfo.Prefixes.Count(e => e.ModRecord.InfluenceType != \"None\") + ModsInfo.Suffixes.Count(e => e.ModRecord.InfluenceType != \"None\")) >= {threshold ?? 1}";

        if (lowerId == "veiled_prefix") return $"ModsInfo.Prefixes.Count(e => e.DisplayName.Contains(\"Veil\")) >= {threshold ?? 1}";
        if (lowerId == "veiled_suffix") return $"ModsInfo.Suffixes.Count(e => e.DisplayName.Contains(\"Veil\")) >= {threshold ?? 1}";
        
        if (lowerId == "pseudo_quantity") return $"ItemStats[GameStat.MapItemDropQuantityPct] >= {threshold ?? 1}";
        if (lowerId == "pseudo_rarity") return $"ItemStats[GameStat.MapItemDropRarityPct] >= {threshold ?? 1}";
        if (lowerId == "pseudo_packsize") return $"ItemStats[GameStat.MapPackSizePct] >= {threshold ?? 1}";

        long? globalMinRoll = null;
        long? globalMaxRoll = null;

        if (coeData != null && threshold.HasValue && threshold.Value > 0)
        {
            var tiersToken = coeData["tiers"]?[coeId];
            if (tiersToken != null && tiersToken.HasValues)
            {
                var firstItemGroup = tiersToken.First as JProperty;
                if (firstItemGroup != null && firstItemGroup.Value is JArray tiersArray)
                {
                    int totalTiers = tiersArray.Count;
                    int coeTreshold = (int)threshold.Value;
                    int targetTierIndex = totalTiers - coeTreshold;
                    
                    if (targetTierIndex >= 0 && targetTierIndex < totalTiers)
                    {
                        for (int i = targetTierIndex; i < totalTiers; i++)
                        {
                            var targetTierStr = tiersArray[i]?["nvalues"]?.ToString();
                            if (!string.IsNullOrEmpty(targetTierStr) && targetTierStr.Length > 4)
                            {
                                try {
                                    var cleanStr = targetTierStr.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("[", "").Replace("]", "");
                                    var split = cleanStr.Split(',');
                                    long curMin = long.Parse(split[0]);
                                    long curMax = split.Length > 1 ? long.Parse(split[1]) : curMin;

                                    if (globalMinRoll == null || curMin < globalMinRoll.Value) globalMinRoll = curMin;
                                    if (globalMaxRoll == null || curMax > globalMaxRoll.Value) globalMaxRoll = curMax;
                                    if (globalMinRoll == null || curMax < globalMinRoll.Value) globalMinRoll = curMax;
                                    if (globalMaxRoll == null || curMin > globalMaxRoll.Value) globalMaxRoll = curMin;
                                } catch (Exception ex) {
                                    DebugWindow.LogError($"[GenerateLinqQuery] Failed to parse nvalues '{targetTierStr}': {ex.Message}");
                                }
                            }
                        }
                    }
                }
                else { DebugWindow.LogError($"[GenerateLinqQuery] Tiers format invalid for {coeId}"); }
            }
        }

        string translationCheck;
        var parts = cleanModName.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => p.Trim())
                                .Where(p => !string.IsNullOrEmpty(p))
                                .ToList();

        if (parts.Count > 0)
        {
            var containsChecks = string.Join(" && ", parts.Select(p => $"x.Translation.Contains(\"{p}\", StringComparison.OrdinalIgnoreCase)"));
            translationCheck = $"!string.IsNullOrEmpty(x.Translation) && {containsChecks}";
        }
        else
        {
            translationCheck = $"!string.IsNullOrEmpty(x.Translation) && x.Translation.Contains(\"{cleanModName}\", StringComparison.OrdinalIgnoreCase)";
        }

        if (globalMinRoll.HasValue && globalMaxRoll.HasValue)
        {
            var minLimit = Math.Min(globalMinRoll.Value, globalMaxRoll.Value);
            var maxLimit = Math.Max(globalMinRoll.Value, globalMaxRoll.Value);

            var tierQuery = $"ModsInfo.ExplicitMods.Any(x => {translationCheck} && x.Values.Any(v => (v >= {minLimit} && v <= {maxLimit}) || (v <= -{minLimit} && v >= -{maxLimit})))";
            DebugWindow.LogMsg($"[GenerateLinqQuery] Success tier query: {tierQuery}");
            return tierQuery;
        }

        var fallbackQuery = $"ModsInfo.ExplicitMods.Any(x => {translationCheck})";
        DebugWindow.LogMsg($"[GenerateLinqQuery] Success fallback query: {fallbackQuery}");
        return fallbackQuery;
    }
}
