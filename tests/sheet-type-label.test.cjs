const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function ctx(orders){
  const c={console,orders,parseFullDate:()=>null,SHEET_SKIP_CANCEL_TYPES:[]};vm.createContext(c);
  for(const n of ['removeVietnameseTones','sheetRowsToParcels','applySheetTypeLabels']){
    const m=source.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);vm.runInContext(m[0],c);}
  return c;
}
test('tab huỷ: giữ nguyên chữ Loại của Sheet',()=>{
  const c=ctx([]);
  const r=c.sheetRowsToParcels([{'Mã đơn':'A1','Loại':'Chỉ hoàn tiền','Ai huỷ':'Khách','Tên sản phẩm':'x'},{'Mã đơn':'B2','Loại':'Trả hàng + hoàn tiền','Ai huỷ':'Khách','Tên sản phẩm':'y'}],'huy',[]);
  assert.equal(r.parcels.map(p=>p.typeLabel).join('|'),'Chỉ hoàn tiền|Trả hàng + hoàn tiền');
});
test('ghi Loại vào đơn theo mã đơn, chỉ đổi khi khác',()=>{
  const c=ctx([{orderId:'a1'},{orderId:'B2',sheetType:'Chỉ hoàn tiền'},{orderId:'C3'}]);
  assert.equal(c.applySheetTypeLabels({A1:'Đang chờ huỷ',B2:'Chỉ hoàn tiền'}),1);
  assert.equal(c.orders[0].sheetType,'Đang chờ huỷ');assert.ok(!c.orders[2].sheetType);
  assert.equal(c.applySheetTypeLabels(null),0);
});
