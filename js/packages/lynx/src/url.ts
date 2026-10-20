// Minimal `URL` / `URLSearchParams` for SignalR's negotiate step (`HttpConnection._resolveNegotiateUrl` calls
// `new URL(url)`, reads/writes `pathname` and `search`, and uses `URLSearchParams` has/get/append/toString). Lynx's
// background thread has neither. Only absolute http(s)/ws(s) URLs are supported — what a hub URL is.

function encode(text: string): string {
  return encodeURIComponent(text).replace(/%20/g, '+');
}

function decode(text: string): string {
  try {
    return decodeURIComponent(text.replace(/\+/g, ' '));
  } catch {
    return text;
  }
}

/** A `URLSearchParams` subset: has, get, getAll, append, set, delete, forEach, toString. */
export class G9UrlSearchParams {
  private _pairs: Array<[string, string]> = [];

  constructor(init?: string | G9UrlSearchParams | Record<string, string>) {
    if (init instanceof G9UrlSearchParams) {
      this._pairs = init._pairs.map(([k, v]) => [k, v] as [string, string]);
    } else if (typeof init === 'string') {
      const text = init.charAt(0) === '?' ? init.slice(1) : init;
      for (const part of text.split('&')) {
        if (!part) continue;
        const eq = part.indexOf('=');
        this._pairs.push(eq < 0 ? [decode(part), ''] : [decode(part.slice(0, eq)), decode(part.slice(eq + 1))]);
      }
    } else if (init && typeof init === 'object') {
      for (const key of Object.keys(init)) this._pairs.push([key, String(init[key])]);
    }
  }

  has(name: string): boolean {
    return this._pairs.some(([k]) => k === name);
  }

  get(name: string): string | null {
    const pair = this._pairs.find(([k]) => k === name);
    return pair ? pair[1] : null;
  }

  getAll(name: string): string[] {
    return this._pairs.filter(([k]) => k === name).map(([, v]) => v);
  }

  append(name: string, value: string): void {
    this._pairs.push([String(name), String(value)]);
  }

  set(name: string, value: string): void {
    const first = this._pairs.findIndex(([k]) => k === name);
    if (first < 0) {
      this._pairs.push([name, String(value)]);
      return;
    }
    this._pairs[first] = [name, String(value)];
    this._pairs = this._pairs.filter(([k], i) => k !== name || i === first);
  }

  delete(name: string): void {
    this._pairs = this._pairs.filter(([k]) => k !== name);
  }

  forEach(callback: (value: string, key: string) => void): void {
    for (const [k, v] of this._pairs) callback(v, k);
  }

  /** The pairs, so a runtime `URLSearchParams` built from this one (`new URLSearchParams(url.searchParams)`) copies them. */
  [Symbol.iterator](): IterableIterator<[string, string]> {
    return this._pairs.map(([k, v]) => [k, v] as [string, string])[Symbol.iterator]();
  }

  toString(): string {
    return this._pairs.map(([k, v]) => `${encode(k)}=${encode(v)}`).join('&');
  }
}

const PATTERN = /^([a-zA-Z][a-zA-Z0-9+.-]*:)\/\/([^/?#]*)([^?#]*)(\?[^#]*)?(#.*)?$/;

/** A `URL` subset for absolute URLs: protocol, host, hostname, port, pathname, search, searchParams, hash, href. */
export class G9Url {
  protocol: string;
  host: string;
  pathname: string;
  hash: string;
  private _search: string;
  private _params: G9UrlSearchParams;

  constructor(url: string, base?: string) {
    const match = PATTERN.exec(String(url));
    if (!match) {
      if (base !== undefined) throw new TypeError(`Relative URLs are not supported here: '${url}'.`);
      throw new TypeError(`Invalid URL: '${url}'.`);
    }
    this.protocol = match[1]!.toLowerCase();
    this.host = match[2]!;
    this.pathname = match[3] || '/';
    this._search = match[4] && match[4] !== '?' ? match[4] : '';
    this.hash = match[5] && match[5] !== '#' ? match[5] : '';
    this._params = new G9UrlSearchParams(this._search);
  }

  get hostname(): string {
    const at = this.host.lastIndexOf('@');
    const host = at >= 0 ? this.host.slice(at + 1) : this.host;
    if (host.charAt(0) === '[') return host.slice(0, host.indexOf(']') + 1);
    const colon = host.lastIndexOf(':');
    return colon >= 0 ? host.slice(0, colon) : host;
  }

  get port(): string {
    const at = this.host.lastIndexOf('@');
    const host = at >= 0 ? this.host.slice(at + 1) : this.host;
    const close = host.lastIndexOf(']');
    const colon = host.lastIndexOf(':');
    return colon > close ? host.slice(colon + 1) : '';
  }

  get search(): string {
    const text = this._params.toString();
    return text ? '?' + text : '';
  }

  set search(value: string) {
    this._params = new G9UrlSearchParams(value);
  }

  get searchParams(): G9UrlSearchParams {
    return this._params;
  }

  get href(): string {
    return `${this.protocol}//${this.host}${this.pathname}${this.search}${this.hash}`;
  }

  toString(): string {
    return this.href;
  }

  toJSON(): string {
    return this.href;
  }
}

/** Whether [U] does what SignalR's negotiate needs of `URL`: parse, read and write `pathname` / `search`, serialize. */
function urlIsUsable(U: unknown): boolean {
  if (typeof U !== 'function') return false;
  try {
    const u = new (U as new (url: string) => { pathname: string; search: string; searchParams?: { has?: unknown }; toString(): string })(
      'https://h:1/a/b?x=1',
    );
    if (u.pathname !== '/a/b' || u.search !== '?x=1' || typeof u.searchParams?.has !== 'function') return false;
    u.pathname += '/negotiate';
    u.search = 'y=2';
    return u.toString() === 'https://h:1/a/b/negotiate?y=2';
  } catch {
    return false;
  }
}

/** Whether [P] does what SignalR's negotiate needs of `URLSearchParams`. */
function searchParamsAreUsable(P: unknown): boolean {
  if (typeof P !== 'function') return false;
  try {
    const p = new (P as new (init: string) => { has(n: string): boolean; get(n: string): string | null; append(n: string, v: string): void; toString(): string })(
      'a=1&b=2',
    );
    p.append('c', '3');
    return p.has('a') && p.get('b') === '2' && p.toString() === 'a=1&b=2&c=3';
  } catch {
    return false;
  }
}

/**
 * Installs {@link G9Url}/{@link G9UrlSearchParams} as the globals `URL`/`URLSearchParams` when the runtime has none OR
 * one that cannot do what SignalR's negotiate needs, so negotiation works. ⛔ Android Lynx 4.1's background thread HAS a
 * global `URL` — without a usable `pathname` — and negotiate died with "cannot read property 'endsWith' of undefined"
 * (found on the emulator 2026-10-20; only a presence check was made before). A complete implementation (a browser, Node)
 * is left alone. Returns whether it installed anything.
 */
export function installUrlShim(): boolean {
  const g = globalThis as Record<string, unknown>;
  // The two go together: SignalR builds `new URLSearchParams(url.searchParams)`, so a shim URL keeps the shim's params.
  if (!urlIsUsable(g.URL)) {
    g.URL = G9Url;
    g.URLSearchParams = G9UrlSearchParams;
    return true;
  }
  if (!searchParamsAreUsable(g.URLSearchParams)) {
    g.URLSearchParams = G9UrlSearchParams;
    return true;
  }
  return false;
}
