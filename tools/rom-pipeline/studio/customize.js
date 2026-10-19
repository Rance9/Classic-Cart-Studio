// ---- Content customization (N3, Classic Cart Studio for Windows only): the person's own box art, Official description and Game
//      overview for one game version, beside the canonical content (never over it). The website has no backend and shows no editing
//      controls; the canonical resolution below (approved texts, library overviews, authentic art, mock cover) applies everywhere.
//      Effective values:   art       = user replacement > authentic box art > mock cover (studio/mock-cover.js)
//                          overview  = user > approved curated overview > library overview > none
//                          official  = user (heading "Official description (edited)", or "Custom description" when no approved text exists) > approved > empty
//      Rows keep their canonical values in a hidden _canon record; g.art / g.official_description / g.game_overview / g.official_label
//      are the EFFECTIVE values, so the list, grid, tray, hover, Details and the HOME preview model all follow a change at once.
//      Overrides are keyed by the exact version id (game_id) and live in the Studio's data folder (tools/companion/overrides.py). ----
var OVR={on:false,loaded:false,loading:false,games:{},lib:null,libTried:false,cur:null};
var OVED=null, OVA=null;
const OV_DISCARD='Discard your unsaved changes?';
const OV_PEN='<svg viewBox="0 0 16 16" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" aria-hidden="true"><path d="M10.8 2.6l2.6 2.6-7.9 7.9-3.3.7.7-3.3z"/><path d="M9.3 4.1l2.6 2.6"/></svg>';
const OV_BOX=[340,466];   // the console HOME's cover box: images are fitted whole inside it (contain), never stretched
function ovEnabled(){ return !!(typeof COMPANION!=='undefined'&&COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('content-overrides')); }
function ovEditable(g){ return !!(g&&g.source!=='personal'&&OVR.on&&OVR.loaded); }
// ---- canonical layer ----
function ovCanon(g){ if(!g._canon){ const d=(DATA.descriptions||{})[g.game_id]; Object.defineProperty(g,'_canon',{value:{art:g.art||'',art_source:g.art_source||'',official:(g.official_description!==undefined?g.official_description:(d&&d.official))||null,overview:(g.game_overview!==undefined?g.game_overview:(d&&d.overview))||null,had:g.official_description!==undefined||g.game_overview!==undefined||!!d},enumerable:false,writable:true}); } return g._canon; }
// canonical text for library identities, resolved exactly as the console packager does (tools/companion/native_home/config.py):
// another version of a curated title shares that title's approved overview; any other identity uses its researched library
// overview; a verified Official description of the identity's own release comes from the library file too
function ovLibOverview(g){ if(!g) return null; const cur=(typeof CUR_BY_TITLE!=='undefined'&&g.title_id)?CUR_BY_TITLE[g.title_id]:null; const cid=cur&&(cur.game_id||cur);
 const d=cid&&cid!==g.game_id?(DATA.descriptions||{})[cid]:null; if(d&&d.overview) return d.overview;
 const L=OVR.lib; if(!L) return null; const t=L[g.title_id]; return (t&&typeof t.overview==='string'&&t.overview.trim())?t.overview:null; }
function ovLibOfficial(g){ const L=OVR.lib; if(!L||!g||!g.title_id) return null; const t=L[g.title_id]; return (t&&typeof t.official==='string'&&t.official.trim())?t.official:null; }
function ovCanonArt(g){ const c=ovCanon(g); return c.art||g.art_generated||(typeof mockCoverUrl==='function'?mockCoverUrl(g):''); }
function ovApply(g){ if(!g||g.source==='personal'||!g.game_id) return g; const c=ovCanon(g); const o=(OVR.on&&OVR.games[g.game_id])||{};
 const art=o.art&&o.art.present!==false?o.art:null;
 if(art){ g.art=artUrl('overrides/display/'+g.game_id+'.jpg?v='+encodeURIComponent(art.key||'')); g.art_source='uploaded'; } else { g.art=c.art; g.art_source=c.art_source; }
 g.art_custom=!!art;
 const coff=c.official||ovLibOfficial(g); const off=o.official?o.official.text:coff; g.official_label=o.official?(coff?'edited':'custom'):null;
 const ov=o.overview?o.overview.text:(c.overview||ovLibOverview(g)); g.overview_custom=!!o.overview;
 if(c.had||off||ov){ g.official_description=off||null; g.game_overview=ov||null; } else { g.official_description=undefined; g.game_overview=undefined; }
 return g; }
