# Code export

Generated from project files.

Project root: D:\Projects\vulkanas

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


## src\world\update\WorldUpdateLoop.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Runs the per-frame World update loop and coordinates chunk, upload, finalize, collision, and diagnostics phases.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/chunks/core/ChunkJobs.h"
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include <algorithm>
#include <chrono>
#include <cstdint>
#include <limits>

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

````

## src\world\update\WorldChunkLoader.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Updates chunk creation/destruction rings and forwards LOD transition scanning.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "vulkan/BufferSuballocator.h"
#include <algorithm>
#include <cmath>
#include <vector>

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

## src\world\update\WorldMeshingDispatch.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Dispatches dirty chunk and LOD remesh jobs into the chunk job pipeline.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/chunks/core/ChunkJobs.h"
#include "vulkan/BufferSuballocator.h"
#include <atomic>
#include <string>

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

## src\world\jobs\WorldChunkJobScheduling.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Implements World lifecycle callbacks that create, destroy, and schedule chunk jobs.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/chunks/core/ChunkJobs.h"
#include "world/config/WorldConfig.h"
#include <algorithm>
#include <atomic>
#include <cmath>

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

````

## src\world\World.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Owns World construction, core state helpers, and small facade utilities.
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
// createChunkEntities(), scheduleChunkJobs(), destroyChunks(), cleanupStaleVersionStates()
// moved to world/jobs/WorldChunkJobScheduling.cpp

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

// ---- World facade/accessor definitions extracted from World.h ----

void World::appendChunkVisualError(
    const glm::ivec3* coord,
    int lodLevel,
    const char* stage,
    const char* reason,
    uint32_t batchId,
    uint32_t expectedVersion,
    uint32_t actualVersion,
    const ChunkDebugAttribution* debugInfo)
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

InGameDebug& World::getDebugOverlay() { return *m_inGameDebug; }
const InGameDebug& World::getDebugOverlay() const { return *m_inGameDebug; }

TerrainEdit::TerrainEditOverlayStore& World::getTerrainEditOverlay() { return m_terrainEditOverlay; }
const TerrainEdit::TerrainEditOverlayStore& World::getTerrainEditOverlay() const { return m_terrainEditOverlay; }

TextureOverlay::TextureOverlayStore& World::getTextureMaterialStore() { return m_textureMaterialStore; }
const TextureOverlay::TextureOverlayStore& World::getTextureMaterialStore() const { return m_textureMaterialStore; }

TerrainEdit::TerrainFieldSource& World::getTerrainFieldSource() { return m_terrainFieldSource; }
const TerrainEdit::TerrainFieldSource& World::getTerrainFieldSource() const { return m_terrainFieldSource; }

const TerrainEdit::HeightmapBaseSampler& World::getHeightmapSampler() const { return m_heightmapSampler; }
const TerrainEdit::VoxelBaseSampler& World::getVoxelBaseSampler() const { return m_voxelBaseSampler; }

ChunkHoleTracker& World::getChunkHoleTracker() { return m_chunkHoleTracker; }
const ChunkHoleTracker& World::getChunkHoleTracker() const { return m_chunkHoleTracker; }

void World::setPhysicsWorld(Physics::PhysicsWorld* physics) {
    m_physics = physics;
    m_collisionSystem.setPhysicsWorld(physics);
    m_collisionSystem.setCollisionCache(m_collisionCache.get());
}

void World::setGPUCullingSystem(GPUCullingSystem* gpuCulling) {
    m_gpuCulling = gpuCulling;
    m_uploadSystem.setGPUCullingSystem(gpuCulling);
}

void World::setCullingStats(const CullingStats& stats) {
    m_cullingStats = stats;
}

const World::LastUpdateBreakdown& World::getLastUpdateBreakdown() const { return m_lastUpdateBreakdown; }
int World::getLoadingCount() const { return m_loadingCount.load(std::memory_order_relaxed); }
int World::getMeshingCount() const { return m_meshingCount.load(std::memory_order_relaxed); }
int World::getReadyCount() const { return m_readyCount.load(std::memory_order_relaxed); }

World::Registry& World::getRegistry() { return m_registry; }
const World::Registry& World::getRegistry() const { return m_registry; }
std::shared_mutex& World::registryMutex() const { return m_registryMutex; }

JobSystem& World::getJobSystem() { return m_jobSystem; }
PayloadPool& World::getPayloadPool() { return m_payloadPool; }
ChunkManager* World::getChunkManager() { return m_chunkManager.get(); }

std::mutex& World::chunkVersionMutex() const { return m_chunkVersionMutex; }
std::unordered_map<entt::entity, std::shared_ptr<struct ChunkVersionState>, struct EntityHash>&
World::getChunkVersionStates() { return m_chunkVersionStates; }

World::StreamingMetrics& World::getStreamingMetrics() { return m_streamingMetrics; }
const World::StreamingMetrics& World::getStreamingMetrics() const { return m_streamingMetrics; }

````

## src\world\WorldChunkCRUD.cpp

Description: No CC-DESC found.

````cpp
// WorldChunkCRUD.cpp — Chunk creation, destruction, reset, terrain switching
// Extracted from World.cpp to reduce god-file size without changing behavior.

#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "physics/PhysicsWorld.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <iostream>
#include <algorithm>
#include <filesystem>

// Helper: Calculate chunk AABB from chunk coordinate (same as in World.cpp)
static AABB calculateChunkAABB(const glm::ivec3& chunkCoord) {
    glm::vec3 minWorld = WorldConfig::microVoxelToWorld(WorldConfig::chunkToMicroVoxel(chunkCoord));
    glm::vec3 maxWorld = minWorld + glm::vec3(WorldConfig::CHUNK_SIZE_M, WorldConfig::CHUNK_HEIGHT_M, WorldConfig::CHUNK_SIZE_M);
    return AABB{minWorld, maxWorld};
}

entt::entity World::createChunk(const glm::ivec3& chunkCoord) {
    // Check if chunk already exists
    entt::entity existing = findChunk(chunkCoord);
    if (existing != entt::null) {
        return existing;
    }

    int desiredLod = getDesiredLODForChunk(chunkCoord);

    // Create entity
    entt::entity entity;
    {
        std::unique_lock lock(m_registryMutex);
        entity = m_registry.create();

        // Add components
        m_registry.emplace<ChunkCoord>(entity, chunkCoord.x, chunkCoord.y, chunkCoord.z);
        m_registry.emplace<ChunkState>(entity, ChunkState::State::Unloaded);

        // Calculate and store AABB
        AABB aabb = calculateChunkAABB(chunkCoord);
        m_registry.emplace<AABB>(entity, aabb);

        // PHASE 5A: Add Chunk component with metadata (isEmpty will be set after terrain generation)
        auto& chunk = m_registry.emplace<Chunk>(entity, chunkCoord, static_cast<uint32_t>(entity));
        chunk.lodLevel = desiredLod;
    }

    setChunkState(chunkCoord, ChunkState::State::Unloaded);
    {
        std::unique_lock setLock(m_chunkSetMutex);
        m_existingChunkSet.insert(chunkCoord);
    }
    {
        std::unique_lock mapLock(m_chunkStateMutex);
        m_chunkEntityMap[chunkCoord] = entity;
    }
    m_streamingMetrics.chunksCreatedTotal.fetch_add(1, std::memory_order_relaxed);

    return entity;
}

// OPTIMIZATION: Batch entity creation (1.5x speedup for bursts)
// Creates multiple chunks under single registry lock, reducing lock overhead
std::vector<entt::entity> World::createChunksBatch(const std::vector<glm::ivec3>& coords) {
    std::vector<entt::entity> entities;
    entities.reserve(coords.size());
    
    if (coords.empty()) {
        return entities;
    }
    
    // Pre-filter: remove from destroy queue and check existing
    std::vector<glm::ivec3> toCreate;
    toCreate.reserve(coords.size());
    
    for (const auto& coord : coords) {
        // Check if chunk already exists
        entt::entity existing = findChunk(coord);
        if (existing != entt::null) {
            entities.push_back(existing);
        } else {
            toCreate.push_back(coord);
            entities.push_back(entt::null); // Placeholder, will be filled
        }
    }
    
    if (toCreate.empty()) {
        return entities;
    }
    
    // OPTIMIZATION: Single registry lock for all entity creations
    std::vector<std::pair<entt::entity, glm::ivec3>> created;
    created.reserve(toCreate.size());
    std::vector<int> desiredLods;
    desiredLods.reserve(toCreate.size());
    for (const auto& coord : toCreate) {
        desiredLods.push_back(getDesiredLODForChunk(coord));
    }

    {
        std::unique_lock lock(m_registryMutex);
        for (size_t idx = 0; idx < toCreate.size(); ++idx) {
            const auto& coord = toCreate[idx];
            int desiredLod = desiredLods[idx];
            entt::entity entity = m_registry.create();
            m_registry.emplace<ChunkCoord>(entity, coord.x, coord.y, coord.z);
            m_registry.emplace<ChunkState>(entity, ChunkState::State::Unloaded);
            AABB aabb = calculateChunkAABB(coord);
            m_registry.emplace<AABB>(entity, aabb);
            // PHASE 5A: Add Chunk component with metadata (isEmpty will be set after terrain generation)
            auto& chunk = m_registry.emplace<Chunk>(entity, coord, static_cast<uint32_t>(entity));
            chunk.lodLevel = desiredLod;
            created.emplace_back(entity, coord);
        }
    }

    if (!created.empty()) {
        std::unique_lock setLock(m_chunkSetMutex);
        for (const auto& entry : created) {
            m_existingChunkSet.insert(entry.second);
        }
    }
    
    // Update state maps and metrics (outside registry lock)
    {
        std::unique_lock mapLock(m_chunkStateMutex);
        for (const auto& [entity, coord] : created) {
            m_chunkStateMap[coord] = ChunkState::State::Unloaded;
            m_chunkEntityMap[coord] = entity;
        }
    }
    
    m_streamingMetrics.chunksCreatedTotal.fetch_add(
        static_cast<uint32_t>(created.size()), std::memory_order_relaxed);
    
    // Fill in entities vector with created entities
    size_t createdIdx = 0;
    for (size_t i = 0; i < entities.size(); ++i) {
        if (entities[i] == entt::null) {
            entities[i] = created[createdIdx++].first;
        }
    }
    
    return entities;
}

bool World::tryDestroyChunk(const glm::ivec3& coord) {
    entt::entity entity = findChunk(coord);
    if (entity == entt::null) {
        // Chunk doesn't exist, clean up tracking state
        clearChunkPending(coord);
        removeChunkState(coord);
        return true;
    }

    // Cancel any in-flight jobs by incrementing version and clearing inFlight.
    // The version bump makes any stale pipeline discard its output, so it's
    // safe to clear inFlight immediately — no new pipeline will be scheduled
    // for a chunk being destroyed.
    auto versionState = ensureChunkVersionState(this, entity);
    if (versionState) {
        versionState->version.fetch_add(1, std::memory_order_acq_rel);
        versionState->inFlight.store(false, std::memory_order_release);
        versionState->pending.store(false, std::memory_order_release);
    }

    // Safe to destroy - no jobs in flight
    clearChunkPending(coord);
    removeChunkState(coord);
    m_lodSystem.clearDesiredLOD(coord);

    bool removedRenderableMesh = false;

    // Free GPU mesh buffers
    {
        std::unique_lock regLock(m_registryMutex);
        if (m_registry.all_of<MeshHandle>(entity)) {
            const auto& meshHandle = m_registry.get<MeshHandle>(entity);
            removedRenderableMesh = true;
            meshStatsSub(meshHandle);
            if (meshHandle.vb.isValid() && m_vbAllocator) {
                m_vbAllocator->free(meshHandle.vb);
            }
            if (meshHandle.ib.isValid() && m_ibAllocator) {
                m_ibAllocator->free(meshHandle.ib);
            }
            if (m_gpuCulling && meshHandle.gpuCullingSlot != UINT32_MAX) {
                m_gpuCulling->freeSlot(meshHandle.gpuCullingSlot);
            }
        }
        if (m_registry.all_of<PendingMeshHandle>(entity)) {
            const auto& pending = m_registry.get<PendingMeshHandle>(entity);
            removedRenderableMesh = true;
            if (pending.handle.vb.isValid() && m_vbAllocator) {
                m_vbAllocator->free(pending.handle.vb);
            }
            if (pending.handle.ib.isValid() && m_ibAllocator) {
                m_ibAllocator->free(pending.handle.ib);
            }
            if (m_gpuCulling && pending.handle.gpuCullingSlot != UINT32_MAX) {
                m_gpuCulling->freeSlot(pending.handle.gpuCullingSlot);
            }
        }
    }

    // Clean up version state
    removeChunkVersionState(this, entity);

    m_lodSystem.clearPending(entity);

    // Destroy entity
    {
        std::unique_lock regLock(m_registryMutex);
        m_registry.destroy(entity);
    }
    if (removedRenderableMesh) {
        recordMeshTopologyChange(coord);
    }
    m_streamingMetrics.chunksDestroyedTotal.fetch_add(1, std::memory_order_relaxed);
    
    return true;
}

// OPTIMIZATION: Batch destruction to reduce per-chunk lock overhead
int World::tryDestroyChunksBatch(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return 0;
    
    int destroyed = 0;
    std::vector<glm::ivec3> renderableChangedCoords;
    std::vector<entt::entity> entitiesToDestroy;
    std::vector<BufferSlice> buffersToFree;
    std::vector<uint32_t> gpuCullingSlotsToFree;
    
    entitiesToDestroy.reserve(coords.size());
    buffersToFree.reserve(coords.size() * 2); // VB + IB per chunk
    gpuCullingSlotsToFree.reserve(coords.size());
    
    // Phase 1: Collect entities and their GPU resources under registry lock
    for (const auto& coord : coords) {
        entt::entity entity = findChunk(coord);
        if (entity == entt::null) {
            clearChunkPending(coord);
            removeChunkState(coord);
            destroyed++;
            continue;
        }

        auto versionState = ensureChunkVersionState(this, entity);
        if (versionState) {
            // Version bump invalidates any stale pipeline output, so it's safe
            // to clear inFlight and destroy immediately.
            versionState->version.fetch_add(1, std::memory_order_acq_rel);
            versionState->inFlight.store(false, std::memory_order_release);
            versionState->pending.store(false, std::memory_order_release);
        }

        // Safe to destroy
        clearChunkPending(coord);
        removeChunkState(coord);
        m_lodSystem.clearDesiredLOD(coord);
        
        // Collect buffers and GPU culling slots to free (under registry lock)
        {
            std::shared_lock regLock(m_registryMutex);
            bool coordRemovedRenderable = false;
            if (m_registry.valid(entity) && m_registry.all_of<MeshHandle>(entity)) {
                const auto& meshHandle = m_registry.get<MeshHandle>(entity);
                coordRemovedRenderable = true;
                meshStatsSub(meshHandle);
                if (meshHandle.vb.isValid()) buffersToFree.push_back(meshHandle.vb);
                if (meshHandle.ib.isValid()) buffersToFree.push_back(meshHandle.ib);
                if (meshHandle.gpuCullingSlot != UINT32_MAX) {
                    gpuCullingSlotsToFree.push_back(meshHandle.gpuCullingSlot);
                }
            }
            
            // Also clean up staged PendingMeshHandle if present
            if (m_registry.valid(entity) && m_registry.all_of<PendingMeshHandle>(entity)) {
                const auto& pending = m_registry.get<PendingMeshHandle>(entity);
                coordRemovedRenderable = true;
                if (pending.handle.vb.isValid()) buffersToFree.push_back(pending.handle.vb);
                if (pending.handle.ib.isValid()) buffersToFree.push_back(pending.handle.ib);
                if (pending.handle.gpuCullingSlot != UINT32_MAX) {
                    gpuCullingSlotsToFree.push_back(pending.handle.gpuCullingSlot);
                }
            }
            
            // Remove physics collider if present
            if (m_physics && m_registry.valid(entity) && m_registry.all_of<ChunkCollider>(entity)) {
                auto& collider = m_registry.get<ChunkCollider>(entity);
                if (collider.isValid()) {
                    m_physics->removeBodyByIndexSeq(collider.bodyIdIndex,
                        static_cast<uint8_t>(collider.bodyIdSequence));
                }
            }
            if (coordRemovedRenderable) {
                renderableChangedCoords.push_back(coord);
            }
        }
        
        entitiesToDestroy.push_back(entity);
        removeChunkVersionState(this, entity);
        m_lodSystem.clearPending(entity);
    }
    
    // Phase 2: Free all GPU buffers (batched — single lock per allocator)
    if (m_vbAllocator && m_ibAllocator && !buffersToFree.empty()) {
        std::vector<BufferSlice> vbSlices;
        std::vector<BufferSlice> ibSlices;
        vbSlices.reserve(buffersToFree.size());
        ibSlices.reserve(buffersToFree.size());

        for (const auto& slice : buffersToFree) {
            if (slice.buffer == m_vbAllocator->getPrimaryBuffer()) {
                vbSlices.push_back(slice);
            } else if (slice.buffer == m_ibAllocator->getPrimaryBuffer()) {
                ibSlices.push_back(slice);
            }
        }

        if (!vbSlices.empty()) m_vbAllocator->freeBatch(vbSlices.data(), vbSlices.size());
        if (!ibSlices.empty()) m_ibAllocator->freeBatch(ibSlices.data(), ibSlices.size());
    }
    
    // Phase 2b: Free GPU culling slots (batched)
    if (m_gpuCulling && !gpuCullingSlotsToFree.empty()) {
        m_gpuCulling->freeSlots(
            gpuCullingSlotsToFree.data(),
            gpuCullingSlotsToFree.size());
    }
    
    // Phase 3: Destroy all entities (under registry lock to prevent races
    // with gatherDrawCommands and processUploads iterating the registry)
    if (!entitiesToDestroy.empty()) {
        std::unique_lock regLock(m_registryMutex);
        m_registry.destroy(entitiesToDestroy.begin(), entitiesToDestroy.end());
        regLock.unlock();
        destroyed += static_cast<int>(entitiesToDestroy.size());
        m_streamingMetrics.chunksDestroyedTotal.fetch_add(
            static_cast<uint32_t>(entitiesToDestroy.size()), std::memory_order_relaxed);
    }
    if (!renderableChangedCoords.empty()) {
        recordMeshTopologyChanges(renderableChangedCoords);
    }
    
    return destroyed;
}

void World::resetChunkGeneration() {
    std::cout << "[World] Reset starting..." << std::endl;
    
    // 1. Pause the background lifecycle thread and wait for any in-progress
    //    batch to finish. This ensures no callbacks are running while we
    //    tear down entities and GPU resources.
    m_lifecycleManager.pauseAndDrain();
    m_lifecycleManager.clearQueues();
    
    // 2. Clear the upload queue to drop any in-flight mesh data
    m_uploadSystem.clearQueue();

    // Flush old mesh buffers that were deliberately budgeted across frames.
    // Reset is already a heavyweight path, so avoid carrying retired slices
    // into the next generation.
    processDeferredMeshBufferFrees(
        m_vbAllocator,
        m_ibAllocator,
        m_pendingMeshBufferFrees.size());
    
    // 3. Invalidate ALL version states so any in-flight jobs become stale
    {
        std::scoped_lock versionLock(m_chunkVersionMutex);
        for (auto& [entity, vs] : m_chunkVersionStates) {
            if (vs) {
                vs->version.fetch_add(1, std::memory_order_acq_rel);
                vs->inFlight.store(false, std::memory_order_release);
                vs->pending.store(false, std::memory_order_release);
            }
        }
    }
    
    // 4. Collect ALL chunk entities for forced destruction
    std::vector<entt::entity> entitiesToDestroy;
    std::vector<BufferSlice> buffersToFree;
    
    {
        std::unique_lock regLock(m_registryMutex);
        auto view = m_registry.view<Chunk, ChunkCoord>();
        entitiesToDestroy.reserve(view.size_hint());
        buffersToFree.reserve(view.size_hint() * 2);
        
        for (auto entity : view) {
            auto& coord = m_registry.get<ChunkCoord>(entity);
            glm::ivec3 coordVec = coord.toVec3();
            
            clearChunkPending(coordVec);
            removeChunkState(coordVec);
            m_lodSystem.clearDesiredLOD(coordVec);
            
            // Collect GPU resources
            if (m_registry.all_of<MeshHandle>(entity)) {
                const auto& meshHandle = m_registry.get<MeshHandle>(entity);
                if (meshHandle.vb.isValid()) buffersToFree.push_back(meshHandle.vb);
                if (meshHandle.ib.isValid()) buffersToFree.push_back(meshHandle.ib);
            }
            
            // Also collect PendingMeshHandle resources
            if (m_registry.all_of<PendingMeshHandle>(entity)) {
                const auto& pending = m_registry.get<PendingMeshHandle>(entity);
                if (pending.handle.vb.isValid()) buffersToFree.push_back(pending.handle.vb);
                if (pending.handle.ib.isValid()) buffersToFree.push_back(pending.handle.ib);
            }
            
            // Remove physics collider
            if (m_physics && m_registry.all_of<ChunkCollider>(entity)) {
                auto& collider = m_registry.get<ChunkCollider>(entity);
                if (collider.isValid()) {
                    m_physics->removeBodyByIndexSeq(collider.bodyIdIndex,
                        static_cast<uint8_t>(collider.bodyIdSequence));
                }
            }
            
            removeChunkVersionState(this, entity);
            m_lodSystem.clearPending(entity);
            entitiesToDestroy.push_back(entity);
        }
    }
    
    // 5. Free GPU buffers
    if (m_vbAllocator && m_ibAllocator) {
        for (const auto& slice : buffersToFree) {
            if (slice.buffer == m_vbAllocator->getPrimaryBuffer()) {
                m_vbAllocator->free(slice);
            } else if (slice.buffer == m_ibAllocator->getPrimaryBuffer()) {
                m_ibAllocator->free(slice);
            }
        }
    }
    
    // 6. Free GPU culling slots — reset the entire slot allocator
    //    since we're destroying everything. This also clears pending
    //    invalidations and resets the high-water mark.
    if (m_gpuCulling) {
        m_gpuCulling->resetAllSlots();
    }
    
    // 7. Destroy all entities
    if (!entitiesToDestroy.empty()) {
        std::unique_lock regLock(m_registryMutex);
        m_registry.destroy(entitiesToDestroy.begin(), entitiesToDestroy.end());
        m_streamingMetrics.chunksDestroyedTotal.fetch_add(
            static_cast<uint32_t>(entitiesToDestroy.size()), std::memory_order_relaxed);
        recordGlobalMeshTopologyChange();
        std::cout << "[World] Force-destroyed " << entitiesToDestroy.size() << " chunks" << std::endl;
    }
    
    // 8. Clear all tracking sets and reset counters
    {
        std::unique_lock lock(m_chunkSetMutex);
        m_readyChunkSet.clear();
        m_existingChunkSet.clear();
    }
    m_pendingChunks.clear();
    m_loadingCount.store(0, std::memory_order_relaxed);
    m_meshingCount.store(0, std::memory_order_relaxed);
    m_readyCount.store(0, std::memory_order_relaxed);
    
    // 9. Clear remaining version states
    {
        std::scoped_lock versionLock(m_chunkVersionMutex);
        m_chunkVersionStates.clear();
    }
    
    // 10. Reset chunk manager
    m_chunkManager->resetChunkGeneration();
    
    // 11. Resume background lifecycle thread
    m_lifecycleManager.resume();
    
    std::cout << "[World] Reset complete - all chunks cleared" << std::endl;
}

