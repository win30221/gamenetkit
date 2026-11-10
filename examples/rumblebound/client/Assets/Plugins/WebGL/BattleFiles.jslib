mergeInto(LibraryManager.library, {
  RF_Rumblebound_SelectReplay: function(targetPtr, requestId, maxBytes) {
    var target = UTF8ToString(targetPtr);
    var previous = Module.rfReplayPicker;
    if (previous) previous.finish('cancelled');

    var input = null;
    var timer = null;
    var delivered = false;
    var armed = false;
    var state = { target: target, requestId: requestId, url: null };
    Module.rfReplayPicker = state;
    function cleanup() {
      if (timer !== null) { clearTimeout(timer); timer = null; }
      window.removeEventListener('focus', onFocus);
      if (input) {
        input.removeEventListener('change', onChange);
        input.removeEventListener('cancel', onCancel);
        input.remove();
      }
    }
    state.finish = function(status, code, file) {
      if (Module.rfReplayPicker !== state) return;
      cleanup();
      if (status !== 'selected') {
        if (state.url) { URL.revokeObjectURL(state.url); state.url = null; }
        Module.rfReplayPicker = null;
      }
      if (delivered) return;
      delivered = true;
      var result = { requestId: requestId, status: status };
      if (code) result.code = code;
      if (file) { result.url = state.url; result.name = file.name; result.size = file.size; }
      SendMessage(target, 'OnReplayFileSelected', JSON.stringify(result));
    };
    state.release = function() {
      if (Module.rfReplayPicker !== state) return;
      cleanup();
      if (state.url) { URL.revokeObjectURL(state.url); state.url = null; }
      Module.rfReplayPicker = null;
    };
    function onCancel() { state.finish('cancelled'); }
    function onChange() {
      if (Module.rfReplayPicker !== state || delivered) return;
      var file = input.files && input.files[0];
      if (!file) { onCancel(); return; }
      // Check the byte bound before creating any URL or reading file contents.
      if (file.size > maxBytes) { state.finish('error', 'too_large'); return; }
      if (!(file.size > 0)) { state.finish('error', 'empty'); return; }
      try { state.url = URL.createObjectURL(file); }
      catch (error) { state.finish('error', 'read_failed'); return; }
      state.finish('selected', null, file);
    }
    function onFocus() {
      if (!armed || delivered || Module.rfReplayPicker !== state) return;
      if (timer !== null) clearTimeout(timer);
      // Some browsers lack the cancel event. Let change settle after focus returns.
      timer = setTimeout(function() {
        timer = null;
        if (Module.rfReplayPicker !== state || delivered) return;
        if (document.hasFocus && !document.hasFocus()) return;
        onChange();
      }, 500);
    }
    try {
      if (!(maxBytes > 0) || typeof URL.createObjectURL !== 'function') {
        state.finish('error', 'unavailable'); return;
      }
      input = document.createElement('input');
      input.type = 'file';
      input.accept = '.playback.json,.json,application/json';
      input.multiple = false;
      input.style.display = 'none';
      input.addEventListener('change', onChange);
      input.addEventListener('cancel', onCancel);
      window.addEventListener('focus', onFocus);
      document.body.appendChild(input);
      // Keep click synchronous with the originating Unity button/user gesture.
      input.click();
      armed = true;
    } catch (error) { state.finish('error', 'unavailable'); }
  },
  RF_Rumblebound_ReleaseReplay: function(targetPtr, requestId) {
    var state = Module.rfReplayPicker;
    if (state && state.target === UTF8ToString(targetPtr) && state.requestId === requestId) state.release();
  },
  RF_Rumblebound_CancelReplay: function(targetPtr, requestId) {
    var state = Module.rfReplayPicker;
    if (state && state.target === UTF8ToString(targetPtr) && state.requestId === requestId) state.finish('cancelled');
  },
  RF_Battle_UIPixelRatio: function() {
    var canvas = Module.canvas;
    return canvas && canvas.clientWidth > 0 ? canvas.width / canvas.clientWidth : 1;
  },
  RF_Rumblebound_Download: function(text) {
    var data = UTF8ToString(text);
    var url = URL.createObjectURL(new Blob([data], { type: 'application/json' }));
    var link = document.createElement('a');
    link.href = url;
    link.download = 'rumblebound.playback.json';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(function() { URL.revokeObjectURL(url); }, 1500);
  }
});
