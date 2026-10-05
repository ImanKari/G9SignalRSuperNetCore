#import <Foundation/Foundation.h>
#import <Lynx/LynxModule.h>
#import "generated/G9SignalRLynxModuleSpec.h"

NS_ASSUME_NONNULL_BEGIN

// Lynx 4.0.3 (the iOS pods) has no self-registration: hosts register the module on their LynxConfig
// (`[config registerModule:G9SignalRLynxModule.class]`), next to any other native module they use.

/**
 * `NativeModules.G9SignalRLynxModule` on iOS (types/g9-signalr-lynx-module.d.ts): a binary WebSocket over
 * NSURLSessionWebSocketTask and the file operations of the .NET-twin transfers. One process-wide socket table, so every
 * LynxView of the app shares it (socket ids carry a per-context prefix).
 */
@interface G9SignalRLynxModule : NSObject <G9SignalRLynxModuleSpec>
@end

NS_ASSUME_NONNULL_END
