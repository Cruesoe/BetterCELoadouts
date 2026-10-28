using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BetterCELoadouts
{
    // The loadout tree drawn down the left of CE's Manage loadouts window: parents with their children indented beneath.
    // Drag a loadout onto another to make it a child, or onto the header or empty space to make it top-level.
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
        private const float DragThreshold = 6f;
        private const float DeleteSize = 16f;

        private static readonly Texture2D DarkBackground = SolidColorMaterials.NewSolidColorTexture(0f, 0f, 0f, .2f);
        private static readonly Color GhostBackground = new(0f, 0f, 0f, .6f);
        private static readonly Color DeleteColor = new(.85f, .25f, .25f);
        private static readonly Color DeleteMouseoverColor = new(1f, .4f, .4f);
        private static readonly HashSet<object> Collapsed = new();
        private static Vector2 scrollPosition;

        // Loadout under the mouse since the button went down, and whether it has moved far enough to count as a drag.
        private static object? pressed;
        private static Vector2 pressPosition;
        private static bool dragging;

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
            Event e = Event.current;
            if (e.rawType == EventType.MouseDown)
            {
                pressed = null;
                dragging = false;
            }

            bool canManage = CeLoadouts.CanManage;
            Rect headerRect = new(rect.x, rect.y, rect.width, HeaderHeight);
            // Bottom of the column: New loadout, with Load and Save loadout side by side beneath it.
            Rect fileRect = new(rect.x, rect.yMax - HeaderHeight, rect.width, HeaderHeight);
            Rect newRect = new(rect.x, fileRect.y - Margin - HeaderHeight, rect.width, HeaderHeight);
            float listBottom = canManage ? newRect.y - Margin : rect.yMax;
            Rect listRect = new(rect.x, headerRect.yMax + Margin * 2, rect.width, listBottom - headerRect.yMax - Margin * 2);
            if (canManage)
            {
                if (Widgets.ButtonText(newRect, "CE_NewLoadout".Translate()) && CeLoadouts.New() is object created)
                {
                    CeLoadouts.Select(dialog, created);
                }

                // Save needs a selected loadout; Load takes the whole row without one. CE's own dialogs run when it next draws.
                bool hasLoadout = CeLoadouts.Current(dialog) != null;
                float half = (fileRect.width - Margin) / 2f;
                Rect loadRect = hasLoadout ? new Rect(fileRect.x, fileRect.y, half, fileRect.height) : fileRect;
                if (Widgets.ButtonText(loadRect, "CE_LoadLoadout".Translate()))
                {
                    TopBar.LoadRequested = true;
                }
                if (hasLoadout && Widgets.ButtonText(new Rect(loadRect.xMax + Margin, fileRect.y, half, fileRect.height), "CE_SaveLoadout".Translate()))
                {
                    TopBar.SaveRequested = true;
                }
            }
            bool canReparent = CeLoadouts.CanReparent;
            bool clearingParent = dragging && pressed != null && CeLoadouts.Parent(pressed) != null;
            DrawHeader(headerRect, clearingParent);

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
            bool overHeader = Mouse.IsOver(headerRect);
            bool overList = Mouse.IsOver(listRect);
            Rect viewRect = new(0f, 0f, listRect.width, rows.Count * RowHeight);
            if (viewRect.height > listRect.height)
            {
                viewRect.width -= 16f;
            }

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            object? hovered = null;
            int hoveredIndex = Mathf.FloorToInt(e.mousePosition.y / RowHeight);
            if (overList && hoveredIndex >= 0 && hoveredIndex < rows.Count)
            {
                hovered = rows[hoveredIndex].Loadout;
            }
            object? target = dragging && hovered != null && CeLoadouts.CanBeParent(pressed!, hovered) ? hovered : null;

            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), rows[i], i, dialog, current, target, pawnCounts, canManage);
            }

            if (pressed != null && canReparent && !dragging && e.type == EventType.MouseDrag && (e.mousePosition - pressPosition).magnitude > DragThreshold)
            {
                dragging = true;
            }
            Widgets.EndScrollView();

            if (pressed != null && e.rawType == EventType.MouseUp)
            {
                if (dragging)
                {
                    Drop(pressed, target, overHeader || (overList && hovered == null));
                }
                else if (hovered == pressed && pressed != current)
                {
                    CeLoadouts.Select(dialog, pressed);
                }
                pressed = null;
                dragging = false;
                e.Use();
            }

            if (dragging && pressed != null)
            {
                DrawGhost(pressed);
            }
            Widgets.DrawLineVertical(rect.xMax + Gap / 2f, listRect.y, listRect.height);
        }

        // Shows where to drop to make a loadout top-level while one with a parent is being dragged.
        private static void DrawHeader(Rect rect, bool clearingParent)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            if (clearingParent)
            {
                if (Mouse.IsOver(rect))
                {
                    Widgets.DrawHighlight(rect);
                }
                GUI.color = Color.grey;
                Widgets.Label(rect, "BetterCELoadouts_DropToClearParent".Translate());
                GUI.color = Color.white;
            }
            else
            {
                Widgets.Label(rect, "CE_Loadouts".Translate());
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawRow(Rect row, Row entry, int index, object dialog, object? current, object? target, Dictionary<object, int> pawnCounts, bool canManage)
        {
            if (index % 2 == 0)
            {
                GUI.DrawTexture(row, DarkBackground);
            }
            if (entry.Loadout == current)
            {
                Widgets.DrawHighlightSelected(row);
            }
            if (entry.Loadout == target)
            {
                Widgets.DrawBox(row);
            }
            Widgets.DrawHighlightIfMouseover(row);

            // Delete buttons line up down the left edge, ahead of the tree's indentation.
            Rect deleteRect = new(row.x + Margin, row.y + (row.height - DeleteSize) / 2f, DeleteSize, DeleteSize);
            bool deletable = canManage && CeLoadouts.CanBeDeleted(entry.Loadout);
            if (deletable && Widgets.ButtonImage(deleteRect, TexButton.CloseXSmall, DeleteColor, DeleteMouseoverColor))
            {
                ConfirmDelete(dialog, entry.Loadout);
            }
            if (deletable && Mouse.IsOver(deleteRect))
            {
                TooltipHandler.TipRegion(deleteRect, "CE_DeleteLoadout".Translate());
            }

            float x = row.x + Margin + (canManage ? DeleteSize + Margin : 0f) + entry.Depth * Indent;
            Rect toggleRect = new(x, row.y + (row.height - ToggleSize) / 2f, ToggleSize, ToggleSize);
            if (entry.HasChildren)
            {
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

            Event e = Event.current;
            if (pressed == null && Mouse.IsOver(row))
            {
                TooltipHandler.TipRegion(row, Tooltip(entry.Loadout, pawns));
            }
            if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(row)
                && !(entry.HasChildren && Mouse.IsOver(toggleRect)) && !(deletable && Mouse.IsOver(deleteRect)))
            {
                pressed = entry.Loadout;
                pressPosition = e.mousePosition;
                e.Use();
            }
        }

        // Asks first, since one click on the X deletes; CE's own delete then moves its colonists to the default loadout.
        private static void ConfirmDelete(object dialog, object loadout)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("BetterCELoadouts_ConfirmDelete".Translate(CeLoadouts.Label(loadout)), () =>
            {
                if (CeLoadouts.Current(dialog) == loadout)
                {
                    CeLoadouts.Select(dialog, null);
                }
                // CE keeps a cached parent after it's deleted, so children are made top-level first.
                foreach (object child in CeLoadouts.Loadouts())
                {
                    if (CeLoadouts.Parent(child) == loadout)
                    {
                        CeLoadouts.SetParent(child, null);
                    }
                }
                CeLoadouts.Remove(loadout);
            }, true));
        }

        private static void Drop(object loadout, object? target, bool toTopLevel)
        {
            if (target != null && CeLoadouts.Parent(loadout) != target)
            {
                CeLoadouts.SetParent(loadout, target);
                Collapsed.Remove(target);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else if (target == null && toTopLevel && CeLoadouts.Parent(loadout) != null)
            {
                CeLoadouts.SetParent(loadout, null);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        // The dragged loadout's name, following the mouse.
        private static void DrawGhost(object loadout)
        {
            string label = CeLoadouts.Label(loadout);
            Vector2 mouse = Event.current.mousePosition;
            Rect rect = new(mouse.x + 12f, mouse.y - RowHeight / 2f, Text.CalcSize(label).x + Margin * 2, RowHeight);
            Widgets.DrawBoxSolid(rect, GhostBackground);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
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
