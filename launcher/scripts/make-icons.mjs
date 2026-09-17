// Generates the launcher icon set. Run with: node scripts/make-icons.mjs
import { deflateSync } from "node:zlib";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const iconsDir = join(dirname(fileURLToPath(import.meta.url)), "..", "src-tauri", "icons");
mkdirSync(iconsDir, { recursive: true });

const PARCHMENT = [200, 164, 93, 255];
const INK = [38, 32, 25, 255];

function renderPixels(size) {
  const pixels = Buffer.alloc(size * size * 4);
  const border = Math.round(size * 0.06);
  const outer = size * 0.36;
  const inner = size * 0.24;

  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const dx = x - size / 2;
      const dy = y - size / 2;
      const radius = Math.sqrt(dx * dx + dy * dy);
      const onBorder = x < border || y < border || x >= size - border || y >= size - border;
      const inRing = radius < outer && radius > inner;

      const colour = onBorder || inRing ? PARCHMENT : INK;
      pixels.set(colour, (y * size + x) * 4);
    }
  }

  return pixels;
}

const CRC_TABLE = (() => {
  const table = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c;
  }
  return table;
})();

function crc32(buffer) {
  let crc = -1;
  for (const byte of buffer) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ -1) >>> 0;
}

function pngChunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const typeBytes = Buffer.from(type, "ascii");
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([typeBytes, data])));
  return Buffer.concat([length, typeBytes, data, crc]);
}

function encodePng(size) {
  const pixels = renderPixels(size);
  const stride = size * 4;
  const raw = Buffer.alloc(size * (stride + 1));
  for (let y = 0; y < size; y++) {
    raw[y * (stride + 1)] = 0; // no per-scanline filter
    pixels.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 6; // RGBA

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk("IHDR", header),
    pngChunk("IDAT", deflateSync(raw, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0))
  ]);
}

/// Vista and later accept PNG-compressed entries, so each ICO entry is just an embedded PNG.
function encodeIco(sizes) {
  const images = sizes.map(encodePng);
  const header = Buffer.alloc(6);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);

  let offset = 6 + images.length * 16;
  const entries = images.map((image, index) => {
    const entry = Buffer.alloc(16);
    entry[0] = sizes[index] >= 256 ? 0 : sizes[index];
    entry[1] = sizes[index] >= 256 ? 0 : sizes[index];
    entry.writeUInt16LE(1, 4); // colour planes
    entry.writeUInt16LE(32, 6); // bits per pixel
    entry.writeUInt32LE(image.length, 8);
    entry.writeUInt32LE(offset, 12);
    offset += image.length;
    return entry;
  });

  return Buffer.concat([header, ...entries, ...images]);
}

for (const size of [32, 128, 256, 512]) {
  const name = size === 512 ? "icon.png" : `${size}x${size}.png`;
  writeFileSync(join(iconsDir, name), encodePng(size));
}

writeFileSync(join(iconsDir, "icon.ico"), encodeIco([16, 32, 48, 64, 256]));

// icon.icns is not generated here: it is a distinct format. Run `npm run tauri icon` on macOS.
console.log(`Wrote launcher icons to ${iconsDir}`);
