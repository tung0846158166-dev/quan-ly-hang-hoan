const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8');
function extract(n){const s=html.search(new RegExp('    (?:async )?function '+n+'\\('));assert(s>=0,n);const r=html.slice(s);return r.slice(0,r.indexOf('\n    }')+6)}
function mk(extra){return vm.createContext({orders:[{orderId:'A'},{orderId:'B'}],archivedOrders:[],ARCHIVED_STORAGE_KEY:'arch',console,updateArchivedOrdersBadge(){},showAppToast(){},renderAll(){},recalcTotalsAndSummary(){},isEligibleForArchive:o=>o.orderId==='A',getNowDateTimeString:()=>'now',...extra})}
const load=c=>vm.runInContext(['saveArchivedOrders','loadArchivedOrders','checkAndAutoArchiveOrders'].map(extract).join('\n'),c);
test('localStorage đầy → lưu ra ổ đĩa rồi mới dời đơn khỏi bảng chính',async()=>{
  const disk={};let order=[];
  const c=mk({localStorage:{setItem(){throw Error('quota')},getItem(){return null}},saveOrders:async()=>{order.push('main');return{success:true}},
    window:{desktopDiskAPI:{auxSave:async(k,v)=>{order.push('disk');disk[k]=v;return{success:true}}}}});
  load(c);const n=await c.checkAndAutoArchiveOrders(false);
  assert.equal(n,1);assert.equal(c.orders.length,1);assert.equal(c.archivedOrders.length,1);
  assert.deepEqual(order,['disk','main']);assert.equal(JSON.parse(disk.arch)[0].orderId,'A');
});
test('ghi ổ đĩa lỗi → giữ đơn trên bảng chính',async()=>{
  const c=mk({localStorage:{setItem(){throw Error('quota')},getItem(){return null}},saveOrders:async()=>({success:true}),
    window:{desktopDiskAPI:{auxSave:async()=>({success:false})}}});
  load(c);assert.equal(await c.checkAndAutoArchiveOrders(false),0);assert.equal(c.orders.length,2);assert.equal(c.archivedOrders.length,0);
});
test('không có đĩa + localStorage đầy → giữ nguyên (như cũ)',async()=>{
  const c=mk({localStorage:{setItem(){throw Error('quota')},getItem(){return null}},saveOrders:async()=>({success:true})});
  load(c);assert.equal(await c.checkAndAutoArchiveOrders(false),0);assert.equal(c.orders.length,2);
});
test('đọc kho: ưu tiên bản trên đĩa nếu đầy đủ hơn',()=>{
  const c=mk({localStorage:{getItem:()=>JSON.stringify([{orderId:'X'}])},window:{desktopDiskAPI:{auxReadAllSync:()=>({data:{arch:JSON.stringify([{orderId:'X'},{orderId:'Y'}])}})}}});
  load(c);c.loadArchivedOrders();assert.equal(c.archivedOrders.length,2);
  const d=mk({localStorage:{getItem:()=>JSON.stringify([{orderId:'X'},{orderId:'Y'},{orderId:'Z'}])},window:{desktopDiskAPI:{auxReadAllSync:()=>({data:{arch:JSON.stringify([{orderId:'X'}])}})}}});
  load(d);d.loadArchivedOrders();assert.equal(d.archivedOrders.length,3);
});
