// Controlled WebSocket snapshots test the actual release WebGL renderer and SDK recovery.
// Web Audio start calls and screenshot pixels provide evidence without production audit hooks.
const fs=require('node:fs');const path=require('node:path');
const root=path.resolve(__dirname,'../../..');
const core=process.env.PLAYWRIGHT_CORE||path.join(root,'.local/tower-duel/browser/node_modules/playwright-core');
const {chromium}=require(core);const {PNG}=require(path.join(core,'lib/utilsBundle.js'));
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(condition,message)=>{if(!condition)throw Error(message)};
async function until(predicate,ms=20000){const deadline=Date.now()+ms;while(Date.now()<deadline){if(predicate())return;await pause(50)}throw Error('Timed out waiting for WebGL network evidence')}
function changedPixels(a,b){
 const left=PNG.sync.read(a),right=PNG.sync.read(b);assert(left.width===right.width&&left.height===right.height,'Screenshot sizes differ');
 let count=0;
 for(let y=Math.floor(left.height*.23);y<left.height*.77;y++)for(let x=Math.floor(left.width*.22);x<left.width*.78;x++){
  const at=(y*left.width+x)*4;let delta=0;for(let c=0;c<3;c++)delta+=Math.abs(left.data[at+c]-right.data[at+c]);if(delta>60)count++;
 }
 return count;
}
async function run(origin='http://127.0.0.1:18115',output=path.join(root,'artifacts/tower-duel/webgl-sdk/timeline')){
 fs.mkdirSync(output,{recursive:true});
 const config=await(await fetch(origin+'/api/config')).json();
 const map=JSON.parse(fs.readFileSync(path.join(root,'examples/tower-duel/protocol/map.json'),'utf8'));
 const rules=JSON.parse(fs.readFileSync(path.join(root,'examples/tower-duel/protocol/rules.json'),'utf8'));
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--enable-unsafe-swiftshader','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows']});
 const context=await browser.newContext({viewport:{width:720,height:1000},deviceScaleFactor:1});
 await context.addInitScript(()=>{
  window.__towerDuelAudio=[];const start=AudioBufferSourceNode.prototype.start;
  AudioBufferSourceNode.prototype.start=function(...args){
   // Unity can recreate a source when unpausing it; only JS_Sound_Play starts a new cue.
   if(this.buffer){const stack=new Error().stack;window.__towerDuelAudio.push({at:performance.now(),duration:this.buffer.duration,play:stack.includes('at _JS_Sound_Play (')})}
   return Reflect.apply(start,this,args);
  };
 });
 const page=await context.newPage();const errors=[],consoleMessages=[],checks={},measurements={};
 const token='q'.repeat(43),resumes=[];let route,heartbeat,welcomes=0,rejectResumes=false;
 const state={type:'welcome',protocol:3,room:'QATEST',match:'timeline-fixture',mapHash:config.mapHash,rulesHash:config.rulesHash,seat:0,tick:100,tickRate:30,snapshotRate:15,phase:'playing',remaining:150,countdown:0,energy:5,winner:-2,reason:'',resumeToken:token,
  players:[0,1].map(seat=>({seat,name:'Network fixture '+seat,connected:true,ready:true})),units:[],
  towers:map.towers.map(t=>({...t,hp:t.kind==='keep'?rules.keepHP:rules.towerHP,maxHP:t.kind==='keep'?rules.keepHP:rules.towerHP,active:t.kind!=='keep'})),
  hand:['guard','archer','swords','bomber'],nextCard:'charger',handVersion:0,actionWatermark:0,effects:[]};
 const send=(tick,effects=state.effects,type='snapshot')=>{state.tick=tick;state.effects=effects;state.type=type;route.send(JSON.stringify(state))};
 const alive=()=>{clearInterval(heartbeat);heartbeat=setInterval(()=>send(state.tick+2),67)};
 const shot=name=>page.screenshot({path:path.join(output,name+'.png')});
 const cueCount=duration=>page.evaluate(d=>window.__towerDuelAudio.filter(c=>c.play&&Math.abs(c.duration-d)<.003).length,duration);
 const fireball=(start,impact,end)=>({id:900,kind:'fireball',seat:0,x:0,z:0,radius:2.2,start,impact,end});
 page.on('pageerror',error=>errors.push(String(error)));
 page.on('console',message=>{consoleMessages.push({type:message.type(),text:message.text()});if(/Tower Duel rejected message|NullReferenceException|InvalidOperationException|RuntimeError/.test(message.text()))errors.push(message.text())});
 await page.routeWebSocket('**/ws',ws=>{
  ws.onMessage(raw=>{
   const hello=JSON.parse(String(raw));if(hello.type!=='hello')return;
   if(hello.mode==='resume'){
    resumes.push({at:Date.now(),credentialValid:hello.token===token&&hello.room===state.room});
    if(rejectResumes&&resumes.length<=2){ws.close({code:1013,reason:'Retry fixture'});return}
   }
   route=ws;send(state.tick,state.effects,'welcome');welcomes++;
   if(hello.mode==='resume')alive();
  });
  ws.onClose(()=>{if(route===ws)clearInterval(heartbeat)});
 });
 try{
  await page.goto(origin.replace(/\/$/,'')+'/?mode=multiplayer',{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>!!window.towerDuel,null,{timeout:60000});await pause(2500);
  await page.mouse.click(360,524);await until(()=>welcomes===1);await pause(400);
  const baseline=await shot('baseline');
  const spell=fireball(80,103,125);
  send(102,[spell]);await pause(650);
  const flying=await shot('fireball-stalled-before-impact');
  measurements.fireballPixels=changedPixels(baseline,flying);
  assert(measurements.fireballPixels>100,'The release renderer did not show the fireball');checks.fireballVisible=true;
  assert(await cueCount(.35)===0,'Fireball impacted beyond the latest received authority tick');checks.snapshotStallDoesNotImpact=true;
  send(104,[spell]);await pause(400);await shot('fireball-impact');
  assert(await cueCount(.35)===1,'Fireball impact sound did not follow the received/rendered timeline');checks.fireballImpact=true;
  send(104,[spell]);await pause(180);
  assert(await cueCount(.35)===1,'Repeated snapshot replayed fireball impact audio');checks.duplicateSnapshotNoRepeatAudio=true;
  send(126,[]);await pause(400);
  measurements.expiredFireballPixels=changedPixels(baseline,await shot('fireball-expired'));
  assert(measurements.expiredFireballPixels<measurements.fireballPixels*.15,'Expired fireball remained in the arena');checks.fireballExpires=true;
  const frost={id:901,kind:'frost',seat:0,x:0,z:0,radius:2.8,start:127,impact:130,end:146};
  send(128,[frost]);await pause(400);
  const frozen=await shot('frost-stalled-before-impact');measurements.frostPixels=changedPixels(baseline,frozen);
  assert(measurements.frostPixels>100&&await cueCount(.32)===0,'Frost did not respect the shared bounded timeline');checks.frostStallDoesNotImpact=true;
  send(132,[frost]);await pause(400);
  assert(await cueCount(.32)===1,'Frost impact sound did not follow the shared timeline');checks.frostImpact=true;
  send(148,[]);await pause(350);
  assert(changedPixels(baseline,await shot('frost-expired'))<measurements.frostPixels*.15,'Expired frost remained in the arena');checks.frostExpires=true;

  // Fail two real SDK reconnect attempts before restoring the same synthetic seat.
  rejectResumes=true;const disconnected=Date.now();route.close({code:1011,reason:'Recovery fixture'});
  await until(()=>resumes.length>=3&&welcomes===2,15000);await pause(300);
  measurements.retryDelaysMS=resumes.slice(0,3).map((attempt,index)=>attempt.at-(index?resumes[index-1].at:disconnected));
  assert(measurements.retryDelaysMS.every((delay,index)=>delay>=[400,850,1800][index]),'WebGL retries did not use SDK exponential backoff');
  assert(resumes.every(attempt=>attempt.credentialValid),'Recovery lost the saved room/token');checks.sdkBackoffAndSeatRecovery=true;
  await shot('socket-recovered');
  const beforeReload=welcomes;await pause(800);await page.reload({waitUntil:'domcontentloaded'});
  await page.waitForFunction(()=>!!window.towerDuel,null,{timeout:60000});await until(()=>welcomes>beforeReload);
  assert(resumes.at(-1).credentialValid,'Page reload lost the saved seat');checks.savedSeatReload=true;await pause(300);await shot('reload-recovered');
  clearInterval(heartbeat);
  // Reuse an effect ID in a new match: it must get a fresh impact lifecycle.
  state.match='timeline-rematch';state.handVersion=0;state.actionWatermark=0;send(0,[],'welcome');await pause(200);
  const rematch=fireball(1,4,30);send(2,[rematch]);await pause(350);
  assert(await cueCount(.35)===0,'Rematch retained the previous effect clock');
  send(6,[rematch]);await pause(350);
  assert(await cueCount(.35)===1,'Reused effect ID did not get a fresh impact lifecycle');checks.rematchResetsEffectTimeline=true;
  state.phase='ended';state.winner=0;state.reason='keep_destroyed';send(8,[]);await pause(300);await shot('ended');
  assert(errors.length===0,'WebGL runtime errors: '+errors.join(';'));
  const result={passed:true,fixture:'Controlled WebSocket snapshots against release WebGL; rendered pixels, real Web Audio starts and SDK retry attempts',origin,browser:browser.version(),checks,measurements,resumeAttempts:resumes.length,errors};
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));return result;
 }catch(error){await shot('failure').catch(()=>{});const audio=await page.evaluate(()=>window.__towerDuelAudio).catch(()=>null);fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({error:String(error),checks,measurements,resumeAttempts:resumes.length,audio,consoleMessages,errors},null,2));throw error}
 finally{clearInterval(heartbeat);await browser.close()}
}
module.exports={run};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
