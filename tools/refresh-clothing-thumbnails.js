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
		const cleared = await client.query(
			'DELETE FROM asset_thumbnail WHERE asset_id IN (' +
			'SELECT id FROM asset WHERE asset_type = ANY($1))', [CLOTHING_TYPES]);
		console.log('Cleared ' + cleared.rowCount + ' clothing thumbnail(s). Give the fix loop a couple of minutes, then reload the page.');
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
