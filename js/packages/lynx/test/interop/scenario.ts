// Runs INSIDE the Lynx-shaped sandbox against the REAL .NET sample server (G9SignalRSuperNetCore.WebServer, ChatHub):
// the client + this package over one transport path, everything a Lynx app does with a G9 hub.
import {
  G9ConnectionQualityMonitor,
  G9FileDownloader,
  G9FileUploader,
  G9UploadStatus,
  type G9ConnectionQuality,
} from '@g9tm/signalr-supernetcore-client';
import { Subject } from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { createLynxClient, lynxFileSystem, type G9LynxTransportChoice } from '../../src/index.js';

interface Host {
  url: string;
  dir: string;
  protocol: 'json' | 'messagepack';
  path: 'native' | 'native-negotiated' | 'long-polling';
}

function same(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

export default async function run(host: Host): Promise<Record<string, unknown>> {
  let choice: G9LynxTransportChoice | undefined;
  const client = createLynxClient({
    url: host.url,
    protocol: host.protocol === 'messagepack' ? new MessagePackHubProtocol() : undefined,
    native: host.path === 'long-polling' ? null : undefined,
    skipNegotiation: host.path === 'native-negotiated' ? false : undefined,
    onTransportChosen: (c) => (choice = c),
  });
  const received: string[] = [];
  client.on('ReceiveMessage', (user: string, message: string) => {
    received.push(`${user}:${message}`);
  });
  await client.start();

  const recent = (await client.invoke('GetRecentMessages')) as string[];
  await client.invoke('SendMessage', 'lynx', `hello over ${host.path}`);
  for (let i = 0; i < 200 && received.length === 0; i++) await new Promise((r) => setTimeout(r, 10));

  const ticks: number[] = [];
  await new Promise<void>((resolve, reject) =>
    client.stream<number>('LiveTickerStream', 5, 10).subscribe({ next: (i) => ticks.push(i), error: reject, complete: resolve }),
  );

  const counter = new Subject<number>();
  const total = client.invoke<number>('BulkCounterStream', counter);
  for (let i = 1; i <= 100; i++) counter.next(i);
  counter.complete();
  const sum = await total;

  const monitor = new G9ConnectionQualityMonitor(client, { intervalMs: 500 });
  const quality = await new Promise<G9ConnectionQuality>((resolve) => {
    const off = monitor.onChange((q) => {
      if (q.level !== 'lost') {
        off();
        resolve(q);
      }
    });
    monitor.start();
  });
  monitor.stop();

  // Files through the real G9CUploadService: upload from disk, then download to disk.
  const fs = lynxFileSystem();
  const content = new Uint8Array(700 * 1024 + 3);
  for (let i = 0; i < content.length; i++) content[i] = (i * 7 + host.path.length + host.protocol.length) & 0xff;
  // The sample server commits under the declared name (no randomizing): one name per case and run.
  const name = `photo-${host.protocol}-${host.path}-${Math.floor(Math.random() * 1e9).toString(36)}.bin`;
  const source = `${host.dir}/${host.protocol}-${host.path}/${name}`;
  await fs.write(source, 0, content);
  const upload = await new G9FileUploader(client).uploadFile(source, { fileSystem: fs });
  const target = `${host.dir}/${host.protocol}-${host.path}/back.bin`;
  const download = await new G9FileDownloader(client).downloadToFile(upload.storedFileName ?? 'missing', target, { fileSystem: fs });
  const back = new Uint8Array(await fs.read(target, 0, content.length + 1));

  await client.stop();
  return {
    choice,
    recent: recent.length,
    received,
    ticks,
    sum,
    quality: quality.level,
    upload: upload.status === G9UploadStatus.Completed,
    storedFileName: upload.storedFileName,
    download: download.status === G9UploadStatus.Completed && same(back, content),
  };
}
