// Browser bridge contract tests using the actual .jslib in a simulated DOM.
// These do not replace a WebGL/browser dialog smoke test.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../client/Assets/Plugins/WebGL/BattleFiles.jslib', import.meta.url), 'utf8');

function harness() {
  const inputs = [], links = [], messages = [], created = [], revoked = [], timers = new Map();
  let timerId = 0;
  function eventTarget() {
    const listeners = new Map();
    return {
      listeners,
      addEventListener(type, fn) { listeners.set(type, fn); },
      removeEventListener(type, fn) { if (listeners.get(type) === fn) listeners.delete(type); },
      emit(type) { listeners.get(type)?.(); },
    };
  }
  const window = eventTarget();
  const document = {
    focused: true,
    hasFocus() { return this.focused; },
    body: { appendChild(node) { node.attached = true; } },
    createElement(tag) {
      const node = Object.assign(eventTarget(), {
        style: {}, files: [], clicked: false, attached: false,
        click() { this.clicked = true; },
        remove() { this.attached = false; },
      });
      (tag === 'input' ? inputs : links).push(node);
      return node;
    },
  };
  const context = {
    Module: { canvas: { width: 800, clientWidth: 400 } },
    LibraryManager: { library: {} },
    mergeInto: Object.assign,
    UTF8ToString: value => value,
    SendMessage(target, method, json) { messages.push({ target, method, ...JSON.parse(json) }); },
    document, window, Blob,
    URL: {
      createObjectURL(blob) { created.push(blob); return `blob:replay-${created.length}`; },
      revokeObjectURL(url) { revoked.push(url); },
    },
    setTimeout(fn) { const id = ++timerId; timers.set(id, fn); return id; },
    clearTimeout(id) { timers.delete(id); },
  };
  vm.runInNewContext(source, context);
  const api = context.LibraryManager.library;
  return {
    context, api, inputs, links, messages, created, revoked, window, document, timers,
    select(id = 1, max = 100, target = 'Replay') { api.RF_Rumblebound_SelectReplay(target, id, max); return inputs.at(-1); },
    change(input, size = 10) { input.files = [{ size, name: 'match.playback.json' }]; input.emit('change'); },
    flush() { const pending = [...timers.values()]; timers.clear(); pending.forEach(fn => fn()); },
  };
}

test('selection returns only a bounded local blob URL, retained until matching release', () => {
  const h = harness(), input = h.select();
  assert.equal(input.clicked, true);
  assert.equal(input.type, 'file');
  assert.equal(input.accept, '.playback.json,.json,application/json');
  assert.equal(input.multiple, false);
  h.change(input, 100);
  assert.deepEqual(h.messages, [{ target: 'Replay', method: 'OnReplayFileSelected', requestId: 1, status: 'selected', url: 'blob:replay-1', name: 'match.playback.json', size: 100 }]);
  assert.equal(h.created[0], input.files[0]);
  assert.equal(input.attached, false);
  assert.equal(input.listeners.size, 0);
  assert.equal(h.window.listeners.size, 0);
  assert.deepEqual(h.revoked, []);
  h.api.RF_Rumblebound_ReleaseReplay('Other', 1);
  h.api.RF_Rumblebound_ReleaseReplay('Replay', 2);
  assert.deepEqual(h.revoked, []);
  h.api.RF_Rumblebound_ReleaseReplay('Replay', 1);
  h.api.RF_Rumblebound_ReleaseReplay('Replay', 1);
  assert.deepEqual(h.revoked, ['blob:replay-1']);
  // No fetch/XHR/FileReader globals exist: selection never uploads or reads text.
});

for (const [size, code] of [[101, 'too_large'], [0, 'empty']]) {
  test(`rejects ${code} before allocating a blob URL`, () => {
    const h = harness(), input = h.select();
    h.change(input, size);
    assert.equal(h.messages[0].code, code);
    assert.equal(h.messages[0].status, 'error');
    assert.equal(h.created.length, 0);
    assert.equal(input.attached, false);
    assert.equal(h.context.Module.rfReplayPicker, null);
  });
}

test('browser cancellation and empty change deliver cancellation only once', () => {
  const h = harness(), input = h.select();
  const lateChange = input.listeners.get('change');
  input.emit('cancel');
  lateChange();
  h.api.RF_Rumblebound_CancelReplay('Replay', 1);
  assert.equal(h.messages.length, 1);
  assert.equal(h.messages[0].status, 'cancelled');
  assert.equal(h.created.length, 0);
  const empty = h.select(2);
  empty.emit('change');
  assert.equal(h.messages[1].status, 'cancelled');
});

