// Real HTTP/WSS rooms and rendered buttons; no synthetic snapshots or client hooks.
const fs=require('node:fs');const path=require('node:path');
const root=path.resolve(__dirname,'../../..');
const core=process.env.PLAYWRIGHT_CORE||path.join(root,'.local/tower-duel/browser/node_modules/playwright-core');
const {chromium}=require(core);const {PNG}=require(path.join(core,'lib/utilsBundle.js'));
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(value,message)=>{if(!value)throw Error(message)};
async function until(predicate){const end=Date.now()+20000;while(Date.now()<end){if(predicate())return;await pause(50)}throw Error('Timed out waiting for room exit evidence')}

function goldButtons(buffer){
 const png=PNG.sync.read(buffer),rows=[];let band;
 for(let y=Math.floor(png.height*.3);y<png.height*.85;y++){
  let first=png.width,last=-1,count=0;
  for(let x=Math.floor(png.width*.1);x<png.width*.9;x++){
   const at=(y*png.width+x)*4,r=png.data[at],g=png.data[at+1],b=png.data[at+2];
   if((Math.abs(r-167)<4&&Math.abs(g-117)<4&&Math.abs(b-47)<4)||(Math.abs(r-193)<4&&Math.abs(g-139)<4&&Math.abs(b-62)<4)){first=Math.min(first,x);last=x;count++}
  }
  if(count>png.width*.07){
   if(!band){band={left:first,right:last,top:y,bottom:y};rows.push(band)}
   band.left=Math.min(band.left,first);band.right=Math.max(band.right,last);band.bottom=y;
  }else band=null;
 }
 return rows.filter(row=>row.bottom-row.top>12);
}
function waitingButtons(buffer){
 const gold=goldButtons(buffer);assert(gold.length===1,'Waiting room must show one ready button');
 const ready=gold[0],png=PNG.sync.read(buffer);let left=png.width,right=-1,count=0;
 for(let y=ready.top;y<=ready.bottom;y++)for(let x=ready.right+1;x<png.width*.9;x++){
  const at=(y*png.width+x)*4,r=png.data[at],g=png.data[at+1],b=png.data[at+2];
  if((Math.abs(r-36)<4&&Math.abs(g-59)<4&&Math.abs(b-84)<4)||(Math.abs(r-53)<4&&Math.abs(g-87)<4&&Math.abs(b-117)<4)){left=Math.min(left,x);right=Math.max(right,x);count++}
 }
 assert(right-left>40&&count>300,'Waiting-room exit button is hidden or clipped');
 return {ready:{x:(ready.left+ready.right)/2,y:(ready.top+ready.bottom)/2},exit:{x:(left+right)/2,y:(ready.top+ready.bottom)/2}};
}

