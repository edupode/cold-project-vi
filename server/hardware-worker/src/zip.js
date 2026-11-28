// The entries of a zip held in memory, from its central directory: { name, method, size, unpacked, local }.
// null when the zip cannot be read.
export function zipEntries(bytes) {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let end = -1;
  for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 65557); i--) if (v.getUint32(i, true) === 0x06054b50) { end = i; break; }
  if (end < 0) return null;
  let at = v.getUint32(end + 16, true);
  const count = v.getUint16(end + 10, true);
  const out = [];
  for (let k = 0; k < count && at + 46 <= bytes.length; k++) {
    if (v.getUint32(at, true) !== 0x02014b50) return null;
    const nameLen = v.getUint16(at + 28, true), extra = v.getUint16(at + 30, true), comment = v.getUint16(at + 32, true);
    out.push({
      name: new TextDecoder().decode(bytes.subarray(at + 46, at + 46 + nameLen)),
      method: v.getUint16(at + 10, true),
      size: v.getUint32(at + 20, true),
      unpacked: v.getUint32(at + 24, true),
      local: v.getUint32(at + 42, true),
    });
    at += 46 + nameLen + extra + comment;
  }
  return out;
}

// One file of a zip held in memory (a report's summary.txt), as text: stored or deflated entries.
// null when the zip or the entry cannot be read.
export async function zipFile(bytes, name) {
  const entry = (zipEntries(bytes) || []).find(e => e.name === name);
  if (!entry) return null;
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const local = entry.local;
  if (local + 30 > bytes.length || v.getUint32(local, true) !== 0x04034b50) return null;
  const start = local + 30 + v.getUint16(local + 26, true) + v.getUint16(local + 28, true);
  const data = bytes.subarray(start, start + entry.size);
  if (entry.method === 0) return new TextDecoder().decode(data);
  if (entry.method !== 8) return null;
  const out = new Response(new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw')));
  return new TextDecoder().decode(await out.arrayBuffer());
}
