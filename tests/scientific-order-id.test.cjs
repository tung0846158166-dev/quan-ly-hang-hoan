const fs=require('node:fs'),path=require('node:path'),test=require('node:test'),assert=require('node:assert/strict');
const src=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
const pick=n=>{const m=src.match(new RegExp('^    function '+n+'\\([\\s\\S]*?^    }','m'));assert.ok(m,n);return m[0];};
const f=new Function(pick('normalizeScientificOrderId')+pick('repairScientificOrderId')+pick('repairScientificOrderIds')+'return {normalizeScientificOrderId,repairScientificOrderId,repairScientificOrderIds};')();
test('real Shopee IDs shaped like digits+E+digits are kept, Excel sci numbers are expanded',()=>{
 for(const id of ['26111238E12345','261112 38E1'.replace(' ',''),'2611123E8ABCD'])assert.equal(f.normalizeScientificOrderId(id),id);
 assert.equal(f.normalizeScientificOrderId('5.87e+17'),'587000000000000000');
 assert.equal(f.normalizeScientificOrderId('5.8705098E+17'),'587050980000000000');
 assert.equal(f.normalizeScientificOrderId(' 587050980362782184 '),'587050980362782184');
});
test('corrupted IDs expanded with zeros are repaired to the 14-char Shopee form',()=>{
 const bad='26111238'+'0'.repeat(12345);
 assert.equal(f.repairScientificOrderId(bad),'26111238E12345');
 assert.equal(f.repairScientificOrderId('587050980000000000'),'587050980000000000');
 assert.equal(f.repairScientificOrderId('261107NS1FVYCW'),'261107NS1FVYCW');
 const list=[{orderId:bad},{orderId:'587050980362782184'},null];
 assert.equal(f.repairScientificOrderIds(list),1);assert.equal(list[0].orderId,'26111238E12345');
});
