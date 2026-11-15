// Select the three-mode menu with rendered mouse/touch input and real backend rooms.
const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../../..');
const core=process.env.PLAYWRIGHT_CORE||path.join(root,'.local/tower-duel/browser/node_modules/playwright-core');
const {chromium}=require(core),{PNG}=require(path.join(core,'lib/utilsBundle.js'));
const {primaryButtons}=require('./Test-Web.cjs');
const {waitingButtons}=require('../scripts/Test-Room-Exit.cjs');
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(value,message)=>{if(!value)throw Error(message)};
async function until(test){const end=Date.now()+25000;while(Date.now()<end){if(await test())return;await pause(100)}throw Error('Timed out waiting for mode selection')}
function blueButtons(buffer){
 const png=PNG.sync.read(buffer),bands=[];let band;
 for(let y=Math.floor(png.height*.15);y<png.height*.88;y++){
  let left=png.width,right=-1,count=0;
  for(let x=Math.floor(png.width*.05);x<png.width*.95;x++){
   const at=(y*png.width+x)*4,r=png.data[at],g=png.data[at+1],b=png.data[at+2];
   if((Math.abs(r-36)<4&&Math.abs(g-59)<4&&Math.abs(b-84)<4)||(Math.abs(r-53)<4&&Math.abs(g-87)<4&&Math.abs(b-117)<4)){left=Math.min(left,x);right=Math.max(right,x);count++}
  }
  if(count>png.width*.008){if(!band){band={left,right,top:y,bottom:y};bands.push(band)}band.left=Math.min(band.left,left);band.right=Math.max(band.right,right);band.bottom=y}else band=null;
 }
 return bands.filter(band=>band.bottom-band.top>7);
}
async function run(origin='http://127.0.0.1:18141',output=path.join(root,'artifacts/tower-duel/merged/menu')){
 origin=origin.replace(/\/$/,'');fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--enable-unsafe-swiftshader','--disable-background-timer-throttling','--disable-renderer-backgrounding','--disable-backgrounding-occluded-windows']});
 const results=[];
 try{
  for(const touch of [false,true]){
   const viewport=touch?{width:390,height:844}:{width:720,height:1000};
   const context=await browser.newContext({viewport,hasTouch:touch,isMobile:touch,deviceScaleFactor:1}),page=await context.newPage();
   const errors=[],sent=[],created=[];let state;
   page.on('pageerror',error=>errors.push(String(error)));
   page.on('console',message=>{if(/NullReferenceException|InvalidOperationException|RuntimeError|Tower Duel rejected message/.test(message.text()))errors.push(message.text())});
   page.on('response',async response=>{if(response.url().endsWith('/solo/rooms')&&response.request().method()==='POST')created.push({status:response.status(),body:await response.json()})});
   page.on('websocket',ws=>{
    assert(ws.url()===origin.replace(/^http/,'ws')+'/ws','Game connection must use the primary origin');
    ws.on('framesent',({payload})=>{try{sent.push(JSON.parse(String(payload)))}catch{}});
    ws.on('framereceived',({payload})=>{try{const message=JSON.parse(String(payload));if(message.type==='welcome'||message.type==='snapshot')state=message}catch{}});
   });
   const click=async point=>{await page.mouse.move(0,0);if(touch)await page.touchscreen.tap(point.x,point.y);else await page.mouse.click(point.x,point.y);await pause(350)};
   const center=band=>({x:(band.left+band.right)/2,y:(band.top+band.bottom)/2});
   async function menu(){let gold;await until(async()=>{const image=await page.screenshot();gold=primaryButtons(image);return gold.length===1&&gold[0].right-gold[0].left<viewport.width*.45&&blueButtons(image).some(b=>b.top>gold[0].bottom&&b.right-b.left>viewport.width*.4)});return gold[0]}
   async function multiplayer(){const gold=await menu(),blue=blueButtons(await page.screenshot()).find(b=>b.top>gold.bottom&&b.right-b.left>viewport.width*.4);assert(blue,'Three-mode menu must show a full-width multiplayer button');await click(center(blue));await until(async()=>{const gold=primaryButtons(await page.screenshot());return gold.length===1&&gold[0].right-gold[0].left>viewport.width*.4})}
   const label=touch?'phone':'desktop';
   await page.goto(origin,{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>!!window.towerDuel,null,{timeout:90000});await pause(1800);
   await menu();await page.screenshot({path:path.join(output,label+'-menu.png')});
   await multiplayer();await page.screenshot({path:path.join(output,label+'-multiplayer.png')});
   const gold=primaryButtons(await page.screenshot())[0],back=blueButtons(await page.screenshot()).find(b=>b.bottom<gold.top);
   assert(back,'Multiplayer form must expose a mode-selection back button');await click({x:gold.left+Math.min(20,viewport.width*.04),y:center(back).y});await menu();
   await multiplayer();await click(center(primaryButtons(await page.screenshot())[0]));await until(()=>state?.phase==='waiting');await pause(600);
   assert(created.length===0,'Multiplayer room must not create a solo bot');
   const friend=state.room;await click(waitingButtons(await page.screenshot()).exit);await until(()=>sent.some(m=>m.type==='leave'));await menu();
   const practice=await menu();await click({x:viewport.width-center(practice).x,y:center(practice).y});
   await until(()=>state?.phase==='playing'&&state.room!==friend);await until(()=>created.length===1);
   assert(created[0].status===201&&created[0].body.mode==='sandbox','Returning from multiplayer must allow sandbox selection');
   await pause(600);await click({x:viewport.width-(touch?27:50),y:touch?24:36});await menu();
   await page.screenshot({path:path.join(output,label+'-returned.png')});
   assert(errors.length===0,errors.join('; '));results.push({touch,friendRoom:friend,sandboxRoom:created[0].body.room,backNavigation:true,leaveReturnsToModes:true,errors});
   await context.close();
  }
  const result={passed:true,origin,results};fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));return result;
 }catch(error){for(const context of browser.contexts())for(const page of context.pages())await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});throw error}
 finally{await browser.close()}
}
module.exports={run,blueButtons};
if(require.main===module)run(process.argv[2],process.argv[3]).catch(error=>{console.error(error);process.exitCode=1});
