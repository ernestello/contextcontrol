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


## src\rendering\lighting\shadow\ShadowSunCascades.cpp

Description: No CC-DESC found.

````cpp
#include "ShadowInternal.h"

#include "world/World.h"
#include "world/config/WorldConfig.h"

// GPT-DESC: Implements sun-cascade helper math, gather bounds, scroll planning, and topology invalidation.

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <initializer_list>
#include <limits>
#include <vector>

namespace ShadowSystemInternal {

SunCascadeScrollPlan makeSunCascadeScrollPlan(
    const glm::mat4& oldVP,
    const glm::mat4& newVP,
    uint32_t mapSize) {
    SunCascadeScrollPlan plan{};
    if (mapSize == 0u) {
        return plan;
    }

    for (int col = 0; col < 4; ++col) {
        for (int row = 0; row < 4; ++row) {
            if (col == 3 && (row == 0 || row == 1)) {
                continue;
            }
            if (floatToBits(oldVP[col][row]) != floatToBits(newVP[col][row])) {
                return plan;
            }
        }
    }

    const float halfMap = static_cast<float>(mapSize) * 0.5f;
    const float dxFloat = (newVP[3][0] - oldVP[3][0]) * halfMap;
    const float dyFloat = (newVP[3][1] - oldVP[3][1]) * halfMap;
    const int32_t dx = static_cast<int32_t>(std::lround(dxFloat));
    const int32_t dy = static_cast<int32_t>(std::lround(dyFloat));
    if (std::abs(dxFloat - static_cast<float>(dx)) > kSunScrollTexelEpsilon ||
        std::abs(dyFloat - static_cast<float>(dy)) > kSunScrollTexelEpsilon) {
        return plan;
    }
    if (dx == 0 && dy == 0) {
        return plan;
    }

    const uint32_t absDx = static_cast<uint32_t>(std::abs(dx));
    const uint32_t absDy = static_cast<uint32_t>(std::abs(dy));
    if (absDx >= mapSize || absDy >= mapSize) {
        return plan;
    }

    const uint64_t fullTexels =
        static_cast<uint64_t>(mapSize) * static_cast<uint64_t>(mapSize);
    const uint64_t copiedTexels =
        static_cast<uint64_t>(mapSize - absDx) *
        static_cast<uint64_t>(mapSize - absDy);
    const uint64_t dirtyTexels = fullTexels - copiedTexels;
    const float dirtyFraction =
        static_cast<float>(static_cast<double>(dirtyTexels) /
                           static_cast<double>(fullTexels));
    if (dirtyFraction > kSunScrollMaxDirtyFraction) {
        return plan;
    }

    plan.enabled = true;
    plan.dxTexels = dx;
    plan.dyTexels = dy;
    plan.copiedTexels = copiedTexels;
    plan.dirtyTexels = dirtyTexels;
    return plan;
}

std::array<VkRect2D, 2> sunScrollDirtyRects(
    uint32_t mapSize,
    const SunCascadeScrollPlan& plan,
    uint32_t& rectCountOut) {
    std::array<VkRect2D, 2> rects{};
    rectCountOut = 0u;
    if (!plan.enabled || mapSize == 0u) {
        return rects;
    }

    const int32_t map = static_cast<int32_t>(mapSize);
    const int32_t dx = plan.dxTexels;
    const int32_t dy = plan.dyTexels;
    if (dx != 0) {
        VkRect2D& r = rects[rectCountOut++];
        r.offset.x = (dx > 0) ? 0 : (map + dx);
        r.offset.y = 0;
        r.extent.width = static_cast<uint32_t>(std::abs(dx));
        r.extent.height = mapSize;
    }
    if (dy != 0) {
        VkRect2D& r = rects[rectCountOut++];
        r.offset.x = (dx > 0) ? std::abs(dx) : 0;
        r.offset.y = (dy > 0) ? 0 : (map + dy);
        r.extent.width = mapSize - static_cast<uint32_t>(std::abs(dx));
        r.extent.height = static_cast<uint32_t>(std::abs(dy));
    }
    return rects;
}

SunGatherBounds computeSunGatherBounds(
    const glm::vec3& cameraPos,
    const glm::vec3& sunDir,
    const std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& cascadeHalfExtents,
    uint32_t cascadeCount) {
    SunGatherBounds b{};
    for (uint32_t c = 0; c < cascadeCount; ++c) {
        b.maxHalfExtent = std::max(b.maxHalfExtent, cascadeHalfExtents[c]);
    }
    b.sinElevation = std::max(-sunDir.y, 0.05f);
    const float invSin = 1.0f / b.sinElevation;
    b.shearX = std::abs(sunDir.x) * invSin;
    b.shearZ = std::abs(sunDir.z) * invSin;
    b.shearMax = std::max(b.shearX, b.shearZ);
    b.casterReach = std::min(
        WorldConfig::CHUNK_HEIGHT_M * b.shearMax,
        std::max(b.maxHalfExtent * 2.0f, 768.0f));
    b.padding = WorldConfig::CHUNK_SIZE_M * 2.0f;
    b.halfX = b.maxHalfExtent + b.casterReach + b.padding;
    b.halfZ = b.maxHalfExtent + b.casterReach + b.padding;

    const glm::vec3 minWorld(cameraPos.x - b.halfX, -10000.0f, cameraPos.z - b.halfZ);
    const glm::vec3 maxWorld(cameraPos.x + b.halfX,  10000.0f, cameraPos.z + b.halfZ);
    b.minChunk = WorldConfig::microVoxelToChunk(WorldConfig::worldToMicroVoxel(minWorld));
    b.maxChunk = WorldConfig::microVoxelToChunk(WorldConfig::worldToMicroVoxel(maxWorld));
    return b;
}

uint16_t sunCascadeMaskForChunkCoord(
    const glm::vec4& chunkCoord,
    const std::array<glm::mat4, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& cascadeVPs,
    uint32_t cascadeCount,
    uint32_t* outInnerRejected) {
    constexpr float kHalfChunkX = WorldConfig::CHUNK_SIZE_M * 0.5f;
    constexpr float kHalfChunkY = WorldConfig::CHUNK_HEIGHT_M * 0.5f;
    constexpr float kHalfChunkZ = WorldConfig::CHUNK_SIZE_M * 0.5f;
    constexpr float kCascadeBlendFrac = 0.12f;
    constexpr float kInnerCascadeScale = 1.0f - kCascadeBlendFrac;

    const glm::vec3 centerWorld(
        (chunkCoord.x + 0.5f) * WorldConfig::CHUNK_SIZE_M,
        (chunkCoord.y + 0.5f) * WorldConfig::CHUNK_HEIGHT_M,
        (chunkCoord.z + 0.5f) * WorldConfig::CHUNK_SIZE_M);

    std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES> clipCx{};
    std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES> clipCy{};
    std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES> clipCz{};
    std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES> clipHx{};
    std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES> clipHy{};
    std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES> clipHz{};
    const uint32_t count = std::min<uint32_t>(cascadeCount, ShadowSystem::MAX_SUN_SHADOW_CASCADES);
    for (uint32_t c = 0; c < count; ++c) {
        const glm::mat4& vp = cascadeVPs[c];
        clipCx[c] =
            vp[0][0] * centerWorld.x + vp[1][0] * centerWorld.y +
            vp[2][0] * centerWorld.z + vp[3][0];
        clipCy[c] =
            vp[0][1] * centerWorld.x + vp[1][1] * centerWorld.y +
            vp[2][1] * centerWorld.z + vp[3][1];
        clipCz[c] =
            vp[0][2] * centerWorld.x + vp[1][2] * centerWorld.y +
            vp[2][2] * centerWorld.z + vp[3][2];
        clipHx[c] =
            std::abs(vp[0][0]) * kHalfChunkX +
            std::abs(vp[1][0]) * kHalfChunkY +
            std::abs(vp[2][0]) * kHalfChunkZ;
        clipHy[c] =
            std::abs(vp[0][1]) * kHalfChunkX +
            std::abs(vp[1][1]) * kHalfChunkY +
            std::abs(vp[2][1]) * kHalfChunkZ;
        clipHz[c] =
            std::abs(vp[0][2]) * kHalfChunkX +
            std::abs(vp[1][2]) * kHalfChunkY +
            std::abs(vp[2][2]) * kHalfChunkZ;
    }

    uint16_t mask = 0u;
    uint32_t innerRejected = 0u;
    for (uint32_t c = 0; c < count; ++c) {
        if ((clipCx[c] + clipHx[c]) < -1.0f || (clipCx[c] - clipHx[c]) > 1.0f) continue;
        if ((clipCy[c] + clipHy[c]) < -1.0f || (clipCy[c] - clipHy[c]) > 1.0f) continue;
        if ((clipCz[c] + clipHz[c]) <  0.0f || (clipCz[c] - clipHz[c]) > 1.0f) continue;

        if (c > 0u) {
            const uint32_t inner = c - 1u;
            const bool fullyInsideInnerReceiverRegion =
                (clipCx[inner] - clipHx[inner]) >= -kInnerCascadeScale &&
                (clipCx[inner] + clipHx[inner]) <=  kInnerCascadeScale &&
                (clipCy[inner] - clipHy[inner]) >= -kInnerCascadeScale &&
                (clipCy[inner] + clipHy[inner]) <=  kInnerCascadeScale &&
                (clipCz[inner] - clipHz[inner]) >=  0.0f &&
                (clipCz[inner] + clipHz[inner]) <=  1.0f;
            if (fullyInsideInnerReceiverRegion) {
                ++innerRejected;
                continue;
            }
        }
        mask |= static_cast<uint16_t>(1u << c);
    }
    if (outInnerRejected) {
        *outInnerRejected += innerRejected;
    }
    return mask;
}

uint32_t allCascadeBits(uint32_t cascadeCount) {
    const uint32_t count =
        std::min<uint32_t>(cascadeCount, ShadowSystem::MAX_SUN_SHADOW_CASCADES);
    return (count >= 32u) ? 0xFFFFFFFFu : ((1u << count) - 1u);
}

uint32_t meshTopologyChangeCascadeMask(
    const ShadowSystem::DrawContext& ctx,
    uint64_t cachedRevision,
    const std::array<glm::mat4, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& cascadeVPs,
    uint32_t cascadeCount,
    bool* reliableOut) {
    if (reliableOut) *reliableOut = true;
    if (!ctx.world || cachedRevision == ctx.terrainMeshRevision) {
        return 0u;
    }

    std::vector<World::MeshTopologyChange> changes;
    if (!ctx.world->getMeshTopologyChangesSince(cachedRevision, changes)) {
        if (reliableOut) *reliableOut = false;
        return allCascadeBits(cascadeCount);
    }

    uint32_t mask = 0u;
    const uint32_t allMask = allCascadeBits(cascadeCount);
    for (const auto& change : changes) {
        const glm::vec4 chunkCoord(
            static_cast<float>(change.coord.x),
            static_cast<float>(change.coord.y),
            static_cast<float>(change.coord.z),
            0.0f);
        mask |= sunCascadeMaskForChunkCoord(chunkCoord, cascadeVPs, cascadeCount);
        if ((mask & allMask) == allMask) {
            break;
        }
    }
    return mask & allMask;
}

} // namespace ShadowSystemInternal

using namespace ShadowSystemInternal;

