// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Earlier CCS HOME versions kept on the console (Settings > CCS HOME > Earlier HOME versions): what the console reported in this session
// of each copy (/var/lib/ccs/pkg/<copy>: its record install/v-<copy>.json, whether every file is exactly what the record names and
// nothing else is there, its collection name, games, logical size, the space it takes, when it was installed and whether an entry of
// Original Menu names its files), read just before the review together with the
// backup made in that session, and which copies go once the new one is in use and read back, by the shared retention rule (StorageSpec.Retention, the same as the Studio installer's).
// Each removal is its own step (ccsprep hdrop COPY CURRENT), which checks the copy against its record again on the console and refuses a
// copy that differs or that an entry of Original Menu names; a failure stops the steps that follow. Nothing here writes.
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsBackup.Engine
{
    public sealed class EarlierCopy
    {
        public string Copy, Label, Previous, Installed, Named = "no"; public long? Games, Bytes, Allocated; public bool Owned, Unchanged, Folder;
        /// <summary>an entry of Original Menu names it, or may (entries on a USB drive or SD card): never removed</summary>
        public bool Referenced => Named != "no";
        public JObj ToJson() => new JObj { { "copy", Copy }, { "label", Label }, { "games", Games }, { "bytes", Bytes }, { "allocated_bytes", Allocated }, { "owned", Owned }, { "unchanged", Unchanged },
            { "previous", Previous }, { "installed_utc", Installed }, { "named", Named } };
    }

    public sealed class EarlierPlan
    {
        public int Keep; public string Current, Prior; public List<EarlierCopy> Copies = new List<EarlierCopy>(); public JObj Retention;
        public List<string> Remove => ((List<object>)Retention["remove"]).Cast<string>().ToList();
        public string Why(string copy) => (Retention["why"] as JObj)?[copy] as string;
        public long RemovedAllocated => Copies.Where(c => Remove.Contains(c.Copy)).Sum(c => c.Allocated ?? 0);
        public JObj ToJson() => new JObj { { "keep", (long)Keep }, { "current", Current }, { "prior", Prior }, { "copies", Copies.Select(c => (object)c.ToJson()).ToList() }, { "retention", Retention } };
    }

    public static class EarlierHome
    {
        public const string RestoreText = "The backup made before this change keeps every removed earlier version: Console Restore can put its files back on this console.";

        /// <summary>CCS HOME's copies as the console reported them in this session (ccsprep mstate: each recorded copy, whether its folder
        /// is there and exactly what its record names, the copy in use before it, its size and the space it takes, its name and games)</summary>
        public static List<EarlierCopy> FromState(MethodState st)
        {
            var list = new List<EarlierCopy>();
            foreach (var kv in st.HomeCopies)
            {
                st.HomeInfo.TryGetValue(kv.Key, out var i);
                list.Add(new EarlierCopy
                {
                    Copy = kv.Key, Owned = kv.Value.dir, Folder = kv.Value.dir, Unchanged = i.state == "exact", Previous = i.previous,
                    Bytes = i.bytes >= 0 ? (long?)i.bytes : null, Allocated = i.kb >= 0 ? (long?)(i.kb * 1024) : null, Label = i.label, Games = i.games >= 0 ? (long?)i.games : null,
                    Installed = i.installed, Named = i.named ?? "unknown",
                });
            }
            return list;
        }

        /// <summary>the copies after the switch: current (the new copy, in use), prior (the copy in use before; null: none), keep (the
        /// setting); the decision is the shared retention rule's</summary>
        public static EarlierPlan Decide(List<EarlierCopy> copies, string current, string prior, int keep)
        {
            var p = new EarlierPlan { Keep = StorageSpec.EarlierKeep((long)keep), Current = current, Prior = prior, Copies = copies.ToList() };
            var items = copies.Select(c => (object)new Dictionary<string, object> { ["copy"] = c.Copy, ["owned"] = c.Owned, ["unchanged"] = c.Unchanged, ["referenced"] = c.Referenced, ["previous"] = c.Previous }).ToList();
            if (!copies.Any(c => c.Copy == current)) items.Add(new Dictionary<string, object> { ["copy"] = current, ["owned"] = true, ["unchanged"] = true });
            p.Retention = StorageSpec.Retention(new Dictionary<string, object> { ["keep"] = (long)p.Keep, ["current"] = current, ["prior"] = prior, ["copies"] = items.ToArray() });
            return p;
        }
    }
}