function ovRows(){ return (DATA.games||[]).concat(typeof EXT_ROWS!=='undefined'?EXT_ROWS:[]); }
function ovApplyAll(){ ovRows().forEach(ovApply); }
function ovRepaint(){ try{ if(typeof TV!=='undefined') TV.proj=null; }catch(e){} try{ render(); }catch(e){} ovRefreshDetails(); }
// library overviews (reports/descriptions-library.json, schema ccs-descriptions-library/1): optional, possibly partial; loaded after start-up
function ovLoadLibrary(){ if(OVR.libTried) return; OVR.libTried=true; if(location.protocol==='file:') return;
 fetch('descriptions-library.json',{cache:'force-cache'}).then(r=>r.ok?r.json():null).then(j=>{ if(!j||j.schema!=='ccs-descriptions-library/1'||!j.titles||typeof j.titles!=='object') return; OVR.lib=j.titles; ovApplyAll(); ovRepaint(); }).catch(()=>{}); }
// overrides from the Studio on this computer (desktop app only)
async function ovReload(){ if(!ovEnabled()) return false; OVR.loading=true; try{ const r=await capi('/api/overrides'); if(r&&r.ok){ OVR.games=r.games||{}; OVR.on=true; OVR.loaded=true; ovApplyAll(); ovRepaint(); ovMenu(); return true; } } finally{ OVR.loading=false; } return false; }
function ovMaybeLoad(){ if(ovEnabled()&&!OVR.loaded&&!OVR.loading) ovReload(); }
const _ovConnPill=connPill; connPill=function(){ _ovConnPill.apply(this,arguments); try{ ovMaybeLoad(); }catch(e){} };
// ---- Details: decoration (Windows app only) ----
const _ovOpenDetails=openDetails;
openDetails=function(id){ if(ovTextDirty()&&OVED.id!==id&&!confirm(OV_DISCARD)) return; OVED=null; _ovOpenDetails(id); OVR.cur=id; ovDecorate(id); };
function ovRefreshDetails(force){ if(!OVR.cur||!$('details').classList.contains('open')) return; if(OVED&&!force) return; const b=$('detailsBox'), sc=b.scrollTop; OVED=null; _ovOpenDetails(OVR.cur); ovDecorate(OVR.cur); b.scrollTop=sc; }
function ovPen(field,label){ return `<button type="button" class="ovpen" data-ovedit="${field}" title="${esc(label)}" aria-label="${esc(label)}">${OV_PEN}</button>`; }
function ovChip(t){ return `<span class="ovchip" title="${esc(t||'Your own version; the original is kept and can be restored')}">Customized</span>`; }
function ovDecorate(id){ const g=byId[id], box=$('detailsBox'); if(!g||!box||!ovEditable(g)) return;
 const cov=box.querySelector('#dCover'); if(cov&&!cov.querySelector('.ovpen-art')) cov.insertAdjacentHTML('beforeend',`<button type="button" class="ovpen ovpen-art" id="ovArtBtn" title="Edit box art" aria-label="Edit box art">${OV_PEN}</button>`);
 const cap=box.querySelector('#dArtCap'); if(cap&&g.art_custom&&!cap.querySelector('.ovchip')) cap.insertAdjacentHTML('beforeend',' '+ovChip('Your own image; the original box art is kept and can be restored'));
 const o=box.querySelector('#dOfficial h3'), v=box.querySelector('#dOverview h3');
 if(o){ o.insertAdjacentHTML('beforeend',ovPen('official',g.official_label==='custom'?'Edit custom description':'Edit official description')+(g.official_label?ovChip():'')); }
 if(v){ v.insertAdjacentHTML('beforeend',ovPen('overview','Edit game overview')+(g.overview_custom?ovChip():'')); }
 if(!o&&v){ v.parentElement.insertAdjacentHTML('beforeend',`<button type="button" class="ovadd" data-ovedit="official" title="Write your own description for this game. It is shown as &quot;Custom description&quot;, never as publisher text.">${OV_PEN}<span>Add a custom description</span></button>`); } }
