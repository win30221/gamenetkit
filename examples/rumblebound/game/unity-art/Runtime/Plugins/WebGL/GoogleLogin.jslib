mergeInto(LibraryManager.library, {
  RB_GoogleCancel: function(){if(globalThis.RumbleboundGoogle)globalThis.RumbleboundGoogle.cancel();var panel=document.getElementById('rb-google-panel');if(panel)panel.remove();},
  RB_GoogleBegin: function(targetPtr){
    var target=UTF8ToString(targetPtr);
    var begin=function(){
      var old=document.getElementById('rb-google-panel');if(old)old.remove();
      var panel=document.createElement('div');panel.id='rb-google-panel';
      panel.style.cssText='position:fixed;z-index:9999;top:30px;right:30px;padding:24px;background:#fff;color:#17202a;border-radius:12px;min-width:260px';
      document.body.append(panel);
      globalThis.RumbleboundGoogle.mount(panel,function(identity){panel.remove();SendMessage(target,'OnGoogleSession',JSON.stringify(identity));},function(error){panel.remove();SendMessage(target,'OnGoogleError',error);});
    };
    if(globalThis.RumbleboundGoogle){begin();return;}
    var script=document.createElement('script');script.src='/google-login.js';
    script.onload=begin;script.onerror=function(){SendMessage(target,'OnGoogleError','google_bridge_unavailable');};document.head.append(script);
  }
});
