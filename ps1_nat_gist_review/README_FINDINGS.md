# PS1-NAT Gist Reproduction Review

Short verdict: the gist is reproducible in a prepared Linux/WSL TON build tree, but it is not quite "easy from clean checkout" as written.

I did not modify the original artifacts. This directory contains corrected helper files you can copy into a TON checkout if you want a cleaner package.

## Main Issues Found

1. The reproduction docs assume an already configured `build/` directory and `.venv-ps1nat` virtualenv.
   A fresh reproducer will need explicit submodule, CMake configure, Python venv, and dependency steps first.

2. The documented package layout and `03_PACKAGE_SHA256SUMS.txt` disagree.
   The docs refer to `patches/...` and `scripts/...`, but the SHA file hashes root-level `./ps1_...` files. That is confusing and will make checksum verification fail unless the files are laid out exactly like the SHA file expects.

3. The Python runner uses an internal `asyncio.wait_for(main(), 10 * 60)`.
   That can abort at 10 minutes even though the shell command says `timeout 2200s`. On slower machines this is a false failure.

4. The repro is Linux-specific.
   `bash`, GNU `timeout`, `/bin` venv activation, and `ps -C validator-engine` are assumed. Native Windows is not the right target; WSL/Linux is.

5. `cmake --build build --target validator-engine` may be insufficient for a totally clean build if the local tontester flow needs `tonlibjson`.
   The safer build instruction is to build both `validator-engine` and `tonlibjson`, or build the default/all target if the repo's CMake layout differs.

## Files Added Here

- `01_REPRODUCTION_FIXED.md`: clearer fresh-checkout reproduction steps.
- `scripts/ps1_tontester_usable_request_mc90.py`: same intent as the gist runner, but with configurable timeouts and clearer preflight errors.
- `scripts/ps1_tontester_seed_desc_retry_mc90.py`: wrapper variant that keeps the usable-request trigger disabled.
- `scripts/ps1_measure_success_rss.sh`: RSS wrapper with preflight checks and configurable paths/timeouts.

## Practical Verdict

If the original author ran it from the same prepared environment, the gist is probably fine enough for triage. For a third party starting clean, I would use the fixed docs/scripts here and keep the original instrumentation patch unchanged unless `git apply --check` fails in the actual TON checkout.