void World::switchTerrainFile(const std::string& newTerrainPath) {
    std::cout << "[World] Switching terrain file to: " << newTerrainPath << std::endl;
    
    // 1. Destroy all existing chunks and GPU resources
    resetChunkGeneration();
    
    // 2. Reload the terrain file loader with the new path
    m_terrainLoader = std::make_unique<TerrainFileLoader>(newTerrainPath);
    
    if (m_terrainLoader->isLoaded()) {
        // Update terrain center on ChunkManager based on new terrain dimensions
        auto dims = m_terrainLoader->getDimensions();
        if (dims.chunksX > 0 && dims.chunksZ > 0) {
            m_chunkManager->setTerrainCenter(dims.chunksX, dims.chunksZ);
        }
        std::cout << "[World] New terrain loaded: " << dims.chunksX << "x" << dims.chunksZ 
                  << " chunks, " << dims.lodLevels << " LOD levels" << std::endl;
    } else {
        std::cerr << "[World] WARNING: Failed to load terrain file: " << newTerrainPath << std::endl;
    }
}

````

## src\world\chunks\core\ChunkManager.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/core/ChunkManager.h"
#include "world/config/WorldConfig.h"
#include <algorithm>
#include <iostream>
#include <cmath>

ChunkManager::ChunkManager() {
    setRenderDistanceRings(80); // 4×20 ring bands for 160x160 world
    m_effectiveRenderDist = m_renderDistanceRings;
}

void ChunkManager::setRenderDistanceRings(int rings) {
    if (rings < 1) rings = 1;
    if (rings > 500) rings = 500; // Reasonable limit for voxel games
    
    int oldRings = m_renderDistanceRings;
    m_renderDistanceRings = rings;
    if (oldRings != rings) {
        std::cout << "[ChunkManager] Render distance " << oldRings << " -> " << rings
                  << " rings (emittedUpTo=" << m_emittedUpToRing
                  << ", currentRing=" << m_ringState.currentRing << ")" << std::endl;
    }
}

void ChunkManager::setTerrainCenter(uint32_t chunksX, uint32_t chunksZ) {
    m_terrainChunksX = chunksX;
    m_terrainChunksZ = chunksZ;
    m_terrainCenterSet = true;
    
    // Don't set center here — it will be set from player position in update()
    // But compute terrain center for render distance capping
    int terrainCenterX = static_cast<int>(chunksX / 2);
    int terrainCenterZ = static_cast<int>(chunksZ / 2);
    
    // Cap render distance to terrain size (max distance from center to edge)
    int maxRingX = std::max(terrainCenterX, static_cast<int>(chunksX) - terrainCenterX - 1);
    int maxRingZ = std::max(terrainCenterZ, static_cast<int>(chunksZ) - terrainCenterZ - 1);
    int maxTerrainRing = std::max(maxRingX, maxRingZ) + 1; // +1 because rings start at 1
    
    if (m_renderDistanceRings > maxTerrainRing) {
        std::cout << "[ChunkManager] Capping render distance from " << m_renderDistanceRings 
                  << " to " << maxTerrainRing << " (terrain bounds)" << std::endl;
        m_renderDistanceRings = maxTerrainRing;
    }
    
    std::cout << "[ChunkManager] Terrain bounds set: " << chunksX << "x" << chunksZ 
              << " chunks (center follows player)" << std::endl;
}

void ChunkManager::resetChunkGeneration() {
    m_ringState.currentRing = 0;
    m_ringState.currentChunkIndex = 0;
    m_ringState.ringComplete = false;
    m_ringState.chunksInRing = 0;
    m_emittedUpToRing = 0;
    m_centerInitialized = false;
    
    // Reset adaptive render distance state so the system starts fresh
    // instead of inheriting a shrunken effectiveRenderDist from before reset.
    m_effectiveRenderDist = m_renderDistanceRings;
    m_adaptWarmupTimer = 0.0f;
    m_shrinkConfirmFrames = 0;
    m_measuredThroughput = 5000.0f;
    m_throughputTimer = 0.0f;
    m_chunksCompletedWindow.store(0, std::memory_order_relaxed);
    m_growBackTimer = 0.0f;
    
    // Clear pending ops — all in-flight work is being discarded by World::resetChunkGeneration
    {
        std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
        m_pendingOps.clear();
    }
    m_pendingCreateCount.store(0, std::memory_order_relaxed);
    m_pendingDestroyCount.store(0, std::memory_order_relaxed);
    
    std::cout << "[ChunkManager] Reset chunk generation - restarting from center" << std::endl;
}

void ChunkManager::setRingProgress(int completedRing) {
    if (completedRing < 0) completedRing = 0;
    m_ringState.currentRing = completedRing;
    m_ringState.currentChunkIndex = 0;
    m_ringState.ringComplete = true;
    m_ringState.chunksInRing = 0;
    m_emittedUpToRing = completedRing;
}

void ChunkManager::clampRingProgress(int maxRing) {
    if (maxRing < 0) maxRing = 0;
    int oldCurrent = m_ringState.currentRing;
    int oldEmitted = m_emittedUpToRing;
    if (m_ringState.currentRing > maxRing) {
        m_ringState.currentRing = maxRing;
        m_ringState.currentChunkIndex = 0;
        m_ringState.ringComplete = true;
        m_ringState.chunksInRing = 0;
    }
    if (m_emittedUpToRing > maxRing) {
        m_emittedUpToRing = maxRing;
    }
    if (oldCurrent != m_ringState.currentRing || oldEmitted != m_emittedUpToRing) {
        std::cout << "[ChunkManager] clampRingProgress(" << maxRing << "): "
                  << "currentRing " << oldCurrent << "->" << m_ringState.currentRing
                  << ", emittedUpTo " << oldEmitted << "->" << m_emittedUpToRing << std::endl;
    }
}

void ChunkManager::resetAdaptWarmup() {
    m_adaptWarmupTimer = 0.0f;
    m_shrinkConfirmFrames = 0;
}

void ChunkManager::update(float deltaTime,
                          const glm::vec3& cameraWorldPos,
                          const std::unordered_set<glm::ivec3, IVec3Hash>& readyChunks,
                          const std::unordered_set<glm::ivec3, IVec3Hash>& existingChunks,
                          std::vector<ChunkCreateRequest>& outChunksToCreate,
                          std::vector<glm::ivec3>& outChunksToDestroy,
                          bool bufferLimitReached) {
    outChunksToCreate.clear();
    outChunksToDestroy.clear();
    m_centerChanged = false;
    m_coalescedThisFrame = 0;
    
    // Skip chunk creation if paused
    if (m_paused) {
        return;
    }

    // ---------------------------------------------------------------
    // FAST IDLE CHECK: skip all work when nothing is happening.
    // Center hasn't changed, no pending ops, rings fully emitted.
    // ---------------------------------------------------------------
    bool hasPending = (m_pendingCreateCount.load(std::memory_order_relaxed) > 0) ||
                      (m_pendingDestroyCount.load(std::memory_order_relaxed) > 0);

    // ---------------------------------------------------------------
    // SPEED TRACKING (smoothed, for budget calculation only — no LOD changes)
    // ---------------------------------------------------------------
    if (deltaTime > 0.0f && m_prevCameraPosValid) {
        glm::vec3 posDelta = cameraWorldPos - m_prevCameraWorldPos;
        float rawSpeed = glm::length(posDelta) / deltaTime;
        // Cap at ~500 m/s to absorb teleport / first-frame spikes
        rawSpeed = std::min(rawSpeed, 500.0f);
        // Reject spike frames: if dt > 50ms the sample is unreliable.
        // Keep the old smoothed value instead of polluting it.
        if (deltaTime < 0.05f) {
            m_playerSpeedMps = m_playerSpeedMps * (1.0f - SPEED_ALPHA) + rawSpeed * SPEED_ALPHA;
        }
    }
    m_prevCameraWorldPos = cameraWorldPos;
    m_prevCameraPosValid = true;

    // Center chunk follows the player's camera position
    glm::ivec3 playerChunk = WorldConfig::microVoxelToChunk(
        WorldConfig::worldToMicroVoxel(cameraWorldPos)
    );
    playerChunk.y = 0; // Y=0 (single layer terrain)
    
    // Clamp to terrain bounds if terrain is loaded
    if (m_terrainCenterSet) {
        playerChunk.x = std::max(0, std::min(playerChunk.x, static_cast<int>(m_terrainChunksX) - 1));
        playerChunk.z = std::max(0, std::min(playerChunk.z, static_cast<int>(m_terrainChunksZ) - 1));
    }
    
    // ---------------------------------------------------------------
    // AUTO-COMPUTE EXTENSION RINGS
    // ---------------------------------------------------------------
    if (m_terrainCenterSet && m_centerInitialized) {
        computeExtensionRings();
    }
    
    // ---------------------------------------------------------------
    // BUDGET-ADAPTIVE RENDER DISTANCE
    // ---------------------------------------------------------------
    // Adapt render distance based on measured throughput.
    // This ONLY shrinks/grows the total distance — LOD bands are immutable.
    m_bufferPressure = bufferLimitReached;
    int prevEffectiveDist = m_effectiveRenderDist;
    if (deltaTime > 0.0f) {
        adaptRenderDistance(deltaTime);
    }
    
    int effectiveDist = m_effectiveRenderDist;

    // ---------------------------------------------------------------
    // STATIONARY SHRINK DESTROY: when adaptRenderDistance shrinks the
    // effective distance, destroy chunks beyond the new boundary even
    // when the player hasn't moved.  Without this, the chunks stay
    // alive and their VB/IB keeps buffer utilization high, creating
    // a feedback loop where pressure never drops.
    // ---------------------------------------------------------------
    if (m_centerInitialized && effectiveDist < prevEffectiveDist) {
        std::vector<std::pair<uint64_t, glm::ivec3>> shrinkDestroyCandidates;
        for (int ring = effectiveDist + 1; ring <= prevEffectiveDist; ++ring) {
            int chebyshevR = ring - 1;
            int chunksInRing = (ring == 1) ? 1 : 8 * chebyshevR;
            for (int idx = 1; idx <= chunksInRing; ++idx) {
                glm::ivec3 coord = getChunkAtIndex(chebyshevR, idx, m_centerChunk, m_ringState.facingDir);
                if (m_terrainCenterSet &&
                    (coord.x < 0 || coord.x >= static_cast<int>(m_terrainChunksX) ||
                     coord.z < 0 || coord.z >= static_cast<int>(m_terrainChunksZ))) {
                    continue;
                }
                if (existingChunks.find(coord) == existingChunks.end()) continue;
                shrinkDestroyCandidates.push_back({packCoord(coord.x, coord.z), coord});
            }
        }
        if (!shrinkDestroyCandidates.empty()) {
            std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
            for (auto& [key, coord] : shrinkDestroyCandidates) {
                auto it = m_pendingOps.find(key);
                if (it != m_pendingOps.end() && it->second.type == PendingOpType::Create) {
                    m_pendingOps.erase(it);
                    m_pendingCreateCount.fetch_sub(1, std::memory_order_relaxed);
                    m_coalescedThisFrame++;
                } else if (it == m_pendingOps.end() || it->second.type != PendingOpType::Destroy) {
                    m_pendingOps[key] = {PendingOpType::Destroy, 0};
                    m_pendingDestroyCount.fetch_add(1, std::memory_order_relaxed);
                    outChunksToDestroy.push_back(coord);
                }
            }
        }
        // Lower the emitted watermark so processRingConstruct can
        // re-emit these rings if the distance grows back later.
        if (m_emittedUpToRing > effectiveDist) {
            m_emittedUpToRing = effectiveDist;
        }
    }

    // ---------------------------------------------------------------
    // DIFFERENTIAL CENTER CHANGE (with operation coalescing)
    // ---------------------------------------------------------------
    if (m_centerInitialized && playerChunk != m_centerChunk) {
        glm::ivec3 oldCenter = m_centerChunk;
        m_prevCenterChunk = oldCenter;
        m_centerChunk = playerChunk;
        m_centerChanged = true;
        
        // Derive facing direction from MOVEMENT direction (not camera).
        glm::ivec3 delta = m_centerChunk - oldCenter;
        if (delta.x != 0 || delta.z != 0) {
            glm::vec3 moveDir(static_cast<float>(delta.x), 0.0f, static_cast<float>(delta.z));
            m_ringState.facingDir = calculateFacingDirection(moveDir);
        }
        
        // Recompute extension for new center position.
        // Do NOT call adaptRenderDistance again — it already ran above
        // with the same deltaTime.  Calling it twice double-counts the
        // warmup timer, shrink confirmations, and throughput window,
        // causing premature shrinks that destroy in-range chunks.
        if (m_terrainCenterSet) {
            computeExtensionRings();
            // Re-clamp effective dist to new base (extension may have changed)
            int newBase = m_renderDistanceRings + m_extensionRings;
            if (m_effectiveRenderDist > newBase) m_effectiveRenderDist = newBase;
            effectiveDist = m_effectiveRenderDist;
        }
        
        // ----- TRAILING EDGE: destroy chunks outside effective render distance -----
        // Instead of scanning all ~26K existing chunks O(N), enumerate only the
        // outermost rings that could have fallen out of range.  A 1-chunk move
        // shifts boundaries by at most moveDist rings, so we only need to check
        // rings [effectiveDist - moveDist, effectiveDist + moveDist + 1].
        // For the old center, chunks at those rings around the old center are the
        // only candidates that might now have newRing >= effectiveDist.
        int moveDx = std::abs(m_centerChunk.x - oldCenter.x);
        int moveDz = std::abs(m_centerChunk.z - oldCenter.z);
        int moveDist = std::max(moveDx, moveDz);
        
        int startRing = std::max(1, effectiveDist - moveDist);
        int endRing = effectiveDist + moveDist + 1;
        
        // Collect destroy candidates WITHOUT holding m_pendingOpsMutex
        // (ring enumeration, existingChunks lookup, and distance math are lock-free)
        std::vector<std::pair<uint64_t, glm::ivec3>> destroyCandidates;
        for (int ring = startRing; ring <= endRing; ++ring) {
            int chebyshevR = ring - 1;
            int chunksInRing = (ring == 1) ? 1 : 8 * chebyshevR;

            for (int idx = 1; idx <= chunksInRing; ++idx) {
                glm::ivec3 coord = getChunkAtIndex(chebyshevR, idx, oldCenter, m_ringState.facingDir);
                if (existingChunks.find(coord) == existingChunks.end()) continue;
                int newRing = calculateRingNumber(coord, m_centerChunk);
                if (newRing >= effectiveDist) {
                    destroyCandidates.push_back({packCoord(coord.x, coord.z), coord});
                }
            }
        }

        // Single lock to batch-update pendingOps
        if (!destroyCandidates.empty()) {
            std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
            for (auto& [key, coord] : destroyCandidates) {
                auto it = m_pendingOps.find(key);

                if (it != m_pendingOps.end() && it->second.type == PendingOpType::Create) {
                    m_pendingOps.erase(it);
                    m_pendingCreateCount.fetch_sub(1, std::memory_order_relaxed);
                    m_coalescedThisFrame++;
                    continue;  // Coalesced: cancel create, don't add destroy
                }

                if (it != m_pendingOps.end() && it->second.type == PendingOpType::Destroy) {
                    m_coalescedThisFrame++;
                    continue;
                }

                m_pendingOps[key] = {PendingOpType::Destroy, 0};
                m_pendingDestroyCount.fetch_add(1, std::memory_order_relaxed);
                outChunksToDestroy.push_back(coord);
            }
        }
        
        // Preserve ring progress — clamp down by movement magnitude
        // (moveDist already computed in trailing edge above)
        int newProgress = std::max(0, m_ringState.currentRing - moveDist);
        m_ringState.currentRing = newProgress;
        m_ringState.currentChunkIndex = 0;
        m_ringState.ringComplete = (newProgress > 0);
        m_ringState.chunksInRing = 0;

        // --- O1: Differential leading edge ---
        // For small moves (≤5 chunks), compute only the L-shaped strip that
        // entered the render area.  This is O(strip) instead of O(total_chunks).
        // For large teleports, fall back to full ring re-emission.
        if (moveDist <= 5) {
            processLeadingEdge(oldCenter, m_centerChunk, effectiveDist, existingChunks, outChunksToCreate);
            m_emittedUpToRing = effectiveDist; // All rings accounted for
        } else {
            m_emittedUpToRing = newProgress;   // processRingConstruct handles re-emission
        }
        
    } else if (!m_centerInitialized) {
        m_centerChunk = playerChunk;
        m_prevCenterChunk = playerChunk;
        m_centerInitialized = true;
        m_centerChanged = true;
        m_adaptWarmupTimer = 0.0f;  // Reset warmup on first center init
        
        if (m_terrainCenterSet) {
            computeExtensionRings();
            adaptRenderDistance(deltaTime);
            effectiveDist = m_effectiveRenderDist;
        }
        
        std::cout << "[ChunkManager] Initial center at player chunk (" << m_centerChunk.x << ", " << m_centerChunk.y << ", " << m_centerChunk.z << ")" << std::endl;
        std::cout << "[ChunkManager] Effective render distance: " << effectiveDist
                  << " rings (base=" << (m_renderDistanceRings + m_extensionRings)
                  << ")" << std::endl;
    }

    // Fast idle path: no center change, no pending work, rings fully emitted
    if (!m_centerChanged && !hasPending && m_emittedUpToRing >= m_effectiveRenderDist) {
        return;
    }

    // Emit all remaining rings — no throttling
    if (m_ringState.currentRing < effectiveDist || m_emittedUpToRing < effectiveDist) {
        processRingConstruct(readyChunks, existingChunks, outChunksToCreate);
    }
}

// ---------------------------------------------------------------
// Effective Render Distance
// ---------------------------------------------------------------
// Keep the full requested distance active. We still track throughput for
// diagnostics, but movement no longer shrinks render distance to satisfy a
// streaming budget.
// ---------------------------------------------------------------
void ChunkManager::adaptRenderDistance(float deltaTime) {
    const int baseEffective = m_renderDistanceRings + m_extensionRings;

    m_adaptWarmupTimer += deltaTime;

    m_throughputTimer += deltaTime;
    if (m_throughputTimer >= THROUGHPUT_WINDOW) {
        float newThroughput = static_cast<float>(m_chunksCompletedWindow.exchange(0, std::memory_order_acq_rel)) / m_throughputTimer;
        if (m_measuredThroughput >= 4999.0f) {
            m_measuredThroughput = newThroughput;
        } else {
            float alpha = (newThroughput >= m_measuredThroughput) ? 0.7f : 0.3f;
            m_measuredThroughput = m_measuredThroughput * (1.0f - alpha) + newThroughput * alpha;
        }
        m_throughputTimer = 0.0f;
    }

    m_effectiveRenderDist = std::clamp(baseEffective, 1, 500);
    m_growBackTimer = 0.0f;
    m_shrinkConfirmFrames = 0;
}



// ---------------------------------------------------------------
// Extension Ring Computation
// ---------------------------------------------------------------
// When the player is off-center, one side of the world is closer
// than the other. Extension rings ensure LOD 3 reaches the far edge.
// Example: player at chunk (120, 0, 80) in a 160×160 world:
//   - Distance to right edge: 160-120-1 = 39 chunks (within base 80)
//   - Distance to left edge: 120 chunks (needs 40 extension rings)
//   - Extension = max(0, 120 - 80) = 40
// ---------------------------------------------------------------
void ChunkManager::computeExtensionRings() {
    if (!m_terrainCenterSet || !m_extensionEnabled) {
        m_extensionRings = 0;
        return;
    }
    
    // Max Chebyshev distance from player to any terrain edge
    int distToLeft   = m_centerChunk.x;
    int distToRight  = static_cast<int>(m_terrainChunksX) - 1 - m_centerChunk.x;
    int distToBottom = m_centerChunk.z;
    int distToTop    = static_cast<int>(m_terrainChunksZ) - 1 - m_centerChunk.z;
    
    int maxDistToEdge = std::max({distToLeft, distToRight, distToBottom, distToTop});
    
    // Extension = how many rings beyond base render distance we need
    // to cover the full terrain from this position.
    // +1 for fence-post: ring N covers Chebyshev distance N from center.
    m_extensionRings = std::max(0, maxDistToEdge + 1 - m_renderDistanceRings);
}