void ShadowSystem::recordSunShadowPasses(VkCommandBuffer cmd,
                                        uint32_t imageIndex,
                                        const DrawContext& ctx) {
    // ═══════════════════════════════════════════════════════════════════
    // Sun (directional) shadow pass
    // ═══════════════════════════════════════════════════════════════════

    using Clock = std::chrono::high_resolution_clock;

    const bool haveSunTimingQueries =
        (m_sunTimingQueryPool != VK_NULL_HANDLE) &&
        (imageIndex < m_sunTimingImageCount);

    if (haveSunTimingQueries) {
        vkCmdResetQueryPool(cmd, m_sunTimingQueryPool, imageIndex * 2u, 2u);
        m_sunTimingWritten[imageIndex] = false;
    }

    const uint32_t sunCascadeCount = std::min<uint32_t>(
        m_activeSunCascadeCount,
        std::min<uint32_t>(
            m_sunRuntimeConfig.cascadeCount,
            static_cast<uint32_t>(m_sunShadowFramebuffers.size())));
    std::array<uint64_t, MAX_SUN_SHADOW_CASCADES> sunCascadeVpHashes{};
    for (uint32_t c = 0; c < sunCascadeCount && c < MAX_SUN_SHADOW_CASCADES; ++c) {
        sunCascadeVpHashes[c] = hashSunCascadeLayerVP(m_sunCascadeVP[c]);
    }
    const uint64_t sunVpHash = hashSunCascadeVP(m_sunCascadeVP, sunCascadeCount);
    m_sunCurrentFrame.activeCascadeCount = sunCascadeCount;
    m_sunCurrentFrame.configuredCascadeCount = m_sunRuntimeConfig.cascadeCount;
    m_sunCurrentFrame.mapSize = m_sunRuntimeConfig.mapSize;
    m_sunCurrentFrame.vpHash = sunVpHash;
    m_sunCurrentFrame.uploadTimeline = ctx.uploadTimelineValue;
    m_sunCurrentFrame.terrainMeshRevision = ctx.terrainMeshRevision;

    if (m_sunShadowActive && sunCascadeCount > 0u) {
        const auto sunTerrainGatherStart = Clock::now();

        const uint64_t terrainSigForSun = hashTerrainDrawContext(ctx);
        const bool useLocalForSun = (ctx.world != nullptr);
        m_sunCurrentFrame.ctxHash = terrainSigForSun;
        m_sunCurrentFrame.usedLocalTerrainGather = useLocalForSun;

        uint32_t sunLocalDrawCount = 0u;
        uint64_t sunTerrainSig = terrainSigForSun;
        std::array<uint64_t, MAX_SUN_SHADOW_CASCADES> sunCascadeTerrainSigs{};

        // ── Fast cache pre-check: skip terrain gather entirely ──────────
        // The local gather walks ~700 chunks (~0.3 ms) every frame. If
        // (ctx hash, sunDir, snapped VP origin) are all bit-identical to
        // the previous cached state, the gather is guaranteed to produce
        // the identical draw set so we can reuse the cached terrainSig
        // without touching the world. Mesh-topology changes are checked
        // locally against the cascade volumes below, so swaps outside the
        // sun shadow footprint do not poison the cache.
        bool sunPrechecked = false;
        {
            const auto& cache = m_sunShadowCache;
            uint32_t precheckMissMask = 0u;
            bool precheckTimelineSafe = true;
            bool precheckMeshSafe = true;
            if (!cache.valid) {
                precheckMissMask |= SUN_CACHE_MISS_INVALID;
            } else {
                if (cache.terrainMeshRevision != ctx.terrainMeshRevision) {
                    bool meshChangesReliable = true;
                    const uint32_t meshChangeMask = meshTopologyChangeCascadeMask(
                        ctx,
                        cache.terrainMeshRevision,
                        m_sunCascadeVP,
                        sunCascadeCount,
                        &meshChangesReliable);
                    m_sunCurrentFrame.renderCachePrecheckMeshChangeMask = meshChangeMask;
                    m_sunCurrentFrame.renderCachePrecheckMeshChangesReliable = meshChangesReliable;
                    if (!meshChangesReliable || meshChangeMask != 0u) {
                        precheckMissMask |= SUN_CACHE_MISS_MESH_REVISION;
                        precheckMeshSafe = false;
                    }
                }
                if (cache.uploadPendingRejects > 0u &&
                    cache.uploadTimeline != ctx.uploadTimelineValue) {
                    precheckMissMask |= SUN_CACHE_MISS_UPLOAD_TIMELINE;
                    precheckTimelineSafe = false;
                }
                if (cache.ctxHash != terrainSigForSun)
                    precheckMissMask |= SUN_CACHE_MISS_CONTEXT;
                if (!sameBitsVec3(cache.renderedSunDir, m_sunDir))
                    precheckMissMask |= SUN_CACHE_MISS_SUN_DIR;
                if (cache.cascadeCount != sunCascadeCount)
                    precheckMissMask |= SUN_CACHE_MISS_CASCADE_COUNT;
                if (cache.vpHash != sunVpHash)
                    precheckMissMask |= SUN_CACHE_MISS_VP_HASH;
            }
            m_sunCurrentFrame.renderCachePrecheckMissMask = precheckMissMask;
            if (cache.valid &&
                precheckTimelineSafe &&
                precheckMeshSafe &&
                cache.ctxHash == terrainSigForSun &&
                sameBitsVec3(cache.renderedSunDir, m_sunDir) &&
                cache.cascadeCount == sunCascadeCount &&
                cache.vpHash == sunVpHash) {
                sunTerrainSig = cache.terrainSignature;
                sunCascadeTerrainSigs = cache.cascadeTerrainSignatures;
                sunLocalDrawCount = cache.terrainDrawCount;
                sunPrechecked = true;
                m_sunCurrentFrame.loadedChunkMapSize = cache.loadedChunkMapSize;
                m_sunCurrentFrame.bboxCandidateChunks = cache.bboxCandidateChunks;
                m_sunCurrentFrame.visitedCandidateChunks = cache.visitedCandidateChunks;
                m_sunCurrentFrame.acceptedChunkCount = cache.acceptedChunks;
                m_sunCurrentFrame.cascadeInnerCullRejects = cache.cascadeInnerCullRejects;
                m_sunCurrentFrame.uploadPendingRejects = cache.uploadPendingRejects;
                m_sunCurrentFrame.gatherSinElevation = cache.sinElevation;
                m_sunCurrentFrame.gatherShearX = cache.shearX;
                m_sunCurrentFrame.gatherShearZ = cache.shearZ;
                m_sunCurrentFrame.gatherShearMax = cache.shearMax;
                m_sunCurrentFrame.gatherCasterReach = cache.casterReach;
                m_sunCurrentFrame.gatherPadding = cache.padding;
                m_sunCurrentFrame.gatherHalfX = cache.halfX;
                m_sunCurrentFrame.gatherHalfZ = cache.halfZ;
                m_sunCurrentFrame.cameraChunk = cache.cameraChunk;
                m_sunCurrentFrame.gatherMinChunk = cache.gatherMinChunk;
                m_sunCurrentFrame.gatherMaxChunk = cache.gatherMaxChunk;
                m_sunCurrentFrame.cascadeGatherChunkHits = cache.cascadeChunkHits;
                m_sunCurrentFrame.cascadeGatherDrawHits = cache.cascadeDrawHits;
            }
        }
        m_sunCurrentFrame.renderCachePrecheckHit = sunPrechecked;

        // ── Gather-cache pre-check (independent of render cache) ─────
        // The gather output is invariant to sub-chunk camera motion.
        // Cache it keyed on snapped camera + sun bits + ctx + cascade
        // VPs so steady-state movement reuses the cached gather instead
        // of re-walking thousands of chunks every frame.
        const float chunkSizeXZ = WorldConfig::CHUNK_SIZE_M;
        const float chunkSizeY  = WorldConfig::CHUNK_HEIGHT_M;
        const glm::ivec3 sunCameraChunk(
            static_cast<int>(std::floor(m_sunCameraPos.x / chunkSizeXZ)),
            static_cast<int>(std::floor(m_sunCameraPos.y / chunkSizeY)),
            static_cast<int>(std::floor(m_sunCameraPos.z / chunkSizeXZ)));
        m_sunCurrentFrame.cameraChunk = sunCameraChunk;
        const SunGatherBounds currentGatherBounds = computeSunGatherBounds(
            m_sunCameraPos, m_sunDir, m_sunCascadeHalfExtents, sunCascadeCount);

        bool gatherCacheHit = false;
        if (!sunPrechecked && useLocalForSun) {
            const auto& gc = m_sunGatherCache;
            uint32_t gatherMissMask = 0u;
            bool extentsMatch = true;
            bool boundsMatch = false;
            bool gatherTimelineSafe = true;
            bool gatherMeshSafe = true;
            bool gatherCanAdvanceMeshRevision = true;
            if (!gc.valid) {
                gatherMissMask |= SUN_CACHE_MISS_INVALID;
                extentsMatch = false;
            } else {
                boundsMatch =
                    gc.gatherMinChunk.x <= currentGatherBounds.minChunk.x &&
                    gc.gatherMinChunk.y <= currentGatherBounds.minChunk.y &&
                    gc.gatherMinChunk.z <= currentGatherBounds.minChunk.z &&
                    gc.gatherMaxChunk.x >= currentGatherBounds.maxChunk.x &&
                    gc.gatherMaxChunk.y >= currentGatherBounds.maxChunk.y &&
                    gc.gatherMaxChunk.z >= currentGatherBounds.maxChunk.z;
                if (!boundsMatch)
                    gatherMissMask |= SUN_CACHE_MISS_CAMERA_CHUNK;
                if (!sameBitsVec3(gc.sunDir, m_sunDir))
                    gatherMissMask |= SUN_CACHE_MISS_SUN_DIR;
                if (gc.terrainMeshRevision != ctx.terrainMeshRevision) {
                    bool meshChangesReliable = true;
                    std::vector<World::MeshTopologyChange> meshChanges;
                    if (!ctx.world ||
                        !ctx.world->getMeshTopologyChangesSince(
                            gc.terrainMeshRevision, meshChanges)) {
                        meshChangesReliable = false;
                    }
                    uint32_t meshChangeMask = 0u;
                    std::vector<glm::ivec3> changedCoordsInCache;
                    if (meshChangesReliable) {
                        for (const auto& change : meshChanges) {
                            const glm::vec4 chunkCoord(
                                static_cast<float>(change.coord.x),
                                static_cast<float>(change.coord.y),
                                static_cast<float>(change.coord.z),
                                0.0f);
                            meshChangeMask |= sunCascadeMaskForChunkCoord(
                                chunkCoord, m_sunCascadeVP, sunCascadeCount);
                            const glm::ivec3& c = change.coord;
                            if (c.x >= gc.gatherMinChunk.x && c.x <= gc.gatherMaxChunk.x &&
                                c.y >= gc.gatherMinChunk.y && c.y <= gc.gatherMaxChunk.y &&
                                c.z >= gc.gatherMinChunk.z && c.z <= gc.gatherMaxChunk.z) {
                                changedCoordsInCache.push_back(c);
                            }
                        }
                    }
                    m_sunCurrentFrame.gatherCacheMeshChangeMask = meshChangeMask;
                    m_sunCurrentFrame.gatherCacheMeshChangesReliable = meshChangesReliable;
                    bool incrementalMeshUpdateOk = meshChangesReliable;
                    if (incrementalMeshUpdateOk && !changedCoordsInCache.empty()) {
                        auto coordLess = [](const glm::ivec3& a, const glm::ivec3& b) {
                            if (a.x != b.x) return a.x < b.x;
                            if (a.y != b.y) return a.y < b.y;
                            return a.z < b.z;
                        };
                        auto coordEqual = [](const glm::ivec3& a, const glm::ivec3& b) {
                            return a.x == b.x && a.y == b.y && a.z == b.z;
                        };
                        std::sort(changedCoordsInCache.begin(), changedCoordsInCache.end(), coordLess);
                        changedCoordsInCache.erase(
                            std::unique(changedCoordsInCache.begin(), changedCoordsInCache.end(), coordEqual),
                            changedCoordsInCache.end());

                        const uint32_t capacity = static_cast<uint32_t>(std::min<size_t>({
                            m_sunLocalTerrainDrawScratch.size(),
                            m_sunLocalTerrainOriginScratch.size(),
                            m_sunLocalTerrainCascadeMaskScratch.size(),
                            static_cast<size_t>(std::numeric_limits<uint32_t>::max())}));
                        uint32_t writeIndex = 0u;
                        const uint32_t cachedDrawCount = std::min(gc.drawCount, capacity);
                        auto originIsChanged = [&](const glm::vec4& origin) {
                            const glm::ivec3 coord(
                                static_cast<int>(origin.x),
                                static_cast<int>(origin.y),
                                static_cast<int>(origin.z));
                            return std::binary_search(
                                changedCoordsInCache.begin(),
                                changedCoordsInCache.end(),
                                coord,
                                coordLess);
                        };
                        for (uint32_t readIndex = 0; readIndex < cachedDrawCount; ++readIndex) {
                            if (originIsChanged(m_sunLocalTerrainOriginScratch[readIndex])) {
                                continue;
                            }
                            if (writeIndex != readIndex) {
                                m_sunLocalTerrainDrawScratch[writeIndex] =
                                    m_sunLocalTerrainDrawScratch[readIndex];
                                m_sunLocalTerrainOriginScratch[writeIndex] =
                                    m_sunLocalTerrainOriginScratch[readIndex];
                                m_sunLocalTerrainCascadeMaskScratch[writeIndex] =
                                    m_sunLocalTerrainCascadeMaskScratch[readIndex];
                            }
                            ++writeIndex;
                        }

                        for (const glm::ivec3& coord : changedCoordsInCache) {
                            if (writeIndex >= capacity) {
                                incrementalMeshUpdateOk = false;
                                break;
                            }
                            const uint32_t appended =
                                ctx.world->gatherDrawCommandsForSunCascadeChunk(
                                    coord,
                                    m_sunCascadeVP.data(),
                                    sunCascadeCount,
                                    m_sunLocalTerrainDrawScratch.data() + writeIndex,
                                    m_sunLocalTerrainOriginScratch.data() + writeIndex,
                                    m_sunLocalTerrainCascadeMaskScratch.data() + writeIndex,
                                    capacity - writeIndex,
                                    ctx.uploadTimelineValue,
                                    true);
                            writeIndex += appended;
                        }
                        if (incrementalMeshUpdateOk) {
                            m_sunGatherCache.drawCount = writeIndex;
                        }
                    }
                    if (incrementalMeshUpdateOk) {
                        m_sunGatherCache.terrainMeshRevision = ctx.terrainMeshRevision;
                    } else {
                        gatherMissMask |= SUN_CACHE_MISS_MESH_REVISION;
                        gatherMeshSafe = false;
                    }
                }
                if (gc.uploadPendingRejects > 0u &&
                    gc.uploadTimeline != ctx.uploadTimelineValue) {
                    gatherMissMask |= SUN_CACHE_MISS_UPLOAD_TIMELINE;
                    gatherTimelineSafe = false;
                }
                if (gc.ctxHash != terrainSigForSun)
                    gatherMissMask |= SUN_CACHE_MISS_CONTEXT;
                if (gc.cascadeCount != sunCascadeCount)
                    gatherMissMask |= SUN_CACHE_MISS_CASCADE_COUNT;
                for (uint32_t c = 0; c < sunCascadeCount; ++c) {
                    if (floatToBits(gc.cascadeHalfExtents[c]) !=
                        floatToBits(m_sunCascadeHalfExtents[c])) {
                        extentsMatch = false;
                        break;
                    }
                }
                if (!extentsMatch)
                    gatherMissMask |= SUN_CACHE_MISS_CASCADE_EXTENTS;
            }
            m_sunCurrentFrame.gatherCacheMissMask = gatherMissMask;
            if (gc.valid &&
                boundsMatch &&
                gatherTimelineSafe &&
                gatherMeshSafe &&
                sameBitsVec3(gc.sunDir, m_sunDir) &&
                gc.ctxHash == terrainSigForSun &&
                gc.cascadeCount == sunCascadeCount) {
                if (extentsMatch) {
                    const auto remaskStart = Clock::now();
                    sunLocalDrawCount = gc.drawCount;
                    sunTerrainSig = terrainSigForSun;
                    gatherCacheHit = true;
                    m_sunCurrentFrame.loadedChunkMapSize = gc.loadedChunkMapSize;
                    m_sunCurrentFrame.bboxCandidateChunks = gc.bboxCandidateChunks;
                    m_sunCurrentFrame.visitedCandidateChunks = gc.visitedCandidateChunks;
                    m_sunCurrentFrame.acceptedChunkCount = 0u;
                    m_sunCurrentFrame.cascadeCullRejects = 0u;
                    m_sunCurrentFrame.cascadeInnerCullRejects = 0u;
                    m_sunCurrentFrame.uploadPendingRejects = gc.uploadPendingRejects;
                    m_sunCurrentFrame.gatherSinElevation = gc.sinElevation;
                    m_sunCurrentFrame.gatherShearX = gc.shearX;
                    m_sunCurrentFrame.gatherShearZ = gc.shearZ;
                    m_sunCurrentFrame.gatherShearMax = gc.shearMax;
                    m_sunCurrentFrame.gatherCasterReach = gc.casterReach;
                    m_sunCurrentFrame.gatherPadding = gc.padding;
                    m_sunCurrentFrame.gatherHalfX = gc.halfX;
                    m_sunCurrentFrame.gatherHalfZ = gc.halfZ;
                    m_sunCurrentFrame.gatherMinChunk = currentGatherBounds.minChunk;
                    m_sunCurrentFrame.gatherMaxChunk = currentGatherBounds.maxChunk;
                    m_sunCurrentFrame.cascadeGatherChunkHits.fill(0u);
                    m_sunCurrentFrame.cascadeGatherDrawHits.fill(0u);
                    uint32_t remaskedInnerRejects = 0u;
                    glm::vec4 prevOrigin(0.0f);
                    bool havePrevOrigin = false;
                    uint16_t currentChunkMask = 0u;
                    auto flushChunkMask = [&]() {
                        if (!havePrevOrigin) {
                            return;
                        }
                        if (currentChunkMask == 0u) {
                            ++m_sunCurrentFrame.cascadeCullRejects;
                            return;
                        }
                        ++m_sunCurrentFrame.acceptedChunkCount;
                        for (uint32_t c = 0; c < sunCascadeCount && c < MAX_SUN_SHADOW_CASCADES; ++c) {
                            if ((currentChunkMask & static_cast<uint16_t>(1u << c)) != 0u) {
                                ++m_sunCurrentFrame.cascadeGatherChunkHits[c];
                            }
                        }
                    };
                    for (uint32_t drawIndex = 0; drawIndex < sunLocalDrawCount; ++drawIndex) {
                        const glm::vec4& origin = m_sunLocalTerrainOriginScratch[drawIndex];
                        const uint16_t mask = sunCascadeMaskForChunkCoord(
                            origin,
                            m_sunCascadeVP,
                            sunCascadeCount,
                            &remaskedInnerRejects);
                        m_sunLocalTerrainCascadeMaskScratch[drawIndex] = mask;
                        const bool newChunk =
                            !havePrevOrigin ||
                            origin.x != prevOrigin.x ||
                            origin.y != prevOrigin.y ||
                            origin.z != prevOrigin.z;
                        if (newChunk) {
                            flushChunkMask();
                            currentChunkMask = 0u;
                        }
                        currentChunkMask |= mask;
                        for (uint32_t c = 0; c < sunCascadeCount && c < MAX_SUN_SHADOW_CASCADES; ++c) {
                            if ((mask & static_cast<uint16_t>(1u << c)) != 0u) {
                                ++m_sunCurrentFrame.cascadeGatherDrawHits[c];
                            }
                        }
                        prevOrigin = origin;
                        havePrevOrigin = true;
                    }
                    flushChunkMask();
                    m_sunCurrentFrame.cascadeInnerCullRejects = remaskedInnerRejects;
                    m_sunCurrentFrame.cpuWorldGatherMs = std::chrono::duration<float, std::milli>(
                        Clock::now() - remaskStart).count();
                    if (gatherCanAdvanceMeshRevision) {
                        m_sunGatherCache.terrainMeshRevision = ctx.terrainMeshRevision;
                    }
                }
            }
        }
        m_sunCurrentFrame.gatherCacheHit = gatherCacheHit;

        const uint32_t sunLocalCap = (useLocalForSun && !sunPrechecked && !gatherCacheHit)
            ? std::max<uint32_t>(ctx.gpuMaxDraws, std::max<uint32_t>(ctx.indirectDrawCount, 65536u))
            : 0u;
        m_sunCurrentFrame.localDrawCapacity = gatherCacheHit
            ? static_cast<uint32_t>(std::min<size_t>(
                m_sunLocalTerrainDrawScratch.size(),
                std::numeric_limits<uint32_t>::max()))
            : sunLocalCap;
        if (useLocalForSun && sunLocalCap > 0u) {
            if (m_sunLocalTerrainDrawScratch.size() < sunLocalCap) {
                m_sunLocalTerrainDrawScratch.resize(sunLocalCap);
                m_sunLocalTerrainOriginScratch.resize(sunLocalCap);
                m_sunLocalTerrainCascadeMaskScratch.resize(sunLocalCap);
            } else if (m_sunLocalTerrainCascadeMaskScratch.size() < sunLocalCap) {
                m_sunLocalTerrainCascadeMaskScratch.resize(sunLocalCap);
            }
            World::SunCascadeGatherDiagnostics gatherDiag{};
            sunLocalDrawCount = ctx.world->gatherDrawCommandsForSunCascades(
                m_sunCameraPos,
                m_sunDir,
                m_sunCascadeVP.data(),
                m_sunCascadeHalfExtents.data(),
                sunCascadeCount,
                m_sunLocalTerrainDrawScratch.data(),
                m_sunLocalTerrainOriginScratch.data(),
                m_sunLocalTerrainCascadeMaskScratch.data(),
                sunLocalCap,
                ctx.uploadTimelineValue,
                &gatherDiag,
                kSunGatherCachePaddingChunks,
                true);
            m_sunCurrentFrame.cpuWorldGatherMs = gatherDiag.totalMs;
            m_sunCurrentFrame.cpuGatherStateScanMs = gatherDiag.stateMapScanMs;
            m_sunCurrentFrame.cpuGatherSortMs = gatherDiag.candidateSortMs;
            m_sunCurrentFrame.cpuGatherRegistryWalkMs = gatherDiag.registryWalkMs;
            m_sunCurrentFrame.loadedChunkMapSize = gatherDiag.loadedChunkMapSize;
            m_sunCurrentFrame.bboxCandidateChunks = gatherDiag.bboxCandidateChunks;
            m_sunCurrentFrame.visitedCandidateChunks = gatherDiag.visitedCandidateChunks;
            m_sunCurrentFrame.acceptedChunkCount = gatherDiag.acceptedChunks;
            m_sunCurrentFrame.invalidEntityRejects = gatherDiag.invalidEntityRejects;
            m_sunCurrentFrame.missingComponentRejects = gatherDiag.missingComponentRejects;
            m_sunCurrentFrame.invisibleRejects = gatherDiag.invisibleRejects;
            m_sunCurrentFrame.notReadyRejects = gatherDiag.notReadyRejects;
            m_sunCurrentFrame.invalidMeshRejects = gatherDiag.invalidMeshRejects;
            m_sunCurrentFrame.emptyMeshRejects = gatherDiag.emptyMeshRejects;
            m_sunCurrentFrame.uploadPendingRejects = gatherDiag.uploadPendingRejects;
            m_sunCurrentFrame.cascadeCullRejects = gatherDiag.cascadeCullRejects;
            m_sunCurrentFrame.cascadeInnerCullRejects = gatherDiag.cascadeInnerCullRejects;
            m_sunCurrentFrame.gatherTruncated = gatherDiag.truncated;
            m_sunCurrentFrame.gatherMaxHalfExtent = gatherDiag.maxHalfExtent;
            m_sunCurrentFrame.gatherSinElevation = gatherDiag.sinElevation;
            m_sunCurrentFrame.gatherShearX = gatherDiag.shearX;
            m_sunCurrentFrame.gatherShearZ = gatherDiag.shearZ;
            m_sunCurrentFrame.gatherShearMax = gatherDiag.shearMax;
            m_sunCurrentFrame.gatherCasterReach = gatherDiag.casterReach;
            m_sunCurrentFrame.gatherPadding = gatherDiag.padding;
            m_sunCurrentFrame.gatherHalfX = gatherDiag.halfX;
            m_sunCurrentFrame.gatherHalfZ = gatherDiag.halfZ;
            m_sunCurrentFrame.gatherMinChunk = gatherDiag.minChunk;
            m_sunCurrentFrame.gatherMaxChunk = gatherDiag.maxChunk;
            for (uint32_t c = 0; c < sunCascadeCount && c < MAX_SUN_SHADOW_CASCADES; ++c) {
                m_sunCurrentFrame.cascadeGatherChunkHits[c] = gatherDiag.cascadeChunkHits[c];
                m_sunCurrentFrame.cascadeGatherDrawHits[c] = gatherDiag.cascadeDrawHits[c];
            }
        }

        m_sunCurrentFrame.terrainChunksGathered = sunLocalDrawCount;
        m_sunCurrentFrame.terrainDrawsGathered = sunLocalDrawCount;

        // Build the terrain signature for cache comparison (only when we
        // actually re-gathered — pre-check / gather-cache paths already
        // populated it).
        if (!sunPrechecked && !gatherCacheHit && useLocalForSun && sunLocalDrawCount > 0u) {
            const auto hashStart = Clock::now();
            const uint64_t localHash = hashLocalTerrainDraws(
                m_sunLocalTerrainDrawScratch, m_sunLocalTerrainOriginScratch, sunLocalDrawCount);
            hashCombine64(sunTerrainSig, localHash);
            sunCascadeTerrainSigs = hashLocalTerrainDrawsPerCascade(
                m_sunLocalTerrainDrawScratch,
                m_sunLocalTerrainOriginScratch,
                m_sunLocalTerrainCascadeMaskScratch,
                sunLocalDrawCount,
                sunCascadeCount);
            m_sunCurrentFrame.cpuTerrainHashMs = std::chrono::duration<float, std::milli>(
                Clock::now() - hashStart).count();
        } else if (!sunPrechecked && gatherCacheHit && useLocalForSun && sunLocalDrawCount > 0u) {
            const auto hashStart = Clock::now();
            const uint64_t localHash = hashLocalTerrainDraws(
                m_sunLocalTerrainDrawScratch, m_sunLocalTerrainOriginScratch, sunLocalDrawCount);
            hashCombine64(sunTerrainSig, localHash);
            sunCascadeTerrainSigs = hashLocalTerrainDrawsPerCascade(
                m_sunLocalTerrainDrawScratch,
                m_sunLocalTerrainOriginScratch,
                m_sunLocalTerrainCascadeMaskScratch,
                sunLocalDrawCount,
                sunCascadeCount);
            m_sunGatherCache.terrainSig = sunTerrainSig;
            m_sunCurrentFrame.cpuTerrainHashMs = std::chrono::duration<float, std::milli>(
                Clock::now() - hashStart).count();
        } else if (!sunPrechecked && gatherCacheHit && useLocalForSun) {
            m_sunGatherCache.terrainSig = sunTerrainSig;
        }
        m_sunCurrentFrame.terrainSignature = sunTerrainSig;

        // ── Persist gather cache on miss ─────────────────────────────
        if (!sunPrechecked && !gatherCacheHit && useLocalForSun) {
            m_sunGatherCache.valid = true;
            m_sunGatherCache.cameraChunk = sunCameraChunk;
            m_sunGatherCache.sunDir = m_sunDir;
            m_sunGatherCache.ctxHash = terrainSigForSun;
            m_sunGatherCache.terrainMeshRevision = ctx.terrainMeshRevision;
            m_sunGatherCache.uploadTimeline = ctx.uploadTimelineValue;
            m_sunGatherCache.uploadPendingRejects = m_sunCurrentFrame.uploadPendingRejects;
            m_sunGatherCache.vpHash = sunVpHash;
            m_sunGatherCache.cascadeCount = sunCascadeCount;
            for (uint32_t c = 0; c < MAX_SUN_SHADOW_CASCADES; ++c) {
                m_sunGatherCache.cascadeHalfExtents[c] =
                    (c < sunCascadeCount) ? m_sunCascadeHalfExtents[c] : 0.0f;
            }
            m_sunGatherCache.drawCount = sunLocalDrawCount;
            m_sunGatherCache.terrainSig = sunTerrainSig;
            m_sunGatherCache.loadedChunkMapSize = m_sunCurrentFrame.loadedChunkMapSize;
            m_sunGatherCache.bboxCandidateChunks = m_sunCurrentFrame.bboxCandidateChunks;
            m_sunGatherCache.visitedCandidateChunks = m_sunCurrentFrame.visitedCandidateChunks;
            m_sunGatherCache.acceptedChunks = m_sunCurrentFrame.acceptedChunkCount;
            m_sunGatherCache.cascadeInnerCullRejects = m_sunCurrentFrame.cascadeInnerCullRejects;
            m_sunGatherCache.sinElevation = m_sunCurrentFrame.gatherSinElevation;
            m_sunGatherCache.shearX = m_sunCurrentFrame.gatherShearX;
            m_sunGatherCache.shearZ = m_sunCurrentFrame.gatherShearZ;
            m_sunGatherCache.shearMax = m_sunCurrentFrame.gatherShearMax;
            m_sunGatherCache.casterReach = m_sunCurrentFrame.gatherCasterReach;
            m_sunGatherCache.padding = m_sunCurrentFrame.gatherPadding;
            m_sunGatherCache.halfX = m_sunCurrentFrame.gatherHalfX;
            m_sunGatherCache.halfZ = m_sunCurrentFrame.gatherHalfZ;
            m_sunGatherCache.gatherMinChunk = m_sunCurrentFrame.gatherMinChunk;
            m_sunGatherCache.gatherMaxChunk = m_sunCurrentFrame.gatherMaxChunk;
            m_sunGatherCache.cascadeChunkHits = m_sunCurrentFrame.cascadeGatherChunkHits;
            m_sunGatherCache.cascadeDrawHits = m_sunCurrentFrame.cascadeGatherDrawHits;
        }

        // Check cache per cascade. A changed near-cascade VP no longer
        // forces far layers to redraw if their own VP and terrain content
        // are unchanged.
        bool sunReused = false;
        std::array<bool, MAX_SUN_SHADOW_CASCADES> sunCascadeNeedsRender{};
        std::array<SunCascadeScrollPlan, MAX_SUN_SHADOW_CASCADES> sunCascadeScrollPlans{};
        uint32_t sunCascadeRenderMask = 0u;
        uint32_t sunCascadeReuseMask = 0u;
        uint32_t sunCascadeFullRenderMask = 0u;
        uint32_t sunCascadeScrollMask = 0u;
        uint32_t sunCascadesRendered = 0u;
        uint32_t sunCascadesReused = 0u;
        uint32_t sunCascadesFullRendered = 0u;
        uint32_t sunCascadesScrolled = 0u;
        uint64_t sunScrollCopiedTexels = 0u;
        uint64_t sunScrollDirtyTexels = 0u;
        {
            const auto cacheDecisionStart = Clock::now();
            const auto& cache = m_sunShadowCache;
            uint32_t renderMissMask = 0u;
            bool renderTimelineSafe = true;
            bool renderMeshChangesReliable = true;
            uint32_t renderMeshChangeMask = 0u;
            if (!cache.valid) {
                renderMissMask |= SUN_CACHE_MISS_INVALID;
            } else {
                if (cache.terrainMeshRevision != ctx.terrainMeshRevision) {
                    renderMeshChangeMask = meshTopologyChangeCascadeMask(
                        ctx,
                        cache.terrainMeshRevision,
                        m_sunCascadeVP,
                        sunCascadeCount,
                        &renderMeshChangesReliable);
                    if (!renderMeshChangesReliable || renderMeshChangeMask != 0u) {
                        renderMissMask |= SUN_CACHE_MISS_MESH_REVISION;
                    }
                }
                if (cache.uploadPendingRejects > 0u &&
                    cache.uploadTimeline != ctx.uploadTimelineValue) {
                    renderMissMask |= SUN_CACHE_MISS_UPLOAD_TIMELINE;
                    renderTimelineSafe = false;
                }
                if (!sameBitsVec3(cache.renderedSunDir, m_sunDir))
                    renderMissMask |= SUN_CACHE_MISS_SUN_DIR;
                if (cache.terrainSignature != sunTerrainSig)
                    renderMissMask |= SUN_CACHE_MISS_TERRAIN_SIG;
                if (cache.cascadeCount != sunCascadeCount)
                    renderMissMask |= SUN_CACHE_MISS_CASCADE_COUNT;
                if (cache.vpHash != sunVpHash)
                    renderMissMask |= SUN_CACHE_MISS_VP_HASH;
            }
            m_sunCurrentFrame.renderCacheMeshChangeMask = renderMeshChangeMask;
            m_sunCurrentFrame.renderCacheMeshChangesReliable = renderMeshChangesReliable;
            m_sunCurrentFrame.renderCacheMissMask = renderMissMask;

            const bool renderBaseReusable =
                cache.valid &&
                renderTimelineSafe &&
                sameBitsVec3(cache.renderedSunDir, m_sunDir) &&
                cache.ctxHash == terrainSigForSun;

            for (uint32_t c = 0; c < sunCascadeCount && c < MAX_SUN_SHADOW_CASCADES; ++c) {
                const uint32_t currentCascadeDraws = m_sunCurrentFrame.cascadeGatherDrawHits[c];
                const bool cascadeMeshReusable =
                    renderMeshChangesReliable &&
                    ((renderMeshChangeMask & (1u << c)) == 0u);
                const bool cascadeTerrainReusable = useLocalForSun
                    ? (cache.cascadeTerrainSignatures[c] == sunCascadeTerrainSigs[c])
                    : (cache.terrainSignature == sunTerrainSig);
                const bool emptyLayerReusable =
                    cache.valid &&
                    cache.cascadeValid[c] &&
                    cache.cascadeDrawCounts[c] == 0u &&
                    currentCascadeDraws == 0u;
                const bool layerReusable =
                    emptyLayerReusable ||
                    (renderBaseReusable &&
                     cascadeMeshReusable &&
                     cascadeTerrainReusable &&
                     cache.cascadeValid[c] &&
                     cache.cascadeVpHashes[c] == sunCascadeVpHashes[c]);
                if (layerReusable) {
                    sunCascadeReuseMask |= (1u << c);
                    ++sunCascadesReused;
                } else {
                    // Scrolling preserves a layer exactly and avoids the full
                    // 4096^2 depth clear. The scroll planner rejects non-
                    // integer shifts and large dirty areas, so this stays on
                    // the tiny-strip path where it beats full-layer redraw.
                    const bool scrollBaseReusable =
                        renderBaseReusable &&
                        cascadeMeshReusable &&
                        cascadeTerrainReusable &&
                        cache.cascadeValid[c] &&
                        m_sunShadowScrollScratchImage != VK_NULL_HANDLE &&
                        c < m_sunShadowLoadFramebuffers.size() &&
                        m_sunShadowLoadFramebuffers[c] != VK_NULL_HANDLE &&
                        c < cache.renderedCascadeVP.size();
                    const SunCascadeScrollPlan scrollPlan = scrollBaseReusable
                        ? makeSunCascadeScrollPlan(
                            cache.renderedCascadeVP[c],
                            m_sunCascadeVP[c],
                            m_sunRuntimeConfig.mapSize)
                        : SunCascadeScrollPlan{};
                    sunCascadeNeedsRender[c] = true;
                    sunCascadeRenderMask |= (1u << c);
                    ++sunCascadesRendered;
                    if (scrollPlan.enabled) {
                        sunCascadeScrollPlans[c] = scrollPlan;
                        m_sunCurrentFrame.cascadeScrollDxTexels[c] = scrollPlan.dxTexels;
                        m_sunCurrentFrame.cascadeScrollDyTexels[c] = scrollPlan.dyTexels;
                        m_sunCurrentFrame.cascadeScrollDirtyTexels[c] = scrollPlan.dirtyTexels;
                        sunCascadeScrollMask |= (1u << c);
                        ++sunCascadesScrolled;
                        sunScrollCopiedTexels += scrollPlan.copiedTexels;
                        sunScrollDirtyTexels += scrollPlan.dirtyTexels;
                    } else {
                        sunCascadeFullRenderMask |= (1u << c);
                        ++sunCascadesFullRendered;
                    }
                }
            }

            sunReused = (sunCascadesRendered == 0u);
            m_sunCurrentFrame.cpuCacheDecisionMs = std::chrono::duration<float, std::milli>(
                Clock::now() - cacheDecisionStart).count();
        }
        m_sunCurrentFrame.renderCacheHit = sunReused;
        m_sunCurrentFrame.cascadeRenderMask = sunCascadeRenderMask;
        m_sunCurrentFrame.cascadeReuseMask = sunCascadeReuseMask;
        m_sunCurrentFrame.cascadeFullRenderMask = sunCascadeFullRenderMask;
        m_sunCurrentFrame.cascadeScrollMask = sunCascadeScrollMask;
        m_sunCurrentFrame.cascadesRendered = sunCascadesRendered;
        m_sunCurrentFrame.cascadesReused = sunCascadesReused;
        m_sunCurrentFrame.cascadesFullRendered = sunCascadesFullRendered;
        m_sunCurrentFrame.cascadesScrolled = sunCascadesScrolled;
        m_sunCurrentFrame.scrollCopiedTexels = sunScrollCopiedTexels;
        m_sunCurrentFrame.scrollDirtyTexels = sunScrollDirtyTexels;

        m_sunCurrentFrame.cpuTerrainGatherMs = std::chrono::duration<float, std::milli>(
            Clock::now() - sunTerrainGatherStart).count();

        const uint64_t sunFrameNumber = m_sunFrameCounter++;

        // ── In-game shadow texel-grid log ──────────────────────────────
        // Record the actual rendered VP state per frame (cache hit/miss,
        // VP origin in shadow-texel units, sun direction, raw vs snapped
        // angles). One entry per actual VP change OR cache flip; this is
        // the in-game equivalent of the patch simulator's shadow-tip log
        // and reveals exactly what the visible shadow does on the texel
        // grid frame-to-frame.
        if (m_sunTexelLogEnabled) {
            const float kHalfMap =
                static_cast<float>(m_sunRuntimeConfig.mapSize) * 0.5f;
            SunShadowTexelEvent ev{};
            ev.frameNumber = sunFrameNumber;
            ev.cacheHit = sunReused;
            ev.rawAzimuth = m_smoothAzimuth;
            ev.rawElevation = m_smoothElevation;
            ev.snappedAzimuth = m_activeAzimuth;
            ev.snappedElevation = m_activeElevation;
            ev.sunDir = m_sunDir;
            ev.texelOriginX = m_sunLightVP[3][0] * kHalfMap;
            ev.texelOriginY = m_sunLightVP[3][1] * kHalfMap;
            ev.texelDepth = m_sunLightVP[3][2] * 65535.0f;
            const float invSinElLog =
                (m_sunDir.y < -1e-4f) ? (1.0f / -m_sunDir.y) : 0.0f;
            ev.shearX = m_sunDir.x * invSinElLog;
            ev.shearZ = m_sunDir.z * invSinElLog;

            bool emit = !m_sunTexelLogPrevValid;
            if (m_sunTexelLogPrevValid) {
                const auto& p = m_sunTexelLogPrev;
                ev.dTexelX = ev.texelOriginX - p.texelOriginX;
                ev.dTexelY = ev.texelOriginY - p.texelOriginY;
                ev.dTexelDepth = ev.texelDepth - p.texelDepth;

                const float absDx = std::abs(ev.dTexelX);
                const float absDy = std::abs(ev.dTexelY);
                const bool sunDirBitsChanged =
                    (floatToBits(ev.sunDir.x) != floatToBits(p.sunDir.x)) ||
                    (floatToBits(ev.sunDir.y) != floatToBits(p.sunDir.y)) ||
                    (floatToBits(ev.sunDir.z) != floatToBits(p.sunDir.z));

                if (ev.cacheHit != p.cacheHit)
                    ev.reasonMask |= SUN_TEXEL_EVENT_REASON_CACHE_FLIP;
                if (absDx >= 1.0f)
                    ev.reasonMask |= SUN_TEXEL_EVENT_REASON_TEXEL_X;
                if (absDy >= 1.0f)
                    ev.reasonMask |= SUN_TEXEL_EVENT_REASON_TEXEL_Y;
                if (std::abs(ev.dTexelDepth) > 0.5f)
                    ev.reasonMask |= SUN_TEXEL_EVENT_REASON_DEPTH;
                if (sunDirBitsChanged)
                    ev.reasonMask |= SUN_TEXEL_EVENT_REASON_SUNDIR_BITS;
                if ((absDx > 0.001f && absDx < 1.0f) ||
                    (absDy > 0.001f && absDy < 1.0f))
                    ev.reasonMask |= SUN_TEXEL_EVENT_REASON_SUBTEXEL_XY;

                emit = (ev.reasonMask != 0u);

                // Suppress camera-motion-only events unless explicitly
                // requested. With realtime celestials disabled, the sun
                // is bit-stationary and every frame's reasonMask is
                // dominated by camera-driven VP shifts (TexelX|TexelY|
                // Depth|SubTexel|CacheFlip with no SUNDIR_BITS). Those
                // are correct rendering behaviour (the shadow map
                // follows the player) but they swamp the realtime-sun
                // signal, so we drop them from the log by default.
                if (emit &&
                    !m_sunTexelLogIncludeCameraOnly &&
                    (ev.reasonMask & SUN_TEXEL_EVENT_SUN_CHANGE_MASK) == 0u) {
                    emit = false;
                }
            }
            ev.framesSincePrev = m_sunTexelLogPrevValid
                ? static_cast<uint32_t>(sunFrameNumber - m_sunTexelLogPrevFrame)
                : 0u;

            if (emit) {
                m_sunTexelLog.push_back(ev);
                while (m_sunTexelLog.size() > SUN_TEXEL_LOG_CAPACITY) {
                    m_sunTexelLog.pop_front();
                }
            }
            // Always track prev VP state so deltas stay frame-local
            // even when intermediate camera-only events were dropped.
            m_sunTexelLogPrev = ev;
            m_sunTexelLogPrevValid = true;
            m_sunTexelLogPrevFrame = sunFrameNumber;
        }

        // Patch simulator: 13×13 voxel grid (each voxel = 16 light cells
        // of 0.015625 m). A virtual 1-voxel cube sits at the centre; its
        // top face shears onto the ground via the sun direction. The
        // integer light-cell offset of that shadow stamp is the only
        // thing that controls visible motion. We log a snapshot whenever
        // that integer offset (or shadowActive) changes — one row per
        // visible shadow step.
        auto logSunDebugSnapshot = [&](bool shadowActive) {
            constexpr float kCellsPerVoxel =
                static_cast<float>(SUN_DEBUG_CELLS_PER_VOXEL); // 16
            const float invSinEl =
                (m_sunDir.y < -1e-4f) ? (1.0f / -m_sunDir.y) : 0.0f;
            const float shearX = m_sunDir.x * invSinEl;
            const float shearZ = m_sunDir.z * invSinEl;
            // Top-of-voxel shadow tip = voxel base + 0.25 m * shear.
            // In light cells (0.25/16 m): floor(16 * shear).
            const int32_t tipCellX =
                static_cast<int32_t>(std::floor(kCellsPerVoxel * shearX));
            const int32_t tipCellZ =
                static_cast<int32_t>(std::floor(kCellsPerVoxel * shearZ));

            int32_t dCellX = 0, dCellZ = 0;
            bool changed = m_sunDebugLog.empty();
            if (!m_sunDebugLog.empty()) {
                const auto& prev = m_sunDebugLog.back();
                dCellX = tipCellX - prev.tipCellX;
                dCellZ = tipCellZ - prev.tipCellZ;
                const bool tipMoved = (dCellX != 0) || (dCellZ != 0);
                const bool activeFlipped =
                    (prev.shadowActive != shadowActive);
                changed = tipMoved || activeFlipped;
            }
            if (!changed) return;

            SunShadowDebugSnapshot s{};
            s.frameNumber = sunFrameNumber;
            s.azimuth = m_activeAzimuth;
            s.elevation = m_activeElevation;
            s.sunDir = m_sunDir;
            s.shearX = shearX;
            s.shearZ = shearZ;
            s.tipCellX = tipCellX;
            s.tipCellZ = tipCellZ;
            s.dCellX = dCellX;
            s.dCellZ = dCellZ;
            s.shadowActive = shadowActive;
            m_sunDebugLog.push_back(s);
            while (m_sunDebugLog.size() > SUN_DEBUG_LOG_CAPACITY)
                m_sunDebugLog.pop_front();
        };

        if (!sunReused) {
            const auto sunRecordStart = Clock::now();
            VkClearValue sunClear{};
            sunClear.depthStencil = {1.0f, 0};

            VkViewport sunVp{};
            sunVp.x = 0.0f;
            sunVp.y = 0.0f;
            sunVp.width = static_cast<float>(m_sunRuntimeConfig.mapSize);
            sunVp.height = static_cast<float>(m_sunRuntimeConfig.mapSize);
            sunVp.minDepth = 0.0f;
            sunVp.maxDepth = 1.0f;

            VkRect2D sunSc{};
            sunSc.offset = {0, 0};
            sunSc.extent = {m_sunRuntimeConfig.mapSize, m_sunRuntimeConfig.mapSize};

            VkDeviceSize sunZero = 0;
            m_sunLocalIndirectCounts.fill(0u);
            const bool canUseSunLocalIndirect =
                useLocalForSun &&
                sunLocalDrawCount > 0u &&
                imageIndex < m_sunLocalIndirectBuffers.size() &&
                imageIndex < m_sunLocalOriginsBuffers.size() &&
                imageIndex < m_sunLocalUploadBuffers.size() &&
                imageIndex < m_sunLocalUploadMapped.size() &&
                m_sunLocalIndirectBuffers[imageIndex] != VK_NULL_HANDLE &&
                m_sunLocalOriginsBuffers[imageIndex] != VK_NULL_HANDLE &&
                m_sunLocalUploadBuffers[imageIndex] != VK_NULL_HANDLE &&
                m_sunLocalUploadMapped[imageIndex] != nullptr;

            // GPU timestamp: begin sun shadow render
            if (haveSunTimingQueries) {
                vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                                   m_sunTimingQueryPool, imageIndex * 2u);
            }

            if (canUseSunLocalIndirect) {
                const auto indirectBuildStart = Clock::now();
                const VkDeviceSize indirectBytes =
                    sizeof(VkDrawIndexedIndirectCommand) * SUN_LOCAL_INDIRECT_TOTAL_DRAWS;
                auto* uploadBase = static_cast<uint8_t*>(m_sunLocalUploadMapped[imageIndex]);
                auto* uploadDraws = reinterpret_cast<VkDrawIndexedIndirectCommand*>(uploadBase);
                auto* uploadOrigins = reinterpret_cast<glm::vec4*>(uploadBase + indirectBytes);

                for (uint32_t drawIndex = 0; drawIndex < sunLocalDrawCount; ++drawIndex) {
                    const uint16_t mask = m_sunLocalTerrainCascadeMaskScratch[drawIndex];
                    for (uint32_t cascade = 0u; cascade < sunCascadeCount; ++cascade) {
                        if (!sunCascadeNeedsRender[cascade]) {
                            continue;
                        }
                        if ((mask & static_cast<uint16_t>(1u << cascade)) == 0u) {
                            continue;
                        }
                        const uint32_t inCascade = m_sunLocalIndirectCounts[cascade]++;
                        const uint32_t outIndex =
                            cascade * SUN_LOCAL_INDIRECT_DRAWS_PER_CASCADE + inCascade;
                        VkDrawIndexedIndirectCommand packed = m_sunLocalTerrainDrawScratch[drawIndex];
                        packed.instanceCount = 1u;
                        packed.firstInstance = outIndex;
                        uploadDraws[outIndex] = packed;
                        uploadOrigins[outIndex] = m_sunLocalTerrainOriginScratch[drawIndex];
                    }
                }
                m_sunCurrentFrame.cpuIndirectBuildMs = std::chrono::duration<float, std::milli>(
                    Clock::now() - indirectBuildStart).count();

                const auto uploadRecordStart = Clock::now();
                std::array<VkBufferCopy, MAX_SUN_SHADOW_CASCADES> indirectCopies{};
                std::array<VkBufferCopy, MAX_SUN_SHADOW_CASCADES> originCopies{};
                uint32_t indirectCopyCount = 0u;
                uint32_t originCopyCount = 0u;
                for (uint32_t cascade = 0u; cascade < sunCascadeCount; ++cascade) {
                    if (!sunCascadeNeedsRender[cascade]) {
                        continue;
                    }
                    const uint32_t count = m_sunLocalIndirectCounts[cascade];
                    if (count == 0u) {
                        continue;
                    }
                    const VkDeviceSize drawOffset =
                        static_cast<VkDeviceSize>(cascade) *
                        SUN_LOCAL_INDIRECT_DRAWS_PER_CASCADE *
                        sizeof(VkDrawIndexedIndirectCommand);
                    const VkDeviceSize originOffset =
                        static_cast<VkDeviceSize>(cascade) *
                        SUN_LOCAL_INDIRECT_DRAWS_PER_CASCADE *
                        sizeof(glm::vec4);
                    indirectCopies[indirectCopyCount++] = VkBufferCopy{
                        drawOffset,
                        drawOffset,
                        static_cast<VkDeviceSize>(count) * sizeof(VkDrawIndexedIndirectCommand)};
                    originCopies[originCopyCount++] = VkBufferCopy{
                        indirectBytes + originOffset,
                        originOffset,
                        static_cast<VkDeviceSize>(count) * sizeof(glm::vec4)};
                }
                if (indirectCopyCount > 0u) {
                    vkCmdCopyBuffer(
                        cmd,
                        m_sunLocalUploadBuffers[imageIndex],
                        m_sunLocalIndirectBuffers[imageIndex],
                        indirectCopyCount,
                        indirectCopies.data());
                }
                if (originCopyCount > 0u) {
                    vkCmdCopyBuffer(
                        cmd,
                        m_sunLocalUploadBuffers[imageIndex],
                        m_sunLocalOriginsBuffers[imageIndex],
                        originCopyCount,
                        originCopies.data());
                }
                if (indirectCopyCount > 0u || originCopyCount > 0u) {
                    std::array<VkBufferMemoryBarrier2, 2> uploadBarriers{};
                    uploadBarriers[0].sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
                    uploadBarriers[0].srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                    uploadBarriers[0].srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                    uploadBarriers[0].dstStageMask = VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT;
                    uploadBarriers[0].dstAccessMask = VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT;
                    uploadBarriers[0].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    uploadBarriers[0].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    uploadBarriers[0].buffer = m_sunLocalIndirectBuffers[imageIndex];
                    uploadBarriers[0].offset = 0;
                    uploadBarriers[0].size = VK_WHOLE_SIZE;

                    uploadBarriers[1].sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
                    uploadBarriers[1].srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                    uploadBarriers[1].srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                    uploadBarriers[1].dstStageMask = VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT;
                    uploadBarriers[1].dstAccessMask = VK_ACCESS_2_SHADER_STORAGE_READ_BIT;
                    uploadBarriers[1].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    uploadBarriers[1].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    uploadBarriers[1].buffer = m_sunLocalOriginsBuffers[imageIndex];
                    uploadBarriers[1].offset = 0;
                    uploadBarriers[1].size = VK_WHOLE_SIZE;

                    VkDependencyInfo uploadDep{};
                    uploadDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                    uploadDep.bufferMemoryBarrierCount = static_cast<uint32_t>(uploadBarriers.size());
                    uploadDep.pBufferMemoryBarriers = uploadBarriers.data();
                    vkCmdPipelineBarrier2(cmd, &uploadDep);
                }
                m_sunCurrentFrame.cpuIndirectUploadRecordMs =
                    std::chrono::duration<float, std::milli>(
                        Clock::now() - uploadRecordStart).count();
            }

            uint32_t sunDrawCallCount = 0u;
            uint32_t sunApiDrawCallCount = 0u;
            auto recordSunCascadeScroll = [&](uint32_t cascade, const SunCascadeScrollPlan& plan) {
                if (!plan.enabled || m_sunShadowScrollScratchImage == VK_NULL_HANDLE) {
                    return;
                }

                const uint32_t mapSize = m_sunRuntimeConfig.mapSize;
                const uint32_t absDx = static_cast<uint32_t>(std::abs(plan.dxTexels));
                const uint32_t absDy = static_cast<uint32_t>(std::abs(plan.dyTexels));
                const uint32_t copyWidth = mapSize - absDx;
                const uint32_t copyHeight = mapSize - absDy;
                if (copyWidth == 0u || copyHeight == 0u) {
                    return;
                }

                const int32_t srcX = (plan.dxTexels > 0) ? 0 : static_cast<int32_t>(absDx);
                const int32_t srcY = (plan.dyTexels > 0) ? 0 : static_cast<int32_t>(absDy);
                const int32_t dstX = (plan.dxTexels > 0) ? plan.dxTexels : 0;
                const int32_t dstY = (plan.dyTexels > 0) ? plan.dyTexels : 0;

                std::array<VkImageMemoryBarrier2, 2> toTransfer{};
                toTransfer[0].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                toTransfer[0].srcStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
                toTransfer[0].srcAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
                toTransfer[0].dstStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toTransfer[0].dstAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
                toTransfer[0].oldLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
                toTransfer[0].newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
                toTransfer[0].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toTransfer[0].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toTransfer[0].image = m_sunShadowImage;
                toTransfer[0].subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                toTransfer[0].subresourceRange.baseMipLevel = 0;
                toTransfer[0].subresourceRange.levelCount = 1;
                toTransfer[0].subresourceRange.baseArrayLayer = cascade;
                toTransfer[0].subresourceRange.layerCount = 1;

                toTransfer[1].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                toTransfer[1].srcStageMask =
                    (m_sunShadowScrollScratchLayout == VK_IMAGE_LAYOUT_UNDEFINED)
                        ? VK_PIPELINE_STAGE_2_NONE
                        : VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toTransfer[1].srcAccessMask =
                    (m_sunShadowScrollScratchLayout == VK_IMAGE_LAYOUT_UNDEFINED)
                        ? VK_ACCESS_2_NONE
                        : VK_ACCESS_2_TRANSFER_READ_BIT;
                toTransfer[1].dstStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toTransfer[1].dstAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                toTransfer[1].oldLayout = m_sunShadowScrollScratchLayout;
                toTransfer[1].newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                toTransfer[1].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toTransfer[1].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toTransfer[1].image = m_sunShadowScrollScratchImage;
                toTransfer[1].subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                toTransfer[1].subresourceRange.baseMipLevel = 0;
                toTransfer[1].subresourceRange.levelCount = 1;
                toTransfer[1].subresourceRange.baseArrayLayer = 0;
                toTransfer[1].subresourceRange.layerCount = 1;

                VkDependencyInfo toTransferDep{};
                toTransferDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                toTransferDep.imageMemoryBarrierCount = static_cast<uint32_t>(toTransfer.size());
                toTransferDep.pImageMemoryBarriers = toTransfer.data();
                vkCmdPipelineBarrier2(cmd, &toTransferDep);
                m_sunShadowScrollScratchLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;

                VkImageCopy copyToScratch{};
                copyToScratch.srcSubresource.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                copyToScratch.srcSubresource.mipLevel = 0;
                copyToScratch.srcSubresource.baseArrayLayer = cascade;
                copyToScratch.srcSubresource.layerCount = 1;
                copyToScratch.srcOffset = {srcX, srcY, 0};
                copyToScratch.dstSubresource.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                copyToScratch.dstSubresource.mipLevel = 0;
                copyToScratch.dstSubresource.baseArrayLayer = 0;
                copyToScratch.dstSubresource.layerCount = 1;
                copyToScratch.dstOffset = {0, 0, 0};
                copyToScratch.extent = {copyWidth, copyHeight, 1};
                vkCmdCopyImage(
                    cmd,
                    m_sunShadowImage,
                    VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    m_sunShadowScrollScratchImage,
                    VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    1,
                    &copyToScratch);

                std::array<VkImageMemoryBarrier2, 2> toCopyBack{};
                toCopyBack[0].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                toCopyBack[0].srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toCopyBack[0].srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                toCopyBack[0].dstStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toCopyBack[0].dstAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
                toCopyBack[0].oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                toCopyBack[0].newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
                toCopyBack[0].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toCopyBack[0].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toCopyBack[0].image = m_sunShadowScrollScratchImage;
                toCopyBack[0].subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                toCopyBack[0].subresourceRange.baseMipLevel = 0;
                toCopyBack[0].subresourceRange.levelCount = 1;
                toCopyBack[0].subresourceRange.baseArrayLayer = 0;
                toCopyBack[0].subresourceRange.layerCount = 1;

                toCopyBack[1] = toTransfer[0];
                toCopyBack[1].srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toCopyBack[1].srcAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
                toCopyBack[1].dstStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toCopyBack[1].dstAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                toCopyBack[1].oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
                toCopyBack[1].newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;

                VkDependencyInfo toCopyBackDep{};
                toCopyBackDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                toCopyBackDep.imageMemoryBarrierCount = static_cast<uint32_t>(toCopyBack.size());
                toCopyBackDep.pImageMemoryBarriers = toCopyBack.data();
                vkCmdPipelineBarrier2(cmd, &toCopyBackDep);
                m_sunShadowScrollScratchLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;

                VkImageCopy copyBack = copyToScratch;
                copyBack.srcSubresource.baseArrayLayer = 0;
                copyBack.srcOffset = {0, 0, 0};
                copyBack.dstSubresource.baseArrayLayer = cascade;
                copyBack.dstOffset = {dstX, dstY, 0};
                vkCmdCopyImage(
                    cmd,
                    m_sunShadowScrollScratchImage,
                    VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    m_sunShadowImage,
                    VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    1,
                    &copyBack);

                VkImageMemoryBarrier2 toAttachment{};
                toAttachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                toAttachment.srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                toAttachment.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                toAttachment.dstStageMask =
                    VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT |
                    VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
                toAttachment.dstAccessMask =
                    VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
                    VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                toAttachment.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                toAttachment.newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
                toAttachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toAttachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                toAttachment.image = m_sunShadowImage;
                toAttachment.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                toAttachment.subresourceRange.baseMipLevel = 0;
                toAttachment.subresourceRange.levelCount = 1;
                toAttachment.subresourceRange.baseArrayLayer = cascade;
                toAttachment.subresourceRange.layerCount = 1;

                VkDependencyInfo toAttachmentDep{};
                toAttachmentDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
                toAttachmentDep.imageMemoryBarrierCount = 1;
                toAttachmentDep.pImageMemoryBarriers = &toAttachment;
                vkCmdPipelineBarrier2(cmd, &toAttachmentDep);
            };

            for (uint32_t cascade = 0u; cascade < sunCascadeCount; ++cascade) {
                if ((sunCascadeScrollMask & (1u << cascade)) == 0u) {
                    continue;
                }
                recordSunCascadeScroll(cascade, sunCascadeScrollPlans[cascade]);
            }

            for (uint32_t cascade = 0u; cascade < sunCascadeCount; ++cascade) {
                if (!sunCascadeNeedsRender[cascade]) {
                    continue;
                }
                const auto cascadeRecordStart = Clock::now();
                const bool scrollUpdate = sunCascadeScrollPlans[cascade].enabled;
                uint32_t drawRectCount = 0u;
                std::array<VkRect2D, 2> drawRects{};
                if (scrollUpdate) {
                    drawRects = sunScrollDirtyRects(
                        m_sunRuntimeConfig.mapSize,
                        sunCascadeScrollPlans[cascade],
                        drawRectCount);
                } else {
                    drawRects[0] = sunSc;
                    drawRectCount = 1u;
                }
                if (drawRectCount == 0u) {
                    m_sunCurrentFrame.cascadeRecordMs[cascade] =
                        std::chrono::duration<float, std::milli>(
                            Clock::now() - cascadeRecordStart).count();
                    continue;
                }

                VkRenderPassBeginInfo sunRpBegin{};
                sunRpBegin.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
                sunRpBegin.renderPass = scrollUpdate ? m_shadowLoadRenderPass : m_shadowRenderPass;
                sunRpBegin.framebuffer = scrollUpdate
                    ? m_sunShadowLoadFramebuffers[cascade]
                    : m_sunShadowFramebuffers[cascade];
                sunRpBegin.renderArea.offset = {0, 0};
                sunRpBegin.renderArea.extent = {m_sunRuntimeConfig.mapSize, m_sunRuntimeConfig.mapSize};
                sunRpBegin.clearValueCount = scrollUpdate ? 0u : 1u;
                sunRpBegin.pClearValues = scrollUpdate ? nullptr : &sunClear;

                vkCmdBeginRenderPass(cmd, &sunRpBegin, VK_SUBPASS_CONTENTS_INLINE);
                vkCmdSetViewport(cmd, 0, 1, &sunVp);
                if (scrollUpdate) {
                    VkClearAttachment stripClear{};
                    stripClear.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                    stripClear.clearValue = sunClear;
                    std::array<VkClearRect, 2> clearRects{};
                    for (uint32_t i = 0; i < drawRectCount; ++i) {
                        clearRects[i].rect = drawRects[i];
                        clearRects[i].baseArrayLayer = 0u;
                        clearRects[i].layerCount = 1u;
                    }
                    vkCmdClearAttachments(
                        cmd,
                        1,
                        &stripClear,
                        drawRectCount,
                        clearRects.data());
                }

                // Depth bias: anchored to cascade 0's behaviour so far rings
                // inherit the HD ring's resistance against missing-shadow
                // peter-panning. Previous formula scaled by log2(texelRatio)
                // AND by (1 + grazing*1.5) on the slope term — at low sun on
                // far cascades that combined into a bias large enough to
                // erase small casters' shadows. Keep a gentle per-cascade
                // bump (sqrt instead of full log) and a mild grazing term.
                const float nearTexel = std::max(m_sunRuntimeConfig.texelMeters, 1e-5f);
                const float cascadeTexel = std::max(m_sunCascadeTexelMeters[cascade], nearTexel);
                const float texelRatio = std::max(cascadeTexel / nearTexel, 1.0f);
                // sqrt(ratio) capped at 2.0: a 16x cascade gets only 2x bias
                // (vs 2.4x with log2*0.35) — small casters survive on far rings.
                const float cascadeBiasScale = std::min(2.0f, std::sqrt(texelRatio));
                const float sunGrazing = 1.0f - std::clamp(-m_sunDir.y, 0.0f, 1.0f);
                const float depthBiasConstant =
                    1.0f * cascadeBiasScale * (1.0f + sunGrazing * 0.35f);
                const float depthBiasSlope =
                    0.8f * cascadeBiasScale * (1.0f + sunGrazing * 0.60f);
                vkCmdSetDepthBias(cmd, depthBiasConstant, 0.0f, depthBiasSlope);

                ShadowPushConstants sunPush{};
                sunPush.lightVP = m_sunCascadeVP[cascade];
                sunPush.lightPosFar = glm::vec4(0.0f, 0.0f, 0.0f, 1.0f);
                sunPush.terrainChunkCoordMode = glm::vec4(0.0f);

                if (ctx.terrainDescriptorSet != VK_NULL_HANDLE &&
                    ctx.terrainVertexBuffer != VK_NULL_HANDLE &&
                    ctx.terrainIndexBuffer != VK_NULL_HANDLE) {

                    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_sunShadowPipeline);
                    vkCmdBindDescriptorSets(
                        cmd,
                        VK_PIPELINE_BIND_POINT_GRAPHICS,
                        m_terrainShadowPipelineLayout,
                        0, 1, &ctx.terrainDescriptorSet, 0, nullptr);
                    vkCmdBindVertexBuffers(cmd, 0, 1, &ctx.terrainVertexBuffer, &sunZero);
                    vkCmdBindIndexBuffer(cmd, ctx.terrainIndexBuffer, 0, VK_INDEX_TYPE_UINT16);

                    uint32_t cascadeIssuedDraws = 0u;
                    uint32_t cascadeApiDraws = 0u;
                    for (uint32_t rectIndex = 0; rectIndex < drawRectCount; ++rectIndex) {
                        vkCmdSetScissor(cmd, 0, 1, &drawRects[rectIndex]);

                        if (canUseSunLocalIndirect) {
                            const uint32_t cascadeLocalDraws = m_sunLocalIndirectCounts[cascade];
                            if (cascadeLocalDraws > 0u) {
                                sunPush.terrainChunkCoordMode = glm::vec4(0.0f, 0.0f, 0.0f, 3.0f);
                                vkCmdPushConstants(cmd, m_terrainShadowPipelineLayout,
                                    VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                                    0, sizeof(ShadowPushConstants), &sunPush);
                                const VkDeviceSize drawOffset =
                                    static_cast<VkDeviceSize>(cascade) *
                                    SUN_LOCAL_INDIRECT_DRAWS_PER_CASCADE *
                                    sizeof(VkDrawIndexedIndirectCommand);
                                vkCmdDrawIndexedIndirect(
                                    cmd,
                                    m_sunLocalIndirectBuffers[imageIndex],
                                    drawOffset,
                                    cascadeLocalDraws,
                                    sizeof(VkDrawIndexedIndirectCommand));
                                sunDrawCallCount += cascadeLocalDraws;
                                ++sunApiDrawCallCount;
                                cascadeIssuedDraws += cascadeLocalDraws;
                                ++cascadeApiDraws;
                            }
                        } else if (useLocalForSun && sunLocalDrawCount > 0u) {
                            const uint16_t cascadeBit = static_cast<uint16_t>(1u << cascade);
                            uint32_t cascadeLocalDraws = 0u;
                            for (uint32_t drawIndex = 0; drawIndex < sunLocalDrawCount; ++drawIndex) {
                                // Per-cascade visibility was decided once during
                                // gather (World::gatherDrawCommandsForSunCascades);
                                // a single bit-test replaces the previous
                                // per-cascade clip-AABB recomputation.
                                if ((m_sunLocalTerrainCascadeMaskScratch[drawIndex] & cascadeBit) == 0u) {
                                    continue;
                                }
                                const auto& terrainDraw = m_sunLocalTerrainDrawScratch[drawIndex];
                                const glm::vec4& chunkOrigin = m_sunLocalTerrainOriginScratch[drawIndex];
                                sunPush.terrainChunkCoordMode = glm::vec4(
                                    chunkOrigin.x, chunkOrigin.y, chunkOrigin.z, 1.0f);
                                vkCmdPushConstants(cmd, m_terrainShadowPipelineLayout,
                                    VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                                    0, sizeof(ShadowPushConstants), &sunPush);
                                vkCmdDrawIndexed(cmd,
                                    terrainDraw.indexCount, 1u,
                                    terrainDraw.firstIndex, terrainDraw.vertexOffset, 0u);
                                ++cascadeLocalDraws;
                                ++sunApiDrawCallCount;
                            }
                            sunDrawCallCount += cascadeLocalDraws;
                            cascadeIssuedDraws += cascadeLocalDraws;
                            cascadeApiDraws += cascadeLocalDraws;
                        } else if (ctx.useGPUCulling &&
                            ctx.gpuVisibleDrawsBuffer != VK_NULL_HANDLE &&
                            ctx.gpuDrawCountBuffer != VK_NULL_HANDLE &&
                            ctx.gpuMaxDraws > 0) {
                            sunPush.terrainChunkCoordMode = glm::vec4(0.0f, 0.0f, 0.0f, 2.0f);
                            vkCmdPushConstants(cmd, m_terrainShadowPipelineLayout,
                                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                                0, sizeof(ShadowPushConstants), &sunPush);
                            vkCmdDrawIndexedIndirectCount(cmd,
                                ctx.gpuVisibleDrawsBuffer, 0,
                                ctx.gpuDrawCountBuffer, 0,
                                ctx.gpuMaxDraws,
                                sizeof(VkDrawIndexedIndirectCommand));
                            ++sunDrawCallCount;
                            ++sunApiDrawCallCount;
                            ++cascadeIssuedDraws;
                            ++cascadeApiDraws;
                        } else if (ctx.indirectBuffer != VK_NULL_HANDLE && ctx.indirectDrawCount > 0) {
                            vkCmdPushConstants(cmd, m_terrainShadowPipelineLayout,
                                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                                0, sizeof(ShadowPushConstants), &sunPush);
                            vkCmdDrawIndexedIndirect(cmd,
                                ctx.indirectBuffer, 0,
                                ctx.indirectDrawCount,
                                sizeof(VkDrawIndexedIndirectCommand));
                            ++sunDrawCallCount;
                            ++sunApiDrawCallCount;
                            ++cascadeIssuedDraws;
                            ++cascadeApiDraws;
                        }
                    }
                    m_sunCurrentFrame.cascadeIssuedDrawCalls[cascade] = cascadeIssuedDraws;
                    m_sunCurrentFrame.cascadeApiDrawCalls[cascade] = cascadeApiDraws;
                }

                vkCmdEndRenderPass(cmd);
                m_sunCurrentFrame.cascadeRecordMs[cascade] =
                    std::chrono::duration<float, std::milli>(
                        Clock::now() - cascadeRecordStart).count();
            }
            m_sunCurrentFrame.drawCallCount = sunDrawCallCount;
            m_sunCurrentFrame.apiDrawCallCount = sunApiDrawCallCount;

            // Barrier: sun shadow map depth write -> shader read
            const auto barrierStart = Clock::now();
            std::array<VkImageMemoryBarrier2, MAX_SUN_SHADOW_CASCADES> sunBarriers{};
            uint32_t sunBarrierCount = 0u;
            for (uint32_t cascade = 0u; cascade < sunCascadeCount; ++cascade) {
                if (!sunCascadeNeedsRender[cascade]) {
                    continue;
                }
                VkImageMemoryBarrier2& sunBarrier = sunBarriers[sunBarrierCount++];
                sunBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                sunBarrier.srcStageMask =
                    VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT |
                    VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
                sunBarrier.srcAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                sunBarrier.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
                sunBarrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
                sunBarrier.oldLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
                sunBarrier.newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
                sunBarrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                sunBarrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                sunBarrier.image = m_sunShadowImage;
                sunBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                sunBarrier.subresourceRange.baseMipLevel = 0;
                sunBarrier.subresourceRange.levelCount = 1;
                sunBarrier.subresourceRange.baseArrayLayer = cascade;
                sunBarrier.subresourceRange.layerCount = 1;
            }

            VkDependencyInfo sunDepInfo{};
            sunDepInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            sunDepInfo.imageMemoryBarrierCount = sunBarrierCount;
            sunDepInfo.pImageMemoryBarriers = sunBarriers.data();
            if (sunBarrierCount > 0u) {
                vkCmdPipelineBarrier2(cmd, &sunDepInfo);
            }

            // GPU timestamp: end sun shadow render (after barrier)
            if (haveSunTimingQueries) {
                vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                                   m_sunTimingQueryPool, imageIndex * 2u + 1u);
                m_sunTimingWritten[imageIndex] = true;
            }
            m_sunCurrentFrame.cpuBarrierMs = std::chrono::duration<float, std::milli>(
                Clock::now() - barrierStart).count();

            const float sunRecordMs = std::chrono::duration<float, std::milli>(
                Clock::now() - sunRecordStart).count();
            m_sunCurrentFrame.cpuCommandRecordMs = sunRecordMs;
            m_sunCurrentFrame.wasReused = false;

            logSunDebugSnapshot(true);

            // Update cache with current state so next frame can skip.
            m_sunShadowCache.valid = true;
            m_sunShadowCache.cameraPos = m_sunCameraPos;
            m_sunShadowCache.azimuth = m_activeAzimuth;
            m_sunShadowCache.elevation = m_activeElevation;
            m_sunShadowCache.terrainSignature = sunTerrainSig;
            m_sunShadowCache.ctxHash = terrainSigForSun;
            m_sunShadowCache.terrainMeshRevision = ctx.terrainMeshRevision;
            m_sunShadowCache.uploadTimeline = ctx.uploadTimelineValue;
            m_sunShadowCache.uploadPendingRejects = m_sunCurrentFrame.uploadPendingRejects;
            m_sunShadowCache.terrainDrawCount = sunLocalDrawCount;
            m_sunShadowCache.cascadeCount = sunCascadeCount;
            m_sunShadowCache.vpHash = sunVpHash;
            m_sunShadowCache.renderedVP = m_sunCascadeVP[0];
            m_sunShadowCache.renderedCascadeVP = {};
            m_sunShadowCache.cascadeValid.fill(false);
            m_sunShadowCache.cascadeVpHashes.fill(0u);
            m_sunShadowCache.cascadeTerrainSignatures.fill(0u);
            m_sunShadowCache.cascadeDrawCounts.fill(0u);
            for (uint32_t i = 0; i < sunCascadeCount; ++i) {
                m_sunShadowCache.cascadeValid[i] = true;
                m_sunShadowCache.cascadeVpHashes[i] = sunCascadeVpHashes[i];
                m_sunShadowCache.cascadeTerrainSignatures[i] = sunCascadeTerrainSigs[i];
                m_sunShadowCache.cascadeDrawCounts[i] = m_sunCurrentFrame.cascadeGatherDrawHits[i];
                m_sunShadowCache.renderedCascadeVP[i] = m_sunCascadeVP[i];
            }
            m_sunShadowCache.renderedSunDir = m_sunDir;
            m_sunShadowCache.loadedChunkMapSize = m_sunCurrentFrame.loadedChunkMapSize;
            m_sunShadowCache.bboxCandidateChunks = m_sunCurrentFrame.bboxCandidateChunks;
            m_sunShadowCache.visitedCandidateChunks = m_sunCurrentFrame.visitedCandidateChunks;
            m_sunShadowCache.acceptedChunks = m_sunCurrentFrame.acceptedChunkCount;
            m_sunShadowCache.cascadeInnerCullRejects = m_sunCurrentFrame.cascadeInnerCullRejects;
            m_sunShadowCache.sinElevation = m_sunCurrentFrame.gatherSinElevation;
            m_sunShadowCache.shearX = m_sunCurrentFrame.gatherShearX;
            m_sunShadowCache.shearZ = m_sunCurrentFrame.gatherShearZ;
            m_sunShadowCache.shearMax = m_sunCurrentFrame.gatherShearMax;
            m_sunShadowCache.casterReach = m_sunCurrentFrame.gatherCasterReach;
            m_sunShadowCache.padding = m_sunCurrentFrame.gatherPadding;
            m_sunShadowCache.halfX = m_sunCurrentFrame.gatherHalfX;
            m_sunShadowCache.halfZ = m_sunCurrentFrame.gatherHalfZ;
            m_sunShadowCache.cameraChunk = sunCameraChunk;
            m_sunShadowCache.gatherMinChunk = m_sunCurrentFrame.gatherMinChunk;
            m_sunShadowCache.gatherMaxChunk = m_sunCurrentFrame.gatherMaxChunk;
            m_sunShadowCache.cascadeChunkHits = m_sunCurrentFrame.cascadeGatherChunkHits;
            m_sunShadowCache.cascadeDrawHits = m_sunCurrentFrame.cascadeGatherDrawHits;
        } else {
            // Cache hit — sun shadow reused.
            // Patch the GPU SSBO to use the VP AND sunDir the shadow map
            // was actually rendered with.  This ensures the depth encoding,
            // push direction, and ndotl all match the shadow map content.
            m_activeSunCascadeCount = std::min<uint32_t>(
                sunCascadeCount,
                MAX_SUN_SHADOW_CASCADES);
            for (uint32_t i = 0; i < MAX_SUN_SHADOW_CASCADES; ++i) {
                m_sunCascadeVP[i] = glm::mat4(1.0f);
            }
            for (uint32_t i = 0; i < m_activeSunCascadeCount && i < MAX_SUN_SHADOW_CASCADES; ++i) {
                m_sunCascadeVP[i] = m_sunShadowCache.renderedCascadeVP[i];
            }
            m_sunLightVP = (m_activeSunCascadeCount > 0u)
                ? m_sunCascadeVP[0]
                : glm::mat4(1.0f);
            m_sunDir = m_sunShadowCache.renderedSunDir;
            m_sunShadowCache.cascadeCount = sunCascadeCount;
            m_sunShadowCache.vpHash = sunVpHash;
            m_sunShadowCache.terrainSignature = sunTerrainSig;
            m_sunShadowCache.ctxHash = terrainSigForSun;
            m_sunShadowCache.terrainMeshRevision = ctx.terrainMeshRevision;
            m_sunShadowCache.uploadTimeline = ctx.uploadTimelineValue;
            m_sunShadowCache.uploadPendingRejects = m_sunCurrentFrame.uploadPendingRejects;
            m_sunShadowCache.terrainDrawCount = sunLocalDrawCount;
            m_sunShadowCache.renderedVP = (sunCascadeCount > 0u)
                ? m_sunCascadeVP[0]
                : glm::mat4(1.0f);
            for (uint32_t i = 0; i < sunCascadeCount && i < MAX_SUN_SHADOW_CASCADES; ++i) {
                m_sunShadowCache.cascadeValid[i] = true;
                m_sunShadowCache.cascadeVpHashes[i] = sunCascadeVpHashes[i];
                m_sunShadowCache.cascadeTerrainSignatures[i] = sunCascadeTerrainSigs[i];
                m_sunShadowCache.cascadeDrawCounts[i] = m_sunCurrentFrame.cascadeGatherDrawHits[i];
                m_sunShadowCache.renderedCascadeVP[i] = m_sunCascadeVP[i];
            }
            if (imageIndex < m_shadowDataMapped.size() && m_shadowDataMapped[imageIndex]) {
                auto* gpu = reinterpret_cast<ShadowGPUData*>(m_shadowDataMapped[imageIndex]);
                for (uint32_t i = 0; i < MAX_SUN_SHADOW_CASCADES; ++i) {
                    gpu->sunLightVP[i] = m_sunCascadeVP[i];
                }
                gpu->sunDirTexelSize = glm::vec4(
                    m_sunShadowCache.renderedSunDir,
                    gpu->sunDirTexelSize.w);  // keep texelWorld (constant)
                gpu->shadowConfig2.x = static_cast<float>(m_activeSunCascadeCount);
            }
            m_sunCurrentFrame.wasReused = true;

            logSunDebugSnapshot(m_sunShadowActive);
        }

        m_sunCurrentFrame.cpuTotalMs =
            m_sunCurrentFrame.cpuTerrainGatherMs +
            m_sunCurrentFrame.cpuCommandRecordMs;
    }


}

