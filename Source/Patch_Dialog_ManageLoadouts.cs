using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    internal static class Patch_Dialog_ManageLoadouts
    {
        // CE's 30px top bar plus two 6px margins.
        private const float TopBarSpace = 42f;

        private static readonly ConditionalWeakTable<object, HashSet<ThingDef>> MapDefs = new();

        public static void InitialSizePostfix(ref Vector2 __result)
        {
            __result.x = Mathf.Min(__result.x + LoadoutNavigator.Width + LoadoutNavigator.Gap, UI.screenWidth);
        }

        // While a colonist is chosen to compare with, CE's Mass and Bulk bars use theirs until the finalizer restores CE's values.
        public static void DoWindowContentsPrefix(Window __instance, ref Rect canvas, out bool __state)
        {
            __state = false;
            __instance.closeOnAccept = false;
            if (CeLoadouts.CanSort)
            {
                LoadoutAmmo.RefreshIfChanged(__instance);
            }
            LoadoutNavigator.Draw(new Rect(canvas.x, canvas.y, LoadoutNavigator.Width, canvas.height), __instance);
            if (PawnComparison.Available)
            {
                PawnComparison.Apply();
            }

            float offset = LoadoutNavigator.Width + LoadoutNavigator.Gap;
            float lift = CeLoadouts.CanManage ? TopBarSpace : 0f;
            Rect content = new(canvas.x + offset, canvas.y - lift, canvas.width - offset, canvas.height + lift);
            Widgets.BeginGroup(content);
            __state = true;
            canvas = content.AtZero();
        }

        public static Exception? DoWindowContentsFinalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                Widgets.EndGroup();
            }
            PawnComparison.Restore();
            TopBar.CopyRequested = false;
            TopBar.LoadRequested = false;
            TopBar.SaveRequested = false;
            return __exception;
        }

        // Runs after CE rebuilds its item list (on opening, switching category and filtering).
        public static void SetSourcePostfix(object __instance)
        {
            if (Find.CurrentMap is not Map map)
            {
                return;
            }
            HashSet<ThingDef> defs = MapDefs.GetValue(__instance, _ => ThingDefsOnMap(map));
            CeLoadouts.SortSourceByStock(__instance, defs, LoadoutAmmo.For(CeLoadouts.Current(__instance)));
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
