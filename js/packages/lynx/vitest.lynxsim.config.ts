import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

const client = (path: string) => fileURLToPath(new URL(`../client/src/${path}`, import.meta.url));

// `npm run test:lynxsim`: test/lynxsim/scenario.ts bundled and run in a Lynx-shaped vm context
// (../../scripts/lynx-sandbox.mjs). The host side of the test resolves the client from its sources, like vitest.config.ts.
export default defineConfig({
  resolve: {
    alias: [
      { find: /^@g9tm\/signalr-supernetcore-client\/node$/, replacement: client('node/index.ts') },
      { find: /^@g9tm\/signalr-supernetcore-client$/, replacement: client('index.ts') },
    ],
  },
  test: {
    include: ['test/lynxsim/**/*.test.ts'],
    environment: 'node',
    testTimeout: 120000,
  },
});
