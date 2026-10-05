#import "G9SignalRFileCore.h"
#include <math.h>

/** .NET ticks (100 ns since 0001-01-01) at the Unix epoch. */
static const long long G9UnixEpochTicks = 621355968000000000LL;

@implementation G9SignalRFileCore

+ (NSDictionary *)stat:(NSString *)path {
  NSFileManager *files = [NSFileManager defaultManager];
  BOOL directory = NO;
  if (![files fileExistsAtPath:path isDirectory:&directory] || directory) return G9SignalROk(@{@"exists" : [NSNumber numberWithBool:NO]});
  NSError *error = nil;
  NSDictionary *attributes = [files attributesOfItemAtPath:path error:&error];
  if (attributes == nil) return G9SignalRFail(@"io", error.localizedDescription);
  NSDate *modified = [attributes objectForKey:NSFileModificationDate] ?: [NSDate dateWithTimeIntervalSince1970:0];
  // NSDate keeps sub-microsecond precision as a double: ticks are exact to ~100 ns for current dates.
  long long ticks = G9UnixEpochTicks + (long long)llround(modified.timeIntervalSince1970 * 10000000.0);
  NSString *full = [path stringByStandardizingPath];  // like .NET FullName: normalized, symlinks kept
  return G9SignalROk(@{
    @"exists" : [NSNumber numberWithBool:YES],
    @"size" : @([[attributes objectForKey:NSFileSize] unsignedLongLongValue]),
    @"fullPath" : full,
    @"lastWriteTicks" : [NSString stringWithFormat:@"%lld", ticks],
  });
}

+ (NSDictionary *)read:(NSString *)path offset:(long long)offset length:(NSUInteger)length {
  if (offset < 0) return G9SignalRFail(@"invalid", @"offset must be non-negative.");
  NSFileHandle *handle = [NSFileHandle fileHandleForReadingAtPath:path];
  if (handle == nil) return G9SignalRFail(@"not-found", [@"No such file: " stringByAppendingString:path]);
  @try {
    [handle seekToFileOffset:(unsigned long long)offset];
    NSData *data = [handle readDataOfLength:length];
    return G9SignalROk(@{@"data" : data ?: [NSData data]});
  } @catch (NSException *exception) {
    return G9SignalRFail(@"io", exception.reason);
  } @finally {
    [handle closeFile];
  }
}

+ (NSDictionary *)write:(NSString *)path offset:(long long)offset data:(NSData *)data truncate:(BOOL)truncate {
  if (offset < 0) return G9SignalRFail(@"invalid", @"offset must be non-negative.");
  NSFileManager *files = [NSFileManager defaultManager];
  NSError *error = nil;
  NSString *directory = [path stringByDeletingLastPathComponent];
  if (directory.length > 0 && ![files createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:nil error:&error]) {
    return G9SignalRFail(@"io", error.localizedDescription);
  }
  if (![files fileExistsAtPath:path] && ![files createFileAtPath:path contents:nil attributes:nil]) {
    return G9SignalRFail(@"io", [@"Could not create " stringByAppendingString:path]);
  }
  NSFileHandle *handle = [NSFileHandle fileHandleForUpdatingAtPath:path];
  if (handle == nil) return G9SignalRFail(@"io", [@"Could not open " stringByAppendingString:path]);
  @try {
    [handle seekToFileOffset:(unsigned long long)offset];
    [handle writeData:data];
    if (truncate) [handle truncateFileAtOffset:(unsigned long long)offset + data.length];
    [handle synchronizeFile];
    return G9SignalROk(nil);
  } @catch (NSException *exception) {
    return G9SignalRFail(@"io", exception.reason);
  } @finally {
    [handle closeFile];
  }
}

+ (NSDictionary *)move:(NSString *)from to:(NSString *)to {
  NSFileManager *files = [NSFileManager defaultManager];
  if (![files fileExistsAtPath:from]) return G9SignalRFail(@"not-found", [@"No such file: " stringByAppendingString:from]);
  if ([files fileExistsAtPath:to]) return G9SignalRFail(@"exists", [@"The target file already exists: " stringByAppendingString:to]);
  NSError *error = nil;
  NSString *directory = [to stringByDeletingLastPathComponent];
  if (directory.length > 0) [files createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:nil error:NULL];
  if (![files moveItemAtPath:from toPath:to error:&error]) return G9SignalRFail(@"io", error.localizedDescription);
  return G9SignalROk(nil);
}

+ (NSDictionary *)remove:(NSString *)path {
  NSFileManager *files = [NSFileManager defaultManager];
  if (![files fileExistsAtPath:path]) return G9SignalROk(nil);
  NSError *error = nil;
  if (![files removeItemAtPath:path error:&error]) return G9SignalRFail(@"io", error.localizedDescription);
  return G9SignalROk(nil);
}

@end