test('explicit cancellation cleans pending input and ignores late events', () => {
  const h = harness(), input = h.select();
  const lateChange = input.listeners.get('change');
  h.api.RF_Rumblebound_CancelReplay('Replay', 1);
  input.files = [{ name: 'late.json', size: 10 }];
  lateChange();
  assert.equal(h.messages.length, 1);
  assert.equal(h.messages[0].status, 'cancelled');
  assert.equal(input.attached, false);
  assert.equal(h.created.length, 0);
});

test('focus fallback does not cancel while the document remains unfocused', () => {
  const h = harness();
  h.select();
  h.document.focused = false;
  h.window.emit('focus');
  h.flush();
  assert.equal(h.messages.length, 0);
  h.document.focused = true;
  h.window.emit('focus');
  h.flush();
  assert.equal(h.messages[0].status, 'cancelled');
});

test('focus fallback waits for change and cannot cancel a selected replay', () => {
  const h = harness(), input = h.select();
  h.window.emit('focus');
  assert.equal(h.messages.length, 0);
  h.change(input);
  h.flush();
  assert.equal(h.messages.length, 1);
  assert.equal(h.messages[0].status, 'selected');
  assert.deepEqual(h.revoked, []);
});

test('focus fallback handles both populated files before change and dialog dismissal', () => {
  const h = harness(), input = h.select();
  input.files = [{ name: 'local.json', size: 10 }];
  h.window.emit('focus');
  h.flush();
  assert.equal(h.messages[0].status, 'selected');
  h.select(2);
  h.window.emit('focus');
  h.flush();
  assert.equal(h.messages[1].status, 'cancelled');
  assert.deepEqual(h.revoked, ['blob:replay-1']);
});

test('replacement cancels pending request and isolates late change/cancel/focus callbacks', () => {
  const h = harness(), old = h.select();
  const lateChange = old.listeners.get('change'), lateCancel = old.listeners.get('cancel');
  h.window.emit('focus');
  const lateFocusTimeout = [...h.timers.values()][0];
  const current = h.select(2);
  old.files = [{ name: 'old.json', size: 10 }];
  lateChange(); lateCancel(); lateFocusTimeout();
  h.api.RF_Rumblebound_CancelReplay('Replay', 1);
  assert.equal(h.messages.length, 1);
  assert.equal(h.messages[0].status, 'cancelled');
  assert.equal(h.created.length, 0);
  assert.equal(current.attached, true);
  h.change(current);
  assert.equal(h.messages[1].requestId, 2);
  assert.equal(h.messages[1].status, 'selected');
});

test('cancel/replacement revoke selected URLs without delivering a second callback', () => {
  const h = harness();
  h.change(h.select());
  h.api.RF_Rumblebound_CancelReplay('Replay', 1);
  assert.deepEqual(h.revoked, ['blob:replay-1']);
  assert.equal(h.messages.length, 1);
  h.change(h.select(2));
  const next = h.select(3);
  assert.deepEqual(h.revoked, ['blob:replay-1', 'blob:replay-2']);
  assert.equal(h.messages.length, 2);
  h.api.RF_Rumblebound_ReleaseReplay('Replay', 3);
  assert.equal(next.attached, false);
});

test('unavailable chooser or failed URL allocation reports an error and cleans up', () => {
  const h = harness();
  h.context.URL.createObjectURL = undefined;
  h.select();
  assert.equal(h.messages[0].code, 'unavailable');
  const other = harness(), input = other.select();
  other.context.URL.createObjectURL = () => { throw new Error('denied'); };
  other.change(input);
  assert.equal(other.messages[0].code, 'read_failed');
  assert.equal(input.attached, false);
});

test('existing pixel ratio and download exports retain their behavior', () => {
  const h = harness();
  assert.equal(h.api.RF_Battle_UIPixelRatio(), 2);
  h.context.Module.canvas.clientWidth = 0;
  assert.equal(h.api.RF_Battle_UIPixelRatio(), 1);
  h.api.RF_Rumblebound_Download('{"replay":true}');
  assert.equal(h.created[0].type, 'application/json');
  assert.equal(h.links[0].download, 'rumblebound.playback.json');
  assert.equal(h.links[0].clicked, true);
  assert.equal(h.links[0].attached, false);
  assert.deepEqual(h.revoked, []);
  h.flush();
  assert.deepEqual(h.revoked, ['blob:replay-1']);
});
