#!/usr/bin/env bash
# Synthetic checks of Mac/build.sh: no build, no app, no sudo, no real dotnet evaluation. Run from anywhere:
#   bash Mac/tools/check-build-script.sh [path/to/build.sh]
# Each check copies the function under test out of the script and runs it against stand-ins in a temporary folder.
set -uo pipefail

script="${1:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)/build.sh}"
work="$(mktemp -d)"
trap 'rm -rf "$work"; [[ -n "${fake_pid:-}" ]] && kill "$fake_pid" 2>/dev/null' EXIT
failures=0
pass() { echo "PASS $1"; }
fail() { echo "FAIL $1"; failures=$((failures + 1)); }

# Source of one top-level function of the script.
function_source() { sed -n "/^$1() {/,/^}/p" "$script"; }

# --- BUILD-04: the required Xcode is what the project evaluates, not the highest pack on disk -----------
# A DOTNET_ROOT with two macOS SDK packs on disk; the highest one asks for 27.5. The stand-in dotnet answers
# the way the project's evaluation would (the workload selected the older pack: 27.1).
root="$work/dotnet-root/packs/Microsoft.macOS.Sdk.net10.0_27.0"
for pack in "27.0.10000:27.1" "27.0.99999:27.5"; do
  mkdir -p "$root/${pack%%:*}/targets"
  printf '<Project><PropertyGroup><RecommendedXcodeVersion>%s</RecommendedXcodeVersion></PropertyGroup></Project>\n' \
    "${pack##*:}" > "$root/${pack%%:*}/targets/Microsoft.macOS.Sdk.Versions.props"
done
mkdir -p "$work/bin"
cat > "$work/bin/dotnet" <<'STUB'
#!/usr/bin/env bash
if [[ "$1" == msbuild && "$*" == *-getProperty:RecommendedXcodeVersion* ]]; then
  [[ "${STUB_FAIL:-}" == 1 ]] && exit 1
  printf '%s\n' "${STUB_XCODE:-27.1.2}"
  exit 0
fi
exit 99
STUB
chmod +x "$work/bin/dotnet"
required_source="$(function_source task_required_xcode_version)"
required() { ( PATH="$work/bin:$PATH" DOTNET_ROOT="$work/dotnet-root" task_script_dir="$work" bash -c "$required_source"$'\n''task_required_xcode_version' ); }
got="$(required)"
if [[ "$got" == "27.1" ]]; then pass "required Xcode comes from the project's evaluation (27.1), not the highest pack on disk (27.5)"
else fail "required Xcode was '$got'; the evaluated selection is 27.1 (the highest pack on disk says 27.5)"; fi
got="$(STUB_FAIL=1 required)"
if [[ -z "$got" ]]; then pass "an evaluation failure gives no value (the caller treats it as unknown)"
else fail "an evaluation failure gave '$got'"; fi
got="$(STUB_XCODE=garbage required)"
if [[ -z "$got" ]]; then pass "a malformed value gives no value"
else fail "a malformed value gave '$got'"; fi

# --- BUILD-03: the running-app check is repeated right before the compile -------------------------------
shared_line="$(grep -n 'dotnet run --project ../Shared.Tests' "$script" | head -1 | cut -d: -f1)"
build_line="$(grep -n '^dotnet build AiBurgerClock.Mac.csproj' "$script" | head -1 | cut -d: -f1)"
guard_lines="$(grep -n '^task_ensure_app_not_running$' "$script" | cut -d: -f1 | tr '\n' ' ')"
between=false
for line in $guard_lines; do
  if [[ -n "$shared_line" && -n "$build_line" && "$line" -gt "$shared_line" && "$line" -lt "$build_line" ]]; then between=true; fi
done
if [[ "$between" == true ]]; then pass "the running-app check runs again between Shared.Tests and the compile"
else fail "no running-app check between Shared.Tests (line ${shared_line:-?}) and the compile (line ${build_line:-?}); an app started meanwhile would be overwritten"; fi

# The check itself: an app started from the bundle path makes it stop (exit 2) instead of building over it.
app_dir="$work/bundle/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app/Contents/MacOS"
mkdir -p "$app_dir"
cp /bin/sleep "$app_dir/AI Burger Clock"
# A copied system binary is killed on launch unless it is signed again (ad hoc, temporary folder only).
/usr/bin/codesign --force --sign - "$app_dir/AI Burger Clock" >/dev/null 2>&1
"$app_dir/AI Burger Clock" 60 &
fake_pid=$!
sleep 0.3
guard_source="$(function_source task_running_pids)"$'\n'"$(function_source task_ensure_app_not_running)"
if [[ -z "$(function_source task_ensure_app_not_running)" ]]; then
  fail "task_ensure_app_not_running is not a function in the script"
else
  output="$( ( task_script_dir="$work/bundle" task_quit_running=false
      task_app="$task_script_dir/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
      task_executables=("$task_app/Contents/MacOS/AI Burger Clock" "$task_app/Contents/MacOS/AI Burger Clock")
      eval "$guard_source"
      task_ensure_app_not_running ) </dev/null 2>&1 )"
  status=$?
  if [[ "$status" -eq 2 && "$output" == *"사용 중입니다"* ]]; then pass "a running app from the bundle stops the build with exit 2 when it may not be quit"
  else fail "a running app did not stop the build (exit $status): $output"; fi
fi
kill "$fake_pid" 2>/dev/null; wait "$fake_pid" 2>/dev/null; fake_pid=""
status=0
( task_script_dir="$work/bundle" task_quit_running=false
  task_app="$task_script_dir/bin/Release/net10.0-macos27.0/osx-arm64/AI Burger Clock.app"
  task_executables=("$task_app/Contents/MacOS/AI Burger Clock" "$task_app/Contents/MacOS/AI Burger Clock")
  eval "$guard_source"
  task_ensure_app_not_running ) </dev/null >/dev/null 2>&1 || status=$?
if [[ "$status" -eq 0 ]]; then pass "with no app running the check lets the build continue"
else fail "with no app running the check stopped the build (exit $status)"; fi

echo
if [[ "$failures" -eq 0 ]]; then echo "ALL PASS"; else echo "$failures FAILED"; exit 1; fi
