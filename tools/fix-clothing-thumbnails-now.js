#!/usr/bin/env node
/**
 * Gives clothing items their catalog picture right now, without waiting for the website.
 *
 * A Shirt / Pants / T-Shirt thumbnail is just the item's own texture copied into the thumbnails
 * folder. The website does that on its own every five minutes, but it can only do it for textures
 * that are still on disk - and a lot of these uploads are missing theirs. This script does the
 * same copy, so you can see immediately which items can be fixed and which lost their texture.
 *
 *   set PGPASSWORD=your-password
 *   node tools\fix-clothing-thumbnails-now.js            (dry run, changes nothing)
 *   node tools\fix-clothing-thumbnails-now.js --apply    (copies files and writes the rows)
 *
 * Same overrides as the other tools: PGHOST, PGPORT, PGUSER, PGDATABASE.
 */
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const pgCandidates = ['pg', '../services/api/node_modules/pg', '../services/2016-roblox-main/node_modules/pg'];
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

const apply = process.argv.includes('--apply');
const repoRoot = path.resolve(__dirname, '..');
const websiteDir = path.join(repoRoot, 'services', 'Roblox', 'Roblox.Website');
const apiDir = path.join(repoRoot, 'services', 'api');

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
	const resolve = (value, fallback) => {
		const normalised = (value || fallback).replace(/\\/g, path.sep).replace(/[\\/]$/, '') + path.sep;
		return path.isAbsolute(normalised) ? normalised : path.resolve(websiteDir, normalised);
	};
	return {
		asset: resolve(dirs.Asset, '../../api/storage/asset/'),
		thumbnails: resolve(dirs.Thumbnails, '../../api/public/images/thumbnails/'),
	};
};

/** Every file under services/api, so a texture that was written somewhere unexpected is still found. */
const indexFiles = (root) => {
	const byName = new Map();
	const walk = (dir) => {
		let entries = [];
		try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch (e) { return; }
		for (const entry of entries) {
			const full = path.join(dir, entry.name);
			if (entry.isDirectory()) {
				if (entry.name === 'node_modules' || entry.name === '.git') continue;
				walk(full);
			} else {
				byName.set(entry.name.toLowerCase(), full);
			}
		}
	};
	walk(root);
	return byName;
};

/** Texture files are stored as a bare sha256, but an older upload may have kept the extension. */
const locateTexture = (assetDir, index, key) => {
	if (!key) return null;
	const candidates = [path.join(assetDir, key), path.join(assetDir, key + '.png')];
	for (const candidate of candidates) {
		try { if (fs.statSync(candidate).isFile()) return candidate; } catch (e) { /* keep looking */ }
	}
	return index.get(key.toLowerCase()) || index.get((key + '.png').toLowerCase()) || null;
};

const main = async () => {
	const dirs = readDirectories();
	const index = indexFiles(apiDir);
	console.log('Looking through ' + index.size + ' files under ' + apiDir);
	console.log('Thumbnails go to ' + dirs.thumbnails + (fs.existsSync(dirs.thumbnails) ? '' : '   (MISSING)'));
	console.log(apply ? 'Mode: APPLY - files and rows will be written.' : 'Mode: dry run - nothing is changed. Add --apply to write.');
	console.log('');

	const client = new pg.Client({
		host: process.env.PGHOST || '127.0.0.1',
		port: Number(process.env.PGPORT) || 5432,
		user: process.env.PGUSER || 'postgres',
		password: process.env.PGPASSWORD,
		database: process.env.PGDATABASE || 'ecsrev',
	});

	await client.connect();
	let fixed = 0;
	const lost = [];
	try {
		const rows = (await client.query(
			'SELECT a.id, a.name, a.asset_type FROM asset a ' +
			'LEFT JOIN asset_thumbnail t ON t.asset_id = a.id ' +
			'WHERE a.asset_type = ANY($1) AND t.content_url IS NULL ORDER BY a.id DESC',
			[[2, 11, 12]])).rows;

		for (const row of rows) {
			const version = (await client.query(
				'SELECT id, content_id FROM asset_version WHERE asset_id = $1 ORDER BY id DESC LIMIT 1',
				[row.id])).rows[0];
			if (!version) { lost.push([row, 'no version row']); continue; }

			let textureKey = null;
			if (version.content_id && Number(version.content_id) !== 0) {
				const textureVersion = (await client.query(
					'SELECT content_url FROM asset_version WHERE asset_id = $1 ORDER BY id DESC LIMIT 1',
					[version.content_id])).rows[0];
				textureKey = textureVersion ? textureVersion.content_url : null;
			}
			if (!textureKey) { lost.push([row, 'no texture asset']); continue; }

			const texturePath = locateTexture(dirs.asset, index, textureKey);
			if (!texturePath) { lost.push([row, 'texture ' + textureKey.slice(0, 12) + '... is gone']); continue; }

			const bytes = fs.readFileSync(texturePath);
			const hash = crypto.createHash('sha256').update(bytes).digest('hex');
			const outFile = path.join(dirs.thumbnails, hash + '.png');

			if (apply) {
				if (!fs.existsSync(outFile)) fs.writeFileSync(outFile, bytes);
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
			fixed++;
			console.log('#' + row.id + '  ' + row.name + '  <- ' + path.relative(apiDir, texturePath));
		}

		console.log('');
		console.log((apply ? 'Fixed ' : 'Can fix ') + fixed + ' of ' + rows.length + ' clothing items from their own texture.');
		if (lost.length > 0) {
			console.log('');
			console.log(lost.length + ' item(s) cannot be pictured, their texture is not on disk at all:');
			for (const [row, why] of lost) {
				console.log('  #' + row.id + '  ' + row.name + '  (' + why + ')');
			}
			console.log('Re-uploading those is the only way to get a picture; the files are not recoverable from the database.');
		}
	} finally {
		await client.end();
	}
};

main().catch((e) => {
	console.error('Failed: ' + e.message);
	process.exit(1);
});
