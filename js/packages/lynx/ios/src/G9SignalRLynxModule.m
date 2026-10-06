#import "G9SignalRLynxModule.h"
#import "core/G9SignalRFileCore.h"
#import "core/G9SignalRSocketCore.h"

typedef void (^G9Reply)(id envelope);

static NSString *const G9SignalRLynxVersion = @"2.10.1";

@implementation G9SignalRLynxModule

+ (NSString *)name {
  return @"G9SignalRLynxModule";
}

+ (NSDictionary<NSString *, NSString *> *)methodLookup {
  return @{
    @"capabilities" : NSStringFromSelector(@selector(capabilities:)),
    @"wsOpen" : NSStringFromSelector(@selector(wsOpen:url:protocols:headers:callback:)),
    @"wsSendText" : NSStringFromSelector(@selector(wsSendText:text:callback:)),
    @"wsSendBinary" : NSStringFromSelector(@selector(wsSendBinary:data:callback:)),
    @"wsClose" : NSStringFromSelector(@selector(wsClose:code:reason:callback:)),
    @"wsPoll" : NSStringFromSelector(@selector(wsPoll:callback:)),
    @"fileStat" : NSStringFromSelector(@selector(fileStat:callback:)),
    @"fileRead" : NSStringFromSelector(@selector(fileRead:offset:length:callback:)),
    @"fileWrite" : NSStringFromSelector(@selector(fileWrite:offset:data:truncate:callback:)),
    @"fileMove" : NSStringFromSelector(@selector(fileMove:to:callback:)),
    @"fileDelete" : NSStringFromSelector(@selector(fileDelete:callback:)),
  };
}

/** File work runs on one serial queue, in call order (the downloader's writes to a `.partial` file depend on it). */
+ (dispatch_queue_t)fileQueue {
  static dispatch_queue_t queue;
  static dispatch_once_t once;
  dispatch_once(&once, ^{
    queue = dispatch_queue_create("g9.signalr.files", DISPATCH_QUEUE_SERIAL);
  });
  return queue;
}

static void G9Send(id callback, NSDictionary *envelope) {
  G9Reply reply = (G9Reply)callback;
  if (reply) reply(envelope);
}

static void G9OnFiles(id callback, NSDictionary * (^work)(void)) {
  dispatch_async([G9SignalRLynxModule fileQueue], ^{
    NSDictionary *envelope;
    @autoreleasepool {
      envelope = work();
    }
    G9Send(callback, envelope);
  });
}

- (void)capabilities:(id)callback {
  G9Send(callback, G9SignalROk(@{
    @"webSocket" : @YES,
    @"binary" : @YES,
    @"files" : @YES,
    @"platform" : @"ios",
    @"version" : G9SignalRLynxVersion,
  }));
}

- (void)wsOpen:(NSString *)socketId url:(NSString *)url protocols:(id)protocols headers:(id)headers callback:(id)callback {
  NSArray *list = [protocols isKindOfClass:[NSArray class]] ? protocols : @[];
  NSDictionary *map = [headers isKindOfClass:[NSDictionary class]] ? headers : @{};
  [[G9SignalRSocketCore shared] open:socketId url:url protocols:list headers:map reply:^(NSDictionary *envelope) {
    G9Send(callback, envelope);
  }];
}

- (void)wsSendText:(NSString *)socketId text:(NSString *)text callback:(id)callback {
  [[G9SignalRSocketCore shared] sendText:socketId text:text reply:^(NSDictionary *envelope) {
    G9Send(callback, envelope);
  }];
}

- (void)wsSendBinary:(NSString *)socketId data:(id)data callback:(id)callback {
  if (![data isKindOfClass:[NSData class]]) {
    G9Send(callback, G9SignalRFail(@"invalid", @"data must be an ArrayBuffer."));
    return;
  }
  [[G9SignalRSocketCore shared] sendBinary:socketId data:data reply:^(NSDictionary *envelope) {
    G9Send(callback, envelope);
  }];
}

- (void)wsClose:(NSString *)socketId code:(double)code reason:(NSString *)reason callback:(id)callback {
  [[G9SignalRSocketCore shared] close:socketId code:(NSInteger)code reason:reason ?: @"" reply:^(NSDictionary *envelope) {
    G9Send(callback, envelope);
  }];
}

- (void)wsPoll:(NSString *)socketId callback:(id)callback {
  [[G9SignalRSocketCore shared] poll:socketId reply:^(NSDictionary *envelope) {
    G9Send(callback, envelope);
  }];
}

- (void)fileStat:(NSString *)path callback:(id)callback {
  G9OnFiles(callback, ^NSDictionary * {
    return [G9SignalRFileCore stat:path];
  });
}

- (void)fileRead:(NSString *)path offset:(double)offset length:(double)length callback:(id)callback {
  G9OnFiles(callback, ^NSDictionary * {
    return [G9SignalRFileCore read:path offset:(long long)offset length:(NSUInteger)length];
  });
}

- (void)fileWrite:(NSString *)path offset:(double)offset data:(id)data truncate:(BOOL)truncate callback:(id)callback {
  if (![data isKindOfClass:[NSData class]]) {
    G9Send(callback, G9SignalRFail(@"invalid", @"data must be an ArrayBuffer."));
    return;
  }
  G9OnFiles(callback, ^NSDictionary * {
    return [G9SignalRFileCore write:path offset:(long long)offset data:data truncate:truncate];
  });
}

- (void)fileMove:(NSString *)from to:(NSString *)to callback:(id)callback {
  G9OnFiles(callback, ^NSDictionary * {
    return [G9SignalRFileCore move:from to:to];
  });
}

- (void)fileDelete:(NSString *)path callback:(id)callback {
  G9OnFiles(callback, ^NSDictionary * {
    return [G9SignalRFileCore remove:path];
  });
}

@end
