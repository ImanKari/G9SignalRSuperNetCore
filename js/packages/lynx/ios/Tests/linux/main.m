// The iOS file core (ios/src/core/G9SignalRFileCore.m) on Linux with clang + GNUstep base: the same checks as the
// Kotlin G9FileCoreTest, so both platforms answer the module contract alike. The socket core needs
// NSURLSessionWebSocketTask (Apple only) and is covered on macOS (Native-Validation-Pending.md, N-SR-02).
// Run: npm run test:ios-core (Windows: inside WSL Ubuntu 24.04 with clang, make, libgnustep-base-dev, gobjc).
#import <Foundation/Foundation.h>
#include <math.h>
#include <unistd.h>
#import "../../src/core/G9SignalRFileCore.h"

static int failures = 0;
static int checks = 0;

// The condition is the LAST argument: a comma inside an Objective-C message would split an earlier one.
#define CHECK(what, ...)                                    \
  do {                                                      \
    checks++;                                               \
    if (!(__VA_ARGS__)) {                                   \
      failures++;                                           \
      fprintf(stderr, "FAIL %s (line %d)\n", what, __LINE__); \
    }                                                       \
  } while (0)

static NSDictionary *value(NSDictionary *reply) {
  if (![[reply objectForKey:@"ok"] boolValue]) {
    fprintf(stderr, "unexpected failure: %s\n", [[reply description] UTF8String]);
    failures++;
    return @{};
  }
  id v = [reply objectForKey:@"value"];
  return [v isKindOfClass:[NSDictionary class]] ? v : @{};
}

static NSString *code(NSDictionary *reply) {
  return [[reply objectForKey:@"ok"] boolValue] ? @"" : [[reply objectForKey:@"error"] objectForKey:@"code"];
}

int main(void) {
  @autoreleasepool {
    NSString *dir = [NSTemporaryDirectory() stringByAppendingPathComponent:[NSString stringWithFormat:@"g9-files-%d", getpid()]];
    [[NSFileManager defaultManager] createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:NULL];

    // stat: the .NET FileInfo facts
    NSString *photo = [dir stringByAppendingPathComponent:@"Photo.JPG"];
    NSMutableData *bytes = [NSMutableData dataWithLength:1234];
    [bytes writeToFile:photo atomically:NO];
    NSDictionary *stat = value([G9SignalRFileCore stat:photo]);
    CHECK("stat exists", [[stat objectForKey:@"exists"] boolValue]);
    CHECK("stat size", [[stat objectForKey:@"size"] longLongValue] == 1234);
    CHECK("stat fullPath", [[stat objectForKey:@"fullPath"] isEqual:[photo stringByStandardizingPath]]);
    NSDate *modified = [[[NSFileManager defaultManager] attributesOfItemAtPath:photo error:NULL] objectForKey:NSFileModificationDate];
    long long expected = 621355968000000000LL + (long long)llround(modified.timeIntervalSince1970 * 10000000.0);
    CHECK("stat ticks", [[stat objectForKey:@"lastWriteTicks"] isEqual:[NSString stringWithFormat:@"%lld", expected]]);
    CHECK("stat missing", ![[value([G9SignalRFileCore stat:[dir stringByAppendingPathComponent:@"missing"]]) objectForKey:@"exists"] boolValue]);

    // write at offsets (creating directories), truncate, ranged reads
    NSString *partial = [dir stringByAppendingPathComponent:@"a/b/out.partial"];
    const unsigned char first[] = {1, 2, 3, 4, 5, 6};
    const unsigned char second[] = {9, 9};
    value([G9SignalRFileCore write:partial offset:0 data:[NSData dataWithBytes:first length:6] truncate:NO]);
    value([G9SignalRFileCore write:partial offset:2 data:[NSData dataWithBytes:second length:2] truncate:YES]);
    const unsigned char whole[] = {1, 2, 9, 9};
    CHECK("write + truncate", [[NSData dataWithContentsOfFile:partial] isEqual:[NSData dataWithBytes:whole length:4]]);
    const unsigned char middle[] = {2, 9};
    CHECK("read range", [[value([G9SignalRFileCore read:partial offset:1 length:2]) objectForKey:@"data"] isEqual:[NSData dataWithBytes:middle length:2]]);
    CHECK("read past end", [[value([G9SignalRFileCore read:partial offset:3 length:100]) objectForKey:@"data"] length] == 1);
    CHECK("read missing", [code([G9SignalRFileCore read:[dir stringByAppendingPathComponent:@"none"] offset:0 length:1]) isEqual:@"not-found"]);
    CHECK("read negative", [code([G9SignalRFileCore read:partial offset:-1 length:1]) isEqual:@"invalid"]);

    // move only onto a free target; delete is idempotent
    NSString *from = [dir stringByAppendingPathComponent:@"x.partial"];
    NSString *taken = [dir stringByAppendingPathComponent:@"taken"];
    [@"x" writeToFile:from atomically:NO encoding:NSUTF8StringEncoding error:NULL];
    [@"t" writeToFile:taken atomically:NO encoding:NSUTF8StringEncoding error:NULL];
    CHECK("move onto existing", [code([G9SignalRFileCore move:from to:taken]) isEqual:@"exists"]);
    NSString *moved = [dir stringByAppendingPathComponent:@"sub/x"];
    value([G9SignalRFileCore move:from to:moved]);
    CHECK("move into new dir", [[NSString stringWithContentsOfFile:moved encoding:NSUTF8StringEncoding error:NULL] isEqual:@"x"]);
    CHECK("move missing", [code([G9SignalRFileCore move:from to:[dir stringByAppendingPathComponent:@"y"]]) isEqual:@"not-found"]);
    value([G9SignalRFileCore remove:taken]);
    value([G9SignalRFileCore remove:taken]);
    CHECK("remove", ![[NSFileManager defaultManager] fileExistsAtPath:taken]);

    [[NSFileManager defaultManager] removeItemAtPath:dir error:NULL];
    printf("ios file core: %d checks, %d failures\n", checks, failures);
  }
  return failures == 0 ? 0 : 1;
}