ChunkManager::DebugInfo ChunkManager::getDebugInfo() const {
    DebugInfo info;
    
    // Completed ring is the ring before current (if any chunks exist)
    if (m_ringState.currentRing > 1) {
        info.completedRing = m_ringState.currentRing - 1;
    } else {
        info.completedRing = 0;
    }
    
    // Current ring being constructed
    info.currentRing = m_ringState.currentRing;
    info.currentRingProgress = m_ringState.currentChunkIndex;
    info.currentRingTotal = m_ringState.chunksInRing;
    
    // Facing direction as string
    switch (m_ringState.facingDir) {
        case FacingDirection::NORTH: info.facingDirection = "NORTH"; break;
        case FacingDirection::EAST:  info.facingDirection = "EAST"; break;
        case FacingDirection::SOUTH: info.facingDirection = "SOUTH"; break;
        case FacingDirection::WEST:  info.facingDirection = "WEST"; break;
    }
    
    // Budget-adaptive state
    info.playerSpeedMps = m_playerSpeedMps;
    info.baseRenderDist = m_renderDistanceRings + m_extensionRings;
    info.effectiveRenderDist = m_effectiveRenderDist;
    info.extensionRings = m_extensionRings;
    info.measuredThroughput = m_measuredThroughput;
    info.coalescedOps = m_coalescedThisFrame;
    
    // Use O(1) atomic counters instead of iterating m_pendingOps
    info.pendingCreates = m_pendingCreateCount.load(std::memory_order_relaxed);
    info.pendingDestroys = m_pendingDestroyCount.load(std::memory_order_relaxed);
    
    return info;
}




````

## src\world\chunks\core\ChunkManagerRings.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/core/ChunkManager.h"
#include <algorithm>
#include <iostream>
#include <cmath>

int ChunkManager::calculateRingNumber(const glm::ivec3& chunkCoord, const glm::ivec3& center) const {
    int dx = std::abs(chunkCoord.x - center.x);
    int dz = std::abs(chunkCoord.z - center.z);
    
    // Ring number = Chebyshev distance (max of dx, dz)
    // This creates perfect square rings
    // Ring 0 = center, Ring 1 = 8 surrounding, Ring 2 = next 16, etc.
    return std::max(dx, dz);
}

FacingDirection ChunkManager::calculateFacingDirection(const glm::vec3& forward) const {
    // Determine primary direction from camera forward vector
    // Forward vector matches Engine's camera formula: x=cos(yaw), z=sin(yaw)
    float absX = std::abs(forward.x);
    float absZ = std::abs(forward.z);
    
    if (absX > absZ) {
        // East/West dominant
        return (forward.x > 0) ? FacingDirection::EAST : FacingDirection::WEST;
    } else {
        // North/South dominant
        return (forward.z > 0) ? FacingDirection::NORTH : FacingDirection::SOUTH;
    }
}

glm::ivec3 ChunkManager::getChunkAtIndex(int ringNumber, int chunkIndex, const glm::ivec3& center, FacingDirection facing) const {
    if (ringNumber == 0) {
        return center; // Ring 0 = center chunk
    }
    
    int R = ringNumber;
    int idx = chunkIndex - 1; // Convert to 0-based index
    int edgeLength = 2 * R + 1; // Each edge has (2R+1) chunks corner to corner
    
    // Clockwise snake pattern around perimeter
    // Edge 1: indices 0 to (2R)         - edgeLength chunks (2R+1)
    // Edge 2: indices (2R+1) to (4R)    - 2R chunks (skip shared corner)
    // Edge 3: indices (4R+1) to (6R)    - 2R chunks (skip shared corner)
    // Edge 4: indices (6R+1) to (8R-1)  - 2R chunks (skip shared corner, which is chunk 0)
    
    glm::ivec3 pos = center;
    
    // Starting corner rotates clockwise with facing direction:
    // NORTH: (-R, -R) top-left, EAST: (R, -R) top-right, SOUTH: (R, R) bottom-right, WEST: (-R, R) bottom-left
    // All directions use same clockwise pattern: RIGHT → DOWN → LEFT → UP
    
    if (idx <= 2 * R) {
        // Edge 1: First edge from starting corner (clockwise)
        switch (facing) {
            case FacingDirection::NORTH: // LEFT from (R, R): X decreases, Z=R
                pos.x = center.x + R - idx;
                pos.z = center.z + R;
                break;
            case FacingDirection::EAST:   // DOWN from (R, -R): Z increases, X=R
                pos.x = center.x + R;
                pos.z = center.z - R + idx;
                break;
            case FacingDirection::SOUTH:  // RIGHT from (-R, -R): X increases, Z=-R
                pos.x = center.x - R + idx;
                pos.z = center.z - R;
                break;
            case FacingDirection::WEST:   // UP from (-R, R): Z decreases, X=-R
                pos.x = center.x - R;
                pos.z = center.z + R - idx;
                break;
        }
    } else if (idx <= 4 * R) {
        // Edge 2: Second edge (clockwise continuation)
        int edgeIdx = idx - (2 * R + 1); // 0 to (2R-1)
        switch (facing) {
            case FacingDirection::NORTH:  // UP from (-R, R): Z decreases, X=-R
                pos.x = center.x - R;
                pos.z = center.z + R - 1 - edgeIdx;
                break;
            case FacingDirection::EAST:   // LEFT from (R, R): X decreases, Z=R
                pos.x = center.x + R - 1 - edgeIdx;
                pos.z = center.z + R;
                break;
            case FacingDirection::SOUTH:  // DOWN from (R, -R): Z increases, X=R
                pos.x = center.x + R;
                pos.z = center.z - R + 1 + edgeIdx;
                break;
            case FacingDirection::WEST:   // RIGHT from (-R, -R): X increases, Z=-R
                pos.x = center.x - R + 1 + edgeIdx;
                pos.z = center.z - R;
                break;
        }
    } else if (idx <= 6 * R) {
        // Edge 3: Third edge (clockwise continuation)
        int edgeIdx = idx - (4 * R + 1); // 0 to (2R-1)
        switch (facing) {
            case FacingDirection::NORTH:  // RIGHT from (-R, -R): X increases, Z=-R
                pos.x = center.x - R + 1 + edgeIdx;
                pos.z = center.z - R;
                break;
            case FacingDirection::EAST:   // UP from (-R, R): Z decreases, X=-R
                pos.x = center.x - R;
                pos.z = center.z + R - 1 - edgeIdx;
                break;
            case FacingDirection::SOUTH:  // LEFT from (R, R): X decreases, Z=R
                pos.x = center.x + R - 1 - edgeIdx;
                pos.z = center.z + R;
                break;
            case FacingDirection::WEST:   // DOWN from (R, -R): Z increases, X=R
                pos.x = center.x + R;
                pos.z = center.z - R + 1 + edgeIdx;
                break;
        }
    } else {
        // Edge 4: Fourth edge (back to start, clockwise)
        int edgeIdx = idx - (6 * R + 1); // 0 to (2R-2) - stops before starting corner
        switch (facing) {
            case FacingDirection::NORTH:  // DOWN from (R, -R): Z increases, X=R
                pos.x = center.x + R;
                pos.z = center.z - R + 1 + edgeIdx;
                break;
            case FacingDirection::EAST:   // LEFT from (-R, -R): X increases, Z=-R
                pos.x = center.x - R + 1 + edgeIdx;
                pos.z = center.z - R;
                break;
            case FacingDirection::SOUTH:  // UP from (-R, R): Z decreases, X=-R
                pos.x = center.x - R;
                pos.z = center.z + R - 1 - edgeIdx;
                break;
            case FacingDirection::WEST:   // RIGHT from (R, R): X decreases, Z=R
                pos.x = center.x + R - 1 - edgeIdx;
                pos.z = center.z + R;
                break;
        }
    }
    
    return pos;
}

bool ChunkManager::isInRingDistance(const glm::ivec3& chunkCoord) const {
    int ring = calculateRingNumber(chunkCoord, m_centerChunk);
    return ring < m_effectiveRenderDist; // Within active render distance
}

int ChunkManager::calculateLODFromRing(int ringNumber) const {
    // 4×20 ring LOD bands (uneven ring system):
    // LOD 0: rings 0–20  → 41×41 area (1,681 chunks, full detail)
    // LOD 1: rings 21–40 → 81×81 area (4,880 chunks)
    // LOD 2: rings 41–60 → 121×121 area (8,080 chunks)
    // LOD 3: rings 61–80 → 161×161 area (11,280 chunks)
    // Extension rings beyond 80 also use LOD 3 (extends last LOD band)
    // LOD bands are NEVER modified by this helper.
    
    if (!m_lodEnabled) return 0;      // LOD disabled: all chunks at highest detail
    
    if (ringNumber <= m_lod0Max) return 0;  // High detail near player
    if (ringNumber <= m_lod1Max) return 1;  // Medium detail
    if (ringNumber <= m_lod2Max) return 2;  // Far detail
    return 3;                                // Very far + extension (always LOD 3)
}

void ChunkManager::setLODRingThresholds(int lod0Max, int lod1Max, int lod2Max, int lod3Max) {
    m_lod0Max = lod0Max;
    m_lod1Max = lod1Max;
    m_lod2Max = lod2Max;
    m_lod3Max = lod3Max;
}

void ChunkManager::setLODRingThresholds(const std::vector<int>& thresholds) {
    m_lod0Max = thresholds.size() > 0 ? thresholds[0] : 9999;
    m_lod1Max = thresholds.size() > 1 ? thresholds[1] : 9999;
    m_lod2Max = thresholds.size() > 2 ? thresholds[2] : 9999;
    m_lod3Max = thresholds.size() > 3 ? thresholds[3] : 9999;
}

uint8_t ChunkManager::getSeamEdgeMask(const glm::ivec3& chunkCoord, const glm::ivec3& center) const {
    // Returns bitmask: bit 0 = NEG_X (West), bit 1 = POS_X (East), 
    //                  bit 2 = NEG_Z (South), bit 3 = POS_Z (North)
    // Seams are needed at LOD boundary rings to prevent cracks.
    // Uses effective (speed-adjusted) LOD thresholds for consistency.
    
    if (!m_lodEnabled) return 0; // No seams needed if LOD disabled
    
    uint8_t seamMask = 0;
    
    int thisRing = calculateRingNumber(chunkCoord, center);
    int thisLOD = calculateLODFromRing(thisRing);
    
    // Check each neighbor direction — seam needed when neighbor has different LOD
    auto checkNeighbor = [&](const glm::ivec3& offset, int bit) {
        glm::ivec3 neighbor = chunkCoord + offset;
        int neighborRing = calculateRingNumber(neighbor, center);
        int neighborLOD = calculateLODFromRing(neighborRing);
        if (neighborLOD != thisLOD) seamMask |= (1 << bit);
    };
    
    checkNeighbor(glm::ivec3(-1, 0, 0), 0);  // NEG_X (West)
    checkNeighbor(glm::ivec3( 1, 0, 0), 1);  // POS_X (East)
    checkNeighbor(glm::ivec3( 0, 0,-1), 2);  // NEG_Z (South)
    checkNeighbor(glm::ivec3( 0, 0, 1), 3);  // POS_Z (North)
    
    return seamMask;
}

void ChunkManager::processRingConstruct(const std::unordered_set<glm::ivec3, IVec3Hash>& /* readyChunks */,
                                        const std::unordered_set<glm::ivec3, IVec3Hash>& existingChunks,
                                        std::vector<ChunkCreateRequest>& outChunksToCreate) {
    // ---------------------------------------------------------------
    // Unrestricted ring emission with the active render distance
    // ---------------------------------------------------------------
    // Uses m_effectiveRenderDist as the current world coverage target.
    // ---------------------------------------------------------------
    int effectiveDist = m_effectiveRenderDist;

    // Phase 1: Advance ring pointer through fully-existing rings
    while (m_ringState.currentRing < effectiveDist) {
        int ring = m_ringState.currentRing + 1;
        int chebyshevR = ring - 1;  // Ring 1 = Chebyshev 0 (center)
        int chunksInRing = (ring == 1) ? 1 : 8 * chebyshevR;
        bool allExist = true;

        for (int idx = 1; idx <= chunksInRing; ++idx) {
            glm::ivec3 pos = getChunkAtIndex(chebyshevR, idx, m_centerChunk, m_ringState.facingDir);
            if (m_terrainCenterSet &&
                (pos.x < 0 || pos.x >= static_cast<int>(m_terrainChunksX) ||
                 pos.z < 0 || pos.z >= static_cast<int>(m_terrainChunksZ))) {
                continue; // OOB — doesn't count
            }
            if (existingChunks.find(pos) == existingChunks.end()) {
                allExist = false;
                break;
            }
        }

        if (!allExist) break;
        m_ringState.currentRing = ring;
        m_ringState.ringComplete = true;
    }

    // Phase 2: Emit create requests for every ring above m_emittedUpToRing
    // --- O4: Collect candidates first, then batch pending-ops under single lock ---
    struct RingCandidate {
        glm::ivec3 pos;
        int lodLevel;
    };
    std::vector<RingCandidate> ringCandidates;

    for (int ring = m_emittedUpToRing + 1; ring <= effectiveDist; ++ring) {
        int chebyshevR = ring - 1;
        int chunksInRing = (ring == 1) ? 1 : 8 * chebyshevR;

        for (int idx = 1; idx <= chunksInRing; ++idx) {
            glm::ivec3 pos = getChunkAtIndex(chebyshevR, idx, m_centerChunk, m_ringState.facingDir);
            if (m_terrainCenterSet &&
                (pos.x < 0 || pos.x >= static_cast<int>(m_terrainChunksX) ||
                 pos.z < 0 || pos.z >= static_cast<int>(m_terrainChunksZ))) {
                continue;
            }
            if (existingChunks.find(pos) == existingChunks.end()) {
                ringCandidates.push_back({pos, calculateLODFromRing(chebyshevR)});
            }
        }
    }

    // Single lock for all pending-ops mutations
    int emitStartRing = m_emittedUpToRing + 1;
    if (!ringCandidates.empty()) {
        std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
        for (const auto& c : ringCandidates) {
            uint64_t key = packCoord(c.pos.x, c.pos.z);
            auto it = m_pendingOps.find(key);

            if (it != m_pendingOps.end()) {
                if (it->second.type == PendingOpType::Destroy) {
                    m_pendingOps.erase(it);
                    m_pendingDestroyCount.fetch_sub(1, std::memory_order_relaxed);
                    m_coalescedThisFrame++;
                    continue;
                }
                m_coalescedThisFrame++;
                continue;
            }

            ChunkCreateRequest req;
            req.coord = c.pos;
            req.lodLevel = c.lodLevel;
            m_pendingOps[key] = {PendingOpType::Create, req.lodLevel};
            m_pendingCreateCount.fetch_add(1, std::memory_order_relaxed);
            outChunksToCreate.push_back(req);
        }
    }
    m_emittedUpToRing = effectiveDist;

    // Log ring emission progress when new chunks are created
    if (!outChunksToCreate.empty()) {
        std::cout << "[ChunkManager] Ring emit: " << outChunksToCreate.size()
                  << " chunks queued for rings " << emitStartRing
                  << "-" << effectiveDist
                  << " (completedRing=" << m_ringState.currentRing
                  << ", emittedUpTo=" << m_emittedUpToRing << ")" << std::endl;
    }

    // Update debug state
    m_ringState.chunksInRing = (m_ringState.currentRing <= 1) ? 1
        : 8 * (m_ringState.currentRing - 1);
    m_ringState.currentChunkIndex = m_ringState.chunksInRing;
}

// ---------------------------------------------------------------
// Differential Leading Edge
// ---------------------------------------------------------------
// Instead of re-scanning all ~26K chunks on center change, compute
// only the L-shaped strip that entered the render area.
//
// For a 1-chunk move in +X at render distance R:
//   Leading edge = 1 column of 2R-1 chunks = ~161 chunks
// vs the old approach: ~25,920 hash lookups + per-chunk mutex locks.
//
// All pending-ops mutations happen under a single lock (O4 fix).
// ---------------------------------------------------------------
void ChunkManager::processLeadingEdge(
    const glm::ivec3& oldCenter,
    const glm::ivec3& newCenter,
    int effectiveDist,
    const std::unordered_set<glm::ivec3, IVec3Hash>& existingChunks,
    std::vector<ChunkCreateRequest>& outChunksToCreate)
{
    int dx = newCenter.x - oldCenter.x;
    int dz = newCenter.z - oldCenter.z;

    if (dx == 0 && dz == 0) return;

    // Ring R means Chebyshev distance < R (indices 0 to R-1).
    // The Chebyshev square spans [center - (R-1), center + (R-1)] inclusive.
    int R = effectiveDist;
    int oldMinX = oldCenter.x - (R - 1), oldMaxX = oldCenter.x + (R - 1);
    int oldMinZ = oldCenter.z - (R - 1), oldMaxZ = oldCenter.z + (R - 1);
    int newMinX = newCenter.x - (R - 1), newMaxX = newCenter.x + (R - 1);
    int newMinZ = newCenter.z - (R - 1), newMaxZ = newCenter.z + (R - 1);

    // Terrain bounds for clamping
    int tMinX = 0, tMaxX = m_terrainCenterSet ? static_cast<int>(m_terrainChunksX) - 1 : 999999;
    int tMinZ = 0, tMaxZ = m_terrainCenterSet ? static_cast<int>(m_terrainChunksZ) - 1 : 999999;

    // Collect leading-edge candidates (no lock, no mutation)
    struct Candidate {
        glm::ivec3 pos;
        int lodLevel;
    };
    std::vector<Candidate> candidates;
    candidates.reserve(std::abs(dx) * (2 * R - 1) + std::abs(dz) * (2 * R - 1));

    auto tryCandidate = [&](int x, int z) {
        if (x < tMinX || x > tMaxX || z < tMinZ || z > tMaxZ) return;
        glm::ivec3 pos(x, 0, z);
        if (existingChunks.find(pos) == existingChunks.end()) {
            int ring = calculateRingNumber(pos, newCenter);
            candidates.push_back({pos, calculateLODFromRing(ring)});
        }
    };

    // Horizontal strips (new columns from X movement)
    if (dx > 0) {
        for (int x = std::max(newMinX, oldMaxX + 1); x <= newMaxX; ++x)
            for (int z = newMinZ; z <= newMaxZ; ++z)
                tryCandidate(x, z);
    } else if (dx < 0) {
        for (int x = newMinX; x <= std::min(newMaxX, oldMinX - 1); ++x)
            for (int z = newMinZ; z <= newMaxZ; ++z)
                tryCandidate(x, z);
    }

    // Vertical strips (new rows from Z movement, excluding columns already handled)
    int zStripMinX = newMinX, zStripMaxX = newMaxX;
    if (dx > 0) {
        zStripMaxX = std::min(zStripMaxX, oldMaxX);
    } else if (dx < 0) {
        zStripMinX = std::max(zStripMinX, oldMinX);
    }

    if (dz > 0) {
        for (int z = std::max(newMinZ, oldMaxZ + 1); z <= newMaxZ; ++z)
            for (int x = zStripMinX; x <= zStripMaxX; ++x)
                tryCandidate(x, z);
    } else if (dz < 0) {
        for (int z = newMinZ; z <= std::min(newMaxZ, oldMinZ - 1); ++z)
            for (int x = zStripMinX; x <= zStripMaxX; ++x)
                tryCandidate(x, z);
    }

    // Single lock for all pending-ops mutations (O4 optimisation)
    if (!candidates.empty()) {
        std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
        for (const auto& c : candidates) {
            uint64_t key = packCoord(c.pos.x, c.pos.z);
            auto it = m_pendingOps.find(key);

            if (it != m_pendingOps.end()) {
                if (it->second.type == PendingOpType::Destroy) {
                    // Cancel destroy — chunk still exists in ECS
                    m_pendingOps.erase(it);
                    m_pendingDestroyCount.fetch_sub(1, std::memory_order_relaxed);
                }
                m_coalescedThisFrame++;
                continue;
            }

            m_pendingOps[key] = {PendingOpType::Create, c.lodLevel};
            m_pendingCreateCount.fetch_add(1, std::memory_order_relaxed);

            ChunkCreateRequest req;
            req.coord = c.pos;
            req.lodLevel = c.lodLevel;
            outChunksToCreate.push_back(req);
        }
    }
}

````

## src\world\chunks\core\ChunkManagerBatches.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/core/ChunkManager.h"

// --- LOD Transition Batch Management ---

uint32_t ChunkManager::createLODTransitionBatch(int targetLOD, const std::vector<entt::entity>& entities) {
    uint32_t batchId = m_nextBatchId++;
    auto batch = std::make_unique<LODTransitionBatch>();
    batch->batchId = batchId;
    batch->targetLOD = targetLOD;
    batch->chunksTotal = static_cast<uint32_t>(entities.size());
    batch->chunksReady.store(0, std::memory_order_relaxed);
    batch->entities = entities;
    m_activeBatchMap[batchId] = std::move(batch);
    return batchId;
}

bool ChunkManager::signalBatchChunkReady(uint32_t batchId) {
    auto it = m_activeBatchMap.find(batchId);
    if (it == m_activeBatchMap.end()) return false;
    uint32_t ready = it->second->chunksReady.fetch_add(1, std::memory_order_acq_rel) + 1;
    bool complete = ready >= it->second->chunksTotal;
    if (complete) {
        m_readyBatchQueue.push_back(batchId);
    }
    return complete;
}

LODTransitionBatch* ChunkManager::getCompletedBatch() {
    if (m_readyBatchQueue.empty()) return nullptr;
    uint32_t id = m_readyBatchQueue.back();
    auto it = m_activeBatchMap.find(id);
    if (it == m_activeBatchMap.end()) {
        m_readyBatchQueue.pop_back();
        return nullptr;
    }
    return it->second.get();
}

void ChunkManager::removeCompletedBatch(uint32_t batchId) {
    m_activeBatchMap.erase(batchId);
    // Remove from ready queue (typically the last element)
    for (auto it = m_readyBatchQueue.begin(); it != m_readyBatchQueue.end(); ++it) {
        if (*it == batchId) {
            m_readyBatchQueue.erase(it);
            break;
        }
    }
}