````

## src\rendering\lighting\shadow\ShadowInternal.h

Description: No CC-DESC found. C++ struct 'SunCascadeScrollPlan'.

````cpp
#pragma once

// GPT-DESC: Shares private shadow-pass helper declarations across split ShadowSystem translation units.

#include "rendering/lighting/ShadowSystem.h"

#include <vulkan/vulkan.h>
#include <glm/glm.hpp>

#include <array>
#include <cstdint>
#include <limits>
#include <vector>

namespace ShadowSystemInternal {

inline constexpr uint32_t kInvalidSourceIndex = std::numeric_limits<uint32_t>::max();
inline constexpr uint32_t kPointShadowFacesPerLight = 6u;
inline constexpr float kShadowCacheEpsilon = 0.0001f;
inline constexpr int32_t kSunGatherCachePaddingChunks = 12;
inline constexpr float kSunScrollTexelEpsilon = 0.01f;
inline constexpr float kSunScrollMaxDirtyFraction = 0.08f;

struct SunCascadeScrollPlan {
    bool enabled{false};
    int32_t dxTexels{0};
    int32_t dyTexels{0};
    uint64_t copiedTexels{0};
    uint64_t dirtyTexels{0};
};

struct SunGatherBounds {
    glm::ivec3 minChunk{0};
    glm::ivec3 maxChunk{0};
    float maxHalfExtent{0.0f};
    float sinElevation{0.0f};
    float shearX{0.0f};
    float shearZ{0.0f};
    float shearMax{0.0f};
    float casterReach{0.0f};
    float padding{0.0f};
    float halfX{0.0f};
    float halfZ{0.0f};
};

uint32_t floatToBits(float value);
void hashCombine64(uint64_t& hash, uint64_t v);
uint64_t rotl64(uint64_t v, uint32_t shift);

bool nearlyEqual(float a, float b, float eps = kShadowCacheEpsilon);
bool nearlyEqualVec3(const glm::vec3& a, const glm::vec3& b, float eps = kShadowCacheEpsilon);
bool sameBitsVec3(const glm::vec3& a, const glm::vec3& b);

uint64_t hashTerrainDrawContext(const ShadowSystem::DrawContext& ctx);
uint64_t hashTerrainDrawKey(const VkDrawIndexedIndirectCommand& d, const glm::vec4& o);
void hashTerrainDrawSetAdd(uint64_t key,
                           uint64_t& sumA,
                           uint64_t& sumB,
                           uint64_t& xorA);
uint64_t finishTerrainDrawSetHash(uint32_t count,
                                  uint64_t sumA,
                                  uint64_t sumB,
                                  uint64_t xorA);
uint64_t hashLocalTerrainDraws(const std::vector<VkDrawIndexedIndirectCommand>& draws,
                               const std::vector<glm::vec4>& origins,
                               uint32_t count);
uint64_t hashSunCascadeVP(
    const std::array<glm::mat4, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& vps,
    uint32_t cascadeCount);
std::array<uint64_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES>
hashLocalTerrainDrawsPerCascade(
    const std::vector<VkDrawIndexedIndirectCommand>& draws,
    const std::vector<glm::vec4>& origins,
    const std::vector<uint16_t>& cascadeMasks,
    uint32_t count,
    uint32_t cascadeCount);
uint64_t hashSunCascadeLayerVP(const glm::mat4& vp);

SunCascadeScrollPlan makeSunCascadeScrollPlan(
    const glm::mat4& oldVP,
    const glm::mat4& newVP,
    uint32_t mapSize);
std::array<VkRect2D, 2> sunScrollDirtyRects(
    uint32_t mapSize,
    const SunCascadeScrollPlan& plan,
    uint32_t& rectCountOut);
SunGatherBounds computeSunGatherBounds(
    const glm::vec3& cameraPos,
    const glm::vec3& sunDir,
    const std::array<float, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& cascadeHalfExtents,
    uint32_t cascadeCount);
uint16_t sunCascadeMaskForChunkCoord(
    const glm::vec4& chunkCoord,
    const std::array<glm::mat4, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& cascadeVPs,
    uint32_t cascadeCount,
    uint32_t* outInnerRejected = nullptr);
uint32_t allCascadeBits(uint32_t cascadeCount);
uint32_t meshTopologyChangeCascadeMask(
    const ShadowSystem::DrawContext& ctx,
    uint64_t cachedRevision,
    const std::array<glm::mat4, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& cascadeVPs,
    uint32_t cascadeCount,
    bool* reliableOut = nullptr);

} // namespace ShadowSystemInternal

