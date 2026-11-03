/**
 * Avatar rendering the way the Korone renderer does it: one BatchJob call.
 *
 * The old path opened a job, waited for a script to POST the image back, and polled the
 * job over SOAP. BatchJob does the whole thing in one request - RCC runs the script and
 * hands back its return value, which for a thumbnail is the base64 PNG. Nothing to poll,
 * no upload route, no TrustCheck on the post, and no place-script wrapper around it.
 *
 * Two script shapes are supported because RCC changed how it takes thumbnails:
 *   2018+  a JSON settings document: {"Settings":{"Type":"Avatar","Arguments":[...]}}
 *   2016   Roblox's own Lua avatar script, with the %placeholders% filled in
 * Both are tried in the order set by AVATAR_RENDER_MODE, so the same build works whether
 * the RCC folder is the 2016 one or a newer one dropped in later.
 */
import axios from 'axios';
import path = require('path');
import fs = require('fs');
import conf from './helpers/Config';

const PNG_PREFIX = 'iVBOR';

/** BatchJob, not OpenJobEx: the reply carries the script's return value. */
export function buildBatchJobXml(namespace: string, expirationSeconds: number, script: string, includeCategory = false): string {
	// CDATA cannot carry the sequence "]]>" - guard it so a script can never break the envelope.
	const safeScript = script.replace(/]]>/g, ']]]]><![CDATA[>');
	return `<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:rob="${namespace}">
  <soap:Body>
    <rob:BatchJob>
      <rob:job>
        <rob:id>${randomJobId()}</rob:id>${includeCategory ? '\n        <rob:category>0</rob:category>' : ''}
        <rob:expirationInSeconds>${expirationSeconds}</rob:expirationInSeconds>
        <rob:cores>1</rob:cores>
      </rob:job>
      <rob:script>
        <rob:name>avatar</rob:name>
        <rob:script><![CDATA[${safeScript}]]></rob:script>
      </rob:script>
    </rob:BatchJob>
  </soap:Body>
</soap:Envelope>`;
}

function randomJobId(): string {
	const hex = () => Math.floor(Math.random() * 0x10000).toString(16).padStart(4, '0');
	return `${hex()}${hex()}-${hex()}-${hex()}-${hex()}-${hex()}${hex()}${hex()}`;
}

/**
 * The image lives in BatchJobResponse/BatchJobResult/value. Read it without pulling in an
 * XML parser for one string: the value is always the last base64 blob in the reply.
 */
export function extractBatchJobValue(response: string): string | null {
	const matches = response.match(/<[^>]*value>([A-Za-z0-9+/=\s]+)<\/[^>]*value>/g);
	if (!matches) return null;
	for (let i = matches.length - 1; i >= 0; i--) {
		const inner = matches[i].replace(/^<[^>]*>/, '').replace(/<\/[^>]*>$/, '').replace(/\s+/g, '');
		if (inner.startsWith(PNG_PREFIX)) return inner;
	}
	// No PNG: hand back whatever error text RCC produced so the caller can log it.
	const text = matches[matches.length - 1].replace(/^<[^>]*>/, '').replace(/<\/[^>]*>$/, '');
	return text.length ? text : null;
}

/** The namespace RCC expects is baked into its build. Try the usual suspects. */
function namespaceCandidates(): string[] {
	const list = ['http://roblox.com/'];
	try {
		const base = conf.baseUrl.replace(/\/$/, '');
		if (base) {
			list.push(base + '/');
			list.push(base);
			// AppSettings.xml usually carries the origin without the website's port, and RCC
			// bakes that value in as its SOAP namespace.
			const origin = base.replace(/:\d+$/, '');
			list.push(origin + '/');
			list.push(origin);
		}
	} catch (e) {}
	list.push('http://www.roblox.com/');
	return Array.from(new Set(list));
}

