// ---- Console Backup & Restore (Classic Cart Studio for Windows only) ----
// The first screen's Console Backup & Restore button and the Studio menu's entry open the app's native Console Backup & Restore window (the
// same window and engine as the CCS Console Backup & Restore Utility). It needs no collection, game, controller or HOME setup. The website
// never shows either entry: a browser cannot read or restore a console, so it offers no button that could not work.
(function(){
 const can=!!(window.__CCS_DESKTOP&&window.chrome&&window.chrome.webview&&typeof window.chrome.webview.postMessage==='function');
 const w=$('cbrWrap'), m=$('mConsoleBackup'), l=$('mCbrL');
 if(w) w.hidden=!can; if(m) m.hidden=!can; if(l) l.hidden=!can;
 if(!can) return;
 const open=e=>{ if(e) e.preventDefault(); try{ window.chrome.webview.postMessage('console-backup-open'); }catch(err){} };
 const b=$('consoleBackupBtn'); if(b) b.addEventListener('click', open);
 document.addEventListener('click', e=>{ const t=e.target.closest&&e.target.closest('#mConsoleBackup'); if(t) open(e); });
})();
