// ---- Classic Cart Studio intelligent search: ONE engine for the website catalog, the console HOME runtime and Preview on TV.
// Loaded inline by build_html.py (the page) and by require() in node tests. Any change here changes every consumer at once.
// exact title, title starts with the query, every word in the title, then matches in the other fields.
const SEARCH_ROMAN={i:'1',ii:'2',iii:'3',iv:'4',v:'5',vi:'6',vii:'7',viii:'8',ix:'9',x:'10',xi:'11',xii:'12'};
const SEARCH_ABBR={dr:'doctor',mr:'mister',mrs:'missus',ms:'miss',st:'saint',jr:'junior',vs:'versus',bros:'brothers',ft:'featuring',feat:'featuring',pt:'part',vol:'volume',n:'and',nd:'and'};
const SEARCH_ABBR_REV=Object.fromEntries(Object.entries(SEARCH_ABBR).map(([k,v])=>[v,k]));
function searchNorm(s){ return String(s||'').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/&/g,' and ').replace(/['\u2019`]/g,'').replace(/[^a-z0-9]+/g,' ').trim(); }
function searchTokens(s){ return searchNorm(s).split(' ').filter(Boolean).map(t=>SEARCH_ROMAN[t]||t); }
function searchIndexOf(g){
 if(g._si) return g._si;
 const title=searchTokens(g.display_title); const idx=(toks)=>({toks, joined:toks.join(''), acro:toks.map(t=>t[0]).join('')});
 g._si={title:idx(title), rest:idx(searchTokens([g.nointro_name,g.builtin_title,g.publisher,g.year,g.genre].filter(Boolean).join(' ')))};
 return g._si;
}
function searchEdit1(a,b){ // true when a and b differ by at most one edit (insert, delete or substitute)
 if(a===b) return true; const la=a.length, lb=b.length; if(Math.abs(la-lb)>1) return false;
 let i=0,j=0,edits=0; while(i<la&&j<lb){ if(a[i]===b[j]){ i++; j++; continue; } if(++edits>1) return false; if(la>lb) i++; else if(lb>la) j++; else { i++; j++; } }
 return edits+(la-i)+(lb-j)<=1;
}
function searchTokenHit(qt, ht, fz){
 if(ht.startsWith(qt)) return true;
 const ex=SEARCH_ABBR[qt]; if(ex&&ht.startsWith(ex)) return true;
 const ab=SEARCH_ABBR_REV[ht]; if(ab&&(ab===qt||ab.startsWith(qt))) return true;
 const hx=SEARCH_ABBR[ht]; if(hx&&hx.startsWith(qt)) return true;
 if(fz&&qt.length>=5&&ht.length>=qt.length-1){ if(searchEdit1(qt,ht)) return true; if(ht.length>qt.length&&searchEdit1(qt,ht.slice(0,qt.length))) return true; }
 return false;
}
function searchFieldHit(q, ix, fz){
 if(ix.joined.includes(q.joined)) return true;                       // "megaman" vs "mega man", "dr mario" vs "dr. mario"
 if(q.toks.length===1&&q.joined.length>=3&&ix.acro.startsWith(q.joined)) return true;   // "smb3"
 return q.toks.every(qt=>ix.toks.some(ht=>searchTokenHit(qt,ht,fz)));
}
let SEARCH_Q=null, SEARCH_QRAW='', SEARCH_FZ={raw:null, fz:false};
// one typo is tolerated only when the query matches nothing as typed (so 'contra' does not also pull in 'Remote Control')
function searchFuzzy(q, list){ if(SEARCH_FZ.raw===SEARCH_QRAW&&SEARCH_FZ.list===list) return SEARCH_FZ.fz; const fz=!list.some(g=>searchScore(g,q,false)>0); SEARCH_FZ={raw:SEARCH_QRAW, fz, list}; return fz; }
function searchQuery(raw){ if(raw===SEARCH_QRAW&&SEARCH_Q) return SEARCH_Q; SEARCH_QRAW=raw; const toks=searchTokens(raw); SEARCH_Q=toks.length?{toks, joined:toks.join(''), norm:searchNorm(raw)}:null; return SEARCH_Q; }
// 0 = not a match; 1 = exact title; 2 = title starts with the query; 3 = every word in the title; 4 = found in the other fields
function searchScore(g, q, fz){
 const si=searchIndexOf(g); const t=si.title;
 if(t.toks.join(' ')===q.toks.join(' ')) return 1;
 if(t.joined.startsWith(q.joined)) return 2;
 if(searchFieldHit(q,t,fz)) return 3;
 if(searchFieldHit(q,si.rest,fz)) return 4;
 return 0;
}

// node (tests) export; in the page these are plain globals
if(typeof module!=='undefined'&&module.exports){ module.exports={searchNorm,searchTokens,searchIndexOf,searchEdit1,searchTokenHit,searchFieldHit,searchFuzzy,searchQuery,searchScore,SEARCH_ABBR,SEARCH_ROMAN,searchEngineState:()=>({SEARCH_Q,SEARCH_QRAW,SEARCH_FZ})}; }
