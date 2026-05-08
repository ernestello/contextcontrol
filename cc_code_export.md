# Code export

Generated from project files.

Project root: D:\projects\vulkanas

## Instructions for Context Control

This export is source context only. Use the standing Context Control instructions for workflow rules and patch format.

Minimal rules for this turn:
- Spend reasoning on the requested code fix, not tool mechanics.
- Prefer a single patch.txt containing raw BEGIN CC-REPLACE blocks. Inline only if tiny.
- Keep the existing architecture and modular ownership boundaries.
- Use MODE: insert_include for include-only edits.
- FIND: reports are discovery only; they never include source bodies. Request exact files/functions after discovery.
- If this export contains Hash hints, copy the matching HASH: value into function/replace_region patch headers. If no hash hint is present, omit HASH:.
- If critical context is missing, ask only for exact paths, FUNCTION exports, or FIND discovery queries, one per line, ending with END.

Default CMake build, when applicable: cmake --build build --config Release -j


## src\world\WorldLODTransitions.cpp

Description: No CC-DESC found.

````cpp
// WorldLODTransitions.cpp — LOD level changes, mesh release/reload, LOD swaps
// Extracted from World.cpp to reduce god-file size without changing behavior.

#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/culling/GPUCullingSystem.h"
#include "physics/PhysicsWorld.h"
#include <iostream>
#include <algorithm>
#include <chrono>
#include <unordered_map>
#include <string>
#include <limits>

void World::setTerrainTypeForLOD(int lodLevel, TerrainType type) {
    if (lodLevel < 0 || lodLevel >= MAX_LOD_LEVELS) return;
    TerrainType oldType = m_terrainTypePerLOD[lodLevel];
    if (oldType == type) return;
    
    m_terrainTypePerLOD[lodLevel] = type;
    std::cout << "[World] LOD " << lodLevel << " terrain type changed to: " 
              << (type == TerrainType::DCCM ? "DCCM" : "Voxel") << std::endl;
    
    // ---------------------------------------------------------------
    // FULL PIPELINE DRAIN before releasing/reloading meshes.
    // Without this, in-flight uploads and LOD batches from the OLD
    // terrain type can arrive AFTER releaseMeshesForLOD clears the
    // handles, installing stale mesh data and leaking GPU culling
    // slots.  This matches what applyLODChangesIncrementally does.
    // ---------------------------------------------------------------
    m_lifecycleManager.pauseAndDrain();
    auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
        [](const glm::ivec3&) { return true; });
    auto purgedDestroys = m_lifecycleManager.purgeDestructionQueue(
        [](const glm::ivec3&) { return true; });
    if (!purgedCreates.empty()) {
        std::lock_guard lock(m_pendingChunksMutex);
        for (const auto& coord : purgedCreates) {
            m_pendingChunks.erase(coord);
        }
    }
    if (!purgedCreates.empty()) {
        m_chunkManager->cancelPendingCreates(purgedCreates);
    }
    if (!purgedDestroys.empty()) {
        m_chunkManager->cancelPendingDestroys(purgedDestroys);
    }
    m_uploadSystem.clearQueue();
    
    // Cancel all in-flight LOD transition batches and clean up their
    // PendingMeshHandle GPU resources (buffers + culling slots).
    auto cancelledBatchIds = m_chunkManager->cancelAllBatches();
    if (!cancelledBatchIds.empty()) {
        std::string reason = "DataLodSwitchCancelledBatches count=" +
            std::to_string(cancelledBatchIds.size());
        noteChunkVisualError(
            nullptr,
            lodLevel,
            "LODTransition",
            reason.c_str(),
            0,
            static_cast<uint32_t>(cancelledBatchIds.size()),
            0);
        cleanupStalePendingMeshHandles();
    }
    // Drop stale remesh queue entries carrying cancelled batch IDs.
    m_lodSystem.clearAllPending();
    m_pendingLODRemeshes.clear();
    
    // Purge pending LOD remeshes for this LOD level so stale remesh
    // requests don't re-queue jobs for the old terrain type.
    m_pendingLODRemeshes.erase(
        std::remove_if(m_pendingLODRemeshes.begin(), m_pendingLODRemeshes.end(),
            [lodLevel](const ChunkManager::ChunkRemeshRequest& req) {
                return req.newLOD == lodLevel;
            }),
        m_pendingLODRemeshes.end());
    
    // Regenerate chunks at this LOD level - release old meshes and reload with new terrain type
    releaseMeshesForLOD(lodLevel);
    reloadMeshesForLOD(lodLevel);
    
    // Resume background thread
    m_lifecycleManager.resume();
}

void World::setTerrainTypesForStartup(const std::array<TerrainType, MAX_LOD_LEVELS>& types) {
    m_terrainTypePerLOD = types;
    std::cout << "[World] Startup terrain types:";
    for (int lod = 0; lod < MAX_LOD_LEVELS; ++lod) {
        std::cout << " LOD" << lod << "="
                  << (m_terrainTypePerLOD[lod] == TerrainType::DCCM ? "DCCM" : "Voxel");
    }
    std::cout << std::endl;
}

void World::setDataLODForBand(int band, int dataLOD) {
    if (band < 0 || band >= MAX_LOD_LEVELS) return;
    if (dataLOD < 0 || dataLOD >= MAX_LOD_LEVELS) return;
    int oldDataLOD = m_dataLODPerBand[band];
    if (oldDataLOD == dataLOD) return;

    m_dataLODPerBand[band] = dataLOD;
    std::cout << "[World] Band " << band << " data LOD changed: "
              << oldDataLOD << " -> " << dataLOD << std::endl;

    // Only Voxel bands use data LOD override; DCCM always uses LOD 0
    if (m_terrainTypePerLOD[band] == TerrainType::DCCM) return;

    // Same drain/reload cycle as setTerrainTypeForLOD
    m_lifecycleManager.pauseAndDrain();
    auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
        [](const glm::ivec3&) { return true; });
    auto purgedDestroys = m_lifecycleManager.purgeDestructionQueue(
        [](const glm::ivec3&) { return true; });
    if (!purgedCreates.empty()) {
        std::lock_guard lock(m_pendingChunksMutex);
        for (const auto& coord : purgedCreates) {
            m_pendingChunks.erase(coord);
        }
    }
    if (!purgedCreates.empty()) {
        m_chunkManager->cancelPendingCreates(purgedCreates);
    }
    if (!purgedDestroys.empty()) {
        m_chunkManager->cancelPendingDestroys(purgedDestroys);
    }
    m_uploadSystem.clearQueue();

    auto cancelledBatchIds = m_chunkManager->cancelAllBatches();
    if (!cancelledBatchIds.empty()) {
        std::string reason = "TerrainTypeSwitchCancelledBatches count=" +
            std::to_string(cancelledBatchIds.size());
        noteChunkVisualError(
            nullptr,
            band,
            "LODTransition",
            reason.c_str(),
            0,
            static_cast<uint32_t>(cancelledBatchIds.size()),
            0);
        cleanupStalePendingMeshHandles();
    }
    m_lodSystem.clearAllPending();
    m_pendingLODRemeshes.clear();

    releaseMeshesForLOD(band);
    reloadMeshesForLOD(band);

    m_lifecycleManager.resume();
}

void World::applyLODChangesIncrementally(int newRenderDistance) {
    // Pause the background thread so it doesn't interfere with scanning
    m_lifecycleManager.pauseAndDrain();
    
    glm::ivec3 center = m_chunkManager->getCenterChunk();
    
    // Compute the full effective distance including extension rings.
    // Extension chunks (at last LOD) must not be destroyed or purged.
    int extensionRings = m_chunkManager->getExtensionRings();
    int effectiveTarget = newRenderDistance + extensionRings;
    
    // SELECTIVE purge: only purge creates that are NOW out of range.
    // In-range creates must stay in the lifecycle queue so the background
    // thread can finish creating them after we resume.  Purging all creates
    // (as was done before) kills mid-pipeline chunks whose uploads then get
    // cleared, leaving grey/empty holes that are never recovered.
    auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
        [&](const glm::ivec3& coord) {
            int ring = m_chunkManager->calculateRingNumber(coord, center);
            return ring >= effectiveTarget;
        });
    auto purgedDestroys = m_lifecycleManager.purgeDestructionQueue(
        [](const glm::ivec3&) { return true; });
    if (!purgedCreates.empty()) {
        std::lock_guard lock(m_pendingChunksMutex);
        for (const auto& coord : purgedCreates) {
            m_pendingChunks.erase(coord);
        }
    }
    if (!purgedCreates.empty()) {
        m_chunkManager->cancelPendingCreates(purgedCreates);
    }
    if (!purgedDestroys.empty()) {
        m_chunkManager->cancelPendingDestroys(purgedDestroys);
    }
    
    // DO NOT clear the upload queue unconditionally.  Chunks that are in-range
    // and mid-pipeline (mesh generated, waiting for upload) would lose their
    // mesh data permanently — the entity exists so processRingConstruct won't
    // re-emit a create, but the mesh is gone → grey/empty chunk forever.
    // The version-checking system (stale version detection in LoadPrecomputedMeshJob
    // and UploadJob) already handles stale uploads for destroyed/remeshed chunks.
    
    // Cancel all in-flight LOD transition batches and clean up their
    // PendingMeshHandle GPU resources.  Without this, old batches from
    // a previous slider change linger because version-stale jobs never
    // signal signalBatchChunkReady.  processLODSwaps only runs when ALL
    // entities in a batch are ready, so even one stale job permanently
    // blocks the swap — chunks appear frozen until the next change.
    auto cancelledBatchIds = m_chunkManager->cancelAllBatches();
    if (!cancelledBatchIds.empty()) {
        std::string reason = "RenderDistanceChangeCancelledBatches count=" +
            std::to_string(cancelledBatchIds.size());
        noteChunkVisualError(
            nullptr,
            -1,
            "LODTransition",
            reason.c_str(),
            0,
            static_cast<uint32_t>(cancelledBatchIds.size()),
            0);
        cleanupStalePendingMeshHandles();
    }
    
    m_lodSystem.clearAllPending();
    m_pendingLODRemeshes.clear();
    
    // Phase 1: Scan existing chunks for:
    //   - out-of-range chunks (destroy)
    //   - LOD mismatches (remesh)
    //   - seam/casing topology mismatches at unchanged LOD (remesh)
    // Also batch-update desired LODs for all in-range chunks so chunks that are
    // currently Loading/Meshing converge to the new target without waiting for
    // a later center-change scan.
    std::vector<glm::ivec3> chunksToDestroy;     // Now out of range — need full destroy
    struct RemeshCandidate {
        entt::entity entity{entt::null};
        glm::ivec3 coord{0, 0, 0};
        int newLOD{0};
        int ring{0};
    };
    std::vector<RemeshCandidate> chunksToRemesh;
    std::vector<std::pair<glm::ivec3, int>> desiredLODUpdates;

    auto computeDCCMCasingMask = [&](const glm::ivec3& pos,
                                     const glm::ivec3& centerPos) -> uint8_t {
        static const glm::ivec3 nOff[4] = {{-1,0,0},{1,0,0},{0,0,-1},{0,0,1}};
        uint8_t mask = 0;
        for (int e = 0; e < 4; ++e) {
            glm::ivec3 nb = pos + nOff[e];
            int nbRing = m_chunkManager->calculateRingNumber(nb, centerPos);
            int nbLOD  = m_chunkManager->calculateLODFromRing(nbRing);
            if (getTerrainTypeForChunk(nb, nbLOD) != TerrainType::DCCM) {
                mask |= (1u << e);
            }
        }
        return mask;
    };
    
    {
        std::shared_lock regLock(m_registryMutex);
        auto view = m_registry.view<Chunk, ChunkCoord, ChunkState>();
        desiredLODUpdates.reserve(view.size_hint());
        chunksToDestroy.reserve(view.size_hint() / 4);
        chunksToRemesh.reserve(view.size_hint() / 8);
        
        for (auto entity : view) {
            auto& chunk = view.get<Chunk>(entity);
            auto& coordComp = view.get<ChunkCoord>(entity);
            auto& stateComp = view.get<ChunkState>(entity);
            glm::ivec3 coord = coordComp.toVec3();
            
            int ring = m_chunkManager->calculateRingNumber(coord, center);
            
            if (ring >= effectiveTarget) {
                chunksToDestroy.push_back(coord);
                continue;
            }
            
            int newLOD = m_chunkManager->calculateLODFromRing(ring);
            desiredLODUpdates.push_back({coord, newLOD});

            // For non-ready chunks, desired LOD update is enough; avoid enqueueing
            // extra remesh work while initial generation is still in progress.
            if (stateComp.state != ChunkState::State::Ready) {
                continue;
            }

            if (newLOD != chunk.lodLevel) {
                chunksToRemesh.push_back({entity, coord, newLOD, ring});
                continue;
            }

            // LOD unchanged, but boundary topology may still need a remesh:
            // voxel seam masks and DCCM casing masks change when thresholds move.
            TerrainType thisType = getTerrainTypeForChunk(coord, chunk.lodLevel);
            if (thisType == TerrainType::DCCM) {
                uint8_t newMask = computeDCCMCasingMask(coord, center);
                if (newMask != chunk.casingSeamMask) {
                    chunksToRemesh.push_back({entity, coord, newLOD, ring});
                }
            } else {
                uint8_t newMask = (newLOD > 0) ? m_chunkManager->getSeamEdgeMask(coord, center) : 0;
                if (newMask != chunk.voxelSeamMask) {
                    chunksToRemesh.push_back({entity, coord, newLOD, ring});
                }
            }
        }
    }

    if (!desiredLODUpdates.empty()) {
        m_lodSystem.setDesiredLODs(desiredLODUpdates);
    }
    
    // Phase 2: Destroy only out-of-range chunks
    if (!chunksToDestroy.empty()) {
        // Invalidate version states so in-flight jobs are discarded
        {
            std::scoped_lock versionLock(m_chunkVersionMutex);
            for (const auto& coord : chunksToDestroy) {
                entt::entity entity = findChunk(coord);
                if (entity != entt::null) {
                    auto it = m_chunkVersionStates.find(entity);
                    if (it != m_chunkVersionStates.end() && it->second) {
                        it->second->version.fetch_add(1, std::memory_order_acq_rel);
                        it->second->inFlight.store(false, std::memory_order_release);
                        it->second->pending.store(false, std::memory_order_release);
                    }
                }
            }
        }
        
        tryDestroyChunksBatch(chunksToDestroy);
    }
    
    // Phase 3: In-place remesh for LOD-changed chunks via the existing remesh pipeline
    // Don't update chunk.lodLevel here — it will be updated when the mesh is actually
    // swapped in processLODSwaps. Pass targetLOD through batch info.
    if (!chunksToRemesh.empty()) {
        // Prioritize near rings first for visual stability while transitioning.
        std::sort(chunksToRemesh.begin(), chunksToRemesh.end(),
                  [](const RemeshCandidate& a, const RemeshCandidate& b) {
                      return a.ring < b.ring;
                  });

        // Group by target LOD but keep near-first order within each group.
        std::unordered_map<int, std::vector<RemeshCandidate>> lodGroups;
        lodGroups.reserve(4);
        for (const auto& c : chunksToRemesh) {
            lodGroups[c.newLOD].push_back(c);
        }
        
        for (auto& [targetLOD, candidates] : lodGroups) {
            // Split groups into moderate batches so swaps complete faster and
            // reduce visible "stuck boundary" windows during rapid slider edits.
            static constexpr size_t MAX_LOD_BATCH_SIZE = 128;
            
            for (size_t batchStart = 0; batchStart < candidates.size(); batchStart += MAX_LOD_BATCH_SIZE) {
                size_t batchEnd = std::min(batchStart + MAX_LOD_BATCH_SIZE, candidates.size());
                std::vector<entt::entity> subBatchEntities;
                subBatchEntities.reserve(batchEnd - batchStart);
                for (size_t i = batchStart; i < batchEnd; ++i) {
                    subBatchEntities.push_back(candidates[i].entity);
                }

                uint32_t batchId = m_chunkManager->createLODTransitionBatch(targetLOD, subBatchEntities);
            
                for (size_t i = batchStart; i < batchEnd; ++i) {
                    const auto& c = candidates[i];
                    auto vs = ensureChunkVersionState(this, c.entity);
                    if (vs) {
                        vs->version.fetch_add(1, std::memory_order_acq_rel);
                        // Reset inFlight so updateMeshingSystem can start a new pipeline.
                        // Without this, stale jobs (which intentionally don't clear inFlight)
                        // leave the flag set, causing updateMeshingSystem to requeue forever.
                        vs->inFlight.store(false, std::memory_order_release);
                        vs->pending.store(false, std::memory_order_release);
                    }
                    
                    m_lodSystem.setDesiredLOD(c.coord, targetLOD);
                    // Signal any superseded batch (unlikely after cancelAllBatches
                    // above, but safe against future race conditions).
                    uint32_t oldBatchId = m_lodSystem.enqueueLODRemesh(c.entity, true, batchId, targetLOD);
                    if (oldBatchId != 0 && m_chunkManager->isBatchActive(oldBatchId)) {
                        m_chunkManager->signalBatchChunkReady(oldBatchId);
                    }
                }
            } // end sub-batch loop
        }     // end target LOD loop
    }
    
    // Phase 4: Update render distance and ring construction state.
    //
    // With selective purge (only out-of-range creates purged), the emission
    // watermark (emittedUpToRing) is accurate for in-range rings:
    //   - INCREASE: no in-range creates purged, emittedUpToRing stays valid,
    //     processRingConstruct emits only the new rings above the old distance.
    //   - DECREASE: out-of-range creates purged, clampRingProgress lowers
    //     emittedUpToRing to newRenderDistance so we don't think we've emitted
    //     rings that no longer exist.
    //
    // setEffectiveRenderDistance: overrides the budget-adaptive cap so the
    // user's slider value takes effect immediately.
    //
    // resetAdaptWarmup: prevents adaptRenderDistance from immediately
    // shrinking effectiveDist back down on the next frame.
    m_chunkManager->setRenderDistanceRings(newRenderDistance);
    // effectiveTarget (base + extension) was already computed at the top
    m_chunkManager->setEffectiveRenderDistance(effectiveTarget);
    m_chunkManager->clampRingProgress(effectiveTarget);
    m_chunkManager->resetAdaptWarmup();
    
    // Resume background thread — in-range creates left in the lifecycle queue
    // will continue processing now.
    m_lifecycleManager.resume();
    
    // Debug: count existing chunks and report progress
    int existingCount = 0;
    int expectedCount = 0;
    {
        std::shared_lock setLock(m_chunkSetMutex);
        existingCount = static_cast<int>(m_existingChunkSet.size());
    }
    for (int r = 1; r <= effectiveTarget; ++r) {
        int chebyshevR = r - 1;
        int chunksInRing = (r == 1) ? 1 : 8 * chebyshevR;
        expectedCount += chunksInRing;
    }
    std::cout << "[World] Incremental update: destroyed " << chunksToDestroy.size()
              << " out-of-range, remeshing " << chunksToRemesh.size()
              << " affected, purged " << purgedCreates.size() << " OOR creates"
              << ", existing=" << existingCount
              << "/" << expectedCount << " expected at " << effectiveTarget
              << " rings (base=" << newRenderDistance << " + ext=" << extensionRings << ")"
              << std::endl;
}

void World::releaseMeshesForLOD(int lodLevel) {
    if (lodLevel < 0 || lodLevel > 4) return;
    
    std::vector<entt::entity> chunksToRelease;
    size_t totalVertices = 0;
    size_t totalIndices = 0;
    
    // Find all chunks at this LOD level
    {
        std::shared_lock regLock(m_registryMutex);
        auto view = m_registry.view<Chunk, MeshHandle>();
        for (auto entity : view) {
            auto& chunk = m_registry.get<Chunk>(entity);
            if (chunk.lodLevel == lodLevel) {
                auto& meshHandle = m_registry.get<MeshHandle>(entity);
                if (meshHandle.vb.isValid() || meshHandle.ib.isValid()) {
                    chunksToRelease.push_back(entity);
                    totalVertices += meshHandle.vb.size;
                    totalIndices += meshHandle.ib.size;
                }
            }
        }
    }
    
    if (chunksToRelease.empty()) {
        return;
    }
    
    // Release mesh handles — collect slices under single lock, batch-free outside lock
    std::vector<BufferSlice> vbSlices;
    std::vector<BufferSlice> ibSlices;
    std::vector<uint32_t> cullingSlots;
    vbSlices.reserve(chunksToRelease.size());
    ibSlices.reserve(chunksToRelease.size());
    cullingSlots.reserve(chunksToRelease.size());
    {
        std::unique_lock regLock(m_registryMutex);
        for (entt::entity entity : chunksToRelease) {
            if (!m_registry.valid(entity)) continue;

            if (m_registry.all_of<MeshHandle>(entity)) {
                auto& meshHandle = m_registry.get<MeshHandle>(entity);
                meshStatsSub(meshHandle);
                if (meshHandle.vb.isValid()) vbSlices.push_back(meshHandle.vb);
                if (meshHandle.ib.isValid()) ibSlices.push_back(meshHandle.ib);
                if (meshHandle.gpuCullingSlot != UINT32_MAX)
                    cullingSlots.push_back(meshHandle.gpuCullingSlot);
                meshHandle = MeshHandle{};
            }

            if (m_registry.all_of<Chunk>(entity)) {
                m_registry.get<Chunk>(entity).isEmpty = true;
            }
        }
    }
    // Batch free outside registry lock
    if (m_vbAllocator && !vbSlices.empty())
        m_vbAllocator->freeBatch(vbSlices.data(), vbSlices.size());
    if (m_ibAllocator && !ibSlices.empty())
        m_ibAllocator->freeBatch(ibSlices.data(), ibSlices.size());
    if (m_gpuCulling && !cullingSlots.empty())
        m_gpuCulling->freeSlots(cullingSlots.data(), cullingSlots.size());
    
    std::cout << "[World] Released " << chunksToRelease.size() << " meshes for LOD " << lodLevel
              << " (freed ~" << (totalVertices / 1024 / 1024) << "MB VB, ~" 
              << (totalIndices / 1024 / 1024) << "MB IB)" << std::endl;
}

