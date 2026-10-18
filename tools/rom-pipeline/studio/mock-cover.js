// ---- Mock box cover on the page: a canvas port of tools/rom-pipeline/mock_cover.py cover() (the approved generated-placeholder design).
//      Same SHA-256 seed, same Mulberry32 stream, same integer pixel math and drawing order, so the landscape is pixel-identical to the
//      Python drawing; the title uses the page's Press Start 2P webfont, the publisher / year line the page's sans (text may differ a
//      little from the Python fonts). Used for every row / version without authentic art and without a user replacement: list
//      thumbnails, grid, tray, Details and the HOME preview model. Returns a cached PNG data URL per game id (and title / subline). ----
var MOCK={W:300,H:420,ready:false,loading:false,cache:new Map(),max:900,
 PAL:[[[10,14,48],[90,40,110],[40,24,80],[22,14,50],[12,8,30],[228,0,15],[255,255,255]],[[8,30,60],[30,120,170],[16,60,100],[10,36,66],[6,20,40],[30,110,220],[255,255,255]],
  [[6,28,24],[40,140,90],[14,70,50],[8,44,34],[4,24,20],[16,140,80],[255,240,120]],[[30,10,40],[190,70,60],[90,30,50],[50,18,36],[24,10,20],[230,120,20],[40,20,10]],
  [[12,12,40],[60,60,160],[28,28,90],[16,16,60],[8,8,32],[120,50,200],[255,220,250]],[[5,25,35],[20,150,160],[10,70,80],[6,44,52],[3,24,30],[20,160,170],[255,255,255]],
  [[40,14,10],[220,120,40],[120,50,20],[70,28,14],[30,12,8],[240,90,30],[255,245,200]]],
 PX:'"Press Start 2P", monospace', UI:'"Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,Roboto,sans-serif'};
