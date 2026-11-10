// ---- the CCS cast walks: each character's picture alternates with its second frame (data-walk) a few times a second while it is on
//      screen; reduced motion and a hidden page stop it (the first frame stays) ----
(function(){
 const MQ=matchMedia('(prefers-reduced-motion: reduce)'); let t=0, on=false;
 function step(){ document.querySelectorAll('img[data-walk]').forEach(im=>{ if(!im.dataset.walk0) im.dataset.walk0=im.getAttribute('src'); const r=im.getBoundingClientRect(); if(r.bottom<0||r.top>innerHeight||!r.width) return;
  im.setAttribute('src', im.getAttribute('src')===im.dataset.walk0?im.dataset.walk:im.dataset.walk0); }); }
 function rest(){ document.querySelectorAll('img[data-walk]').forEach(im=>{ if(im.dataset.walk0) im.setAttribute('src', im.dataset.walk0); }); }
 function sync(){ const want=!MQ.matches&&!document.hidden; if(want===on) return; on=want; clearInterval(t); t=0; if(on) t=setInterval(step, 280); else rest(); }
 MQ.addEventListener?MQ.addEventListener('change',sync):MQ.addListener(sync); document.addEventListener('visibilitychange',sync); sync();
})();
