import { strictEqual as assert } from 'assert';

interface IWebsiteConfiguration {
	authorization: string;
	baseUrl: string;
	rccPort: number;
	port: number;
	thumbnailWebsocketPort: number;
	rcc?: string;
	content?: string;
	dockerDisabled?: boolean;
	websiteBotAuth: string;
}

// Strip a UTF-8 BOM before parsing: PowerShell's Set-Content -Encoding UTF8 writes one, and
// JSON.parse then dies with "Unexpected token '\ufeff'" pointing at a file that looks fine.
const rawConfig = require('fs').readFileSync(require('path').join(__dirname, '../../config.json')).toString().replace(/^\uFEFF/, '');

const conf: Readonly<IWebsiteConfiguration> = JSON.parse(rawConfig)
export default conf;

assert(typeof conf.authorization, 'string');
assert(typeof conf.baseUrl, 'string');
assert(typeof conf.rccPort, 'number');
assert(typeof conf.port, 'number');
assert(typeof conf.websiteBotAuth, 'string');
assert(typeof conf.thumbnailWebsocketPort, 'number');
if (typeof conf.rcc !== 'undefined') {
	assert(typeof conf.rcc === 'string' && conf.rcc.length > 0, true);
}