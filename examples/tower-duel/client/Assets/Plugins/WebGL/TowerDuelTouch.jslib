mergeInto(LibraryManager.library, {
  $TDTouch: {
    canvas: null,
    active: {},
    mode: '',
    queue: [],
    until: 0,
    listeners: [],
    push: function(event) {
      // Never replay partial gestures after an overflowing/paused tab.
      if (TDTouch.queue.length >= 256) { TDTouch.cancel(); return; }
      TDTouch.queue.push(event);
    },
    cancel: function() {
      TDTouch.active = {}; TDTouch.mode = '';
      TDTouch.queue = [[0, 4, 0, 0]];
      TDTouch.until = performance.now() + 500;
    },
    input: function(event, phase, mode) {
      if (TDTouch.mode && TDTouch.mode !== mode) return;
      if (!TDTouch.mode) {
        if (phase !== 0) return;
        TDTouch.mode = mode;
      }
      var bounds = TDTouch.canvas.getBoundingClientRect();
      if (!bounds.width || !bounds.height) { TDTouch.cancel(); return; }
      TDTouch.until = performance.now() + 500;
      for (var i = 0; i < event.changedTouches.length; ++i) {
        var touch = event.changedTouches[i], id = touch.identifier;
        if (phase === 0) TDTouch.active[id] = true;
        else if (!TDTouch.active[id]) continue;
        TDTouch.push([id, phase, (touch.clientX - bounds.left) / bounds.width,
          (touch.clientY - bounds.top) / bounds.height]);
        if (phase >= 2) delete TDTouch.active[id];
      }
      if (!Object.keys(TDTouch.active).length) TDTouch.mode = '';
    },
    pointer: function(event, phase) {
      if (event.pointerType !== 'touch') return;
      TDTouch.input({changedTouches: [{identifier: event.pointerId,
        clientX: event.clientX, clientY: event.clientY}]}, phase, 'pointer');
      if (phase === 0 && TDTouch.mode === 'pointer') {
        try { TDTouch.canvas.setPointerCapture(event.pointerId); } catch (_) {}
      }
    },
    install: function() {
      if (TDTouch.canvas) return;
      var canvas = Module.canvas || document.getElementById('unity-canvas');
      if (!canvas) return;
      TDTouch.canvas = canvas;
      function listen(target, name, callback) {
        target.addEventListener(name, callback, {capture: true, passive: true});
        TDTouch.listeners.push([target, name, callback]);
      }
      // Some browsers send both APIs. Lock to the first one for a complete gesture,
      // so compatibility events cannot deploy twice. Neither path filters primary.
      listen(canvas, 'touchstart', function(e) { TDTouch.input(e, 0, 'touch'); });
      listen(canvas, 'touchmove', function(e) { TDTouch.input(e, 1, 'touch'); });
      listen(canvas, 'touchend', function(e) { TDTouch.input(e, 2, 'touch'); });
      listen(canvas, 'touchcancel', function(e) { TDTouch.input(e, 3, 'touch'); });
      listen(canvas, 'pointerdown', function(e) { TDTouch.pointer(e, 0); });
      listen(canvas, 'pointermove', function(e) { TDTouch.pointer(e, 1); });
      listen(canvas, 'pointerup', function(e) { TDTouch.pointer(e, 2); });
      listen(canvas, 'pointercancel', function(e) { TDTouch.pointer(e, 3); });
      listen(canvas, 'lostpointercapture', function(e) { TDTouch.pointer(e, 3); });
      listen(window, 'blur', function() { TDTouch.cancel(); });
      listen(document, 'visibilitychange', function() { if (document.hidden) TDTouch.cancel(); });
    },
    dispose: function() {
      for (var i = 0; i < TDTouch.listeners.length; ++i) {
        var l = TDTouch.listeners[i]; l[0].removeEventListener(l[1], l[2], true);
      }
      TDTouch.listeners = []; TDTouch.active = {}; TDTouch.mode = ''; TDTouch.queue = [];
      TDTouch.canvas = null; TDTouch.until = 0;
    }
  },
  TD_InstallTouchInput__deps: ['$TDTouch'],
  TD_InstallTouchInput: function() { TDTouch.install(); },
  TD_RemoveTouchInput__deps: ['$TDTouch'],
  TD_RemoveTouchInput: function() { TDTouch.dispose(); },
  TD_RawTouchActive__deps: ['$TDTouch'],
  TD_RawTouchActive: function() {
    return (Object.keys(TDTouch.active).length || performance.now() < TDTouch.until) ? 1 : 0;
  },
  TD_PollTouch__deps: ['$TDTouch'],
  TD_PollTouch: function(id, phase, x, y) {
    if (!TDTouch.queue.length) return 0;
    var e = TDTouch.queue.shift();
    HEAP32[id >> 2] = e[0]; HEAP32[phase >> 2] = e[1];
    HEAPF32[x >> 2] = e[2]; HEAPF32[y >> 2] = e[3];
    return 1;
  }
});
