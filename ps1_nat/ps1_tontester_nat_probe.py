import asyncio
import logging
import shutil
from pathlib import Path

from tontester.install import Install
from tontester.network import FullNode, Network, StartOptions


async def run_node(node, opts):
    await node.run(opts)


async def main():
    repo_root = Path(__file__).resolve().parents[2]
    art = repo_root / "contextcontrol" / "ps1_nat"
    working_dir = art / "ps1_tontester_nat_probe.network"

    shutil.rmtree(working_dir, ignore_errors=True)
    working_dir.mkdir(parents=True, exist_ok=True)

    logging.basicConfig(
        level=logging.INFO,
        format="[%(levelname)s][%(asctime)s][%(name)s] %(message)s",
        datefmt="%Y-%m-%d %H-%M-%S",
    )

    install = Install(repo_root / "build", repo_root)
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
            asyncio.create_task(attacker.run(StartOptions(
                verbosity=4,
                console_verbosity=4,
                threads=2,
                env={
                    "TON_POC_MALICIOUS_STATE_SLICE": "1",
                },
            ))),
            asyncio.create_task(victim.run(StartOptions(
                verbosity=4,
                console_verbosity=4,
                threads=2,
                env={},
            ))),
        ]

        try:
            print("NETWORK_STARTED")
            print(f"WORKDIR={working_dir}")

            await network.wait_mc_block(seqno=8)
            print("MC_BLOCK_8_REACHED")

            await asyncio.sleep(120)
            print("NAT_SLEEP_DONE")

            print("ATTACKER_LOG=" + str(attacker.log_path))
            print("VICTIM_LOG=" + str(victim.log_path))
        finally:
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)


if __name__ == "__main__":
    asyncio.run(asyncio.wait_for(main(), 10 * 60))
