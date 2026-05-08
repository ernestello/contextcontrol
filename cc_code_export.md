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


## src\world\WorldUpdate.cpp

Description: No CC-DESC found.

````cpp
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include "rendering/common/VulkanHelpers.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <iostream>
#include <algorithm>
#include <cmath>
#include <chrono>

// update(), updateChunkLoader(), updateMarkDirtyOnGeneration()
// See also: WorldUpdateLODScan.cpp, WorldUpdateMeshing.cpp, WorldUpdateFinalize.cpp

void World::update(float deltaTime, const glm::vec3& cameraPos,
                   float cameraYaw,
                   BufferSuballocator* vbAllocator,
                   BufferSuballocator* ibAllocator,
                   UploadArena* uploadArena,
                   ResourceUploader* uploader,
                   uint64_t uploadReadyValue,
                   float cpuFrameMs,
                   float gpuFrameMs,
                   uint64_t deviceTimeline) {
    // Timing for CPU breakdown display
    auto startTime = std::chrono::high_resolution_clock::now();

    // System 1: Chunk loader (create/destroy/remesh chunks in circular area)
    auto chunkLoadStart = std::chrono::high_resolution_clock::now();
    updateChunkLoader(deltaTime, cameraPos, cameraYaw);
    auto chunkLoadEnd = std::chrono::high_resolution_clock::now();

    // System 2: Mark newly generated chunks as dirty
    updateMarkDirtyOnGeneration();

    // System 3: Meshing system (kick off jobs for dirty chunks)
    auto meshingStart = std::chrono::high_resolution_clock::now();
    updateMeshingSystem();
    auto meshingEnd = std::chrono::high_resolution_clock::now();

    // System 3b: Terrain edit re-mesh (greedy-mesh dirty edited chunks)
    m_editRemeshScheduler.processRemeshQueue(this, /*budget=*/0);

    // System 4: Upload queue (upload finished meshes to GPU)
    auto uploadStart = std::chrono::high_resolution_clock::now();
    m_hadUploadsThisFrame = false;
    m_hadEditUploadsThisFrame = false;
    // Tick down the post-edit Hi-Z cooldown so temporal-visibility reuse stays
    // suppressed for a few frames after each edit upload, not just upload frame.
    if (m_hiZEditCooldown > 0) { --m_hiZEditCooldown; }
    if (vbAllocator && ibAllocator && uploadArena && uploader) {
        // Smooth bulk streaming uploads instead of draining every finished mesh
        // in one frame. The previous unlimited path could feed finalize with
        // 200-350 chunks per frame, adding CPU time even when the camera was
        // stable. Keep burst recovery high after teleports, but use a bounded
        // steady-state budget so looking at a small area does not inherit the
        // full streaming backlog cost. Edit/late uploads below remain immediate.
        constexpr size_t kSteadyStreamingUploadBudget = 96;
        constexpr size_t kBurstStreamingUploadBudget = 256;
        const size_t streamingUploadBudget =
            (m_burstRecoveryFrames > 0)
                ? kBurstStreamingUploadBudget
                : kSteadyStreamingUploadBudget;

        size_t uploaded = updateUploadQueueSystem(
            vbAllocator,
            ibAllocator,
            uploadArena,
            uploader,
            uploadReadyValue,
            streamingUploadBudget,
            /*terrainEditOnly=*/false);
        m_hadUploadsThisFrame = (uploaded > 0);
        if (m_uploadSystem.consumeRemeshUploadCount() > 0) {
            m_hadEditUploadsThisFrame = true;
            m_hiZEditCooldown = 8;  // suppress temporal skip for 8 more frames after last topology edit
        }
    }
    auto uploadEnd = std::chrono::high_resolution_clock::now();

    // System 5: Finalize queue (mark chunks as Ready after upload)
    // Reset per-frame diagnostics, then let processFinalizeQueue + processLODSwaps populate it
    m_currentFinalizeDiag = FinalizeDiagFrame{};
    m_currentFinalizeDiag.frameNumber = m_finalizeDiagFrameCounter++;
    auto finalizeStart = std::chrono::high_resolution_clock::now();
    processFinalizeQueue();

    // System 5b: LOD batch swap (atomically swap staged meshes when batch is complete)
    if (vbAllocator && ibAllocator) {
        processLODSwaps(vbAllocator, ibAllocator, deviceTimeline);
        processSoloPendingSwaps(vbAllocator, ibAllocator, deviceTimeline);
        processDeferredMeshBufferFrees(vbAllocator, ibAllocator);
    }

    // Late visual catch-up: pick up edit remesh jobs that finished mid-frame,
    // then immediately upload/finalize them instead of waiting a whole frame.
    if (vbAllocator && ibAllocator && uploadArena && uploader) {
        auto lateFlushStart = std::chrono::high_resolution_clock::now();
        const size_t lateEditUploadsQueued = m_editRemeshScheduler.flushReadyCompletions(this);
        auto lateUploadStart = std::chrono::high_resolution_clock::now();
        m_currentFinalizeDiag.lateFlushMs +=
            std::chrono::duration<float, std::milli>(lateUploadStart - lateFlushStart).count();
        size_t lateUploaded = 0;
        if (lateEditUploadsQueued > 0) {
            lateUploaded = updateUploadQueueSystem(
                vbAllocator,
                ibAllocator,
                uploadArena,
                uploader,
                uploadReadyValue,
                lateEditUploadsQueued,
                /*terrainEditOnly=*/true);
            auto lateUploadEnd = std::chrono::high_resolution_clock::now();
            m_currentFinalizeDiag.lateUploadMs +=
                std::chrono::duration<float, std::milli>(lateUploadEnd - lateUploadStart).count();
        }
        m_hadUploadsThisFrame = m_hadUploadsThisFrame || (lateUploaded > 0);
        if (m_uploadSystem.consumeRemeshUploadCount() > 0) {
            m_hadEditUploadsThisFrame = true;
            m_hiZEditCooldown = 8;  // suppress temporal skip for 8 more frames after last topology edit
        }
        if (lateUploaded > 0 || m_uploadSystem.getFinalizeQueueSize() > 0) {
            auto lateFinalizeStart = std::chrono::high_resolution_clock::now();
            processFinalizeQueue();
            auto lateFinalizeEnd = std::chrono::high_resolution_clock::now();
            m_currentFinalizeDiag.lateFinalizeMs +=
                std::chrono::duration<float, std::milli>(lateFinalizeEnd - lateFinalizeStart).count();
        }
        if (lateUploaded > 0 || m_uploadSystem.getFinalizeQueueSize() > 0) {
            auto lateSwapStart = std::chrono::high_resolution_clock::now();
            processLODSwaps(vbAllocator, ibAllocator, deviceTimeline);
            processSoloPendingSwaps(vbAllocator, ibAllocator, deviceTimeline);
            processDeferredMeshBufferFrees(vbAllocator, ibAllocator);
            auto lateSwapEnd = std::chrono::high_resolution_clock::now();
            m_currentFinalizeDiag.lateSwapMs +=
                std::chrono::duration<float, std::milli>(lateSwapEnd - lateSwapStart).count();
        }
    }
    auto finalizeEnd = std::chrono::high_resolution_clock::now();
    m_currentFinalizeDiag.totalMs = std::chrono::duration<float, std::milli>(finalizeEnd - finalizeStart).count();

    // Update LOD switch progress tracker (must run after both processLODSwaps passes)
    updateLODSwitchDiag();

    // System 6: Deferred collision building
    auto collisionStart = std::chrono::high_resolution_clock::now();
    m_collisionSystem.processPendingCollisions(m_registry, m_registryMutex);
    auto collisionEnd = std::chrono::high_resolution_clock::now();

    // Detect chunks that have render geometry but no physics collider — see
    // World::scanForGhostGeometry for the rationale and which silent drops it
    // catches. Self-throttled internally.
    scanForGhostGeometry();

    if (m_lastEditDiag.valid) {
        m_lastEditDiag.pendingRemeshChunks =
            static_cast<uint32_t>(std::min<size_t>(m_editRemeshScheduler.pendingCount(), UINT32_MAX));
        m_lastEditDiag.pendingUploadChunks = m_uploadSystem.getQueueSize();
        m_lastEditDiag.pendingFinalizeChunks =
            static_cast<uint32_t>(std::min<size_t>(m_uploadSystem.getFinalizeQueueSize(), UINT32_MAX));
        m_lastEditDiag.visualPendingChunks =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisualChunks.size(), UINT32_MAX));
        m_lastEditDiag.visualPendingEdits =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisuals.size(), UINT32_MAX));
        m_lastEditDiag.asyncFinalizeMs =
            std::chrono::duration<float, std::milli>(finalizeEnd - finalizeStart).count();
        m_lastEditDiag.asyncFinalizeCount = m_currentFinalizeDiag.finalizeCount;
        m_lastEditDiag.asyncLodSwapEntityCount = m_currentFinalizeDiag.lodSwapEntityCount;
        m_lastEditDiag.asyncLodSwapFreeMs = m_currentFinalizeDiag.lodSwapFreeMs;
    }

    // Copy edit-path collision timing into the edit diagnostics struct
    {
        float editCollMs = m_collisionSystem.consumeLastEditCollisionMs();
        if (editCollMs > 0.0f && m_lastEditDiag.valid) {
            m_lastEditDiag.collisionBvhMs = editCollMs;
            m_lastEditDiag.collisionTotalMs = std::chrono::duration<float, std::milli>(collisionEnd - collisionStart).count();
            m_lastEditDiag.grandTotalMs = m_lastEditDiag.applyTotalMs + m_lastEditDiag.remeshTotalMs + m_lastEditDiag.collisionBvhMs;
        }
    }

    // Store in ring buffer only if there was actual finalize/LOD-swap work.
    // Idle frames would overwrite useful data in the fixed-size ring buffer,
    // making the report appear empty once the world reaches steady state.
    if (m_currentFinalizeDiag.finalizeCount > 0 || m_currentFinalizeDiag.lodSwapEntityCount > 0) {
        if (m_finalizeDiagHistory.size() < FINALIZE_DIAG_CAPACITY) {
            m_finalizeDiagHistory.push_back(m_currentFinalizeDiag);
        } else {
            m_finalizeDiagHistory[m_finalizeDiagWriteIdx] = m_currentFinalizeDiag;
        }
        m_finalizeDiagWriteIdx = (m_finalizeDiagWriteIdx + 1) % FINALIZE_DIAG_CAPACITY;
    }

    auto worldUpdateEnd = std::chrono::high_resolution_clock::now();

    m_lastUpdateBreakdown.chunkLoadingMs =
        std::chrono::duration<float, std::milli>(chunkLoadEnd - chunkLoadStart).count();
    m_lastUpdateBreakdown.meshingMs =
        std::chrono::duration<float, std::milli>(meshingEnd - meshingStart).count();
    m_lastUpdateBreakdown.uploadMs =
        std::chrono::duration<float, std::milli>(uploadEnd - uploadStart).count();
    m_lastUpdateBreakdown.collisionMs =
        std::chrono::duration<float, std::milli>(collisionEnd - collisionStart).count();
    m_lastUpdateBreakdown.finalizeMs =
        std::chrono::duration<float, std::milli>(finalizeEnd - finalizeStart).count();
    m_lastUpdateBreakdown.worldUpdateMs =
        std::chrono::duration<float, std::milli>(worldUpdateEnd - startTime).count();

    // Periodic buffer and chunk statistics
    static int statsCounter = 0;
    if (++statsCounter % 6000 == 0) {
        if (vbAllocator && ibAllocator) {
            // Stats tracking (no logging)
        }
    }

    // Update in-game debug display (delegated to WorldDebugMetrics.cpp)
    UpdateTimings timings;
    timings.startTime = startTime;
    timings.chunkLoadStart = chunkLoadStart;
    timings.chunkLoadEnd = chunkLoadEnd;
    timings.meshingStart = meshingStart;
    timings.meshingEnd = meshingEnd;
    timings.uploadStart = uploadStart;
    timings.uploadEnd = uploadEnd;
    timings.collisionStart = collisionStart;
    timings.collisionEnd = collisionEnd;
    timings.finalizeStart = finalizeStart;
    timings.finalizeEnd = finalizeEnd;
    timings.worldUpdateEnd = worldUpdateEnd;
    assembleDebugInfo(timings, vbAllocator, ibAllocator, cpuFrameMs, gpuFrameMs);
}

