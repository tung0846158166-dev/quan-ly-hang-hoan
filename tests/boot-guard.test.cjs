const test=require('node:test'),fs=require('node:fs'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8');
test('màn chờ được đặt trước các script CDN và có cơ chế gỡ',()=>{
  const g=html.indexOf("classList.add('app-booting')"), t=html.indexOf('cdn.tailwindcss.com');
  assert(g>0 && t>g);
  assert(/html\.app-booting body \{ opacity: 0/.test(html));
  assert(html.includes('function revealAppWhenStyled'));
  assert(/renderAll\(\);\s*try \{ revealAppWhenStyled\(\)/.test(html));
  assert(html.includes("remove('app-booting')"));
});
