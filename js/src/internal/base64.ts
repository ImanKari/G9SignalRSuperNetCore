// Base64 helpers that work in browsers, WebViews and Node without Buffer. They use the native
// Uint8Array.prototype.toBase64 / Uint8Array.fromBase64 (ES2026) when the runtime has them.

type NativeToBase64 = { toBase64?: () => string };
type NativeFromBase64 = { fromBase64?: (text: string) => Uint8Array };

const CHUNK = 0x8000;

/** Standard (RFC 4648, padded) base64 of `bytes` — what System.Text.Json writes and reads for `byte[]`. */
export function bytesToBase64(bytes: Uint8Array): string {
  const native = (bytes as unknown as NativeToBase64).toBase64;
  if (typeof native === 'function') return native.call(bytes);

  let binary = '';
  for (let i = 0; i < bytes.length; i += CHUNK) {
    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK) as unknown as number[]);
  }
  return btoa(binary);
}

/** Decodes standard base64 (padding optional, whitespace ignored). */
export function base64ToBytes(text: string): Uint8Array {
  const native = (Uint8Array as unknown as NativeFromBase64).fromBase64;
  if (typeof native === 'function') {
    try {
      return native(text);
    } catch {
      // Fall through to atob, which is more lenient about missing padding.
    }
  }

  const binary = atob(text.replace(/\s+/g, ''));
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
  return out;
}
