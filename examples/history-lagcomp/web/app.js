const $=id=>document.getElementById(id),canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
let socket,generation=0,frame=null,tab='history',compensate=false,selected='prediction',correction='smooth',remote='adaptive';
const reasons={outside_window:'超過允許的回溯時間窗',unobserved_tick:'要求尚未可用的時間',life_changed:'目標已建立新生命',missing_history:'該時間沒有目前生命的歷史'};
function send(message){if(socket?.readyState===1&&socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function axes(){const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function controls(){const busy=!frame?.connected||frame.pending>0||!frame.target;$('shoot').disabled=busy;$('expired-shot').disabled=busy||frame.tick<31;$('reset-target').disabled=!frame?.connected;}
function render(){
  $('status').textContent=frame.connected?'已連線':frame.status;$('identity').textContent=`玩家 ${frame.id||'—'} · ${frame.players.length} 人`;
  $('target-status').textContent=frame.target?`life ${frame.target.life} · ${frame.target.hp}/5`:'—';$('score').textContent=`${frame.hits} / ${frame.misses}`;
  const history=frame.history;$('history-span').textContent=history?((history.last-history.first)/30).toFixed(1)+' 秒':'—';$('lookback-label').textContent=Number($('lookback').value).toFixed(1)+' 秒';
  let feedback='淺藍圈是查詢結果；查詢只讀取歷史，綠色目標持續向前模擬。';
  if(tab==='history'&&frame.preview)feedback=frame.preview.found?`查到 tick ${frame.preview.tick} 的位置，當下世界仍繼續移動。`:'目前尚無該時刻的歷史，稍等或減少回溯秒數。';
  if(tab==='shoot'){
    feedback='按「對準畫面目標射擊」，比較現在與歷史位置的判定。';
    const shot=frame.lastShot;
    if(shot)feedback=!shot.accepted?'伺服器拒絕：'+(reasons[shot.reason]||shot.reason):`${shot.compensated?'採回溯位置':'採目前位置'}：${shot.hit?'命中，伺服器扣血':'未命中'}。目前判定 ${shot.currentHit?'命中':'未中'}，歷史判定 ${shot.historicalHit?'命中':'未中'}。`;
    if(frame.pending>0)feedback='等待伺服器確認射擊結果…';
  }
  if(!frame.connected)feedback=frame.status==='Connecting'?'正在加入世界…':'連線中斷，按「重新連線」再加入。';$('feedback').textContent=feedback;
  $('history-info').textContent=history?`SDK 歷史 ${history.count}/90 份 · tick ${history.first}～${history.last} · 補償窗 ${history.maxRewind/30} 秒 · 拒絕 ${frame.rejectedShots} 次`:'';
  const shot=frame.lastShot;$('shot-info').textContent=shot?`射擊 #${shot.id} · life ${shot.life} · 判定 tick ${shot.shotTick} · 查詢 tick ${shot.usedTick}\n目前位置 (${shot.currentX.toFixed(2)}, ${shot.currentZ.toFixed(2)}) · 歷史位置 (${shot.pastX.toFixed(2)}, ${shot.pastZ.toFixed(2)})`:'';controls();
}
function connect(){const attempt=++generation;socket?.close();frame=null;clearInput();controls();$('status').textContent='正在連線…';const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,correction,remote,profile:$('profile').value,inspect:tab==='history'&&$('inspect').checked,lookback:Number($('lookback').value),compensate});};
  socket.onmessage=e=>{if(attempt!==generation)return;try{frame=JSON.parse(e.data);render();}catch{$('status').textContent='收到無效的教學資料';}};
  const closed=()=>{if(attempt!==generation)return;frame=null;controls();$('status').textContent='連線已結束，按「重新連線」再加入';};socket.onerror=closed;socket.onclose=closed;
}
for(const name of ['history','shoot'])$('tab-'+name).addEventListener('click',()=>{tab=name;for(const value of ['history','shoot']){$('tab-'+value).setAttribute('aria-selected',String(value===name));$('panel-'+value).hidden=value!==name;}send({inspect:name==='history'&&$('inspect').checked});if(frame)render();});
$('inspect').addEventListener('change',()=>send({inspect:tab==='history'&&$('inspect').checked}));$('lookback').addEventListener('input',()=>{$('lookback-label').textContent=Number($('lookback').value).toFixed(1)+' 秒';send({lookback:Number($('lookback').value)});});
for(const value of [false,true])$(value?'compensated':'current').addEventListener('click',()=>{compensate=value;$('current').setAttribute('aria-pressed',String(!value));$('compensated').setAttribute('aria-pressed',String(value));send({compensate:value});});
$('shoot').addEventListener('click',()=>{if(!$('shoot').disabled)send({shoot:true});});$('expired-shot').addEventListener('click',()=>{if(!$('expired-shot').disabled)send({expiredShot:true});});$('reset-target').addEventListener('click',()=>send({resetTarget:true}));
for(const mode of ['authority','prediction'])$('mode-'+mode).addEventListener('click',()=>{selected=mode;for(const id of ['authority','prediction'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));send({mode});});
for(const value of ['snap','smooth'])$('correction-'+value).addEventListener('click',()=>{correction=value;for(const id of ['snap','smooth'])$('correction-'+id).setAttribute('aria-pressed',String(id===value));send({correction});});
for(const value of ['latest','fixed','adaptive'])$('remote-'+value).addEventListener('click',()=>{remote=value;for(const id of ['latest','fixed','adaptive'])$('remote-'+id).setAttribute('aria-pressed',String(id===value));send({remote});});$('profile').addEventListener('change',()=>send({profile:$('profile').value}));$('disconnect').addEventListener('click',()=>{clearInput();send({action:'disconnect'});});$('reconnect').addEventListener('click',connect);
const movement=new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);canvas.addEventListener('pointerdown',()=>canvas.focus());canvas.addEventListener('keydown',e=>{const key=e.key.toLowerCase();if(movement.has(key)){e.preventDefault();keys.add(key);axes();}});canvas.addEventListener('keyup',e=>{const key=e.key.toLowerCase();if(movement.has(key)){e.preventDefault();keys.delete(key);axes();}});canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
function draw(){const w=canvas.width,h=canvas.height,unit=Math.min(w/20,h/20),cx=w/2,cy=h/2;ctx.clearRect(0,0,w,h);ctx.strokeStyle='#24364a';ctx.lineWidth=1;for(let n=-8;n<=8;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-8*unit);ctx.lineTo(cx+n*unit,cy+8*unit);ctx.moveTo(cx-8*unit,cy+n*unit);ctx.lineTo(cx+8*unit,cy+n*unit);ctx.stroke();}ctx.font='13px sans-serif';ctx.textAlign='center';
  function circle(x,z,color,fill=false){ctx.strokeStyle=color;ctx.fillStyle=color;ctx.lineWidth=3;ctx.beginPath();ctx.arc(cx+x*unit,cy-z*unit,.35*unit,0,Math.PI*2);if(fill)ctx.fill();else ctx.stroke();}
  if(tab==='history'&&frame?.preview?.found){const p=frame.preview.target;circle(p.x,p.z,'#9dceff');ctx.fillStyle='#9dceff';ctx.fillText('歷史 '+frame.preview.tick,cx+p.x*unit,cy-p.z*unit-18);}
  const shot=frame?.lastShot;if(tab==='shoot'&&shot?.accepted&&frame.tick-shot.shotTick<45){circle(shot.pastX,shot.pastZ,'#b59af3');circle(shot.currentX,shot.currentZ,'#efb36c');ctx.strokeStyle=shot.hit?'#77d5a4':'#f38d8d';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(cx+shot.originX*unit,cy-shot.originZ*unit);ctx.lineTo(cx+shot.aimX*unit,cy-shot.aimZ*unit);ctx.stroke();}
  if(frame?.target){const t=frame.target;circle(t.x,t.z,'#77d5a4',true);ctx.fillStyle='#77d5a4';ctx.fillText(`目標 · life ${t.life} · ${t.hp}/5`,cx+t.x*unit,cy-t.z*unit-18);}
  for(const p of frame?.players??[]){ctx.fillStyle=p.id===frame.id?'#66b6ff':'#ffb56a';ctx.fillRect(cx+p.x*unit-8,cy-p.z*unit-8,16,16);ctx.fillText((p.id===frame.id?'你':'玩家')+' #'+p.id,cx+p.x*unit,cy-p.z*unit-16);}requestAnimationFrame(draw);
}
const heartbeat=setInterval(()=>{if(document.activeElement===canvas&&!document.hidden)axes();},200);window.addEventListener('pagehide',()=>{clearInterval(heartbeat);++generation;socket?.close();});connect();requestAnimationFrame(draw);
