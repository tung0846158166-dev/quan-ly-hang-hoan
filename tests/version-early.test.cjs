const test=require('node:test'),fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const html=fs.readFileSync('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html','utf8');
test('nhãn phiên bản không còn chữ cứng v1.8.6 trong HTML tĩnh',()=>{
  assert(!/id="appVersionBadge"[\s\S]{0,400}v1\.8\.6/.test(html));
  assert(!/id="menuVersionText"[^>]*>v1\.8\.6/.test(html));
});
test('script sớm đặt phiên bản thật ngay khi có getAppVersionSync',()=>{
  const m=[...html.matchAll(/<script>\(function\(\)\{try\{var a=window\.desktopDiskAPI,v=[\s\S]*?<\/script>/g)];
  assert.equal(m.length,2);
  const els={appVersionBadge:{},menuVersionText:{}}; const doc={title:'',getElementById:id=>els[id]};
  vm.runInNewContext(m[0][0].replace(/^<script>|<\/script>$/g,''),{window:{desktopDiskAPI:{getAppVersionSync:()=>'9.8.7'}},document:doc});
  assert.match(els.appVersionBadge.textContent,/v9\.8\.7$/); assert.equal(els.menuVersionText.textContent,'v9.8.7'); assert.match(doc.title,/v9\.8\.7/);
  const d2={title:'x',getElementById:id=>els[id]}; els.appVersionBadge.textContent='keep';
  vm.runInNewContext(m[0][0].replace(/^<script>|<\/script>$/g,''),{window:{},document:d2});
  assert.equal(els.appVersionBadge.textContent,'keep');
});
