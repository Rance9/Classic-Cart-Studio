// Classic Cart Studio browser player: the sound worklet (an AudioWorklet module, play/ccs-play-worklet.js). The page runtime (ccs-play.js)
// posts the game's stereo samples here as Float32 blocks at the emulator's rate; this plays them at the device's rate (linear
// resampling), one ring buffer, and reports how many source frames it has consumed so the runtime can pace the game by the sound clock.
// 'clear' drops what is queued (the game paused: silence at once, as on Windows); nothing is ever recorded or sent anywhere.
class CcsPlayer extends AudioWorkletProcessor {
  constructor(){ super(); this.n=1<<16; this.l=new Float32Array(this.n); this.r=new Float32Array(this.n); this.w=0; this.rd=0; this.frac=0; this.step=1; this.used=0; this.last=0; this.peak=0;
    this.port.onmessage=e=>{ const m=e.data;
      if(m.type==='rate') this.step=m.src/sampleRate;
      else if(m.type==='clear'){ this.rd=this.w; this.frac=0; }
      else if(m.type==='push'){ const d=m.data, k=d.length>>1; for(let i=0;i<k;i++){ if(this.w-this.rd>=this.n-1) break; const j=this.w&(this.n-1); this.l[j]=d[2*i]; this.r[j]=d[2*i+1]; this.w++; } } }; }
  process(_in, outs){ const o=outs[0], L=o[0], R=o[1]||o[0]; let used=0;
    for(let i=0;i<L.length;i++){
      if(this.w-this.rd<2){ L[i]=0; if(R!==L) R[i]=0; continue; }
      const a=this.rd&(this.n-1), b=(this.rd+1)&(this.n-1), f=this.frac, vl=this.l[a]+(this.l[b]-this.l[a])*f, vr=this.r[a]+(this.r[b]-this.r[a])*f;
      L[i]=vl; if(R!==L) R[i]=vr; const p=Math.abs(vl)>Math.abs(vr)?Math.abs(vl):Math.abs(vr); if(p>this.peak) this.peak=p;
      this.frac+=this.step; const adv=Math.floor(this.frac); this.frac-=adv; this.rd+=adv; used+=adv; }
    this.used+=used; if(currentTime-this.last>=0.02){ this.port.postMessage({type:'used', used:this.used, queued:this.w-this.rd, peak:this.peak}); this.last=currentTime; this.peak=0; }
    return true; }
}
registerProcessor('ccs-player', CcsPlayer);
