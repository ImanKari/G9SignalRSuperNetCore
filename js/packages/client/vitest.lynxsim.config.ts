import { defineConfig } from 'vitest/config';

// `npm run test:lynxsim`: bundles test/lynxsim/scenario.ts and runs it in a Lynx-shaped vm context
// (../../scripts/lynx-sandbox.mjs): no BigInt/Intl/TextEncoder/URL/Blob/btoa/WebSocket, timers only as bundle-scope
// identifiers, native modules behind a copying bridge.
export default defineConfig({
  test: {
    include: ['test/lynxsim/**/*.test.ts'],
    environment: 'node',
    testTimeout: 120000,
  },
});
