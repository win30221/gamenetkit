// Exercises real WebGL, browser input and the unchanged game protocol through the solo proxy.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const root=path.resolve(__dirname,'../../..');
const local=path.join(root,'.local/tower-duel/browser/node_modules/playwright-core');
const core=process.env.PLAYWRIGHT_CORE||(fs.existsSync(local)?local:path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright-core'));
const {chromium}=require(core);const {PNG}=require(path.join(core,'lib/utilsBundle.js'));
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(value,message)=>{if(!value)throw Error(message)};
async function until(test,timeout=20000){const deadline=Date.now()+timeout;while(Date.now()<deadline){if(await test())return;await pause(80)}throw Error('Timed out waiting for solo browser evidence')}

function primaryButtons(buffer){
 const png=PNG.sync.read(buffer),bands=[];let band;
 for(let y=Math.floor(png.height*.18);y<png.height*.88;y++){
  let left=png.width,right=-1,count=0;
  for(let x=Math.floor(png.width*.08);x<png.width*.92;x++){
   const at=(y*png.width+x)*4,r=png.data[at],g=png.data[at+1],b=png.data[at+2];
   if((Math.abs(r-167)<5&&Math.abs(g-117)<5&&Math.abs(b-47)<5)||(Math.abs(r-193)<5&&Math.abs(g-139)<5&&Math.abs(b-62)<5)){left=Math.min(left,x);right=Math.max(right,x);count++}
  }
  if(count>png.width*.06){if(!band){band={left,right,top:y,bottom:y};bands.push(band)}band.left=Math.min(band.left,left);band.right=Math.max(band.right,right);band.bottom=y}else band=null;
 }
 return bands.filter(band=>band.bottom-band.top>12);
}

async function run(origin='http://127.0.0.1:18113',output=path.join(root,'artifacts/tower-duel/solo-web/local')){
 origin=origin.replace(/\/$/,'');fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,
  args:['--enable-unsafe-swiftshader','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows']});
 const records=[],checks={},results=[];
 const config=await(await fetch(origin+'/api/config')).json();
 const shot=(r,name)=>r.page.screenshot({path:path.join(output,name+'.png')});
 const accepted=r=>[...r.actions.values()].filter(action=>action.accepted&&action.match===r.state?.match);
 const tap=(r,x,y)=>r.touch?r.page.touchscreen.tap(x,y):r.page.mouse.click(x,y);
 async function make(touch){
  const viewport=touch?{width:390,height:844}:{width:720,height:1000};
  const context=await browser.newContext({viewport,hasTouch:touch,isMobile:touch,deviceScaleFactor:1});
  await context.addInitScript(()=>{const Native=window.WebSocket;window.__soloSockets=[];window.WebSocket=new Proxy(Native,{construct(Target,args){const ws=Reflect.construct(Target,args);window.__soloSockets.push(ws);return ws}})});
  const page=await context.newPage(),r={page,context,viewport,touch,welcomes:0,sent:[],actions:new Map(),errors:[],enemySeen:false,apiCalls:[]};records.push(r);
  page.on('pageerror',error=>r.errors.push(String(error)));
  page.on('console',message=>{if(/NullReferenceException|InvalidOperationException|RuntimeError|Tower Duel rejected message|Solo API request failed/.test(message.text()))r.errors.push(message.text())});
  page.on('response',async response=>{if(response.url().endsWith('/solo/rooms')&&response.request().method()==='POST'){r.apiCalls.push({status:response.status(),body:await response.json().catch(()=>null)})}});
  page.on('websocket',ws=>{
   ws.on('framesent',({payload})=>{try{r.sent.push(JSON.parse(String(payload)))}catch{}});
   ws.on('framereceived',({payload})=>{
    let s;try{s=JSON.parse(String(payload))}catch{return}
    if(s.type==='welcome'||s.type==='snapshot'){
     const prior=r.state;if(prior?.match===s.match&&prior.handVersion>s.handVersion){s.hand=prior.hand;s.handVersion=prior.handVersion;s.energy=prior.energy}
     r.state=s;if(s.type==='welcome')r.welcomes++;
     if(s.units.some(unit=>unit.seat!==s.seat))r.enemySeen=true;
    }else if(s.type==='action'){
     r.actions.set(s.match+'#'+s.action,s);
     if(r.state?.match===s.match&&s.handVersion>=r.state.handVersion){r.state.hand=s.hand;r.state.handVersion=s.handVersion;r.state.energy=s.energy}
    }else if(s.type==='error')r.errors.push(s.code);
   });
  });
  await page.goto(origin,{waitUntil:'domcontentloaded'});await loaded(r);return r;
 }
 async function loaded(r){await r.page.waitForFunction(()=>!!window.towerDuel,null,{timeout:90000});await pause(1800)}
 async function start(r,mode){
  let gold;
  await until(async()=>{gold=primaryButtons(await r.page.screenshot());return gold.length===1},25000);
  await shot(r,mode+'-lobby');
  const button=gold[0],x=(button.left+button.right)/2,y=(button.top+button.bottom)/2;
  await tap(r,mode==='practice'?x:r.viewport.width-x,y);
  await until(()=>r.state?.phase==='playing',25000);assert(r.state.seat===1,'Solo browser did not join the ordinary second seat');
  assert(r.apiCalls.at(-1)?.status===201&&r.apiCalls.at(-1)?.body?.mode===mode,'UI did not create the requested backend bot mode');
  // Wire arrival precedes Unity's next input/layout frame, especially on mobile.
  await pause(700);
 }
 function screen(r,x,z){
  const {width:w,height:h}=r.viewport,bottom=.19,top=.02,height=1-bottom-top,center=bottom+height/2;
  const sin=Math.sin(Math.PI/3),cos=Math.cos(Math.PI/3),far=11*sin+5.3*cos+.45,near=13.1*sin+.15,focus=(far-near)/2;
  const size=Math.max((far+near)/2/height,(15*sin+2*cos+.15-focus)/(2*(1-center))),sign=r.state.seat===0?1:-1;
  return{x:w/2+sign*x*h/(2*size),y:h*(1-center)+(focus-sign*sin*z)*h/(2*size)};
 }
 async function deploy(r){
  const card=r.state.hand.find(id=>{const card=config.cards.find(c=>c.id===id);return card.type==='troop'&&r.state.energy>=card.cost+.08});
  assert(card,'No affordable troop in the current hand');
  const index=r.state.hand.indexOf(card),{width,height}=r.viewport,panelScale=Math.sqrt(width/720*height/1000);
  const scale=panelScale*Math.min(.9,(width/panelScale-16)/600,height/panelScale*.18/234);
  const from={x:width/2+(-300+16+56+122*index)*scale,y:height*.82+121*scale};
  const aim=screen(r,-5.5,r.state.seat===0?-3.5:3.5),lift=r.touch?Math.max(48,Math.min(72,height*.065)):Math.max(20,Math.min(36,height*.035));
  const to={x:aim.x,y:aim.y+lift},before=accepted(r).length;
  if(r.touch){
   const cdp=await r.context.newCDPSession(r.page);
   await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{...from,id:1,radiusX:4,radiusY:4}]});
   for(let i=1;i<=10;i++){await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:from.x+(to.x-from.x)*i/10,y:from.y+(to.y-from.y)*i/10,id:1,radiusX:4,radiusY:4}]});await pause(30)}
   await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await cdp.detach();
  }else{await r.page.mouse.move(from.x,from.y);await r.page.mouse.down();await pause(80);await r.page.mouse.move(to.x,to.y,{steps:12});await pause(180);await r.page.mouse.up()}
  await until(()=>accepted(r).length>before);const ack=accepted(r).at(-1);
  assert(ack.handVersion>before&&ack.energy<10,'No authoritative hand/energy update');
  return ack;
 }
 async function leave(r,mode){
  const room=r.state.room,oldSent=r.sent.length;
  await tap(r,r.viewport.width-(r.touch?27:50),r.touch?24:36);
  await until(()=>r.sent.slice(oldSent).some(message=>message.type==='leave'));
  await pause(900);await shot(r,mode+'-returned-to-lobby');
  assert(primaryButtons(await r.page.screenshot()).length===1,'Leaving did not restore the solo mode menu');
  await until(()=>r.context.request.get(origin+'/solo/rooms/'+room).then(response=>response.status()===404));
 }
 try{
  assert(config.protocol===3&&config.cards.length===8,'Wrong authoritative game config');
  const desktop=await make(false);await start(desktop,'practice');
  const first=await deploy(desktop);await until(()=>desktop.enemySeen);await shot(desktop,'practice-battle');
  checks.realBackendPractice=true;
  const same={match:desktop.state.match,version:desktop.state.handVersion,welcomes:desktop.welcomes};
  await desktop.page.evaluate(()=>{for(const socket of window.__soloSockets)if(socket.readyState===WebSocket.OPEN)socket.close(1000,'Solo reconnect check')});
  await until(()=>desktop.welcomes>same.welcomes,25000);
  assert(desktop.state.match===same.match&&desktop.state.handVersion===same.version,'Socket recovery lost the original match or hand');checks.socketRecovery=true;
  const count=desktop.welcomes;await desktop.page.reload({waitUntil:'domcontentloaded'});await loaded(desktop);
  await until(()=>desktop.welcomes>count,25000);assert(desktop.state.match===same.match&&desktop.state.handVersion===same.version,'Page reload lost the saved solo room');checks.reloadRecovery=true;
  const room=desktop.state.room;
  const foreign=await browser.newContext();const forbidden=await foreign.request.delete(origin+'/solo/rooms/'+room,{headers:{Origin:origin}});assert(forbidden.status()===404,'Another browser could stop this test room');await foreign.close();checks.roomOwnership=true;
  results.push({mode:'practice',room,match:desktop.state.match,accepted:first.action,welcomes:desktop.welcomes,enemySeen:desktop.enemySeen});
  await leave(desktop,'practice');checks.leaveAndCleanup=true;
  const mobile=await make(true);await start(mobile,'sandbox');await deploy(mobile);await pause(2500);await shot(mobile,'sandbox-phone');
  assert(!mobile.enemySeen,'Sandbox opponent deployed a troop');checks.realBackendSandbox=true;checks.touchDeployment=true;
  results.push({mode:'sandbox',room:mobile.state.room,match:mobile.state.match,accepted:accepted(mobile).length,enemySeen:mobile.enemySeen});
  await leave(mobile,'sandbox');
  assert(records.every(r=>r.errors.length===0),'Browser errors: '+records.flatMap(r=>r.errors).join('; '));
  const result={passed:true,origin,browser:browser.version(),checks,results,errors:records.map(r=>r.errors)};
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));return result;
 }catch(error){
  for(let i=0;i<records.length;i++)await shot(records[i],'failure-'+i).catch(()=>{});
  fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({error:String(error),checks,records:records.map(r=>({state:r.state,errors:r.errors,api:r.apiCalls,sent:r.sent.slice(-5)}))},null,2));throw error;
 }finally{await browser.close()}
}
module.exports={run};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
