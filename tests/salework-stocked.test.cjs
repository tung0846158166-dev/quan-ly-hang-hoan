const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function ctx(orders){
  const c={console,orders,isLocked:false,cellLocks:{},saveOrders(){c.saved=(c.saved||0)+1},addAuditLog(){},recordCellEditHistory(){},getNowDateTimeString:()=>'21/11/2026 23:00:00'};
  vm.createContext(c);
  for(const n of ['removeVietnameseTones','extractCleanTrackingCode','isOrderReceived','applySaleworkStocked']){
    const m=source.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);vm.runInContext(m[0],c);}
  return c;
}
const row=o=>Object.assign({orderId:'A1',platform:'TikTok Shop',status:'Chưa về',items:[{product:'x'}]},o);
const sw=(id,fwd,ret,when,slip)=>({'Mã đơn hàng':id,'Mã vận đơn':fwd,'Mã trả hàng':ret||'','Ngày xác nhận hoàn hàng':when===undefined?'15:10:42 21/11/2026':when,'Mã phiếu nhập hoàn':slip===undefined?'IM1':slip});
test('khớp theo mã vận đơn -> Hàng đã trả, giờ về = ngày xác nhận của Salework',()=>{
  const c=ctx([row({forwardTrack:'862700000001'}),row({orderId:'B2',forwardTrack:'862700000002'})]);
  const r=c.applySaleworkStocked([sw('A1','862700000001','854170000001')]);
  assert.equal(r.marked,1);assert.equal(c.orders[0].status,'Hàng đã trả');assert.equal(c.orders[0].recvDate,'21/11/2026 15:10:42');
  assert.equal(c.orders[1].status,'Chưa về');
});
test('không tạo dòng mới, đơn chưa có trong bảng chỉ được đếm',()=>{
  const c=ctx([row({forwardTrack:'862700000001'})]);
  const r=c.applySaleworkStocked([sw('Z9','862799999999')]);
  assert.equal(r.notInApp,1);assert.equal(r.marked,0);assert.equal(c.orders.length,1);
});
test('đã nhận / không cần nhận / bị mất / ô khoá: không đổi, không ghi đè giờ',()=>{
  const c=ctx([row({forwardTrack:'862700000001',status:'Hàng đã trả',recvDate:'20/11/2026 09:00:00'}),row({orderId:'B2',forwardTrack:'862700000002',status:'Không cần nhận'}),row({orderId:'C3',forwardTrack:'862700000003',status:'Bị mất'}),row({orderId:'D4',forwardTrack:'862700000004'})]);
  c.cellLocks['3:status']=true;
  const r=c.applySaleworkStocked([sw('A1','862700000001'),sw('B2','862700000002'),sw('C3','862700000003'),sw('D4','862700000004')]);
  assert.equal(r.marked,0);assert.equal(r.already,1);assert.equal(r.skipped,3);assert.equal(c.orders[0].recvDate,'20/11/2026 09:00:00');
});
test('đơn nhiều kiện: chỉ kiện có mã khớp; không khớp mã nào thì bỏ qua (không đoán)',()=>{
  const c=ctx([row({returnTrack:'854170000001'}),row({returnTrack:'854170000002'})]);
  let r=c.applySaleworkStocked([sw('A1','862700000001','854170000002')]);
  assert.equal(r.marked,1);assert.equal(c.orders[0].status,'Chưa về');assert.equal(c.orders[1].status,'Hàng đã trả');
  const d=ctx([row({returnTrack:'854170000001'}),row({returnTrack:'854170000002'})]);
  r=d.applySaleworkStocked([sw('A1','862700000001','854170000009')]);
  assert.equal(r.ambiguous,1);assert.equal(r.marked,0);
});
test('dòng chưa mã nhưng là dòng duy nhất của đơn -> đánh dấu; dòng tiếp theo trống Mã đơn thuộc cùng đơn',()=>{
  const c=ctx([row({})]);
  const r=c.applySaleworkStocked([sw('A1','862700000001'),Object.assign(sw('','862700000001'),{'Mã đơn hàng':''})]);
  assert.equal(r.total,1);assert.equal(r.marked,1);assert.equal(c.orders[0].status,'Hàng đã trả');
});
test('không có dấu vết kho nhập (không ngày xác nhận, không mã phiếu) -> không đánh dấu',()=>{
  const c=ctx([row({forwardTrack:'862700000001'})]);
  const r=c.applySaleworkStocked([sw('A1','862700000001','', '', '')]);
  assert.equal(r.total,0);assert.equal(c.orders[0].status,'Chưa về');
});
test('chạy lần 2 không đổi gì thêm (idempotent)',()=>{
  const c=ctx([row({forwardTrack:'862700000001'})]);
  c.applySaleworkStocked([sw('A1','862700000001')]);const r=c.applySaleworkStocked([sw('A1','862700000001')]);
  assert.equal(r.marked,0);assert.equal(r.already,1);
});
