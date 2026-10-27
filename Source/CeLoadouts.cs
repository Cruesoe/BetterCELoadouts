using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterCELoadouts
{
    // Reflection access to Combat Extended's loadout types, so one build works with every CE version.
    internal static class CeLoadouts
    {
        public static readonly Type? DialogType = AccessTools.TypeByName("CombatExtended.Dialog_ManageLoadouts");
        private static readonly Type? LoadoutType = AccessTools.TypeByName("CombatExtended.Loadout");
        private static readonly Type? ManagerType = AccessTools.TypeByName("CombatExtended.LoadoutManager");
        private static readonly Type? GenericType = AccessTools.TypeByName("CombatExtended.LoadoutGenericDef");
        private static readonly Type? ItemType = DialogType == null ? null : AccessTools.Inner(DialogType, "SelectableItem");

        private static readonly PropertyInfo? LoadoutsProperty = Property(ManagerType, "Loadouts");
        private static readonly PropertyInfo? AssignedProperty = Property(ManagerType, "AssignedLoadouts");
        private static readonly FieldInfo? DefaultField = Field(LoadoutType, "defaultLoadout");
        private static readonly PropertyInfo? LabelCapProperty = Property(LoadoutType, "LabelCap");
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
        private static readonly PropertyInfo? LambdaProperty = Property(GenericType, "lambda");

        public static bool Ready => DialogType != null && LoadoutsProperty != null && AssignedProperty != null
            && DefaultField != null && LabelCapProperty != null && ParentProperty != null && CurrentProperty != null;

        public static bool CanReparent => ParentIdProperty?.CanWrite == true && UniqueIdProperty != null && AncestorsProperty != null;

        public static bool CanManage => CanBeDeletedField != null && NewMethod != null && RemoveMethod != null;

        public static bool CanSort => SourceField != null && SourceGenericField != null && GreyedOutField != null && LambdaProperty != null;

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

        public static string Label(object loadout) => LabelCapProperty!.GetValue(loadout) as string ?? "";

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

        // Number of pawns assigned to each loadout.
        public static Dictionary<object, int> PawnCounts()
        {
            Dictionary<object, int> counts = new();
            if (AssignedProperty!.GetValue(null) is IDictionary assigned)
            {
                foreach (object loadout in assigned.Values)
                {
                    counts.TryGetValue(loadout, out int count);
                    counts[loadout] = count + 1;
                }
            }
            return counts;
        }

        // Moves items the colony has to the top of the dialog's item list, keeping CE's alphabetical order within each group.
        public static void SortSourceByStock(object dialog, HashSet<ThingDef> mapDefs)
        {
            if (SourceField!.GetValue(dialog) is IList items && items.Count > 1)
            {
                IList sorted = (IList)Activator.CreateInstance(items.GetType())!;
                foreach (object item in items.Cast<object>().OrderBy(i => (bool)GreyedOutField!.GetValue(i)!))
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