void World::updateChunkLoader(float deltaTime, const glm::vec3& cameraPos, float cameraYaw) {
    // Update camera position for background thread
    m_lastCameraPos = cameraPos;
    m_lastCameraYaw = cameraYaw;  // For minimap view cone
    
    // Ring-based chunk management: get chunks to create/destroy
    std::vector<ChunkManager::ChunkCreateRequest> chunksToCreate;
    std::vector<glm::ivec3> chunksToDestroy;
    
    // Check buffer capacity to prevent crashes (cached — avoids per-frame mutex lock)
    bool bufferLimitReached = false;
    if (m_vbAllocator && m_ibAllocator) {
        static int bufferCheckCounter = 0;
        static float cachedVbUtil = 0.0f;
        static float cachedIbUtil = 0.0f;
        if (++bufferCheckCounter >= 10) { // Check every ~0.17s at 60fps
            bufferCheckCounter = 0;
            auto vbTotal = m_vbAllocator->getTotalCapacity();
            auto ibTotal = m_ibAllocator->getTotalCapacity();
            if (vbTotal > 0 && ibTotal > 0) {
                cachedVbUtil = static_cast<float>(m_vbAllocator->getAllocatedBytes()) / vbTotal;
                cachedIbUtil = static_cast<float>(m_ibAllocator->getAllocatedBytes()) / ibTotal;
            }
        }
        if (cachedVbUtil > 0.80f || cachedIbUtil > 0.80f) {
            bufferLimitReached = true;
        }
    }
    
    std::shared_lock setLock(m_chunkSetMutex);
    m_chunkManager->update(deltaTime, cameraPos, m_readyChunkSet, m_existingChunkSet, chunksToCreate, chunksToDestroy, bufferLimitReached);
    setLock.unlock();
    
    bool centerChanged = m_chunkManager->wasCenterChanged();

    // Detect large teleport (explosion knockback, etc.)
    // moveDist > 5 triggers burst recovery: accelerated LOD scans and
    // full upload throughput so the world fills in as fast as possible.
    int moveDist = 0;
    if (centerChanged) {
        glm::ivec3 newCenter = m_chunkManager->getCenterChunk();
        glm::ivec3 prevCenter = m_chunkManager->getPreviousCenter();
        moveDist = std::max(std::abs(newCenter.x - prevCenter.x),
                            std::abs(newCenter.z - prevCenter.z));
        if (moveDist > 5) {
            m_burstRecoveryFrames = 10;
        }
    }

    // Burst recovery still accelerates LOD scanning after teleports, but the
    // upload/finalize path now always drains at full throughput.

    // Queue destructions for background thread — BATCHED (single lock instead of per-coord)
    if (!chunksToDestroy.empty()) {
        m_lifecycleManager.queueDestructions(chunksToDestroy);
        m_lodSystem.clearDesiredLODs(chunksToDestroy);
    }

    // On center change, purge any pending creations that are now out of range.
    // Without this, chunks queued during forward movement stay in the lifecycle
    // manager's creation queue even after the player reverses direction, and
    // get created as orphans that never appear in a destroy sweep.
    if (centerChanged) {
        glm::ivec3 newCenter = m_chunkManager->getCenterChunk();
        int renderDist = m_chunkManager->getEffectiveRenderDistance();
        auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
            [&](const glm::ivec3& coord) {
                int ring = m_chunkManager->calculateRingNumber(coord, newCenter);
                return ring >= renderDist;
            });
        if (!purgedCreates.empty()) {
            std::lock_guard lock(m_pendingChunksMutex);
            for (const auto& coord : purgedCreates) {
                m_pendingChunks.erase(coord);
            }
        }
        if (!purgedCreates.empty()) {
            m_chunkManager->cancelPendingCreates(purgedCreates);
        }

        // If center moved back toward previously-queued destroys, cancel those
        // obsolete destruction requests before worker thread executes them.
        auto purgedDestroys = m_lifecycleManager.purgeDestructionQueue(
            [&](const glm::ivec3& coord) {
                int ring = m_chunkManager->calculateRingNumber(coord, newCenter);
                return ring < renderDist;
            });
        if (!purgedDestroys.empty()) {
            m_chunkManager->cancelPendingDestroys(purgedDestroys);
        }
    }

    // Out-of-range sweep: SKIPPED — ChunkManager::update already produced
    // outChunksToDestroy via its trailing-edge pass, and the lifecycle
    // manager purge above cleans stale creation queue entries.
    // A second full O(N) scan of ~26K entries added ~0.9ms for no benefit.

    // Queue creations — BATCHED (single lock each for pending check + LOD + lifecycle)
    if (!chunksToCreate.empty()) {
        // Batch LOD updates (1 lock instead of N)
        std::vector<std::pair<glm::ivec3, int>> lodEntries;
        lodEntries.reserve(chunksToCreate.size());
        for (const auto& req : chunksToCreate) {
            lodEntries.push_back({req.coord, req.lodLevel});
        }
        m_lodSystem.setDesiredLODs(lodEntries);

        // Batch pending check + mark (1 lock instead of 2N)
        std::vector<glm::ivec3> nonPendingCoords;
        nonPendingCoords.reserve(chunksToCreate.size());
        {
            std::lock_guard lock(m_pendingChunksMutex);
            for (const auto& req : chunksToCreate) {
                if (m_pendingChunks.find(req.coord) == m_pendingChunks.end()) {
                    nonPendingCoords.push_back(req.coord);
                    m_pendingChunks.insert(req.coord);
                }
            }
        }

        // Batch lifecycle queue (1 lock instead of N)
        if (!nonPendingCoords.empty()) {
            m_lifecycleManager.queueCreations(nonPendingCoords);
        }
    }

    updateLODTransitions(deltaTime, centerChanged);

    // Wake up background thread if there's work
    if (!chunksToCreate.empty() || !chunksToDestroy.empty()) {
        m_lifecycleManager.wakeUp();
    }
}