void World::reloadMeshesForLOD(int lodLevel) {
    if (lodLevel < 0 || lodLevel > 4) return;
    
    std::vector<std::pair<entt::entity, glm::ivec3>> chunksToReload;
    
    // Find all chunks at this LOD level that need mesh data
    {
        std::shared_lock regLock(m_registryMutex);
        auto view = m_registry.view<Chunk, ChunkCoord>();
        for (auto entity : view) {
            auto& chunk = m_registry.get<Chunk>(entity);
            if (chunk.lodLevel == lodLevel && chunk.isEmpty) {
                auto& coord = m_registry.get<ChunkCoord>(entity);
                chunksToReload.push_back({entity, coord.toVec3()});
            }
        }
    }
    
    if (chunksToReload.empty()) {
        std::cout << "[World] No empty chunks to reload for LOD " << lodLevel << std::endl;
        return;
    }
    
    std::cout << "[World] Reloading " << chunksToReload.size() << " meshes for LOD " << lodLevel << std::endl;
    
    // Queue each chunk for mesh loading via the job system
    for (const auto& [entity, coord] : chunksToReload) {
        auto versionState = ensureChunkVersionState(this, entity);
        if (!versionState) continue;
        
        // Increment version to invalidate any in-flight jobs
        versionState->version.fetch_add(1, std::memory_order_acq_rel);
        versionState->inFlight.store(true, std::memory_order_release);
        versionState->pending.store(false, std::memory_order_release);
        
        // Mark chunk for reloading
        {
            std::unique_lock regLock(m_registryMutex);
            if (m_registry.valid(entity) && m_registry.all_of<Chunk>(entity)) {
                auto& chunk = m_registry.get<Chunk>(entity);
                chunk.isEmpty = false;  // Will be set correctly by LoadPrecomputedMeshJob
            }
        }
        
        setChunkState(entity, ChunkState::State::Loading);
        
        // Get AABB
        AABB aabb;
        {
            std::shared_lock regLock(m_registryMutex);
            if (m_registry.all_of<AABB>(entity)) {
                aabb = m_registry.get<AABB>(entity);
            }
        }
        
        // Create payload for job pipeline
        auto* payload = new ChunkPipelinePayload();
        payload->world = this;
        payload->entity = entity;
        payload->coord = ChunkCoord{coord.x, coord.y, coord.z};
        payload->bounds = aabb;
        payload->versionState = versionState;
        payload->version = versionState->version.load(std::memory_order_acquire);
        payload->distanceFromPlayer = 0;  // High priority for reload
        payload->lodLevel = lodLevel;
        payload->centerAtEnqueue = m_chunkManager ? m_chunkManager->getCenterChunk() : glm::ivec3(0, 0, 0);
        
        // Schedule jobs — use edit mesher for chunks with overlay edits
        TerrainType chunkTerrainType = getTerrainTypeForChunk(coord, lodLevel);
        const bool useEditMesher = chunkNeedsRuntimeVoxel(coord);
        payload->fromTerrainEdit = useEditMesher && chunkTerrainType != TerrainType::DCCM;
        auto loadJobFn = (useEditMesher && chunkTerrainType != TerrainType::DCCM)
            ? LoadEditMeshJob
            : LoadPrecomputedMeshJob;
        JobHandle load = m_jobSystem.makeWithPriority(loadJobFn, payload, 0, 1000000);
        JobHandle upload = m_jobSystem.makeWithPriority(UploadChunkJob, payload, 0, 1000000);
        JobHandle finalize = m_jobSystem.makeWithPriority(FinalizeChunkJob, payload, 0, 1000000);
        
        m_jobSystem.addDependency(upload, load);
        m_jobSystem.addDependency(finalize, upload);
        
        payload->jobHandles = {load, upload, finalize};
        
        m_jobSystem.schedule(load);
        m_jobSystem.schedule(upload);
        m_jobSystem.schedule(finalize);
    }
}

void World::cleanupStalePendingMeshHandles() {
    // Free GPU resources for ALL entities with a PendingMeshHandle.
    // Called when LOD batches are cancelled (center changed, old batches stale).
    size_t pendingCount = 0;
    std::vector<BufferSlice> vbSlices;
    std::vector<BufferSlice> ibSlices;
    std::vector<uint32_t> cullingSlots;
    {
        std::unique_lock regLock(m_registryMutex);
        auto view = m_registry.view<PendingMeshHandle>();
        for (auto entity : view) {
            ++pendingCount;
            auto& pending = view.get<PendingMeshHandle>(entity);
            if (pending.handle.vb.isValid()) vbSlices.push_back(pending.handle.vb);
            if (pending.handle.ib.isValid()) ibSlices.push_back(pending.handle.ib);
            if (pending.handle.gpuCullingSlot != UINT32_MAX)
                cullingSlots.push_back(pending.handle.gpuCullingSlot);
        }
        // Remove all PendingMeshHandle components in one pass
        m_registry.clear<PendingMeshHandle>();
    }
    if (m_vbAllocator && !vbSlices.empty()) {
        m_vbAllocator->freeBatch(vbSlices.data(), vbSlices.size());
    }
    if (m_ibAllocator && !ibSlices.empty()) {
        m_ibAllocator->freeBatch(ibSlices.data(), ibSlices.size());
    }
    if (m_gpuCulling && !cullingSlots.empty()) {
        m_gpuCulling->freeSlots(cullingSlots.data(), cullingSlots.size());
    }
    if (pendingCount > 0) {
        std::string reason = "CleanupStalePendingMeshes count=" + std::to_string(pendingCount);
        noteChunkVisualError(
            nullptr,
            -1,
            "LODCleanup",
            reason.c_str(),
            0,
            static_cast<uint32_t>(pendingCount),
            0);
    }
}

bool World::onBatchChunkReady(uint32_t batchId) {
    if (!m_chunkManager) return false;
    return m_chunkManager->signalBatchChunkReady(batchId);
}

bool World::isBatchActive(uint32_t batchId) const {
    return m_chunkManager && m_chunkManager->isBatchActive(batchId);
}

void World::enqueueDeferredMeshBufferFree(const BufferSlice& vb, const BufferSlice& ib) {
    if (!vb.isValid() && !ib.isValid()) {
        return;
    }
    m_pendingMeshBufferFrees.push_back(PendingMeshBufferFree{vb, ib});
    ++m_currentFinalizeDiag.lodSwapFreeQueuedCount;
    m_currentFinalizeDiag.lodSwapFreeBacklog =
        static_cast<uint32_t>(std::min<size_t>(
            m_pendingMeshBufferFrees.size(),
            static_cast<size_t>(std::numeric_limits<uint32_t>::max())));
}

void World::processDeferredMeshBufferFrees(BufferSuballocator* vbAllocator,
                                           BufferSuballocator* ibAllocator,
                                           size_t maxFreeCount) {
    if (m_pendingMeshBufferFrees.empty() || (!vbAllocator && !ibAllocator)) {
        return;
    }

    size_t budget = (maxFreeCount == 0)
        ? m_pendingMeshBufferFrees.size()
        : std::min(maxFreeCount, m_pendingMeshBufferFrees.size());
    if (budget == 0) {
        return;
    }

    using Clock = std::chrono::high_resolution_clock;
    auto t = Clock::now();

    m_deferredFreeVbScratch.clear();
    m_deferredFreeIbScratch.clear();
    m_deferredFreeVbScratch.reserve(budget);
    m_deferredFreeIbScratch.reserve(budget);

    for (size_t i = 0; i < budget; ++i) {
        PendingMeshBufferFree pending = m_pendingMeshBufferFrees.front();
        m_pendingMeshBufferFrees.pop_front();
        if (pending.vb.isValid()) m_deferredFreeVbScratch.push_back(pending.vb);
        if (pending.ib.isValid()) m_deferredFreeIbScratch.push_back(pending.ib);
    }

    if (vbAllocator && !m_deferredFreeVbScratch.empty()) {
        vbAllocator->freeBatch(m_deferredFreeVbScratch.data(), m_deferredFreeVbScratch.size());
    }
    if (ibAllocator && !m_deferredFreeIbScratch.empty()) {
        ibAllocator->freeBatch(m_deferredFreeIbScratch.data(), m_deferredFreeIbScratch.size());
    }

    m_currentFinalizeDiag.lodSwapFreeDrainedCount += static_cast<uint32_t>(
        std::min<size_t>(budget, static_cast<size_t>(std::numeric_limits<uint32_t>::max())));
    m_currentFinalizeDiag.lodSwapFreeBacklog =
        static_cast<uint32_t>(std::min<size_t>(
            m_pendingMeshBufferFrees.size(),
            static_cast<size_t>(std::numeric_limits<uint32_t>::max())));
    m_currentFinalizeDiag.lodSwapFreeMs +=
        std::chrono::duration<float, std::milli>(Clock::now() - t).count();
}

