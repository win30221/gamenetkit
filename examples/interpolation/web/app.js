const $=id=>document.getElementById(id);
const canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
const descriptions={latest:'直接顯示最新交付位置；快照之間會停住，收到下一份時跳動。',fixed:'固定緩衝後在快照間取樣；移動更連續，但呈現會晚一點。',adaptive:'根據快照到達間隔調整緩衝；抖動後會保守地降低延後時間。'};
let socket,generation=0,frame=null,selected='fixed';
function send(message){if(socket?.readyState===1 && socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function axes(){const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function connect(){
  const attempt=++generation;socket?.close();frame=null;clearInput();$('status').textContent='正在連線…';
  const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';
  try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線，請確認教學服務已啟動';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,profile:$('profile').value,auto:$('auto').checked,self:$('self').checked});};
  socket.onmessage=event=>{
    if(attempt!==generation)return;
    try{frame=JSON.parse(event.data);}catch{$('status').textContent='收到無效的教學資料';return;}
    $('status').textContent=frame.connected?'已連線 · '+(frame.mode==='latest'?'插值關閉':frame.mode==='fixed'?'固定插值':'自適應插值'):frame.status;
    $('identity').textContent=`玩家 ${frame.id||'—'} · ${frame.players.length} 人`;
    $('tick').textContent=frame.tick<0?'—':frame.tick;
    $('render').textContent=frame.renderTick<0?'—':frame.renderTick.toFixed(2);
    $('buffer').textContent=frame.buffer.toFixed(0)+' ms';$('jitter').textContent=frame.jitter.toFixed(0)+' ms';$('dropped').textContent=frame.dropped;
  };
  const closed=()=>{if(attempt!==generation)return;frame=null;$('status').textContent='連線已結束，按「重新連線」再加入';};
  socket.onerror=closed;socket.onclose=closed;
}
for(const mode of ['latest','fixed','adaptive'])$('mode-'+mode).addEventListener('click',()=>{
  selected=mode;for(const id of ['latest','fixed','adaptive'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));
  $('explanation').textContent=descriptions[mode];send({mode});
});
$('profile').addEventListener('change',()=>send({profile:$('profile').value}));
$('auto').addEventListener('change',()=>{clearInput();send({auto:$('auto').checked});});
$('self').addEventListener('change',()=>send({self:$('self').checked}));
$('reconnect').addEventListener('click',connect);
$('disconnect').addEventListener('click',()=>{clearInput();send({action:'disconnect'});});
const movement=new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);
canvas.addEventListener('pointerdown',()=>canvas.focus());
canvas.addEventListener('keydown',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.add(key);axes();}});
canvas.addEventListener('keyup',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.delete(key);axes();}});
canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);
document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
function draw(){
  const w=canvas.width,h=canvas.height,unit=Math.min(w/20,h/20),cx=w/2,cy=h/2;
  ctx.clearRect(0,0,w,h);ctx.strokeStyle='#24364a';ctx.lineWidth=1;
  for(let n=-8;n<=8;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-8*unit);ctx.lineTo(cx+n*unit,cy+8*unit);ctx.moveTo(cx-8*unit,cy+n*unit);ctx.lineTo(cx+8*unit,cy+n*unit);ctx.stroke();}
  ctx.strokeStyle='#55758a';ctx.strokeRect(cx-8*unit,cy-8*unit,16*unit,16*unit);
  ctx.font='13px sans-serif';ctx.textAlign='center';
  for(const p of frame?.players??[]){
    const own=p.id===frame.id,size=unit*.7,color=own?'#66b6ff':'#ffb56a';ctx.fillStyle=color;ctx.strokeStyle=color;
    if($('ghost').checked){ctx.lineWidth=2;ctx.strokeRect(cx+p.latestX*unit-size*.65,cy-p.latestZ*unit-size*.65,size*1.3,size*1.3);}
    ctx.fillRect(cx+p.x*unit-size/2,cy-p.z*unit-size/2,size,size);ctx.fillText((own?'你':'玩家')+' #'+p.id,cx+p.x*unit,cy-p.z*unit-size);
  }
  requestAnimationFrame(draw);
}
const heartbeat=setInterval(()=>{if(document.activeElement===canvas&&!document.hidden)axes();},200);
window.addEventListener('pagehide',()=>{clearInterval(heartbeat);++generation;socket?.close();});
connect();requestAnimationFrame(draw);