void World::updateMarkDirtyOnGeneration() {
    // Job pipeline handles state transitions automatically via dependency chain
}

````

## src\world\WorldUpdateMeshing.cpp

Description: No CC-DESC found.

````cpp
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/chunks/core/ChunkJobs.h"
#include "vulkan/BufferSuballocator.h"
#include <iostream>
#include <string>

// updateMeshingSystem() — extracted from WorldUpdate.cpp

void World::updateMeshingSystem() {
    size_t iterations = m_lodSystem.getRemeshQueueSize();
    while (iterations-- > 0) {
        entt::entity entity = m_lodSystem.popRemeshQueue();
        if (entity == entt::null) break;

        // Get batch info EARLY so we can signal the batch if the entity gets dropped.
        // Without this, dropped entities silently decrement the batch's effective count,
        // and the batch never completes — freezing all chunks at old LOD.
        auto batchInfo = m_lodSystem.getRemeshBatchInfo(entity);

        auto versionState = ensureChunkVersionState(this, entity);
        if (!versionState) {
            if (batchInfo.isRemesh && m_chunkManager && m_chunkManager->isBatchActive(batchInfo.batchId))
                m_chunkManager->signalBatchChunkReady(batchInfo.batchId);
            if (batchInfo.isRemesh) {
                noteChunkVisualError(
                    nullptr,
                    batchInfo.targetLOD,
                    "LODQueue",
                    "MissingVersionState",
                    batchInfo.batchId,
                    0,
                    0);
            }
            m_lodSystem.clearPending(entity);
            continue;
        }

        if (versionState->inFlight.load(std::memory_order_acquire)) {
            m_lodSystem.requeue(entity);
            continue;
        }

        ChunkCoord coordComponent;
        Chunk chunkComponent;
        AABB aabbComponent;
        {
            std::shared_lock regLock(m_registryMutex);
            if (!m_registry.valid(entity) ||
                !m_registry.all_of<ChunkCoord, ChunkState, Chunk, AABB>(entity)) {
                if (batchInfo.isRemesh && m_chunkManager && m_chunkManager->isBatchActive(batchInfo.batchId))
                    m_chunkManager->signalBatchChunkReady(batchInfo.batchId);
                if (batchInfo.isRemesh) {
                    noteChunkVisualError(
                        nullptr,
                        batchInfo.targetLOD,
                        "LODQueue",
                        "InvalidEntityOrMissingComponents",
                        batchInfo.batchId,
                        0,
                        0);
                }
                m_lodSystem.clearPending(entity);
                continue;
            }

            coordComponent = m_registry.get<ChunkCoord>(entity);
            chunkComponent = m_registry.get<Chunk>(entity);
            aabbComponent = m_registry.get<AABB>(entity);
        }

        glm::ivec3 coordVec = coordComponent.toVec3();
        if (batchInfo.isRemesh) {
            if (!m_chunkManager || !m_chunkManager->isBatchActive(batchInfo.batchId)) {
                noteChunkVisualError(
                    &coordVec,
                    batchInfo.targetLOD,
                    "LODQueue",
                    "BatchInactiveBeforeDispatch",
                    batchInfo.batchId,
                    0,
                    0);
                m_lodSystem.clearPending(entity);
                continue;
            }

            // Entry became stale (center moved and desired LOD changed) while waiting in queue.
            int desiredLodNow = m_lodSystem.getDesiredLOD(coordVec);
            if (batchInfo.targetLOD != desiredLodNow) {
                m_chunkManager->signalBatchChunkReady(batchInfo.batchId);
                std::string reason = "QueuedTargetLodStale target=" +
                    std::to_string(batchInfo.targetLOD) +
                    " desiredNow=" + std::to_string(desiredLodNow);
                noteChunkVisualError(
                    &coordVec,
                    batchInfo.targetLOD,
                    "LODQueue",
                    reason.c_str(),
                    batchInfo.batchId,
                    static_cast<uint32_t>(batchInfo.targetLOD),
                    static_cast<uint32_t>(desiredLodNow));
                m_lodSystem.clearPending(entity);
                continue;
            }
        }

        // Start a new pipeline generation token now that we are actually scheduling.
        versionState->version.fetch_add(1, std::memory_order_acq_rel);
        versionState->inFlight.store(true, std::memory_order_release);
        versionState->pending.store(false, std::memory_order_release);

        // Clear from LOD system pending set now that dispatch is committed.
        // This lets the LOD scan distinguish "in queue" (isPending=true) from
        // "dispatched / in flight" (isPending=false, inFlight=true) so the
        // drain doesn't create duplicate batches that overwrite batch info.
        m_lodSystem.clearPending(entity);

        // For LOD remeshes (chunk already Ready), keep old mesh visible
        // until the new one is uploaded. Only set Loading for initial loads.
        if (chunkComponent.lodLevel >= 0) {
            // Check if this is a remesh (chunk already has a mesh / is Ready)
            ChunkState::State currentState = ChunkState::State::Unloaded;
            {
                std::shared_lock regLock(m_registryMutex);
                if (m_registry.valid(entity) && m_registry.all_of<ChunkState>(entity)) {
                    currentState = m_registry.get<ChunkState>(entity).state;
                }
            }
            // Only transition to Loading for initial chunk creation, not remeshes
            if (currentState != ChunkState::State::Ready) {
                setChunkState(entity, ChunkState::State::Loading);
            }
        }

        auto* payload = m_payloadPool.acquire();
        payload->world = this;
        payload->entity = entity;
        payload->coord = coordComponent;
        payload->bounds = aabbComponent;
        payload->versionState = versionState;
        payload->version = versionState->version.load(std::memory_order_acquire);
        
        // Check if this is a batched LOD remesh
        payload->isRemesh = batchInfo.isRemesh;
        payload->batchId = batchInfo.batchId;
        payload->affectsShadowGeometry = batchInfo.affectsShadowGeometry;
        
        // For remeshes, use the target LOD from batch info (chunk.lodLevel
        // is NOT updated until the mesh is actually swapped in processLODSwaps).
        // For initial loads, use chunk.lodLevel (set at creation time).
        payload->lodLevel = batchInfo.isRemesh ? batchInfo.targetLOD : chunkComponent.lodLevel;

        glm::ivec3 center = m_chunkManager ? m_chunkManager->getCenterChunk() : glm::ivec3(0, 0, 0);
        payload->centerAtEnqueue = center;
        int ringNumber = m_chunkManager ? m_chunkManager->calculateRingNumber(coordVec, center) : 0;
        int dx = coordVec.x - center.x;
        int dz = coordVec.z - center.z;
        int distSq = dx * dx + dz * dz;
        int priorityKey = ringNumber * 1000000 + distSq;
        int jobPriority = 1000000 - priorityKey;
        payload->distanceFromPlayer = priorityKey;

        // Check if we should use precomputed meshes
        // For chunks with terrain edits, use the edit mesher instead of loading from terrain.bin.
        // DCCM terrain is not affected by edits — always use precomputed path.
        const bool useRuntimeVoxel = chunkNeedsRuntimeVoxel(coordVec);
        const TerrainType lodTerrainType = getTerrainTypeForChunk(coordVec, payload->lodLevel);
        const bool isDCCM = (lodTerrainType == TerrainType::DCCM) && m_heightmapSampler.isLoaded();
        const bool useEditMesher = useRuntimeVoxel;
        payload->fromTerrainEdit = useEditMesher && !isDCCM;
        auto loadJobFn = (useEditMesher && !isDCCM) ? LoadEditMeshJob : LoadPrecomputedMeshJob;

        JobHandle load = m_jobSystem.makeWithPriority(loadJobFn, payload, 0, jobPriority);
        JobHandle upload = m_jobSystem.makeWithPriority(UploadChunkJob, payload, 0, jobPriority);
        JobHandle finalize = m_jobSystem.makeWithPriority(FinalizeChunkJob, payload, 0, jobPriority);

        m_jobSystem.addDependency(upload, load);
        m_jobSystem.addDependency(finalize, upload);

        payload->jobHandles = {load, upload, finalize};

        m_jobSystem.schedule(load);
        m_jobSystem.schedule(upload);
        m_jobSystem.schedule(finalize);

        m_lodSystem.clearPending(entity);
    }
}

