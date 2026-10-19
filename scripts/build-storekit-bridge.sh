#!/usr/bin/env bash
set -euo pipefail

# Compiles native/LapsusStoreKit/StoreBridge.swift into libLapsusStoreKit.dylib, the StoreKit 2
# bridge the Mac App Store edition loads. Needs Xcode (or its command line tools) for swiftc.
#
# Usage: scripts/build-storekit-bridge.sh <output-dir>

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$1"
mkdir -p "$OUT_DIR"

# Swift 5 mode: the C callback is shared with StoreKit's tasks, which Swift 6's strict
# concurrency checking would reject although every call into it is independent.
xcrun swiftc "$ROOT/native/LapsusStoreKit/StoreBridge.swift" \
  -emit-library -O -swift-version 5 \
  -module-name LapsusStoreKit \
  -target arm64-apple-macos13.0 \
  -Xlinker -install_name -Xlinker @rpath/libLapsusStoreKit.dylib \
  -o "$OUT_DIR/libLapsusStoreKit.dylib"
