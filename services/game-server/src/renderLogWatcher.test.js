const fs = require('fs'), os = require('os'), path = require('path'), crypto = require('crypto'), assert = require('assert');
const ROOT = '/app/conversations/6ae441b0707ef780407c5508/repo/services/game-server';
const { watchRenderLog } = require(ROOT + '/dist/renderLogWatcher');

const jobId = '57cf8892-de20-4f2a-a4a1-1b1fdfdc10ca';
const png = crypto.randomBytes(41724);            // the actual image RCC returns
const image = png.toString('base64');              // what Click hands the script
const hex = Buffer.from(image, 'utf8').toString('hex');   // what the script prints
const chunk = 1800;
const total = Math.ceil(hex.length / chunk);

const lines = [`[payload] begin ${jobId} ${total} ${hex.length} hex`];
for (let i = 1; i <= total; i++) lines.push(`[payload] ${jobId} ${i} ${hex.substr((i - 1) * chunk, chunk)}`);
lines.push(`[payload] end ${jobId}`);
lines.forEach((l, i) => { if (i % 9 === 0) lines[i] = "PerformanceStatsVisibleChanged is not a valid member of UserGameSettings\n" + l; });

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'rcclog-'));
const logPath = path.join(dir, 'rcc-53640.log');
fs.writeFileSync(logPath, '');
const stream = lines.join('\n') + '\n';

const done = new Promise((resolve) => {
	const w = watchRenderLog(logPath, jobId, (b64) => { w.cancel(); resolve(b64); }, 20);
	let i = 0;
	const feed = setInterval(() => {
		if (i >= stream.length) { clearInterval(feed); setTimeout(() => { w.cancel(); resolve(null); }, 500); return; }
		fs.appendFileSync(logPath, stream.substr(i, 3000));
		i += 3000;
	}, 5);
});

done.then((b64) => {
	assert.ok(b64, 'no image delivered');
	assert.strictEqual(b64, image, 'image differs from what Click produced');
	const back = Buffer.from(b64, 'base64');
	assert.deepStrictEqual(back, png, 'decoded bytes differ from the PNG');
	console.log('PASS', total, 'hex chunks ->', b64.length, 'chars of base64 ->', back.length, 'PNG bytes, byte identical');
	fs.rmSync(dir, { recursive: true, force: true });
});
