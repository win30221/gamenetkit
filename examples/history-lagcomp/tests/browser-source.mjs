import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';
const origin=process.argv[process.argv.indexOf('--server')+1],html=await(await fetch(origin+'/')).text();assert.match(html,/<details id="advanced">/);assert.doesNotMatch(html,/<details id="advanced"[^>]*open/);
const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]));for(const[path,type]of[['/','text/html'],['/app.js','text/javascript'],['/style.css','text/css']]){const r=await fetch(origin+path);assert.equal(r.status,200);assert.ok(r.headers.get('content-type').startsWith(type));}
const source=await(await fetch(origin+'/app.js')).text(),elements=new Map(),windows={},documents={},sockets=[],timers=[],animation=[];let fills=0,arcs=0;
function element(id){assert.ok(ids.has(id),'missing HTML '+id);if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:id==='inspect',value:id==='profile'?'delay':id==='lookback'?'0.5':'',width:1000,height:440,textContent:'',hidden:id==='panel-shoot',addEventListener(n,h){this.handlers[n]=h;},setAttribute(n,v){this.attributes[n]=v;},focus(){document.activeElement=this;},getContext(){return new Proxy({},{get(_t,k){return k==='fillRect'?()=>fills++:k==='arc'?()=>arcs++:()=>{};},set(){return true;}});}});return elements.get(id);}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(n,h){documents[n]=h;}};class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(n,h){windows[n]=h;}},location:{href:origin+'/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Number,String,Set,console,requestAnimationFrame(f){animation.push(f);},setInterval(f,ms){const t=setInterval(f,ms);timers.push(t);return t;},clearInterval});
async function until(predicate){const end=Date.now()+9500;while(!predicate()){assert.ok(Date.now()<end,'history page timeout');animation.shift()?.();await delay(10);}}
const state=()=>vm.runInContext('frame',context),click=id=>element(id).handlers.click();let peer,peerFrame;
try{
  vm.runInContext(source,context);await until(()=>state()?.connected&&fills>0&&arcs>0&&state().preview?.found&&state().tick>35);
  assert.equal(element('panel-shoot').hidden,true);assert.ok(state().preview.tick<state().tick);const initialHP=state().target.hp;
  element('lookback').value='1';element('lookback').handlers.input();await until(()=>state().lookback===1&&state().preview?.found);assert.equal(state().target.hp,initialHP);assert.equal(element('lookback-label').textContent,'1.0 秒');
  peer=new WebSocket(origin.replace('http:','ws:')+'/lesson');peer.onmessage=e=>{peerFrame=JSON.parse(e.data);};await until(()=>peerFrame?.connected&&state().players.length===2);
  click('tab-shoot');assert.equal(element('panel-history').hidden,true);assert.equal(element('tab-shoot').attributes['aria-selected'],'true');await until(()=>state().inspect===false);
  click('current');await until(()=>!state().compensate&&Math.abs(state().target.x)<2);click('shoot');await until(()=>state().lastShot&&state().pending===0);
  assert.equal(state().lastShot.accepted,true);assert.equal(state().lastShot.currentHit,false);assert.equal(state().lastShot.historicalHit,true);assert.equal(state().lastShot.hit,false);assert.match(element('feedback').textContent,/未命中/);
  const first=state().lastShot.id;click('compensated');await until(()=>state().compensate&&Math.abs(state().target.x)<2);click('shoot');await until(()=>state().lastShot.id>first&&state().pending===0);
  assert.equal(state().lastShot.hit,true);assert.equal(state().hits,1);await until(()=>peerFrame.target.hp===initialHP-1);assert.match(element('feedback').textContent,/命中/);
  click('expired-shot');const prior=state().lastShot.id;await until(()=>state().lastShot.id>prior&&state().pending===0);assert.equal(state().lastShot.accepted,false);assert.equal(state().lastShot.reason,'outside_window');assert.equal(state().rejectedShots,1);assert.match(element('feedback').textContent,/拒絕/);
  const life=state().target.life;click('reset-target');await until(()=>state().target.life>life&&peerFrame.target.life>life);assert.equal(state().target.hp,5);
  click('tab-history');await until(()=>state().inspect&&state().preview?.found&&state().preview.target.life===state().target.life);assert.equal(element('panel-shoot').hidden,true);
  element('inspect').checked=false;element('inspect').handlers.change();await until(()=>!state().inspect&&state().preview===null);element('inspect').checked=true;element('inspect').handlers.change();await until(()=>state().preview?.found);
  for(const remote of ['latest','fixed','adaptive']){click('remote-'+remote);await until(()=>state().remote===remote);}
  element('profile').value='clean';element('profile').handlers.change();await until(()=>state().profile==='clean');
  element('world').focus();const own=state().id,z=state().players.find(p=>p.id===own).z;element('world').handlers.keydown({key:'w',preventDefault(){}});await until(()=>state().players.find(p=>p.id===own).z>z+.1);windows.blur();document.activeElement=null;
  let tick=state().tick;await until(()=>state().tick>tick+18);const stopped=state().players.find(p=>p.id===own).latestZ;tick=state().tick;await until(()=>state().tick>tick+6);assert.equal(state().players.find(p=>p.id===own).latestZ,stopped);
  click('disconnect');await until(()=>!state().connected&&peerFrame.players.length===1);assert.equal(state().target,null);assert.equal(state().lastShot,null);assert.equal(state().preview,null);
  click('reconnect');await until(()=>state()?.connected&&state().preview?.found&&state().players.length===2);assert.ok(state().id>own);assert.equal(state().hits,0);
  console.log('RF_HISTORY_BROWSER_SOURCE PASS: actual page/C# presentation/Go History+Bounded, history inspection, current miss vs compensated hit/live HP, expired rejection, shared life/reset, coalesced query modes, remote controls, input/blur and reconnect; DOM/Canvas doubles');
}finally{windows.pagehide?.();peer?.close();for(const s of sockets)s.close();for(const t of timers)clearInterval(t);}