bool ChunkManager::isBatchActive(uint32_t batchId) const {
    return m_activeBatchMap.find(batchId) != m_activeBatchMap.end();
}

std::vector<int> ChunkManager::getLODThresholdRings() const {
    std::vector<int> thresholds;
    if (m_lod0Max < 9999) thresholds.push_back(m_lod0Max);
    if (m_lod1Max < 9999) thresholds.push_back(m_lod1Max);
    if (m_lod2Max < 9999) thresholds.push_back(m_lod2Max);
    if (m_lod3Max < 9999) thresholds.push_back(m_lod3Max);
    return thresholds;
}

std::vector<uint32_t> ChunkManager::cancelAllBatches() {
    std::vector<uint32_t> cancelledIds;
    cancelledIds.reserve(m_activeBatchMap.size());
    for (auto& [id, batch] : m_activeBatchMap) {
        cancelledIds.push_back(id);
    }
    m_activeBatchMap.clear();
    m_readyBatchQueue.clear();
    return cancelledIds;
}

// ---------------------------------------------------------------
// Operation Coalescing: Completion Notifications
// ---------------------------------------------------------------
// Called by World.cpp when a chunk reaches Ready state.
// Clears the pending Create entry and counts toward throughput.
void ChunkManager::notifyChunkCreated(const glm::ivec3& coord) {
    uint64_t key = packCoord(coord.x, coord.z);
    {
        std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
        auto it = m_pendingOps.find(key);
        if (it != m_pendingOps.end() && it->second.type == PendingOpType::Create) {
            m_pendingOps.erase(it);
            m_pendingCreateCount.fetch_sub(1, std::memory_order_relaxed);
        }
    }
    m_chunksCompletedWindow.fetch_add(1, std::memory_order_relaxed);
}

void ChunkManager::notifyChunksCreated(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return;
    {
        std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
        for (const auto& coord : coords) {
            uint64_t key = packCoord(coord.x, coord.z);
            auto it = m_pendingOps.find(key);
            if (it != m_pendingOps.end() && it->second.type == PendingOpType::Create) {
                m_pendingOps.erase(it);
                m_pendingCreateCount.fetch_sub(1, std::memory_order_relaxed);
            }
        }
    }
    m_chunksCompletedWindow.fetch_add(static_cast<uint32_t>(coords.size()), std::memory_order_relaxed);
}

void ChunkManager::cancelPendingCreates(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return;
    std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
    for (const auto& coord : coords) {
        uint64_t key = packCoord(coord.x, coord.z);
        auto it = m_pendingOps.find(key);
        if (it != m_pendingOps.end() && it->second.type == PendingOpType::Create) {
            m_pendingOps.erase(it);
            m_pendingCreateCount.fetch_sub(1, std::memory_order_relaxed);
        }
    }
}

// Called by World.cpp when a chunk entity is fully destroyed.
// Clears the pending Destroy entry.
void ChunkManager::notifyChunkDestroyed(const glm::ivec3& coord) {
    uint64_t key = packCoord(coord.x, coord.z);
    std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
    auto it = m_pendingOps.find(key);
    if (it != m_pendingOps.end() && it->second.type == PendingOpType::Destroy) {
        m_pendingOps.erase(it);
        m_pendingDestroyCount.fetch_sub(1, std::memory_order_relaxed);
    }
}

void ChunkManager::cancelPendingDestroys(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return;
    std::lock_guard<std::mutex> lock(m_pendingOpsMutex);
    for (const auto& coord : coords) {
        uint64_t key = packCoord(coord.x, coord.z);
        auto it = m_pendingOps.find(key);
        if (it != m_pendingOps.end() && it->second.type == PendingOpType::Destroy) {
            m_pendingOps.erase(it);
            m_pendingDestroyCount.fetch_sub(1, std::memory_order_relaxed);
        }
    }
}

````

## src\world\chunks\core\ChunkLifecycleManager.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/core/ChunkLifecycleManager.h"
#include "world/config/WorldConfig.h"
#include <iostream>

ChunkLifecycleManager::ChunkLifecycleManager() = default;

ChunkLifecycleManager::~ChunkLifecycleManager() {
    stop();
}

void ChunkLifecycleManager::start() {
    if (m_running.load(std::memory_order_acquire)) {
        return; // Already running
    }

    if (!m_callback) {
        std::cerr << "[ChunkLifecycleManager] ERROR: No callback set, cannot start\n";
        return;
    }

    m_running.store(true, std::memory_order_release);
    m_thread = std::thread(&ChunkLifecycleManager::workerThread, this);
}

void ChunkLifecycleManager::stop() {
    if (!m_running.load(std::memory_order_acquire)) {
        return; // Not running
    }

    m_running.store(false, std::memory_order_release);
    m_cv.notify_all();

    if (m_thread.joinable()) {
        m_thread.join();
    }
}

void ChunkLifecycleManager::queueCreation(const glm::ivec3& coord) {
    std::lock_guard lock(m_mutex);
    m_creationQueue.push_back(coord);
}

void ChunkLifecycleManager::queueCreations(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return;
    
    std::lock_guard lock(m_mutex);
    for (const auto& coord : coords) {
        m_creationQueue.push_back(coord);
    }
}

void ChunkLifecycleManager::queueDestruction(const glm::ivec3& coord) {
    std::lock_guard lock(m_mutex);
    if (m_queuedDestructions.insert(coord).second) {
        m_destructionQueue.push_back(coord);
    }
}

void ChunkLifecycleManager::queueDestructions(const std::vector<glm::ivec3>& coords) {
    if (coords.empty()) return;
    
    std::lock_guard lock(m_mutex);
    for (const auto& coord : coords) {
        if (m_queuedDestructions.insert(coord).second) {
            m_destructionQueue.push_back(coord);
        }
    }
}

std::vector<glm::ivec3> ChunkLifecycleManager::purgeCreationQueue(
    const std::function<bool(const glm::ivec3&)>& shouldRemove) {
    std::lock_guard lock(m_mutex);
    std::vector<glm::ivec3> removed;
    std::deque<glm::ivec3> kept;
    for (const auto& coord : m_creationQueue) {
        if (shouldRemove(coord)) {
            removed.push_back(coord);
        } else {
            kept.push_back(coord);
        }
    }
    m_creationQueue = std::move(kept);
    return removed;
}

std::vector<glm::ivec3> ChunkLifecycleManager::purgeDestructionQueue(
    const std::function<bool(const glm::ivec3&)>& shouldRemove) {
    std::lock_guard lock(m_mutex);
    std::vector<glm::ivec3> removed;
    std::deque<glm::ivec3> kept;
    for (const auto& coord : m_destructionQueue) {
        if (shouldRemove(coord)) {
            removed.push_back(coord);
            m_queuedDestructions.erase(coord);
        } else {
            kept.push_back(coord);
        }
    }
    m_destructionQueue = std::move(kept);
    return removed;
}

void ChunkLifecycleManager::clearQueues() {
    std::lock_guard lock(m_mutex);
    m_creationQueue.clear();
    m_destructionQueue.clear();
    m_queuedDestructions.clear();
}

void ChunkLifecycleManager::pauseAndDrain() {
    // Signal worker to pause
    m_pauseRequested.store(true, std::memory_order_release);
    
    // Wake it up in case it's sleeping in wait_for
    m_cv.notify_all();
    
    // Wait until the worker finishes its current batch and goes idle
    std::unique_lock lock(m_pauseMutex);
    m_idleCv.wait(lock, [this] {
        return m_workerIdle.load(std::memory_order_acquire);
    });
    // Worker is now blocked at the pause gate. Safe to modify state.
}

void ChunkLifecycleManager::resume() {
    m_pauseRequested.store(false, std::memory_order_release);
    m_pauseCv.notify_all();
}

bool ChunkLifecycleManager::hasPendingWork() const {
    std::lock_guard lock(m_mutex);
    return !m_creationQueue.empty() || !m_destructionQueue.empty();
}

size_t ChunkLifecycleManager::getCreationQueueSize() const {
    std::lock_guard lock(m_mutex);
    return m_creationQueue.size();
}

size_t ChunkLifecycleManager::getDestructionQueueSize() const {
    std::lock_guard lock(m_mutex);
    return m_destructionQueue.size();
}

void ChunkLifecycleManager::wakeUp() {
    m_cv.notify_one();
}

void ChunkLifecycleManager::workerThread() {
    std::cout << "[ChunkLifecycleManager] Background thread started\n";

    int cleanupCounter = 0;

    while (m_running.load(std::memory_order_acquire)) {
        std::unique_lock lock(m_mutex);

        // Wait for work or shutdown
        m_cv.wait_for(lock, std::chrono::milliseconds(16), [this] {
            return !m_creationQueue.empty() || 
                   !m_destructionQueue.empty() ||
                   !m_running.load(std::memory_order_acquire);
        });

        if (!m_running.load(std::memory_order_acquire)) {
            break;
        }

        // Pause gate: if a reset requested pause, block here until resumed
        if (m_pauseRequested.load(std::memory_order_acquire)) {
            // Signal that we're idle (not in any callback)
            m_workerIdle.store(true, std::memory_order_release);
            m_idleCv.notify_all();
            
            // Wait until resume() clears the pause flag
            std::unique_lock pauseLock(m_pauseMutex);
            m_pauseCv.wait(pauseLock, [this] {
                return !m_pauseRequested.load(std::memory_order_acquire) ||
                       !m_running.load(std::memory_order_acquire);
            });
            continue;  // Re-check m_running and re-enter the main loop
        }

        // Periodic cleanup
        if (++cleanupCounter % CLEANUP_INTERVAL == 0) {
            lock.unlock();
            if (m_callback) {
                m_callback->cleanupStaleVersionStates();
            }
            lock.lock();
        }

        // Collect creations to process
        std::vector<glm::ivec3> creationsToProcess;
        int createBudget = CREATION_BATCH_SIZE;
        while (createBudget-- > 0 && !m_creationQueue.empty()) {
            creationsToProcess.push_back(m_creationQueue.front());
            m_creationQueue.pop_front();
        }

        // Collect destructions to process
        std::vector<glm::ivec3> destructionsToProcess;
        int destroyBudget = DESTRUCTION_BATCH_SIZE;
        while (destroyBudget-- > 0 && !m_destructionQueue.empty()) {
            glm::ivec3 coord = m_destructionQueue.front();
            m_destructionQueue.pop_front();
            m_queuedDestructions.erase(coord);
            destructionsToProcess.push_back(coord);
        }

        // Release lock for actual work
        lock.unlock();

        // Mark as not-idle while calling back into World
        m_workerIdle.store(false, std::memory_order_release);

        // Process creations
        if (!creationsToProcess.empty() && m_callback) {
            // Get player position for priority
            glm::vec3 cameraPos = m_callback->getCameraPosition();
            WorldConfig::MicroVoxelCoord cameraMicro = WorldConfig::worldToMicroVoxel(cameraPos);
            WorldConfig::ChunkCoord cameraChunk = WorldConfig::microVoxelToChunk(cameraMicro);
            glm::ivec3 playerChunk(cameraChunk.x, 0, cameraChunk.z);

            // Create entities
            std::vector<entt::entity> entities = m_callback->createChunkEntities(creationsToProcess);

            // Schedule jobs for each created entity
            for (size_t i = 0; i < entities.size(); ++i) {
                if (entities[i] != entt::null) {
                    m_callback->scheduleChunkJobs(entities[i], creationsToProcess[i], playerChunk);
                }
            }
        }

        // Process destructions
        if (!destructionsToProcess.empty() && m_callback) {
            m_callback->destroyChunks(destructionsToProcess);
        }

        // Mark as idle after batch completes
        m_workerIdle.store(true, std::memory_order_release);
        m_idleCv.notify_all();
    }

    std::cout << "[ChunkLifecycleManager] Background thread stopped\n";
}

````

## src\world\chunks\core\ChunkLODSystem.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/core/ChunkLODSystem.h"
#include "world/chunks/core/ChunkManager.h"

void ChunkLODSystem::setDesiredLOD(const glm::ivec3& coord, int lodLevel) {
    std::unique_lock lock(m_lodMapMutex);
    m_desiredLodMap[coord] = lodLevel;
}

void ChunkLODSystem::clearDesiredLOD(const glm::ivec3& coord) {
    std::unique_lock lock(m_lodMapMutex);
    m_desiredLodMap.erase(coord);
}

void ChunkLODSystem::setDesiredLODs(const std::vector<std::pair<glm::ivec3, int>>& entries) {
    std::unique_lock lock(m_lodMapMutex);
    for (const auto& [coord, lodLevel] : entries) {
        m_desiredLodMap[coord] = lodLevel;
    }
}

void ChunkLODSystem::clearDesiredLODs(const std::vector<glm::ivec3>& coords) {
    std::unique_lock lock(m_lodMapMutex);
    for (const auto& coord : coords) {
        m_desiredLodMap.erase(coord);
    }
}

int ChunkLODSystem::getDesiredLOD(const glm::ivec3& coord) const {
    // First check cache
    {
        std::shared_lock lock(m_lodMapMutex);
        auto it = m_desiredLodMap.find(coord);
        if (it != m_desiredLodMap.end()) {
            return it->second;
        }
    }

    // Fall back to ring-based calculation
    if (m_chunkManager) {
        glm::ivec3 center = m_chunkManager->getCenterChunk();
        int ring = m_chunkManager->calculateRingNumber(coord, center);
        return m_chunkManager->calculateLODFromRing(ring);
    }

    return 0; // Default to highest detail
}

uint32_t ChunkLODSystem::enqueueLODRemesh(entt::entity entity,
                                          bool isRemesh,
                                          uint32_t batchId,
                                          int targetLOD,
                                          bool affectsShadowGeometry) {
    if (entity == entt::null) {
        return 0;
    }

    uint32_t supersededBatchId = 0;
    std::unique_lock lock(m_queueMutex);

    bool wasAlreadyPending = !m_remeshPending.insert(entity).second;
    if (!wasAlreadyPending) {
        m_remeshQueue.push_back(entity);
    }

    // Store batch info (overwrite if re-queued with new batch).
    // If the entity was already pending with a DIFFERENT batch, return the
    // old batchId so the caller can signal it — otherwise that batch would
    // wait for a signal that will never arrive and get permanently stuck.
    if (isRemesh && batchId != 0) {
        if (wasAlreadyPending) {
            auto it = m_batchInfoMap.find(entity);
            if (it != m_batchInfoMap.end() && it->second.batchId != 0 && it->second.batchId != batchId) {
                supersededBatchId = it->second.batchId;
            }
        }
    }

    RemeshBatchInfo nextInfo{
        isRemesh,
        batchId,
        targetLOD,
        affectsShadowGeometry
    };
    if (wasAlreadyPending) {
        auto it = m_batchInfoMap.find(entity);
        if (it != m_batchInfoMap.end()) {
            nextInfo.affectsShadowGeometry =
                it->second.affectsShadowGeometry || affectsShadowGeometry;
            // A non-batch material refresh must not erase an already-pending
            // LOD batch. The batch still owns completion/signaling.
            if (it->second.isRemesh && it->second.batchId != 0 &&
                !(isRemesh && batchId != 0)) {
                nextInfo.isRemesh = it->second.isRemesh;
                nextInfo.batchId = it->second.batchId;
                nextInfo.targetLOD = it->second.targetLOD;
            }
        }
    }
    m_batchInfoMap[entity] = nextInfo;
    return supersededBatchId;
}

ChunkLODSystem::RemeshBatchInfo ChunkLODSystem::getRemeshBatchInfo(entt::entity entity) const {
    std::shared_lock lock(m_queueMutex);
    auto it = m_batchInfoMap.find(entity);
    if (it != m_batchInfoMap.end()) {
        return it->second;
    }
    return {false, 0, 0, true};
}

entt::entity ChunkLODSystem::popRemeshQueue() {
    std::unique_lock lock(m_queueMutex);
    if (m_remeshQueue.empty()) {
        return entt::null;
    }
    entt::entity entity = m_remeshQueue.front();
    m_remeshQueue.pop_front();
    return entity;
}

void ChunkLODSystem::requeue(entt::entity entity) {
    std::unique_lock lock(m_queueMutex);
    m_remeshQueue.push_back(entity);
}

void ChunkLODSystem::clearPending(entt::entity entity) {
    std::unique_lock lock(m_queueMutex);
    m_remeshPending.erase(entity);
    m_batchInfoMap.erase(entity);
}

bool ChunkLODSystem::isPending(entt::entity entity) const {
    std::shared_lock lock(m_queueMutex);
    return m_remeshPending.count(entity) > 0;
}

void ChunkLODSystem::clearPendingBatch(const entt::entity* entities, size_t count) {
    if (count == 0) return;
    std::unique_lock lock(m_queueMutex);
    for (size_t i = 0; i < count; ++i) {
        m_remeshPending.erase(entities[i]);
        m_batchInfoMap.erase(entities[i]);
    }
}

void ChunkLODSystem::clearAllPending() {
    std::unique_lock lock(m_queueMutex);
    m_remeshQueue.clear();
    m_remeshPending.clear();
    m_batchInfoMap.clear();
}

bool ChunkLODSystem::isRemeshQueueEmpty() const {
    std::shared_lock lock(m_queueMutex);
    return m_remeshQueue.empty();
}

size_t ChunkLODSystem::getRemeshQueueSize() const {
    std::shared_lock lock(m_queueMutex);
    return m_remeshQueue.size();
}

````

## src\world\chunks\core\ChunkJobs.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/core/ChunkJobs.h"
#include "world/World.h"
#include "world/config/WorldConfig.h"
#include "world/TerrainFileLoader.h"
#include "world/chunks/core/ChunkManager.h"
#include "world/edit/TerrainEditMesher.h"
#include "world/edit/TerrainEditDCCMMesher.h"
#include "world/edit/TerrainFieldSource.h"
#include "world/edit/HeightmapBaseSampler.h"
#include "rendering/common/Mesh.h"
#include <algorithm>
#include <iostream>
#include <map>

// C-nits 8: Global state removed, now owned by World class

// EntityHash implementation
std::size_t EntityHash::operator()(entt::entity value) const noexcept {
    return static_cast<std::size_t>(entt::to_integral(value));
}

// Chunk version state management
std::shared_ptr<ChunkVersionState> ensureChunkVersionState(World* world, entt::entity entity) {
    if (!world || entity == entt::null) {
        return nullptr;
    }
    std::scoped_lock lock(world->chunkVersionMutex());
    auto& states = world->getChunkVersionStates();
    auto it = states.find(entity);
    if (it != states.end()) {
        return it->second;
    }
    auto state = std::make_shared<ChunkVersionState>();
    states.emplace(entity, state);
    return state;
}

void removeChunkVersionState(World* world, entt::entity entity) {
    if (!world || entity == entt::null) {
        return;
    }
    std::scoped_lock lock(world->chunkVersionMutex());
    world->getChunkVersionStates().erase(entity);
}

bool payloadStillCurrent(const ChunkPipelinePayload* payload) {
    if (!payload || !payload->world || !payload->versionState) {
        return false;
    }
    // NOTE: We intentionally do NOT call registry.valid() here.
    // This function is called from worker threads without holding
    // m_registryMutex, so registry.valid() would race with
    // entity destruction on the lifecycle thread.
    // The atomic version check alone is sufficient: the version is
    // bumped before entity destruction, so a mismatch guarantees
    // the entity is stale / being destroyed.
    const uint32_t activeVersion = payload->versionState->version.load(std::memory_order_acquire);
    return activeVersion == payload->version;
}

// Legacy terrain files store packed verts as:
//   X(8) | Y(9, height-1) | Z(8) | face(3) | AO(3)
// Runtime now uses:
//   X(8) | Y(10) | Z(8) | face(3) | AO(3)
//         bits 0..7  8..17   18..25   26..28   29..31
static uint32_t repackLegacyPackedVertex(uint32_t packedLegacy) {
    const uint32_t x = (packedLegacy >> 0) & 0xFFu;
    const uint32_t yLegacy = (packedLegacy >> 8) & 0x1FFu;
    const uint32_t z = (packedLegacy >> 17) & 0xFFu;
    const uint32_t face = (packedLegacy >> 25) & 0x7u;
    const uint32_t ao = (packedLegacy >> 28) & 0x7u;
    const uint32_t y = std::min<uint32_t>(yLegacy + 1u, 0x3FFu);
    return (x << 0) | (y << 8) | (z << 18) | (face << 26) | (ao << 29);
}

static bool isLikelyLegacyPackedLayout(const std::vector<uint32_t>& vertices) {
    if (vertices.empty()) {
        return false;
    }

    const size_t sampleCount = std::min<size_t>(vertices.size(), 256);
    size_t invalidNewDecode = 0;
    for (size_t i = 0; i < sampleCount; ++i) {
        const uint32_t p = vertices[i];
        const uint32_t y = (p >> 8) & 0x3FFu;
        const uint32_t z = (p >> 18) & 0xFFu;
        const uint32_t face = (p >> 26) & 0x7u;
        if (y > static_cast<uint32_t>(WorldConfig::CHUNK_HEIGHT) ||
            z > static_cast<uint32_t>(WorldConfig::CHUNK_SIZE) ||
            face > 6u) {
            ++invalidNewDecode;
        }
    }

    // Legacy vertices decoded as new format produce many invalid Y/Z values.
    return (invalidNewDecode * 3) > sampleCount;
}

static void normalizePackedVertexLayout(std::vector<uint32_t>& vertices, bool forceLegacyLayout) {
    if (!forceLegacyLayout && !isLikelyLegacyPackedLayout(vertices)) {
        return;
    }
    for (uint32_t& packed : vertices) {
        packed = repackLegacyPackedVertex(packed);
    }
}

static constexpr uint16_t kEditPagesPerAxis =
    static_cast<uint16_t>(WorldConfig::CHUNK_SIZE / ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS);
static constexpr uint16_t kEditPageCount = kEditPagesPerAxis * kEditPagesPerAxis;

