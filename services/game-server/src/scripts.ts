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
	// RCC assembles the character from the website's CharacterFetch URL instead of the job
	// inserting assets one by one - see the comment at the top of the file.
	playerAvatarFetch: fs.readFileSync(path.join(__dirname, '../scripts/player/avatarFetch.lua')).toString(),
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
		// RCC only trusts URLs under its BaseUrl, so uploads go to the website, which forwards
		// them to this game server. Posting to the game server's own port is rejected outright.
		.replace(/UPLOAD_URL_HERE/g, (() => {
			try {
				const base = new URL(renderBaseUrl);
				if (base.hostname === 'localhost' || base.hostname === '127.0.0.1') {
					return 'http://127.0.0.1:' + conf.port + '/api/upload-thumbnail-v1';
				}
				return base.origin + '/api/upload-thumbnail-v1';
			} catch (e) {
				return 'http://127.0.0.1:' + conf.port + '/api/upload-thumbnail-v1';
			}
		})())
		.replace(/AccessKey/g, conf.authorization.replace('"', '\\"'))
		.replace(/local baseURL = "http:\/\/localhost";/g, `local baseURL = "${conf.baseUrl}";`)
}

export default () => {
	return scripts;
}