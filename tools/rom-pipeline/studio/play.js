// ---- Play on PC (Classic Cart Studio for Windows; M3): the game player, a separate window ----
// Game Details offers "Play on PC" for the exact version it shows, enabled only when this computer holds that version's ROM file (checked by
// content identity in the Background Helper, never by title); otherwise the button stays off and says why. Preview on TV offers "Play This
// HOME on PC": the HOME as it is configured now (theme, games, order, folders, artwork, texts, imported games) in the same player. One game
// window at a time; while it is open the buttons say so. The website (no Background Helper) never shows these actions. Saves live in the
// player's per-user save library, shared by both ways in (tools/player). Settings: Studio menu > Play on PC Settings.
const PLAY={running:false, title:null, poll:0, settings:null};
function playCapable(){ return typeof COMPANION!=='undefined'&&COMPANION.ok&&COMPANION.desktop&&(COMPANION.capabilities||[]).includes('play-on-pc'); }
function playBusyText(){ return `${PLAY.title||'A game'} is open in the game window. Close it there first.`; }
async function playAvailability(id){
 let r; try{ r=await capi('/api/play/availability?ids='+encodeURIComponent(id),{timeout:30000}); }catch(e){ r={ok:false}; }
 if(!r||!r.ok) return {playable:false, reason:'Whether this game can be played could not be checked. Try again in a moment.'};
 if(!r.player) return {playable:false, reason:r.problem||'The game player is missing from this installation. Run the Classic Cart Studio installer again to repair it.'};
 return (r.games||{})[id]||{playable:false, reason:'Whether this game can be played could not be checked.'}; }
// ---- Game Details: Play on PC ----
async function playDetails(id){
 if(!playCapable()) return; const tech=$('dTech'); const bar=tech&&tech.parentElement; if(!bar||$('dPlay')) return;
 const b=document.createElement('button'); b.type='button'; b.className='btn sm playbtn'; b.id='dPlay'; b.disabled=true; b.innerHTML='<span aria-hidden="true">▶</span> Play on PC'; b.title='Checking this computer for this version’s ROM file…';
 bar.insertBefore(b, tech); const note=document.createElement('p'); note.className='small playnote'; note.id='dPlayNote'; note.hidden=true; bar.after(note);
 const a=await playAvailability(id); if(!b.isConnected||$('details').dataset.game!==id) return;   // Game Details moved on meanwhile
 playDetailsState(a); b.onclick=()=>playDirect(id); }
function playDetailsState(a){ const b=$('dPlay'), note=$('dPlayNote'); if(!b) return; if(a) b._a=a; a=b._a||a||{};
 const why=PLAY.running?playBusyText():(a.playable?'':(a.reason||''));
 b.disabled=!!why; b.title=why||'Play this exact version in the game window'; if(note){ note.hidden=!why; note.textContent=why?'Play on PC: '+why:''; } }
(function(){ const _od=openDetails; openDetails=function(id){ const r=_od.apply(this,arguments); try{ playDetails(id); }catch(e){} return r; }; })();
async function playDirect(id){
 if(PLAY.running){ toast(playBusyText()); return; } const b=$('dPlay'); if(b) b.disabled=true;
 let r; try{ r=await capi('/api/play/direct',{method:'POST',body:{id},timeout:60000}); }catch(e){ r={ok:false,error:String(e)}; }
 if(!r.ok){ if(typeof CCSM!=='undefined') CCSM.fail('play','other'); toast(r.error||'The game could not start.'); if(b) playDetailsState(); return; }
 if(typeof CCSM!=='undefined') CCSM.played(); playStarted(r); }   // the game window is open with the game
// ---- Preview on TV: Play This HOME on PC ----
function playTvButton(){ const tools=document.querySelector('#tv .tvtools'); if(!tools) return; let b=$('tvPlay');
 if(!playCapable()||(typeof tpAppCapable==='function'&&tpAppCapable())){ if(b) b.hidden=true; return; }   // the Preview plays the HOME itself now (D-104): Pop Out gives it its own window
 if(!b){ b=document.createElement('button'); b.type='button'; b.className='btn sm playbtn'; b.id='tvPlay'; b.innerHTML='<span aria-hidden="true">▶</span> Play This HOME on PC'; tools.appendChild(b); b.onclick=playHome; }
 const sample=!!(TV.model&&TV.model.sample); b.hidden=sample; b.disabled=PLAY.running; b.title=PLAY.running?playBusyText():'Play this HOME and its games in the game window, as the console would show them'; }
