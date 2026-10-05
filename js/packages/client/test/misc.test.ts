import { describe, expect, it } from 'vitest';
import * as api from '../src/index.js';
import { G9ErrorCodes, getG9ErrorCode, isAbortError } from '../src/index.js';
import { base64ToBytes, bytesToBase64 } from '../src/internal/base64.js';
import { pseudoRandomBytes } from './helpers/transferServer.js';

describe('base64 (System.Text.Json byte[] form)', () => {
  it('round-trips and matches Node for every length class', () => {
    for (const length of [0, 1, 2, 3, 4, 100, 65536, 65537, 200_000]) {
      const bytes = pseudoRandomBytes(length, length + 1);
      const text = bytesToBase64(bytes);
      expect(text).toBe(Buffer.from(bytes).toString('base64'));
      expect(Buffer.from(base64ToBytes(text)).equals(Buffer.from(bytes))).toBe(true);
    }
  });
});

describe('getG9ErrorCode', () => {
  it('extracts the code from a SignalR HubException message', () => {
    const error = new Error("An unexpected error occurred invoking 'SendMessage' on the server. HubException: G9_RATE_LIMITED");
    expect(getG9ErrorCode(error)).toBe(G9ErrorCodes.RateLimited);
    expect(getG9ErrorCode({ errorCode: 'G9_UPLOAD_FORBIDDEN' })).toBe('G9_UPLOAD_FORBIDDEN');
    expect(getG9ErrorCode('HubException: G9_PERMISSION_REQUIRED')).toBe('G9_PERMISSION_REQUIRED');
    expect(getG9ErrorCode(new Error('plain failure'))).toBeNull();
    expect(getG9ErrorCode(undefined)).toBeNull();
  });

  it('recognizes abort errors', () => {
    const controller = new AbortController();
    controller.abort();
    expect(isAbortError(controller.signal.reason)).toBe(true);
    expect(isAbortError(new Error('x'))).toBe(false);
  });
});

describe('public API', () => {
  it('exports the documented surface', () => {
    const expected = [
      'VERSION',
      'G9Client',
      'G9ReconnectPolicy',
      'authorize',
      'G9FileUploader',
      'G9FileDownloader',
      'G9ConnectionQualityMonitor',
      'G9ErrorCodes',
      'G9DownloadErrorCodes',
      'G9UploadStatus',
      'G9TransferError',
      'G9TimeoutError',
      'sha256Hex',
      'waitUntilConnected',
      'getG9ErrorCode',
      'isAbortError',
    ];
    for (const name of expected) expect(api, name).toHaveProperty(name);
    expect(api.VERSION).toBe('2.10.0');
    expect(Object.isFrozen(api.G9ErrorCodes)).toBe(true);
    expect(api.G9ErrorCodes.UploadForbidden).toBe('G9_UPLOAD_FORBIDDEN');
    expect(api.G9ErrorCodes.PermissionRequired).toBe('G9_PERMISSION_REQUIRED');
  });
});