// ---- text editors (one field at a time, inline in its section) ----
function ovTextDirty(){ const t=$('ovText'); return !!(OVED&&t&&t.value!==OVED.initial); }
function ovEditText(field){ const g=byId[OVR.cur]; if(!g||!ovEditable(g)) return; if(OVED){ if(OVED.field===field) return; if(ovTextDirty()&&!confirm(OV_DISCARD)) return; ovRefreshDetails(true); }
 const c=ovCanon(g); const canon=field==='official'?(c.official||ovLibOfficial(g)):(c.overview||ovLibOverview(g)); const o=(OVR.games[g.game_id]||{})[field]||null; const cur=o?o.text:(canon||'');
 OVED={field,id:g.game_id,initial:cur,hasOverride:!!o};
 let sec=$(field==='official'?'dOfficial':'dOverview');
 if(!sec&&field==='official'){ const v=$('dOverview'); (v||$('detailsBox').querySelector('h2')).insertAdjacentHTML(v?'beforebegin':'afterend','<div class="sec dtext" id="dOfficial"></div>'); sec=$('dOfficial'); }
 const add=$('detailsBox').querySelector('.ovadd'); if(add) add.remove();
 const head=field==='official'?(c.official?(o?'Official description (edited)':'Official description'):'Custom description'):'Game overview';
 const note=field==='official'?(c.official?'Your text replaces the approved Official description on this computer and in the console preview; the original is kept.':'This game has no approved Official description. Text you write is shown as "Custom description", never as publisher text.'):'Your text replaces the Game overview on this computer and in the console preview; the original is kept.';
 sec.classList.add('oved'); sec.innerHTML=`<h3>${esc(head)} <span class="ovchip ed">Editing</span></h3><p class="small ovnote">${esc(note)}</p>
  <div class="ovedgrid" id="ovGrid"><div class="ovta"><textarea id="ovText" rows="9" spellcheck="true" aria-label="${esc(head)}">${esc(cur)}</textarea></div>
  <div class="ovcanon" id="ovCanon" hidden><div class="lbl">Original${field==='official'&&c.official?' (approved Official description)':''}</div>${canon?descParas(canon):`<p class="miss">${field==='official'?'No approved Official description exists for this game.':'No original overview exists for this game.'}</p>`}</div></div>
  <div class="acts"><button type="button" class="btn sm ghost" id="ovCanonBtn" aria-pressed="false" aria-controls="ovCanon">Show original</button><button type="button" class="btn sm secondary" id="ovReset" ${o?'':'disabled'} title="${o?'Remove your text and show the original again':'Nothing to reset: this field shows the original'}">Reset to Original</button><span class="sp"></span><span class="small ovmsg" id="ovMsg" role="status"></span><button type="button" class="btn sm" id="ovCancel">Cancel</button><button type="button" class="btn sm primary" id="ovSave">Save</button></div>`;
 const ta=$('ovText'); ta.focus(); ta.setSelectionRange(ta.value.length,ta.value.length); sec.scrollIntoView({block:'nearest'}); }
function ovMsg(id,t,cls){ const m=$(id); if(m){ m.textContent=t||''; m.className='small ovmsg'+(cls?' '+cls:''); } }
function ovCancelEdit(){ if(ovTextDirty()&&!confirm(OV_DISCARD)) return false; ovRefreshDetails(true); return true; }
async function ovSaveText(){ if(!OVED) return; const t=$('ovText').value, ed=OVED;
 if(!t.trim()){ ovMsg('ovMsg','Write some text, or use Reset to Original.','bad'); return; }
 if(t.trim()===String(ed.initial).trim()&&!ed.hasOverride){ ovRefreshDetails(true); return; }   // unchanged original: nothing to keep
 $('ovSave').disabled=true; ovMsg('ovMsg','Saving…'); const r=await hkPost('/api/overrides/text',{id:ed.id,field:ed.field,text:t});
 if(!r||!r.ok){ $('ovSave').disabled=false; ovMsg('ovMsg','Not saved: '+((r&&r.error)||'unknown error'),'bad'); return; }
 OVED=null; await ovReload(); toast(ed.field==='official'?'Description saved. The original stays available with Reset to Original.':'Game overview saved. The original stays available with Reset to Original.'); }
