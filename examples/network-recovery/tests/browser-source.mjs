import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';

const origin=process.argv[process.argv.indexOf('--server')+1];
for(const [path,type] of [['/','text/html'],['/app.js','text/javascript'],['/style.css','text/css']]) {
  const response=await fetch(origin+path);assert.equal(response.status,200);assert.ok(response.headers.get('content-type').startsWith(type));
}
const source=await (await fetch(origin+'/app.js')).text();
const elements=new Map(),windows={},documents={},sockets=[],timers=[],animation=[];
let fills=0;
function element(id){
  if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:['auto','ghost','target','auto-retry','diagnostics'].includes(id),value:id==='profile'?'delay':id==='up'||id==='down'?'75':'0',width:1100,height:560,textContent:'',
    addEventListener(name,handler){this.handlers[name]=handler;},setAttribute(name,value){this.attributes[name]=value;},focus(){document.activeElement=this;},
    getContext(){return new Proxy({}, {get(_target,key){return key==='fillRect'?()=>fills++:()=>{};},set(){return true;}});}});
  return elements.get(id);
}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(name,handler){documents[name]=handler;}};
class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(name,handler){windows[name]=handler;}},location:{href:origin+'/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Set,console,
  requestAnimationFrame(fn){animation.push(fn);},setInterval(fn,ms){const timer=setInterval(fn,ms);timers.push(timer);return timer;},clearInterval});
