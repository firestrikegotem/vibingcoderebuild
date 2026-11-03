// TODO: This is garbage. RCC managment, image resize/compression, and everything else should be split into their own files
import StdExceptions from '../helpers/Exceptions';
import fs = require('fs');
import path = require('path');
import util = require('util');
import cp = require('child_process');
import pf = require('portfinder');
const sleep = util.promisify(setTimeout);
import os = require('os');
import conf from '../helpers/Config';
import * as models from '../models';
import sharp = require('sharp');
import uuid = require('uuid');
import http = require('http');
let myIp = 'UNKNOWN';
const dockerEnabled: boolean = !conf.dockerDisabled;
console.log('[info] dockerEnabled:',dockerEnabled);
const rccPort = conf.rccPort || 63914; // default: 64989

const isPortAvailable = (port: number): Promise<boolean> => {
	return new Promise((res, rej) => {
		let didAnything = false;
		let timer = setTimeout(() => {
			if (didAnything) return;
			
			console.log('[info] isPortAvailable timeout for',port);
			res(false);
			try {
				server.close();
			}catch(e) {}
			// @ts-ignore
			timer = undefined;
		}, 5 * 1000);
		const server = http.createServer();
		server.on('error', () => {
			if (didAnything) return;
			if (timer) {
				clearTimeout(timer);
			}
			didAnything = true;
			res(false);
		});
		server.on('listening', () => {
			server.close();
			if (didAnything) return;
			console.log('[info] isPortAvailable',port);
			if (timer) {
				clearTimeout(timer);
			}
			didAnything = true;
			sleep(1000).then(() => {
				res(true);
			})
		})
		server.listen(port);
	});
}

// Must stay in step with GameServerPorts in Roblox.Services/Games/GameServer.cs: the website tells
// the client to connect to 127.0.0.1:53640, so RCC has to be the process listening there.
const RCC_PORT_MIN = 53640;
const RCC_PORT_MAX = 53650;

const getFreeRccPort = async (): Promise<number> => {
	for (let i = RCC_PORT_MIN; i < RCC_PORT_MAX; i++) {
		let avail = await isPortAvailable(i);
		console.log('[info] port available?',i,avail);
		if (avail) {
			return i;
		}
	}
	throw new Error('No ports available');
}

import getScripts from '../scripts';
const scripts = getScripts();

import axiosStatic from 'axios';
import { awaitResult, doesCallbackExist, getUploadCallbacks, getResult, resolutionMultiplier } from '../rendering';
import { addMessage } from '../discord';
const axiosClient = axiosStatic.create({
	headers: {
		'user-agent': 'GameServer/1.0',
	}
});

(async() => {
	let result = await axiosClient.get('http://ipv4.icanhazip.com');
	myIp = result.data;
	console.log('[info] server ip:',myIp);
})().catch(e => {
	console.error('[error] could not fetch ip:',e);
});

/**
 * The 2016 RCC build refuses to fetch any URL containing "localhost", and it prefixes bare
 * hosts with "www.", so every script we send has its backend URLs rewritten to config.json's
 * baseUrl. That value must carry the website's port: without one RCC fetches
 * http://<host>/asset/... on port 80, finds no listener, and the job dies with
 * StartProcessException while loading the avatar's clothing.
 */
const rccSafeBaseUrl = (): string => conf.baseUrl;

if (conf.baseUrl && !/:\d+(\/|$)/.test(conf.baseUrl)) {
	console.warn('[warn] config.json baseUrl "' + conf.baseUrl + '" has no port. RCC will fetch assets on port 80 and fail - set it to http://bbblox.org:5000');
}

/**
 * One pass, one regex: chained replace() calls reprocessed their own output, which is how
 * "http://bbblox.org:5000" became "http://bbblox.org:5000:5000". The optional port keeps a
 * host that already carries one from collecting a second.
 */
const backendHostPattern = /https?:\/\/(?:(?:www\.)?(?:economy-simulator\.org|bbblox\.org|bloxyn\.net)|localhost:5000)(?::\d+)?/g;

const localizeScriptUrls = (script: string): string => {
	const base = rccSafeBaseUrl();
	return script
		.replace(backendHostPattern, base)
		// any leftover bare "localhost" (no explicit port), e.g. http://localhost/asset/
		.replace(/https?:\/\/localhost(?![.\w])/g, 'http://127.0.0.1');
};

const maxRendersBeforeRestart = 100;

/**
 * RCCService.exe started with piped stdio exits immediately on Windows (exit code null, no
 * output at all), while the very same command line runs fine from a console. Handing it real
 * file handles instead of pipes keeps it alive and leaves a log we can read when a render fails.
 */
const rccLogPath = (port: number): string => path.join(__dirname, '../../rcc-' + port + '.log');

/**
 * When RCC exits non-zero it dies before answering SOAP, so the job's own prints are lost.
 * They survive in the log file the child writes to, minus the crash-reporter noise, so print
 * the tail here: the game server console is what we actually get to see.
 */
const logRccCrashContext = (port: number): void => {
	try {
		const logPath = rccLogPath(port);
		if (!fs.existsSync(logPath)) return;
		const noisy = (line: string): boolean => {
			const t = line.trim();
			return t === ''
				|| t.startsWith('rl>http')
				|| t.startsWith('httpGet  failed')
				|| t.startsWith('.  Elapsed time')
				|| t.includes('Trust check failed');
		};
		const lines = fs.readFileSync(logPath, 'utf8').split(/\r?\n/).filter(l => !noisy(l));
		const tail = lines.slice(-25);
		if (tail.length === 0) return;
		console.log('[crash] last useful RCC output before it exited:');
		for (const line of tail) console.log('[crash]   ' + line.trim());
	} catch (e) {
		// diagnostics must never break a render
	}
};

const spawnStdioFor = (port: number): cp.StdioOptions => {
	if (os.platform() !== 'win32') return 'pipe';
	try {
		// Truncate: appending across every restart mixed several RCC runs into one file, so a
		// crash tail showed another process's lines and read as nonsense.
		const fd = fs.openSync(rccLogPath(port), 'w');
		return ['ignore', fd, fd];
	} catch (e) {
		return 'pipe';
	}
};

