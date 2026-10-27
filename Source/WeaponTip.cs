using System.Collections;
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

        private static readonly StatDef? BurstShotCount = DefDatabase<StatDef>.GetNamedSilentFail("BurstShotCount");
        private static readonly StatDef? TicksBetweenBurstShots = DefDatabase<StatDef>.GetNamedSilentFail("TicksBetweenBurstShots");

        public static MethodInfo? Target()
        {
            System.Type? utility = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
            return utility == null ? null : AccessTools.Method(utility, "GetWeightAndBulkTip", new[] { typeof(ThingDef), typeof(int) });
        }

        public static void Postfix(ThingDef def, ref string __result)
        {
            if (!def.IsWeapon)
            {
                return;
            }
            string? dps = def.IsRangedWeapon ? RangedDps(def) : MeleeDps(def);
            if (dps != null)
            {
                __result += "\n" + dps;
            }
            bool oneHanded = def.weaponTags?.Contains(OneHandedTag) ?? false;
            __result += "\n" + (oneHanded ? "BetterCELoadouts_OneHanded" : "BetterCELoadouts_TwoHanded").Translate();
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

        // Damage of one burst divided by the time it takes to aim, fire and cool down, with the first ammo CE lists and every shot hitting.
        private static string? RangedDps(ThingDef def)
        {
            VerbProperties? verb = def.Verbs?.FirstOrDefault(v => v.isPrimary) ?? def.Verbs?.FirstOrDefault();
            if (verb == null)
            {
                return null;
            }
            ThingDef? projectile = FirstAmmoProjectile(def, out string? ammoLabel) ?? verb.defaultProjectile;
            if (projectile?.projectile == null)
            {
                return null;
            }

            int pellets = AccessTools.Field(projectile.projectile.GetType(), "pelletCount")?.GetValue(projectile.projectile) is int p && p > 0 ? p : 1;
            float damage = projectile.projectile.GetDamageAmount(def, null) * pellets;
            float burst = BurstShotCount != null ? def.GetStatValueAbstract(BurstShotCount) : verb.burstShotCount;
            float ticksBetween = TicksBetweenBurstShots != null ? def.GetStatValueAbstract(TicksBetweenBurstShots) : verb.ticksBetweenBurstShots;
            float seconds = verb.warmupTime + (burst - 1f) * ticksBetween / GenTicks.TicksPerRealSecond + def.GetStatValueAbstract(StatDefOf.RangedWeapon_Cooldown);
            if (damage <= 0f || burst < 1f || seconds <= 0f)
            {
                return null;
            }

            string value = (damage * burst / seconds).ToStringByStyle(ToStringStyle.FloatTwo);
            return ammoLabel == null
                ? "BetterCELoadouts_RangedDps".Translate(value)
                : "BetterCELoadouts_RangedDpsAmmo".Translate(value, ammoLabel);
        }

        // The projectile of the first ammo in the weapon's CE ammo set, if it has one.
        private static ThingDef? FirstAmmoProjectile(ThingDef def, out string? ammoLabel)
        {
            ammoLabel = null;
            CompProperties? ammoUser = def.comps?.FirstOrDefault(c => c.GetType().Name == "CompProperties_AmmoUser");
            object? ammoSet = ammoUser == null ? null : AccessTools.Field(ammoUser.GetType(), "ammoSet")?.GetValue(ammoUser);
            if (ammoSet == null || AccessTools.Field(ammoSet.GetType(), "ammoTypes")?.GetValue(ammoSet) is not IList ammoTypes || ammoTypes.Count == 0)
            {
                return null;
            }
            object link = ammoTypes[0]!;
            ammoLabel = (AccessTools.Field(link.GetType(), "ammo")?.GetValue(link) as ThingDef)?.label;
            return AccessTools.Field(link.GetType(), "projectile")?.GetValue(link) as ThingDef;
        }
    }
}
