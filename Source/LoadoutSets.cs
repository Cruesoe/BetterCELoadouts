using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterCELoadouts
{
    // Saves every loadout in the list to one file, and loads a file of loadouts back in, using CE's own loadout config, file dialogs and add-loadout code.
    // A set file is <LoadoutSet> holding one CE <LoadoutConfig> per loadout, kept in its own folder so CE's single-loadout list never shows it.
    internal static class LoadoutSets
    {
        private const string Folder = "loadoutset";
        private const string DefaultFileName = "Loadouts";

        private static readonly Type? ConfigType = AccessTools.TypeByName("CombatExtended.LoadoutConfig");
        private static readonly Type? FileListType = AccessTools.TypeByName("CombatExtended.FileListDialog");
        private static readonly Type? SaveDialogType = AccessTools.TypeByName("CombatExtended.SaveLoadoutDialog");
        private static readonly Type? LoadDialogType = AccessTools.TypeByName("CombatExtended.LoadLoadoutDialog");

        private static readonly MethodInfo? ToConfig = CeLoadouts.LoadoutType == null ? null : AccessTools.Method(CeLoadouts.LoadoutType, "ToConfig");
        private static readonly MethodInfo? FromConfig = CeLoadouts.LoadoutType == null ? null : AccessTools.Method(CeLoadouts.LoadoutType, "FromConfig");
        private static readonly MethodInfo? AddLoadout = CeLoadouts.DialogType == null ? null : AccessTools.Method(CeLoadouts.DialogType, "AddLoadoutExpose");
        private static readonly FieldInfo? ConfigLabel = ConfigType == null ? null : AccessTools.Field(ConfigType, "label");
        private static readonly FieldInfo? ConfigParentLabel = ConfigType == null ? null : AccessTools.Field(ConfigType, "parentLabel");

        public static bool Available => ConfigType != null && FileListType != null && SaveDialogType != null && LoadDialogType != null
            && ToConfig != null && FromConfig != null && AddLoadout != null && ConfigLabel != null && ConfigParentLabel != null;

        public static void SaveAll()
        {
            Open(SaveDialogType!, nameof(Write), DefaultFileName);
        }

        public static void LoadAll()
        {
            Open(LoadDialogType!, nameof(Read));
        }

        // CE's file dialogs are internal: they're built by reflection with a delegate for the file action, and every one lists the loadoutset folder.
        private static void Open(Type dialogType, string handler, params object[] extra)
        {
            Type action = typeof(Action<,>).MakeGenericType(typeof(FileInfo), FileListType!);
            Delegate fileAction = Delegate.CreateDelegate(action, AccessTools.Method(typeof(LoadoutSets), handler));
            object[] args = new object[] { Folder, fileAction }.Concat(extra).ToArray();
            object dialog = Activator.CreateInstance(dialogType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)!;
            Find.WindowStack.Add((Window)dialog);
        }

        private static void Write(FileInfo file, Window dialog)
        {
            List<object> loadouts = CeLoadouts.Loadouts();
            try
            {
                XmlSerializer serializer = new(ConfigType!);
                XmlDocument document = new();
                XmlElement root = document.CreateElement("LoadoutSet");
                document.AppendChild(root);
                document.InsertBefore(document.CreateXmlDeclaration("1.0", "utf-8", null), root);
                foreach (object loadout in loadouts)
                {
                    using StringWriter writer = new();
                    serializer.Serialize(writer, ToConfig!.Invoke(loadout, null));
                    XmlDocument part = new();
                    part.LoadXml(writer.ToString());
                    root.AppendChild(document.ImportNode(part.DocumentElement!, true));
                }
                document.Save(file.FullName);
            }
            catch (Exception exception)
            {
                Log.Error($"[Better CE Loadouts] Could not save the loadout set '{file.FullName}': {exception}");
                Messages.Message("BetterCELoadouts_SaveFailed".Translate(Path.GetFileNameWithoutExtension(file.Name)), MessageTypeDefOf.RejectInput, false);
                return;
            }
            dialog.Close();
            Messages.Message("BetterCELoadouts_SavedAll".Translate(loadouts.Count, Path.GetFileNameWithoutExtension(file.Name)), MessageTypeDefOf.TaskCompletion, false);
        }

        private static void Read(FileInfo file, Window dialog)
        {
            List<object> configs;
            try
            {
                configs = ReadConfigs(file);
            }
            catch (Exception exception)
            {
                Log.Error($"[Better CE Loadouts] Could not read the loadout set '{file.FullName}': {exception}");
                Messages.Message("BetterCELoadouts_LoadFailed".Translate(file.Name), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<string> missing = new();
            int added = AddAll(configs, missing, file);
            dialog.Close();
            string name = Path.GetFileNameWithoutExtension(file.Name);
            Messages.Message("BetterCELoadouts_LoadedAll".Translate(added, name), MessageTypeDefOf.TaskCompletion, false);
            if (added < configs.Count)
            {
                Messages.Message("BetterCELoadouts_LoadSkipped".Translate(configs.Count - added, name), MessageTypeDefOf.RejectInput, false);
            }
            if (missing.Count > 0)
            {
                Messages.Message("CE_MissingLoadoutSlots".Translate(string.Join(", ", missing.Distinct())), null, MessageTypeDefOf.RejectInput);
            }
        }

        private static List<object> ReadConfigs(FileInfo file)
        {
            XmlSerializer serializer = new(ConfigType!);
            XmlDocument document = new();
            document.Load(file.FullName);
            if (document.DocumentElement?.Name != "LoadoutSet")
            {
                throw new InvalidDataException("The file isn't a loadout set.");
            }
            List<object> configs = new();
            foreach (XmlElement element in document.DocumentElement.ChildNodes.OfType<XmlElement>())
            {
                configs.Add(serializer.Deserialize(new XmlNodeReader(element))!);
            }
            return configs;
        }

        // Adds each loadout from its config, parents first, one at a time so CE renames any whose name is taken. Children are pointed at the
        // copies added here: CE links a parent by name, which would pick an older loadout of the same name when the loaded one was renamed.
        // A loadout CE can't build is skipped and logged. Returns how many were added.
        private static int AddAll(List<object> configs, List<string> missing, FileInfo file)
        {
            Dictionary<string, object> byLabel = new();
            foreach (object config in configs)
            {
                string label = (string)ConfigLabel!.GetValue(config)!;
                if (!byLabel.ContainsKey(label))
                {
                    byLabel[label] = config;
                }
            }

            List<object> ordered = new();
            HashSet<object> visited = new();
            void Visit(object config)
            {
                if (!visited.Add(config))
                {
                    return;
                }
                string parentLabel = (string?)ConfigParentLabel!.GetValue(config) ?? "";
                if (parentLabel.Length > 0 && byLabel.TryGetValue(parentLabel, out object? parent))
                {
                    Visit(parent);
                }
                ordered.Add(config);
            }
            configs.ForEach(Visit);

            Dictionary<object, object> added = new();
            foreach (object config in ordered)
            {
                object loadout;
                try
                {
                    object?[] args = { config, null };
                    loadout = FromConfig!.Invoke(null, args)!;
                    missing.AddRange((List<string>)args[1]!);
                }
                catch (Exception exception)
                {
                    Log.Error($"[Better CE Loadouts] Skipped loadout '{ConfigLabel!.GetValue(config)}' in '{file.FullName}': {exception.InnerException ?? exception}");
                    continue;
                }

                string parentLabel = (string?)ConfigParentLabel!.GetValue(config) ?? "";
                if (parentLabel.Length > 0 && byLabel.TryGetValue(parentLabel, out object? parentConfig) && added.TryGetValue(parentConfig, out object? parent))
                {
                    CeLoadouts.SetParent(loadout, parent);
                }
                AddLoadout!.Invoke(null, new[] { loadout });
                added[config] = loadout;
            }
            return added.Count;
        }
    }
}