````

## src\rendering\lighting\shadow\ShadowPass.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

// GPT-DESC: Coordinates shadow pass recording across sun and point-light domain implementations.

void ShadowSystem::recordShadowPasses(VkCommandBuffer cmd,
                                      uint32_t imageIndex,
                                      const DrawContext& ctx) {
    if (!m_initialized || imageIndex >= m_shadowDataBuffers.size()) {
        return;
    }

    recordSunShadowPasses(cmd, imageIndex, ctx);
    recordPointShadowPasses(cmd, imageIndex, ctx);
}

````

## src\rendering\lighting\shadow\ShadowCache.cpp

Description: No CC-DESC found.

````cpp
#include "ShadowInternal.h"

// GPT-DESC: Implements shadow cache hashing and equality helpers shared by sun and point shadow passes.

#include <algorithm>
#include <cstring>
#include <type_traits>

namespace ShadowSystemInternal {

uint32_t floatToBits(float value) {
    uint32_t bits = 0u;
    std::memcpy(&bits, &value, sizeof(bits));
    return bits;
}

void hashCombine64(uint64_t& hash, uint64_t v) {
    hash ^= v + 0x9e3779b97f4a7c15ull + (hash << 6u) + (hash >> 2u);
}

uint64_t rotl64(uint64_t v, uint32_t shift) {
    return (v << shift) | (v >> (64u - shift));
}

bool nearlyEqual(float a, float b, float eps) {
    return std::abs(a - b) <= eps;
}

bool nearlyEqualVec3(const glm::vec3& a, const glm::vec3& b, float eps) {
    return nearlyEqual(a.x, b.x, eps) &&
           nearlyEqual(a.y, b.y, eps) &&
           nearlyEqual(a.z, b.z, eps);
}

bool sameBitsVec3(const glm::vec3& a, const glm::vec3& b) {
    return floatToBits(a.x) == floatToBits(b.x) &&
           floatToBits(a.y) == floatToBits(b.y) &&
           floatToBits(a.z) == floatToBits(b.z);
}

uint64_t hashTerrainDrawContext(const ShadowSystem::DrawContext& ctx) {
    auto handleToU64 = [](auto handle) -> uint64_t {
        using T = decltype(handle);
        if constexpr (std::is_pointer_v<T>) {
            return static_cast<uint64_t>(reinterpret_cast<uintptr_t>(handle));
        } else {
            return static_cast<uint64_t>(handle);
        }
    };

    uint64_t h = 1469598103934665603ull;
    hashCombine64(h, handleToU64(ctx.terrainVertexBuffer));
    hashCombine64(h, handleToU64(ctx.terrainIndexBuffer));
    hashCombine64(h, handleToU64(ctx.indirectBuffer));
    hashCombine64(h, static_cast<uint64_t>(ctx.indirectDrawCount));
    hashCombine64(h, static_cast<uint64_t>(ctx.useGPUCulling ? 1u : 0u));
    hashCombine64(h, handleToU64(ctx.gpuVisibleDrawsBuffer));
    hashCombine64(h, handleToU64(ctx.gpuDrawCountBuffer));
    hashCombine64(h, static_cast<uint64_t>(ctx.gpuMaxDraws));
    hashCombine64(h, ctx.terrainEditRevision);
    hashCombine64(h, ctx.terrainMeshRevision);
    return h;
}

uint64_t hashTerrainDrawKey(const VkDrawIndexedIndirectCommand& d, const glm::vec4& o) {
    uint64_t h = 1469598103934665603ull;
    hashCombine64(h, static_cast<uint64_t>(d.indexCount));
    hashCombine64(h, static_cast<uint64_t>(d.firstIndex));
    hashCombine64(h, static_cast<uint64_t>(static_cast<uint32_t>(d.vertexOffset)));
    hashCombine64(h, static_cast<uint64_t>(floatToBits(o.x)));
    hashCombine64(h, static_cast<uint64_t>(floatToBits(o.y)));
    hashCombine64(h, static_cast<uint64_t>(floatToBits(o.z)));
    return h;
}

void hashTerrainDrawSetAdd(uint64_t key,
                           uint64_t& sumA,
                           uint64_t& sumB,
                           uint64_t& xorA) {
    constexpr uint64_t kMulA = 0xbf58476d1ce4e5b9ull;
    constexpr uint64_t kMulB = 0x94d049bb133111ebull;
    sumA += key;
    sumB += rotl64(key * kMulA, 23u) ^ (key * kMulB);
    xorA ^= key + 0x9e3779b97f4a7c15ull;
}

uint64_t finishTerrainDrawSetHash(uint32_t count,
                                  uint64_t sumA,
                                  uint64_t sumB,
                                  uint64_t xorA) {
    uint64_t h = 1469598103934665603ull;
    hashCombine64(h, static_cast<uint64_t>(count));
    hashCombine64(h, sumA);
    hashCombine64(h, sumB);
    hashCombine64(h, xorA);
    return h;
}

uint64_t hashLocalTerrainDraws(const std::vector<VkDrawIndexedIndirectCommand>& draws,
                               const std::vector<glm::vec4>& origins,
                               uint32_t count) {
    uint64_t sumA = 0u;
    uint64_t sumB = 0u;
    uint64_t xorA = 0u;
    for (uint32_t i = 0; i < count; ++i) {
        hashTerrainDrawSetAdd(hashTerrainDrawKey(draws[i], origins[i]), sumA, sumB, xorA);
    }
    return finishTerrainDrawSetHash(count, sumA, sumB, xorA);
}

uint64_t hashSunCascadeVP(const std::array<glm::mat4, ShadowSystem::MAX_SUN_SHADOW_CASCADES>& vps,
                          uint32_t cascadeCount) {
    uint64_t h = 1469598103934665603ull;
    hashCombine64(h, static_cast<uint64_t>(cascadeCount));
    const uint32_t count = std::min<uint32_t>(cascadeCount, ShadowSystem::MAX_SUN_SHADOW_CASCADES);
    for (uint32_t c = 0; c < count; ++c) {
        const glm::mat4& m = vps[c];
        for (int col = 0; col < 4; ++col) {
            for (int row = 0; row < 4; ++row) {
                hashCombine64(h, static_cast<uint64_t>(floatToBits(m[col][row])));
            }
        }
    }
    return h;
}

std::array<uint64_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES>
hashLocalTerrainDrawsPerCascade(
    const std::vector<VkDrawIndexedIndirectCommand>& draws,
    const std::vector<glm::vec4>& origins,
    const std::vector<uint16_t>& cascadeMasks,
    uint32_t count,
    uint32_t cascadeCount) {
    std::array<uint64_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES> hashes{};
    std::array<uint32_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES> counts{};
    std::array<uint64_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES> sumA{};
    std::array<uint64_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES> sumB{};
    std::array<uint64_t, ShadowSystem::MAX_SUN_SHADOW_CASCADES> xorA{};

    const uint32_t evalCount =
        std::min<uint32_t>(cascadeCount, ShadowSystem::MAX_SUN_SHADOW_CASCADES);
    for (uint32_t i = 0; i < count; ++i) {
        const auto& d = draws[i];
        const auto& o = origins[i];
        const uint16_t mask = cascadeMasks[i];
        const uint64_t key = hashTerrainDrawKey(d, o);
        for (uint32_t c = 0; c < evalCount; ++c) {
            if ((mask & static_cast<uint16_t>(1u << c)) == 0u) {
                continue;
            }
            ++counts[c];
            hashTerrainDrawSetAdd(key, sumA[c], sumB[c], xorA[c]);
        }
    }

    for (uint32_t c = 0; c < evalCount; ++c) {
        hashes[c] = finishTerrainDrawSetHash(counts[c], sumA[c], sumB[c], xorA[c]);
        hashCombine64(hashes[c], static_cast<uint64_t>(c));
    }
    return hashes;
}

uint64_t hashSunCascadeLayerVP(const glm::mat4& vp) {
    uint64_t h = 1469598103934665603ull;
    for (int col = 0; col < 4; ++col) {
        for (int row = 0; row < 4; ++row) {
            hashCombine64(h, static_cast<uint64_t>(floatToBits(vp[col][row])));
        }
    }
    return h;
}

} // namespace ShadowSystemInternal

