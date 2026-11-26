// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The worker's Community Mods request (one console session):
//   {"cmd":"community","console":"real"|"simulated","folder":…}   the console's mods read and the PC's ledger reconciled (nothing written):
//        event {"event":"community-state",…}; then the window sends
//   {"cmd":"community-change","install":[ID…],"update":[ID…],"remove":[ID…],"restore_settings":[ID…],"remove_settings":true|false,
//        "files":{ID: LOCAL APPROVED FILE…},"storage_backup":USB AND SD BACKUP FILE (a mod that writes a USB drive or SD card)}
//        → event {"event":"community-plan","plan":{…}} (after the protective backup), then
//   {"cmd":"community-confirm","digest":…,"nonce":…,"storage_confirmed":true|false}   → {"event":"result","operation":"community",…}; or
//        "quit"/"end" at any point. storage_confirmed: the person confirmed the USB and SD backup is of the storage the console uses.
// The catalog is the one this build carries. A simulated console may name its own catalog file ("catalog": the tests' and the private
// development fixtures); a real console never does.
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CcsBackup.Engine
{
    public static partial class WorkerHost
    {
        sealed partial class Session
        {
            static (CommunityCatalog cat, string problem) CatalogFor(Dictionary<string, object> req, RecoveryConsole con)
            {
                var file = req.S("catalog");
                if (file == null) return CommunityCatalog.Shipping();
                if (!con.Simulated) return (null, "A real console uses only the community catalog this copy of Workbench carries.");
                try { return (CommunityCatalog.Parse(File.ReadAllBytes(file)), null); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CommunityCatalogException) { return (null, "The catalog file cannot be used (" + ex.Message + ")."); }
            }

            static List<string> Ids(Dictionary<string, object> c, string k) => (c.A(k) ?? new List<object>()).OfType<string>().Where(x => x.Length <= 48).Distinct().Take(32).ToList();

            int Community(RecoveryConsole con, EngineContext ctx, Dictionary<string, object> req)
            {
                JObj Result(string state, string problem) => new JObj { { "event", "result" }, { "operation", "community" }, { "state", state }, { "problem", problem } };
                var (cat, cp) = CatalogFor(req, con);
                if (cat == null) { Send(Result("refused", cp)); AfterOperation(con); return 1; }
                var rd = CommunityEngine.Read(con, ctx, cat);
                Send(rd.ToJson(cat));
                if (rd.Refusal != null) { Send(Result("refused", rd.Refusal)); AfterOperation(con); return 1; }
                var c = Next(DateTime.UtcNow.AddHours(1), "community-change", "end");
                if (c == null || c.S("cmd") != "community-change") { Send(Result("checked", null).Set("ledger", rd.ToJson(cat)["ledger"])); AfterOperation(con); return 0; }
                var ch = new CommunityChange { Install = Ids(c, "install"), Update = Ids(c, "update"), Remove = Ids(c, "remove"), RestoreSettings = Ids(c, "restore_settings"), RemoveSettings = c.B("remove_settings") };
                foreach (var kv in c.O("files") ?? new Dictionary<string, object>()) if (kv.Value is string f && f.Length < 1000) ch.Files[kv.Key] = f;
                var sb = c.S("storage_backup"); if (sb != null && sb.Length < 1000) ch.StorageBackup = sb;
                var (plan, refused) = CommunityEngine.Prepare(con, ctx, cat, ch);
                if (refused != null) { Send(Result(refused.State, refused.Problem)); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "community-plan" }, { "plan", plan.ToJson() } });
                Dictionary<string, object> k = null;
                while (!cancel && DateTime.UtcNow < plan.ExpiresUtc) { if (queue.TryTake(out k, 500)) { if (k == null || k.S("cmd") == "community-confirm" || k.S("cmd") == "quit") break; } }
                if (k == null || k.S("cmd") != "community-confirm") { Send(Result("canceled", "Nothing was changed.").Set("protective", plan.ProtectivePath)); AfterOperation(con); return 0; }
                var o = CommunityEngine.Execute(con, plan, k.S("digest"), k.S("nonce"), ctx, k.B("storage_confirmed"));
                Send(Result(o.State, o.Problem).Set("done", o.Done).Set("record", o.RecordFile).Set("protective", plan.ProtectivePath).Set("storage_backup", plan.Storage?.Path).Set("recovery_set", o.RecoverySetRecord)
                    .Set("applies", o.Ok ? "at the console's next normal start" : null).Set("ui_gone", uiGone));
                AfterOperation(con); return o.Ok ? 0 : 1;
            }
        }
    }
}
