const { RenderLogPayload } = require('../dist/renderLogPayload');
const crypto = require('crypto');
const assert = require('assert');

// A stand-in for the base64 PNG RCC returns: 41724 bytes of random data, base64 encoded.
const image = crypto.randomBytes(41724).toString('base64');
const jobId = 'b8cd5a1d-eed9-4b15-b568-cc2c96d82670';
const chunk = 900;
const total = Math.ceil(image.length / chunk);

const lines = [`[payload] begin ${jobId} ${total} ${image.length}`];
for (let i = 1; i <= total; i++)
	lines.push(`[payload] ${jobId} ${i} ${image.substr((i - 1) * chunk, chunk)}`);
lines.push(`[payload] end ${jobId}`);

// Interleave the CoreGui noise that really appears mid-render, and split the feed into
// arbitrary byte windows to imitate partial file reads (chunks landing half-written).
const noisy = [
	'Failure to Start CoreScript moduleChatSelector.',
	'PerformanceStatsVisibleChanged is not a valid member of UserGameSettings',
	"Script 'CoreGui.RobloxGui.Modules.PlayerlistModule', Line 359",
	'Stack End',
];
const mixed = [];
lines.forEach((line, i) => {
	mixed.push(line);
	if (i % 7 === 0) mixed.push(noisy[i % noisy.length]);
});

const stream = mixed.join('\n') + '\n';
const collector = new RenderLogPayload(jobId);
let image2 = null;
for (let i = 0; i < stream.length; i += 137) {
	image2 = collector.feed(stream.substr(i, 137));
	if (image2) break;
}
assert.ok(image2, 'collector returned no image');
assert.strictEqual(image2.length, image.length, 'length mismatch');
assert.strictEqual(image2, image, 'reassembled image differs from the original');
console.log('PASS reassembled', image2.length, 'base64 chars from', total, 'chunks with noise interleaved');

// A different job's payload must be ignored.
const other = new RenderLogPayload('some-other-job');
assert.strictEqual(other.feed(stream), null, 'collector accepted another job id');
console.log('PASS another job id is ignored');

// A missing chunk must not yield a truncated image.
const holed = mixed.filter(l => !l.startsWith(`[payload] ${jobId} 3 `)).join('\n') + '\n';
const broken = new RenderLogPayload(jobId);
assert.strictEqual(broken.feed(holed), null, 'collector returned an image with a chunk missing');
console.log('PASS a missing chunk is refused');
