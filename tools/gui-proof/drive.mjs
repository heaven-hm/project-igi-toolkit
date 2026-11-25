// GUI/CUA proof: drives the generated UI preview in headless Chrome via the
// DevTools protocol, clicks both apply buttons, records the resulting status
// text and captures a screenshot. No test framework or npm packages required.
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const html = 'file://' + resolve(root, 'tools/gui-proof/preview.html');
const chrome = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const port = 9333;
const userData = '/private/var/folders/q0/rj0k1xgn28x14thfz4gnbm5c0000gn/T/opencode/igi-cdp-' + process.pid;

mkdirSync(resolve(root, 'evidence'), { recursive: true });

const child = spawn(chrome, [
  '--headless=new', '--disable-gpu', '--no-sandbox', '--hide-scrollbars',
  '--remote-debugging-port=' + port, '--remote-allow-origins=*',
  '--user-data-dir=' + userData, '--window-size=1280,1200', html
], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitForPage() {
  for (let i = 0; i < 60; i++) {
    try {
      const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
      const page = targets.find(t => t.type === 'page');
      if (page) return page;
    } catch (_) { /* chrome not ready */ }
    await sleep(250);
  }
  throw new Error('Chrome DevTools endpoint did not become ready');
}

try {
  const page = await waitForPage();
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolveOpen, rejectOpen) => {
    ws.onopen = resolveOpen;
    ws.onerror = rejectOpen;
  });

  let nextId = 0;
  const pending = new Map();
  ws.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id && pending.has(message.id)) {
      pending.get(message.id)(message);
      pending.delete(message.id);
    }
  };
  const send = (method, params = {}) => new Promise(resolveSend => {
    const id = ++nextId;
    pending.set(id, resolveSend);
    ws.send(JSON.stringify({ id, method, params }));
  });

  await send('Page.enable');
  await send('Runtime.enable');
  await sleep(600);

  const before = await send('Runtime.evaluate', {
    expression: "document.getElementById('status').textContent", returnByValue: true
  });
  const clicked = await send('Runtime.evaluate', {
    expression: "document.getElementById('applyBtn').click();" +
                "document.getElementById('objBtn').click();" +
                "JSON.stringify({status:document.getElementById('status').textContent," +
                "success:document.querySelectorAll('#success li').length," +
                "failure:document.querySelectorAll('#failure li').length," +
                "flow:document.getElementById('flow').textContent})",
    returnByValue: true
  });
  const result = JSON.parse(clicked.result.result.value);

  const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(resolve(root, 'evidence/gui-proof-interaction.png'),
    Buffer.from(shot.result.data, 'base64'));

  const log = [
    'GUI / CDP interaction proof',
    'Initial status: ' + before.result.result.value,
    'After clicking Apply LevelFlow + Apply property edits:',
    result.status,
    'Success trigger rows: ' + result.success,
    'Failure trigger rows: ' + result.failure,
    'Generated LevelFlow: ' + result.flow,
    'Verdict: ' + (result.status.startsWith('STATUS: PASS') ? 'PASS' : 'FAIL')
  ].join('\n') + '\n';
  writeFileSync(resolve(root, 'evidence/gui-proof-interaction.txt'), log);
  console.log(log);

  ws.close();
} finally {
  child.kill('SIGTERM');
}