````

## src\world\WorldUpdateFinalize.cpp

Description: No CC-DESC found.

````cpp
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include "rendering/common/Mesh.h"
#include <chrono>
#include <limits>

// updateUploadQueueSystem(), onMeshUploaded(), processFinalizeQueue()
// — extracted from WorldUpdate.cpp

size_t World::updateUploadQueueSystem(BufferSuballocator* vbAllocator,
                                     BufferSuballocator* ibAllocator,
                                     UploadArena* uploadArena,
                                     ResourceUploader* uploader,
                                     uint64_t uploadReadyValue,
                                     size_t maxUploadsOverride,
                                     bool terrainEditOnly) {
    // Store allocators for later cleanup
    m_vbAllocator = vbAllocator;
    m_ibAllocator = ibAllocator;
    
    // Set center for distance-sorted upload ordering
    if (m_chunkManager) {
        m_uploadSystem.setCenterChunk(m_chunkManager->getCenterChunk());
    }
    
    // Delegate to ChunkUploadSystem
    size_t processed = m_uploadSystem.processUploads(
        m_registry,
        m_registryMutex,
        vbAllocator,
        ibAllocator,
        uploadArena,
        uploader,
        uploadReadyValue,
        this,  // World implements IUploadCallback
        maxUploadsOverride,
        terrainEditOnly);
    
    // Update streaming metrics
    m_streamingMetrics.uploadQueueSize = m_uploadSystem.getQueueSize();
    m_streamingMetrics.currentUploadBudget = m_uploadSystem.getCurrentUploadBudget();
    m_streamingMetrics.meshesUploaded.store(m_uploadSystem.getTotalUploaded(), std::memory_order_relaxed);
    
    return processed;
}

