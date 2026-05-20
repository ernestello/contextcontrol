#!/usr/bin/env bash
set -u

OUT="artifacts/ps1_persistent_state_oom/ps1_measured_rss_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$OUT"

RUNNER="artifacts/ps1_persistent_state_oom/ps1_tontester_forced_victim_slice_probe_long.py"

echo "OUT=$OUT" | tee "$OUT/summary.txt"
echo "RUNNER=$RUNNER" | tee -a "$OUT/summary.txt"

(
  timeout 600s python3 "$RUNNER"
  echo "PS1_MEASURED_RUN_RC=$?"
) 2>&1 | tee "$OUT/run.log" &

RUN_PID=$!

echo "RUN_PID=$RUN_PID" | tee -a "$OUT/summary.txt"
echo "time,pid,rss_kb,vsz_kb,cmd" > "$OUT/rss_samples.csv"

while kill -0 "$RUN_PID" 2>/dev/null; do
  for p in $(pgrep -f "validator-engine" || true); do
    ps -o rss=,vsz=,cmd= -p "$p" | awk -v t="$(date +%s)" -v pid="$p" '
      {
        rss=$1; vsz=$2;
        $1=""; $2="";
        sub(/^  */, "", $0);
        print t "," pid "," rss "," vsz "," $0;
      }
    ' >> "$OUT/rss_samples.csv"
  done
  sleep 0.25
done

wait "$RUN_PID" || true

echo "=== markers ===" | tee -a "$OUT/summary.txt"
grep -nE "PS1_ATTACKER|PS1_VICTIM part_received|MC_BLOCK_3_REACHED|PS1_.*RC" "$OUT/run.log" \
  | tee "$OUT/markers.txt" | tail -40 | tee -a "$OUT/summary.txt"

echo "=== max rss by pid ===" | tee -a "$OUT/summary.txt"
awk -F, '
  NR>1 {
    if ($3 > max[$2]) max[$2]=$3
  }
  END {
    for (pid in max) printf("pid=%s max_rss_kb=%s max_rss_mb=%.2f\n", pid, max[pid], max[pid]/1024)
  }
' "$OUT/rss_samples.csv" | sort | tee -a "$OUT/summary.txt"

echo "=== victim accumulation max ===" | tee -a "$OUT/summary.txt"
grep -oE "sum_after=[0-9]+|parts=[0-9]+" "$OUT/run.log" | awk '
  /sum_after=/ { split($0,a,"="); if (a[2] > maxsum) maxsum=a[2] }
  /parts=/ { split($0,a,"="); if (a[2] > maxparts) maxparts=a[2] }
  END { print "max_sum_after=" maxsum; print "max_parts=" maxparts }
' | tee -a "$OUT/summary.txt"

echo "DONE_OUT=$OUT"
