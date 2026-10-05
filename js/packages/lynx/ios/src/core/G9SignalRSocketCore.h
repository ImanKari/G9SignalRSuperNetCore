#import <Foundation/Foundation.h>
#import "G9SignalREnvelope.h"

typedef void (^G9SignalRReply)(NSDictionary *envelope);

NS_ASSUME_NONNULL_BEGIN

/**
 * The WebSocket half of the module contract over NSURLSessionWebSocketTask (iOS 13+). Each socket queues its events;
 * `poll` replies with everything queued or waits for the next event. The last event is always `close`; the socket is
 * forgotten once it has been delivered. Messages up to 64 MiB (NSURLSession's default limit is 1 MiB, too small for
 * sync frames).
 */
@interface G9SignalRSocketCore : NSObject
+ (instancetype)shared;
- (void)open:(NSString *)socketId
          url:(NSString *)url
    protocols:(NSArray *)protocols
      headers:(NSDictionary *)headers
        reply:(G9SignalRReply)reply;
- (void)sendText:(NSString *)socketId text:(NSString *)text reply:(G9SignalRReply)reply;
- (void)sendBinary:(NSString *)socketId data:(NSData *)data reply:(G9SignalRReply)reply;
- (void)close:(NSString *)socketId code:(NSInteger)code reason:(NSString *)reason reply:(G9SignalRReply)reply;
- (void)poll:(NSString *)socketId reply:(G9SignalRReply)reply;
@end

NS_ASSUME_NONNULL_END
