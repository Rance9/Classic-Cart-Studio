// ---- Classic Cart Studio's own Open, Save and folder dialog (CCSF): one dialog for every file and folder the page asks for ----
// In Classic Cart Studio for Windows (CO.desktop) the dialog is the app's own, in its dark look with the red accent: Back, Forward and Up,
// an editable folder path, the folder's steps to click, Desktop, Documents, Downloads, Pictures and this PC's drives at the side, the
// folder's folders and files sortable by name, date, kind and size, search within the folder, the kinds of file asked for, a name box,
// New Folder where something is saved or chosen, plain messages, a question before a file is replaced, and full keyboard use (Alt+Left,
// Alt+Right, Alt+Up or Backspace, F5, Ctrl+L, Ctrl+F, Ctrl+Shift+N, Enter, Space, Escape, typing a name) with names for screen readers.
// It shows this PC's folders through the app's own backend (/api/files/*: answered only to the app's own window); the page then sends the
// chosen path(s) to the operation that asked, and reads or saves a file the person chose there through it (/api/files/read, /write).
// On the website the browser owns the choice of a file or folder: the page asks the browser's own picker (showOpenFilePicker,
// showSaveFilePicker, showDirectoryPicker where the browser has them; otherwise a file input, or a named download for a save). A website
// cannot restyle that picker or list this PC's folders, and nothing here pretends otherwise.
//   CCSF.open({title, kinds, multiple, purpose, name})   -> [{name, path?, size, file()}] or null (canceled)
//   CCSF.folder({title, purpose, newFolder})              -> {path} (app) | {handle, name} (website) | null
//   CCSF.save({title, name, kinds, purpose})               -> {path} (app only) | null; it asks before an existing file is replaced
//   CCSF.saveBlob(name, blob, kind)                        -> {ok, name, path?, download?} | {ok:false, canceled}
// kinds: [{label, ext:['.zip', …]}]; an empty ext list is any file.
const CCSF=(()=>{
 const desk=()=>typeof CO!=='undefined'&&!!CO.desktop;
 const ANY={label:'All files', ext:[]};
 const REM='ccs-file-dialog-folders';   // the folder last used for each kind of choice (this browser only; a convenience)
 const remGet=p=>{ try{ return (JSON.parse(localStorage.getItem(REM)||'{}'))[p]||''; }catch(e){ return ''; } };
 const remSet=(p,f)=>{ try{ const o=JSON.parse(localStorage.getItem(REM)||'{}'); o[p]=f; localStorage.setItem(REM, JSON.stringify(o)); }catch(e){} };
 const e_=s=>String(s==null?'':s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const base=p=>String(p||'').replace(/[\\/]+$/,'').split(/[\\/]/).pop();
 const extOf=n=>{ const m=/\.[^.\\/]+$/.exec(n||''); return m?m[0].toLowerCase():''; };
 const okKind=(n,k)=>!k||!k.ext||!k.ext.length||k.ext.includes(extOf(n));
 const RESERVED=/^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;
 function nameProblem(n, what){ n=String(n||''); if(!n.trim()) return `Type a name for the ${what}.`;
  if(/[\\/:*?"<>|\u0000-\u001f]/.test(n)) return `A ${what} name cannot contain any of these: \\ / : * ? " < > |`;
  if(/[. ]$/.test(n)) return `A ${what} name cannot end with a period or a space.`;
  if(RESERVED.test(n.split('.')[0].trim())) return `“${n.split('.')[0].trim()}” is a name Windows keeps for itself. Type another name.`;
  if(n.length>240) return `That name is too long (${n.length} characters). Use a shorter one.`; return ''; }
 function size(n){ if(n==null) return ''; if(n<1000) return n.toLocaleString('en-US')+' B'; const u=[['KB',1e3,1],['MB',1e6,2],['GB',1e9,2],['TB',1e12,2]]; let i=0; while(i<u.length-1&&n>=u[i+1][1]) i++; let t=(n/u[i][1]).toFixed(u[i][2]); if(t.includes('.')) t=t.replace(/0+$/,'').replace(/\.$/,''); return t+' '+u[i][0]; }
 function when(s){ if(!s) return ''; const d=new Date(s); if(isNaN(d)) return ''; return d.toLocaleDateString('en-US',{month:'short',day:'numeric',year:'numeric'})+'  '+d.toLocaleTimeString('en-US',{hour:'numeric',minute:'2-digit'}); }
 const ICON={folder:'<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M2 5.5A1.5 1.5 0 0 1 3.5 4h4l2 2h7A1.5 1.5 0 0 1 18 7.5v7A1.5 1.5 0 0 1 16.5 16h-13A1.5 1.5 0 0 1 2 14.5z" fill="#e8b84a"/></svg>',
  file:'<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M5 2.5h6.5L15.5 6.5v11H5z" fill="none" stroke="#9c9a95" stroke-width="1.3"/><path d="M11.5 2.5v4h4" fill="none" stroke="#9c9a95" stroke-width="1.3"/></svg>',
  drive:'<svg viewBox="0 0 20 20" aria-hidden="true"><rect x="2.5" y="6" width="15" height="8" rx="1.5" fill="none" stroke="#9c9a95" stroke-width="1.3"/><circle cx="14.5" cy="10" r="1" fill="#9c9a95"/></svg>',
  pc:'<svg viewBox="0 0 20 20" aria-hidden="true"><rect x="2.5" y="3.5" width="15" height="10" rx="1" fill="none" stroke="#9c9a95" stroke-width="1.3"/><path d="M7 16.5h6M10 13.5v3" stroke="#9c9a95" stroke-width="1.3"/></svg>'};

 // ---- the app's own dialog (Classic Cart Studio for Windows) ----
 function dialog(o){ return new Promise(resolve=>{
  const mode=o.mode, multi=mode==='open'&&!!o.multiple, kinds=(o.kinds&&o.kinds.length?o.kinds:(mode==='folder'?[]:[ANY])), purpose=o.purpose||mode;
  const opener=document.activeElement; let cur=null, rows=[], shown=[], places=[], back=[], fwd=[], sortBy='name', down=false, kind=0, focusIx=0, lastClick=-1, loadId=0, toggled=false;
  const sel=new Set();   // indexes into shown
  const old=document.getElementById('ccsfd'); if(old) old.remove();
  const m=document.createElement('div'); m.id='ccsfd'; m.className='modal open'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','ccsfdT');
  const okLabel=mode==='save'?'Save':mode==='open'?'Open':'Choose This Folder', nameLabel=mode==='save'?'File name':mode==='open'?(multi?'File names':'File name'):'Folder';
  m.innerHTML=`<div class="mbox" role="document">
   <div class="mhd"><h2 id="ccsfdT">${e_(o.title||(mode==='save'?'Save As':mode==='open'?'Choose a File':'Choose a Folder'))}</h2></div>
   <div class="fd-bar">
    <button type="button" class="fd-tool" data-a="back" aria-label="Back" title="Back (Alt+Left)">&#8592;</button><button type="button" class="fd-tool" data-a="fwd" aria-label="Forward" title="Forward (Alt+Right)">&#8594;</button><button type="button" class="fd-tool" data-a="up" aria-label="Up one folder" title="Up one folder (Alt+Up)">&#8593;</button><button type="button" class="fd-tool" data-a="refresh" aria-label="Refresh" title="Refresh (F5)">&#10227;</button>
    <input type="text" class="fd-addr" aria-label="Folder path" title="Type or paste a folder's path, then press Enter (Ctrl+L)" spellcheck="false" autocomplete="off">
    <input type="search" class="fd-search" aria-label="Search this folder" placeholder="Search this folder" spellcheck="false" autocomplete="off">
   </div>
   <nav class="fd-crumbs" aria-label="Folder steps"></nav>
   <div class="fd-mid">
    <ul class="fd-places" role="listbox" aria-label="Places" tabindex="0"></ul>
    <div class="fd-list" role="grid" aria-label="${mode==='folder'?'Folders':'Folders and files'}" aria-multiselectable="${multi}" tabindex="0">
     <div class="fd-head" role="row">${multi?'<span class="fd-c fd-cc" role="columnheader"></span>':''}<button type="button" class="fd-c fd-cn" role="columnheader" data-s="name">Name</button><button type="button" class="fd-c fd-cd" role="columnheader" data-s="date">Date changed</button><button type="button" class="fd-c fd-ct" role="columnheader" data-s="kind">Type</button><button type="button" class="fd-c fd-cs" role="columnheader" data-s="size">Size</button></div>
     <div class="fd-rows" role="rowgroup"></div>
    </div>
   </div>
   <div class="fd-status small" aria-live="polite"></div>
   <div class="msgbox bad fd-msg" role="alert" hidden></div>
   <div class="fd-ask" hidden></div>
   <div class="fd-foot">
    <label class="fd-nl" for="ccsfdN">${nameLabel}:</label><input type="text" id="ccsfdN" class="fd-name" spellcheck="false" autocomplete="off">
    <select class="fd-kind" aria-label="Kind of file" ${kinds.length?'':'hidden'}>${kinds.map((k,i)=>`<option value="${i}" title="${e_(k.ext&&k.ext.length?k.ext.map(x=>'*'+x).join(', '):'Any file')}">${e_(k.label)}</option>`).join('')}</select>
   </div>
   <div class="acts fd-acts">${mode!=='open'&&o.newFolder!==false?'<button type="button" class="btn" data-a="new" title="Make a new folder here (Ctrl+Shift+N)">New Folder…</button>':''}<span class="sp"></span><button type="button" class="btn primary" data-a="ok">${okLabel}</button><button type="button" class="btn" data-a="cancel">Cancel</button></div>
  </div>`;
  document.body.appendChild(m);
  const q=s=>m.querySelector(s), addr=q('.fd-addr'), srch=q('.fd-search'), name=q('.fd-name'), list=q('.fd-list'), body=q('.fd-rows'), pl=q('.fd-places'), status=q('.fd-status'), msg=q('.fd-msg'), ask=q('.fd-ask'), kindSel=q('.fd-kind');
  const btn=a=>m.querySelector(`[data-a="${a}"]`);
  function say(t){ if(t){ msg.textContent=t; msg.hidden=false; } else msg.hidden=true; }
  function close(r){ window.removeEventListener('keydown', onKey, true); m.remove(); try{ if(opener&&opener.focus&&document.contains(opener)) opener.focus({preventScroll:true}); }catch(e){} if(r&&cur) remSet(purpose, mode==='folder'?r[0]:(r[0]||'').replace(/[\\/][^\\/]*$/,'')); resolve(r); }
  async function api(p, b){ return hkPost(p, b||{}); }
  function setName(t){ name.value=t; }
  function crumbs(cs){ const n=q('.fd-crumbs'); n.innerHTML=(cs||[{name:'This PC',path:''}]).map((c,i,a)=>`<button type="button" class="fd-crumb${i===a.length-1?' on':''}" data-p="${e_(c.path)}"${i===a.length-1?' aria-current="location"':''}>${e_(c.name)}</button>`).join('<span class="fd-sep" aria-hidden="true">›</span>'); }
  function markPlace(){ pl.querySelectorAll('[role=option]').forEach(li=>li.setAttribute('aria-selected', String((li.dataset.p||'').replace(/\\$/,'').toLowerCase()===String(cur||'').replace(/\\$/,'').toLowerCase()))); }
  async function load(p, remember, select){
   p=p||''; if(remember&&cur!==null&&p.toLowerCase()!==String(cur).toLowerCase()){ back.push(cur); fwd=[]; }
   const id=++loadId; status.textContent='Opening…'; say('');
   const r=await api('/api/files/list',{path:p}); if(id!==loadId) return;
   if(!r||!r.ok){ status.textContent=''; if(remember&&back.length&&back[back.length-1]===cur) back.pop(); say((r&&r.error)||'This folder could not be opened.'); if(cur===null){ cur=''; rows=[]; render(); } return; }
   cur=r.path||''; rows=r.entries||[]; addr.value=cur||'This PC'; crumbs(r.crumbs); markPlace(); srch.value='';
   if(mode==='open'&&remember) setName('');
   btn('back').disabled=!back.length; btn('fwd').disabled=!fwd.length; btn('up').disabled=!cur; if(btn('new')) btn('new').disabled=!cur;
   render(select); }
  function render(select){
   const s=srch.value.trim().toLowerCase(), k=kinds[kind];
   shown=rows.filter(r=>(!s||r.name.toLowerCase().includes(s))&&(r.folder?true:mode!=='folder'&&okKind(r.name,k)));
   const dir=down?-1:1, cmp={name:(a,b)=>a.name.localeCompare(b.name,undefined,{sensitivity:'base',numeric:true}), date:(a,b)=>String(a.modified||'').localeCompare(String(b.modified||'')), kind:(a,b)=>String(a.kind||'').localeCompare(String(b.kind||'')), size:(a,b)=>(a.size==null?-1:a.size)-(b.size==null?-1:b.size)}[sortBy];
   shown.sort((a,b)=>a.folder!==b.folder?(a.folder?-1:1):(cmp(a,b)*dir||a.name.localeCompare(b.name)));
   sel.clear(); focusIx=0; toggled=false;
   if(select){ const i=shown.findIndex(r=>r.path.toLowerCase()===select.toLowerCase()); if(i>=0){ sel.add(i); focusIx=i; } }
   body.innerHTML=shown.map((r,i)=>`<div class="fd-row" role="row" id="ccsfdR${i}" data-i="${i}" aria-selected="${sel.has(i)}" aria-label="${e_(r.name+', '+(r.drive?'drive':r.folder?'folder':(r.kind||'file')+(r.size!=null?', '+size(r.size):''))+(r.modified?', changed '+when(r.modified):''))}">${multi?`<span class="fd-c fd-cc" role="gridcell">${r.folder?'':'<span class="fd-check" aria-hidden="true"></span>'}</span>`:''}<span class="fd-c fd-cn" role="gridcell"><span class="fd-ico">${r.drive?ICON.drive:r.folder?ICON.folder:ICON.file}</span><span class="fd-nm">${e_(r.name)}</span></span><span class="fd-c fd-cd" role="gridcell">${e_(when(r.modified))}</span><span class="fd-c fd-ct" role="gridcell">${e_(r.kind||'')}</span><span class="fd-c fd-cs" role="gridcell">${e_(size(r.size))}</span></div>`).join('');
   q('.fd-head').querySelectorAll('[data-s]').forEach(h=>{ const on=h.dataset.s===sortBy; h.setAttribute('aria-sort', on?(down?'descending':'ascending'):'none'); h.textContent=h.textContent.replace(/[ ↑↓]+$/,'')+(on?(down?'  ↓':'  ↑'):''); });
   const folders=shown.filter(r=>r.folder).length, files=shown.length-folders, hidden=rows.filter(r=>!r.folder).length-files;
   status.textContent=(s?`Matching “${srch.value.trim()}”: `:'')+(cur===''?`${shown.length} drive${shown.length===1?'':'s'}`:mode==='folder'?`${folders} folder${folders===1?'':'s'}`:`${folders} folder${folders===1?'':'s'}, ${files} file${files===1?'':'s'}`)+(hidden>0&&!s&&mode!=='folder'&&kinds.length>1?' (files of other kinds are not shown; choose the kind below to see them)':'');
   paintSel(); }
  function paintSel(){ body.querySelectorAll('.fd-row').forEach(el=>{ const i=+el.dataset.i; el.setAttribute('aria-selected', String(sel.has(i))); el.classList.toggle('on', sel.has(i)); el.classList.toggle('fx', i===focusIx); });
   if(shown.length) list.setAttribute('aria-activedescendant','ccsfdR'+focusIx); else list.removeAttribute('aria-activedescendant');
   const files=[...sel].map(i=>shown[i]).filter(r=>r&&!r.folder);
   if(mode==='open'){ if(files.length===1) setName(files[0].name); else if(files.length>1) setName(files.map(f=>'"'+f.name+'"').join(' ')); }
   else if(mode==='save'){ if(files.length) setName(files[0].name); }
   else { const f=[...sel].map(i=>shown[i])[0]; setName(f?(f.drive?f.path:f.name):''); } }
  function focusRow(i, extend){ if(!shown.length) return; i=Math.max(0,Math.min(shown.length-1,i)); const from=focusIx; focusIx=i;
   if(multi&&extend){ for(let j=Math.min(from,i); j<=Math.max(from,i); j++) if(!shown[j].folder) sel.add(j); }
   else if(!(multi&&toggled)){ sel.clear(); sel.add(i); }
   paintSel(); const el=body.querySelector(`[data-i="${i}"]`); if(el) el.scrollIntoView({block:'nearest'}); }
  function openRow(i){ const r=shown[i]; if(!r) return; if(r.folder){ load(r.path, true); list.focus(); return; } if(mode==='open'){ if(!multi||sel.size<=1) return finish([r.path]); return accept(); } if(mode==='save'){ setName(r.name); accept(); } }
  function up(){ if(!cur) return; const from=cur; const parent=cur.replace(/[\\/]+$/,'').replace(/[\\/][^\\/]*$/,''); const to=/^[A-Za-z]:$/.test(parent)?parent+'\\':(cur.length<=3?'':parent); load(to, true, from); }
  async function goTyped(t){ t=String(t||'').trim().replace(/^"|"$/g,''); if(!t||/^this pc$/i.test(t)) return load('', true);
   const full=/^[A-Za-z]:|^\\\\/.test(t)?t:(cur?cur.replace(/[\\/]+$/,'')+'\\'+t:t);
   const st=(await api('/api/files/stat',{path:full})||{}).items||[]; const s0=st[0]||{};
   if(s0.ok&&s0.exists&&s0.folder) return load(s0.path, true);
   if(s0.ok&&s0.exists&&s0.file&&mode!=='folder'){ const p=s0.path.replace(/[\\/][^\\/]*$/,''); return load(/^[A-Za-z]:$/.test(p)?p+'\\':p, true, s0.path); }
   say(s0.ok===false?s0.error:`There is no folder at that place: ${t}. Check the spelling, or choose a place at the side.`); }
  function finish(r){ close(r); }
  function kindExt(){ const k=kinds[kind]; return k&&k.ext&&k.ext.length?k.ext[0]:''; }
  function askReplace(text, yes, no){ return new Promise(res=>{ ask.innerHTML=`<div class="msgbox warn"><b>Replace the file?</b> ${e_(text)}</div><div class="acts"><span class="sp"></span><button type="button" class="btn primary" data-c="1">${e_(yes)}</button><button type="button" class="btn" data-c="0">${e_(no)}</button></div>`; ask.hidden=false;
   const b0=ask.querySelector('[data-c="0"]'); b0.focus(); ask.onclick=ev=>{ const b=ev.target.closest('[data-c]'); if(!b) return; ask.hidden=true; ask.innerHTML=''; res(b.dataset.c==='1'); }; askKeys=ev=>{ if(ev.key==='Escape'){ ev.preventDefault(); ask.hidden=true; ask.innerHTML=''; askKeys=null; res(false); } }; }); }
  function askName(def){ return new Promise(res=>{ ask.innerHTML=`<div class="fld"><label for="ccsfdNew">New folder name (made in ${e_(cur)})</label><input type="text" id="ccsfdNew" spellcheck="false" autocomplete="off" value="${e_(def)}"></div><div class="msgbox bad" hidden></div><div class="acts"><span class="sp"></span><button type="button" class="btn primary" data-c="1">Make the Folder</button><button type="button" class="btn" data-c="0">Cancel</button></div>`; ask.hidden=false;
   const inp=ask.querySelector('#ccsfdNew'), err=ask.querySelector('.msgbox'); inp.focus(); inp.select();
   const done=v=>{ ask.hidden=true; ask.innerHTML=''; res(v); };
   const go=()=>{ const w=nameProblem(inp.value.trim(),'folder'); if(w){ err.textContent=w; err.hidden=false; inp.focus(); return; } done(inp.value.trim()); };
   ask.onclick=ev=>{ const b=ev.target.closest('[data-c]'); if(!b) return; if(b.dataset.c==='1') go(); else done(null); };
   askKeys=ev=>{ if(ev.key==='Enter'&&ev.target===inp){ ev.preventDefault(); go(); } else if(ev.key==='Escape'){ ev.preventDefault(); done(null); } }; }); }
  async function newFolder(){ if(!cur){ say('Open a drive or a folder first; a new folder is made in the folder shown.'); return; }
   let s='New folder'; for(let i=2; rows.some(r=>r.name.toLowerCase()===s.toLowerCase()); i++) s=`New folder (${i})`;
   const n=await askName(s); if(!n){ list.focus(); return; }
   const r=await api('/api/files/mkdir',{path:cur, name:n}); if(!r||!r.ok){ say((r&&r.error)||'The folder could not be made here.'); return; }
   await load(cur, false, r.path); list.focus(); }
  async function accept(){ say('');
   const typed=name.value.trim();
   if(mode==='folder'){ const s=[...sel].map(i=>shown[i])[0];
    if(s&&(!typed||typed===s.name||typed===s.path)) return finish([s.path]);
    if(typed){ const full=/^[A-Za-z]:|^\\\\/.test(typed)?typed:(cur?cur.replace(/[\\/]+$/,'')+'\\'+typed:typed); const st=((await api('/api/files/stat',{path:full}))||{}).items||[]; if(st[0]&&st[0].exists&&st[0].folder) return finish([st[0].path]); return say(`There is no folder named “${typed}” here. Choose a folder in the list, or make one with New Folder.`); }
    if(!cur) return say('Choose a drive or a folder first.'); return finish([cur]); }
   if(mode==='open'){ let names=typed.includes('"')?[...typed.matchAll(/"([^"]+)"/g)].map(x=>x[1].trim()).filter(Boolean):(typed?[typed]:[]);
    if(!names.length){ const s=[...sel].map(i=>shown[i])[0]; if(s&&s.folder) return openRow([...sel][0]); return say(multi?'Choose one or more files first.':'Choose a file first.'); }
    if(!multi&&names.length>1) return say('Choose only one file.');
    const full=names.map(n=>/^[A-Za-z]:|^\\\\/.test(n)?n:(cur?cur.replace(/[\\/]+$/,'')+'\\'+n:n));
    const st=((await api('/api/files/stat',{paths:full}))||{}).items||[];
    if(st.length===1&&st[0].exists&&st[0].folder){ setName(''); return load(st[0].path, true); }
    const bad=st.findIndex(x=>!x.ok||!x.exists||!x.file); if(bad>=0) return say(`There is no file named “${names[bad]}” here. Check the name, or choose the file in the list.`);
    return finish([...new Set(st.map(x=>x.path))]); }
   // save
   if(!typed){ name.focus(); return say('Type a name for the file.'); }
   let full=/^[A-Za-z]:|^\\\\/.test(typed)?typed:(cur?cur.replace(/[\\/]+$/,'')+'\\'+typed:'');
   if(!full) return say('Choose a drive or a folder to save in first.');
   let st=(((await api('/api/files/stat',{path:full}))||{}).items||[])[0]||{};
   if(st.ok&&st.exists&&st.folder){ setName(''); return load(st.path, true); }
   const fname=base(full), w=nameProblem(fname,'file'); if(w){ name.focus(); return say(w); }
   if(!extOf(fname)&&kindExt()){ full+=kindExt(); st=(((await api('/api/files/stat',{path:full}))||{}).items||[])[0]||{}; }
   if(st.ok===false) return say(st.error);
   if(st.exists&&st.folder) return say(`A folder named “${base(full)}” is already there. Type another name for the file.`);
   if(st.exists){ const yes=await askReplace(`“${base(full)}” is already in ${full.replace(/[\\/][^\\/]*$/,'')}. Replace it with the new file? The file there now is lost.`, 'Replace It', 'Keep It'); if(!yes){ name.focus(); return; } }
   return finish([st.path||full]); }
  // ---- events ----
  m.addEventListener('click',ev=>{ const a=ev.target.closest('[data-a]'); if(a&&!a.disabled){ const k=a.dataset.a;
    if(k==='back'&&back.length){ fwd.push(cur); load(back.pop(), false); } else if(k==='fwd'&&fwd.length){ back.push(cur); load(fwd.pop(), false); } else if(k==='up') up(); else if(k==='refresh') load(cur, false); else if(k==='new') newFolder(); else if(k==='ok') accept(); else if(k==='cancel') close(null); return; }
   const c=ev.target.closest('.fd-crumb'); if(c){ load(c.dataset.p, true); return; }
   const h=ev.target.closest('[data-s]'); if(h){ if(sortBy===h.dataset.s) down=!down; else { sortBy=h.dataset.s; down=sortBy==='date'||sortBy==='size'; } render(); return; }
   const li=ev.target.closest('.fd-places [role=option]'); if(li){ load(li.dataset.p, true); return; }
   const row=ev.target.closest('.fd-row'); if(row){ const i=+row.dataset.i; list.focus();
    if(multi&&(ev.ctrlKey||ev.target.closest('.fd-cc'))&&!shown[i].folder){ if(sel.has(i)&&toggled) sel.delete(i); else sel.add(i); toggled=true; focusIx=i; paintSel(); }
    else if(multi&&ev.shiftKey&&lastClick>=0){ sel.clear(); for(let j=Math.min(lastClick,i); j<=Math.max(lastClick,i); j++) sel.add(j); focusIx=i; paintSel(); }
    else { toggled=false; focusRow(i); } lastClick=i; } });
  m.addEventListener('dblclick',ev=>{ const row=ev.target.closest('.fd-row'); if(row) openRow(+row.dataset.i); });
  srch.addEventListener('input',()=>render());
  kindSel.addEventListener('change',()=>{ kind=+kindSel.value||0; if(mode==='save'){ const n=name.value.trim(), x=extOf(n), k=kindExt(); if(n&&x&&k&&kinds.some(kk=>(kk.ext||[]).includes(x))) setName(n.slice(0,-x.length)+k); } render(); });
  let askKeys=null;   // the question inside the dialog (Replace the file?, New Folder) while it is shown
  function onKey(ev){ if(!ccsTopDialog(m)) return; const t=ev.target, inText=t&&(t.tagName==='INPUT'||t.tagName==='SELECT'), k=ev.key;
   if(!ask.hidden&&askKeys&&ask.contains(t)){ askKeys(ev); ev.stopImmediatePropagation(); return; }
   let handled=true;
   if(ev.altKey&&k==='ArrowLeft'){ if(back.length){ fwd.push(cur); load(back.pop(), false); } }
   else if(ev.altKey&&k==='ArrowRight'){ if(fwd.length){ back.push(cur); load(fwd.pop(), false); } }
   else if(ev.altKey&&k==='ArrowUp') up();
   else if(k==='F5') load(cur, false);
   else if((ev.ctrlKey&&(k==='l'||k==='L'))||(ev.altKey&&(k==='d'||k==='D'))||k==='F4'){ addr.focus(); addr.select(); }
   else if((ev.ctrlKey&&(k==='f'||k==='F'))||k==='F3'){ srch.focus(); srch.select(); }
   else if(ev.ctrlKey&&ev.shiftKey&&(k==='n'||k==='N')){ if(btn('new')) newFolder(); }
   else if(k==='Escape'){ if(t===addr){ addr.value=cur||'This PC'; list.focus(); } else if(t===srch&&srch.value){ srch.value=''; render(); } else close(null); }
   else if(k==='Enter'&&t===addr) goTyped(addr.value);
   else if(k==='Enter'&&t===srch) list.focus();
   else if(k==='Enter'&&t===name) accept();
   else if(t===list){ const pg=Math.max(1,Math.floor(list.clientHeight/32)-2);
    if(k==='ArrowDown') focusRow(focusIx+1, ev.shiftKey); else if(k==='ArrowUp') focusRow(focusIx-1, ev.shiftKey); else if(k==='Home') focusRow(0); else if(k==='End') focusRow(shown.length-1);
    else if(k==='PageDown') focusRow(focusIx+pg); else if(k==='PageUp') focusRow(focusIx-pg);
    else if(k==='Enter'){ if(multi&&sel.size>1) accept(); else openRow(focusIx); }
    else if(k===' '&&multi){ const r=shown[focusIx]; if(r&&!r.folder){ if(!sel.has(focusIx)) sel.add(focusIx); else if(toggled) sel.delete(focusIx); [...sel].forEach(j=>{ if(shown[j]&&shown[j].folder) sel.delete(j); }); toggled=true; paintSel(); } }
    else if(k==='Backspace') up();
    else if(k.length===1&&!ev.ctrlKey&&!ev.altKey&&!ev.metaKey){ const ch=k.toLowerCase(); const n=shown.length; for(let j=1;j<=n;j++){ const i=(focusIx+j)%n; if(shown[i].name.toLowerCase().startsWith(ch)){ focusRow(i); break; } } }
    else handled=false; }
   else if(t===pl){ const items=[...pl.querySelectorAll('[role=option]')]; let i=items.indexOf(pl.querySelector('.fx')); if(i<0) i=Math.max(0,items.findIndex(x=>x.getAttribute('aria-selected')==='true'));
    if(k==='ArrowDown'||k==='ArrowUp'){ i=Math.max(0,Math.min(items.length-1,i+(k==='ArrowDown'?1:-1))); items.forEach((x,j)=>x.classList.toggle('fx',j===i)); pl.setAttribute('aria-activedescendant', items[i].id); }
    else if(k==='Enter'||k===' '){ if(items[i]){ load(items[i].dataset.p, true); list.focus(); } }
    else handled=false; }
   else if(k==='Backspace'&&!inText) up();
   else if(k==='Tab'){ const f=[...m.querySelectorAll('button:not([disabled]),input:not([hidden]),select:not([hidden]),[tabindex="0"]')].filter(x=>x.offsetParent!==null); const a=f.indexOf(document.activeElement);
    if(ev.shiftKey&&a<=0){ f[f.length-1].focus(); } else if(!ev.shiftKey&&a===f.length-1){ f[0].focus(); } else handled=false; }
   else handled=false;
   if(handled){ ev.preventDefault(); } ev.stopImmediatePropagation(); }
  window.addEventListener('keydown', onKey, true);
  // ---- start ----
  (async()=>{ const pr=await api('/api/files/places'); if(!pr||!pr.ok){ say((pr&&pr.error)||'This PC’s folders could not be shown.'); cur=''; return; }
   places=pr.places||[]; pl.innerHTML=places.map((p,i)=>`<li role="option" id="ccsfdP${i}" data-p="${e_(p.path)}" aria-selected="false" class="${p.pc?'pc':p.drive?'drive':''}"><span class="fd-ico">${p.pc?ICON.pc:p.drive?ICON.drive:ICON.folder}</span><span>${e_(p.name)}</span></li>`).join('');
   if(mode==='save') setName(o.name||''); if(mode==='open'&&o.name) setName(o.name);
   const start=o.folder||remGet(purpose)||((places.find(p=>p.name==='Documents')||places.find(p=>!p.pc)||{}).path)||'';
   await load(start, false); if(cur===null||(start&&cur==='')) await load(((places.find(p=>p.name==='Documents')||{}).path)||'', false);
   if(mode==='save'){ name.focus(); const x=extOf(name.value); name.setSelectionRange(0, Math.max(0, name.value.length-x.length)); } else list.focus(); })();
 }); }

 // ---- reading a chosen file in the app (the page needs its bytes: a collection file, a picture) ----
 async function readChosen(paths){ const g=await hkPost('/api/files/grant',{mode:'read', paths}); if(!g||!g.ok) throw new Error((g&&g.error)||'The file could not be read.');
  return g.files.map(f=>({name:f.name, path:f.path, size:f.size, file:async()=>{ const r=await fetch(CO.base+'/api/files/read?token='+encodeURIComponent(f.token), lnaInit({headers:hdrs(), cache:'no-store'})); if(!r.ok) throw new Error('The file could not be read ('+r.status+').'); const b=await r.blob(); return new File([b], f.name, {type:b.type||''}); }})); }

 // ---- the website: the browser's own picker ----
 function input(accept, multiple, doc){ doc=doc||document; return new Promise(res=>{ const inp=doc.createElement('input'); inp.type='file'; if(accept) inp.accept=accept; inp.multiple=!!multiple; inp.onchange=()=>res([...inp.files]); inp.addEventListener('cancel',()=>res([])); inp.click(); }); }
 // the kinds a page's accept list names (".zip,application/zip" -> ZIP files), always with All files after them
 function kindsFrom(accept){ const ex=[...new Set(String(accept||'').split(',').map(x=>x.trim().toLowerCase()).filter(x=>/^\.[a-z0-9]+$/.test(x)))]; if(!ex.length) return [ANY];
  const pic=['.png','.jpg','.jpeg','.webp','.gif','.bmp'], rom=['.nes','.unh','.unif','.bin','.rom','.fds','.zip'];
  const label=ex.every(x=>pic.includes(x))?'Pictures':ex.length===1&&ex[0]==='.zip'?'ZIP files':ex.length===1&&ex[0]==='.json'?'Classic Cart Studio files (JSON)':ex.every(x=>rom.includes(x))?'NES game files and archives':'Files of this kind';
  return [{label, ext:ex}, ANY]; }
 // the browser's own picker: showOpenFilePicker where the browser has it (its kinds offered, All files kept), else a file input; a picker
 // in another window's document (a pop-out player) is that window's own file input
 async function webOpen(o){
  const doc=o.doc||document;
  if(window.showOpenFilePicker&&doc===document){ const types=(o.kinds||[]).filter(k=>k.ext&&k.ext.length).map(k=>({description:k.label, accept:{[k.ext.every(x=>['.png','.jpg','.jpeg','.webp','.gif','.bmp'].includes(x))?'image/*':'application/octet-stream']:k.ext}}));
   try{ const hs=await window.showOpenFilePicker({multiple:!!o.multiple, types:types.length?types:undefined, excludeAcceptAllOption:false}); const fs=await Promise.all(hs.map(h=>h.getFile())); return fs.length?fs.map(f=>({name:f.name, size:f.size, file:async()=>f})):null; }
   catch(e){ if(e&&e.name==='AbortError') return null; } }   // anything else (a browser that refuses the picker here): the file input below
  const fs=await input(o.accept||'', o.multiple, doc); return fs.length?fs.map(f=>({name:f.name, size:f.size, file:async()=>f})):null; }

 // ---- the website's Save: the browser's own Save picker where the browser has it; otherwise an editable name first, then the browser's
 //      download (its own settings decide the folder; no path or finished write is known)
 async function webSave(defName, blob, kind){ const ext=extOf(defName); const mime=blob.type||'application/octet-stream';
 if(window.showSaveFilePicker){ try{ const h=await window.showSaveFilePicker({suggestedName:defName, types:[{description:kind||'File', accept:{[mime]:[ext]}}]}); const w=await h.createWritable(); await w.write(blob); await w.close(); return {ok:true, name:h.name}; }
  catch(e){ if(e&&e.name==='AbortError') return {ok:false, canceled:true}; } }
 return new Promise(res=>{ let m=document.getElementById('savem'); if(!m){ m=document.createElement('div'); m.id='savem'; m.className='modal'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','savemT'); document.body.appendChild(m); }
  m.innerHTML=`<div class="mbox" style="max-width:560px"><div class="mhd"><h2 id="savemT">Save ${esc(kind||'File')}</h2></div><div class="fld"><label for="savemN">File name</label><input type="text" id="savemN" value="${esc(defName)}" spellcheck="false"></div><p class="small">Your browser saves it to its usual download location.</p><div class="acts"><span class="sp"></span><button class="btn" id="savemC">Cancel</button><button class="btn hk" id="savemS">Save</button></div></div>`;
  m.classList.add('open'); const inp=$('savemN'); inp.focus(); inp.setSelectionRange(0, Math.max(0, inp.value.length-ext.length));
  const done=r=>{ m.classList.remove('open'); res(r); };
  $('savemC').onclick=()=>done({ok:false,canceled:true}); m.onkeydown=e=>{ if(e.key==='Escape') done({ok:false,canceled:true}); if(e.key==='Enter'&&e.target===inp) $('savemS').click(); };
  $('savemS').onclick=()=>{ const name=csSafeName(inp.value, ext); const a=document.createElement('a'); a.href=URL.createObjectURL(blob); a.download=name; document.body.appendChild(a); a.click(); a.remove(); setTimeout(()=>URL.revokeObjectURL(a.href),60000); done({ok:true,name,download:true}); }; }); }

 return {
  desktop:desk, nameProblem, kindsFrom,
  async read(paths){ const r=await readChosen(paths); return Promise.all(r.map(x=>x.file())); },
  async open(o){ o=o||{}; if(desk()){ const r=await dialog({mode:'open', title:o.title, kinds:o.kinds, multiple:o.multiple, purpose:o.purpose, name:o.name, folder:o.folder}); if(!r) return null; return readChosen(r); } return webOpen(o); },
  async openPaths(o){ o=o||{}; const r=await dialog({mode:'open', title:o.title, kinds:o.kinds, multiple:o.multiple, purpose:o.purpose, name:o.name, folder:o.folder}); return r&&r.length?r:null; },
  async folder(o){ o=o||{}; if(desk()){ const r=await dialog({mode:'folder', title:o.title, purpose:o.purpose, newFolder:o.newFolder, folder:o.folder}); return r?{path:r[0]}:null; }
   if(window.showDirectoryPicker){ try{ const h=await window.showDirectoryPicker({mode:'read'}); return {handle:h, name:h.name}; }catch(e){ return null; } } return null; },
  async save(o){ o=o||{}; const r=await dialog({mode:'save', title:o.title, name:o.name, kinds:o.kinds, purpose:o.purpose, folder:o.folder}); return r?{path:r[0]}:null; },
  async saveBlob(defName, blob, kind, o){ o=o||{};
   if(!desk()) return webSave(defName, blob, kind);
   const ext=extOf(defName); const r=await dialog({mode:'save', title:o.title||('Save '+(kind||'File')), name:defName, kinds:ext?[{label:kind||'File', ext:[ext]}, ANY]:[ANY], purpose:o.purpose||'save-'+(ext||'file')}); if(!r) return {ok:false, canceled:true};
   const g=await hkPost('/api/files/grant',{mode:'write', paths:[r[0]]}); if(!g||!g.ok) return {ok:false, error:(g&&g.error)||'The file could not be saved there.'};
   let w; try{ const res=await fetch(CO.base+'/api/files/write?token='+encodeURIComponent(g.files[0].token), lnaInit({method:'POST', headers:hdrs({'Content-Type':'application/octet-stream'}), body:blob, cache:'no-store'})); w=await res.json(); }catch(e){ w={ok:false, error:'The file could not be saved ('+(e&&e.message||e)+').'}; }
   return w&&w.ok?{ok:true, name:w.name, path:w.path}:{ok:false, error:(w&&w.error)||'The file was not saved.'}; }
 };
})();
window.CCSF=CCSF;
// ---- the page's own question, notice and text answer, in place of the browser's confirm(), alert() and prompt() (which show the page's
//      address and the browser's own look): the same words, OK and Cancel (OK is the default: Enter; Escape and Cancel answer Cancel; a
//      notice has OK only), the app's look with the red edge, the keyboard kept inside it, the focus back where it was afterwards.
//   ccsAsk(text) -> Promise<true|false>     ccsTell(text) -> Promise     ccsPrompt(text, value) -> Promise<string|null>
function ccsDialog(text, o){ o=o||{}; return new Promise(resolve=>{
 const opener=document.activeElement; const id='ccsask'+Math.random().toString(36).slice(2,8);
 const m=document.createElement('div'); m.className='modal open ccsask'; m.setAttribute('role', o.input!=null?'dialog':'alertdialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-describedby', id+'T'); m.setAttribute('aria-label', o.label||'Classic Cart Studio');
 const e_=s=>String(s==null?'':s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 m.innerHTML=`<div class="mbox"><div class="ccsask-t" id="${id}T">${e_(text)}</div>${o.input!=null?`<input type="text" class="ccsask-in" aria-label="${e_(String(text).split('\n')[0].slice(0,120))}" spellcheck="false" autocomplete="off">`:''}
  <div class="acts"><span class="sp"></span>${o.cancel!==false?'<button type="button" class="btn" data-k="0">Cancel</button>':''}<button type="button" class="btn primary" data-k="1">OK</button></div></div>`;
 document.body.appendChild(m);
 const inp=m.querySelector('.ccsask-in'), ok=m.querySelector('[data-k="1"]'); if(inp) inp.value=String(o.input);
 const done=v=>{ window.removeEventListener('keydown', onKey, true); m.remove(); try{ if(opener&&opener.focus&&document.contains(opener)) opener.focus({preventScroll:true}); }catch(e){} resolve(v); };
 const yes=()=>done(o.input!=null?inp.value:true), no=()=>done(o.input!=null?null:o.cancel===false?undefined:false);
 m.addEventListener('click',ev=>{ const b=ev.target.closest('[data-k]'); if(b){ if(b.dataset.k==='1') yes(); else no(); } });
 const onKey=ev=>{ if(!ccsTopDialog(m)) return; ev.stopImmediatePropagation(); if(ev.key==='Escape'){ ev.preventDefault(); no(); } else if(ev.key==='Enter'&&(ev.target===inp||ev.target===ok)){ ev.preventDefault(); yes(); }
  else if(ev.key==='Tab'){ const f=[...m.querySelectorAll('input,button')]; const i=f.indexOf(document.activeElement); if(ev.shiftKey&&i<=0){ ev.preventDefault(); f[f.length-1].focus(); } else if(!ev.shiftKey&&i===f.length-1){ ev.preventDefault(); f[0].focus(); } } };
 window.addEventListener('keydown', onKey, true);
 if(inp){ inp.focus(); inp.select(); } else ok.focus(); }); }
// the page's own dialog that is on top (the last one opened): only it takes the keys
function ccsTopDialog(m){ const all=document.querySelectorAll('#ccsfd, .modal.ccsask'); return all.length>0&&all[all.length-1]===m; }
function ccsAsk(text){ return ccsDialog(text, {}); }
function ccsTell(text){ return ccsDialog(text, {cancel:false}); }
function ccsPrompt(text, value){ return ccsDialog(text, {input:value==null?'':value}); }
window.ccsAsk=ccsAsk; window.ccsTell=ccsTell; window.ccsPrompt=ccsPrompt;
// the backend routes whose file or folder the person chooses first in the app's own dialog: the request then carries the choice (a canceled
// choice answers {canceled:true} without asking the backend). ask: {mode:'open'|'save'|'folder', title, kinds, multiple, name, purpose}
async function hkChoose(route, body, ask){ let r;
 if(ask.mode==='folder') r=await CCSF.folder(ask); else if(ask.mode==='save') r=await CCSF.save(ask); else { const p=await CCSF.openPaths(ask); r=p?(ask.multiple?{paths:p}:{path:p[0]}):null; }
 if(!r) return {ok:false, canceled:true};
 return hkPost(route, Object.assign({}, body||{}, r.paths?{paths:r.paths}:{path:r.path}, ask.mode==='folder'?{paths:[r.path]}:{})); }
const CCSF_ROMS=[{label:'NES game files and archives', ext:['.nes','.unh','.unif','.bin','.rom','.fds','.zip']},{label:'All files', ext:[]}];
const CCSF_PICTURES=[{label:'Pictures', ext:['.png','.jpg','.jpeg','.webp','.bmp','.gif']},{label:'All files', ext:[]}];
