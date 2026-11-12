const fs=require('node:fs'),path=require('node:path'),test=require('node:test'),assert=require('node:assert/strict');
const src=fs.readFileSync(path.join(__dirname,'..','mobile_scanner.html'),'utf8');
test('scan loop throttles decoding and slows down when idle',()=>{
 const m=src.match(/ACTIVE_FRAME_GAP_MS = (\d+), IDLE_FRAME_GAP_MS = (\d+), IDLE_AFTER_MS = (\d+)/);assert(m);
 const [a,i,t]=m.slice(1).map(Number);assert(a>=50&&a<=100);assert(i>=500&&i<=2000);assert(t>=10000&&t<=60000);
 const loop=src.slice(src.indexOf('const processNextFrame'));
 assert(/waitMs > 1\) \{ cameraScanInterval = setTimeout\(processNextFrame/.test(loop),'throttle reschedules');
 assert(/wakeScanner\(\);\s*handleCameraFrameDecoded/.test(src),'accepted code wakes the scanner');
 assert(/pointerdown', wakeScanner/.test(src));
});
test('wake listeners are detached when the scanner stops',()=>{
 assert(/scanWakeDetach = detachWake/.test(src));
 assert(/if \(scanWakeDetach\) \{ scanWakeDetach\(\); scanWakeDetach = null; \}/.test(src));
});