````

## src\rendering\lighting\shadow\ShadowMatrices.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include "world/config/WorldConfig.h"

// GPT-DESC: Builds point-light cube face matrices and directional sun shadow VP matrices.

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#include <glm/gtc/matrix_transform.hpp>

#include <algorithm>
#include <array>
#include <cmath>

std::array<glm::mat4, 6> ShadowSystem::buildPointLightFaceMatrices(const glm::vec3& lightPos,
                                                                    float nearPlane,
                                                                    float farPlane) const {
    const glm::mat4 proj = glm::perspective(glm::radians(90.0f), 1.0f, nearPlane, farPlane);

    const std::array<glm::vec3, 6> dirs = {
        glm::vec3(1.0f, 0.0f, 0.0f),
        glm::vec3(-1.0f, 0.0f, 0.0f),
        glm::vec3(0.0f, 1.0f, 0.0f),
        glm::vec3(0.0f, -1.0f, 0.0f),
        glm::vec3(0.0f, 0.0f, 1.0f),
        glm::vec3(0.0f, 0.0f, -1.0f)
    };
    const std::array<glm::vec3, 6> ups = {
        glm::vec3(0.0f, -1.0f, 0.0f),
        glm::vec3(0.0f, -1.0f, 0.0f),
        glm::vec3(0.0f, 0.0f, 1.0f),
        glm::vec3(0.0f, 0.0f, -1.0f),
        glm::vec3(0.0f, -1.0f, 0.0f),
        glm::vec3(0.0f, -1.0f, 0.0f)
    };

    std::array<glm::mat4, 6> out{};
    for (size_t i = 0; i < 6; ++i) {
        const glm::mat4 view = glm::lookAt(lightPos, lightPos + dirs[i], ups[i]);
        out[i] = proj * view;
    }
    return out;
}

glm::mat4 ShadowSystem::buildSunLightVP(const glm::vec3& cameraPos,
                                          float azimuthDeg,
                                          float elevationDeg,
                                          float halfExtentMeters,
                                          float texelMeters,
                                          bool enableTexelSnap,
                                          bool enableShearQuantization,
                                          glm::vec3* outQuantizedSunDir,
                                          bool isHDCascade) {
    // ─── Sun shadow projection (clean rewrite) ─────────────────────────
    //
    // Design goals:
    //   1. Texel grid is FIXED at one shadow-texel == one light-grid cell
    //      (0.015625 m == VOXEL_SIZE_M / 16). Changing the fade radius does
    //      NOT change the texel size — the shadow ortho footprint is
    //      always runtimeMapSize * texel (derived from budget + texels/voxel).
    //      Circular fade is handled separately in the shader.
    //   2. The shadow center tracks the player with deterministic steps:
    //      camera XZ is snapped to the voxel grid (0.25 m, i.e. 16 texels).
    //   3. NO shear quantization, NO origin hysteresis, NO sinEl clamp.
    //      The continuous mathematical projection is used directly. Pixel
    //      stability comes solely from snapping camera XZ to the texel
    //      grid (which itself is world-axis-aligned and invariant under
    //      sun motion). Realtime celestial motion therefore yields
    //      continuous, single-pixel-step shadow movement without the
    //      whole-map texel jumps that the old shear-quantum produced
    //      whenever the sun crossed a quantum threshold.
    //
    // The two `enable*` flags are kept for source-compatibility but are
    // intentionally ignored: the new projection has no quantization to
    // toggle. `outQuantizedSunDir` returns the exact sun direction used
    // to build the VP so the GLSL receiver push lines up with the
    // rendered depth content.
    (void)enableTexelSnap;
    (void)enableShearQuantization;

    m_prevQuantizedSunShearValid = false;
    m_prevQuantizedSunOriginValid = false;

    // ── Sun direction (towards the ground) ─────────────────────────────
    // Azimuth: 0 = North (+Z), 90 = East (+X), clockwise from above.
    // Elevation: 0 = horizontal, 90 = overhead.
    //
    // SHADOW-LENGTH CAP: clamp elevation so shadows below kMinShadowElevDeg
    // keep the same shear they have at that elevation.
    //
    // We use a UNIFIED 13° floor for ALL cascades. Earlier the HD cascade
    // used a 5° clamp to keep shadows attached to their casters at low sun
    // — but that produced two artifacts in realtime mode:
    //   1. Shadow length kept growing well past sunset (shadows from the
    //      HD ring would stretch ~10× longer than at 13°).
    //   2. Below ~5° the HD ring (clamped at 5°) drew long shadows while
    //      far cascades (clamped at 13°) drew shorter ones, creating a
    //      visible "shadow disappears in 64 m circle and a shorter one
    //      appears beneath it" pop on jumping (the two cascade shears
    //      disagreed by cot(5°) − cot(13°) ≈ 7×).
    // Keeping the floor uniform at 13° makes the shear identical across
    // all active cascades and stops further elongation past 13°.
    const float kMinShadowElevDeg = 13.0f;
    (void)isHDCascade;
    const float clampedElevDeg = std::max(elevationDeg, kMinShadowElevDeg);
    const float azRad = glm::radians(azimuthDeg);
    const float elRad = glm::radians(clampedElevDeg);
    const float cosEl = std::cos(elRad);
    const float sinEl = std::sin(elRad);

    glm::vec3 sunDir(
        -std::sin(azRad) * cosEl,
        -sinEl,
         -std::cos(azRad) * cosEl);
    sunDir = glm::normalize(sunDir);

    // ── Fixed ortho footprint ──────────────────────────────────────────
    // Keep one texel == one light-grid cell (0.015625 m) at all times.
    const float kSunShadowTexelMeters = std::max(texelMeters, 1e-5f);
    const float halfExtent = std::max(halfExtentMeters, kSunShadowTexelMeters);

    // ── Shear factors ──────────────────────────────────────────────────
    // Oblique parallel projection: shadow UV = world XZ + Y * shear, so
    // shadow texels are world-axis-aligned and never rotate with azimuth.
    // |sunDir.y| is guaranteed > sin(0.01°) by the elevation clamp above
    // and the elevation fade keeps the contribution at zero before sinEl
    // becomes small enough to matter, so a hard floor here is not needed.
    const float invSinEl = 1.0f / std::max(-sunDir.y, 1e-4f);
    const float shearX = sunDir.x * invSinEl;
    const float shearZ = sunDir.z * invSinEl;

    // ── Snap shadow ORIGIN to the voxel grid (16 texels = 0.25 m) ─────
    // Stability pivot: the shader UV for a receiver at world Y is
    //
    //   UV = (worldXZ + worldY*shear - origin) / halfExtent
    //
    // so the residual (origin - raw) that stays after snapping is
    // distributed linearly in (worldY - pivotY), where `pivotY` is the
    // Y at which we chose to zero the residual. Previously we snapped
    // `cameraXZ + cameraY*shear` — pivot at Y = 0, residual maximal at
    // the player. On vertical faces that made a single 1° azimuth tick
    // jitter the UV by a Y-dependent amount across the whole face, and
    // wherever that crosses a caster-boundary texel the user saw a
    // horizontal line appear for one frame.
    //
    // ── Texel-grid snap of the shadow ORIGIN ──────────────────────────
    // The shader UV for a receiver at world (Wx,Wy,Wz) is
    //     UV = (Wxz + Wy*shear − origin) / halfExtent
    // For UV stability under arbitrary camera motion (XZ AND Y, e.g. the
    // player jumping), we must snap the camera's PROJECTED position in
    // shadow space — i.e. snap (cameraXZ + cameraY*shear) to the texel
    // grid. Then any sub-texel motion of the camera (in any axis)
    // produces zero UV change for every receiver, and motion past one
    // texel produces exactly a one-texel UV jump for every receiver. No
    // axis becomes a "pivot" that propagates camera-motion as a Y- or
    // azimuth-dependent UV smear.
    //
    // For the HD cascade (first 64 m) the snap is applied in shadow
    // space and the snap step is exactly one shadow texel (and by
    // construction one light-grid cell == 0.015625 m for the default
    // profile), so vertical faces and corner alignment stay perfect
    // across player jumps and continuous sun motion.
    //
    // For far cascades we keep the legacy world-XZ snap (no Y term) so
    // the cascade footprint stays centered around the player horizontally
    // independent of altitude, avoiding the visible cascade-shrink that
    // a large shear*cameraY shift would cause on the wide rings.
    constexpr float kVoxelSizeMeters = 0.25f;
    // Snap step: one shadow texel for the HD cascade (texel-perfect
    // stability), one voxel for far cascades (legacy stable behaviour).
    // Round texelMeters UP to a multiple of the voxel size for far
    // cascades so cross-cascade phase alignment is preserved.
    const float kSnapStepMeters = isHDCascade
        ? kSunShadowTexelMeters
        : std::max(
              kVoxelSizeMeters,
              std::ceil(kSunShadowTexelMeters / kVoxelSizeMeters) * kVoxelSizeMeters);
    auto snapTo = [](float v, float step) {
        return std::floor(v / step + 0.5f) * step;
    };

    float originX;
    float originZ;
    if (isHDCascade) {
        // Snap the projected camera position (shadow-space) to the texel
        // grid. This yields whole-texel UV jumps under any motion.
        const float projCamX = cameraPos.x + cameraPos.y * shearX;
        const float projCamZ = cameraPos.z + cameraPos.y * shearZ;
        originX = snapTo(projCamX, kSnapStepMeters);
        originZ = snapTo(projCamZ, kSnapStepMeters);
    } else {
        originX = snapTo(cameraPos.x, kSnapStepMeters);
        originZ = snapTo(cameraPos.z, kSnapStepMeters);
    }

    // ── Depth range (along sunDir) ─────────────────────────────────────
    // Must contain every caster that can shadow into the ortho footprint
    // plus terrain elevation variation.
    //
    // IMPORTANT: low-elevation sun significantly increases the horizontal
    // caster reach (height * shear). Without accounting for that in depth
    // range, portions of the near ring can drop out at specific elevations
    // because valid caster depths are clipped out of [0,1].
    const float horizontalDepthReach =
        halfExtent * (std::abs(sunDir.x) + std::abs(sunDir.z));
    const float verticalDepthReach =
        WorldConfig::CHUNK_HEIGHT_M * std::abs(sunDir.y);
    const float shearCasterReachX = WorldConfig::CHUNK_HEIGHT_M * std::abs(shearX);
    const float shearCasterReachZ = WorldConfig::CHUNK_HEIGHT_M * std::abs(shearZ);
    float shearDepthReach =
        shearCasterReachX * std::abs(sunDir.x) +
        shearCasterReachZ * std::abs(sunDir.z);
    // Clamp extreme near-horizon amplification to preserve depth precision.
    shearDepthReach = std::min(shearDepthReach, halfExtent * 8.0f);
    const float depthHalfRange = std::max(
        horizontalDepthReach + verticalDepthReach + shearDepthReach + WorldConfig::CHUNK_SIZE_M * 2.0f,
        halfExtent * 2.0f);
    const float depthRange = depthHalfRange * 2.0f;

    // Reference depth at the camera, snapped to the same voxel grid
    // along the sun direction. refDepth is a scalar anchor that shifts
    // compareDepth uniformly across all receivers, so a snap step here
    // only causes a global depth rebase — not a Y-dependent line like
    // the XZ origin did. Keeping the snap bounds depth drift at ½
    // voxel per sun tick without affecting vertical-face UV stability.
    const float refDepthRaw =
        cameraPos.x * sunDir.x +
        cameraPos.y * sunDir.y +
        cameraPos.z * sunDir.z;
    const float refDepth = snapTo(refDepthRaw, kSnapStepMeters);

    // ── Build the VP ───────────────────────────────────────────────────
    // For a world point P:
    //   clip.x = (P.x - originX + P.y * shearX) / halfExtent
    //   clip.y = (P.z - originZ + P.y * shearZ) / halfExtent
    //   clip.z = (dot(P, sunDir) - refDepth + depthHalfRange) / depthRange
    // The constant terms (-originX, -originZ, refDepth) collapse into
    // vp[3]; the per-vertex shear contribution lives on row 1 as before.
    glm::mat4 vp(0.0f);

    vp[0][0] = 1.0f / halfExtent;          // worldX → clip.x
    vp[0][2] = sunDir.x / depthRange;      // worldX → clip.z

    vp[1][0] = shearX / halfExtent;        // worldY → clip.x (shear)
    vp[1][1] = shearZ / halfExtent;        // worldY → clip.y (shear)
    vp[1][2] = sunDir.y / depthRange;      // worldY → clip.z

    vp[2][1] = 1.0f / halfExtent;          // worldZ → clip.y
    vp[2][2] = sunDir.z / depthRange;      // worldZ → clip.z

    vp[3][0] = -originX / halfExtent;
    vp[3][1] = -originZ / halfExtent;
    vp[3][2] = (depthHalfRange - refDepth) / depthRange;
    vp[3][3] = 1.0f;

    if (outQuantizedSunDir) {
        *outQuantizedSunDir = sunDir;
    }

    return vp;
}

