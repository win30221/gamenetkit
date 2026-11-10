const $=id=>document.getElementById(id),canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
let socket,generation=0,frame=null,selected='prediction',correction='smooth',remote='adaptive',seeking=false,pendingRecord='';
const reasons={recording_limit:'本次服務已達 16 次錄製上限，重新啟動此教學可繼續。',recording_failed:'錄製未完成，請重新錄製。',recording_unavailable:'錄製服務尚未就緒。'};
function send(message){if(socket?.readyState===1&&socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function live(){return !!frame?.connected&&frame.view==='live'&&!frame.loading;}
function axes(){if(!live())return;const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function controls(){
  const recording=frame?.record.status==='recording',finishing=frame?.record.status==='finalizing';
  $('record-start').disabled=!live()||recording||finishing||!!pendingRecord;
  $('record-stop').disabled=!live()||!recording||!!pendingRecord;
  $('play').disabled=!frame||frame.loading||!!pendingRecord||recording||finishing||(frame.view!=='playback'&&frame.record.status!=='ready');
  $('live').disabled=frame?.view!=='playback';
  const owned=(frame?.objects??[]).filter(o=>o.owner===frame?.id).length,busy=!live()||frame.pending>0;
  $('spawn').disabled=busy;for(const id of ['remove','rebirth'])$(id).disabled=busy||!owned;
  $('auto').disabled=!live();$('progress').disabled=frame?.view!=='playback';$('speed').disabled=frame?.view!=='playback';
}
function render(){
  const replay=frame.view==='playback',record=frame.record,playback=frame.playback;
  if(pendingRecord==='start'&&['recording','error'].includes(record.status)||pendingRecord==='stop'&&['finalizing','ready','error'].includes(record.status))pendingRecord='';
  if(!frame.connected)pendingRecord='';
  $('status').textContent=frame.connected?'即時連線已建立':frame.status;
  $('identity').textContent=`玩家 ${frame.liveID||'—'} · ${frame.players.length} 人`;
  $('view-label').textContent=replay?'回放畫面':'即時世界';$('object-count').textContent=`目前 ${frame.objects.length} 個標記`;
  $('record-status').textContent=({idle:'尚未錄製',recording:'● 錄製中',finalizing:'正在完成並驗證錄製…',ready:'✓ 已完成，可播放',error:'錄製失敗'}[record.status]||record.status)+(['recording','ready'].includes(record.status)?` · ${record.duration.toFixed(2)} 秒`:'');
  let message='先按「開始錄製」，再點畫面使用 WASD 或方向鍵移動。';
  if(record.status==='recording')message='錄製中：移動角色，或新增／移除／重生標記，完成後按「結束錄製」。';
  if(record.status==='finalizing')message='正在寫入並驗證完整錄製，完成後就能播放。';
  if(record.status==='ready')message='錄製已完成。按「播放紀錄」，看一次剛才的操作。';
  if(record.status==='error')message=reasons[record.reason]||'錄製失敗，請重新錄製。';
  if(replay)message=playback.ended?'播放完畢。可重新播放、拖曳進度，或返回即時世界。':playback.playing?'正在播放錄製好的權威畫面。':'播放已暫停。可拖曳進度，觀察物件如何出現與消失。';
  if(frame.loading)message='正在載入已驗證的播放檔…';
  if(frame.notice)message=frame.notice;
  if(!frame.connected&&!replay)message='即時連線已中斷，按「重新連線」再加入。';
  $('feedback').textContent=message;$('feedback').setAttribute('data-phase',replay?'playback':record.status);
  $('play').textContent=replay?(playback.ended?'重新播放':playback.playing?'暫停':'繼續播放'):'3. 播放紀錄';
  $('playback-controls').hidden=!replay;$('progress').max=String(playback.duration);
  if(!seeking)$('progress').value=String(playback.position);
  $('position').textContent=`${playback.position.toFixed(2)} / ${playback.duration.toFixed(2)} 秒`;$('speed').value=String(playback.speed);
  $('clip-info').textContent=`錄製 ${record.frames} 份權威快照 · 回放 ${playback.frames} 份 · 目前第 ${Math.max(0,playback.frameIndex+1)} 份 · 即時 tick ${frame.liveTick}`;
  $('objects-list').textContent=frame.objects.map(o=>`${o.id} · life ${o.life} · 玩家 ${o.owner}`).join('\n')||'目前沒有標記。';
  $('download').hidden=record.status!=='ready';if(record.status==='ready')$('download').href='/recordings/'+record.id;
  controls();
}
function connect(){
  const attempt=++generation;socket?.close();frame=null;pendingRecord='';clearInput();controls();$('status').textContent='正在連線…';
  const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';
  try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線，請確認教學服務已啟動';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,correction,remote,profile:$('profile').value,auto:$('auto').checked});};
  socket.onmessage=event=>{if(attempt!==generation)return;const old=frame;try{frame=JSON.parse(event.data);if(old?.view!==frame.view){clearInput();if(frame.view==='playback')$('auto').checked=false;}render();}catch{$('status').textContent='收到無效的教學資料';}};
  const closed=()=>{if(attempt!==generation)return;frame=null;pendingRecord='';controls();$('status').textContent='教學連線已結束，按「重新連線」再加入';};socket.onerror=closed;socket.onclose=closed;
}
$('record-start').addEventListener('click',()=>{if($('record-start').disabled)return;pendingRecord='start';controls();send({record:'start'});});
$('record-stop').addEventListener('click',()=>{if($('record-stop').disabled)return;pendingRecord='stop';controls();clearInput();$('auto').checked=false;send({auto:false,record:'stop'});});
$('play').addEventListener('click',()=>{if($('play').disabled)return;clearInput();send({play:true});});
$('live').addEventListener('click',()=>{clearInput();send({view:'live'});});
$('progress').addEventListener('pointerdown',()=>{seeking=true;});
$('progress').addEventListener('input',()=>{if(frame?.view==='playback')send({seek:Number($('progress').value)});});
$('progress').addEventListener('pointerup',()=>{if(frame?.view==='playback')send({seek:Number($('progress').value)});seeking=false;});
$('progress').addEventListener('blur',()=>{seeking=false;});
$('speed').addEventListener('change',()=>send({speed:Number($('speed').value)}));
for(const [id,kind] of [['spawn','spawn'],['remove','remove'],['rebirth','rebirth']])$(id).addEventListener('click',()=>{if(!$(id).disabled)send({perform:kind});});
for(const mode of ['authority','prediction'])$('mode-'+mode).addEventListener('click',()=>{selected=mode;for(const id of ['authority','prediction'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));send({mode});});
for(const value of ['snap','smooth'])$('correction-'+value).addEventListener('click',()=>{correction=value;for(const id of ['snap','smooth'])$('correction-'+id).setAttribute('aria-pressed',String(id===value));send({correction});});
for(const value of ['latest','fixed','adaptive'])$('remote-'+value).addEventListener('click',()=>{remote=value;for(const id of ['latest','fixed','adaptive'])$('remote-'+id).setAttribute('aria-pressed',String(id===value));send({remote});});
$('profile').addEventListener('change',()=>send({profile:$('profile').value}));
$('auto').addEventListener('change',()=>{clearInput();send({auto:$('auto').checked});});
$('disconnect').addEventListener('click',()=>{clearInput();send({connection:'disconnect'});});
$('reconnect').addEventListener('click',connect);
const movement=new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);
canvas.addEventListener('pointerdown',()=>canvas.focus());
canvas.addEventListener('keydown',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();if(live()){keys.add(key);axes();}}});
canvas.addEventListener('keyup',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.delete(key);axes();}});
canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
function draw(){
  const w=canvas.width,h=canvas.height,unit=Math.min(w/20,h/20),cx=w/2,cy=h/2;
  ctx.clearRect(0,0,w,h);ctx.strokeStyle='#24364a';ctx.lineWidth=1;
  for(let n=-8;n<=8;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-8*unit);ctx.lineTo(cx+n*unit,cy+8*unit);ctx.moveTo(cx-8*unit,cy+n*unit);ctx.lineTo(cx+8*unit,cy+n*unit);ctx.stroke();}
  ctx.font='12px sans-serif';ctx.textAlign='center';
  for(const o of frame?.objects??[]){const x=cx+o.x*unit,y=cy-o.z*unit,r=9;ctx.fillStyle=o.owner===frame.id?'#77d5a4':'#c5b0fb';ctx.beginPath();ctx.moveTo(x,y-r);ctx.lineTo(x+r,y);ctx.lineTo(x,y+r);ctx.lineTo(x-r,y);ctx.closePath();ctx.fill();ctx.fillText(`${o.id} · life ${o.life}`,x,y-17);}
  for(const p of frame?.players??[]){const own=p.id===frame.id,size=unit*.7,color=own?'#66b6ff':'#ffb56a';ctx.fillStyle=color;ctx.strokeStyle=color;
    if($('ghost').checked&&frame.view==='live'){ctx.lineWidth=2;ctx.strokeRect(cx+p.latestX*unit-size*.65,cy-p.latestZ*unit-size*.65,size*1.3,size*1.3);}
    ctx.fillRect(cx+p.x*unit-size/2,cy-p.z*unit-size/2,size,size);ctx.fillText((own?'你':'玩家')+' #'+p.id,cx+p.x*unit,cy-p.z*unit-size);
  }
  requestAnimationFrame(draw);
}
const heartbeat=setInterval(()=>{if(document.activeElement===canvas&&!document.hidden)axes();},200);
window.addEventListener('pagehide',()=>{clearInterval(heartbeat);++generation;socket?.close();});
connect();requestAnimationFrame(draw);
