// Exercise the actual diagnostics HTML and app.js with a minimal DOM/socket.
// This checks settings behavior, not browser rendering or network conditioning.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const html = readFileSync(new URL('../../web/index.html', import.meta.url), 'utf8');
const source = readFileSync(new URL('../../web/app.js', import.meta.url), 'utf8');
const ids = new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]));
const controls = [
  ['send-delay', 'SendDelaySeconds', .3],
  ['send-jitter', 'SendJitterSeconds', .3],
  ['send-loss', 'SendLossProbability', 100],
  ['delay', 'DelaySeconds', .3],
  ['receive-jitter', 'ReceiveJitterSeconds', .3],
  ['receive-loss', 'ReceiveLossProbability', 100],
];

function settings() {
  return {
    Transport: 'websocket', Prediction: 'frame', Correction: 'exponential', Timeline: 'adaptive', Clock: 'disciplined',
    DelaySeconds: .21, ReceiveJitterSeconds: .03, ReceiveLossProbability: .2,
    SendDelaySeconds: .065, SendJitterSeconds: .012, SendLossProbability: .7, SpeculativeActions: true,
    Server: { codec: 'json', interest: 'all', radius: 12, quantize: false, history: false, lag: 'off' },
  };
}

function harness() {
  const nodes = new Map(), sent = [], sockets = [];
  function node() {
    return {
      value: '', checked: false, children: [], handlers: {},
      addEventListener(name, fn) { this.handlers[name] = fn; },
      append(...children) { this.children.push(...children); },
      replaceChildren(...children) { this.children = children; },
      getContext() { return {}; },
    };
  }
  function element(id) {
    assert.ok(ids.has(id), 'actual HTML missing ' + id);
    if (!nodes.has(id)) nodes.set(id, node());
    return nodes.get(id);
  }
  class Socket {
    readyState = 1;
    constructor() { sockets.push(this); }
    send(message) { sent.push(JSON.parse(message)); }
  }
  const context = vm.createContext({
    document: { getElementById: element, createElement: node, addEventListener() {}, activeElement: null },
    window: { addEventListener() {} }, location: { href: 'http://localhost/diagnostics/', protocol: 'http:' },
    WebSocket: Socket, URL, JSON, Number, String, Set, Math, console,
    requestAnimationFrame() {},
  });
  vm.runInContext(source, context, { filename: 'diagnostics/app.js' });
  return {
    element, sent,
    apply() { element('apply').handlers.click(); },
    receive(selection) {
      const frame = {
        viewerID: 'settings-test', stage: 'login', notice: '', busy: false, userID: '', room: null,
        world: { snapshot: null, players: [], tick: 0, rtt: 0, correction: 0, renderTick: 0 },
        recording: { active: false, frames: 0, notice: '' }, replay: { active: false },
        connected: false, canControl: false, loaded: false,
        recovery: { enabled: true, pending: false, wait: 0 },
        net: {
          settings: selection, predictions: ['authority', 'frame', 'fixed-step'], corrections: ['snap', 'exponential'],
          timelines: ['latest', 'fixed', 'adaptive'], clocks: ['snapshot', 'disciplined'], codecs: ['json', 'delta'],
          speculative: true, frameAck: 0, missing: 0, confirmed: 0, rejected: 0, action: '',
        },
      };
      sockets[0].onmessage({ data: JSON.stringify(frame) });
    },
  };
}

test('diagnostics loads independent directions and roundtrips percent fields through the actual apply handler', () => {
  const h = harness(), selection = settings();
  h.receive(selection);
  assert.equal(h.element('send-delay').value, '0.065');
  assert.equal(h.element('send-jitter').value, '0.012');
  assert.equal(h.element('send-loss').value, '70');
  assert.equal(h.element('delay').value, '0.21');
  assert.equal(h.element('receive-jitter').value, '0.03');
  assert.equal(h.element('receive-loss').value, '20');
  h.apply();
  assert.deepEqual(h.sent, [{ op: 'settings', value: selection }]);
});

test('unchanged network frames preserve a draft and newly applied settings refresh it', () => {
  const h = harness(), original = settings();
  h.receive(original);
  h.element('send-delay').value = '.123';
  h.element('receive-loss').value = '4.5';
  h.receive(original);
  assert.equal(h.element('send-delay').value, '.123');
  assert.equal(h.element('receive-loss').value, '4.5');
  h.apply();
  const applied = { ...original, SendDelaySeconds: .123, ReceiveLossProbability: .045 };
  assert.deepEqual(h.sent, [{ op: 'settings', value: applied }]);
  h.receive(applied);
  assert.equal(h.element('send-delay').value, '0.123');
  assert.equal(h.element('receive-loss').value, '4.5');
  h.receive(original);
  assert.equal(h.element('send-delay').value, '0.065');
  assert.equal(h.element('receive-loss').value, '20');
});

test('legacy receive delay leaves both directions lossless and adds no send delay', () => {
  const h = harness(), legacy = settings();
  for (const [, field] of controls) delete legacy[field];
  legacy.DelaySeconds = .15;
  h.receive(legacy);
  h.apply();
  assert.deepEqual(h.sent, [{ op: 'settings', value: {
    ...legacy, SendDelaySeconds: 0, SendJitterSeconds: 0, SendLossProbability: 0,
    ReceiveJitterSeconds: 0, ReceiveLossProbability: 0,
  } }]);
});

test('HTML controls expose finite ranges and accept the zero and maximum settings', () => {
  const h = harness();
  h.receive(settings());
  for (const [id, , maximum] of controls) {
    const input = html.match(new RegExp('<input\\b[^>]*\\bid="' + id + '"[^>]*>'))?.[0];
    assert.ok(input, 'input missing ' + id);
    assert.match(input, /\btype="number"/);
    assert.match(input, /\bmin="0"/);
    assert.ok(input.includes('max="' + maximum + '"'), 'HTML bound differs for ' + id);
    h.element(id).value = '0';
  }
  h.apply();
  for (const [, field] of controls) assert.equal(h.sent[0].value[field], 0);
  for (const [id, , maximum] of controls) h.element(id).value = String(maximum);
  h.apply();
  for (const [, field, maximum] of controls) assert.equal(h.sent[1].value[field], maximum === 100 ? 1 : maximum);
});

for (const [id, , maximum] of controls) {
  test(id + ' rejects blank, nonfinite, negative and excessive input without sending', () => {
    const h = harness();
    h.receive(settings());
    for (const value of ['', '   ', '-0.001', 'Infinity', '-Infinity', 'NaN', 'invalid', String(maximum + .001)]) {
      h.element(id).value = value;
      h.apply();
      assert.equal(h.sent.length, 0, id + ' submitted invalid ' + JSON.stringify(value));
      assert.match(h.element('notice').textContent, /0～0\.3 秒.*0～100%/);
    }
  });
}
