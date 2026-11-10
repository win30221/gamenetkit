// Browser chrome only. Game input stays in Unity's touch controls.
const container = document.getElementById('unity-container');
if (container) {
  container.addEventListener('contextmenu', event => event.preventDefault());
  const coarse = matchMedia('(pointer: coarse)');
  const portrait = matchMedia('(orientation: portrait)');
  const prompt = document.createElement('section');
  prompt.className = 'mobile-orientation';
  prompt.setAttribute('aria-label', '手機橫向遊玩');
  const title = document.createElement('h2');
  title.textContent = '請橫放手機遊玩';
  const guide = document.createElement('p');
  guide.textContent = '左側移動，右側普攻、技能與衝刺。';
  const action = document.createElement('button');
  action.type = 'button'; action.textContent = '橫向全螢幕';
  prompt.append(title, guide, action);
  const fullscreen = document.createElement('button');
  fullscreen.type = 'button'; fullscreen.className = 'mobile-fullscreen';
  fullscreen.textContent = '全螢幕'; fullscreen.setAttribute('aria-label', '橫向全螢幕');
  container.append(prompt, fullscreen);
  let touch = coarse.matches;
  function refresh() {
    const mobile = touch || coarse.matches;
    prompt.hidden = !mobile || !portrait.matches;
    fullscreen.hidden = !mobile || portrait.matches || !!document.fullscreenElement;
    document.documentElement.classList.toggle('mobile-game', mobile);
    container.setAttribute('aria-describedby', mobile && portrait.matches ? 'mobile-orientation-guide' : '');
  }
  guide.id = 'mobile-orientation-guide';
  async function landscape() {
    try {
      if (!document.fullscreenElement && container.requestFullscreen) {
        await container.requestFullscreen({ navigationUI: 'hide' });
      }
      if (screen.orientation?.lock) await screen.orientation.lock('landscape');
    } catch {
      // iOS and embedded browsers may require the user to rotate the device.
    }
    if (portrait.matches) guide.textContent = '請手動橫放手機；若畫面未旋轉，請關閉手機的旋轉鎖定。';
    refresh();
  }
  action.addEventListener('click', landscape);
  fullscreen.addEventListener('click', landscape);
  // Also recognize tablets whose browser requests a desktop user agent.
  container.addEventListener('pointerdown', event => {
    if (event.pointerType === 'touch' && !touch) { touch = true; refresh(); }
  }, { passive: true });
  coarse.addEventListener('change', refresh);
  portrait.addEventListener('change', refresh);
  document.addEventListener('fullscreenchange', refresh);
  refresh();
}