void World::onMeshUploaded(
    entt::entity entity,
    const glm::ivec3& chunkCoord,
    const std::vector<Vertex>& vertices,
    const std::vector<uint16_t>& indices,
    int lodLevel)
{
    // Queue collision building for LOD 0 chunks ONLY
    // Higher LODs are visual-only (distant terrain) - no physics needed
    if (m_physics && lodLevel == 0) {
        // If this chunk already has edit collision data (from a terrain edit),
        // skip queueing from the stale base collision cache — the correct
        // body was already created by enqueueEditCollision.
        if (m_editCollisionData.count(chunkCoord)) {
            m_chunkCollisionSources[chunkCoord] = ChunkCollisionSource::ExistingEditedCollision;
            return;
        }

        PendingCollision pending;
        pending.entity = entity;
        pending.chunkCoord = chunkCoord;
        pending.vertices = vertices;
        pending.indices = indices;
        m_chunkCollisionSources[chunkCoord] = ChunkCollisionSource::BaseCollisionCache;
        m_collisionSystem.enqueueCollision(std::move(pending));
    }
}

void World::onUploadPipelineEvent(
    entt::entity /*entity*/,
    const glm::ivec3* chunkCoord,
    int lodLevel,
    const char* stage,
    const char* reason,
    uint32_t batchId,
    uint32_t expectedVersion,
    uint32_t actualVersion,
    const ChunkDebugAttribution* debugInfo)
{
    noteChunkVisualError(
        chunkCoord,
        lodLevel,
        stage,
        reason,
        batchId,
        expectedVersion,
        actualVersion,
        debugInfo);
}

