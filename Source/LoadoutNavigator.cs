using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace BetterCELoadouts
{
    // The loadout tree drawn down the left of CE's Manage loadouts window: parents with their children indented beneath.
    [StaticConstructorOnStartup]
    internal static class LoadoutNavigator
    {
        public const float Width = 220f;
        public const float Gap = 12f;

        // Match CE's dialog: 30px top bar, 6px margins, 28px rows.
        private const float HeaderHeight = 30f;
        private const float Margin = 6f;
        private const float RowHeight = 28f;
        private const float Indent = 14f;
        private const float ToggleSize = 18f;

        private static readonly Texture2D DarkBackground = SolidColorMaterials.NewSolidColorTexture(0f, 0f, 0f, .2f);
        private static readonly HashSet<object> Collapsed = new();
        private static Vector2 scrollPosition;

        private readonly struct Row
        {
            public readonly object Loadout;
            public readonly int Depth;
            public readonly bool HasChildren;

            public Row(object loadout, int depth, bool hasChildren)
            {
                Loadout = loadout;
                Depth = depth;
                HasChildren = hasChildren;
            }
        }

        public static void Draw(Rect rect, object dialog)
        {
            Text.Font = GameFont.Small;
            Rect headerRect = new(rect.x, rect.y, rect.width, HeaderHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(headerRect, "CE_Loadouts".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect listRect = new(rect.x, headerRect.yMax + Margin * 2, rect.width, rect.height - HeaderHeight - Margin * 2);
            List<Row> rows = BuildRows(CeLoadouts.Loadouts());
            if (rows.Count == 0)
            {
                GUI.color = Color.grey;
                Widgets.Label(listRect, "CE_NoLoadouts".Translate());
                GUI.color = Color.white;
                return;
            }

            Dictionary<object, int> pawnCounts = CeLoadouts.PawnCounts();
            object? current = CeLoadouts.Current(dialog);
            Rect viewRect = new(0f, 0f, listRect.width, rows.Count * RowHeight);
            if (viewRect.height > listRect.height)
            {
                viewRect.width -= 16f;
            }

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), rows[i], i, dialog, current, pawnCounts);
            }
            Widgets.EndScrollView();

            Widgets.DrawLineVertical(rect.xMax + Gap / 2f, listRect.y, listRect.height);
        }

        private static void DrawRow(Rect row, Row entry, int index, object dialog, object? current, Dictionary<object, int> pawnCounts)
        {
            if (index % 2 == 0)
            {
                GUI.DrawTexture(row, DarkBackground);
            }
            if (entry.Loadout == current)
            {
                Widgets.DrawHighlightSelected(row);
            }
            Widgets.DrawHighlightIfMouseover(row);

            float x = row.x + Margin + entry.Depth * Indent;
            if (entry.HasChildren)
            {
                Rect toggleRect = new(x, row.y + (row.height - ToggleSize) / 2f, ToggleSize, ToggleSize);
                bool collapsed = Collapsed.Contains(entry.Loadout);
                if (Widgets.ButtonImage(toggleRect, collapsed ? TexButton.Reveal : TexButton.Collapse))
                {
                    if (collapsed)
                    {
                        Collapsed.Remove(entry.Loadout);
                    }
                    else
                    {
                        Collapsed.Add(entry.Loadout);
                    }
                }
            }
            x += ToggleSize + Margin;

            pawnCounts.TryGetValue(entry.Loadout, out int pawns);
            Rect countRect = new(row.xMax - 30f - Margin, row.y, 30f, row.height);
            Rect labelRect = new(x, row.y, countRect.x - x - Margin, row.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(labelRect, CeLoadouts.Label(entry.Loadout).Truncate(labelRect.width));
            Text.WordWrap = true;
            if (pawns > 0)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.grey;
                Widgets.Label(countRect, pawns.ToString());
                GUI.color = Color.white;
            }
            Text.Anchor = TextAnchor.UpperLeft;

            if (Mouse.IsOver(row))
            {
                TooltipHandler.TipRegion(row, Tooltip(entry.Loadout, pawns));
            }
            if (Widgets.ButtonInvisible(labelRect) && entry.Loadout != current)
            {
                CeLoadouts.Select(dialog, entry.Loadout);
            }
        }

        private static string Tooltip(object loadout, int pawns)
        {
            List<string> lines = new() { CeLoadouts.Label(loadout) };
            if (CeLoadouts.Parent(loadout) is object parent)
            {
                lines.Add("BetterCELoadouts_InheritsFrom".Translate(CeLoadouts.Label(parent)));
            }
            lines.Add("BetterCELoadouts_AssignedPawns".Translate(pawns));
            return string.Join("\n", lines);
        }

        // Depth-first list of loadouts under their parents, skipping the children of collapsed parents.
        private static List<Row> BuildRows(List<object> loadouts)
        {
            HashSet<object> known = new(loadouts);
            Dictionary<object, List<object>> children = new();
            List<object> roots = new();
            foreach (object loadout in loadouts)
            {
                object? parent = CeLoadouts.Parent(loadout);
                if (parent != null && known.Contains(parent))
                {
                    if (!children.TryGetValue(parent, out List<object>? list))
                    {
                        children[parent] = list = new List<object>();
                    }
                    list.Add(loadout);
                }
                else
                {
                    roots.Add(loadout);
                }
            }

            List<Row> rows = new();
            foreach (object root in roots)
            {
                AddRows(root, 0, children, rows);
            }
            return rows;
        }

        private static void AddRows(object loadout, int depth, Dictionary<object, List<object>> children, List<Row> rows)
        {
            children.TryGetValue(loadout, out List<object>? kids);
            rows.Add(new Row(loadout, depth, kids != null));
            if (kids != null && !Collapsed.Contains(loadout))
            {
                foreach (object child in kids)
                {
                    AddRows(child, depth + 1, children, rows);
                }
            }
        }
    }
}
