import sharp = require('sharp');
import palette = require('./data/avatar-colors.json');

/**
 * Draws the classic R6 avatar from its body colours, so an avatar still has a picture when the
 * RCC render does not come back. RCC renders are the real thing; this is the safety net, and it
 * only ever draws what the avatar record already says (no invented clothing).
 */

export type BodyColors = {
	headColorId?: number;
	torsoColorId?: number;
	leftArmColorId?: number;
	rightArmColorId?: number;
	leftLegColorId?: number;
	rightLegColorId?: number;
};

export type FallbackAvatarRequest = {
	bodyColors?: BodyColors;
	playerAvatarType?: string;
};

const DEFAULT_COLORS: BodyColors = {
	headColorId: 24,
	torsoColorId: 23,
	leftArmColorId: 24,
	rightArmColorId: 24,
	leftLegColorId: 119,
	rightLegColorId: 119,
};

const colorMap: Record<number, string> = {};
for (const entry of palette as Array<{ brickColorId: number; hexColor: string }>) {
	colorMap[entry.brickColorId] = entry.hexColor;
}
// Roblox's 1021 is Institutional white; the shared palette file lists it as Camo.
colorMap[1021] = '#F8F8F8';
colorMap[1001] = '#F8F8F8';

const colorOf = (id: number | undefined, fallback: number): string => {
	const picked = id !== undefined && colorMap[id] ? colorMap[id] : colorMap[fallback];
	return picked || '#A3A2A5';
};

// A slightly darker edge gives the flat blocks some shape without pretending to be a real render.
const shade = (hex: string, factor: number): string => {
	const clean = hex.replace('#', '');
	const part = (offset: number) =>
		Math.max(0, Math.min(255, Math.round(parseInt(clean.substr(offset, 2), 16) * factor)))
			.toString(16)
			.padStart(2, '0');
	return '#' + part(0) + part(2) + part(4);
};

const rect = (x: number, y: number, w: number, h: number, fill: string, rx = 0) =>
	`<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="${fill}" rx="${rx}" />`;

const svgToPng = async (svg: string, size: number): Promise<string> => {
	const buffer = await sharp(Buffer.from(svg))
		.resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
		.png({ compressionLevel: 9 })
		.toBuffer();
	return buffer.toString('base64');
};

/**
 * Full body, front view, classic R6 proportions: 4 studs wide by 5 tall
 * (arm 1, torso 2, arm 1) by (head 1, torso 2, legs 2).
 */
export const renderFallbackThumbnail = async (
	request: FallbackAvatarRequest,
	size = 420
): Promise<string> => {
	const c = { ...DEFAULT_COLORS, ...(request.bodyColors || {}) };
	const head = colorOf(c.headColorId, 24);
	const torso = colorOf(c.torsoColorId, 23);
	const leftArm = colorOf(c.leftArmColorId, 24);
	const rightArm = colorOf(c.rightArmColorId, 24);
	const leftLeg = colorOf(c.leftLegColorId, 119);
	const rightLeg = colorOf(c.rightLegColorId, 119);

	const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 4 5" width="4" height="5">
		${rect(1.5, 0, 1, 1, head, 0.1)}
		${rect(2.32, 0.08, 0.18, 0.84, shade(head, 0.86), 0.06)}
		${rect(0, 1, 1, 2, leftArm)}
		${rect(0.86, 1, 0.14, 2, shade(leftArm, 0.86))}
		${rect(3, 1, 1, 2, rightArm)}
		${rect(3.86, 1, 0.14, 2, shade(rightArm, 0.86))}
		${rect(1, 1, 2, 2, torso)}
		${rect(2.86, 1, 0.14, 2, shade(torso, 0.86))}
		${rect(1, 3, 1, 2, leftLeg)}
		${rect(1.86, 3, 0.14, 2, shade(leftLeg, 0.86))}
		${rect(2, 3, 1, 2, rightLeg)}
		${rect(2.86, 3, 0.14, 2, shade(rightLeg, 0.86))}
		${rect(1.68, 0.34, 0.16, 0.17, '#2A2A2A', 0.04)}
		${rect(2.16, 0.34, 0.16, 0.17, '#2A2A2A', 0.04)}
		${rect(1.76, 0.64, 0.48, 0.06, '#2A2A2A', 0.03)}
	</svg>`;

	return svgToPng(svg, size);
};

/** Head only, framed the way a headshot is: the head plus the top of the torso. */
export const renderFallbackHeadshot = async (
	request: FallbackAvatarRequest,
	size = 420
): Promise<string> => {
	const c = { ...DEFAULT_COLORS, ...(request.bodyColors || {}) };
	const head = colorOf(c.headColorId, 24);
	const torso = colorOf(c.torsoColorId, 23);

	const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 2 2" width="2" height="2">
		${rect(0.48, 0.16, 1.04, 1.06, head, 0.1)}
		${rect(1.34, 0.24, 0.18, 0.9, shade(head, 0.86), 0.06)}
		${rect(0.34, 1.32, 1.32, 0.68, torso, 0.05)}
		${rect(1.48, 1.32, 0.18, 0.68, shade(torso, 0.86))}
		${rect(0.68, 0.52, 0.17, 0.18, '#2A2A2A', 0.04)}
		${rect(1.15, 0.52, 0.17, 0.18, '#2A2A2A', 0.04)}
		${rect(0.76, 0.86, 0.48, 0.06, '#2A2A2A', 0.03)}
	</svg>`;

	return svgToPng(svg, size);
};
