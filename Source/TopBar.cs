using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    // Drops CE's New and Delete buttons from the top bar (the loadout list has its own) and closes the gaps they leave.
    internal static class TopBar
    {
        // CE's top bar: seven buttons 6px apart, New second and Delete fourth.
        private const float Margin = 6f;
        private const float BarHeight = 30f;
        private const int NewSlot = 1;
        private const int DeleteSlot = 3;

        private static readonly MethodInfo ButtonText = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) });

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo button = AccessTools.Method(typeof(TopBar), nameof(Button));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(ButtonText))
                {
                    instruction.operand = button;
                }
                yield return instruction;
            }
        }

        private static bool Button(Rect rect, string label, bool drawBackground, bool doMouseoverSound, bool active, TextAnchor? overrideTextAnchor)
        {
            if (rect.y == 0f && rect.height == BarHeight)
            {
                float step = rect.width + Margin;
                int slot = Mathf.RoundToInt(rect.x / step);
                if (slot == NewSlot || slot == DeleteSlot)
                {
                    return false;
                }
                rect.x = (slot - (slot > NewSlot ? 1 : 0) - (slot > DeleteSlot ? 1 : 0)) * step;
            }
            return Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
        }
    }
}