````

## src\rendering\lighting\shadow\ShadowPointLights.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include "ShadowInternal.h"
#include "world/World.h"

// GPT-DESC: Records point-light shadow cube-map passes and point-shadow cache reuse.

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>

using namespace ShadowSystemInternal;

void ShadowSystem::recordPointShadowPasses(VkCommandBuffer cmd,
                                          uint32_t imageIndex,
                                          const DrawContext& ctx) {
    const bool haveTimingQueries =
        (m_lightTimingQueryPool != VK_NULL_HANDLE) &&
        (imageIndex < m_lightTimingImageCount) &&
        (imageIndex < m_querySourceByImage.size()) &&
        (imageIndex < m_queryLightCountByImage.size());
    const uint32_t queryBase = imageIndex * (MAX_POINT_SHADOW_LIGHTS * 2u);

    if (haveTimingQueries) {
        vkCmdResetQueryPool(cmd, m_lightTimingQueryPool, queryBase, MAX_POINT_SHADOW_LIGHTS * 2u);
        m_queryLightCountByImage[imageIndex] = 0u;
        m_querySourceByImage[imageIndex].fill(kInvalidSourceIndex);
    }


    using Clock = std::chrono::high_resolution_clock;

    // ═══════════════════════════════════════════════════════════════════
    // Point light shadow passes
    // ═══════════════════════════════════════════════════════════════════

    if (m_activeLights.empty()) {
        return;
    }

    VkViewport viewport{};
    viewport.x = 0.0f;
    viewport.y = 0.0f;
    viewport.width = static_cast<float>(POINT_SHADOW_MAP_SIZE);
    viewport.height = static_cast<float>(POINT_SHADOW_MAP_SIZE);
    viewport.minDepth = 0.0f;
    viewport.maxDepth = 1.0f;

    VkRect2D scissor{};
    scissor.offset = {0, 0};
    scissor.extent = {POINT_SHADOW_MAP_SIZE, POINT_SHADOW_MAP_SIZE};

    VkClearValue clearValue{};
    clearValue.depthStencil = {1.0f, 0};

    VkDeviceSize zeroOffset = 0;

    const uint32_t activeCount = std::min<uint32_t>(
        static_cast<uint32_t>(m_activeLights.size()),
        MAX_POINT_SHADOW_LIGHTS);
    if (haveTimingQueries) {
        m_queryLightCountByImage[imageIndex] = activeCount;
    }
    const uint64_t perLightShadowTexels =
        static_cast<uint64_t>(kPointShadowFacesPerLight) *
        static_cast<uint64_t>(POINT_SHADOW_MAP_SIZE) *
        static_cast<uint64_t>(POINT_SHADOW_MAP_SIZE);
    const uint64_t terrainSignature = hashTerrainDrawContext(ctx);
    const bool useLocalSphereTerrain = (ctx.world != nullptr);
    const uint32_t localDrawCapacity = useLocalSphereTerrain
        ? std::max<uint32_t>(ctx.gpuMaxDraws, std::max<uint32_t>(ctx.indirectDrawCount, 65536u))
        : 0u;
    if (useLocalSphereTerrain) {
        if (m_localTerrainDrawScratch.size() < localDrawCapacity) {
            m_localTerrainDrawScratch.resize(localDrawCapacity);
            m_localTerrainOriginScratch.resize(localDrawCapacity);
        }
    }
    m_frameDiagnostics.totalRenderedFaces = 0u;
    m_frameDiagnostics.totalShadowMapTexelsRendered = 0u;

    for (uint32_t lightSlot = 0; lightSlot < activeCount; ++lightSlot) {
        const ActiveLight& light = m_activeLights[lightSlot];
        const auto lightCpuStart = Clock::now();
        if (haveTimingQueries) {
            m_querySourceByImage[imageIndex][lightSlot] = light.sourceIndex;
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, m_lightTimingQueryPool,
                                queryBase + lightSlot * 2u);
        }

        uint32_t localTerrainDrawCount = 0u;
        float terrainGatherMs = 0.0f;
        uint64_t lightTerrainSignature = terrainSignature;
        bool reusedFromCache = false;
        if (lightSlot < m_slotShadowCache.size()) {
            const auto& cache = m_slotShadowCache[lightSlot];
            const bool allowReuseThisFrame = (!ctx.useGPUCulling) || useLocalSphereTerrain;
            reusedFromCache =
                allowReuseThisFrame &&
                cache.valid &&
                cache.sourceIndex == light.sourceIndex &&
                nearlyEqualVec3(cache.lightPosition, light.position) &&
                nearlyEqual(cache.nearPlane, light.nearPlane) &&
                nearlyEqual(cache.farPlane, light.farPlane) &&
                cache.terrainSignature == lightTerrainSignature;
        }
        if (!reusedFromCache &&
            useLocalSphereTerrain &&
            !m_localTerrainDrawScratch.empty() &&
            !m_localTerrainOriginScratch.empty()) {
            const auto terrainGatherStart = Clock::now();
            localTerrainDrawCount = ctx.world->gatherDrawCommandsInSphere(
                light.position,
                light.farPlane,
                m_localTerrainDrawScratch.data(),
                m_localTerrainOriginScratch.data(),
                localDrawCapacity,
                ctx.uploadTimelineValue);
            terrainGatherMs = std::chrono::duration<float, std::milli>(
                Clock::now() - terrainGatherStart).count();
        }
        if (light.sourceIndex < m_lightDiagnostics.size()) {
            auto& diag = m_lightDiagnostics[light.sourceIndex];
            diag.cpuTerrainGatherMs = terrainGatherMs;
            diag.usedLocalTerrainCulling = useLocalSphereTerrain;
            diag.localTerrainDrawCount = localTerrainDrawCount;
            diag.localTerrainDrawCapacity = localDrawCapacity;
        }
        if (!reusedFromCache) {
            const auto faceVP = buildPointLightFaceMatrices(light.position, light.nearPlane, light.farPlane);
            for (uint32_t face = 0; face < kPointShadowFacesPerLight; ++face) {
                const uint32_t layer = lightSlot * kPointShadowFacesPerLight + face;

                VkRenderPassBeginInfo rpBegin{};
                rpBegin.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
                rpBegin.renderPass = m_shadowRenderPass;
                rpBegin.framebuffer = m_shadowFramebuffers[layer];
                rpBegin.renderArea.offset = {0, 0};
                rpBegin.renderArea.extent = {POINT_SHADOW_MAP_SIZE, POINT_SHADOW_MAP_SIZE};
                rpBegin.clearValueCount = 1;
                rpBegin.pClearValues = &clearValue;

                vkCmdBeginRenderPass(cmd, &rpBegin, VK_SUBPASS_CONTENTS_INLINE);
                vkCmdSetViewport(cmd, 0, 1, &viewport);
                vkCmdSetScissor(cmd, 0, 1, &scissor);
                vkCmdSetDepthBias(cmd, 1.35f, 0.0f, 1.75f);

                ShadowPushConstants push{};
                push.lightVP = faceVP[face];
                push.lightPosFar = glm::vec4(light.position, light.farPlane);
                push.terrainChunkCoordMode = glm::vec4(0.0f);

                if (ctx.terrainDescriptorSet != VK_NULL_HANDLE &&
                    ctx.terrainVertexBuffer != VK_NULL_HANDLE &&
                    ctx.terrainIndexBuffer != VK_NULL_HANDLE) {

                    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_terrainShadowPipeline);
                    vkCmdBindDescriptorSets(
                        cmd,
                        VK_PIPELINE_BIND_POINT_GRAPHICS,
                        m_terrainShadowPipelineLayout,
                        0,
                        1,
                        &ctx.terrainDescriptorSet,
                        0,
                        nullptr);

                    vkCmdBindVertexBuffers(cmd, 0, 1, &ctx.terrainVertexBuffer, &zeroOffset);
                    vkCmdBindIndexBuffer(cmd, ctx.terrainIndexBuffer, 0, VK_INDEX_TYPE_UINT16);

                    if (useLocalSphereTerrain) {
                        for (uint32_t drawIndex = 0; drawIndex < localTerrainDrawCount; ++drawIndex) {
                            const auto& terrainDraw = m_localTerrainDrawScratch[drawIndex];
                            const glm::vec4& chunkOrigin = m_localTerrainOriginScratch[drawIndex];
                            push.terrainChunkCoordMode = glm::vec4(
                                chunkOrigin.x,
                                chunkOrigin.y,
                                chunkOrigin.z,
                                1.0f);
                            vkCmdPushConstants(
                                cmd,
                                m_terrainShadowPipelineLayout,
                                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                                0,
                                sizeof(ShadowPushConstants),
                                &push);
                            vkCmdDrawIndexed(
                                cmd,
                                terrainDraw.indexCount,
                                1u,
                                terrainDraw.firstIndex,
                                terrainDraw.vertexOffset,
                                0u);
                        }
                    } else if (ctx.useGPUCulling &&
                        ctx.gpuVisibleDrawsBuffer != VK_NULL_HANDLE &&
                        ctx.gpuDrawCountBuffer != VK_NULL_HANDLE &&
                        ctx.gpuMaxDraws > 0) {
                        // Phase D: signal point_shadow_terrain.vert to read from
                        // bindless ChunkOrigins[1] (GPU visible-origins) instead of slot 0.
                        push.terrainChunkCoordMode = glm::vec4(0.0f, 0.0f, 0.0f, 2.0f);
                        vkCmdPushConstants(
                            cmd,
                            m_terrainShadowPipelineLayout,
                            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                            0,
                            sizeof(ShadowPushConstants),
                            &push);
                        vkCmdDrawIndexedIndirectCount(
                            cmd,
                            ctx.gpuVisibleDrawsBuffer,
                            0,
                            ctx.gpuDrawCountBuffer,
                            0,
                            ctx.gpuMaxDraws,
                            sizeof(VkDrawIndexedIndirectCommand));
                    } else if (ctx.indirectBuffer != VK_NULL_HANDLE && ctx.indirectDrawCount > 0) {
                        vkCmdPushConstants(
                            cmd,
                            m_terrainShadowPipelineLayout,
                            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                            0,
                            sizeof(ShadowPushConstants),
                            &push);
                        vkCmdDrawIndexedIndirect(
                            cmd,
                            ctx.indirectBuffer,
                            0,
                            ctx.indirectDrawCount,
                            sizeof(VkDrawIndexedIndirectCommand));
                    }
                }

                vkCmdEndRenderPass(cmd);
            }

            m_frameDiagnostics.totalRenderedFaces += kPointShadowFacesPerLight;
            m_frameDiagnostics.totalShadowMapTexelsRendered += perLightShadowTexels;

            if (lightSlot < m_slotShadowCache.size()) {
                auto& cache = m_slotShadowCache[lightSlot];
                cache.valid = true;
                cache.sourceIndex = light.sourceIndex;
                cache.lightPosition = light.position;
                cache.nearPlane = light.nearPlane;
                cache.farPlane = light.farPlane;
                cache.terrainSignature = lightTerrainSignature;
            }
        }

        if (haveTimingQueries) {
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, m_lightTimingQueryPool,
                                queryBase + lightSlot * 2u + 1u);
        }

        const float lightCpuMs = std::chrono::duration<float, std::milli>(Clock::now() - lightCpuStart).count();
        if (light.sourceIndex < m_lightDiagnostics.size()) {
            auto& diag = m_lightDiagnostics[light.sourceIndex];
            diag.reusedShadowCache = reusedFromCache;
            diag.renderedFaces = reusedFromCache ? 0u : kPointShadowFacesPerLight;
            diag.reusedFaces = reusedFromCache ? kPointShadowFacesPerLight : 0u;
            diag.shadowMapTexelsRendered = reusedFromCache ? 0u : perLightShadowTexels;
            diag.cpuCommandRecordMs = std::max(0.0f, lightCpuMs - terrainGatherMs);
            diag.cpuTotalMs += diag.cpuTerrainGatherMs + diag.cpuCommandRecordMs;
        }
    }

    VkImageMemoryBarrier2 barrier{};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    barrier.srcStageMask = VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
    barrier.srcAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
    barrier.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
    barrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier.oldLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    barrier.newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.image = m_pointShadowImage;
    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    barrier.subresourceRange.baseMipLevel = 0;
    barrier.subresourceRange.levelCount = 1;
    barrier.subresourceRange.baseArrayLayer = 0;
    barrier.subresourceRange.layerCount = MAX_POINT_SHADOW_LIGHTS * 6;

    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.imageMemoryBarrierCount = 1;
    depInfo.pImageMemoryBarriers = &barrier;
    vkCmdPipelineBarrier2(cmd, &depInfo);
}

````

## include\rendering\lighting\ShadowSystem.h

Description: No CC-DESC found. C++ struct 'LightingSettings'.

````cpp
#pragma once

// GPT-DESC: Declares the ShadowSystem facade, diagnostics, resources, and private pass split points.

#include <vulkan/vulkan.h>
#include <glm/glm.hpp>
#include <array>
#include <chrono>
#include <cstdint>
#include <deque>
#include <vector>

struct LightingSettings;
struct PointLight;
class ObjectManager;
class TimeManager;
class World;

namespace TerrainEdit { class HeightmapBaseSampler; }

class ShadowSystem {
public:
    static constexpr uint32_t MAX_POINT_SHADOW_LIGHTS = 32;
    static constexpr uint32_t MAX_SUN_SHADOW_CASCADES = 6;

    // Sun shadow patch simulator: a 13×13 voxel patch around the camera
    // (each voxel = 16×16 light cells, 0.015625 m each), with a virtual
    // 1-voxel cube at the centre. Its top face shears onto the ground;
    // the integer light-cell offset of the shadow stamp is logged each
    // time it changes so motion vs. quantization jumps are directly
    // visible.
    static constexpr uint32_t SUN_DEBUG_GRID_VOXELS_PER_SIDE = 13;
    static constexpr uint32_t SUN_DEBUG_CELLS_PER_VOXEL = 16;
    static constexpr uint32_t SUN_DEBUG_GRID_CELLS_PER_SIDE =
        SUN_DEBUG_GRID_VOXELS_PER_SIDE * SUN_DEBUG_CELLS_PER_VOXEL; // 208
    static constexpr uint32_t SUN_DEBUG_LOG_CAPACITY = 256;

    // Which celestial body is currently casting directional shadows
    enum class ActiveCelestial { None, Sun, Moon };

    // Directional (sun/moon) light parameters.
    // Exposed in the DirectionalShadowWindow debug panel.
    struct DirectionalShadowConfig {
        // Sun
        float azimuth{225.0f};          // Degrees, 0=North, clockwise
        float elevation{45.0f};         // Degrees above horizon (0=horizontal, 90=overhead)
        float shadowAreaRadius{32.0f};  // Meters from camera where sun shadows fade out
        float shadowIntensity{0.75f};   // 0=no shadow, 1=fully dark
        float sunShadowBudgetMB{256.0f}; // Target depth-memory budget for directional shadow map
        float sunTexelsPerVoxel{16.0f};  // 16=legacy, 32=2x detail, 8=2x distance
        uint32_t sunCascadeCount{4};     // Number of directional cascades
        float sunCascadeScale{4.0f};     // Range multiplier between cascades

        // Moon
        float moonAzimuth{45.0f};       // Degrees, 0=North, clockwise
        float moonElevation{30.0f};     // Degrees above horizon
        float moonShadowIntensity{0.20f}; // Weaker than sun

        // Realtime mode: derive sun/moon azimuth+elevation from TimeManager
        bool useRealtimeCelestials{false};
        bool realtimeTracksAzimuth{true};   // When false, keep manual azimuth while realtime is on
        bool realtimeTracksElevation{true}; // When false, keep manual elevation while realtime is on
        bool smoothDirectionalMotion{true}; // Disable shear quantization while sun/moon angles are changing

        int   debugMode{0};             // 0=off, 1=UV, 2=bilinear f, 3=raw texels, 4=coverage, 5=grid, 6=depth

        // ── In-world cascade visualization (debug overlay) ──
        bool  showCascadeOverlay{false};      // Master toggle
        bool  cascadeOverlayWireOnly{false};  // Skip translucent face fills
        bool  cascadeOverlayShowFade{true};   // Draw shadowAreaRadius circle
        bool  cascadeOverlayShowSunDir{true}; // Draw sun direction line
        bool  cascadeOverlayShowLabels{true}; // Per-cascade text label
        float cascadeOverlayAlphaScale{1.0f}; // Multiplier on overlay opacity
    };

    // Runtime directional-shadow profile derived from budget + texel density.
    struct SunShadowRuntimeConfig {
        uint32_t mapSize{4096};         // Per-cascade depth-map dimension (NxN)
        uint32_t cascadeCount{4};       // Number of active cascades
        float cascadeScale{4.0f};       // Range multiplier between cascades
        float halfExtentMeters{32.0f};  // Cascade 0 cast range from camera center
        float maxCastRadiusMeters{2048.0f}; // Last-cascade cast range from camera center
        float texelMeters{0.015625f};   // World-space size of one shadow texel
        float texelsPerVoxel{16.0f};    // texelDensity = voxelSize / texelMeters
        float budgetMB{256.0f};         // Requested budget
        float actualMemoryMB{256.0f};   // Real memory for all cascades
        float perCascadeMemoryMB{64.0f};// Real memory per cascade
    };

    struct ShadowGPUData {
        std::array<glm::mat4, MAX_SUN_SHADOW_CASCADES> sunLightVP{};
        // x=halfExtent, y=texelMeters, z=reserved, w=reserved
        std::array<glm::vec4, MAX_SUN_SHADOW_CASCADES> sunCascadeParams{};
        glm::vec4 sunDirTexelSize{0.0f};  // xyz=sun direction (towards ground), w=world-space texel footprint
        glm::vec4 shadowConfig{0.0f};  // x=sunEnabled, y=pointEnabled, z=sunMapSize, w=pointMapSize
        glm::vec4 shadowConfig2{0.0f}; // x=sunCascadeCount, y=cascadeBlendFrac, z=maxCastRadius, w=reserved
        std::array<glm::vec4, MAX_POINT_SHADOW_LIGHTS> pointShadowInfo{};  // x=near, y=far, z=radius, w=enabled
        // x=pointShadowSamples, y=lightEvalFragments, z=fullyOccludedFragments, w=litContribFragments
        std::array<glm::uvec4, MAX_POINT_SHADOW_LIGHTS> pointShadowDiag{};
        glm::vec4 diagConfig{0.0f};  // x=enableDetailedDiagnostics, y=debugMode, z=sunAreaRadius, w=activeElevationFade
        // Sky enclosure ("sun cannot reach" deep-pocket darkening) parameters.
        // x=intensity, y=minAmbient, z=probeMaxHeight (m), w=mode (0=off, 1=on, 2=on+visualize)
        glm::vec4 skyEnclosureParams{0.0f};
        // Sky-vis static heightmap (uploaded once from HeightmapBaseSampler).
        // x=worldOriginXMeters, y=worldOriginZMeters,
        // z=metersPerTexel (assumes square coverage),
        // w=valueScaleToWorldYMeters (heightmap stores voxel counts → multiply by 0.25)
        glm::vec4 skyHeightmapInfo{0.0f};
    };

