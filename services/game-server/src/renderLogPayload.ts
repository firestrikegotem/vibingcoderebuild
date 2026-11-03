/**
 * Puts a render back together from the lines RCC printed to its log.
 *
 * A script inside an RCC job has no io and no writefile, and this build's
 * HttpService:PostAsync kills the process, so the image has to leave the job as text on stdout.
 * The script prints `[payload] begin <jobId> <chunks> <bytes>`, then one
 * `[payload] <jobId> <index> <chunk>` line per 900 base64 characters, then `[payload] end`.
 *
 * Every chunk carries its index, so RCC's other threads interleaving their own output (the
 * CoreGui stack traces, for instance) cannot corrupt the result.
 */
export class RenderLogPayload {
	private readonly chunks = new Map<number, string>();
	private pending = '';
	private expected = 0;
	private declaredLength = 0;
	private finished = false;

	public constructor(private readonly jobId: string) {}

	/** Feed whatever has been appended to the log. Returns the finished base64 once it is all in. */
	public feed(text: string): string | null {
		if (this.finished) return null;
		const buffer = this.pending + text;
		const lastBreak = buffer.lastIndexOf('\n');
		if (lastBreak === -1) {
			// No complete line yet; a chunk can arrive split across two reads.
			this.pending = buffer;
			return null;
		}
		this.pending = buffer.substring(lastBreak + 1);
		for (const raw of buffer.substring(0, lastBreak).split('\n')) {
			this.absorb(raw.trim());
		}
		return this.assemble();
	}

	private absorb(line: string): void {
		if (!line.startsWith('[payload]')) return;
		const begin = /^\[payload\] begin (\S+) (\d+) (\d+)/.exec(line);
		if (begin) {
			if (begin[1] !== this.jobId) return;
			this.expected = parseInt(begin[2], 10);
			this.declaredLength = parseInt(begin[3], 10);
			return;
		}
		const piece = /^\[payload\] (\S+) (\d+) (.*)$/.exec(line);
		if (piece && piece[1] === this.jobId) {
			this.chunks.set(parseInt(piece[2], 10), piece[3]);
		}
	}

	private assemble(): string | null {
		if (this.finished || this.expected === 0 || this.chunks.size < this.expected) return null;
		const parts: string[] = [];
		for (let i = 1; i <= this.expected; i++) {
			const part = this.chunks.get(i);
			// A gap means a chunk was lost; keep collecting rather than return half an image.
			if (part === undefined) return null;
			parts.push(part);
		}
		const image = parts.join('');
		if (this.declaredLength !== 0 && image.length !== this.declaredLength) return null;
		this.finished = true;
		return image;
	}

	/** True once a complete image has been handed back. */
	public get done(): boolean {
		return this.finished;
	}
}
