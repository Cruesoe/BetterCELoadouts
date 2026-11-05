using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    // Takes every button out of CE's top bar (the loadout list does their jobs now).
    // Copy, Load and Save keep running CE's own code: a click in the list sets a flag that CE's hidden button then reports as pressed.
    internal static class TopBar
    {
        // CE's top bar: seven buttons 6px apart, in the order Select, New, Copy, Delete, Load, Save, Parent.
        private const float Margin = 6f;
        private const float BarHeight = 30f;
        private const int CopySlot = 2;
        private const int LoadSlot = 4;
        private const int SaveSlot = 5;

        private static readonly MethodInfo ButtonText = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonText),
            new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?) });

        // Set by the loadout list during a frame; cleared when CE finishes drawing it.
        public static bool CopyRequested;
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
            if (rect.y != 0f || rect.height != BarHeight)
            {
                return Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor);
            }
            return Mathf.RoundToInt(rect.x / (rect.width + Margin)) switch
            {
                CopySlot => CopyRequested,
                LoadSlot => LoadRequested,
                SaveSlot => SaveRequested,
                _ => false,
            };
        }
    }
}
