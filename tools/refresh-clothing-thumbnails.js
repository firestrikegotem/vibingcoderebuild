#!/usr/bin/env node
/**
 * Drops the stale Shirt / Pants / T-Shirt thumbnails so the website draws them again.
 *
 * Clothing items were given thumbnails by a renderer that could not load them, which is why the
 * catalog showed a plain grey torso (or nothing at all). Their pictures are their own textures,
 * which the site now uses directly - but only for items it has to draw from scratch. Deleting the
 * rows puts them back in the queue the five-minute thumbnail fix loop works through.
 *
 * Run it with the same database settings the website uses:
 *
 *   set PGPASSWORD=your-password
 *   node tools\refresh-clothing-thumbnails.js
 *
 * With --redraw it only clears items whose texture is still on disk, so the site redraws the ones it
 * can actually draw (the others would only burn renders and come back blank again).
 *
 * Optional overrides: PGHOST, PGPORT, PGUSER, PGDATABASE.
 */
'use strict';

const pgCandidates = [
	'pg',
	'../services/api/node_modules/pg',
	'../services/2016-roblox-main/node_modules/pg',
	'../services/Roblox/node_modules/pg',
];

let pg = null;
for (const candidate of pgCandidates) {
	try {
		pg = require(candidate);
		break;
	} catch (e) {
		// try the next location
	}
}

if (pg === null) {
	console.error('Could not find a postgres client. Install one with "npm i pg" and run this again.');
	process.exit(1);
}

if (!process.env.PGPASSWORD) {
	console.error('Set the database password first:  set PGPASSWORD=your-password');
	process.exit(1);
}

const config = {
	host: process.env.PGHOST || '127.0.0.1',
	port: Number(process.env.PGPORT) || 5432,
	user: process.env.PGUSER || 'postgres',
	password: process.env.PGPASSWORD,
	database: process.env.PGDATABASE || 'ecsrev',
};

// T-Shirt 2, Shirt 11, Pants 12.
const CLOTHING_TYPES = [2, 11, 12];

const redrawOnly = process.argv.includes('--redraw');

/**
 * The texture files the items point at, for --redraw: an item with a missing texture cannot be
 * redrawn into anything, so clearing its thumbnail just loses the placeholder it has.
 */
const texturesOnDisk = async (client) => {
	const dirs = (() => {
		const fs = require('fs');
		const path = require('path');
		const file = path.resolve(__dirname, '..', 'services', 'Roblox', 'Roblox.Website', 'appsettings.json');
		try {
			const raw = fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '');
			const configured = (JSON.parse(raw).Directories || {}).Asset || '../../api/storage/asset/';
			const normalised = configured.replace(/\\/g, path.sep).replace(/[\\/]$/, '') + path.sep;
			return path.isAbsolute(normalised) ? normalised : path.resolve(path.dirname(file), normalised);
		} catch (e) {
			return null;
		}
	})();
	if (!dirs) return null;
	const fs = require('fs');
	const path = require('path');
	const rows = (await client.query(
		'SELECT a.id, v.content_id FROM asset a ' +
		'JOIN LATERAL (SELECT content_id FROM asset_version WHERE asset_id = a.id ORDER BY id DESC LIMIT 1) v ON true ' +
		'WHERE a.asset_type = ANY($1)', [CLOTHING_TYPES])).rows;
	const usable = [];
	for (const row of rows) {
		if (!row.content_id) continue;
		const texture = (await client.query(
			'SELECT content_url FROM asset_version WHERE asset_id = $1 ORDER BY id DESC LIMIT 1', [row.content_id])).rows[0];
		if (!texture || !texture.content_url || texture.content_url.includes('/')) continue;
		try {
			if (fs.statSync(path.join(dirs, texture.content_url)).isFile()) usable.push(row.id);
		} catch (e) { /* texture is gone */ }
	}
	return usable;
};

const report = async (client) => {
	const result = await client.query(
		'SELECT a.id, a.name, a.asset_type FROM asset a ' +
		'LEFT JOIN asset_thumbnail t ON t.asset_id = a.id ' +
		'WHERE a.asset_type = ANY($1) AND t.content_url IS NULL ORDER BY a.id DESC',
		[CLOTHING_TYPES]);
	if (result.rowCount === 0) {
		console.log('Every clothing item has a thumbnail.');
		return;
	}
	console.log(result.rowCount + ' clothing item(s) still waiting to be drawn. The site fills these in as it works through them:');
	for (const row of result.rows) {
		console.log('  #' + row.id + '  ' + row.name + '  (type ' + row.asset_type + ')');
	}
};

const main = async () => {
	const client = new pg.Client(config);
	await client.connect();
	try {
		let cleared;
		if (redrawOnly) {
			const usable = await texturesOnDisk(client);
			if (usable === null) {
				console.error('Could not read the asset directory from appsettings.json, so --redraw has nothing to check.');
				process.exit(1);
			}
			cleared = usable.length === 0
				? { rowCount: 0 }
				: await client.query('DELETE FROM asset_thumbnail WHERE asset_id = ANY($1)', [usable]);
			console.log('Cleared ' + cleared.rowCount + ' clothing thumbnail(s) that can be drawn from their texture.');
			console.log(usable.length + ' item(s) have a texture on disk; the rest were left alone.');
		} else {
			cleared = await client.query(
				'DELETE FROM asset_thumbnail WHERE asset_id IN (' +
				'SELECT id FROM asset WHERE asset_type = ANY($1))', [CLOTHING_TYPES]);
			console.log('Cleared ' + cleared.rowCount + ' clothing thumbnail(s).');
		}
		console.log('Give the fix loop a couple of minutes, then reload the page.');
		await report(client);
	} finally {
		await client.end();
	}
};

main().catch((e) => {
	console.error('Failed: ' + e.message);
	if (String(e.message).toLowerCase().includes('password')) {
		console.error('Check PGPASSWORD, and that the database name matches the one the website uses.');
	}
	process.exit(1);
});
