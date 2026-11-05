using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterCELoadouts
{
    // Damage per second and one- or two-handedness added to CE's mass and bulk tooltip for weapons.
    internal static class WeaponTip
    {
        // CE's tag for weapons usable with a shield; every other weapon is two-handed.
        private const string OneHandedTag = "CE_OneHandedWeapon";

        // CE builds every visible row's tooltip on every pass, so the lines are worked out once per item.
        private static readonly Dictionary<ThingDef, string> Lines = new();

        public static MethodInfo? Target()
        {
            Type? utility = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
            return utility == null ? null : AccessTools.Method(utility, "GetWeightAndBulkTip", new[] { typeof(ThingDef), typeof(int) });
        }

        public static void Postfix(ThingDef def, ref string __result)
        {
            if (!def.IsWeapon)
            {
                return;
            }
            if (!Lines.TryGetValue(def, out string? lines))
            {
                Lines[def] = lines = BuildLines(def);
            }
            __result += lines;
        }

        private static string BuildLines(ThingDef def)
        {
            string? dps = def.IsRangedWeapon ? RangedDps(def) : MeleeDps(def);
            bool oneHanded = def.weaponTags?.Contains(OneHandedTag) ?? false;
            return (dps == null ? "" : "\n" + dps) + "\n" + (oneHanded ? "BetterCELoadouts_OneHanded" : "BetterCELoadouts_TwoHanded").Translate();
        }

        // CE's own average melee DPS stat, for the default material when the weapon is made from stuff.
        private static string? MeleeDps(ThingDef def)
        {
            // CE's stat worker logs an error for tools it doesn't support.
            if (def.tools.NullOrEmpty() || def.tools.Any(t => t.GetType().Name != "ToolCE"))
            {
                return null;
            }
            ThingDef? stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            float dps = def.GetStatValueAbstract(StatDefOf.MeleeWeapon_AverageDPS, stuff);
            string value = StatDefOf.MeleeWeapon_AverageDPS.ValueToString(dps);
            return stuff == null
                ? "CE_DPS".Translate() + ": " + value
                : "BetterCELoadouts_MeleeDpsStuff".Translate(value, stuff.label);
        }

        // Damage of one burst divided by the time it takes to aim, fire and cool down, with the first ammo CE lists, every shot hitting and no reloads.
        private static string? RangedDps(ThingDef def)
        {
            VerbProperties? verb = def.Verbs?.FirstOrDefault(v => v.isPrimary) ?? def.Verbs?.FirstOrDefault();
            if (verb == null)
            {
                return null;
            }
            (ThingDef? ammo, ThingDef? firstProjectile) = CeAmmo.Links(def, false).FirstOrDefault();
            ThingDef? projectile = firstProjectile ?? verb.defaultProjectile;
            if (projectile?.projectile == null)
            {
                return null;
            }

            // Looked up per weapon, since a language change reloads every Def.
            StatDef? burstShotCount = DefDatabase<StatDef>.GetNamedSilentFail("BurstShotCount");
            StatDef? ticksBetweenBurstShots = DefDatabase<StatDef>.GetNamedSilentFail("TicksBetweenBurstShots");
            float damage = projectile.projectile.GetDamageAmount(def, null) * CeAmmo.Pellets(projectile.projectile);
            float burst = burstShotCount != null ? def.GetStatValueAbstract(burstShotCount) : verb.burstShotCount;
            float ticksBetween = ticksBetweenBurstShots != null ? def.GetStatValueAbstract(ticksBetweenBurstShots) : verb.ticksBetweenBurstShots;
            float seconds = verb.warmupTime + (burst - 1f) * ticksBetween / GenTicks.TicksPerRealSecond + def.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
            if (damage <= 0f || burst < 1f || seconds <= 0f)
            {
                return null;
            }

            string value = (damage * burst / seconds).ToStringByStyle(ToStringStyle.FloatTwo);
            return firstProjectile == null || ammo == null
                ? "BetterCELoadouts_RangedDps".Translate(value)
                : "BetterCELoadouts_RangedDpsAmmo".Translate(value, ammo.label);
        }
    }
}
