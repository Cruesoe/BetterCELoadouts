using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace BetterCELoadouts
{
    // The ammo that fits the weapons in a loadout, from each weapon's CE ammo set (and its under-barrel launcher's, if it has one).
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
                    foreach ((ThingDef? def, _) in CeAmmo.Links(weapon, true))
                    {
                        if (def != null)
                        {
                            ammo.Add(def);
                        }
                    }
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
    }
}
