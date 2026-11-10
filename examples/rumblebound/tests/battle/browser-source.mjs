import assert from 'node:assert/strict';
import vm from 'node:vm';
import {setTimeout as delay} from 'node:timers/promises';

const origin=process.argv[process.argv.indexOf('--server')+1],html=await(await fetch(origin+'/diagnostics/')).text(),source=await(await fetch(origin+'/diagnostics/app.js')).text();
assert.match(html,/<details id="advanced"/);assert.doesNotMatch(html,/<details id="advanced"[^>]*open/);
for(const[path,type]of[['/diagnostics/','text/html'],['/diagnostics/app.js','text/javascript'],['/diagnostics/style.css','text/css'],['/app.js','text/javascript'],['/style.css','text/css'],['/transport.js','text/javascript'],['/api/config','application/json']]){const reply=await fetch(origin+path);assert.equal(reply.status,200,path);assert.ok(reply.headers.get('content-type').startsWith(type),path);}
const entry=await fetch(origin+'/',{redirect:'manual'});assert.ok([200,503].includes(entry.status));assert.equal(entry.headers.get('location'),null);const rootHTML=await entry.text();assert.match(rootHTML,entry.status===200?/createUnityInstance/:/Build-Unity\.ps1/);
for(const path of ['/Build/WebGL.loader.js','/TemplateData/style.css','/mobile-view.js']){const response=await fetch(origin+path);assert.equal(response.status,200,path);}
const wasm=await fetch(origin+'/Build/WebGL.wasm',{method:'HEAD'});assert.equal(wasm.status,200);assert.match(wasm.headers.get('content-type'),/application\/wasm/);
const diagnosticsRedirect=await fetch(origin+'/diagnostics',{redirect:'manual'});assert.equal(diagnosticsRedirect.status,302);assert.equal(diagnosticsRedirect.headers.get('location'),'/diagnostics/');
const unityRedirect=await fetch(origin+'/unity',{redirect:'manual'});assert.equal(unityRedirect.status,302);assert.equal(unityRedirect.headers.get('location'),'/unity/');
const unityDirectory=await fetch(origin+'/unity/',{redirect:'manual'});assert.ok([200,503].includes(unityDirectory.status),'Unity directory must serve the player or actionable build instructions');assert.ok(unityDirectory.headers.get('content-type').startsWith('text/html'));const unityHTML=await unityDirectory.text();if(unityDirectory.status===503){assert.match(unityHTML,/Build-Unity\.ps1 -Project RumbleboundExample -Target Web/);assert.match(unityHTML,/href="\/diagnostics\/"/);}else{assert.doesNotMatch(unityHTML,/<canvas id="world"/);assert.match(unityHTML,/createUnityInstance/);}
assert.equal((await fetch(origin+'/unity/does-not-exist.wasm')).status,404);
assert.equal((await fetch(origin+'/unity/%2e%2e%2fProjectSettings%2fProjectSettings.asset')).status,404);
assert.equal((await fetch(origin+'/api/session/guest',{method:'POST',headers:{Origin:'http://untrusted.invalid','Content-Type':'application/json'},body:'{}'})).status,403);
const loginStart=await fetch(origin+'/api/auth/google/start',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json'},body:'{}'});assert.equal(loginStart.status,200);const attempt=await loginStart.json(),cookie=loginStart.headers.get('set-cookie').split(';')[0];assert.ok(attempt.nonce&&cookie.startsWith('rb_google_attempt='));
const exchange=()=>fetch(origin+'/api/auth/google/exchange',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({attemptID:attempt.attemptID,idToken:'invalid-token'})});assert.equal((await exchange()).status,401);assert.equal((await exchange()).status,401,'consumed nonce accepted');
const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]));
const pages=[];
function page(storage=new Map()){
  const nodes=new Map(),windows={},documents={},animation=[],sockets=[];let draws=0;
  const initial={'team-count':'2','practice-hero':'ranger','bot-hero':'tank',prediction:'frame',correction:'exponential',timeline:'adaptive',clock:'disciplined',transport:'websocket',codec:'json',interest:'all',radius:'12',delay:'0','bot-mode':'idle'};
  let document;
  function node(id,tag='div'){
    return {id,tagName:tag.toUpperCase(),handlers:{},children:[],value:initial[id]??'',checked:id==='outlines'||id==='reconnect',hidden:false,disabled:false,textContent:'',className:'',width:1100,height:820,
      addEventListener(name,fn){this.handlers[name]=fn;},append(...children){this.children.push(...children);if(this.tagName==='SELECT'&&!this.value&&children[0])this.value=children[0].value;},replaceChildren(...children){this.children=[...children];},focus(){document.activeElement=this;},getBoundingClientRect(){return {left:0,top:0,width:1100,height:820};},
      getContext(){return new Proxy({},{get(_t,key){return key==='clearRect'?()=>draws++:()=>{};},set(){return true;}});}};
  }
  function element(id){assert.ok(ids.has(id),'actual HTML missing '+id);if(!nodes.has(id))nodes.set(id,node(id));return nodes.get(id);}
  document={getElementById:element,createElement:tag=>node('',tag),activeElement:null,hidden:false,addEventListener(name,fn){documents[name]=fn;}};
  class Socket extends WebSocket{constructor(url){super(url);sockets.push(this);}}
  const context=vm.createContext({document,window:{addEventListener(name,fn){windows[name]=fn;}},location:{href:origin+'/diagnostics/',protocol:'http:'},WebSocket:Socket,URL,JSON,Math,Number,String,Set,console,setTimeout,requestAnimationFrame(fn){animation.push(fn);},sessionStorage:{getItem:key=>storage.get(key)??null,setItem:(key,value)=>storage.set(key,value),removeItem:key=>storage.delete(key)}});
  vm.runInContext(source,context);
  const result={element,storage,context,sockets,state:()=>vm.runInContext('frame',context),draws:()=>draws,pump(){animation.splice(0).forEach(fn=>fn());},click(id){assert.equal(element(id).disabled,false,'disabled '+id);element(id).handlers.click();},change(id,value){element(id).value=value;element(id).handlers.change();},stop(){windows.pagehide?.();}};
  pages.push(result);return result;
}
async function until(predicate,label){const end=Date.now()+15000;while(!predicate()){for(const p of pages)p.pump();assert.ok(Date.now()<end,label+' timed out '+JSON.stringify(pages.map(p=>{const f=p.state();return {stage:f?.stage,notice:f?.notice,connected:f?.connected,generation:f?.world?.generation,ack:f?.net?.frameAck,missing:f?.net?.missing,codec:f?.net?.settings?.Server?.codec};})));await delay(10);}}
let one,two;
try{
  one=page();two=page();await until(()=>one.state()?.stage==='login'&&two.state()?.stage==='login','config');
  one.click('guest');two.click('guest');await until(()=>one.state()?.stage==='lobby'&&two.state()?.stage==='lobby'&&!one.state().busy&&!two.state().busy,'guest');
  one.click('create');await until(()=>one.state().stage==='room'&&!one.state().busy,'create');two.element('room-code').value=one.state().room.code;two.click('join');await until(()=>two.state().stage==='room'&&!two.state().busy,'join');
  one.change('team',one.state().room.teams[0].teamID);two.change('team',two.state().room.teams[1].teamID);await until(()=>one.state().room.members.find(m=>m.userID===one.state().userID).teamID===one.state().room.teams[0].teamID&&two.state().room.members.find(m=>m.userID===two.state().userID).teamID===two.state().room.teams[1].teamID&&!one.state().busy&&!two.state().busy,'teams');
  one.change('hero','ranger');two.change('hero','tank');await until(()=>one.state().room.members.find(m=>m.userID===one.state().userID).heroID==='ranger'&&two.state().room.members.find(m=>m.userID===two.state().userID).heroID==='tank'&&!one.state().busy&&!two.state().busy,'heroes');
  one.click('ready');two.click('ready');await until(()=>!one.element('start').disabled&&!one.state().busy&&!two.state().busy,'ready');assert.equal(two.element('start').disabled,true,'non-host can start');
  one.click('start');await until(()=>one.state().canControl&&two.state().canControl,'loaded battle');assert.ok(one.draws()>0&&two.draws()>0);
  const identity=one.state().userID,id=one.state().world.playerID,generation=one.state().world.generation;
  one.element('world').focus();const start=two.state().world.players.find(p=>p.state.playerID===id).state.z;
  one.element('world').handlers.keydown({key:'w',code:'KeyW',preventDefault(){}});await until(()=>two.state().world.players.find(p=>p.state.playerID===id).state.z>start+.35,'owned input');one.element('world').handlers.blur();
  one.click('fire');await until(()=>one.state().net.action==='fired','fire');
  one.click('record-start');await until(()=>one.state().recording.frames>=4,'recording');one.click('record-stop');await until(()=>!one.state().recording.active,'stop recording');
  const recording=await(await fetch(origin+one.element('record-download').href)).text();assert.equal(JSON.parse(recording).model,'rumblebound-view-v1');
  one.element('codec').value='delta';one.element('delay').value='0.15';one.element('speculative').checked=true;if(process.argv.includes('--webtransport'))one.element('transport').value='webtransport';one.click('apply');
  await until(()=>one.state().connected&&one.state().world.generation>generation&&one.state().canControl&&one.state().net.frameAck>5,'selection rejoin');assert.equal(one.state().userID,identity);assert.equal(one.state().world.playerID,id);
  if(process.argv.includes('--webtransport'))assert.equal(one.state().net.settings.Transport,'webtransport');
  await until(()=>one.state().world.players.find(p=>p.own).state.fireReadyTick<=one.state().world.tick,'skill cooldown before cue');
  one.click('fire');await until(()=>one.state().world.actions.some(a=>a.status==='pending'),'local action cue');await until(()=>one.state().net.confirmed>0,'cue confirmed');
  const missing=one.state().net.missing,ack=one.state().net.frameAck;one.click('forget');await until(()=>one.state().net.missing>missing&&one.state().net.frameAck>ack,'baseline recovery');
  const before=one.state().world.generation;one.click('cut');await until(()=>one.state().recovery.pending,'retry pending');assert.equal(one.state().world.frozen,true);await until(()=>one.state().connected&&one.state().world.generation>before&&one.state().canControl,'retry complete');
  const storage=one.storage;one.stop();await until(()=>two.state().world.players.find(p=>p.state.playerID===id)?.state.online===false,'page close');
  one=page(storage);await until(()=>one.state()?.userID===identity&&one.state().world.playerID===id&&one.state().canControl,'page identity restore');
  await until(()=>!one.state().busy,'logout ready');one.click('logout');await until(()=>one.state().stage==='login'&&!one.state().busy,'logout');assert.equal(storage.has('rf.remake.identity'),false);
  one.click('guest');await until(()=>one.state().stage==='lobby'&&!one.state().busy,'practice guest');one.element('practice-hero').value='mage';one.click('practice');await until(()=>one.state().room?.practice&&one.state().canControl&&!one.state().busy,'practice');
  one.click('fire');await until(()=>one.state().net.action==='cast','mage cast');one.change('bot-mode','capture+attack+dodge+kite');await until(()=>one.state().room.botMode==='capture+attack+dodge+kite'&&!one.state().busy,'AI mode');
  one.element('bot-movement').value='capture';one.element('bot-attack').checked=false;one.element('bot-dodge').checked=true;one.element('bot-kite').checked=true;one.click('bot-apply');await until(()=>one.state().room.botMode==='capture+dodge+kite'&&!one.state().busy,'composed AI controls');assert.equal(one.element('bot-mode').value,'capture+dodge+kite','custom mode must remain visible');
  one.click('leave-practice');await until(()=>one.state().stage==='lobby'&&!one.state().busy,'practice leave');
  const importURL=origin+'/viewer/'+one.state().viewerID+'/replay';assert.equal((await fetch(importURL,{method:'POST',headers:{'Content-Type':'application/json'},body:recording})).status,200);await until(()=>one.state().stage==='replay'&&one.state().world.players.length===2,'offline playback');
  one.click('replay-pause');await until(()=>!one.state().replay.playing,'pause');one.change('replay-seek',String(one.state().replay.first));await until(()=>Math.abs(one.state().replay.tick-one.state().replay.first)<.1,'seek');one.change('replay-speed','2');await until(()=>one.state().replay.speed===2,'speed');
  assert.equal((await fetch(importURL,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status,400);assert.equal(one.state().stage,'replay','invalid import replaced playback');
  one.click('replay-end');await until(()=>one.state().stage==='lobby','end playback');
  await until(()=>!one.state().busy,'second practice ready');one.click('practice');await until(()=>one.state().canControl&&!one.state().busy,'second generation-one match loaded');one.click('leave-practice');await until(()=>one.state().stage==='lobby'&&!one.state().busy,'second practice leave');
  console.log('REMAKE_BROWSER_SOURCE PASS actual page + C# flow/SDK + shared Go game: two-player rooms/loading/movement/actions, codec/recovery/identity, mage/AI, authoritative snapshot recording/export, offline playback/pause/seek/speed/invalid-file isolation; DOM/Canvas doubles, no renderer claim');
}finally{for(const p of pages)p.stop();}
