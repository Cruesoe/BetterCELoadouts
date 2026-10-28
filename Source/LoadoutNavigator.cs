using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private const float IconSize = 20f;

        private static readonly Texture2D DarkBackground = SolidColorMaterials.NewSolidColorTexture(0f, 0f, 0f, .2f);
        private static readonly Texture2D NewIcon = ContentFinder<Texture2D>.Get("UI/BetterCELoadouts_New");
        private static readonly Texture2D CopyIcon = ContentFinder<Texture2D>.Get("UI/BetterCELoadouts_Copy");
        private static readonly Texture2D LoadIcon = ContentFinder<Texture2D>.Get("UI/BetterCELoadouts_Load");
        private static readonly Texture2D SaveIcon = ContentFinder<Texture2D>.Get("UI/BetterCELoadouts_Save");
        private static readonly Color GhostBackground = new(0f, 0f, 0f, .6f);

        // Collapsed parents, held weakly so loadouts from earlier games are let go.
        private static readonly ConditionalWeakTable<object, object> Collapsed = new();
        private static Vector2 scrollPosition;

        // Loadout under the mouse since the button went down, and whether it has moved far enough to count as a drag; reset for each new window.
        private static object? pressed;
        private static Vector2 pressPosition;
        private static bool dragging;
        private static System.WeakReference<object>? lastDialog;

        private readonly struct Row
        {
            public readonly object Loadout;
            public readonly string Label;
            public readonly int Depth;
            public readonly bool HasChildren;
            public readonly bool Deletable;

            public Row(object loadout, string label, int depth, bool hasChildren, bool deletable)
            {
                Loadout = loadout;
                Label = label;
                Depth = depth;
                HasChildren = hasChildren;
                Deletable = deletable;
            }
        }

        public static void Draw(Rect rect, object dialog)
        {
            Text.Font = GameFont.Small;
            Event e = Event.current;
            if (lastDialog == null || !lastDialog.TryGetTarget(out object? last) || last != dialog)
            {
                lastDialog = new System.WeakReference<object>(dialog);
                pressed = null;
                dragging = false;
            }
            if (e.rawType == EventType.MouseDown)
            {
                pressed = null;
                dragging = false;
            }

            // Read once per pass and shared by everything drawn below.
            bool canManage = CeLoadouts.CanManage;
            List<object> loadouts = CeLoadouts.Loadouts();
            object? current = CeLoadouts.Current(dialog);

            Rect headerRect = new(rect.x, rect.y, rect.width, HeaderHeight);
            // Bottom of the column: one row of icon buttons for New, Copy, Load and Save loadout.
            Rect controlsRect = new(rect.x, rect.yMax - HeaderHeight, rect.width, HeaderHeight);
            float listBottom = canManage ? controlsRect.y - Margin : rect.yMax;
            Rect listRect = new(rect.x, headerRect.yMax + Margin * 2, rect.width, listBottom - headerRect.yMax - Margin * 2);
            if (canManage)
            {
                DrawControls(controlsRect, dialog, current != null, loadouts.Count > 0);
            }
            bool canReparent = CeLoadouts.CanReparent;
            bool clearingParent = dragging && pressed != null && CeLoadouts.Parent(pressed) != null;
            DrawHeader(headerRect, clearingParent);

            List<Row> rows = BuildRows(loadouts, canManage);
            if (rows.Count == 0)
            {
                GUI.color = Color.grey;
                Widgets.Label(listRect, "CE_NoLoadouts".Translate());
                GUI.color = Color.white;
                return;
            }

            Dictionary<object, int> pawnCounts = CeLoadouts.PawnCounts();
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

        // Copy, Load and Save set a flag that CE's own (hidden) buttons report as pressed, so its code runs when it next draws.
        private static void DrawControls(Rect row, object dialog, bool hasLoadout, bool anyLoadouts)
        {
            float width = (row.width - Margin * 3) / 4f;
            Rect newRect = new(row.x, row.y, width, row.height);
            Rect copyRect = new(newRect.xMax + Margin, row.y, width, row.height);
            Rect loadRect = new(copyRect.xMax + Margin, row.y, width, row.height);
            Rect saveRect = new(loadRect.xMax + Margin, row.y, width, row.height);

            if (IconButton(newRect, NewIcon, "CE_NewLoadout".Translate(), true) && CeLoadouts.New() is object created)
            {
                CeLoadouts.Select(dialog, created);
            }
            TaggedString copyTip = hasLoadout ? "CE_CopyLoadout".Translate() : "BetterCELoadouts_SelectToCopy".Translate();
            if (IconButton(copyRect, CopyIcon, copyTip, hasLoadout))
            {
                TopBar.CopyRequested = true;
            }
            bool sets = LoadoutSets.Available;
            if (IconButton(loadRect, LoadIcon, sets ? "BetterCELoadouts_LoadTip".Translate() : "CE_LoadLoadout".Translate(), true))
            {
                if (sets)
                {
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new("BetterCELoadouts_LoadOne".Translate(), () => TopBar.LoadRequested = true),
                        new("BetterCELoadouts_LoadAll".Translate(), LoadoutSets.LoadAll),
                    }));
                }
                else
                {
                    TopBar.LoadRequested = true;
                }
            }

            // With sets, Save is available whenever there's a loadout in the list; saving just the selected one needs a selection.
            bool canSave = sets ? anyLoadouts : hasLoadout;
            TaggedString saveTip = !canSave ? "BetterCELoadouts_NothingToSave".Translate()
                : sets ? "BetterCELoadouts_SaveTip".Translate() : "CE_SaveLoadout".Translate();
            if (IconButton(saveRect, SaveIcon, saveTip, canSave))
            {
                if (sets)
                {
                    FloatMenuOption saveOne = new("BetterCELoadouts_SaveOne".Translate(), hasLoadout ? () => TopBar.SaveRequested = true : null);
                    if (!hasLoadout)
                    {
                        saveOne.tooltip = new TipSignal("BetterCELoadouts_SelectToSave".Translate());
                    }
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        saveOne,
                        new("BetterCELoadouts_SaveAll".Translate(), LoadoutSets.SaveAll),
                    }));
                }
                else
                {
                    TopBar.SaveRequested = true;
                }
            }
        }

        // CE's button background with an icon on it, greyed out and inert when inactive.
        private static bool IconButton(Rect rect, Texture2D icon, TaggedString tooltip, bool active)
        {
            bool clicked = Widgets.ButtonText(rect, "", true, true, active);
            Rect iconRect = new(rect.center.x - IconSize / 2f, rect.center.y - IconSize / 2f, IconSize, IconSize);
            GUI.color = active ? Color.white : Color.grey;
            GUI.DrawTexture(iconRect, icon);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, tooltip);
            return clicked;
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
            if (entry.Deletable && Widgets.ButtonImage(deleteRect, TexButton.Delete))
            {
                ConfirmDelete(dialog, entry.Loadout);
            }
            if (entry.Deletable && Mouse.IsOver(deleteRect))
            {
                TooltipHandler.TipRegion(deleteRect, "CE_DeleteLoadout".Translate());
            }

            float x = row.x + Margin + (canManage ? DeleteSize + Margin : 0f) + entry.Depth * Indent;
            Rect toggleRect = new(x, row.y + (row.height - ToggleSize) / 2f, ToggleSize, ToggleSize);
            if (entry.HasChildren)
            {
                bool collapsed = IsCollapsed(entry.Loadout);
                if (Widgets.ButtonImage(toggleRect, collapsed ? TexButton.Reveal : TexButton.Collapse))
                {
                    SetCollapsed(entry.Loadout, !collapsed);
                }
            }
            x += ToggleSize + Margin;

            pawnCounts.TryGetValue(entry.Loadout, out int pawns);
            Rect countRect = new(row.xMax - 30f - Margin, row.y, 30f, row.height);
            Rect labelRect = new(x, row.y, countRect.x - x - Margin, row.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(labelRect, entry.Label.Truncate(labelRect.width));
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
                TooltipHandler.TipRegion(row, Tooltip(entry, pawns));
            }
            if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(row)
                && !(entry.HasChildren && Mouse.IsOver(toggleRect)) && !(entry.Deletable && Mouse.IsOver(deleteRect)))
            {
                pressed = entry.Loadout;
                pressPosition = e.mousePosition;
                e.Use();
            }
        }

        // Asks first, since one click on the bin deletes; CE's own delete then moves its colonists to the default loadout.
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
                SetCollapsed(target, false);
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

        private static string Tooltip(Row entry, int pawns)
        {
            List<string> lines = new() { entry.Label };
            if (CeLoadouts.Parent(entry.Loadout) is object parent)
            {
                lines.Add("BetterCELoadouts_InheritsFrom".Translate(CeLoadouts.Label(parent)));
            }
            lines.Add("BetterCELoadouts_AssignedPawns".Translate(pawns));
            return string.Join("\n", lines);
        }

        private static bool IsCollapsed(object loadout) => Collapsed.TryGetValue(loadout, out _);

        private static void SetCollapsed(object loadout, bool collapsed)
        {
            Collapsed.Remove(loadout);
            if (collapsed)
            {
                Collapsed.Add(loadout, loadout);
            }
        }

        // Depth-first list of loadouts under their parents, skipping the children of collapsed parents.
        private static List<Row> BuildRows(List<object> loadouts, bool canManage)
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
                AddRows(root, 0, children, rows, canManage);
            }
            return rows;
        }

        private static void AddRows(object loadout, int depth, Dictionary<object, List<object>> children, List<Row> rows, bool canManage)
        {
            children.TryGetValue(loadout, out List<object>? kids);
            rows.Add(new Row(loadout, CeLoadouts.Label(loadout), depth, kids != null, canManage && CeLoadouts.CanBeDeleted(loadout)));
            if (kids != null && !IsCollapsed(loadout))
            {
                foreach (object child in kids)
                {
                    AddRows(child, depth + 1, children, rows, canManage);
                }
            }
        }
    }
}