void World::recordMeshTopologyChange(const glm::ivec3& coord) {
    std::vector<glm::ivec3> coords;
    coords.push_back(coord);
    recordMeshTopologyChanges(coords);
}

void World::recordMeshTopologyChanges(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return;

    const uint64_t revision =
        m_meshTopologyVersion.fetch_add(1, std::memory_order_relaxed) + 1u;

    std::lock_guard lock(m_meshTopologyChangeMutex);
    for (const glm::ivec3& coord : coords) {
        m_meshTopologyChanges.push_back(MeshTopologyChange{revision, coord});
    }

    constexpr size_t kMaxMeshTopologyChangeHistory = 65536u;
    while (m_meshTopologyChanges.size() > kMaxMeshTopologyChangeHistory) {
        m_meshTopologyOldestDroppedRevision =
            std::max(m_meshTopologyOldestDroppedRevision,
                     m_meshTopologyChanges.front().revision);
        m_meshTopologyChanges.pop_front();
    }
}

void World::recordGlobalMeshTopologyChange() {
    const uint64_t revision =
        m_meshTopologyVersion.fetch_add(1, std::memory_order_relaxed) + 1u;
    std::lock_guard lock(m_meshTopologyChangeMutex);
    m_meshTopologyChanges.clear();
    m_meshTopologyOldestDroppedRevision = revision;
}

bool World::getMeshTopologyChangesSince(
    uint64_t revision,
    std::vector<MeshTopologyChange>& outChanges,
    size_t maxChanges) const {
    outChanges.clear();

    const uint64_t currentRevision =
        m_meshTopologyVersion.load(std::memory_order_relaxed);
    if (revision == currentRevision) {
        return true;
    }

    std::lock_guard lock(m_meshTopologyChangeMutex);
    if (revision < m_meshTopologyOldestDroppedRevision) {
        return false;
    }
    if (m_meshTopologyChanges.empty()) {
        return false;
    }

    for (const MeshTopologyChange& change : m_meshTopologyChanges) {
        if (change.revision <= revision) continue;
        if (outChanges.size() >= maxChanges) {
            outChanges.clear();
            return false;
        }
        outChanges.push_back(change);
    }
    return true;
}