const readRccLogTail = (port: number, lines = 12): string => {
	try {
		const content = fs.readFileSync(rccLogPath(port)).toString();
		return content.trim().split('\n').slice(-lines).join('\n');
	} catch (e) {
		return '';
	}
};

interface IQueueEntry {
	request: string;
	jobId: string;
	createdAt: number;
}

interface IRccConnection {
	close: () => void;
	port: number;
	id: string;
}

interface IGamePlayerEntry {

}

interface IGameEntry {
	rccReference: IRccConnection|null;
	rccClosed: boolean;
	rccFailureReason?: string;
	stdout: string;
	stderr: string;
	exitCode: number|string;

	createdAt: number;
	
	renderCount: number;
	runningGames: number;
	placeId: number;
	port: number;
	maxPlayerCount: number;
	players: IGamePlayerEntry[];
	serverId: string;
	interval: {
		latest: any;
		onEnd: () => void;
	};
}

interface IExecutorStatus {
	games: IGameEntry[];
}
const maxJobQueueRunningCount = (() => {
	if (!dockerEnabled)
		return Math.max(1, Math.trunc(os.cpus().length/2));
	return Math.max(1, Math.trunc(os.cpus().length/2));
})();
console.log('[info] system thread count',maxJobQueueRunningCount);

const truncateForDiscordMessage = (msg: string) => {
	msg = msg.replace(/\*/g, '\\*').replace(/\_/g, '\\_').replace(/\~/g, '\\~').replace(/\`/g, '\\`');
	const maxLen = 500;
	if (msg.length < maxLen)
		return msg;
	return msg.substring(msg.length-maxLen) + '...';
}

/**
 * Handle incoming WS commands
 */
export default class CommandHandler extends StdExceptions {

	private executorStatus: IExecutorStatus = {
		games: [],
	}
	private reservedPorts: number[] = [];
	
	constructor() {
		super();
		this.onStartup();
	}

	private randomId(): string {
		// use UUID for consistency
		return uuid.v4();
		// return require('crypto').randomBytes(32).toString('hex');
	}

	private onStartup() {
		// Kill any RCC instances left behind by a crash or a previous run. Linux uses the sudo
		// helper script; Windows has no sudo and spawnSync returns null streams there, which used
		// to throw a confusing "cannot read properties of undefined" at every startup.
		try {
			if (os.platform() === 'win32') {
				const result = cp.spawnSync('taskkill', ['/IM', 'RCCService.exe', '/F']);
				const output = ((result.stdout?.toString() ?? '') + (result.stderr?.toString() ?? '')).trim();
				console.log('[info] result for stopping rcc', output || 'no leftover RCC processes', result.status ?? 'n/a');
				return;
			}
			const rccStopPath = path.join(__dirname, '../../stop-all-rcc.sh');
			const stopRequest = cp.spawnSync('/usr/bin/sudo', [rccStopPath]);
			console.log('[info] result for stopping rcc', stopRequest.stdout?.toString() ?? '', stopRequest.status ?? 'n/a', stopRequest.stderr?.toString() ?? '');
		}catch(e) {
			console.log('[error] error in OnStartup - could not shut down existing RCC instances (probably not fatal):',e);
		}
	}

	private async startRcc(game: IGameEntry): Promise<void> {
		// isRccReady call is to check if a dev is already running rcc e.g. for debugging.
		if (game.rccReference || ( await this.isRccReady(rccPort))) {
			console.log('[info] rcc already running');
			if (!game.rccReference) {
				game.rccReference = {
					close: () => {},
					port: rccPort,
					id: this.randomId(),
				}
			}
			return;
		}
		console.log('[info] looking for port...');
		// res port timeout = 1m
		let start = Date.now();
		let portToRunOn = await getFreeRccPort();
		let lastRecPort = portToRunOn;
		while (this.reservedPorts.includes(portToRunOn)) {
			console.log('[info] port is already in use',portToRunOn);
			await sleep(1000);
			portToRunOn = await getFreeRccPort();
			let cur = Date.now();
			// reset timer if getFreeRccPort returns a new port
			if (lastRecPort !== portToRunOn) {
				start = Date.now();
				lastRecPort = portToRunOn;
			}
			let diff = cur - start;
			if (diff > 60*1000) {
				console.log('[info] port has been reserved for over 1m despite being available, will use it');
				break
			}
		}
		this.reservedPorts.push(portToRunOn);
		console.log('[info] found port for rcc!',portToRunOn);
		let rcc: cp.ChildProcess;
		const dockerPidFileLocation = path.join(__dirname, '../../rcccid-' + portToRunOn);
		let rccCid: string|undefined;
		// Describes what was launched so a failure can tell the operator what to fix.
		let rccTarget = 'docker start-rcc.sh';
		if (!dockerEnabled) {
			// Default to the RCC folder that sits next to the other services, so a checkout works
			// without editing config.json. Events waiting for players use conf.rcc when set.
			// path.resolve drops any trailing separator a config value may carry: a current
			// directory ending in "\" is passed straight to CreateProcess on Windows and is a
			// classic source of "spawn EFTYPE" complaints.
			const rccPath = path.resolve(conf.rcc || path.join(__dirname, '../../../RCCService'));
			const rccExecutableName = os.platform() === 'win32' ? 'RCCService.exe' : 'rcc';
			const rccExecutable = path.join(rccPath, rccExecutableName);
			// Same invocation the upstream docker script uses: -Port is the SOAP port RCC answers
			// job requests on. Passing -PlaceId instead makes RCC treat the number as its game
			// network port, leaving no SOAP listener and every job with ECONNREFUSED. The place a
			// job needs comes from the job script itself (local placeId = ...), not from here.
			const rccArgs = ['-Console', '-Content', conf.content || path.join(rccPath, 'content'), '-Port', portToRunOn.toString()];
			rccTarget = rccExecutable;
			try {
				rcc = cp.spawn(rccExecutable, rccArgs, {
					cwd: rccPath,
					stdio: spawnStdioFor(portToRunOn),
				});
			} catch (spawnError) {
				const code = (spawnError as NodeJS.ErrnoException)?.code;
				// libuv reports ERROR_BAD_EXE_FORMAT as EFTYPE, and on Windows a direct spawn is
				// refused for some perfectly good binaries. cmd.exe can start the same file, so
				// retry through it rather than failing the render outright.
				if (os.platform() !== 'win32' || code !== 'EFTYPE') throw spawnError;
				console.log('[warning] direct spawn of RCC returned EFTYPE, retrying through cmd.exe:', rccExecutable);
				const quoted = '"' + rccExecutable + '" ' + rccArgs.map(a => /\s/.test(a) ? '"' + a + '"' : a).join(' ');
				rcc = cp.spawn('cmd.exe', ['/d', '/s', '/c', quoted], {
					cwd: rccPath,
					stdio: spawnStdioFor(portToRunOn),
					windowsVerbatimArguments: true,
				});
			}
		}else{
			try {
				fs.unlinkSync(dockerPidFileLocation);
			}catch(e) {}
			rcc = cp.spawn('/usr/bin/sudo', [path.join(__dirname, '../../start-rcc.sh'), portToRunOn.toString()]);
		}
		game.rccClosed = false;
		// A missing or non-executable RCC binary used to surface as an unhandled 'error' event which
		// killed the whole game server. Record a readable reason instead so the HTTP caller and the
		// logs explain what is wrong, and let waitForRcc() fail this one game.
		rcc.on('error', (err: NodeJS.ErrnoException) => {
			const reason = err && err.code === 'ENOENT'
				? `RCCService executable not found at ${rccTarget}. Install RCCService, or point "rcc" in config.json at the folder containing ${os.platform() === 'win32' ? 'RCCService.exe' : 'rcc'}.`
				: `Could not start RCCService (${rccTarget}): ${err?.message ?? err}`;
			console.error('[error] ' + reason);
			game.rccFailureReason = reason;
			game.stderr += reason + '\n';
			game.rccClosed = true;
			this.reservedPorts = this.reservedPorts.filter(v => v !== portToRunOn);
		});
		rcc.on('message', (msg) => {
			console.log(msg);
		});
		rcc.stdout?.on('data', function (data) {
			game.stdout += data.toString();
			if (!game.stdout.endsWith('\n')) {
				game.stdout += '\n';
			}
			console.log('stdout: ' + data.toString());
		});
		rcc.stderr?.on('data', function (data) {
			game.stderr += data.toString();
			if (!game.stderr.endsWith('\n')) {
				game.stderr += '\n';
			}
			console.log('stderr: ' + data.toString());
		});
		rcc.stdout?.resume();
		rcc.stderr?.resume();
		rcc.on('exit', (code) => {
			this.reservedPorts = this.reservedPorts.filter(v => v !== portToRunOn);
			console.log('[info] rcc child process exited with code ', code);
			if (code !== 0 && code !== null) {
				logRccCrashContext(portToRunOn);
				const lastedMilliseconds = ((Date.now() - game.createdAt) / 1000).toFixed(2);
				const msg = `**RCC exited with code ${code}**\nSTDOUT=${truncateForDiscordMessage(game.stdout)}\nSTDERR=${truncateForDiscordMessage(game.stderr)}\nplaceId=${game.placeId}\nserverId=${game.serverId}\ndurationSeconds=${lastedMilliseconds}\nip=${myIp}`;
			}
			setTimeout(() => {
				if (!game.rccClosed) {
					this.executorStatus.games.forEach(v => {
						if (v.rccReference && game.rccReference && v.rccReference.id === game.rccReference.id) {
							// Kill any running game servers
							this.closeGame(v.serverId);
						}
					})
					game.rccClosed = true;
				}
			}, 1000);
		});
		
		game.rccReference = {
			id: this.randomId(),
			port: portToRunOn,
			close: () => {
				try {
					// SIGINT is not honoured by RCCService.exe: it kept running and held the SOAP
					// port, so the next instance died with "Only one usage of each socket address".
					if (os.platform() === 'win32' && rcc.pid)
						cp.spawnSync('taskkill', ['/PID', String(rcc.pid), '/F']);
					else
						rcc.kill('SIGINT');
					game.rccReference = null;
				} catch (e) { }
				if (dockerEnabled) {
					// ID is passed to docker, do regex to prevent arbitrary command execution
					let match = fs.readFileSync(dockerPidFileLocation).toString('utf-8').trim().match(/[a-z0-9]+/g);
					if (!match) {
						console.error('[warning] rcc id does not exist, container will not be deleted');
						return;
					}
					rccCid = match[0];
					console.log('[info] rcc running on docker container:',rccCid);
					if (rccCid) {
						try {
							const rccStopPath = path.join(__dirname, '../../stop-rcc.sh');
							console.log('trying to kill rcc...',rccStopPath,rccCid);
							// cp.execSync('docker kill ' + rccCid);
							let proc = cp.spawn('/usr/bin/sudo',[
								rccStopPath,
								rccCid,
							], {
								stdio: 'inherit',
							});
							proc.on('exit', (c) => {
								console.log('rcc killed with exit code',c);
							});
							console.log('rcc killed');
						}catch(e) {
							console.error('[error] could not kill rcc container',e);
						}
					}
				}
			}
		};

		console.log('[info] waiting for rcc...');
		try {
			await this.waitForRcc(game, portToRunOn);
		}catch(e) {
			// Release the reservation we took for this attempt, otherwise the next render
			// thinks the port is still in use and stalls for a minute before trying again.
			this.reservedPorts = this.reservedPorts.filter(v => v !== portToRunOn);
			if (game.rccReference) {
				game.rccReference.close();
			}
			game.rccReference = null;
			throw e
		}
		console.log('[info] RCC ok');
	}

	protected async isRccReady(port: number) {
		try {
			const result = await axiosClient.request({
				method: 'GET',
				url: 'http://127.0.0.1:' + port + '/',
				headers: {
					'Content-Type': 'text/xml; charset=utf-8',
				},
				validateStatus: () => true,
				timeout: 1000,
			});
			await sleep(1500); // rcc requires a second before it actually replies to commands, even if it responds to our ping
			return true;
		} catch (e) {
			return false;
		}
	}

	protected async waitForRcc(game: IGameEntry, port: number) {
		let start = Date.now();
		do {
			let elapsedSeconds = (Date.now() - start) / 1000;
			if (elapsedSeconds > 10) {
				const tail = readRccLogTail(port);
				throw new Error('Waited over 10 seconds for RCC on port ' + port + ', give up' +
					(tail ? '. RCC said:\n' + tail : '. It printed nothing at all, see ' + rccLogPath(port)));
			}
			if (game.rccClosed) {
				throw new Error(game.rccFailureReason || 'RCC was closed');
			}
			try {
				const result = await axiosClient.request({
					method: 'GET',
					url: 'http://127.0.0.1:' + port + '/',
					headers: {
						'Content-Type': 'text/xml; charset=utf-8',
					},
					validateStatus: () => true,
					timeout: 1000,
				});
				await sleep(1500);
				console.log('[info] rcc ok');
				return
			} catch (e) {
				console.log('[info] rcc not ok', e.message);
				await sleep(250);
			}
		} while (true)
	}

	public async runJob(game: IGameEntry, job: IQueueEntry) {
		if (!game.rccReference) {
			throw new Error('RCC is not configured');
		}
		// send request
		const result = await axiosClient.request({
			method: 'POST',
			url: 'http://127.0.0.1:' + game.rccReference.port + '/',
			headers: {
				'Content-Type': 'text/xml; charset=utf-8',
			},
			data: job.request,
			timeout: 2 * 60 * 1000,
		});
		// wait until over
		// await getResult(job.jobId);
		console.log('[info] got rcc job', job.jobId);
	}

	/**
	 * Get the status of the web server
	 */
	public async status() {
		return {
			status: 'OK',
		}
	}

	private createSoapRequest(script: string, jobId: string): { request: string; jobId: string; createdAt: number; } {
		// RCC must never see localhost in an asset URL, see localizeScriptUrls()
		const scriptToSend = localizeScriptUrls(script)
		.replace(/InsertJobIdHere/g, jobId);
		const xml = `<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <OpenJobEx xmlns="http://roblox.com/">
        <job>
            <id>${jobId}</id>
            <category>0</category>
            <cores>1</cores>
            <expirationInSeconds>43200</expirationInSeconds>
        </job>
        <script>
            <name>GameStart</name>
            <script>
			<![CDATA[
			${scriptToSend}
			]]>
			</script>
        </script>
    </OpenJobEx>
  </soap:Body>
</soap:Envelope>`;
		return {
			request: xml,
			jobId,
			createdAt: Date.now(),
		}
	}

	/**
	 * BatchJobEx runs a script inside an already-open job and returns whatever the script
	 * returns. Used to read a render result back when RCC cannot upload it itself.
	 */
	/** One raw SOAP reply per job, so a rejected BatchJobEx is visible in the console. */
	private soapDiagnostics = new Set<string>();

	private createBatchJobRequest(jobId: string, lua: string): string {
		// BatchJobEx takes the job id as a plain string, not a job struct like OpenJobEx does.
		// Sending a job struct here is what RCC answered with a 500 and an empty body.
		return `<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <BatchJobEx xmlns="http://roblox.com/" jobID="${jobId}">
        <jobID>${jobId}</jobID>
        <script>
            <name>BloxynRenderResult</name>
            <script>
			<![CDATA[
			${lua}
			]]>
			</script>
        </script>
    </BatchJobEx>
  </soap:Body>
</soap:Envelope>`;
	}

	private decodeXmlText(input: string): string {
		return input
			.replace(/&lt;/g, '<')
			.replace(/&gt;/g, '>')
			.replace(/&quot;/g, '"')
			.replace(/&#34;/g, '"')
			.replace(/&apos;/g, "'")
			.replace(/&#39;/g, "'")
			.replace(/&amp;/g, '&');
	}

	/**
	 * The render scripts park their finished payload on the job, so it can be collected here
	 * even when the script's own HTTP upload never completes. Returns null while the render is
	 * still running, or when this RCC build does not support BatchJobEx.
	 */
	private async readJobRenderResult(port: number, jobId: string): Promise<string | null> {
		const lua = [
			'local ok, value = pcall(function()',
			'    local holder = game:GetService("ServerStorage"):FindFirstChild("__BloxynThumbnail")',
			'    if holder ~= nil and holder.Value ~= "" then return holder.Value end',
			'    return _G.__bloxynThumbnail',
			'end)',
			'if not ok or type(value) ~= "string" then return "" end',
			'return value',
		].join('\n');
		const res = await axiosClient.request({
			method: 'POST',
			url: 'http://127.0.0.1:' + port + '/',
			headers: {
				'Content-Type': 'text/xml; charset=utf-8',
			},
			data: this.createBatchJobRequest(jobId, lua),
			validateStatus: () => true,
			timeout: 30 * 1000,
		});
		const raw = typeof res.data === 'string' ? res.data : '';
		if (!this.soapDiagnostics.has(jobId)) {
			this.soapDiagnostics.add(jobId);
			const fault = /<faultstring[^>]*>([\s\S]*?)<\/faultstring>/i.exec(raw);
			const detail = fault
				? 'fault: ' + JSON.stringify(fault[1].substring(0, 300))
				: JSON.stringify(raw.substring(Math.max(0, raw.length - 300)));
			console.log('[debug] soap reply', res.status, detail);
		}
		if (!raw || raw.indexOf('Fault') !== -1)
			return null;
		const decoded = this.decodeXmlText(raw);
		const key = decoded.indexOf('"thumbnail"');
		if (key === -1)
			return null;
		const start = decoded.lastIndexOf('{', key);
		const end = decoded.indexOf('}', key);
		if (start === -1 || end === -1)
			return null;
		return decoded.substring(start, end + 1);
	}

	/** Hand a collected payload to whoever waits for this job, exactly like the HTTP upload. */
	private deliverJobResult(jobId: string, json: string): boolean {
		let parsed: any;
		try {
			parsed = JSON.parse(json);
		} catch (e) {
			return false;
		}
		if (!parsed || typeof parsed.thumbnail !== 'string')
			return false;
		const callbacks = getUploadCallbacks()[jobId];
		this.removeFromRunningJobs(jobId);
		if (!callbacks) {
			console.error('[warning] no job id callback for', jobId);
			return true;
		}
		console.log('[info] collected render result over SOAP for job', jobId);
		for (const item of callbacks)
			item(parsed);
		delete getUploadCallbacks()[jobId];
		return true;
	}

	private pollJobRenderResult(port: number, jobId: string): { cancel: () => void } {
		let cancelled = false;
		let warned = false;
		const startedAt = Date.now();
		const tick = async () => {
			if (cancelled)
				return;
			if (Date.now() - startedAt > 3 * 60 * 1000) {
				console.log('[info] giving up on collecting render result for job', jobId);
				return;
			}
			try {
				const json = await this.readJobRenderResult(port, jobId);
				if (json && this.deliverJobResult(jobId, json)) {
					cancelled = true;
					return;
				}
				if (!json && !warned) {
					warned = true;
					console.log('[info] no render result on the job yet, will keep checking', jobId);
				}
			} catch (e) {
				if (!warned) {
					warned = true;
					console.log('[warn] could not read render result from RCC', jobId, (e as Error)?.message);
				}
			}
			if (!cancelled)
				setTimeout(tick, 3000);
		};
		setTimeout(tick, 3000);
		return {
			cancel: () => {
				cancelled = true;
			},
		};
	}

	private createShutdownRequest(jobId: string): { request: string; jobId: string; } {
		const xml = `<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <CloseJob xmlns="http://roblox.com/" jobID="${jobId}">
		<jobID>${jobId}</jobID>
        <job>
            <id>${jobId}</id>
        </job>
    </CloseJob>
  </soap:Body>
</soap:Envelope>`;
		return {
			request: xml,
			jobId,
		}
	}

	public openJob(game: IGameEntry, timeout: number, port: number, jobRequest: { request: string; jobId: string; }): Promise<void> {
		if (!game.rccReference) {
			throw new Error('RCC is not configured');
		}
		let didTimeout = false;
		return new Promise((res, rej) => {
			const timeoutTimer = setTimeout(() => {
				didTimeout = true;
				rej(new Error('Request timeout'));
			}, timeout);

			axiosClient.request({
				method: 'POST',
				url: 'http://127.0.0.1:' + port + '/',
				headers: {
					'Content-Type': 'text/xml; charset=utf-8',
				},
				data: jobRequest.request,
				timeout: timeout,
			}).then(result => {
				if (didTimeout) return;
				clearTimeout(timeoutTimer);
				// todo: parse response, then check for lua error?
				console.log('RPC Result:',result.data);

				res();
			}).catch(e => {
				if (didTimeout) return;
				clearTimeout(timeoutTimer);

				rej(e);
			})
		});
	}

	public getStatus() {
		const status = this.executorStatus.games.map(v => {
			return {
				id: v.serverId,
				placeId: v.placeId,
				playerCount: v.players.length,
				port: v.port,
				isFull: v.players.length >= v.maxPlayerCount,
			};
		});
		console.log('[info] GS status:',status);
		return {
			data: status,
		};
	}

	public createGameScript(placeId: number, gameId: string, port: number): string {
		// todo: we still need to edit other vars in gameserver.lua
		return scripts.gameServer
			.replace(/local port = 123/g, `local port = ${port}`)
			.replace(/local placeId = 123/g, `local placeId = ${placeId}`)
	}

	public async startGame(placeId: number, id: string, port: number) {
		if (this.executorStatus.games.length >= 5) {
			throw new Error('Already running max games');
		}
		console.log('[info] startGame called with', placeId, id, port);
		try {
			const game = {
				rccClosed: false,
				rccReference: null,
				createdAt: Date.now(),

				runningGames: 0,
				renderCount: 0,
				maxPlayerCount: 10, // TODO: Make this configurable...
				placeId: placeId,
				players: [],
				serverId: id,
				port: port,
				stdout: '',
				stderr: '',
				exitCode: '',
				interval: {
					onEnd: () => {
						console.log('[info] server died or timed out');
						this.closeGame(id);
					},
					latest: setInterval(() => {
						this.isServerAlive(id).then(isAlive => {
							if (isAlive) {
								console.log('[info] server OK');
							} else {
								console.log('[info] server is not alive, killing it...');
								const game = this.executorStatus.games.find(v => v.serverId === id);
								if (game && game.interval) {
									game.interval.onEnd();
								}
							}
						})
					}, 30 * 1000),
				},
			} as IGameEntry;
			this.executorStatus.games.push(game);

			await this.startRcc(game);
			const req = this.createSoapRequest(this.createGameScript(placeId, id, port), id);
			if (!game.rccReference) {
				throw new Error('No RCC Reference after calling startRcc');
			}
			console.log('open job...');
			await this.openJob(game, 30 * 1000, game.rccReference.port, req);
			console.log('job opened');
			// wait a second before saying we're OK - rcc takes a second to open the network
			await sleep(1500);
		} catch (e) {
			await this.closeGame(id);
			throw e;
		}
		return {}
	}

	public async closeGame(serverId: string) {
		let game = this.executorStatus.games.find(v => v.serverId === serverId);
		if (!game) return;
		if (game.players.length > 0) {
			// todo: iterate over players and inform web that these people left the game?
		}
		if (!game.serverId) return;
		try {
			clearInterval(game.interval.latest);
		} catch (e) {
			console.error(e);
		}
		try {
			await axiosClient.post(conf.baseUrl + '/gs/delete', {
				serverId: game.serverId,
				authorization: conf.authorization,
			})
		} catch (e) {
			console.error('[error] could not close server (double)', e.message, e.response?.data);
		}
		try {
			if (game.rccReference) {
				await this.openJob(game, 10 * 1000, game.rccReference.port, this.createShutdownRequest(serverId));
			}
		}catch(e) {
			console.error('[error] could not request rcc to shut down. will ignore.',e.response, e.message);
		}
		// Remove our game entry
		this.executorStatus.games = this.executorStatus.games.filter(v => v.serverId !== serverId);
		console.log('[info] successfully shut down', serverId, 'there are now', this.executorStatus.games.length, 'game instances running');
		// Check for anyone else using our RCC instance.
		// Currently this is useless - we have it setup as one RCC for each game, but this is done to make it easier
		// to expand to multiple games per rcc in the future.
		const gamesRunningOnThisRcc = this.executorStatus.games.filter(v => {
			if (v.rccReference && game && game.rccReference && v.rccReference.id === game.rccReference.id) {
				return true;
			}
			return false;
		});

		if (gamesRunningOnThisRcc.length === 0 && game.rccReference) {
			// RIP Rcc :(
			console.log('[info] There are no game servers left. RCC is being shut down.');
			game.rccReference.close();
		}
	}

	public async isServerAlive(serverId: string): Promise<boolean> {
		let status = this.executorStatus.games.find(v => v.serverId === serverId);
		if (!status) {
			return false;
		}
		try {
			const resp = await axiosClient.post<{ isAlive: boolean; updatedAt: string }>(conf.baseUrl + '/gs/activity', {
				serverId: status.serverId,
				authorization: conf.authorization,
			});
			return resp.data.isAlive;
		} catch (e) {
			// rip
			console.error('[error] could not get gs activity for serverId', status.serverId, e);
			return false;
		}
	}

	public async shutdown(serverId: string): Promise<{}> {
		console.log('[info] call shutdown for', serverId);
		await this.closeGame(serverId);
		return {}
	}

	private JobQueue: IQueueEntry[] = [];
	private RunningJobIds: string[] = [];
	private JobQueueRunningCount = 0;
	private RenderRcc: IGameEntry[] = [];
	public addToQueue(item: IQueueEntry): void {
		this.JobQueue.push(item);
		console.log('[info] addToQueue. queue length =', this.JobQueue.length, 'rcc runner count =', this.JobQueueRunningCount, 'rcc instance count =', this.RenderRcc.length);
		if (this.JobQueueRunningCount < maxJobQueueRunningCount) {
			console.log('[info] start job queue because it is not running');
			this.runJobQueue();
		}
	}

	/** True when something already answers SOAP on the port, i.e. an RCC is already running. */
	private async isRccListening(port: number): Promise<boolean> {
		try {
			await axiosClient.request({
				method: 'GET',
				url: 'http://127.0.0.1:' + port + '/',
				validateStatus: () => true,
				timeout: 1000,
			});
			return true;
		} catch (e) {
			return false;
		}
	}

	private async GetRccForRender(): Promise<IGameEntry> {
		let applicable = this.RenderRcc.filter(v => !v.rccClosed && v.rccReference).sort((a,b) => {
			return a.runningGames > b.runningGames ? 1 : a.runningGames === b.runningGames ? (
				a.renderCount > b.renderCount ? 1 : a.renderCount === b.renderCount ? 0 : -1
			) : -1;
		});
		if (applicable && applicable.length) {
			let i = 0;
			for (const rcc of applicable) {
				console.log('[info] picked RCC instance data: running =',rcc.runningGames, 'renders =',rcc.renderCount, 'created =',rcc.createdAt, 'idx =',i);
				i++;

				if (rcc.renderCount >= maxRendersBeforeRestart) {
					console.log('[info] RCC picked has too many renders, requesting a shutdown',rcc.renderCount,'vs',maxRendersBeforeRestart);
					await this.requestRccThumbnailerClose(rcc);
				}else{
					return rcc;
				}
			}
		}
		// An RCC from an earlier job can still be listening even though its entry was dropped.
		// Starting a second one on the same port only produces "Only one usage of each socket
		// address" and an immediate exit, so reuse whatever is already answering instead.
		if (await this.isRccListening(rccPort)) {
			console.log('[info] an RCC is already listening on', rccPort, '- reusing it instead of starting another');
			const adoptedId = this.randomId();
			const adopted = {
				rccClosed: false,
				rccReference: {
					close: () => {},
					port: rccPort,
					id: this.randomId(),
				},
				createdAt: Date.now(),
				runningGames: 0,
				renderCount: 0,
				maxPlayerCount: 0,
				placeId: 0,
				players: [],
				serverId: adoptedId,
				port: 0,
				stdout: '',
				stderr: '',
				exitCode: '',
				interval: {
					onEnd: () => {},
					latest: setInterval(() => {}, 30 * 1000),
				},
			} as any as IGameEntry;
			this.RenderRcc = this.RenderRcc.filter(v => !v.rccClosed);
			this.RenderRcc.push(adopted);
			return adopted;
		}
		console.log('[info] no applicable RCC instances for render, will create one');
		const id = this.randomId();
		const port = 0;

		// ugly hack to use a fake game to start rcc :(
		// rcc should be startable WITHOUT a game...
		const game = {
			rccClosed: false,
			rccReference: null,
			createdAt: Date.now(),

			runningGames: 0,
			renderCount: 0,
			maxPlayerCount: 0,
			placeId: 0,
			players: [],
			serverId: id,
			port: port,
			stdout: '',
			stderr: '',
			exitCode: '',
			interval: {
				onEnd: () => {
					console.log('[info] server died or timed out (render)');
					this.closeGame(id);
				},
				latest: setInterval(() => {

				}, 30 * 1000),
			},
		} as IGameEntry;
		let start = Date.now();
		// A render request must never take the whole game server down: if RCC cannot start, fail
		// this job with an explanation so the website can show it.
		try {
			await this.startRcc(game);
		} catch (e) {
			await this.closeGame(id);
			throw new Error('Could not start RCC for render: ' + (e as Error)?.message);
		}
		if (!game.rccReference) {
			await this.closeGame(id);
			throw new Error(game.rccFailureReason || 'RCC did not start for render');
		}
		this.RenderRcc.push(game);
		console.log('[info] started a new RCC instance for renders. time =',(Date.now()-start),'ms');
		return game;
	}

	public removeFromRunningJobs(jobId: string): void {
		this.RunningJobIds = this.RunningJobIds.filter(v => v !== jobId);
	}

	private async runJobQueueTask(rcc: IGameEntry, job: IQueueEntry) {
		if (!rcc || !rcc.rccReference) {
			// Skip the job instead of exiting: one dead RCC should not stop every later render.
			console.error('[error] RCC reference is not available - it was probably closed, skipping job', job.jobId);
			return;
		}
		// send request
		const result = await axiosClient.request({
			method: 'POST',
			url: 'http://127.0.0.1:' + rcc.rccReference.port + '/',
			headers: {
				'Content-Type': 'text/xml; charset=utf-8',
			},
			data: job.request,
			timeout: 2 * 60 * 1000,
		});
		// Collect the render over SOAP as well as waiting for the upload: builds whose TrustCheck
		// refuses outbound HTTP leave the script blocked on its own post forever.
		const poller = this.pollJobRenderResult(rcc.rccReference.port, job.jobId);
		// wait until over
		try {
			await awaitResult(job.jobId);
		} finally {
			poller.cancel();
		}
	}
	
	private async requestRccThumbnailerClose(rcc: IGameEntry) {
		if (rcc.rccReference && !rcc.rccClosed) {
			rcc.rccReference.close();
		}
		// remove dead rcc after killing it
		this.RenderRcc = this.RenderRcc.filter(v => v !== rcc);
	}

	public async runJobQueue() {
		this.JobQueueRunningCount++;
		try {
			while (true) {
				// get our job
				let item = this.JobQueue[0];
				if (!item) {
					console.log('[info] runJobQueue empty, no more jobs left');
					return
				}
				// remove from queue, add to running queue
				if (this.RunningJobIds.includes(item.jobId)) {
					console.log('[info] this job is already running. will skip. id =',item.jobId);
					continue;
				}
				this.JobQueue = this.JobQueue.filter(v => v.jobId !== item.jobId);
				this.RunningJobIds.push(item.jobId);
				let rcc: IGameEntry;
				try {
					rcc = await this.GetRccForRender();
				} catch (e) {
					// One render that cannot get an RCC must not take the game server down with it.
					console.error('[error] could not get an RCC for job', item.jobId, '-', (e as Error)?.message);
					this.RunningJobIds = this.RunningJobIds.filter(v => v !== item.jobId);
					continue;
				}
				let msSinceCreation = Date.now() - item.createdAt;
				// because we are an async function, we should be able to guarantee that doesCallbackExist will only ever return false if 2 minutes has passed, not if the callback was never created in the first place.
				if (!doesCallbackExist(item.jobId)) {
					if (msSinceCreation >= 60*1000) {
						console.log('[warn] skipping job',item.jobId,'because a callback for it does not exist and it was created over 1m ago');
						this.RunningJobIds = this.RunningJobIds.filter(v => v !== item.jobId);
						continue;
					}else{
						console.log('[info] doesCallbackExist returned false, but job was created',msSinceCreation,'ms ago, so run it anyway');
					}
				}
				console.log('[info] [jq] run', item.jobId);
				rcc.runningGames++;
				try {
					// start the task
					await this.runJobQueueTask(rcc, item);
					rcc.renderCount++;
				} catch (e) {
					// timeout error or rcc is not responding to our calls, so it must be killed
					if (e && e.isAxiosError && !e.response) {
						await this.requestRccThumbnailerClose(rcc);
					}
					// runJobQueueTask removes us from job queue, so re-add and try again.
					// if the jobId is removed from RunningJobIds, it means that either the
					// render was cancelled or the render was finished before erroring.
					if (this.RunningJobIds.includes(item.jobId)) {
						this.JobQueue = [item, ...this.JobQueue];
						this.RunningJobIds = this.RunningJobIds.filter(v => v !== item.jobId);
					}
					console.error('[error] [jq]', item.jobId, e);
				}finally{
					rcc.runningGames--;
				}
				console.log('[info] [jq] task', item.jobId, 'finished');
			}
		} catch (e) {
			throw e;
		} finally {
			this.JobQueueRunningCount--;
		}
	}

	public async Cancel(jobId: string): Promise<null> {
		if (this.JobQueue.find(v => v.jobId === jobId)){
			this.JobQueue = this.JobQueue.filter(v => v.jobId !== jobId);
		}else{
			// Already ran or currently running?
			this.removeFromRunningJobs(jobId);
		}
		return null;
	}

	public async GenerateThumbnailAsset(assetId: number): Promise<string> {
		const job = this.createSoapRequest(
			scripts.assetThumbnail
				.replace(/\{1234\}/g, `{${assetId}}`)
				.replace(/_X_RES_/g, (420 * resolutionMultiplier.asset).toString())
				.replace(/_Y_RES_/g, (420 * resolutionMultiplier.asset).toString())
		, uuid.v4());
		this.addToQueue(job);
		return (await getResult(job.jobId, resolutionMultiplier.asset)).thumbnail;
	}

	private async GetTeeShirtThumb(assetId: number): Promise<Buffer> {
		// https://example.com
		const result = await axiosClient.get(`${conf.baseUrl}/asset/?id=${assetId}`, {
			responseType: 'arraybuffer',
			headers: {
				// bot-auth is used to bypass moderation status and encryption
				'bot-auth': conf.websiteBotAuth,
			}
		});
		return Buffer.from(result.data, 'binary');
	}
	
	private bgBuffer?: Buffer = undefined;

	public async GenerateThumbnailTeeShirt(assetId: number, contentId: number): Promise<string> {
		if (!this.bgBuffer) {
			this.bgBuffer = fs.readFileSync(path.join(__dirname, '../../TeeShirtTemplate.png'));
			console.log('[info] read teeShirtBgBuffer into memory. size =', this.bgBuffer.length, 'bytes');
		}
		const bg = await sharp(this.bgBuffer);
		const content = await this.GetTeeShirtThumb(contentId);
		const image = await sharp(content).resize(250, 250, {
			fit: 'contain',
		}).png().toBuffer();
		bg.composite([
			{
				top: 85,
				left: 85,
				input: image,
			}
		]);
		const buff = await bg.png().toBuffer();
		return buff.toString('base64');
	}

	public async GenerateThumbnailMesh(assetId: number): Promise<string> {
		const job = this.createSoapRequest(
			scripts.meshThumbnail
				.replace(/\{1234\}/g, `{${assetId}}`)
				.replace(/_X_RES_/g, (420 * resolutionMultiplier.asset).toString())
				.replace(/_Y_RES_/g, (420 * resolutionMultiplier.asset).toString())
		, uuid.v4());
		this.addToQueue(job);
		return (await getResult(job.jobId, resolutionMultiplier.asset)).thumbnail;
	}

	public async GenerateThumbnailHead(assetId: number): Promise<string> {
		const job = this.createSoapRequest(
			scripts.headThumbnail
				.replace(/\{1234\}/g, `{${assetId}}`)
				.replace(/_X_RES_/g, (420 * resolutionMultiplier.asset).toString())
				.replace(/_Y_RES_/g, (420 * resolutionMultiplier.asset).toString())
		, uuid.v4());
		this.addToQueue(job);
		return (await getResult(job.jobId, resolutionMultiplier.asset)).thumbnail;
	}


	public async GenerateThumbnailGame(assetId: number, x = 640, y = 360): Promise<string> {
		const job = this.createSoapRequest(
			scripts.gameThumbnail
				.replace(/\{1234\}/g, `{${assetId}}`)
				.replace(/_X_RES_/g, (x * resolutionMultiplier.game).toString())
				.replace(/_Y_RES_/g, (y * resolutionMultiplier.game).toString())
		, uuid.v4());
		this.addToQueue(job);
		return (await getResult(job.jobId, resolutionMultiplier.game)).thumbnail;
	}

	/**
	 * Generate a thumbnail for a texture (such as a Decal or Texture)
	 * @param assetId 
	 * @returns 
	 */
	public async GenerateThumbnailTexture(assetId: number, assetTypeId: number): Promise<string> {
		const jobRequest = this.createSoapRequest(
			scripts.imageTexture
				.replace(/65789275746246/g, assetId.toString())
				.replace(/358843/g, assetTypeId.toString())
		, uuid.v4())
		this.addToQueue(jobRequest);
		return (await getResult(jobRequest.jobId, 1)).thumbnail;
	}

	/**
	 * Generate a user avatar headshot
	 * @param user 
	 */
	public async GenerateThumbnailHeadshot(user: models.AvatarRenderRequest): Promise<string> {
		console.log(`[info] thumbnail requested`, user);
		const jobRequest = this.createSoapRequest(
			scripts.playerHeadshot
				// set user id
				.replace(/65789275746246/g, user.userId.toString())
				// set user avatar json
				.replace(/JSON_AVATAR/g, JSON.stringify(user).replace(`'`, `\\'`))
				.replace(/_X_RES_/g, (420 * resolutionMultiplier.userHeadshot).toString())
				.replace(/_Y_RES_/g, (420 * resolutionMultiplier.userHeadshot).toString())
		, uuid.v4());
		// create the job
		this.addToQueue(jobRequest);
		// await job
		const result = await getResult(jobRequest.jobId, resolutionMultiplier.userHeadshot);
		// return base64 value
		return result.thumbnail;
	}

	/**
	 * Generate a user avatar thumbnail
	 * @param user 
	 */
	public async GenerateThumbnail(user: models.AvatarRenderRequest): Promise<string> {
		console.log(`[info] thumbnail requested`, user);
		const jobRequest = this.createSoapRequest(
			scripts.playerThumbnail
				// set user id
				.replace(/65789275746246/g, user.userId.toString())
				// set user avatar json
				.replace(/JSON_AVATAR/g, JSON.stringify(user).replace(`'`, `\\'`))
				.replace(/_X_RES_/g, (420 * resolutionMultiplier.userThumbnail).toString())
				.replace(/_Y_RES_/g, (420 * resolutionMultiplier.userThumbnail).toString())
		, uuid.v4());
		// create the job
		this.addToQueue(jobRequest);
		// await job
		const result = await getResult(jobRequest.jobId, resolutionMultiplier.userThumbnail);
		// return base64 value
		return result.thumbnail;
	}

	private async ConvertGeneric(mode: string, base64EncodedFile: string): Promise<string> {
		if (mode !== "convertgame" && mode !== "converthat")
			throw new Error("Bad mode");

		if (dockerEnabled) {
			const scriptPath = path.join(__dirname, '../../start-place-convert.sh');
			// const mode = 'convertgame';
			const dirPath = path.join(__dirname, '../../place-conversion-data');
			if (!fs.existsSync(dirPath)) {
				await fs.promises.mkdir(dirPath);
			}
			// remove anything leftover
			for (const file of await fs.promises.readdir(dirPath)) {
				fs.rmSync(dirPath + '/' + file);
			}
			// write in file
			const inLocation = path.join(dirPath, './in.rbxl');
			const outLocation = path.join(dirPath, './out.rbxl');

			await fs.promises.writeFile(inLocation, Buffer.from(base64EncodedFile, 'base64'));
			// now exec script
			return new Promise((res, rej) => {
				let exec = cp.spawn('/usr/bin/sudo', [scriptPath, mode], {
					stdio: 'inherit',
				});
				exec.on('message', (msg) => {
					console.log('[PlaceConvert]',msg);
				})

				exec.on('exit', (code) => {
					if (code === 0) {
						// read the output
						fs.readFile(outLocation, (err, data) => {
							if (err) {
								return rej(err);
							}
							// delete files
							fs.rmSync(inLocation);
							fs.rmSync(outLocation);
							// return the place as a base64 string
							res(data.toString('base64'));
						});
					}else{
						rej(new Error('Failed with exit code ' + code))
					}
				})
			});
		} else {
			// write to disk
			const p = path.join(__dirname, '../../tmp_place_file.rbxl');
			const out = path.join(__dirname, '../../tmp_place_file_out.rbxl');
			// delete if exists
			try {
				fs.unlinkSync(p);
			} catch (e) { }
			try {
				fs.unlinkSync(out);
			} catch (e) { }
			// Write the base64 place to disk for placeconverter to read...
			await fs.promises.writeFile(p, Buffer.from(base64EncodedFile, 'base64'));
			// confusing argument order: first is output, second is input
			const cmd = `./RobloxPlaceConverter.exe ${mode === "convertgame" ? "game" : "hat"} "${out}" "${p}"`;
			return new Promise((res, rej) => {
				cp.exec(cmd, (err) => {
					if (err) {
						return rej(err);
					}
					// read the output
					fs.readFile(out, (err, data) => {
						if (err) {
							return rej(err);
						}
						// return the place as a base64 string
						res(data.toString('base64'));
					});
				})
			});
		}
	}

	public async ConvertRobloxPlace(placeBase64Encoded: string): Promise<string> {
		return await this.ConvertGeneric('convertgame', placeBase64Encoded);
	}

	public async ConvertHat(hatBase64Encoded: string): Promise<string> {
		return await this.ConvertGeneric('converthat', hatBase64Encoded);
	}
}