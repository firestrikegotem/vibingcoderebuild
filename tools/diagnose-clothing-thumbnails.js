#!/usr/bin/env node
/**
 * Works out why a clothing item has no thumbnail.
 *
 * For every Shirt / Pants / T-Shirt it prints the two things a thumbnail can come from - the
 * texture asset the item points at (content_id) and the file on disk - plus whether the running
 * website even has the code that copies that texture. Reading it left to right tells you which
 * step is broken.
 *
 *   set PGPASSWORD=your-password
 *   node tools\diagnose-clothing-thumbnails.js
 *
 * Same overrides as refresh-clothing-thumbnails.js: PGHOST, PGPORT, PGUSER, PGDATABASE.
 */
'use strict';

const fs = require('fs');
const path = require('path');

const pgCandidates = [
	'pg',
	'../services/api/node_modules/pg',
	'../services/2016-roblox-main/node_modules/pg',
];
let pg = null;
for (const candidate of pgCandidates) {
	try { pg = require(candidate); break; } catch (e) { /* next */ }
}
if (pg === null) {
	console.error('Could not find a postgres client. Install one with "npm i pg" and run this again.');
	process.exit(1);
}
if (!process.env.PGPASSWORD) {
	console.error('Set the database password first:  set PGPASSWORD=your-password');
	process.exit(1);
}

const repoRoot = path.resolve(__dirname, '..');
const websiteDir = path.join(repoRoot, 'services', 'Roblox', 'Roblox.Website');

// Directories are relative to the website project, same as the app resolves them.
const readDirectories = () => {
	const file = path.join(websiteDir, 'appsettings.json');
	let dirs = {};
	try {
		const raw = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '');
		dirs = (JSON.parse(raw).Directories) || {};
	} catch (e) {
		console.error('Could not read ' + file + ': ' + e.message);
		process.exit(1);
	}
	const resolve = (value) => {
		if (!value) return null;
		const normalised = value.replace(/\\/g, path.sep).replace(/\/$/, path.sep);
		return path.isAbsolute(normalised) ? normalised : path.resolve(websiteDir, normalised);
	};
	return {
		asset: resolve(dirs.Asset),
		thumbnails: resolve(dirs.Thumbnails),
	};
};

// The new code lives in Roblox.Services. If the built assembly has no such method, the running
// site is an older build and no amount of waiting will change the thumbnails.
const checkBuild = () => {
	const candidates = [];
	for (const config of ['Debug', 'Release']) {
		candidates.push(path.join(websiteDir, 'bin', config, 'net6.0', 'Roblox.Services.dll'));
	}
	const found = candidates.filter((f) => fs.existsSync(f));
	if (found.length === 0) return { known: false, message: 'no built Roblox.Services.dll found - has the site been built?' };
	const has = found.filter((f) => fs.readFileSync(f).includes('TryUseClothingTexture'));
	if (has.length > 0) return { known: true, built: true, message: 'built code has the texture path (' + path.relative(repoRoot, has[0]) + ')' };
	return {
		known: true,
		built: false,
		message: 'the built ' + path.relative(repoRoot, found[0]) + ' does NOT contain the texture code - the website needs rebuilding',
	};
};

const exists = (dir, key) => {
	if (!dir || !key || key.includes('/')) return false;
	try { return fs.statSync(path.join(dir, key)).isFile(); } catch (e) { return false; }
};

const main = async () => {
	const dirs = readDirectories();
	const build = checkBuild();

	console.log('Asset directory:      ' + dirs.asset + (fs.existsSync(dirs.asset) ? '' : '   (MISSING)'));
	console.log('Thumbnail directory:  ' + dirs.thumbnails + (fs.existsSync(dirs.thumbnails) ? '' : '   (MISSING)'));
	console.log('Build:                ' + build.message);
	console.log('');

	const client = new pg.Client({
		host: process.env.PGHOST || '127.0.0.1',
		port: Number(process.env.PGPORT) || 5432,
		user: process.env.PGUSER || 'postgres',
		password: process.env.PGPASSWORD,
		database: process.env.PGDATABASE || 'ecsrev',
	});
	await client.connect();
	try {
		const rows = (await client.query(
			'SELECT a.id, a.name, a.asset_type, v.content_id, v.content_url, ' +
			't.content_url AS thumb_url FROM asset a ' +
			'JOIN LATERAL (SELECT content_id, content_url FROM asset_version WHERE asset_id = a.id ORDER BY id DESC LIMIT 1) v ON true ' +
			'LEFT JOIN asset_thumbnail t ON t.asset_id = a.id ' +
			'WHERE a.asset_type = ANY($1) ORDER BY a.id DESC', [[2, 11, 12]])).rows;

		const textureIds = rows.map((r) => r.content_id).filter((id) => id && Number(id) !== 0);
		const textureContent = new Map();
		if (textureIds.length > 0) {
			const versions = (await client.query(
				'SELECT DISTINCT ON (asset_id) asset_id, content_url FROM asset_version ' +
				'WHERE asset_id = ANY($1) ORDER BY asset_id, id DESC', [textureIds])).rows;
			for (const v of versions) textureContent.set(String(v.asset_id), v.content_url);
		}

		if (rows.length === 0) {
			console.log('No Shirt / Pants / T-Shirt assets in this database.');
			return;
		}

		let ready = 0;
		for (const row of rows) {
			const textureKey = row.content_id ? textureContent.get(String(row.content_id)) : null;
			const textureOnDisk = exists(dirs.asset, textureKey);
			const thumbOnDisk = exists(dirs.thumbnails, row.thumb_url);
			const hasThumbRow = Boolean(row.thumb_url);
			if (hasThumbRow && thumbOnDisk) ready++;

			console.log('#' + row.id + '  ' + row.name + '  (type ' + row.asset_type + ')');
			console.log('    texture asset: ' + (row.content_id || 'none') +
				(textureKey ? ' -> ' + textureKey + (textureOnDisk ? ' (on disk)' : ' (FILE MISSING)') : ' (no content_url)'));
			console.log('    thumbnail:     ' + (hasThumbRow
				? row.thumb_url + (thumbOnDisk ? ' (on disk)' : ' (FILE MISSING)')
				: 'none'));
		}

		console.log('');
		console.log(ready + ' of ' + rows.length + ' clothing items have a usable thumbnail file.');
		if (!build.built) console.log('Rebuild the website first - the texture path is not in the running build.');
	} finally {
		await client.end();
	}
};

main().catch((e) => {
	console.error('Failed: ' + e.message);
	process.exit(1);
});
