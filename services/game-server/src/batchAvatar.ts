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

/**
 * Both job calls carry the script's return value in the reply; the difference is the year.
 * 2016 RCC implements BatchJobEx and answers "Method 'rob:BatchJob' not implemented" for
 * BatchJob, while a 2018+ build is the other way round.
 */
export function buildBatchJobXml(namespace: string, expirationSeconds: number, script: string, includeCategory = false, operation = 'BatchJob'): string {
	// CDATA cannot carry the sequence "]]>" - guard it so a script can never break the envelope.
	const safeScript = script.replace(/]]>/g, ']]]]><![CDATA[>');
	return `<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:rob="${namespace}">
  <soap:Body>
    <rob:${operation}>
      <rob:job>
        <rob:id>${randomJobId()}</rob:id>${includeCategory ? '\n        <rob:category>0</rob:category>' : ''}
        <rob:expirationInSeconds>${expirationSeconds}</rob:expirationInSeconds>
        <rob:cores>1</rob:cores>
      </rob:job>
      <rob:script>
        <rob:name>avatar</rob:name>
        <rob:script><![CDATA[${safeScript}]]></rob:script>
      </rob:script>
    </rob:${operation}>
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

export async function postBatchJob(port: number, script: string, timeoutSeconds = 120, operations: string[] = ['BatchJobEx', 'BatchJob']): Promise<{ value: string | null, reply: string }> {
	let lastReply = '';
	// Each failed attempt is one immediate request, so sweep operations, namespaces and the
	// category shape rather than guessing which combination this binary wants.
	for (const operation of operations) {
		for (const includeCategory of [false, true]) {
			for (const namespace of namespaceCandidates()) {
				try {
					const xml = buildBatchJobXml(namespace, timeoutSeconds, script, includeCategory, operation);
					const response = await axios.post(`http://127.0.0.1:${port}/`, xml, {
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
					const value = extractBatchJobValue(reply);
					if (value && value.startsWith(PNG_PREFIX)) {
						console.log('[info] ' + operation + ' returned an image using the ' + namespace + ' namespace' + (includeCategory ? ' and a job category' : ''));
						return { value, reply };
					}
					if (reply.includes(operation + 'Result')) {
						// The operation exists and ran; another shape would only repeat it.
						lastReply = value ?? reply.slice(0, 300);
						return { value, reply };
					}
					const fault = extractFault(reply);
					lastReply = 'HTTP ' + response.status + ' (' + operation + ', ' + namespace + ') ' + (fault ?? reply.slice(0, 200));
				} catch (e: any) {
					lastReply = e?.message ?? String(e);
					if (e?.code === 'ECONNREFUSED' || e?.code === 'ECONNRESET') {
						// The process is gone; another shape will not help.
						return { value: null, reply: lastReply };
					}
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
/**
 * Roblox's own 2016 avatar script, with the placeholders filled in. Its last line returns
 * ThumbnailGenerator:Click with five arguments, which this binary rejects, so that line is
 * swapped for a call that tries the four-argument form first. The return value is what the
 * job hands back, so it is the image itself.
 */
export function legacyScript(appearanceUrl: string, baseUrl: string, width: number, height: number): string {
	if (officialScript == null) {
		officialScript = fs.readFileSync(path.join(__dirname, '../scripts/player/avatarOfficial2016.lua')).toString();
	}
	// The placeholders sit on the right of a Lua assignment, so the text values have to be
	// quoted literals, not raw URLs (raw would be a syntax error at the first slash).
	const quoted = (value: string) => '"' + value.replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
	const filled = officialScript
		.replace(/%baseUrl%/g, quoted(baseUrl))
		.replace(/%characterAppearanceUrl%/g, quoted(appearanceUrl))
		.replace(/%fileExtension%/g, quoted('png'))
		.replace(/%x%/g, String(width))
		.replace(/%y%/g, String(height));

	const clickCall = 'return game:GetService("ThumbnailGenerator"):Click(fileExtension, x, y, true, true)';
	if (!filled.includes(clickCall)) {
		throw new Error('avatarOfficial2016.lua no longer ends with the expected click call');
	}
	const wrapper = `local thumbnailGenerator = game:GetService("ThumbnailGenerator")
-- Software GL: the process dies on the click with the other backends on this machine.
thumbnailGenerator.GraphicsMode = 3
print('[debug] graphics mode', thumbnailGenerator.GraphicsMode)
local function __bloxynClick()
    -- This binary fails the first click after RCC starts and returns error text instead of
    -- throwing, so warm it up and check that the result really is PNG data.
    pcall(function() return thumbnailGenerator:Click(fileExtension, 1, 1, true) end)
    local forms = {
        function() return thumbnailGenerator:Click(fileExtension, x, y, true) end,
        function() return thumbnailGenerator:Click(fileExtension, x, y, true, true) end,
    }
    for attempt = 1, 3 do
        for index, click in ipairs(forms) do
            local ok, result = pcall(click)
            if ok and type(result) == 'string' and string.sub(result, 1, 5) == 'iVBOR' then
                print('[debug] render ok with the ' .. (index == 1 and 'four' or 'five') .. ' argument click, bytes', string.len(result))
                return result
            end
            print('[debug] click form', index, 'returned', ok, tostring(result))
        end
        wait(0.5)
    end
    return nil
end
return __bloxynClick()`;
	return filled.replace(clickCall, wrapper);
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
	// BatchJobEx is the operation a 2016 build implements; BatchJob arrived later.
	const operations = mode === 'modern' ? ['BatchJob'] : ['BatchJobEx', 'BatchJob'];

	for (let i = 0; i < order.length; i++) {
		console.log('[info] avatar render attempt with the ' + labels[i] + ' job on port ' + port);
		const { value, reply } = await postBatchJob(port, order[i](), 120, operations);
		if (value && value.startsWith(PNG_PREFIX)) {
			console.log('[info] ' + labels[i] + ' job returned an image, ' + value.length + ' base64 chars');
			return value;
		}
		console.log('[warn] ' + labels[i] + ' job produced no image: ' + (value ?? reply).slice(0, 300));
	}
	return null;
}