export async function postBatchJob(port: number, script: string, timeoutSeconds = 120): Promise<{ value: string | null, reply: string }> {
	let lastReply = '';
	// Some builds insist on a category on the job and answer 500 without it, so try both
	// shapes against each candidate namespace. A 500 costs nothing; each returns immediately.
	for (const includeCategory of [false, true]) {
		for (const namespace of namespaceCandidates()) {
			try {
				const response = await axios.post(`http://127.0.0.1:${port}/`, buildBatchJobXml(namespace, timeoutSeconds, script, includeCategory), {
					headers: {
						'Content-Type': 'text/xml; charset=utf-8',
						'user-agent': 'GameServer/1.0',
					},
					timeout: timeoutSeconds * 1000,
					responseType: 'text',
					transformResponse: [(data) => data],
					// Read the body even on a fault: RCC explains itself in the SOAP fault string.
					validateStatus: () => true,
				});
				const reply = typeof response.data === 'string' ? response.data : String(response.data);
				lastReply = reply;
				const value = extractBatchJobValue(reply);
				if (value && value.startsWith(PNG_PREFIX)) {
					console.log('[info] BatchJob accepted the ' + namespace + ' namespace' + (includeCategory ? ' with a job category' : ''));
					return { value, reply };
				}
				if (reply.includes('BatchJobResult')) {
					// The script ran; a different namespace would only repeat the same result.
					return { value, reply };
				}
				const fault = extractFault(reply);
				lastReply = 'HTTP ' + response.status + (fault ? ' - ' + fault : ' - ' + reply.slice(0, 200));
			} catch (e: any) {
				lastReply = e?.message ?? String(e);
				if (e?.code === 'ECONNREFUSED' || e?.code === 'ECONNRESET') {
					// The process is gone; another namespace will not help.
					return { value: null, reply: lastReply };
				}
			}
		}
	}
	return { value: null, reply: lastReply };
}

/** SOAP faults carry the useful sentence; log that instead of a wall of XML. */
export function extractFault(reply: string): string | null {
	const match = reply.match(/<[^>]*faultstring[^>]*>([\s\S]*?)<\/[^>]*faultstring>/i)
		|| reply.match(/<[^>]*faultcode[^>]*>([\s\S]*?)<\/[^>]*faultcode>/i);
	if (!match) return null;
	return match[1].replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 300);
}

let officialScript: string | null = null;
function legacyScript(appearanceUrl: string, baseUrl: string, width: number, height: number): string {
	if (officialScript == null) {
		officialScript = fs.readFileSync(path.join(__dirname, '../scripts/player/avatarOfficial2016.lua')).toString();
	}
	return officialScript
		.replace(/%baseUrl%/g, baseUrl)
		.replace(/%characterAppearanceUrl%/g, appearanceUrl)
		.replace(/%fileExtension%/g, 'png')
		.replace(/%x%/g, String(width))
		.replace(/%y%/g, String(height));
}

function modernScript(appearanceUrl: string, baseUrl: string, width: number, height: number, rig: string): string {
	const type = rig === 'R15' ? 'Avatar_R15_Action' : 'Avatar';
	const args = rig === 'R15'
		? [baseUrl, appearanceUrl, 'PNG', width, height, true, 30, 100, 0, 0]
		: [appearanceUrl, baseUrl, 'PNG', width, height];
	return JSON.stringify({ Settings: { Type: type, Arguments: args } });
}

export type AvatarRenderInput = {
	userId: number;
	width: number;
	height: number;
	rig?: string;
};

/**
 * Ask RCC for an avatar PNG and return it base64 encoded, or null if it produced nothing.
 */
export async function renderAvatarOnRcc(port: number, input: AvatarRenderInput): Promise<string | null> {
	const baseUrl = conf.baseUrl.replace(/\/$/, '');
	const appearanceUrl = `${baseUrl}/Asset/CharacterFetch.ashx?userId=${input.userId}`;
	const legacy = () => legacyScript(appearanceUrl, baseUrl, input.width, input.height);
	const modern = () => modernScript(appearanceUrl, baseUrl, input.width, input.height, input.rig ?? 'R6');

	const mode = (process.env.AVATAR_RENDER_MODE ?? 'legacy').toLowerCase();
	const order = mode === 'modern' ? [modern] : mode === 'both' ? [legacy, modern] : [legacy];
	const labels = mode === 'modern' ? ['modern'] : mode === 'both' ? ['legacy', 'modern'] : ['legacy'];

	for (let i = 0; i < order.length; i++) {
		console.log('[info] avatar render attempt with the ' + labels[i] + ' job on port ' + port);
		const { value, reply } = await postBatchJob(port, order[i]());
		if (value && value.startsWith(PNG_PREFIX)) {
			console.log('[info] ' + labels[i] + ' job returned an image, ' + value.length + ' base64 chars');
			return value;
		}
		console.log('[warn] ' + labels[i] + ' job produced no image: ' + (value ?? reply).slice(0, 300));
	}
	return null;
}
