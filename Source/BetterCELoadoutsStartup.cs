using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterCELoadouts
{
    // Registers the patches, skipping any whose CE code wasn't found so the rest still work.
    [StaticConstructorOnStartup]
    public static class BetterCELoadoutsStartup
    {
        static BetterCELoadoutsStartup()
        {
            if (!CeLoadouts.Ready)
            {
                Log.Error("[Better CE Loadouts] Combat Extended's loadout dialog was not found or has changed; the loadout window is left unchanged.");
                return;
            }

            Harmony harmony = new("cruesoe.betterceloadouts");
            Type dialog = CeLoadouts.DialogType!;
            harmony.Patch(AccessTools.PropertyGetter(dialog, "InitialSize"), postfix: Method(typeof(Patch_Dialog_ManageLoadouts), nameof(Patch_Dialog_ManageLoadouts.InitialSizePostfix)));
            harmony.Patch(AccessTools.Method(dialog, "DoWindowContents"),
                prefix: Method(typeof(Patch_Dialog_ManageLoadouts), nameof(Patch_Dialog_ManageLoadouts.DoWindowContentsPrefix)),
                finalizer: Method(typeof(Patch_Dialog_ManageLoadouts), nameof(Patch_Dialog_ManageLoadouts.DoWindowContentsFinalizer)));

            if (CeLoadouts.CanManage)
            {
                harmony.Patch(AccessTools.Method(dialog, "DoWindowContents"), transpiler: Method(typeof(TopBar), nameof(TopBar.Transpiler)));
            }
            else
            {
                Log.Warning("[Better CE Loadouts] Combat Extended's new and delete loadout code has changed; its own buttons are kept.");
            }

            if (WeaponTip.Target() is MethodInfo tip)
            {
                harmony.Patch(tip, postfix: Method(typeof(WeaponTip), nameof(WeaponTip.Postfix)));
            }
            else
            {
                Log.Warning("[Better CE Loadouts] Combat Extended's item tooltip has changed; weapon tooltips are left unchanged.");
            }

            if (CeLoadouts.CanSort)
            {
                harmony.Patch(AccessTools.Method(dialog, "SetSource"), postfix: Method(typeof(Patch_Dialog_ManageLoadouts), nameof(Patch_Dialog_ManageLoadouts.SetSourcePostfix)));
            }
            else
            {
                Log.Warning("[Better CE Loadouts] Combat Extended's item list has changed; items stay in alphabetical order.");
            }
        }

        private static HarmonyMethod Method(Type type, string name) => new(AccessTools.Method(type, name));
    }
}