size_t World::processLODSwaps(BufferSuballocator* vbAllocator,
                               BufferSuballocator* ibAllocator,
                               uint64_t deviceTimeline) {
    using Clock = std::chrono::high_resolution_clock;
    auto& diag = m_currentFinalizeDiag;

    processDeferredMeshBufferFrees(vbAllocator, ibAllocator);

    if (!m_chunkManager) return 0;

    size_t totalSwapped = 0;

    struct DeferredFree {
        BufferSlice vb;
        BufferSlice ib;
        uint32_t gpuCullingSlot{UINT32_MAX};
    };
    struct CollisionRefreshRequest {
        entt::entity entity{entt::null};
        glm::ivec3 coord{0};
        int lodLevel{0};
    };
    struct ColliderRemoval {
        uint32_t bodyIdIndex{0xFFFFFFFFu};
        uint32_t bodyIdSequence{0};
    };
    struct SwapVisualReady {
        glm::ivec3 coord{0};
        std::chrono::steady_clock::time_point uploadEnqueueTime{};
        int lodLevel{0};
        uint64_t vramBytes{0};
        uint32_t vertexCount{0};
        uint32_t indexCount{0};
        ChunkDebugAttribution debugInfo{};
    };
    std::vector<DeferredFree> deferredFrees;
    std::vector<CollisionRefreshRequest> collisionRefreshes;
    std::vector<ColliderRemoval> colliderRemovals;
    std::vector<SwapVisualReady> visualReadyEntries;
    std::vector<uint32_t> cullingSlotsToActivate;

    // Drain every completed batch whose uploads are visible to the GPU.
    while (LODTransitionBatch* batch = m_chunkManager->getCompletedBatch()) {
        uint32_t batchId = batch->batchId;
        size_t batchSwapped = 0;
        size_t batchInvalidEntities = 0;
        size_t batchMissingPending = 0;
        size_t batchMismatchedPending = 0;

        // A completed batch means every PendingMeshHandle has been uploaded
        // into the staging component, not necessarily that the graphics queue
        // can see those uploads yet. Keep the old visible MeshHandle until the
        // upload timeline has crossed every pending handle to avoid
        // LODN -> blank -> LODM frames.
        bool batchGpuReady = true;
        {
            std::shared_lock regLock(m_registryMutex);
            for (entt::entity entity : batch->entities) {
                if (!m_registry.valid(entity) ||
                    !m_registry.all_of<PendingMeshHandle>(entity)) {
                    continue;
                }
                const auto& pending = m_registry.get<PendingMeshHandle>(entity);
                if (pending.batchId == batchId &&
                    pending.handle.gpuReadyValue > deviceTimeline) {
                    batchGpuReady = false;
                    break;
                }
            }
        }
        if (!batchGpuReady) {
            break;
        }

        ++diag.lodSwapBatchCount;

        // Phase 1: Under unique_lock — swap components only (no allocator calls)
        {
            auto lockWaitStart = Clock::now();
            std::unique_lock regLock(m_registryMutex);
            auto lockAcquired = Clock::now();
            glm::ivec3 center = m_chunkManager ? m_chunkManager->getCenterChunk() : glm::ivec3(0, 0, 0);
            for (entt::entity entity : batch->entities) {
                if (!m_registry.valid(entity)) {
                    ++batchInvalidEntities;
                    continue;
                }
                if (!m_registry.all_of<PendingMeshHandle>(entity)) {
                    ++batchMissingPending;
                    continue;
                }

                auto& pending = m_registry.get<PendingMeshHandle>(entity);
                const auto pendingUploadEnqueueTime = pending.uploadEnqueueTime;
                const uint64_t pendingVramBytes = pending.handle.vb.size + pending.handle.ib.size;
                const uint32_t pendingVertexCount = static_cast<uint32_t>(pending.handle.vb.size / sizeof(Vertex));
                const uint32_t pendingIndexCount = pending.handle.getTotalIndexCount();
                ChunkDebugAttribution pendingDebug = pending.debugInfo;
                if (pendingDebug.uploadBytes == 0) {
                    pendingDebug.uploadBytes = pendingVramBytes;
                }
                if (pendingDebug.subChunkCount == 0) {
                    pendingDebug.subChunkCount = pending.handle.subChunkCount;
                }
                pendingDebug.residency = deriveChunkResidencyKind(
                    /*gpuResident=*/true,
                    pendingDebug.artifactCacheResident,
                    /*pendingBatch=*/false);

                // Only swap if this PendingMeshHandle belongs to THIS batch.
                // If the entity was reassigned to a newer batch, its pending
                // mesh belongs to that batch and must not be consumed here.
                if (pending.batchId != batchId) {
                    ++batchMismatchedPending;
                    continue;
                }

                if (m_registry.all_of<MeshHandle>(entity)) {
                    auto& oldHandle = m_registry.get<MeshHandle>(entity);
                    meshStatsSub(oldHandle);
                    DeferredFree df;
                    df.vb = oldHandle.vb;
                    df.ib = oldHandle.ib;
                    df.gpuCullingSlot = oldHandle.gpuCullingSlot;
                    deferredFrees.push_back(df);
                }

                const uint32_t pendingCullingSlot = pending.handle.gpuCullingSlot;
                meshStatsAdd(pending.handle);
                m_registry.emplace_or_replace<MeshHandle>(entity, pending.handle);
                m_registry.remove<PendingMeshHandle>(entity);
                if (pendingCullingSlot != UINT32_MAX) {
                    cullingSlotsToActivate.push_back(pendingCullingSlot);
                }

                if (m_registry.all_of<Chunk>(entity)) {
                    auto& chunk = m_registry.get<Chunk>(entity);
                    chunk.lodLevel = batch->targetLOD;
                    if (m_registry.all_of<ChunkCoord>(entity)) {
                        glm::ivec3 coord = m_registry.get<ChunkCoord>(entity).toVec3();
                        chunk.effectiveDataLod = static_cast<uint8_t>(
                            std::clamp(getEffectiveLODForChunk(coord, batch->targetLOD), 0, 255));
                        TerrainType chunkType = getTerrainTypeForChunk(coord, batch->targetLOD);
                        if (chunkType == TerrainType::DCCM) {
                            uint8_t casingMask = 0;
                            static const glm::ivec3 nOff[4] = {{-1,0,0},{1,0,0},{0,0,-1},{0,0,1}};
                            for (int e = 0; e < 4; ++e) {
                                glm::ivec3 nb = coord + nOff[e];
                                int nbRing = m_chunkManager->calculateRingNumber(nb, center);
                                int nbLOD = m_chunkManager->calculateLODFromRing(nbRing);
                                if (getTerrainTypeForChunk(nb, nbLOD) != TerrainType::DCCM) {
                                    casingMask |= (1 << e);
                                }
                            }
                            chunk.casingSeamMask = casingMask;
                            chunk.voxelSeamMask = 0;
                        } else {
                            chunk.voxelSeamMask = (batch->targetLOD > 0)
                                ? m_chunkManager->getSeamEdgeMask(coord, center)
                                : 0;
                            chunk.casingSeamMask = 0;
                        }
                    }
                }

                if (m_registry.all_of<ChunkCoord>(entity)) {
                    const glm::ivec3 coord = m_registry.get<ChunkCoord>(entity).toVec3();
                    collisionRefreshes.push_back({
                        entity,
                        coord,
                        batch->targetLOD
                    });
                    visualReadyEntries.push_back({
                        coord,
                        pendingUploadEnqueueTime,
                        batch->targetLOD,
                        pendingVramBytes,
                        pendingVertexCount,
                        pendingIndexCount,
                        pendingDebug
                    });
                }

                // Physics colliders are tied to visible LOD 0, not chunk
                // lifetime.  Moving across a LOD boundary must therefore add
                // a collider when the LOD-0 mesh becomes visible and remove it
                // when the chunk leaves LOD 0.
                if (batch->targetLOD > 0 && m_registry.all_of<ChunkCollider>(entity)) {
                    const ChunkCollider collider = m_registry.get<ChunkCollider>(entity);
                    if (collider.isValid()) {
                        colliderRemovals.push_back({
                            collider.bodyIdIndex,
                            collider.bodyIdSequence
                        });
                    }
                    m_registry.remove<ChunkCollider>(entity);
                }

                ++batchSwapped;
                ++totalSwapped;
            }
            auto lockDone = Clock::now();
            diag.lodSwapLockWaitMs += std::chrono::duration<float, std::milli>(lockAcquired - lockWaitStart).count();
            diag.lodSwapLockHeldMs += std::chrono::duration<float, std::milli>(lockDone - lockAcquired).count();
        }

        if (batchSwapped == 0 || batchInvalidEntities > 0 || batchMissingPending > 0 || batchMismatchedPending > 0) {
            std::string reason = "BatchSwapSummary swapped=" + std::to_string(batchSwapped) +
                                 " invalid=" + std::to_string(batchInvalidEntities) +
                                 " missingPending=" + std::to_string(batchMissingPending) +
                                 " mismatchedPending=" + std::to_string(batchMismatchedPending);
            noteChunkVisualError(
                nullptr,
                batch->targetLOD,
                "LODSwap",
                reason.c_str(),
                batchId,
                static_cast<uint32_t>(batch->entities.size()),
                static_cast<uint32_t>(batchSwapped));
        }

        m_chunkManager->removeCompletedBatch(batchId);
    }

    diag.lodSwapEntityCount += static_cast<uint32_t>(totalSwapped);
    if (totalSwapped > 0) {
        std::vector<glm::ivec3> changedCoords;
        changedCoords.reserve(visualReadyEntries.size());
        for (const auto& entry : visualReadyEntries) {
            if (entry.debugInfo.affectsShadowGeometry) {
                changedCoords.push_back(entry.coord);
            }
        }
        if (!changedCoords.empty()) {
            recordMeshTopologyChanges(changedCoords);
        }
    }

    // Phase 2: Update GPU-culling slot visibility and retire old resources
    // OUTSIDE the registry lock. VB/IB allocator frees are queued and then
    // drained by the world update.
    {
        auto t = Clock::now();

        std::vector<uint32_t> cullingSlots;
        cullingSlots.reserve(deferredFrees.size());

        uint32_t queuedBufferFrees = 0;
        for (auto& df : deferredFrees) {
            if (df.vb.isValid() || df.ib.isValid()) {
                m_pendingMeshBufferFrees.push_back(PendingMeshBufferFree{df.vb, df.ib});
                ++queuedBufferFrees;
            }
            if (df.gpuCullingSlot != UINT32_MAX) cullingSlots.push_back(df.gpuCullingSlot);
        }
        if (queuedBufferFrees > 0) {
            diag.lodSwapFreeQueuedCount += queuedBufferFrees;
            diag.lodSwapFreeBacklog =
                static_cast<uint32_t>(std::min<size_t>(
                    m_pendingMeshBufferFrees.size(),
                    static_cast<size_t>(std::numeric_limits<uint32_t>::max())));
        }

        // Single culling-list handoff: replacement slots become active at the
        // same time old slots retire, so no LOD update frame can see both.
        if (m_gpuCulling && (!cullingSlotsToActivate.empty() || !cullingSlots.empty())) {
            m_gpuCulling->activateSlotsAndFreeSlots(
                cullingSlotsToActivate.empty() ? nullptr : cullingSlotsToActivate.data(),
                cullingSlotsToActivate.size(),
                cullingSlots.empty() ? nullptr : cullingSlots.data(),
                cullingSlots.size());
        }

        diag.lodSwapFreeMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    for (const auto& request : collisionRefreshes) {
        if (request.lodLevel != 0) {
            continue;
        }

        const bool hasEditedCollision =
            m_editCollisionData.find(request.coord) != m_editCollisionData.end();
        const bool hasRuntimeEdit =
            (request.coord.y != 0) ||
            m_terrainEditOverlay.hasEditsInChunk(request.coord) ||
            hasEditedCollision;

        if (hasRuntimeEdit) {
            refreshEditedChunkCollisionFromArtifact(
                request.entity,
                request.coord,
                request.lodLevel);
        } else {
            static const std::vector<Vertex> emptyVertices;
            static const std::vector<uint16_t> emptyIndices;
            onMeshUploaded(
                request.entity,
                request.coord,
                emptyVertices,
                emptyIndices,
                request.lodLevel);
        }
    }

    if (m_physics) {
        for (const auto& removal : colliderRemovals) {
            m_physics->removeBodyByIndexSeq(
                removal.bodyIdIndex,
                static_cast<uint8_t>(removal.bodyIdSequence));
        }
    }

    const auto finalizeTime = std::chrono::steady_clock::now();
    for (const auto& entry : visualReadyEntries) {
        noteChunkVisualReady(
            entry.coord,
            entry.uploadEnqueueTime,
            finalizeTime,
            entry.lodLevel,
            entry.vramBytes,
            entry.vertexCount,
            entry.indexCount,
            &entry.debugInfo);
    }

    return totalSwapped;
}

size_t World::processSoloPendingSwaps(BufferSuballocator* vbAllocator,
                                       BufferSuballocator* ibAllocator,
                                       uint64_t deviceTimeline) {
    if (!vbAllocator || !ibAllocator) return 0;

    // Phase 1 (shared lock): collect entities with solo PendingMeshHandles ready to swap.
    // batchId == 0 is the solo-edit sentinel set by the NORMAL PATH in processUploads.
    std::vector<entt::entity> toSwap;
    {
        std::shared_lock regLock(m_registryMutex);
        auto view = m_registry.view<const PendingMeshHandle>();
        for (auto entity : view) {
            const auto& pending = view.get<const PendingMeshHandle>(entity);
            if (pending.batchId == 0 && pending.handle.gpuReadyValue <= deviceTimeline) {
                toSwap.push_back(entity);
            }
        }
    }
    if (toSwap.empty()) return 0;

    struct DeferredFree {
        std::vector<BufferSlice> vbs;
        std::vector<BufferSlice> ibs;
        uint32_t gpuCullingSlot{UINT32_MAX};
    };
    std::vector<DeferredFree> deferredFrees;
    deferredFrees.reserve(toSwap.size());

    struct SwapVisualReady {
        glm::ivec3 coord{0};
        std::chrono::steady_clock::time_point uploadEnqueueTime{};
        int lodLevel{0};
        uint64_t vramBytes{0};
        uint32_t vertexCount{0};
        uint32_t indexCount{0};
        ChunkDebugAttribution debugInfo{};
    };
    std::vector<SwapVisualReady> visualReadyEntries;
    visualReadyEntries.reserve(toSwap.size());

    // Phase 2 (unique lock): swap components, collect old buffer slices for deferred free.
    size_t swapped = 0;
    const glm::ivec3 center = m_chunkManager
        ? m_chunkManager->getCenterChunk()
        : glm::ivec3(0, 0, 0);
    {
        std::unique_lock regLock(m_registryMutex);
        for (auto entity : toSwap) {
            if (!m_registry.valid(entity)) continue;
            if (!m_registry.all_of<PendingMeshHandle>(entity)) continue;

            auto& pending = m_registry.get<PendingMeshHandle>(entity);
            // Re-validate: a newer upload may have changed batchId or gpuReadyValue.
            if (pending.batchId != 0 || pending.handle.gpuReadyValue > deviceTimeline) continue;

            if (m_registry.all_of<MeshHandle>(entity)) {
                auto& oldHandle = m_registry.get<MeshHandle>(entity);
                meshStatsSub(oldHandle);
                DeferredFree df;
                oldHandle.collectBufferSlices(df.vbs, df.ibs);
                // Normal path typically reuses culling slot. Only free when slot changes.
                if (oldHandle.gpuCullingSlot != UINT32_MAX &&
                    oldHandle.gpuCullingSlot != pending.handle.gpuCullingSlot) {
                    df.gpuCullingSlot = oldHandle.gpuCullingSlot;
                }
                deferredFrees.push_back(std::move(df));
            }

            meshStatsAdd(pending.handle);
            // Capture visual-ready diagnostics before removing PendingMeshHandle.
            if (m_registry.all_of<ChunkCoord>(entity)) {
                SwapVisualReady vr;
                vr.coord = m_registry.get<ChunkCoord>(entity).toVec3();
                vr.uploadEnqueueTime = pending.uploadEnqueueTime;
                vr.lodLevel = m_registry.all_of<Chunk>(entity)
                    ? m_registry.get<Chunk>(entity).lodLevel
                    : 0;
                vr.vramBytes = pending.handle.getTotalVramBytes();
                vr.vertexCount = pending.handle.getTotalVertexCount();
                vr.indexCount = pending.handle.getTotalIndexCount();
                vr.debugInfo = pending.debugInfo;
                if (vr.debugInfo.uploadBytes == 0) {
                    vr.debugInfo.uploadBytes = vr.vramBytes;
                }
                if (vr.debugInfo.subChunkCount == 0) {
                    vr.debugInfo.subChunkCount = pending.handle.subChunkCount;
                }
                vr.debugInfo.residency = deriveChunkResidencyKind(
                    /*gpuResident=*/true,
                    vr.debugInfo.artifactCacheResident,
                    /*pendingBatch=*/false);
                visualReadyEntries.push_back(std::move(vr));
            }

            if (m_registry.all_of<Chunk, ChunkCoord>(entity)) {
                auto& chunk = m_registry.get<Chunk>(entity);
                const glm::ivec3 coord = m_registry.get<ChunkCoord>(entity).toVec3();
                const int lodLevel = chunk.lodLevel;
                chunk.effectiveDataLod = static_cast<uint8_t>(
                    std::clamp(getEffectiveLODForChunk(coord, lodLevel), 0, 255));

                const TerrainType chunkType = getTerrainTypeForChunk(coord, lodLevel);
                if (chunkType == TerrainType::DCCM) {
                    uint8_t casingMask = 0;
                    if (m_chunkManager) {
                        static const glm::ivec3 nOff[4] = {
                            {-1, 0, 0}, {1, 0, 0}, {0, 0, -1}, {0, 0, 1}
                        };
                        for (int e = 0; e < 4; ++e) {
                            const glm::ivec3 nb = coord + nOff[e];
                            const int nbRing = m_chunkManager->calculateRingNumber(nb, center);
                            const int nbLOD = m_chunkManager->calculateLODFromRing(nbRing);
                            if (getTerrainTypeForChunk(nb, nbLOD) != TerrainType::DCCM) {
                                casingMask |= (1 << e);
                            }
                        }
                    }
                    chunk.casingSeamMask = casingMask;
                    chunk.voxelSeamMask = 0;
                } else {
                    chunk.voxelSeamMask = (lodLevel > 0 && m_chunkManager)
                        ? m_chunkManager->getSeamEdgeMask(coord, center)
                        : 0;
                    chunk.casingSeamMask = 0;
                }
            }

            m_registry.emplace_or_replace<MeshHandle>(entity, pending.handle);
            m_registry.remove<PendingMeshHandle>(entity);
            ++swapped;
        }
    }

    if (swapped > 0) {
        std::vector<glm::ivec3> changedCoords;
        changedCoords.reserve(visualReadyEntries.size());
        size_t shadowAffectingSwaps = 0;
        for (const auto& entry : visualReadyEntries) {
            if (entry.debugInfo.affectsShadowGeometry) {
                ++shadowAffectingSwaps;
                changedCoords.push_back(entry.coord);
            }
        }
        if (shadowAffectingSwaps == 0) {
            // Material-only mesh swaps keep the depth/shadow shape identical.
        } else if (changedCoords.size() == shadowAffectingSwaps) {
            recordMeshTopologyChanges(changedCoords);
        } else {
            recordGlobalMeshTopologyChange();
        }
    }

    // Phase 3: retire old resources outside registry lock.
    {
        std::vector<uint32_t> cullingSlots;
        cullingSlots.reserve(deferredFrees.size());
        uint32_t queuedBufferFrees = 0;
        for (auto& df : deferredFrees) {
            const size_t freeCount = std::max(df.vbs.size(), df.ibs.size());
            for (size_t i = 0; i < freeCount; ++i) {
                BufferSlice vb = (i < df.vbs.size()) ? df.vbs[i] : BufferSlice{};
                BufferSlice ib = (i < df.ibs.size()) ? df.ibs[i] : BufferSlice{};
                if (vb.isValid() || ib.isValid()) {
                    m_pendingMeshBufferFrees.push_back(PendingMeshBufferFree{vb, ib});
                    ++queuedBufferFrees;
                }
            }
            if (df.gpuCullingSlot != UINT32_MAX)
                cullingSlots.push_back(df.gpuCullingSlot);
        }
        if (queuedBufferFrees > 0) {
            m_currentFinalizeDiag.lodSwapFreeQueuedCount += queuedBufferFrees;
            m_currentFinalizeDiag.lodSwapFreeBacklog =
                static_cast<uint32_t>(std::min<size_t>(
                    m_pendingMeshBufferFrees.size(),
                    static_cast<size_t>(std::numeric_limits<uint32_t>::max())));
        }
        if (m_gpuCulling && !cullingSlots.empty())
            m_gpuCulling->freeSlots(cullingSlots.data(), cullingSlots.size());
    }

    // Phase 4: emit visual-ready logs (finalize path misses these while pending).
    if (!visualReadyEntries.empty()) {
        const auto finalizeTime = std::chrono::steady_clock::now();
        for (const auto& entry : visualReadyEntries) {
            noteChunkVisualReady(
                entry.coord,
                entry.uploadEnqueueTime,
                finalizeTime,
                entry.lodLevel,
                entry.vramBytes,
                entry.vertexCount,
                entry.indexCount,
                &entry.debugInfo);
        }
    }

    return swapped;
}

void World::updateLODSwitchDiag() {
    if (!m_lodSwitchDiag.active) return;

    auto now = std::chrono::steady_clock::now();
    if (m_lodSwitchDiag.completedMs == 0.0f) {
        m_lodSwitchDiag.elapsedMs = std::chrono::duration<float, std::milli>(now - m_lodSwitchDiag.startTime).count();
        m_lodSwitchDiag.lastFrameSwapped = m_currentFinalizeDiag.lodSwapEntityCount;
        m_lodSwitchDiag.chunksSwappedTotal += m_currentFinalizeDiag.lodSwapEntityCount;

        // Pipeline stage snapshot
        m_lodSwitchDiag.activeBatches = static_cast<uint32_t>(
            m_chunkManager ? m_chunkManager->getActiveBatchCount() : 0);
        m_lodSwitchDiag.pendingRemeshes = static_cast<uint32_t>(m_pendingLODRemeshes.size());
        m_lodSwitchDiag.lodRemeshQueueSize = static_cast<uint32_t>(m_lodSystem.getRemeshQueueSize());
        m_lodSwitchDiag.uploadQueueSize = m_uploadSystem.getQueueSize();
        m_lodSwitchDiag.finalizeQueueSize = static_cast<uint32_t>(m_uploadSystem.getFinalizeQueueSize());
        m_lodSwitchDiag.peakActiveBatches = std::max(
            m_lodSwitchDiag.peakActiveBatches, m_lodSwitchDiag.activeBatches);

        // Sparkline: record swaps per frame
        m_lodSwitchDiag.sparkline[m_lodSwitchDiag.sparklineIdx % LODSwitchDiag::SPARKLINE_SIZE] =
            m_currentFinalizeDiag.lodSwapEntityCount;
        m_lodSwitchDiag.sparklineIdx++;

        // Mark complete when all batches and pipeline queues are drained
        if (m_lodSwitchDiag.activeBatches == 0 &&
            m_lodSwitchDiag.pendingRemeshes == 0 &&
            m_lodSwitchDiag.lodRemeshQueueSize == 0) {
            m_lodSwitchDiag.completedMs = m_lodSwitchDiag.elapsedMs;
        }
    }

    // Post-completion audit: scan all chunks in the band to find any still stuck
    if (m_lodSwitchDiag.completedMs > 0.0f && !m_lodSwitchDiag.auditDone) {
        m_lodSwitchDiag.auditDone = true;
        m_lodSwitchDiag.auditMs = m_lodSwitchDiag.elapsedMs;
        uint32_t stuckReady = 0, stuckNotReady = 0;
        {
            std::shared_lock regLock(m_registryMutex);
            auto view = m_registry.view<Chunk, ChunkCoord, ChunkState>();
            for (auto entity : view) {
                auto& chunk = view.get<Chunk>(entity);
                if (chunk.lodLevel != m_lodSwitchDiag.band) continue;
                const glm::ivec3 coord = view.get<ChunkCoord>(entity).toVec3();
                if (getTerrainTypeForChunk(coord, m_lodSwitchDiag.band) == TerrainType::DCCM) continue;
                int desired = getEffectiveLODForChunk(coord, m_lodSwitchDiag.band);
                if (chunk.effectiveDataLod != static_cast<uint8_t>(std::clamp(desired, 0, 255))) {
                    auto& state = view.get<ChunkState>(entity);
                    if (state.state == ChunkState::State::Ready) {
                        ++stuckReady;
                    } else {
                        ++stuckNotReady;
                    }
                }
            }
        }
        m_lodSwitchDiag.auditStuckReady = stuckReady;
        m_lodSwitchDiag.auditStuckNotReady = stuckNotReady;
        m_lodSwitchDiag.auditStuckChunks = stuckReady + stuckNotReady;
        if (m_lodSwitchDiag.auditStuckChunks > 0) {
            std::cout << "[LOD-AUDIT] Band " << m_lodSwitchDiag.band
                      << " switch reported complete but " << m_lodSwitchDiag.auditStuckChunks
                      << " chunks stuck (ready=" << stuckReady
                      << " notReady=" << stuckNotReady << ")" << std::endl;
        }
    }
}

std::string World::formatLODSwitchDiagReport() const {
    const auto& d = m_lodSwitchDiag;
    if (!d.active) return "No LOD switch active.\n";

    const char* lodNames[] = { "Full", "1/2", "1/4", "1/8", "1/16" };
    auto safeName = [&](int idx) { return lodNames[std::clamp(idx, 0, 4)]; };

    std::string r;
    r.reserve(2048);
    r += "=== LOD Switch Diagnostics ===\n";
    r += "Band " + std::to_string(d.band) + ": " + safeName(d.oldDataLOD) + " -> " + safeName(d.newDataLOD) + "\n";
    r += "Status: " + std::string(d.completedMs > 0.0f ? "COMPLETE" : "IN PROGRESS") + "\n\n";

    // Timing
    float elapsed = d.completedMs > 0.0f ? d.completedMs : d.elapsedMs;
    r += "--- Timing ---\n";
    r += "Setup scan:    " + std::to_string(d.setupMs) + " ms\n";
    if (elapsed < 1000.0f)
        r += "Total elapsed: " + std::to_string(elapsed) + " ms\n";
    else
        r += "Total elapsed: " + std::to_string(elapsed / 1000.0f) + " s\n";

    // Initial scan
    r += "\n--- Initial Scan ---\n";
    r += "Total chunks in band:   " + std::to_string(d.totalChunksInBand) + "\n";
    r += "Queued for remesh:      " + std::to_string(d.totalChunksQueued) + "\n";
    r += "Skipped (already OK):   " + std::to_string(d.skippedAlreadyCorrect) + "\n";
    r += "Skipped (DCCM):         " + std::to_string(d.skippedDCCM) + "\n";
    r += "Deferred (non-Ready):   " + std::to_string(d.deferredChunks) + "\n";
    if (d.deferredChunks > 0) {
        r += "  Loading: " + std::to_string(d.deferredLoading) +
             "  Meshing: " + std::to_string(d.deferredMeshing) +
             "  Other: " + std::to_string(d.deferredOther) + "\n";
    }
    r += "Cancelled old batches:  " + std::to_string(d.cancelledOldBatches) + "\n";
    r += "Batches created:        " + std::to_string(d.batchesCreated) + "\n";

    // Progress
    r += "\n--- Progress ---\n";
    r += "Chunks swapped: " + std::to_string(d.chunksSwappedTotal) + " / " + std::to_string(d.totalChunksQueued) + "\n";
    r += "Active batches: " + std::to_string(d.activeBatches) + " (peak " + std::to_string(d.peakActiveBatches) + ")\n";
    r += "Pending remeshes: " + std::to_string(d.pendingRemeshes) + "\n";
    r += "LOD remesh queue: " + std::to_string(d.lodRemeshQueueSize) + "\n";
    r += "Upload queue:     " + std::to_string(d.uploadQueueSize) + "\n";
    r += "Finalize queue:   " + std::to_string(d.finalizeQueueSize) + "\n";

    r += "\n--- Attribution ---\n";
    r += "Visual entries:    " + std::to_string(d.readyVisualEntries) + "\n";
    r += "Uploaded bytes:    " + std::to_string(d.uploadedBytesTotal) + "\n";
    r += "Artifact builds:   " + std::to_string(d.artifactBuilds) + "\n";
    r += "Artifact cache:    " + std::to_string(d.artifactCacheHits) + "\n";
    r += "Precomputed:       " + std::to_string(d.precomputedLoads) + "\n";
    r += "Collision base:    " + std::to_string(d.collisionBaseCache) + "\n";
    r += "Collision edit:    " + std::to_string(d.collisionEditPacked) + "\n";
    r += "Collision refresh: " + std::to_string(d.collisionArtifactRefresh) + "\n";
    r += "Collision reused:  " + std::to_string(d.collisionExistingEdit) + "\n";
    r += "GPU resident:      " + std::to_string(d.gpuResidentChunks) + "\n";
    r += "Artifact resident: " + std::to_string(d.artifactResidentChunks) + "\n";
    r += "Monolithic work:   " + std::to_string(d.monolithicChunks) + "\n";
    r += "Paged work:        " + std::to_string(d.pagedChunks) + "\n";
    r += "Dirty pages:       " + std::to_string(d.dirtyPages) + "\n";
    r += "Rebuilt pages:     " + std::to_string(d.rebuiltPages) + "\n";
    r += "Resident pages:    " + std::to_string(d.residentPages) + "\n";
    r += "Evicted pages:     " + std::to_string(d.evictedPages) + "\n";

    // Errors
    uint32_t totalErr = d.errTotalFromSwaps + d.errFilteredByDrain;
    r += "\n--- Errors ---\n";
    r += "Total errors:          " + std::to_string(totalErr) + "\n";
    if (d.errInvalidEntities > 0)
        r += "  Invalid entities:    " + std::to_string(d.errInvalidEntities) + "\n";
    if (d.errMissingPending > 0)
        r += "  Missing pending:     " + std::to_string(d.errMissingPending) + "\n";
    if (d.errMismatchedBatch > 0)
        r += "  Mismatched batch:    " + std::to_string(d.errMismatchedBatch) + "\n";
    if (d.errFilteredByDrain > 0)
        r += "  Filtered by drain:   " + std::to_string(d.errFilteredByDrain) + "\n";

    // Audit
    if (d.auditDone) {
        r += "\n--- Post-Completion Audit ---\n";
        r += "Audit at: " + std::to_string(d.auditMs) + " ms\n";
        r += "Stuck chunks: " + std::to_string(d.auditStuckChunks) + "\n";
        if (d.auditStuckChunks > 0) {
            r += "  Ready (should have been caught): " + std::to_string(d.auditStuckReady) + "\n";
            r += "  Not-Ready (still in pipeline):   " + std::to_string(d.auditStuckNotReady) + "\n";
        }
    }

    return r;
}

````

## MISSING: src/world/WorldLODTransitions.h


## src\world\WorldUpdateLODScan.cpp

Description: No CC-DESC found. C++ struct 'PendingLODInfo'.

````cpp
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/ChunkHoleTracker.h"
#include <iostream>
#include <algorithm>
#include <cmath>
#include <unordered_map>
#include <unordered_set>

// updateLODTransitions() — extracted from updateChunkLoader()
// LOD mismatch scan + eager LOD remesh drain.

void World::updateLODTransitions(float deltaTime, bool centerChanged) {
    // ---------------------------------------------------------------
    // LOD TRANSITION DETECTION (runs in World, not ChunkManager)
    //
    // On center change:
    //   1. Cancel all in-flight LOD batches (stale transitions)
    //   2. Clean up orphaned PendingMeshHandles (free GPU resources)
    //   3. Scan ALL Ready chunks: compare chunk.lodLevel (actual mesh LOD)
    //      vs desired LOD from ring position.  Queue mismatches.
    //   4. Sort closest-first for visual priority.
    //
    // Every frame: drain the queue as fast as the rest of the pipeline allows.
    //
    // KEY INVARIANT: chunk.lodLevel is ONLY updated when the new mesh
    // is actually swapped in (processLODSwaps), never when enqueued.
    // This ensures the scan always sees the TRUE mesh LOD.
    // ---------------------------------------------------------------
    if (centerChanged) {
        // Do NOT cancel LOD batches or clear the remesh queue on center change.
        // Slider/UI-triggered LOD batches (from applyLODChangesIncrementally)
        // must survive movement — they represent deliberate user changes.
        // The version system + desiredLOD checks in updateMeshingSystem handle
        // stale jobs gracefully: if a batch's targetLOD no longer matches the
        // desired LOD (because the center moved), the entity is signaled and
        // dropped, and the next LOD scan re-queues it with the correct target.

        // Rate-limited diagnostic log (once per second)
        static float s_lodDiagTimer = 0.0f;
        s_lodDiagTimer += deltaTime;
        if (s_lodDiagTimer >= 1.0f) {
            s_lodDiagTimer = 0.0f;
            int effDist = m_chunkManager->getEffectiveRenderDistance();
            int baseDist = m_chunkManager->getRenderDistanceRings();
            int extRings = m_chunkManager->getExtensionRings();
            size_t lodQueueSize = m_lodSystem.getRemeshQueueSize();
            size_t pendingRemeshes = m_pendingLODRemeshes.size();
            std::cout << "[World] Center-change LOD: effectiveDist=" << effDist
                      << " (base=" << baseDist << " + ext=" << extRings << ")"
                      << ", lodQueue=" << lodQueueSize
                      << ", pendingRemeshes=" << pendingRemeshes
                      << ", bufPressure=" << (m_chunkManager->hasBufferPressure() ? "YES" : "no")
                      << std::endl;
        }
    }
    
    // LOD mismatch scan: runs on center change AND periodically (every
    // LOD_SCAN_INTERVAL frames) to catch chunks that reached Ready state
    // after the last scan.  Instead of clearing the queue, we MERGE new
    // mismatches so far-away chunks aren't perpetually evicted.
    // During burst recovery, scan every frame to catch chunks that
    // reach Ready state after a large teleport.
    static constexpr size_t LOD_SCAN_INTERVAL = 60;
    m_lodScanCounter++;
    bool runLodScan = centerChanged || (m_lodScanCounter >= LOD_SCAN_INTERVAL)
                      || (m_burstRecoveryFrames > 0);

    // Tick down burst recovery counter (after using it for this frame's decisions)
    if (m_burstRecoveryFrames > 0) {
        m_burstRecoveryFrames--;
    }
    
    if (runLodScan) {
        m_lodScanCounter = 0;
        glm::ivec3 newCenter = m_chunkManager->getCenterChunk();

        // Build a map of pending entries for O(1) update/remove.
        // Key = coord, Value = {desired LOD, isCasingUpdate}.
        struct PendingLODInfo {
            int newLOD{0};
            bool isCasingUpdate{false};
        };
        std::unordered_map<glm::ivec3, PendingLODInfo, IVec3Hash> pendingMap;
        pendingMap.reserve(m_pendingLODRemeshes.size());

        auto lodThresholds = m_chunkManager->getLODThresholdRings();

        // Helper: compute DCCM casing mask for a position relative to a center
        auto computeDCCMCasingMask = [&](const glm::ivec3& pos,
                                         const glm::ivec3& center) -> uint8_t {
            static const glm::ivec3 nOff[4] = {{-1,0,0},{1,0,0},{0,0,-1},{0,0,1}};
            uint8_t mask = 0;
            for (int e = 0; e < 4; ++e) {
                glm::ivec3 nb = pos + nOff[e];
                int nbRing = m_chunkManager->calculateRingNumber(nb, center);
                int nbLOD  = m_chunkManager->calculateLODFromRing(nbRing);
                if (getTerrainTypeForChunk(nb, nbLOD) != TerrainType::DCCM)
                    mask |= (1 << e);
            }
            return mask;
        };

        auto computeVoxelSeamMask = [&](const glm::ivec3& pos,
                                        const glm::ivec3& center,
                                        int lodLevel) -> uint8_t {
            if (!m_chunkManager || lodLevel <= 0) return 0;
            return m_chunkManager->getSeamEdgeMask(pos, center);
        };

        std::unordered_set<entt::entity> scanInFlightSet;
        auto addAllInFlightEntities = [&]() {
            std::scoped_lock vLock(m_chunkVersionMutex);
            scanInFlightSet.reserve(m_chunkVersionStates.size());
            for (const auto& [ent, vs] : m_chunkVersionStates) {
                if (vs && vs->inFlight.load(std::memory_order_acquire)) {
                    scanInFlightSet.insert(ent);
                }
            }
        };
        auto addCandidateInFlightEntities = [&](const std::vector<entt::entity>& entities) {
            if (entities.empty()) return;
            scanInFlightSet.reserve(entities.size());
            std::scoped_lock vLock(m_chunkVersionMutex);
            for (entt::entity ent : entities) {
                auto it = m_chunkVersionStates.find(ent);
                if (it != m_chunkVersionStates.end() && it->second &&
                    it->second->inFlight.load(std::memory_order_acquire)) {
                    scanInFlightSet.insert(ent);
                }
            }
        };

        if (centerChanged) {
            // Center-change LOD scan: enumerate only the square-band cells
            // whose classification can actually change.  For each true LOD
            // boundary T, the changed set is the symmetric difference of the
            // old/new Chebyshev squares of radius T, expanded by one cell so
            // same-LOD seam/casing removals on the former boundary are seen.
            glm::ivec3 prevCenter = m_chunkManager->getPreviousCenter();
            int moveDist = std::max(std::abs(newCenter.x - prevCenter.x),
                                    std::abs(newCenter.z - prevCenter.z));
            int effectiveDist = m_chunkManager->getEffectiveRenderDistance();

            // Lambda to check one coordinate position for LOD mismatch.
            auto checkPos = [&](const glm::ivec3& pos) {
                const int ring = m_chunkManager->calculateRingNumber(pos, newCenter);
                if (ring >= effectiveDist) return;

                auto entityIt = m_chunkEntityMap.find(pos);
                if (entityIt == m_chunkEntityMap.end()) return;
                entt::entity entity = entityIt->second;

                if (!m_registry.valid(entity)) return;
                if (!m_registry.all_of<Chunk, ChunkState>(entity)) return;
                if (m_registry.get<ChunkState>(entity).state != ChunkState::State::Ready) return;

                // Skip chunks whose new mesh is already uploaded and waiting for batch swap.
                // Re-detecting them would create a duplicate batch that overwrites batch info,
                // causing VersionMismatchDrop cascades and mismatched-batch errors.
                if (m_registry.all_of<PendingMeshHandle>(entity)) return;

                // Skip entities whose remesh pipeline is in-flight or queued.
                if (scanInFlightSet.count(entity)) return;
                if (m_lodSystem.isPending(entity)) return;

                auto& chunk = m_registry.get<Chunk>(entity);
                int desiredLOD = m_chunkManager->calculateLODFromRing(ring);
                int currentLOD = chunk.lodLevel;

                if (currentLOD != desiredLOD) {
                    pendingMap[pos] = {desiredLOD, false};
                } else {
                    const int desiredEffectiveDataLod = getEffectiveLODForChunk(pos, currentLOD);
                    if (chunk.effectiveDataLod != desiredEffectiveDataLod) {
                        // During an active data-LOD switch, setDataLODForBand already
                        // queued all chunks in this band.  Re-detecting them fills
                        // m_pendingLODRemeshes with redundant entries that the drain
                        // converts into duplicate batches — overwriting batch info,
                        // causing VersionMismatchDrop and mismatched-batch errors.
                        if (m_lodSwitchDiag.active && m_lodSwitchDiag.completedMs == 0.0f &&
                            currentLOD == m_lodSwitchDiag.band) {
                            return;
                        }
                        pendingMap[pos] = {currentLOD, false};
                        return;
                    }

                    // LOD matches — check if seam/casing topology changed
                    TerrainType thisType = getTerrainTypeForChunk(pos, currentLOD);
                    if (thisType == TerrainType::DCCM) {
                        uint8_t newMask = computeDCCMCasingMask(pos, newCenter);
                        if (newMask != chunk.casingSeamMask) {
                            pendingMap[pos] = {currentLOD, true};
                        } else {
                            pendingMap.erase(pos);
                        }
                    } else {
                        uint8_t newMask = computeVoxelSeamMask(pos, newCenter, currentLOD);
                        if (newMask != chunk.voxelSeamMask) {
                            pendingMap[pos] = {currentLOD, true};
                        } else {
                            pendingMap.erase(pos);
                        }
                    }
                }
            };

            struct Rect {
                int minX;
                int maxX;
                int minZ;
                int maxZ;
            };

            std::vector<glm::ivec3> candidates;
            candidates.reserve(std::max<size_t>(128, lodThresholds.size() *
                static_cast<size_t>(std::max(1, moveDist + 2)) * 256u));

            auto addRect = [&](int minX, int maxX, int minZ, int maxZ, int expand) {
                if (minX > maxX || minZ > maxZ) return;
                minX -= expand;
                maxX += expand;
                minZ -= expand;
                maxZ += expand;
                for (int z = minZ; z <= maxZ; ++z) {
                    for (int x = minX; x <= maxX; ++x) {
                        candidates.push_back(glm::ivec3{x, 0, z});
                    }
                }
            };

            auto addRectDifference = [&](const Rect& a, const Rect& b, int expand) {
                addRect(a.minX, std::min(a.maxX, b.minX - 1), a.minZ, a.maxZ, expand);
                addRect(std::max(a.minX, b.maxX + 1), a.maxX, a.minZ, a.maxZ, expand);

                const int overlapMinX = std::max(a.minX, b.minX);
                const int overlapMaxX = std::min(a.maxX, b.maxX);
                if (overlapMinX > overlapMaxX) return;

                addRect(overlapMinX, overlapMaxX, a.minZ, std::min(a.maxZ, b.minZ - 1), expand);
                addRect(overlapMinX, overlapMaxX, std::max(a.minZ, b.maxZ + 1), a.maxZ, expand);
            };

            auto addThresholdCandidates = [&](int threshold) {
                if (threshold < 0 || threshold >= effectiveDist) return;
                if (m_chunkManager->calculateLODFromRing(threshold) ==
                    m_chunkManager->calculateLODFromRing(threshold + 1)) {
                    return;
                }

                Rect oldRect{
                    prevCenter.x - threshold,
                    prevCenter.x + threshold,
                    prevCenter.z - threshold,
                    prevCenter.z + threshold
                };
                Rect newRect{
                    newCenter.x - threshold,
                    newCenter.x + threshold,
                    newCenter.z - threshold,
                    newCenter.z + threshold
                };

                addRectDifference(oldRect, newRect, 1);
                addRectDifference(newRect, oldRect, 1);
            };

            for (int threshold : lodThresholds) {
                addThresholdCandidates(threshold);
            }

            // Preserve already-deferred work, but re-validate it below so stale
            // center-change entries don't keep cycling through the queue.
            for (const auto& req : m_pendingLODRemeshes) {
                candidates.push_back(req.coord);
            }

            std::sort(candidates.begin(), candidates.end(),
                [](const glm::ivec3& a, const glm::ivec3& b) {
                    if (a.x != b.x) return a.x < b.x;
                    if (a.z != b.z) return a.z < b.z;
                    return a.y < b.y;
                });
            candidates.erase(
                std::unique(candidates.begin(), candidates.end(),
                    [](const glm::ivec3& a, const glm::ivec3& b) {
                        return a.x == b.x && a.y == b.y && a.z == b.z;
                    }),
                candidates.end());

            // Build the in-flight snapshot only for differential candidates.
            // A center change usually touches a few boundary strips; scanning
            // every version state here was pure overhead during movement.
            std::vector<entt::entity> candidateEntities;
            candidateEntities.reserve(candidates.size());
            {
                std::shared_lock regLock(m_registryMutex);
                for (const glm::ivec3& pos : candidates) {
                    auto entityIt = m_chunkEntityMap.find(pos);
                    if (entityIt != m_chunkEntityMap.end()) {
                        candidateEntities.push_back(entityIt->second);
                    }
                }
            }
            addCandidateInFlightEntities(candidateEntities);

            {
                std::shared_lock csLock(m_chunkStateMutex);
                std::shared_lock regLock(m_registryMutex);
                for (const glm::ivec3& pos : candidates) {
                    checkPos(pos);
                }
            }
        } else {
            // Full periodic scan: build a full in-flight snapshot once, then
            // do O(1) membership checks while walking the ECS view.
            addAllInFlightEntities();

            for (const auto& req : m_pendingLODRemeshes)
                pendingMap[req.coord] = {req.newLOD, req.isCasingUpdate};

            // Periodic scan (every LOD_SCAN_INTERVAL frames): iterate full ECS view
            // as TRUE catch-all for any mismatches missed by the differential scan.
            // This must check ALL chunks, not just near-boundary ones, to catch
            // interior chunks that have wrong LOD after ring allocation changes.

            // Storm detection: log when a chunk keeps getting re-queued with the same mismatch
            static std::unordered_map<glm::ivec3, uint32_t, IVec3Hash> s_remeshStormCounts;

            {
                std::shared_lock regLock(m_registryMutex);
                auto view = m_registry.view<ChunkCoord, Chunk, ChunkState>();
                for (auto entity : view) {
                    auto& state = view.get<ChunkState>(entity);
                    if (state.state != ChunkState::State::Ready) continue;

                    // Skip chunks whose new mesh is already uploaded and waiting for batch swap.
                    if (m_registry.all_of<PendingMeshHandle>(entity)) continue;

                    // Skip entities whose remesh pipeline is in-flight or queued.
                    if (scanInFlightSet.count(entity)) continue;
                    if (m_lodSystem.isPending(entity)) continue;

                    auto& coord = view.get<ChunkCoord>(entity);
                    glm::ivec3 cv = coord.toVec3();
                    int newRing = m_chunkManager->calculateRingNumber(cv, newCenter);

                    auto& chunk = view.get<Chunk>(entity);
                    int desiredLOD = m_chunkManager->calculateLODFromRing(newRing);

                    if (chunk.lodLevel != desiredLOD) {
                        pendingMap[cv] = {desiredLOD, false};
                        if (++s_remeshStormCounts[cv] == 5) {
                            std::cout << "[LOD-STORM] (" << cv.x << "," << cv.y << "," << cv.z << ")"
                                      << " lodMismatch current=" << chunk.lodLevel
                                      << " desired=" << desiredLOD << std::endl;
                        }
                    } else {
                        const int desiredEffectiveDataLod = getEffectiveLODForChunk(cv, chunk.lodLevel);
                        if (chunk.effectiveDataLod != desiredEffectiveDataLod) {
                            // Suppress during active data-LOD switch (see center-change path).
                            if (m_lodSwitchDiag.active && m_lodSwitchDiag.completedMs == 0.0f &&
                                chunk.lodLevel == m_lodSwitchDiag.band) {
                                continue;
                            }
                            pendingMap[cv] = {chunk.lodLevel, false};
                            if (++s_remeshStormCounts[cv] == 5) {
                                std::cout << "[LOD-STORM] (" << cv.x << "," << cv.y << "," << cv.z << ")"
                                          << " dataLodMismatch current=" << (int)chunk.effectiveDataLod
                                          << " desired=" << desiredEffectiveDataLod
                                          << " lod=" << chunk.lodLevel << std::endl;
                            }
                            continue;
                        }

                        // LOD matches — check if seam/casing topology changed
                        TerrainType thisType = getTerrainTypeForChunk(cv, chunk.lodLevel);
                        if (thisType == TerrainType::DCCM) {
                            uint8_t newMask = computeDCCMCasingMask(cv, newCenter);
                            if (newMask != chunk.casingSeamMask) {
                                pendingMap[cv] = {chunk.lodLevel, true};
                                if (++s_remeshStormCounts[cv] == 5) {
                                    std::cout << "[LOD-STORM] (" << cv.x << "," << cv.y << "," << cv.z << ")"
                                              << " casingMismatch chunk=" << (int)chunk.casingSeamMask
                                              << " computed=" << (int)newMask
                                              << " lod=" << chunk.lodLevel << std::endl;
                                }
                            } else {
                                pendingMap.erase(cv);
                                s_remeshStormCounts.erase(cv);
                            }
                        } else {
                            uint8_t newMask = computeVoxelSeamMask(cv, newCenter, chunk.lodLevel);
                            if (newMask != chunk.voxelSeamMask) {
                                pendingMap[cv] = {chunk.lodLevel, true};
                                if (++s_remeshStormCounts[cv] == 5) {
                                    std::cout << "[LOD-STORM] (" << cv.x << "," << cv.y << "," << cv.z << ")"
                                              << " voxelSeamMismatch chunk=" << (int)chunk.voxelSeamMask
                                              << " computed=" << (int)newMask
                                              << " lod=" << chunk.lodLevel << std::endl;
                                }
                            } else {
                                pendingMap.erase(cv);
                                s_remeshStormCounts.erase(cv);
                            }
                        }
                    }
                }
            }
        }

        // Rebuild vector from map
        m_pendingLODRemeshes.clear();
        m_pendingLODRemeshes.reserve(pendingMap.size());
        for (const auto& [cv, info] : pendingMap)
            m_pendingLODRemeshes.push_back({cv, info.newLOD, info.isCasingUpdate});

        // Keep nearest work at the back so the per-frame drain can pop without
        // shifting the remaining queue.
        glm::ivec3 center = newCenter;
        std::sort(m_pendingLODRemeshes.begin(), m_pendingLODRemeshes.end(),
                  [&center](const ChunkManager::ChunkRemeshRequest& a,
                            const ChunkManager::ChunkRemeshRequest& b) {
                      int da = std::max(std::abs(a.coord.x - center.x),
                                        std::abs(a.coord.z - center.z));
                      int db = std::max(std::abs(b.coord.x - center.x),
                                        std::abs(b.coord.z - center.z));
                      return da > db;
                  });

        // Periodic LOD health diagnostic (rate-limited to once per second)
        static float s_lodHealthTimer = 0.0f;
        s_lodHealthTimer += deltaTime;
        if (s_lodHealthTimer >= 1.0f) {
            s_lodHealthTimer = 0.0f;
            size_t lodQueueSize = m_lodSystem.getRemeshQueueSize();
            if (!m_pendingLODRemeshes.empty() || lodQueueSize > 0) {
                std::cout << "[LOD-diag] pendingRemeshes=" << m_pendingLODRemeshes.size()
                          << " lodQueue=" << lodQueueSize
                          << " activeBatches=" << m_chunkManager->getActiveBatchCount()
                          << std::endl;
            }
        }
    }
    
    // --- Drain pending LOD remesh queue ---
    // Process the whole queue so movement doesn't leave remesh work parked
    // behind arbitrary per-frame caps.
    // Each drained chunk: resolve entity, verify still needs remesh,
    // create batch, enqueue meshing job with targetLOD in batch info.
    // chunk.lodLevel is NOT updated here — only in processLODSwaps.
    {
        size_t budget = m_pendingLODRemeshes.size();
        if (budget > 0) {
            // Extract this frame's nearest work from the back of the queue.
            std::vector<ChunkManager::ChunkRemeshRequest> frameBatch;
            frameBatch.reserve(budget);
            for (size_t i = 0; i < budget; ++i) {
                frameBatch.push_back(m_pendingLODRemeshes.back());
                m_pendingLODRemeshes.pop_back();
            }
            
            // Resolve entities and filter stale/no-op entries
            struct ResolvedRemesh {
                entt::entity entity;
                glm::ivec3   coord;
                int          newLOD;
                bool         isCasingUpdate{false};
            };
            std::vector<ResolvedRemesh> resolved;
            resolved.reserve(frameBatch.size());
            {
                std::shared_lock csLock(m_chunkStateMutex);
                for (const auto& req : frameBatch) {
                    auto it = m_chunkEntityMap.find(req.coord);
                    if (it != m_chunkEntityMap.end())
                        resolved.push_back({it->second, req.coord, req.newLOD, req.isCasingUpdate});
                }
            }

            // Filter: skip entities that are gone or already at desired state.
            // Same-LOD topology updates (voxel seam mask / DCCM casing mask)
            // bypass the LOD equality check.
            {
                std::shared_lock regLock(m_registryMutex);
                resolved.erase(
                    std::remove_if(resolved.begin(), resolved.end(),
                        [this](const ResolvedRemesh& r) {
                            if (!m_registry.valid(r.entity) || !m_registry.all_of<Chunk>(r.entity))
                                return true;
                            // Skip entities whose new mesh is already waiting for batch swap.
                            // Creating another batch would overwrite batch info, causing
                            // mismatched-batch errors in processLODSwaps.
                            if (m_registry.all_of<PendingMeshHandle>(r.entity))
                                return true;
                            if (r.isCasingUpdate) return false; // Always process casing updates
                            auto& chunk = m_registry.get<Chunk>(r.entity);
                            if (chunk.lodLevel != r.newLOD) return false; // LOD mismatch — needs remesh
                            // LOD level matches — also check effectiveDataLod.
                            // Data LOD switches keep lodLevel the same but change
                            // the effective data LOD. Without this check, chunks
                            // detected by the periodic scan as data-LOD-mismatched
                            // were silently discarded here.
                            int desiredDataLod = getEffectiveLODForChunk(r.coord, r.newLOD);
                            return chunk.effectiveDataLod == static_cast<uint8_t>(
                                std::clamp(desiredDataLod, 0, 255));
                        }),
                    resolved.end());
            }

            // Filter entities whose remesh pipeline is already in-flight.
            // Without this, the drain creates duplicate batches that overwrite
            // existing batch info via enqueueLODRemesh, causing version-mismatch
            // cascades (upload version differs from bumped version) and
            // mismatched-batch errors in processLODSwaps.
            {
                std::scoped_lock versionLock(m_chunkVersionMutex);
                resolved.erase(
                    std::remove_if(resolved.begin(), resolved.end(),
                        [this](const ResolvedRemesh& r) {
                            auto it = m_chunkVersionStates.find(r.entity);
                            return it != m_chunkVersionStates.end() && it->second &&
                                   it->second->inFlight.load(std::memory_order_acquire);
                        }),
                    resolved.end());
            }

            // Filter entities still queued for remesh (not yet dispatched).
            // isPending is true between enqueueLODRemesh and dispatch in
            // updateMeshingSystem — these entities' batches are already correct.
            resolved.erase(
                std::remove_if(resolved.begin(), resolved.end(),
                    [this](const ResolvedRemesh& r) {
                        return m_lodSystem.isPending(r.entity);
                    }),
                resolved.end());
            
            // Group by target LOD and create batches
            if (!resolved.empty()) {
                std::unordered_map<int, std::vector<ResolvedRemesh*>> lodGroups;
                for (auto& r : resolved)
                    lodGroups[r.newLOD].push_back(&r);
                
                for (auto& [targetLOD, group] : lodGroups) {
                    static constexpr size_t MAX_LOD_BATCH_SIZE = 128;

                    for (size_t batchStart = 0; batchStart < group.size(); batchStart += MAX_LOD_BATCH_SIZE) {
                        const size_t batchEnd = std::min(batchStart + MAX_LOD_BATCH_SIZE, group.size());

                        std::vector<entt::entity> batchEntities;
                        batchEntities.reserve(batchEnd - batchStart);
                        for (size_t i = batchStart; i < batchEnd; ++i) {
                            batchEntities.push_back(group[i]->entity);
                        }

                        uint32_t batchId = m_chunkManager->createLODTransitionBatch(targetLOD, batchEntities);

                        for (size_t i = batchStart; i < batchEnd; ++i) {
                            auto* r = group[i];
                            // Do not bump version here. If a mesh job is in-flight, let it finish;
                            // updateMeshingSystem will schedule the remesh once inFlight clears.
                            m_lodSystem.setDesiredLOD(r->coord, targetLOD);
                            // Pass targetLOD through batch info — NOT via chunk.lodLevel.
                            // If the entity was already pending with a different batch,
                            // signal the old batch so it doesn't get permanently stuck.
                            uint32_t oldBatchId = m_lodSystem.enqueueLODRemesh(r->entity, true, batchId, targetLOD);
                            if (oldBatchId != 0 && m_chunkManager->isBatchActive(oldBatchId)) {
                                m_chunkManager->signalBatchChunkReady(oldBatchId);
                            }

                            // ChunkHole tracking: record LOD remesh enqueue
                            {
                                ChunkHoleEvent ev{};
                                ev.type = ChunkHoleEvent::Type::LODRemeshEnqueued;
                                ev.timestampSec = ChunkHoleEvent::nowSec();
                                ev.toLOD = targetLOD;
                                ev.batchId = batchId;
                                m_chunkHoleTracker.recordEvent(r->coord, std::move(ev));
                            }
                        }
                    }
                }
            }
        }
    }
}

````

## src\world\World.cpp

Description: No CC-DESC found.

````cpp
#include "world/World.h"
#include "ui/InGameDebug.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/config/MapConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "physics/PhysicsWorld.h"
#include <Jolt/Jolt.h>
#include <Jolt/Physics/Body/BodyID.h>
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include "rendering/common/VulkanHelpers.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <iostream>
#include <algorithm>
#include <cmath>
#include <chrono>
#include <thread>
#include <condition_variable>
#include <limits>
#include <iomanip>
#include <sstream>
#include <filesystem>
#include <ctime>
#include <glm/gtc/matrix_transform.hpp>

void World::meshStatsAdd(const MeshHandle& h) {
    if (h.subChunkCount > 0) {
        m_statsChunksWithMesh.fetch_add(1, std::memory_order_relaxed);
        m_statsTotalSubChunks.fetch_add(h.subChunkCount, std::memory_order_relaxed);
        if (h.mainSubChunkCount > 1)
            m_statsSplitChunks.fetch_add(1, std::memory_order_relaxed);
        if (h.subChunkCount > h.mainSubChunkCount)
            m_statsSeamSubChunks.fetch_add(h.subChunkCount - h.mainSubChunkCount, std::memory_order_relaxed);
    }
}

void World::meshStatsSub(const MeshHandle& h) {
    if (h.subChunkCount > 0) {
        m_statsChunksWithMesh.fetch_sub(1, std::memory_order_relaxed);
        m_statsTotalSubChunks.fetch_sub(h.subChunkCount, std::memory_order_relaxed);
        if (h.mainSubChunkCount > 1)
            m_statsSplitChunks.fetch_sub(1, std::memory_order_relaxed);
        if (h.subChunkCount > h.mainSubChunkCount)
            m_statsSeamSubChunks.fetch_sub(h.subChunkCount - h.mainSubChunkCount, std::memory_order_relaxed);
    }
}

World::World()
    : m_chunkManager(std::make_unique<ChunkManager>())
{
    std::cout << "[World] Initialized with full terrain system" << std::endl;
    
    // Initialize subsystems with chunk manager reference
    m_lodSystem.setChunkManager(m_chunkManager.get());
    
    // Set up debug overlay with World pointer
    m_inGameDebug = std::make_unique<InGameDebug>();
    m_inGameDebug->setWorld(this);

    // Wire the new editable terrain foundation.
    // Load the heightmap CSV as the base terrain field, then layer the sparse
    // overlay on top for runtime edits.
    m_terrainFieldSource.setOverlay(&m_terrainEditOverlay);
    m_terrainFieldSource.setTextureMaterialStore(&m_textureMaterialStore);

    {
        std::string heightmapPath = MapConfig::getHeightmapPath();
        if (m_heightmapSampler.load(heightmapPath)) {
            m_terrainFieldSource.setBaseSampler(m_heightmapSampler.makeSamplerFunc());
            std::cout << "[World] Heightmap base sampler wired ("
                      << m_heightmapSampler.getMapWidth() << "x"
                      << m_heightmapSampler.getMapHeight() << ")\n";
        } else {
            // No 2D heightmap — try 3D voxel base (worlds with overhangs / floating islands).
            const std::string voxelBasePath = MapConfig::getBaseVoxelsBinPath();
            if (m_voxelBaseSampler.load(voxelBasePath)) {
                m_terrainFieldSource.setBaseSampler(m_voxelBaseSampler.makeSamplerFunc());
                std::cout << "[World] 3D voxel base sampler wired (base_voxels.bin)\n";
            } else {
                std::cout << "[World] WARNING: No base terrain loaded"
                             " — terrain edits will have no base data\n";
            }
        }
    }
    
    // Initialize terrain file loader using MapConfig (flat maps/ directory)
    std::string terrainFilePath = MapConfig::getTerrainBinPath();
    std::string dccmTerrainFilePath = MapConfig::getDCCMTerrainBinPath();
    std::string collisionFilePath = MapConfig::getCollisionPath();
    m_baseTerrainPath = terrainFilePath;
    m_baseCollisionPath = collisionFilePath;
    m_snapshotRootDir = MapConfig::getMapsBasePath().string();
    
    std::cout << "[World] Loading terrain from maps/" << std::endl;
    std::cout << "[World] Terrain (voxel): " << terrainFilePath << std::endl;
    std::cout << "[World] Terrain (DCCM):  " << dccmTerrainFilePath << std::endl;
    std::cout << "[World] Collision: " << collisionFilePath << std::endl;
    
    m_terrainLoader = std::make_unique<TerrainFileLoader>(terrainFilePath);
    
    // Load DCCM terrain file if it exists
    if (MapConfig::dccmMapExists()) {
        m_dccmTerrainLoader = std::make_unique<TerrainFileLoader>(dccmTerrainFilePath);
        if (m_dccmTerrainLoader->isLoaded()) {
            std::cout << "[World] DCCM terrain loaded successfully" << std::endl;
        } else {
            std::cout << "[World] DCCM terrain file exists but failed to load" << std::endl;
            m_dccmTerrainLoader.reset();
        }
    } else {
        std::cout << "[World] No DCCM terrain file found (terrain_dccm.bin)" << std::endl;
    }
    
    // Set terrain center on ChunkManager based on actual terrain dimensions
    auto dims = m_terrainLoader->getDimensions();
    if (dims.chunksX > 0 && dims.chunksZ > 0) {
        m_chunkManager->setTerrainCenter(dims.chunksX, dims.chunksZ);
    }
    
    m_collisionCache = std::make_unique<Collision::CollisionCache>();
    if (m_collisionCache->load(collisionFilePath)) {
        std::cout << "[World] Loaded precomputed collision cache\n";
    } else {
        std::cout << "[World] No collision cache found, will compute at runtime\n";
        m_collisionCache.reset();
    }
    
    // Set world name
    m_worldName = "terrain";
    
    // Get file modification time as generation date
    try {
        auto ftime = std::filesystem::last_write_time(terrainFilePath);
        auto sctp = std::chrono::time_point_cast<std::chrono::system_clock::duration>(
            ftime - std::filesystem::file_time_type::clock::now() + std::chrono::system_clock::now()
        );
        std::time_t cftime = std::chrono::system_clock::to_time_t(sctp);
        std::tm tm_buf;
        localtime_s(&tm_buf, &cftime);
        char buffer[64];
        std::strftime(buffer, sizeof(buffer), "%Y-%m-%d %H:%M", &tm_buf);
        m_worldGenerationDate = buffer;
    } catch (...) {
        m_worldGenerationDate = "Unknown";
    }
    
    std::cout << "[World] Loaded: " << m_worldName << " (Generated: " << m_worldGenerationDate << ")\n";

    refreshSnapshots();
    updateWorldIdentityFromActiveSnapshot();
    
    // Start lifecycle manager background thread
    m_lifecycleManager.setCallback(this);
    m_lifecycleManager.start();
    std::cout << "[World] Background lifecycle thread started\n";
    
    // Set up batch signal callback for LOD transitions
    m_uploadSystem.setBatchSignalCallback(this);
}

World::~World() {
    // Stop lifecycle manager
    m_lifecycleManager.stop();
    std::cout << "[World] Background lifecycle thread stopped\n";
    
    // ChunkUploadSystem cleans up its own queue in destructor
    // unique_ptr<InGameDebug> destructor runs here (InGameDebug is complete in this TU)
}

std::vector<FramePassKind> World::enumerateFramePasses() const {
    // Current runtime only submits the voxel opaque pass; UI/debug remain inactive.
    return {FramePassKind::VoxelOpaque};
}

// --- IChunkLifecycleCallback implementation ---

std::vector<entt::entity> World::createChunkEntities(const std::vector<glm::ivec3>& coords) {
    return createChunksBatch(coords);
}

void World::scheduleChunkJobs(entt::entity entity, const glm::ivec3& coord, const glm::ivec3& playerChunk) {
    if (entity == entt::null) return;
    
    markChunkPending(coord);

    int desiredLod = getDesiredLODForChunk(coord);
    {
        std::unique_lock regLock(m_registryMutex);
        if (m_registry.valid(entity) && m_registry.all_of<Chunk>(entity)) {
            auto& chunk = m_registry.get<Chunk>(entity);
            chunk.lodLevel = desiredLod;
        }
    }
    
    auto versionState = ensureChunkVersionState(this, entity);
    if (!versionState) {
        return;
    }
    
    // Start pipeline
    versionState->inFlight.store(true, std::memory_order_release);
    versionState->pending.store(false, std::memory_order_release);
    
    setChunkState(entity, ChunkState::State::Loading);
    
    ChunkCoord chunkCoord;
    AABB aabb;
    {
        std::shared_lock regLock(m_registryMutex);
        if (!m_registry.valid(entity)) return;
        chunkCoord = m_registry.get<ChunkCoord>(entity);
        aabb = m_registry.get<AABB>(entity);
    }
    
    // Ring-based priority
    int dx = std::abs(coord.x - playerChunk.x);
    int dz = std::abs(coord.z - playerChunk.z);
    int ringNumber = std::max(dx, dz);
    int distSq = dx * dx + dz * dz;
    int priority = ringNumber * 1000000 + distSq;
    
    auto* payload = m_payloadPool.acquire();
    payload->world = this;
    payload->entity = entity;
    payload->coord = chunkCoord;
    payload->bounds = aabb;
    payload->versionState = versionState;
    payload->version = versionState->version.load(std::memory_order_acquire);
    payload->distanceFromPlayer = priority;
    payload->lodLevel = desiredLod;
    payload->centerAtEnqueue = playerChunk;
    
    int chunkPriority = 1000000 - priority;
    
    // Choose mesh pipeline based on whether chunk has runtime voxel edits
    const bool useRuntimeVoxel = chunkNeedsRuntimeVoxel(coord);
    const TerrainType lodTerrainType = getTerrainTypeForChunk(coord, desiredLod);
    const bool isDCCM = (lodTerrainType == TerrainType::DCCM) && m_heightmapSampler.isLoaded();
    const bool useEditMesher = useRuntimeVoxel && !isDCCM;
    payload->fromTerrainEdit = useEditMesher;
    auto loadJobFn = useEditMesher ? LoadEditMeshJob : LoadPrecomputedMeshJob;
    
    JobHandle load = m_jobSystem.makeWithPriority(loadJobFn, payload, 0, chunkPriority);
    JobHandle upload = m_jobSystem.makeWithPriority(UploadChunkJob, payload, 0, chunkPriority);
    JobHandle finalize = m_jobSystem.makeWithPriority(FinalizeChunkJob, payload, 0, chunkPriority);
    
    m_jobSystem.addDependency(upload, load);
    m_jobSystem.addDependency(finalize, upload);
    
    payload->jobHandles = {load, upload, finalize};
    
    m_jobSystem.schedule(load);
    m_jobSystem.schedule(upload);
    m_jobSystem.schedule(finalize);
}

int World::destroyChunks(const std::vector<glm::ivec3>& coords) {
    return tryDestroyChunksBatch(coords);
}

void World::cleanupStaleVersionStates() {
    std::shared_lock regLock(m_registryMutex);   // protect registry.valid()
    std::scoped_lock versionLock(m_chunkVersionMutex);
    auto& states = m_chunkVersionStates;
    for (auto it = states.begin(); it != states.end(); ) {
        if (!m_registry.valid(it->first)) {
            it = states.erase(it);
        } else {
            ++it;
        }
    }
}

void World::transitionChunkState(entt::entity entity, ChunkState::State state) {
    setChunkState(entity, state);
}

void World::setChunkState(entt::entity entity, ChunkState::State state) {
    glm::ivec3 coord;
    {
        std::unique_lock lock(m_registryMutex);
        if (!m_registry.valid(entity) ||
            !m_registry.all_of<ChunkState, ChunkCoord>(entity)) {
            return;
        }
        auto& chunkState = m_registry.get<ChunkState>(entity);
        chunkState.state = state;
        const auto& chunkCoord = m_registry.get<ChunkCoord>(entity);
        coord = chunkCoord.toVec3();
    }
    setChunkState(coord, state);
}

void World::setChunkState(const glm::ivec3& coord, ChunkState::State state) {
    ChunkState::State oldState = ChunkState::State::Unloaded;
    {
        std::unique_lock lock(m_chunkStateMutex);
        auto it = m_chunkStateMap.find(coord);
        if (it != m_chunkStateMap.end()) {
            oldState = it->second;
        }
        m_chunkStateMap[coord] = state;
    }
    
    // Update atomic counters (decrement old, increment new)
    if (oldState == ChunkState::State::Loading) m_loadingCount.fetch_sub(1, std::memory_order_relaxed);
    else if (oldState == ChunkState::State::Meshing) m_meshingCount.fetch_sub(1, std::memory_order_relaxed);
    else if (oldState == ChunkState::State::Ready) m_readyCount.fetch_sub(1, std::memory_order_relaxed);
    
    if (state == ChunkState::State::Loading) m_loadingCount.fetch_add(1, std::memory_order_relaxed);
    else if (state == ChunkState::State::Meshing) m_meshingCount.fetch_add(1, std::memory_order_relaxed);
    else if (state == ChunkState::State::Ready) m_readyCount.fetch_add(1, std::memory_order_relaxed);

    {
        std::unique_lock setLock(m_chunkSetMutex);
        if (state == ChunkState::State::Ready) {
            m_readyChunkSet.insert(coord);
            if (m_chunkManager && coord.y == 0) {
                m_chunkManager->notifyChunkCreated(coord);
            }
        } else {
            m_readyChunkSet.erase(coord);
        }
    }
}

void World::removeChunkState(const glm::ivec3& coord) {
    std::unique_lock lock(m_chunkStateMutex);
    auto it = m_chunkStateMap.find(coord);
    if (it != m_chunkStateMap.end()) {
        ChunkState::State oldState = it->second;
        if (oldState == ChunkState::State::Loading) m_loadingCount.fetch_sub(1, std::memory_order_relaxed);
        else if (oldState == ChunkState::State::Meshing) m_meshingCount.fetch_sub(1, std::memory_order_relaxed);
        else if (oldState == ChunkState::State::Ready) m_readyCount.fetch_sub(1, std::memory_order_relaxed);
        m_chunkStateMap.erase(it);
    }
    m_chunkEntityMap.erase(coord);
    lock.unlock();
    if (m_chunkManager && coord.y == 0) {
        m_chunkManager->notifyChunkDestroyed(coord);
    }
    {
        std::unique_lock setLock(m_chunkSetMutex);
        m_readyChunkSet.erase(coord);
        m_existingChunkSet.erase(coord);
    }
}

ChunkState::State World::getChunkStateSnapshot(const glm::ivec3& coord) const {
    std::shared_lock lock(m_chunkStateMutex);
    auto it = m_chunkStateMap.find(coord);
    if (it != m_chunkStateMap.end()) {
        return it->second;
    }
    return ChunkState::State::Unloaded;
}

void World::markChunkPending(const glm::ivec3& coord) {
    std::lock_guard lock(m_pendingChunksMutex);
    m_pendingChunks.insert(coord);
}

void World::clearChunkPending(const glm::ivec3& coord) {
    std::lock_guard lock(m_pendingChunksMutex);
    m_pendingChunks.erase(coord);
}

bool World::isChunkPending(const glm::ivec3& coord) const {
    std::lock_guard lock(m_pendingChunksMutex);
    return m_pendingChunks.find(coord) != m_pendingChunks.end();
}

// update(), updateChunkLoader(), updateMarkDirtyOnGeneration(),
// updateMeshingSystem(), updateUploadQueueSystem(), onMeshUploaded(),
// processFinalizeQueue() moved to WorldUpdate.cpp

// createChunk() moved to WorldChunkCRUD.cpp

// createChunksBatch(), tryDestroyChunk(), tryDestroyChunksBatch(),
// resetChunkGeneration(), switchTerrainFile() moved to WorldChunkCRUD.cpp

// setTerrainTypeForLOD(), applyLODChangesIncrementally(),
// releaseMeshesForLOD(), reloadMeshesForLOD() moved to WorldLODTransitions.cpp

int World::getDesiredLODForChunk(const glm::ivec3& coord) const {
    return m_lodSystem.getDesiredLOD(coord);
}

entt::entity World::findChunk(const glm::ivec3& chunkCoord) const {
    std::shared_lock lock(m_chunkStateMutex);
    auto it = m_chunkEntityMap.find(chunkCoord);
    if (it != m_chunkEntityMap.end()) {
        return it->second;
    }
    return entt::null;
}

TerrainEdit::TerrainEditOverlayStore::ChunkSet World::collectExistingChunksInRange(
    const glm::ivec3& minChunk,
    const glm::ivec3& maxChunk) const {
    TerrainEdit::TerrainEditOverlayStore::ChunkSet chunks;
    const glm::ivec3 lo(
        std::min(minChunk.x, maxChunk.x),
        std::min(minChunk.y, maxChunk.y),
        std::min(minChunk.z, maxChunk.z));
    const glm::ivec3 hi(
        std::max(minChunk.x, maxChunk.x),
        std::max(minChunk.y, maxChunk.y),
        std::max(minChunk.z, maxChunk.z));

    std::shared_lock lock(m_chunkStateMutex);
    for (const auto& [coord, entity] : m_chunkEntityMap) {
        if (entity == entt::null) {
            continue;
        }
        if (coord.x < lo.x || coord.x > hi.x ||
            coord.y < lo.y || coord.y > hi.y ||
            coord.z < lo.z || coord.z > hi.z) {
            continue;
        }
        chunks.insert(coord);
    }
    return chunks;
}

World::LoadManagementDiag World::getLoadManagementDiag() const {
    LoadManagementDiag diag{};
    if (m_chunkManager) {
        const auto info = m_chunkManager->getDebugInfo();
        diag.baseRenderDist = info.baseRenderDist;
        diag.effectiveRenderDist = info.effectiveRenderDist;
        diag.extensionRings = info.extensionRings;
        diag.measuredThroughput = info.measuredThroughput;
        diag.pendingCreates = static_cast<uint32_t>(std::max(info.pendingCreates, 0));
        diag.pendingDestroys = static_cast<uint32_t>(std::max(info.pendingDestroys, 0));
        diag.bufferPressure = m_chunkManager->hasBufferPressure();
    }

    diag.lodRemeshQueue =
        static_cast<uint32_t>(std::min<size_t>(m_lodSystem.getRemeshQueueSize(), UINT32_MAX));
    diag.pendingLodRemeshes =
        static_cast<uint32_t>(std::min<size_t>(m_pendingLODRemeshes.size(), UINT32_MAX));
    diag.editRemeshPending =
        static_cast<uint32_t>(std::min<size_t>(m_editRemeshScheduler.pendingCount(), UINT32_MAX));
    diag.uploadQueue = m_uploadSystem.getQueueSize();
    diag.finalizeQueue =
        static_cast<uint32_t>(std::min<size_t>(m_uploadSystem.getFinalizeQueueSize(), UINT32_MAX));
    return diag;
}

size_t World::getChunkCount() const {
    std::shared_lock lock(m_registryMutex);
    return m_registry.view<ChunkCoord>().size();
}

World::TerrainEditPlacementContext World::getTerrainEditPlacementContext(const glm::vec3& worldPos) const {
    TerrainEditPlacementContext context;

    const auto micro = WorldConfig::worldToMicroVoxel(worldPos);
    const auto chunk = WorldConfig::microVoxelToChunk(micro);
    context.chunkCoord = glm::ivec3(chunk.x, chunk.y, chunk.z);

    int bandLodLevel = 0;
    bool foundLoadedChunk = false;

    entt::entity entity = findChunk(context.chunkCoord);
    if (entity != entt::null) {
        std::shared_lock regLock(m_registryMutex);
        if (m_registry.valid(entity) && m_registry.all_of<Chunk>(entity)) {
            bandLodLevel = m_registry.get<Chunk>(entity).lodLevel;
            foundLoadedChunk = true;
        }
    }

    if (!foundLoadedChunk) {
        bandLodLevel = getDesiredLODForChunk(context.chunkCoord);
        if (bandLodLevel < 0 && m_chunkManager) {
            const glm::ivec3 center = m_chunkManager->getCenterChunk();
            const int ring = m_chunkManager->calculateRingNumber(context.chunkCoord, center);
            bandLodLevel = m_chunkManager->calculateLODFromRing(ring);
        }
    }

    bandLodLevel = std::clamp(bandLodLevel, 0, MAX_LOD_LEVELS - 1);
    context.valid = true;
    context.bandLodLevel = bandLodLevel;
    context.terrainType = getTerrainTypeForChunk(context.chunkCoord, bandLodLevel);
    context.previewLodLevel = (context.terrainType == TerrainType::Voxel)
        ? getEffectiveLODForChunk(context.chunkCoord, bandLodLevel)
        : bandLodLevel;
    context.previewLodLevel = std::clamp(context.previewLodLevel, 0, MAX_LOD_LEVELS - 1);
    context.voxelSizeM = WorldConfig::getLODVoxelSizeM(context.previewLodLevel);

    return context;
}

void World::clearEditArtifactCache() {
    std::unique_lock lock(m_editArtifactCacheMutex);
    m_editArtifactCache.clear();
}

void World::markRuntimeVoxelChunks(
    const TerrainEdit::TerrainEditOverlayStore::ChunkSet& chunkCoords)
{
    if (chunkCoords.empty()) {
        return;
    }

    std::unique_lock lock(m_runtimeVoxelChunkMutex);
    m_runtimeVoxelChunks.insert(chunkCoords.begin(), chunkCoords.end());
}

void World::clearRuntimeVoxelChunks() {
    std::unique_lock lock(m_runtimeVoxelChunkMutex);
    m_runtimeVoxelChunks.clear();
}

TerrainEdit::TerrainEditOverlayStore::ChunkSet World::getRuntimeVoxelChunkCoords() const {
    std::shared_lock lock(m_runtimeVoxelChunkMutex);
    return m_runtimeVoxelChunks;
}

bool World::chunkNeedsRuntimeVoxel(const glm::ivec3& chunkCoord) const {
    if (chunkCoord.y != 0 || m_terrainEditOverlay.hasEditsInChunk(chunkCoord)) {
        return true;
    }

    const glm::ivec3 minVoxel = WorldConfig::chunkToMicroVoxel(chunkCoord);
    const glm::ivec3 maxVoxel = minVoxel + glm::ivec3(
        WorldConfig::CHUNK_SIZE,
        WorldConfig::CHUNK_HEIGHT,
        WorldConfig::CHUNK_SIZE);
    if (m_textureMaterialStore.hasSurfaceTexturesInBox(minVoxel, maxVoxel, 0)) {
        return true;
    }

    std::shared_lock lock(m_runtimeVoxelChunkMutex);
    return m_runtimeVoxelChunks.find(chunkCoord) != m_runtimeVoxelChunks.end();
}

void World::markEditsDirty(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks) {
    if (touchedChunks.empty()) return;
    markRuntimeVoxelChunks(touchedChunks);
    m_editRemeshScheduler.markChunksDirty(touchedChunks);
}

void World::markTextureMaterialsDirty(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks) {
    if (touchedChunks.empty()) return;

    // Texture paint changes material only, not occupancy/collision.
    // Invalidate all cached runtime voxel artifacts for these chunks before
    // scheduling the material-only rebake. Otherwise LOD swaps can reuse a
    // pre-paint cached lower-LOD artifact, which makes LOD0 look correct while
    // coarser LODs still show procedural/default material.
    markRuntimeVoxelChunks(touchedChunks);
    invalidateEditArtifacts(touchedChunks);
    m_editRemeshScheduler.markMaterialChunksDirty(touchedChunks);
}

void World::invalidateEditArtifact(const glm::ivec3& chunkCoord) {
    std::unique_lock lock(m_editArtifactCacheMutex);
    for (auto it = m_editArtifactCache.begin(); it != m_editArtifactCache.end(); ) {
        if (it->first.chunkCoord == chunkCoord) {
            it = m_editArtifactCache.erase(it);
        } else {
            ++it;
        }
    }
}

void World::invalidateEditArtifacts(
    const TerrainEdit::TerrainEditOverlayStore::ChunkSet& chunkCoords)
{
    if (chunkCoords.empty()) {
        return;
    }

    std::unique_lock lock(m_editArtifactCacheMutex);
    for (auto it = m_editArtifactCache.begin(); it != m_editArtifactCache.end(); ) {
        if (chunkCoords.find(it->first.chunkCoord) != chunkCoords.end()) {
            it = m_editArtifactCache.erase(it);
        } else {
            ++it;
        }
    }
}

void World::storeEditArtifact(const glm::ivec3& chunkCoord,
                              TerrainType terrainType,
                              int lodLevel,
                              std::vector<Vertex>&& vertices,
                              std::vector<uint32_t>&& indices,
                              glm::vec3 aabbMin,
                              glm::vec3 aabbMax,
                              bool isEmpty,
                              bool deferredBuild) {
    EditArtifactKey key;
    key.chunkCoord = chunkCoord;
    key.terrainType = terrainType;
    key.lodLevel = lodLevel;

    EditArtifact artifact;
    artifact.terrainType = terrainType;
    artifact.lodLevel = lodLevel;
    artifact.isEmpty = isEmpty;
    artifact.deferredBuild = deferredBuild;
    artifact.vertices = std::move(vertices);
    artifact.indices = std::move(indices);
    artifact.aabbMin = aabbMin;
    artifact.aabbMax = aabbMax;

    artifact.generation = ++m_editArtifactGenCounter;

    std::unique_lock lock(m_editArtifactCacheMutex);
    m_editArtifactCache[key] = std::move(artifact);
}

bool World::tryGetEditArtifact(const glm::ivec3& chunkCoord,
                               TerrainType terrainType,
                               int lodLevel,
                               EditArtifact& outArtifact) const {
    EditArtifactKey key;
    key.chunkCoord = chunkCoord;
    key.terrainType = terrainType;
    key.lodLevel = lodLevel;

    std::shared_lock lock(m_editArtifactCacheMutex);
    auto it = m_editArtifactCache.find(key);
    if (it == m_editArtifactCache.end()) {
        return false;
    }

    outArtifact = it->second;
    return true;
}

uint64_t World::getEditArtifactGeneration(const glm::ivec3& chunkCoord,
                                          TerrainType terrainType,
                                          int lodLevel) const {
    EditArtifactKey key;
    key.chunkCoord = chunkCoord;
    key.terrainType = terrainType;
    key.lodLevel = lodLevel;

    std::shared_lock lock(m_editArtifactCacheMutex);
    auto it = m_editArtifactCache.find(key);
    return (it != m_editArtifactCache.end()) ? it->second.generation : 0;
}

void World::preDeserializeCollisionShapes() {
    if (m_collisionCache && m_collisionCache->isLoaded()) {
        m_collisionCache->preDeserializeAll();
    }
}

// cleanupStalePendingMeshHandles(), onBatchChunkReady(),
// processLODSwaps() moved to WorldLODTransitions.cpp

// generateFinalizeDiagReport() moved to WorldDebugMetrics.cpp

// gatherDrawCommands(), gatherDrawCommandsInSphere(),
// enqueueMeshForUpload() moved to WorldRendering.cpp


````

## include\world\World.h

Description: No CC-DESC found. C++ class 'BufferSuballocator'.

````cpp
#pragma once

#include <entt/entt.hpp>
#include <glm/glm.hpp>
#include "vulkan/FramePassTypes.h"
#include "world/WorldTypes.h"
#include "world/chunks/core/Chunk.h"
#include "world/chunks/core/ChunkManager.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "world/TerrainFileLoader.h"
#include "world/edit/TerrainEditOverlayStore.h"
#include "world/edit/TerrainFieldSource.h"
#include "world/edit/TextureOverlayStore.h"
#include "world/edit/HeightmapBaseSampler.h"
#include "world/edit/VoxelBaseSampler.h"
#include "world/edit/TerrainEditRemeshScheduler.h"
#include "world/ChunkHoleTracker.h"
#include "world/chunks/physics/CollisionCache.h"
#include "world/chunks/streaming/ChunkRenderSystem.h"
#include "world/chunks/streaming/ChunkUploadSystem.h"
#include "world/chunks/physics/ChunkCollisionSystem.h"
#include "world/chunks/core/ChunkLODSystem.h"
#include "world/chunks/core/ChunkLifecycleManager.h"
#include "world/WorldDiagnostics.h"
#include "rendering/common/Mesh.h"
#include "ui/InGameDebug.h"
#include <array>
#include <memory>
#include <vector>
#include <deque>
#include <queue>
#include <unordered_set>
#include <unordered_map>
#include <mutex>
#include <shared_mutex>
#include <atomic>
#include <string>
#include <thread>
#include <condition_variable>
#include <set>
#include <chrono>
#include <vulkan/vulkan.h>

// Forward declarations
class BufferSuballocator;
class UploadArena;
class ResourceUploader;
class GPUCullingSystem;

namespace Physics { class PhysicsWorld; }

/**
 * World - Manages chunk entity lifecycle, terrain generation, and rendering
 * 
 * Full terrain system with:
 * - Chunk loading/unloading in circular area around camera
 * - Job-based terrain generation and meshing pipeline
 * - Upload queue system for GPU mesh data
 * - Multi-draw indirect (MDI) rendering with frustum culling
 */
class World : public IUploadCallback, public IChunkLifecycleCallback, public IBatchSignalCallback {
public:
    using Registry = entt::registry;

    // ---- Diagnostic / stats / history types live in WorldDiagnostics.h ----
    // These using-aliases preserve the historical `World::*` qualified names
    // used throughout the codebase.
    using CullingStats              = WorldDiag::CullingStats;
    using TerrainEditDiag           = WorldDiag::TerrainEditDiag;
    using TerrainEditStats          = WorldDiag::TerrainEditStats;
    using TerrainEditHistoryEntry   = WorldDiag::TerrainEditHistoryEntry;
    using TerrainEditHistory        = WorldDiag::TerrainEditHistory;
    using LoadManagementDiag        = WorldDiag::LoadManagementDiag;
    using ChunkVisualHistoryEntry   = WorldDiag::ChunkVisualHistoryEntry;
    using ChunkVisualHistory        = WorldDiag::ChunkVisualHistory;
    using ChunkVisualErrorEntry     = WorldDiag::ChunkVisualErrorEntry;
    using ChunkVisualErrorHistory   = WorldDiag::ChunkVisualErrorHistory;
    using FinalizeDiagFrame         = WorldDiag::FinalizeDiagFrame;
    using LODSwitchDiag             = WorldDiag::LODSwitchDiag;
    using LastUpdateBreakdown       = WorldDiag::LastUpdateBreakdown;

    // Per-snapshot edit collision data (world-space positions + indices per chunk)
    struct EditCollisionEntry {
        std::vector<uint32_t> packedVerts;   // Vertex::packed values
        std::vector<uint32_t> indices;
        glm::ivec3 chunkCoord{0};            // For reconstructing world positions
        uint64_t collisionArtifactGen{0};    // last artifact generation synced to collision
    };

    // --- Terrain edit diagnostics: see WorldDiag::TerrainEditDiag / TerrainEditStats ---

    const TerrainEditDiag& getLastEditDiag() const { return m_lastEditDiag; }
    TerrainEditDiag& editDiagMut() { return m_lastEditDiag; }
    const TerrainEditStats& getEditStats() const { return m_editStats; }
    TerrainEditStats& editStatsMut() { return m_editStats; }

    // --- Terrain edit history: see WorldDiag::TerrainEditHistoryEntry / TerrainEditHistory ---

    const TerrainEditHistory& getEditHistory() const { return m_editHistory; }
    TerrainEditHistory& editHistoryMut() { return m_editHistory; }

    // --- Load management diagnostics: see WorldDiag::LoadManagementDiag ---
    LoadManagementDiag getLoadManagementDiag() const;

    // --- Chunk visual history: see WorldDiag::ChunkVisualHistoryEntry / ChunkVisualHistory ---

    const ChunkVisualHistory& getChunkVisualHistory() const { return m_chunkVisualHistory; }

    // --- Chunk visual error history: see WorldDiag::ChunkVisualErrorEntry / ChunkVisualErrorHistory ---

    const ChunkVisualErrorHistory& getChunkVisualErrorHistory() const { return m_chunkVisualErrorHistory; }
    void appendChunkVisualError(
        const glm::ivec3* coord,
        int lodLevel,
        const char* stage,
        const char* reason,
        uint32_t batchId = 0,
        uint32_t expectedVersion = 0,
        uint32_t actualVersion = 0,
        const ChunkDebugAttribution* debugInfo = nullptr)
    {
        noteChunkVisualError(
            coord,
            lodLevel,
            stage,
            reason,
            batchId,
            expectedVersion,
            actualVersion,
            debugInfo);
    }

    // --- Finalize diagnostics: see WorldDiag::FinalizeDiagFrame ---
    static constexpr size_t FINALIZE_DIAG_CAPACITY = 1200; // 20 seconds at 60fps
    const std::vector<FinalizeDiagFrame>& getFinalizeDiagHistory() const { return m_finalizeDiagHistory; }
    size_t getFinalizeDiagWriteIndex() const { return m_finalizeDiagWriteIdx; }
    std::string generateFinalizeDiagReport(float spikeThresholdMs = 2.0f) const;

    // --- LOD Switch diagnostics: see WorldDiag::LODSwitchDiag ---

    const LODSwitchDiag& getLODSwitchDiag() const { return m_lodSwitchDiag; }
    void updateLODSwitchDiag();
    std::string formatLODSwitchDiagReport() const;

    explicit World();
    ~World();

    /**
     * Update full terrain system
     * @param deltaTime Frame time
     * @param cameraPos World position of camera
     * @param vbAllocator Vertex buffer allocator
     * @param ibAllocator Index buffer allocator
     * @param uploadArena Upload staging arena
     * @param uploader Upload manager
     * @param uploadReadyValue Timeline semaphore value for upload gating
     */
    void update(float deltaTime, 
                const glm::vec3& cameraPos,
                float cameraYaw,
                BufferSuballocator* vbAllocator = nullptr,
                BufferSuballocator* ibAllocator = nullptr,
                UploadArena* uploadArena = nullptr,
                ResourceUploader* uploader = nullptr,
                uint64_t uploadReadyValue = 0,
                float cpuFrameMs = 0.0f,
                float gpuFrameMs = 0.0f,
                uint64_t deviceTimeline = 0);

    /**
     * Gather draw commands for visible chunks
     * @param viewProj View-projection matrix for frustum culling
     * @param outCmds Output buffer for indirect draw commands
     * @param outOrigins Output buffer for chunk origins (vec4)
     * @param maxDraws Maximum number of draws
     * @param deviceTimeline Current timeline value (for upload gating)
     * @return Number of draw commands written
     */
    uint32_t gatherDrawCommands(const glm::mat4& viewProj,
                                 VkDrawIndexedIndirectCommand* outCmds,
                                 glm::vec4* outOrigins,
                                 uint32_t maxDraws,
                                 uint64_t deviceTimeline);

    /**
     * Gather draw commands for chunks intersecting a world-space sphere.
     * Used by per-light point shadow rendering to avoid world-scale terrain draws.
     */
    uint32_t gatherDrawCommandsInSphere(const glm::vec3& center,
                                        float radius,
                                        VkDrawIndexedIndirectCommand* outCmds,
                                        glm::vec4* outOrigins,
                                        uint32_t maxDraws,
                                        uint64_t deviceTimeline);

    static constexpr uint32_t SUN_GATHER_DIAG_MAX_CASCADES = 6;

    struct SunCascadeGatherDiagnostics {
        float totalMs{0.0f};
        float stateMapScanMs{0.0f};
        float candidateSortMs{0.0f};
        float registryWalkMs{0.0f};

        uint32_t cascadeCount{0};
        uint32_t maxDraws{0};
        bool truncated{false};

        glm::ivec3 minChunk{0};
        glm::ivec3 maxChunk{0};
        uint32_t loadedChunkMapSize{0};
        uint32_t bboxCandidateChunks{0};
        uint32_t visitedCandidateChunks{0};

        uint32_t invalidEntityRejects{0};
        uint32_t missingComponentRejects{0};
        uint32_t invisibleRejects{0};
        uint32_t notReadyRejects{0};
        uint32_t invalidMeshRejects{0};
        uint32_t emptyMeshRejects{0};
        uint32_t uploadPendingRejects{0};
        uint32_t cascadeCullRejects{0};
        uint32_t cascadeInnerCullRejects{0};
        uint32_t acceptedChunks{0};
        uint32_t emittedDraws{0};

        float maxHalfExtent{0.0f};
        float sinElevation{0.0f};
        float shearX{0.0f};
        float shearZ{0.0f};
        float shearMax{0.0f};
        float casterReach{0.0f};
        float padding{0.0f};
        float halfX{0.0f};
        float halfZ{0.0f};

        std::array<uint32_t, SUN_GATHER_DIAG_MAX_CASCADES> cascadeChunkHits{};
        std::array<uint32_t, SUN_GATHER_DIAG_MAX_CASCADES> cascadeDrawHits{};
    };

    /**
     * Gather draw commands for the sun shadow cascades in a single walk.
     *
     * Walks chunks intersecting the UNION of per-cascade extruded XZ
     * footprints (cascade footprint = halfExtent + chunkHeight * |shear|),
     * then tests each candidate against every cascade clip-AABB. Returns
     * a per-draw cascade bitmask (`outCascadeMasks[i]`, bit c = 1 means
     * draw `i` is needed by cascade `c`). Skips chunks that don't
     * intersect ANY cascade.
     *
     * This replaces the legacy "single huge sphere then per-cascade
     * AABB cull" pipeline: the walk bounding box is shrunk to the
     * actual visible cascade volume, and each chunk is tested once
     * against all cascades instead of once per cascade per render.
     *
     * @param cameraPos  Camera world position.
     * @param sunDir     Normalised sun direction (towards ground, y < 0).
     * @param cascadeVPs Per-cascade view-projection matrices (length = cascadeCount).
     * @param cascadeHalfExtents Per-cascade ortho half-extent in metres (length = cascadeCount).
     * @param cascadeCount Number of cascades (1..MAX_SUN_SHADOW_CASCADES).
     * @param outCmds   Output draw commands.
     * @param outOrigins Output chunk origin in chunk coordinates.
     * @param outCascadeMasks Output bitmask (bit c = needed by cascade c).
     * @param maxDraws  Output capacity.
     * @param deviceTimeline Upload timeline gate.
     * @param diagnostics Optional detailed counters/timings for debug UI.
     * @param extraChunkPadding Extra X/Z chunk padding for reusable gather supersets.
     * @param includeZeroMaskCandidates Emit ready chunks even when they miss current cascades.
     * @return Number of draws emitted.
     */
    uint32_t gatherDrawCommandsForSunCascades(const glm::vec3& cameraPos,
                                              const glm::vec3& sunDir,
                                              const glm::mat4* cascadeVPs,
                                              const float* cascadeHalfExtents,
                                              uint32_t cascadeCount,
                                              VkDrawIndexedIndirectCommand* outCmds,
                                              glm::vec4* outOrigins,
                                              uint16_t* outCascadeMasks,
                                              uint32_t maxDraws,
                                              uint64_t deviceTimeline,
                                              SunCascadeGatherDiagnostics* diagnostics = nullptr,
                                              int32_t extraChunkPadding = 0,
                                              bool includeZeroMaskCandidates = false);

    uint32_t gatherDrawCommandsForSunCascadeChunk(const glm::ivec3& chunkCoord,
                                                  const glm::mat4* cascadeVPs,
                                                  uint32_t cascadeCount,
                                                  VkDrawIndexedIndirectCommand* outCmds,
                                                  glm::vec4* outOrigins,
                                                  uint16_t* outCascadeMasks,
                                                  uint32_t maxDraws,
                                                  uint64_t deviceTimeline,
                                                  bool includeZeroMaskCandidate = false);



    /**
     * Enqueue mesh for upload to GPU
     * Called by job pipeline after meshing completes
     */
    void enqueueMeshForUpload(entt::entity entity,
                              MeshData&& mesh,
                              bool fromTerrainEdit,
                              std::shared_ptr<struct ChunkVersionState> versionState,
                              uint32_t version,
                              ChunkDebugAttribution debugInfo = {});
    
    /**
     * Enqueue multiple SubChunks for a single entity (v4 format)
     * SubChunks are NEVER merged - each becomes a separate draw call
     * @param mainSubChunkCount How many subChunks are main mesh (rest are seams)
     * @param isRemesh If true, stages to PendingMeshHandle for batch swap
     * @param batchId LOD transition batch ID (0 = none)
     */
    void enqueueMeshForUpload(entt::entity entity,
                              std::vector<MeshData>&& subChunks,
                              uint8_t mainSubChunkCount,
                              bool fromTerrainEdit,
                              std::shared_ptr<struct ChunkVersionState> versionState,
                              uint32_t version,
                              glm::vec3 tightMin = glm::vec3(1e10f),
                              glm::vec3 tightMax = glm::vec3(-1e10f),
                              bool hasTight = false,
                              bool isRemesh = false,
                              uint32_t batchId = 0,
                              ChunkDebugAttribution debugInfo = {});

    /**
     * Access to debug overlay
     */
    InGameDebug& getDebugOverlay() { return *m_inGameDebug; }
    const InGameDebug& getDebugOverlay() const { return *m_inGameDebug; }

    /**
     * Runtime voxel chunk tracking (chunks that need voxel meshing due to edits)
     */
    void markRuntimeVoxelChunks(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& chunkCoords);
    void clearRuntimeVoxelChunks();
    TerrainEdit::TerrainEditOverlayStore::ChunkSet getRuntimeVoxelChunkCoords() const;
    bool chunkNeedsRuntimeVoxel(const glm::ivec3& chunkCoord) const;

    /**
     * Access to the sparse runtime terrain edit overlay.
     * This is the new mutable terrain layer; untouched terrain still uses
     * the precomputed mesh/collision fast path.
     */
    TerrainEdit::TerrainEditOverlayStore& getTerrainEditOverlay() { return m_terrainEditOverlay; }
    const TerrainEdit::TerrainEditOverlayStore& getTerrainEditOverlay() const { return m_terrainEditOverlay; }

    /// Sparse voxel-face material assignments authored by the texture brush.
    TextureOverlay::TextureOverlayStore& getTextureMaterialStore() { return m_textureMaterialStore; }
    const TextureOverlay::TextureOverlayStore& getTextureMaterialStore() const { return m_textureMaterialStore; }

    /**
     * Access to the merged terrain field source (base + edit overlay).
     * The base sampler is not wired yet; this is the foundation for the
     * editable terrain pipeline.
     */
    TerrainEdit::TerrainFieldSource& getTerrainFieldSource() { return m_terrainFieldSource; }
    const TerrainEdit::TerrainFieldSource& getTerrainFieldSource() const { return m_terrainFieldSource; }

    /// Access the heightmap base sampler (for height-range queries).
    const TerrainEdit::HeightmapBaseSampler& getHeightmapSampler() const { return m_heightmapSampler; }

    /// Access the voxel base sampler (for 3D voxel range queries).
    const TerrainEdit::VoxelBaseSampler& getVoxelBaseSampler() const { return m_voxelBaseSampler; }

    /// Access the chunk hole tracker (for diagnosing LOD swap visual holes).
    ChunkHoleTracker& getChunkHoleTracker() { return m_chunkHoleTracker; }
    const ChunkHoleTracker& getChunkHoleTracker() const { return m_chunkHoleTracker; }

    /// Mark edited chunks dirty for remeshing.
    void markEditsDirty(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks);
    void markTextureMaterialsDirty(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks);

    struct SnapshotInfo {
        std::string id;
        std::string displayName;
        std::string createdAt;
        bool isBase{false};
        uint64_t editedCells{0};
        uint64_t editedBricks{0};
    };

    struct TerrainBoxRecord {
        glm::vec3 minCorner{0.0f};
        glm::vec3 maxCorner{0.0f};
    };

    const std::vector<SnapshotInfo>& getSnapshotInfos() const { return m_snapshotInfos; }
    const std::vector<TerrainBoxRecord>& getTerrainBoxes() const { return m_terrainBoxes; }
    uint64_t getTerrainBoxRevision() const { return m_terrainBoxRevision; }
    struct MeshTopologyChange {
        uint64_t revision{0};
        glm::ivec3 coord{0};
    };
    uint64_t getMeshTopologyVersion() const {
        return m_meshTopologyVersion.load(std::memory_order_relaxed);
    }
    bool getMeshTopologyChangesSince(uint64_t revision,
                                     std::vector<MeshTopologyChange>& outChanges,
                                     size_t maxChanges = 8192) const;
    int getActiveSnapshotIndex() const { return m_activeSnapshotIndex; }
    std::string getActiveSnapshotName() const {
        if (m_activeSnapshotIndex >= 0 &&
            m_activeSnapshotIndex < static_cast<int>(m_snapshotInfos.size())) {
            return m_snapshotInfos[static_cast<size_t>(m_activeSnapshotIndex)].displayName;
        }
        return m_worldName;
    }
    bool hasActiveEditableSnapshot() const {
        return m_activeSnapshotIndex > 0 &&
               m_activeSnapshotIndex < static_cast<int>(m_snapshotInfos.size());
    }

    void markSnapshotDirty() { m_snapshotDirty = true; }

    const std::string& getLastSnapshotStatusMessage() const { return m_lastSnapshotStatusMessage; }
    bool lastSnapshotStatusIsError() const { return m_lastSnapshotStatusIsError; }

    bool createSnapshot(const std::string& desiredName = "");
    bool selectSnapshotByIndex(int index);
    bool saveActiveSnapshot();
    bool deleteSnapshot(int index);
    void deleteAllSnapshots();
    void flushDirtySnapshot();

    struct TerrainEditPlacementContext {
        bool valid{false};
        glm::ivec3 chunkCoord{0, 0, 0};
        int bandLodLevel{0};
        int previewLodLevel{0};
        TerrainType terrainType{TerrainType::Voxel};
        float voxelSizeM{WorldConfig::VOXEL_SIZE_M};
    };

    TerrainEditPlacementContext getTerrainEditPlacementContext(const glm::vec3& worldPos) const;

    bool applyTerrainBoxEdit(const glm::vec3& minCorner,
                             const glm::vec3& maxCorner,
                             bool additive,
                             float requestedStep,
                             int brushShape = 0);

    /**
     * Store collision data for an edited chunk and enqueue a physics body.
     * Called by TerrainEditRemeshScheduler after re-meshing.
     */
    void enqueueEditCollision(entt::entity entity,
                              const glm::ivec3& chunkCoord,
                              std::vector<Vertex>&& packedVerts,
                              std::vector<uint32_t>&& indices32,
                              std::shared_ptr<struct ChunkVersionState> versionState = {},
                              uint32_t version = 0,
                              ChunkCollisionSource source = ChunkCollisionSource::EditMeshPacked);

    /**
     * Immediately create physics bodies for all chunks in m_editCollisionData.
     * Called after loading a snapshot's collision file.
     */
    void applyEditCollisionData();

    struct EditArtifactKey {
        glm::ivec3 chunkCoord{0, 0, 0};
        TerrainType terrainType{TerrainType::Voxel};
        int lodLevel{0};

        bool operator==(const EditArtifactKey& other) const {
            return chunkCoord == other.chunkCoord &&
                   terrainType == other.terrainType &&
                   lodLevel == other.lodLevel;
        }
    };

    struct EditArtifactKeyHash {
        size_t operator()(const EditArtifactKey& key) const noexcept {
            size_t hash = IVec3Hash{}(key.chunkCoord);
            hash ^= (static_cast<size_t>(key.lodLevel) + 0x9e3779b9u + (hash << 6) + (hash >> 2));
            hash ^= (static_cast<size_t>(key.terrainType) + 0x9e3779b9u + (hash << 6) + (hash >> 2));
            return hash;
        }
    };

    struct EditArtifact {
        TerrainType terrainType{TerrainType::Voxel};
        int lodLevel{0};
        bool isEmpty{true};
        bool deferredBuild{false};
        std::vector<Vertex> vertices;
        std::vector<uint32_t> indices;
        glm::vec3 aabbMin{1e10f};
        glm::vec3 aabbMax{-1e10f};
        uint64_t generation{0};  // bumped on every storeEditArtifact
    };

    void clearEditArtifactCache();
    void invalidateEditArtifact(const glm::ivec3& chunkCoord);
    void invalidateEditArtifacts(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& chunkCoords);
    void storeEditArtifact(const glm::ivec3& chunkCoord,
                           TerrainType terrainType,
                           int lodLevel,
                           std::vector<Vertex>&& vertices,
                           std::vector<uint32_t>&& indices,
                           glm::vec3 aabbMin,
                           glm::vec3 aabbMax,
                           bool isEmpty,
                           bool deferredBuild = false);
    bool tryGetEditArtifact(const glm::ivec3& chunkCoord,
                            TerrainType terrainType,
                            int lodLevel,
                            EditArtifact& outArtifact) const;
    uint64_t getEditArtifactGeneration(const glm::ivec3& chunkCoord,
                                       TerrainType terrainType,
                                       int lodLevel) const;
    
    /**
     * Access to terrain file loader (for heightmap-based terrain)
     */
    TerrainFileLoader* getTerrainLoader() { return m_terrainLoader.get(); }
    
    /**
     * Access to DCCM terrain file loader
     */
    TerrainFileLoader* getDCCMTerrainLoader() { return m_dccmTerrainLoader.get(); }
    
    /**
     * Get the appropriate terrain loader for a given LOD level
     * Returns DCCM or voxel loader based on per-LOD terrain type config
     */
    TerrainFileLoader* getTerrainLoaderForLOD(int lodLevel) {
        if (lodLevel >= 0 && lodLevel < MAX_LOD_LEVELS &&
            m_terrainTypePerLOD[lodLevel] == TerrainType::DCCM &&
            m_dccmTerrainLoader && m_dccmTerrainLoader->isLoaded()) {
            return m_dccmTerrainLoader.get();
        }
        return m_terrainLoader.get();
    }
    
    /**
     * Get the effective LOD level for mesh loading
     * DCCM always uses LOD 0 regardless of actual LOD
     */
    int getEffectiveLOD(int lodLevel) const {
        if (lodLevel >= 0 && lodLevel < MAX_LOD_LEVELS &&
            m_terrainTypePerLOD[lodLevel] == TerrainType::DCCM) {
            return 0; // DCCM always uses LOD 0
        }
        if (lodLevel >= 0 && lodLevel < MAX_LOD_LEVELS) {
            return m_dataLODPerBand[lodLevel];
        }
        return lodLevel;
    }
    int getEffectiveLODForChunk(const glm::ivec3& chunkCoord, int lodLevel) const {
        if (getTerrainTypeForChunk(chunkCoord, lodLevel) == TerrainType::DCCM) {
            return 0;
        }
        if (lodLevel >= 0 && lodLevel < MAX_LOD_LEVELS) {
            return m_dataLODPerBand[lodLevel];
        }
        return lodLevel;
    }
    
    /**
     * Per-LOD terrain type configuration
     */
    void setTerrainTypeForLOD(int lodLevel, TerrainType type);
    void setDataLODForBand(int band, int dataLOD);
    void setTerrainTypesForStartup(const std::array<TerrainType, MAX_LOD_LEVELS>& types);
    TerrainType getTerrainTypeForLOD(int lodLevel) const {
        if (lodLevel >= 0 && lodLevel < MAX_LOD_LEVELS) return m_terrainTypePerLOD[lodLevel];
        return TerrainType::Voxel;
    }
    TerrainType getTerrainTypeForChunk(const glm::ivec3& chunkCoord, int lodLevel) const {
        if (chunkCoord.y != 0 || m_terrainEditOverlay.hasEditsInChunk(chunkCoord)) {
            return TerrainType::Voxel;
        }
        return getTerrainTypeForLOD(lodLevel);
    }
    const std::array<TerrainType, MAX_LOD_LEVELS>& getTerrainTypePerLOD() const { return m_terrainTypePerLOD; }
    int getDataLODForBand(int band) const {
        if (band >= 0 && band < MAX_LOD_LEVELS) return m_dataLODPerBand[band];
        return band;
    }
    const std::array<int, MAX_LOD_LEVELS>& getDataLODPerBand() const { return m_dataLODPerBand; }
    
    /**
     * Returns true when chunks are loading/meshing/uploading.
     * Used to block terrain type changes that would corrupt GPU state.
     */
    bool isTerrainBusy() const {
        return m_loadingCount.load(std::memory_order_relaxed) > 0
            || m_meshingCount.load(std::memory_order_relaxed) > 0
            || m_uploadSystem.getQueueSize() > 0;
    }
    
    /**
     * Check if any LOD uses a specific terrain type
     */
    bool anyLODUsesType(TerrainType type) const {
        for (int i = 0; i < MAX_LOD_LEVELS; ++i) {
            if (m_terrainTypePerLOD[i] == type) return true;
        }
        return false;
    }
    
    /**
     * Switch to a different terrain file (e.g. voxel <-> DCCM)
     * Destroys all chunks, reloads the terrain file, and restarts generation.
     * @param newTerrainPath Full path to the new terrain .bin file
     */
    void switchTerrainFile(const std::string& newTerrainPath);
    const std::string& getBaseTerrainPath() const { return m_baseTerrainPath; }
    
    /**
     * Get terrain dimensions in chunks (from loaded terrain.bin)
     * Returns {chunksX, chunksZ, lodLevels} or {0,0,0} if not loaded
     */
    TerrainFileLoader::Dimensions getTerrainDimensions() const {
        if (m_terrainLoader && m_terrainLoader->isLoaded()) {
            return m_terrainLoader->getDimensions();
        }
        return {0, 0, 0};
    }
    
    /**
     * Get maximum render distance in rings based on terrain size
     * This is the furthest ring that can contain any terrain data
     */
    int getMaxRenderDistanceRings() const {
        auto dims = getTerrainDimensions();
        if (dims.chunksX == 0 || dims.chunksZ == 0) return 500; // Default fallback
        // Max ring = max distance from center to corner
        // For NxN grid, center is at (N/2, N/2), corner is at (N-1, N-1)
        // Max Chebyshev distance = max(N/2, N/2) = N/2 (roughly)
        int maxDim = static_cast<int>(std::max(dims.chunksX, dims.chunksZ));
        return (maxDim + 1) / 2 + 1;
    }

    /**
     * Get the number of LOD levels available in the loaded terrain
     * Returns terrain file's lodLevels (typically 5 for LOD 0-4), or 5 as default
     */
    int getAvailableLODLevels() const {
        auto dims = getTerrainDimensions();
        if (dims.lodLevels > 0) return static_cast<int>(dims.lodLevels);
        return 5; // Default: LOD 0-4
    }

    /**
     * Set physics world for collision generation
     * Call after physics system is initialized
     */
    void setPhysicsWorld(class Physics::PhysicsWorld* physics) { 
        m_physics = physics; 
        m_collisionSystem.setPhysicsWorld(physics);
        m_collisionSystem.setCollisionCache(m_collisionCache.get());
    }
    
    /**
     * Set GPU culling system for persistent slot allocation
     * Call after GPUCullingSystem is initialized
     */
    void setGPUCullingSystem(GPUCullingSystem* gpuCulling) {
        m_gpuCulling = gpuCulling;
        m_uploadSystem.setGPUCullingSystem(gpuCulling);
    }
    
    /**
     * Set culling statistics for debug display
     * Called from Engine after culling is complete
     */
    void setCullingStats(const CullingStats& stats) {
        m_cullingStats = stats;
    }

    const LastUpdateBreakdown& getLastUpdateBreakdown() const { return m_lastUpdateBreakdown; }
    int getLoadingCount() const { return m_loadingCount.load(std::memory_order_relaxed); }
    int getMeshingCount() const { return m_meshingCount.load(std::memory_order_relaxed); }
    int getReadyCount() const { return m_readyCount.load(std::memory_order_relaxed); }

    /**
     * Create a chunk entity at given chunk coordinates
     * @return Entity handle (entt::null if failed)
     */
    entt::entity createChunk(const glm::ivec3& chunkCoord);

    /**
     * OPTIMIZATION: Create multiple chunks in a single batch (1.5x speedup)
     * Reduces lock overhead by creating all entities under one registry lock
     * @param coords Vector of chunk coordinates to create
     * @return Vector of created entities (same order as coords)
     */
    std::vector<entt::entity> createChunksBatch(const std::vector<glm::ivec3>& coords);

    /**
     * Find chunk entity by coordinate
     * @return Entity handle (entt::null if not found)
     */
    entt::entity findChunk(const glm::ivec3& chunkCoord) const;
    TerrainEdit::TerrainEditOverlayStore::ChunkSet collectExistingChunksInRange(
        const glm::ivec3& minChunk,
        const glm::ivec3& maxChunk) const;

    /**
     * Get count of active chunks
     */
    size_t getChunkCount() const;

    // Direct registry access for external systems
    Registry& getRegistry() { return m_registry; }
    const Registry& getRegistry() const { return m_registry; }
    std::shared_mutex& registryMutex() const { return m_registryMutex; }

    // C-nits 8: Job system and synchronization accessors
    JobSystem& getJobSystem() { return m_jobSystem; }
    PayloadPool& getPayloadPool() { return m_payloadPool; }
    ChunkManager* getChunkManager() { return m_chunkManager.get(); }
    
    /**
     * Pre-deserialize all collision shapes for instant runtime access
     * MUST be called AFTER Jolt physics is initialized
     */
    void preDeserializeCollisionShapes();
    
    /**
     * Chunk generation control
     */
    void resetChunkGeneration();
    
    /**
     * Apply LOD parameter changes incrementally.
     * Instead of destroying all chunks and rebuilding, this:
     * 1. Compares each chunk's current LOD vs desired LOD under new thresholds
     * 2. Only destroys+recreates chunks whose LOD actually changed
     * 3. Creates new chunks for increased render distance / destroys for decreased
     * 
     * Much faster than resetChunkGeneration() for small parameter tweaks.
     * @param newRenderDistance  The new total render distance in rings
     */
    void applyLODChangesIncrementally(int newRenderDistance);
    
    /**
     * Release mesh data for all chunks at a specific LOD level
     * Used when switching LOD from Mesh to SVO mode - frees VRAM without destroying chunks
     * @param lodLevel The LOD level (0-4) to release meshes for
     */
    void releaseMeshesForLOD(int lodLevel);
    
    /**
     * Queue mesh reload for all chunks at a specific LOD level
     * Used when switching LOD from SVO back to Mesh mode
     * @param lodLevel The LOD level (0-4) to reload meshes for
     */
    void reloadMeshesForLOD(int lodLevel);
    
    std::mutex& chunkVersionMutex() const { return m_chunkVersionMutex; }
    std::unordered_map<entt::entity, std::shared_ptr<struct ChunkVersionState>, struct EntityHash>& 
        getChunkVersionStates() { return m_chunkVersionStates; }

    void transitionChunkState(entt::entity entity, ChunkState::State state);

private:
    /**
     * Chunk loading system - creates/destroys/remeshes chunks in circular area
     * @param deltaTime Frame time for speed-adaptive LOD
     */
    void updateChunkLoader(float deltaTime, const glm::vec3& cameraPos, float cameraYaw);

    /**
     * LOD transition scan + remesh drain (called from updateChunkLoader)
     */
    void updateLODTransitions(float deltaTime, bool centerChanged);

    /**
     * Mark newly generated chunks as dirty for meshing
     */
    void updateMarkDirtyOnGeneration();

    /**
     * Meshing system - kicks off mesh generation jobs for dirty chunks
     */
    void updateMeshingSystem();

    /**
     * Upload queue system - uploads finished meshes to GPU
     * @return Number of meshes uploaded this frame
     */
    size_t updateUploadQueueSystem(BufferSuballocator* vbAllocator,
                                  BufferSuballocator* ibAllocator,
                                  UploadArena* uploadArena,
                                  ResourceUploader* uploader,
                                  uint64_t uploadReadyValue,
                                  size_t maxUploadsOverride = 0,
                                  bool terrainEditOnly = false);

    /**
     * Finalize queue - processes chunks after GPU upload completes
     * @return Number of chunks finalized this frame
     */
    size_t processFinalizeQueue(size_t maxFinalizeCount = 0);
    
    /**
     * Process LOD batch swaps - atomically swap PendingMeshHandle → MeshHandle
     * for all chunks in a completed LOD transition batch.
     * This eliminates visual holes at LOD boundaries.
     * @return Number of chunks swapped this frame
     */
    size_t processLODSwaps(BufferSuballocator* vbAllocator,
                           BufferSuballocator* ibAllocator,
                           uint64_t deviceTimeline);

    /**
     * Old mesh buffers retired by LOD swaps. GPU culling slots are freed
     * immediately; buffer allocator reclamation is deferred until the upload
     * and swap path drains it.
     */
    void enqueueDeferredMeshBufferFree(const BufferSlice& vb, const BufferSlice& ib);
    void processDeferredMeshBufferFrees(BufferSuballocator* vbAllocator,
                                        BufferSuballocator* ibAllocator,
                                        size_t maxFreeCount = 0);

    /**
     * Process solo edit swaps - swap PendingMeshHandle (batchId=0) → MeshHandle
     * once deviceTimeline >= pending.handle.gpuReadyValue.
     * Used for terrain edit remeshes where the old mesh must keep rendering
     * until the GPU has finished reading the replacement upload.
     * @return Number of chunks swapped this frame
     */
    size_t processSoloPendingSwaps(BufferSuballocator* vbAllocator,
                                   BufferSuballocator* ibAllocator,
                                   uint64_t deviceTimeline);
    
    /**
     * Clean up orphaned PendingMeshHandles from cancelled LOD batches.
     * Frees GPU resources (VB, IB, culling slots) and removes the component.
     * Called when center changes and old batches are invalidated.
     */
    void cleanupStalePendingMeshHandles();
    
    // --- Pending LOD remesh queue ---
    // LOD transition detection lives in World (not ChunkManager) because we
    // need access to chunk.lodLevel (the ACTUAL mesh LOD, not the calculated one).
    // On center change: scan all Ready chunks, find mismatches, populate queue.
    // Drain it eagerly so movement can converge as quickly as the rest of the
    // pipeline allows.
    std::vector<ChunkManager::ChunkRemeshRequest> m_pendingLODRemeshes;
    size_t m_lodScanCounter = 0;

    // --- Burst recovery (explosion knockbacks, teleports) ---
    // After a large center jump (moveDist > 5 chunks), accelerate LOD scans
    // and skip upload throttling so the world fills in quickly.
    int m_burstRecoveryFrames{0};

    struct PendingMeshBufferFree {
        BufferSlice vb;
        BufferSlice ib;
    };
    std::deque<PendingMeshBufferFree> m_pendingMeshBufferFrees;
    std::vector<BufferSlice> m_deferredFreeVbScratch;
    std::vector<BufferSlice> m_deferredFreeIbScratch;

    // --- Core ECS ---
    Registry m_registry;

    // --- Chunk loading ---
    std::unique_ptr<ChunkManager> m_chunkManager;

    // --- C-nits 8: Job system and synchronization (de-globalized) ---
    JobSystem m_jobSystem;
    PayloadPool m_payloadPool;  // Thread-safe pool for ChunkPipelinePayload
    mutable std::mutex m_chunkVersionMutex;  // Protects m_chunkVersionStates
    std::unordered_map<entt::entity, std::shared_ptr<struct ChunkVersionState>, struct EntityHash> m_chunkVersionStates;

    // --- Streaming queues ---
    mutable std::mutex m_pendingChunksMutex;  // Protects m_pendingChunks (accessed from main + lifecycle threads)
    std::unordered_set<glm::ivec3, IVec3Hash> m_pendingChunks;
    std::unordered_map<glm::ivec3, entt::entity, IVec3Hash> m_chunkEntityMap;
    std::unordered_set<glm::ivec3, IVec3Hash> m_existingChunkSet;
    std::unordered_set<glm::ivec3, IVec3Hash> m_readyChunkSet;
    mutable std::shared_mutex m_chunkSetMutex;
    // --- LOD system (extracted) ---
    ChunkLODSystem m_lodSystem;
    
    // --- Lifecycle manager (extracted) ---
    ChunkLifecycleManager m_lifecycleManager;
    
    // IChunkLifecycleCallback implementation
    std::vector<entt::entity> createChunkEntities(const std::vector<glm::ivec3>& coords) override;
    void scheduleChunkJobs(entt::entity entity, const glm::ivec3& coord, const glm::ivec3& playerChunk) override;
    int destroyChunks(const std::vector<glm::ivec3>& coords) override;
    glm::vec3 getCameraPosition() const override { return m_lastCameraPos; }
    void cleanupStaleVersionStates() override;

    // --- Upload system (extracted) ---
    ChunkUploadSystem m_uploadSystem;
    
    // IUploadCallback implementation
    void onMeshUploaded(
        entt::entity entity,
        const glm::ivec3& chunkCoord,
        const std::vector<Vertex>& vertices,
        const std::vector<uint16_t>& indices,
        int lodLevel) override;
    void onUploadPipelineEvent(
        entt::entity entity,
        const glm::ivec3* chunkCoord,
        int lodLevel,
        const char* stage,
        const char* reason,
        uint32_t batchId,
        uint32_t expectedVersion,
        uint32_t actualVersion,
        const ChunkDebugAttribution* debugInfo = nullptr) override;
    void onMeshHandleAdded(const MeshHandle& h) override { meshStatsAdd(h); }
    void onMeshHandleRemoved(const MeshHandle& h) override { meshStatsSub(h); }
    
    // IBatchSignalCallback implementation
    bool onBatchChunkReady(uint32_t batchId) override;
    bool isBatchActive(uint32_t batchId) const override;

    // --- Camera tracking for directional priority ---
    glm::vec3 m_lastCameraPos{0.0f};
    float m_lastCameraYaw{0.0f};  // For minimap view cone
    glm::vec3 m_cameraVelocity{0.0f};

    // --- Collision system (extracted) ---
    ChunkCollisionSystem m_collisionSystem;
    
    // --- GPU buffer allocators (for cleanup) ---
    BufferSuballocator* m_vbAllocator = nullptr;
    BufferSuballocator* m_ibAllocator = nullptr;

    // --- Chunk state tracking for worker threads ---
    mutable std::shared_mutex m_chunkStateMutex;
    std::unordered_map<glm::ivec3, ChunkState::State, IVec3Hash> m_chunkStateMap;

    // --- Registry synchronization ---
    mutable std::shared_mutex m_registryMutex;

    // --- Helpers ---
    void setChunkState(entt::entity entity, ChunkState::State state);
    void setChunkState(const glm::ivec3& coord, ChunkState::State state);
    void removeChunkState(const glm::ivec3& coord);
    ChunkState::State getChunkStateSnapshot(const glm::ivec3& coord) const;
    void markChunkPending(const glm::ivec3& coord);
    void clearChunkPending(const glm::ivec3& coord);
    bool isChunkPending(const glm::ivec3& coord) const;
    bool tryDestroyChunk(const glm::ivec3& coord);
    int tryDestroyChunksBatch(const std::vector<glm::ivec3>& coords);
    int getDesiredLODForChunk(const glm::ivec3& coord) const;

    // --- Debug metrics assembly (implementation in WorldDebugMetrics.cpp) ---
    struct UpdateTimings {
        std::chrono::high_resolution_clock::time_point startTime;
        std::chrono::high_resolution_clock::time_point chunkLoadStart, chunkLoadEnd;
        std::chrono::high_resolution_clock::time_point meshingStart, meshingEnd;
        std::chrono::high_resolution_clock::time_point uploadStart, uploadEnd;
        std::chrono::high_resolution_clock::time_point collisionStart, collisionEnd;
        std::chrono::high_resolution_clock::time_point finalizeStart, finalizeEnd;
        std::chrono::high_resolution_clock::time_point worldUpdateEnd;
    };
    void assembleDebugInfo(const UpdateTimings& timings,
                           BufferSuballocator* vbAllocator,
                           BufferSuballocator* ibAllocator,
                           float cpuFrameMs,
                           float gpuFrameMs);

public:
    // --- Streaming metrics ---
    struct StreamingMetrics {
        std::atomic<uint64_t> chunksCreatedTotal{0};
        std::atomic<uint64_t> chunksDestroyedTotal{0};
        std::atomic<uint64_t> meshesUploaded{0};
        uint32_t creationQueueSize{0};
        uint32_t uploadQueueSize{0};
        uint32_t currentBurstSize{0};
        uint32_t currentUploadBudget{0};
        
        void reset() {
            chunksCreatedTotal.store(0, std::memory_order_relaxed);
            chunksDestroyedTotal.store(0, std::memory_order_relaxed);
            meshesUploaded.store(0, std::memory_order_relaxed);
            creationQueueSize = 0;
            uploadQueueSize = 0;
            currentBurstSize = 0;
            currentUploadBudget = 0;
        }
    };
    
    StreamingMetrics& getStreamingMetrics() { return m_streamingMetrics; }
    const StreamingMetrics& getStreamingMetrics() const { return m_streamingMetrics; }

    // Phase E1: expose passes this world contributes to (currently voxel opaque only)
    std::vector<FramePassKind> enumerateFramePasses() const;

private:
    StreamingMetrics m_streamingMetrics;
    std::unique_ptr<InGameDebug> m_inGameDebug;  // Debug overlay display
    TerrainEdit::TerrainEditOverlayStore m_terrainEditOverlay; // Sparse mutable terrain edits
    TextureOverlay::TextureOverlayStore m_textureMaterialStore; // Sparse voxel-face material edits
    TerrainEdit::TerrainFieldSource m_terrainFieldSource;      // Merged base/edit terrain view
    TerrainEdit::HeightmapBaseSampler m_heightmapSampler;      // Base heightmap for terrain edits
    TerrainEdit::VoxelBaseSampler m_voxelBaseSampler;            // 3D voxel base for terrain edits
    TerrainEdit::TerrainEditRemeshScheduler m_editRemeshScheduler; // Dirty-chunk re-mesh scheduler
    std::vector<SnapshotInfo> m_snapshotInfos;
    std::vector<TerrainBoxRecord> m_terrainBoxes;
    uint64_t m_terrainBoxRevision{1};
    int m_activeSnapshotIndex{0};
    std::string m_snapshotRootDir;
    std::string m_baseTerrainPath;
    std::string m_baseCollisionPath;
    std::unique_ptr<TerrainFileLoader> m_terrainLoader;      // Voxel terrain loader
    std::unique_ptr<TerrainFileLoader> m_dccmTerrainLoader;  // DCCM terrain loader
    std::array<TerrainType, MAX_LOD_LEVELS> m_terrainTypePerLOD{TerrainType::Voxel, TerrainType::Voxel, TerrainType::Voxel, TerrainType::Voxel, TerrainType::Voxel};
    std::array<int, MAX_LOD_LEVELS> m_dataLODPerBand{0, 1, 2, 3, 4};  // Per-band data LOD override (Voxel only)
    std::unique_ptr<Collision::CollisionCache> m_collisionCache;  // Precomputed collision data

    bool m_snapshotDirty{false}; // Deferred save flag — set by edits, flushed on switch/save
    std::string m_lastSnapshotStatusMessage;
    bool m_lastSnapshotStatusIsError{false};
    TerrainEditDiag m_lastEditDiag; // Last terrain edit timing diagnostics
    TerrainEditStats m_editStats;   // Rolling history + averages
    TerrainEditHistory m_editHistory; // Individual edit log entries
    ChunkVisualHistory m_chunkVisualHistory; // End-to-end chunk upload/finalize history
    ChunkVisualErrorHistory m_chunkVisualErrorHistory; // Upload/LOD/finalize error timeline

    // Persisted edit collision map (saved/loaded with each snapshot)
    std::unordered_map<glm::ivec3, EditCollisionEntry, IVec3Hash> m_editCollisionData;
    mutable std::shared_mutex m_editArtifactCacheMutex;
    std::unordered_map<EditArtifactKey, EditArtifact, EditArtifactKeyHash> m_editArtifactCache;
    uint64_t m_editArtifactGenCounter{0};  // monotonic, bumped per storeEditArtifact
    class Physics::PhysicsWorld* m_physics{nullptr};     // Physics world for collision (non-owning)
    GPUCullingSystem* m_gpuCulling{nullptr};             // GPU culling system for persistent slots (non-owning)
    CullingStats m_cullingStats;                          // Culling statistics from Engine
    LastUpdateBreakdown m_lastUpdateBreakdown;            // Always-on timings for perf HUD / runtime metrics
    
    // Runtime voxel chunk tracking
    mutable std::shared_mutex m_runtimeVoxelChunkMutex;
    TerrainEdit::TerrainEditOverlayStore::ChunkSet m_runtimeVoxelChunks;
    std::atomic<uint64_t> m_meshTopologyVersion{0};  // Bumped when mesh topology changes
    mutable std::mutex m_meshTopologyChangeMutex;
    std::deque<MeshTopologyChange> m_meshTopologyChanges;
    uint64_t m_meshTopologyOldestDroppedRevision{0};
    ChunkHoleTracker m_chunkHoleTracker;              // Tracks chunk visual hole events
    
    // --- Render system (extracted) ---
    ChunkRenderSystem m_renderSystem;
    
    // World metadata
    std::string m_worldName;
    std::string m_worldGenerationDate;
    
    // --- Atomic state counters for O(1) tracking instead of O(N) iteration ---
    std::atomic<int> m_loadingCount{0};
    std::atomic<int> m_meshingCount{0};
    std::atomic<int> m_readyCount{0};
    
    // Track whether any uploads/copies were recorded this frame (for skipping empty submits)
    bool m_hadUploadsThisFrame{false};
    // Track whether any of those uploads were topology-changing replacements.
    // Initial chunk loads and LOD batch swaps do NOT set this. Used to suppress
    // temporal visibility reuse after frames that may have exposed previously
    // occluded neighbor chunks, breaking the temporal occlusion death spiral on edits.
    bool m_hadEditUploadsThisFrame{false};
    // Cooldown frames remaining after the last edit/remesh upload. While > 0, the
    // temporal-coherence shortcut is suppressed so chunks re-run Hi-Z depth tests
    // instead of reusing potentially stale temporal visibility bits.
    uint32_t m_hiZEditCooldown{0};
    uint64_t m_nextTerrainEditId{1};

    struct PendingEditVisualChunk {
        uint64_t editId{0};
        std::chrono::steady_clock::time_point startTime{};
    };
    struct PendingEditVisualAggregate {
        std::chrono::steady_clock::time_point startTime{};
        uint32_t totalChunks{0};
        uint32_t readyChunks{0};
        uint32_t supersededChunks{0};
        float visualFirstChunkMs{0.0f};
        float visualCompleteMs{0.0f};
        uint64_t uploadBytes{0};
        uint32_t artifactBuilds{0};
        uint32_t artifactCacheHits{0};
        uint32_t precomputedLoads{0};
        uint32_t collisionBaseCache{0};
        uint32_t collisionEditPacked{0};
        uint32_t collisionArtifactRefresh{0};
        uint32_t collisionExistingEdit{0};
        uint32_t gpuResidentChunks{0};
        uint32_t artifactResidentChunks{0};
        uint32_t monolithicChunks{0};
        uint32_t pagedChunks{0};
        uint32_t dirtyPages{0};
        uint32_t rebuiltPages{0};
        uint32_t residentPages{0};
        uint32_t evictedPages{0};
    };
    std::unordered_map<glm::ivec3, PendingEditVisualChunk, IVec3Hash> m_pendingEditVisualChunks;
    std::unordered_map<uint64_t, PendingEditVisualAggregate> m_pendingEditVisuals;
    std::unordered_map<glm::ivec3, ChunkCollisionSource, IVec3Hash> m_chunkCollisionSources;

    // Edit→Load correlation tracker: counts consecutive [Load] entries per chunk
    // since the last [Edit], propagating the editId to subsequent [Load] entries.
    struct ChunkLoadTracker {
        uint64_t editId{0};
        uint32_t loadCount{0};
    };
    std::unordered_map<glm::ivec3, ChunkLoadTracker, IVec3Hash> m_chunkLoadTrackers;

    // --- Finalize diagnostics ring buffer ---
    std::vector<FinalizeDiagFrame> m_finalizeDiagHistory;
    size_t m_finalizeDiagWriteIdx{0};
    uint64_t m_finalizeDiagFrameCounter{0};
    FinalizeDiagFrame m_currentFinalizeDiag;  // populated by processFinalizeQueue + processLODSwaps each frame
    LODSwitchDiag m_lodSwitchDiag;

    // Running subchunk stats (D3: avoid periodic full-entity scan)
    std::atomic<uint32_t> m_statsChunksWithMesh{0};
    std::atomic<uint32_t> m_statsTotalSubChunks{0};
    std::atomic<uint32_t> m_statsSplitChunks{0};
    std::atomic<uint32_t> m_statsSeamSubChunks{0};
    void meshStatsAdd(const MeshHandle& h);
    void meshStatsSub(const MeshHandle& h);
    void refreshSnapshots();
    void updateWorldIdentityFromActiveSnapshot();
    bool ensureEditableSnapshot();
    void beginTerrainEditVisualTracking(
        uint64_t editId,
        const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks,
        const std::chrono::steady_clock::time_point& startTime);
    void noteChunkVisualReady(
        const glm::ivec3& coord,
        const std::chrono::steady_clock::time_point& uploadEnqueueTime,
        const std::chrono::steady_clock::time_point& finalizeTime,
        int lodLevel,
        uint64_t vramBytes,
        uint32_t vertexCount,
        uint32_t indexCount,
        const ChunkDebugAttribution* debugInfo = nullptr);
    void noteChunkVisualError(
        const glm::ivec3* coord,
        int lodLevel,
        const char* stage,
        const char* reason,
        uint32_t batchId = 0,
        uint32_t expectedVersion = 0,
        uint32_t actualVersion = 0,
        const ChunkDebugAttribution* debugInfo = nullptr);
    void recordMeshTopologyChange(const glm::ivec3& coord);
    void recordMeshTopologyChanges(const std::vector<glm::ivec3>& coords);
    void recordGlobalMeshTopologyChange();

    // --- Ghost-geometry detector ---
    // Periodic scan that flags LOD-0 chunks with valid render meshes but no
    // valid physics collider. The actual culprit (edit-collision drop, async
    // BVH null, stale-version drop, missing collision cache, etc.) is logged
    // into the chunk visual error history so VRAM panel inspection can show
    // the cause directly per chunk.
    void scanForGhostGeometry();
    struct GhostGeometryState {
        uint32_t consecutiveBrokenScans{0};
        uint64_t lastReportedScanIndex{0};
    };
    std::unordered_map<glm::ivec3, GhostGeometryState, IVec3Hash> m_ghostGeometryState;
    int m_ghostScanFrameCounter{0};
    uint64_t m_ghostScanIndex{0};
    void refreshEditedChunkCollisionFromArtifact(
        entt::entity entity,
        const glm::ivec3& chunkCoord,
        int lodLevel);
    TerrainEditHistoryEntry* findTerrainEditHistoryEntry(uint64_t editId);
    void syncTerrainEditVisualState(uint64_t editId, bool eraseIfComplete);
    
public:
    // VRAM budget control (delegated to ChunkRenderSystem)
    void setVramBudgetMB(uint32_t megabytes) { 
        m_renderSystem.setVramBudgetMB(megabytes);
    }
    uint64_t getVramBudgetBytes() const { 
        return m_renderSystem.getVramBudgetBytes();
    }
    uint64_t getCurrentVramUsage() const { 
        return m_renderSystem.getCurrentVramUsage();
    }
    void setEnableVramLimiting(bool enable) { 
        m_renderSystem.setEnableVramLimiting(enable);
    }
    bool isVramLimitingEnabled() const { 
        return m_renderSystem.isVramLimitingEnabled();
    }
    
    // Query whether the most recent update() recorded any GPU copies
    bool hadUploadsThisFrame() const { return m_hadUploadsThisFrame; }
    // Query whether the most recent update() recorded any topology-changing
    // replacement uploads (i.e., uploads that may have exposed previously
    // occluded neighbor chunks).
    // Returns true during edit upload frames AND for several frames afterward,
    // suppressing temporal-visibility reuse while fresh depth settles.
    bool hadEditUploadsThisFrame() const { return m_hadEditUploadsThisFrame || (m_hiZEditCooldown > 0); }
    
    // Per-chunk visibility control
    void setChunkVisible(entt::entity entity, bool visible) {
        if (m_registry.valid(entity) && m_registry.all_of<Chunk>(entity)) {
            m_registry.get<Chunk>(entity).isVisible = visible;
        }
    }
};

````

## src\CMakeLists.txt

Description: No CC-DESC found.

````cmake
# GPT-DESC: Defines VulkanVX source targets and shader build helpers.
cmake_minimum_required(VERSION 3.16)

# Core engine files (minimal - just app lifecycle)
set(CORE_SOURCES
    # core/engine/ - Engine facade + domain-owned implementation files
    core/engine/Engine.cpp
    core/engine/lifecycle/EngineLifecycle.cpp
    core/engine/lifecycle/EngineCleanup.cpp
    core/engine/window/EngineWindow.cpp
    core/engine/window/EngineWindowControls.cpp
    core/engine/window/EngineGameplayWindow.cpp
    core/engine/diagnostics/EnginePerfDiagnostics.cpp
    core/engine/diagnostics/EngineGModeDiagnostics.cpp
    core/engine/diagnostics/EngineGModeControls.cpp
    core/engine/diagnostics/EngineTimestamps.cpp
    core/engine/init/EngineVulkanInit.cpp
    core/engine/init/EngineSubsystemInit.cpp
    core/engine/init/EngineMaterialOverlay.cpp
    core/engine/init/EngineRenderResources.cpp
    core/engine/init/EngineDebugWiring.cpp
    core/engine/init/EngineShaderHotReload.cpp
    core/engine/init/EngineSettingsPersistence.cpp
    core/engine/rendering/EngineRenderLoop.cpp
    core/engine/rendering/EngineCommandBuffer.cpp
    core/engine/rendering/EngineDepthPrePass.cpp
    core/engine/rendering/EngineShadowPass.cpp
    core/engine/rendering/EngineGameplayRendering.cpp
    core/engine/rendering/EngineSwapchainLifecycle.cpp
    # core/ - standalone core components
    core/CodeRebuildService.cpp
    core/EngineImGui.cpp
    core/GameplayWindow.cpp
    core/Jobs.cpp
    core/TimeManager.cpp
    core/WindowIcon.cpp
)

# Vulkan subsystem (GPU/rendering infrastructure)
set(VULKAN_SOURCES
    vulkan/VulkanContext.cpp
    vulkan/Buffers.cpp
    vulkan/Swapchain.cpp
    vulkan/Pipeline.cpp
    vulkan/Sync.cpp
    vulkan/FrameGraph.cpp
    vulkan/UploadArena.cpp
    vulkan/BufferSuballocator.cpp
)

# Input subsystem
set(INPUT_SOURCES
    input/InputManager.cpp
    input/CameraController.cpp
)

# Physics subsystem
set(PHYSICS_SOURCES
    physics/PhysicsWorld.cpp
)

# Player subsystem
set(PLAYER_SOURCES
    player/PlayerController.cpp
    player/PlayerCamera.cpp
)

# Rendering subsystem (visual systems)
set(RENDERING_SOURCES
    # rendering/common/ - shared rendering utilities
    rendering/common/Renderer.cpp
    rendering/common/Mesh.cpp
    rendering/common/VulkanHelpers.cpp
    rendering/common/ParallelCommandRecorder.cpp
    # rendering/hotreload/ - shader compilation & hot-reload
    rendering/hotreload/ShaderCompiler.cpp
    rendering/hotreload/ShaderHotReloadService.cpp
    # rendering/postprocess/
    rendering/postprocess/RetroPixelPassSystem.cpp
    # rendering/tjunctionfix/
    rendering/tjunctionfix/TJunctionFixSystem.cpp
    # rendering/culling/
    rendering/culling/GPUCullingSystem.cpp
    rendering/culling/GPUCullingSlots.cpp
    rendering/culling/GPUCullingReadback.cpp
    rendering/culling/HiZPyramid.cpp
    rendering/culling/HiZPyramidResources.cpp
    rendering/culling/HiZPyramidDiagnostics.cpp
    # rendering/lighting/
    rendering/lighting/LightingSettings.cpp
    rendering/lighting/LightGlowSystem.cpp
    rendering/lighting/ShadowSystem.cpp
    rendering/lighting/ShadowSystemUpdate.cpp
    rendering/lighting/ShadowSystemResources.cpp
    rendering/lighting/shadow/ShadowPass.cpp
    rendering/lighting/shadow/ShadowSunCascades.cpp
    rendering/lighting/shadow/sun/ShadowSunGather.cpp
    rendering/lighting/shadow/sun/ShadowSunScroll.cpp
    rendering/lighting/shadow/sun/ShadowSunRender.cpp
    rendering/lighting/shadow/sun/ShadowSunDiagnostics.cpp
    rendering/lighting/shadow/ShadowPointLights.cpp
    rendering/lighting/shadow/ShadowCache.cpp
    rendering/lighting/shadow/ShadowMatrices.cpp
    rendering/lighting/ShadowDiagnostics.cpp
    rendering/lighting/ClusteredLightingSystem.cpp
    # rendering/sky/
    rendering/sky/CloudSystem.cpp
    rendering/sky/CloudWeather.cpp
    rendering/sky/CloudFormation.cpp
    rendering/sky/CloudAnimation.cpp
    rendering/sky/CelestialSystem.cpp
    rendering/sky/StarSystem.cpp
    rendering/sky/SkySystem.cpp
)

# World subsystem (chunk management, streaming, world state, config)
set(WORLD_SOURCES
    world/World.cpp
    world/WorldUpdate.cpp
    world/WorldUpdateLODScan.cpp
    world/WorldUpdateMeshing.cpp
    world/WorldUpdateFinalize.cpp
    world/WorldRendering.cpp
    world/WorldDebugMetrics.cpp
    world/WorldChunkCRUD.cpp
    world/WorldLODTransitions.cpp
    world/WorldSnapshots.cpp
    world/WorldTerrainEditCollision.cpp
    world/config/WorldConfig.cpp
    world/TerrainFileLoader.cpp
    world/edit/overlay/TerrainEditOverlayStore.cpp
    world/edit/overlay/TerrainEditOverlayBrush.cpp
    world/edit/overlay/TerrainEditOverlayDeferredFill.cpp
    world/edit/overlay/TerrainEditOverlayQuery.cpp
    world/edit/overlay/TerrainEditOverlaySolidCache.cpp
    world/edit/TerrainEditOverlayStore_IO.cpp
    world/edit/texture/TextureOverlayStore.cpp
    world/edit/texture/TextureOverlayCells.cpp
    world/edit/texture/TextureOverlayPaint.cpp
    world/edit/texture/TextureOverlayStamps.cpp
    world/edit/texture/TextureOverlayGPU.cpp
    world/edit/texture/TextureOverlayIO.cpp
    world/edit/texture/TextureBrushStyles.cpp
    world/edit/TerrainFieldSource.cpp
    world/edit/HeightmapBaseSampler.cpp
    world/edit/VoxelBaseSampler.cpp
    world/edit/meshing/TerrainEditMesher.cpp
    world/edit/meshing/TerrainEditSolidCache.cpp
    world/edit/meshing/TerrainEditMaterialResolve.cpp
    world/edit/meshing/TerrainEditSubMeshSplit.cpp
    world/edit/meshing/greedy/TerrainEditGreedyMesh.cpp
    world/edit/meshing/greedy/TerrainEditGreedyCache.cpp
    world/edit/meshing/greedy/TerrainEditGreedyRegions.cpp
    world/edit/TerrainEditDCCMMesher.cpp
    world/edit/remesh/RemeshScheduler.cpp
    world/edit/remesh/RemeshSchedulerQueue.cpp
    world/edit/remesh/RemeshSchedulerJobs.cpp
    world/edit/remesh/RemeshSchedulerArtifacts.cpp
    world/edit/remesh/RemeshSchedulerPagedRuntime.cpp
    world/vxm/VxmImport.cpp
    # chunks/core/ - universal chunk infrastructure
    world/chunks/core/ChunkManager.cpp
    world/chunks/core/ChunkManagerRings.cpp
    world/chunks/core/ChunkManagerBatches.cpp
    world/chunks/core/ChunkJobs.cpp
    world/chunks/core/ChunkLifecycleManager.cpp
    world/chunks/core/ChunkLODSystem.cpp
    # chunks/streaming/ - GPU resource management
    world/chunks/streaming/ChunkRenderSystem.cpp
    world/chunks/streaming/ChunkUploadSystem.cpp
    # chunks/physics/ - collision subsystem
    world/chunks/physics/ChunkCollisionSystem.cpp
    world/chunks/physics/CollisionCache.cpp
    world/ChunkHoleTracker.cpp
)

# SVO (Sparse Voxel Octree) subsystem
set(SVO_SOURCES
    svo/SparseVoxelOctree.cpp
    svo/SVOBuilder.cpp
    svo/SVOSerializer.cpp
)

# UI subsystem
set(UI_SOURCES
    ui/InGameDebug.cpp
    ui/EngineInterface.cpp
    ui/EngineInterfaceGameplay.cpp
    ui/EngineInterfaceLayout.cpp
    # ui/cursor/ - OS cursor image loading and hotspot configuration
    ui/cursor/CursorManager.cpp
    # ui/debug_menu/ - shared debug UI infrastructure
    ui/debug_menu/IconManagerForDebug.cpp
    # debug_menu/profiling/ - performance & diagnostic windows
    ui/debug_menu/profiling/DebugControlPanel.cpp
    ui/debug_menu/profiling/FPSProfilerWindow.cpp
    ui/debug_menu/profiling/TerminalOutputWindow.cpp
    ui/debug_menu/profiling/WorkerThreadsWindow.cpp
    # debug_menu/world/ - chunk, terrain & object debug windows
    ui/debug_menu/world/ChunkDebugWindow.cpp
    ui/debug_menu/world/ChunkMinimapWindow.cpp
    ui/debug_menu/world/ChunkMinimapCullingOverlay.cpp
    ui/debug_menu/world/ChunkVramWindow.cpp
    ui/debug_menu/world/ChunkVramWindow_Text.cpp
    ui/debug_menu/world/MinimapCullingReadback.cpp
    ui/debug_menu/world/ObjectManagerWindow.cpp
    ui/debug_menu/world/TerrainEditTool.cpp
    ui/debug_menu/world/texture_paint/TexturePaintTool.cpp
    ui/debug_menu/world/texture_paint/TexturePaintToolUI.cpp
    ui/debug_menu/world/texture_paint/TexturePaintToolPreview.cpp
    ui/debug_menu/world/texture_paint/TexturePaintToolExecution.cpp
    ui/debug_menu/world/texture_paint/TexturePaintToolDiagnostics.cpp
    # debug_menu/rendering/ - render settings & visual debug windows
    ui/debug_menu/rendering/RenderSettingsWindow.cpp
    ui/debug_menu/rendering/HiZDebugWindow.cpp
    ui/debug_menu/rendering/AOSettingsWindow.cpp
    ui/debug_menu/rendering/DCCMAOSettingsWindow.cpp
    ui/debug_menu/rendering/LightingSettingsWindow.cpp
    ui/debug_menu/rendering/CloudDebugWindow.cpp
    ui/debug_menu/rendering/PixelPassWindow.cpp
    ui/debug_menu/rendering/ShaderHotReloadWindow.cpp
    ui/debug_menu/rendering/DirectionalShadowWindow.cpp
    ui/debug_menu/rendering/SkyEnclosureWindow.cpp
    # ui/widgets/ - reusable UI widgets
    ui/widgets/CompassSphereWidget.cpp
    # debug_menu/gameplay/ - player-facing tools & info
    ui/debug_menu/gameplay/CursorPlaceTool.cpp
    ui/debug_menu/gameplay/CursorSettingsWindow.cpp
    ui/debug_menu/gameplay/ControlsWindow.cpp
    ui/debug_menu/gameplay/TimeManagerWindow.cpp
    # ui/style/ - centralized theme & animation
    ui/style/EngineTheme.cpp
    ui/style/UIAnimator.cpp
)

add_executable(VulkanVX
    main.cpp
    ${CORE_SOURCES}
    ${VULKAN_SOURCES}
    ${INPUT_SOURCES}
    ${PHYSICS_SOURCES}
    ${PLAYER_SOURCES}
    ${RENDERING_SOURCES}
    ${WORLD_SOURCES}
    ${SVO_SOURCES}
    ${UI_SOURCES}
    $<$<PLATFORM_ID:Windows>:${CMAKE_CURRENT_BINARY_DIR}/VulkanVX.rc>
)

if(WIN32)
    # Generate the .rc with an absolute icon path so the resource compiler can
    # locate it regardless of build directory layout.
    set(VULKANVX_ICON_PATH "${CMAKE_SOURCE_DIR}/assets/img/vulkanvx.ico")
    # RC needs forward slashes or escaped backslashes.
    string(REPLACE "\\" "/" VULKANVX_ICON_PATH "${VULKANVX_ICON_PATH}")
    file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/VulkanVX.rc"
        "1 ICON \"${VULKANVX_ICON_PATH}\"\n")

    # Legacy cleanup: older builds produced `vulkan-engine.exe`.
    # Remove it after linking so only VulkanVX.exe remains in Release/Debug.
    add_custom_command(TARGET VulkanVX POST_BUILD
        COMMAND powershell -NoProfile -ExecutionPolicy Bypass -Command
            "if (Test-Path '$<TARGET_FILE_DIR:VulkanVX>/vulkan-engine.exe') { Remove-Item -Force -ErrorAction SilentlyContinue '$<TARGET_FILE_DIR:VulkanVX>/vulkan-engine.exe' }; exit 0"
        COMMENT "Removing legacy vulkan-engine.exe (best effort)"
    )
