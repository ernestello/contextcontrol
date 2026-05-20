#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -n "${TON_REPO_ROOT:-}" ]; then
  REPO_ROOT="$(cd "$TON_REPO_ROOT" && pwd)"
else
  REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
fi
cd "$REPO_ROOT"

VENV="${PS1_NAT_VENV:-$REPO_ROOT/.venv-ps1nat}"
if [ "${PS1_NAT_SKIP_VENV:-0}" != "1" ]; then
  if [ ! -r "$VENV/bin/activate" ]; then
    echo "Missing venv activation script: $VENV/bin/activate" >&2
    echo "Create it with: python3 -m venv .venv-ps1nat" >&2
    exit 2
  fi
  # shellcheck disable=SC1090
  . "$VENV/bin/activate"
fi

export PYTHONPATH="${PYTHONPATH:-$PWD/test/tontester/src:$PWD}"

RUNNER="${PS1_NAT_RUNNER:-scripts/ps1_tontester_usable_request_mc90.py}"
if [ ! -r "$RUNNER" ]; then
  echo "Missing runner: $RUNNER" >&2
  exit 2
fi

PYTHON_BIN="${PYTHON_BIN:-python3}"
TIMEOUT_SECONDS="${PS1_NAT_TIMEOUT_SECONDS:-2200}"

OUT="${PS1_NAT_OUT:-artifacts/ps1_nat_repro/ps1_measured_success_$(date +%Y%m%d_%H%M%S)}"
mkdir -p "$OUT"

RUN_LOG="$OUT/run.log"
RSS_LOG="$OUT/rss_samples.log"
SUMMARY="$OUT/summary.txt"

echo "OUT=$OUT" | tee "$SUMMARY"
echo "START_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a "$SUMMARY"

sampler() {
  while true; do
    ts="$(date +%s)"
    ps -C validator-engine -o pid=,rss=,args= 2>/dev/null | while read -r pid rss args; do
      [ -n "${pid:-}" ] || continue
      echo "ts=$ts pid=$pid rss_kb=$rss args=$args"
    done
    sleep 1
  done
}

sampler > "$RSS_LOG" &
SAMPLER_PID=$!

