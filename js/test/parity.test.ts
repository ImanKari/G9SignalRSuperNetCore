// Guards the parity rule mechanically where it can: when the .NET sources sit next to this package (the repository
// layout), their error codes, file-transfer DTO property names and enum values must match the TypeScript twin.
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { G9DownloadErrorCodes, G9ErrorCodes, G9UploadStatus } from '../src/index.js';
import {
  normalizeBeginDownloadResult,
  normalizeBeginUploadResult,
  normalizeServerUploadProgress,
  normalizeUploadResult,
} from '../src/fileTransfer/dtos.js';
import { normalizeAuthorizeResult } from '../src/authorize.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'G9SignalRSuperNetCore');
const server = join(root, 'G9SignalRSuperNetCore.Server', 'Classes');
const client = join(root, 'G9SignalRSuperNetCore.Client');
const hasSources = existsSync(join(server, 'Errors', 'G9CErrorCodes.cs'));

function read(path: string): string {
  return readFileSync(path, 'utf8');
}

function constants(source: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const match of source.matchAll(/const\s+string\s+(\w+)\s*=\s*"([^"]+)"/g)) out[match[1]!] = match[2]!;
  return out;
}

/** Property names of `class name` in a C# file, camelCased like System.Text.Json does for these names. */
function properties(source: string, className: string): string[] {
  const start = source.indexOf(`class ${className}`);
  if (start < 0) throw new Error(`class ${className} not found`);
  const rest = source.slice(start);
  const next = rest.slice(1).search(/\n(public|internal)\s+(sealed\s+)?(class|enum|readonly|record)/);
  const body = next < 0 ? rest : rest.slice(0, next + 1);
  return [...body.matchAll(/public\s+(?:required\s+)?[\w?<>[\],. ]+?\s+(\w+)\s*\{\s*get;/g)].map((m) => camel(m[1]!));
}

function camel(name: string): string {
  // System.Text.Json JsonNamingPolicy.CamelCase: lower the leading run of capitals except the last one before a lower.
  const chars = [...name];
  for (let i = 0; i < chars.length; i++) {
    if (i === 1 && chars[i] !== chars[i]!.toUpperCase()) break;
    const hasNext = i + 1 < chars.length;
    if (i > 0 && hasNext && chars[i + 1] !== chars[i + 1]!.toUpperCase()) break;
    chars[i] = chars[i]!.toLowerCase();
  }
  return chars.join('');
}

function enumMembers(source: string, enumName: string): string[] {
  const start = source.indexOf(`enum ${enumName}`);
  const body = source.slice(source.indexOf('{', start) + 1, source.indexOf('}', start));
  return body
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line && !line.startsWith('//') && !line.startsWith('///'))
    .map((line) => line.replace(/[,\s].*$/, ''))
    .filter(Boolean);
}

describe.runIf(hasSources)('parity with the .NET sources', () => {
  it('G9ErrorCodes equals G9CErrorCodes (names and values)', () => {
    expect({ ...G9ErrorCodes }).toEqual(constants(read(join(server, 'Errors', 'G9CErrorCodes.cs'))));
  });

  it('G9DownloadErrorCodes equals the constants of G9CFileDownloader', () => {
    const source = constants(read(join(client, 'FileUpload', 'G9CFileDownloader.cs')));
    expect(Object.values(G9DownloadErrorCodes).sort()).toEqual(Object.values(source).sort());
  });

  it('G9UploadStatus has the members and numbers of G9EUploadStatus', () => {
    const members = enumMembers(read(join(server, 'FileUpload', 'G9DtUploadDtos.cs')), 'G9EUploadStatus');
    expect(Object.fromEntries(members.map((name, index) => [name, index]))).toEqual({ ...G9UploadStatus });
  });

  it('file-transfer DTO twins read every property of the server DTOs', () => {
    const uploads = read(join(server, 'FileUpload', 'G9DtUploadDtos.cs'));
    const downloads = read(join(server, 'FileUpload', 'G9DtDownloadDtos.cs'));
    const progress = read(join(server, 'FileUpload', 'G9DtUploadProgress.cs'));
    const keys = (value: object | null) => Object.keys(value ?? {}).sort();

    expect(keys(normalizeBeginUploadResult({}))).toEqual(properties(uploads, 'G9DtBeginUploadResult').sort());
    expect(keys(normalizeUploadResult({}))).toEqual(properties(uploads, 'G9DtUploadResult').sort());
    expect(keys(normalizeBeginDownloadResult({}))).toEqual(properties(downloads, 'G9DtBeginDownloadResult').sort());
    expect(keys(normalizeServerUploadProgress({ uploadId: 'x' }))).toEqual(properties(progress, 'G9DtUploadProgress').sort());
  });

  it('G9AuthorizeResult reads every property of the server G9DtAuthorizeResult', () => {
    const dto = read(join(server, 'DataTypes', 'G9DtAuthorizeResult.cs'));
    expect(Object.keys(normalizeAuthorizeResult({})).sort()).toEqual(properties(dto, 'G9DtAuthorizeResult').sort());
  });

  it('the quality thresholds and ping method match G9CConnectionQualityMonitor', async () => {
    const { G9ConnectionQualityMonitor } = await import('../src/index.js');
    const source = read(join(client, 'G9CConnectionQualityMonitor.cs'));
    expect(source).toMatch(/GoodBelowMs\s*=\s*150;/);
    expect(source).toMatch(/FairBelowMs\s*=\s*400;/);
    expect(source).toMatch(/LostAfterFailures\s*=\s*3;/);
    expect(source).toMatch(/DefaultPingMethod\s*=\s*"G9Ping";/);
    expect(G9ConnectionQualityMonitor.GOOD_BELOW_MS).toBe(150);
    expect(G9ConnectionQualityMonitor.FAIR_BELOW_MS).toBe(400);
  });
});
