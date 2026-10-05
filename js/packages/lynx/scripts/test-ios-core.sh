#!/usr/bin/env bash
# Builds and runs the iOS file core (ios/src/core/G9SignalRFileCore.m) with its Linux suite: clang + GNUstep base.
# Needs: apt-get install -y clang make libgnustep-base-dev gobjc. On Windows `npm run test:ios-core` runs this in WSL.
set -euo pipefail
cd "$(dirname "$0")/.."
flags=$(gnustep-config --objc-flags | tr ' ' '\n' | grep -v '^-M' | tr '\n' ' ')
libs=$(gnustep-config --base-libs)
if [ -z "$flags" ] || [ -z "$libs" ]; then
  echo "blocked: gnustep-config printed no flags (apt-get install -y make libgnustep-base-dev gobjc)" >&2
  exit 2
fi
gcc_include=$(ls -d /usr/lib/gcc/x86_64-linux-gnu/*/include 2>/dev/null | while read -r d; do [ -f "$d/objc/objc.h" ] && echo "$d"; done | head -n 1)
out=build/ios-core
mkdir -p "$out"
# shellcheck disable=SC2086
clang $flags -fobjc-runtime=gcc ${gcc_include:+-I$gcc_include} -Wno-unused-command-line-argument -Werror=objc-method-access \
  -o "$out/ios-core-tests" ios/src/core/G9SignalRFileCore.m ios/Tests/linux/main.m $libs -lm
"$out/ios-core-tests"
