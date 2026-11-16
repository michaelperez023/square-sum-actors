#!/usr/bin/env bash
# Timing runs for the README:
#   sweep    N and K with every unit size in UNITS (and every worker count in WORKERS)
#   handout  the run the assignment asked to time, "1000000 4" (HANDOUT)
#   large    k = K and each N in LARGE, with --verify
# Run the sweep first. If it finds a better unit size than the program's default, run handout and large
# with UNIT set to it, or change the default first. The handout run is small: with N = 1000000 and units of
# U starts there are N / U units, so a U above N / workers leaves some workers without a unit.
#
# For each run, the program's --stats lines give the compute phase: real time, CPU time, CPU/real, and the
# time the workers spent on their units (busy, busy/real). CPU time is the whole process's, so it also
# counts the runtime's own threads, such as background JIT compilation and thread-pool threads that spin
# while they wait for the next message; on an otherwise idle machine, busy/real is the closer measure of
# how many workers computed at once. bash's time keyword gives the whole process, which also includes
# compiling the script.
# Rows are appended to results/measurements.csv, and results/machine.txt describes the machine. The
# "expected" column compares each output with tests/expected/<N>_<k>.txt or, when there is no such file,
# with scripts/reference.py (if python3 is installed and the script handles that N and k).
#
# Usage: scripts/measure.sh [sweep] [handout] [large]     (default: all three)
#
# Settings (environment variables):
#   DOTNET    command that runs .NET (default: dotnet)
#   N, K      problem for the sweep (default: 1000000000 and 24); K is also the k of the large runs
#   UNITS     unit sizes for the sweep (default: 100 1000 10000 100000 1000000 10000000 100000000)
#   WORKERS   worker counts for the sweep, e.g. "1 2 4 8 16 24" (default: the program's default,
#             one per processor)
#   UNIT      unit size for the handout and large runs (default: the program's default)
#   REPEATS   runs of each sweep and handout setting, interleaved (default: 3)
#   HANDOUT   N and k of the assignment's run (default: "1000000 4")
#   LARGE     values of N for the large runs (default: "1000000000 10000000000 100000000000")
#   TIMEOUT   seconds before a run is stopped, where timeout(1) exists (default: 7200)
#   OUT       CSV file (default: results/measurements.csv)
#
# Ctrl-C stops the script and the run in progress. Before measuring again, check that no run was left
# behind (pgrep -fl SquareSums.fsx), since it would slow down the next runs.
set -euo pipefail

cd "$(dirname "$0")/.."

DOTNET=${DOTNET:-dotnet}
N=${N:-1000000000}
K=${K:-24}
UNITS=${UNITS:-100 1000 10000 100000 1000000 10000000 100000000}
WORKERS=${WORKERS:-}
UNIT=${UNIT:-}
REPEATS=${REPEATS:-3}
HANDOUT=${HANDOUT:-1000000 4}
LARGE=${LARGE:-1000000000 10000000000 100000000000}
TIMEOUT=${TIMEOUT:-7200}
OUT=${OUT:-results/measurements.csv}
MODES=${*:-sweep handout large}

for mode in $MODES; do
  case $mode in
    sweep | handout | large) ;;
    *)
      echo "unknown mode \"$mode\" (use sweep, handout or large)" >&2
      exit 2
      ;;
  esac
done

header="session,label,n,k,workers,unit,units,repeat,starts,last_start,output_cksum,expected,real_s,cpu_s,cpu_per_real,busy_s,busy_per_real,process_real_s,process_user_s,process_sys_s,process_cpu_per_real,load1_before,runtime_env"
mkdir -p "$(dirname "$OUT")"
if [ ! -s "$OUT" ]; then
  echo "$header" > "$OUT"
elif [ "$(head -n 1 "$OUT")" != "$header" ]; then
  echo "$OUT has different columns; move it away or set OUT to another file" >&2
  exit 2
fi

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
session=$(date -u +%Y-%m-%dT%H:%M:%SZ)

# .NET settings that change how the runtime compiles, collects garbage or schedules threads, if any are set
runtime_env=$(env | grep -E '^(DOTNET|COMPlus)_(Tiered|TC_|ReadyToRun|gc|GC|Thread|PROCESSOR_COUNT)' | sort | paste -sd ';' - || true)

load1() {
  if [ -r /proc/loadavg ]; then
    cut -d ' ' -f 1 /proc/loadavg
  else
    sysctl -n vm.loadavg | awk '{print $2}'
  fi
}

# GNU timeout needs --foreground, or Ctrl-C would not reach the run (timeout puts it in its own process group).
if command -v timeout > /dev/null && timeout --foreground 10 true 2> /dev/null; then
  has_timeout=1
else
  has_timeout=""
fi

limited() {
  if [ -n "$has_timeout" ]; then
    timeout --foreground "$TIMEOUT" "$@"
  else
    "$@"
  fi
}

