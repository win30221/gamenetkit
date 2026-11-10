import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';

const at=process.argv.indexOf('--server');
const origin=process.argv[at+1];
for(const [path,type] of [['/','text/html'],['/app.js','text/javascript'],['/style.css','text/css']]) {
  const response=await fetch(origin+path);assert.equal(response.status,200);
  assert.ok(response.headers.get('content-type').startsWith(type));
}
assert.equal((await fetch(origin+'/missing.js')).status,404);
const source=await (await fetch(origin+'/app.js')).text();
const elements=new Map(),windows={},documents={},sockets=[],timers=[],animation=[];
let fills=0;
function element(id){
  if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:['auto','self','ghost'].includes(id),value:id==='profile'?'clean':'',
    width:1100,height:560,textContent:'',addEventListener(name,handler){this.handlers[name]=handler;},
    setAttribute(name,value){this.attributes[name]=value;},focus(){document.activeElement=this;},
    getContext(){return new Proxy({}, {get(_target,key){return key==='fillRect'?()=>fills++:()=>{};},set(){return true;}});}});
  return elements.get(id);
}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(name,handler){documents[name]=handler;}};
class Socket extends WebSocket {constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(name,handler){windows[name]=handler;}},location:{href:origin+'/',protocol:'http:'},
  WebSocket:Socket,URL,JSON,Math,Set,console,requestAnimationFrame(fn){animation.push(fn);},
  setInterval(fn,ms){const timer=setInterval(fn,ms);timers.push(timer);return timer;},clearInterval});
let peer,peerFrame;
async function until(predicate){const end=Date.now()+6500;while(!predicate()){assert.ok(Date.now()<end,'browser-source timeout');animation.shift()?.();await delay(15);}}
const state=()=>vm.runInContext('frame',context);
const click=id=>element(id).handlers.click();
const change=(id,value)=>{element(id).checked=value;element(id).handlers.change();};
try {
  vm.runInContext(source,context);
  await until(()=>state()?.connected && fills>0);
  const first=state().id;
  assert.equal(state().mode,'fixed');
  const start=state().players.find(p=>p.id===first).x;
  await until(()=>state().players.find(p=>p.id===first).x>start+.4);
  for(const mode of ['latest','fixed','adaptive']) {
    click('mode-'+mode);await until(()=>state().mode===mode);
    assert.equal(element('mode-'+mode).attributes['aria-pressed'],'true');
  }
  element('profile').value='jitter';element('profile').handlers.change();
  await until(()=>state().profile==='jitter' && state().dropped>0);
  change('self',false);
  await delay(100);
  let own=state().players.find(p=>p.id===first);assert.equal(own.x,own.latestX,'self opt-out');
  element('profile').value='clean';element('profile').handlers.change();
  change('auto',false);element('world').focus();
  const key=(name)=>({key:name,preventDefault(){}});
  element('world').handlers.keydown(key('w'));
  await until(()=>state().players.find(p=>p.id===first).z>.3);
  windows.blur();document.activeElement=null;
  const stopTick=state().tick;await until(()=>state().tick>stopTick+12);
  const stopped=state().players.find(p=>p.id===first).latestZ,tick=state().tick;
  await until(()=>state().tick>tick+6);
  assert.equal(state().players.find(p=>p.id===first).latestZ,stopped,'blur did not stop input');
  peer=new WebSocket(origin.replace('http:','ws:')+'/lesson');
  peer.onopen=()=>peer.send(JSON.stringify({auto:false,mode:'latest'}));
  peer.onmessage=event=>{peerFrame=JSON.parse(event.data);};
  await until(()=>peerFrame?.connected && peerFrame.players.length===2 && state().players.length===2);
  assert.notEqual(first,peerFrame.id);
  click('disconnect');await until(()=>state() && !state().connected && peerFrame.players.length===1);
  assert.equal(state().players.length,0);
  click('reconnect');await until(()=>state()?.connected && state().players.length===2 && peerFrame.players.length===2);
  assert.ok(state().id>first);assert.equal(state().mode,'adaptive');
  console.log('RF_INTERPOLATION_BROWSER_SOURCE PASS: actual HTTP + C# host WS, page initialization/draw, all modes, jitter/loss, self toggle, manual input/blur, two viewers, disconnect/reconnect; DOM/canvas are test doubles');
} finally {
  windows.pagehide?.();peer?.close();for(const socket of sockets)socket.close();for(const timer of timers)clearInterval(timer);
}
