#import "G9SignalRSocketCore.h"

static const NSInteger G9MaximumMessageSize = 64 * 1024 * 1024;

@class G9SignalRSocketCore;

/** One socket: its task, its session (the delegate), its queued events and the pending poll. */
@interface G9SignalRSocket : NSObject <NSURLSessionWebSocketDelegate>
@property(nonatomic, copy) NSString *socketId;
@property(nonatomic, strong, nullable) NSURLSession *session;
@property(nonatomic, strong, nullable) NSURLSessionWebSocketTask *task;
@property(nonatomic, strong) NSMutableArray<NSDictionary *> *queue;
@property(nonatomic, copy, nullable) G9SignalRReply waiter;
@property(nonatomic, assign) BOOL closeQueued;
@property(nonatomic, weak) G9SignalRSocketCore *core;
@end

@interface G9SignalRSocketCore ()
- (void)deliver:(G9SignalRSocket *)socket events:(NSArray<NSDictionary *> *)events reply:(G9SignalRReply)reply;
@end

@implementation G9SignalRSocket

- (instancetype)init {
  if ((self = [super init])) _queue = [NSMutableArray array];
  return self;
}

- (void)push:(NSDictionary *)event {
  G9SignalRReply waiter = nil;
  NSArray *events = nil;
  @synchronized(self) {
    if (self.closeQueued) return;
    if ([[event objectForKey:@"type"] isEqual:@"close"]) self.closeQueued = YES;
    [self.queue addObject:event];
    if (self.waiter == nil) return;
    waiter = self.waiter;
    self.waiter = nil;
    events = [self.queue copy];
    [self.queue removeAllObjects];
  }
  [self.core deliver:self events:events reply:waiter];
}

- (void)pushClose:(NSInteger)code reason:(NSString *)reason clean:(BOOL)clean {
  [self push:@{@"type" : @"close", @"code" : @(code), @"reason" : reason ?: @"", @"wasClean" : @(clean)}];
  [self.session finishTasksAndInvalidate];  // releases this delegate
}

- (void)receive {
  __weak G9SignalRSocket *weakSelf = self;
  [self.task receiveMessageWithCompletionHandler:^(NSURLSessionWebSocketMessage *message, NSError *error) {
    G9SignalRSocket *socket = weakSelf;
    if (socket == nil || error != nil || message == nil) return;  // failures arrive through the delegate
    if (message.type == NSURLSessionWebSocketMessageTypeString) {
      [socket push:@{@"type" : @"text", @"data" : message.string ?: @""}];
    } else {
      [socket push:@{@"type" : @"binary", @"data" : message.data ?: [NSData data]}];
    }
    [socket receive];
  }];
}

- (void)URLSession:(NSURLSession *)session
          webSocketTask:(NSURLSessionWebSocketTask *)webSocketTask
    didOpenWithProtocol:(NSString *)protocol {
  [self push:@{@"type" : @"open", @"protocol" : protocol ?: @""}];
}

- (void)URLSession:(NSURLSession *)session
       webSocketTask:(NSURLSessionWebSocketTask *)webSocketTask
    didCloseWithCode:(NSURLSessionWebSocketCloseCode)closeCode
              reason:(NSData *)reason {
  NSString *text = reason.length > 0 ? [[NSString alloc] initWithData:reason encoding:NSUTF8StringEncoding] : @"";
  [self pushClose:(NSInteger)closeCode reason:text clean:YES];
}

- (void)URLSession:(NSURLSession *)session task:(NSURLSessionTask *)task didCompleteWithError:(NSError *)error {
  if (error != nil) {
    [self push:@{@"type" : @"error", @"message" : error.localizedDescription ?: @"WebSocket error."}];
    [self pushClose:1006 reason:error.localizedDescription ?: @"" clean:NO];
  } else {
    NSInteger code = self.task.closeCode == NSURLSessionWebSocketCloseCodeInvalid ? 1006 : (NSInteger)self.task.closeCode;
    [self pushClose:code reason:@"" clean:code != 1006];
  }
}

@end

@implementation G9SignalRSocketCore {
  NSMutableDictionary<NSString *, G9SignalRSocket *> *_sockets;
}

+ (instancetype)shared {
  static G9SignalRSocketCore *core;
  static dispatch_once_t once;
  dispatch_once(&once, ^{
    core = [[G9SignalRSocketCore alloc] init];
  });
  return core;
}

- (instancetype)init {
  if ((self = [super init])) _sockets = [NSMutableDictionary dictionary];
  return self;
}

- (nullable G9SignalRSocket *)socket:(NSString *)socketId {
  @synchronized(self) {
    return [_sockets objectForKey:socketId];
  }
}

