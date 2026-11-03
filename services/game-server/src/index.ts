import cp = require('child_process');
import scripts from './scripts';
import server from './server';

/**
 * A crashed RCC leaves RCCService.exe behind holding the SOAP port, and the next render then
 * gets "Only one usage of each socket address" or an instant "Service Started then
 * StartProcessException". Clear them at boot so each session starts from the same state.
 */
function clearStrayRccProcesses(): void {
	if (process.platform !== 'win32') {
		return;
	}
	try {
		const out = cp.execSync('taskkill /F /IM RCCService.exe', { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
		console.log('[info] cleared stray RCC processes:', out.trim().split('\n').join(' '));
	} catch (e) {
		// "not found" simply means nothing was left over.
		console.log('[info] no stray RCC processes to clear');
	}
}

/**
 * Print a fingerprint of the render scripts. Twice now a run was spent debugging behaviour
 * that turned out to be an older build, so the log says what is actually loaded.
 */
function logBuildFingerprint(): void {
	const loaded = scripts();
	const thumbnail = loaded.playerThumbnail ?? '';
	const headshot = loaded.playerHeadshot ?? '';
	const markers = [
		'full-body camera: ' + (thumbnail.includes('look at the body') ? 'yes' : 'NO'),
		'job held open: ' + (thumbnail.includes('for _ = 1, 120 do wait(0.5)') ? 'yes' : 'NO'),
		'headshot held open: ' + (headshot.includes('for _ = 1, 120 do wait(0.5)') ? 'yes' : 'NO'),
		'http post: ' + (/local TRY_HTTP_POST = true/.test(thumbnail) ? 'on' : 'off (reading back over SOAP)'),
	];
	console.log('[info] render scripts loaded - thumbnail', thumbnail.length, 'bytes, headshot', headshot.length, 'bytes');
	console.log('[info] ' + markers.join(' | '));
}

clearStrayRccProcesses();
logBuildFingerprint();
server();