(function(){ const _ot=openTv; openTv=function(){ const r=_ot.apply(this,arguments); try{ playTvButton(); }catch(e){} return r; }; })();
async function playHome(){
 if(PLAY.running){ toast(playBusyText()); return; } const b=$('tvPlay'); if(!b) return; b.disabled=true; const label=b.innerHTML; b.textContent='Preparing the HOME…';
 const home=tvnHome(); if(typeof TVN!=='undefined'&&TVN.on&&TVN.theme) home.theme=TVN.theme;   // the theme the preview shows (its theme buttons and START, Change Theme set TVN.theme)
 let r; try{ r=await capi('/api/play/home',{method:'POST',body:{manifest:tvnManifest(),home},timeout:300000}); }catch(e){ r={ok:false,error:String(e)}; }
 b.innerHTML=label; if(!r.ok){ b.disabled=false; toast(r.error||'The HOME could not start.'); return; }
 playStarted(r);
 if(r.unavailable_count) toast(`${r.playable} of ${r.games} games can be played on this computer. The others stay in the HOME and say why when chosen.`); }
// ---- while the game window is open ----
function playStarted(r){ PLAY.running=true; PLAY.title=r.title||null; playRefresh(); toast(`${PLAY.title||'The game'} is open in the game window. Hold SELECT + START for its menu.`);
 clearInterval(PLAY.poll); PLAY.poll=setInterval(playPoll, 1500); }
async function playPoll(){ let s; try{ s=await capi('/api/play/status',{timeout:8000}); }catch(e){ return; } if(!s||!s.ok||s.running) return;
 clearInterval(PLAY.poll); PLAY.poll=0; PLAY.running=false; const last=s.last||{}, res=last.result||{}, ev=(res.events||[]).slice(-1)[0]||{};
 playRefresh();
 if(res.exit==='error'&&res.error) toast(`The game window closed: ${res.error}`);
 else if(/^saved_slot/.test(ev.ended||'')) toast(`${last.title||'The game'} was saved to Slot ${ev.slot||''}. Next time, choose Load Last Used Save to continue.`);
 else if(ev.ended) toast(`${last.title||'The game window'} closed.`); }
function playRefresh(){ try{ playDetailsState(); }catch(e){} try{ playTvButton(); }catch(e){} }
// ---- Play on PC Settings (Studio menu; the player reads them when a game window opens) ----
const PLAY_ACTIONS=[['up','Up'],['down','Down'],['left','Left'],['right','Right'],['a','A'],['b','B'],['select','SELECT'],['start','START']];
const PLAY_CODES={ArrowUp:'Up',ArrowDown:'Down',ArrowLeft:'Left',ArrowRight:'Right',Enter:'Enter',NumpadEnter:'Enter',Backspace:'Backspace',Space:'Space',Tab:'Tab',ShiftLeft:'LShift',ShiftRight:'RShift',
 ControlLeft:'LCtrl',ControlRight:'RCtrl',Home:'Home',End:'End',PageUp:'PageUp',PageDown:'PageDown',Insert:'Insert',Delete:'Delete',Comma:'Comma',Period:'Period',Slash:'Slash',Semicolon:'Semicolon',
 Quote:'Quote',BracketLeft:'LeftBracket',BracketRight:'RightBracket',Minus:'Minus',Equal:'Equals',Backslash:'Backslash',Backquote:'Backquote'};
function playKeyName(e){ const c=e.code||''; if(PLAY_CODES[c]) return PLAY_CODES[c]; let m=c.match(/^Key([A-Z])$/); if(m) return m[1]; m=c.match(/^Digit([0-9])$/); if(m) return m[1];
 m=c.match(/^Numpad([0-9])$/); if(m) return 'Numpad'+m[1]; m=c.match(/^F([1-9]|1[0-2])$/); if(m) return c; return null; }
function playKeyLabel(v){ return v?v.split('|').map(k=>({LShift:'Left Shift',RShift:'Right Shift',LCtrl:'Left Ctrl',RCtrl:'Right Ctrl',LeftBracket:'[',RightBracket:']',Backquote:'`'}[k]||k)).join(' or '):'(none)'; }
async function playSettings(){
 let r; try{ r=await capi('/api/play/settings',{timeout:15000}); }catch(e){ r={ok:false}; } if(!r.ok){ toast('The Play on PC settings could not be read.'); return; }
 PLAY.settings=JSON.parse(JSON.stringify(r.settings)); playSettingsDraw(); }
