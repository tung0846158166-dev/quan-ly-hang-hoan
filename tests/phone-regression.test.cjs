const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.join(__dirname, '..');
const mobile = fs.readFileSync(process.env.PHONE_MOBILE_FIXTURE || path.join(root, 'mobile_scanner.html'), 'utf8');
const desktop = fs.readFileSync(process.env.PHONE_DESKTOP_FIXTURE || path.join(root, 'Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'), 'utf8');
function fn(source, name) {
 const re = new RegExp('    (?:async )?function '+name+'\\('); const at = source.search(re); assert(at >= 0, name);
 const rest = source.slice(at + 4); const next = rest.slice(1).search(/\n    (?:async )?function |\n    \/\/ Parse OCR/);
 return next < 0 ? rest : rest.slice(0, next + 1);
}
const noop = () => {};
function ctx(extra={}) { return vm.createContext({console, Date, Math, Map, Set, JSON, Number, String, Array, clearTimeout:noop, setTimeout:()=>1, ...extra}); }
function clean(c) { vm.runInContext(fn(mobile, 'extractCleanTrackingCode'), c); }
test('failed offline upload retains snapshot, concurrent items and unrelated history', async () => {
 const queue=[{code:'TEST000001',time:1,photo:''}]; const history=[{code:'TEST000001',synced:false},{code:'TEST000002',synced:false}];
 const c=ctx({offlineQueue:queue,scanHistory:history,isSyncingOffline:false,room:'TEST',setTimeout:(f)=>{f();return 1},document:{getElementById:()=>null},sendPayloadToPc:()=>queue.push({code:'TEST000003'}),updateOfflineQueueUI:noop,sendPhotoPacket:async()=>{}, getNtfyTopic:()=> 'TEST',client:null,fetch:async()=>{if(queue.length===1)queue.push({code:'TEST000003'});throw Error('offline')},saveOfflineQueue:noop,saveScanHistory:noop,renderScanHistory:noop,alert:noop}); clean(c); vm.runInContext(fn(mobile,'syncOfflineQueueToPc'),c); await c.syncOfflineQueueToPc(); assert.equal(c.offlineQueue.length,2);assert.equal(c.scanHistory.length,2);
});
test('obsolete or mismatched ACK cannot confirm or delete pending work', () => {
 const c=ctx({pendingScans:new Map([['current',{code:'TEST000001'}]]),offlineQueue:[{code:'TEST000001',requestId:'current'}],scanHistory:[{code:'TEST000001',requestId:'current',status:'pending'}],triggerScanFeedback:noop,saveScanHistory:noop,saveOfflineQueue:noop,renderScanHistory:noop,updateOfflineQueueUI:noop});clean(c); vm.runInContext(fn(mobile,'handleScanAck'),c);
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:'old',outcome:'success'}); assert.equal(c.scanHistory[0].status,'pending'); assert.equal(c.offlineQueue.length,1);
 c.handleScanAck({type:'pc_ack',code:'TEST000002',requestId:'current',outcome:'success'}); assert.equal(c.offlineQueue.length,1);
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:'current',outcome:'error'}); assert.equal(c.offlineQueue.length,1);
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:'current',outcome:'success'}); assert.equal(c.offlineQueue.length,0);assert.equal(c.scanHistory.length,1);
});
test('unknown scan stays pending until PC receipt', () => {
 const feedback=[];const c=ctx({lastScannedRawCode:'',lastScannedRawTime:0,currentMobileMode:'return',scanHistory:[],knownSystemCodes:new Set(),captureCurrentVideoFrame:()=>null,triggerScanFeedback:(...a)=>feedback.push(a),saveScanHistory:noop,renderScanHistory:noop,addOfflineItem:noop,sendPayloadToPc:noop});clean(c);vm.runInContext(fn(mobile,'sendCodeToPc'),c);c.sendCodeToPc('TEST000099');assert.notEqual(feedback[0][0],'success');assert.equal(c.scanHistory[0].status,'pending');
});
function pc(save) {
 const acks=[],records=[];const c=ctx({orders:[{track:'TEST000001',status:'Chờ'}],pendingPhotosByCode:new Map(),lastScannedCode:'',lastScannedTime:0,phoneScanCount:0,activePhotoOrderIndex:null,document:{getElementById:()=>null,querySelector:()=>({value:'smart_auto'})},detectCarrierByTrackingCode:()=>'',getNowDateTimeString:()=> 'DATE',lookupShopeeDb:()=>null,savePhotoToDb:noop,saveOrders:save,addAuditLog:noop,appendPhoneLiveLog:noop,playScanDuplicateTone:noop,playScanSuccessTone:noop,playScanUnknownTone:noop,recordCodeToActiveSession:(...a)=>records.push(a),sendAckToPhone:(...a)=>acks.push(a),addAppNotification:noop,highlightAndFlashOrderRow:noop});clean(c);
 let identity=0;c.notificationOrderUid=o=>o._notificationOrderUid||(o._notificationOrderUid='TEST-UID-'+(++identity));
 const start=desktop.indexOf('    const phoneRequestReceipts =');const end=desktop.indexOf('    // Parse OCR text');vm.runInContext(start>=0 ? desktop.slice(start,end) : fn(desktop,'handleIncomingPhoneBarcode'),c);return {c,acks,records};
}
test('same request replay and inflight delivery repeat original receipt and record once',async()=>{
 let release;const p=pc(()=>new Promise(r=>release=r));const first=p.c.handleIncomingPhoneBarcode('TEST000001','same');const second=p.c.handleIncomingPhoneBarcode('TEST000001','same');await Promise.resolve();assert.equal(p.acks.length,0);release({success:true});await Promise.all([first,second]);await p.c.handleIncomingPhoneBarcode('TEST000001','same');assert.equal(p.records.length,1);assert.equal(p.acks.length,3);assert(p.acks.every(a=>a[3]==='success'));
});
test('failed persistence returns error and retries same intended receipt without duplicate record',async()=>{
 let succeeds=false;const p=pc(async()=>({success:succeeds}));await p.c.handleIncomingPhoneBarcode('TEST000001','retry');assert.equal(p.acks[0][3],'error');assert.equal(p.records.length,0);succeeds=true;await p.c.handleIncomingPhoneBarcode('TEST000001','retry');assert.equal(p.acks[1][3],'success');assert.equal(p.records.length,1);
});
test('older and legacy photos preserve newest evidence',()=>{
 const c=ctx({orders:[{track:'TEST000001'}],pendingPhotosByCode:new Map(),activePhotoOrderIndex:null,document:{getElementById:()=>null},savePhotoToDb:noop,saveOrders:noop,renderAll:noop,appendPhoneLiveLog:noop});clean(c);vm.runInContext(desktop.slice(desktop.indexOf('    async function attachPhotoToOrderByCode(') >= 0 ? desktop.indexOf('    async function attachPhotoToOrderByCode(') : desktop.indexOf('    function attachPhotoToOrderByCode('), desktop.indexOf('    function handleIncomingPhotoChunk(')),c);
 c.attachPhotoToOrderByCode('TEST000001','data:image/jpeg;base64,NEW',{captureId:'new',captureTime:20});c.attachPhotoToOrderByCode('TEST000001','data:image/jpeg;base64,OLD',{captureId:'old',captureTime:10});c.attachPhotoToOrderByCode('TEST000001','data:image/jpeg;base64,LEGACY');assert.equal(c.orders[0].photo,'data:image/jpeg;base64,NEW');
});
test('timeout and resend preserve the original request and payload photo',()=>{
 const timers=[];const sent=[];const c=ctx({offlineQueue:[{code:'TEST000001',time:10,photo:'ORIGINAL'}],scanHistory:[{code:'TEST000001',synced:false}],pendingScans:new Map(),scanSession:'session',scanSequence:0,room:'TEST',window:{},document:{getElementById:()=>null},setTimeout:f=>{timers.push(f);return timers.length},saveOfflineQueue:noop,saveScanHistory:noop,getNtfyTopic:()=> 'TEST',client:null,fetch:(_u,o)=>{sent.push(JSON.parse(o.body));return Promise.resolve({ok:true})}});clean(c);vm.runInContext(fn(mobile,'sendPayloadToPc'),c);
 c.sendPayloadToPc({type:'barcode',code:'TEST000001'},'TEST000001');const id=c.offlineQueue[0].requestId;timers[0]();assert(c.pendingScans.has(id));c.sendPayloadToPc({type:'sync_item',code:'TEST000001'},'TEST000001');assert.equal(sent[0].requestId,sent[1].requestId);assert.equal(c.offlineQueue[0].photo,'ORIGINAL');assert.equal(c.offlineQueue.length,1);
});
test('photo work needs both exact scan and capture receipts in either arrival order',()=>{
 for (const photoFirst of [false,true]) {
  const c=ctx({pendingScans:new Map([['req',{code:'TEST000001'}]]),offlineQueue:[{code:'TEST000001',requestId:'req',photo:'IMAGE',captureId:'cap'}],scanHistory:[{code:'TEST000001',requestId:'req',status:'pending',synced:false}],triggerScanFeedback:noop,saveScanHistory:noop,saveOfflineQueue:noop,renderScanHistory:noop,updateOfflineQueueUI:noop});clean(c);vm.runInContext(fn(mobile,'handleScanAck'),c);
  const scan={type:'pc_ack',code:'TEST000001',requestId:'req',outcome:'success',ackStage:'scan'};const photo={...scan,ackStage:'photo',photoSaved:true,captureId:'cap'};
  c.handleScanAck({...photo,captureId:'stale'});assert.equal(c.offlineQueue.length,1);
  c.handleScanAck(photoFirst?photo:scan);assert.equal(c.offlineQueue.length,1);assert.equal(c.scanHistory[0].synced,false);
  c.handleScanAck({...photo,outcome:'error',photoSaved:false});assert.equal(c.offlineQueue.length,1);
  c.handleScanAck(photoFirst?scan:photo);assert.equal(c.offlineQueue.length,0);assert.equal(c.scanHistory[0].synced,true);
 }
});
test('photo receipt is sent only after successful awaited persistence, failure stays retryable',async()=>{
 const acks=[];let success=false;const c=ctx({orders:[{track:'TEST000001'}],pendingPhotosByCode:new Map(),activePhotoOrderIndex:null,document:{getElementById:()=>null},savePhotoToDb:noop,saveOrders:async()=>({success}),sendAckToPhone:(...a)=>acks.push(a),renderAll:noop,appendPhoneLiveLog:noop});clean(c);
 vm.runInContext(desktop.slice(desktop.indexOf('    async function attachPhotoToOrderByCode('),desktop.indexOf('    function handleIncomingPhotoChunk(')),c);
 const cap={requestId:'req',captureId:'cap',captureTime:20,room:'TEST'};
 await c.attachPhotoToOrderByCode('TEST000001','data:image/jpeg;base64,NEW',cap);assert.equal(acks[0][3],'error');assert.equal(acks[0][7].photoSaved,false);
 success=true;await c.attachPhotoToOrderByCode('TEST000001','data:image/jpeg;base64,NEW',cap);assert.equal(acks[1][3],'success');assert.equal(acks[1][7].ackStage,'photo');assert.equal(acks[1][7].captureId,'cap');
});
test('failed scan retry cannot ACK a deleted or reverted target',async()=>{
 for(const change of ['orders=[];',"orders[0].status='Chưa trả';"]){
  let success=false;const p=pc(async()=>({success}));await p.c.handleIncomingPhoneBarcode('TEST000001','deleted-retry');vm.runInContext(change,p.c);success=true;await p.c.handleIncomingPhoneBarcode('TEST000001','deleted-retry');assert(p.acks.every(a=>a[3]!=='success'));assert.equal(p.records.length,0);
 }
});
test('photos retained in history receive a durable completion ACK',async()=>{
 const acks=[];const c=ctx({orders:[{track:'TEST000001',photo:'data:image/jpeg;base64,LOCAL'}],pendingPhotosByCode:new Map(),activePhotoOrderIndex:null,document:{getElementById:()=>null},savePhotoToDb:noop,saveOrders:async()=>({success:true}),sendAckToPhone:(...a)=>acks.push(a),renderAll:noop,appendPhoneLiveLog:noop});clean(c);
 vm.runInContext(desktop.slice(desktop.indexOf('    async function attachPhotoToOrderByCode('),desktop.indexOf('    function handleIncomingPhotoChunk(')),c);
 await c.attachPhotoToOrderByCode('TEST000001','data:image/jpeg;base64,NEW',{requestId:'req',captureId:'cap',captureTime:20,room:'TEST'});
 assert.equal(c.orders[0].photo,'data:image/jpeg;base64,LOCAL');assert.equal(c.orders[0].photoHistory.length,1);assert.equal(acks.length,1);assert.equal(acks[0][7].photoSaved,true);
});
test('smart label uses matching photo bytes and awaits the shared persistence receipt',async()=>{
 let release;const gate=new Promise(r=>release=r),p=pc(()=>gate);p.c.playBeepSound=noop;p.c.renderAll=noop;
 vm.runInContext(fn(desktop,'applyLabelInfoToOrder')+'\n'+fn(desktop,'handleIncomingSmartLabel'),p.c);
 vm.runInContext(desktop.slice(desktop.indexOf('    async function attachPhotoToOrderByCode('),desktop.indexOf('    function handleIncomingPhotoChunk(')),p.c);
 p.c.pendingPhotosByCode.set('TEST000001',{photo:'data:image/jpeg;base64,NEW',requestId:'smart',captureId:'cap',captureTime:20,room:'TEST'});
 const work=p.c.handleIncomingSmartLabel({track:'TEST000001',orderId:'ORDER',requestId:'smart',captureId:'cap',captureTime:20,room:'TEST',product:'Fixture'});await Promise.resolve();
 assert.equal(p.acks.length,0);assert.equal(typeof p.c.orders[0].photo,'string');release({success:true});await work;assert(p.acks.some(a=>a[3]==='success'));assert.equal(p.c.orders[0].photo,'data:image/jpeg;base64,NEW');assert.equal(p.c.orders[0].items[0].product,'Fixture');
 await p.c.handleIncomingSmartLabel({track:'TEST000001',requestId:'smart',room:'TEST'});assert.equal(p.records.length,1);
});
test('reloading phone history never promotes pending scans to success',()=>{
 const c=ctx({scanHistory:[],localStorage:{getItem:()=>JSON.stringify([{code:'TEST000001',status:'pending',synced:false}])},historyKey:()=> 'history',renderScanHistory:noop});vm.runInContext(fn(mobile,'loadScanHistory'),c);c.loadScanHistory();assert.equal(c.scanHistory[0].status,'pending');
});
test('standalone photo stays queued until its exact durable photo receipt',async()=>{
 const c=ctx({offlineQueue:[],scanHistory:[],pendingScans:new Map(),scanSequence:0,scanSession:'session',room:'ROOM',client:null,GLOBAL_TOPIC:'global',getNtfyTopic:()=> 'TEST',fetch:async()=>({ok:true}),saveOfflineQueue:noop,saveScanHistory:noop,renderScanHistory:noop,updateOfflineQueueUI:noop,triggerScanFeedback:noop,setTimeout:()=>1,clearTimeout:noop});clean(c);c.localStorage={getItem:()=>null};vm.runInContext(fn(mobile,'sendPhotoPacket')+'\n'+fn(mobile,'handleScanAck'),c);
 await c.sendPhotoPacket('data:image/jpeg;base64,PHOTO','TEST000001');assert.equal(c.offlineQueue.length,1);const item=c.offlineQueue[0];assert(item.requestId);assert.equal(item.photoOnly,true);assert.equal(c.scanHistory[0].status,'pending');
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:item.requestId,captureId:item.captureId,ackStage:'photo',photoSaved:true,outcome:'success'});assert.equal(c.offlineQueue.length,0);assert.equal(c.scanHistory[0].synced,true);
});
test('new-order requests also retain an exact pending receipt instead of early success',()=>{
 const c=ctx({offlineQueue:[{code:'TEST000001',photo:''}],scanHistory:[{code:'TEST000001',status:'new_order',synced:false}],pendingScans:new Map(),scanSession:'session',scanSequence:0,room:'TEST',window:{},document:{getElementById:()=>null},setTimeout:()=>1,saveOfflineQueue:noop,saveScanHistory:noop,renderScanHistory:noop,updateOfflineQueueUI:noop,triggerScanFeedback:noop,getNtfyTopic:()=> 'TEST',client:null,fetch:async()=>({ok:true})});clean(c);vm.runInContext(fn(mobile,'sendPayloadToPc')+'\n'+fn(mobile,'handleScanAck'),c);
 c.sendPayloadToPc({type:'new_order_scan',code:'TEST000001'},'TEST000001');const item=c.offlineQueue[0];assert(item.requestId);assert(c.pendingScans.has(item.requestId));assert.equal(item.kind,'new_order_scan');
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:item.requestId,outcome:'success',ackStage:'scan'});assert.equal(c.offlineQueue.length,0);assert.equal(c.scanHistory[0].status,'new_order');assert.equal(c.scanHistory[0].synced,true);
});
test('new-order inbox acknowledges verified storage and same-request retries only',()=>{
 let fails=true;const saved=new Map(),acks=[];const c=ctx({lastMsgTimestamp:0,phoneRoomId:'TEST',newOrdersInbox:[],localStorage:{setItem:(k,v)=>{if(fails)throw Error('quota');saved.set(k,v);},getItem:k=>saved.get(k)},updateNewOrdersBadge:noop,renderNewOrdersTable:noop,showAppToast:noop,autoDetectPlatformFromCode:()=> 'TikTok',detectCarrierByTrackingCode:()=> 'Carrier',getNowDateTimeString:()=> 'TIME',playScanSuccessTone:noop,appendPhoneLiveLog:noop,addAppNotification:noop,sendAckToPhone:(...a)=>acks.push(a)});clean(c);vm.runInContext(fn(desktop,'saveNewOrdersInbox')+'\n'+fn(desktop,'processIncomingPhonePayload'),c);
 const packet={type:'new_order_scan',code:'TEST000001',requestId:'new-req',captureId:'cap',room:'TEST',time:1,photo:'PHOTO'};
 c.processIncomingPhonePayload(packet);assert.equal(acks[0][3],'error');assert.equal(saved.size,0);fails=false;c.processIncomingPhonePayload(packet);assert.equal(acks[1][3],'success');assert.equal(c.newOrdersInbox.length,1);assert.equal(acks[1][7].photoSaved,true);assert.equal(JSON.parse(saved.get('shopee_new_orders_inbox_v1')).length,1);
});
test('scan receipt reports match before photo receipt and keeps photo retryable',()=>{
 const feedback=[];const c=ctx({pendingScans:new Map([['req',{code:'TEST000001'}]]),offlineQueue:[{code:'TEST000001',requestId:'req',photo:'IMAGE',captureId:'cap'}],scanHistory:[{code:'TEST000001',requestId:'req',status:'pending',synced:false}],triggerScanFeedback:(...a)=>feedback.push(a),saveScanHistory:noop,saveOfflineQueue:noop,renderScanHistory:noop,updateOfflineQueueUI:noop});clean(c);vm.runInContext(fn(mobile,'handleScanAck'),c);
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:'req',outcome:'success',ackStage:'scan',orderNum:12});
 assert.equal(feedback[0][0],'success');assert.equal(c.scanHistory[0].status,'success');assert.match(c.scanHistory[0].detail,/ảnh/i);assert.equal(c.scanHistory[0].synced,false);assert.equal(c.offlineQueue.length,1);
 c.scanHistory.unshift({code:'TEST000002',requestId:'next',status:'pending'});
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:'req',outcome:'success',ackStage:'photo',captureId:'cap',photoSaved:true});
 assert.equal(feedback.length,1);assert.equal(c.offlineQueue.length,0);assert.equal(c.scanHistory[1].synced,true);assert.match(c.scanHistory[1].detail,/Ảnh đã lưu/);
});
test('missing and duplicate scan results are immediate while their photos stay queued',()=>{
 for(const outcome of ['not_found','duplicate']){
 const feedback=[];const c=ctx({pendingScans:new Map([['req',{code:'TEST000001'}]]),offlineQueue:[{code:'TEST000001',requestId:'req',photo:'IMAGE',captureId:'cap'}],scanHistory:[{code:'TEST000001',requestId:'req',status:'pending',synced:false}],triggerScanFeedback:(...a)=>feedback.push(a),saveScanHistory:noop,saveOfflineQueue:noop,renderScanHistory:noop,updateOfflineQueueUI:noop});clean(c);vm.runInContext(fn(mobile,'handleScanAck'),c);
 c.handleScanAck({type:'pc_ack',code:'TEST000001',requestId:'req',outcome,ackStage:'scan'});assert.equal(feedback[0][0],outcome);assert.equal(c.scanHistory[0].status,outcome);assert.equal(c.offlineQueue.length,1);
 }
});
