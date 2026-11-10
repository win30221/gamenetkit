import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';
const origin=process.argv[process.argv.indexOf('--server')+1];
const html=await(await fetch(origin+'/')).text();assert.match(html,/<details id="advanced">/);assert.doesNotMatch(html,/<details id="advanced"[^>]*open/);
const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]));
for(const [path,type] of [['/','text/html'],['/app.js','text/javascript'],['/style.css','text/css']]){const r=await fetch(origin+path);assert.equal(r.status,200);assert.ok(r.headers.get('content-type').startsWith(type));}
const source=await(await fetch(origin+'/app.js')).text(),elements=new Map(),windows={},documents={},sockets=[],timers=[],animation=[];let fills=0;
function element(id){assert.ok(ids.has(id),'missing actual HTML element '+id);if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:false,value:id==='profile'?'delay':id==='speed'?'1':'0',width:1000,height:440,textContent:'',hidden:id==='playback-controls',
  addEventListener(name,handler){this.handlers[name]=handler;},setAttribute(name,value){this.attributes[name]=value;},focus(){document.activeElement=this;},
  getContext(){return new Proxy({},{get(_t,key){return key==='fillRect'?()=>fills++:()=>{};},set(){return true;}});}});return elements.get(id);}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(name,handler){documents[name]=handler;}};
class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(name,handler){windows[name]=handler;}},location:{href:origin+'/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Number,String,Set,console,
  requestAnimationFrame(fn){animation.push(fn);},setInterval(fn,ms){const t=setInterval(fn,ms);timers.push(t);return t;},clearInterval});
async function until(predicate){const end=Date.now()+7500;while(!predicate()){assert.ok(Date.now()<end,'replay page timeout');animation.shift()?.();await delay(10);}}
const state=()=>vm.runInContext('frame',context),click=id=>element(id).handlers.click();let peer,peerFrame;
try{
  vm.runInContext(source,context);await until(()=>state()?.connected&&fills>0);assert.equal(state().view,'live');assert.equal(element('play').disabled,true);assert.equal(element('record-stop').disabled,true);
  peer=new WebSocket(origin.replace('http:','ws:')+'/lesson');peer.onopen=()=>peer.send(JSON.stringify({auto:true,profile:'clean'}));peer.onmessage=e=>{peerFrame=JSON.parse(e.data);};
  await until(()=>peerFrame?.connected&&state().players.length===2);const identity=state().liveID;
  click('record-start');assert.equal(element('record-start').disabled,true);await until(()=>state().record.status==='recording');const recordingID=state().record.id;
  assert.equal(peerFrame.record.status,'idle');assert.equal((await fetch(origin+'/recordings/'+recordingID)).status,404);
  element('world').focus();element('world').handlers.keydown({key:'d',preventDefault(){}});
  click('spawn');await until(()=>state().objects.length===1&&peerFrame.objects.length===1);const objectID=state().objects[0].id;
  windows.blur();document.activeElement=null;
  click('rebirth');await until(()=>state().objects[0].life===2&&state().pending===0);
  click('remove');await until(()=>state().objects.length===0&&state().pending===0);
  click('record-stop');await until(()=>state().record.status==='ready');assert.equal(element('play').disabled,false);assert.match(element('record-status').textContent,/已完成/);
  const response=await fetch(origin+element('download').href);assert.equal(response.status,200);assert.match(response.headers.get('content-disposition'),/attachment/);const file=await response.json();
  assert.ok(file.frames.length>20);assert.ok(file.frames.some(f=>f.objects.some(o=>o.life===1)));assert.ok(file.frames.some(f=>f.objects.some(o=>o.life===2)));assert.equal(file.frames.at(-1).objects.length,0);
  click('play');await until(()=>state().view==='playback'&&state().playback.playing);assert.equal(element('playback-controls').hidden,false);assert.equal(element('spawn').disabled,true);assert.equal(element('record-start').disabled,true);
  click('play');await until(()=>!state().playback.playing);const paused=state().playback.position,tick=state().liveTick;
  await until(()=>state().liveTick>tick+8);assert.equal(state().playback.position,paused,'paused playback advanced with live updates');
  const ownLive=peerFrame.players.find(p=>p.id===identity),liveX=ownLive.latestX,liveZ=ownLive.latestZ;
  sockets[0].send(JSON.stringify({x:1,z:1,perform:'spawn',auto:true,record:'start'}));let wait=state().liveTick;await until(()=>state().liveTick>wait+8);
  const after=peerFrame.players.find(p=>p.id===identity);assert.equal(after.latestX,liveX);assert.equal(after.latestZ,liveZ);assert.equal(peerFrame.objects.filter(o=>o.owner===identity).length,0);assert.equal(state().record.id,recordingID);
  for(const life of [1,2]){
    const frame=file.frames.find(f=>f.objects.some(o=>o.id===objectID&&o.life===life));const position=(frame.tick-file.frames[0].tick)/30;
    element('progress').value=String(position);element('progress').handlers.input();await until(()=>state().tick===frame.tick);assert.equal(state().objects.find(o=>o.id===objectID).life,life);assert.equal(state().playback.playing,false);
  }
  element('progress').value='0';element('progress').handlers.input();await until(()=>state().playback.position===0);assert.equal(state().objects.length,0);
  element('speed').value='2';element('speed').handlers.change();await until(()=>state().playback.speed===2);click('play');await until(()=>state().playback.position>.2);click('play');await until(()=>!state().playback.playing);
  element('progress').value=String(state().playback.duration);element('progress').handlers.input();await until(()=>state().playback.ended);assert.equal(state().objects.length,0);assert.equal(state().playback.playing,false);assert.equal(element('play').textContent,'重新播放');
  click('live');await until(()=>state().view==='live');assert.equal(element('playback-controls').hidden,true);assert.equal(state().liveID,identity);assert.equal(state().objects.length,0);assert.equal(element('record-start').disabled,false);
  for(const remote of ['latest','fixed','adaptive']){click('remote-'+remote);await until(()=>state().remote===remote);}
  element('world').focus();const start=state().players.find(p=>p.id===identity).z;element('world').handlers.keydown({key:'w',preventDefault(){}});await until(()=>state().players.find(p=>p.id===identity).z>start+.1);windows.blur();document.activeElement=null;
  click('record-start');await until(()=>state().record.status==='recording'&&state().record.id!==recordingID);wait=state().liveTick;await until(()=>state().liveTick>wait+8);click('record-stop');await until(()=>state().record.status==='ready');
  const newerID=state().record.id;click('play');await until(()=>state().view==='playback'&&state().playback.frames<file.frames.length);assert.equal(state().record.id,newerID);
  click('disconnect');await until(()=>!state().connected);assert.equal(state().view,'playback');assert.equal(state().playback.loaded,true);
  element('progress').value='0';element('progress').handlers.input();await until(()=>state().playback.position===0);
  click('live');await until(()=>state().view==='live');assert.equal(element('record-start').disabled,true);
  click('reconnect');await until(()=>state()?.connected&&state().players.length===2);assert.ok(state().liveID>identity);assert.equal(state().record.status,'idle');assert.equal(state().playback.loaded,false);
  console.log('RF_REPLAY_LESSON_BROWSER_SOURCE PASS: actual page/C# host/Go SDK journal, record-stop-verified download-play, object spawn/respawn/removal, pause/seek/speed/end, playback/live input isolation, refreshed clip, retained smoothing controls and reconnect; DOM/Canvas doubles');
}finally{windows.pagehide?.();peer?.close();for(const s of sockets)s.close();for(const t of timers)clearInterval(t);}
