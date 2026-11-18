// ---- right-click menus (D-174): a short menu for the thing under the pointer (a game card, row or open Game Details; a collection in the
//      collection menu or Manage Collections; a HOME folder in HOME Settings; the empty part of the collection list). Its rows are the same
//      actions as the visible controls, which stay the way to reach them on touch screens (a long press is left alone). One menu at a time;
//      arrows, Enter/Space, Escape, Tab or a click outside; focus returns to the thing. The ContextMenu key and Shift+F10 open the focused
//      thing's menu. Text fields, links, selected text, the games' screens and HOME keep the browser's own behavior. Before an action runs
//      the thing is checked again (still there, the same collection open): nothing is done to a different game or collection. ----
const CTX={menu:null, ret:null, t:null, pointer:'mouse', kb:0};
addEventListener('pointerdown', e=>{ CTX.pointer=e.pointerType||'mouse'; }, true);
function ctxTarget(node){ if(!node||!node.closest) return null;
 if(node.closest('input,textarea,select,[contenteditable],a[href],canvas,video,.cpsurf,.cpmenu,#tv,#tpHome,#tpGame,.playv,#guide,#gsetm,.ctxmenu')) return null;
 const sel0=getSelection&&getSelection(); if(sel0&&!sel0.isCollapsed&&String(sel0).trim()&&sel0.containsNode&&sel0.containsNode(node, true)) return null;   // selected text: Copy
 const det=node.closest('#details'); if(det){ const id=det.classList.contains('open')&&det.dataset.game; return id&&gameOf(id)?{kind:'game', where:'details', id, el:det.querySelector('.star')||det}:null; }
 const g=node.closest('#gridView [data-g],#listView [data-g]'); if(g) return gameOf(g.dataset.g)?{kind:'game', where:'list', id:g.dataset.g, el:g}:null;
 const c=node.closest('#csQuick [data-cs],#csQuick [data-cs-cur],.csrow[data-id]'); if(c){ const id=c.dataset.cs||c.dataset.id||(c.dataset.csCur&&CS&&CS.active); return id&&CS&&CS.items[id]?{kind:'coll', id, el:c, manager:!!c.closest('.csrow')}:null; }
 const f=node.closest('.hf-f[data-f]'); if(f) return typeof TV!=='undefined'&&TV.model&&homeFolder(TV.model,f.dataset.f)?{kind:'folder', id:f.dataset.f, el:f}:null;
 if(node.closest('#gridView,#listView,.cvempty')&&!node.closest('button,[role=button],[data-g],th')&&CV_MODE==='collection'&&CS_STATE==='ok'&&csActive()) return {kind:'space', el:node.closest('#gridView,#listView,.cvempty')};
 return null; }
// the rows for a thing: [label, run, danger]; only what applies to it now
function ctxItems(t){
 if(t.kind==='game'){ const g=gameOf(t.id); const inColl=CV_MODE==='collection'&&sel.has(t.id); const out=[];
  if(inColl){ const s=rdyS().st.get(t.id); out.push(s&&s!=='ready'?['Locate Game File…', ()=>rdyLocate(t.id)]:['Play', ()=>cvPlay(t.id)]); }
  else if(g&&g.inc){ out.push(['Play', ()=>incPlay(g.inc)]); if(!sel.has(t.id)) out.push(['Add to Collection', ()=>incAddTo(g.inc, null)]); }
  else if(CV_MODE==='catalog'&&!(TITLES[g.title_id]||{versions:[]}).versions.some(v=>sel.has(v))) out.push(['Add Game File…', ()=>cvAddForTitle(t.id)]);
  if(t.where!=='details') out.push(['Game Details', ()=>openDetails(t.id)]);
  out.push([fav.has(t.id)?'Remove from Favorites':'Add to Favorites', ()=>favToggle(t.id)]);
  if(inColl){ out.push(['Show in HOME', ()=>openTv({focus:t.id})]); out.push(['Remove from This Collection', ()=>cvRemove(t.id), true]); }
  return out; }
 if(t.kind==='coll'){ const r=CS.items[t.id], m=t.manager, back=()=>{ if(m) csManager(); else closeColl(); }; const out=[];
  if(t.id!==CS.active) out.push(['Open', ()=>{ csSwitch(t.id); if(m) csManager(); }]);
  out.push(['Rename…', ()=>csNameDialog('Rename Collection', r.name, 'Rename', nm=>{ csRename(t.id, nm); back(); }, '', m)]);
  out.push(['Duplicate', ()=>{ const d=csDuplicate(t.id); if(m) csManager(); else if(d) csSwitch(d.id, true); }]);
  out.push(['Back Up…', ()=>{ if(m) closeColl(); ccsBackupDialog(t.id); }]);
  out.push(['Remove…', ()=>csDeleteDialog(t.id, m), true]); return out; }
 if(t.kind==='folder'){ const pick=b=>()=>{ HF.fid=t.id; hfRender(); const x=$(b); if(x&&!x.disabled) x.click(); };
  return [['Rename…', pick('hfRen')], ['New Subfolder…', pick('hfSub')], ['Move Up', pick('hfUp')], ['Move Down', pick('hfDown')], ['Delete…', pick('hfDel'), true]]; }
 if(t.kind==='space') return [['Add Game Files…', ()=>cvAddFiles()], ['Included Games…', ()=>incBrowse()], ['Restore a Backup…', ()=>cvRestore()], ['Open HOME', ()=>openTv()]];
 return []; }
