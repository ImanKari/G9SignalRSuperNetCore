// The codecs Lynx relies on (no btoa/atob, no TextEncoder/TextDecoder) must give exactly what Node's do.
import { HttpTransportType } from '@microsoft/signalr';
import { describe, expect, it } from 'vitest';
import { buildHubConnection, G9Client } from '../src/index.js';
import { base64ToBytesJs, bytesToBase64Js } from '../src/internal/base64.js';
import { utf8DecodeJs, utf8EncodeJs } from '../src/internal/utf8.js';
import { addDecimal, unixNanosecondsToTicks } from '../src/node/index.js';
import { pseudoRandomBytes } from './helpers/transferServer.js';

describe('pure-JS base64 (the Lynx path)', () => {
  it('matches Node for every length class', () => {
    for (const length of [0, 1, 2, 3, 4, 5, 100, 8191, 8192, 65536, 65537, 200_001]) {
      const bytes = pseudoRandomBytes(length, length + 7);
      const text = bytesToBase64Js(bytes);
      expect(text).toBe(Buffer.from(bytes).toString('base64'));
      expect(Buffer.from(base64ToBytesJs(text)).equals(Buffer.from(bytes))).toBe(true);
    }
  });

  it('accepts missing padding, whitespace and url-safe digits, and rejects garbage', () => {
    const bytes = pseudoRandomBytes(1000, 3);
    const url = Buffer.from(bytes).toString('base64url');
    expect(Buffer.from(base64ToBytesJs(url)).equals(Buffer.from(bytes))).toBe(true);
    expect(Array.from(base64ToBytesJs('AQID\nBA'))).toEqual([1, 2, 3, 4]);
    expect(() => base64ToBytesJs('AB*C')).toThrow(SyntaxError);
  });
});

describe('pure-JS UTF-8 (the Lynx path)', () => {
  const samples = [
    '',
    'ascii only',
    'سلام دنیا — Persian with an em dash',
    'emoji 😀 and 𝄞 (surrogate pairs)',
    '\u0000\u007f\u0080߿ࠀ￿',
    'lone \ud800 high and \udc00 low surrogates',
  ];

  it('encodes like TextEncoder (lone surrogates become U+FFFD)', () => {
    for (const text of samples) expect(Array.from(utf8EncodeJs(text))).toEqual(Array.from(new TextEncoder().encode(text)));
  });

  it('decodes like TextDecoder, including invalid and truncated sequences', () => {
    for (const text of samples) {
      const bytes = new TextEncoder().encode(text);
      expect(utf8DecodeJs(bytes)).toBe(new TextDecoder().decode(bytes));
    }
    for (const bad of [[0xff], [0xc0, 0x80], [0xe2, 0x82], [0xf4, 0x90, 0x80, 0x80], [0xed, 0xa0, 0x80], [0x61, 0xf0, 0x9f]]) {
      const bytes = new Uint8Array(bad);
      expect(utf8DecodeJs(bytes)).toBe(new TextDecoder().decode(bytes));
    }
    const big = pseudoRandomBytes(100_000, 9);
    expect(utf8DecodeJs(big)).toBe(new TextDecoder().decode(big));
  });
});

describe('.NET ticks without BigInt', () => {
  it('adds decimal strings', () => {
    expect(addDecimal('0', '0')).toBe('0');
    expect(addDecimal('999', '1')).toBe('1000');
    expect(addDecimal('621355968000000000', '17592186044416')).toBe((621355968000000000n + 17592186044416n).toString());
  });

  it('converts Unix nanoseconds to LastWriteTimeUtc.Ticks', () => {
    for (const ns of [0n, 99n, 100n, 1_700_000_000_123_456_789n]) {
      expect(unixNanosecondsToTicks(ns.toString())).toBe((621355968000000000n + ns / 100n).toString());
    }
  });
});

describe('connection options for runtimes without WebSocket or URL (Lynx)', () => {
  class FakeSocket {}

  it('passes the WebSocket constructor and skips negotiation over WebSockets only', () => {
    const connection = buildHubConnection({
      url: 'https://host/hub',
      transport: 'websockets',
      webSocket: FakeSocket,
      skipNegotiation: true,
    });
    const options = (connection as unknown as { connection: { _options: Record<string, unknown> } }).connection._options;
    expect(options.WebSocket).toBe(FakeSocket);
    expect(options.skipNegotiation).toBe(true);
    expect(options.transport).toBe(HttpTransportType.WebSockets);
    // Stateful reconnect is negotiated: off by default without negotiation.
    expect(options._useStatefulReconnect).toBeFalsy();
  });

  it('refuses skipNegotiation without the WebSockets transport', () => {
    expect(() => new G9Client({ url: 'https://host/hub', skipNegotiation: true })).toThrow(/websockets/);
  });
});