endif()

target_include_directories(VulkanVX PRIVATE 
    ${CMAKE_SOURCE_DIR}/include
    ${CMAKE_SOURCE_DIR}/include/core
    ${CMAKE_SOURCE_DIR}/include/core/engine
    ${CMAKE_SOURCE_DIR}/include/vulkan
    ${CMAKE_SOURCE_DIR}/include/input
    ${CMAKE_SOURCE_DIR}/include/rendering
    ${CMAKE_SOURCE_DIR}/include/rendering/common
    ${CMAKE_SOURCE_DIR}/include/rendering/hotreload
    ${CMAKE_SOURCE_DIR}/include/rendering/lighting
    ${CMAKE_SOURCE_DIR}/include/rendering/postprocess
    ${CMAKE_SOURCE_DIR}/include/rendering/sky
    ${CMAKE_SOURCE_DIR}/include/rendering/svo
    ${CMAKE_SOURCE_DIR}/include/rendering/culling
    ${CMAKE_SOURCE_DIR}/include/rendering/tjunctionfix
    ${CMAKE_SOURCE_DIR}/include/world
    ${CMAKE_SOURCE_DIR}/include/world/config
    ${CMAKE_SOURCE_DIR}/include/world/chunks/core
    ${CMAKE_SOURCE_DIR}/include/world/chunks/streaming
    ${CMAKE_SOURCE_DIR}/include/world/chunks/physics
    ${CMAKE_SOURCE_DIR}/include/svo
    ${CMAKE_SOURCE_DIR}/include/ui
    ${CMAKE_SOURCE_DIR}/include/ui/debug_menu
    ${CMAKE_SOURCE_DIR}/include/ui/debug_menu/base
    ${CMAKE_SOURCE_DIR}/include/ui/debug_menu/world
    ${CMAKE_SOURCE_DIR}/include/ui/debug_menu/rendering
    ${CMAKE_SOURCE_DIR}/include/ui/debug_menu/gameplay
    ${CMAKE_SOURCE_DIR}/include/ui/debug_menu/profiling
    ${CMAKE_SOURCE_DIR}/include/ui/style
    ${CMAKE_SOURCE_DIR}/include/physics
    ${CMAKE_SOURCE_DIR}/include/player
)

