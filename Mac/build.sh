#!/usr/bin/env bash
set -euo pipefail

task_quit_running=false
for task_arg in "$@"; do
  case "$task_arg" in
    --quit-running) task_quit_running=true ;;
    *)
      echo "지원 옵션: --quit-running (이 bundle로 실행 중인 앱을 묻지 않고 종료한 뒤 빌드)" >&2
      exit 2
      ;;
  esac
done

echo "AI Burger Clock macOS 빌드 준비를 확인합니다."
task_script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
task_app="$task_script_dir/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
  echo "Apple Silicon macOS에서 실행하세요. Windows/Linux 빌드는 네이티브 검증이 아닙니다." >&2
  exit 2
fi
if [[ "$EUID" -eq 0 ]]; then
  echo "앱 빌드는 sudo 없이 실행하세요. 관리자 권한은 workload 설치 때만 필요할 수 있습니다." >&2
  exit 2
fi

# A build rewrites the bundle in place. An app running from that bundle can then read replaced
# code or resources (macOS may kill it on a code-signature mismatch), so never overwrite it silently.
# Only processes started from this exact bundle count; a copy in /Applications is not touched.
task_executables=(
  "$task_app/Contents/MacOS/AI Burger Clock"
  "$(cd -- "$task_script_dir" && pwd -P)/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS/AI Burger Clock"
)
task_running_pids() {
  local pid command
  while read -r pid command; do
    if [[ "$command" == "${task_executables[0]}" || "$command" == "${task_executables[1]}" ]]; then
      printf '%s\n' "$pid"
    fi
  done < <(/bin/ps -axo pid=,comm=)
}
task_pids="$(task_running_pids)"
if [[ -n "$task_pids" ]]; then
  task_pid_list="$(printf '%s' "$task_pids" | tr '\n' ' ')"
  echo "실행 중인 AI Burger Clock(PID ${task_pid_list% })이 빌드할 bundle을 사용 중입니다: $task_app" >&2
  task_quit=false
  if [[ "$task_quit_running" == true ]]; then
    task_quit=true
  elif [[ -t 0 ]]; then
    # EOF (Ctrl-D) counts as No instead of ending the script through set -e.
    read -r -p "종료하고 빌드할까요? [y/N] " task_answer || task_answer=""
    if [[ "$task_answer" == [yY] || "$task_answer" == [yY][eE][sS] ]]; then
      task_quit=true
    fi
  else
    echo "대화형 터미널이 아니어서 묻지 않고 멈춥니다. 앱을 메뉴바 아이콘 오른쪽 클릭 → 종료로 닫은 뒤 다시 실행하거나, --quit-running으로 종료 후 빌드하세요." >&2
    exit 2
  fi
  if [[ "$task_quit" != true ]]; then
    echo "빌드를 시작하지 않았습니다. 앱을 종료한 뒤 다시 실행하세요." >&2
    exit 2
  fi
  # A normal quit request (NSRunningApplication terminate) to exactly these PIDs, never by bundle id:
  # another copy of the app may be running elsewhere. The app finishes pending writes before it exits.
  echo "앱에 종료를 요청합니다..."
  # shellcheck disable=SC2086 # One argument per PID.
  if ! /usr/bin/osascript - $task_pids <<'APPLESCRIPT'
use framework "AppKit"
on run argv
  repeat with pidText in argv
    set target to current application's NSRunningApplication's runningApplicationWithProcessIdentifier:(pidText as integer)
    if target is missing value then error "PID " & pidText & " is not a running application."
    if not ((target's terminate()) as boolean) then error "PID " & pidText & " did not accept the quit request."
  end repeat
end run
APPLESCRIPT
  then
    echo "종료 요청에 실패했습니다. 앱을 직접 종료한 뒤 다시 실행하세요. 빌드를 시작하지 않았습니다." >&2
    exit 2
  fi
  for _ in $(seq 1 40); do
    [[ -z "$(task_running_pids)" ]] && break
    sleep 0.5
  done
  if [[ -n "$(task_running_pids)" ]]; then
    echo "20초 안에 앱이 종료되지 않았습니다. 앱을 직접 종료한 뒤 다시 실행하세요. 빌드를 시작하지 않았습니다." >&2
    exit 2
  fi
  echo "앱이 종료되었습니다. 빌드를 계속합니다."
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET 10 SDK가 필요합니다. 이 스크립트는 소프트웨어를 설치하지 않습니다." >&2
  exit 2
fi
echo "Xcode 개발 도구 경로 확인 중..."
if ! /usr/bin/xcode-select -p; then
  echo "Xcode 27 개발 도구 경로를 먼저 확인하세요." >&2
  exit 2
fi
echo "Xcode 버전 확인 중..."
# Keep Apple's diagnostic on stderr; do not let set -e silently end an assignment.
if ! task_xcode_version="$(/usr/bin/xcodebuild -version)"; then
  printf '%s\n' "$task_xcode_version" >&2
  echo "Xcode 버전 확인 실패: 위 오류를 확인하세요. 앱 검사와 컴파일은 시작하지 않았습니다." >&2
  echo "읽기 전용 확인: /usr/bin/xcodebuild -version 및 /usr/bin/xcode-select -p" >&2
  exit 2
fi
if [[ "$task_xcode_version" != Xcode\ 27* ]]; then
  echo "macOS 27 SDK를 제공하는 Xcode 27이 필요합니다. 현재: $task_xcode_version" >&2
  exit 2
fi
echo "$task_xcode_version"
cd "$task_script_dir"
echo ".NET SDK 확인 중..."
if ! task_sdk_version="$(dotnet --version)"; then
  printf '%s\n' "$task_sdk_version" >&2
  echo ".NET SDK 확인에 실패했습니다. 위 오류와 dotnet 설치 경로를 확인하세요." >&2
  exit 2
fi
if [[ "$task_sdk_version" != 10.0.* || "$task_sdk_version" == *-* ]]; then
  echo "안정판 .NET 10 SDK가 필요합니다. 현재: $task_sdk_version" >&2
  exit 2
fi
echo ".NET SDK: $task_sdk_version"
echo ".NET macos workload 확인 중..."
if ! task_workloads="$(dotnet workload list)"; then
  printf '%s\n' "$task_workloads" >&2
  echo ".NET workload 조회에 실패했습니다. 설치나 설정은 변경하지 않았습니다." >&2
  exit 2
fi
if ! printf '%s\n' "$task_workloads" | awk '$1 == "macos" { found = 1 } END { exit !found }'; then
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

echo "NuGet HTTP cache: $NUGET_HTTP_CACHE_PATH"
dotnet run --project ../Shared.Tests/AiBurgerClock.Shared.Tests.csproj -c Release --property:TreatWarningsAsErrors=true
dotnet build AiBurgerClock.Mac.csproj -c Release -warnaserror
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
