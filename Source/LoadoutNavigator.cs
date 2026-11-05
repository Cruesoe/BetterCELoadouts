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

        // Window state, reset when a new window opens: scroll, the loadout under the mouse since the button went down, and whether it's being dragged.
        private static System.WeakReference<object>? lastDialog;
        private static Vector2 scrollPosition;
        private static object? pressed;
        private static string pressedLabel = "";
        private static Vector2 pressPosition;
        private static bool dragging;
        private static float ghostWidth;

        // Rows for the open window, rebuilt when it opens, after the list's own changes, and when CE adds, removes, renames or reparents a loadout.
        private static readonly List<Row> Rows = new();
        private static readonly Dictionary<string, string> TruncateCache = new();
        private static bool dirty = true;
        private static bool anyLoadouts;
        private static int lastCount = -1;
        private static object? lastCurrent;
        private static string? lastLabel;
        private static object? lastParent;

        // Fixed text, translated when a window opens.
        private static string headerText = "";
        private static string dropText = "";
        private static string noLoadoutsText = "";
        private static string newTip = "";
        private static string copyTip = "";
        private static string selectToCopyTip = "";
        private static string ceLoadTip = "";
        private static string ceSaveTip = "";
        private static string nothingToSaveTip = "";
        private static string deleteTip = "";

        private readonly struct Row
        {
            public readonly object Loadout;
            public readonly string Label;
            public readonly int Depth;
            public readonly bool HasChildren;
            public readonly bool Deletable;
            public readonly string Pawns;
            public readonly string Tooltip;

            public Row(object loadout, string label, int depth, bool hasChildren, bool deletable, string pawns, string tooltip)
            {
                Loadout = loadout;
                Label = label;
                Depth = depth;
                HasChildren = hasChildren;
                Deletable = deletable;
                Pawns = pawns;
                Tooltip = tooltip;
            }
        }

        public static void Draw(Rect rect, object dialog)
        {
            Text.Font = GameFont.Small;
            Event e = Event.current;
            if (lastDialog == null || !lastDialog.TryGetTarget(out object? last) || last != dialog)
            {
                StartWindow(dialog);
            }
            if (e.rawType == EventType.MouseDown)
            {
                pressed = null;
                dragging = false;
            }

            bool canManage = CeLoadouts.CanManage;
            object? current = CeLoadouts.Current(dialog);
            RefreshRows(current, canManage);

            Rect headerRect = new(rect.x, rect.y, rect.width, HeaderHeight);
            // Bottom of the column: one row of icon buttons for New, Copy, Load and Save loadout.
            Rect controlsRect = new(rect.x, rect.yMax - HeaderHeight, rect.width, HeaderHeight);
            float listBottom = canManage ? controlsRect.y - Margin : rect.yMax;
            Rect listRect = new(rect.x, headerRect.yMax + Margin * 2, rect.width, listBottom - headerRect.yMax - Margin * 2);
            if (canManage)
            {
                DrawControls(controlsRect, dialog, current != null);
            }
            bool canReparent = CeLoadouts.CanReparent;
            bool clearingParent = dragging && pressed != null && CeLoadouts.Parent(pressed) != null;
            DrawHeader(headerRect, clearingParent);

            if (Rows.Count == 0)
            {
                GUI.color = Color.grey;
                Widgets.Label(listRect, noLoadoutsText);
                GUI.color = Color.white;
                return;
            }

            bool overHeader = Mouse.IsOver(headerRect);
            bool overList = Mouse.IsOver(listRect);
            Rect viewRect = new(0f, 0f, listRect.width, Rows.Count * RowHeight);
            if (viewRect.height > listRect.height)
            {
                viewRect.width -= 16f;
            }

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            object? hovered = null;
            int hoveredIndex = Mathf.FloorToInt(e.mousePosition.y / RowHeight);
            if (overList && hoveredIndex >= 0 && hoveredIndex < Rows.Count)
            {
                hovered = Rows[hoveredIndex].Loadout;
            }
            object? target = dragging && hovered != null && CeLoadouts.CanBeParent(pressed!, hovered) ? hovered : null;

            for (int i = 0; i < Rows.Count; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), Rows[i], i, dialog, current, target, canManage);
            }

            if (pressed != null && canReparent && !dragging && e.type == EventType.MouseDrag && (e.mousePosition - pressPosition).magnitude > DragThreshold)
            {
                dragging = true;
                ghostWidth = Text.CalcSize(pressedLabel).x + Margin * 2;
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
                DrawGhost();
            }
            Widgets.DrawLineVertical(rect.xMax + Gap / 2f, listRect.y, listRect.height);
        }

        private static void StartWindow(object dialog)
        {
            lastDialog = new System.WeakReference<object>(dialog);
            scrollPosition = Vector2.zero;
            pressed = null;
            dragging = false;
            dirty = true;
            TruncateCache.Clear();

            headerText = "CE_Loadouts".Translate();
            dropText = "BetterCELoadouts_DropToClearParent".Translate();
            noLoadoutsText = "CE_NoLoadouts".Translate();
            newTip = "CE_NewLoadout".Translate();
            copyTip = "CE_CopyLoadout".Translate();
            selectToCopyTip = "BetterCELoadouts_SelectToCopy".Translate();
            ceLoadTip = "CE_LoadLoadout".Translate();
            ceSaveTip = "CE_SaveLoadout".Translate();
            nothingToSaveTip = "BetterCELoadouts_NothingToSave".Translate();
            deleteTip = "CE_DeleteLoadout".Translate();
        }

        // Rebuilds the rows when they're marked dirty, the number of loadouts changes, or the selected loadout changes, is renamed or gets a new parent.
        private static void RefreshRows(object? current, bool canManage)
        {
            int count = CeLoadouts.Count();
            string? label = current == null ? null : CeLoadouts.RawLabel(current);
            object? parent = current == null ? null : CeLoadouts.Parent(current);
            if (!dirty && count == lastCount && current == lastCurrent && ReferenceEquals(label, lastLabel) && parent == lastParent)
            {
                return;
            }
            dirty = false;
            lastCount = count;
            lastCurrent = current;
            lastLabel = label;
            lastParent = parent;

            List<object> loadouts = CeLoadouts.Loadouts();
            anyLoadouts = loadouts.Count > 0;
            TruncateCache.Clear();
            BuildRows(loadouts, canManage, CeLoadouts.PawnCounts());
        }

        // Copy, Load and Save set a flag that CE's own (hidden) buttons report as pressed, so its code runs when it next draws.
        private static void DrawControls(Rect row, object dialog, bool hasLoadout)
        {
            float width = (row.width - Margin * 3) / 4f;
            Rect newRect = new(row.x, row.y, width, row.height);
            Rect copyRect = new(newRect.xMax + Margin, row.y, width, row.height);
            Rect loadRect = new(copyRect.xMax + Margin, row.y, width, row.height);
            Rect saveRect = new(loadRect.xMax + Margin, row.y, width, row.height);

            if (IconButton(newRect, NewIcon, newTip, true) && CeLoadouts.New() is object created)
            {
                CeLoadouts.Select(dialog, created);
            }
            if (IconButton(copyRect, CopyIcon, hasLoadout ? copyTip : selectToCopyTip, hasLoadout))
            {
                TopBar.CopyRequested = true;
            }

            // With sets, Load and Save open a menu, so they need no tooltip of their own.
            bool sets = LoadoutSets.Available;
            if (IconButton(loadRect, LoadIcon, sets ? null : ceLoadTip, true))
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
            string? saveTip = !canSave ? nothingToSaveTip : sets ? null : ceSaveTip;
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
        private static bool IconButton(Rect rect, Texture2D icon, string? tooltip, bool active)
        {
            bool clicked = Widgets.ButtonText(rect, "", true, true, active);
            Rect iconRect = new(rect.center.x - IconSize / 2f, rect.center.y - IconSize / 2f, IconSize, IconSize);
            GUI.color = active ? Color.white : Color.grey;
            GUI.DrawTexture(iconRect, icon);
            GUI.color = Color.white;
            if (tooltip != null)
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
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
                Widgets.Label(rect, dropText);
                GUI.color = Color.white;
            }
            else
            {
                Widgets.Label(rect, headerText);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawRow(Rect row, Row entry, int index, object dialog, object? current, object? target, bool canManage)
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
                TooltipHandler.TipRegion(deleteRect, deleteTip);
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

            Rect countRect = new(row.xMax - 30f - Margin, row.y, 30f, row.height);
            Rect labelRect = new(x, row.y, countRect.x - x - Margin, row.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(labelRect, entry.Label.Truncate(labelRect.width, TruncateCache));
            Text.WordWrap = true;
            if (entry.Pawns.Length > 0)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.grey;
                Widgets.Label(countRect, entry.Pawns);
                GUI.color = Color.white;
            }
            Text.Anchor = TextAnchor.UpperLeft;

            Event e = Event.current;
            if (pressed == null && Mouse.IsOver(row))
            {
                TooltipHandler.TipRegion(row, entry.Tooltip);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(row)
                && !(entry.HasChildren && Mouse.IsOver(toggleRect)) && !(entry.Deletable && Mouse.IsOver(deleteRect)))
            {
                pressed = entry.Loadout;
                pressedLabel = entry.Label;
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
                dirty = true;
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
            dirty = true;
        }

        // The dragged loadout's name, following the mouse.
        private static void DrawGhost()
        {
            Vector2 mouse = Event.current.mousePosition;
            Rect rect = new(mouse.x + 12f, mouse.y - RowHeight / 2f, ghostWidth, RowHeight);
            Widgets.DrawBoxSolid(rect, GhostBackground);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, pressedLabel);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static string Tooltip(object loadout, string label, int pawns)
        {
            string tooltip = label;
            if (CeLoadouts.Parent(loadout) is object parent)
            {
                tooltip += "\n" + "BetterCELoadouts_InheritsFrom".Translate(CeLoadouts.Label(parent));
            }
            return tooltip + "\n" + "BetterCELoadouts_AssignedPawns".Translate(pawns);
        }

        private static bool IsCollapsed(object loadout) => Collapsed.TryGetValue(loadout, out _);

        private static void SetCollapsed(object loadout, bool collapsed)
        {
            Collapsed.Remove(loadout);
            if (collapsed)
            {
                Collapsed.Add(loadout, loadout);
            }
            dirty = true;
        }

        // Depth-first list of loadouts under their parents, skipping the children of collapsed parents.
        private static void BuildRows(List<object> loadouts, bool canManage, Dictionary<object, int> pawnCounts)
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

            Rows.Clear();
            foreach (object root in roots)
            {
                AddRows(root, 0, children, canManage, pawnCounts);
            }
        }

        private static void AddRows(object loadout, int depth, Dictionary<object, List<object>> children, bool canManage, Dictionary<object, int> pawnCounts)
        {
            children.TryGetValue(loadout, out List<object>? kids);
            pawnCounts.TryGetValue(loadout, out int pawns);
            string label = CeLoadouts.Label(loadout);
            Rows.Add(new Row(loadout, label, depth, kids != null, canManage && CeLoadouts.CanBeDeleted(loadout),
                pawns > 0 ? pawns.ToString() : "", Tooltip(loadout, label, pawns)));
            if (kids != null && !IsCollapsed(loadout))
            {
                foreach (object child in kids)
                {
                    AddRows(child, depth + 1, children, canManage, pawnCounts);
                }
            }
        }
    }
}