target_compile_definitions(VulkanVX PRIVATE GLFW_INCLUDE_VULKAN)

# Find and link EnTT
find_package(EnTT CONFIG REQUIRED)

# Find and link ImGui
find_package(imgui CONFIG REQUIRED)

# Find and link libpng for custom cursor PNG loading
find_package(PNG REQUIRED)

# Find and link Jolt Physics
find_package(Jolt CONFIG REQUIRED)

target_link_libraries(VulkanVX PRIVATE Vulkan::Vulkan glfw glm::glm EnTT::EnTT imgui::imgui PNG::PNG Jolt::Jolt)

# Precompiled header — CMake injects /FI so no source files need modification.
# All heavy external headers (vulkan, entt, glm, STL) are parsed once into a
# .pch file and reused by all 116 translation units.
target_precompile_headers(VulkanVX PRIVATE ${CMAKE_SOURCE_DIR}/include/pch.h)

# MSVC: /MP enables within-project parallel compilation (one cl.exe thread per
# logical core). Without this, cl.exe compiles 116 files sequentially even when
# cmake -j is passed — because -j only parallelizes across .vcxproj projects.
if(MSVC)
    target_compile_options(VulkanVX PRIVATE /MP)
    # Incremental linking for fast developer builds.
    # /INCREMENTAL overrides CMake's default /INCREMENTAL:NO (last flag wins in MSVC).
    # /OPT:NOREF,NOICF are required companions when /INCREMENTAL is used.
    target_link_options(VulkanVX PRIVATE
        $<$<CONFIG:Release>:/INCREMENTAL /OPT:NOREF /OPT:NOICF>
    )
