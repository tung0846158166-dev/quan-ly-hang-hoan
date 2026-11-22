const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const src=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
const t=fs.readFileSync(path.join(__dirname,'import-tiktok-189.test.cjs'),'utf8');
const mk=new Function('require','source','fs','path','vm','assert','return ('+t.split('\n')[2].replace(/^function context\(\)/,'function()')+')')(require,src,fs,path,vm,assert);
const sw=(o)=>Object.assign({'Mã đơn hàng':'587000000000000001','Trạng thái':'THẤT THU','Mã sản phẩm':'Stripes_Nau_M','Số lượng':'1','Tên sản phẩm trên sàn':'Áo','Mã vận đơn':'862700000001','Mã trả hàng':'','Lý do hoàn':'Change of mind','Loại đơn hoàn':'Trả hàng hoàn tiền','Ngày tạo đơn':'01:00:51 17/11/2026','Cập nhật lúc':'10:20:52 18/11/2026'},o);
async function run(rows){const c=mk();await c.ingestTikTokOnlyRecords(rows);return c;}
test('file Salework TikTok: Trạng thái kiện là Chờ khách gửi hàng / Đang về, KHÔNG phải lý do hoàn tiếng Anh',async()=>{
  const c=await run([sw({}),sw({'Mã đơn hàng':'587000000000000002','Mã vận đơn':'862700000002','Mã trả hàng':'VTPVN9055921766'})]);
  const by=Object.fromEntries(c.orders.map(o=>[o.orderId,o]));
  assert.equal(by['587000000000000001'].platformStatus,'Chờ khách gửi hàng');
  assert.equal(by['587000000000000002'].platformStatus,'Đang về');
  assert.match(by['587000000000000001'].note,/Đổi ý/);   // lý do vẫn nằm ở Ghi chú (đã dịch)
});
test('lý do "Wrong product sent" được dịch sang tiếng Việt',async()=>{
  const c=await run([sw({'Lý do hoàn':'Wrong product sent'})]);
  assert.match(c.orders[0].note,/sai sản phẩm/i);
});
test('file Salework: có "Ngày trả hàng thành công" => Đã về kho (chưa kiểm tra), kể cả khi chưa có mã trả hàng',async()=>{
  const c=await run([sw({'Mã đơn hàng':'587000000000000003','Mã vận đơn':'862700000003','Mã trả hàng':'VTPVN1','Ngày trả hàng thành công':'10:15:22 21/11/2026'}),sw({'Mã đơn hàng':'587000000000000004','Mã vận đơn':'862700000004','Mã trả hàng':'VTPVN2','Ngày trả hàng thành công':''})]);
  const by=Object.fromEntries(c.orders.map(o=>[o.orderId,o]));
  assert.equal(by['587000000000000003'].platformStatus,'Đã về');
  assert.equal(by['587000000000000004'].platformStatus,'Đang về');
});
test('Salework: thời gian về được lưu vào returnedAt; đơn Đang về thì không có',async()=>{
  const c=await run([sw({'Mã đơn hàng':'587000000000000005','Mã vận đơn':'862700000005','Mã trả hàng':'V5','Ngày trả hàng thành công':'10:15:22 21/11/2026'}),sw({'Mã đơn hàng':'587000000000000006','Mã vận đơn':'862700000006','Mã trả hàng':'V6'})]);
  const by=Object.fromEntries(c.orders.map(o=>[o.orderId,o]));
  assert.equal(by['587000000000000005'].returnedAt,'10:15:22 21/11/2026');
  assert.ok(!by['587000000000000006'].returnedAt);
});
