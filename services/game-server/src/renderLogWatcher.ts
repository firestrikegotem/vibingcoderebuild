import fs = require('fs');
import { RenderLogPayload } from './renderLogPayload';

/**
 * Watches an RCC log file for the render a job printed into it.
 *
 * This exists because of how little a job script can reach: no io, no writefile, and this
 * build's HttpService:PostAsync kills the process outright. print() is what is left, and RCC
 * writes it to this file.
 *
 * The file is re-read from the last offset every tick, so the payload is picked up as it is
 * written rather than after RCC exits - which matters, because it exits about a second later.
 */
export const watchRenderLog = (
	logPath: string,
	jobId: string,
	onComplete: (base64Image: string) => void,
	intervalMs = 200
): { cancel: () => void } => {
	const collector = new RenderLogPayload(jobId);
	let offset = 0;
	let delivered = false;

	const timer = setInterval(() => {
		if (delivered) {
			clearInterval(timer);
			return;
		}
		try {
			if (!fs.existsSync(logPath)) return;
			const size = fs.statSync(logPath).size;
			// RCC truncates the file on some starts; go back to the top if it shrank.
			if (size < offset) offset = 0;
			if (size <= offset) return;
			const length = size - offset;
			const buffer = Buffer.alloc(length);
			const fd = fs.openSync(logPath, 'r');
			try {
				fs.readSync(fd, buffer, 0, length, offset);
			} finally {
				fs.closeSync(fd);
			}
			offset = size;
			const collected = collector.feed(buffer.toString('utf8'));
			if (!collected) return;
			// The script hex encoded the base64 string it got from Click, so decoding gives that
			// same base64 text back. Decoding to base64 here would encode it a second time.
			const image = collector.format === 'hex'
				? Buffer.from(collected, 'hex').toString('utf8')
				: collected;
			// Not collector.done: that flips true the instant the image is complete, so testing it
			// here was silently discarding every finished payload.
			delivered = true;
			onComplete(image);
		} catch (e) {
			// The next tick reads again.
		}
	}, intervalMs);

	return {
		cancel: () => {
			clearInterval(timer);
			if (!delivered) {
				console.log('[warn] RCC log payload for job', jobId, 'was incomplete:', collector.state);
			}
		},
	};
};
