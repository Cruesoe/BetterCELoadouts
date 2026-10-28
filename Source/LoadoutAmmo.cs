using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace BetterCELoadouts
{
    // The ammo that fits the weapons in a loadout, read from each weapon's CE ammo set (and its under-barrel launcher's, if it has one).
    internal static class LoadoutAmmo
    {
        private static readonly ConditionalWeakTable<object, StrongBox<int>> LastSeen = new();

        public static HashSet<ThingDef> For(object? loadout)
        {
            HashSet<ThingDef> ammo = new();
            if (loadout != null)
            {
                foreach (ThingDef weapon in CeLoadouts.WeaponsIn(loadout))
                {
                    AddAmmoOf(weapon, ammo);
                }
            }
            return ammo;
        }

        // Re-sorts the dialog's item list when the selected loadout, or the weapons in it, change.
        public static void RefreshIfChanged(object dialog)
        {
            object? loadout = CeLoadouts.Current(dialog);
            int signature = loadout == null ? 0 : RuntimeHelpers.GetHashCode(loadout);
            if (loadout != null)
            {
                foreach (ThingDef weapon in CeLoadouts.WeaponsIn(loadout))
                {
                    signature = signature * 31 + weapon.shortHash;
                }
            }

            StrongBox<int> seen = LastSeen.GetValue(dialog, _ => new StrongBox<int>(signature));
            if (seen.Value != signature)
            {
                seen.Value = signature;
                CeLoadouts.RefreshSource(dialog);
            }
        }

        private static void AddAmmoOf(ThingDef weapon, HashSet<ThingDef> ammo)
        {
            foreach (CompProperties comp in weapon.comps ?? Enumerable.Empty<CompProperties>())
            {
                switch (comp.GetType().Name)
                {
                    case "CompProperties_AmmoUser":
                        AddAmmoSet(AccessTools.Field(comp.GetType(), "ammoSet")?.GetValue(comp), ammo);
                        break;
                    case "CompProperties_UnderBarrel":
                        object? launcher = AccessTools.Field(comp.GetType(), "propsUnderBarrel")?.GetValue(comp);
                        if (launcher != null)
                        {
                            AddAmmoSet(AccessTools.Field(launcher.GetType(), "ammoSet")?.GetValue(launcher), ammo);
                        }
                        break;
                }
            }
        }

        private static void AddAmmoSet(object? ammoSet, HashSet<ThingDef> ammo)
        {
            if (ammoSet != null && AccessTools.Field(ammoSet.GetType(), "ammoTypes")?.GetValue(ammoSet) is IEnumerable links)
            {
                foreach (object link in links)
                {
                    if (AccessTools.Field(link.GetType(), "ammo")?.GetValue(link) is ThingDef def)
                    {
                        ammo.Add(def);
                    }
                }
            }
        }
    }
}
