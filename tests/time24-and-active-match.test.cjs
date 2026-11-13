const fs=require('node:fs'),path=require('node:path'),test=require('node:test'),assert=require('node:assert/strict');
const src=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
const pick=n=>{const m=src.match(new RegExp('^    function '+n+'\\([\\s\\S]*?^    }','m'));assert.ok(m,n);return m[0];};
test('time inputs are 24h text fields, not locale AM/PM time pickers',()=>{
 for(const id of ['filterTimeFrom','filterTimeTo','archivedFilterFromTime','archivedFilterToTime'])
  assert.match(src,new RegExp('<input type="text"[^>]*oninput="formatTime24Input\\(this\\)"[^>]*id="'+id+'"'));
 assert.equal((src.match(/<input type="time"/g)||[]).length,0);
});
test('formatTime24Input formats and clamps, readTime24Value validates',()=>{
 const f=new Function(pick('formatTime24Input')+pick('readTime24Value')+'return {formatTime24Input,readTime24Value};')();
 const mk=v=>({value:v,dispatchEvent(){this.fired=(this.fired||0)+1}});
 let e=mk('0839');f.formatTime24Input(e);assert.equal(e.value,'08:39');assert.equal(e.fired,1);
 e=mk('8');f.formatTime24Input(e);assert.equal(e.value,'8');assert.equal(e.fired,undefined);
 e=mk('2575');f.formatTime24Input(e);assert.equal(e.value,'23:59');
 e=mk('');f.formatTime24Input(e);assert.equal(e.fired,1);
 assert.equal(f.readTime24Value(mk('18:05')),'18:05');assert.equal(f.readTime24Value(mk('18:')),'');assert.equal(f.readTime24Value(null),'');
});
test('phone match prefers the single claim that still needs receiving',()=>{
 const g=new Function('orders','extractCleanTrackingCode','getOrderTrackingCodes',pick('getPhoneOrderMatches')+'return getPhoneOrderMatches;');
 const orders=[{orderId:'A',track:'T1',status:'Không cần nhận'},{orderId:'A',track:'T1',status:'Không cần nhận'},{orderId:'A',track:'T1',returnTrack:'R1',status:'Chưa trả'},{orderId:'B',track:'T2',status:'Chưa trả'},{orderId:'B',track:'T2',status:'Chưa trả'}];
 const m=g(orders,v=>String(v||'').toUpperCase(),o=>[o.track,o.returnTrack].filter(Boolean))('T1');
 assert.deepEqual(m.map(x=>x.index),[2]);
 assert.equal(g(orders,v=>String(v||'').toUpperCase(),o=>[o.track,o.returnTrack].filter(Boolean))('T2').length,2);
});
