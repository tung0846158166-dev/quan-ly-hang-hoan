const fs=require('node:fs'),path=require('node:path'),test=require('node:test'),assert=require('node:assert/strict');
function load(file){const s=fs.readFileSync(path.join(__dirname,'..',file),'utf8');const m=s.match(/^    function extractCleanTrackingCode\([\s\S]*?^    }/m);assert.ok(m,file);
 return new Function('const extractCleanTrackingCode='+m[0].replace(/^\s*function extractCleanTrackingCode/,'function extractCleanTrackingCode')+';return extractCleanTrackingCode;')();}
for(const file of ['mobile_scanner.html','Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html']){
 const clean=load(file);
 test(file+': long TikTok order IDs are not cut into a fake J&T code',()=>{
  for(const id of ['587088842975709162','4043039842719270406','586805093934662757','4043028471137666705','587058217797453200'])assert.equal(clean(id),id);
 });
 test(file+': real J&T and carrier codes still extracted',()=>{
  assert.equal(clean('862633916173'),'862633916173');
  assert.equal(clean('J&T 862633916173 HN'),'862633916173');
  assert.equal(clean('spxvn06522189996b'),'SPXVN06522189996B');
  assert.equal(clean('261109t633s55v'),'261109T633S55V');
 });
}