endif()

# Increase stack size to 8 MB — Engine object (containing World, Physics, GPU culling,
# rendering subsystems) exceeds the default 1 MB Windows stack.
if(MSVC)
    target_link_options(VulkanVX PRIVATE /STACK:8388608)
endif()

# Shader compile step: try to find glslc; if not available, user must provide precompiled SPV
find_program(GLSLC_EXECUTABLE glslc)
if(GLSLC_EXECUTABLE)
    message(STATUS "Found glslc: ${GLSLC_EXECUTABLE}")
    set(SHADER_DIR ${CMAKE_SOURCE_DIR}/shaders)
    
    # Create shader output directories
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/sky)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/terrain)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/lighting)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/shadow)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/svo)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/culling)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/pixelpass)
    file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/shaders/tjunctionfix)
    
    # Collect shaders from subdirectories
    file(GLOB SKY_SHADERS "${SHADER_DIR}/sky/*.vert" "${SHADER_DIR}/sky/*.frag")
    file(GLOB TERRAIN_SHADERS "${SHADER_DIR}/terrain/*.vert" "${SHADER_DIR}/terrain/*.frag" "${SHADER_DIR}/terrain/*.comp")
    file(GLOB LIGHTING_SHADERS "${SHADER_DIR}/lighting/*.vert" "${SHADER_DIR}/lighting/*.frag")
    file(GLOB SHADOW_SHADERS "${SHADER_DIR}/shadow/*.vert" "${SHADER_DIR}/shadow/*.frag" "${SHADER_DIR}/shadow/*.comp")
    file(GLOB SVO_SHADERS "${SHADER_DIR}/svo/*.comp")
    file(GLOB CULLING_SHADERS CONFIGURE_DEPENDS "${SHADER_DIR}/culling/*.comp")
    file(GLOB PIXELPASS_SHADERS "${SHADER_DIR}/pixelpass/*.vert" "${SHADER_DIR}/pixelpass/*.frag")
    file(GLOB TJUNCTIONFIX_SHADERS "${SHADER_DIR}/tjunctionfix/*.vert" "${SHADER_DIR}/tjunctionfix/*.frag")
    file(GLOB ROOT_COMPUTE_SHADERS "${SHADER_DIR}/*.comp")
    
    # Common shader includes (not compiled directly, but tracked as dependencies)
    file(GLOB COMMON_SHADER_INCLUDES "${SHADER_DIR}/common/*.glsl")
    
    # Compile sky shaders
    foreach(GLSL ${SKY_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/sky/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    # Compile terrain shaders (depend on common includes for #include resolution)
    foreach(GLSL ${TERRAIN_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/terrain/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL} ${COMMON_SHADER_INCLUDES}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    # Compile lighting shaders
    foreach(GLSL ${LIGHTING_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/lighting/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()

    # Compile shadow shaders
    foreach(GLSL ${SHADOW_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/shadow/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    # Compile svo shaders (compute)
    foreach(GLSL ${SVO_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/svo/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    # Compile culling shaders
    foreach(GLSL ${CULLING_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/culling/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()

    # Compile pixel pass shaders
    foreach(GLSL ${PIXELPASS_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/pixelpass/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    # Compile tjunctionfix shaders
    foreach(GLSL ${TJUNCTIONFIX_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/shaders/tjunctionfix/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    # Compile root compute shaders (voxel_mesh.comp, etc.)
    foreach(GLSL ${ROOT_COMPUTE_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        set(SPV ${CMAKE_BINARY_DIR}/${FNAME}.spv)
        add_custom_command(
            OUTPUT ${SPV}
            COMMAND ${GLSLC_EXECUTABLE} -o ${SPV} ${GLSL}
            DEPENDS ${GLSL}
        )
        list(APPEND SPV_OUTPUTS ${SPV})
    endforeach()
    
    add_custom_target(shaders ALL DEPENDS ${SPV_OUTPUTS})
    add_dependencies(VulkanVX shaders)
    
    # Copy runtime DLLs next to the executable.
    # Use copy_if_different (not copy_directory) so incremental builds don't
    # try to overwrite in-use DLLs when VulkanVX is already running.
    file(GLOB VCPKG_RUNTIME_DLLS
        "${CMAKE_SOURCE_DIR}/vcpkg/installed/x64-windows/bin/*.dll")
    add_custom_command(TARGET VulkanVX POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            ${VCPKG_RUNTIME_DLLS}
            $<TARGET_FILE_DIR:VulkanVX>
        COMMAND_EXPAND_LISTS
        COMMENT "Copying vcpkg runtime DLLs"
    )

    # Copy shaders to exe directory maintaining folder structure
    add_custom_command(TARGET VulkanVX POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/sky
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/terrain
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/lighting
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/shadow
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/svo
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/culling
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/pixelpass
        COMMAND ${CMAKE_COMMAND} -E make_directory $<TARGET_FILE_DIR:VulkanVX>/shaders/tjunctionfix
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/sky
            $<TARGET_FILE_DIR:VulkanVX>/shaders/sky
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/terrain
            $<TARGET_FILE_DIR:VulkanVX>/shaders/terrain
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/lighting
            $<TARGET_FILE_DIR:VulkanVX>/shaders/lighting
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/shadow
            $<TARGET_FILE_DIR:VulkanVX>/shaders/shadow
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/svo
            $<TARGET_FILE_DIR:VulkanVX>/shaders/svo
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/culling
            $<TARGET_FILE_DIR:VulkanVX>/shaders/culling
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/pixelpass
            $<TARGET_FILE_DIR:VulkanVX>/shaders/pixelpass
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            ${CMAKE_BINARY_DIR}/shaders/tjunctionfix
            $<TARGET_FILE_DIR:VulkanVX>/shaders/tjunctionfix
        COMMENT "Copying shaders to executable directory"
    )
    
    # Copy root-level compiled shaders (voxel_mesh.comp.spv, etc.)
    foreach(GLSL ${ROOT_COMPUTE_SHADERS})
        get_filename_component(FNAME ${GLSL} NAME)
        add_custom_command(TARGET VulkanVX POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
                ${CMAKE_BINARY_DIR}/${FNAME}.spv
                $<TARGET_FILE_DIR:VulkanVX>/${FNAME}.spv
            COMMENT "Copying ${FNAME}.spv to executable directory"
        )
    endforeach()
else()
    message(WARNING "glslc not found: shader compilation disabled. Provide precompiled .spv in build/shaders or install glslc.")
endif()

````