// the thing, checked again just before its action: still shown, the same collection and view
function ctxStill(t){ if(!t) return false;
 if(t.kind==='game'){ if(!gameOf(t.id)||CS.active!==t.coll||CV_MODE!==t.mode||(t.inColl&&!sel.has(t.id))) return false;   // the same collection and view; still a member when the menu offered its actions
  if(t.where==='details'){ const d=$('details'); return d.classList.contains('open')&&d.dataset.game===t.id; }
  return !!document.querySelector(`#gridView [data-g="${CSS.escape(t.id)}"],#listView [data-g="${CSS.escape(t.id)}"]`); }   // still listed (a redraw of the same list is fine)
 if(t.kind==='coll') return !!(CS&&CS.items[t.id]);
 if(t.kind==='folder') return typeof TV!=='undefined'&&TV.model&&!!homeFolder(TV.model,t.id)&&!!$('hfBody');
 if(t.kind==='space') return CS.active===t.coll&&CV_MODE==='collection'&&CS_STATE==='ok';
 return false; }
function ctxClose(back){ const m=CTX.menu; if(!m) return; CTX.menu=null; m.remove(); const r=CTX.ret; CTX.ret=null; if(back&&r&&r.isConnected&&r.focus) r.focus({preventScroll:true}); }
function ctxOpen(t, x, y){ ctxClose(false); document.querySelectorAll('.menu').forEach(m=>m.hidden=true); if(typeof hvHide==='function') hvHide();
 const items=ctxItems(t); if(!items.length) return false; t.coll=CS&&CS.active; t.mode=CV_MODE; t.inColl=t.kind==='game'&&CV_MODE==='collection'&&sel.has(t.id);
 const m=document.createElement('div'); m.className='menu ctxmenu'; m.setAttribute('role','menu'); m.setAttribute('aria-label', t.kind==='game'?`${cvTitle(t.id)}`:t.kind==='coll'?CS.items[t.id].name:t.kind==='folder'?homeFolder(TV.model,t.id).name:'This collection');
 m.innerHTML=items.map((it,i)=>miHtml({label:esc(it[0]), cls:it[2]?'danger':'', attrs:`data-ctx="${i}"`})).join(''); document.body.appendChild(m);
 const w=m.offsetWidth, h=m.offsetHeight; m.style.left=Math.max(8, Math.min(x, innerWidth-w-8))+'px'; m.style.top=Math.max(8, Math.min(y, innerHeight-h-8))+'px';
 // focus goes back where it was (the focused thing, when the key opened the menu); after a right-click on plain content, to the thing's own control
 const a=document.activeElement; CTX.menu=m; CTX.t=t; CTX.ret=a&&a!==document.body&&a!==document.documentElement?a:(t.el&&(t.el.matches('button,[tabindex]')?t.el:(t.el.querySelector('.topen')||t.el.querySelector('button:not([disabled])'))))||a;
 m.addEventListener('click', e=>{ const b=e.target.closest('[data-ctx]'); if(!b) return; e.stopPropagation(); const it=items[+b.dataset.ctx], tt=CTX.t; ctxClose(true);
  if(!ctxStill(tt)){ toast('That item changed before the menu was used. Nothing was done.'); return; } it[1](); });
 const f=menuItems(m)[0]; if(f) f.focus({preventScroll:true}); return true; }
document.addEventListener('contextmenu', e=>{ if(performance.now()-CTX.kb<400){ e.preventDefault(); return; }   // the key already opened it
 if((e.pointerType||CTX.pointer)==='touch') return;   // a long press on a touch screen: the page's own controls do the same, nothing pops up
 const t=ctxTarget(e.target); if(!t) return; if(ctxOpen(t, e.clientX, e.clientY)) e.preventDefault(); });
addEventListener('keydown', e=>{ const m=CTX.menu;
 if(m){ const items=menuItems(m), i=items.indexOf(document.activeElement);
  if(e.key==='Escape'||e.key==='Tab'){ e.preventDefault(); e.stopImmediatePropagation(); ctxClose(true); return; }   // closes only this menu (not Details or a dialog under it)
  if(e.key==='ArrowDown'){ e.preventDefault(); e.stopImmediatePropagation(); (items[i+1]||items[0]).focus(); return; }
  if(e.key==='ArrowUp'){ e.preventDefault(); e.stopImmediatePropagation(); (items[i-1]||items[items.length-1]).focus(); return; }
  if(e.key==='Home'||e.key==='End'){ e.preventDefault(); e.stopImmediatePropagation(); (e.key==='Home'?items[0]:items[items.length-1]).focus(); return; }
  if(e.key==='ArrowLeft'||e.key==='ArrowRight'){ e.preventDefault(); e.stopImmediatePropagation(); return; }
  if(e.key==='ContextMenu'||(e.key==='F10'&&e.shiftKey)){ e.preventDefault(); e.stopImmediatePropagation(); CTX.kb=performance.now(); ctxClose(true); return; }
  return; }
 if(e.key==='ContextMenu'||(e.key==='F10'&&e.shiftKey)){ const a=document.activeElement; const t=ctxTarget(a); if(!t) return;
  const r=(a&&a.getBoundingClientRect)?a.getBoundingClientRect():{left:20,bottom:20}; if(ctxOpen(t, r.left+8, r.bottom+4)){ e.preventDefault(); e.stopImmediatePropagation(); CTX.kb=performance.now(); } } }, true);
addEventListener('pointerdown', e=>{ if(CTX.menu&&!CTX.menu.contains(e.target)) ctxClose(false); }, true);
addEventListener('resize', ()=>ctxClose(false)); addEventListener('blur', ()=>ctxClose(false));
document.addEventListener('scroll', e=>{ if(CTX.menu&&!CTX.menu.contains(e.target)) ctxClose(false); }, true);