static void ensureChunkEditRuntimeScaffold(ChunkEditRuntime& editRuntime) {
    editRuntime.targetMode = ChunkMeshMode::PagedEditable;
    if (!editRuntime.pages.empty()) {
        return;
    }

    editRuntime.pages.reserve(kEditPageCount);
    for (uint16_t pageZ = 0; pageZ < kEditPagesPerAxis; ++pageZ) {
        for (uint16_t pageX = 0; pageX < kEditPagesPerAxis; ++pageX) {
            ChunkEditPageRuntime page{};
            page.pageId = static_cast<uint16_t>(pageZ * kEditPagesPerAxis + pageX);
            page.bounds.minX = static_cast<uint16_t>(pageX * ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS);
            page.bounds.maxX = static_cast<uint16_t>(page.bounds.minX + ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS - 1);
            page.bounds.minY = 0;
            page.bounds.maxY = 0;
            page.bounds.minZ = static_cast<uint16_t>(pageZ * ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS);
            page.bounds.maxZ = static_cast<uint16_t>(page.bounds.minZ + ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS - 1);
            editRuntime.pages.push_back(page);
        }
    }
}

static uint16_t countResidentEditPages(const ChunkEditRuntime& editRuntime) {
    uint16_t resident = 0;
    for (const auto& page : editRuntime.pages) {
        if (page.resident) {
            ++resident;
        }
    }
    return resident;
}

static bool shouldExposePagedEditableMode(TerrainType terrainType,
                                          int effectiveLOD,
                                          const ChunkEditRuntime* editRuntime = nullptr) {
    if (terrainType != TerrainType::Voxel || effectiveLOD != 0) {
        return false;
    }

    return !editRuntime || editRuntime->targetMode == ChunkMeshMode::PagedEditable;
}

static void fillPagedDebugInfo(const ChunkEditRuntime& editRuntime, ChunkDebugAttribution& debugInfo) {
    debugInfo.workModel = ChunkWorkModel::PagedLocal;
    debugInfo.meshMode = static_cast<uint8_t>(ChunkMeshMode::PagedEditable);
    debugInfo.dirtyPages = static_cast<uint16_t>(
        std::min<size_t>(editRuntime.dirtyPageIds.size(), UINT16_MAX));
    debugInfo.residentPages = countResidentEditPages(editRuntime);
}

// Procedural generation functions removed - using precomputed meshes only

//──────────────────────────────────────────────────────────────────────────────
// Runtime DCCM casing generation
//──────────────────────────────────────────────────────────────────────────────
// Generates vertical side-faces (casing) at chunk boundaries from the loaded
// surface mesh's boundary vertices.  Replaces precomputed casing which had
// tiny gaps at the surface/casing junction.
//
// Edge definitions (must match converter & fix_dccm_gaps):
//   EDGE 0 = NEG_X (x=0),   face=0, reverseWinding=false
//   EDGE 1 = POS_X (x=128), face=1, reverseWinding=true
//   EDGE 2 = NEG_Z (z=0),   face=4, reverseWinding=true
//   EDGE 3 = POS_Z (z=128), face=5, reverseWinding=false

static void generateDCCMCasingForEdge(
    int edge,
    const std::vector<SubChunkMesh>& mainSubChunks,
    MeshData& outCasing)
{
    constexpr uint8_t FACE_DCCM_SURFACE = 6;
    struct EdgeDef {
        uint8_t coord;          // boundary coordinate (0 or 128)
        bool    isXBound;       // true = match by X, false = match by Z
        bool    reverseWinding;
    };
    static constexpr EdgeDef edgeDefs[CHUNK_EDGE_COUNT] = {
        {   0, true,  false }, // NEG_X
        { 128, true,  true  }, // POS_X
        {   0, false, true  }, // NEG_Z
        { 128, false, false }, // POS_Z
    };

    const auto& def = edgeDefs[edge];

    // Collect boundary vertices: position_along_edge → highest surface height
    std::map<uint8_t, uint16_t> boundaryVerts;

    for (const auto& subChunk : mainSubChunks) {
        for (uint32_t packedVert : subChunk.vertices) {
            uint8_t  vx = packedVert & 0xFF;
            uint16_t vy = (packedVert >> 8) & 0x3FF;
            uint8_t  vz = (packedVert >> 18) & 0xFF;

            if (def.isXBound) {
                if (vx == def.coord) {
                    auto it = boundaryVerts.find(vz);
                    if (it == boundaryVerts.end() || vy > it->second)
                        boundaryVerts[vz] = vy;
                }
            } else {
                if (vz == def.coord) {
                    auto it = boundaryVerts.find(vx);
                    if (it == boundaryVerts.end() || vy > it->second)
                        boundaryVerts[vx] = vy;
                }
            }
        }
    }

    if (boundaryVerts.size() < 2) return;

    // Pack a casing vertex (same layout as engine PackedVertex / fix_dccm_gaps)
    auto packVert = [](uint8_t x, uint16_t y, uint8_t z,
                       uint8_t face, uint8_t ao = 0) -> uint32_t {
        return uint32_t(x)
             | (uint32_t(y)        << 8)
             | (uint32_t(z)        << 18)
             | (uint32_t(face & 7) << 26)
             | (uint32_t(ao & 7)   << 29);
    };

    constexpr uint16_t Y_BASE = 0; // minimum packed Y

    auto it = boundaryVerts.begin();
    auto prev = it;
    ++it;
    for (; it != boundaryVerts.end(); prev = it, ++it) {
        uint8_t  pos0 = prev->first;
        uint16_t h0   = prev->second;
        uint8_t  pos1 = it->first;
        uint16_t h1   = it->second;

        if (h0 <= 1 && h1 <= 1) continue;

        uint8_t x0, z0, x1, z1;
        if (def.isXBound) {
            x0 = def.coord; z0 = pos0;
            x1 = def.coord; z1 = pos1;
        } else {
            x0 = pos0; z0 = def.coord;
            x1 = pos1; z1 = def.coord;
        }

        uint32_t baseIdx = static_cast<uint32_t>(outCasing.vertices.size());

        // 4 vertices: bottom-left, bottom-right, top-right, top-left
        Vertex v;
        v.packed = packVert(x0, Y_BASE, z0, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);
        v.packed = packVert(x1, Y_BASE, z1, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);
        v.packed = packVert(x1, h1, z1, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);
        v.packed = packVert(x0, h0, z0, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);

        if (def.reverseWinding) {
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 1));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 3));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
        } else {
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 1));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 3));
        }
    }
}

static void generateDCCMCasingForEdge(
    int edge,
    const std::vector<MeshData>& mainSubChunks,
    uint8_t mainSubChunkCount,
    MeshData& outCasing)
{
    constexpr uint8_t FACE_DCCM_SURFACE = 6;
    struct EdgeDef {
        uint8_t coord;
        bool    isXBound;
        bool    reverseWinding;
    };
    static constexpr EdgeDef edgeDefs[CHUNK_EDGE_COUNT] = {
        {   0, true,  false },
        { 128, true,  true  },
        {   0, false, true  },
        { 128, false, false },
    };

    const auto& def = edgeDefs[edge];
    std::map<uint8_t, uint16_t> boundaryVerts;

    for (uint8_t subIndex = 0; subIndex < mainSubChunkCount && subIndex < mainSubChunks.size(); ++subIndex) {
        const auto& subChunk = mainSubChunks[subIndex];
        for (const Vertex& vertex : subChunk.vertices) {
            const uint32_t packedVert = vertex.packed;
            const uint8_t vx = packedVert & 0xFF;
            const uint16_t vy = (packedVert >> 8) & 0x3FF;
            const uint8_t vz = (packedVert >> 18) & 0xFF;

            if (def.isXBound) {
                if (vx == def.coord) {
                    auto it = boundaryVerts.find(vz);
                    if (it == boundaryVerts.end() || vy > it->second) {
                        boundaryVerts[vz] = vy;
                    }
                }
            } else {
                if (vz == def.coord) {
                    auto it = boundaryVerts.find(vx);
                    if (it == boundaryVerts.end() || vy > it->second) {
                        boundaryVerts[vx] = vy;
                    }
                }
            }
        }
    }

    if (boundaryVerts.size() < 2) return;

    auto packVert = [](uint8_t x, uint16_t y, uint8_t z,
                       uint8_t face, uint8_t ao = 0) -> uint32_t {
        return uint32_t(x)
             | (uint32_t(y)        << 8)
             | (uint32_t(z)        << 18)
             | (uint32_t(face & 7) << 26)
             | (uint32_t(ao & 7)   << 29);
    };

    constexpr uint16_t Y_BASE = 0;
    auto it = boundaryVerts.begin();
    auto prev = it;
    ++it;
    for (; it != boundaryVerts.end(); prev = it, ++it) {
        const uint8_t pos0 = prev->first;
        const uint16_t h0 = prev->second;
        const uint8_t pos1 = it->first;
        const uint16_t h1 = it->second;

        if (h0 <= 1 && h1 <= 1) continue;

        uint8_t x0, z0, x1, z1;
        if (def.isXBound) {
            x0 = def.coord; z0 = pos0;
            x1 = def.coord; z1 = pos1;
        } else {
            x0 = pos0; z0 = def.coord;
            x1 = pos1; z1 = def.coord;
        }

        const uint32_t baseIdx = static_cast<uint32_t>(outCasing.vertices.size());
        Vertex v;
        v.packed = packVert(x0, Y_BASE, z0, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);
        v.packed = packVert(x1, Y_BASE, z1, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);
        v.packed = packVert(x1, h1, z1, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);
        v.packed = packVert(x0, h0, z0, FACE_DCCM_SURFACE);
        outCasing.vertices.push_back(v);

        if (def.reverseWinding) {
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 1));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 3));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
        } else {
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 1));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 0));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 2));
            outCasing.indices.push_back(static_cast<uint16_t>(baseIdx + 3));
        }
    }
}

static uint8_t computeDCCMCasingMask(
    World* world,
    const glm::ivec3& chunkCoord,
    const glm::ivec3& centerAtEnqueue)
{
    if (!world) {
        return 0;
    }

    ChunkManager* chunkMgr = world->getChunkManager();
    if (!chunkMgr) {
        return 0;
    }

    static const glm::ivec3 neighborOffsets[4] = {
        {-1,0,0}, {1,0,0}, {0,0,-1}, {0,0,1}
    };

    uint8_t casingMask = 0;
    for (int edge = 0; edge < 4; ++edge) {
        const glm::ivec3 neighbor = chunkCoord + neighborOffsets[edge];
        const int neighborRing = chunkMgr->calculateRingNumber(neighbor, centerAtEnqueue);
        const int neighborLOD = chunkMgr->calculateLODFromRing(neighborRing);
        if (world->getTerrainTypeForChunk(neighbor, neighborLOD) != TerrainType::DCCM) {
            casingMask |= (1 << edge);
        }
    }

    return casingMask;
}

static bool computeTightAABBFromSubChunks(const std::vector<MeshData>& subChunks,
                                          glm::vec3& outMin,
                                          glm::vec3& outMax)
{
    constexpr float voxelSize = 0.25f;
    glm::vec3 localMin(1e10f);
    glm::vec3 localMax(-1e10f);
    bool hasVerts = false;

    for (const auto& sub : subChunks) {
        for (const auto& v : sub.vertices) {
            const uint32_t packed = v.packed;
            const uint32_t xBits = (packed >> 0) & 0xFF;
            const uint32_t yBits = (packed >> 8) & 0x3FF;
            const uint32_t zBits = (packed >> 18) & 0xFF;
            const float x = static_cast<float>(xBits) * voxelSize;
            const float y = static_cast<float>(yBits) * voxelSize;
            const float z = static_cast<float>(zBits) * voxelSize;
            localMin = glm::min(localMin, glm::vec3(x, y, z));
            localMax = glm::max(localMax, glm::vec3(x, y, z));
            hasVerts = true;
        }
    }

    if (!hasVerts) {
        return false;
    }

    const float padding = voxelSize * 0.5f;
    outMin = localMin - padding;
    outMax = localMax + padding;
    return true;
}

//──────────────────────────────────────────────────────────────────────────────
// Job Pipeline Functions
//──────────────────────────────────────────────────────────────────────────────
// Each job function operates on ChunkPipelinePayload.
// Pipeline stages: Load → Upload → Finalize
//
// Thread Safety:
// - Registry: protected by World's m_registryMutex
// - Upload queue: lock-free (worker threads enqueue)
//──────────────────────────────────────────────────────────────────────────────

void LoadPrecomputedMeshJob(JobCtx& ctx, void* user) {
    auto* payload = static_cast<ChunkPipelinePayload*>(user);
    if (!payload) {
        std::cout << "[LoadPrecomputedMeshJob] ERROR: null payload" << std::endl;
        return;
    }

    if (payload->cancelled.load(std::memory_order_acquire)) {
        std::cout << "[LoadPrecomputedMeshJob] Cancelled" << std::endl;
        return;
    }

    if (!payloadStillCurrent(payload)) {
        payload->cancelled.store(true, std::memory_order_release);
        // Do NOT clear inFlight/pending here — a newer pipeline for this
        // entity owns those flags.  Clearing them would race with the new
        // pipeline and can cause double-scheduling → double-free → crash.
        static std::atomic<int> staleCount{0};
        if (staleCount.fetch_add(1, std::memory_order_relaxed) % 200 == 0) {
            std::cout << "[LoadPrecomputedMeshJob] Stale version (total: " << staleCount.load() << ")" << std::endl;
        }
        return;
    }

    if (!payload->world) {
        std::cout << "[LoadPrecomputedMeshJob] ERROR: null world" << std::endl;
        return;
    }

    // Safety net: if this chunk has runtime voxel edits, the dispatcher should
    // have selected LoadEditMeshJob.  Redirect here to guarantee correct mesh
    // data regardless of which code path enqueued the job.
    if (payload->world->chunkNeedsRuntimeVoxel(payload->coord.toVec3())) {
        LoadEditMeshJob(ctx, user);
        return;
    }

    // Load precomputed mesh data from terrain file
    // Use per-LOD terrain loader (voxel or DCCM) and effective LOD level
    // DCCM always uses LOD 0 regardless of actual LOD level
    TerrainFileLoader* terrainLoader = payload->world->getTerrainLoaderForLOD(payload->lodLevel);
    if (!terrainLoader || !terrainLoader->isLoaded()) {
        std::cout << "[LoadPrecomputedMeshJob] ERROR: no terrain file loader available" << std::endl;
        return;
    }

    int effectiveLOD = payload->world->getEffectiveLODForChunk(payload->coord.toVec3(), payload->lodLevel);
    uint8_t computedVoxelSeamMask = 0;
    payload->debugInfo = {};
    payload->debugInfo.artifactSource = ChunkArtifactSource::PrecomputedTerrain;
    payload->debugInfo.workModel = ChunkWorkModel::MonolithicChunk;
    payload->debugInfo.meshMode = static_cast<uint8_t>(ChunkMeshMode::MonolithicPristine);
    payload->debugInfo.fromLodBatch = payload->isRemesh;
    payload->debugInfo.fromTerrainEdit = payload->fromTerrainEdit;
    payload->debugInfo.affectsShadowGeometry = payload->affectsShadowGeometry;
    payload->debugInfo.collisionSource = (payload->lodLevel == 0)
        ? ChunkCollisionSource::BaseCollisionCache
        : ChunkCollisionSource::None;

    // Use v4 SubChunk API - SubChunks are NEVER merged, each stays under 65535 vertices
    auto meshWithSubChunks = terrainLoader->getMeshDataWithSubChunks(
        payload->coord.x,
        payload->coord.z,
        effectiveLOD
    );

    const bool forceLegacyLayout = terrainLoader->usesLegacyPackedVertexLayout();

    // Normalize legacy packed vertex layout on load so shaders/physics/edit
    // all consume one consistent format.
    for (auto& subChunk : meshWithSubChunks.mainSubChunks) {
        normalizePackedVertexLayout(subChunk.vertices, forceLegacyLayout);
    }
    for (auto& seamEdge : meshWithSubChunks.seamSubChunks) {
        for (auto& subChunk : seamEdge) {
            normalizePackedVertexLayout(subChunk.vertices, forceLegacyLayout);
        }
    }

    if (!meshWithSubChunks.isEmpty()) {
        payload->isEmpty = false;
        
        // Get the seam edge mask to determine which edges need seams
        // Uses centerAtEnqueue (captured when the job was queued) for
        // a stable seam mask even if the player moves between enqueue
        // and worker-thread execution.
        uint8_t seamMask = 0;     // LOD boundary seams (voxel only)
        uint8_t casingMask = 0;   // DCCM casing at terrain type boundaries
        ChunkManager* chunkMgr = payload->world->getChunkManager();
        TerrainType thisType = payload->world->getTerrainTypeForChunk(payload->coord.toVec3(), payload->lodLevel);
        if (chunkMgr) {
            glm::ivec3 chunkCoord(payload->coord.x, payload->coord.y, payload->coord.z);

            // LOD boundary seams (voxel chunks only - prevents cracks between LOD levels)
            if (payload->lodLevel > 0) {
                seamMask = chunkMgr->getSeamEdgeMask(chunkCoord, payload->centerAtEnqueue);
            }
            computedVoxelSeamMask = seamMask;

            // DCCM casing: side faces at edges where DCCM meets voxel terrain
            if (thisType == TerrainType::DCCM) {
                casingMask = computeDCCMCasingMask(payload->world, chunkCoord, payload->centerAtEnqueue);
            }
        }
        
        // Convert main mesh SubChunks to MeshData format
        // Each SubChunk becomes a separate MeshData, NEVER merged
        for (const auto& subChunk : meshWithSubChunks.mainSubChunks) {
            if (subChunk.isEmpty()) continue;
            
            MeshData meshData;
            meshData.entity = payload->entity;
            meshData.vertices.resize(subChunk.vertices.size());
            for (size_t i = 0; i < subChunk.vertices.size(); ++i) {
                uint32_t v = subChunk.vertices[i];
                if (thisType == TerrainType::DCCM) {
                    // Stamp face=6 so DCCM surface vertices route through
                    // dccm_terrain.frag (which keeps face==6) instead of
                    // cube.frag (which discards face==6).
                    v = (v & ~(0x7u << 26)) | (6u << 26);
                }
                meshData.vertices[i].packed = v;
            }
            meshData.indices = subChunk.indices;
            payload->subChunks.push_back(std::move(meshData));
        }
        
        // Track how many are main mesh SubChunks
        payload->mainSubChunkCount = static_cast<uint8_t>(payload->subChunks.size());

        // Add seam/casing SubChunks based on terrain type
        if (thisType == TerrainType::DCCM) {
            // DCCM: generate casing at runtime from boundary vertices
            // This replaces precomputed seam data, fixing gaps between
            // the casing top edge and the DCCM surface.
            for (int e = 0; e < CHUNK_EDGE_COUNT; ++e) {
                if ((casingMask & (1 << e)) == 0) continue;

                MeshData casingData;
                casingData.entity = payload->entity;
                generateDCCMCasingForEdge(e, meshWithSubChunks.mainSubChunks, casingData);
                if (!casingData.isEmpty()) {
                    payload->subChunks.push_back(std::move(casingData));
                }
            }
        } else {
            // Voxel: use precomputed seam SubChunks from terrain file
            for (int e = 0; e < CHUNK_EDGE_COUNT; ++e) {
                if ((seamMask & (1 << e)) == 0) continue;

                for (const auto& seamSubChunk : meshWithSubChunks.seamSubChunks[e]) {
                    if (seamSubChunk.isEmpty()) continue;

                    MeshData meshData;
                    meshData.entity = payload->entity;
                    meshData.vertices.resize(seamSubChunk.vertices.size());
                    for (size_t i = 0; i < seamSubChunk.vertices.size(); ++i) {
                        meshData.vertices[i].packed = seamSubChunk.vertices[i];
                    }
                    meshData.indices = seamSubChunk.indices;
                    payload->subChunks.push_back(std::move(meshData));
                }
            }
        }
    } else {
        // Empty chunk
        payload->isEmpty = true;
    }

    // Precompute tight AABB on worker thread (avoids vertex scan on main thread)
    if (!payload->isEmpty && !payload->subChunks.empty()) {
        constexpr float VOXEL_SIZE = 0.25f;
        glm::vec3 localMin(1e10f);
        glm::vec3 localMax(-1e10f);
        bool hasVerts = false;
        
        for (const auto& sub : payload->subChunks) {
            for (const auto& v : sub.vertices) {
                uint32_t packed = v.packed;
                uint32_t xBits = (packed >> 0) & 0xFF;
                uint32_t yBits = (packed >> 8) & 0x3FF;
                uint32_t zBits = (packed >> 18) & 0xFF;
                float x = static_cast<float>(xBits) * VOXEL_SIZE;
                float y = static_cast<float>(yBits) * VOXEL_SIZE;
                float z = static_cast<float>(zBits) * VOXEL_SIZE;
                localMin = glm::min(localMin, glm::vec3(x, y, z));
                localMax = glm::max(localMax, glm::vec3(x, y, z));
                hasVerts = true;
            }
        }
        
        if (hasVerts) {
            const float padding = VOXEL_SIZE * 0.5f;
            payload->tightAABBMin = localMin - padding;
            payload->tightAABBMax = localMax + padding;
            payload->hasTightAABB = true;
        }
    }
    payload->debugInfo.subChunkCount = static_cast<uint16_t>(payload->subChunks.size());

    // Mark chunk as not empty and meshed (under lock — main thread also accesses Chunk)
    {
        std::unique_lock regLock(payload->world->registryMutex());
        auto& registry = payload->world->getRegistry();
        if (registry.valid(payload->entity) && registry.all_of<Chunk>(payload->entity)) {
            auto& chunk = registry.get<Chunk>(payload->entity);
            chunk.isEmpty = payload->isEmpty;
            chunk.meshMode = ChunkMeshMode::MonolithicPristine;
            // For remesh batches, topology metadata is applied in processLODSwaps()
            // when the new mesh is actually swapped in.
            if (!payload->isRemesh) {
                chunk.effectiveDataLod = static_cast<uint8_t>(std::clamp(effectiveLOD, 0, 255));
                // Store DCCM casing mask / voxel seam mask for same-LOD topology detection.
                TerrainType chunkType = payload->world->getTerrainTypeForChunk(payload->coord.toVec3(), payload->lodLevel);
                if (chunkType == TerrainType::DCCM) {
                    chunk.voxelSeamMask = 0;
                    uint8_t storedCasingMask = 0;
                    ChunkManager* mgr = payload->world->getChunkManager();
                    if (mgr) {
                        storedCasingMask = computeDCCMCasingMask(
                            payload->world,
                            glm::ivec3(payload->coord.x, payload->coord.y, payload->coord.z),
                            payload->centerAtEnqueue);
                    }
                    chunk.casingSeamMask = storedCasingMask;
                } else {
                    chunk.voxelSeamMask = computedVoxelSeamMask;
                    chunk.casingSeamMask = 0;
                }
            }
            // IMPORTANT: Only set lodLevel for initial loads, NOT for remeshes.
            // For remeshes, processLODSwaps() sets lodLevel atomically when
            // the entire batch swaps PendingMeshHandle → MeshHandle.
            // Writing it early here would cause the LOD mismatch scan to
            // think the chunk is already at the target LOD, skip it, and
            // if the batch gets cancelled, the chunk is stuck with the
            // wrong lodLevel vs. the mesh it's actually rendering.
            if (!payload->isRemesh) {
                chunk.lodLevel = payload->lodLevel;
            }
        }
    }

    // Transition chunk to Ready for initial loads.
    // This makes the chunk visible to LOD scans and rendering.
    // processFinalizeQueue will also set Ready after upload completes (idempotent).
    // The previous freeze bug was NOT caused by this early Ready — it was caused by
    // FinalizeChunkJob not clearing inFlight on version mismatch (now fixed).
    if (!payload->isRemesh) {
        payload->world->transitionChunkState(payload->entity, ChunkState::State::Ready);
    }
}

