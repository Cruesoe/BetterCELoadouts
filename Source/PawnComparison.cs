using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    // CE's Mass and Bulk bars read its median capacity fields, so the chosen colonist's are swapped in while CE draws, then put back.
    internal static class PawnComparison
    {
        private static readonly Type? UtilityType = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
        private static readonly FieldInfo? MedianWeightField = UtilityType == null ? null : AccessTools.Field(UtilityType, "medianWeightCapacity");
        private static readonly FieldInfo? MedianBulkField = UtilityType == null ? null : AccessTools.Field(UtilityType, "medianBulkCapacity");
        private static readonly PropertyInfo? WeightProperty = CeLoadouts.LoadoutType == null ? null : AccessTools.Property(CeLoadouts.LoadoutType, "Weight");
        private static readonly PropertyInfo? BulkProperty = CeLoadouts.LoadoutType == null ? null : AccessTools.Property(CeLoadouts.LoadoutType, "Bulk");

        public static bool Available => MedianWeightField != null && MedianBulkField != null && WeightProperty != null && BulkProperty != null;

        // Window state, reset when a new window opens; CE pauses the game, so capacities are worked out once per choice.
        private static System.WeakReference<object>? lastDialog;
        private static StatDef? carryWeight;
        private static StatDef? carryBulk;
        private static StatDef? wornBulk;
        private static Pawn? pawn;
        private static float weightCapacity;
        private static float bulkCapacity;
        private static string buttonLabel = "";
        private static string compareTip = "";
        private static string selectTip = "";
        private static string weightLine = "";
        private static string bulkLine = "";

        // CE's median capacities while the colonist's are swapped in, and whether they are.
        private static float savedWeight;
        private static float savedBulk;
        private static bool applied;

        public static MethodInfo? WeightTipTarget() => TipTarget("GetWeightTip");

        public static MethodInfo? BulkTipTarget() => TipTarget("GetBulkTip");

        private static MethodInfo? TipTarget(string name)
        {
            return UtilityType == null || CeLoadouts.LoadoutType == null ? null : AccessTools.Method(UtilityType, name, new[] { CeLoadouts.LoadoutType });
        }

        public static void DrawButton(Rect rect, object dialog, object? loadout)
        {
            if (lastDialog == null || !lastDialog.TryGetTarget(out object? last) || last != dialog)
            {
                lastDialog = new System.WeakReference<object>(dialog);
                carryWeight = DefDatabase<StatDef>.GetNamedSilentFail("CarryWeight");
                carryBulk = DefDatabase<StatDef>.GetNamedSilentFail("CarryBulk");
                wornBulk = DefDatabase<StatDef>.GetNamedSilentFail("WornBulk");
                compareTip = "BetterCELoadouts_CompareTip".Translate();
                selectTip = "BetterCELoadouts_SelectToCompare".Translate();
                Choose(null);
            }
            if (pawn != null && (pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer))
            {
                Choose(null);
            }

            bool active = loadout != null;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (Widgets.ButtonText(rect, buttonLabel, true, true, active) && loadout != null)
            {
                Find.WindowStack.Add(new FloatMenu(Options(loadout)));
            }
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, active ? compareTip : selectTip);
        }

        public static void Apply()
        {
            if (pawn == null || applied)
            {
                return;
            }
            savedWeight = (float)MedianWeightField!.GetValue(null)!;
            savedBulk = (float)MedianBulkField!.GetValue(null)!;
            MedianWeightField.SetValue(null, weightCapacity);
            MedianBulkField.SetValue(null, bulkCapacity);
            applied = true;
        }

        public static void Restore()
        {
            if (!applied)
            {
                return;
            }
            MedianWeightField!.SetValue(null, savedWeight);
            MedianBulkField!.SetValue(null, savedBulk);
            applied = false;
        }

        public static void WeightTipPostfix(ref string __result)
        {
            if (applied)
            {
                __result += weightLine;
            }
        }

        public static void BulkTipPostfix(ref string __result)
        {
            if (applied)
            {
                __result += bulkLine;
            }
        }

        private static List<FloatMenuOption> Options(object loadout)
        {
            float weight = (float)WeightProperty!.GetValue(loadout)!;
            float bulk = (float)BulkProperty!.GetValue(loadout)!;
            List<FloatMenuOption> options = new() { new FloatMenuOption("BetterCELoadouts_MedianColonist".Translate().CapitalizeFirst(), () => Choose(null)) };

            List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonists.ToList();
            foreach (Pawn colonist in colonists.OrderBy(p => CeLoadouts.LoadoutOf(p) == loadout ? 0 : 1).ThenBy(p => p.LabelShortCap))
            {
                (float freeWeight, float freeBulk, _, _) = Capacities(colonist);
                string fit = weight > freeWeight && bulk > freeBulk ? "BetterCELoadouts_TooHeavyAndBulky".Translate()
                    : weight > freeWeight ? "BetterCELoadouts_TooHeavy".Translate()
                    : bulk > freeBulk ? "BetterCELoadouts_TooBulky".Translate()
                    : "BetterCELoadouts_Fits".Translate();
                Pawn chosen = colonist;
                options.Add(new FloatMenuOption("BetterCELoadouts_CompareOption".Translate(colonist.LabelShortCap, fit), () => Choose(chosen)));
            }
            return options;
        }

        private static void Choose(Pawn? colonist)
        {
            pawn = colonist;
            if (colonist == null)
            {
                buttonLabel = "BetterCELoadouts_CompareWith".Translate("BetterCELoadouts_MedianColonist".Translate());
                weightLine = bulkLine = "";
                return;
            }

            (weightCapacity, bulkCapacity, float apparelWeight, float apparelBulk) = Capacities(colonist);
            buttonLabel = "BetterCELoadouts_CompareWith".Translate(colonist.LabelShortCap);
            weightLine = "\n\n" + "BetterCELoadouts_ComparedWeight".Translate(colonist.LabelShortCap,
                Format(carryWeight, weightCapacity + apparelWeight), Format(carryWeight, apparelWeight));
            bulkLine = "\n\n" + "BetterCELoadouts_ComparedBulk".Translate(colonist.LabelShortCap,
                Format(carryBulk, bulkCapacity + apparelBulk), Format(carryBulk, apparelBulk));
        }

        // Capacity left after worn apparel, counted as CE's inventory does (apparel mass and worn bulk).
        private static (float Weight, float Bulk, float ApparelWeight, float ApparelBulk) Capacities(Pawn colonist)
        {
            float apparelWeight = 0f;
            float apparelBulk = 0f;
            if (colonist.apparel != null)
            {
                foreach (Apparel apparel in colonist.apparel.WornApparel)
                {
                    apparelWeight += apparel.GetStatValue(StatDefOf.Mass);
                    apparelBulk += wornBulk == null ? 0f : apparel.GetStatValue(wornBulk);
                }
            }
            float weight = carryWeight == null ? 0f : colonist.GetStatValue(carryWeight);
            float bulk = carryBulk == null ? 0f : colonist.GetStatValue(carryBulk);
            return (Mathf.Max(0f, weight - apparelWeight), Mathf.Max(0f, bulk - apparelBulk), apparelWeight, apparelBulk);
        }

        private static string Format(StatDef? stat, float value)
        {
            return stat == null ? value.ToString("0.#") : stat.ValueToString(value, stat.toStringNumberSense);
        }
    }
}
