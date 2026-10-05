import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

const client = (path: string) => fileURLToPath(new URL(`../client/src/${path}`, import.meta.url));

// Tests run against the client's SOURCES (no build step between the two packages).
export default defineConfig({
  resolve: {
    alias: [
      { find: /^@g9tm\/signalr-supernetcore-client\/node$/, replacement: client('node/index.ts') },
      { find: /^@g9tm\/signalr-supernetcore-client$/, replacement: client('index.ts') },
    ],
  },
  test: {
    include: ['test/**/*.test.ts'],
    exclude: ['test/lynxsim/**', 'test/interop/**'],
    environment: 'node',
    testTimeout: 30000,
  },
});
