using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterCELoadouts
{
    // Reflection access to Combat Extended's ammo sets, resolved once.
    internal static class CeAmmo
    {
        private static readonly Type? AmmoUserType = AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser");
        private static readonly Type? UnderBarrelType = AccessTools.TypeByName("CombatExtended.CompProperties_UnderBarrel");
        private static readonly Type? ProjectileType = AccessTools.TypeByName("CombatExtended.ProjectilePropertiesCE");
        private static readonly FieldInfo? AmmoSetField = Field(AmmoUserType, "ammoSet");
        private static readonly FieldInfo? LauncherField = Field(UnderBarrelType, "propsUnderBarrel");
        private static readonly FieldInfo? AmmoTypesField = Field(AccessTools.TypeByName("CombatExtended.AmmoSetDef"), "ammoTypes");
        private static readonly FieldInfo? AmmoField = Field(AccessTools.TypeByName("CombatExtended.AmmoLink"), "ammo");
        private static readonly FieldInfo? ProjectileField = Field(AccessTools.TypeByName("CombatExtended.AmmoLink"), "projectile");
        private static readonly FieldInfo? PelletField = Field(ProjectileType, "pelletCount");

        private static FieldInfo? Field(Type? type, string name) => type == null ? null : AccessTools.Field(type, name);

        // The ammo and its projectile for each entry in a weapon's ammo set, in CE's order; with launchers, its under-barrel launcher's too.
        public static IEnumerable<(ThingDef? Ammo, ThingDef? Projectile)> Links(ThingDef weapon, bool launchers)
        {
            foreach (CompProperties comp in weapon.comps ?? Enumerable.Empty<CompProperties>())
            {
                if (AmmoUserType?.IsInstanceOfType(comp) == true)
                {
                    foreach (var link in LinksOf(comp))
                    {
                        yield return link;
                    }
                }
                else if (launchers && UnderBarrelType?.IsInstanceOfType(comp) == true && LauncherField?.GetValue(comp) is object launcher)
                {
                    foreach (var link in LinksOf(launcher))
                    {
                        yield return link;
                    }
                }
            }
        }

        // Shotgun-style projectiles fire several pellets per shot.
        public static int Pellets(ProjectileProperties projectile)
        {
            return ProjectileType?.IsInstanceOfType(projectile) == true && PelletField?.GetValue(projectile) is int pellets && pellets > 0 ? pellets : 1;
        }

        private static IEnumerable<(ThingDef? Ammo, ThingDef? Projectile)> LinksOf(object ammoUser)
        {
            if (AmmoSetField?.GetValue(ammoUser) is object ammoSet && AmmoTypesField?.GetValue(ammoSet) is IEnumerable links)
            {
                foreach (object link in links)
                {
                    yield return (AmmoField?.GetValue(link) as ThingDef, ProjectileField?.GetValue(link) as ThingDef);
                }
            }
        }
    }
}
