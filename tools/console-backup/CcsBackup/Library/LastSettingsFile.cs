// CCS Console Workbench: which settings file the standalone window used last. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
using System;
using System.IO;

namespace CcsBackup.Library
{
    /// <summary>The standalone Workbench's one remembered thing: the settings file the person last saved or loaded successfully, so the window
    /// opens it again next time. Only its path, never a copy of the settings and never a list of files:
    ///   %LOCALAPPDATA%\CCS Console Workbench\last-settings-file.json
    ///   {"schema": "ccs-workbench-last-settings-file/1", "path": "&lt;the settings file's full path&gt;"}
    /// Written only after a successful Save Settings, Save Settings As or Load Settings from a File in the standalone window (never by
    /// Classic Cart Studio's Workbench, the controlled worker, the scheduled check or any other mode), and read only when that window opens
    /// without --settings. A remembered file that is missing or cannot be used is never replaced by itself: only the person's next successful
    /// choice replaces it. CCS_WORKBENCH_STATE (a folder) stands in for the AppData folder in tests.</summary>
    public static class LastSettingsFile
    {
        public const string Schema = "ccs-workbench-last-settings-file/1";
        public static string RecordPath => Path.Combine(Environment.GetEnvironmentVariable("CCS_WORKBENCH_STATE") is string d && d.Length > 0 ? d
                                                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCS Console Workbench"), "last-settings-file.json");

        /// <summary>the remembered settings file (a full path), or null when there is none (no record, or a record this version cannot read:
        /// left as it is)</summary>
        public static string Read()
        {
            try
            {
                if (!File.Exists(RecordPath)) return null;
                var d = Json.Parse(File.ReadAllText(RecordPath));
                var p = d.S("schema") == Schema ? d.S("path") : null;
                return !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p) ? p : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>remembers this settings file (its full path) for the next standalone start</summary>
        public static void Write(string settingsFile)
        {
            var j = new JObj { { "schema", Schema }, { "path", Path.GetFullPath(settingsFile) } };
            Durable.WriteAtomic(RecordPath, Json.Write(j));
        }

        /// <summary>why this file cannot be opened as Workbench settings now, in plain words; null when it can</summary>
        public static string Problem(string settingsFile)
        {
            try
            {
                if (!File.Exists(settingsFile)) return "it is not there now (it may have been moved, renamed or deleted, or its drive may not be connected)";
                if (Json.Parse(File.ReadAllText(settingsFile)).S("schema") != "ccs-console-backup-settings/1") return "it is not a Workbench settings file";
                return null;
            }
            catch (Exception ex) { return "it could not be read (" + ex.Message.TrimEnd('.') + ")"; }
        }
    }
}
