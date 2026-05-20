import asyncio
import os

from ps1_tontester_usable_request_mc90 import env_int, main


if __name__ == "__main__":
    os.environ.setdefault("PS1_NAT_TRIGGER_USABLE_REQUEST", "0")
    run_timeout = env_int("PS1_NAT_RUN_TIMEOUT_SECONDS", 2200)
    try:
        asyncio.run(asyncio.wait_for(main(), run_timeout))
    except asyncio.TimeoutError:
        print(f"PS1_NAT_RUN_TIMEOUT hit after {run_timeout}s")
        raise
