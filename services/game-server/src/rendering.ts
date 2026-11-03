import sharp = require("sharp");

// Poor man's anti-aliasing. These multiply the 420px base size the scripts render at.
// RCC's software renderer (GraphicsMode 3, Mesa llvmpipe) returns a real PNG at 420 but
// exits with code 1 at 1680, so avatar renders default to the size that is proven to work
// and can be raised again per renderer without touching code:
//   RENDER_MULT_USER_THUMBNAIL=2 npm run start
const multiplier = (envName: string, fallback: number): number => {
	const raw = process.env[envName];
	const parsed = raw === undefined ? Number.NaN : Number(raw);
	return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
};

export const resolutionMultiplier = {
	game: multiplier('RENDER_MULT_GAME', 4),
	asset: multiplier('RENDER_MULT_ASSET', 1),
	userThumbnail: multiplier('RENDER_MULT_USER_THUMBNAIL', 1),
	userHeadshot: multiplier('RENDER_MULT_USER_HEADSHOT', 1),
};

let uploadCallbacks: Record<string, Array<(args: any) => void>> = {};
// Set when a job can no longer produce anything, so the caller falls back at once instead of
// waiting out the two minute timeout on a render that already died.
const jobFailures: Record<string, (reason: string) => void> = {};
export const failJob = (key: string, reason: string): void => {
	const reject = jobFailures[key];
	if (reject) {
		delete jobFailures[key];
		reject(reason);
	}
};
export const doesCallbackExist = (id: string): boolean => {
	return uploadCallbacks[id] !== undefined;
}
export const getUploadCallbacks = () => uploadCallbacks;
export const awaitResult = (key: string): Promise<void> => {
	return new Promise((res, rej) => {
		let cb = async () => {
			clearTimeout(interval);
			res();
		}
		let interval = setTimeout(() => {
			rej('Timeout');
			if (!uploadCallbacks[key])
				return;
			uploadCallbacks[key] = uploadCallbacks[key].filter(v => v !== cb);
			delete uploadCallbacks[key];
		}, 2 * 60 * 1000);
		if (!uploadCallbacks[key]) {
			uploadCallbacks[key] = [];
		}
		uploadCallbacks[key].push(cb);
	});
}

let shutdownTimer: any;
export const getResult = (key: string, upscaleAmount: number): Promise<any> => {
	return new Promise((res, rej) => {
		jobFailures[key] = (reason: string) => {
			clearTimeout(interval);
			delete uploadCallbacks[key];
			rej(reason);
		};
		let interval = setTimeout(() => {
			rej('Timeout');
			delete uploadCallbacks[key];
			delete jobFailures[key];
		}, 2 * 60 * 1000);
		if (!uploadCallbacks[key]) {
			uploadCallbacks[key] = [];
		}
		uploadCallbacks[key].push(async (data) => {
			clearTimeout(interval);
			delete jobFailures[key];
			if (typeof data.thumbnail === 'string') {
				if (!shutdownTimer) {
					console.log('[info] creating shutdown timer');
					shutdownTimer = setTimeout(() => {
						process.exit(0);
					}, 24 * 60 * 60 * 1000);
				}
				let originalImage = await sharp(Buffer.from(data.thumbnail, 'base64')).metadata();
				if (typeof originalImage.width !== 'number' || typeof originalImage.height !== 'number') {
					throw new Error('Bad image metadata: ' + JSON.stringify(originalImage));
				}
				let image = await sharp(Buffer.from(data.thumbnail, 'base64')).resize(Math.trunc(originalImage.width / upscaleAmount), Math.trunc(originalImage.height / upscaleAmount))
				
				.png({
					compressionLevel: 9,
					quality: 99,
					effort: 10,
				})
				
				.toBuffer();
				data.thumbnail = image.toString('base64');
			}

			res(data);
		})
	});
}