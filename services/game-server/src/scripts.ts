import fs =require('fs');
import path =require('path');

import conf from './helpers/Config';

const env = conf.baseUrl.indexOf('localhost') !== -1 ? 'development' : 'production';
console.log('Env =',env);

const scripts: Record<string, string> = {
	// GS
	gameServer: fs.readFileSync(path.join(__dirname, '../scripts/gameserver.lua')).toString(),
	// Thumbs
	playerThumbnail: fs.readFileSync(path.join(__dirname, '../scripts/player/thumbnail.lua')).toString(),
	playerHeadshot: fs.readFileSync(path.join(__dirname, '../scripts/player/headshot.lua')).toString(),
	imageTexture: fs.readFileSync(path.join(__dirname, '../scripts/asset/image.lua')).toString(),
	assetThumbnail: fs.readFileSync(path.join(__dirname, '../scripts/asset/asset.lua')).toString(),
	meshThumbnail: fs.readFileSync(path.join(__dirname, '../scripts/asset/mesh.lua')).toString(),
	headThumbnail: fs.readFileSync(path.join(__dirname, '../scripts/asset/head.lua')).toString(),
	gameThumbnail: fs.readFileSync(path.join(__dirname, '../scripts/asset/game.lua')).toString(),
}

// RCC's TrustCheck refuses assets served from the "localhost" hostname, so always hand the
// render scripts the raw loopback IP. This is also what makes self-hosted deployments work:
// without it the scripts keep pointing at whatever domain was hardcoded in them.
const renderBaseUrl = conf.baseUrl.replace('//localhost', '//127.0.0.1');

// RCC only fetches from the host it was patched to trust, and that applies to the thumbnail
// upload back to us as well. Post to the same host the render used, not a hardcoded loopback
// address, so a hosts-file setup (bbblox.org -> 127.0.0.1) works end to end.
const uploadHost = (() => {
	try {
		const host = new URL(renderBaseUrl).hostname;
		return host === 'localhost' ? '127.0.0.1' : host;
	} catch (e) {
		return '127.0.0.1';
	}
})();

for (const s in scripts) {
	scripts[s] = scripts[s]
		// set base url
		.replace(/local url = "base_url"/g, `local url = "${renderBaseUrl}"`)
		// every script declares its own hardcoded host - overwrite all of them
		.replace(/local baseURL = "http[^"]*";/g, `local baseURL = "${renderBaseUrl}";`)
		.replace(/http:\/\/economy-simulator\.org/g, renderBaseUrl)
		// set access key
		.replace(/_AUTHORIZATION_STRING_/g, conf.authorization.replace('"', '\\"'))
		.replace(/isDebugServer = false/g, env === 'development' ? 'isDebugServer = true' : 'isDebugServer = false')
		// set upload url
		// .replace(/UPLOAD_URL_HERE/g, (dockerEnabled ? 'http://host.docker.internal' : 'http://127.0.0.1:') + conf.port + '/api/upload-thumbnail-v1')
		.replace(/UPLOAD_URL_HERE/g, 'http://' + uploadHost + ':' + conf.port + '/api/upload-thumbnail-v1')
		.replace(/AccessKey/g, conf.authorization.replace('"', '\\"'))
		.replace(/local baseURL = "http:\/\/localhost";/g, `local baseURL = "${conf.baseUrl}";`)
}

export default () => {
	return scripts;
}