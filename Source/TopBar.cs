using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    // Drops CE's Select, New and Delete buttons from the top bar (the loadout list does those) and closes up the rest.
    internal static class TopBar
    {
        // CE's top bar: seven buttons 6px apart, in the order Select, New, Copy, Delete, Load, Save, Parent.
        private const float Margin = 6f;
        private const float BarHeight = 30f;
        private const int LoadSlot = 4;

        // Slots still drawn: Copy, Load, Save and Parent with a loadout selected; CE shows only Load without one.
        private static readonly List<int> WithLoadout = new() { 2, LoadSlot, 5, 6 };
        private static readonly List<int> WithoutLoadout = new() { LoadSlot };

        private static readonly MethodInfo ButtonText = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) });

        // Set before CE draws each frame.
        public static bool HasLoadout;

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
                int position = (HasLoadout ? WithLoadout : WithoutLoadout).IndexOf(Mathf.RoundToInt(rect.x / step));
                if (position < 0)
                {
                    return false;
                }
                rect.x = position * step;
            }
            return Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
        }
    }
}
