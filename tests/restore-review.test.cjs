const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const html=fs.readFileSync(path.join(__dirname,'../Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
const at=html.indexOf('    async function restoreBackupJson('),end=html.indexOf('\n    }',at),code=html.slice(at,end+6);
function fixture(){let done;const c={orders:[{orderId:'LIVE',photo:'OLD',photoId:'P',items:[]}],archivedOrders:[{orderId:'OLD-ARCHIVE'}],catalogItems:[{name:'OLD'}],accountantStock:{old:1},cellEditHistory:{old:[]},photoMemoryCache:new Map([['P','OLD']]),FileReader:class{readAsText(value){done=this.onload({target:{result:JSON.stringify(value)}});}},requireUnlocked:async()=>true,appConfirm:async()=>true,savePhotoToDb:async()=>true,saveOrders:async()=>({success:true}),renderAll(){},recalcTotalsAndSummary(){},addAuditLog(){},alert(){},saveCatalog(){},saveAccountantStock(){},saveCellEditHistory(){},updateArchivedOrdersBadge(){}};vm.createContext(c);vm.runInContext(code,c);return{c,async restore(data){await c.restoreBackupJson({target:{files:[data],value:'file'}});await done;}};}
test('archive restore failure does not publish imported main data or caches',async()=>{
 const f=fixture();f.c.saveArchivedOrders=()=>({success:false});await f.restore({orders:[{orderId:'RESTORED',photo:'NEW',photoId:'P',items:[]}],archivedOrders:[{orderId:'NEW-ARCHIVE'}],catalog:[{name:'NEW'}],photos:[{id:'P',data:'NEW'}]});
 assert.equal(f.c.orders[0].orderId,'LIVE');assert.equal(f.c.catalogItems[0].name,'OLD');assert.equal(f.c.photoMemoryCache.get('P'),'OLD');assert.equal(f.c.archivedOrders[0].orderId,'OLD-ARCHIVE');
});
test('inline backup photo wins over stale current cache with the same photo ID',async()=>{
 const f=fixture();await f.restore([{orderId:'RESTORED',photo:'NEW',photoId:'P',items:[]}]);assert.equal(f.c.orders[0].photo,'NEW');assert.equal(f.c.photoMemoryCache.get('P'),'NEW');
});
