using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace BetterCELoadouts
{
    // Ammo for a loadout's weapons, including under-barrel launchers.
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

        public static void RefreshIfChanged(object dialog)
        {
            object? loadout = CeLoadouts.Current(dialog);
            int signature = loadout == null ? 0 : CeLoadouts.WeaponSignature(loadout);

            StrongBox<int> seen = LastSeen.GetValue(dialog, _ => new StrongBox<int>(signature));
            if (seen.Value != signature)
            {
                seen.Value = signature;
                CeLoadouts.RefreshSource(dialog);
            }
        }
    }
}
