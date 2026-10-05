import { describe, expect, it } from 'vitest';
import { G9Url, G9UrlSearchParams } from '../src/index.js';

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
});
