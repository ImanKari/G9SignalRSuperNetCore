#import <Foundation/Foundation.h>
#import "G9SignalREnvelope.h"

NS_ASSUME_NONNULL_BEGIN

/**
 * The file half of the module contract (what the .NET-twin uploader and downloader need): `FileInfo` facts, ranged
 * reads, positioned writes for the `.partial` file, rename, delete. Foundation only, so it also builds with GNUstep.
 */
@interface G9SignalRFileCore : NSObject
+ (NSDictionary *)stat:(NSString *)path;
+ (NSDictionary *)read:(NSString *)path offset:(long long)offset length:(NSUInteger)length;
+ (NSDictionary *)write:(NSString *)path offset:(long long)offset data:(NSData *)data truncate:(BOOL)truncate;
+ (NSDictionary *)move:(NSString *)from to:(NSString *)to;
+ (NSDictionary *)remove:(NSString *)path;
@end

NS_ASSUME_NONNULL_END