async function ovResetField(field, id){ const g=byId[id]; if(!g) return;
 const what=field==='art'?'box art':field==='official'?((ovCanon(g).official)?'Official description':'custom description'):'Game overview';
 if(!confirm(field==='art'?'Use the original box art again? Your imported image is removed from the Studio’s data (the file you chose is not touched).':field==='official'&&!ovCanon(g).official?'Remove your custom description? This game has no approved Official description, so the section disappears.':`Reset the ${what} to the original? Your text is removed.`)) return false;
 const r=await hkPost('/api/overrides/reset',{id,field}); if(!r||!r.ok){ toast('Not reset: '+((r&&r.error)||'unknown error')); return false; }
 if(field!=='art') OVED=null; await ovReload(); toast(`The original ${what==='custom description'?'state':what} is shown again.`); return true; }
$('detailsBox').addEventListener('click',e=>{ const t=e.target;
 const ed=t.closest&&t.closest('[data-ovedit]'); if(ed){ e.preventDefault(); ovEditText(ed.dataset.ovedit); return; }
 if(t.closest&&t.closest('#ovArtBtn')){ e.preventDefault(); ovArtOpen(OVR.cur); return; }
 const b=t.closest&&t.closest('button'); if(!b||!OVED) return;
 if(b.id==='ovSave') ovSaveText(); else if(b.id==='ovCancel') ovCancelEdit(); else if(b.id==='ovReset') ovResetField(OVED.field,OVED.id);
 else if(b.id==='ovCanonBtn'){ const c=$('ovCanon'), on=c.hidden; c.hidden=!on; $('ovGrid').classList.toggle('two',on); b.setAttribute('aria-pressed',String(on)); b.textContent=on?'Hide original':'Show original'; } });
// a dirty editor is never lost silently: Escape cancels the editor (with the warning) instead of closing Details; the close button
// and the backdrop ask first
document.addEventListener('keydown',e=>{ if(e.key!=='Escape') return;
 if(OVA&&$('ovArt')&&$('ovArt').classList.contains('open')){ e.preventDefault(); e.stopImmediatePropagation(); ovArtClose(); return; }
 if(OVED&&$('details').classList.contains('open')){ e.preventDefault(); e.stopImmediatePropagation(); ovCancelEdit(); } },true);
document.addEventListener('click',e=>{ const t=e.target; if(!(t.id==='details'||(t.closest&&t.closest('#dClose')))) return;
 if(ovTextDirty()&&!confirm(OV_DISCARD)){ e.preventDefault(); e.stopImmediatePropagation(); return; } OVED=null; },true);
