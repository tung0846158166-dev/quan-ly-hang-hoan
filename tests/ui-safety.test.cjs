const {test}=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {webcrypto}=require('node:crypto');
const html=fs.readFileSync(path.join(__dirname,'../Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function fn(name){const match=new RegExp('^    (?:async )?function '+name+'\\(','m').exec(html);if(!match)return '';const end=html.indexOf('\n    }',match.index);return html.slice(match.index,end+6);}
function helpers(){return fn('notificationOrderUid')+'\n'+fn('findAsyncOrderTarget');}
function context(extra={}){const c={crypto:webcrypto,console,saveOrders:async()=>({success:true}),hasLockedCellsInOrder:()=>false,photoMemoryCache:new Map(),document:{getElementById:()=>null},alert(){},showToast(){},renderAll(){},playBeepSound(){},addAuditLog(){},...extra};vm.createContext(c);vm.runInContext("let isLocked=false;let isOcrBusy=false;let orders=[{testId:'A',_notificationOrderUid:'A',orderId:'',track:'',photo:'PHOTO-A',photoId:'P-A',items:[{product:'',color:'',size:'',qty:1}]}];const localPhotoDecodeJobs=new Map();let localPhotoDecodeSequence=0;let currentZoomOrderIndex=0,currentZoomOrderUid='A',currentZoomPhotoId='P-A';",c);vm.runInContext(helpers(),c);return c;}
test('OCR follows stable identity after row insertion and preserves other rows',async()=>{
 let release;const gate=new Promise(r=>release=r);const c=context({Tesseract:{recognize:()=>gate},parseLabelTextWithCatalog:()=>({orderId:'ORDER-A',product:'Product A'}),detectCarrierByTrackingCode:()=>''});vm.runInContext(fn('triggerPcOcrForOrder'),c);
 const work=vm.runInContext('triggerPcOcrForOrder(0,true)',c);vm.runInContext("orders.unshift({testId:'NEW',orderId:'',track:'',items:[{product:'',color:'',size:'',qty:1}]});",c);release({data:{text:'Fixture A'}});await work;
 assert.equal(vm.runInContext('orders[0].orderId',c),'');assert.equal(vm.runInContext('orders[1].orderId',c),'ORDER-A');
});
test('OCR does not apply after target deletion or table lock',async()=>{
 for(const mutation of ["orders.splice(0,1);orders.push({testId:'OTHER',orderId:'',items:[{product:'',qty:1}]});",'isLocked=true;']){
  let release;const gate=new Promise(r=>release=r);const c=context({Tesseract:{recognize:()=>gate},parseLabelTextWithCatalog:()=>({orderId:'ORDER-A'}),detectCarrierByTrackingCode:()=>''});vm.runInContext(fn('triggerPcOcrForOrder'),c);const work=vm.runInContext('triggerPcOcrForOrder(0,true)',c);vm.runInContext(mutation,c);release({data:{text:'A'}});await work;assert.equal(vm.runInContext('orders[0].orderId',c),'');
 }
});
test('batch OCR preserves each selected order when an earlier read inserts rows',async()=>{
 const visited=[];let c; c=context({requireUnlocked:async()=>true,triggerPcOcrForOrder:async index=>{visited.push(vm.runInContext('orders',c)[index].testId);if(visited.length===1)vm.runInContext("orders.unshift({testId:'NEW',photo:'',items:[]});",c);}});
 vm.runInContext("orders.push({testId:'B',_notificationOrderUid:'B',photo:'PHOTO-B',orderId:'',items:[{product:''}]});"+fn('triggerPcOcrForMissingOrders'),c);await vm.runInContext('triggerPcOcrForMissingOrders()',c);assert.deepEqual(visited,['A','B']);
});
function photoContext(){let reader,image;const c=context({FileReader:class{constructor(){reader=this;}readAsDataURL(){}},Image:class{constructor(){image=this;this.width=100;this.height=100;}set src(v){}},savePhotoToDb:async()=>true,document:{getElementById:()=>null,createElement:()=>({getContext:()=>({drawImage(){}}),toDataURL:()=> 'NEW-PHOTO-A'})}});vm.runInContext(fn('compressAndSaveImage'),c);return{c,read(){reader.onload({target:{result:'fixture'}});},decode(){return image.onload();}};}
test('photo decode follows stable identity after insertion',async()=>{
 const {c,read,decode}=photoContext();vm.runInContext('compressAndSaveImage({},0)',c);read();vm.runInContext("orders.unshift({testId:'NEW',photo:'',track:'NEW'});",c);await decode();assert.equal(vm.runInContext('orders[0].photo',c),'');assert.equal(vm.runInContext('orders[1].photo',c),'NEW-PHOTO-A');
});
test('photo decode cannot attach to a replacement row or a locked target',async()=>{
 for(const mutation of ["orders.splice(0,1,{testId:'REPLACEMENT',photo:''});",'isLocked=true;']){
  const {c,read,decode}=photoContext();vm.runInContext('compressAndSaveImage({},0)',c);read();vm.runInContext(mutation,c);await decode();assert.notEqual(vm.runInContext('orders[0].photo',c),'NEW-PHOTO-A');
 }
});
test('delete photo checks lock and preserves photo when persistence fails',async()=>{
 for(const locked of [true,false]){
  const deleted=[];const c=context({deletePhotoFromDb:async id=>{deleted.push(id);return true;},closeZoomModal(){},saveOrders:async()=>({success:false})});vm.runInContext(fn('removeCurrentZoomPhoto'),c);vm.runInContext('isLocked='+locked,c);await vm.runInContext('removeCurrentZoomPhoto()',c);assert.deepEqual(deleted,[]);assert.equal(vm.runInContext('orders[0].photo',c),'PHOTO-A');
 }
});
test('delete photo resolves the order viewed before inserting another row',async()=>{
 const deleted=[];const c=context({deletePhotoFromDb:async id=>{deleted.push(id);return true;},closeZoomModal(){}});vm.runInContext(fn('removeCurrentZoomPhoto'),c);vm.runInContext("orders.unshift({testId:'NEW',photo:'KEEP',photoId:'P-NEW'});",c);await vm.runInContext('removeCurrentZoomPhoto()',c);assert.equal(vm.runInContext('orders[0].photo',c),'KEEP');assert.equal(vm.runInContext('orders[1].photo',c),'');assert.deepEqual(deleted,['P-A']);
});
test('notification details are escaped rather than creating executable elements',()=>{
 const container={innerHTML:''};const c=context({document:{getElementById:id=>id==='notificationItemsContainer'?container:id==='notifSearchInput'?{value:''}:null},removeVietnameseTones:v=>String(v)});vm.runInContext("let currentNotifFilter='all';let appNotifications=[{id:'test',type:'excel_update',title:'Fixture',detail:'<img src=x onerror=evil()>',code:'',timeFormatted:''}];",c);vm.runInContext(fn('escapeAttr')+'\n'+fn('renderNotificationList'),c);vm.runInContext('renderNotificationList()',c);assert.doesNotMatch(container.innerHTML,/<img src=x/);assert.match(container.innerHTML,/&lt;img/);
});
test('toast renders untrusted text without HTML interpretation',()=>{
 const payload='<img src=x onerror=evil()>';let toast;const classList={remove(){},add(){}};const c=context({requestAnimationFrame:cb=>cb(),setTimeout(){},document:{getElementById:()=>({appendChild:node=>{toast=node;}}),createElement:()=>({classList})}});vm.runInContext(fn('showAppToast'),c);c.payload=payload;vm.runInContext('showAppToast(payload)',c);assert.equal(toast.textContent,payload);assert.equal(toast.innerHTML,undefined);
});
test('photo targets respect photo locks without blocking unrelated price/status locks',()=>{
 const c=context();
 vm.runInContext("let cellLocks={'0:price':true,'0:status':true};",c);
 assert.equal(vm.runInContext("findAsyncOrderTarget('A','photo').order.testId",c),'A');
 vm.runInContext("cellLocks['0:photo']=true;",c);
 assert.equal(vm.runInContext("findAsyncOrderTarget('A','photo')",c),null);
 vm.runInContext("delete cellLocks['0:photo'];cellLocks['0:track']=true;",c);
 assert.equal(vm.runInContext("findAsyncOrderTarget('A')",c),null);
});