// synchronous SHA-256 (the seed is needed while a row is being painted; crypto.subtle is asynchronous)
function mockSha256(str){ const b=new TextEncoder().encode(str); const K=[0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
 const H=[0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19]; const L=b.length; const n=((L+9+63)>>6)<<6; const m=new Uint8Array(n); m.set(b); m[L]=0x80; const bits=L*8; for(let i=0;i<4;i++) m[n-1-i]=(bits>>>(8*i))&255; m[n-5]=Math.floor(bits/4294967296)&255;
 const w=new Uint32Array(64); const r=(x,k)=>(x>>>k)|(x<<(32-k));
 for(let o=0;o<n;o+=64){ for(let i=0;i<16;i++) w[i]=(m[o+4*i]<<24)|(m[o+4*i+1]<<16)|(m[o+4*i+2]<<8)|m[o+4*i+3]; for(let i=16;i<64;i++){ const s0=r(w[i-15],7)^r(w[i-15],18)^(w[i-15]>>>3), s1=r(w[i-2],17)^r(w[i-2],19)^(w[i-2]>>>10); w[i]=(w[i-16]+s0+w[i-7]+s1)>>>0; }
  let [a,bb,c,d,e,f,g,h]=H; for(let i=0;i<64;i++){ const S1=r(e,6)^r(e,11)^r(e,25), ch=(e&f)^(~e&g), t1=(h+S1+ch+K[i]+w[i])>>>0, S0=r(a,2)^r(a,13)^r(a,22), mj=(a&bb)^(a&c)^(bb&c), t2=(S0+mj)>>>0; h=g; g=f; f=e; e=(d+t1)>>>0; d=c; c=bb; bb=a; a=(t1+t2)>>>0; }
  H[0]=(H[0]+a)>>>0; H[1]=(H[1]+bb)>>>0; H[2]=(H[2]+c)>>>0; H[3]=(H[3]+d)>>>0; H[4]=(H[4]+e)>>>0; H[5]=(H[5]+f)>>>0; H[6]=(H[6]+g)>>>0; H[7]=(H[7]+h)>>>0; }
 const out=new Uint8Array(32); H.forEach((v,i)=>{ out[4*i]=v>>>24; out[4*i+1]=(v>>>16)&255; out[4*i+2]=(v>>>8)&255; out[4*i+3]=v&255; }); return out; }
// the same stream as mock_cover.Mulberry32 (all arithmetic in uint32)
function mockRng(seed){ let a=seed>>>0; const R={next(){ a=(a+0x6D2B79F5)>>>0; let t=a; t=Math.imul(t^(t>>>15),t|1)>>>0; t=(t^((t+(Math.imul(t^(t>>>7),t|61)>>>0))>>>0))>>>0; return ((t^(t>>>14))>>>0)/4294967296; }};
 R.below=n=>Math.trunc(R.next()*n); R.between=(x,y)=>x+R.below(y-x); R.pick=s=>s[R.below(s.length)]; return R; }
function mockSeed(id){ const h=mockSha256(String(id)); return [h[0]%MOCK.PAL.length, (h[0]|(h[1]<<8)|(h[2]<<16)|(h[3]<<24))>>>0]; }
// the 75 x 62 pixel night landscape, returned as RGB bytes (scaled 4x with nearest neighbor by the caller)
function mockLandscape(rnd,pal,w,h){ w=w||75; h=h||62; const [top,hor,far,near,ground]=pal; const px=new Uint8Array(w*h*3); const set=(x,y,c)=>{ const i=(y*w+x)*3; px[i]=c[0]; px[i+1]=c[1]; px[i+2]=c[2]; };
 for(let y=0;y<h;y++){ const t=y/(h-1); const k=Math.pow(t,1.6); const c=[0,1,2].map(i=>Math.trunc(top[i]+(hor[i]-top[i])*k)); for(let x=0;x<w;x++) set(x,y,c); }
 for(let s=0;s<28;s++){ const x=rnd.below(w); const y=rnd.below(Math.trunc(h*0.55)); set(x,y,rnd.next()<0.6?[235,235,245]:[160,170,210]); }
 const mx=rnd.between(8,w-8), my=rnd.between(6,18), r=rnd.pick([3,4,5]);
 for(let dy=-r;dy<=r;dy++) for(let dx=-r;dx<=r;dx++) if(dx*dx+dy*dy<=r*r) set(mx+dx,my+dy,[250,240,200]);
 for(const [col,base,amp] of [[far,Math.trunc(h*0.58),10],[near,Math.trunc(h*0.74),7]]){ let yy=base; const hs=[]; for(let x=0;x<w;x++){ yy+=rnd.pick([-1,0,0,1]); yy=Math.max(base-amp,Math.min(base+amp,yy)); hs.push(yy); } for(let x=0;x<w;x++) for(let y=hs[x];y<h;y++) set(x,y,col); }
 const gl=ground.map(c=>Math.min(255,c+18)); for(let y=Math.trunc(h*0.86);y<h;y++) for(let x=0;x<w;x++) set(x,y,(y+Math.floor(x/3))%3?ground:gl);
 const nd=near.map(c=>Math.max(0,c-6)); for(let s=0;s<3;s++){ const x=rnd.between(4,w-6); const by=Math.trunc(h*0.74)-4; const ht=rnd.between(5,12); for(let y=by-ht;y<by+6;y++) for(let dx=0;dx<3;dx++) set(x+dx,y,nd); set(x+1,by-ht-1,[255,214,102]); }
 return px; }
function mockWrap(ctx,title,maxw){ const measure=(t,size)=>{ ctx.font=size+'px '+MOCK.PX; return ctx.measureText(t).width; };
 const wrap=size=>{ const lines=[]; let cur=''; for(const wd of title.split(' ')){ const t=(cur+' '+wd).trim(); if(measure(t,size)<=maxw||!cur) cur=t; else { lines.push(cur); cur=wd; } } if(cur) lines.push(cur); return lines; };
 for(const size of [18,16,14,12,10]){ const lines=wrap(size); if(lines.length<=3&&lines.every(l=>measure(l,size)<=maxw)) return [lines,size]; }
 let lines=wrap(10); if(lines.length>3){ let last=lines.slice(2).join(' '); while(last&&measure(last+'...',10)>maxw) last=last.slice(0,-1); lines=lines.slice(0,2).concat([last.trimEnd()+'...']); } return [lines,10]; }
// draws the cover on a 300 x 420 canvas: the exact order of mock_cover.cover() (PIL rectangles are inclusive of both corners: +1)
function mockDraw(title,publisher,year,id){ const W=MOCK.W,H=MOCK.H; const cv=document.createElement('canvas'); cv.width=W; cv.height=H; const x=cv.getContext('2d'); x.imageSmoothingEnabled=false;
 const [pi,seed]=mockSeed(id); const pal=MOCK.PAL[pi], plate=pal[5], ink=pal[6]; const rnd=mockRng(seed); const rgb=c=>`rgb(${c[0]},${c[1]},${c[2]})`;
 const rect=(x0,y0,x1,y1,c)=>{ x.fillStyle=rgb(c); x.fillRect(x0,y0,Math.min(x1,W-1)-x0+1,Math.min(y1,H-1)-y0+1); };
 const outline=(x0,y0,x1,y1,c,wd)=>{ rect(x0,y0,x1,y0+wd-1,c); rect(x0,y1-wd+1,x1,y1,c); rect(x0,y0,x0+wd-1,y1,c); rect(x1-wd+1,y0,x1,y1,c); };
 const text=(s,tx,ty,size,c,font)=>{ x.font=size+'px '+(font||MOCK.PX); x.textBaseline='alphabetic'; x.fillStyle=rgb(c); x.fillText(s,tx,ty+size); };   // PIL draws from the ascender line; Press Start 2P's ascender is 1 em, descender 0
 rect(0,0,W,H,[14,14,18]);
 const lw=75,lh=62, px=mockLandscape(rnd,pal,lw,lh); const img=x.createImageData(lw*4,lh*4); for(let yy=0;yy<lh*4;yy++) for(let xx=0;xx<lw*4;xx++){ const s=((yy>>2)*lw+(xx>>2))*3, d=(yy*lw*4+xx)*4; img.data[d]=px[s]; img.data[d+1]=px[s+1]; img.data[d+2]=px[s+2]; img.data[d+3]=255; } x.putImageData(img,0,40);
 rect(0,0,W,40,[8,8,10]); rect(0,40,W,42,plate);
 text('CLASSIC CART STUDIO',14,16,8,[190,190,200]);
 [10,16,12].forEach((hh,i)=>rect(W-48+i*12,30-hh,W-40+i*12,30,plate));
 const [lines,size]=mockWrap(x,String(title||id),W-44); const lhh=size+8, ph=18+lines.length*lhh; const y0=288-ph-10;
 rect(12,y0,W-12,y0+ph,plate); outline(12,y0,W-12,y0+ph,[0,0,0],2); outline(16,y0+4,W-16,y0+ph-4,plate.map(c=>Math.min(255,c+60)),1);
 let y=y0+11; for(const ln of lines){ text(ln,24,y+2,size,[0,0,0]); text(ln,22,y,size,ink); y+=lhh; }
 rect(0,288,W,H,[10,10,13]); rect(0,288,W,291,plate);
 let sub=[publisher,year].filter(Boolean).join(' · ')||'Nintendo Entertainment System'; x.font='15px '+MOCK.UI; while(x.measureText(sub).width>W-32&&sub.length>4) sub=sub.slice(0,-2).trimEnd()+'…';
 x.textBaseline='alphabetic'; x.fillStyle='rgb(205,205,212)'; x.fillText(sub,16,319);
 x.beginPath(); if(x.roundRect) x.roundRect(16.5,334.5,70,22,4); else x.rect(16.5,334.5,70,22); x.fillStyle='rgb(40,40,48)'; x.fill(); x.lineWidth=1; x.strokeStyle='rgb(120,120,130)'; x.stroke();
 text('NES',23,341,8,[230,230,236]);
 const cx=W-44, cy=345; x.beginPath(); x.arc(cx+0.5,cy+0.5,22.5,0,Math.PI*2); x.fillStyle='rgb(200,200,206)'; x.fill(); x.beginPath(); x.arc(cx+0.5,cy+0.5,21.5,0,Math.PI*2); x.lineWidth=2; x.strokeStyle='rgb(120,120,130)'; x.stroke();
 x.beginPath(); x.arc(cx+0.5,cy+0.5,16,0,Math.PI*2); x.lineWidth=1; x.strokeStyle='rgb(110,110,118)'; x.stroke();
 text('CCS',cx-11,cy-4,7,[50,50,60]); text('GENERATED COVER',100,342,7,[214,182,96]);
 outline(0,0,W-1,H-1,[70,70,80],2);
 return cv; }
// fonts first: a canvas draws with whatever font is loaded at that moment, so the first request starts the webfont load and the page
// repaints once it is there (rows show the plain placeholder until then)
function mockFontsReady(){ if(MOCK.ready||MOCK.loading) return MOCK.ready; MOCK.loading=true;
 const done=()=>{ MOCK.ready=true; MOCK.loading=false; MOCK.cache.clear(); try{ if(typeof render==='function') render(); }catch(e){} try{ if(window.ovRefreshDetails) ovRefreshDetails(); }catch(e){} };
 try{ Promise.all([document.fonts.load('18px "Press Start 2P"'),document.fonts.load('8px "Press Start 2P"')]).then(done,done); }catch(e){ setTimeout(done,0); } return false; }
function mockCanvas(g){ return mockDraw(g.display_title||g.title||'', g.publisher||'', g.year||'', g.game_id||g.id||''); }
function mockCoverUrl(g){ if(!g||typeof MOCK==='undefined'||!MOCK) return '';   /* var-hoisted: rows painted before this part runs get the plain placeholder */ const id=g.game_id||g.id; if(!id) return ''; if(!mockFontsReady()) return '';
 const key=id+'|'+(g.display_title||'')+'|'+(g.publisher||'')+'|'+(g.year||''); const hit=MOCK.cache.get(key); if(hit){ MOCK.cache.delete(key); MOCK.cache.set(key,hit); return hit; }
 let url=''; try{ url=mockCanvas(g).toDataURL('image/png'); }catch(e){ url=''; }
 if(url){ MOCK.cache.set(key,url); if(MOCK.cache.size>MOCK.max) MOCK.cache.delete(MOCK.cache.keys().next().value); } return url; }
// ---- a missing box-art file (a fresh installation without the locally acquired scans, a file removed later) shows the generated
//      NES-style cover of that game, never a broken image: one capture-phase listener covers every art image on the page (N3.1) ----
document.addEventListener('error',e=>{ const im=e.target; if(!im||im.tagName!=='IMG'||im.dataset.mockfb) return; const m=(im.getAttribute('src')||'').match(/(?:^|\/)art\/([0-9a-f]{16})\.jpg(?:$|[?#])/); if(!m) return;
 im.dataset.mockfb='1'; const g=(typeof byId!=='undefined'&&byId)?byId[m[1]]:null; const url=g?mockCoverUrl(g):''; if(url){ im.src=url; im.classList.add('mockfb'); } },true);
