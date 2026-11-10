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
  if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:['auto','ghost','target'].includes(id),value:id==='profile'?'delay':'',width:1100,height:560,textContent:'',
    addEventListener(name,handler){this.handlers[name]=handler;},setAttribute(name,value){this.attributes[name]=value;},focus(){document.activeElement=this;},
    getContext(){return new Proxy({}, {get(_target,key){return key==='fillRect'?()=>fills++:()=>{};},set(){return true;}});}});
  return elements.get(id);
}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(name,handler){documents[name]=handler;}};
class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(name,handler){windows[name]=handler;}},location:{href:origin+'/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Set,console,
  requestAnimationFrame(fn){animation.push(fn);},setInterval(fn,ms){const timer=setInterval(fn,ms);timers.push(timer);return timer;},clearInterval});
async function until(predicate){const end=Date.now()+6500;while(!predicate()){assert.ok(Date.now()<end,'prediction page timeout');animation.shift()?.();await delay(10);}}
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
  console.log('RF_PREDICTION_BROWSER_SOURCE PASS: real C# host + Go WS, page draw, prediction/correction, input/blur, authority bump, moving remote latest/fixed/adaptive without affecting own prediction, two viewers, reconnect; DOM/canvas doubles');
}finally{windows.pagehide?.();peer?.close();for(const socket of sockets)socket.close();for(const timer of timers)clearInterval(timer);}