// ---- artwork editor ----
function ovArtEnsure(){ if($('ovArt')) return; const m=document.createElement('div'); m.id='ovArt'; m.className='modal ovart'; m.setAttribute('role','dialog'); m.setAttribute('aria-modal','true'); m.setAttribute('aria-labelledby','ovArtTitle');
 m.innerHTML=`<div class="mbox"><div class="mhd"><h2 id="ovArtTitle">Edit box art</h2><span class="small" id="ovArtSub"></span><button class="btn sm" id="ovArtClose" type="button">Close</button></div>
  <div class="ovagrid"><div class="ovacol">
   <div class="seg ovview" role="group" aria-label="Compare"><button type="button" class="btn sm" data-ovview="mine">Your image</button><button type="button" class="btn sm" data-ovview="orig">Original</button></div>
   <div class="ovstage" id="ovStage" aria-live="polite"></div><div class="small ovinfo" id="ovInfo"></div></div>
  <div class="ovacol side"><div class="ovdrop" id="ovDrop"><b>Import an image</b><span class="small">PNG, JPEG, WebP, GIF or BMP, any size. Drop it here or choose a file; the file is copied, so moving or deleting it later changes nothing.</span><button type="button" class="btn sm" id="ovArtPick">Choose image…</button><input type="file" id="ovArtFile" accept="image/png,image/jpeg,image/webp,image/gif,image/bmp,.png,.jpg,.jpeg,.webp,.gif,.bmp" hidden></div>
   <div class="lbl">Framing</div><div class="seg" role="group" aria-label="Framing"><button type="button" class="btn sm" id="ovFit">Fit whole image</button><button type="button" class="btn sm" id="ovCrop">Crop</button></div>
   <label class="small ovzoom">Zoom <input type="range" id="ovZoom" min="1" max="5" step="0.01" value="1" aria-label="Crop zoom"></label><div class="small" id="ovCropHelp">The whole image is fitted inside the cover box (never stretched or cut). Choose Crop to frame part of it; drag the frame or use the arrow keys.</div>
   <div class="lbl">Console preview</div><div class="ovconsole"><div class="ovbox" id="ovConsole" role="img" aria-label="How the cover looks in the console's 340 by 466 cover box"></div><span class="small">The console's 340 × 466 cover box, shown at half size.</span></div></div></div>
  <div class="acts"><button type="button" class="btn secondary" id="ovArtReset">Reset to Original</button><span class="sp"></span><span class="small ovmsg" id="ovArtMsg" role="status"></span><button type="button" class="btn" id="ovArtCancel">Cancel</button><button type="button" class="btn primary" id="ovArtSave">Save</button></div></div>`;
 document.body.appendChild(m);
 m.addEventListener('click',e=>{ if(e.target===m) ovArtClose(); });
 ['dragenter','dragover'].forEach(ev=>m.addEventListener(ev,e=>{ e.preventDefault(); if(OVA&&!OVA.busy) $('ovDrop').classList.add('over'); }));
 m.addEventListener('dragleave',e=>{ if(e.target===m||e.target===$('ovDrop')) $('ovDrop').classList.remove('over'); });
 m.addEventListener('drop',e=>{ e.preventDefault(); $('ovDrop').classList.remove('over'); const f=e.dataTransfer&&e.dataTransfer.files&&e.dataTransfer.files[0]; if(f) ovArtUpload(f); });
 $('ovArtClose').onclick=()=>ovArtClose(); $('ovArtCancel').onclick=()=>ovArtClose(); $('ovArtPick').onclick=()=>$('ovArtFile').click();
 $('ovArtFile').onchange=e=>{ const f=e.target.files&&e.target.files[0]; e.target.value=''; if(f) ovArtUpload(f); };
 m.querySelector('.ovview').onclick=e=>{ const b=e.target.closest('[data-ovview]'); if(!b||b.disabled) return; OVA.view=b.dataset.ovview; ovArtPaint(); };
 $('ovFit').onclick=()=>{ OVA.crop=null; ovArtPaint(); }; $('ovCrop').onclick=()=>{ if(!OVA.crop){ OVA.crop=ovCropAt(1,0.5,0.5); } OVA.view='mine'; ovArtPaint(); const f=$('ovFrame'); if(f) f.focus(); };
 $('ovZoom').oninput=e=>{ if(!OVA.crop) return; const c=OVA.crop; ovSetCrop(ovCropAt(Number(e.target.value),c.x+c.w/2,c.y+c.h/2)); };
 $('ovArtReset').onclick=async()=>{ if(!OVA||!OVA.cur) return; const id=OVA.id; if(await ovResetField('art',id)){ OVA=null; $('ovArt').classList.remove('open'); } };
 $('ovArtSave').onclick=ovArtSave; }
function ovArtRec(){ return OVA&&(OVA.staged||OVA.cur)||null; }
function ovArtDirty(){ return !!(OVA&&(OVA.staged||JSON.stringify(OVA.crop||null)!==OVA.initialCrop)); }
function ovArtOpen(id){ const g=byId[id]; if(!g||!ovEditable(g)) return; if(OVED&&ovTextDirty()&&!confirm(OV_DISCARD)) return; if(OVED) ovRefreshDetails(true);
 ovArtEnsure(); const o=(OVR.games[id]||{}).art||null; OVA={id,g,cur:o&&o.present!==false?o:null,staged:null,crop:o?o.crop||null:null,view:o?'mine':'orig',busy:false,initialCrop:JSON.stringify((o&&o.crop)||null),ret:document.activeElement};
 $('ovArtSub').textContent=g.display_title+(g.nointro_name&&g.nointro_name!==g.display_title?' · '+g.nointro_name:''); ovMsg('ovArtMsg',''); $('ovArt').classList.add('open'); ovArtPaint(); $('ovArtPick').focus(); }
