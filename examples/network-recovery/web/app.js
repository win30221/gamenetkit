const $=id=>document.getElementById(id);
const canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
const descriptions={authority:'等待權威快照，回包延遲會直接影響看到自己移動的時間。',prediction:'立即模擬自己的輸入，收到權威後重播尚未確認的部分。'};
let socket,generation=0,frame=null,selected='prediction',correction='smooth',remote='adaptive',clock='disciplined',guideAction='normal';
function send(message){if(socket?.readyState===1 && socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function axes(){const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function connect(){
  const attempt=++generation;socket?.close();frame=null;clearInput();$('status').textContent='正在連線…';
  const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';
  try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線，請確認教學服務已啟動';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,correction,remote,clock,profile:$('profile').value==='custom'?'delay':$('profile').value,auto:$('auto').checked,autoretry:$('auto-retry').checked,diagnostics:$('diagnostics').checked});};
  socket.onmessage=event=>{
    if(attempt!==generation)return;
    try{frame=JSON.parse(event.data);}catch{$('status').textContent='收到無效的教學資料';return;}
    $('status').textContent=frame.connected?'已連線 · '+(frame.mode==='authority'?'等待權威':frame.correction==='snap'?'預測＋直接校正':'預測＋平滑校正'):frame.status;
    $('identity').textContent=`玩家 ${frame.id||'—'} · ${frame.players.length} 人`;
    $('tick').textContent=frame.tick<0?'—':frame.tick;
    $('ack').textContent=frame.ack;$('error').textContent=frame.error.toFixed(3);$('offset').textContent=frame.offset.toFixed(3);$('replay').textContent=frame.replays;
    $('remote-buffer').textContent='遠端緩衝 '+(frame.remoteBuffer??0).toFixed(0)+' ms';
    updateNetworkPanel();
    updateGuide();
  };
  const closed=()=>{if(attempt!==generation)return;frame=null;$('status').textContent='連線已結束，按「重新連線」再加入';};
  socket.onerror=closed;socket.onclose=closed;
}
for(const mode of ['authority','prediction'])$('mode-'+mode).addEventListener('click',()=>{
  selected=mode;for(const id of ['authority','prediction'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));
  $('explanation').textContent=descriptions[mode];send({mode});
});
$('profile').addEventListener('change',()=>{if($('profile').value==='custom')applyNetwork();else send({profile:$('profile').value});});
$('auto').addEventListener('change',()=>{clearInput();send({auto:$('auto').checked});});
for(const value of ['snap','smooth'])$('correction-'+value).addEventListener('click',()=>{
  correction=value;for(const id of ['snap','smooth'])$('correction-'+id).setAttribute('aria-pressed',String(id===value));send({correction});
});
$('bump').addEventListener('click',()=>send({action:'bump'}));
for(const value of ['latest','fixed','adaptive'])$('remote-'+value).addEventListener('click',()=>{
  remote=value;for(const id of ['latest','fixed','adaptive'])$('remote-'+id).setAttribute('aria-pressed',String(id===value));send({remote});
});
$('reconnect').addEventListener('click',connect);
$('disconnect').addEventListener('click',()=>{clearInput();send({action:'disconnect'});});
$('guide-normal').addEventListener('click',()=>{
  guideAction='normal';$('auto').checked=true;$('auto-retry').checked=true;$('profile').value='clean';
  $('ghost').checked=false;$('target').checked=false;connect();
});
$('guide-outage').addEventListener('click',()=>{
  guideAction='outage';clearInput();$('auto-retry').checked=true;send({autoretry:true,action:'outage'});
});
$('guide-stop').addEventListener('click',()=>{
  guideAction='manual';clearInput();send({action:'disconnect'});
});
function updateGuide(){
  let message,status;
  if(frame.connected){
    status=frame.timing.snapshotAge>.8?'連線還在，畫面資料暫停中':'連線正常';
    message=guideAction==='outage'&&frame.recovery.rejoins>0?'已自動重新加入！剛才連線中斷後，程式自己重試，讓方塊重新出現。':'方塊正在移動，表示連線正常。按「模擬意外斷線」，看看它能不能自己回來。';
  }else if(frame.recovery.pending){
    status='正在自動恢復連線…';
    message='方塊暫時消失了。先等幾秒，程式會自己重試，不需要一直按重新連線。';
  }else if(guideAction==='manual'||frame.status.includes('stopped')){
    status='已手動停止';message='這次是你主動停止，所以程式不會自動連回。按「正常移動／重新開始」再加入。';
  }else{
    status='尚未連線';message='按「正常移動／重新開始」加入世界。若仍無法連線，確認教學服務的終端還開著。';
  }
  if($('status').textContent!==status)$('status').textContent=status;
  if($('guide-message').textContent!==message)$('guide-message').textContent=message;
}
const settings=[['up','up','up-value',300,' ms'],['down','down','down-value',300,' ms'],['jitter-setting','jitter','jitter-value',100,' ms'],['loss','loss','loss-value',30,'%']];
function applyNetwork(){const network={};for(const [id,key,,max] of settings)network[key]=Math.max(0,Math.min(max,Number($(id).value)||0));$('profile').value='custom';send({network});}
for(const [id,,label,,unit] of settings){$(id).addEventListener('input',()=>{$(label).textContent=$(id).value+unit;});$(id).addEventListener('change',applyNetwork);}
for(const [id,action] of [['pause','pause'],['outage','outage'],['stop-retry','stop'],['reset-metrics','reset-metrics']])$(id).addEventListener('click',()=>{if(action==='outage'||action==='stop')clearInput();send({action});});
$('auto-retry').addEventListener('change',()=>send({autoretry:$('auto-retry').checked}));
$('diagnostics').addEventListener('change',()=>send({diagnostics:$('diagnostics').checked}));
for(const value of ['snapshot','disciplined'])$('clock-'+value).addEventListener('click',()=>{clock=value;for(const id of ['snapshot','disciplined'])$('clock-'+id).setAttribute('aria-pressed',String(id===value));send({clock});});
const eventNames={connecting:'開始連線',connected:'連線成功',lost:'連線中斷',scheduled:'安排重試',retry:'嘗試重連',expired:'重試期限已到',stopped:'已停止',auto:'自動重連',resumed:'快照恢復'};
function updateNetworkPanel(){
  const {network:n,recovery:r,diagnostics:d,timing:t}=frame;
  if(!settings.some(([id])=>document.activeElement===$(id)))for(const [id,key,label,,unit] of settings){$(id).value=String(n[key]);$(label).textContent=n[key]+unit;}
  $('queue').textContent=`待送 ${n.pendingSend}／待收 ${n.pendingReceive}；送出丟失 ${n.droppedSend}／快照丟失 ${n.droppedReceive}`+(n.pause>0?`；快照暫停剩 ${n.pause.toFixed(1)} 秒`:'');
  $('recovery-status').textContent=r.pending?`第 ${r.attempts} 次重試；下次等待 ${r.next.toFixed(1)} 秒`+(r.blocked>0?`；模擬斷線剩 ${r.blocked.toFixed(1)} 秒`:''):`目前沒有待執行重試；重新加入 ${r.rejoins} 次`;
  if(r.pending)$('status').textContent=r.next>0?'等待自動重連…':'正在嘗試恢復連線…';
  else if(frame.status==='Reconnect window expired')$('status').textContent='重試期限已到，請手動重新連線';
  else if(frame.connected&&t.snapshotAge>.8)$('status').textContent='快照停止交付，預測已達停止期限';
  $('events').textContent=r.events.map(e=>`${e.at.toFixed(1)}s ${eventNames[e.kind]??e.kind} · ${e.detail}`).join('\n');
  const ms=(value,known=true)=>!d.enabled?'已停用':known?value.toFixed(1)+' ms':'暖機中';
  $('rtt').textContent=ms(d.rtt,d.rttSamples>0);$('rtt-p95').textContent=ms(d.rttP95,d.rttSamples>0);
  $('input-reply').textContent=ms(d.inputReply,d.inputReply>0);$('snapshot-gap').textContent=ms(d.snapshotGap,d.snapshotGap>0);$('snapshot-p95').textContent=ms(d.snapshotP95,d.snapshotP95>0);
  $('sent').textContent=d.enabled?d.sent+' B/s':'已停用';$('received').textContent=d.enabled?d.received+' B/s':'已停用';
  $('clock-tick').textContent=frame.connected?t.estimated.toFixed(2):'—';$('snapshot-age').textContent=frame.connected?(t.snapshotAge*1000).toFixed(0)+' ms':'—';$('frame-ms').textContent=ms(d.frameMS,d.frameMS>0);
}
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
    if(own && $('target').checked){ctx.fillStyle='#ffe58a';ctx.fillRect(cx+p.predictedX*unit-3,cy-p.predictedZ*unit-3,6,6);}
  }
  requestAnimationFrame(draw);
}
const heartbeat=setInterval(()=>{if(document.activeElement===canvas&&!document.hidden)axes();},200);
window.addEventListener('pagehide',()=>{clearInterval(heartbeat);++generation;socket?.close();});
connect();requestAnimationFrame(draw);
