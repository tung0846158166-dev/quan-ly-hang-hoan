const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function ctx(orders){
  const c={console,orders,cellLocks:{}};vm.createContext(c);
  for(const n of ['parseFullDate','getOrderPlatform','shopeeClaimDeadline','applyShopeeClaimDeadlines','sheetParcelsToRows']){
    const m=source.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);vm.runInContext(m[0],c);}
  return c;
}
test('hạn = Về shop lúc + 24h (định dạng dd/mm/yyyy HH:mm), qua ngày/tháng đúng',()=>{
  const c=ctx([]);const f=c.shopeeClaimDeadline;
  assert.equal(f('16/11/2026 20:56'),'17/11/2026 20:56');assert.equal(f('30/11/2026 23:30'),'01/12/2026 23:30');
  assert.equal(f(''),'');assert.equal(f('không phải ngày'),'');
});
test('dòng nạp từ Sheet: dùng mốc 24h, không có Về shop lúc thì giữ hạn Sheet',()=>{
  const c=ctx([]);
  const rows=c.sheetParcelsToRows([{orderId:'A1',track:'',items:[],deliveredAt:'16/11/2026 20:56',deadline:'18/11/2026 15:04',type:'return',parcelStatus:'Đã về shop'},{orderId:'B2',items:[],deliveredAt:'',deadline:'19/11/2026 10:00',type:'return'}]);
  assert.equal(rows[0]['Người bán cần phản hồi trước'],'17/11/2026 20:56');assert.equal(rows[1]['Người bán cần phản hồi trước'],'19/11/2026 10:00');
});
test('dòng đã có: đổi nếu hạn trống hoặc còn là giá trị gốc của Sheet; không đổi nếu sửa tay / ô khoá / không phải Shopee',()=>{
  const c=ctx([{orderId:'A1',platform:'Shopee',deadline:'18/11/2026 15:04'},{orderId:'B2',platform:'Shopee',deadline:'tự sửa'},{orderId:'C3',platform:'Shopee',deadline:''},{orderId:'D4',platform:'Shopee',deadline:'18/11/2026 15:04'},{orderId:'E5',platform:'TikTok Shop',deadline:'18/11/2026 15:04'}]);
  c.cellLocks['3:deadline']=true;
  const P=id=>({orderId:id,deliveredAt:'16/11/2026 20:56',deadline:'18/11/2026 15:04'});
  assert.equal(c.applyShopeeClaimDeadlines(['A1','B2','C3','D4','E5'].map(P)),2);
  assert.deepEqual(c.orders.map(o=>o.deadline),['17/11/2026 20:56','tự sửa','17/11/2026 20:56','18/11/2026 15:04','18/11/2026 15:04']);
  assert.equal(c.applyShopeeClaimDeadlines(['A1','C3'].map(P)),0);   // chạy lại: không đổi gì
});
