const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync(process.env.HTML_STORAGE_SOURCE||'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8');
function extract(n){const s=html.search(new RegExp('    (?:async )?function '+n+'\\('));assert(s>=0,n);const r=html.slice(s);return r.slice(0,r.indexOf('\n    }')+6)}
function ctx(extra={}){return vm.createContext({orders:[{orderId:'NEW'}],archivedOrders:[{orderId:'OLD'}],diskPersistDebounceTimer:null,photoMemoryCache:new Map(),STORAGE_KEY:'main',ARCHIVED_STORAGE_KEY:'archive',console,clearTimeout,setTimeout,savePhotoToDb:async()=>{},updateSafeStorageHeaderBadge(){},updateArchivedOrdersBadge(){},updateDiskNoticeStatus(){},showAppToast(){},renderAll(){},recalcTotalsAndSummary(){},...extra})}
(async()=>{
let safe=0,disk=0;const c=ctx({localStorage:{setItem(){throw Error('quota')}},saveOrdersToSafeStore:async()=>{safe++;return true},window:{desktopDiskAPI:{saveToDisk:async()=>{disk++;return {success:true}}}}});vm.runInContext(extract('flushPersistOrdersToDiskAndStorage'),c);const r=await c.flushPersistOrdersToDiskAndStorage();assert.equal(safe,0);assert.equal(disk,1);assert.equal(r.success,true);
let shown=0; c.window.desktopDiskAPI.saveToDisk=async()=>{throw Error('disk failed')};c.showAppToast=()=>shown++;assert.equal((await c.flushPersistOrdersToDiskAndStorage()).success,false);assert(shown);
let mainWrites=0;const a=ctx({isEligibleForArchive:()=>true,localStorage:{setItem(){throw Error('quota')},getItem(){return null}},saveOrders:async()=>{mainWrites++;return {success:true}}});vm.runInContext(extract('saveArchivedOrders')+'\n'+extract('checkAndAutoArchiveOrders'),a);await a.checkAndAutoArchiveOrders(false);assert.equal(a.orders.length,1);assert.equal(a.archivedOrders.length,1);assert.equal(mainWrites,0);
const storage=new Map();a.localStorage={setItem:(k,v)=>storage.set(k,v),getItem:k=>storage.get(k)};a.saveOrders=async()=>({success:false});await a.checkAndAutoArchiveOrders(false);assert.equal(a.orders.length,1);assert.equal(a.archivedOrders.length,2);assert.equal(JSON.parse(storage.get('archive')).length,2);
const a2=ctx({isEligibleForArchive:()=>true,localStorage:{setItem:(k,v)=>storage.set(k,v),getItem:k=>storage.get(k)},saveOrders:async()=>({success:++mainWrites>1})});mainWrites=0;vm.runInContext(extract('saveArchivedOrders')+'\n'+extract('checkAndAutoArchiveOrders'),a2);await a2.checkAndAutoArchiveOrders(false);assert.equal(a2.orders.length,1);assert.equal(a2.archivedOrders.length,1);
vm.runInContext(extract('saveOrders'),c);c.markUnsavedToFile=()=>{};assert.equal((await c.saveOrders(true)).success,false);
let readDone, confirms=0;const alerts=[];
class Reader { readAsText(value){readDone=this.onload({target:{result:JSON.stringify(value)}})} }
const b=ctx({FileReader:Reader,requireUnlocked:async()=>true,appConfirm:async()=>{confirms++;return true},alert:x=>alerts.push(x),saveOrders:async()=>({success:true}),addAuditLog(){},catalogItems:[],accountantStock:{},cellEditHistory:{},saveCatalog(){},saveAccountantStock(){},saveCellEditHistory(){}});
vm.runInContext(extract('restoreBackupJson'),b);
await b.restoreBackupJson({target:{files:[[{orderId:'LEGACY'}]],value:'x'}});await readDone;assert.equal(b.orders[0].orderId,'LEGACY');assert.equal(confirms,1);
await b.restoreBackupJson({target:{files:[{orders:[null]}],value:'x'}});await readDone;assert.equal(confirms,1);assert.equal(b.orders[0].orderId,'LEGACY');
console.log('HTML storage regressions passed');
})().catch(e=>{console.error(e);process.exitCode=1});
