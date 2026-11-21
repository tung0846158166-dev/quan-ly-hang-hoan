const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),test=require('node:test'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'..','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html'),'utf8');
test('script chính vẫn biên dịch được',()=>{
  const scripts=[...source.matchAll(/<script(?![^>]*src)[^>]*>([^]*?)<\/script>/g)].map(m=>m[1]).filter(t=>t.includes('passesTypeStatusFilters'));
  assert.equal(scripts.length,1);new vm.Script(scripts[0]);
});
test('lọc Loại (theo sàn) và Trạng thái kiện',()=>{
  const c={orders:[]};vm.createContext(c);
  const g=n=>source.match(new RegExp('^    function '+n+'\\([^]*?^    }','m'))[0];
  vm.runInContext("let currentSheetTypeFilter='all',currentPlatStatusFilter='all';"+g('orderPlatStatusText')+g('passesTypeStatusFilters'),c);
  const a={sheetType:'Chỉ hoàn tiền',platformStatus:'Đang về'},b={sheetType:'Trả hàng + hoàn tiền',rawDetails:{returnStatus:'Đã về shop'}};
  assert.ok(vm.runInContext('passesTypeStatusFilters',c)(a));
  vm.runInContext("currentSheetTypeFilter='Chỉ hoàn tiền'",c);
  assert.ok(vm.runInContext('passesTypeStatusFilters',c)(a));assert.ok(!vm.runInContext('passesTypeStatusFilters',c)(b));
  vm.runInContext("currentSheetTypeFilter='all';currentPlatStatusFilter='Đã về shop'",c);
  assert.ok(!vm.runInContext('passesTypeStatusFilters',c)(a));assert.ok(vm.runInContext('passesTypeStatusFilters',c)(b));
});
