import assert from 'node:assert/strict';

// Run against a host whose web directory has no sibling client/Builds/WebGL.
const origin=process.argv[process.argv.indexOf('--server')+1];
const entry=await fetch(origin+'/',{redirect:'manual'});
assert.equal(entry.status,503);assert.equal(entry.headers.get('location'),null);
for(const path of ['/','/unity/','/unity/index.html']){
  const reply=await fetch(origin+path,{redirect:'manual'});
  assert.equal(reply.status,503,path);assert.equal(reply.headers.get('cache-control'),'no-store');
  assert.match(reply.headers.get('content-type'),/text\/html/);
  const html=await reply.text();
  assert.match(html,/Build-Unity\.ps1 -Project RumbleboundExample -Target Web/);
  assert.match(html,/href="\/"/);assert.match(html,/href="\/diagnostics\/"/);
  assert.doesNotMatch(html,/<canvas/);
}
assert.equal((await fetch(origin+'/unity/Build/missing.wasm')).status,404);
assert.equal((await fetch(origin+'/Build/missing.wasm')).status,404);
assert.equal((await fetch(origin+'/unity/%2e%2e%2fProjectSettings%2fProjectSettings.asset')).status,404);
console.log('RUMBLEBOUND_MISSING_UNITY PASS root player entry, actionable 503 build instructions, explicit diagnostics link and missing asset/traversal 404');
