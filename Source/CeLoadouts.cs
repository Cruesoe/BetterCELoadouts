using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterCELoadouts
{
    // Reflection access to Combat Extended's loadout types, so one build works with every CE version.
    internal static class CeLoadouts
    {
        public static readonly Type? DialogType = AccessTools.TypeByName("CombatExtended.Dialog_ManageLoadouts");
        public static readonly Type? LoadoutType = AccessTools.TypeByName("CombatExtended.Loadout");
        private static readonly Type? ManagerType = AccessTools.TypeByName("CombatExtended.LoadoutManager");
        private static readonly Type? GenericType = AccessTools.TypeByName("CombatExtended.LoadoutGenericDef");
        private static readonly Type? ItemType = DialogType == null ? null : AccessTools.Inner(DialogType, "SelectableItem");

        private static readonly PropertyInfo? LoadoutsProperty = Property(ManagerType, "Loadouts");
        private static readonly PropertyInfo? AssignedProperty = Property(ManagerType, "AssignedLoadouts");
        private static readonly FieldInfo? DefaultField = Field(LoadoutType, "defaultLoadout");
        private static readonly PropertyInfo? LabelCapProperty = Property(LoadoutType, "LabelCap");
        private static readonly FieldInfo? LabelField = Field(LoadoutType, "label");
        private static readonly PropertyInfo? ParentProperty = Property(LoadoutType, "ParentLoadout");
        private static readonly PropertyInfo? ParentIdProperty = Property(LoadoutType, "parentID");
        private static readonly PropertyInfo? UniqueIdProperty = Property(LoadoutType, "UniqueID");
        private static readonly PropertyInfo? AncestorsProperty = Property(LoadoutType, "Ancestors");
        private static readonly FieldInfo? CanBeDeletedField = Field(LoadoutType, "canBeDeleted");
        private static readonly PropertyInfo? CurrentProperty = Property(DialogType, "CurrentLoadout");
        private static readonly MethodInfo? NewMethod = DialogType == null ? null : AccessTools.Method(DialogType, "NewLoadout");
        private static readonly MethodInfo? RemoveMethod = DialogType == null ? null : AccessTools.Method(DialogType, "RemoveLoadout");
        private static readonly FieldInfo? SourceField = Field(DialogType, "_source");
        private static readonly FieldInfo? SourceGenericField = Field(DialogType, "_sourceGeneric");
        private static readonly FieldInfo? GreyedOutField = Field(ItemType, "isGreyedOut");
        private static readonly FieldInfo? ItemDefField = Field(ItemType, "thingDef");
        private static readonly PropertyInfo? SlotsProperty = Property(LoadoutType, "Slots");
        private static readonly PropertyInfo? OwnSlotsProperty = Property(LoadoutType, "OwnSlots");

        // Deepest parent chain followed; CE breaks parent cycles itself, so this only guards against a changed CE.
        private const int MaxDepth = 64;
        private static readonly PropertyInfo? SlotDefProperty = Property(AccessTools.TypeByName("CombatExtended.LoadoutSlot"), "thingDef");
        private static readonly FieldInfo? SourceTypeField = Field(DialogType, "_sourceType");
        private static readonly MethodInfo? FilterSourceMethod = DialogType == null ? null : AccessTools.Method(DialogType, "FilterSource");
        private static readonly PropertyInfo? LambdaProperty = Property(GenericType, "lambda");

        public static bool Ready => DialogType != null && LoadoutsProperty != null && AssignedProperty != null
            && DefaultField != null && LabelCapProperty != null && LabelField != null && ParentProperty != null && CurrentProperty != null;

        public static bool CanReparent => ParentIdProperty?.CanWrite == true && UniqueIdProperty != null && AncestorsProperty != null;

        public static bool CanManage => CanReparent && CanBeDeletedField != null && NewMethod != null && RemoveMethod != null;

        public static bool CanSort => SourceField != null && SourceGenericField != null && GreyedOutField != null && LambdaProperty != null && ItemDefField != null;

        // Every weapon in the loadout, including the ones it inherits from its parents.
        public static List<ThingDef> WeaponsIn(object loadout)
        {
            List<ThingDef> weapons = new();
            if (SlotsProperty?.GetValue(loadout) is IEnumerable slots && SlotDefProperty != null)
            {
                foreach (object slot in slots)
                {
                    if (SlotDefProperty.GetValue(slot) is ThingDef { IsWeapon: true } weapon && !weapons.Contains(weapon))
                    {
                        weapons.Add(weapon);
                    }
                }
            }
            return weapons;
        }

        // A hash of the weapons in the loadout and its parents, read from each one's own slots without building CE's combined list.
        public static int WeaponSignature(object loadout)
        {
            int signature = RuntimeHelpers.GetHashCode(loadout);
            if (OwnSlotsProperty == null || SlotDefProperty == null)
            {
                return signature;
            }
            object? level = loadout;
            for (int depth = 0; level != null && depth < MaxDepth; depth++)
            {
                if (OwnSlotsProperty.GetValue(level) is IList slots)
                {
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (SlotDefProperty.GetValue(slots[i]) is ThingDef { IsWeapon: true } weapon)
                        {
                            signature = signature * 31 + weapon.shortHash;
                        }
                    }
                }
                level = Parent(level);
            }
            return signature;
        }

        // Rebuilds the dialog's item list, which runs the sort again; the generic list doesn't depend on weapons.
        public static void RefreshSource(object dialog)
        {
            if (SourceTypeField != null && FilterSourceMethod != null && SourceTypeField.GetValue(dialog)?.ToString() != "Generic")
            {
                FilterSourceMethod.Invoke(dialog, new object[] { "" });
            }
        }

        private static PropertyInfo? Property(Type? type, string name) => type == null ? null : AccessTools.Property(type, name);

        private static FieldInfo? Field(Type? type, string name) => type == null ? null : AccessTools.Field(type, name);

        // Every loadout except CE's built-in default, in CE's own order.
        public static List<object> Loadouts()
        {
            if (LoadoutsProperty!.GetValue(null) is not IEnumerable loadouts)
            {
                return new List<object>();
            }
            return loadouts.Cast<object>().Where(l => !(bool)DefaultField!.GetValue(l)!).ToList();
        }

        // Number of loadouts in CE's list, its default included.
        public static int Count() => LoadoutsProperty!.GetValue(null) is IList loadouts ? loadouts.Count : 0;

        public static string Label(object loadout) => LabelCapProperty!.GetValue(loadout) as string ?? "";

        // The stored name; CE replaces the string when it's renamed, so a reference check spots a rename.
        public static string? RawLabel(object loadout) => LabelField!.GetValue(loadout) as string;

        public static object? Parent(object loadout) => ParentProperty!.GetValue(loadout);

        // Same rule as CE's Parent loadout menu: a loadout can't become a parent of itself or of its own ancestors.
        public static bool CanBeParent(object child, object parent)
        {
            return parent != child && !(AncestorsProperty!.GetValue(parent) is IEnumerable ancestors && ancestors.Cast<object>().Contains(child));
        }

        // Sets the parent the way CE's Parent loadout menu does; null makes it a top-level loadout.
        public static void SetParent(object child, object? parent)
        {
            ParentIdProperty!.SetValue(child, parent == null ? 0 : (int)UniqueIdProperty!.GetValue(parent)!);
        }

        public static bool CanBeDeleted(object loadout) => (bool)CanBeDeletedField!.GetValue(loadout)!;

        // CE's own create and delete, which are also its multiplayer sync points.
        public static object? New() => NewMethod!.Invoke(null, null);

        public static void Remove(object loadout) => RemoveMethod!.Invoke(null, new[] { loadout });

        public static object? Current(object dialog) => CurrentProperty!.GetValue(dialog);

        public static void Select(object dialog, object? loadout) => CurrentProperty!.SetValue(dialog, loadout);

        // Number of living player pawns assigned to each loadout; CE keeps banished, sold and (until a save) dead pawns in its list.
        public static Dictionary<object, int> PawnCounts()
        {
            Dictionary<object, int> counts = new();
            if (AssignedProperty!.GetValue(null) is IDictionary assigned)
            {
                foreach (DictionaryEntry entry in assigned)
                {
                    if (entry.Key is Pawn { Dead: false, Destroyed: false } pawn && pawn.Faction == Faction.OfPlayer && entry.Value is object loadout)
                    {
                        counts.TryGetValue(loadout, out int count);
                        counts[loadout] = count + 1;
                    }
                }
            }
            return counts;
        }

        // Orders the dialog's item list: things that suit the loadout's weapons first, then the rest, with items the colony has ahead
        // of those it doesn't within each group. CE's alphabetical order is kept inside every group.
        public static void SortSourceByStock(object dialog, HashSet<ThingDef> mapDefs, HashSet<ThingDef> loadoutAmmo)
        {
            if (SourceField!.GetValue(dialog) is IList items && items.Count > 1)
            {
                IList sorted = (IList)Activator.CreateInstance(items.GetType())!;
                foreach (object item in items.Cast<object>()
                    .OrderBy(i => loadoutAmmo.Contains((ThingDef)ItemDefField!.GetValue(i)!) ? 0 : 1)
                    .ThenBy(i => (bool)GreyedOutField!.GetValue(i)!))
                {
                    sorted.Add(item);
                }
                SourceField.SetValue(dialog, sorted);
            }

            if (SourceGenericField!.GetValue(dialog) is IList generics && generics.Count > 1)
            {
                IList sorted = (IList)Activator.CreateInstance(generics.GetType())!;
                foreach (object generic in generics.Cast<object>().OrderBy(g => !InStock(g, mapDefs)))
                {
                    sorted.Add(generic);
                }
                SourceGenericField.SetValue(dialog, sorted);
            }
        }

        private static bool InStock(object generic, HashSet<ThingDef> mapDefs)
        {
            Predicate<ThingDef> accepts = (Predicate<ThingDef>)LambdaProperty!.GetValue(generic)!;
            return mapDefs.Any(def => accepts(def));
        }
    }
}
