const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8').replace(/\r\n/g,'\n');
function fn(n){const m=html.match(new RegExp('^    (?:async )?function '+n+'\\([^]*?^    }','m'));assert(m,n);return m[0]}
function mk(order){
  const calls=[];
  const c=vm.createContext({Date,Number,Boolean,isNaN,document:{getElementById:()=>null,querySelector:()=>null},phoneScanCount:0,
    orders:[order],extractCleanTrackingCode:s=>String(s||'').trim().toUpperCase(),attachPhotoToOrderByCode(){},detectCarrierByTrackingCode:()=>'SPX Express',
    phoneOrderMatches:()=>[{index:0,order}],addAuditLog:(...a)=>calls.push(['audit']),appendPhoneLiveLog:()=>calls.push(['live']),
    playScanDuplicateTone:()=>calls.push(['tone']),addAppNotification:()=>calls.push(['notif']),highlightAndFlashOrderRow:()=>calls.push(['flash']),
    saveOrders(){},calls});
  vm.runInContext(['parseFullDate','isReplayOfOwnReceipt','applyIncomingPhoneBarcode'].map(fn).join('\n'),c);
  return c;
}
function run(order,capture){
  const c=mk(order);const acks=[],rec=[];
  c.applyIncomingPhoneBarcode('261115C5U7CCRA','r1','','room',capture,(...a)=>acks.push(a),(...a)=>rec.push(a),()=>{},f=>f());
  return {calls:c.calls,acks,rec};
}
const returned=()=>({status:'Hàng đã trả',recvDate:'22/11/2026 14:33:02'});
const ms=s=>{const m=s.match(/(\d+)\/(\d+)\/(\d+) (\d+):(\d+):(\d+)/);return new Date(+m[3],+m[2]-1,+m[1],+m[4],+m[5],+m[6]).getTime()};
test('quét trùng thật (quét sau lúc đã nhận): vẫn báo trùng đầy đủ',()=>{
  const r=run(returned(),{__replayAt:ms('22/11/2026 20:00:00')});
  assert.ok(r.calls.some(c=>c[0]==='notif'));assert.ok(r.calls.some(c=>c[0]==='flash'));assert.ok(r.calls.some(c=>c[0]==='tone'));
});
test('tin phát lại khi mở app: quét lúc 14:33 đã nhận bởi chính nó -> im lặng, vẫn gửi ack',()=>{
  const r=run(returned(),{__replayAt:ms('22/11/2026 14:32:50')});
  assert.deepEqual(r.calls,[]);assert.equal(r.rec.length,0);
  assert.equal(r.acks.length,1);assert.equal(r.acks[0][3],'duplicate');
});
test('tin đã đánh dấu xử lý (__replaySilent) -> im lặng',()=>{
  const r=run(returned(),{__replaySilent:true});
  assert.deepEqual(r.calls,[]);assert.equal(r.acks.length,1);
});
test('quét trực tiếp (không phải phát lại) vẫn báo trùng',()=>{
  const r=run(returned(),{});
  assert.ok(r.calls.some(c=>c[0]==='notif'));
});
test('đối soát đám mây gắn cờ phát lại vào gói tin',()=>{
  assert.match(html,/payload\.__replaySilent = Boolean\(ev\.id && processedCloudMsgIds\.has\(String\(ev\.id\)\)\)/);
  assert.match(html,/payload\.__replayAt = Number\(ev\.time\) > 0/);
});
