import { describe, expect, it } from 'vitest';
import { G9Url, G9UrlSearchParams } from '../src/index.js';
import { installUrlShim } from '../src/url.js';

/** SignalR's HttpConnection._resolveNegotiateUrl (10.0), with the URL classes passed in. */
function resolveNegotiateUrl(url: string, U: typeof URL, P: typeof URLSearchParams): string {
  const negotiateUrl = new U(url);
  if (negotiateUrl.pathname.endsWith('/')) negotiateUrl.pathname += 'negotiate';
  else negotiateUrl.pathname += '/negotiate';
  const searchParams = new P(negotiateUrl.searchParams);
  if (!searchParams.has('negotiateVersion')) searchParams.append('negotiateVersion', '1');
  if (searchParams.has('useStatefulReconnect')) searchParams.get('useStatefulReconnect');
  else searchParams.append('useStatefulReconnect', 'true');
  negotiateUrl.search = searchParams.toString();
  return negotiateUrl.toString();
}

describe('the URL shim', () => {
  const urls = [
    'https://sync.example.com/g9sync',
    'https://sync.example.com/g9sync/',
    'http://10.0.2.2:5015/api/hubs/chat?tenant=7&name=a%20b',
    'https://host/hub?negotiateVersion=1',
    'https://host:8443/a/b?x=1&x=2#frag',
    'https://[::1]:5001/hub',
  ];

  it('produces the negotiate URL SignalR builds with the platform URL', () => {
    for (const url of urls) {
      expect(
        resolveNegotiateUrl(url, G9Url as unknown as typeof URL, G9UrlSearchParams as unknown as typeof URLSearchParams),
        url,
      ).toBe(resolveNegotiateUrl(url, URL, URLSearchParams));
    }
  });

  it('parses host parts and query values', () => {
    const url = new G9Url('https://user@host.example:8443/p?q=a+b&r=%C3%A9');
    expect(url.hostname).toBe('host.example');
    expect(url.port).toBe('8443');
    expect(url.searchParams.get('q')).toBe('a b');
    expect(url.searchParams.get('r')).toBe('é');
    const params = new G9UrlSearchParams('a=1&a=2&b=3');
    params.set('a', '9');
    expect(params.toString()).toBe('a=9&b=3');
    expect(() => new G9Url('/relative')).toThrow(TypeError);
  });

  // Android Lynx 4.1's background thread has a global URL without a usable pathname: negotiate died with "cannot read
  // property 'endsWith' of undefined" (emulator, 2026-10-20). A presence check is not enough.
  it('replaces a runtime URL that negotiate cannot use, and keeps a complete one', () => {
    const g = globalThis as Record<string, unknown>;
    const saved = { URL: g.URL, URLSearchParams: g.URLSearchParams };
    try {
      class LynxLikeUrl {
        readonly href: string;
        constructor(url: string) {
          this.href = url;
        }
      }
      g.URL = LynxLikeUrl;
      g.URLSearchParams = saved.URLSearchParams;
      expect(installUrlShim()).toBe(true);
      expect(g.URL).toBe(G9Url);
      expect(g.URLSearchParams).toBe(G9UrlSearchParams); // replaced WITH the URL: SignalR copies url.searchParams into it
      expect(resolveNegotiateUrl('https://h/g9sync', g.URL as typeof URL, g.URLSearchParams as typeof URLSearchParams)).toBe(
        'https://h/g9sync/negotiate?negotiateVersion=1&useStatefulReconnect=true',
      );

      g.URL = saved.URL;
      g.URLSearchParams = saved.URLSearchParams;
      expect(installUrlShim()).toBe(false); // Node's are complete: left alone
      // the shim's params feed a runtime URLSearchParams too
      expect(new URLSearchParams(new G9UrlSearchParams('a=1&b=2') as unknown as URLSearchParams).toString()).toBe('a=1&b=2');

      g.URLSearchParams = class {
        has() {
          return false;
        }
      };
      expect(installUrlShim()).toBe(true);
      expect(g.URLSearchParams).toBe(G9UrlSearchParams);

      g.URL = 'not a constructor';
      expect(installUrlShim()).toBe(true);
      expect(g.URL).toBe(G9Url);
    } finally {
      g.URL = saved.URL;
      g.URLSearchParams = saved.URLSearchParams;
    }
  });
});
