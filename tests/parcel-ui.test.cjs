const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const html=fs.readFileSync(path.join(__dirname,'../Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
function fn(name){const re=new RegExp('^    (?:async )?function '+name+'\\([^]*?^    }','m');const m=html.match(re);assert(m,name);return m[0];}
test('notification can locate a preserved tracking alias',()=>{const c={};vm.createContext(c);vm.runInContext(fn('notificationCodeMatches'),c);assert.equal(c.notificationCodeMatches({track:'NEW00001',trackingCodes:['OLD00001']},'OLD00001'),true);});
test('keyword search includes preserved parcel aliases',()=>{const c={getOrderPlatform:()=> 'Shopee',getOrderType:()=> 'Trả hàng',removeVietnameseTones:s=>s};vm.createContext(c);vm.runInContext(fn('isOrderMatch'),c);assert.equal(c.isOrderMatch({track:'NEW00001',trackingCodes:['OLD00001'],items:[]},'OLD00001'),true);});
