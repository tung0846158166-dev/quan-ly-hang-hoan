const fs=require('node:fs'),path=require('node:path'),test=require('node:test'),assert=require('node:assert/strict');
const src=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
const pick=n=>{const m=src.match(new RegExp('^    function '+n+'\\([\\s\\S]*?^    }','m'));assert.ok(m,n);return m[0];};
const ctx=new Function(pick('removeVietnameseTones')+pick('getOrderMatchFieldLabels')+'const getOrderPlatform=o=>o.platform||"";const getOrderType=o=>o.type||"";return getOrderMatchFieldLabels;')();
test('reports which fields matched the search keyword',()=>{
 const o={orderId:'261107NS1FVYCW',track:'SPXVN06925529624B',returnTrack:'2611090THNVCD6T',buyerUsername:'abc',items:[{product:'Áo Polo',color:'Xám',size:'XL'}],platform:'Shopee',type:'Hoàn'};
 assert.deepEqual(ctx(o,'2611090thnvcd6t'),['mã vận đơn hoàn về']);
 assert.deepEqual(ctx(o,'ao polo'),['sản phẩm / phân loại']);
 assert.deepEqual(ctx(o,'261107NS1FVYCW'),['mã đơn hàng']);
 assert.deepEqual(ctx(o,'zzz'),[]);
});
