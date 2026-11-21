const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8');
const a=html.indexOf('(function installDiskMirror() {'); assert(a>=0);
const code=html.slice(a,html.indexOf('\n    })();',a)+10);
const KEY='shopee_archived_orders_v1';
function run(localVal,diskVal,quota){
  const store=new Map(); if(localVal!=null) store.set(KEY,localVal);
  class Storage{ setItem(k,v){ if(String(v).length>quota) throw new Error('QuotaExceededError'); store.set(k,String(v)); } removeItem(k){store.delete(k)} getItem(k){return store.has(k)?store.get(k):null} }
  const localStorage=new Storage(); const saves=[]; const timers=[];
  const window={desktopDiskAPI:{auxReadAllSync:()=>({keys:[KEY],data:diskVal==null?{}:{[KEY]:diskVal}}),auxSave:async(k,v)=>{saves.push(v);return{success:true}}}};
  const ctx=vm.createContext({window,localStorage,Storage,console,clearTimeout:()=>{},setTimeout:(f)=>{timers.push(f);return timers.length}});
  vm.runInContext(code,ctx);
  return {store,saves,timers,window,localStorage};
}
const flushAll=async r=>{for(let i=0;i<5;i++){const t=r.timers.splice(0);t.forEach(f=>f());await Promise.resolve();}};
test('ổ cứng mới hơn nhưng quá lớn cho localStorage → không bị ghi đè bằng bản cũ',async()=>{
  const r=run('[1]'.padEnd(100,' '),'[1,2,3]'.padEnd(500,' '),200);
  await flushAll(r);
  assert.equal(r.window.__auxDiskOnly[KEY],true);
  assert.equal(r.saves.length,0);
  assert.equal(r.store.get(KEY).length,100);
});
test('ổ cứng mới hơn và vừa localStorage → localStorage được cập nhật theo ổ cứng',async()=>{
  const r=run('[1]','[1,2,3]',200); await flushAll(r);
  assert.equal(r.store.get(KEY),'[1,2,3]');
});
test('localStorage mới hơn ổ cứng → vẫn ghi lên ổ cứng như cũ',async()=>{
  const r=run('[1,2,3,4]','[1]',200); await flushAll(r);
  assert.deepEqual(r.saves,['[1,2,3,4]']);
});
test('đang chạy: localStorage đầy thì đánh dấu và không flush bản cũ',async()=>{
  const r=run('[1]','[1]',50); await flushAll(r); r.saves.length=0;
  assert.throws(()=>r.localStorage.setItem(KEY,'x'.repeat(100)));
  await flushAll(r);
  assert.equal(r.saves.length,1); assert.equal(r.saves[0].length,100);
  r.saves.length=0; r.window.__auxDiskOnly[KEY]=true; r.timers.push(()=>{}); await flushAll(r);
  assert.equal(r.saves.length,0);
});