//──────────────────────────────────────────────────────────────────────────────
// LoadEditMeshJob — runs edit mesher (Voxel or DCCM) for chunks with overlays
//──────────────────────────────────────────────────────────────────────────────

void LoadEditMeshJob(JobCtx& /*ctx*/, void* user) {
    auto* payload = static_cast<ChunkPipelinePayload*>(user);
    if (!payload || !payload->world) return;
    if (!payloadStillCurrent(payload)) {
        payload->cancelled.store(true, std::memory_order_release);
        return;
    }

    World* world = payload->world;
    const auto& fieldSource = world->getTerrainFieldSource();
    const auto& heightmap   = world->getHeightmapSampler();

    TerrainType terrainType = world->getTerrainTypeForChunk(payload->coord.toVec3(), payload->lodLevel);
    bool useDCCM = (terrainType == TerrainType::DCCM) && heightmap.isLoaded();
    int effectiveLOD = world->getEffectiveLODForChunk(payload->coord.toVec3(), payload->lodLevel);
    TerrainType artifactTerrainType = useDCCM ? TerrainType::DCCM : TerrainType::Voxel;
    const glm::ivec3 chunkCoord(payload->coord.x, payload->coord.y, payload->coord.z);
    const auto* overlay = fieldSource.getOverlay();
    const bool hasGeometryEdits = overlay && overlay->hasEditsInChunk(chunkCoord);
    const uint8_t dccmCasingMask = useDCCM
        ? computeDCCMCasingMask(world, chunkCoord, payload->centerAtEnqueue)
        : 0;
    payload->debugInfo = {};
    payload->debugInfo.workModel = ChunkWorkModel::MonolithicChunk;
    payload->debugInfo.meshMode = static_cast<uint8_t>(ChunkMeshMode::MonolithicEdited);
    payload->debugInfo.fromLodBatch = payload->isRemesh;
    payload->debugInfo.fromTerrainEdit = true;
    payload->debugInfo.affectsShadowGeometry = payload->affectsShadowGeometry;
    payload->debugInfo.collisionSource = (effectiveLOD == 0 && hasGeometryEdits)
        ? ChunkCollisionSource::ArtifactRefresh
        : ChunkCollisionSource::None;
    {
        std::unique_lock regLock(payload->world->registryMutex());
        auto& registry = payload->world->getRegistry();
        if (registry.valid(payload->entity) && registry.all_of<Chunk>(payload->entity)) {
            const bool hasEditOwnership =
                registry.any_of<ChunkEditRuntime>(payload->entity) ||
                (overlay && overlay->hasEditsInChunk(chunkCoord));
            if (hasEditOwnership &&
                shouldExposePagedEditableMode(artifactTerrainType, effectiveLOD)) {
                auto& editRuntime = registry.get_or_emplace<ChunkEditRuntime>(payload->entity);
                ensureChunkEditRuntimeScaffold(editRuntime);
                fillPagedDebugInfo(editRuntime, payload->debugInfo);
                registry.get<Chunk>(payload->entity).meshMode = ChunkMeshMode::PagedEditable;
            }
        }
    }
    const TerrainEdit::RemeshCancellationToken cancelToken{
        &payload->versionState->version,
        payload->version
    };

    World::EditArtifact cachedArtifact;
    if (world->tryGetEditArtifact(payload->coord.toVec3(),
                                  artifactTerrainType,
                                  effectiveLOD,
                                  cachedArtifact)) {
        payload->debugInfo.artifactSource = cachedArtifact.deferredBuild
            ? ChunkArtifactSource::DeferredArtifactCache
            : ChunkArtifactSource::EditArtifactCache;
        payload->debugInfo.artifactCacheHit = true;
        payload->debugInfo.artifactCacheResident = true;
        payload->debugInfo.artifactGeneration = cachedArtifact.generation;
        if (cachedArtifact.isEmpty) {
            payload->isEmpty = true;
        } else {
            // Split cached artifact into sub-meshes (handles >65535 vertex overflow)
            TerrainEdit::TerrainEditMesher::MeshResult tmpResult;
            tmpResult.vertices = std::move(cachedArtifact.vertices);
            tmpResult.indices  = std::move(cachedArtifact.indices);
            auto subs = TerrainEdit::TerrainEditMesher::splitToSubMeshes(tmpResult);
            for (auto& sub : subs) {
                MeshData md(payload->entity);
                md.vertices = std::move(sub.vertices);
                md.indices  = std::move(sub.indices);
                payload->subChunks.push_back(std::move(md));
            }
            payload->mainSubChunkCount = static_cast<uint8_t>(payload->subChunks.size());
            if (useDCCM) {
                for (int edge = 0; edge < CHUNK_EDGE_COUNT; ++edge) {
                    if ((dccmCasingMask & (1 << edge)) == 0) continue;
                    MeshData casingData(payload->entity);
                    generateDCCMCasingForEdge(edge, payload->subChunks, payload->mainSubChunkCount, casingData);
                    if (!casingData.isEmpty()) {
                        payload->subChunks.push_back(std::move(casingData));
                    }
                }
            }
            payload->isEmpty = false;
            payload->hasTightAABB = computeTightAABBFromSubChunks(
                payload->subChunks,
                payload->tightAABBMin,
                payload->tightAABBMax);
        }
        payload->debugInfo.subChunkCount = static_cast<uint16_t>(payload->subChunks.size());

        if (!payloadStillCurrent(payload)) {
            payload->cancelled.store(true, std::memory_order_release);
            return;
        }

        {
            std::unique_lock regLock(payload->world->registryMutex());
            auto& registry = payload->world->getRegistry();
            if (registry.valid(payload->entity) && registry.all_of<Chunk>(payload->entity)) {
                auto& chunk = registry.get<Chunk>(payload->entity);
                chunk.isEmpty = payload->isEmpty;
                if (registry.any_of<ChunkEditRuntime>(payload->entity)) {
                    auto& editRuntime = registry.get<ChunkEditRuntime>(payload->entity);
                    if (shouldExposePagedEditableMode(
                            artifactTerrainType,
                            effectiveLOD,
                            &editRuntime)) {
                        ensureChunkEditRuntimeScaffold(editRuntime);
                        fillPagedDebugInfo(editRuntime, payload->debugInfo);
                        chunk.meshMode = ChunkMeshMode::PagedEditable;
                    } else {
                        chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                    }
                } else {
                    chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                }
                if (useDCCM) {
                    chunk.voxelSeamMask = 0;
                    chunk.casingSeamMask = dccmCasingMask;
                } else {
                    // Match what WorldUpdateLODScan compares against, so the
                    // reconciliation pass doesn't immediately re-batch this
                    // chunk after a non-batch finalize requeue.
                    uint8_t computedSeam = 0;
                    if (payload->lodLevel > 0) {
                        if (auto* mgr = payload->world->getChunkManager()) {
                            computedSeam = mgr->getSeamEdgeMask(
                                chunkCoord, payload->centerAtEnqueue);
                        }
                    }
                    chunk.voxelSeamMask = computedSeam;
                    chunk.casingSeamMask = 0;
                }
                if (!payload->isRemesh) {
                    chunk.effectiveDataLod = static_cast<uint8_t>(
                        std::clamp(effectiveLOD, 0, 255));
                    chunk.lodLevel = payload->lodLevel;
                }
            }
        }

        if (!payload->isRemesh) {
            payload->world->transitionChunkState(payload->entity, ChunkState::State::Ready);
        }
        return;
    }

    TerrainEdit::TerrainEditMesher::MeshResult result;

    if (useDCCM) {
        auto dccm = TerrainEdit::TerrainEditDCCMMesher::meshChunk(
            heightmap, nullptr,
            payload->coord.x, payload->coord.z,
            WorldConfig::CHUNK_SIZE,
            WorldConfig::CHUNK_HEIGHT,
            effectiveLOD);
        result.vertices = std::move(dccm.vertices);
        result.indices.assign(dccm.indices.begin(), dccm.indices.end());
        result.aabbMin  = dccm.aabbMin;
        result.aabbMax  = dccm.aabbMax;
    } else {
        int hintMinY = -1, hintMaxY = -1;
        if (heightmap.isLoaded()) {
            const int chunkBaseY = payload->coord.y * WorldConfig::CHUNK_HEIGHT;
            if (payload->coord.y < 0) {
                hintMinY = chunkBaseY;
                hintMaxY = chunkBaseY + WorldConfig::CHUNK_HEIGHT - 1;
            } else if (payload->coord.y == 0) {
                auto [hMin, hMax] = heightmap.getHeightRangeForChunk(
                    payload->coord.x, payload->coord.z, WorldConfig::CHUNK_SIZE);
                hintMinY = hMin;
                hintMaxY = hMax;
            }

            const auto* overlay = fieldSource.getOverlay();
            if (overlay && overlay->hasAnyEdits()) {
                auto [editMinY, editMaxY] = overlay->getEditVoxelYRange(
                    payload->coord.x * WorldConfig::CHUNK_SIZE,
                    payload->coord.z * WorldConfig::CHUNK_SIZE,
                    WorldConfig::CHUNK_SIZE);
                if (editMinY <= editMaxY) {
                    if (hintMinY < 0) { hintMinY = editMinY; hintMaxY = editMaxY; }
                    else {
                        if (editMinY < hintMinY) hintMinY = editMinY;
                        if (editMaxY > hintMaxY) hintMaxY = editMaxY;
                    }
                }
            }
        }

        glm::ivec3 editCoord(payload->coord.x, payload->coord.y, payload->coord.z);
        result = TerrainEdit::TerrainEditMesher::meshChunk(
            fieldSource, editCoord,
            WorldConfig::CHUNK_SIZE,
            WorldConfig::CHUNK_HEIGHT,
            WorldConfig::VOXEL_SIZE_M,
            hintMinY, hintMaxY,
            heightmap.isLoaded() ? &heightmap : nullptr,
            effectiveLOD,
            /*skipPostProcess=*/false,
            /*skipAmbientOcclusion=*/false,
            &cancelToken);
    }

    if (!payloadStillCurrent(payload)) {
        payload->cancelled.store(true, std::memory_order_release);
        return;
    }

    if (result.empty()) {
        payload->isEmpty = true;
        // Cache the empty result so future loads at this LOD/type are instant.
        world->storeEditArtifact(
            payload->coord.toVec3(),
            artifactTerrainType,
            effectiveLOD,
            {}, {},
            glm::vec3(1e10f), glm::vec3(-1e10f),
            /*isEmpty=*/true);
    } else {
        // Cache the meshed result before moving data into the payload.
        world->storeEditArtifact(
            payload->coord.toVec3(),
            artifactTerrainType,
            effectiveLOD,
            std::vector<Vertex>(result.vertices),
            std::vector<uint32_t>(result.indices),
            result.aabbMin,
            result.aabbMax,
            /*isEmpty=*/false);

        // Split into sub-meshes for GPU upload (handles >65535 vertex overflow)
        auto subs = TerrainEdit::TerrainEditMesher::splitToSubMeshes(result);
        for (auto& sub : subs) {
            MeshData md(payload->entity);
            md.vertices = std::move(sub.vertices);
            md.indices  = std::move(sub.indices);
            payload->subChunks.push_back(std::move(md));
        }
        payload->mainSubChunkCount = static_cast<uint8_t>(payload->subChunks.size());
        if (useDCCM) {
            for (int edge = 0; edge < CHUNK_EDGE_COUNT; ++edge) {
                if ((dccmCasingMask & (1 << edge)) == 0) continue;
                MeshData casingData(payload->entity);
                generateDCCMCasingForEdge(edge, payload->subChunks, payload->mainSubChunkCount, casingData);
                if (!casingData.isEmpty()) {
                    payload->subChunks.push_back(std::move(casingData));
                }
            }
        }
        payload->isEmpty = false;
        payload->hasTightAABB = computeTightAABBFromSubChunks(
            payload->subChunks,
            payload->tightAABBMin,
            payload->tightAABBMax);
    }
    payload->debugInfo.artifactSource = ChunkArtifactSource::RuntimeEditBuild;
    payload->debugInfo.artifactCacheResident = true;
    payload->debugInfo.artifactGeneration = world->getEditArtifactGeneration(
        payload->coord.toVec3(),
        artifactTerrainType,
        effectiveLOD);
    payload->debugInfo.subChunkCount = static_cast<uint16_t>(payload->subChunks.size());

    // Update chunk component
    {
        std::unique_lock regLock(payload->world->registryMutex());
        auto& registry = payload->world->getRegistry();
        if (registry.valid(payload->entity) && registry.all_of<Chunk>(payload->entity)) {
            auto& chunk = registry.get<Chunk>(payload->entity);
            chunk.isEmpty = payload->isEmpty;
            if (registry.any_of<ChunkEditRuntime>(payload->entity)) {
                auto& editRuntime = registry.get<ChunkEditRuntime>(payload->entity);
                if (shouldExposePagedEditableMode(
                        artifactTerrainType,
                        effectiveLOD,
                        &editRuntime)) {
                    ensureChunkEditRuntimeScaffold(editRuntime);
                    fillPagedDebugInfo(editRuntime, payload->debugInfo);
                    chunk.meshMode = ChunkMeshMode::PagedEditable;
                } else {
                    chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                }
            } else {
                chunk.meshMode = ChunkMeshMode::MonolithicEdited;
            }
            if (!payload->isRemesh) {
                chunk.effectiveDataLod = static_cast<uint8_t>(std::clamp(effectiveLOD, 0, 255));
                chunk.lodLevel = payload->lodLevel;
            }
            if (useDCCM) {
                chunk.voxelSeamMask = 0;
                chunk.casingSeamMask = dccmCasingMask;
            } else {
                // Match what WorldUpdateLODScan compares against, so the
                // reconciliation pass doesn't immediately re-batch this
                // chunk after a non-batch finalize requeue.
                uint8_t computedSeam = 0;
                if (payload->lodLevel > 0) {
                    if (auto* mgr = payload->world->getChunkManager()) {
                        computedSeam = mgr->getSeamEdgeMask(
                            chunkCoord, payload->centerAtEnqueue);
                    }
                }
                chunk.voxelSeamMask = computedSeam;
                chunk.casingSeamMask = 0;
            }
        }
    }

    if (!payload->isRemesh) {
        payload->world->transitionChunkState(payload->entity, ChunkState::State::Ready);
    }
}

void UploadChunkJob(JobCtx&, void* user) {
    auto* payload = static_cast<ChunkPipelinePayload*>(user);
    if (!payload) {
        std::cout << "[UploadJob] ERROR: null payload" << std::endl;
        return;
    }
    
    // Empty chunks have nothing to upload — skip.
    // LoadPrecomputedMeshJob already transitioned them to Ready.
    if (payload->isEmpty || payload->subChunks.empty()) {
        // For LOD remesh batches, empty chunks must still flow through the
        // upload system so the batch gets signaled (via onBatchChunkReady).
        // Without this, one empty chunk permanently blocks the entire batch's
        // PendingMeshHandle swap, freezing all chunks in that batch at old LOD.
        if (payload->isRemesh && payload->batchId != 0 && payload->world) {
            payload->world->enqueueMeshForUpload(
                payload->entity,
                std::move(payload->subChunks),
                payload->mainSubChunkCount,
                payload->fromTerrainEdit,
                payload->versionState,
                payload->version,
                payload->tightAABBMin,
                payload->tightAABBMax,
                payload->hasTightAABB,
                payload->isRemesh,
                payload->batchId,
                payload->debugInfo);
        }
        return;
    }

    if (payload->cancelled.load(std::memory_order_acquire)) {
        // Do NOT clear inFlight — a newer pipeline may own it.
        return;
    }

    if (!payloadStillCurrent(payload)) {
        payload->cancelled.store(true, std::memory_order_release);
        // Do NOT clear inFlight/pending — a newer pipeline owns those flags.
        static std::atomic<int> staleCount{0};
        if (staleCount.fetch_add(1, std::memory_order_relaxed) % 200 == 0) {
            std::cout << "[UploadJob] Stale version (total: " << staleCount.load() << ")" << std::endl;
        }
        return;
    }

    if (!payload->world) {
        if (payload->versionState) {
            payload->versionState->inFlight.store(false, std::memory_order_release);
        }
        std::cout << "[UploadJob] ERROR: null world" << std::endl;
        return;
    }

    // Upload all SubChunks together - they share a single MeshHandle with multiple draw ranges
    payload->world->enqueueMeshForUpload(payload->entity,
                                         std::move(payload->subChunks),
                                         payload->mainSubChunkCount,
                                         payload->fromTerrainEdit,
                                         payload->versionState,
                                         payload->version,
                                         payload->tightAABBMin,
                                         payload->tightAABBMax,
                                         payload->hasTightAABB,
                                         payload->isRemesh,
                                         payload->batchId,
                                         payload->debugInfo);
}

void FinalizeChunkJob(JobCtx&, void* user) {
    // Return payload to pool instead of deleting (avoids heap free)
    ChunkPipelinePayload* payload = static_cast<ChunkPipelinePayload*>(user);
    if (!payload) {
        std::cout << "[FinalizeJob] ERROR: null payload" << std::endl;
        return;
    }

    // Successful uploads are only truly complete once the main-thread upload
    // + finalize stages drain their queues. Clearing inFlight here makes the
    // chunk look idle while an older mesh is still pending upload/finalize,
    // which allows duplicate remesh dispatches and eventually produces
    // VersionMismatchDrop / FinalizeDataLodMismatchRequeued cascades.
    //
    // Keep inFlight owned until processFinalizeQueue() for any pipeline that
    // reached the upload path. Only release it here for terminal paths that do
    // NOT enqueue a finalize request:
    //  - cancelled / stale pipelines
    //  - initial empty loads (non-remesh) that skip upload entirely
    //  - malformed payloads with no world pointer
    const bool terminalWithoutMainThreadFinalize =
        !payload->world ||
        payload->cancelled.load(std::memory_order_acquire) ||
        (!payloadStillCurrent(payload)) ||
        ((payload->isEmpty || payload->subChunks.empty()) && !payload->isRemesh);

    if (terminalWithoutMainThreadFinalize && payload->versionState) {
        payload->versionState->inFlight.store(false, std::memory_order_release);
        payload->versionState->pending.store(false, std::memory_order_release);
    }
    
    // Return to pool for reuse (world pointer gives us access to the pool)
    if (payload->world) {
        payload->world->getPayloadPool().release(payload);
    } else {
        delete payload;
    }
}

````

## src\world\upload\WorldUploadQueue.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Bridges World mesh upload requests to ChunkUploadSystem and upload pipeline diagnostics.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include "rendering/common/Mesh.h"
#include <atomic>

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

````

## src\world\chunks\streaming\ChunkUploadSystem.cpp

Description: No CC-DESC found.

````cpp
#include "world/chunks/streaming/ChunkUploadSystem.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include "rendering/common/VulkanHelpers.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <iostream>
#include <algorithm>
#include <unordered_set>

ChunkUploadSystem::~ChunkUploadSystem() {
    // Clean up any remaining nodes in the lock-free queue
    UploadNode* head = m_queueHead.load(std::memory_order_acquire);
    while (head) {
        UploadNode* next = head->next.load(std::memory_order_relaxed);
        delete head;
        head = next;
    }
    // Clean up pool
    head = m_poolHead.load(std::memory_order_acquire);
    while (head) {
        UploadNode* next = head->next.load(std::memory_order_relaxed);
        delete head;
        head = next;
    }
}

ChunkUploadSystem::UploadNode* ChunkUploadSystem::allocNode(PendingUpload&& data) {
    // Try to pop from the lock-free pool
    UploadNode* node = m_poolHead.load(std::memory_order_acquire);
    while (node) {
        UploadNode* next = node->next.load(std::memory_order_relaxed);
        if (m_poolHead.compare_exchange_weak(node, next,
                                              std::memory_order_release,
                                              std::memory_order_acquire)) {
            node->data = std::move(data);
            node->next.store(nullptr, std::memory_order_relaxed);
            return node;
        }
    }
    return new UploadNode(std::move(data));
}