- (void)open:(NSString *)socketId url:(NSString *)url protocols:(NSArray *)protocols headers:(NSDictionary *)headers reply:(G9SignalRReply)reply {
  NSURL *target = [NSURL URLWithString:url];
  if (target == nil) {
    reply(G9SignalRFail(@"socket", [@"Invalid URL: " stringByAppendingString:url]));
    return;
  }
  G9SignalRSocket *socket = [[G9SignalRSocket alloc] init];
  socket.socketId = socketId;
  socket.core = self;
  @synchronized(self) {
    if ([_sockets objectForKey:socketId] != nil) {
      reply(G9SignalRFail(@"exists", [NSString stringWithFormat:@"Socket %@ is already open.", socketId]));
      return;
    }
    [_sockets setObject:socket forKey:socketId];
  }

  NSMutableURLRequest *request = [NSMutableURLRequest requestWithURL:target];
  for (id name in headers) {
    id value = [headers objectForKey:name];
    if ([name isKindOfClass:[NSString class]] && value != nil && value != [NSNull null]) {
      [request setValue:[value description] forHTTPHeaderField:name];
    }
  }
  NSMutableArray *names = [NSMutableArray array];
  for (id protocol in protocols) {
    if ([protocol isKindOfClass:[NSString class]]) [names addObject:protocol];
  }
  if (names.count > 0) [request setValue:[names componentsJoinedByString:@", "] forHTTPHeaderField:@"Sec-WebSocket-Protocol"];

  NSOperationQueue *queue = [[NSOperationQueue alloc] init];
  queue.maxConcurrentOperationCount = 1;  // events of one socket stay in order
  socket.session = [NSURLSession sessionWithConfiguration:[NSURLSessionConfiguration defaultSessionConfiguration]
                                                 delegate:socket
                                            delegateQueue:queue];
  socket.task = [socket.session webSocketTaskWithRequest:request];
  socket.task.maximumMessageSize = G9MaximumMessageSize;
  [socket.task resume];
  [socket receive];
  reply(G9SignalROk(nil));
}

- (nullable NSURLSessionWebSocketTask *)live:(NSString *)socketId {
  G9SignalRSocket *socket = [self socket:socketId];
  if (socket == nil) return nil;
  @synchronized(socket) {
    if (socket.closeQueued) return nil;
  }
  return socket.task;
}

- (void)send:(NSString *)socketId message:(NSURLSessionWebSocketMessage *)message reply:(G9SignalRReply)reply {
  NSURLSessionWebSocketTask *task = [self live:socketId];
  if (task == nil) {
    reply(G9SignalRFail(@"closed", [NSString stringWithFormat:@"Socket %@ is not open.", socketId]));
    return;
  }
  [task sendMessage:message
      completionHandler:^(NSError *error) {
        reply(error == nil ? G9SignalROk(nil) : G9SignalRFail(@"socket", error.localizedDescription));
      }];
}

- (void)sendText:(NSString *)socketId text:(NSString *)text reply:(G9SignalRReply)reply {
  [self send:socketId message:[[NSURLSessionWebSocketMessage alloc] initWithString:text ?: @""] reply:reply];
}

- (void)sendBinary:(NSString *)socketId data:(NSData *)data reply:(G9SignalRReply)reply {
  [self send:socketId message:[[NSURLSessionWebSocketMessage alloc] initWithData:data] reply:reply];
}

- (void)close:(NSString *)socketId code:(NSInteger)code reason:(NSString *)reason reply:(G9SignalRReply)reply {
  G9SignalRSocket *socket = [self socket:socketId];
  if (socket != nil) {
    NSData *why = reason.length > 0 ? [reason dataUsingEncoding:NSUTF8StringEncoding] : nil;
    [socket.task cancelWithCloseCode:(NSURLSessionWebSocketCloseCode)(code == 0 ? 1000 : code) reason:why];
  }
  reply(G9SignalROk(nil));
}

- (void)poll:(NSString *)socketId reply:(G9SignalRReply)reply {
  G9SignalRSocket *socket = [self socket:socketId];
  if (socket == nil) {
    reply(G9SignalROk(@{@"events" : @[ @{@"type" : @"close", @"code" : @1006, @"reason" : @"Unknown socket.", @"wasClean" : @NO} ]}));
    return;
  }
  NSArray *events = nil;
  @synchronized(socket) {
    if (socket.waiter != nil) {
      reply(G9SignalRFail(@"busy", @"Only one wsPoll at a time per socket."));
      return;
    }
    if (socket.queue.count == 0) {
      socket.waiter = reply;
      return;
    }
    events = [socket.queue copy];
    [socket.queue removeAllObjects];
  }
  [self deliver:socket events:events reply:reply];
}

- (void)deliver:(G9SignalRSocket *)socket events:(NSArray<NSDictionary *> *)events reply:(G9SignalRReply)reply {
  for (NSDictionary *event in events) {
    if ([[event objectForKey:@"type"] isEqual:@"close"]) {
      @synchronized(self) {
        [_sockets removeObjectForKey:socket.socketId];
      }
      break;
    }
  }
  reply(G9SignalROk(@{@"events" : events}));
}

@end
