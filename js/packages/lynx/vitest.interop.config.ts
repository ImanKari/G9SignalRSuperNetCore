import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

const client = (path: string) => fileURLToPath(new URL(`../client/src/${path}`, import.meta.url));

// `npm run test:interop`: the Lynx-shaped sandbox against the real .NET sample server (needs the .NET 10 SDK).
export default defineConfig({
  resolve: {
    alias: [
      { find: /^@g9tm\/signalr-supernetcore-client\/node$/, replacement: client('node/index.ts') },
      { find: /^@g9tm\/signalr-supernetcore-client$/, replacement: client('index.ts') },
    ],
  },
  test: {
    include: ['test/interop/**/*.test.ts'],
    environment: 'node',
    testTimeout: 180000,
    hookTimeout: 300000,
    fileParallelism: false,
  },
});