    // Tunables for the sky-enclosure ambient darkening pass (sky_enclosure.glsl).
    // Exposed in the SkyEnclosureWindow debug panel.
    struct SkyEnclosureSettings {
        bool  enabled{true};        // master toggle
        bool  visualize{false};     // false=apply darkening, true=heatmap output (debug)
        float intensity{1.0f};      // 0..4, sharpens the response curve
        float minAmbient{0.18f};    // 0..1, ambient floor in fully enclosed pixels
        float probeMaxHeight{12.0f};// 0.5..32 meters, top of vertical CSM probe column
        float cavityGain{0.6f};     // 0..1, weight of sun-independent Y-cavity term
    };

    struct DrawContext {
        VkDescriptorSet terrainDescriptorSet{VK_NULL_HANDLE};
        VkBuffer terrainVertexBuffer{VK_NULL_HANDLE};
        VkBuffer terrainIndexBuffer{VK_NULL_HANDLE};
        VkBuffer indirectBuffer{VK_NULL_HANDLE};
        uint32_t indirectDrawCount{0};
        World* world{nullptr};
        uint64_t uploadTimelineValue{0u};
        uint64_t terrainEditRevision{0u};
        uint64_t terrainMeshRevision{0u};

        bool useGPUCulling{false};
        VkBuffer gpuVisibleDrawsBuffer{VK_NULL_HANDLE};
        VkBuffer gpuDrawCountBuffer{VK_NULL_HANDLE};
        uint32_t gpuMaxDraws{0};

        const ObjectManager* objectManager{nullptr};
    };

    // Per-source-light diagnostic data (indexed by LightingSettings::pointLights index).
    struct LightDiagnostics {
        static constexpr uint32_t CULL_NONE = 0u;
        static constexpr uint32_t CULL_CASTS_SHADOW_DISABLED = 1u << 0;
        static constexpr uint32_t CULL_INVALID_RADIUS_OR_INTENSITY = 1u << 1;
        static constexpr uint32_t CULL_BEHIND_CAMERA = 1u << 2;
        static constexpr uint32_t CULL_LOW_IRRADIANCE = 1u << 3;
        static constexpr uint32_t CULL_OUTSIDE_BUDGET = 1u << 4;

        uint32_t sourceLightIndex{0};
        bool castsShadow{false};
        bool eligibleForShadow{false};
        bool selectedForShadow{false};
        bool reusedShadowCache{false};
        bool usedLocalTerrainCulling{false};
        uint32_t cullMask{CULL_NONE};
        uint32_t selectionRank{0};
        uint32_t eligibleCountThisFrame{0};
        uint32_t selectedCountThisFrame{0};
        uint32_t selectionBudgetThisFrame{0};
        float selectionScore{0.0f};
        float irradianceAtCamera{0.0f};
        float facingDot{0.0f};
        uint32_t renderedFaces{0};
        uint32_t reusedFaces{0};
        uint32_t localTerrainDrawCount{0};
        uint32_t localTerrainDrawCapacity{0};
        float distanceToCamera{0.0f};
        float effectiveRadius{0.0f};
        float effectiveIntensity{0.0f};
        uint32_t shadowMapSize{0};
        float shadowNearPlane{0.0f};
        float shadowFarPlane{0.0f};
        uint64_t shadowMapTexelsRendered{0};
        float cpuSelectionShareMs{0.0f};
        float cpuTerrainGatherMs{0.0f};
        float cpuCommandRecordMs{0.0f};
        float cpuTotalMs{0.0f};
        float gpuShadowMs{0.0f};
        float gpuShadowMsAvg{0.0f};
        uint32_t pointShadowSamples{0};
        uint32_t pointLightEvaluations{0};
        uint32_t pointShadowFullyOccluded{0};
        uint32_t pointShadowLitContrib{0};
        uint64_t estShadowDepthCompareOps{0};
        float shadowOccludedRatio{0.0f};
        float shadowLitRatio{0.0f};
        float evalToSampleRatio{0.0f};
        float gpuMsPerMegaShadowSample{0.0f};
        float terrainPassGpuMs{0.0f};
        float estTerrainLightingShareMs{0.0f};
        float estShadowSamplingMs{0.0f};
        float estShadowOffDeltaMs{0.0f};
        float estLightOffDeltaMs{0.0f};
    };

    struct FrameDiagnostics {
        uint32_t shadowLightBudget{0};
        uint32_t eligibleShadowLights{0};
        uint32_t selectedShadowLights{0};
        uint32_t totalRenderedFaces{0};
        uint32_t pointShadowMapSize{0};
        uint64_t totalShadowMapTexelsRendered{0};
        float terrainPassGpuMs{0.0f};
        float totalShadowGpuMs{0.0f};
        float avgShadowGpuMsPerLight{0.0f};
        float terrainMsPerMegaShadowSample{0.0f};
        uint64_t totalPointShadowSamples{0};
        uint64_t totalPointLightEvaluations{0};
        uint64_t totalPointShadowFullyOccluded{0};
        uint64_t totalPointShadowLitContrib{0};
        uint64_t totalEstimatedShadowDepthCompareOps{0};
        bool detailedCountersEnabled{false};
    };

    // ── Sun shadow patch simulator snapshot ─────────────────────────
    // Logged only when the integer light-cell offset of the central
    // voxel's shadow stamp changes (or shadowActive flips). One row per
    // visible shadow step on the 16-cells-per-voxel light grid.
    struct SunShadowDebugSnapshot {
        uint64_t frameNumber{0};
        float azimuth{0.0f};               // active azimuth (degrees)
        float elevation{0.0f};             // active elevation (degrees)
        glm::vec3 sunDir{0.0f, -1.0f, 0.0f};
        // shearX/shearZ = sunDir.xz / |sunDir.y|: meters of XZ travel per
        // meter of height for the projected shadow.
        float shearX{0.0f};
        float shearZ{0.0f};
        // Tip light-cell offset of the shadow stamp from the central
        // voxel's footprint, in 0.015625 m cells. Computed as
        //   floor(SUN_DEBUG_CELLS_PER_VOXEL * shear).
        // Range typically [-104, +104] inside the 13-voxel patch.
        int32_t tipCellX{0};
        int32_t tipCellZ{0};
        // Delta in light cells from the previously logged snapshot.
        // |dCell| == 1 = smooth single-pixel step; |dCell| > 1 = jump.
        int32_t dCellX{0};
        int32_t dCellZ{0};
        bool shadowActive{false};
    };

    // ── In-game shadow texel-grid event (per actual VP rebuild) ────
    // Logged whenever the actual rendered shadow map changes between
    // frames — i.e. the cache missed and a new VP was used. Captures
    // exactly what the in-game shadow does on the texel grid, in
    // contrast to SunShadowDebugSnapshot which logs the ideal
    // mathematical patch-simulator shadow tip.
    //
    // If pixels visibly flicker on the edge but |dTexelX|, |dTexelY|
    // stay within ±1 between rendered frames, the issue is shader-side
    // dither / sampling, not VP wobble. Conversely, frequent multi-
    // texel jumps (or many cache misses per second) indicate residual
    // VP instability in buildSunLightVP / angle quantization.
    struct SunShadowTexelEvent {
        uint64_t frameNumber{0};
        uint32_t framesSincePrev{0};       // frames since last logged event
        bool cacheHit{false};              // false = re-rendered this frame
        // Bitmask of reasons this event was logged. Lets the UI count
        // how many entries are real shadow motion (TexelXY) vs noise
        // (depth jitter, bit-level sunDir churn, cache flips with no
        // visible change).
        //   0x01 ReasonCacheFlip   — cacheHit toggled vs previous event
        //   0x02 ReasonTexelX      — |dTexelX| >= 1 (visible XY motion)
        //   0x04 ReasonTexelY      — |dTexelY| >= 1
        //   0x08 ReasonDepth       — |dTexelDepth| > 0.5 depth steps
        //   0x10 ReasonSunDirBits  — sunDir bit-pattern changed
        //   0x20 ReasonSubTexelXY  — 0 < |dTexelX|/|dTexelY| < 1
        //                            (sub-texel VP wobble, invisible
        //                             on its own but invalidates cache)
        uint32_t reasonMask{0};
        float rawAzimuth{0.0f};            // pre-quantization angle (deg)
        float rawElevation{0.0f};
        float snappedAzimuth{0.0f};        // angle actually fed to VP build
        float snappedElevation{0.0f};
        glm::vec3 sunDir{0.0f, -1.0f, 0.0f};
        // VP origin in shadow-texel coordinates: vp[3][0..1] * mapSize/2.
        // One unit = exactly one shadow texel (= 0.015625 m on the ground).
        float texelOriginX{0.0f};
        float texelOriginY{0.0f};
        float texelDepth{0.0f};            // vp[3][2] * 65535 (depth steps)
        // Delta from the previously logged event in shadow texels.
        float dTexelX{0.0f};
        float dTexelY{0.0f};
        float dTexelDepth{0.0f};
        // Raw shear factors (sunDir.xz / |sunDir.y|). Useful for
        // confirming whether shear is actually changing per frame.
        float shearX{0.0f};
        float shearZ{0.0f};
    };
    static constexpr uint32_t SUN_TEXEL_EVENT_REASON_CACHE_FLIP   = 0x01u;
    static constexpr uint32_t SUN_TEXEL_EVENT_REASON_TEXEL_X      = 0x02u;
    static constexpr uint32_t SUN_TEXEL_EVENT_REASON_TEXEL_Y      = 0x04u;
    static constexpr uint32_t SUN_TEXEL_EVENT_REASON_DEPTH        = 0x08u;
    static constexpr uint32_t SUN_TEXEL_EVENT_REASON_SUNDIR_BITS  = 0x10u;
    static constexpr uint32_t SUN_TEXEL_EVENT_REASON_SUBTEXEL_XY  = 0x20u;
    // Convenience mask: any bit here means the sun ITSELF changed
    // (snapped angles or sun-direction bits). Anything OUTSIDE this
    // mask is movement caused purely by the camera shifting the shadow
    // map, which is normal behaviour and not the source of the
    // realtime-celestial flicker the user is investigating.
    static constexpr uint32_t SUN_TEXEL_EVENT_SUN_CHANGE_MASK =
        SUN_TEXEL_EVENT_REASON_SUNDIR_BITS;
    static constexpr uint32_t SUN_TEXEL_LOG_CAPACITY = 512;

    static constexpr uint32_t SUN_CACHE_MISS_INVALID          = 0x0001u;
    static constexpr uint32_t SUN_CACHE_MISS_CONTEXT          = 0x0002u;
    static constexpr uint32_t SUN_CACHE_MISS_SUN_DIR          = 0x0004u;
    static constexpr uint32_t SUN_CACHE_MISS_CASCADE_COUNT    = 0x0008u;
    static constexpr uint32_t SUN_CACHE_MISS_VP_HASH          = 0x0010u;
    static constexpr uint32_t SUN_CACHE_MISS_TERRAIN_SIG      = 0x0020u;
    static constexpr uint32_t SUN_CACHE_MISS_CAMERA_CHUNK     = 0x0040u;
    static constexpr uint32_t SUN_CACHE_MISS_UPLOAD_TIMELINE  = 0x0080u;
    static constexpr uint32_t SUN_CACHE_MISS_CASCADE_EXTENTS  = 0x0100u;
    static constexpr uint32_t SUN_CACHE_MISS_MESH_REVISION    = 0x0200u;

