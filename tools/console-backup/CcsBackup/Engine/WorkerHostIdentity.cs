// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The controlled worker's early identity and Back Up Console's backup name (WorkerHost.cs has the rest of the worker).
//   {"event":"console","console_tag":…,"console_key":…,"physical_model":…,"model":…}
//             sent once this session has checked the console's identity (its hardware ID, model and storage layout), before anything is
//             read from its storage (a backup, a task's backup made first, a check-up). console_tag is the short label the window shows;
//             console_key the full key of its hardware ID (ConsoleNameBook.KeyOf), for the window's friendly names only. Starting the
//             console's memory program is not an identification, and an identity that was refused is never sent.
//   {"event":"backup-name","console_tag":…,"console_key":…,"physical_model":…,"usual":…}
//             Back Up Console asked for a name ({"cmd":"backup","name":"ask",…}): once the console is identified and before anything is
//             read, the worker waits (at most ten minutes, in the same session) for {"cmd":"backup-name","name":…} (empty or missing: the
//             usual name), or "cancel" / "quit" / the window ending, which stop the backup before anything is read.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;

namespace CcsBackup.Engine
{
    public static partial class WorkerHost
    {
        /// <summary>the identity event for a checked identity</summary>
        public static JObj IdentityEvent(ConsoleIdentity id) => new JObj
        {
            { "event", "console" }, { "console_tag", id.ShortTag }, { "console_key", ConsoleNameBook.KeyOf(id.Hwid) },
            { "physical_model", id.Model?.Id }, { "model", id.Model?.ShortName },
        };

        sealed partial class Session
        {
            /// <summary>sends this session's checked console identity to the window</summary>
            void SendIdentity(ConsoleIdentity id) { if (id != null && !string.IsNullOrEmpty(id.Hwid)) Send(IdentityEvent(id)); }

            /// <summary>Back Up Console's backup name: asks the window, then waits for its answer in this same session</summary>
            BackupNameAnswer AskBackupName(ConsoleIdentity id, string usual)
            {
                var ev = IdentityEvent(id); ev.Set("event", "backup-name"); ev.Add("usual", usual);
                Send(ev);
                var c = Next(DateTime.UtcNow + RestoreEngine.ConfirmWindow, "backup-name");
                if (c == null || c.S("cmd") != "backup-name")
                    return new BackupNameAnswer { Stop = cancel || (c != null && c.S("cmd") == "quit") ? "Canceled before anything was read. Nothing on the console changed, and no backup file was made."
                                                                                                         : "The backup did not start: no name was chosen within 10 minutes. Nothing was read, and no backup file was made." };
                var name = c.S("name");
                return new BackupNameAnswer { Name = string.IsNullOrWhiteSpace(name) || BackupNames.Clean(name).Length == 0 ? null : name };
            }
        }
    }
}
