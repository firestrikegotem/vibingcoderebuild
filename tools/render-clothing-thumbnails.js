#!/usr/bin/env node
/**
 * Draws clothing catalog pictures with the renderer, without waiting for the website.
 *
 * The shop picture of a Shirt / Pants / T-Shirt should be a dummy wearing it. That render already
 * exists behind the game server (scripts/asset/image.lua), but asking for it by hand is awkward, and
 * when the website's repair loop is not running nothing gets drawn at all. This tool talks to the
 * game server the same way the website does and does the work directly.
 *
 *   set PGPASSWORD=your-password
 *   node tools\render-clothing-thumbnails.js --inspect 72
 *       Renders item 72 and saves it to tools\output\clothing-72.png so you can look at it.
 *       Nothing is written to the database, so this is safe to run.
 *
 *   node tools\render-clothing-thumbnails.js
 *       Renders every clothing item whose texture is still on disk and reports the result.
 *
 *   node tools\render-clothing-thumbnails.js --apply
 *       Same, but also saves the pictures and writes the thumbnail rows, so the shop fills in.
 *
 * Same overrides as the other tools: PGHOST, PGPORT, PGUSER, PGDATABASE.
 */
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const websiteDir = path.join(repoRoot, 'services', 'Roblox', 'Roblox.Website');
const gameServerDir = path.join(repoRoot, 'services', 'game-server');
const outputDir = path.join(__dirname, 'output');

const requireFrom = (names) => {
	for (const name of names) {
		try { return require(name); } catch (e) { /* try the next place */ }
	}
	return null;
};

const pg = requireFrom(['pg', '../services/api/node_modules/pg', '../services/2016-roblox-main/node_modules/pg']);
if (pg === null) {
	console.error('Could not find a postgres client. Install one with "npm i pg" and run this again.');
	process.exit(1);
}
const sharp = requireFrom([path.join(gameServerDir, 'node_modules', 'sharp'), 'sharp']);
const WebSocket = requireFrom([
	'ws',
	path.join(gameServerDir, 'node_modules', 'ws'),
	path.join(repoRoot, 'node_modules', 'ws'),
]);
if (WebSocket === null) {
	console.error('Could not find the websocket client. Run "npm install" in services\\game-server first.');
	process.exit(1);
}
if (!process.env.PGPASSWORD) {
	console.error('Set the database password first:  set PGPASSWORD=your-password');
	process.exit(1);
}

const args = process.argv.slice(2);
const apply = args.includes('--apply');
const inspectIndex = args.indexOf('--inspect');
const inspectId = inspectIndex === -1 ? null : Number(args[inspectIndex + 1]);
const CLOTHING_TYPES = [2, 11, 12];

const readJson = (file) => {
	try {
		return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
	} catch (e) {
		return null;
	}
};

/** Where the site keeps textures and thumbnails, resolved the same way the tools that copy files do. */
const readDirectories = () => {
	const settings = readJson(path.join(websiteDir, 'appsettings.json'));
	if (settings === null) {
		console.error('Could not read ' + path.join(websiteDir, 'appsettings.json'));
		process.exit(1);
	}
	const dirs = settings.Directories || {};
	const resolve = (value, fallback) => {
		const normalised = (value || fallback).replace(/\\/g, path.sep).replace(/[\\/]$/, '') + path.sep;
		return path.isAbsolute(normalised) ? normalised : path.resolve(websiteDir, normalised);
	};
	return {
		asset: resolve(dirs.Asset, '../../api/storage/asset/'),
		thumbnails: resolve(dirs.Thumbnails, '../../api/public/images/thumbnails/'),
	};
};

/** The game server only accepts a socket from someone who knows its key. */
const readGameServerConfig = () => {
	const local = readJson(path.join(gameServerDir, 'config.json')) || {};
	const settings = readJson(path.join(websiteDir, 'appsettings.json')) || {};
	return {
		port: Number(local.thumbnailWebsocketPort) || 5002,
		key: local.authorization || settings.GameServerAuthorization || '',
	};
};