cleanup() {
  if [ -n "${SAMPLER_PID:-}" ]; then
    kill "$SAMPLER_PID" 2>/dev/null || true
    wait "$SAMPLER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

timeout "${TIMEOUT_SECONDS}s" "$PYTHON_BIN" "$RUNNER" 2>&1 | tee "$RUN_LOG"
RUN_RC=${PIPESTATUS[0]}

cleanup
trap - EXIT INT TERM

echo "PS1_NAT_MEASURED_SUCCESS_RC=$RUN_RC" | tee -a "$RUN_LOG"
echo "END_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a "$SUMMARY"
echo "PS1_NAT_MEASURED_SUCCESS_RC=$RUN_RC" | tee -a "$SUMMARY"

"$PYTHON_BIN" - "$RUN_LOG" "$RSS_LOG" "$SUMMARY" <<'PY'
from pathlib import Path
import re
import sys
from collections import defaultdict

run_log = Path(sys.argv[1])
rss_log = Path(sys.argv[2])
summary = Path(sys.argv[3])

text = run_log.read_text(errors="replace") if run_log.exists() else ""
patterns = {
    "usable_send_request": "PS1_NAT_TRIGGER usable_send_request",
    "branch_size_v2": "PS1_NAT_ROUTE branch_size_v2",
    "attacker_size": "PS1_ATTACKER shard got_getPersistentStateSizeV2",
    "attacker_slice": "PS1_ATTACKER shard got_downloadPersistentStateSliceV2",
    "attacker_sent_full_slice": "PS1_ATTACKER shard sent_full_slice",
    "victim_next_request": "PS1_VICTIM next_request",
    "victim_part_received": "PS1_VICTIM part_received",
    "victim_eof_copy": "PS1_VICTIM eof_copy",
    "traceback": "Traceback",
    "fatal": "Fatal",
}

max_sum = 0
max_parts = 0
max_next_offset = 0
max_attacker_sent_offset = 0
max_attacker_sent_size = 0

for line in text.splitlines():
    if "PS1_VICTIM part_received" in line:
        m = re.search(r"sum_after=(\d+)", line)
        if m:
            max_sum = max(max_sum, int(m.group(1)))
        m = re.search(r"parts=(\d+)", line)
        if m:
            max_parts = max(max_parts, int(m.group(1)))
    if "PS1_VICTIM next_request" in line:
        m = re.search(r"offset=(\d+)", line)
        if m:
            max_next_offset = max(max_next_offset, int(m.group(1)))
    if "PS1_ATTACKER shard sent_full_slice" in line:
        m = re.search(r"offset=(\d+) size=(\d+)", line)
        if m:
            max_attacker_sent_offset = max(max_attacker_sent_offset, int(m.group(1)))
            max_attacker_sent_size = max(max_attacker_sent_size, int(m.group(2)))

max_rss_by_pid = defaultdict(int)
sum_by_ts = defaultdict(int)
if rss_log.exists():
    for line in rss_log.read_text(errors="replace").splitlines():
        m = re.search(r"ts=(\d+) pid=(\d+) rss_kb=(\d+)", line)
        if not m:
            continue
        ts, pid, rss = int(m.group(1)), m.group(2), int(m.group(3))
        max_rss_by_pid[pid] = max(max_rss_by_pid[pid], rss)
        sum_by_ts[ts] += rss

max_single_rss_kb = max(max_rss_by_pid.values(), default=0)
max_total_validator_rss_kb = max(sum_by_ts.values(), default=0)

with summary.open("a", encoding="utf-8") as f:
    f.write("\n=== COUNTS ===\n")
    for name, needle in patterns.items():
        f.write(f"{name}={text.count(needle)}\n")

    f.write("\n=== MAX OBSERVED LOGICAL ACCUMULATION ===\n")
    f.write(f"max_victim_sum_after={max_sum}\n")
    f.write(f"max_victim_sum_after_mib={max_sum / 1024 / 1024:.2f}\n")
    f.write(f"max_victim_parts={max_parts}\n")
    f.write(f"max_victim_next_request_offset={max_next_offset}\n")
    f.write(f"max_attacker_sent_offset={max_attacker_sent_offset}\n")
    f.write(f"max_attacker_sent_size={max_attacker_sent_size}\n")

    f.write("\n=== RSS OBSERVED ===\n")
    f.write(f"max_single_validator_engine_rss_kb={max_single_rss_kb}\n")
    f.write(f"max_single_validator_engine_rss_mib={max_single_rss_kb / 1024:.2f}\n")
    f.write(f"max_total_validator_engine_rss_kb={max_total_validator_rss_kb}\n")
    f.write(f"max_total_validator_engine_rss_mib={max_total_validator_rss_kb / 1024:.2f}\n")
    f.write("max_rss_by_pid_kb=" + repr(dict(max_rss_by_pid)) + "\n")

    def first(title, needle, limit=6):
        f.write(f"\n=== {title} FIRST ===\n")
        n = 0
        for line in text.splitlines():
            if needle in line:
                f.write(line + "\n")
                n += 1
                if n >= limit:
                    break

    def last(title, needle, limit=8):
        f.write(f"\n=== {title} LAST ===\n")
        arr = [line for line in text.splitlines() if needle in line]
        for line in arr[-limit:]:
            f.write(line + "\n")

    first("TRIGGER", "PS1_NAT_TRIGGER usable_send_request", 4)
    first("ROUTE", "PS1_NAT_ROUTE branch_size_v2", 4)
    first("ATTACKER SIZE", "PS1_ATTACKER shard got_getPersistentStateSizeV2", 4)
    first("ATTACKER SLICE", "PS1_ATTACKER shard got_downloadPersistentStateSliceV2", 6)
    first("ATTACKER SENT", "PS1_ATTACKER shard sent_full_slice", 6)
    first("VICTIM PART", "PS1_VICTIM part_received", 6)
    last("VICTIM PART", "PS1_VICTIM part_received", 10)
    last("VICTIM NEXT_REQUEST", "PS1_VICTIM next_request", 10)
PY

cat "$SUMMARY"
echo
echo "SUMMARY_PATH=$SUMMARY"