function ovArtClose(){ if(!OVA) { $('ovArt')&&$('ovArt').classList.remove('open'); return true; } if(OVA.busy) return false; if(ovArtDirty()&&!confirm(OV_DISCARD)) return false; const ret=OVA.ret; OVA=null; $('ovArt').classList.remove('open'); if(ret&&ret.focus&&ret.isConnected) ret.focus(); else { const b=$('ovArtBtn'); if(b) b.focus(); } return true; }
function ovWorkUrl(rec){ return artUrl('overrides/work/'+OVA.id+'.jpg?m='+String(rec.sha256||'').slice(0,16)); }
// crop geometry: the frame keeps the console box shape (340:466 in pixels); zoom 1 = the largest such frame inside the image
function ovBase(){ const r=ovArtRec(); const W=r.width, H=r.height, A=OV_BOX[0]/OV_BOX[1]; return W/H>=A?{w:A*H/W,h:1}:{w:1,h:W/(A*H)}; }
function ovCropAt(z,cx,cy){ const b=ovBase(); z=Math.max(1,Math.min(5,z||1)); const w=b.w/z, h=b.h/z; return {x:Math.max(0,Math.min(1-w,cx-w/2)),y:Math.max(0,Math.min(1-h,cy-h/2)),w,h}; }
function ovZoomOf(c){ const b=ovBase(); return c?Math.max(1,Math.min(5,b.w/c.w)):1; }
function ovSetCrop(c){ OVA.crop=c; ovPlaceFrame(); ovConsolePaint(); ovArtButtons(); }
function ovFitBox(W,H,BW,BH){ const s=Math.min(BW/W,BH/H); return {s,w:W*s,h:H*s,x:(BW-W*s)/2,y:(BH-H*s)/2}; }
function ovArtButtons(){ const rec=ovArtRec(), dirty=ovArtDirty(); $('ovArtSave').disabled=!dirty||OVA.busy; $('ovArtReset').disabled=!OVA.cur||OVA.busy; $('ovArtReset').title=OVA.cur?'Remove your image and use the original box art again':'Nothing to reset: the original box art is in use';
 $('ovFit').disabled=$('ovCrop').disabled=!rec||OVA.busy; $('ovFit').classList.toggle('active',!!rec&&!OVA.crop); $('ovCrop').classList.toggle('active',!!OVA.crop); $('ovZoom').disabled=!OVA.crop; if(OVA.crop) $('ovZoom').value=ovZoomOf(OVA.crop).toFixed(2);
 $('ovArtPick').disabled=OVA.busy; }
function ovArtPaint(){ if(!OVA) return; const g=OVA.g, rec=ovArtRec(); if(!rec) OVA.view='orig';
 $('ovArt').querySelectorAll('[data-ovview]').forEach(b=>{ b.classList.toggle('active',b.dataset.ovview===OVA.view); b.disabled=b.dataset.ovview==='mine'&&!rec; b.setAttribute('aria-pressed',String(b.dataset.ovview===OVA.view)); });
 const st=$('ovStage');
 if(OVA.view==='orig'){ const src=ovCanonArt(g); st.innerHTML=`${src?`<img class="ovorig" src="${esc(src)}" alt="Original box art">`:'<div class="ph"><span>NES</span></div>'}${rec?'':'<div class="ovempty">No custom image yet. Import one to replace the box art; the original stays available.</div>'}`;
  $('ovInfo').textContent=g.art_custom||rec?`Original: ${ovCanon(g).art?'authentic box art':'generated cover (no authentic box art found)'}`:(ovCanon(g).art?'Authentic box art (the original)':'Generated cover: no authentic box art was found for this version'); }
 else { st.innerHTML=`<img class="ovwork" id="ovWork" src="${esc(ovWorkUrl(rec))}" alt="Your image" draggable="false"><div class="ovframe" id="ovFrame" tabindex="0" role="slider" aria-label="Crop frame: drag or use the arrow keys" hidden></div>`;
  $('ovInfo').innerHTML=`${rec.width.toLocaleString()} × ${rec.height.toLocaleString()} ${esc(String(rec.format||'').toUpperCase())} · ${esc(rec.original_name||'image')}${OVA.staged?' · <b>not saved yet</b>':''}`;
  ovPlaceFrame(); ovFrameWire(); }
 ovConsolePaint(); ovArtButtons(); }
function ovStageGeom(){ const st=$('ovStage'), rec=ovArtRec(); return ovFitBox(rec.width,rec.height,st.clientWidth,st.clientHeight); }
function ovPlaceFrame(){ const im=$('ovWork'), f=$('ovFrame'); if(!im||!f) return; const G=ovStageGeom(); Object.assign(im.style,{left:G.x+'px',top:G.y+'px',width:G.w+'px',height:G.h+'px'});
 const c=OVA.crop; f.hidden=!c; $('ovStage').classList.toggle('cropping',!!c); if(!c) return; Object.assign(f.style,{left:(G.x+c.x*G.w)+'px',top:(G.y+c.y*G.h)+'px',width:(c.w*G.w)+'px',height:(c.h*G.h)+'px'}); f.setAttribute('aria-valuetext',`frame at ${Math.round(c.x*100)}% across, ${Math.round(c.y*100)}% down, ${Math.round(c.w*100)}% wide`); }