/** Finds a texture file even when the stored key has no extension. */
const locateTexture = (assetDir, key) => {
	if (!key) return null;
	for (const candidate of [path.join(assetDir, key), path.join(assetDir, key + '.png')]) {
		try { if (fs.statSync(candidate).isFile()) return candidate; } catch (e) { /* keep looking */ }
	}
	return null;
};

const connect = (options) => new Promise((resolve, reject) => {
	const socket = new WebSocket('ws://127.0.0.1:' + options.port + '/?key=' + encodeURIComponent(options.key));
	const pending = new Map();
	let nextId = 1;

	socket.on('open', () => resolve({
		request: (command, requestArgs) => new Promise((res, rej) => {
			const id = 'tool-' + nextId++;
			pending.set(id, { res, rej });
			socket.send(JSON.stringify({ command, args: requestArgs, id }));
			setTimeout(() => {
				if (!pending.has(id)) return;
				pending.delete(id);
				rej(new Error('the renderer did not answer ' + command + ' in two minutes'));
			}, 120000);
		}),
		close: () => socket.close(),
	}));

	socket.on('message', (raw) => {
		let reply;
		try { reply = JSON.parse(raw.toString()); } catch (e) { return; }
		const waiting = pending.get(reply.id);
		if (!waiting) return;
		pending.delete(reply.id);
		if (reply.status === 200 && reply.data) {
			waiting.res(reply.data);
		} else {
			waiting.rej(new Error('renderer said status ' + reply.status + ' ' + (reply.code || '')));
		}
	});

	socket.on('error', (e) => reject(new Error('could not reach the game server on port ' + options.port + ' (' + e.message + '). Is it running?')));
});

/**
 * How much of the picture is actually coloured. A dummy that never got its clothes is white plastic
 * with a face on it, so a share near zero means the item did not make it onto the mannequin.
 */
const colourCoverage = async (png) => {
	if (sharp === null) return null;
	try {
		const { data, info } = await sharp(png).raw().toBuffer({ resolveWithObject: true });
		const stride = info.channels;
		let colourful = 0;
		for (let i = 0; i + 2 < data.length; i += stride) {
			const r = data[i], g = data[i + 1], b = data[i + 2];
			if (Math.max(r, g, b) - Math.min(r, g, b) > 40) colourful++;
		}
		return Math.round((colourful / (info.width * info.height)) * 1000) / 10;
	} catch (e) {
		return null;
	}
};

/** The renderer hands back base64, sometimes with the data url prefix still attached. */
const decodePng = (data) => {
	const text = typeof data === 'string' ? data : (data && data.thumbnail) || '';
	const base64 = text.replace(/^data:image\/png;base64,/, '');
	return Buffer.from(base64, 'base64');
};