machine_info() {
  local os cpu memory
  if [ -r /etc/os-release ]; then
    os="$(. /etc/os-release && echo "$PRETTY_NAME") ($(uname -sr))"
  else
    os=$(uname -sr)
  fi
  cpu=$(lscpu 2> /dev/null | sed -n 's/^Model name: *//p' | head -n 1 || true)
  [ -n "$cpu" ] || cpu=$(sysctl -n machdep.cpu.brand_string 2> /dev/null || echo unknown)
  if [ -r /proc/meminfo ]; then
    memory=$(awk '/^MemTotal:/ {printf "%.0f GiB", $2 / 1048576}' /proc/meminfo)
  else
    memory=$(sysctl -n hw.memsize | awk '{printf "%.0f GiB", $1 / 1073741824}')
  fi
  echo "session: $session"
  echo "os: $os"
  echo "cpu: $cpu"
  echo "logical processors: $(getconf _NPROCESSORS_ONLN)"
  echo "memory: $memory"
  echo ".NET SDK: $("$DOTNET" --version)"
  echo "Akka.FSharp: $(sed -n 's/^#r "nuget: Akka.FSharp, \(.*\)"$/\1/p' SquareSums.fsx)"
  echo "runtime settings: ${runtime_env:-default}"
  echo
}
machine_info | tee -a "$(dirname "$OUT")/machine.txt"

# expected_output N K: prints the name of a file with the right output for N and K, or nothing
expected_output() {
  local file="tests/expected/${1}_${2}.txt"
  if [ -f "$file" ]; then
    echo "$file"
    return
  fi
  file="$tmp/reference_${1}_${2}.txt"
  if [ ! -f "$file" ] && command -v python3 > /dev/null \
    && python3 scripts/reference.py "$1" "$2" > "$tmp/reference.txt" 2> /dev/null; then
    mv "$tmp/reference.txt" "$file"
  fi
  if [ -f "$file" ]; then echo "$file"; fi
}

# run LABEL REPEAT N K WORKERS UNIT [more arguments]: one timed run, appended to $OUT
run() {
  local label=$1 repeat=$2 n=$3 k=$4 workers=$5 unit=$6
  shift 6
  local args=("$n" "$k" --stats)
  if [ -n "$workers" ]; then args+=(--workers "$workers"); fi
  if [ -n "$unit" ]; then args+=(--unit "$unit"); fi
  args+=("$@")

  local load code=0
  load=$(load1)
  TIMEFORMAT='%R %U %S'
  { time limited "$DOTNET" fsi SquareSums.fsx "${args[@]}" > "$tmp/out" 2> "$tmp/err"; } 2> "$tmp/time" || code=$?
  case $code in
    0) ;;
    130)
      echo "interrupted" >&2
      exit 130
      ;;
    *)
      if [ "$code" -eq 124 ] && [ -n "$has_timeout" ]; then
        echo "run stopped after $TIMEOUT s: $DOTNET fsi SquareSums.fsx ${args[*]}" >&2
      else
        echo "run failed with exit code $code: $DOTNET fsi SquareSums.fsx ${args[*]}" >&2
      fi
      cat "$tmp/err" "$tmp/time" >&2
      exit 1
      ;;
  esac

  # The --stats lines look like
  #   12 workers, 10000 units of up to 100000 starts
  #   real 3.116 s, CPU 24.172 s, CPU/real 7.76
  #   workers busy 30.215 s, busy/real 6.38
  local used units size real cpu ratio busy bratio preal puser psys pratio starts last sum reference expected=""
  read -r used units size <<< "$(awk '/ units? of up to / {print $1, $3, $8}' "$tmp/err")"
  read -r real cpu ratio <<< "$(awk '/^real .* CPU .* CPU\/real / {print $2, $5, $8}' "$tmp/err")"
  read -r busy bratio <<< "$(awk '/^workers busy / {print $3, $6}' "$tmp/err")"
  read -r preal puser psys <<< "$(tail -n 1 "$tmp/time")"
  pratio=$(awk -v r="$preal" -v u="$puser" -v s="$psys" 'BEGIN {printf "%.2f", (u + s) / r}')
  starts=$(wc -l < "$tmp/out" | tr -d ' ')
  last=$(tail -n 1 "$tmp/out")
  sum=$(cksum < "$tmp/out" | awk '{print $1}')
  reference=$(expected_output "$n" "$k")
  if [ -n "$reference" ]; then
    if cmp -s "$reference" "$tmp/out"; then expected=match; else expected=MISMATCH; fi
  fi

  echo "$session,$label,$n,$k,$used,$size,$units,$repeat,$starts,$last,$sum,$expected,$real,$cpu,$ratio,$busy,$bratio,$preal,$puser,$psys,$pratio,$load,$runtime_env" >> "$OUT"
  printf '%-7s N=%s k=%s workers=%s unit=%s: real %s s, CPU/real %s, busy/real %s | process %s s | %s %s %s| load %s\n' \
    "$label" "$n" "$k" "$used" "$size" "$real" "$ratio" "$bratio" "$preal" "$starts" \
    "$([ "$starts" = 1 ] && echo start || echo starts)" "${expected:+($expected) }" "$load"
}

# One untimed run first, so that the NuGet restore and cold disk caches do not count.
limited "$DOTNET" fsi SquareSums.fsx 3 2 > /dev/null

for mode in $MODES; do
  case $mode in
    sweep)
      for repeat in $(seq "$REPEATS"); do
        for workers in ${WORKERS:-""}; do
          for unit in $UNITS; do
            run sweep "$repeat" "$N" "$K" "$workers" "$unit"
          done
        done
      done
      ;;
    handout)
      read -r handout_n handout_k <<< "$HANDOUT"
      for repeat in $(seq "$REPEATS"); do
        run handout "$repeat" "$handout_n" "$handout_k" "" "$UNIT"
      done
      ;;
    large)
      for n in $LARGE; do
        run large 1 "$n" "$K" "" "$UNIT" --verify
      done
      ;;
  esac
done

echo "results appended to $OUT"
