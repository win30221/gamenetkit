import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('./mobile-view.js', import.meta.url), 'utf8');
function setup({coarse = true, portrait = true, allowFullscreen = true} = {}) {
  const media = new Map();
  class Element {
    children = []; events = {}; hidden = false; attributes = {};
    classList = { toggle() {} };
    setAttribute(key, value) { this.attributes[key] = value; }
    append(...values) { this.children.push(...values); }
    addEventListener(name, handler) { this.events[name] = handler; }
  }
  const container = new Element(), document = new Element();
  document.fullscreenElement = null; document.documentElement = new Element();
  document.getElementById = () => container; document.createElement = () => new Element();
  const screen = { orientation: { locks: [], async lock(value) { this.locks.push(value); if (!allowFullscreen) throw Error('not supported'); } } };
  container.requestFullscreen = async () => { if (!allowFullscreen) throw Error('not supported'); document.fullscreenElement = container; };
  const matchMedia = key => {
    if (!media.has(key)) media.set(key, { matches: key.includes('coarse') ? coarse : portrait, addEventListener(name, handler) { this.changed = handler; } });
    return media.get(key);
  };
  vm.runInNewContext(source, { document, matchMedia, screen });
  return { container, document, screen, media, prompt: container.children[0], fullscreen: container.children[1] };
}
test('portrait phone is guided to landscape; rotating reveals the game', () => {
  const app = setup();
  assert.equal(app.prompt.hidden, false); assert.equal(app.fullscreen.hidden, true);
  const orientation = app.media.get('(orientation: portrait)'); orientation.matches = false; orientation.changed();
  assert.equal(app.prompt.hidden, true); assert.equal(app.fullscreen.hidden, false);
});
test('fullscreen and landscape lock require the explicit button click', async () => {
  const app = setup(); assert.equal(app.document.fullscreenElement, null); assert.equal(app.screen.orientation.locks.length, 0);
  await app.prompt.children[2].events.click();
  assert.equal(app.document.fullscreenElement, app.container); assert.deepEqual(app.screen.orientation.locks, ['landscape']);
});
test('unsupported fullscreen leaves manual rotation guidance and can recover', async () => {
  const app = setup({ allowFullscreen: false });
  await app.prompt.children[2].events.click();
  assert.match(app.prompt.children[1].textContent, /手動橫放/);
  const orientation = app.media.get('(orientation: portrait)'); orientation.matches = false; orientation.changed();
  assert.equal(app.prompt.hidden, true);
});
test('desktop narrow windows are not blocked; real tablet touch enables guidance', () => {
  const app = setup({ coarse: false }); assert.equal(app.prompt.hidden, true);
  app.container.events.pointerdown({ pointerType: 'touch' }); assert.equal(app.prompt.hidden, false);
});
