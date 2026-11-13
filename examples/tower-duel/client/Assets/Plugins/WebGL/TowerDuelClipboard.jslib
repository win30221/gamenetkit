mergeInto(LibraryManager.library, {
  $TDClipboard: {
    version: 0,
    dialog: null,
    notify: function(request, result) {
      if (request.id === TDClipboard.version) SendMessage(request.target, 'OnRoomCodeCopied', result);
    },
    cancel: function() {
      TDClipboard.version++;
      if (TDClipboard.dialog) {
        var dialog = TDClipboard.dialog;
        TDClipboard.dialog = null;
        dialog.close();
        dialog.remove();
      }
    },
    complete: function(request) {
      if (request.id !== TDClipboard.version) return;
      request.finished = true;
      if (TDClipboard.dialog) {
        var dialog = TDClipboard.dialog;
        TDClipboard.dialog = null;
        dialog.close();
        dialog.remove();
      }
      TDClipboard.notify(request, 'copied');
    },
    write: function(request) {
      if (!navigator.clipboard || !navigator.clipboard.writeText) throw new Error('Clipboard unavailable');
      return navigator.clipboard.writeText(request.code);
    },
    manual: function(request) {
      if (request.id !== TDClipboard.version) return;
      var dialog = document.createElement('dialog');
      dialog.id = 'td-copy-dialog';
      dialog.setAttribute('aria-label', '複製房間碼');
      dialog.style.cssText = 'width:min(420px,calc(100vw - 32px));padding:24px;border:1px solid #63798c;border-radius:14px;background:#192d43;color:#f7efd9;font:16px system-ui,sans-serif';
      var title = document.createElement('h2');
      title.textContent = '複製房間碼';
      var help = document.createElement('p');
      help.textContent = '請選取代碼後複製。電腦可按 Ctrl+C／⌘C；手機可長按複製。';
      help.style.cssText = 'margin:12px 0;line-height:1.7';
      var input = document.createElement('input');
      input.id = 'td-copy-code';
      input.readOnly = true;
      input.value = request.code;
      input.setAttribute('aria-label', '房間碼');
      input.style.cssText = 'box-sizing:border-box;width:100%;padding:12px;font:28px monospace;text-align:center;color:#f5d17a;background:#0c1e2e;border:1px solid #63798c;border-radius:8px';
      input.addEventListener('click', function() { input.select(); });
      var status = document.createElement('p');
      status.setAttribute('role', 'status');
      status.style.cssText = 'margin:12px 0;line-height:1.7';
      status.textContent = '瀏覽器尚未完成自動複製，也可以再試一次。';
      var buttons = document.createElement('div');
      buttons.style.cssText = 'display:flex;gap:12px;margin-top:16px';
      var retry = document.createElement('button');
      retry.type = 'button';
      retry.textContent = '再試一次';
      retry.addEventListener('click', function() {
        try {
          TDClipboard.write(request).then(function() { TDClipboard.complete(request); }, function() {
            status.textContent = '無法自動複製，請選取上方代碼後手動複製。';
            input.focus(); input.select();
          });
        } catch (_) {
          status.textContent = '請選取上方代碼後手動複製。';
          input.focus(); input.select();
        }
      });
      var close = document.createElement('button');
      close.type = 'button';
      close.textContent = '關閉';
      close.addEventListener('click', function() { dialog.close(); });
      buttons.append(retry, close);
      dialog.append(title, help, input, status, buttons);
      dialog.addEventListener('keydown', function(event) { event.stopPropagation(); });
      // Keep Unity's global copy handler from replacing this native input selection.
      dialog.addEventListener('copy', function(event) {
        event.clipboardData.setData('text/plain', request.code);
        event.preventDefault(); event.stopPropagation();
      });
      dialog.addEventListener('close', function() {
        if (!request.finished) TDClipboard.notify(request, 'cancelled');
        if (TDClipboard.dialog === dialog) TDClipboard.dialog = null;
        dialog.remove();
        var canvas = document.querySelector('#unity-canvas');
        if (canvas) canvas.focus();
      });
      TDClipboard.dialog = dialog;
      document.body.append(dialog);
      dialog.showModal();
      input.focus(); input.select();
      TDClipboard.notify(request, 'manual');
    },
    copy: function(code, target) {
      TDClipboard.cancel();
      var request = { id: TDClipboard.version, code: code, target: target, finished: false };
      try {
        TDClipboard.write(request).then(function() { TDClipboard.complete(request); }, function() { TDClipboard.manual(request); });
      } catch (_) { TDClipboard.manual(request); }
    }
  },
  TD_CopyRoomCode__deps: ['$TDClipboard'],
  TD_CopyRoomCode: function(code, target) { TDClipboard.copy(UTF8ToString(code), UTF8ToString(target)); },
  TD_CancelRoomCodeCopy__deps: ['$TDClipboard'],
  TD_CancelRoomCodeCopy: function() { TDClipboard.cancel(); }
});