size_t World::processFinalizeQueue(size_t maxFinalizeCount) {
    using Clock = std::chrono::high_resolution_clock;
    auto& diag = m_currentFinalizeDiag;

    // Step 1: Drain finalize queue without any locks (main-thread-only queue)
    auto t0 = Clock::now();
    std::vector<ChunkFinalizeRequest> toFinalize;
    const size_t drainLimit = (maxFinalizeCount == 0u)
        ? std::numeric_limits<size_t>::max()
        : maxFinalizeCount;
    m_uploadSystem.drainFinalizeQueue(toFinalize, drainLimit);
    auto t1 = Clock::now();
    diag.drainMs += std::chrono::duration<float, std::milli>(t1 - t0).count();
    diag.finalizeCount += static_cast<uint32_t>(toFinalize.size());
    if (toFinalize.empty()) return 0;

    // Step 2: Validate entities + read coords + set ChunkState component (ONE unique_lock)
    struct FinalizeEntry {
        entt::entity entity;
        glm::ivec3 coord;
        ChunkState::State oldState{ChunkState::State::Unloaded};
        std::chrono::steady_clock::time_point uploadEnqueueTime{};
        int lodLevel{0};
        uint8_t effectiveDataLod{0};
        int desiredEffectiveDataLod{0};
        bool dataLodMismatch{false};
        bool hasPendingMeshHandle{false};
        uint32_t pendingBatchId{0};
        uint64_t vramBytes{0};
        uint32_t vertexCount{0};
        uint32_t indexCount{0};
        std::shared_ptr<ChunkVersionState> versionState;
        ChunkDebugAttribution debugInfo;
    };
    std::vector<FinalizeEntry> entries;
    entries.reserve(toFinalize.size());
    {
        auto lockWaitStart = Clock::now();
        std::unique_lock regLock(m_registryMutex);
        auto lockAcquired = Clock::now();
        for (const auto& req : toFinalize) {
            const entt::entity entity = req.entity;
            if (!m_registry.valid(entity) ||
                !m_registry.all_of<ChunkState, ChunkCoord>(entity)) {
                // Entity gone — clear inFlight so the edit scheduler doesn't deadlock
                if (req.versionState) {
                    req.versionState->inFlight.store(false, std::memory_order_release);
                }
                continue;
            }
            m_registry.get<ChunkState>(entity).state = ChunkState::State::Ready;

            FinalizeEntry entry{};
            entry.entity = entity;
            entry.coord = m_registry.get<ChunkCoord>(entity).toVec3();
            entry.uploadEnqueueTime = req.enqueueTime;
            entry.versionState = req.versionState;
            entry.debugInfo = req.debugInfo;

            if (m_registry.all_of<Chunk>(entity)) {
                const auto& chunk = m_registry.get<Chunk>(entity);
                entry.lodLevel = chunk.lodLevel;
                entry.effectiveDataLod = chunk.effectiveDataLod;
                if (entry.debugInfo.meshMode == 0xFF) {
                    entry.debugInfo.meshMode = static_cast<uint8_t>(chunk.meshMode);
                }
                entry.desiredEffectiveDataLod = getEffectiveLODForChunk(entry.coord, chunk.lodLevel);
                entry.dataLodMismatch =
                    static_cast<int>(chunk.effectiveDataLod) != entry.desiredEffectiveDataLod;
                entry.hasPendingMeshHandle = m_registry.all_of<PendingMeshHandle>(entity);
                if (entry.hasPendingMeshHandle) {
                    entry.pendingBatchId = m_registry.get<PendingMeshHandle>(entity).batchId;
                }
            }
            if (m_registry.all_of<MeshHandle>(entity)) {
                const auto& mesh = m_registry.get<MeshHandle>(entity);
                entry.vramBytes = mesh.getTotalVramBytes();
                entry.vertexCount = mesh.getTotalVertexCount();
                entry.indexCount = mesh.getTotalIndexCount();
                if (entry.debugInfo.uploadBytes == 0) {
                    entry.debugInfo.uploadBytes = entry.vramBytes;
                }
                if (entry.debugInfo.subChunkCount == 0) {
                    entry.debugInfo.subChunkCount = mesh.subChunkCount;
                }
            }

            entries.push_back(entry);
        }
        auto lockDone = Clock::now();
        diag.regLockWaitMs += std::chrono::duration<float, std::milli>(lockAcquired - lockWaitStart).count();
        diag.regLockHeldMs += std::chrono::duration<float, std::milli>(lockDone - lockAcquired).count();
    }
    if (entries.empty()) return 0;

    // Step 3: Update state map (ONE unique_lock on m_chunkStateMutex)
    {
        auto t = Clock::now();
        std::unique_lock lock(m_chunkStateMutex);
        for (auto& entry : entries) {
            auto it = m_chunkStateMap.find(entry.coord);
            if (it != m_chunkStateMap.end()) {
                entry.oldState = it->second;
            }
            m_chunkStateMap[entry.coord] = ChunkState::State::Ready;
        }
        diag.stateMapLockMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    // Step 4: Adjust atomic counters (no lock needed)
    for (auto& entry : entries) {
        if (entry.oldState == ChunkState::State::Loading)
            m_loadingCount.fetch_sub(1, std::memory_order_relaxed);
        else if (entry.oldState == ChunkState::State::Meshing)
            m_meshingCount.fetch_sub(1, std::memory_order_relaxed);
        else if (entry.oldState == ChunkState::State::Ready)
            m_readyCount.fetch_sub(1, std::memory_order_relaxed);
        m_readyCount.fetch_add(1, std::memory_order_relaxed);
    }

    // Step 5: Insert into readyChunkSet (ONE unique_lock on m_chunkSetMutex)
    {
        auto t = Clock::now();
        std::unique_lock setLock(m_chunkSetMutex);
        for (auto& entry : entries) {
            m_readyChunkSet.insert(entry.coord);
        }
        diag.readySetLockMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    // Step 6: Batch notify ChunkManager (ONE lock on m_pendingOpsMutex internally)
    {
        auto t = Clock::now();
        std::vector<glm::ivec3> coords;
        coords.reserve(entries.size());
        for (auto& entry : entries) coords.push_back(entry.coord);
        m_chunkManager->notifyChunksCreated(coords);
        diag.notifyMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    // Step 7: Clear pending chunks (ONE lock on m_pendingChunksMutex)
    {
        auto t = Clock::now();
        std::lock_guard lock(m_pendingChunksMutex);
        for (auto& entry : entries) {
            m_pendingChunks.erase(entry.coord);
        }
        diag.clearPendingMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    const auto finalizeTime = std::chrono::steady_clock::now();
    bool shadowGeometryFinalized = false;
    std::vector<glm::ivec3> shadowGeometryCoords;
    for (const auto& entry : entries) {
        // Batched LOD remesh uploads stage into PendingMeshHandle first and do
        // not become visible until processLODSwaps() atomically swaps the whole
        // batch. Logging them here floods history with fake "shown" events.
        if (!entry.hasPendingMeshHandle) {
            if (entry.debugInfo.affectsShadowGeometry) {
                shadowGeometryFinalized = true;
                shadowGeometryCoords.push_back(entry.coord);
            }
            auto tVisual = Clock::now();
            noteChunkVisualReady(
                entry.coord,
                entry.uploadEnqueueTime,
                finalizeTime,
                entry.lodLevel,
                entry.vramBytes,
                entry.vertexCount,
                entry.indexCount,
                &entry.debugInfo);
            diag.visualReadyMs += std::chrono::duration<float, std::milli>(Clock::now() - tVisual).count();
        }

        auto tMismatch = Clock::now();
        if (entry.dataLodMismatch) {
            if (entry.hasPendingMeshHandle) {
                const bool pendingBatchActive =
                    (entry.pendingBatchId != 0) && m_chunkManager && m_chunkManager->isBatchActive(entry.pendingBatchId);
                if (!pendingBatchActive) {
                    std::string reason = "FinalizeDataLodMismatchPendingInactiveBatch effective=" +
                        std::to_string(entry.effectiveDataLod) +
                        " desired=" + std::to_string(entry.desiredEffectiveDataLod) +
                        " batch=" + std::to_string(entry.pendingBatchId);
                    noteChunkVisualError(
                        &entry.coord,
                        entry.lodLevel,
                        "Finalize",
                        reason.c_str(),
                        entry.pendingBatchId,
                        entry.effectiveDataLod,
                        static_cast<uint32_t>(entry.desiredEffectiveDataLod));
                }
            } else {
                m_lodSystem.setDesiredLOD(entry.coord, entry.lodLevel);
                m_lodSystem.enqueueLODRemesh(
                    entry.entity,
                    /*isRemesh=*/false,
                    /*batchId=*/0,
                    entry.lodLevel);
                std::string reason = "FinalizeDataLodMismatchRequeued effective=" +
                    std::to_string(entry.effectiveDataLod) +
                    " desired=" + std::to_string(entry.desiredEffectiveDataLod);
                noteChunkVisualError(
                    &entry.coord,
                    entry.lodLevel,
                    "Finalize",
                    reason.c_str(),
                    0,
                    entry.effectiveDataLod,
                    static_cast<uint32_t>(entry.desiredEffectiveDataLod));
            }
        }
        diag.lodMismatchMs += std::chrono::duration<float, std::milli>(Clock::now() - tMismatch).count();

        auto tCollision = Clock::now();
        refreshEditedChunkCollisionFromArtifact(entry.entity, entry.coord, entry.lodLevel);
        diag.collisionRefreshMs += std::chrono::duration<float, std::milli>(Clock::now() - tCollision).count();
    }

    // Clear inFlight for entries that carry a versionState — this completes
    // the dispatch→drain→upload→finalize pipeline and unlocks the entity for
    // new edit / remesh dispatches.  Done AFTER all finalize work so no
    // concurrent dispatch can race against partial state updates above.
    {
        auto t = Clock::now();
        for (const auto& entry : entries) {
            if (entry.versionState) {
                entry.versionState->inFlight.store(false, std::memory_order_release);
            }
        }
        diag.inFlightClearMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    // New or replaced geometry is now eligible for rendering, so cached
    // shadows must refresh on the next frame. Texture-only material remeshes
    // keep the depth shape identical and must not invalidate shadow caches.
    // Staged PendingMeshHandles bump this later, at the atomic swap point.
    if (shadowGeometryFinalized) {
        auto t = Clock::now();
        recordMeshTopologyChanges(shadowGeometryCoords);
        diag.topologyRecordMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    return entries.size();
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
    world/WorldUpdateMeshing.cpp
    world/WorldUpdateFinalize.cpp
    world/WorldRendering.cpp
    world/WorldDebugMetrics.cpp
    world/WorldChunkCRUD.cpp
    world/lod/WorldLODConfig.cpp
    world/lod/WorldLODTransitions.cpp
    world/lod/WorldLODSwaps.cpp
    world/lod/WorldLODDiagnostics.cpp
    world/snapshot/WorldSnapshotStore.cpp
    world/snapshot/WorldSnapshotLoad.cpp
    world/snapshot/WorldSnapshotSave.cpp
    world/snapshot/WorldSnapshotDelete.cpp
    world/snapshot/WorldSnapshotIdentity.cpp
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
