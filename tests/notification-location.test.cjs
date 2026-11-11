const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {webcrypto}=require('node:crypto');
const html=fs.readFileSync(path.join(__dirname,'../Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function codeBetween(start,end){return html.slice(html.indexOf(start),html.indexOf(end,html.indexOf(start)));}
function setup(){
 const queue=[],located=[],warnings=[];
 const context={crypto:webcrypto,saveAppNotifications(){},renderNotificationList(){},saveOrders(){},closeNotificationModal(){},setTimeout:fn=>queue.push(fn),showToast:(text,type)=>warnings.push({text,type}),document:{getElementById:()=>null},highlightAndFlashOrderRow:index=>located.push(vm.runInContext('orders',context)[index]?.orderId)};
 vm.createContext(context);
 vm.runInContext("let orders=[{orderId:'A',track:'TRACK-A'},{orderId:'B',track:'TRACK-B'},{orderId:'C',track:'TRACK-C'}];let appNotifications=[];let unreadNotifCount=0;let currentOrderTypeFilter='all',currentPreOrderFilter='all',currentTrackingFilter='all',currentCustomDateFrom='',currentCustomDateTo='',currentCustomTimeFrom='',currentCustomTimeTo='';",context);
 const start=html.includes('    function notificationOrderUid(')?'    function notificationOrderUid(':'    function addAppNotification(';
 vm.runInContext(codeBetween(start,'    function openNotificationModal(')+'\n'+codeBetween('    function locateOrderFromNotification(','    function clearAllNotifications('),context);
 return{context,located,warnings,run:source=>vm.runInContext(source,context),drain(){while(queue.length)queue.shift()();}};
}
function notify(s){s.run("addAppNotification({type:'phone_scan',code:'TRACK-B',orderIdx:1});");}
test('notification follows its order after inserting at the beginning',()=>{
 const s=setup();notify(s);s.run("orders.unshift({orderId:'NEW',track:'NEW'});locateOrderFromNotification(appNotifications[0].id);");s.drain();assert.deepEqual(s.located,['B']);
});
test('notification follows its order after deleting an earlier row',()=>{
 const s=setup();notify(s);s.run('orders.splice(0,1);locateOrderFromNotification(appNotifications[0].id);');s.drain();assert.deepEqual(s.located,['B']);
});
test('new notification identity survives persisted data and code edits',()=>{
 const s=setup();notify(s);s.run("orders=JSON.parse(JSON.stringify(orders));orders[1].track='CHANGED';orders[1].orderId='B-EDITED';orders.unshift({orderId:'REUSED',track:'TRACK-B'});locateOrderFromNotification(appNotifications[0].id);");s.drain();assert.deepEqual(s.located,['B-EDITED']);
});
test('deleted target must not fall back to a different order reusing its code',()=>{
 const s=setup();notify(s);s.run("orders.splice(1,1,{orderId:'OTHER',track:'TRACK-B'});locateOrderFromNotification(appNotifications[0].id);");s.drain();assert.deepEqual(s.located,[]);assert.equal(s.warnings.length,1);
});
test('legacy notification resolves unique code rather than its stale index',()=>{
 const s=setup();s.run("appNotifications=[{id:'legacy',code:'TRACK-B',orderIdx:1}];orders.unshift({orderId:'NEW',track:'NEW'});locateOrderFromNotification('legacy');");s.drain();assert.deepEqual(s.located,['B']);
});
test('ambiguous legacy code refuses to select the first matching order',()=>{
 const s=setup();s.run("appNotifications=[{id:'legacy',code:'TRACK-B',orderIdx:1}];orders.push({orderId:'D',track:'TRACK-B'});locateOrderFromNotification('legacy');");s.drain();assert.deepEqual(s.located,[]);assert.equal(s.warnings.length,1);
});
test('legacy notifications without a code never fall back to a numeric index',()=>{
 const s=setup();s.run("appNotifications=[{id:'legacy',code:'',orderIdx:1}];locateOrderFromNotification('legacy');");s.drain();assert.deepEqual(s.located,[]);assert.equal(s.warnings.length,1);
});
test('resolve again after the modal delay when order positions change meanwhile',()=>{
 const s=setup();notify(s);s.run("locateOrderFromNotification(appNotifications[0].id);orders.reverse();orders.unshift({orderId:'NEW'});");s.drain();assert.deepEqual(s.located,['B']);
});
test('legacy exact string codes retain leading zeros and ignore letter case',()=>{
 const s=setup();s.run("orders=[{orderId:'WRONG',track:'123'},{orderId:'RIGHT',track:'00123'}];appNotifications=[{id:'legacy',code:' 00123 ',orderIdx:0}];locateOrderFromNotification('legacy');");s.drain();assert.deepEqual(s.located,['RIGHT']);
});
test('legacy return and alternate tracking codes still locate the correct order',()=>{
 const s=setup();s.run("orders[1].returnTrack='RETURN-B';appNotifications=[{id:'legacy',code:' return-b ',orderIdx:0}];locateOrderFromNotification('legacy');");s.drain();assert.deepEqual(s.located,['B']);
});
test('duplicate stable identities refuse to highlight either order',()=>{
 const s=setup();notify(s);s.run("orders.push({...orders[1],orderId:'COPY'});locateOrderFromNotification(appNotifications[0].id);");s.drain();assert.deepEqual(s.located,[]);assert.equal(s.warnings.length,1);
});
test('notification IDs containing quotes are escaped in the rendered button',()=>{
 const s=setup();const nodes={notificationItemsContainer:{innerHTML:''},notifSearchInput:{value:''}};
 s.context.document={getElementById:id=>nodes[id]||null};
 s.context.escapeAttr=value=>String(value).replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/'/g,'&#39;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
 s.context.removeVietnameseTones=value=>String(value);
 s.run("let currentNotifFilter='all';appNotifications=[{id:'legacy\"quote',code:'TRACK-B',orderIdx:0,title:'T',detail:'',timeFormatted:''}];");
 vm.runInContext(codeBetween('    function renderNotificationList(','    function locateOrderFromNotification('),s.context);
 s.run('renderNotificationList();');
 assert.match(nodes.notificationItemsContainer.innerHTML,/locateOrderFromNotification\(&quot;legacy/);
 assert.doesNotMatch(nodes.notificationItemsContainer.innerHTML,/locateOrderFromNotification\(0\)/);
});
