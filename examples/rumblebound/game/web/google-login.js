// Shared by the diagnostic page and Unity's WebGL bridge. No Google credential
// enters a URL, storage, analytics or logs; only a game session reaches Unity.
(function () {
  let library, cancelActive, exchangePending=false, generation=0;
  async function post(path, body) {
    const response = await fetch(path, {method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json'}, body:JSON.stringify(body)});
    const value = await response.json();
    if (!response.ok) throw Error(value.error || 'google_login_failed');
    return value;
  }
  function loadGIS() {
    if (globalThis.google?.accounts?.id) return Promise.resolve();
    if (!library) library = new Promise((resolve,reject) => {
      const script = document.createElement('script');
      script.src='https://accounts.google.com/gsi/client'; script.async=true;
      const timer=setTimeout(()=>reject(Error('google_library_timeout')),10000);
      script.onload=()=>{clearTimeout(timer); resolve();};
      script.onerror=()=>{clearTimeout(timer); script.remove(); reject(Error('google_library_unavailable'));};
      document.head.append(script);
    }).catch(error=>{library=null;throw error;});
    return library;
  }
  globalThis.RumbleboundGoogle = {
    cancel() { generation++; cancelActive?.(); },
    async mount(container, onSuccess, onError) {
      if(exchangePending){onError('google_login_in_progress');return;}
      cancelActive?.();
      const attemptGeneration=++generation;
      let live=true, timer;
      const clear=()=>{live=false;clearTimeout(timer);container.replaceChildren();};
      const cancel=()=>{if(live){clear();onError('google_login_cancelled');}};
      cancelActive=cancel;
      container.textContent='準備 Google 登入…';
      try {
        const attempt=await post('/api/auth/google/start',{});
        await loadGIS(); if(!live)return;
        const button=document.createElement('div'), close=document.createElement('button');
        close.textContent='取消登入';close.type='button';close.onclick=cancel;
        container.replaceChildren(button,close);
        timer=setTimeout(()=>{if(live){clear();onError('google_login_attempt_expired');}},120000);
        google.accounts.id.initialize({client_id:attempt.clientID,nonce:attempt.nonce,auto_select:false,ux_mode:'popup',callback:async result=>{
          if(!live)return;
          // Close UI and disable duplicate callbacks before sending the token.
          clear();
          exchangePending=true;
          try {
            const identity=await post('/api/auth/google/exchange',{attemptID:attempt.attemptID,idToken:result.credential});
            if(attemptGeneration===generation)onSuccess(identity);
          } catch(error) { if(attemptGeneration===generation)onError(error.message); }
          finally { exchangePending=false; }
        }});
        google.accounts.id.renderButton(button,{theme:'outline',size:'large',text:'signin_with',width:250});
      } catch(error) { if(live){clear();onError(error.message);} }
    }
  };
})();
