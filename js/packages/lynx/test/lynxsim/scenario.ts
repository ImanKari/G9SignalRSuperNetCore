// Runs INSIDE the Lynx-shaped sandbox: the real client + this package, `NativeModules.G9SignalRLynxModule` behind a
// copying bridge (backed by the Node implementation of the contract), a real hub on the other side.
import { G9FileDownloader, G9FileUploader, G9UploadStatus } from '@g9tm/signalr-supernetcore-client';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { createLynxClient, lynxFileSystem, type G9LynxTransportChoice } from '../../src/index.js';

interface Host {
  url: string;
  dir: string;
}

function same(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

export default async function run(host: Host): Promise<Record<string, unknown>> {
  const g = globalThis as Record<string, unknown>;
  const out: Record<string, unknown> = {
    env: { URL: typeof g.URL, WebSocket: typeof g.WebSocket, TextEncoder: typeof g.TextEncoder, BigInt: typeof g.BigInt },
  };

  for (const name of ['json', 'messagepack'] as const) {
    let choice: G9LynxTransportChoice | undefined;
    const client = createLynxClient({
      url: host.url,
      protocol: name === 'messagepack' ? new MessagePackHubProtocol() : undefined,
      accessTokenFactory: () => 'tok en',
      onTransportChosen: (c) => (choice = c),
    });
    const heard: string[] = [];
    client.on('Heard', (text: string) => heard.push(text));
    await client.start();

    const bytes = new Uint8Array(50_000);
    for (let i = 0; i < bytes.length; i++) bytes[i] = (i * 13) & 0xff;
    const echoed = (await client.invoke('EchoBytes', bytes)) as Uint8Array | string;

    const items: number[] = [];
    await new Promise<void>((resolve, reject) =>
      client.stream<number>('Count', 4).subscribe({ next: (i) => items.push(i), error: reject, complete: resolve }),
    );
    await client.send('Shout', 'salam');
    for (let i = 0; i < 100 && heard.length === 0; i++) await new Promise((r) => setTimeout(r, 10));

    // Files: upload from disk and download back through the native file system.
    const fs = lynxFileSystem();
    const content = new Uint8Array(200 * 1024 + 5);
    for (let i = 0; i < content.length; i++) content[i] = (i * 31 + name.length) & 0xff;
    const source = `${host.dir}/${name}/in.bin`;
    await fs.write(source, 0, content);
    const upload = await new G9FileUploader(client).uploadFile(source, { fileSystem: fs });
    const target = `${host.dir}/${name}/out.bin`;
    const download = await new G9FileDownloader(client).downloadToFile(upload.storedFileName ?? 'x', target, { fileSystem: fs });
    const back = new Uint8Array(await fs.read(target, 0, content.length + 10));

    out[name] = {
      choice,
      phase: client.state.phase,
      echoedKind: typeof echoed === 'string' ? 'base64' : Object.prototype.toString.call(echoed),
      echoedLength: typeof echoed === 'string' ? echoed.length : echoed.length,
      items,
      heard,
      upload: upload.status === G9UploadStatus.Completed,
      download: download.status === G9UploadStatus.Completed && same(back, content),
    };
    await client.stop();
  }
  return out;
}
