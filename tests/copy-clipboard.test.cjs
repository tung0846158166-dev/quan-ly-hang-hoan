const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8').replace(/\r\n/g,'\n');
const s=html.indexOf('    function copyTextToClipboard(');assert(s>=0,'hàm copyTextToClipboard phải được định nghĩa');
const code=html.slice(s,html.indexOf('\n    }\n',s)+7);
function mk(nav,docExtra){const toasts=[];const ctx=vm.createContext({navigator:nav,document:Object.assign({createElement:()=>({style:{},select(){}}),body:{appendChild(){},removeChild(){}},execCommand:()=>true},docExtra||{}),showAppToast:(m,t)=>toasts.push([m,t])});vm.runInContext(code,ctx);return{ctx,toasts};}
test('bấm mã đơn: chép vào clipboard và báo bằng câu đã đặt',async()=>{
  let got='';const {ctx,toasts}=mk({clipboard:{writeText:async v=>{got=v}}});
  assert.equal(await ctx.copyTextToClipboard('2611125EUGC7D4','Đã copy Mã đơn: 2611125EUGC7D4'),true);
  assert.equal(got,'2611125EUGC7D4');assert.match(toasts[0][0],/Đã copy Mã đơn: 2611125EUGC7D4/);assert.equal(toasts[0][1],'success');
});
test('clipboard API lỗi → dùng cách dự phòng',async()=>{
  const {ctx,toasts}=mk({clipboard:{writeText:async()=>{throw new Error('denied')}}});
  assert.equal(await ctx.copyTextToClipboard('ABC','ok'),true);assert.equal(toasts[0][1],'success');
});
test('không có gì để chép ("-" hoặc rỗng) → bỏ qua',async()=>{
  const {ctx,toasts}=mk({clipboard:{writeText:async()=>{}}});
  assert.equal(await ctx.copyTextToClipboard('-'),false);assert.equal(await ctx.copyTextToClipboard(''),false);assert.equal(toasts.length,0);
});