function ovFrameWire(){ const f=$('ovFrame'); if(!f) return; let drag=null;
 f.onpointerdown=e=>{ if(!OVA.crop) return; e.preventDefault(); f.setPointerCapture(e.pointerId); drag={x:e.clientX,y:e.clientY,c:Object.assign({},OVA.crop),G:ovStageGeom()}; };
 f.onpointermove=e=>{ if(!drag) return; const c=drag.c; ovSetCrop({x:Math.max(0,Math.min(1-c.w,c.x+(e.clientX-drag.x)/drag.G.w)),y:Math.max(0,Math.min(1-c.h,c.y+(e.clientY-drag.y)/drag.G.h)),w:c.w,h:c.h}); };
 f.onpointerup=f.onpointercancel=()=>{ drag=null; };
 f.onkeydown=e=>{ const c=OVA.crop; if(!c) return; const st=e.shiftKey?0.05:0.01; const d={ArrowLeft:[-st,0],ArrowRight:[st,0],ArrowUp:[0,-st],ArrowDown:[0,st]}[e.key]; if(d){ e.preventDefault(); ovSetCrop({x:Math.max(0,Math.min(1-c.w,c.x+d[0])),y:Math.max(0,Math.min(1-c.h,c.y+d[1])),w:c.w,h:c.h}); } else if(e.key==='+'||e.key==='='||e.key==='-'){ e.preventDefault(); ovSetCrop(ovCropAt(ovZoomOf(c)*(e.key==='-'?1/1.1:1.1),c.x+c.w/2,c.y+c.h/2)); } };
 f.onwheel=e=>{ if(!OVA.crop) return; e.preventDefault(); const c=OVA.crop; ovSetCrop(ovCropAt(ovZoomOf(c)*(e.deltaY>0?1/1.08:1.08),c.x+c.w/2,c.y+c.h/2)); }; }
// console preview: the result fitted whole inside the 340 x 466 box (half size), exactly as the console HOME shows covers
function ovConsolePaint(){ const box=$('ovConsole'); if(!box||!OVA) return; const BW=OV_BOX[0]/2, BH=OV_BOX[1]/2, rec=ovArtRec();
 if(!rec){ const src=ovCanonArt(OVA.g); box.innerHTML=src?`<img src="${esc(src)}" alt="" style="width:100%;height:100%;object-fit:contain">`:''; return; }
 const W=rec.width, H=rec.height, c=OVA.crop||{x:0,y:0,w:1,h:1}; const R=ovFitBox(c.w*W,c.h*H,BW,BH);
 box.innerHTML=`<div class="ovclip" style="left:${R.x}px;top:${R.y}px;width:${R.w}px;height:${R.h}px"><img src="${esc(ovWorkUrl(rec))}" alt="" style="left:${-c.x*W*R.s}px;top:${-c.y*H*R.s}px;width:${W*R.s}px;height:${H*R.s}px"></div>`; }
async function ovArtUpload(file){ if(!OVA||OVA.busy) return; if(OVA.staged&&!confirm('Replace the image you imported but have not saved yet?')) return;
 OVA.busy=true; ovArtButtons(); ovMsg('ovArtMsg',`Importing “${file.name||'image'}” (${fmt(file.size||0)})…`);
 try{ const qs=new URLSearchParams({id:OVA.id,name:file.name||'image',stage:'1'}); let j;
  try{ const r=await fetch(CO.base+'/api/overrides/art?'+qs.toString(),lnaInit({method:'POST',headers:hdrs({'Content-Type':file.type||'application/octet-stream'}),body:file,cache:'no-store'})); j=await r.json().catch(()=>({ok:false,error:'unexpected response (HTTP '+r.status+')'})); }
  catch(e){ j={ok:false,error:'the Studio backend on this computer did not answer'}; }
  if(!OVA) return; if(!j.ok){ ovMsg('ovArtMsg','Not accepted: '+(j.error||'unknown error'),'bad'); return; }
  OVA.staged=j.art; OVA.crop=null; OVA.view='mine'; ovMsg('ovArtMsg',`Imported ${j.art.width.toLocaleString()} × ${j.art.height.toLocaleString()}: fitted whole. Save to use it.`,'ok');
 } finally{ if(OVA){ OVA.busy=false; ovArtPaint(); } } }
