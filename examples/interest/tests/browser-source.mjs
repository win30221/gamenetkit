import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';
const origin=process.argv[process.argv.indexOf('--server')+1],html=await(await fetch(origin+'/')).text();
assert.match(html,/<details id="advanced">/);assert.doesNotMatch(html,/<details id="advanced"[^>]*open/);
const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]));
for(const [path,type] of [['/','text/html'],['/app.js','text/javascript'],['/style.css','text/css']]){const r=await fetch(origin+path);assert.equal(r.status,200);assert.ok(r.headers.get('content-type').startsWith(type));}
const source=await(await fetch(origin+'/app.js')).text(),elements=new Map(),windows={},documents={},sockets=[],timers=[],animation=[],radii=[];let fills=0;
function element(id){assert.ok(ids.has(id),'missing HTML element '+id);if(!elements.has(id))elements.set(id,{handlers:{},attributes:{},checked:false,value:id==='profile'?'clean':id==='radius'?'3':'',width:1000,height:440,textContent:'',
  addEventListener(name,handler){this.handlers[name]=handler;},setAttribute(name,value){this.attributes[name]=value;},focus(){document.activeElement=this;},
  getContext(){return new Proxy({},{get(_t,key){return key==='fillRect'?()=>fills++:key==='arc'?(_x,_y,r)=>radii.push(r):()=>{};},set(){return true;}});}});return elements.get(id);}
const document={getElementById:element,hidden:false,activeElement:null,addEventListener(name,handler){documents[name]=handler;}};
class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
const context=vm.createContext({document,window:{addEventListener(name,handler){windows[name]=handler;}},location:{href:origin+'/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Number,String,Set,console,
  requestAnimationFrame(fn){animation.push(fn);},setInterval(fn,ms){const t=setInterval(fn,ms);timers.push(t);return t;},clearInterval});
async function until(predicate){const end=Date.now()+7500;while(!predicate()){assert.ok(Date.now()<end,'AOI page timeout');animation.shift()?.();await delay(10);}}
const state=()=>vm.runInContext('frame',context),click=id=>element(id).handlers.click();let peer,peerFrame;
const key=name=>({key:name,preventDefault(){}});
const move=name=>{element('world').focus();element('world').handlers.keydown(key(name));};
const stop=()=>{windows.blur();document.activeElement=null;};
try{
  vm.runInContext(source,context);await until(()=>state()?.connected&&fills>0&&radii.length>0);
  peer=new WebSocket(origin.replace('http:','ws:')+'/lesson');peer.onmessage=e=>{peerFrame=JSON.parse(e.data);};
  await until(()=>peerFrame?.connected&&state().players.length===2&&state().snapshotBytes>0);
  const identity=state().id,other=peerFrame.id,allBytes=state().snapshotBytes;assert.equal(state().objects.length,13);assert.equal(state().interest.mode,'all');
  click('interest-aoi');await until(()=>state().interest.mode==='aoi'&&state().players.length===1);
  assert.equal(element('interest-aoi').attributes['aria-pressed'],'true');assert.equal(element('world-count').textContent,'13');assert.equal(Number(element('received-count').textContent),state().objects.length);
  assert.ok(state().objects.length<13&&state().snapshotBytes<allBytes);assert.ok(state().objects.some(o=>o.id==='landmark-hub'));assert.ok(!state().objects.some(o=>o.id==='landmark-12'));assert.equal(peerFrame.objects.length,13);assert.equal(peerFrame.interest.mode,'all');
  element('radius').value='5';element('radius').handlers.change();await until(()=>state().interest.radius===5&&state().players.some(p=>p.id===other));
  element('margin').checked=true;await until(()=>radii.includes((5+.75)*22));
  element('radius').value='3';element('radius').handlers.change();await until(()=>state().interest.radius===3&&state().players.length===1);
  move('d');await until(()=>state().players.find(p=>p.id===identity).latestX> -1&&state().players.some(p=>p.id===other));stop();
  assert.equal(state().interest.centerX,state().players.find(p=>p.id===identity).latestX);
  click('spawn');await until(()=>state().interest.ownedObjects===1&&state().objects.some(o=>o.kind==='marker')&&state().pending===0);
  const marker=state().objects.find(o=>o.kind==='marker').id;assert.equal(state().interest.totalObjects,14);
  move('a');await until(()=>state().players.find(p=>p.id===identity).latestX< -6&&!state().objects.some(o=>o.id===marker));stop();
  assert.equal(state().interest.totalObjects,14);assert.ok(peerFrame.objects.some(o=>o.id===marker));assert.equal(element('rebirth').disabled,false);
  click('rebirth');await until(()=>peerFrame.objects.find(o=>o.id===marker).life===2&&state().pending===0);assert.ok(!state().objects.some(o=>o.id===marker));
  move('d');await until(()=>state().objects.some(o=>o.id===marker));stop();assert.equal(state().objects.find(o=>o.id===marker).life,2);
  click('remove');await until(()=>state().interest.totalObjects===13&&peerFrame.objects.length===13);assert.equal(element('world-count').textContent,'13');
  click('interest-all');await until(()=>state().interest.mode==='all'&&state().objects.length===13&&state().players.length===2);
  element('auto').checked=true;element('auto').handlers.change();const start=state().players.find(p=>p.id===identity).latestX;await until(()=>Math.abs(state().players.find(p=>p.id===identity).latestX-start)>.4);
  click('interest-aoi');await until(()=>state().interest.mode==='aoi'&&state().objects.length<13);element('auto').checked=false;element('auto').handlers.change();
  for(const remote of ['latest','fixed','adaptive']){click('remote-'+remote);await until(()=>state().remote===remote);}
  for(const mode of ['authority','prediction']){click('mode-'+mode);await until(()=>state().mode===mode);}
  for(const correction of ['snap','smooth']){click('correction-'+correction);await until(()=>state().correction===correction);}
  element('profile').value='high';element('profile').handlers.change();await until(()=>state().profile==='high');
  let tick=state().tick;await until(()=>state().tick>tick+15);const stopped=state().players.find(p=>p.id===identity).latestX;tick=state().tick;await until(()=>state().tick>tick+6);assert.equal(state().players.find(p=>p.id===identity).latestX,stopped);
  click('disconnect');await until(()=>!state().connected&&peerFrame.players.length===1);assert.equal(state().interest,null);assert.equal(state().objects.length,0);assert.equal(state().snapshotBytes,0);
  click('reconnect');await until(()=>state()?.connected&&state().interest.mode==='aoi'&&peerFrame.players.length===2);assert.ok(state().id>identity);assert.equal(state().interest.radius,3);assert.equal(state().interest.totalObjects,13);
  console.log('RF_INTEREST_BROWSER_SOURCE PASS: real page/C# SDK/Go Radius, all/AOI + actual reduced payload, independent viewers, circle/radius/exit-margin, player reentry, hidden owned marker life and authority removal, auto/manual/blur, preserved prediction/smoothing controls and reconnect; DOM/Canvas doubles');
}finally{windows.pagehide?.();peer?.close();for(const s of sockets)s.close();for(const t of timers)clearInterval(t);}
