import {createTransport} from '/transport.js';
import {SharedWorldSession} from './session.mjs';

const $ = id => document.getElementById(id);
const canvas = $('world'), ctx = canvas.getContext('2d'), keys = new Set();
const endpoint = new URL('/ws', location.href); endpoint.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
$('endpoint').textContent = endpoint.host;
const labels = {connected:'已連線 · 世界同步中', connecting:'連線中…', disconnected:'尚未連線'};
const session = new SharedWorldSession(createTransport, state => {
  $('status').textContent = state.error ? `連線已結束 · ${state.error}` : labels[state.status];
  $('light').className = `light ${state.status}`;
  $('player').textContent = state.playerID ?? '—'; $('tick').textContent = state.tick < 0 ? '—' : state.tick;
  $('count').replaceChildren(document.createTextNode(`${state.players.length} `), Object.assign(document.createElement('small'), {textContent:'/ 8'}));
  $('connect').disabled = state.status === 'connecting'; $('connect').textContent = state.status === 'disconnected' ? '連線' : '重新連線';
  $('disconnect').disabled = state.status === 'disconnected';
  if (state.status !== 'connected') keys.clear();
  draw(state);
});
function draw(state) {
  const w=canvas.width,h=canvas.height,unit=Math.min(w/20,h/20),cx=w/2,cy=h/2;
  ctx.clearRect(0,0,w,h); ctx.strokeStyle='#233045'; ctx.lineWidth=1;
  for(let n=-8;n<=8;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-8*unit);ctx.lineTo(cx+n*unit,cy+8*unit);ctx.moveTo(cx-8*unit,cy+n*unit);ctx.lineTo(cx+8*unit,cy+n*unit);ctx.stroke();}
  ctx.strokeStyle='#435671';ctx.strokeRect(cx-8*unit,cy-8*unit,16*unit,16*unit);
  ctx.font='12px "Segoe UI",sans-serif';ctx.textAlign='center';
  if(!state.players.length){ctx.fillStyle='#697e9d';ctx.fillText('連線後，方塊將出現在這裡',cx,cy);}
  for(const p of state.players){const x=cx+p.x*unit,y=cy-p.z*unit,self=p.id===state.playerID,size=unit*.65;
    ctx.fillStyle=self?'#609dff':'#ffad65';ctx.shadowColor=ctx.fillStyle;ctx.shadowBlur=self?18:8;ctx.fillRect(x-size/2,y-size/2,size,size);ctx.shadowBlur=0;
    ctx.fillStyle=self?'#b9d5ff':'#eed0b8';ctx.fillText(`${self?'你 · ':''}#${p.id}`,x,y-size/2-12);
  }
}
const movement = new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);
function clearInput(){keys.clear();void session.input(0,0);$('hint').textContent='點擊場地取得控制；離開焦點會立即停止';}
canvas.addEventListener('keydown',e=>{const k=e.key.toLowerCase();if(movement.has(k)){e.preventDefault();keys.add(k);}});
canvas.addEventListener('keyup',e=>{const k=e.key.toLowerCase();if(movement.has(k)){e.preventDefault();keys.delete(k);pump();}});
canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);
document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
canvas.addEventListener('focus',()=>{$('hint').textContent='控制中 · WASD / 方向鍵移動';});
canvas.addEventListener('pointerdown',()=>canvas.focus());
function pump(){if(document.activeElement!==canvas||document.hidden)return;const down=(...list)=>list.some(k=>keys.has(k))?1:0;void session.input(down('d','arrowright')-down('a','arrowleft'),down('w','arrowup')-down('s','arrowdown'));}
const timer=setInterval(pump,1000/30);
$('connect').addEventListener('click',()=>{keys.clear();session.connect(endpoint.href);});
$('disconnect').addEventListener('click',()=>{clearInput();session.disconnect();});
window.addEventListener('pagehide',()=>{clearInterval(timer);session.disconnect();});