async function until(predicate){const end=Date.now()+12000;while(!predicate()){assert.ok(Date.now()<end,'network recovery page timeout');animation.shift()?.();await delay(10);}}
const state=()=>vm.runInContext('frame',context);
const click=id=>element(id).handlers.click();
let peer,peerFrame;
try {
  vm.runInContext(source,context);await until(()=>state()?.connected&&fills>0);
  const first=state().id,start=state().players.find(p=>p.id===first).x;
  await until(()=>state().players.find(p=>p.id===first).x>start+.4);
  for(const mode of ['authority','prediction']){click('mode-'+mode);await until(()=>state().mode===mode);assert.equal(element('mode-'+mode).attributes['aria-pressed'],'true');}
  for(const correction of ['snap','smooth']){click('correction-'+correction);await until(()=>state().correction===correction);}
  element('auto').checked=false;element('auto').handlers.change();
  element('profile').value='high';element('profile').handlers.change();await until(()=>state().profile==='high');
  const wait=state().tick;await until(()=>state().tick>wait+18);
  element('world').focus();const key={key:'w',preventDefault(){}};
  const own=state().players.find(p=>p.id===first),priorZ=own.z,priorAuthority=own.latestZ;
  element('world').handlers.keydown(key);await until(()=>state().players.find(p=>p.id===first).z>priorZ+.1);
  assert.equal(state().players.find(p=>p.id===first).latestZ,priorAuthority,'movement waited for delayed authority');
  windows.blur();document.activeElement=null;
  let tick=state().tick;await until(()=>state().tick>tick+18);
  const stopped=state().players.find(p=>p.id===first).latestZ;tick=state().tick;await until(()=>state().tick>tick+6);
  assert.equal(state().players.find(p=>p.id===first).latestZ,stopped,'blur did not stop input');
  const before=state().corrections;click('bump');await until(()=>state().corrections>before&&state().error>.8&&state().offset>.6);
  const peak=state().offset;await until(()=>state().offset<peak/2);
  click('correction-snap');await until(()=>state().correction==='snap');const snapCount=state().corrections;
  click('bump');await until(()=>state().corrections>snapCount&&state().error>.8);assert.equal(state().offset,0);
  peer=new WebSocket(origin.replace('http:','ws:')+'/lesson');peer.onopen=()=>peer.send(JSON.stringify({auto:false,mode:'authority',profile:'clean'}));peer.onmessage=event=>{peerFrame=JSON.parse(event.data);};
  await until(()=>peerFrame?.connected&&peerFrame.players.length===2&&state().players.length===2);assert.notEqual(peerFrame.id,first);
  peer.send(JSON.stringify({auto:true}));
  const remotePlayer=()=>state().players.find(p=>p.id===peerFrame.id);
  const remoteStart=remotePlayer().x;
  await until(()=>remotePlayer().latestX>remoteStart+.3);
  for(const remote of ['latest','fixed','adaptive']) {
    click('remote-'+remote);await until(()=>state().remote===remote);
    assert.equal(element('remote-'+remote).attributes['aria-pressed'],'true');
    if(remote==='latest')assert.ok(Math.abs(remotePlayer().x-remotePlayer().latestX)<1e-5,'disabled interpolation must show latest position');
    else await until(()=>state().remoteBuffer>0&&Math.abs(remotePlayer().x-remotePlayer().latestX)>.1);
    assert.equal(state().players.find(p=>p.id===first).x,state().players.find(p=>p.id===first).predictedX,'remote interpolation affected own snap-corrected position');
  }
  click('disconnect');await until(()=>state()&&!state().connected&&peerFrame.players.length===1);assert.equal(state().offset,0);
  click('reconnect');await until(()=>state()?.connected&&state().players.length===2&&peerFrame.players.length===2);assert.ok(state().id>first);
  assert.equal(state().mode,'prediction');assert.equal(state().correction,'snap');
  assert.equal(state().remote,'adaptive');
  element('up').value='80';element('down').value='120';element('jitter-setting').value='30';element('loss').value='10';element('up').handlers.change();
  await until(()=>state().profile==='custom'&&state().network.up===80&&state().network.down===120);
  await until(()=>state().diagnostics.rttSamples>0&&state().diagnostics.inputReply>0&&state().network.droppedSend+state().network.droppedReceive>0);
  assert.ok(state().diagnostics.sent>0&&state().diagnostics.received>0);
  for(const clock of ['snapshot','disciplined']){click('clock-'+clock);await until(()=>state().timing.mode===clock);assert.equal(element('clock-'+clock).attributes['aria-pressed'],'true');}
  element('diagnostics').checked=false;element('diagnostics').handlers.change();await until(()=>!state().diagnostics.enabled);assert.equal(element('rtt').textContent,'已停用');
  element('diagnostics').checked=true;element('diagnostics').handlers.change();await until(()=>state().diagnostics.enabled&&state().diagnostics.rttSamples>0);
  click('pause');await until(()=>state().network.pause>2);const pausedTick=state().tick;
  await until(()=>state().timing.snapshotAge>1);assert.equal(state().tick,pausedTick);assert.ok(state().connected&&!state().recovery.pending);
  await until(()=>state().network.pause===0&&state().tick>pausedTick+60&&state().timing.snapshotAge<.5);
  click('guide-outage');await until(()=>state().recovery.pending&&!state().connected);assert.ok(element('guide-message').textContent.includes('自己重試'));await until(()=>state().recovery.attempts>0);
  click('stop-retry');await until(()=>!state().recovery.pending&&!state().connected);await delay(700);assert.ok(!state().connected);
  click('reconnect');await until(()=>state()?.connected&&state().players.length===2);
  const recoveringID=state().id;click('outage');await until(()=>state().recovery.pending);
  await until(()=>state().connected&&state().id>recoveringID&&state().recovery.rejoins>0);
  for(const kind of ['lost','scheduled','retry','connected'])assert.ok(state().recovery.events.some(event=>event.kind===kind),'missing recovery event '+kind);
  assert.ok(state().recovery.events.length<=16);
  click('guide-stop');await until(()=>!state().connected);await delay(700);assert.equal(state().recovery.pending,false);assert.ok(element('guide-message').textContent.includes('主動停止'));
  click('guide-normal');await until(()=>state()?.connected&&state().profile==='clean');assert.equal(state().recovery.enabled,true);assert.ok(element('guide-message').textContent.includes('連線正常'));
  console.log('RF_NETWORK_RECOVERY_BROWSER_SOURCE PASS: retained prediction/interpolation, bidirectional settings, metrics/clock, snapshot pause/resume, outage/backoff/new identity, cancel/manual-stop; real C# + Go, DOM/canvas doubles');
}finally{windows.pagehide?.();peer?.close();for(const socket of sockets)socket.close();for(const timer of timers)clearInterval(timer);}
