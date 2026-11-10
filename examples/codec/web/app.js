const $=id=>document.getElementById(id),canvas=$('world'),ctx=canvas.getContext('2d'),keys=new Set();
let socket,generation=0,frame=null,selected='prediction',correction='smooth',remote='adaptive',interestMode='all',codecMode='json';
function send(message){if(socket?.readyState===1&&socket.bufferedAmount<4096)socket.send(JSON.stringify(message));}
function axes(){const down=(...names)=>names.some(name=>keys.has(name))?1:0;send({x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send({x:0,z:0});}
function controls(){const busy=!frame?.connected||frame.pending>0;$('spawn').disabled=busy||frame?.interest?.ownedObjects>=4;for(const id of ['remove','rebirth'])$(id).disabled=busy||!frame?.interest?.ownedObjects;$('forget').disabled=!frame?.connected||frame.codec?.mode!=='delta'||frame.frameKind!=='delta'||frame.recovering;}
function render(){
  $('status').textContent=frame.connected?'已連線':frame.status;$('identity').textContent=`玩家 ${frame.id||'—'}`;
  for(const value of ['json','delta'])$('codec-'+value).setAttribute('aria-pressed',String(frame.codec?.mode===value));
  for(const value of ['all','aoi'])$('interest-'+value).setAttribute('aria-pressed',String(frame.interest?.mode===value));
  $('wire-size').textContent=frame.wireBytes?frame.wireBytes+' B':'—';$('json-size').textContent=frame.jsonBytes?frame.jsonBytes+' B':'—';
  const saving=frame.jsonBytes?100*(1-frame.wireBytes/frame.jsonBytes):0;$('saving').textContent=frame.jsonBytes?(saving>=0?'省下 ':'增加 ')+Math.abs(saving).toFixed(1)+'%':'—';
  $('explanation').textContent=frame.codec?.mode==='delta'?'以已成功解碼的基準還原差分；必要時改傳完整基準。':'直接傳送完整 JSON，還原前後大小相同。';
  $('feedback').textContent=frame.recovering?'缺少解碼基準，正在要求完整快照…':frame.codec?.mode==='delta'&&frame.recoveries>0?'已從完整快照恢復，持續接收差分。':'完整基準需要額外封裝，偶爾會更大；差分只在有利時採用。';
  if(!frame.connected)$('feedback').textContent=frame.status==='Connecting'?'正在加入世界…':'連線中斷，按「重新連線」再加入。';
  const names={json:'完整 JSON',keyframe:'完整基準',delta:'差分',none:'等待快照'};
  $('frame-stats').textContent=`本份：${names[frame.frameKind]} · frame ${frame.frameID} / base ${frame.baseline} · 已解碼 ACK ${frame.frameAck} · stream ${frame.codecStream}\n完整 JSON ${frame.jsonFrames} · 完整基準 ${frame.fullFrames} · 差分 ${frame.deltaFrames} · 缺基準 ${frame.missing} · 恢復 ${frame.recoveries}`;
  $('traffic').textContent=`最近 2 秒平均快照下行：${frame.wirePerSecond.toFixed(0)} B/s`;
  $('quantize-error').textContent=`本份輸出最大單軸量化誤差：${(frame.codec?.maxError??0).toFixed(5)} 單位`;
  $('object-stats').textContent=frame.interest?`收到 ${frame.objects.length} / 世界 ${frame.interest.totalObjects} 個物件；收到 ${frame.players.length} / 世界 ${frame.interest.totalPlayers} 名玩家。`:'';
  $('objects-list').textContent=frame.objects.map(o=>`${o.id} · life ${o.life} · (${o.x.toFixed(3)}, ${o.z.toFixed(3)})`).join('\n');controls();
}
function connect(){const attempt=++generation;socket?.close();frame=null;clearInput();controls();$('status').textContent='正在連線…';const url=new URL('/lesson',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';try{socket=new WebSocket(url.href);}catch{$('status').textContent='無法建立連線，請確認服務已啟動';return;}
  socket.onopen=()=>{if(attempt!==generation)return;send({mode:selected,correction,remote,interest:interestMode,radius:Number($('radius').value),codec:codecMode,quantize:Number($('quantize').value),profile:$('profile').value,auto:$('auto').checked});};
  socket.onmessage=e=>{if(attempt!==generation)return;try{frame=JSON.parse(e.data);render();}catch{$('status').textContent='收到無效的教學資料';}};
  const closed=()=>{if(attempt!==generation)return;frame=null;controls();$('status').textContent='連線已結束，按「重新連線」再加入';};socket.onerror=closed;socket.onclose=closed;
}
for(const value of ['json','delta'])$('codec-'+value).addEventListener('click',()=>{codecMode=value;send({codec:value});});
$('quantize').addEventListener('change',()=>send({quantize:Number($('quantize').value)}));$('forget').addEventListener('click',()=>{if(!$('forget').disabled)send({forget:true});});$('clear-metrics').addEventListener('click',()=>send({clearMetrics:true}));
for(const value of ['all','aoi'])$('interest-'+value).addEventListener('click',()=>{interestMode=value;send({interest:value});});$('radius').addEventListener('change',()=>send({radius:Number($('radius').value)}));$('auto').addEventListener('change',()=>{clearInput();send({auto:$('auto').checked});});
for(const [id,kind] of [['spawn','spawn'],['remove','remove'],['rebirth','rebirth']])$(id).addEventListener('click',()=>{if(!$(id).disabled)send({perform:kind});});
for(const mode of ['authority','prediction'])$('mode-'+mode).addEventListener('click',()=>{selected=mode;for(const id of ['authority','prediction'])$('mode-'+id).setAttribute('aria-pressed',String(id===mode));send({mode});});
for(const value of ['snap','smooth'])$('correction-'+value).addEventListener('click',()=>{correction=value;for(const id of ['snap','smooth'])$('correction-'+id).setAttribute('aria-pressed',String(id===value));send({correction});});
for(const value of ['latest','fixed','adaptive'])$('remote-'+value).addEventListener('click',()=>{remote=value;for(const id of ['latest','fixed','adaptive'])$('remote-'+id).setAttribute('aria-pressed',String(id===value));send({remote});});$('profile').addEventListener('change',()=>send({profile:$('profile').value}));$('disconnect').addEventListener('click',()=>{clearInput();send({action:'disconnect'});});$('reconnect').addEventListener('click',connect);
const movement=new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);canvas.addEventListener('pointerdown',()=>canvas.focus());canvas.addEventListener('keydown',e=>{const key=e.key.toLowerCase();if(movement.has(key)){e.preventDefault();keys.add(key);axes();}});canvas.addEventListener('keyup',e=>{const key=e.key.toLowerCase();if(movement.has(key)){e.preventDefault();keys.delete(key);axes();}});canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
function draw(){const w=canvas.width,h=canvas.height,unit=Math.min(w/20,h/20),cx=w/2,cy=h/2;ctx.clearRect(0,0,w,h);ctx.strokeStyle='#24364a';ctx.lineWidth=1;for(let n=-8;n<=8;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-8*unit);ctx.lineTo(cx+n*unit,cy+8*unit);ctx.moveTo(cx-8*unit,cy+n*unit);ctx.lineTo(cx+8*unit,cy+n*unit);ctx.stroke();}
  const info=frame?.interest;if(info?.mode==='aoi'){ctx.save();ctx.strokeStyle='#74ce9b';ctx.lineWidth=2;ctx.beginPath();ctx.arc(cx+info.centerX*unit,cy-info.centerZ*unit,info.radius*unit,0,Math.PI*2);ctx.stroke();if($('margin').checked){ctx.setLineDash([4,5]);ctx.beginPath();ctx.arc(cx+info.centerX*unit,cy-info.centerZ*unit,(info.radius+info.margin)*unit,0,Math.PI*2);ctx.stroke();}ctx.restore();}
  ctx.font='12px sans-serif';ctx.textAlign='center';for(const o of frame?.objects??[]){const x=cx+o.x*unit,y=cy-o.z*unit,r=8;ctx.fillStyle=o.kind==='global'?'#efc85e':o.kind==='landmark'?'#77d5a4':'#c5b0fb';ctx.beginPath();ctx.moveTo(x,y-r);ctx.lineTo(x+r,y);ctx.lineTo(x,y+r);ctx.lineTo(x-r,y);ctx.closePath();ctx.fill();ctx.fillText(o.kind==='global'?'常駐地標':o.kind==='landmark'?o.id.replace('landmark-','地標 '):`${o.id} · life ${o.life}`,x,y-16);}
  for(const p of frame?.players??[]){const own=p.id===frame.id,size=unit*.7,color=own?'#66b6ff':'#ffb56a';ctx.fillStyle=color;ctx.strokeStyle=color;if($('ghost').checked){ctx.lineWidth=2;ctx.strokeRect(cx+p.latestX*unit-size*.65,cy-p.latestZ*unit-size*.65,size*1.3,size*1.3);}ctx.fillRect(cx+p.x*unit-size/2,cy-p.z*unit-size/2,size,size);ctx.fillText((own?'你':'玩家')+' #'+p.id,cx+p.x*unit,cy-p.z*unit-size);}requestAnimationFrame(draw);
}
const heartbeat=setInterval(()=>{if(document.activeElement===canvas&&!document.hidden)axes();},200);window.addEventListener('pagehide',()=>{clearInterval(heartbeat);++generation;socket?.close();});connect();requestAnimationFrame(draw);
