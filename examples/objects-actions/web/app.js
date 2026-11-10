const $=id=>document.getElementById(id);
const canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
let socket,generation=0,frame=null,selected='prediction',correction='smooth',remote='adaptive',tab='objects',lastResult=null;
const kinds={spawn:'新增標記',remove:'移除標記',rebirth:'同 ID 重生',deny:'模擬拒絕',timeout:'模擬沒有回覆'};
const reasons={capacity:'已達每人 4 個標記上限',empty:'目前沒有自己的標記',demo_rejected:'伺服器拒絕了這次動作',busy:'等待中的動作太多'};
function send(message){if(socket?.readyState===1 && socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function axes(){const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function controls(){
  const busy=!frame?.connected||frame.pending>0,owned=(frame?.objects??[]).filter(o=>o.owner===frame?.id).length;
  for(const id of ['spawn','accept','deny','timeout'])$(id).disabled=busy;
  for(const id of ['remove','rebirth'])$(id).disabled=busy||!owned;
  $('resend').disabled=!frame?.connected||!lastResult;
}
function render(){
  $('status').textContent=frame.connected?'已連線':frame.status;
  $('identity').textContent=`玩家 ${frame.id||'—'} · ${frame.players.length} 人`;
  $('object-count').textContent=`目前 ${frame.objects.length} 個標記`;
  $('replica-stats').textContent=`SDK 物件差異：新增 ${frame.added} 次 · 移除 ${frame.removed} 次（同 ID 重生 = 移除舊生命＋新增新生命）`;
  $('objects-list').textContent=frame.objects.map(o=>`${o.id} · life ${o.life} · 玩家 ${o.owner}`).join('\n')||'世界目前沒有標記。';
  $('changes').textContent=frame.changes.join('\n');
  $('action-stats').textContent=`SDK 動作：等待 ${frame.pending} · 確認 ${frame.confirmed} · 拒絕 ${frame.rejected} · 逾時 ${frame.expired}`;
  const latest=frame.actions.at(-1);if(latest)lastResult=latest;
  if(!frame.connected){lastResult=null;$('feedback').textContent=frame.status==='Connecting'?'正在加入世界…':'連線中斷，按「重新連線」再加入。';$('feedback').setAttribute('data-phase','');}
  else if(lastResult){
    const row=lastResult,phase={pending:row.speculative?'等待確認：黃色只是暫時效果':'等待確認：尚未顯示效果',confirmed:'已確認：伺服器接受了動作',rejected:'已拒絕：'+(reasons[row.reason]||'動作未成立'),expired:'已逾時：暫時效果已收回，尚未確認結果'};
    $('feedback').textContent=`#${row.id} ${kinds[row.kind]} — ${phase[row.phase]}`;$('feedback').setAttribute('data-phase',row.phase);
  }
  else{$('feedback').textContent='先按「新增標記」，看看伺服器確認後的變化。';$('feedback').setAttribute('data-phase','');}
  controls();
}
function connect(){
  const attempt=++generation;socket?.close();frame=null;lastResult=null;clearInput();controls();$('status').textContent='正在連線…';
  const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';
  try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線，請確認教學服務已啟動';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,correction,remote,profile:$('profile').value,auto:$('auto').checked});};
  socket.onmessage=event=>{if(attempt!==generation)return;try{frame=JSON.parse(event.data);render();}catch{$('status').textContent='收到無效的教學資料';}};
  const closed=()=>{if(attempt!==generation)return;frame=null;lastResult=null;controls();$('status').textContent='連線已結束，按「重新連線」再加入';};
  socket.onerror=closed;socket.onclose=closed;
}
for(const name of ['objects','actions'])$('tab-'+name).addEventListener('click',()=>{
  tab=name;for(const value of ['objects','actions']){$('tab-'+value).setAttribute('aria-selected',String(value===name));$('panel-'+value).hidden=value!==name;}
});
for(const [id,kind] of [['spawn','spawn'],['remove','remove'],['rebirth','rebirth'],['accept','spawn'],['deny','deny'],['timeout','timeout']])$(id).addEventListener('click',()=>{
  if($(id).disabled)return;send({perform:kind,speculative:tab==='actions'&&$('speculative').checked});
});
$('resend').addEventListener('click',()=>send({resend:true}));
for(const mode of ['authority','prediction'])$('mode-'+mode).addEventListener('click',()=>{
  selected=mode;for(const id of ['authority','prediction'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));send({mode});
});
for(const value of ['snap','smooth'])$('correction-'+value).addEventListener('click',()=>{
  correction=value;for(const id of ['snap','smooth'])$('correction-'+id).setAttribute('aria-pressed',String(id===value));send({correction});
});
for(const value of ['latest','fixed','adaptive'])$('remote-'+value).addEventListener('click',()=>{
  remote=value;for(const id of ['latest','fixed','adaptive'])$('remote-'+id).setAttribute('aria-pressed',String(id===value));send({remote});
});
$('profile').addEventListener('change',()=>send({profile:$('profile').value}));
$('auto').addEventListener('change',()=>{clearInput();send({auto:$('auto').checked});});
$('bump').addEventListener('click',()=>send({action:'bump'}));
$('disconnect').addEventListener('click',()=>{clearInput();send({action:'disconnect'});});
$('reconnect').addEventListener('click',connect);
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
  ctx.font='12px sans-serif';ctx.textAlign='center';
  for(const o of frame?.objects??[]){
    const x=cx+o.x*unit,y=cy-o.z*unit,r=9;ctx.fillStyle=o.owner===frame.id?'#77d5a4':'#c5b0fb';ctx.beginPath();ctx.moveTo(x,y-r);ctx.lineTo(x+r,y);ctx.lineTo(x,y+r);ctx.lineTo(x-r,y);ctx.closePath();ctx.fill();ctx.fillText(`${o.id} · life ${o.life}`,x,y-17);
  }
  for(const a of frame?.actions??[]){if(!a.visible)continue;ctx.strokeStyle={pending:'#efc85e',confirmed:'#77d5a4',rejected:'#f38d8d'}[a.phase]||'#9baaba';ctx.lineWidth=3;ctx.beginPath();ctx.arc(cx+a.x*unit,cy-a.z*unit,17,0,Math.PI*2);ctx.stroke();}
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
