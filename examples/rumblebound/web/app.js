const $=id=>document.getElementById(id),canvas=$('world'),ctx=canvas.getContext('2d');
const names={ranger:'弓箭手',tank:'騎士',mage:'魔法師'},teamColors=['#70d0de','#ffb068','#bf9bfa','#8cd799'];
const errors={server_unavailable:'伺服器暫時無法連線。',room_started:'對戰進行中，席位會保留；可暫時斷線，結束後再離開房間。',room_not_ready:'所有玩家都準備完成後才能開始。',takeover_required:'舊連線仍在線。請等候片刻，或勾選接管自己的舊連線。',session_expired:'身份已過期，請重新進入。',cooldown:'冷卻中。',attack_locked:'攻擊動作尚未結束。',reconnect_expired:'自動重連已逾時，可手動重新連線。',room_full:'房間已滿。',team_full:'隊伍已滿。',version_mismatch:'遊戲版本不同，請重新載入。',map_mismatch:'地圖版本不同，請重新載入。',guest_rate_limited:'訪客建立過於頻繁，請稍後再試。'};
let socket,frame,config,roomKey='',loadedGeneration='',closing=false,drawQueued=false,aim={x:0,z:0};const keys=new Set();
let googleActive=false;
let aimDirty=false;
let catalogKey='',botKey='',settingsKey='';
const networkInputs=[['send-delay','SendDelaySeconds',.3,1],['send-jitter','SendJitterSeconds',.3,1],['send-loss','SendLossProbability',1,100],['delay','DelaySeconds',.3,1],['receive-jitter','ReceiveJitterSeconds',.3,1],['receive-loss','ReceiveLossProbability',1,100]];
function loadSettings(f){
  const s=f.net.settings,key=JSON.stringify(s);if(!s||key===settingsKey)return;settingsKey=key;
  for(const [id,field]of [['transport','Transport'],['prediction','Prediction'],['correction','Correction'],['timeline','Timeline'],['clock','Clock']])$(id).value=s[field];
  for(const [id,field,,scale]of networkInputs)$(id).value=String((s[field]??0)*scale);
  $('speculative').checked=s.SpeculativeActions;
  for(const id of ['codec','interest','radius'])$(id).value=String(s.Server[id]);
  $('quantize').checked=s.Server.quantize;$('history').checked=s.Server.history;$('lag').checked=s.Server.lag==='melee';
}
function readNetworkSettings(){
  const result={};for(const [id,field,max,scale]of networkInputs){const raw=$(id).value,value=Number(raw)/scale;if(raw.trim()===''||!Number.isFinite(value)||value<0||value>max)return null;result[field]=value;}return result;
}
function installedChoices(f){
  const lists={prediction:f.net.predictions,correction:f.net.corrections,timeline:f.net.timelines,clock:f.net.clocks,codec:f.net.codecs},key=JSON.stringify(lists);if(key===catalogKey)return;catalogKey=key;
  const labels={frame:'本機預測','fixed-step':'固定步進預測',authority:'等待伺服器',exponential:'平滑校正',snap:'直接校正',adaptive:'自適應',fixed:'固定',latest:'關閉',disciplined:'往返校時',snapshot:'快照時鐘',json:'完整 JSON',delta:'差分'};
  for(const [id,values]of Object.entries(lists)){const select=$(id),previous=select.value;select.replaceChildren(...values.map(value=>option(value,labels[value]||value)));select.value=values.includes(previous)?previous:values[0];}
  $('speculative').disabled=!f.net.speculative;if(!f.net.speculative)$('speculative').checked=false;
}
function send(op,data={}){if(socket?.readyState===1)socket.send(JSON.stringify({op,...data}));}
function saved(){try{return JSON.parse(sessionStorage.getItem('rf.remake.identity')||'null');}catch{return null;}}
function connect(){
  const url=new URL('/play',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';socket=new WebSocket(url);
  socket.onopen=()=>{const identity=saved();if(identity?.value)send('restore',{credential:identity.value,userID:identity.userID});};
  socket.onmessage=event=>{frame=JSON.parse(event.data);if(frame.config)config=frame.config;if(frame.credential){try{if(frame.credential.value)sessionStorage.setItem('rf.remake.identity',JSON.stringify(frame.credential));else sessionStorage.removeItem('rf.remake.identity');}catch{}}update();scheduleDraw();};
  socket.onclose=()=>{clearInput();$('status').textContent='介面連線中斷，正在重新連接…';if(!closing)setTimeout(connect,1000);};
}
const text=(id,value)=>{$(id).textContent=value??'';};
function button(label,fn){const b=document.createElement('button');b.textContent=label;b.addEventListener('click',fn);return b;}
function option(value,label){const o=document.createElement('option');o.value=value;o.textContent=label;return o;}
function updateRoom(){
  const r=frame.room,key=JSON.stringify(r);if(key===roomKey)return;roomKey=key;if(!r)return;
  text('code',r.code);$('members').replaceChildren();$('team').replaceChildren();$('capacities').replaceChildren();$('assignments').replaceChildren();
  for(const t of r.teams)$('team').append(option(t.teamID,t.teamID+' · '+r.members.filter(m=>m.teamID===t.teamID).length+'/'+t.capacity));
  for(const m of r.members){const row=document.createElement('div');row.className='member';const name=document.createElement('strong');name.textContent=(m.userID===r.youID?'你':m.bot?'練習對手':m.userID.slice(0,9))+' · '+(names[m.heroID]||'尚未選角');const ready=document.createElement('small');ready.textContent=(m.teamID||'未選隊')+' / '+(m.ready?'已準備':'準備中');row.append(name,ready);$('members').append(row);}
  const own=r.members.find(m=>m.userID===r.youID);if(own){$('team').value=own.teamID;$('hero').value=own.heroID;text('ready',own.ready?'取消準備':'準備');}
  $('room-options').hidden=r.hostID!==r.youID;
  if(r.hostID===r.youID){
    for(const t of r.teams){const row=document.createElement('div');row.className='capacity';const label=document.createElement('label');label.textContent=t.teamID+' 容量';const n=document.createElement('select');for(let i=1;i<=4;i++)n.append(option(String(i),String(i)));n.value=String(t.capacity);label.append(n);row.append(label,button('設定',()=>send('capacity',{team:t.teamID,capacity:Number(n.value)})));$('capacities').append(row);}
    for(const m of r.members.filter(m=>!m.bot)){const label=document.createElement('label');label.textContent=(m.userID===r.youID?'你':m.userID.slice(0,9))+' 的隊伍';const select=document.createElement('select');for(const t of r.teams)select.append(option(t.teamID,t.teamID));select.value=m.teamID;select.addEventListener('change',()=>send('assign',{member:m.userID,team:select.value}));label.append(select);$('assignments').append(label);}
  }
}
function update(){
  const f=frame,r=f.room,w=f.world,s=w.snapshot,own=w.players.find(p=>p.own),stage=f.stage;
  installedChoices(f);loadSettings(f);
  for(const id of ['loading','login','lobby','room','battle','result','replay'])$(id).hidden=id!==stage;
  $('practice-setup').hidden=stage!=='lobby';$('logout').hidden=!f.userID;
  text('status',stage==='replay'?(f.replay.playing?'離線播放中':'離線播放已暫停'):stage==='result'?'對戰已結束':f.recovery.pending?'重新連線 · '+f.recovery.wait.toFixed(1)+' 秒後重試':f.connected?(f.canControl?'對戰連線正常':'等待載入完成'):stage==='room'?'房間準備中':stage==='lobby'?'已登入':stage==='login'?'請選擇進入方式':'正在連線');
  text('identity',f.userID?'玩家 '+f.userID.slice(0,10):'');text('network',f.connected?Math.round(w.rtt*1000)+' ms · tick '+w.tick:'');text('notice',errors[f.notice]||f.notice||'');
  text('phase',stage==='replay'?'離線播放':stage==='battle'?(f.canControl?'對戰中':f.recovery.pending?'重新連線中':'等待載入'):stage==='result'?'對戰結束':'尚未進入');
  updateRoom();
  const locked=f.busy;
  $('room-management').disabled=locked;
  for(const id of ['guest','create','join','practice','ready','leave-room','leave-practice','return','logout','enter','apply'])$(id).disabled=locked;
  $('team').disabled=locked;$('hero').disabled=locked;$('start').disabled=locked||!r?.canStart||r.hostID!==r.youID;
  $('fire').disabled=$('dash').disabled=!f.canControl;
  $('practice-controls').hidden=!r?.practice;
  if(r?.practice){
    const mode=r.botMode||'idle',parts=mode.split('+'),key=r.matchID+':'+mode,select=$('bot-mode');
    if(!Array.from(select.children).some(item=>item.value===mode)){const words={idle:'待命',move:'巡邏',capture:'搶佔據點',attack:'攻擊',dodge:'閃避',kite:'保持距離'};select.append(option(mode,parts.map(part=>words[part]).join('＋')));}
    if(document.activeElement!==select)select.value=mode;
    if(key!==botKey){botKey=key;$('bot-movement').value=parts.includes('capture')?'capture':parts.includes('move')?'move':'idle';for(const part of ['attack','dodge','kite'])$('bot-'+part).checked=parts.includes(part);}
    $('bot-options').disabled=select.disabled=locked;
  }
  text('battle-title',r?.practice?'練習場':f.canControl?'佔領據點，取得勝利':'等待玩家載入');
  text('self',own?(names[own.state.heroID]+' · HP '+own.state.hp+' / '+heroHP(own.state.heroID)+'\n攻擊 '+remaining(own.state.fireReadyTick,w.tick)+' 秒 · 衝刺 '+remaining(own.state.dashReadyTick,w.tick)+' 秒'):'正在取得玩家狀態…');
  $('scores').replaceChildren();for(const score of s?.match?.scores||[]){const row=document.createElement('div');row.className='score';row.textContent=score.teamID+'　'+score.score+' 分'+(score.forfeited?' · 已退出':'');$('scores').append(row);}
  const match=s?.match;text('point',match?.phase==='playing'?'據點：'+(match.scoringTeamID||'無隊伍得分')+' · 剩餘 '+Math.max(0,Math.ceil((match.endsAtTick-w.tick)/30))+' 秒':'等待所有玩家載入');
  const result=match?.result;if(result){text('outcome',result.outcome==='draw'?'平手':result.winnerTeamID===own?.state.teamID?'你的隊伍獲勝':'對戰結束');text('result-detail',(result.winnerTeamID?'勝利隊伍 '+result.winnerTeamID+' · ':'')+result.reason);$('return').hidden=!!r?.practice;}
  $('reconnect').checked=f.recovery.enabled;
  $('google').hidden=!config?.googleClientID;$('google').disabled=f.busy||googleActive;
  if(stage!=='login'&&googleActive){googleActive=false;globalThis.RumbleboundGoogle?.cancel();}
  $('record-start').disabled=!f.connected||f.recording.active;$('record-stop').disabled=!f.recording.active;
  text('record-state',(f.recording.active?'錄製中 · ':'已保存 · ')+f.recording.frames+' 張快照'+(f.recording.notice?' · '+f.recording.notice:''));
  $('record-download').hidden=f.recording.frames===0;$('record-download').href='/viewer/'+f.viewerID+'/replay';$('replay-file').disabled=!!r||f.busy||f.replay.active;
  if(f.replay.active){const seek=$('replay-seek');seek.min=f.replay.first;seek.max=f.replay.last;if(document.activeElement!==seek)seek.value=f.replay.tick;$('replay-speed').value=String(f.replay.speed);}
  text('metrics','校正距離 '+w.correction.toFixed(3)+' · 顯示 tick '+w.renderTick.toFixed(1)+'\n解碼 ACK '+f.net.frameAck+' · 缺基準 '+f.net.missing+' · 動作確認 '+f.net.confirmed+' / 拒絕 '+f.net.rejected+(f.net.action?' · '+(errors[f.net.action]||f.net.action):''));
  if(!f.connected)loadedGeneration='';
}
function heroHP(id){return config?.rules.heroes.find(h=>h.heroID===id)?.maxHP||100;}
function remaining(ready,tick){return Math.max(0,(ready-tick)/30).toFixed(1);}
function scheduleDraw(){if(drawQueued)return;drawQueued=true;requestAnimationFrame(()=>{drawQueued=false;draw();if(aimDirty&&frame?.canControl){aimDirty=false;send('aim',aim);}const key=frame?.world?.snapshot?.matchID+':'+frame?.world?.generation;if(frame?.connected&&!frame.loaded&&loadedGeneration!==key&&config){loadedGeneration=key;send('scene-ready');}});}
function geometry(){const half=config?.map.arenaHalfSize||10,unit=Math.min(canvas.width,canvas.height)/(half*2+3);return{half,unit,cx:canvas.width/2,cy:canvas.height/2};}
function draw(){
  const {half,unit,cx,cy}=geometry(),w=frame?.world;ctx.clearRect(0,0,canvas.width,canvas.height);ctx.lineWidth=1;ctx.strokeStyle='#213047';
  for(let n=-half;n<=half;n++){ctx.beginPath();ctx.moveTo(cx+n*unit,cy-half*unit);ctx.lineTo(cx+n*unit,cy+half*unit);ctx.moveTo(cx-half*unit,cy+n*unit);ctx.lineTo(cx+half*unit,cy+n*unit);ctx.stroke();}
  ctx.strokeStyle='#4a637e';ctx.strokeRect(cx-half*unit,cy-half*unit,half*unit*2,half*unit*2);
  for(const o of config?.map.obstacles||[]){ctx.fillStyle='#2d3d51';ctx.fillRect(cx+(o.x-o.halfX)*unit,cy-(o.z+o.halfZ)*unit,o.halfX*2*unit,o.halfZ*2*unit);ctx.strokeStyle='#647b95';ctx.strokeRect(cx+(o.x-o.halfX)*unit,cy-(o.z+o.halfZ)*unit,o.halfX*2*unit,o.halfZ*2*unit);}
  function circle(x,z,r,color,fill=false){ctx.beginPath();ctx.arc(cx+x*unit,cy-z*unit,r*unit,0,Math.PI*2);if(fill){ctx.fillStyle=color;ctx.fill();}else{ctx.strokeStyle=color;ctx.lineWidth=2;ctx.stroke();}}
  const match=w?.snapshot?.match;if(match?.point)circle(match.point.x,match.point.z,match.point.radius,match.pointStatus==='contested'?'#d3a2f8':'#448c8c44',true);
  for(const cast of w?.snapshot?.groundCasts||[])circle(cast.x,cast.z,cast.radius,'#cba0ff');
  for(const shot of w?.projectiles||[])circle(shot.x,shot.z,.14,'#ffdc8a',true);
  for(const cue of w?.actions||[]){const color=cue.status==='rejected'?'#ff8585':cue.status==='confirmed'?'#9ce9c3':'#f4d7a0';circle(cue.x,cue.z,cue.kind==='ground'?cue.radius:.62,color);if(cue.kind!=='ground'){ctx.beginPath();ctx.moveTo(cx+cue.x*unit,cy-cue.z*unit);ctx.lineTo(cx+(cue.x+cue.dx*cue.radius)*unit,cy-(cue.z+cue.dz*cue.radius)*unit);ctx.strokeStyle=color;ctx.stroke();}}
  const teams=match?.scores.map(s=>s.teamID)||[];
  for(const p of w?.players||[]){const s=p.state;if(s.left)continue;const color=p.own?'#73b7ff':teamColors[Math.max(0,teams.indexOf(s.teamID))%teamColors.length];
    if($('outlines').checked)circle(s.x,s.z,config.rules.playerRadius+.12,p.own?'#4b7092':'#536b6b');
    if(s.hp<=0){ctx.fillStyle='#7e90a5';ctx.font='13px Segoe UI';ctx.textAlign='center';ctx.fillText('復活中 '+remaining(s.respawnTick,w.tick)+'s',cx+p.x*unit,cy-p.z*unit);continue;}
    circle(p.x,p.z,config.rules.playerRadius,color,true);circle(p.x,p.z,config.rules.playerRadius+.1,teamColors[Math.max(0,teams.indexOf(s.teamID))%teamColors.length]);
    if(s.protectedUntilTick>w.tick)circle(p.x,p.z,config.rules.playerRadius+.22,'#dceaff');
    ctx.fillStyle='#e6effc';ctx.font='12px Microsoft JhengHei';ctx.textAlign='center';ctx.fillText((p.own?'你 · ':'')+names[s.heroID],cx+p.x*unit,cy-p.z*unit-25);
    const width=unit*1.2;ctx.fillStyle='#24374a';ctx.fillRect(cx+p.x*unit-width/2,cy-p.z*unit-19,width,4);ctx.fillStyle=color;ctx.fillRect(cx+p.x*unit-width/2,cy-p.z*unit-19,width*s.hp/heroHP(s.heroID),4);
  }
  if(w?.frozen){ctx.fillStyle='#0c152299';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.fillStyle='#c6d7e9';ctx.font='20px Microsoft JhengHei';ctx.textAlign='center';ctx.fillText('連線暫停，保留最後畫面',cx,cy);}
}
function bind(id,fn,event='click'){$(id).addEventListener(event,fn);}
bind('guest',()=>{if(googleActive){googleActive=false;globalThis.RumbleboundGoogle?.cancel();}send('guest');});bind('retry-config',()=>send('config'));bind('create',()=>send('create',{teams:Number($('team-count').value)}));bind('join',()=>send('join',{code:$('room-code').value}));
bind('google',()=>{if(!globalThis.RumbleboundGoogle){text('notice','登入介面尚未載入，請重新整理。');return;}googleActive=true;globalThis.RumbleboundGoogle.mount($('google-panel'),identity=>{googleActive=false;if(frame.stage==='login')send('restore',{credential:identity.session,userID:identity.userID});},error=>{googleActive=false;text('notice',errors[error]||error);});});
bind('practice',()=>send('practice',{hero:$('practice-hero').value,botHero:$('bot-hero').value,mode:'idle'}));bind('team',()=>send('team',{team:$('team').value}),'change');bind('hero',()=>send('hero',{hero:$('hero').value}),'change');
bind('ready',()=>send('ready',{value:!frame.room.members.find(m=>m.userID===frame.room.youID)?.ready}));bind('start',()=>send('start'));bind('leave-room',()=>send('leave'));bind('leave-practice',()=>send('leave'));bind('return',()=>send('return'));bind('logout',()=>{clearInput();send('logout');});
bind('bot-mode',()=>send('practice-mode',{mode:$('bot-mode').value}),'change');bind('enter',()=>send('enter',{takeover:$('takeover').checked}));bind('disconnect',()=>{clearInput();send('disconnect');});bind('cut',()=>send('cut'));bind('forget',()=>send('forget'));
bind('bot-apply',()=>{const parts=[],movement=$('bot-movement').value;if(movement!=='idle')parts.push(movement);for(const part of ['attack','dodge','kite'])if($('bot-'+part).checked)parts.push(part);send('practice-mode',{mode:parts.join('+')||'idle'});});
bind('reconnect',()=>send('reconnect',{value:$('reconnect').checked}),'change');bind('auto',()=>send('auto',{value:$('auto').checked}),'change');bind('outlines',scheduleDraw,'change');
bind('record-start',()=>send('record-start'));bind('record-stop',()=>send('record-stop'));bind('replay-play',()=>send('replay-play'));bind('replay-pause',()=>send('replay-pause'));bind('replay-end',()=>send('replay-end'));
bind('replay-seek',()=>send('replay-seek',{tick:Number($('replay-seek').value)}),'change');bind('replay-speed',()=>send('replay-speed',{value:Number($('replay-speed').value)}),'change');
bind('replay-file',async()=>{const file=$('replay-file').files?.[0];if(!file)return;if(file.size>32*1024*1024){text('replay-notice','檔案超過 32 MiB。');return;}try{const reply=await fetch('/viewer/'+frame.viewerID+'/replay',{method:'POST',headers:{'Content-Type':'application/json'},body:file});text('replay-notice',reply.ok?'重播已載入。':await reply.text());}catch{text('replay-notice','無法載入重播。');}},'change');
bind('apply',()=>{if($('transport').value==='webtransport'&&!config?.webTransport){text('notice','請先用 -WebTransport 啟動完整範例。');return;}const network=readNetworkSettings();if(!network){text('notice','延遲與抖動須為 0～0.3 秒，丟包須為 0～100%。');return;}const value={Transport:$('transport').value,Prediction:$('prediction').value,Correction:$('prediction').value==='authority'?'snap':$('correction').value,Timeline:$('timeline').value,Clock:$('clock').value,...network,SpeculativeActions:$('speculative').checked,Server:{codec:$('codec').value,interest:$('interest').value,radius:Number($('radius').value),quantize:$('quantize').checked,history:$('history').checked||$('lag').checked,lag:$('lag').checked?'melee':'off'}};send('settings',{value});if(frame.room?.started)send('enter',{takeover:$('takeover').checked});});
function action(kind){if(frame?.canControl)send('action',{kind,x:aim.x,z:aim.z});}bind('fire',()=>action('fire'));bind('dash',()=>action('dash'));
const movement=new Set(['w','a','s','d','arrowup','arrowleft','arrowdown','arrowright']);
function drive(){const down=(...list)=>list.some(k=>keys.has(k))?1:0;send('drive',{x:down('d','arrowright')-down('a','arrowleft'),z:down('w','arrowup')-down('s','arrowdown')});}
function clearInput(){keys.clear();send('stop');}
canvas.addEventListener('keydown',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.add(key);drive();}if(event.code==='Space'&&!event.repeat){event.preventDefault();action('dash');}});
canvas.addEventListener('keyup',event=>{const key=event.key.toLowerCase();if(movement.has(key)){event.preventDefault();keys.delete(key);drive();}});
canvas.addEventListener('pointermove',event=>{const rect=canvas.getBoundingClientRect(),g=geometry();aim={x:((event.clientX-rect.left)*canvas.width/rect.width-g.cx)/g.unit,z:-((event.clientY-rect.top)*canvas.height/rect.height-g.cy)/g.unit};aimDirty=true;scheduleDraw();});
canvas.addEventListener('pointerdown',event=>{canvas.focus();if(event.button===0)action('fire');});canvas.addEventListener('blur',clearInput);window.addEventListener('blur',clearInput);document.addEventListener('visibilitychange',()=>{if(document.hidden)clearInput();});
window.addEventListener('pagehide',()=>{closing=true;clearInput();socket?.close();});connect();
