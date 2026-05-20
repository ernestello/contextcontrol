
## PS1-NAT — Natural persistent-state downloader aggregate memory growth

Status: Alive
Class: resource
Scope: validator/net/download-state.cpp, validator/full-node-shard.cpp, validator/manager.cpp
Symptom checked: current origin/testnet still appends peer-controlled persistent-state slices without enforcing total_size_ as an aggregate cap.
Evidence:
- Current export from /home/a1/ton-testnet-current shows DownloadState uses data.size() < requested_size as EOF and stores every slice in parts_.
- total_size_ is populated from peer state-size response but not used to abort when sum_ exceeds it.
- FullNodeShardImpl::download_persistent_state chooses a neighbour and starts DownloadState with a 3-hour manager timeout.
- Current branch has inbound heavy-query limiting, but that does not protect the victim from malicious selected peer responses.
Verdict: root cause survives latest origin/testnet; bounty upgrade now depends on proving natural trigger + malicious selected peer + RSS/OOM without forced victim path.
Reopen if:
- Natural callsite cannot be reached in local/testnet harness.
- Selected malicious peer cannot be made the persistent-state source.
- Existing code validates aggregate size outside DownloadState before memory growth.
Next: grep/export natural callsites for send_get_persistent_state_request and persistent-state download trigger.

## PS1-NAT — Natural persistent-state fallback to peer-controlled downloader

Status: Alive
Class: resource
Scope: validator/downloaders/wait-block-state.cpp, validator/downloaders/download-state.cpp, validator/manager.cpp, validator/net/download-state.cpp
Symptom checked: source-confirmed normal WaitBlockState fallback into persistent-state download.
Evidence:
- ValidatorManagerImpl::wait_block_state creates WaitBlockState with get_block_persistent_state_to_download(handle->id()).
- get_block_persistent_state_to_download returns a persistent-state description for non-masterchain shard blocks present in persistent_state_blocks_ and older than min_confirmed_masterchain_seqno_ by more than 16.
- WaitBlockState::start creates DownloadShardState when check_persistent_state_desc(), !received_state, and allow_download are true.
- DownloadShardState::checked_proof_link naturally calls send_get_persistent_state_request for unsplit/split persistent state data.
- Prior PS1 evidence showed the lower-level full-node slice downloader can grow aggregate received data beyond advertised total_size.
Verdict: natural source path is confirmed at source level; still needs malicious peer selection and real RSS/OOM reproduction without forced victim path.
Reopen if:
- persistent-state descriptions are not reachable in realistic node modes.
- malicious peer cannot be selected as persistent-state source.
- lower-level downloader is externally capped before memory growth in current origin/testnet.
Next: grep/export persistent-state description origin and full-node peer selection path.

## PS1-NAT — Descriptor origin and peer-selection grep

Status: Alive
Class: resource
Scope: validator/state-serializer.cpp, validator/manager.cpp, validator/db/statedb.cpp, validator/full-node-shard.cpp, validator/net/download-state.cpp
Symptom checked: persistent-state description origin and selected peer path for natural downloader.
Evidence:
- grep shows state-serializer.cpp creates PersistentStateDescription and calls ValidatorManager::add_persistent_state_description.
- manager stores/loads descriptions through add_persistent_state_description, got_persistent_state_descriptions, and get_block_persistent_state_to_download.
- full-node persistent-state download forwards to FullNodeShardImpl::download_persistent_state.
- FullNodeShardImpl::download_persistent_state uses choose_neighbour; neighbour set is populated from overlay random peers.
Verdict: descriptor is likely local/legitimate, while attacker influence is through selected peer response; natural reproduction must make victim miss state but keep/load a valid descriptor.
Reopen if:
- descriptions cannot be produced in local harness without forced path.
- victim cannot be made to select malicious local peer naturally.
- current DownloadState has an aggregate cap not seen in earlier evidence.
Next: export exact descriptor persistence and peer-selection functions.

## PS1-NAT — Full-node neighbour selection source-confirmed

Status: Alive
Class: resource
Scope: validator/full-node.cpp, validator/full-node-shard.cpp, validator/net/download-state.cpp
Symptom checked: source path from full-node persistent-state request to selected overlay neighbour.
Evidence:
- FullNodeImpl::download_persistent_state forwards the request to the shard actor.
- FullNodeShardImpl::download_persistent_state calls choose_neighbour and passes the chosen adnl_id into DownloadState.
- reload_neighbours obtains candidates from overlay get_overlay_random_peers; got_neighbours stores them in neighbours_.
- choose_neighbour randomly selects among eligible low-unreliability neighbours.
- DownloadState still accumulates peer-controlled slices and lacks an aggregate total_size_ cap.
Verdict: malicious selected-neighbour path is source-confirmed; remaining proof is clean local reproduction without victim-force instrumentation.
Reopen if:
- victim cannot naturally enter persistent-state fallback in local harness.
- attacker cannot be selected as source without forced victim path.
- measured run shows no meaningful memory/RSS growth.
Next: locate local tontester/full-node harness files and build attacker-only malicious responder + victim logging PoC.

## PS1-NAT — Harness discovery

Status: Alive
Class: resource
Scope: test/, artifacts/, contextcontrol/
Symptom checked: searched clean current tree for local/test harness candidates.
Evidence:
- Clean tree harness list contains no obvious PS1/tontester runner.
- Relevant upstream entries are limited to test/integration/test_basic.py, test/generate-test-node-config.py, and test/tontester/generate_tl.py.
- Previous PS1 reproduction was likely driven by custom artifacts/scripts from the old ton-ps1 workspace.
Verdict: proceed by locating old PS1 PoC scripts/artifacts and porting the runner to the current clean worktree with attacker-only malicious behavior and victim logging only.
Reopen if:
- current test tree contains a better C++/tontester harness after deeper test/ mapping.
- old PS1 scripts are missing or unusable.
Next: map old PS1 artifacts and current test C++ harness files.

## PS1-NAT — Old PoC harness/artifact map

Status: Alive
Class: resource
Scope: artifacts/ps1_persistent_state_oom, test/
Symptom checked: located prior local PoC assets and checked clean current tree for usable upstream harness.
Evidence:
- Clean current tree has no obvious PS1/tontester runner beyond generate-test-node-config.py, generate_tl.py, and validator consensus test files.
- Old ton-ps1 artifacts include malicious responder diffs, victim logging diffs, forced-victim scripts, tontester probe scripts, RSS measurement script, and prior logs.
- Prior forced reproduction should be reused only as a base; victim-force behavior must be removed for PS1-NAT.
Verdict: next step is to inspect old PoC source bundle and split it into attacker-only behavior, victim logging only, and local runner.
Reopen if:
- old PoC scripts are missing or unusable on current origin/testnet.
- current tree has a better harness after reading old runner.
- natural trigger cannot be reached after removing victim-force code.
Next: bundle old PS1 PoC source files into contextcontrol/ps1_nat and inspect.
