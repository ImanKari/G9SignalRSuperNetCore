import { HubConnectionState } from '@microsoft/signalr';
import { describe, expect, it } from 'vitest';
import { authorize, G9ErrorCodes, G9TimeoutError, getG9ErrorCode } from '../src/index.js';
import { FakeHub } from './helpers/fakeHub.js';

function authHub(answer?: (data: unknown, hub: FakeHub) => void): FakeHub {
  const hub = new FakeHub();
  hub.state = HubConnectionState.Disconnected;
  hub.connectionId = null;
  if (answer) hub.methods.set('Authorize', (data: unknown) => setTimeout(() => answer(data, hub), 5));
  return hub;
}

describe('authorize', () => {
  it('connects, sends Authorize(data), returns the AuthorizeResult callback and disconnects', async () => {
    const hub = authHub((_, h) =>
      h.emit('AuthorizeResult', { isAccepted: true, jwToken: 'eyJhbGciOi', rejectionReason: null, extraData: { role: 'admin' } }),
    );
    const result = await authorize('http://test/auth/chat', { user: 'u', password: 'p' }, { connectionFactory: () => hub.connection });

    expect(result).toEqual({ isAccepted: true, jwToken: 'eyJhbGciOi', rejectionReason: null, extraData: { role: 'admin' } });
    expect(hub.calls).toEqual([{ kind: 'send', method: 'Authorize', args: [{ user: 'u', password: 'p' }] }]);
    expect(hub.startCalls).toBe(1);
    expect(hub.stopCalls).toBe(1);
    expect(hub.state).toBe(HubConnectionState.Disconnected);
  });

  it('surfaces the 2.9 auth throttle as a refusal with G9_RATE_LIMITED', async () => {
    const hub = authHub((_, h) =>
      h.emit('AuthorizeResult', { isAccepted: false, jwToken: null, rejectionReason: 'G9_RATE_LIMITED', extraData: null }),
    );
    const result = await authorize('http://test/auth/chat', { user: 'u' }, { connectionFactory: () => hub.connection });
    expect(result.isAccepted).toBe(false);
    expect(result.rejectionReason).toBe(G9ErrorCodes.RateLimited);
    expect(getG9ErrorCode(result)).toBe('G9_RATE_LIMITED');
  });

  it('reads PascalCase property names too', async () => {
    const hub = authHub((_, h) => h.emit('AuthorizeResult', { IsAccepted: true, JWToken: 'tok', RejectionReason: null }));
    const result = await authorize('http://test/auth', 'credentials', { connectionFactory: () => hub.connection });
    expect(result).toMatchObject({ isAccepted: true, jwToken: 'tok' });
  });

  it('rejects with a TimeoutError when no answer arrives', async () => {
    const hub = authHub();
    hub.methods.set('Authorize', () => undefined);
    const error = await authorize('http://test/auth', {}, { timeoutMs: 50, connectionFactory: () => hub.connection }).catch(
      (e: unknown) => e,
    );
    expect(error).toBeInstanceOf(G9TimeoutError);
    expect(hub.stopCalls).toBe(1);
  });

  it('rejects when the connection cannot start', async () => {
    const hub = authHub();
    hub.startImpl = async () => {
      throw new Error('Failed to complete negotiation with the server: 404');
    };
    await expect(authorize('http://test/auth', {}, { connectionFactory: () => hub.connection })).rejects.toThrow(/negotiation/);
  });

  it('rejects when the connection closes before the result', async () => {
    const hub = authHub((_, h) => h.simulateClose(new Error('Server closed the connection.')));
    await expect(authorize('http://test/auth', {}, { connectionFactory: () => hub.connection })).rejects.toThrow(/Server closed/);
  });

  it('rejects with an AbortError when aborted', async () => {
    const hub = authHub();
    hub.methods.set('Authorize', () => undefined);
    const controller = new AbortController();
    const promise = authorize('http://test/auth', {}, { signal: controller.signal, connectionFactory: () => hub.connection });
    setTimeout(() => controller.abort(), 10);
    await expect(promise).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('validates its arguments', async () => {
    await expect(authorize('', {})).rejects.toBeInstanceOf(TypeError);
    await expect(authorize('http://test/auth', null)).rejects.toBeInstanceOf(TypeError);
  });
});
