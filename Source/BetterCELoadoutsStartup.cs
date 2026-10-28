using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    [StaticConstructorOnStartup]
    public static class BetterCELoadoutsStartup
    {
        private static readonly ConditionalWeakTable<object, HashSet<ThingDef>> MapDefs = new();

        static BetterCELoadoutsStartup()
        {
            if (!CeLoadouts.Ready)
            {
                Log.Error("[Better CE Loadouts] Combat Extended's loadout dialog was not found or has changed; the loadout window is left unchanged.");
                return;
            }

            Harmony harmony = new("cruesoe.betterceloadouts");
            Type dialog = CeLoadouts.DialogType!;
            harmony.Patch(AccessTools.PropertyGetter(dialog, "InitialSize"), postfix: new HarmonyMethod(typeof(BetterCELoadoutsStartup), nameof(InitialSizePostfix)));
            harmony.Patch(AccessTools.Method(dialog, "DoWindowContents"),
                prefix: new HarmonyMethod(typeof(BetterCELoadoutsStartup), nameof(DoWindowContentsPrefix)),
                finalizer: new HarmonyMethod(typeof(BetterCELoadoutsStartup), nameof(DoWindowContentsFinalizer)));

            if (CeLoadouts.CanManage)
            {
                harmony.Patch(AccessTools.Method(dialog, "DoWindowContents"), transpiler: new HarmonyMethod(typeof(TopBar), nameof(TopBar.Transpiler)));
            }
            else
            {
                Log.Warning("[Better CE Loadouts] Combat Extended's new and delete loadout code has changed; its own buttons are kept.");
            }

            if (WeaponTip.Target() is MethodInfo tip)
            {
                harmony.Patch(tip, postfix: new HarmonyMethod(typeof(WeaponTip), nameof(WeaponTip.Postfix)));
            }
            else
            {
                Log.Warning("[Better CE Loadouts] Combat Extended's item tooltip has changed; weapon tooltips are left unchanged.");
            }

            if (CeLoadouts.CanSort)
            {
                harmony.Patch(AccessTools.Method(dialog, "SetSource"), postfix: new HarmonyMethod(typeof(BetterCELoadoutsStartup), nameof(SetSourcePostfix)));
            }
            else
            {
                Log.Warning("[Better CE Loadouts] Combat Extended's item list has changed; items stay in alphabetical order.");
            }
        }

        // Widens the window to fit the loadout tree.
        private static void InitialSizePostfix(ref Vector2 __result)
        {
            __result.x = Mathf.Min(__result.x + LoadoutNavigator.Width + LoadoutNavigator.Gap, UI.screenWidth);
        }

        // Draws the loadout tree, then hands CE the rest of the window.
        private static void DoWindowContentsPrefix(Window __instance, ref Rect canvas, out bool __state)
        {
            __state = false;
            LoadoutNavigator.Draw(new Rect(canvas.x, canvas.y, LoadoutNavigator.Width, canvas.height), __instance);
            float offset = LoadoutNavigator.Width + LoadoutNavigator.Gap;
            Rect content = new(canvas.x + offset, canvas.y, canvas.width - offset, canvas.height);
            Widgets.BeginGroup(content);
            __state = true;
            canvas = content.AtZero();
        }

        private static Exception? DoWindowContentsFinalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                Widgets.EndGroup();
            }
            TopBar.LoadRequested = false;
            TopBar.SaveRequested = false;
            return __exception;
        }

        // Runs after CE rebuilds its item list (on opening, switching category and filtering).
        private static void SetSourcePostfix(object __instance)
        {
            if (Find.CurrentMap is not Map map)
            {
                return;
            }
            HashSet<ThingDef> defs = MapDefs.GetValue(__instance, _ => ThingDefsOnMap(map));
            CeLoadouts.SortSourceByStock(__instance, defs);
        }

        // The same stock check CE uses to grey out items: things on the map outside fog, minified buildings excluded.
        private static HashSet<ThingDef> ThingDefsOnMap(Map map)
        {
            return new HashSet<ThingDef>(map.listerThings.AllThings
                .Where(t => !t.PositionHeld.Fogged(t.MapHeld) && !t.GetInnerIfMinified().def.Minifiable)
                .Select(t => t.def));
        }
    }
}