async function ovArtSave(){ if(!OVA||!ovArtDirty()||OVA.busy) return; const id=OVA.id; OVA.busy=true; ovArtButtons(); ovMsg('ovArtMsg','Saving…'); let r;
 if(OVA.staged) r=await hkPost('/api/overrides/art',{id,op:'commit',master:OVA.staged.master,crop:OVA.crop||null});
 else r=await hkPost('/api/overrides/crop',{id,crop:OVA.crop||null});
 if(!OVA) return; OVA.busy=false;
 if(!r||!r.ok){ ovArtButtons(); ovMsg('ovArtMsg','Not saved: '+((r&&r.error)||'unknown error'),'bad'); return; }
 OVA=null; $('ovArt').classList.remove('open'); await ovReload(); toast('Box art saved. The original stays available with Reset to Original.'); const b=$('ovArtBtn'); if(b) b.focus(); }
addEventListener('resize',()=>{ if(OVA&&$('ovArt').classList.contains('open')){ ovPlaceFrame(); } });
// ---- backup: the Studio data file carries the customized texts and the facts about custom images; the ZIP carries the images too ----
function ovExportMeta(){ const out={}; Object.entries(OVR.games||{}).forEach(([vid,g])=>{ const e={}; if(g.official) e.official=g.official; if(g.overview) e.overview=g.overview; if(g.art) e.art={custom_image:true,sha256:g.art.sha256,width:g.art.width,height:g.art.height,format:g.art.format,crop:g.art.crop||null,imported:g.art.imported,original_name:g.art.original_name}; out[vid]=e; }); return {schema:'ccs-overrides/1',note:'Your own texts and the facts about your images; the image files themselves are in Save / Load, Export Customized Content (ZIP).',games:out}; }
const _ovStudioData=studioData; studioData=function(){ const d=_ovStudioData.apply(this,arguments); if(OVR.loaded&&Object.keys(OVR.games||{}).length) d.content_overrides=ovExportMeta(); return d; };
function ovMenu(){ const menu=$('appMenu'); if(!menu) return; let b=$('ovExport'); if(!b){ const anchor=$('sdImport'); (anchor||menu).insertAdjacentHTML(anchor?'afterend':'beforeend','<button id="ovExport" class="mi" role="menuitem" hidden><b>Back Up Custom Artwork &amp; Text…</b><span class="d">Your own box art and descriptions, with the original image files, as one ZIP backup.</span></button>'); b=$('ovExport'); b.onclick=ovExportZip; } b.hidden=!ovEnabled(); }
async function ovExportZip(){ $('appMenu').hidden=true; try{ const r=await fetch(CO.base+'/api/overrides/export',lnaInit({headers:hdrs(),cache:'no-store'})); if(!r.ok) throw new Error('HTTP '+r.status); const b=await r.blob(); const res=await ccsSaveFile(`Classic Cart Studio Custom Content ${csStamp()}.zip`, b, 'Custom Content Backup'); if(res.ok) toast('Custom artwork and text saved to '+res.name+' ('+fmt(b.size)+')'); }catch(e){ toast('Could not export: '+(e&&e.message||e)); } }
// ---- start-up: canonical layer for every library row now; library overviews after the first paint; overrides once the backend is up ----
ovApplyAll(); setTimeout(ovLoadLibrary,600); ovMaybeLoad();
// ---- Box art source (discreet attribution in Game Details): the scan's recorded source file from art/index.json, read once from the
//      local copy (works offline); only sources that are recorded are shown, nothing is invented, and the link is optional reference ----
let ART_INDEX=null;
async function artCredit(g){ try{ const cap=$('dArtCap'); if(!cap||!g||g.art_custom) return; const m=/art\/([0-9a-f]{16})\.jpg$/.exec(g.art||g.art_family||''); if(!m) return;
 if(!ART_INDEX) ART_INDEX=await (await fetch('art/index.json')).json();
 const e=ART_INDEX&&ART_INDEX[m[1]]; if(!e||!e.url||!/^https:\/\//.test(e.url)||cap.querySelector('.artsrc')||!$('dArtCap')) return;
 cap.insertAdjacentHTML('beforeend',` · <a class="artsrc" href="${esc(e.url)}" target="_blank" rel="noopener" title="${esc((e.source||'source')+': '+(e.file||''))}">source file</a>`); }catch(e){} }