function playSettingsDraw(){ const s=PLAY.settings; const row=(p,a,l)=>`<tr><th scope="row">${l}</th>${['p1','p2'].map(pp=>`<td><button type="button" class="btn sm secondary pkey" data-p="${pp}" data-a="${a}">${esc(playKeyLabel(s.keys[pp][a]))}</button></td>`).join('')}</tr>`;
 const dup=playDuplicates(s);
 openColl('Play on PC Settings','Keys, controllers and display for the game window', `<div class="playset">
  <p class="small">Choose a key, then press the new key (Esc cancels; Esc always opens the game menu). Controllers: Xbox-compatible controllers work at once (A, B, Back = SELECT, Start); other USB or Bluetooth controllers are set up from the game menu, Controllers. The first two controllers join Players 1 and 2.</p>
  <p class="small">The keyboard plays the player no controller plays: Player 1, or Player 2 when a controller plays Player 1. The second keyboard player's keys are for two people sharing the keyboard; they play Player 2 only when no controller is connected.</p>
  <table class="playkeys"><thead><tr><th scope="col">NES button</th><th scope="col">Keyboard</th><th scope="col">Second keyboard player</th></tr></thead><tbody>${PLAY_ACTIONS.map(([a,l])=>row(null,a,l)).join('')}</tbody></table>
  ${dup.length?`<div class="msgbox warn">A key is used twice: ${esc(dup.join('; '))}.</div>`:''}
  <div class="playopts"><label><input type="radio" name="paspect" value="tv" ${s.display.aspect==='tv'?'checked':''}> TV proportions (NES pixels are slightly wide, as on a TV)</label>
   <label><input type="radio" name="paspect" value="square" ${s.display.aspect==='square'?'checked':''}> Square pixels</label>
   <label><input type="checkbox" id="pint" ${s.display.integer?'checked':''}> Whole-number scaling only (the sharpest picture; it may leave wider borders)</label>
   <label><input type="checkbox" id="pswap" ${s.swap_pads?'checked':''}> Swap Players 1 and 2 (the first controller plays Player 2; with one controller, the keyboard then plays Player 1)</label></div>
  <p class="small">Full screen: Alt+Enter or F11 in the game window; the game window remembers it. Holding SELECT + START for three seconds opens the game menu (save to a slot, load a slot, exit).</p>
  <div class="row" style="gap:8px;justify-content:flex-end"><button type="button" class="btn sm secondary" id="pReset">Reset to Defaults</button><button type="button" class="btn sm" id="pSave">Save Settings</button></div></div>`);
 document.querySelectorAll('.pkey').forEach(b=>b.onclick=()=>playCapture(b));
 $('pReset').onclick=()=>{ PLAY.settings={schema:'ccs-player-settings/1',keys:{p1:{up:'Up',down:'Down',left:'Left',right:'Right',a:'Z|Enter',b:'X|Backspace',select:'S',start:'M'},p2:{up:'I',down:'K',left:'J',right:'L',a:'H',b:'G',select:'T',start:'Y'}},display:{aspect:'tv',integer:false},swap_pads:false}; playSettingsDraw(); };
 $('pSave').onclick=async()=>{ const s=PLAY.settings; s.display.aspect=(document.querySelector('input[name=paspect]:checked')||{}).value||'tv'; s.display.integer=$('pint').checked; s.swap_pads=$('pswap').checked;
  let r; try{ r=await capi('/api/play/settings',{method:'POST',body:s,timeout:15000}); }catch(e){ r={ok:false,error:String(e)}; }
  if(r.ok){ closeColl(); toast(PLAY.running?'Saved. The open game window keeps its settings until it closes.':'Saved. The next game window uses them.'); } else toast(r.error||'The settings could not be saved.'); }; }
function playDuplicates(s){ const seen={}, out=[]; ['p1','p2'].forEach(p=>PLAY_ACTIONS.forEach(([a,l])=>(s.keys[p][a]||'').split('|').filter(Boolean).forEach(k=>{ const who=`${p==='p1'?'Keyboard':'Second keyboard player'} ${l}`; if(seen[k]) out.push(`${playKeyLabel(k)} for ${seen[k]} and ${who}`); else seen[k]=who; }))); return out; }
function playCapture(b){ b.textContent='Press a key…'; b.classList.add('capturing');
 const done=()=>{ removeEventListener('keydown', on, true); playSettingsDraw(); const nb=document.querySelector(`.pkey[data-p="${b.dataset.p}"][data-a="${b.dataset.a}"]`); if(nb) nb.focus(); };   // the redrawn button keeps the focus
 const on=e=>{ e.preventDefault(); e.stopPropagation(); if(e.key==='Escape'){ done(); return; } const k=playKeyName(e); if(!k){ b.textContent='That key cannot be used'; return; }
  PLAY.settings.keys[b.dataset.p][b.dataset.a]=k; done(); };
 addEventListener('keydown', on, true); }
(function(){ const show=()=>{ const ok=playCapable(); const i=$('mPlaySet'), l=$('mPlayL'); if(i) i.hidden=!ok; if(l) l.hidden=!ok; };
 const _cp=typeof connPill==='function'?connPill:null; if(_cp) connPill=function(){ const r=_cp.apply(this,arguments); try{ show(); }catch(e){} return r; };
 document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('#mPlaySet'); if(!b) return; document.querySelectorAll('.menu').forEach(x=>x.hidden=true); playSettings(); });   /* navigation.js keeps its closeMenus private */ setTimeout(show, 1500); })();
