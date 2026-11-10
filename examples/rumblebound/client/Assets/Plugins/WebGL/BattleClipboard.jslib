mergeInto(LibraryManager.library, {
  $RF_BattleClipboard: { active: null, sequence: 0 },
  RF_Battle_CopyRoomCode__deps: ['$RF_BattleClipboard'],
  RF_Battle_CopyRoomCode: function(codePtr, targetPtr, requestId) {
    var code = UTF8ToString(codePtr), target = UTF8ToString(targetPtr);
    if (RF_BattleClipboard.active) RF_BattleClipboard.active.finish('cancelled');
    var request = { done: false, panel: null, target: target };
    RF_BattleClipboard.active = request;
    var current = function() { return !request.done && RF_BattleClipboard.active === request; };
    request.finish = function(status) {
      if (!current()) return;
      request.done = true;
      if (request.panel) request.panel.remove();
      RF_BattleClipboard.active = null;
      if (Module.canvas) Module.canvas.focus();
      SendMessage(target, 'OnBattleRoomCodeCopyResult', JSON.stringify({ code: code, requestId: requestId, status: status }));
    };
    var input, notice;
    var selectCode = function() { input.focus(); input.select(); input.setSelectionRange(0, code.length); };
    var failed = function() {
      if (!current()) return;
      notice.textContent = '瀏覽器仍未允許複製。請選取代碼後按 Ctrl+C／⌘C，或長按使用複製選單。';
      selectCode();
    };
    var manual = function() {
      if (!current() || request.panel) return;
      var id = 'rb-room-copy-' + (++RF_BattleClipboard.sequence);
      var panel = document.createElement('div');
      request.panel = panel;
      panel.id = id;
      panel.setAttribute('role', 'dialog');
      panel.setAttribute('aria-modal', 'true');
      panel.setAttribute('aria-labelledby', id + '-title');
      panel.style.cssText = 'position:fixed;inset:0;z-index:10000;background:rgba(7,16,28,.88);display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box;font:18px system-ui,sans-serif;color:#ecf3fc';
      var card = document.createElement('div');
      card.style.cssText = 'box-sizing:border-box;width:100%;max-width:440px;max-height:calc(100vh - 32px);overflow:auto;padding:24px;background:#152438;border:1px solid #71849d;border-radius:18px;box-shadow:0 12px 50px #0008';
      var heading = document.createElement('h2');
      heading.id = id + '-title'; heading.textContent = '複製房間代碼';
      heading.style.cssText = 'font-size:26px;color:#fff2d6;margin:0 0 16px';
      var label = document.createElement('label');
      label.htmlFor = id + '-code'; label.textContent = '房間代碼';
      label.style.cssText = 'display:block;margin-bottom:8px';
      input = document.createElement('input');
      input.id = id + '-code'; input.type = 'text'; input.readOnly = true; input.value = code;
      input.setAttribute('aria-describedby', id + '-notice');
      input.style.cssText = 'box-sizing:border-box;width:100%;padding:14px;font:700 25px monospace;color:#ecf3fc;background:#0a1524;border:2px solid #71849d;border-radius:8px;text-align:center';
      input.addEventListener('click', selectCode);
      notice = document.createElement('p');
      notice.id = id + '-notice'; notice.setAttribute('role', 'status'); notice.setAttribute('aria-live', 'polite');
      notice.textContent = '請複製已選取的代碼，回到另一個瀏覽器貼上。可按 Ctrl+C／⌘C，或長按使用複製選單。';
      notice.style.cssText = 'line-height:1.6;color:#bacadd;margin:16px 0';
      var retry = document.createElement('button'); retry.type = 'button'; retry.textContent = '複製代碼';
      var close = document.createElement('button'); close.type = 'button'; close.textContent = '關閉';
      retry.style.cssText = 'font:700 19px system-ui,sans-serif;min-height:48px;padding:10px 18px;margin-right:10px;border:0;border-radius:8px;background:#1976bb;color:white;cursor:pointer';
      close.style.cssText = 'font:700 19px system-ui,sans-serif;min-height:48px;padding:10px 18px;border:0;border-radius:8px;background:#d9dfec;color:#23334b;cursor:pointer';
      retry.addEventListener('click', function(event) { event.stopPropagation(); attempt(true); });
      close.addEventListener('click', function(event) { event.stopPropagation(); request.finish('cancelled'); });
      panel.addEventListener('keydown', function(event) {
        event.stopPropagation();
        if (event.key === 'Escape') { event.preventDefault(); request.finish('cancelled'); }
        if (event.key === 'Tab') {
          if (event.shiftKey && document.activeElement === input) { event.preventDefault(); close.focus(); }
          else if (!event.shiftKey && document.activeElement === close) { event.preventDefault(); input.focus(); }
        }
      });
      ['pointerdown', 'pointerup', 'click', 'keyup', 'copy', 'cut', 'paste'].forEach(function(type) {
        panel.addEventListener(type, function(event) { event.stopPropagation(); });
      });
      card.append(heading, label, input, notice, retry, close); panel.append(card); document.body.append(panel);
      selectCode();
    };
    var attempt = function(fromClick) {
      if (!current()) return;
      if (fromClick) { selectCode(); notice.textContent = '正在複製…'; }
      var pending = null;
      try {
        if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function')
          pending = navigator.clipboard.writeText(code);
      } catch (error) { }
      var copied = false;
      if (fromClick && typeof document.execCommand === 'function') {
        try { copied = document.execCommand('copy') === true; } catch (error) { }
      }
      if (pending && typeof pending.then === 'function') {
        Promise.resolve(pending).then(function() { if (current()) request.finish('copied'); }, function() {
          if (!current()) return;
          if (fromClick) failed(); else manual();
        });
      } else if (!copied) {
        if (fromClick) failed(); else manual();
      }
      if (copied) request.finish('copied');
    };
    attempt(false);
  },
  RF_Battle_CancelRoomCopy__deps: ['$RF_BattleClipboard'],
  RF_Battle_CancelRoomCopy: function(targetPtr) {
    var request = RF_BattleClipboard.active;
    if (request && request.target === UTF8ToString(targetPtr)) request.finish('cancelled');
  },
});