void ChunkUploadSystem::freeNode(UploadNode* node) {
    // Clear heavy data to free memory, then push to pool
    node->data.subChunks.clear();
    node->data.subChunks.shrink_to_fit();
    node->data.versionState.reset();
    UploadNode* oldHead = m_poolHead.load(std::memory_order_relaxed);
    do {
        node->next.store(oldHead, std::memory_order_relaxed);
    } while (!m_poolHead.compare_exchange_weak(oldHead, node,
                                                std::memory_order_release,
                                                std::memory_order_relaxed));
}

void ChunkUploadSystem::enqueueMeshForUpload(
    entt::entity entity,
    MeshData&& mesh,
    bool fromTerrainEdit,
    std::shared_ptr<ChunkVersionState> versionState,
    uint32_t version,
    std::chrono::steady_clock::time_point enqueueTime,
    ChunkDebugAttribution debugInfo)
{
    // Legacy single-mesh path: wrap in SubChunks vector
    std::vector<MeshData> subChunks;
    subChunks.push_back(std::move(mesh));
    enqueueMeshForUpload(entity, std::move(subChunks), 1, fromTerrainEdit, versionState, version,
                         glm::vec3(1e10f), glm::vec3(-1e10f), false, false, 0, enqueueTime,
                         debugInfo);
}

void ChunkUploadSystem::enqueueMeshForUpload(
    entt::entity entity,
    std::vector<MeshData>&& subChunks,
    uint8_t mainSubChunkCount,
    bool fromTerrainEdit,
    std::shared_ptr<ChunkVersionState> versionState,
    uint32_t version,
    glm::vec3 tightMin,
    glm::vec3 tightMax,
    bool hasTight,
    bool isRemesh,
    uint32_t batchId,
    std::chrono::steady_clock::time_point enqueueTime,
    ChunkDebugAttribution debugInfo)
{
    // Lock-free enqueue: create node and push to head
    PendingUpload pu;
    pu.entity = entity;
    pu.subChunks = std::move(subChunks);
    pu.mainSubChunkCount = mainSubChunkCount;
    pu.fromTerrainEdit = fromTerrainEdit;
    pu.versionState = versionState;
    pu.version = version;
    pu.tightAABBMin = tightMin;
    pu.tightAABBMax = tightMax;
    pu.hasTightAABB = hasTight;
    pu.isRemesh = isRemesh;
    pu.batchId = batchId;
    pu.enqueueTime = (enqueueTime == std::chrono::steady_clock::time_point{})
        ? std::chrono::steady_clock::now()
        : enqueueTime;
    pu.debugInfo = debugInfo;
    
    auto* node = allocNode(std::move(pu));
    
    // Push to head atomically (LIFO stack pattern for simplicity)
    UploadNode* oldHead = m_queueHead.load(std::memory_order_relaxed);
    do {
        node->next.store(oldHead, std::memory_order_relaxed);
    } while (!m_queueHead.compare_exchange_weak(oldHead, node, 
                                                 std::memory_order_release,
                                                 std::memory_order_relaxed));
    
    // Update size for metrics
    m_queueSize.fetch_add(1, std::memory_order_relaxed);
}

void ChunkUploadSystem::clearQueue() {
    // Drain and return all nodes to pool from the lock-free stack
    UploadNode* head = m_queueHead.exchange(nullptr, std::memory_order_acquire);
    while (head) {
        UploadNode* next = head->next.load(std::memory_order_relaxed);
        freeNode(head);
        head = next;
    }
    m_queueSize.store(0, std::memory_order_relaxed);
    
    // Clear finalize queue
    while (!m_finalizeQueue.empty()) {
        m_finalizeQueue.pop();
    }
}

