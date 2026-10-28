using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    // Takes CE's Select, New, Delete, Load, Save and Parent buttons out of the top bar (the loadout list does their jobs now) and closes up the rest.
    // Load and Save keep running CE's own code: a click in the list sets a flag that CE's hidden button then reports as pressed.
    internal static class TopBar
    {
        // CE's top bar: seven buttons 6px apart, in the order Select, New, Copy, Delete, Load, Save, Parent.
        private const float Margin = 6f;
        private const float BarHeight = 30f;
        private const int CopySlot = 2;
        private const int LoadSlot = 4;
        private const int SaveSlot = 5;

        // Slots still drawn: only Copy, which CE shows with a loadout selected. Parent (slot 6) is set by dragging in the list.
        private static readonly List<int> Visible = new() { CopySlot };

        private static readonly MethodInfo ButtonText = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) });

        // Set by the loadout list during a frame; cleared when CE finishes drawing it.
        public static bool LoadRequested;
        public static bool SaveRequested;

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
                if (slot == LoadSlot)
                {
                    return LoadRequested;
                }
                if (slot == SaveSlot)
                {
                    return SaveRequested;
                }
                int position = Visible.IndexOf(slot);
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
