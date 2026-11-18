// ---- explained words: the console words this Studio's own pages use (CCS HOME, CCS Mod, CCS Nucleus, Hakchi2 CE, startup software…), with a dotted
// underline where they appear; a click, a tap, or Tab then Enter or Space opens a short explanation just under the word; Escape, Close or a click
// elsewhere shuts it (Escape and Close put the focus back on the word). One open at a time; nothing on hover. The words and explanations are the
// shared glossary (tools/rom-pipeline/studio/glossary.json), the same as in CCS Console Workbench. Explained only inside [data-terms] areas (Help,
// Install on NES Classic, and the authored text of the dialogs that use these words); never inside links, buttons, labels, form fields, code,
// headings, game lists or [data-no-terms] (legal text, Credits, the Installation Terms, a collection's own name). Classic Cart Studio itself is
// not explained here: this is it.
const GLOSSARY=__GLOSSARY__;
const GL_WEB=GLOSSARY.terms.filter(t=>t.key!=='ccs');
const GL_FORMS=GL_WEB.flatMap(t=>t.forms.map(f=>[f,t])).sort((a,b)=>b[0].length-a[0].length);
const GL_SKIP='a,button,label,input,select,textarea,option,code,pre,kbd,script,style,svg,h1,h2,h3,h4,h5,h6,summary,.gterm,#gpop,.list,table.pv,[data-no-terms],section[data-ch="legal"],section[data-ch="terms"],section[data-ch="credits"],section[data-ch="about"]';
const GL_BLOCK='p,li,td,th,dd,dt,figcaption,.cap,.msg,.small,div';
function glJoins(c){ return /[\p{L}\p{N}\-_\\/@]/u.test(c); }
function glBlocked(s){ const b=new Array(s.length).fill(false); const re=/\S+/g; let m; while((m=re.exec(s))){ const w=m[0].replace(/[.,;:)\]"'\u201d\u2019]+$/,''); if(w.includes('://')||w.includes('\\')||w.includes('/')||/^www\./i.test(w)||/[A-Za-z0-9_\-]\.[A-Za-z0-9]{2,8}$/.test(w)) for(let k=m.index;k<m.index+m[0].length;k++) b[k]=true; } return b; }
// where the glossary's words are in a piece of text: the first place of each term in its block (used), the product names once per page (page)
function glMatches(s, used, page){
 const out=[]; if(!s) return out; const bl=glBlocked(s); let i=0;
 const endOk=j=>j>=s.length||(!glJoins(s[j])&&!(s[j]==='.'&&j+1<s.length&&/[\p{L}\p{N}]/u.test(s[j+1])));
 while(i<s.length){
  if(bl[i]||(i>0&&(glJoins(s[i-1])||s[i-1]==='.'))){ i++; continue; }
  let hit=null, len=0;
  for(const [f,t] of GL_FORMS){
   if(!s.startsWith(f,i)) continue; let n=f.length;
   if(s[i+n]==='s'&&/[\p{L}]/u.test(f[f.length-1])&&endOk(i+n+1)) n++; else if(!endOk(i+n)) continue;
   if(bl.slice(i,i+n).some(Boolean)) continue; hit=t; len=n; break;
  }
  if(!hit){ i++; continue; }
  const set=hit.scope==='page'?page:used; if(!set.has(hit.key)){ set.add(hit.key); out.push([i,len,hit]); }
  i+=len;
 }
 return out;
}
// explains the words in an area: every text node not yet explained, in reading order
function glApply(root){
 if(!root||!root.isConnected) return;
 const pages=new Map(), blocks=new Map();
 const w=document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {acceptNode:n=>n.nodeValue.trim()&&!(n.parentElement&&n.parentElement.closest(GL_SKIP))?NodeFilter.FILTER_ACCEPT:NodeFilter.FILTER_REJECT});
 const nodes=[]; let n; while((n=w.nextNode())) nodes.push(n);
 for(const t of nodes){
  const el=t.parentElement; if(!el) continue;
  const pg=el.closest('section[data-ch]')||el.closest('[data-terms]')||root, bk=el.closest(GL_BLOCK)||pg;
  if(!pages.has(pg)) pages.set(pg, new Set([...pg.querySelectorAll('.gterm')].map(x=>x.dataset.g).filter(k=>(GL_WEB.find(g=>g.key===k)||{}).scope==='page')));
  if(!blocks.has(bk)) blocks.set(bk, new Set([...bk.querySelectorAll('.gterm')].map(x=>x.dataset.g)));
  const m=glMatches(t.nodeValue, blocks.get(bk), pages.get(pg)); if(!m.length) continue;
  const f=document.createDocumentFragment(); let at=0; const s=t.nodeValue;
  for(const [i,len,term] of m){
   if(i>at) f.append(s.slice(at,i));
   const b=document.createElement('button'); b.type='button'; b.className='gterm'; b.dataset.g=term.key; b.textContent=s.slice(i,i+len);
   b.setAttribute('aria-expanded','false'); b.setAttribute('aria-haspopup','dialog'); b.setAttribute('aria-describedby','gpopHint');
   f.append(b); at=i+len;
  }
  if(at<s.length) f.append(s.slice(at));
  t.replaceWith(f);
 }
}
let glOpenBtn=null;
function glClose(refocus){ const p=document.getElementById('gpop'); const b=glOpenBtn; glOpenBtn=null; if(p){ p.hidden=true; p.remove(); } if(b){ b.setAttribute('aria-expanded','false'); if(refocus&&b.isConnected) b.focus(); } }
function glOpen(b){
 if(glOpenBtn===b){ glClose(true); return; } glClose(false);
 const t=GL_WEB.find(x=>x.key===b.dataset.g); if(!t) return;
 const p=document.createElement('div'); p.id='gpop'; p.setAttribute('role','dialog'); p.setAttribute('aria-labelledby','gpopT');
 p.innerHTML=`<b id="gpopT">${esc(t.word)}</b><p>${esc(t.text)}</p>${t.tech?`<p class="gtech">Technical name: ${esc(t.tech)}</p>`:''}<button type="button" class="btn sm" id="gpopClose">Close</button>`;
 // inside the same dialog as the word (so a dialog's own keyboard handling keeps it), placed just under the word, inside the window
 (b.closest('.mbox,[role=dialog]')||document.body).append(p);
 b.setAttribute('aria-expanded','true'); glOpenBtn=b;
 try{ b.scrollIntoView({block:'nearest', inline:'nearest', behavior:'instant'}); }catch(e){}   // the word in sight (a keyboard user's focus may still be scrolling)
 glPlace(true);
 document.getElementById('gpopClose').onclick=()=>glClose(true);
 p.addEventListener('keydown',e=>{ if(e.key==='Escape'){ e.preventDefault(); e.stopPropagation(); glClose(true); } });
 document.getElementById('gpopClose').focus({preventScroll:true});
}
// just under the word (above it when there is no room below), inside the window; it follows the word when the page scrolls, and closes when
// the word scrolls out of sight
function glPlace(first){
 const p=document.getElementById('gpop'), b=glOpenBtn; if(!p||!b) return;
 const r=b.getBoundingClientRect(), vw=document.documentElement.clientWidth, vh=document.documentElement.clientHeight, pw=Math.min(380, vw-32);
 if(!b.isConnected||(!first&&(r.bottom<0||r.top>vh))){ glClose(false); return; }
 p.style.width=pw+'px'; p.style.left=Math.max(16, Math.min(r.left, vw-pw-16))+'px';
 const ph=p.offsetHeight; p.style.top=Math.max(8, r.bottom+6+ph<=vh-8||r.top-6-ph<8?r.bottom+6:r.top-6-ph)+'px';
}
document.addEventListener('click',e=>{ const b=e.target.closest&&e.target.closest('.gterm'); if(b){ e.preventDefault(); e.stopPropagation(); glOpen(b); return; } if(glOpenBtn&&!(e.target.closest&&e.target.closest('#gpop'))) glClose(false); }, true);
document.addEventListener('keydown',e=>{ if(e.key==='Escape'&&glOpenBtn){ e.preventDefault(); e.stopImmediatePropagation(); glClose(true); return; }
 const b=(e.key==='Enter'||e.key===' ')&&!e.repeat&&e.target.closest&&e.target.closest('.gterm'); if(b){ e.preventDefault(); e.stopImmediatePropagation(); glOpen(b); } }, true);   // Enter or Space on a word opens it
addEventListener('scroll',e=>{ if(glOpenBtn&&!(e.target.closest&&e.target.closest('#gpop'))) glPlace(); }, true);
addEventListener('resize',()=>{ if(glOpenBtn) glPlace(); });
// the areas: explained when they appear and whenever their content changes
(function(){
 const hint=document.createElement('span'); hint.id='gpopHint'; hint.className='sr'; hint.textContent='Explained word: opens a short explanation'; document.body.append(hint);
 const pending=new Set(); let raf=0;
 const run=()=>{ raf=0; for(const r of pending) glApply(r); pending.clear(); };
 const add=r=>{ pending.add(r); if(!raf) raf=requestAnimationFrame(run); };
 const mark=el=>{ const r=el&&el.closest&&el.closest('[data-terms]'); if(r) add(r); };
 // an area that changed inside a marked one, or marked parts that just arrived (a dialog's body)
 new MutationObserver(ms=>{ for(const m of ms){ if(m.target.closest&&m.target.closest('#gpop')) continue; mark(m.target.nodeType===1?m.target:m.target.parentElement);
  for(const n of m.addedNodes||[]){ if(n.nodeType!==1) continue; if(n.matches('[data-terms]')) add(n); n.querySelectorAll('[data-terms]').forEach(add); } } }).observe(document.body,{childList:true,subtree:true,characterData:true});
 document.querySelectorAll('[data-terms]').forEach(r=>glApply(r));
})();