size_t ChunkUploadSystem::processUploads(
    entt::registry& registry,
    std::shared_mutex& registryMutex,
    BufferSuballocator* vbAllocator,
    BufferSuballocator* ibAllocator,
    UploadArena* uploadArena,
    ResourceUploader* uploader,
    uint64_t uploadReadyValue,
    IUploadCallback* callback,
    size_t maxUploadsOverride,
    bool terrainEditOnly)
{
    // Quick check: is there any work at all? If not, skip everything.
    UploadNode* head = m_queueHead.load(std::memory_order_acquire);
    if (!head && m_finalizeQueue.empty()) {
        m_currentUploadBudget = 0;
        return 0;
    }

    if (!head) {
        m_currentUploadBudget = 0;
        return 0;
    }

    const size_t queuedAtStart = m_queueSize.load(std::memory_order_relaxed);
    size_t maxBudget = queuedAtStart;
    if (maxUploadsOverride > 0) {
        maxBudget = std::min(maxBudget, maxUploadsOverride);
    }

    auto popQueuedNode = [this]() -> UploadNode* {
        UploadNode* node = m_queueHead.load(std::memory_order_acquire);
        while (node) {
            UploadNode* next = node->next.load(std::memory_order_relaxed);
            if (m_queueHead.compare_exchange_weak(
                    node,
                    next,
                    std::memory_order_acquire,
                    std::memory_order_acquire)) {
                node->next.store(nullptr, std::memory_order_relaxed);
                return node;
            }
        }
        return nullptr;
    };

    // Do not drain/sort/deduplicate the entire streaming backlog every frame
    // when the caller explicitly supplied a small budget. The old path touched
    // queuedAtStart nodes even if only 96 uploads could be processed, so a large
    // finished-mesh backlog showed up as persistent CPU time outside finalize.
    //
    // Keep terrainEditOnly full-scan: late edit catch-up must be allowed to fish
    // edit uploads out of a mixed queue immediately. Normal streaming gets a
    // bounded overscan window so distance sorting still has useful candidates
    // without scanning thousands of queued uploads per frame.
    size_t scanLimit = queuedAtStart;
    if (!terrainEditOnly && maxUploadsOverride > 0) {
        constexpr size_t kStreamingOverscanMultiplier = 4;
        constexpr size_t kStreamingMinimumScanWindow = 128;
        const size_t budgetWindow = std::max(maxBudget, kStreamingMinimumScanWindow);
        const size_t boundedScanLimit = budgetWindow * kStreamingOverscanMultiplier;
        scanLimit = std::min(queuedAtStart, boundedScanLimit);
    }

    // Reuse pre-allocated drain buffer (avoid per-frame heap allocation)
    m_drainBuffer.clear();
    for (size_t i = 0; i < scanLimit; ++i) {
        UploadNode* node = popQueuedNode();
        if (!node) break;
        m_drainBuffer.push_back(node);
    }
    if (m_drainBuffer.empty()) {
        m_currentUploadBudget = 0;
        return 0;
    }

    auto requeueNode = [this](UploadNode* node) {
        UploadNode* expected = m_queueHead.load(std::memory_order_relaxed);
        do {
            node->next.store(expected, std::memory_order_relaxed);
        } while (!m_queueHead.compare_exchange_weak(expected, node,
            std::memory_order_release, std::memory_order_relaxed));
    };

    // Deduplicate: if multiple uploads exist for the same entity, keep only the latest.
    // The drain buffer is in LIFO order: index 0 = newest push, index N-1 = oldest push.
    // Iterate newest-first so the first occurrence (newest) is kept and older duplicates
    // are freed.  The old reverse iteration kept the OLDEST upload, which then failed
    // the version check because a newer edit had already bumped the version.
    {
        m_deduplicateSet.clear();
        for (size_t i = 0; i < m_drainBuffer.size(); ++i) {
            uint32_t key = static_cast<uint32_t>(entt::to_integral(m_drainBuffer[i]->data.entity));
            if (!m_deduplicateSet.insert(key).second) {
                freeNode(m_drainBuffer[i]);
                m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                m_drainBuffer[i] = nullptr;
            }
        }
        m_drainBuffer.erase(std::remove(m_drainBuffer.begin(), m_drainBuffer.end(), nullptr), m_drainBuffer.end());
    }

    if (terrainEditOnly) {
        for (size_t i = 0; i < m_drainBuffer.size(); ++i) {
            UploadNode* node = m_drainBuffer[i];
            if (!node->data.fromTerrainEdit) {
                requeueNode(node);
                m_drainBuffer[i] = nullptr;
            }
        }
        m_drainBuffer.erase(std::remove(m_drainBuffer.begin(), m_drainBuffer.end(), nullptr), m_drainBuffer.end());
        if (m_drainBuffer.empty()) {
            m_currentUploadBudget = 0;
            return 0;
        }
    }

    // Sort by Chebyshev distance from center (ascending) and mark invalid entities
    glm::ivec3 center = m_centerChunk;
    {
        std::shared_lock regLock(registryMutex);
        for (size_t i = 0; i < m_drainBuffer.size(); ) {
            auto* node = m_drainBuffer[i];
            if (!registry.valid(node->data.entity)) {
                // Entity destroyed before upload — prune now under this single lock
                freeNode(node);
                m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                m_drainBuffer[i] = m_drainBuffer.back();
                m_drainBuffer.pop_back();
                continue;
            }
            if (registry.all_of<ChunkCoord>(node->data.entity)) {
                const auto& cc = registry.get<ChunkCoord>(node->data.entity);
                node->data.distanceSortKey =
                    std::max(std::abs(cc.x - center.x), std::abs(cc.z - center.z));
            } else {
                node->data.distanceSortKey = 999999;
            }
            ++i;
        }
    }
    std::sort(m_drainBuffer.begin(), m_drainBuffer.end(), [&](UploadNode* a, UploadNode* b) {
        if (a->data.fromTerrainEdit != b->data.fromTerrainEdit) {
            return a->data.fromTerrainEdit && !b->data.fromTerrainEdit;
        }
        const int distA = a->data.distanceSortKey;
        const int distB = b->data.distanceSortKey;
        if (distA != distB) {
            return distA < distB;
        }
        return a->data.enqueueTime < b->data.enqueueTime;
    });

    const size_t initialQueueSize = m_drainBuffer.size();
    const size_t uploadBudget = std::min(initialQueueSize, maxBudget);
    m_currentUploadBudget = static_cast<uint32_t>(uploadBudget);

    auto emitUploadEvent = [&](entt::entity entity,
                               const glm::ivec3* chunkCoord,
                               int lodLevel,
                               const char* stage,
                               const char* reason,
                               uint32_t batchId,
                               uint32_t expectedVersion,
                               uint32_t actualVersion,
                               const ChunkDebugAttribution* debugInfo) {
        if (!callback) return;
        callback->onUploadPipelineEvent(
            entity,
            chunkCoord,
            lodLevel,
            stage,
            reason,
            batchId,
            expectedVersion,
            actualVersion,
            debugInfo);
    };

    auto lookupEntityContext = [&](entt::entity entity, glm::ivec3& outCoord, int& outLodLevel) -> const glm::ivec3* {
        outLodLevel = -1;
        std::shared_lock regLock(registryMutex);
        if (!registry.valid(entity)) {
            return nullptr;
        }
        if (registry.all_of<ChunkCoord>(entity)) {
            outCoord = registry.get<ChunkCoord>(entity).toVec3();
        } else {
            return nullptr;
        }
        if (registry.all_of<Chunk>(entity)) {
            outLodLevel = registry.get<Chunk>(entity).lodLevel;
        }
        return &outCoord;
    };

    // ── PHASE 1: Prepare upload data OUTSIDE the unique lock ──
    // Validate entities, allocate GPU memory, and record staging copies.
    // This moves the expensive work (allocator locks, staging copies) out
    // of the registry unique_lock, dramatically reducing lock hold time.
    struct PreparedUpload {
        UploadNode* node;
        BufferSlice vb;
        BufferSlice ib;
        MeshHandle handle;
        bool allEmpty;
        bool valid;
        bool skipVersion;
        const MeshData* firstSubChunk;
    };

    // Use a local small buffer for typical case, heap only if many uploads
    std::vector<PreparedUpload> prepared;
    prepared.reserve(uploadBudget);
    std::vector<UploadNode*> deferredUploads;
    deferredUploads.reserve(m_drainBuffer.size());

    size_t processed = 0;
    bool allocationFailed = false;
    for (size_t nodeIdx = 0; nodeIdx < m_drainBuffer.size(); ++nodeIdx) {
        UploadNode* node = m_drainBuffer[nodeIdx];

        // Re-enqueue over-budget nodes or nodes after allocation failure
        if (processed >= uploadBudget || allocationFailed) {
            deferredUploads.push_back(node);
            continue;
        }

        PendingUpload& req = node->data;

        // Entity validity already checked during distance-sort shared_lock above.
        // Re-check is deferred to Phase 2 under the unique_lock.

        // Version check (no lock needed — atomic)
        if (req.versionState) {
            uint32_t currentVersion = req.versionState->version.load(std::memory_order_acquire);
            if (currentVersion != req.version) {
                glm::ivec3 chunkCoord{};
                int lodLevel = -1;
                emitUploadEvent(
                    req.entity,
                    lookupEntityContext(req.entity, chunkCoord, lodLevel),
                    lodLevel,
                    "UploadPrepare",
                    "VersionMismatchDrop",
                    req.batchId,
                    req.version,
                    currentVersion,
                    &req.debugInfo);
                // Signal batch so LOD transitions don't get permanently stuck
                // waiting for a chunk whose upload was superseded by a newer
                // pipeline run.  The entity that bumped the version owns
                // inFlight and will clear it via its own finalize path —
                // do NOT clear inFlight here or the drain may create
                // duplicate batches while the newer pipeline is in-flight.
                if (req.isRemesh && req.batchId != 0 && m_batchCallback) {
                    m_batchCallback->onBatchChunkReady(req.batchId);
                }
                freeNode(node);
                m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                continue;
            }
        }

        // Check for empty SubChunks
        bool allEmpty = req.subChunks.empty();
        if (!allEmpty) {
            allEmpty = true;
            for (const auto& sub : req.subChunks) {
                if (!sub.isEmpty()) {
                    allEmpty = false;
                    break;
                }
            }
        }

        PreparedUpload prep;
        prep.node = node;
        prep.allEmpty = allEmpty;
        prep.valid = true;
        prep.skipVersion = false;
        prep.firstSubChunk = nullptr;
        req.debugInfo.fromLodBatch = req.isRemesh;
        req.debugInfo.fromTerrainEdit = req.fromTerrainEdit;

        if (allEmpty) {
            // Empty mesh — no GPU allocation needed
            req.debugInfo.uploadBytes = 0;
            req.debugInfo.subChunkCount = 0;
            prep.handle = MeshHandle{};
            prep.handle.subChunkCount = 0;
            prep.handle.gpuReadyValue = uploadReadyValue;
            prepared.push_back(prep);
            ++processed;
            continue;
        }

        // Calculate total sizes
        VkDeviceSize totalVbSize = 0;
        VkDeviceSize totalIbSize = 0;
        size_t validSubChunks = 0;

        for (const auto& sub : req.subChunks) {
            if (sub.isEmpty()) continue;
            totalVbSize += sub.vertices.size() * sizeof(Vertex);
            totalIbSize += sub.indices.size() * sizeof(uint16_t);
            validSubChunks++;
        }

        if (validSubChunks > MAX_SUBCHUNKS) {
            // Loud + persistent: silent truncation here was a known cause of stale
            // GPU vb/ib slices being reused by other chunks, producing stretched
            // "far away" triangles. If this fires, the splitter (or its caller)
            // produced more sub-meshes than MAX_SUBCHUNKS can hold; bump
            // MAX_SUBCHUNKS in include/world/chunks/core/Chunk.h (and the matching
            // GPU_MAX_SUBCHUNKS / shader #define / MAX_RUNTIME_SUBMESHES).
            std::cerr << "[ChunkUploadSystem] CRITICAL: Chunk has " << validSubChunks
                      << " SubChunks, max is " << MAX_SUBCHUNKS
                      << ". TRUNCATING (geometry will be lost — expect visual corruption)."
                      << std::endl;
            validSubChunks = MAX_SUBCHUNKS;
        }

        const VkDeviceSize totalUploadBytes = totalVbSize + totalIbSize;
        req.debugInfo.uploadBytes = static_cast<uint64_t>(totalUploadBytes);
        req.debugInfo.subChunkCount = static_cast<uint16_t>(validSubChunks);
        // Allocate GPU buffers (outside registry lock — allocator has its own lock)
        BufferSlice vb = vbAllocator->allocate(totalVbSize, 16);
        BufferSlice ib = ibAllocator->allocate(totalIbSize, 16);

        if (!vb.isValid() || !ib.isValid()) {
            // Throttle log: only print every 60 failures to avoid spam
            uint32_t failCount = m_allocationFailures.fetch_add(1, std::memory_order_relaxed) + 1;
            if (failCount == 1 || failCount % 60 == 0) {
                VkDeviceSize vbTotal = vbAllocator->getTotalCapacity();
                VkDeviceSize vbUsed = vbAllocator->getAllocatedBytes();
                VkDeviceSize ibTotal = ibAllocator->getTotalCapacity();
                VkDeviceSize ibUsed = ibAllocator->getAllocatedBytes();
                std::cout << "[ChunkUploadSystem] BUFFER ALLOCATION FAILED (x" << failCount << ") - VB: " << (vbUsed / 1024 / 1024) << "/" << (vbTotal / 1024 / 1024)
                          << "MB, IB: " << (ibUsed / 1024 / 1024) << "/" << (ibTotal / 1024 / 1024) << "MB" << std::endl;
            }
            if (vb.isValid()) vbAllocator->free(vb);
            if (ib.isValid()) ibAllocator->free(ib);

            glm::ivec3 chunkCoord{};
            int lodLevel = -1;
            emitUploadEvent(
                req.entity,
                lookupEntityContext(req.entity, chunkCoord, lodLevel),
                lodLevel,
                "UploadPrepare",
                "GpuBufferAllocFailedRequeue",
                req.batchId,
                req.version,
                req.version,
                &req.debugInfo);

            // Preserve the same node. Rebuilding the request via enqueue would
            // transiently double-count m_queueSize and allocate another node.
            deferredUploads.push_back(node);
            allocationFailed = true;
            continue;
        }

        // Allocation succeeded — reset failure counter
        m_allocationFailures.store(0, std::memory_order_relaxed);

        // Build MeshHandle and record staging copies (outside registry lock)
        MeshHandle handle;
        handle.vb = vb;
        handle.ib = ib;
        handle.sourceBuffer = vb.buffer;
        handle.gpuReadyValue = uploadReadyValue;
        handle.subChunkCount = 0;
        handle.mainSubChunkCount = req.mainSubChunkCount;

        VkDeviceSize vbOffset = 0;
        VkDeviceSize ibOffset = 0;
        const MeshData* firstSubChunk = nullptr;

        for (const auto& sub : req.subChunks) {
            if (sub.isEmpty()) continue;
            if (handle.subChunkCount >= MAX_SUBCHUNKS) {
                std::cerr << "[ChunkUploadSystem] CRITICAL: dropping submesh #"
                          << static_cast<int>(handle.subChunkCount)
                          << " — MAX_SUBCHUNKS=" << MAX_SUBCHUNKS
                          << " reached. This will corrupt rendering." << std::endl;
                break;
            }

            // Validate that every 16-bit index fits within this submesh's
            // vertex range. If not, the splitter has a bug and we'd render
            // garbage from a neighbouring chunk's slice.
            const uint32_t subVertCount = static_cast<uint32_t>(sub.vertices.size());
            if (subVertCount > 65536u) {
                std::cerr << "[ChunkUploadSystem] CRITICAL: submesh has "
                          << subVertCount << " verts > 65536 — 16-bit indices will overflow."
                          << std::endl;
            }
#ifndef NDEBUG
            for (uint16_t idx : sub.indices) {
                if (static_cast<uint32_t>(idx) >= subVertCount) {
                    std::cerr << "[ChunkUploadSystem] CRITICAL: submesh index "
                              << static_cast<uint32_t>(idx)
                              << " >= submesh vertex count " << subVertCount
                              << " — splitter produced out-of-range indices." << std::endl;
                    break;
                }
            }
#endif

            VkDeviceSize subVbSize = sub.vertices.size() * sizeof(Vertex);
            VkDeviceSize subIbSize = sub.indices.size() * sizeof(uint16_t);

            BufferSlice subVb = vb;
            subVb.offset += vbOffset;
            subVb.size = subVbSize;

            BufferSlice subIb = ib;
            subIb.offset += ibOffset;
            subIb.size = subIbSize;

            // Record staging copies (uses uploadArena which has its own lock)
            UploadRequest vbReq{sub.vertices.data(), subVbSize, subVb};
            UploadRequest ibReq{sub.indices.data(), subIbSize, subIb};
            uploader->recordCopy(vbReq, *uploadArena);
            uploader->recordCopy(ibReq, *uploadArena);

            uint32_t firstIndex = static_cast<uint32_t>((ib.offset + ibOffset) / sizeof(uint16_t));
            int32_t vertexOffset = static_cast<int32_t>((vb.offset + vbOffset) / sizeof(Vertex));

            handle.subChunks[handle.subChunkCount].firstIndex = firstIndex;
            handle.subChunks[handle.subChunkCount].indexCount = static_cast<uint32_t>(sub.indices.size());
            handle.subChunks[handle.subChunkCount].vertexOffset = vertexOffset;
            handle.subChunkCount++;

            if (!firstSubChunk) {
                firstSubChunk = &sub;
            }

            vbOffset += subVbSize;
            ibOffset += subIbSize;
        }

        prep.vb = vb;
        prep.ib = ib;
        prep.handle = handle;
        prep.firstSubChunk = firstSubChunk;
        prepared.push_back(prep);
        ++processed;
    }

    // m_drainBuffer is sorted nearest-first. Push deferred nodes back in reverse
    // order so the nearest remaining upload is at the stack head next frame.
    for (auto it = deferredUploads.rbegin(); it != deferredUploads.rend(); ++it) {
        requeueNode(*it);
    }

    // ── PHASE 2: Apply to registry under unique lock (fast — no allocations) ──
    // Only emplace_or_replace / get<> calls — bounded by budget count.
    {
        std::unique_lock regLock(registryMutex);

        for (auto& prep : prepared) {
            PendingUpload& req = prep.node->data;

            if (!registry.valid(req.entity)) {
                // Entity destroyed between phase 1 and 2 — free allocated buffers
                if (!prep.allEmpty && prep.vb.isValid()) vbAllocator->free(prep.vb);
                if (!prep.allEmpty && prep.ib.isValid()) ibAllocator->free(prep.ib);
                // Signal batch so it doesn't get permanently stuck
                if (req.isRemesh && req.batchId != 0 && m_batchCallback) {
                    m_batchCallback->onBatchChunkReady(req.batchId);
                }
                emitUploadEvent(
                    req.entity,
                    nullptr,
                    -1,
                    "UploadApply",
                    "EntityDestroyedBeforeApply",
                    req.batchId,
                    req.version,
                    req.version,
                    &req.debugInfo);
                freeNode(prep.node);
                m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                continue;
            }

            if (prep.allEmpty) {
                if (req.isRemesh && req.batchId != 0) {
                    // Empty chunk in a LOD remesh batch — store an empty
                    // PendingMeshHandle so processLODSwaps can swap it in
                    // (replacing the old mesh) and push a finalize request
                    // so inFlight is cleared.  Without this, empty batch
                    // entities are permanently stuck with inFlight=true and
                    // processLODSwaps reports missingPending.
                    PendingMeshHandle pending;
                    pending.handle = prep.handle;
                    pending.batchId = req.batchId;
                    pending.uploadEnqueueTime = req.enqueueTime;
                    pending.debugInfo = req.debugInfo;
                    pending.debugInfo.residency = deriveChunkResidencyKind(
                        /*gpuResident=*/false,
                        pending.debugInfo.artifactCacheResident,
                        /*pendingBatch=*/true);

                    // Free any prior PendingMeshHandle's GPU resources
                    if (registry.all_of<PendingMeshHandle>(req.entity)) {
                        auto& old = registry.get<PendingMeshHandle>(req.entity);
                        if (old.handle.vb.isValid() && vbAllocator) vbAllocator->free(old.handle.vb);
                        if (old.handle.ib.isValid() && ibAllocator) ibAllocator->free(old.handle.ib);
                        if (m_gpuCulling && old.handle.gpuCullingSlot != UINT32_MAX) {
                            m_gpuCulling->freeSlot(old.handle.gpuCullingSlot);
                        }
                    }

                    registry.emplace_or_replace<PendingMeshHandle>(req.entity, pending);

                    if (m_batchCallback) {
                        m_batchCallback->onBatchChunkReady(req.batchId);
                    }

                    ChunkFinalizeRequest finalizeReq;
                    finalizeReq.entity = req.entity;
                    finalizeReq.uploadValue = uploadReadyValue;
                    finalizeReq.enqueueTime = req.enqueueTime;
                    finalizeReq.versionState = req.versionState;
                    finalizeReq.debugInfo = pending.debugInfo;
                    m_finalizeQueue.push(finalizeReq);

                    freeNode(prep.node);
                    m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                    continue;
                }

                if (callback && registry.all_of<MeshHandle>(req.entity)) {
                    callback->onMeshHandleRemoved(registry.get<MeshHandle>(req.entity));
                }
                // GHOST-GEOMETRY FIX: when a solo edit fully empties a chunk
                // (allEmpty + non-batch), we replace the existing MeshHandle
                // with an empty one. The old handle's vb/ib slices and GPU
                // culling slot must be freed explicitly — emplace_or_replace
                // simply overwrites the component without touching its GPU
                // resources. Leaking the slot leaves stale ChunkDrawData
                // active in the GPU compute culling shader, which keeps
                // drawing the pre-edit mesh from the leaked vb/ib (visible
                // only with G mode ON, since the CPU gather path filters on
                // mesh.getTotalIndexCount() == 0). The chunk also has no
                // collider because enqueueEditCollision tore the body down,
                // matching the "ghost faces with no collision" symptom.
                if (registry.all_of<MeshHandle>(req.entity)) {
                    auto& oldMesh = registry.get<MeshHandle>(req.entity);
                    if (oldMesh.vb.isValid() && vbAllocator) vbAllocator->free(oldMesh.vb);
                    if (oldMesh.ib.isValid() && ibAllocator) ibAllocator->free(oldMesh.ib);
                    if (m_gpuCulling && oldMesh.gpuCullingSlot != UINT32_MAX) {
                        m_gpuCulling->freeSlot(oldMesh.gpuCullingSlot);
                    }
                }
                // Free stale PendingMeshHandle from a prior LOD batch; otherwise
                // processLODSwaps would overwrite this newer empty mesh.
                if (registry.all_of<PendingMeshHandle>(req.entity)) {
                    auto& stale = registry.get<PendingMeshHandle>(req.entity);
                    if (stale.handle.vb.isValid() && vbAllocator) vbAllocator->free(stale.handle.vb);
                    if (stale.handle.ib.isValid() && ibAllocator) ibAllocator->free(stale.handle.ib);
                    if (m_gpuCulling && stale.handle.gpuCullingSlot != UINT32_MAX) {
                        m_gpuCulling->freeSlot(stale.handle.gpuCullingSlot);
                    }
                    registry.remove<PendingMeshHandle>(req.entity);
                }
                registry.emplace_or_replace<MeshHandle>(req.entity, prep.handle);

                ChunkFinalizeRequest finalizeReq;
                finalizeReq.entity = req.entity;
                finalizeReq.uploadValue = uploadReadyValue;
                finalizeReq.enqueueTime = req.enqueueTime;
                finalizeReq.versionState = req.versionState;
                finalizeReq.debugInfo = req.debugInfo;
                finalizeReq.debugInfo.residency = deriveChunkResidencyKind(
                    /*gpuResident=*/true,
                    finalizeReq.debugInfo.artifactCacheResident,
                    /*pendingBatch=*/false);
                m_finalizeQueue.push(finalizeReq);

                freeNode(prep.node);
                m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                continue;
            }

            MeshHandle& handle = prep.handle;

            if (req.isRemesh && req.batchId != 0) {
                // --- LOD REMESH PATH ---
                // Batch was cancelled while this upload was in-flight: drop it.
                if (!m_batchCallback || !m_batchCallback->isBatchActive(req.batchId)) {
                    glm::ivec3 chunkCoord{};
                    const glm::ivec3* chunkCoordPtr = nullptr;
                    int lodLevel = -1;
                    if (registry.all_of<ChunkCoord>(req.entity)) {
                        chunkCoord = registry.get<ChunkCoord>(req.entity).toVec3();
                        chunkCoordPtr = &chunkCoord;
                    }
                    if (registry.all_of<Chunk>(req.entity)) {
                        lodLevel = registry.get<Chunk>(req.entity).lodLevel;
                    }
                    if (prep.vb.isValid()) vbAllocator->free(prep.vb);
                    if (prep.ib.isValid()) ibAllocator->free(prep.ib);
                    emitUploadEvent(
                        req.entity,
                        chunkCoordPtr,
                        lodLevel,
                        "UploadApply",
                        "RemeshBatchCancelledDrop",
                        req.batchId,
                        req.version,
                        req.version,
                        &req.debugInfo);
                    freeNode(prep.node);
                    m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                    continue;
                }

                if (m_gpuCulling && m_gpuCulling->isReady() && registry.all_of<AABB, ChunkCoord>(req.entity)) {
                    // LOD remeshes live in PendingMeshHandle until the whole
                    // batch swaps. Keep their culling slots inactive so the
                    // GPU cannot draw pending LOD meshes beside the old mesh.
                    uint32_t slot = m_gpuCulling->allocateSlot(/*active=*/false);
                    if (slot != UINT32_MAX) {
                        handle.gpuCullingSlot = slot;

                        const auto& chunkAabb = registry.get<AABB>(req.entity);
                        const auto& coord = registry.get<ChunkCoord>(req.entity);

                        ChunkDrawData drawData{};
                        drawData.subChunkCount = handle.subChunkCount;

                        for (uint8_t i = 0; i < handle.subChunkCount && i < GPU_MAX_SUBCHUNKS; ++i) {
                            const auto& sc = handle.subChunks[i];
                            drawData.draws[i].indexCount = sc.indexCount;
                            drawData.draws[i].instanceCount = 1;
                            drawData.draws[i].firstIndex = sc.firstIndex;
                            drawData.draws[i].vertexOffset = sc.vertexOffset;
                            drawData.draws[i].firstInstance = 0;
                        }

                        if (req.hasTightAABB) {
                            glm::vec3 chunkOrigin = chunkAabb.min;
                            drawData.aabbMin = glm::vec4(chunkOrigin + req.tightAABBMin, 0.0f);
                            drawData.aabbMax = glm::vec4(chunkOrigin + req.tightAABBMax, 0.0f);
                            drawData.aabbMin = glm::max(drawData.aabbMin, glm::vec4(chunkAabb.min, 0.0f));
                            drawData.aabbMax = glm::min(drawData.aabbMax, glm::vec4(chunkAabb.max, 0.0f));
                        } else {
                            drawData.aabbMin = glm::vec4(chunkAabb.min, 0.0f);
                            drawData.aabbMax = glm::vec4(chunkAabb.max, 0.0f);
                        }

                        drawData.origin = glm::vec4(float(coord.x), float(coord.y), float(coord.z), 0.0f);
                        uint32_t readyValueBits = static_cast<uint32_t>(handle.gpuReadyValue);
                        drawData.origin.w = *reinterpret_cast<float*>(&readyValueBits);
                        // LOD swaps replace one representation of the same
                        // terrain with another. They must remain occludable
                        // immediately; only topology-changing edits need Hi-Z
                        // grace to avoid stale-pyramid death spirals.
                        drawData.hiZGraceTimeline = readyValueBits;

                        VkDeviceSize dataSize = sizeof(ChunkDrawData);
                        VkDeviceSize slotOffset = slot * sizeof(ChunkDrawData);

                        UploadRequest cullReq{};
                        cullReq.src = &drawData;
                        cullReq.size = dataSize;
                        cullReq.dst = BufferSlice{m_gpuCulling->getAllDrawsBuffer(), slotOffset, dataSize};
                        uploader->recordCopy(cullReq, *uploadArena);
                        m_gpuCulling->noteChunkDrawDataUpload(
                            slot,
                            drawData,
                            req.fromTerrainEdit,
                            /*replacesExistingMesh=*/true);
                    }
                }

                if (registry.all_of<PendingMeshHandle>(req.entity)) {
                    auto& oldPending = registry.get<PendingMeshHandle>(req.entity);
                    if (oldPending.handle.vb.isValid() && vbAllocator) vbAllocator->free(oldPending.handle.vb);
                    if (oldPending.handle.ib.isValid() && ibAllocator) ibAllocator->free(oldPending.handle.ib);
                    if (m_gpuCulling && oldPending.handle.gpuCullingSlot != UINT32_MAX) {
                        m_gpuCulling->freeSlot(oldPending.handle.gpuCullingSlot);
                    }
                }

                PendingMeshHandle pending;
                pending.handle = handle;
                pending.batchId = req.batchId;
                pending.uploadEnqueueTime = req.enqueueTime;
                pending.debugInfo = req.debugInfo;
                pending.debugInfo.residency = deriveChunkResidencyKind(
                    /*gpuResident=*/false,
                    pending.debugInfo.artifactCacheResident,
                    /*pendingBatch=*/true);
                registry.emplace_or_replace<PendingMeshHandle>(req.entity, pending);

                if (m_batchCallback) {
                    m_batchCallback->onBatchChunkReady(req.batchId);
                }

                ChunkFinalizeRequest finalizeReq;
                finalizeReq.entity = req.entity;
                finalizeReq.uploadValue = uploadReadyValue;
                finalizeReq.enqueueTime = req.enqueueTime;
                finalizeReq.versionState = req.versionState;
                finalizeReq.debugInfo = pending.debugInfo;
                m_finalizeQueue.push(finalizeReq);

                m_meshesUploaded.fetch_add(1, std::memory_order_relaxed);
                freeNode(prep.node);
                m_queueSize.fetch_sub(1, std::memory_order_relaxed);
                continue;
            }

            // --- NORMAL PATH ---
            const bool hasExistingMesh = registry.all_of<MeshHandle>(req.entity);
            const uint32_t existingCullingSlot =
                hasExistingMesh ? registry.get<MeshHandle>(req.entity).gpuCullingSlot : UINT32_MAX;
            // If a LOD batch stored a PendingMeshHandle before this entity was
            // updated through a non-batch path (e.g. edit remesh), the stale
            // pending mesh must be freed.  Otherwise processLODSwaps would
            // overwrite the newer non-batch mesh with the older batch mesh,
            // producing [Load] entries after every [Edit]. Also drops any prior
            // solo-edit PendingMeshHandle superseded by this newer upload.
            if (registry.all_of<PendingMeshHandle>(req.entity)) {
                auto& stale = registry.get<PendingMeshHandle>(req.entity);
                if (stale.handle.vb.isValid()) vbAllocator->free(stale.handle.vb);
                if (stale.handle.ib.isValid()) ibAllocator->free(stale.handle.ib);
                // Solo-edit uploads reuse the current MeshHandle's culling slot.
                // If the stale pending handle references that same slot, freeing
                // it here would deactivate the live chunk and make it vanish
                // under GPU culling (NotDrawn.SlotInactive).
                if (m_gpuCulling &&
                    stale.handle.gpuCullingSlot != UINT32_MAX &&
                    stale.handle.gpuCullingSlot != existingCullingSlot) {
                    m_gpuCulling->freeSlot(stale.handle.gpuCullingSlot);
                }
                registry.remove<PendingMeshHandle>(req.entity);
            }

            // GPU Culling slot allocation + upload
            if (m_gpuCulling && m_gpuCulling->isReady() && registry.all_of<AABB, ChunkCoord>(req.entity)) {
                // Solo-edit path: REUSE the existing chunk's gpuCullingSlot so we
                // don't briefly have two active slots for the same chunk (which
                // causes the GPU to draw the chunk twice → z-fighting / flicker).
                // The slot's draw data is overwritten with the new vb/ib pointers
                // and gpuReadyValue=batchValue; this frame's culling dispatch
                // waits for the upload semaphore so it reads the NEW data, while
                // in-flight previous frames already captured indirect draws using
                // the OLD vb/ib (still alive until processSoloPendingSwaps).
                uint32_t reuseSlot = UINT32_MAX;
                if (hasExistingMesh) {
                    reuseSlot = existingCullingSlot;
                }
                uint32_t slot = (reuseSlot != UINT32_MAX) ? reuseSlot : m_gpuCulling->allocateSlot();
                if (slot != UINT32_MAX) {
                    handle.gpuCullingSlot = slot;

                    const auto& chunkAabb = registry.get<AABB>(req.entity);
                    const auto& coord = registry.get<ChunkCoord>(req.entity);

                    ChunkDrawData drawData{};
                    drawData.subChunkCount = handle.subChunkCount;

                    for (uint8_t i = 0; i < handle.subChunkCount && i < GPU_MAX_SUBCHUNKS; ++i) {
                        const auto& sc = handle.subChunks[i];
                        drawData.draws[i].indexCount = sc.indexCount;
                        drawData.draws[i].instanceCount = 1;
                        drawData.draws[i].firstIndex = sc.firstIndex;
                        drawData.draws[i].vertexOffset = sc.vertexOffset;
                        drawData.draws[i].firstInstance = 0;
                    }

                    if (req.hasTightAABB) {
                        glm::vec3 chunkOrigin = chunkAabb.min;
                        drawData.aabbMin = glm::vec4(chunkOrigin + req.tightAABBMin, 0.0f);
                        drawData.aabbMax = glm::vec4(chunkOrigin + req.tightAABBMax, 0.0f);
                        drawData.aabbMin = glm::max(drawData.aabbMin, glm::vec4(chunkAabb.min, 0.0f));
                        drawData.aabbMax = glm::min(drawData.aabbMax, glm::vec4(chunkAabb.max, 0.0f));
                    } else {
                        drawData.aabbMin = glm::vec4(chunkAabb.min, 0.0f);
                        drawData.aabbMax = glm::vec4(chunkAabb.max, 0.0f);
                    }

                    glm::ivec3 chunkCoordVec(coord.x, coord.y, coord.z);
                    drawData.origin = glm::vec4(float(coord.x), float(coord.y), float(coord.z), 0.0f);
                    uint32_t readyValueBits = static_cast<uint32_t>(handle.gpuReadyValue);
                    drawData.origin.w = *reinterpret_cast<float*>(&readyValueBits);
                    // Hi-Z grace is only needed when replacing already-visible
                    // topology (terrain edits / solo remeshes). Initial loads
                    // and LOD batch swaps should be occludable immediately.
                    constexpr uint32_t HI_Z_GRACE_OFFSET = 4u;
                    const uint32_t hiZGraceOffset = hasExistingMesh ? HI_Z_GRACE_OFFSET : 0u;
                    drawData.hiZGraceTimeline = readyValueBits + hiZGraceOffset;

                    VkDeviceSize dataSize = sizeof(ChunkDrawData);
                    VkDeviceSize slotOffset = slot * sizeof(ChunkDrawData);

                    UploadRequest cullReq{};
                    cullReq.src = &drawData;
                    cullReq.size = dataSize;
                    cullReq.dst = BufferSlice{m_gpuCulling->getAllDrawsBuffer(), slotOffset, dataSize};
                    uploader->recordCopy(cullReq, *uploadArena);
                    m_gpuCulling->noteChunkDrawDataUpload(
                        slot,
                        drawData,
                        req.fromTerrainEdit,
                        hasExistingMesh);
                }
            }

            // Edit path: an existing MeshHandle is already visible. Stage the
            // new handle as PendingMeshHandle (batchId=0 = solo swap sentinel)
            // so the old mesh keeps rendering until the upload fence signals.
            // World::processSoloPendingSwaps performs the atomic swap once
            // deviceTimeline >= pending.handle.gpuReadyValue.
            // Initial-load path: no prior mesh, install directly (renderer
            // gates visibility on the timeline via mesh.gpuReadyValue anyway).
            if (hasExistingMesh) {
                PendingMeshHandle pending;
                pending.handle = handle;
                pending.batchId = 0;
                pending.uploadEnqueueTime = req.enqueueTime;
                pending.debugInfo = req.debugInfo;
                pending.debugInfo.residency = deriveChunkResidencyKind(
                    /*gpuResident=*/false,
                    pending.debugInfo.artifactCacheResident,
                    /*pendingBatch=*/true);
                registry.emplace_or_replace<PendingMeshHandle>(req.entity, pending);
            } else {
                if (callback) callback->onMeshHandleAdded(handle);
                registry.emplace_or_replace<MeshHandle>(req.entity, handle);
            }

            // Callback for collision (LOD 0 only)
            if (callback &&
                req.debugInfo.collisionSource != ChunkCollisionSource::None &&
                prep.firstSubChunk &&
                registry.all_of<Chunk, ChunkCoord>(req.entity)) {
                const auto& chunk = registry.get<Chunk>(req.entity);
                if (chunk.lodLevel == 0) {
                    const auto& chunkCoord = registry.get<ChunkCoord>(req.entity);
                    callback->onMeshUploaded(
                        req.entity,
                        chunkCoord.toVec3(),
                        prep.firstSubChunk->vertices,
                        prep.firstSubChunk->indices,
                        chunk.lodLevel);
                }
            }

            m_meshesUploaded.fetch_add(1, std::memory_order_relaxed);
            // Any upload that REPLACES an existing mesh (terrain edit, late
            // remesh, regen) can expose previously-occluded neighbor chunks.
            // Initial chunk loads (no prior MeshHandle) cannot — the area was
            // empty, so the pyramid has nothing stale for that screen region.
            // NOTE: req.isRemesh in this codebase only means "LOD-batch remesh"
            // — terrain edits set it to false. Use hasExistingMesh instead.
            if (hasExistingMesh && req.debugInfo.affectsShadowGeometry) {
                m_remeshUploadsThisCall.fetch_add(1, std::memory_order_relaxed);
            }

            ChunkFinalizeRequest finalizeReq;
            finalizeReq.entity = req.entity;
            finalizeReq.uploadValue = uploadReadyValue;
            finalizeReq.enqueueTime = req.enqueueTime;
            finalizeReq.versionState = req.versionState;
            finalizeReq.debugInfo = req.debugInfo;
            finalizeReq.debugInfo.residency = deriveChunkResidencyKind(
                /*gpuResident=*/!hasExistingMesh,
                finalizeReq.debugInfo.artifactCacheResident,
                /*pendingBatch=*/hasExistingMesh);
            if (!hasExistingMesh) {
                // Initial visibility introduces geometry to shadow gather even
                // if the upload was queued by a material refresh path.
                finalizeReq.debugInfo.affectsShadowGeometry = true;
            }
            m_finalizeQueue.push(finalizeReq);

            freeNode(prep.node);
            m_queueSize.fetch_sub(1, std::memory_order_relaxed);
        }
    }

    return processed;
}

````

## src\world\finalize\WorldFinalizeQueue.cpp

Description: No CC-DESC found. C++ struct 'FinalizeEntry'.

````cpp
// GPT-DESC: Finalizes uploaded chunk meshes, ready-state transitions, visual tracking, and collision refresh.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/common/Mesh.h"
#include <algorithm>
#include <chrono>
#include <limits>
#include <memory>
#include <string>
#include <vector>

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

## src\world\finalize\WorldTopologyChanges.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Tracks mesh topology revision history for terrain/shadow cache invalidation.
#include "world/World.h"
#include <algorithm>
#include <atomic>
#include <mutex>
#include <vector>

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

````

## src\world\lod\WorldLODTransitions.cpp

Description: No CC-DESC found. C++ struct 'RemeshCandidate'.

````cpp
// GPT-DESC: Owns World LOD transition scans, render-distance LOD updates, and remesh queue draining.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "world/ChunkHoleTracker.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/culling/GPUCullingSystem.h"
#include "physics/PhysicsWorld.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iostream>
#include <limits>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>


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

## src\world\lod\WorldLODSwaps.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Owns World pending LOD/edit mesh swaps and deferred GPU resource retirement.
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "world/ChunkHoleTracker.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/culling/GPUCullingSystem.h"
#include "physics/PhysicsWorld.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <iostream>
#include <limits>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>


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

````