const main = async () => {
	const dirs = readDirectories();
	const server = readGameServerConfig();
	if (!server.key) {
		console.error('No game server key found in services\\game-server\\config.json');
		process.exit(1);
	}

	const client = new pg.Client({
		host: process.env.PGHOST || '127.0.0.1',
		port: Number(process.env.PGPORT) || 5432,
		user: process.env.PGUSER || 'postgres',
		password: process.env.PGPASSWORD,
		database: process.env.PGDATABASE || 'ecsrev',
	});
	await client.connect();

	let renderer;
	try {
		renderer = await connect(server);
	} catch (e) {
		console.error(e.message);
		await client.end();
		process.exit(1);
	}

	let rendered = 0;
	let skipped = 0;
	try {
		const where = inspectId === null
			? 'a.asset_type = ANY($1)'
			: 'a.id = $1';
		const params = inspectId === null ? [CLOTHING_TYPES] : [inspectId];
		const rows = (await client.query(
			'SELECT a.id, a.name, a.asset_type FROM asset a WHERE ' + where + ' ORDER BY a.id DESC',
			params)).rows;

		if (rows.length === 0) {
			console.log('No clothing items matched.');
			return;
		}
		console.log(rows.length + ' clothing item(s) to consider. Rendering through the game server on port ' + server.port + '.');
		console.log(apply ? 'Mode: APPLY - pictures and rows will be written.' : 'Mode: dry run - nothing is written. Add --apply to save them.');
		console.log('');

		for (const row of rows) {
			const version = (await client.query(
				'SELECT id, content_id FROM asset_version WHERE asset_id = $1 ORDER BY id DESC LIMIT 1',
				[row.id])).rows[0];
			if (!version) {
				skipped++;
				console.log('#' + row.id + '  ' + row.name + '  -- no version row');
				continue;
			}

			let textureKey = null;
			if (version.content_id && Number(version.content_id) !== 0) {
				const textureVersion = (await client.query(
					'SELECT content_url FROM asset_version WHERE asset_id = $1 ORDER BY id DESC LIMIT 1',
					[version.content_id])).rows[0];
				textureKey = textureVersion ? textureVersion.content_url : null;
			}
			if (!locateTexture(dirs.asset, textureKey)) {
				skipped++;
				console.log('#' + row.id + '  ' + row.name + '  -- texture is not on disk, nothing to draw');
				continue;
			}

			let png;
			try {
				const data = await renderer.request('GenerateThumbnailTexture', [row.id, row.asset_type, version.content_id]);
				png = decodePng(data);
			} catch (e) {
				skipped++;
				console.log('#' + row.id + '  ' + row.name + '  -- render failed: ' + e.message);
				continue;
			}

			if (png.length < 1000 || png.slice(1, 4).toString() !== 'PNG') {
				skipped++;
				console.log('#' + row.id + '  ' + row.name + '  -- renderer sent back something that is not a picture (' + png.length + ' bytes)');
				continue;
			}

			rendered++;
			const hash = crypto.createHash('sha256').update(png).digest('hex');
			const coverage = await colourCoverage(png);
			const verdict = coverage === null
				? ''
				: coverage < 1
					? '  [the mannequin looks undressed - the clothing picture did not arrive]'
					: '  [the mannequin is wearing it]';
			if (inspectId !== null) {
				fs.mkdirSync(outputDir, { recursive: true });
				const file = path.join(outputDir, 'clothing-' + row.id + '.png');
				fs.writeFileSync(file, png);
				console.log('#' + row.id + '  ' + row.name + '  -- ' + png.length + ' bytes, ' +
					(coverage === null ? '' : coverage + '% coloured, ') + 'saved to ' + path.relative(repoRoot, file) + verdict);
			} else {
				console.log('#' + row.id + '  ' + row.name + '  -- ' + png.length + ' bytes' +
					(coverage === null ? '' : ', ' + coverage + '% coloured') + verdict);
			}

			if (!apply) continue;

			const file = path.join(dirs.thumbnails, hash + '.png');
			if (!fs.existsSync(file)) fs.writeFileSync(file, png);
			await client.query('BEGIN');
			try {
				await client.query('DELETE FROM asset_thumbnail WHERE asset_id = $1', [row.id]);
				await client.query(
					'INSERT INTO asset_thumbnail (asset_id, content_url, moderation_status, asset_version_id) ' +
					'VALUES ($1, $2, $3, $4)',
					[row.id, hash, 2, version.id]);
				await client.query('COMMIT');
			} catch (e) {
				await client.query('ROLLBACK');
				throw e;
			}
		}

		console.log('');
		console.log('Rendered ' + rendered + ' item(s), skipped ' + skipped + '.');
		if (!apply && inspectId === null) {
			console.log('Nothing was saved. Run it again with --apply to fill in the shop.');
		}
	} finally {
		renderer.close();
		await client.end();
	}
};

main().catch((e) => {
	console.error('Failed: ' + e.message);
	process.exit(1);
});