async function run(origin='http://127.0.0.1:18137',output=path.join(root,'artifacts/tower-duel/room-exit/local')){
 fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--enable-unsafe-swiftshader','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows']});
 const records=[],checks={},rooms=[];
 async function make(touch){
  const viewport=touch?{width:390,height:844}:{width:720,height:1000};
  const context=await browser.newContext({viewport,hasTouch:touch,isMobile:touch,deviceScaleFactor:1});const page=await context.newPage();
  const record={context,page,touch,messages:[],errors:[],welcomes:0,closed:0};records.push(record);
  page.on('pageerror',error=>record.errors.push(String(error)));
  page.on('console',message=>{if(/NullReferenceException|InvalidOperationException|RuntimeError|Tower Duel rejected message/.test(message.text()))record.errors.push(message.text())});
  page.on('websocket',ws=>{
   ws.on('framesent',({payload})=>{try{record.messages.push(JSON.parse(String(payload)))}catch{}});
   ws.on('framereceived',({payload})=>{let message;try{message=JSON.parse(String(payload))}catch{return}
    if(message.type==='welcome'||message.type==='snapshot'){record.state=message;if(message.type==='welcome')record.welcomes++}
    else if(message.type==='error')record.errors.push(message.code);
   });ws.on('close',()=>record.closed++);
  });
  await page.goto(origin.replace(/\/$/,'')+'/?mode=multiplayer',{waitUntil:'domcontentloaded'});await loaded(record);return record;
 }
 async function loaded(record){await record.page.waitForFunction(()=>!!window.towerDuel,null,{timeout:60000});await pause(1500)}
 const tap=(record,point)=>record.touch?record.page.touchscreen.tap(point.x,point.y):record.page.mouse.click(point.x,point.y);
 const shot=(record,name)=>record.page.screenshot({path:path.join(output,name+'.png')});
 async function create(record){
  const before=record.welcomes;await tap(record,{x:360,y:524});await until(()=>record.welcomes>before);await pause(350);
  assert(record.state.phase==='waiting'&&record.state.seat===0,'Create did not enter a waiting room');rooms.push(record.state.room);
  return waitingButtons(await shot(record,'created-'+rooms.length));
 }
 async function join(record,code){
  const before=record.welcomes;await tap(record,{x:195,y:533});await record.page.locator('input').fill(code);await pause(100);
  await tap(record,{x:195,y:582});await until(()=>record.welcomes>before);await pause(350);
  assert(record.state.room===code&&record.state.seat===1,'Join did not enter the requested room');
  return waitingButtons(await shot(record,'joined-'+record.welcomes));
 }
 async function leave(record,point){
  const before=record.closed,match=record.state.match,count=record.messages.length;
  await tap(record,point);await until(()=>record.closed>before);await pause(250);
  assert(record.messages.slice(count).some(message=>message.type==='leave'&&message.match===match),'Exit did not send an explicit leave for the current room');
 }
 try{
  const host=await make(false),guest=await make(true);
  let hostButtons=await create(host);await leave(host,hostButtons.exit);await shot(host,'creator-returned-to-lobby');checks.creatorCanExit=true;
  const helloCount=()=>host.messages.filter(message=>message.type==='hello').length,beforeReload=helloCount();
  await host.page.reload({waitUntil:'domcontentloaded'});await loaded(host);await pause(800);
  assert(helloCount()===beforeReload,'Reload restored a room that was explicitly left');checks.leaveClearsSavedSeat=true;
  hostButtons=await create(host);assert(rooms[0]!==rooms[1],'Create reused the exited room');checks.creatorCanCreateAgain=true;
  let guestButtons=await join(guest,host.state.room);
  await until(()=>host.state.players[1].connected);await tap(guest,guestButtons.ready);await until(()=>host.state.players[1].ready);
  await leave(guest,guestButtons.exit);await until(()=>!host.state.players[1].connected&&!host.state.players[1].ready);
  assert(host.state.phase==='waiting','Leaving before the countdown ended the waiting room');checks.readyGuestCanExit=true;
  guestButtons=await join(guest,host.state.room);await until(()=>host.state.players[1].connected);checks.guestCanRejoin=true;
  await tap(host,hostButtons.ready);await tap(guest,guestButtons.ready);await until(()=>guest.state.phase==='countdown');
  await shot(guest,'countdown-exit-available');await leave(guest,guestButtons.exit);
  await until(()=>host.state.phase==='ended');assert(host.state.reason==='opponent_left','Countdown exit did not notify the opponent');checks.countdownCanExit=true;
  const back=goldButtons(await shot(host,'opponent-left-result')).at(-1);assert(back,'Result has no return-to-lobby button');
  await tap(host,{x:(back.left+back.right)/2,y:(back.top+back.bottom)/2});await pause(250);
  hostButtons=await create(host);await leave(host,hostButtons.exit);checks.resultCanReturnAndCreateAgain=true;
  assert(records.every(record=>record.errors.length===0),'Browser/runtime errors: '+JSON.stringify(records.map(record=>record.errors)));
  const result={passed:true,origin,browser:browser.version(),checks,rooms,errors:records.map(record=>record.errors)};
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));return result;
 }catch(error){
  for(let index=0;index<records.length;index++)await shot(records[index],'failure-'+index).catch(()=>{});
  fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({error:String(error),checks,rooms,states:records.map(record=>({phase:record.state?.phase,room:record.state?.room,errors:record.errors}))},null,2));throw error;
 }finally{await browser.close()}
}
module.exports={run,waitingButtons};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
