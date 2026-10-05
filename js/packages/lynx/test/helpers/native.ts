// The Lynxtron bridge (the contract's reference implementation) in the two shapes the Lynx side meets: promise methods
// (desktop) and callback methods (what the Android/iOS module looks like through Lynx's bridge).
import { createG9SignalRBridge } from '../../src/lynxtron/index.js';
import { nativeFromCallbackModule, nativeFromPromiseModule, type G9SignalRNative } from '../../src/native.js';

export function callbackModuleOf(
  bridge: Record<string, (...args: never[]) => Promise<unknown>>,
): Record<string, (...args: unknown[]) => void> {
  const module: Record<string, (...args: unknown[]) => void> = {};
  for (const [name, fn] of Object.entries(bridge)) {
    module[name] = (...args: unknown[]) => {
      const callback = args.pop() as (reply: unknown) => void;
      void (fn as (...a: unknown[]) => Promise<unknown>)(...args).then(callback);
    };
  }
  return module;
}

/** Both shapes over one fresh bridge each. */
export function natives(): Array<[string, G9SignalRNative]> {
  return [
    ['callback module (Android/iOS shape)', nativeFromCallbackModule(callbackModuleOf(createG9SignalRBridge()))],
    ['promise bridge (Lynxtron shape)', nativeFromPromiseModule(createG9SignalRBridge())],
  ];
}

export async function until(predicate: () => boolean, ms = 5000): Promise<void> {
  const deadline = Date.now() + ms;
  while (!predicate()) {
    if (Date.now() > deadline) throw new Error('timed out');
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}