    // ── Per-frame sun shadow timing sample ──────────────────────────
    struct SunShadowFrameSample {
        float cpuVpComputeMs{0.0f};       // buildSunLightVP + direction math
        float cpuTerrainGatherMs{0.0f};   // cache checks + world gather + terrain hash
        float cpuWorldGatherMs{0.0f};     // World::gatherDrawCommandsForSunCascades
        float cpuGatherStateScanMs{0.0f};
        float cpuGatherSortMs{0.0f};
        float cpuGatherRegistryWalkMs{0.0f};
        float cpuTerrainHashMs{0.0f};     // hash gathered draw/origin arrays
        float cpuCacheDecisionMs{0.0f};   // final render-cache comparison
        float cpuIndirectBuildMs{0.0f};   // pack sun-local indirect/origin upload data
        float cpuIndirectUploadRecordMs{0.0f}; // vkCmdCopyBuffer + barriers for local indirect data
        float cpuCommandRecordMs{0.0f};   // render pass + draw call recording
        float cpuBarrierMs{0.0f};         // sun depth write -> sampled barrier
        float cpuTotalMs{0.0f};           // sum of all CPU phases
        float gpuRenderMs{0.0f};          // GPU shadow render pass (timestamp delta)
        uint32_t drawCallCount{0};        // terrain draw commands consumed by the GPU
        uint32_t apiDrawCallCount{0};     // Vulkan draw commands recorded
        uint32_t terrainChunksGathered{0};// legacy name: gathered draw commands
        uint32_t terrainDrawsGathered{0}; // unique local draw commands gathered
        uint32_t localDrawCapacity{0};
        uint32_t activeCascadeCount{0};
        uint32_t configuredCascadeCount{0};
        uint32_t mapSize{0};
        uint32_t loadedChunkMapSize{0};
        uint32_t bboxCandidateChunks{0};
        uint32_t visitedCandidateChunks{0};
        uint32_t acceptedChunkCount{0};
        uint32_t invalidEntityRejects{0};
        uint32_t missingComponentRejects{0};
        uint32_t invisibleRejects{0};
        uint32_t notReadyRejects{0};
        uint32_t invalidMeshRejects{0};
        uint32_t emptyMeshRejects{0};
        uint32_t uploadPendingRejects{0};
        uint32_t cascadeCullRejects{0};
        uint32_t cascadeInnerCullRejects{0};
        bool gatherTruncated{false};
        bool sunActive{false};
        bool usedLocalTerrainGather{false};
        bool renderCachePrecheckHit{false};
        bool gatherCacheHit{false};
        bool renderCacheHit{false};
        uint32_t renderCachePrecheckMissMask{0};
        uint32_t gatherCacheMissMask{0};
        uint32_t renderCacheMissMask{0};
        uint32_t renderCachePrecheckMeshChangeMask{0};
        uint32_t gatherCacheMeshChangeMask{0};
        uint32_t renderCacheMeshChangeMask{0};
        bool renderCachePrecheckMeshChangesReliable{true};
        bool gatherCacheMeshChangesReliable{true};
        bool renderCacheMeshChangesReliable{true};
        uint32_t cascadeRenderMask{0};
        uint32_t cascadeReuseMask{0};
        uint32_t cascadeFullRenderMask{0};
        uint32_t cascadeScrollMask{0};
        uint32_t cascadesRendered{0};
        uint32_t cascadesReused{0};
        uint32_t cascadesFullRendered{0};
        uint32_t cascadesScrolled{0};
        uint64_t scrollCopiedTexels{0};
        uint64_t scrollDirtyTexels{0};
        bool wasReused{false};            // true if cache hit (no re-render)
        ActiveCelestial activeCelestial{ActiveCelestial::None};
        float effectiveShadowIntensity{0.0f};
        float activeElevationFade{0.0f};
        float rawAzimuth{0.0f};
        float rawElevation{0.0f};
        float activeAzimuth{0.0f};
        float activeElevation{0.0f};
        glm::vec3 sunDir{0.0f, -1.0f, 0.0f};
        glm::vec3 cameraPos{0.0f};
        glm::ivec3 cameraChunk{0};
        uint64_t ctxHash{0u};
        uint64_t vpHash{0u};
        uint64_t terrainSignature{0u};
        uint64_t uploadTimeline{0u};
        uint64_t terrainMeshRevision{0u};
        float shadowAreaRadius{0.0f};
        float runtimeHalfExtentMeters{0.0f};
        float maxCastRadiusMeters{0.0f};
        float runtimeTexelMeters{0.0f};
        float runtimeTexelsPerVoxel{0.0f};
        float runtimeActualMemoryMB{0.0f};
        float runtimePerCascadeMemoryMB{0.0f};
        float gatherMaxHalfExtent{0.0f};
        float gatherSinElevation{0.0f};
        float gatherShearX{0.0f};
        float gatherShearZ{0.0f};
        float gatherShearMax{0.0f};
        float gatherCasterReach{0.0f};
        float gatherPadding{0.0f};
        float gatherHalfX{0.0f};
        float gatherHalfZ{0.0f};
        glm::ivec3 gatherMinChunk{0};
        glm::ivec3 gatherMaxChunk{0};
        std::array<float, MAX_SUN_SHADOW_CASCADES> cascadeHalfExtents{};
        std::array<float, MAX_SUN_SHADOW_CASCADES> cascadeTexelMeters{};
        std::array<float, MAX_SUN_SHADOW_CASCADES> cascadeRecordMs{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeGatherChunkHits{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeGatherDrawHits{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeIssuedDrawCalls{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeApiDrawCalls{};
        std::array<int32_t, MAX_SUN_SHADOW_CASCADES> cascadeScrollDxTexels{};
        std::array<int32_t, MAX_SUN_SHADOW_CASCADES> cascadeScrollDyTexels{};
        std::array<uint64_t, MAX_SUN_SHADOW_CASCADES> cascadeScrollDirtyTexels{};
    };

    // ── Rolling-window aggregated sun shadow diagnostics ────────────
    struct SunShadowDiagnostics {
        // Latest single-frame values
        SunShadowFrameSample latest{};

        // Rolling window stats (configurable, default 20s)
        float windowSeconds{20.0f};
        uint32_t sampleCount{0};

        // CPU averages over window
        float avgCpuVpComputeMs{0.0f};
        float avgCpuTerrainGatherMs{0.0f};
        float avgCpuWorldGatherMs{0.0f};
        float avgCpuTerrainHashMs{0.0f};
        float avgCpuCacheDecisionMs{0.0f};
        float avgCpuCommandRecordMs{0.0f};
        float avgCpuTotalMs{0.0f};

        // CPU peaks over window
        float maxCpuVpComputeMs{0.0f};
        float maxCpuTerrainGatherMs{0.0f};
        float maxCpuWorldGatherMs{0.0f};
        float maxCpuTerrainHashMs{0.0f};
        float maxCpuCacheDecisionMs{0.0f};
        float maxCpuCommandRecordMs{0.0f};
        float maxCpuTotalMs{0.0f};

        // GPU averages over window
        float avgGpuRenderMs{0.0f};
        float maxGpuRenderMs{0.0f};
        float minGpuRenderMs{0.0f};

        // Draw call stats over window
        float avgDrawCallCount{0.0f};
        float avgApiDrawCallCount{0.0f};
        float avgTerrainChunks{0.0f};
        float avgGatherCandidateChunks{0.0f};
        float avgAcceptedChunks{0.0f};
        float avgRenderedFrameDrawCalls{0.0f};
        float avgRenderedFrameApiDrawCalls{0.0f};
        float avgRenderedFrameGpuMs{0.0f};
        float avgCascadesRendered{0.0f};
        float avgCascadesReused{0.0f};
        uint32_t renderCachePrecheckHits{0};
        uint32_t gatherCacheHits{0};
        uint32_t gatherCacheMisses{0};
        uint32_t reusedFrames{0};         // cache-hit frames in window
        uint32_t renderedFrames{0};       // re-rendered frames in window
    };

    void init(VkDevice device,
              VkPhysicalDevice physicalDevice,
              VkDescriptorSetLayout mainDescriptorSetLayout,
              VkCommandPool commandPool,
              VkQueue graphicsQueue,
              uint32_t swapchainImageCount,
              bool enableGpuTiming = true);
    void cleanup();

    void recreatePerImageResources(uint32_t swapchainImageCount);

    void updateForFrame(uint32_t imageIndex,
                        const LightingSettings& lighting,
                        const std::vector<PointLight>& transientLights,
                        const glm::vec3& cameraPos,
                        const glm::vec3& cameraForward);

    void recordShadowPasses(VkCommandBuffer cmd,
                            uint32_t imageIndex,
                            const DrawContext& ctx);

    // Read per-light GPU timestamp results for a completed swapchain image.
    void collectGpuTimings(uint32_t imageIndex);
    void setFrameGpuPassCosts(float terrainPassGpuMs);
    void setDetailedDiagnosticsEnabled(bool enabled) { m_detailedDiagnosticsEnabled = enabled; }
    void setTimeManager(TimeManager* tm) { m_timeManager = tm; }

    bool isReady() const { return m_initialized; }

    const std::vector<uint32_t>& getActiveLightRemap() const { return m_activeLightRemap; }
    const std::vector<VkBuffer>& getShadowDataBuffers() const { return m_shadowDataBuffers; }
    const std::vector<VkBuffer>& getSunLocalOriginsBuffers() const { return m_sunLocalOriginsBuffers; }
    const std::vector<LightDiagnostics>& getAllLightDiagnostics() const { return m_lightDiagnostics; }
    const LightDiagnostics* getLightDiagnostics(uint32_t sourceLightIndex) const;
    const FrameDiagnostics& getFrameDiagnostics() const { return m_frameDiagnostics; }
    const SunShadowDiagnostics& getSunShadowDiagnostics() const { return m_sunDiagnostics; }
    const std::deque<SunShadowDebugSnapshot>& getSunDebugLog() const { return m_sunDebugLog; }
    const std::deque<SunShadowTexelEvent>& getSunTexelLog() const { return m_sunTexelLog; }
    void clearSunTexelLog() { m_sunTexelLog.clear(); m_sunTexelLogPrevValid = false; }
    void setSunTexelLogEnabled(bool e) { m_sunTexelLogEnabled = e; }
    bool isSunTexelLogEnabled() const { return m_sunTexelLogEnabled; }
    // When false (default), camera-motion-only events (i.e. events
    // whose reasonMask has no SUN_CHANGE bits set) are suppressed,
    // so the log only shows entries actually driven by realtime
    // celestial motion — matching the patch-simulator log.
    void setSunTexelLogIncludeCameraOnly(bool e) { m_sunTexelLogIncludeCameraOnly = e; }
    bool isSunTexelLogIncludeCameraOnly() const { return m_sunTexelLogIncludeCameraOnly; }
    ActiveCelestial getActiveCelestial() const { return m_activeCelestial; }
    float getEffectiveShadowIntensity() const { return m_effectiveShadowIntensity; }
    float getActiveAzimuth() const { return m_activeAzimuth; }
    float getActiveElevation() const { return m_activeElevation; }
    const glm::vec3& getDirectionalSunDir() const { return m_sunDir; }
    bool isDirectionalShadowActive() const { return m_sunShadowActive; }
    const SunShadowRuntimeConfig& getSunShadowRuntimeConfig() const { return m_sunRuntimeConfig; }

    // Cascade introspection (used by DirectionalShadowWindow overlay).
    uint32_t getActiveSunCascadeCount() const { return m_activeSunCascadeCount; }
    const std::array<glm::mat4, MAX_SUN_SHADOW_CASCADES>& getSunCascadeVPArray() const { return m_sunCascadeVP; }
    const std::array<float, MAX_SUN_SHADOW_CASCADES>& getSunCascadeHalfExtentsArray() const { return m_sunCascadeHalfExtents; }
    const std::array<float, MAX_SUN_SHADOW_CASCADES>& getSunCascadeTexelMetersArray() const { return m_sunCascadeTexelMeters; }
    const glm::vec3& getSunCameraPos() const { return m_sunCameraPos; }
    SunShadowRuntimeConfig estimateSunShadowRuntimeConfig(float budgetMB,
                                                          float texelsPerVoxel,
                                                          uint32_t cascadeCount,
                                                          float cascadeScale,
                                                          float shadowAreaRadius = 0.0f) const;
    bool applySunShadowProfile(bool forceRecreate = false);

    VkDescriptorImageInfo getSunShadowDescriptor() const;
    VkDescriptorImageInfo getPointShadowDescriptor() const;
    VkDescriptorImageInfo getSkyHeightmapDescriptor() const;

    // Upload a static, downsampled copy of the world heightmap into the
    // sky-vis texture. Safe to call once after the world's HeightmapBaseSampler
    // has been loaded; if the sampler is empty, the texture stays zero-filled
    // (the sky-enclosure shader will then simply contribute nothing).
    void uploadSkyHeightmap(const TerrainEdit::HeightmapBaseSampler& sampler,
                            float voxelToMeter = 0.25f);

    DirectionalShadowConfig& getSunShadowConfig() { return m_sunShadowConfig; }
    const DirectionalShadowConfig& getSunShadowConfig() const { return m_sunShadowConfig; }

    SkyEnclosureSettings& getSkyEnclosureSettings() { return m_skyEnclosureSettings; }
    const SkyEnclosureSettings& getSkyEnclosureSettings() const { return m_skyEnclosureSettings; }

private:
    struct ActiveLight {
        uint32_t sourceIndex{0};
        glm::vec3 position{0.0f};
        float nearPlane{0.1f};
        float farPlane{10.0f};
        float radius{10.0f};
        float intensity{1.0f};
    };

    struct SlotShadowCache {
        bool valid{false};
        uint32_t sourceIndex{0};
        glm::vec3 lightPosition{0.0f};
        float nearPlane{0.0f};
        float farPlane{0.0f};
        uint64_t terrainSignature{0u};
    };

    struct ShadowPushConstants {
        glm::mat4 lightVP{1.0f};
        glm::vec4 lightPosFar{0.0f};       // xyz=lightPos, w=farPlane
        glm::vec4 cubeOriginHalf{0.0f};    // xyz=center, w=halfSize (kept for shader layout alignment)
        glm::vec4 terrainChunkCoordMode{0.0f}; // xyz=chunkCoord, w=1.0 local draw / 0.0 indirect path
    };

    static constexpr uint32_t POINT_SHADOW_MAP_SIZE = 512;
    static constexpr uint32_t SUN_DEFAULT_MAP_SIZE = 8192;
    static constexpr float SUN_DEFAULT_HALF_EXTENT_METERS = 64.0f;
    // Floor for the dynamic cascade-0 half extent. Below this we still
    // render at this footprint so projection / texel snap stay well-defined.
    static constexpr float SUN_MIN_HALF_EXTENT_METERS = 8.0f;
    static constexpr float SUN_VOXEL_SIZE_METERS = 0.25f;
    static constexpr float SUN_DEFAULT_TEXELS_PER_VOXEL = 16.0f;
    static constexpr float SUN_MIN_TEXELS_PER_VOXEL = 4.0f;
    static constexpr float SUN_MAX_TEXELS_PER_VOXEL = 64.0f;
    static constexpr float SUN_MIN_BUDGET_MB = 16.0f;
    static constexpr uint32_t SUN_MIN_CASCADE_COUNT = 1;
    static constexpr uint32_t SUN_MAX_CASCADE_COUNT = MAX_SUN_SHADOW_CASCADES;
    static constexpr float SUN_MIN_CASCADE_SCALE = 1.25f;
    static constexpr float SUN_MAX_CASCADE_SCALE = 8.0f;
    static constexpr uint32_t SUN_LOCAL_INDIRECT_DRAWS_PER_CASCADE = 65536u;
    static constexpr uint32_t SUN_LOCAL_INDIRECT_TOTAL_DRAWS =
        SUN_LOCAL_INDIRECT_DRAWS_PER_CASCADE * MAX_SUN_SHADOW_CASCADES;

    struct SunShadowCache {
        bool valid{false};
        glm::vec3 cameraPos{0.0f};
        float azimuth{0.0f};
        float elevation{0.0f};
        uint64_t terrainSignature{0u};
        uint64_t ctxHash{0u};         // hashTerrainDrawContext(ctx) at last gather
        uint64_t terrainMeshRevision{0u};
        uint64_t uploadTimeline{0u};
        uint32_t uploadPendingRejects{0u};
        uint32_t terrainDrawCount{0u};// gathered chunk count at last gather
        uint32_t cascadeCount{0u};
        uint64_t vpHash{0u};      // hash of all cascade VP matrices
        std::array<bool, MAX_SUN_SHADOW_CASCADES> cascadeValid{};
        std::array<uint64_t, MAX_SUN_SHADOW_CASCADES> cascadeVpHashes{};
        std::array<uint64_t, MAX_SUN_SHADOW_CASCADES> cascadeTerrainSignatures{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeDrawCounts{};
        glm::mat4 renderedVP{1.0f};  // cascade 0 VP used for diagnostics/logs
        std::array<glm::mat4, MAX_SUN_SHADOW_CASCADES> renderedCascadeVP{};
        glm::vec3 renderedSunDir{0.0f, -1.0f, 0.0f};  // sunDir matching rendered shadow map
        uint32_t loadedChunkMapSize{0u};
        uint32_t bboxCandidateChunks{0u};
        uint32_t visitedCandidateChunks{0u};
        uint32_t acceptedChunks{0u};
        uint32_t cascadeInnerCullRejects{0u};
        float sinElevation{0.0f};
        float shearX{0.0f};
        float shearZ{0.0f};
        float shearMax{0.0f};
        float casterReach{0.0f};
        float padding{0.0f};
        float halfX{0.0f};
        float halfZ{0.0f};
        glm::ivec3 cameraChunk{0};
        glm::ivec3 gatherMinChunk{0};
        glm::ivec3 gatherMaxChunk{0};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeChunkHits{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeDrawHits{};
    };

    // ── Sun-shadow GATHER cache (independent of render cache) ──────
    // The render cache (SunShadowCache above) requires bit-equal cascade
    // VPs to skip re-rendering — it almost always misses during player
    // motion because the cascade VP texel-snaps every frame. The GATHER,
    // however, depends only on snapped camera position, sun direction,
    // chunk topology and gather radius — it is invariant under sub-chunk
    // motion. Caching it eliminates the ~7.6 ms per-frame terrain walk
    // (~25 k chunks) when the player is moving but no chunks have been
    // added/removed and the sun is stationary.
    //
    // The per-cascade clip-AABB cull is still re-run every frame on the
    // cached gather output, preserving the exact draw set (no visual
    // change). Cache invalidates on: gather chunk-range change, sun
    // direction bit change, ctx hash change (incl. terrainEditRevision),
    // mesh-topology changes inside the cascade footprint, pending-upload
    // timeline catch-up, or cascade config change.
    struct SunGatherCache {
        bool valid{false};
        glm::ivec3 cameraChunk{0};   // floor(m_sunCameraPos / chunkExtents)
        glm::vec3 sunDir{0.0f, -1.0f, 0.0f};
        uint64_t ctxHash{0u};
        uint64_t terrainMeshRevision{0u};
        uint64_t uploadTimeline{0u};
        uint32_t uploadPendingRejects{0u};
        uint64_t vpHash{0u};         // hash of cascade VPs (gather depends on them)
        uint32_t cascadeCount{0u};
        std::array<float, MAX_SUN_SHADOW_CASCADES> cascadeHalfExtents{};
        uint32_t drawCount{0u};
        uint64_t terrainSig{0u};
        uint32_t loadedChunkMapSize{0u};
        uint32_t bboxCandidateChunks{0u};
        uint32_t visitedCandidateChunks{0u};
        uint32_t acceptedChunks{0u};
        uint32_t cascadeInnerCullRejects{0u};
        float sinElevation{0.0f};
        float shearX{0.0f};
        float shearZ{0.0f};
        float shearMax{0.0f};
        float casterReach{0.0f};
        float padding{0.0f};
        float halfX{0.0f};
        float halfZ{0.0f};
        glm::ivec3 gatherMinChunk{0};
        glm::ivec3 gatherMaxChunk{0};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeChunkHits{};
        std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> cascadeDrawHits{};
    };

    void recordSunShadowPasses(VkCommandBuffer cmd,
                               uint32_t imageIndex,
                               const DrawContext& ctx);
    void recordPointShadowPasses(VkCommandBuffer cmd,
                                 uint32_t imageIndex,
                                 const DrawContext& ctx);

    void createPerImageBuffers(uint32_t swapchainImageCount);
    void destroyPerImageBuffers();

    void createShadowImages();
    void createShadowRenderPass();
    void createShadowFramebuffers();
    void createShadowPipelines();
    void transitionImagesToSampled(VkCommandPool commandPool, VkQueue queue);
    void createSunShadowImageAndView();
    void createSunShadowScrollScratch();
    void destroySunShadowScrollScratch();
    void createSunShadowFramebuffers();
    void transitionSunImageToSampled(VkCommandPool commandPool, VkQueue queue);

    std::array<glm::mat4, 6> buildPointLightFaceMatrices(const glm::vec3& lightPos,
                                                          float nearPlane,
                                                          float farPlane) const;

    // Builds the oblique-shear directional VP for the sun shadow map.
    // In pixel-stable mode, the shear factors (shearX, shearZ) are
    // quantized to texel-aligned steps and the XY origin can be snapped
    // to shadow texels so sun motion produces clean discrete projection
    // changes. In smooth-motion mode, both can be disabled so azimuth
    // changes glide continuously while depth remains stable.
    //
    // The quantized sun direction (which is consistent with the returned
    // VP) is written to *outQuantizedSunDir if non-null and MUST be used
    // by the GLSL receiver push, otherwise the push direction drifts out
    // of alignment with the shadow-map content.
    glm::mat4 buildSunLightVP(const glm::vec3& cameraPos,
                               float azimuthDeg,
                               float elevationDeg,
                               float halfExtent,
                               float texelMeters,
                               bool enableTexelSnap,
                               bool enableShearQuantization,
                               glm::vec3* outQuantizedSunDir = nullptr,
                               bool isHDCascade = false);

private:
    VkDevice m_device{VK_NULL_HANDLE};
    VkPhysicalDevice m_physicalDevice{VK_NULL_HANDLE};
    VkDescriptorSetLayout m_mainDescriptorSetLayout{VK_NULL_HANDLE};

    bool m_initialized{false};

    // Per-swapchain-image shadow metadata SSBO
    std::vector<VkBuffer> m_shadowDataBuffers;
    std::vector<VkDeviceMemory> m_shadowDataMemories;
    std::vector<void*> m_shadowDataMapped;
    std::vector<VkBuffer> m_sunLocalIndirectBuffers;
    std::vector<VkDeviceMemory> m_sunLocalIndirectMemories;
    std::vector<VkBuffer> m_sunLocalOriginsBuffers;
    std::vector<VkDeviceMemory> m_sunLocalOriginsMemories;
    std::vector<VkBuffer> m_sunLocalUploadBuffers;
    std::vector<VkDeviceMemory> m_sunLocalUploadMemories;
    std::vector<void*> m_sunLocalUploadMapped;
    std::array<uint32_t, MAX_SUN_SHADOW_CASCADES> m_sunLocalIndirectCounts{};

    // Point shadow map array resources (32 lights * 6 faces)
    VkImage m_pointShadowImage{VK_NULL_HANDLE};
    VkDeviceMemory m_pointShadowMemory{VK_NULL_HANDLE};
    VkImageView m_pointShadowCubeArrayView{VK_NULL_HANDLE};
    std::vector<VkImageView> m_pointShadowLayerViews;

    // Sun (directional) shadow map resources
    VkImage m_sunShadowImage{VK_NULL_HANDLE};
    VkDeviceMemory m_sunShadowMemory{VK_NULL_HANDLE};
    VkImageView m_sunShadowArrayView{VK_NULL_HANDLE};
    std::vector<VkImageView> m_sunShadowLayerViews;
    std::vector<VkFramebuffer> m_sunShadowFramebuffers;
    std::vector<VkFramebuffer> m_sunShadowLoadFramebuffers;
    VkImage m_sunShadowScrollScratchImage{VK_NULL_HANDLE};
    VkDeviceMemory m_sunShadowScrollScratchMemory{VK_NULL_HANDLE};
    VkImageLayout m_sunShadowScrollScratchLayout{VK_IMAGE_LAYOUT_UNDEFINED};
    VkPipeline m_sunShadowPipeline{VK_NULL_HANDLE};
    SunShadowRuntimeConfig m_sunRuntimeConfig{};
    uint32_t m_maxSunShadowMapDimension{SUN_DEFAULT_MAP_SIZE};
    VkCommandPool m_commandPool{VK_NULL_HANDLE};
    VkQueue m_graphicsQueue{VK_NULL_HANDLE};

    DirectionalShadowConfig m_sunShadowConfig{};
    SkyEnclosureSettings m_skyEnclosureSettings{};
    ActiveCelestial m_activeCelestial{ActiveCelestial::Sun};
    float m_effectiveShadowIntensity{0.75f};  // after fade
    float m_activeAzimuth{225.0f};            // azimuth used for current VP
    float m_activeElevation{45.0f};           // elevation used for current VP
    float m_smoothAzimuth{225.0f};            // smoothly interpolated toward target
    float m_smoothElevation{45.0f};
    float m_prevActiveAzimuth{225.0f};        // previous frame active azimuth
    float m_prevActiveElevation{45.0f};       // previous frame active elevation
    bool  m_prevSunAnglesValid{false};
    bool  m_sunAnglesMoving{false};           // used for smooth directional motion mode
    bool  m_sunSmoothMotionActive{false};     // disables cache reuse while smoothly moving
    bool  m_smoothInitialized{false};         // first-frame init flag
    std::chrono::steady_clock::time_point m_lastShadowUpdateTime{};
    TimeManager* m_timeManager{nullptr};
    SunShadowCache m_sunShadowCache{};
    SunGatherCache m_sunGatherCache{};
    glm::mat4 m_sunLightVP{1.0f};
    std::array<glm::mat4, MAX_SUN_SHADOW_CASCADES> m_sunCascadeVP{};
    std::array<float, MAX_SUN_SHADOW_CASCADES> m_sunCascadeHalfExtents{};
    std::array<float, MAX_SUN_SHADOW_CASCADES> m_sunCascadeTexelMeters{};
    uint32_t m_activeSunCascadeCount{1u};
    glm::vec3 m_sunDir{0.0f, -1.0f, 0.0f};
    glm::vec3 m_sunCameraPos{0.0f};
    float m_prevQuantizedSunShearX{0.0f};
    float m_prevQuantizedSunShearZ{0.0f};
    bool m_prevQuantizedSunShearValid{false};
    float m_prevQuantizedSunOriginClipX{0.0f};
    float m_prevQuantizedSunOriginClipY{0.0f};
    bool m_prevQuantizedSunOriginValid{false};
    bool m_sunShadowActive{false};

    VkSampler m_shadowSampler{VK_NULL_HANDLE};

    // Sky-vis heightmap texture (static after upload).
    // Format: VK_FORMAT_R16_SFLOAT, fixed dimension SKY_HEIGHTMAP_DIM.
    static constexpr uint32_t SKY_HEIGHTMAP_DIM = 2048u;
    VkImage        m_skyHeightmapImage{VK_NULL_HANDLE};
    VkDeviceMemory m_skyHeightmapMemory{VK_NULL_HANDLE};
    VkImageView    m_skyHeightmapView{VK_NULL_HANDLE};
    VkSampler      m_skyHeightmapSampler{VK_NULL_HANDLE};
    glm::vec4      m_skyHeightmapInfo{0.0f}; // matches ShadowGPUData::skyHeightmapInfo
    bool           m_skyHeightmapInitialized{false};

    VkRenderPass m_shadowRenderPass{VK_NULL_HANDLE};
    VkRenderPass m_shadowLoadRenderPass{VK_NULL_HANDLE};
    std::vector<VkFramebuffer> m_shadowFramebuffers;

    VkPipelineLayout m_terrainShadowPipelineLayout{VK_NULL_HANDLE};
    VkPipeline m_terrainShadowPipeline{VK_NULL_HANDLE};

    // Frame-selected lights
    std::vector<ActiveLight> m_activeLights;
    std::vector<uint32_t> m_activeLightRemap;
    std::array<SlotShadowCache, MAX_POINT_SHADOW_LIGHTS> m_slotShadowCache{};
    std::vector<VkDrawIndexedIndirectCommand> m_localTerrainDrawScratch;
    std::vector<glm::vec4> m_localTerrainOriginScratch;
    // Separate scratch for sun gather so point-light gather (which
    // reuses m_localTerrainDrawScratch / m_localTerrainOriginScratch)
    // doesn't clobber the sun gather cache mid-frame.
    std::vector<VkDrawIndexedIndirectCommand> m_sunLocalTerrainDrawScratch;
    std::vector<glm::vec4> m_sunLocalTerrainOriginScratch;
    // Per-draw cascade bitmask emitted by World::gatherDrawCommandsForSunCascades.
    // Bit c = 1 means draw should be issued in cascade c.
    std::vector<uint16_t> m_sunLocalTerrainCascadeMaskScratch;

    // Per-light diagnostics (indexed by source light index).
    std::vector<LightDiagnostics> m_lightDiagnostics;

    // Per-image timestamp query pool for point-shadow GPU timings.
    VkQueryPool m_lightTimingQueryPool{VK_NULL_HANDLE};
    uint32_t m_lightTimingImageCount{0};
    float m_timestampPeriod{0.0f};
    std::vector<std::array<uint32_t, MAX_POINT_SHADOW_LIGHTS>> m_querySourceByImage;
    std::vector<uint32_t> m_queryLightCountByImage;

    bool m_detailedDiagnosticsEnabled{false};
    bool m_gpuTimingEnabled{true};
    float m_lastTerrainPassGpuMs{0.0f};
    FrameDiagnostics m_frameDiagnostics{};

    // ── Sun shadow diagnostics ──────────────────────────────────────
    struct SunShadowTimedSample {
        std::chrono::steady_clock::time_point timestamp;
        SunShadowFrameSample data;
    };
    VkQueryPool m_sunTimingQueryPool{VK_NULL_HANDLE};
    uint32_t m_sunTimingImageCount{0};
    std::vector<bool> m_sunTimingWritten;  // per-image: was a timestamp pair written?
    std::deque<SunShadowTimedSample> m_sunSampleHistory;
    SunShadowDiagnostics m_sunDiagnostics{};
    SunShadowFrameSample m_sunCurrentFrame{};  // accumulated during record
    std::deque<SunShadowDebugSnapshot> m_sunDebugLog;  // patch-simulator snapshots
    std::deque<SunShadowTexelEvent> m_sunTexelLog;     // in-game per-frame VP/cache log
    SunShadowTexelEvent m_sunTexelLogPrev{};
    bool m_sunTexelLogPrevValid{false};
    bool m_sunTexelLogEnabled{false};
    bool m_sunTexelLogIncludeCameraOnly{false};
    uint64_t m_sunTexelLogPrevFrame{0};
    uint64_t m_sunFrameCounter{0};
    void collectSunGpuTiming(uint32_t imageIndex);
    void pushSunSample(const SunShadowFrameSample& sample);
    void recomputeSunWindowStats();
};

````

## src\CMakeLists.txt

Description: No CC-DESC found.

````cmake
# GPT-DESC: Defines VulkanVX source targets and shader build helpers.
cmake_minimum_required(VERSION 3.16)

# Core engine files (minimal - just app lifecycle)
set(CORE_SOURCES
    # core/engine/ - Engine class split files
    core/engine/Engine.cpp
    core/engine/EngineVulkanInit.cpp
    core/engine/EngineSubsystemInit.cpp
    core/engine/EngineDebugWiring.cpp
    core/engine/EngineCleanup.cpp
    core/engine/EngineRenderLoop.cpp
    core/engine/EngineTimestamps.cpp
    core/engine/EngineShadowPass.cpp
    core/engine/EngineDepthPrePass.cpp
    core/engine/EngineGameplayRendering.cpp
    core/engine/EngineCommandBuffer.cpp
    core/engine/EngineShaderHotReload.cpp
    core/engine/EngineSettingsPersistence.cpp
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
    world/edit/TerrainEditOverlayStore.cpp
    world/edit/TerrainEditOverlayStore_IO.cpp
    world/edit/TextureOverlayStore.cpp
    world/edit/TerrainFieldSource.cpp
    world/edit/HeightmapBaseSampler.cpp
    world/edit/VoxelBaseSampler.cpp
    world/edit/TerrainEditMesher.cpp
    world/edit/TerrainEditDCCMMesher.cpp
    world/edit/TerrainEditRemeshScheduler.cpp
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
    ui/debug_menu/world/TexturePaintTool.cpp
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
