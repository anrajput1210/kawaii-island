// Renders the app icon from mascot.js with headless Chrome (no npm deps):
//   windows/src/KawaiiIsland/Assets/icon.ico  (16,24,32,48,64,128,256 — PNG-compressed entries)
//   design/mascot/icon.png (256) and icon-1024.png (macOS AppIcon source)
// Run: node icons.mjs [path-to-chrome]
import { writeFileSync, readFileSync, mkdtempSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const chrome = process.argv[2] || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const SKIN = 'kiko';
const mascotJs = readFileSync(new URL('./mascot.js', import.meta.url), 'utf8');
const page = `<!doctype html><body><pre id="out"></pre><script>${mascotJs}</script><script>
const svg = Mascot.face('${SKIN}', 'idle').replace('<svg ', '<svg width="1024" height="1024" ');
const img = new Image();
img.onload = () => {
  const png = s => { const c = document.createElement('canvas'); c.width = c.height = s; const g = c.getContext('2d'); g.imageSmoothingQuality = 'high'; g.drawImage(img, 0, 0, s, s); return atob(c.toDataURL('image/png').split(',')[1]); };
  const sizes = [16, 24, 32, 48, 64, 128, 256], imgs = sizes.map(png);
  let head = [0,0, 1,0, sizes.length,0], dir = [], offset = 6 + 16 * sizes.length;
  imgs.forEach((d, i) => { const s = sizes[i] % 256, n = d.length;
    dir.push(s, s, 0, 0, 1,0, 32,0, n & 255, n >> 8 & 255, n >> 16 & 255, n >>> 24, offset & 255, offset >> 8 & 255, offset >> 16 & 255, offset >>> 24); offset += n; });
  const ico = String.fromCharCode(...head, ...dir) + imgs.join('');
  document.getElementById('out').textContent = JSON.stringify({ ico: btoa(ico), p256: btoa(png(256)), p1024: btoa(png(1024)) });
};
img.src = 'data:image/svg+xml;base64,' + btoa(svg);
</script></body>`;

const tmp = join(mkdtempSync(join(tmpdir(), 'ki-icon-')), 'icon.html');
writeFileSync(tmp, page);
const dom = execFileSync(chrome, ['--headless=new', '--disable-gpu', '--virtual-time-budget=5000', '--dump-dom', pathToFileURL(tmp).href], { encoding: 'utf8', maxBuffer: 64 << 20 });
const json = /<pre id="out">(.*?)<\/pre>/s.exec(dom)?.[1];
if (!json) throw new Error('Chrome did not render the icon');
const { ico, p256, p1024 } = JSON.parse(json.replace(/&quot;/g, '"'));
writeFileSync(new URL('../../windows/src/KawaiiIsland/Assets/icon.ico', import.meta.url), Buffer.from(ico, 'base64'));
writeFileSync(new URL('./icon.png', import.meta.url), Buffer.from(p256, 'base64'));
writeFileSync(new URL('./icon-1024.png', import.meta.url), Buffer.from(p1024, 'base64'));
console.log('icon.ico, icon.png, icon-1024.png written');
