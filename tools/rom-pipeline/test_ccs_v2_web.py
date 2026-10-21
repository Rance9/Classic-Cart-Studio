r"""Complete .ccs packages on the website (ccs-package/2, tools/companion/CCS-FORMAT.md) in headless Edge, with synthetic ROMs that carry a
sentinel byte pattern (no real games). Every request the page makes is recorded (fetch, XMLHttpRequest, sendBeacon, WebSocket wrappers and
the browser's resource timing), and no request may leave 127.0.0.1 or carry ROM bytes.
  complete      a collection's ROM files chosen as files -> the dialog says all are ready -> the package lists every game with a verified ROM
  missing       a game without its ROM blocks Export and Share; "Share Only the N Ready Games" packages exactly that subset (loadouts follow)
  identity      a ROM of a different version under a game's id, a package missing a ROM, and an old ROM-free (ccs-package/1) package are refused
  import        the website verifies every ROM, saves the collection and can share it again without choosing files
  flows         Export .ccs (save), Share Games / Send to Phone (share sheet, a refusing app, a browser without file sharing): always the complete file
  size          3,000 games; ZIP64 records (forced) read back; a > 4 GB archive (Blob references, no memory copy) parsed with 64-bit offsets
  retired       public share links and ROM-free exports are gone; Studio data backups are JSON files, not .ccs
  python test_ccs_v2_web.py <built site folder> [--out DIR]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, json, os, sys, tempfile, time
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from test_loadouts_web import EdgeSession, serve, READY, CALM

NET = r"""(()=>{ if(window.__net) return 1; window.__net=[]; const S='CCS-SENTINEL-ROM-BYTES';
 const scan=async b=>{ try{ if(!b) return false; let u8=null; if(typeof b==='string') return b.includes(S); if(b instanceof Blob) u8=new Uint8Array(await b.arrayBuffer()); else if(b instanceof ArrayBuffer) u8=new Uint8Array(b); else if(ArrayBuffer.isView(b)) u8=new Uint8Array(b.buffer,b.byteOffset,b.byteLength); else if(b instanceof FormData){ for(const [,v] of b) if(await scan(v)) return true; return false; }
   if(!u8) return false; return new TextDecoder('latin1').decode(u8).includes(S); }catch(e){ return false; } };
 const rec=(kind,url,body)=>{ const e={kind,url:String(url),bytes:body&&(body.size||body.byteLength||body.length)||0,sentinel:false}; window.__net.push(e); scan(body).then(x=>{ e.sentinel=x; }); };
 const f=window.fetch; window.fetch=function(u,o){ rec('fetch', u&&u.url||u, o&&o.body); return f.apply(this, arguments); };
 const so=XMLHttpRequest.prototype.open, ss=XMLHttpRequest.prototype.send; XMLHttpRequest.prototype.open=function(m,u){ this.__u=u; return so.apply(this, arguments); }; XMLHttpRequest.prototype.send=function(b){ rec('xhr', this.__u, b); return ss.apply(this, arguments); };
 if(navigator.sendBeacon){ const sb=navigator.sendBeacon.bind(navigator); navigator.sendBeacon=(u,b)=>{ rec('beacon',u,b); return sb(u,b); }; }
 const WS=window.WebSocket; window.WebSocket=function(u,p){ rec('websocket',u,null); return new WS(u,p); };
 return 1; })()"""
# synthetic ROMs: iNES-headed or headerless, 16 KB of content with the sentinel and a unique tail; registered as personal games (p_ + content sha16)
ROMS = r"""async function mkRom(i, headered, n){ n=n||16384; const c=new Uint8Array(n); const s=new TextEncoder().encode('CCS-SENTINEL-ROM-BYTES');
  for(let k=0;k<n;k++) c[k]=s[k%s.length]; const tag=new TextEncoder().encode('#'+i+'#'+Math.random()); c.set(tag, n-tag.length);
  const u8=headered?(()=>{ const o=new Uint8Array(16+n); o.set([0x4E,0x45,0x53,0x1A,1,1,0,0]); o.set(c,16); return o; })():c;
  const sha=await ccsSha(c); const id='p_'+sha.slice(0,16); const title='Sentinel Game '+i;
  const e={id, content_sha256:sha, file_sha256:await ccsSha(u8), content_size:n, file_size:u8.length, format:headered?'nes':'unh', file_name:title+(headered?'.nes':'.unh'), origin:'file', user:{title, year:'2024', publisher:'Test'}, detected:{title}, file_present:true};
  PERSONAL[id]=e; byId[id]=perRow(e); return {id, title, file:new File([u8], title+(headered?'.nes':'.unh'))}; }
 window.mkRom=mkRom; 1"""

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "ccs-v2-web")); a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True); checks = []
    def ok(k, v, n=""): checks.append((k, bool(v))); print(f"{k}={'PASS' if v else 'FAIL'}" + (f" ({str(n)[:300]})" if n and not v else ""), flush=True)
    web = serve(a.site); URL = f"http://127.0.0.1:{web.server_address[1]}/index.html"
    with EdgeSession(width=1440, height=900, profile=tempfile.mkdtemp(prefix="ccs-v2-"), call_timeout=600) as ed:
        J = lambda e: json.loads(ed.js(e))
        ed.navigate(URL.replace("index.html", "robots.txt")); ed.js("localStorage.clear(); 1"); ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(1); ed.js(CALM); ed.js(NET); ed.js(ROMS)
        # ---- a collection of five synthetic games (three headered, two headerless) with a loadout of three ----
        J(r"""(async()=>{ window.R=[]; for(let i=0;i<5;i++) R.push(await mkRom(i, i<3)); sel.clear(); R.forEach(x=>sel.add(x.id)); COLL.name='Sentinel Collection'; saveColl(); persist(); render(); csSaveActiveNow();
   const r=csActive(); r.loadouts=[{id:loNewId(), target:'nes-classic', name:'Three', created:csNow(), modified:csNow(), sel:R.slice(0,3).map(x=>x.id), decisions:{}, home:null}]; csWrite(); return JSON.stringify(1); })()""")
        st = J("""(async()=>{ ccsShareDialog(); await new Promise(r=>setTimeout(r,800)); return JSON.stringify({missing:CCS.missing.length, ready:CCS.ready.length, exp:$('ccsExp').disabled, send:$('ccsSend').disabled, label:$('ccsSend').textContent, pick:!!$('ccsPick'), text:$('collBody').innerText}); })()""")
        ok("missing_roms_block_export_and_share", st["missing"] == 5 and st["exp"] is True and st["send"] is True and "5 of 5 games need their ROM file" in st["text"] and st["pick"], st)
        ok("desktop_label_send_to_phone", st["label"] == "Send to Phone", st["label"])
        st = J("""(async()=>{ await ccsAddRomSources(R.map(x=>x.file)); await ccsCheck(); return JSON.stringify({missing:CCS.missing.length, ready:CCS.ready.length, exp:$('ccsExp').disabled, text:$('collBody').innerText}); })()""")
        ok("chosen_rom_files_make_every_game_ready", st["missing"] == 0 and st["ready"] == 5 and st["exp"] is False and "All 5 ROM files are ready" in st["text"], st)
        pk = J("""(async()=>{ const b=await ccsBuild(CCS.parts); window.PKG=b; const i=await ccsInspect(b); const ids=(i.data.ccs_collection||{}).sel||[];
   return JSON.stringify({ok:i.ok, schema:i.manifest.schema, kind:i.manifest.kind, roms:i.roms.length, verified:i.roms.every(r=>r.verified), inc:(i.manifest.roms.included||[]).length, missingKey:'missing' in (i.manifest.roms||{}), games:ids.length, everyHasRom:ids.every(id=>i.roms.some(r=>r.version_id===id)), loadout:(i.data.ccs_collection.loadouts||[]).map(l=>l.sel.length), size:b.size, problems:i.problems}); })()""")
        ok("complete_package_every_game_has_a_verified_rom", pk["ok"] and pk["schema"] == "ccs-package/2" and pk["kind"] == "collection" and pk["roms"] == 5 and pk["verified"] and pk["inc"] == 5 and pk["games"] == 5 and pk["everyHasRom"] and not pk["missingKey"], pk)
        ok("package_keeps_loadouts", pk["loadout"] == [3], pk["loadout"])
        open(os.path.join(a.out, "web-complete.ccs"), "wb").write(base64.b64decode(ed.js("(async()=>{ const u8=new Uint8Array(await PKG.arrayBuffer()); let s=''; for(let i=0;i<u8.length;i+=0x8000) s+=String.fromCharCode.apply(null,u8.subarray(i,i+0x8000)); return btoa(s); })()")))
        # ---- missing ROM and an explicit smaller selection ----
        st = J("""(async()=>{ ROMSRC.clear(); await ccsAddRomSources(R.slice(0,4).map(x=>x.file)); await ccsCheck(); const before={missing:CCS.missing.length, exp:$('ccsExp').disabled, only:!!$('ccsOnly')};
   $('ccsOnly').click(); await new Promise(r=>setTimeout(r,800)); const rec=ccsSubsetRecord(CS.items[CCS.cid], CCS.subset); const parts=await ccsCollectionParts(rec); const b=await ccsBuild(parts); const i=await ccsInspect(b);
   return JSON.stringify({before, ids:CCS.ids.length, ready:CCS.ready.length, exp:$('ccsExp').disabled, ok:i.ok, games:(i.data.ccs_collection.sel||[]).length, roms:i.roms.length, lo:(i.data.ccs_collection.loadouts||[]).map(l=>l.sel.length), name:ccsName(CS.items[CCS.cid], CCS.subset.length)}); })()""")
        ok("one_missing_rom_blocks_the_whole_package", st["before"]["missing"] == 1 and st["before"]["exp"] is True and st["before"]["only"], st["before"])
        ok("explicit_subset_packages_exactly_the_ready_games", st["ids"] == 4 and st["ready"] == 4 and st["exp"] is False and st["ok"] and st["games"] == 4 and st["roms"] == 4 and st["lo"] == [3] and "(4 games)" in st["name"], st)
        # ---- identity: wrong revision, missing ROM in a package, old ROM-free package ----
        st = J(r"""(async()=>{ const id=R[0].id, other=new Uint8Array(await R[1].file.arrayBuffer()); const parts=await ccsCollectionParts(CS.items[CCS.cid]); ROMSRC.clear(); await ccsAddRomSources(R.map(x=>x.file));
   const s1=ROMSRC.get(R[1].id); ROMSRC.set(id, s1); let wrong; try{ wrong=await ccsInspect(await ccsBuild(parts)); }catch(e){ wrong={ok:false, problems:[{problem:e.message}]}; }
   ROMSRC.clear(); await ccsAddRomSources(R.map(x=>x.file)); ROMSRC.delete(R[4].id); let nobuild=''; try{ await ccsBuild(parts); }catch(e){ nobuild=e.message; }
   const man={format:CCS_FORMAT, schema:'ccs-package/1', kind:'collection', files:[]}; const old=ccsZipBlob([(await ccsJsonEntry(CCS_MANIFEST, JSON.stringify(man))).entry]); let olderr=''; try{ await ccsInspect(old); }catch(e){ olderr=e.message; }
   return JSON.stringify({wrong:wrong.ok, wrongWhy:(wrong.problems||[]).map(p=>p.problem).join('; '), nobuild, olderr}); })()""")
        ok("rom_of_a_different_version_is_refused", st["wrong"] is False and "different version" in st["wrongWhy"], st)
        ok("package_cannot_be_built_without_every_rom", "missing ROM" in st["nobuild"], st["nobuild"])
        ok("old_rom_free_package_refused_plainly", "does not include its ROM files" in st["olderr"], st["olderr"])
        st = J(r"""(async()=>{ const z=await ccsUnzip(PKG); const keep=z.entries.filter(e=>!e.name.startsWith('roms/'+R[2].id)); const entries=[]; for(const e of keep){ entries.push({name:e.name, method:e.method, crc:e.crc, usize:e.usize, csize:e.csize, body:await z.raw(e)}); }
   const man=JSON.parse(new TextDecoder().decode(await z.read(z.entries.find(e=>e.name===CCS_MANIFEST), 1e9))); man.files=man.files.filter(f=>!f.path.startsWith('roms/'+R[2].id)); man.roms.included=man.roms.included.filter(f=>f.version_id!==R[2].id);
   const mi=entries.findIndex(e=>e.name===CCS_MANIFEST); entries[mi]=(await ccsJsonEntry(CCS_MANIFEST, JSON.stringify(man))).entry; const b=ccsZipBlob(entries); const i=await ccsInspect(b);
   ccsOpenFile(new File([b],'Incomplete.ccs')); await new Promise(r=>setTimeout(r,1500)); return JSON.stringify({ok:i.ok, why:i.problems.map(p=>p.problem).join('; '), dialog:$('collBody').innerText, save:!!$('ccsGo')}); })()""")
        ok("incomplete_package_refused_and_not_importable", st["ok"] is False and "no ROM file" in st["why"] and "cannot be opened" in st["dialog"] and not st["save"], st)
        # ---- import on the website, then share again without choosing files ----
        st = J(r"""(async()=>{ ROMSRC.clear(); closeColl(); const n0=csList().length; ccsOpenFile(new File([PKG],'Sentinel Collection.ccs')); for(let i=0;i<40&&!$('ccsGo');i++) await new Promise(r=>setTimeout(r,250));
   const text=$('collBody').innerText; $('ccsGo').click(); await new Promise(r=>setTimeout(r,800)); const n1=csList().length; const rec=csList().find(r=>r.name.startsWith('Sentinel Collection')&&r.id!==CCS.cid);
   const ready=R.every(x=>ROMSRC.has(x.id)); closeColl(); return JSON.stringify({text, added:n1-n0, ready, games:rec?(rec.sel||[]).length:0}); })()""")
        ok("website_import_verifies_every_rom", "5 verified" in st["text"] and "does not keep them" in st["text"], st["text"][:300])
        ok("website_import_saves_collection_and_can_share_again", st["added"] == 1 and st["games"] == 5 and st["ready"], st)
        # ---- the flows: Export .ccs, Send to Phone with a share sheet, a refusing app, no file sharing ----
        flow = r"""(async(mode, share)=>{ window.__saved=null; window.__shared=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, blob:b}; return {ok:true, name:n}; };
   if(share==='none'){ try{ delete navigator.canShare; }catch(e){} Object.defineProperty(navigator,'canShare',{value:undefined, configurable:true}); }
   else { Object.defineProperty(navigator,'canShare',{value:()=>true, configurable:true}); Object.defineProperty(navigator,'share',{value:async o=>{ if(share==='refuse'){ const e=new Error('too large'); e.name='NotAllowedError'; throw e; } window.__shared=o.files[0]; }, configurable:true}); }
   ccsShareDialog(csList().find(r=>r.name==='Sentinel Collection').id); await new Promise(r=>setTimeout(r,900)); (mode==='export'?$('ccsExp'):$('ccsSend')).click(); await new Promise(r=>setTimeout(r,300));
   const ack=$('ccsAck'); const ackTitle=ack&&ack.querySelector('h2').textContent; $('ccsAckC').click(); [...ack.querySelectorAll('button')].find(b=>b.textContent==='Export with ROMs').click();
   for(let i=0;i<60&&!(window.__saved||$('ccsSave')||$('ccsShare'));i++) await new Promise(r=>setTimeout(r,250));
   const panel=$('collm').classList.contains('open')?$('collBody').innerText:''; if(mode!=='export'){ if($('ccsShare')) { $('ccsShare').click(); await new Promise(r=>setTimeout(r,500)); } }
   const got=window.__saved?window.__saved.blob:window.__shared; const i=got?await ccsInspect(got):null;
   return JSON.stringify({ackTitle, panel, after:$('collm').classList.contains('open')?$('collBody').innerText:'', saved:!!window.__saved, shared:!!window.__shared, savedName:window.__saved&&window.__saved.name, ok:i&&i.ok, roms:i?i.roms.length:0, shareBtn:!!$('ccsShare'), saveBtn:!!$('ccsSave')}); })"""
        ed.js("ROMSRC.clear(); ccsAddRomSources(R.map(x=>x.file)).then(()=>1)"); time.sleep(1)
        st = J(f"({flow})('export','sheet')"); ok("export_ccs_saves_the_complete_package_after_the_acknowledgment", st["ackTitle"] == "Export Collection with ROM Files" and st["saved"] and st["ok"] and st["roms"] == 5 and st["savedName"].endswith(".ccs"), st)
        st = J(f"({flow})('share','sheet')"); ok("send_to_phone_hands_the_complete_package_to_the_share_sheet", st["shared"] and st["ok"] and st["roms"] == 5 and "The package is ready" in st["panel"], st)
        st = J(f"({flow})('share','refuse')"); ok("a_refusing_app_keeps_the_package_and_offers_save", "didn't take the file" in st["after"] and st["saveBtn"] and st["shareBtn"], st)
        st = J(f"({flow})('share','none')"); ok("no_file_sharing_offers_save_instead", "can't hand files to other apps" in st["panel"] and st["saveBtn"] and not st["shareBtn"], st)
        ed.js("closeColl(); 1")
        # ---- size: thousands of games, forced ZIP64, and > 4 GB archive structure ----
        t0 = time.time()
        st = J(r"""(async()=>{ const N=3000; const ids=[], files=[]; for(let i=0;i<N;i++){ const x=await mkRom('big'+i, i%2===0, 8192); ids.push(x.id); files.push(x.file); }
   ROMSRC.clear(); await ccsAddRomSources(files); const r=csCreate('Three Thousand', {sel:ids, decisions:{}, coll:{}, home:null, loadouts:[]}); const parts=await ccsCollectionParts(r); const b=await ccsBuild(parts); const i=await ccsInspect(b);
   return JSON.stringify({games:ids.length, ok:i.ok, roms:i.roms.length, size:b.size}); })()""")
        ok("three_thousand_games_package_and_verify", st["ok"] and st["roms"] == 3000, st); print(f"   3000 games: {st['size']:,} bytes in {time.time()-t0:.1f}s")
        st = J(r"""(async()=>{ window.CCS_FORCE_ZIP64=true; const parts=await ccsCollectionParts(csList().find(r=>r.name==='Sentinel Collection')); ROMSRC.clear(); await ccsAddRomSources(R.map(x=>x.file)); const b=await ccsBuild(parts); window.CCS_FORCE_ZIP64=false; window.PKG64=b; const i=await ccsInspect(b); return JSON.stringify({ok:i.ok, roms:i.roms.length}); })()""")
        ok("forced_zip64_package_reads_back", st["ok"] and st["roms"] == 5, st)
        open(os.path.join(a.out, "web-zip64.ccs"), "wb").write(base64.b64decode(ed.js("(async()=>{ const u8=new Uint8Array(await PKG64.arrayBuffer()); let s=''; for(let i=0;i<u8.length;i+=0x8000) s+=String.fromCharCode.apply(null,u8.subarray(i,i+0x8000)); return btoa(s); })()")))
        st = J(r"""(async()=>{ const part=new Uint8Array(64<<20); for(let i=0;i<part.length;i+=4096) part[i]=i>>12&255; const blob=new Blob([part]); const crc=ccsCrc32(part); const entries=[];
   for(let k=0;k<70;k++) entries.push({name:'roms/p_'+String(k).padStart(16,'0')+'.unh', method:0, crc, usize:blob.size, csize:blob.size, body:blob});
   const z=ccsZipBlob(entries); const u=await ccsUnzip(z); const last=u.entries[69]; const raw=await u.raw(last); const tail=new Uint8Array(await raw.slice(raw.size-8192).arrayBuffer());
   return JSON.stringify({size:z.size, over4g:z.size>2**32, n:u.entries.length, lastOffset:last.lho, offsetOver4g:last.lho>2**32, rawSize:raw.size, tailOk:tail[0]===((part.length-8192)>>12&255)}); })()""")
        ok("archive_over_4_gb_parsed_with_64_bit_offsets", st["over4g"] and st["n"] == 70 and st["offsetOver4g"] and st["rawSize"] == 64 << 20 and st["tailOk"], st); print(f"   ZIP64 archive: {st['size']:,} bytes")
        # ---- retired public sharing; Studio data backups are JSON ----
        st = J(r"""(async()=>{ window.__ccsLink('share','AbCdEf123456'); await new Promise(r=>setTimeout(r,600)); const msg=$('collBody').innerText; closeColl();
   window.__saved=null; ccsSaveFile=async(n,b)=>{ window.__saved={name:n, type:b.type, text:await b.text()}; return {ok:true,name:n}; }; await csBackup(); const s=window.__saved;
   const fns=['openShare','shPublish','shReceive','shareConfig','ccsExportWithRoms'].filter(f=>typeof window[f]==='function');
   const ui=[...document.querySelectorAll('button,a,.mi,label')].map(e=>e.textContent).join('|');
   return JSON.stringify({msg, backupName:s&&s.name, backupJson:!!s&&(()=>{ try{ return JSON.parse(s.text).schema===SBACKUP_SCHEMA; }catch(e){ return false; } })(), fns, linkUi:/Share Collection Link|Export with ROMs \(\.ccs\)|Unpublish/.test(ui), shareParam:typeof SHARE_API!=='undefined'}); })()""")
        ok("shared_links_answer_with_a_plain_message", "no longer used" in st["msg"], st["msg"])
        ok("public_share_and_rom_free_export_code_removed", not st["fns"] and not st["linkUi"] and not st["shareParam"], st)
        ok("studio_data_backup_is_json_not_ccs", (st["backupName"] or "").endswith(".json") and st["backupJson"], st)
        # ---- network: nothing left this computer; no request carried ROM bytes ----
        time.sleep(1)
        st = J("""JSON.stringify({net:window.__net, res:performance.getEntriesByType('resource').map(e=>e.name)})""")
        hosts = sorted({u.split('/')[2] for u in [x["url"] for x in st["net"]] + st["res"] if u.startswith("http")})
        ok("no_request_left_this_computer", all(h.startswith("127.0.0.1") for h in hosts), hosts)
        ok("no_request_carried_rom_bytes", not any(x["sentinel"] for x in st["net"]) and not any("share.classiccartstudio" in u for u in st["res"]), [x for x in st["net"] if x["sentinel"]][:3])
        print(f"   requests recorded: {len(st['net'])} scripted, {len(st['res'])} resources; hosts: {hosts}")
    # ---- phone: the same dialog says Share Games ----
    with EdgeSession(width=390, height=844, scale=2, profile=tempfile.mkdtemp(prefix="ccs-v2m-")) as ed:
        ed.call("Emulation.setUserAgentOverride", {"userAgent": "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"})
        ed.navigate(URL); ed.wait_for(READY, timeout=120); time.sleep(1); ed.js(CALM); ed.js(ROMS)
        st = json.loads(ed.js(r"""(async()=>{ const x=await mkRom(1,true); sel.clear(); sel.add(x.id); persist(); render(); csSaveActiveNow(); await ccsAddRomSources([x.file]); ccsShareDialog(); await new Promise(r=>setTimeout(r,900));
   return JSON.stringify({label:$('ccsSend').textContent, exp:$('ccsExp').textContent, overflow:document.documentElement.scrollWidth>innerWidth+1, inView:[...document.querySelectorAll('#collm .acts button')].every(b=>{ const r=b.getBoundingClientRect(); return r.right<=innerWidth+1&&r.bottom<=innerHeight+1; })}); })()"""))
        ok("phone_label_share_games", st["label"] == "Share Games" and st["exp"] == "Export .ccs", st)
        ok("phone_dialog_fits", not st["overflow"] and st["inView"], st)
        open(os.path.join(a.out, "phone-share-dialog.png"), "wb").write(ed.screenshot())
    bad = [k for k, v in checks if not v]; print("RESULT:", "PASS" if not bad else "FAIL " + json.dumps(bad), f"({len(checks)} checks; packages in {a.out})"); return 0 if not bad else 1

if __name__ == "__main__": sys.exit(main())
