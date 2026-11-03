const ROOT=require('path').resolve(__dirname, '..');
// Run with: node src/pipeline.test.js after npm run build
const sharp=require('sharp');
const { getResult, getUploadCallbacks } = require('../dist/rendering');
const assert=require('assert');
(async()=>{
  // A stand-in for the 420x420 PNG RCC renders.
  const png = await sharp({create:{width:420,height:420,channels:4,background:{r:200,g:180,b:60,alpha:1}}}).png().toBuffer();
  const jobId='pipeline-test-job';
  const pending = getResult(jobId, 1);
  const callbacks = getUploadCallbacks()[jobId];
  assert.ok(callbacks && callbacks.length===1, 'no callback registered for the job');
  // Exactly what the log watcher hands over: a JSON payload with the base64 image.
  callbacks[0]({ thumbnail: png.toString('base64'), userId: 0, accessKey:'AccessKey', type:'PlayerHeadshot', jobId });
  const result = await pending;
  const meta = await sharp(Buffer.from(result.thumbnail,'base64')).metadata();
  assert.strictEqual(meta.width, 420); assert.strictEqual(meta.height, 420);
  assert.strictEqual(meta.format, 'png');
  console.log('PASS delivered payload survives resize:', meta.width+'x'+meta.height, meta.format);
  process.exit(0);
})().catch(e=>{ console.error('FAIL', e.message); process.exit(1); });
