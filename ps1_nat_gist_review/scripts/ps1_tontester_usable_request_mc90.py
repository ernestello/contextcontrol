import asyncio
import logging
import os
import shutil
import sys
from pathlib import Path


def env_int(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None or raw == "":
        return default
    try:
        value = int(raw)
    except ValueError as exc:
        raise RuntimeError(f"{name} must be an integer, got {raw!r}") from exc
    if value <= 0:
        raise RuntimeError(f"{name} must be positive, got {value}")
    return value


def env_bool(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None or raw == "":
        return default
    return raw.lower() not in {"0", "false", "no", "off"}


def find_repo_root(start: Path) -> Path:
    cur = start.resolve()
    if cur.is_file():
        cur = cur.parent
    for p in [cur, *cur.parents]:
        if (p / ".git").exists() and (p / "validator").exists() and (p / "test").exists():
            return p
    raise RuntimeError(f"Could not find TON repo root from {start}")


repo_root = find_repo_root(Path(__file__))

for p in [
    repo_root / "test" / "tontester" / "src",
    repo_root / "test" / "tontester",
    repo_root / "test",
]:
    if p.exists():
        sys.path.insert(0, str(p))

from tontester.install import Install
from tontester.network import FullNode, Network, StartOptions


def build_dir() -> Path:
    raw = os.getenv("TON_BUILD_DIR")
    path = Path(raw).expanduser() if raw else repo_root / "build"
    path = path.resolve()
    if not path.exists():
        raise RuntimeError(
            f"Build directory not found: {path}. Set TON_BUILD_DIR or configure/build TON first."
        )
    return path


def work_dir() -> Path:
    raw = os.getenv("PS1_NAT_WORKDIR")
    if raw:
        return Path(raw).expanduser().resolve()
    return repo_root / "artifacts" / "ps1_nat_repro" / "ps1_tontester_nat_probe.network"


def victim_env() -> dict[str, str]:
    env = {
        "TON_POC_PS1_SEED_DESC": "1",
        "TON_POC_PS1_DELAYED_RETRY": "1",
    }
    if env_bool("PS1_NAT_TRIGGER_USABLE_REQUEST", True):
        env["TON_POC_PS1_TRIGGER_USABLE_REQUEST"] = "1"
    return env


async def main() -> None:
    mc_seqno = env_int("PS1_NAT_WAIT_MC_SEQNO", 90)
    post_mc_sleep = env_int("PS1_NAT_POST_MC_SLEEP_SECONDS", 300)
    working_dir = work_dir()

    if not env_bool("PS1_NAT_KEEP_WORKDIR", False):
        shutil.rmtree(working_dir, ignore_errors=True)
    working_dir.mkdir(parents=True, exist_ok=True)

    logging.basicConfig(
        level=logging.INFO,
        format="[%(levelname)s][%(asctime)s][%(name)s] %(message)s",
        datefmt="%Y-%m-%d %H-%M-%S",
    )

    print("PYTHONPATH_HEAD=" + repr(sys.path[:5]))
    print("REPO_ROOT=" + str(repo_root))
    print("BUILD_DIR=" + str(build_dir()))
    print("WORKDIR=" + str(working_dir))
    print(f"WAIT_MC_SEQNO={mc_seqno}")
    print(f"POST_MC_SLEEP_SECONDS={post_mc_sleep}")

    install = Install(build_dir(), repo_root)
    install.tonlibjson.client_set_verbosity_level(3)

    async with Network(install, working_dir) as network:
        dht = network.create_dht_node()
        network.config.shard_validators = 2

        attacker: FullNode = network.create_full_node()
        victim: FullNode = network.create_full_node()

        attacker.make_initial_validator()
        victim.make_initial_validator()

        attacker.announce_to(dht)
        victim.announce_to(dht)

        tasks = [
            asyncio.create_task(dht.run(StartOptions(verbosity=4, console_verbosity=4, threads=1))),
            asyncio.create_task(
                attacker.run(
                    StartOptions(
                        verbosity=4,
                        console_verbosity=4,
                        threads=2,
                        env={"TON_POC_MALICIOUS_STATE_SLICE": "1"},
                    )
                )
            ),
            asyncio.create_task(
                victim.run(
                    StartOptions(
                        verbosity=4,
                        console_verbosity=4,
                        threads=2,
                        env=victim_env(),
                    )
                )
            ),
        ]

        try:
            print("NETWORK_STARTED")
            await network.wait_mc_block(seqno=mc_seqno)
            print(f"MC_BLOCK_{mc_seqno}_REACHED")

            await asyncio.sleep(post_mc_sleep)
            print("NAT_SLEEP_DONE")

            print("ATTACKER_LOG=" + str(attacker.log_path))
            print("VICTIM_LOG=" + str(victim.log_path))
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)


if __name__ == "__main__":
    run_timeout = env_int("PS1_NAT_RUN_TIMEOUT_SECONDS", 2200)
    try:
        asyncio.run(asyncio.wait_for(main(), run_timeout))
    except asyncio.TimeoutError:
        print(f"PS1_NAT_RUN_TIMEOUT hit after {run_timeout}s")
        raise
