const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const html = fs.readFileSync(process.env.PHONE_MOBILE_FIXTURE || require('node:path').join(__dirname, '../mobile_scanner.html'), 'utf8');
function fn(name) {
 const start=html.search(new RegExp('    (?:async )?function '+name+'\\('));
 assert(start>=0,name); const rest=html.slice(start+4);const end=rest.slice(1).search(/\n    (?:async )?function /);
 return end<0?rest:rest.slice(0,end+1);
}
const noop=()=>{};
function context(extra={}) { const c = vm.createContext({console,Date,Math,Map,Set,JSON,Number,String,Array,Promise,room:'ROOM',scanSession:'session',scanSequence:0,queueHydrating:false,queueStorageError:false,setTimeout:()=>1,clearTimeout:noop,document:{getElementById:()=>null},saveScanHistory:noop,saveOfflineQueue:async()=>true,renderScanHistory:noop,updateOfflineQueueUI:noop,triggerScanFeedback:noop,markPcReceipt:noop,...extra}); vm.runInContext(fn('sameMobileScanIntent'),c); return c; }
function ackContext() {
 const feedback=[];const c=context({pendingScans:new Map([['req',{code:'CODE0001',sentAt:Date.now()-20}]]),offlineQueue:[{code:'CODE0001',requestId:'req',captureId:'cap',photo:'IMAGE'}],scanHistory:[{code:'CODE0001',requestId:'req',status:'pending',synced:false}],triggerScanFeedback:(...a)=>feedback.push(a)});
 vm.runInContext(fn('extractCleanTrackingCode')+'\n'+fn('handleScanAck'),c); return {c,feedback};
}
test('strict room, code, request and capture protect pending work',()=>{
 const {c}=ackContext(); const packet={type:'pc_ack',room:'ROOM',requestId:'req',code:'CODE0001',outcome:'success',ackStage:'scan'};
 for (const change of [{room:undefined},{room:'OTHER'},{code:'CODE0002'},{requestId:'old'},{captureId:'old'}]) c.handleScanAck({...packet,...change});
 assert.equal(c.scanHistory[0].status,'pending');assert.equal(c.offlineQueue.length,1);
 c.handleScanAck(packet);assert.equal(c.scanHistory[0].status,'success');assert.equal(c.offlineQueue.length,1);assert(c.scanHistory[0].responseMs>=0);
});
test('ambiguous and not_found preserve unmatched photos without retry or fake saved receipt',()=>{
 for (const outcome of ['ambiguous','not_found']) {
  const {c,feedback}=ackContext(); c.handleScanAck({type:'pc_ack',room:'ROOM',requestId:'req',code:'CODE0001',outcome,ackStage:'scan'});
  assert.equal(c.scanHistory[0].status,outcome);assert.equal(c.offlineQueue[0].reviewRequired,true);assert.equal(c.offlineQueue[0].photo,'IMAGE');assert.notEqual(c.offlineQueue[0].photoConfirmed,true);assert.equal(c.pendingScans.size,0);assert(feedback.length);
 }
});
test('error ACK is immediate and keeps the exact request retryable',()=>{
 const {c,feedback}=ackContext();c.handleScanAck({type:'pc_ack',room:'ROOM',requestId:'req',code:'CODE0001',outcome:'error',ackStage:'scan'});
 assert.equal(c.scanHistory[0].status,'error');assert.equal(c.offlineQueue.length,1);assert.notEqual(c.offlineQueue[0].scanConfirmed,true);assert(c.pendingScans.has('req'));assert(feedback.length);
});
test('manifest and previous history never decide new scan outcomes',()=>{
 for (const status of ['success','duplicate','not_found']) {
  const feedback=[];const c=context({lastScannedRawCode:'',lastScannedRawTime:0,currentMobileMode:'return',offlineQueue:[],knownSystemCodes:new Set(['OTHER']),scanHistory:[{code:'CODE0001',status,synced:true}],captureCurrentVideoFrame:()=>null,triggerScanFeedback:(...a)=>feedback.push(a),addOfflineItem:noop,sendPayloadToPc:noop});
  vm.runInContext(fn('extractCleanTrackingCode')+'\n'+fn('sendCodeToPc'),c);c.sendCodeToPc('CODE0001');assert.equal(c.scanHistory[0].status,'pending');assert(feedback.every(a=>a[0]==='pending'));
 }
});
test('recovered pending membership preserves request and capture identities',()=>{
 const c=context({offlineQueue:[{id:'legacy',code:'CODE0001',photo:'IMAGE',time:2},{requestId:'review',code:'CODE0002',reviewRequired:true}],scanHistory:[],pendingScans:new Map()});
 vm.runInContext(fn('rebuildPendingScans'),c);c.rebuildPendingScans();assert(c.pendingScans.has('legacy'));assert(!c.pendingScans.has('review'));assert.equal(c.offlineQueue[0].captureId,'legacy');assert.equal(c.scanHistory[0].requestId,'legacy');
});
test('terminal unmatched items do not resend and photo-only bypasses scan',async()=>{
 const sent=[],photos=[];const c=context({isSyncingOffline:false,offlineQueue:[{code:'CODE0001',reviewRequired:true,photo:'IMAGE'},{code:'CODE0002',photoOnly:true,photo:'PHOTO'},{code:'CODE0003',scanConfirmed:true,photo:'IMAGE'}],sendPayloadToPc:p=>sent.push(p),sendPhotoPacket:async(...p)=>photos.push(p)});
 vm.runInContext(fn('syncOfflineQueueToPc'),c);await c.syncOfflineQueueToPc();assert.equal(sent.length,0);assert.equal(photos.length,2);
});
function durableContext(extra={}) {
 const records=new Map(),metadata=new Map(),writes=[];
 const db={transaction:(_store,mode)=> {
  const tx={objectStore:()=>({put:(value,key)=>{writes.push(JSON.parse(JSON.stringify(value)));queueMicrotask(()=>{records.set(key,JSON.parse(JSON.stringify(value)));tx.oncomplete();});},get:key=>{const req={};queueMicrotask(()=>{req.result=records.get(key);req.onsuccess();});return req;}})};
  return tx;
 }};
 const c=context({loadedQueueRoom:null,queueHydration:Promise.resolve(),queueWriteChain:Promise.resolve(),historyPhotoArchive:{},queueDatabase:null,offlineQueue:[],scanHistory:[],pendingScans:new Map(),openQueueDatabase:async()=>db,localStorage:{setItem:(k,v)=>metadata.set(k,v),getItem:k=>metadata.get(k)||null},getOfflineQueueStorageKey:()=> 'queue_ROOM',historyKey:()=> 'history_ROOM',autoSyncOfflineQueueIfPending:noop,...extra});
 vm.runInContext(fn('reportQueueStorageError')+'\n'+fn('rebuildPendingScans')+'\n'+fn('saveOfflineQueue')+'\n'+fn('saveScanHistory')+'\n'+fn('loadOfflineQueue'),c);
 return {c,records,metadata,writes};
}
test('serialized durable snapshots retain concurrent updates and localStorage has no image bytes',async()=>{
 const {c,writes,metadata,records}=durableContext();c.offlineQueue.push({code:'CODE0001',requestId:'one',photo:'data:image/jpeg;base64,LARGE',time:1});
 c.scanHistory.push({code:'CODE0001',photo:'data:image/jpeg;base64,LARGE',time:1});const first=c.saveOfflineQueue();
 c.offlineQueue.push({code:'CODE0002',requestId:'two',photo:'PHOTO2'});const second=c.saveOfflineQueue();
 await Promise.all([first,second]);await c.saveScanHistory();assert.equal(records.get('ROOM').queue.length,2);assert.equal(writes[0].queue.length,1);assert.equal(writes[1].queue.length,2);
 assert(![...metadata.values()].some(v=>/LARGE|PHOTO2|base64/.test(v)));assert.equal(Object.keys(records.get('ROOM').historyPhotos).length,0);
});
test('legacy queue and unresolved history photos migrate and reload reconstructs exact ACK membership',async()=>{
 const {c,records,metadata}=durableContext();metadata.set('queue_ROOM',JSON.stringify([{code:'CODE0001',requestId:'legacy',captureId:'capture',photo:'LEGACY',time:1}]));
 c.scanHistory=[{code:'CODE0002',time:2,photo:'HISTORY',status:'pending',synced:false}];await c.loadOfflineQueue();assert.equal(c.offlineQueue.length,2);assert.equal(records.get('ROOM').queue[0].photo,'LEGACY');assert(c.pendingScans.has('legacy'));assert(!metadata.get('queue_ROOM').includes('LEGACY'));
 c.loadedQueueRoom=null;c.pendingScans.clear();c.scanHistory=[];await c.loadOfflineQueue();assert.equal(c.offlineQueue.length,2);assert.equal(c.offlineQueue[0].captureId,'capture');assert(c.pendingScans.has('legacy'));
});
test('durable quota failure keeps legacy photo bytes and visibly reports unprotected storage',async()=>{
 const warning={style:{},textContent:''};const {c,metadata}=durableContext({document:{getElementById:()=>warning},openQueueDatabase:async()=>{throw Error('quota');}});
 metadata.set('history_ROOM','LEGACY_BYTES');c.offlineQueue=[{code:'CODE0001',photo:'PHOTO'}];c.scanHistory=[{code:'CODE0001',photo:'PHOTO'}];assert.equal(await c.saveScanHistory(),false);assert.equal(metadata.get('history_ROOM'),'LEGACY_BYTES');assert.equal(c.queueStorageError,true);assert.match(warning.textContent,/Không lưu được/);assert.equal(c.offlineQueue[0].photo,'PHOTO');
});
test('switching rooms stops old queue replay after in-flight photo',async()=>{let release;const gate=new Promise(r=>release=r),sent=[];const c=context({isSyncingOffline:false,offlineQueue:[{code:'CODE0001',photo:'IMAGE'},{code:'CODE0002'}],sendPayloadToPc:p=>sent.push({code:p.code,room:c.room}),sendPhotoPacket:()=>gate});vm.runInContext(fn('syncOfflineQueueToPc'),c);const work=c.syncOfflineQueueToPc();c.room='OTHER';release();await work;assert.equal(sent.length,1);assert.equal(sent[0].room,'ROOM');});
test('photo traffic publishes only to its room channel',async()=>{const published=[],capture={requestId:'r',captureId:'c',code:'CODE0001'};const c=context({offlineQueue:[capture],client:{connected:true,publish:t=>published.push(t)},GLOBAL_TOPIC:'GLOBAL',localStorage:{getItem:()=>null},getNtfyTopic:()=> 'ROOM',fetch:async()=>({ok:true})});vm.runInContext(fn('extractCleanTrackingCode')+'\n'+fn('sendPhotoPacket'),c);await c.sendPhotoPacket('data:image/jpeg;base64,QQ==','CODE0001',capture);assert.deepEqual(published,['agy_shopee_return_scanner_2026/ROOM']);});
test('duplicate inbox receipt is shown as duplicate rather than newly created',()=>{const {c}=ackContext();c.offlineQueue[0].kind='new_order_scan';c.handleScanAck({type:'pc_ack',room:'ROOM',requestId:'req',code:'CODE0001',outcome:'duplicate',ackStage:'scan'});assert.equal(c.scanHistory[0].status,'duplicate');});
