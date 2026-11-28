const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../test-output/fixture');
let active = 0, peak = 0;
function serve(req, res) {
  const name = path.basename(new URL(req.url, 'http://localhost').pathname);
  if (name === 'metrics') { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({active, peak})); return; }
  if (name === 'reset-metrics') { peak = active; res.end('ok'); return; }
  const slow = /^slow-[a-z]+\.mp4$/.test(name);
  const cookieCheck = name === 'cookie-check.mp4';
  if (cookieCheck && req.headers.cookie !== 'fixture=' + req.headers.host.split(':')[0]) {
    res.writeHead(403); res.end(); return;
  }
  const file = path.join(root, slow || cookieCheck ? 'video.mp4' : name);
  if (!name || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); res.end('not found'); return; }
  const size = fs.statSync(file).size;
  const mime = name.endsWith('.mpd') ? 'application/dash+xml' : name.endsWith('.mp4') ? 'video/mp4' : name.endsWith('.webm') ? 'audio/webm' : 'application/octet-stream';
  const range = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range || '');
  let start = range ? Number(range[1]) : 0, end = range && range[2] ? Math.min(Number(range[2]), size - 1) : size - 1;
  if (start >= size) { res.writeHead(416, { 'Content-Range': `bytes */${size}` }); res.end(); return; }
  const headers = { 'Content-Type': mime, 'Content-Length': end - start + 1, 'Accept-Ranges': 'bytes' };
  if (range) headers['Content-Range'] = `bytes ${start}-${end}/${size}`;
  res.writeHead(range ? 206 : 200, headers);
  if (req.method === 'HEAD') res.end();
  else if (slow) {
    const data = fs.readFileSync(file); let offset = start;
    active++; peak = Math.max(peak, active);
    const timer = setInterval(() => {
      const next = Math.min(offset + 512, end + 1);
      res.write(data.subarray(offset, next)); offset = next;
      if (offset > end) res.end();
    }, 100);
    res.once('close', () => { clearInterval(timer); active--; });
  } else fs.createReadStream(file, { start, end }).pipe(res);
}
const server = http.createServer(serve);
server.listen(0, '127.0.0.1', () => {
  const port = server.address().port;
  const ready = () => fs.writeFileSync(path.resolve(__dirname, '../test-output/server-port.txt'), String(port));
  if (process.argv.includes('--cookie-peer')) http.createServer(serve).listen(port, '127.0.0.2', ready);
  else ready();
  console.log(`fixture server http://127.0.0.1:${server.address().port}`);
});
