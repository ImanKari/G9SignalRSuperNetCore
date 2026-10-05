#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

/** `{ ok: true, value }` (types/g9-signalr-lynx-module.d.ts). `nil` becomes NSNull. */
static inline NSDictionary *G9SignalROk(id _Nullable value) {
  return @{@"ok" : [NSNumber numberWithBool:YES], @"value" : value ?: [NSNull null]};
}

/** `{ ok: false, error: { code, message } }`. */
static inline NSDictionary *G9SignalRFail(NSString *code, NSString *_Nullable message) {
  return @{@"ok" : [NSNumber numberWithBool:NO], @"error" : @{@"code" : code, @"message" : message ?: code}};
}


NS_ASSUME_NONNULL_END
