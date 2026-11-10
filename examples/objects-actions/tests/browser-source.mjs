import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';
const origin=process.argv[process.argv.indexOf('--server')+1];
const html=await(await fetch(origin+'/')).text();
assert.match(html,/<details id="advanced">/);assert.doesNotMatch(html,/<details id="advanced"[^>]*open/);
assert.match(html,/<section id="panel-actions"[^>]*hidden/);
const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]));
for(const [path,type] of [['/','text/html'],['/app.js','text/javascript'],['/style.css','text/css']]){
  const response=await fetch(origin+path);assert.equal(response.status,200);assert.ok(response.headers.get('content-type').startsWith(type));
}
const source=await(await fetch(origin+'/app.js')).text();
const elements=new Map(),windows={},documents={},sockets=[],timers=[],animation=[];let fills=0,arcs=0;
function element(id){
  assert.ok(ids.has(id),'missing actual HTML element '+id);
  if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:false,value:id==='profile'?'delay':'',width:1000,height:440,textContent:'',hidden:id==='panel-actions',
    addEventListener(name,handler){this.handlers[name]=handler;},setAttribute(name,value){this.attributes[name]=value;},focus(){document.activeElement=this;},
    getContext(){return new Proxy({}, {get(_target,key){return key==='fillRect'?()=>fills++:key==='arc'?()=>arcs++:()=>{};},set(){return true;}});}});
  return elements.get(id);
}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(name,handler){documents[name]=handler;}};
class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(name,handler){windows[name]=handler;}},location:{href:origin+'/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Set,console,
  requestAnimationFrame(fn){animation.push(fn);},setInterval(fn,ms){const timer=setInterval(fn,ms);timers.push(timer);return timer;},clearInterval});
async function until(predicate){const end=Date.now()+6500;while(!predicate()){assert.ok(Date.now()<end,'objects/actions page timeout');animation.shift()?.();await delay(10);}}
const state=()=>vm.runInContext('frame',context),click=id=>element(id).handlers.click();
let peer,peerFrame;
try{
  vm.runInContext(source,context);await until(()=>state()?.connected&&fills>0);
  assert.equal(element('auto').checked,false);assert.equal(state().mode,'prediction');assert.equal(state().remote,'adaptive');
  assert.equal(element('remove').disabled,true);
  peer=new WebSocket(origin.replace('http:','ws:')+'/lesson');peer.onmessage=event=>{peerFrame=JSON.parse(event.data);};
  await until(()=>peerFrame?.connected&&state().players.length===2);
  click('spawn');await until(()=>state().pending===1);assert.equal(state().objects.length,0);assert.equal(state().actions.at(-1).visible,false);
  assert.equal(element('spawn').disabled,true);assert.match(element('feedback').textContent,/等待確認/);
  await until(()=>state().objects.length===1&&peerFrame.objects.length===1&&state().confirmed===1);
  const id=state().objects[0].id;assert.equal(state().actions.at(-1).starts,1);
  click('rebirth');await until(()=>state().objects[0].life===2&&peerFrame.objects[0].life===2);
  assert.equal(state().objects[0].id,id);assert.equal(state().added,2);assert.equal(state().removed,1);assert.match(element('objects-list').textContent,/life 2/);
  click('resend');let tick=state().tick;await until(()=>state().tick>tick+8);assert.equal(state().objects.length,1);assert.equal(state().confirmed,2);assert.equal(state().actions.at(-1).starts,1);
  peer.send(JSON.stringify({perform:'remove'}));await until(()=>peerFrame.rejected===1);assert.equal(state().objects.length,1);
  click('tab-actions');assert.equal(element('panel-objects').hidden,true);assert.equal(element('panel-actions').hidden,false);assert.equal(element('tab-actions').attributes['aria-selected'],'true');
  element('speculative').checked=true;
  click('accept');await until(()=>state().pending===1);assert.equal(state().actions.at(-1).visible,true);await until(()=>arcs>0);
  await until(()=>state().confirmed===3&&state().objects.length===2&&peerFrame.objects.length===2);assert.equal(state().actions.at(-1).starts,1);
  click('deny');await until(()=>state().pending===1&&state().actions.at(-1).visible);
  await until(()=>state().rejected===1);assert.equal(state().objects.length,2);assert.equal(state().actions.at(-1).visible,false);assert.match(element('feedback').textContent,/已拒絕/);
  click('timeout');await until(()=>state().expired===1);assert.equal(state().actions.at(-1).visible,false);assert.equal(state().objects.length,2);assert.match(element('feedback').textContent,/已逾時/);
  click('tab-objects');click('remove');await until(()=>state().objects.length===1&&state().pending===0);
  for(const remote of ['latest','fixed','adaptive']){click('remote-'+remote);await until(()=>state().remote===remote);}
  for(const mode of ['authority','prediction']){click('mode-'+mode);await until(()=>state().mode===mode);}
  for(const correction of ['snap','smooth']){click('correction-'+correction);await until(()=>state().correction===correction);}
  element('world').focus();const own=state().players.find(p=>p.id===state().id),start=own.z;
  element('world').handlers.keydown({key:'w',preventDefault(){}});await until(()=>state().players.find(p=>p.id===state().id).z>start+.1);
  windows.blur();document.activeElement=null;tick=state().tick;await until(()=>state().tick>tick+20);
  const stopped=state().players.find(p=>p.id===state().id).latestZ;tick=state().tick;await until(()=>state().tick>tick+8);assert.equal(state().players.find(p=>p.id===state().id).latestZ,stopped);
  const old=state().id;click('disconnect');await until(()=>!state().connected&&peerFrame.players.length===1&&peerFrame.objects.length===0);
  assert.equal(state().objects.length,0);assert.equal(state().actions.length,0);
  click('reconnect');await until(()=>state()?.connected&&state().players.length===2);assert.ok(state().id>old);
  assert.equal(state().objects.length,0);assert.equal(state().confirmed,0);
  assert.match(element('feedback').textContent,/先按/);
  console.log('RF_OBJECTS_ACTIONS_BROWSER_SOURCE PASS: actual page source + C# SDK + Go WS, guided tabs, waiting/provisional feedback, shared spawn/remove/rebirth, ownership, dedup, reject/timeout, retained movement controls, blur, disconnect cleanup/rejoin; DOM/Canvas doubles');
}finally{windows.pagehide?.();peer?.close();for(const socket of sockets)socket.close();for(const timer of timers)clearInterval(timer);}
