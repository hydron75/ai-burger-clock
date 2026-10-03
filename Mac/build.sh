#!/usr/bin/env bash
set -euo pipefail

task_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
  echo "Apple Silicon macOS에서 실행하세요. Windows/Linux 빌드는 네이티브 검증이 아닙니다." >&2
  exit 2
fi
if [[ "$EUID" -eq 0 ]]; then
  echo "앱 빌드는 sudo 없이 실행하세요. 관리자 권한은 workload 설치 때만 필요할 수 있습니다." >&2
  exit 2
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET 10 SDK가 필요합니다. 이 스크립트는 소프트웨어를 설치하지 않습니다." >&2
  exit 2
fi
if ! /usr/bin/xcode-select -p >/dev/null 2>&1; then
  echo "Xcode 27 개발 도구 경로를 먼저 확인하세요." >&2
  exit 2
fi
task_xcode_version="$(/usr/bin/xcodebuild -version 2>/dev/null)"
if [[ "$task_xcode_version" != Xcode\ 27* ]]; then
  echo "macOS 27 SDK를 제공하는 Xcode 27이 필요합니다. 현재: $task_xcode_version" >&2
  exit 2
fi
cd "$task_script_dir"
task_sdk_version="$(dotnet --version)"
if [[ "$task_sdk_version" != 10.0.* || "$task_sdk_version" == *-* ]]; then
  echo "안정판 .NET 10 SDK가 필요합니다. 현재: $task_sdk_version" >&2
  exit 2
fi
if ! dotnet workload list | awk '$1 == "macos" { found = 1 } END { exit !found }'; then
  echo ".NET macos workload가 필요합니다. 공식 설치 안내를 확인하세요(자동 설치하지 않음)." >&2
  exit 2
fi
# A sudo workload installation can leave the default user HTTP cache inaccessible.
# Use a build-local cache unless the caller explicitly supplies a different one.
# This changes neither NuGet audit nor the global package cache/settings.
task_http_cache="${NUGET_HTTP_CACHE_PATH:-$task_script_dir/../artifacts/mac-build/nuget-http-cache}"
mkdir -p -- "$task_http_cache"
if [[ ! -w "$task_http_cache" ]]; then
  echo "NuGet HTTP 캐시에 쓸 수 없습니다: $task_http_cache" >&2
  exit 2
fi
export NUGET_HTTP_CACHE_PATH="$task_http_cache"

echo ".NET SDK: $task_sdk_version"
echo "$task_xcode_version"
echo "NuGet HTTP cache: $NUGET_HTTP_CACHE_PATH"
dotnet run --project ../Shared.Tests/AiBurgerClock.Shared.Tests.csproj -c Release --property:TreatWarningsAsErrors=true
dotnet build AiBurgerClock.Mac.csproj -c Release -warnaserror
task_app="$task_script_dir/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
if [[ ! -d "$task_app" ]]; then
  echo "빌드는 끝났으나 예상 .app가 없습니다. MSBuild 출력에서 실제 bundle 경로를 확인하세요." >&2
  exit 1
fi
/usr/bin/codesign --verify --deep --strict "$task_app"
task_sqlite_libraries="$(find "$task_app" -type f -name 'libe_sqlite3.dylib' -print)"
if [[ -z "$task_sqlite_libraries" ]]; then
  echo "SQLite 네이티브 라이브러리가 .app에 없습니다. bundle 출력을 확인하세요." >&2
  exit 1
fi
while IFS= read -r task_sqlite_library; do
  /usr/bin/file "$task_sqlite_library"
done <<< "$task_sqlite_libraries"
echo "Bundle: $task_app"
echo "앱을 실행하려면: open \"$task_app\""
echo "안전한 네이티브 검사: \"$task_app/Contents/MacOS/AI Burger Clock\" --smoke-test"
