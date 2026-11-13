// Requires playwright-core (PLAYWRIGHT_CORE may point to an existing installation).
const fs=require('node:fs');const path=require('node:path');
const root=path.resolve(__dirname,'../../..');
const {chromium}=require(process.env.PLAYWRIGHT_CORE||path.join(root,'.local/tower-duel/browser/node_modules/playwright-core'));
const {portraitFrames,verticalFraming}=require('./Card-Image-Checks.cjs');
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function until(predicate,ms=20000){const end=Date.now()+ms;while(Date.now()<end){if(predicate())return;await pause(80)}throw Error('Timed out waiting for browser evidence')}
function assert(value,message){if(!value)throw Error(message)}
async function run(origin='http://127.0.0.1:18115',output=path.join(root,'artifacts/tower-duel/p0/web')){
 fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--enable-unsafe-swiftshader','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows']});
 const records=[];const checks={};const framing=[];const widthInvariant=[];let result;
 const readProxy=()=>fetch(origin+'/__qa/stats').then(r=>r.ok?r.json():null).catch(()=>null);
 const proxyBefore=await readProxy();
 const shot=(record,name)=>record.page.screenshot({path:path.join(output,name+'.png')});
 const accepted=record=>[...record.actions.values()].filter(a=>a.accepted&&a.match===record.state?.match);
 const tap=(record,x,y)=>record.touch?record.page.touchscreen.tap(x,y):record.page.mouse.click(x,y);
 async function make(viewport,touch){
  const context=await browser.newContext({viewport,hasTouch:touch,isMobile:touch,deviceScaleFactor:1});const page=await context.newPage();
  await context.addInitScript(()=>{
   const NativeWebSocket=window.WebSocket;window.__towerDuelTestSockets=[];
   window.WebSocket=new Proxy(NativeWebSocket,{construct(Target,args){const socket=Reflect.construct(Target,args);window.__towerDuelTestSockets.push(socket);return socket}});
  });
  const record={context,page,viewport,touch,actions:new Map(),sent:[],errors:[],kinds:new Set(),spells:new Set(),welcomes:0};records.push(record);
  page.on('pageerror',error=>record.errors.push(String(error)));
  page.on('websocket',ws=>{
   ws.on('framesent',({payload})=>{try{const m=JSON.parse(String(payload));if(m.type==='deploy')record.sent.push(m)}catch{}});
   ws.on('framereceived',({payload})=>{
    let m;try{m=JSON.parse(String(payload))}catch{return}
    if(m.type==='welcome'||m.type==='snapshot'){
     const prior=record.state;
     if(prior?.match===m.match&&prior.handVersion>m.handVersion){m.hand=prior.hand;m.nextCard=prior.nextCard;m.handVersion=prior.handVersion;m.energy=prior.energy}
     record.state=m;if(m.type==='welcome')record.welcomes++;
     for(const unit of m.units)record.kinds.add(unit.kind);for(const effect of m.effects)record.spells.add(effect.kind);
    }else if(m.type==='action'){
     const key=m.match+'#'+m.action;const prior=record.actions.get(key);if(prior&&JSON.stringify(prior)!==JSON.stringify(m))record.errors.push('Retry changed an action result');record.actions.set(key,m);
     if(record.state?.match===m.match&&m.handVersion>=record.state.handVersion){record.state.hand=m.hand;record.state.nextCard=m.nextCard;record.state.handVersion=m.handVersion;record.state.energy=m.energy}
    }else if(m.type==='error')record.errors.push(m.code);
   });
  });
  await page.goto(origin,{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>!!window.towerDuel,null,{timeout:45000});await pause(4000);return record;
 }
 function screen(record,x,z){
  const {width:w,height:h}=record.viewport;
  const bottom=.19,top=.02,height=1-bottom-top,center=bottom+height/2;
  const sin=Math.sin(Math.PI/3),cos=Math.cos(Math.PI/3),far=11*sin+5.3*cos+.45,near=13.1*sin+.15,focus=(far-near)/2;
  const size=Math.max((far+near)/2/height,(15*sin+2*cos+.15-focus)/(2*(1-center))),sign=record.state.seat===0?1:-1;
  return {x:w/2+sign*x*h/(2*size),y:h*(1-center)+(focus-sign*sin*z)*h/(2*size)};
 }
 function pointerForAim(record,point){
  const {height}=record.viewport;
  const lift=record.touch?Math.max(48,Math.min(72,height*.065)):Math.max(20,Math.min(36,height*.035));
  return {x:point.x,y:point.y+lift};
 }
 async function drag(record,index,target,release=true){
  const {width,height}=record.viewport,panelScale=Math.sqrt(width/720*height/1000);
  const scale=panelScale*Math.min(.9,(width/panelScale-16)/600,height/panelScale*.18/234);
  const x=width/2+(-300+16+56+122*index)*scale,y=height*.82+121*scale;
  if(record.touch){
   record.cdp=record.cdp||await record.context.newCDPSession(record.page);
   await record.cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y,id:1,radiusX:4,radiusY:4}]});
   for(let i=1;i<=10;i++){await record.cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:x+(target.x-x)*i/10,y:y+(target.y-y)*i/10,id:1,radiusX:4,radiusY:4}]});await pause(25)}
   if(release)await record.cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
  }else{await record.page.mouse.move(x,y);await record.page.mouse.down();await pause(80);await record.page.mouse.move(target.x,target.y,{steps:12});await pause(120);if(release)await record.page.mouse.up()}
 }
 async function deploy(record,card){
  const count=accepted(record).length;const index=record.state.hand.indexOf(card.id);assert(index>=0,'Requested card is outside hand');
  let target=screen(record,-5,record.state.seat===0?-3.5:3.5);
  if(card.type==='spell'){const enemy=record.state.units.find(u=>u.seat!==record.state.seat);target=enemy?screen(record,enemy.x,enemy.z):screen(record,-5,record.state.seat===0?7.7:-7.7)}
  const headerDrop=record===records[0]&&card.id==='fireball'&&!checks.headerOpeningDrop;
  if(headerDrop)target=screen(record,0,14.6);
  await drag(record,index,pointerForAim(record,target),false);await pause(160);await shot(record,'preview-'+card.id+'-'+record.state.seat);
  if(record.touch)await record.cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});else await record.page.mouse.up();
  await until(()=>accepted(record).length>count);await pause(200);
  const sent=record.sent.at(-1);
  if(card.type==='troop'){
   assert(Math.abs(sent.x*2-Math.round(sent.x*2))<.001&&Math.abs(Math.abs(sent.x%1)-.5)<.001&&Math.abs(Math.abs(sent.z%1)-.5)<.001,'Troop did not snap to cell centre');
   assert(Math.abs(sent.x+5)<1&&Math.abs(sent.z-(record.state.seat===0?-3.5:3.5))<1,'Finger lift changed the intended deployment cell');
   checks.gridCentres=true;checks.fingerLift=true;
  }
  if(headerDrop)checks.headerOpeningDrop=true;
  if(card.type==='spell')await shot(record,card.id+'-'+record.state.seat);
 }
 async function checkInsufficientEnergy(record,catalog){
  const index=record.state.hand.findIndex(id=>catalog.find(card=>card.id===id).cost-record.state.energy>=1);
  if(index<0)return false;
  const count=record.sent.length,version=record.state.handVersion;
  await drag(record,index,screen(record,-5,record.state.seat===0?-3.5:3.5));await pause(400);
  assert(record.sent.length===count&&record.state.handVersion===version,'Insufficient energy changed hand');return true;
 }
 try{
  const config=await(await fetch(origin+'/api/config')).json();assert(config.protocol===3&&config.cards.length===8,'Wrong browser/server version');
  const a=await make({width:720,height:1000},false),b=await make({width:390,height:844},true);
  await shot(a,'lobby-desktop');await shot(b,'lobby-touch');
  await tap(a,360,524);await until(()=>!!a.state?.room);
  await tap(b,195,533);await b.page.keyboard.type(a.state.room,{delay:50});await tap(b,195,582);await until(()=>!!b.state?.room);await pause(500);
  assert(await b.page.locator('input').count()===0,'Mobile keyboard remained after joining');checks.mobileKeyboardClosed=true;
  assert(a.state.match===b.state.match&&a.state.seat===0&&b.state.seat===1,'Different rooms/seats');
  await shot(b,'waiting-touch');await tap(a,360,632);await tap(b,195,514);await until(()=>records.every(r=>r.state?.phase==='playing'));
  const initial=a.state.match;
  // A small pull stays a translucent card; the following valid drag becomes a model.
  let before=a.sent.length;
  const cardScale=Math.min(.9,(720-16)/600,1000*.18/234);
  await drag(a,0,{x:360+(-300+16+56)*cardScale,y:820+121*cardScale-22},false);
  await shot(a,'card-follow-before-board');await a.page.mouse.up();await pause(250);
  assert(a.sent.length===before,'Short card pull deployed a unit');checks.shortPullCancel=true;
  let count=a.sent.length;await drag(a,0,{x:180,y:950});await pause(400);assert(a.sent.length===count,'Cancelled mouse drag sent a deployment');checks.mouseCancel=true;
  count=b.sent.length;await drag(b,0,screen(b,-5,-4),false);await pause(150);await shot(b,'invalid-touch-preview');await b.cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await pause(400);assert(b.sent.length===count,'Illegal touch placement sent a deployment');checks.touchIllegalCancel=true;
  await deploy(a,config.cards[0]);await deploy(b,config.cards[0]);
  if(await checkInsufficientEnergy(b,config.cards))checks.insufficientEnergyCancel=true;
  let recovered=false,socketRecovered=false,framingCaptured=false,nextProgress=0;const deadline=Date.now()+310000;
  while(Date.now()<deadline&&!records.every(r=>r.state?.phase==='ended')){
   for(const record of records){
    if(record.state?.phase!=='playing'&&record.state?.phase!=='overtime')continue;
    const n=accepted(record).length;if(record===b&&n>=8)continue;
    const card=config.cards[n%8];if(record.state.hand.includes(card.id)&&record.state.energy>=card.cost+.04){await deploy(record,card);if(!checks.insufficientEnergyCancel&&await checkInsufficientEnergy(record,config.cards))checks.insufficientEnergyCancel=true;}
    if(record===a&&!recovered&&accepted(a).length>=2){
     const before={match:a.state.match,version:a.state.handVersion,watermark:a.state.actionWatermark,welcomes:a.welcomes};
     await a.page.reload({waitUntil:'domcontentloaded'});await until(()=>a.welcomes>before.welcomes,30000);await pause(800);
     assert(a.state.match===before.match&&a.state.handVersion===before.version&&a.state.actionWatermark>=before.watermark,'Reload did not restore same match/hand');recovered=true;checks.reloadRecovery=true;await shot(a,'recovered-desktop');
    }
    if(record===a&&recovered&&!socketRecovered&&accepted(a).length>=3){
     const before={match:a.state.match,version:a.state.handVersion,watermark:a.state.actionWatermark,welcomes:a.welcomes};
     const closed=await a.page.evaluate(()=>{let count=0;for(const socket of window.__towerDuelTestSockets)if(socket.readyState===WebSocket.OPEN){socket.close(1000,'Recovery regression test');count++}return count});
     assert(closed===1,'Expected one active game WebSocket');
     await until(()=>a.welcomes>before.welcomes,30000);await pause(500);
     assert(a.state.match===before.match&&a.state.handVersion===before.version&&a.state.actionWatermark>=before.watermark,'Socket reconnect did not restore same match/hand');
     socketRecovered=true;checks.socketRecovery=true;await shot(a,'socket-recovered-desktop');
    }
    if(record===a&&recovered&&!framingCaptured){
     await shot(b,'battle-phone');
     for(const [name,width,height] of [['user-window',912,892],['portrait-phone',900,1600],['small-phone',320,568],['portrait-tablet',768,1024],['landscape-tablet',1024,768],['short-window',912,500],['wide-desktop',1366,768]]){
      await a.page.setViewportSize({width,height});await pause(800);await shot(a,'framing-'+name);
      const canvas=await a.page.locator('canvas').boundingBox();assert(canvas&&Math.abs(canvas.width-width)<2&&Math.abs(canvas.height-height)<2,'Canvas did not fill '+name);
      framing.push({name,width,height,cards:portraitFrames(fs.readFileSync(path.join(output,'framing-'+name+'.png')))});
     }
     for(const width of [390,768,912,1366]){
      await a.page.setViewportSize({width,height:892});await pause(800);const name='width-'+width;await shot(a,name);
      const buffer=fs.readFileSync(path.join(output,name+'.png'));
      widthInvariant.push({width,height:892,...verticalFraming(buffer),cards:portraitFrames(buffer)});
     }
     for(const key of ['riverTop','riverBottom','riverHeight','dockTop'])assert(Math.max(...widthInvariant.map(r=>r[key]))-Math.min(...widthInvariant.map(r=>r[key]))<=2,'Width changed vertical '+key);
     checks.widthOnlyChangesSides=true;
     await a.page.setViewportSize(a.viewport);await pause(800);framingCaptured=true;checks.fullWindowCanvas=true;checks.portraitCardsOnResize=true;
    }
   }
   if(Date.now()>nextProgress){console.log(JSON.stringify({phase:a.state?.phase,tick:a.state?.tick,accepted:records.map(r=>accepted(r).length),spells:records.map(r=>[...r.spells]),welcomes:records.map(r=>r.welcomes)}));nextProgress=Date.now()+15000}
   await pause(300);
  }
  assert(records.every(r=>r.state?.phase==='ended'),'Browser match did not finish');
  assert(checks.insufficientEnergyCancel,'No low-energy deployment was exercised');
  assert(checks.reloadRecovery&&checks.socketRecovery,'Both page reload and in-page reconnect must be exercised');
  assert(a.state.match===b.state.match&&a.state.winner===b.state.winner&&a.state.reason===b.state.reason,'Browser results disagree');
  for(const record of records){assert(record.errors.length===0,'Browser errors: '+record.errors.join(';'));assert(record.kinds.size===6&&record.spells.size===2,'Missing troop/spell presentation');assert(new Set(accepted(record).map(m=>record.sent.find(s=>s.match===m.match&&s.action===m.action)?.card)).size===8,'Did not play all eight cards')}
  assert(JSON.stringify(a.state.towers.map(t=>t.hp))===JSON.stringify(b.state.towers.map(t=>t.hp)),'Final tower health differs');
  await shot(a,'result-desktop');await shot(b,'result-touch');
  const final={match:a.state.match,winner:a.state.winner,reason:a.state.reason,accepted:records.map(r=>accepted(r).length),towers:a.state.towers.map(t=>({id:t.id,hp:t.hp}))};
  await tap(a,360,555);await tap(b,195,460);await until(()=>records.every(r=>r.state?.match!==initial&&r.state?.phase==='waiting'));
  assert(a.state.match===b.state.match&&a.state.handVersion===0&&b.state.handVersion===0&&a.state.units.length===0&&a.state.energy===5,'Rematch retained old state');checks.rematch=true;
  // Receiving the new welcome precedes the next Unity/UI frame.
  await pause(800);await tap(a,360,632);await tap(b,195,514);await until(()=>records.every(r=>r.state?.phase==='playing'));await pause(500);await deploy(b,config.cards[0]);
  await shot(b,'new-match-touch');
  const proxyTotals=await readProxy();const proxy=proxyTotals?Object.fromEntries(Object.entries(proxyTotals).map(([key,value])=>[key,value-(proxyBefore?.[key]||0)])):null;
  result={passed:true,browser:browser.version(),origin,protocol:3,checks,final,newMatch:a.state.match,viewports:records.map(r=>r.viewport),framing,widthInvariant,troops:records.map(r=>[...r.kinds]),spells:records.map(r=>[...r.spells]),welcomes:records.map(r=>r.welcomes),proxy,errors:records.map(r=>r.errors)};
  if(proxy)assert(proxy.dropped>0&&proxy.duplicated>0,'Proxy did not exercise message impairment');
  const reportPath=path.join(output,'result.json');fs.writeFileSync(reportPath,JSON.stringify(result,null,2));console.log(JSON.stringify({passed:true,browser:result.browser,origin,checks,match:final.match,newMatch:result.newMatch,framingChecks:framing.length,report:reportPath,errors:result.errors}));return result;
 }catch(error){for(let i=0;i<records.length;i++)await shot(records[i],'failure-'+i).catch(()=>{});fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({error:String(error),checks,state:records.map(r=>({phase:r.state?.phase,hand:r.state?.hand,accepted:accepted(r).length,errors:r.errors}))},null,2));throw error}
 finally{await browser.close()}
}
module.exports={run};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
