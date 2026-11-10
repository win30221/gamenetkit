const $=id=>document.getElementById(id),canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
let socket,generation=0,frame=null,selected='prediction',correction='smooth',remote='adaptive',interestMode='all';
function send(message){if(socket?.readyState===1&&socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function axes(){const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function controls(){
  const busy=!frame?.connected||frame.pending>0;
  $('spawn').disabled=busy;for(const id of ['remove','rebirth'])$(id).disabled=busy||!frame?.interest?.ownedObjects;
}
function render(){
  $('status').textContent=frame.connected?'已連線':frame.status;$('identity').textContent=`玩家 ${frame.id||'—'}`;
  const info=frame.interest;
  $('received-count').textContent=info?String(frame.objects.length):'—';$('world-count').textContent=info?String(info.totalObjects):'—';$('player-count').textContent=info?`${frame.players.length} / ${info.totalPlayers}`:'—';
  for(const value of ['all','aoi'])$('interest-'+value).setAttribute('aria-pressed',String(info?.mode===value));
  $('explanation').textContent=info?.mode==='aoi'?`只接收附近物件，進入半徑 ${info.radius}，離開半徑 ${info.radius+info.margin}；自己與金色地標常駐。`:'目前接收全部物件。按「範圍同步 AOI」，比較範圍內外的差別。';
  $('feedback').textContent=info?.mode==='aoi'?`收到 ${frame.objects.length} 個物件，權威世界仍有 ${info.totalObjects} 個。走近圈外區域，會再收到當地物件。`:'目前收到全部物件。切到範圍同步後，觀察接收數量變化，世界物件數量會保持。';
  if(!frame.connected)$('feedback').textContent=frame.status==='Connecting'?'正在加入世界…':'連線中斷，按「重新連線」再加入。';
  $('snapshot-bytes').textContent=`最新權威快照：${frame.snapshotBytes} JSON bytes（只計 Go → C# 的應用訊息）`;
  $('replica-stats').textContent=`SDK 接收清單：加入 ${frame.added} 次 · 移出 ${frame.removed} 次；移出可能來自 AOI 或權威移除。`;
  $('objects-list').textContent=frame.objects.map(o=>`${o.id} · life ${o.life} · ${o.kind==='global'?'常駐':o.kind==='landmark'?'地標':'玩家 '+o.owner+' 的標記'}`).join('\n');
  $('changes').textContent=frame.changes.join('\n');controls();
}
function connect(){
  const attempt=++generation;socket?.close();frame=null;clearInput();controls();$('status').textContent='正在連線…';
  const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';
  try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線，請確認教學服務已啟動';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,correction,remote,interest:interestMode,radius:Number($('radius').value),profile:$('profile').value,auto:$('auto').checked});};
  socket.onmessage=event=>{if(attempt!==generation)return;try{frame=JSON.parse(event.data);render();}catch{$('status').textContent='收到無效的教學資料';}};
  const closed=()=>{if(attempt!==generation)return;frame=null;controls();$('status').textContent='連線已結束，按「重新連線」再加入';};socket.onerror=closed;socket.onclose=closed;
}
for(const value of ['all','aoi'])$('interest-'+value).addEventListener('click',()=>{interestMode=value;send({interest:value});});
$('radius').addEventListener('change',()=>send({radius:Number($('radius').value)}));
$('auto').addEventListener('change',()=>{clearInput();send({auto:$('auto').checked});});
for(const [id,kind] of [['spawn','spawn'],['remove','remove'],['rebirth','rebirth']])$(id).addEventListener('click',()=>{if(!$(id).disabled)send({perform:kind});});
for(const mode of ['authority','prediction'])$('mode-'+mode).addEventListener('click',()=>{selected=mode;for(const id of ['authority','prediction'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));send({mode});});
for(const value of ['snap','smooth'])$('correction-'+value).addEventListener('click',()=>{correction=value;for(const id of ['snap','smooth'])$('correction-'+id).setAttribute('aria-pressed',String(id===value));send({correction});});
for(const value of ['latest','fixed','adaptive'])$('remote-'+value).addEventListener('click',()=>{remote=value;for(const id of ['latest','fixed','adaptive'])$('remote-'+id).setAttribute('aria-pressed',String(id===value));send({remote});});
$('profile').addEventListener('change',()=>send({profile:$('profile').value}));
$('disconnect').addEventListener('click',()=>{clearInput();send({action:'disconnect'});});$('reconnect').addEventListener('click',connect);
const movement=new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);
canvas.addEventListener('pointerdown',()=>canvas.focus());canvas.addEventListener('keydown',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.add(key);axes();}});canvas.addEventListener('keyup',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.delete(key);axes();}});
canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
function draw(){
  const w=canvas.width,h=canvas.height,unit=Math.min(w/20,h/20),cx=w/2,cy=h/2;
  ctx.clearRect(0,0,w,h);ctx.strokeStyle='#24364a';ctx.lineWidth=1;
  for(let n=-8;n<=8;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-8*unit);ctx.lineTo(cx+n*unit,cy+8*unit);ctx.moveTo(cx-8*unit,cy+n*unit);ctx.lineTo(cx+8*unit,cy+n*unit);ctx.stroke();}
  const info=frame?.interest;
  if(info){ctx.save();const x=cx+info.centerX*unit,y=cy-info.centerZ*unit;ctx.strokeStyle=info.mode==='aoi'?'#74ce9b':'#6094b4';ctx.fillStyle='rgba(78,173,126,.07)';ctx.lineWidth=2;if(info.mode==='all')ctx.setLineDash([5,5]);ctx.beginPath();ctx.arc(x,y,info.radius*unit,0,Math.PI*2);ctx.fill();ctx.stroke();
    if($('margin').checked){ctx.setLineDash([4,5]);ctx.strokeStyle='#b19b65';ctx.beginPath();ctx.arc(x,y,(info.radius+info.margin)*unit,0,Math.PI*2);ctx.stroke();}ctx.restore();}
  ctx.font='12px sans-serif';ctx.textAlign='center';
  for(const o of frame?.objects??[]){const x=cx+o.x*unit,y=cy-o.z*unit,r=o.kind==='global'?11:8;ctx.fillStyle=o.kind==='global'?'#efc85e':o.kind==='landmark'?'#77d5a4':'#c5b0fb';ctx.beginPath();ctx.moveTo(x,y-r);ctx.lineTo(x+r,y);ctx.lineTo(x,y+r);ctx.lineTo(x-r,y);ctx.closePath();ctx.fill();ctx.fillText(o.kind==='global'?'常駐地標':o.kind==='landmark'?o.id.replace('landmark-','地標 '):`${o.id} · life ${o.life}`,x,y-16);}
  for(const p of frame?.players??[]){const own=p.id===frame.id,size=unit*.7,color=own?'#66b6ff':'#ffb56a';ctx.fillStyle=color;ctx.strokeStyle=color;if($('ghost').checked){ctx.lineWidth=2;ctx.strokeRect(cx+p.latestX*unit-size*.65,cy-p.latestZ*unit-size*.65,size*1.3,size*1.3);}ctx.fillRect(cx+p.x*unit-size/2,cy-p.z*unit-size/2,size,size);ctx.fillText((own?'你':'玩家')+' #'+p.id,cx+p.x*unit,cy-p.z*unit-size);}
  requestAnimationFrame(draw);
}
const heartbeat=setInterval(()=>{if(document.activeElement===canvas&&!document.hidden)axes();},200);
window.addEventListener('pagehide',()=>{clearInterval(heartbeat);++generation;socket?.close();});connect();requestAnimationFrame(draw);
