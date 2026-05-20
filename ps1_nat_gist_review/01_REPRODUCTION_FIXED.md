# Reproduction, Cleaned

This is a local-only tontester reproduction. Do not run it against mainnet, public testnet, Toncenter, or third-party services.

## Target

- Repository: `https://github.com/ton-blockchain/ton`
- Branch: `testnet`
- Commit: `b4added638b23fbb7c62722f4ba9190521e7ae39`
- Target binary: `validator-engine`
- Recommended host: Linux or WSL with enough RAM for several GiB of validator RSS growth.

## Expected Layout

From the TON repo root:

```text
patches/ps1_nat_full_reproduction_instrumentation.diff
scripts/ps1_tontester_usable_request_mc90.py
scripts/ps1_tontester_seed_desc_retry_mc90.py
scripts/ps1_measure_success_rss.sh
evidence/ps1_nat_success_evidence_summary.txt
evidence/ps1_nat_measured_success_rss_summary.txt
```

If you keep the original `03_PACKAGE_SHA256SUMS.txt`, update the paths in it to match this layout, or place the files at the root exactly as the SHA file expects.

## Prepare

```bash
git checkout testnet
git reset --hard b4added638b23fbb7c62722f4ba9190521e7ae39
git submodule update --init --recursive
git apply --check patches/ps1_nat_full_reproduction_instrumentation.diff
git apply patches/ps1_nat_full_reproduction_instrumentation.diff
```

Configure the build if `build/` does not already exist. The exact TON dependency set varies by host, but the important point is that `validator-engine` and the tontester/tonlibjson dependency are available.

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --target validator-engine tonlibjson -j 2
```

If `tonlibjson` is not a valid target in your checkout, build `validator-engine` and then build the default target, or inspect `cmake --build build --target help | grep -i tonlib`.

Create or activate the Python environment used by tontester:

```bash
python3 -m venv .venv-ps1nat
. .venv-ps1nat/bin/activate
python -m pip install -U pip
python -m pip install pytest pytest-asyncio crc16 bitarray requests
export PYTHONPATH="$PWD/test/tontester/src:$PWD"
```

## Functional Run

```bash
. .venv-ps1nat/bin/activate
export PYTHONPATH="$PWD/test/tontester/src:$PWD"
timeout 2200s python scripts/ps1_tontester_usable_request_mc90.py 2>&1 | tee ps1_nat_repro_run.log
```

Useful knobs if the machine is slower:

```bash
export PS1_NAT_RUN_TIMEOUT_SECONDS=2200
export PS1_NAT_WAIT_MC_SEQNO=90
export PS1_NAT_POST_MC_SLEEP_SECONDS=300
```

## RSS Measurement Run

```bash
bash scripts/ps1_measure_success_rss.sh
```

The wrapper writes fresh output under:

```text
artifacts/ps1_nat_repro/ps1_measured_success_YYYYmmdd_HHMMSS/
```

## Expected Markers

```text
PS1_NAT_TRIGGER usable_send_request
PS1_NAT_ROUTE branch_size_v2
PS1_ATTACKER shard got_getPersistentStateSizeV2
PS1_ATTACKER shard got_downloadPersistentStateSliceV2
PS1_ATTACKER shard sent_full_slice
PS1_VICTIM next_request
PS1_VICTIM part_received
```

## Expected Vulnerable Result

- Attacker returns `returning_size=1`.
- Victim repeatedly logs `PS1_VICTIM part_received ... total_size=1 last_part=false`.
- `sum_after` increases by 2 MiB per successful full slice.
- No `PS1_VICTIM eof_copy` appears during the accumulation window.
- Victim `validator-engine` RSS rises with retained slices.

This demonstrates unbounded accumulation and OOM risk. It does not need to capture a kernel OOM-kill log.
