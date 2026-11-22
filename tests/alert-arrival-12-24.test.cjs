const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8').replace(/\r\n/g,'\n');
function fn(n){const m=html.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);return m[0]}
const pad=n=>String(n).padStart(2,'0');
const fmt=d=>`${pad(d.getDate())}/${pad(d.getMonth()+1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
const fmtSw=d=>`${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())} ${pad(d.getDate())}/${pad(d.getMonth()+1)}/${d.getFullYear()}`;
function mk(){
  const c=vm.createContext({Date,Number,String,isNaN,Boolean,isOrderReceived:o=>o.status==='Đã trả',isOrderUnneeded:()=>false,isOrderPending:o=>o.status!=='Đã trả'});
  const consts='const ARRIVED_WARN_HOURS = 12, ARRIVED_DANGER_HOURS = 24;';
  vm.runInContext(consts+['parseFullDate','getOrderArrivedAt','getOrderReturnElapsedInfo','isOrderOverdue','getDeadlineStatus','getOrderAlertLevel'].map(fn).join('\n'),c);return c;
}
const ago=h=>fmt(new Date(Date.now()-h*3600e3));
const agoSw=h=>fmtSw(new Date(Date.now()-h*3600e3));
test('có thời gian về: <12h an toàn, 12-24h báo vàng, >=24h báo đỏ',()=>{
  const c=mk();
  const o=h=>({status:'Chưa trả',returnDate:ago(1),returnedAt:agoSw(h)});
  assert.equal(c.getOrderAlertLevel(o(5)),'');
  assert.equal(c.getOrderAlertLevel(o(13)),'soon');
  assert.equal(c.getOrderAlertLevel(o(25)),'overdue');
  assert.equal(c.getOrderReturnElapsedInfo(o(13)).fromArrival,true);
});
test('Shopee: lấy thời gian về từ rawDetails.returnSuccessTime',()=>{
  const c=mk();
  assert.equal(c.getOrderAlertLevel({status:'Chưa trả',returnDate:ago(1),rawDetails:{returnSuccessTime:ago(30)}}),'overdue');
  assert.equal(c.getOrderAlertLevel({status:'Chưa trả',returnDate:ago(1),rawDetails:{returnSuccessTime:ago(14)}}),'soon');
});
test('đã nhận thì không báo; chưa có thời gian về giữ luật cũ 48h/72h từ ngày hủy',()=>{
  const c=mk();
  assert.equal(c.getOrderAlertLevel({status:'Đã trả',returnDate:ago(100),returnedAt:ago(50)}),'');
  assert.equal(c.getOrderAlertLevel({status:'Chưa trả',returnDate:ago(20)}),'');
  assert.equal(c.getOrderAlertLevel({status:'Chưa trả',returnDate:ago(50)}),'soon');
  assert.equal(c.getOrderAlertLevel({status:'Chưa trả',returnDate:ago(80)}),'overdue');
});
test('đơn đã về: hạn sàn cũ (deadline) không làm báo vàng sớm',()=>{
  const c=mk();
  const soon=new Date(Date.now()+3600e3);
  assert.equal(c.getOrderAlertLevel({status:'Chưa trả',returnDate:ago(1),returnedAt:ago(2),deadline:fmt(soon)}),'');
});
test('định dạng giờ-trước-ngày của Salework được đọc đúng',()=>{
  const c=mk();
  const d=c.getOrderArrivedAt({returnedAt:'10:15:22 21/11/2026'});
  assert.equal(d.getFullYear(),2026);assert.equal(d.getMonth(),10);assert.equal(d.getDate(),21);assert.equal(d.getHours(),10);
});
