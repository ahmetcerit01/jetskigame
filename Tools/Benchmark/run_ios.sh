#!/bin/bash
# Builds and runs the benchmark player on a connected iPhone, and prints its report.
#   JETSKI_TEAM=<apple team id> [JETSKI_DEVICE=<udid>] Tools/Benchmark/run_ios.sh Release|Dev
# A run that cannot be completed ends with NOT MEASURED, never with an older report: the report on
# the phone is compared with the one from before this run and must have changed.
set -u
variant=${1:?Release or Dev}
unity=/Applications/Unity/Hub/Editor/$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)/Unity.app/Contents/MacOS/Unity
bundle=com.indiestudio.jetskirunner
report=$(echo "$variant" | tr 'A-Z' 'a-z').txt
logs=Logs/Benchmark/ios; mkdir -p "$logs"; out=$logs/$report; rm -f "$out"
fail() { echo "NOT MEASURED: $1"; exit 2; }
# With more than one iPhone around, say which: JETSKI_DEVICE=<udid>
paired=$(xcrun devicectl list devices 2>/dev/null | grep -F "available (paired)" | grep -v simulated | grep -oE '[0-9A-F]{8}-[0-9A-F]{16}')
if [ -n "${JETSKI_DEVICE:-}" ]; then device=$JETSKI_DEVICE; echo "$paired" | grep -qx "$device" || fail "iPhone $device is not available"
else
  [ -n "$paired" ] || fail "no paired iPhone is available"
  [ "$(echo "$paired" | wc -l | tr -d ' ')" = 1 ] || fail "several iPhones are available, set JETSKI_DEVICE"
  device=$paired
fi
app=Build/iOSBenchmark/dd$variant/Build/Products/Release-iphoneos/JetskiRunner.app
: "${JETSKI_TEAM:?set JETSKI_TEAM to the Apple team id}"
rm -rf "$app"
"$unity" -batchmode -quit -projectPath . -buildTarget iOS -executeMethod JetskiBuild.IOSBenchmark -benchmarkVariant "$variant" -logFile "$logs/unity.log" || fail "Unity build failed, see $logs/unity.log"
grep -q "JETSKI BUILD iOS: Succeeded" "$logs/unity.log" || fail "Unity build failed, see $logs/unity.log"
xcodebuild -project "Build/iOSBenchmark/$variant/Unity-iPhone.xcodeproj" -scheme Unity-iPhone -configuration Release -destination generic/platform=iOS \
  -derivedDataPath "Build/iOSBenchmark/dd$variant" -allowProvisioningUpdates DEVELOPMENT_TEAM="$JETSKI_TEAM" CODE_SIGN_STYLE=Automatic CODE_SIGN_IDENTITY="Apple Development" PROVISIONING_PROFILE_SPECIFIER= build > "$logs/xcode$variant.log" 2>&1 || fail "Xcode build failed, see $logs/xcode$variant.log"
[ -d "$app" ] || fail "the build produced no app"
before=$(mktemp)
xcrun devicectl device copy from --device "$device" --domain-type appDataContainer --domain-identifier "$bundle" --source "Documents/$report" --destination "$before" >/dev/null 2>&1
xcrun devicectl device install app --device "$device" "$app" >/dev/null 2>&1 || fail "could not install on the iPhone"
launched=
for _ in $(seq 1 30); do
  if xcrun devicectl device process launch --activate --terminate-existing --device "$device" "$bundle" 2>&1 | grep -q "Launched application"; then launched=1; break; fi
  sleep 10
done
[ -n "$launched" ] || fail "could not launch: is the iPhone unlocked?"
echo "Running on the iPhone. Do not touch it."
# Asking for the file during the run can itself disturb it, so wait out the usual length first.
sleep 200
for _ in $(seq 1 30); do
  xcrun devicectl device copy from --device "$device" --domain-type appDataContainer --domain-identifier "$bundle" --source "Documents/$report" --destination "$out" >/dev/null 2>&1
  [ -s "$out" ] && ! cmp -s "$out" "$before" && break
  rm -f "$out"
  sleep 10
done
[ -s "$out" ] || fail "the run never wrote a new report"
sed -n '1p;/^biome/,/^counters/p' "$out"
