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


## src\rendering\lighting\ShadowMapRendering.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include "world/World.h"
#include "world/config/WorldConfig.h"

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#include <glm/gtc/matrix_transform.hpp>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <type_traits>
#include <vector>

namespace {

constexpr uint32_t kInvalidSourceIndex = std::numeric_limits<uint32_t>::max();
constexpr uint32_t kPointShadowFacesPerLight = 6u;
constexpr float kShadowCacheEpsilon = 0.0001f;
constexpr int32_t kSunGatherCachePaddingChunks = 12;
constexpr float kSunScrollTexelEpsilon = 0.01f;
constexpr float kSunScrollMaxDirtyFraction = 0.08f;

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

bool nearlyEqual(float a, float b, float eps = kShadowCacheEpsilon) {
    return std::abs(a - b) <= eps;
}

bool nearlyEqualVec3(const glm::vec3& a, const glm::vec3& b, float eps = kShadowCacheEpsilon) {
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

struct SunCascadeScrollPlan {
    bool enabled{false};
    int32_t dxTexels{0};
    int32_t dyTexels{0};
    uint64_t copiedTexels{0};
    uint64_t dirtyTexels{0};
};

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

bool chunkIntersectsSunCascadeClip(const glm::mat4& vp, const glm::vec4& chunkCoord) {
    constexpr float kHalfChunkX = WorldConfig::CHUNK_SIZE_M * 0.5f;
    constexpr float kHalfChunkY = WorldConfig::CHUNK_HEIGHT_M * 0.5f;
    constexpr float kHalfChunkZ = WorldConfig::CHUNK_SIZE_M * 0.5f;

    const glm::vec3 centerWorld(
        (chunkCoord.x + 0.5f) * WorldConfig::CHUNK_SIZE_M,
        (chunkCoord.y + 0.5f) * WorldConfig::CHUNK_HEIGHT_M,
        (chunkCoord.z + 0.5f) * WorldConfig::CHUNK_SIZE_M);

    const float clipCenterX =
        vp[0][0] * centerWorld.x +
        vp[1][0] * centerWorld.y +
        vp[2][0] * centerWorld.z +
        vp[3][0];
    const float clipCenterY =
        vp[0][1] * centerWorld.x +
        vp[1][1] * centerWorld.y +
        vp[2][1] * centerWorld.z +
        vp[3][1];
    const float clipCenterZ =
        vp[0][2] * centerWorld.x +
        vp[1][2] * centerWorld.y +
        vp[2][2] * centerWorld.z +
        vp[3][2];

    const float clipHalfX =
        std::abs(vp[0][0]) * kHalfChunkX +
        std::abs(vp[1][0]) * kHalfChunkY +
        std::abs(vp[2][0]) * kHalfChunkZ;
    const float clipHalfY =
        std::abs(vp[0][1]) * kHalfChunkX +
        std::abs(vp[1][1]) * kHalfChunkY +
        std::abs(vp[2][1]) * kHalfChunkZ;
    const float clipHalfZ =
        std::abs(vp[0][2]) * kHalfChunkX +
        std::abs(vp[1][2]) * kHalfChunkY +
        std::abs(vp[2][2]) * kHalfChunkZ;

    if ((clipCenterX + clipHalfX) < -1.0f || (clipCenterX - clipHalfX) > 1.0f) {
        return false;
    }
    if ((clipCenterY + clipHalfY) < -1.0f || (clipCenterY - clipHalfY) > 1.0f) {
        return false;
    }
    if ((clipCenterZ + clipHalfZ) < 0.0f || (clipCenterZ - clipHalfZ) > 1.0f) {
        return false;
    }
    return true;
}

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
    uint32_t* outInnerRejected = nullptr) {
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
    bool* reliableOut = nullptr) {
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

} // namespace

void ShadowSystem::recordShadowPasses(VkCommandBuffer cmd,
                                      uint32_t imageIndex,
                                      const DrawContext& ctx) {
    if (!m_initialized || imageIndex >= m_shadowDataBuffers.size()) {
        return;
    }

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

        std::vector<glm::vec4> nearbyCubes;
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

## include\rendering\lighting\ShadowSystem.h

Description: No CC-DESC found. C++ struct 'LightingSettings'.

````cpp
#pragma once

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

## src\rendering\lighting\ShadowSystem.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include "rendering/common/VulkanHelpers.h"
#include "rendering/common/Mesh.h"
#include "rendering/lighting/LightingSettings.h"
#include "world/edit/HeightmapBaseSampler.h"
#include "world/config/WorldConfig.h"

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <vector>

// init(), cleanup(), recreatePerImageResources(), getSun/PointShadowDescriptor()
// See also: ShadowSystemUpdate.cpp, ShadowSystemResources.cpp

void ShadowSystem::init(VkDevice device,
                        VkPhysicalDevice physicalDevice,
                        VkDescriptorSetLayout mainDescriptorSetLayout,
                        VkCommandPool commandPool,
                        VkQueue graphicsQueue,
                        uint32_t swapchainImageCount,
                        bool enableGpuTiming) {
    cleanup();

    m_device = device;
    m_physicalDevice = physicalDevice;
    m_mainDescriptorSetLayout = mainDescriptorSetLayout;
    m_commandPool = commandPool;
    m_graphicsQueue = graphicsQueue;
    m_gpuTimingEnabled = enableGpuTiming;
    VkPhysicalDeviceProperties props{};
    vkGetPhysicalDeviceProperties(m_physicalDevice, &props);
    m_timestampPeriod = props.limits.timestampPeriod;
    m_maxSunShadowMapDimension = props.limits.maxImageDimension2D;
    applySunShadowProfile(true);

    createPerImageBuffers(swapchainImageCount);
    createShadowImages();
    createShadowRenderPass();
    createShadowFramebuffers();
    createShadowPipelines();
    transitionImagesToSampled(commandPool, graphicsQueue);

    // Sky-vis static heightmap texture: created here zero-filled at the
    // fixed SKY_HEIGHTMAP_DIM. uploadSkyHeightmap() fills the content
    // later (after the world's HeightmapBaseSampler has loaded). The
    // descriptor binding never needs to be re-written because the image
    // handle is stable from this point on.
    {
        VkImageCreateInfo imgInfo{};
        imgInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        imgInfo.imageType = VK_IMAGE_TYPE_2D;
        imgInfo.format = VK_FORMAT_R16_SFLOAT;
        imgInfo.extent = { SKY_HEIGHTMAP_DIM, SKY_HEIGHTMAP_DIM, 1 };
        imgInfo.mipLevels = 1;
        imgInfo.arrayLayers = 1;
        imgInfo.samples = VK_SAMPLE_COUNT_1_BIT;
        imgInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
        imgInfo.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
        imgInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        imgInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (vkCreateImage(m_device, &imgInfo, nullptr, &m_skyHeightmapImage) != VK_SUCCESS) {
            throw std::runtime_error("failed to create sky heightmap image");
        }

        VkMemoryRequirements memReqs{};
        vkGetImageMemoryRequirements(m_device, m_skyHeightmapImage, &memReqs);
        VkMemoryAllocateInfo memAlloc{};
        memAlloc.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        memAlloc.allocationSize = memReqs.size;
        memAlloc.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReqs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (vkAllocateMemory(m_device, &memAlloc, nullptr, &m_skyHeightmapMemory) != VK_SUCCESS) {
            throw std::runtime_error("failed to allocate sky heightmap memory");
        }
        vkBindImageMemory(m_device, m_skyHeightmapImage, m_skyHeightmapMemory, 0);

        VkImageViewCreateInfo viewInfo{};
        viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        viewInfo.image = m_skyHeightmapImage;
        viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        viewInfo.format = VK_FORMAT_R16_SFLOAT;
        viewInfo.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        if (vkCreateImageView(m_device, &viewInfo, nullptr, &m_skyHeightmapView) != VK_SUCCESS) {
            throw std::runtime_error("failed to create sky heightmap view");
        }

        VkSamplerCreateInfo samplerInfo{};
        samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        samplerInfo.magFilter = VK_FILTER_NEAREST;
        samplerInfo.minFilter = VK_FILTER_NEAREST;
        samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.compareEnable = VK_FALSE;
        samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
        if (vkCreateSampler(m_device, &samplerInfo, nullptr, &m_skyHeightmapSampler) != VK_SUCCESS) {
            throw std::runtime_error("failed to create sky heightmap sampler");
        }

        // Transition UNDEFINED -> SHADER_READ_ONLY (zero content; safe to sample).
        VkCommandBufferAllocateInfo cmdAlloc{};
        cmdAlloc.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        cmdAlloc.commandPool = m_commandPool;
        cmdAlloc.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        cmdAlloc.commandBufferCount = 1;
        VkCommandBuffer cmd{VK_NULL_HANDLE};
        vkAllocateCommandBuffers(m_device, &cmdAlloc, &cmd);
        VkCommandBufferBeginInfo bi{};
        bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        vkBeginCommandBuffer(cmd, &bi);
        VkImageMemoryBarrier b{};
        b.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        b.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        b.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        b.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        b.image = m_skyHeightmapImage;
        b.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        b.srcAccessMask = 0;
        b.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        vkCmdPipelineBarrier(cmd,
            VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
            0, 0, nullptr, 0, nullptr, 1, &b);
        vkEndCommandBuffer(cmd);
        VkSubmitInfo si{};
        si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        si.commandBufferCount = 1;
        si.pCommandBuffers = &cmd;
        vkQueueSubmit(m_graphicsQueue, 1, &si, VK_NULL_HANDLE);
        vkQueueWaitIdle(m_graphicsQueue);
        vkFreeCommandBuffers(m_device, m_commandPool, 1, &cmd);
    }
    m_skyHeightmapInitialized = false; // becomes true once content is uploaded

    m_initialized = true;
    std::cout << "[ShadowSystem] Initialized (point shadow map: "
              << POINT_SHADOW_MAP_SIZE << "x" << POINT_SHADOW_MAP_SIZE
              << ", max lights: " << MAX_POINT_SHADOW_LIGHTS
              << ", sun cascades: " << m_sunRuntimeConfig.cascadeCount
              << " x " << m_sunRuntimeConfig.mapSize << "x" << m_sunRuntimeConfig.mapSize
              << ", max cast radius: " << m_sunRuntimeConfig.maxCastRadiusMeters << "m"
              << ", texels/voxel: " << m_sunRuntimeConfig.texelsPerVoxel
              << ", mem: " << m_sunRuntimeConfig.actualMemoryMB << " MB)\n";

    // Directional-shadow resources are ready; profile can be changed at runtime via applySunShadowProfile().
}

void ShadowSystem::cleanup() {
    if (m_device == VK_NULL_HANDLE) {
        return;
    }

    vkDeviceWaitIdle(m_device);

    destroyPerImageBuffers();

    if (m_terrainShadowPipeline) vkDestroyPipeline(m_device, m_terrainShadowPipeline, nullptr);
    if (m_terrainShadowPipelineLayout) vkDestroyPipelineLayout(m_device, m_terrainShadowPipelineLayout, nullptr);
    m_terrainShadowPipeline = VK_NULL_HANDLE;
    m_terrainShadowPipelineLayout = VK_NULL_HANDLE;

    if (m_sunShadowPipeline) vkDestroyPipeline(m_device, m_sunShadowPipeline, nullptr);
    m_sunShadowPipeline = VK_NULL_HANDLE;

    for (VkFramebuffer fb : m_shadowFramebuffers) {
        vkDestroyFramebuffer(m_device, fb, nullptr);
    }
    m_shadowFramebuffers.clear();

    for (VkFramebuffer fb : m_sunShadowFramebuffers) {
        vkDestroyFramebuffer(m_device, fb, nullptr);
    }
    m_sunShadowFramebuffers.clear();
    for (VkFramebuffer fb : m_sunShadowLoadFramebuffers) {
        vkDestroyFramebuffer(m_device, fb, nullptr);
    }
    m_sunShadowLoadFramebuffers.clear();

    if (m_shadowRenderPass) vkDestroyRenderPass(m_device, m_shadowRenderPass, nullptr);
    m_shadowRenderPass = VK_NULL_HANDLE;
    if (m_shadowLoadRenderPass) vkDestroyRenderPass(m_device, m_shadowLoadRenderPass, nullptr);
    m_shadowLoadRenderPass = VK_NULL_HANDLE;

    for (VkImageView layerView : m_pointShadowLayerViews) {
        vkDestroyImageView(m_device, layerView, nullptr);
    }
    m_pointShadowLayerViews.clear();

    if (m_pointShadowCubeArrayView) vkDestroyImageView(m_device, m_pointShadowCubeArrayView, nullptr);
    if (m_pointShadowImage) vkDestroyImage(m_device, m_pointShadowImage, nullptr);
    if (m_pointShadowMemory) vkFreeMemory(m_device, m_pointShadowMemory, nullptr);
    m_pointShadowCubeArrayView = VK_NULL_HANDLE;
    m_pointShadowImage = VK_NULL_HANDLE;
    m_pointShadowMemory = VK_NULL_HANDLE;

    for (VkImageView layerView : m_sunShadowLayerViews) {
        vkDestroyImageView(m_device, layerView, nullptr);
    }
    m_sunShadowLayerViews.clear();
    if (m_sunShadowArrayView) vkDestroyImageView(m_device, m_sunShadowArrayView, nullptr);
    if (m_sunShadowImage) vkDestroyImage(m_device, m_sunShadowImage, nullptr);
    if (m_sunShadowMemory) vkFreeMemory(m_device, m_sunShadowMemory, nullptr);
    m_sunShadowArrayView = VK_NULL_HANDLE;
    m_sunShadowImage = VK_NULL_HANDLE;
    m_sunShadowMemory = VK_NULL_HANDLE;
    destroySunShadowScrollScratch();

    if (m_shadowSampler) vkDestroySampler(m_device, m_shadowSampler, nullptr);
    m_shadowSampler = VK_NULL_HANDLE;

    // Sky-vis heightmap texture
    if (m_skyHeightmapSampler) vkDestroySampler(m_device, m_skyHeightmapSampler, nullptr);
    if (m_skyHeightmapView)    vkDestroyImageView(m_device, m_skyHeightmapView, nullptr);
    if (m_skyHeightmapImage)   vkDestroyImage(m_device, m_skyHeightmapImage, nullptr);
    if (m_skyHeightmapMemory)  vkFreeMemory(m_device, m_skyHeightmapMemory, nullptr);
    m_skyHeightmapSampler = VK_NULL_HANDLE;
    m_skyHeightmapView = VK_NULL_HANDLE;
    m_skyHeightmapImage = VK_NULL_HANDLE;
    m_skyHeightmapMemory = VK_NULL_HANDLE;
    m_skyHeightmapInfo = glm::vec4(0.0f);
    m_skyHeightmapInitialized = false;

    m_activeLights.clear();
    m_activeLightRemap.clear();
    m_slotShadowCache = {};
    m_localTerrainDrawScratch.clear();
    m_localTerrainOriginScratch.clear();
    m_lightDiagnostics.clear();
    m_timestampPeriod = 0.0f;
    m_detailedDiagnosticsEnabled = false;
    m_lastTerrainPassGpuMs = 0.0f;
    m_frameDiagnostics = {};
    m_sunShadowCache = {};
    m_sunLightVP = glm::mat4(1.0f);
    m_sunCascadeVP = {};
    m_sunCascadeHalfExtents = {};
    m_sunCascadeTexelMeters = {};
    m_activeSunCascadeCount = 1u;
    m_sunDir = glm::vec3(0.0f, -1.0f, 0.0f);
    m_sunCameraPos = glm::vec3(0.0f);
    m_prevQuantizedSunShearX = 0.0f;
    m_prevQuantizedSunShearZ = 0.0f;
    m_prevQuantizedSunShearValid = false;
    m_prevQuantizedSunOriginClipX = 0.0f;
    m_prevQuantizedSunOriginClipY = 0.0f;
    m_prevQuantizedSunOriginValid = false;
    m_prevActiveAzimuth = 225.0f;
    m_prevActiveElevation = 45.0f;
    m_prevSunAnglesValid = false;
    m_sunAnglesMoving = false;
    m_sunShadowActive = false;
    m_commandPool = VK_NULL_HANDLE;
    m_graphicsQueue = VK_NULL_HANDLE;
    m_initialized = false;
}

void ShadowSystem::recreatePerImageResources(uint32_t swapchainImageCount) {
    if (!m_device) {
        return;
    }
    destroyPerImageBuffers();
    createPerImageBuffers(swapchainImageCount);
}

VkDescriptorImageInfo ShadowSystem::getSunShadowDescriptor() const {
    VkDescriptorImageInfo info{};
    info.sampler = m_shadowSampler;
    info.imageView = m_sunShadowArrayView;
    info.imageLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    return info;
}

VkDescriptorImageInfo ShadowSystem::getPointShadowDescriptor() const {
    VkDescriptorImageInfo info{};
    info.sampler = m_shadowSampler;
    info.imageView = m_pointShadowCubeArrayView;
    info.imageLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    return info;
}

VkDescriptorImageInfo ShadowSystem::getSkyHeightmapDescriptor() const {
    VkDescriptorImageInfo info{};
    info.sampler = m_skyHeightmapSampler;
    info.imageView = m_skyHeightmapView;
    info.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    return info;
}

void ShadowSystem::uploadSkyHeightmap(
    const TerrainEdit::HeightmapBaseSampler& sampler,
    float voxelToMeter)
{
    if (!m_skyHeightmapImage || !sampler.isLoaded()) {
        return;
    }

    const int srcW = sampler.getMapWidth();
    const int srcH = sampler.getMapHeight();
    const uint32_t dstDim = SKY_HEIGHTMAP_DIM;

    // World-space bounds: heightmap covers world voxel grid (0,0)..(srcW-1, srcH-1).
    // World meters per heightmap voxel = voxelToMeter (default 0.25 = 4 voxels/m).
    const float worldSizeXMeters = static_cast<float>(srcW) * voxelToMeter;
    const float worldSizeZMeters = static_cast<float>(srcH) * voxelToMeter;
    const float metersPerTexel   = worldSizeXMeters / static_cast<float>(dstDim);

    m_skyHeightmapInfo = glm::vec4(
        0.0f,             // worldOriginXMeters
        0.0f,             // worldOriginZMeters
        metersPerTexel,   // assumes square coverage
        voxelToMeter);    // value (voxels) → meters scale

    // Downsample with conservative MAX (so we never miss tall features).
    const size_t pixelCount = static_cast<size_t>(dstDim) * dstDim;
    std::vector<uint16_t> halfData(pixelCount, 0);

    auto floatToHalf = [](float f) -> uint16_t {
        // Minimal IEEE-754 float32 → float16 (round toward zero, no NaN handling).
        // Heightmap values are non-negative finite, so this is sufficient.
        union { float f; uint32_t u; } v;
        v.f = std::max(0.0f, f);
        uint32_t e = (v.u >> 23) & 0xFFu;
        uint32_t m = v.u & 0x7FFFFFu;
        if (e == 0u) return 0u;
        if (e >= 143u) return 0x7BFFu; // clamp to max half (~65504)
        if (e <= 112u) return 0u;
        uint32_t newE = e - 112u;
        return static_cast<uint16_t>((newE << 10) | (m >> 13));
    };

    const float xStep = static_cast<float>(srcW) / static_cast<float>(dstDim);
    const float zStep = static_cast<float>(srcH) / static_cast<float>(dstDim);
    for (uint32_t pz = 0; pz < dstDim; ++pz) {
        const int z0 = std::min(srcH - 1, static_cast<int>(pz       * zStep));
        const int z1 = std::min(srcH - 1, static_cast<int>((pz + 1) * zStep));
        for (uint32_t px = 0; px < dstDim; ++px) {
            const int x0 = std::min(srcW - 1, static_cast<int>(px       * xStep));
            const int x1 = std::min(srcW - 1, static_cast<int>((px + 1) * xStep));
            // Average downsample. Using MAX caused per-texel inflation
            // that LINEAR/NEAREST sampling turned into chaotic stripes
            // around tall features. Averaging keeps flat ground flat in
            // the texture; the small (~one source-cell) downsampling
            // error is removed by the shader's dead-zone.
            double hSum = 0.0;
            int    hCount = 0;
            for (int z = z0; z <= z1; ++z) {
                for (int x = x0; x <= x1; ++x) {
                    hSum += static_cast<double>(sampler.getHeightAtVoxelUnchecked(x, z));
                    ++hCount;
                }
            }
            const float hAvg = hCount > 0 ? static_cast<float>(hSum / hCount) : 0.0f;
            halfData[static_cast<size_t>(pz) * dstDim + px] = floatToHalf(hAvg);
        }
    }

    const VkDeviceSize byteSize = pixelCount * sizeof(uint16_t);

    // Staging buffer
    VkBufferCreateInfo bufInfo{};
    bufInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufInfo.size = byteSize;
    bufInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VkBuffer staging{VK_NULL_HANDLE};
    if (vkCreateBuffer(m_device, &bufInfo, nullptr, &staging) != VK_SUCCESS) {
        std::cerr << "[ShadowSystem] uploadSkyHeightmap: staging buffer create failed\n";
        return;
    }
    VkMemoryRequirements memReqs{};
    vkGetBufferMemoryRequirements(m_device, staging, &memReqs);
    VkMemoryAllocateInfo memAlloc{};
    memAlloc.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    memAlloc.allocationSize = memReqs.size;
    memAlloc.memoryTypeIndex = VulkanHelpers::findMemoryType(
        m_physicalDevice, memReqs.memoryTypeBits,
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    VkDeviceMemory stagingMem{VK_NULL_HANDLE};
    if (vkAllocateMemory(m_device, &memAlloc, nullptr, &stagingMem) != VK_SUCCESS) {
        vkDestroyBuffer(m_device, staging, nullptr);
        std::cerr << "[ShadowSystem] uploadSkyHeightmap: staging memory alloc failed\n";
        return;
    }
    vkBindBufferMemory(m_device, staging, stagingMem, 0);
    void* mapped = nullptr;
    vkMapMemory(m_device, stagingMem, 0, byteSize, 0, &mapped);
    std::memcpy(mapped, halfData.data(), byteSize);
    vkUnmapMemory(m_device, stagingMem);

    // One-shot command buffer: SHADER_READ_ONLY -> TRANSFER_DST -> copy -> SHADER_READ_ONLY
    VkCommandBufferAllocateInfo cmdAlloc{};
    cmdAlloc.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    cmdAlloc.commandPool = m_commandPool;
    cmdAlloc.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    cmdAlloc.commandBufferCount = 1;
    VkCommandBuffer cmd{VK_NULL_HANDLE};
    vkAllocateCommandBuffers(m_device, &cmdAlloc, &cmd);
    VkCommandBufferBeginInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkBeginCommandBuffer(cmd, &bi);

    VkImageMemoryBarrier b{};
    b.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    b.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    b.image = m_skyHeightmapImage;
    b.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    b.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    b.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    b.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
    b.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    vkCmdPipelineBarrier(cmd,
        VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
        0, 0, nullptr, 0, nullptr, 1, &b);

    VkBufferImageCopy region{};
    region.imageSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };
    region.imageExtent = { dstDim, dstDim, 1 };
    vkCmdCopyBufferToImage(cmd, staging, m_skyHeightmapImage,
        VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

    b.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    b.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    b.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    b.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    vkCmdPipelineBarrier(cmd,
        VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
        0, 0, nullptr, 0, nullptr, 1, &b);

    vkEndCommandBuffer(cmd);
    VkSubmitInfo si{};
    si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    si.commandBufferCount = 1;
    si.pCommandBuffers = &cmd;
    vkQueueSubmit(m_graphicsQueue, 1, &si, VK_NULL_HANDLE);
    vkQueueWaitIdle(m_graphicsQueue);
    vkFreeCommandBuffers(m_device, m_commandPool, 1, &cmd);

    vkDestroyBuffer(m_device, staging, nullptr);
    vkFreeMemory(m_device, stagingMem, nullptr);

    m_skyHeightmapInitialized = true;
    std::cout << "[ShadowSystem] Sky heightmap uploaded ("
              << srcW << "x" << srcH << " -> " << dstDim << "x" << dstDim
              << ", " << metersPerTexel << " m/texel, world span "
              << worldSizeXMeters << "x" << worldSizeZMeters << " m)\n";
}

ShadowSystem::SunShadowRuntimeConfig ShadowSystem::estimateSunShadowRuntimeConfig(
    float budgetMB,
    float texelsPerVoxel,
    uint32_t cascadeCount,
    float cascadeScale,
    float shadowAreaRadius) const {
    SunShadowRuntimeConfig cfg{};
    cfg.budgetMB = std::max(budgetMB, SUN_MIN_BUDGET_MB);
    const float requestedTexelsPerVoxel =
        std::clamp(texelsPerVoxel, SUN_MIN_TEXELS_PER_VOXEL, SUN_MAX_TEXELS_PER_VOXEL);
    cfg.cascadeCount = std::clamp(cascadeCount, SUN_MIN_CASCADE_COUNT, SUN_MAX_CASCADE_COUNT);
    cfg.cascadeScale = std::clamp(cascadeScale, SUN_MIN_CASCADE_SCALE, SUN_MAX_CASCADE_SCALE);
    // Cascade 0 half-extent tracks the area radius down to a floor, capped
    // at the legacy default. This is the HD ring: budget-derived map dims
    // are sized to this extent at the requested texel density, so a smaller
    // visible radius gives more pixels per metre rather than wasting them
    // on a 64m ring the user can't see.
    float dynHalf = SUN_DEFAULT_HALF_EXTENT_METERS;
    if (shadowAreaRadius > 0.0f) {
        dynHalf = std::clamp(shadowAreaRadius,
                             SUN_MIN_HALF_EXTENT_METERS,
                             SUN_DEFAULT_HALF_EXTENT_METERS);
    }
    cfg.halfExtentMeters = dynHalf;
    cfg.texelMeters = SUN_VOXEL_SIZE_METERS / requestedTexelsPerVoxel;

    const double budgetBytes = static_cast<double>(cfg.budgetMB) * 1024.0 * 1024.0;
    const double perCascadeBytes = budgetBytes / static_cast<double>(cfg.cascadeCount);
    const double maxDimFromBudget = std::sqrt(std::max(perCascadeBytes / 4.0, 1.0));
    const double targetDimFromRequestedTexel =
        (2.0 * static_cast<double>(cfg.halfExtentMeters)) / static_cast<double>(cfg.texelMeters);
    uint32_t mapSize = static_cast<uint32_t>(std::floor(std::min(maxDimFromBudget, targetDimFromRequestedTexel)));
    uint32_t maxDim = m_maxSunShadowMapDimension;
    if (maxDim == 0u) {
        maxDim = SUN_DEFAULT_MAP_SIZE;
    }
    mapSize = std::min(mapSize, maxDim);
    mapSize = std::max<uint32_t>(mapSize, 256u);

    // Keep dimensions coarse-aligned for predictable allocations on low-end GPUs.
    mapSize = (mapSize / 256u) * 256u;
    if (mapSize == 0u) {
        mapSize = std::min<uint32_t>(maxDim, 256u);
    }
    if (mapSize > maxDim) {
        mapSize = maxDim;
    }

    cfg.mapSize = mapSize;
    // Keep the requested texel density (texelsPerVoxel) honored exactly:
    // if the budget-derived map dimension is smaller than what's needed
    // for the requested halfExtent at that density, SHRINK halfExtent to
    // preserve the texel size rather than letting the texel grow (which
    // would silently degrade the shadow grain from 16×16 to 8×8 etc.).
    const float requestedTexelMeters = SUN_VOXEL_SIZE_METERS / requestedTexelsPerVoxel;
    const float maxHalfExtentForTexel =
        0.5f * static_cast<float>(cfg.mapSize) * requestedTexelMeters;
    if (cfg.halfExtentMeters > maxHalfExtentForTexel) {
        cfg.halfExtentMeters = std::max(maxHalfExtentForTexel, SUN_MIN_HALF_EXTENT_METERS);
    }
    cfg.texelMeters = (2.0f * cfg.halfExtentMeters) / static_cast<float>(cfg.mapSize);
    cfg.texelsPerVoxel = SUN_VOXEL_SIZE_METERS / cfg.texelMeters;
    cfg.maxCastRadiusMeters = cfg.halfExtentMeters *
        std::pow(cfg.cascadeScale, static_cast<float>(cfg.cascadeCount - 1u));
    cfg.perCascadeMemoryMB =
        (static_cast<float>(cfg.mapSize) * static_cast<float>(cfg.mapSize) * 4.0f) /
        (1024.0f * 1024.0f);
    cfg.actualMemoryMB = cfg.perCascadeMemoryMB * static_cast<float>(cfg.cascadeCount);
    return cfg;
}

bool ShadowSystem::applySunShadowProfile(bool forceRecreate) {
    const SunShadowRuntimeConfig desired = estimateSunShadowRuntimeConfig(
        m_sunShadowConfig.sunShadowBudgetMB,
        m_sunShadowConfig.sunTexelsPerVoxel,
        m_sunShadowConfig.sunCascadeCount,
        m_sunShadowConfig.sunCascadeScale,
        m_sunShadowConfig.shadowAreaRadius);
    const bool profileChanged =
        desired.mapSize != m_sunRuntimeConfig.mapSize ||
        desired.cascadeCount != m_sunRuntimeConfig.cascadeCount ||
        std::abs(desired.cascadeScale - m_sunRuntimeConfig.cascadeScale) > 1e-5f ||
        std::abs(desired.texelMeters - m_sunRuntimeConfig.texelMeters) > 1e-6f ||
        std::abs(desired.halfExtentMeters - m_sunRuntimeConfig.halfExtentMeters) > 1e-5f ||
        std::abs(desired.maxCastRadiusMeters - m_sunRuntimeConfig.maxCastRadiusMeters) > 1e-4f;

    m_sunRuntimeConfig = desired;
    m_activeSunCascadeCount = m_sunRuntimeConfig.cascadeCount;

    if (!m_initialized) {
        return profileChanged || forceRecreate;
    }
    if (!profileChanged && !forceRecreate) {
        return false;
    }
    if (m_device == VK_NULL_HANDLE || m_commandPool == VK_NULL_HANDLE || m_graphicsQueue == VK_NULL_HANDLE) {
        return false;
    }

    vkDeviceWaitIdle(m_device);

    for (VkFramebuffer fb : m_sunShadowFramebuffers) {
        vkDestroyFramebuffer(m_device, fb, nullptr);
    }
    m_sunShadowFramebuffers.clear();
    for (VkFramebuffer fb : m_sunShadowLoadFramebuffers) {
        vkDestroyFramebuffer(m_device, fb, nullptr);
    }
    m_sunShadowLoadFramebuffers.clear();
    for (VkImageView layerView : m_sunShadowLayerViews) {
        vkDestroyImageView(m_device, layerView, nullptr);
    }
    m_sunShadowLayerViews.clear();
    if (m_sunShadowArrayView != VK_NULL_HANDLE) {
        vkDestroyImageView(m_device, m_sunShadowArrayView, nullptr);
        m_sunShadowArrayView = VK_NULL_HANDLE;
    }
    if (m_sunShadowImage != VK_NULL_HANDLE) {
        vkDestroyImage(m_device, m_sunShadowImage, nullptr);
        m_sunShadowImage = VK_NULL_HANDLE;
    }
    if (m_sunShadowMemory != VK_NULL_HANDLE) {
        vkFreeMemory(m_device, m_sunShadowMemory, nullptr);
        m_sunShadowMemory = VK_NULL_HANDLE;
    }
    destroySunShadowScrollScratch();

    createSunShadowImageAndView();
    createSunShadowFramebuffers();
    transitionSunImageToSampled(m_commandPool, m_graphicsQueue);

    m_sunShadowCache = {};
    m_sunLightVP = glm::mat4(1.0f);
    m_sunCascadeVP = {};
    m_sunCascadeHalfExtents = {};
    m_sunCascadeTexelMeters = {};
    m_sunTexelLogPrevValid = false;

    std::cout << "[ShadowSystem] Applied sun profile: cascades=" << m_sunRuntimeConfig.cascadeCount
              << " map=" << m_sunRuntimeConfig.mapSize
              << " texels/voxel=" << m_sunRuntimeConfig.texelsPerVoxel
              << " nearHalfExtent=" << m_sunRuntimeConfig.halfExtentMeters << "m"
              << " maxCast=" << m_sunRuntimeConfig.maxCastRadiusMeters << "m"
              << " mem=" << m_sunRuntimeConfig.actualMemoryMB << "MB" << std::endl;
    return true;
}

````

## src\rendering\lighting\ShadowSystemResources.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include "rendering/common/VulkanHelpers.h"
#include "rendering/common/Mesh.h"
#include "rendering/lighting/LightingSettings.h"

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <vector>

// Resource creation/destruction functions - extracted from ShadowSystem.cpp

namespace {

constexpr uint32_t kInvalidSourceIndex = std::numeric_limits<uint32_t>::max();

void createBuffer(VkDevice device,
                  VkPhysicalDevice physicalDevice,
                  VkDeviceSize size,
                  VkBufferUsageFlags usage,
                  VkMemoryPropertyFlags properties,
                  VkBuffer& outBuffer,
                  VkDeviceMemory& outMemory) {
    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = size;
    bufferInfo.usage = usage;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

    if (vkCreateBuffer(device, &bufferInfo, nullptr, &outBuffer) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create buffer");
    }

    VkMemoryRequirements memRequirements{};
    vkGetBufferMemoryRequirements(device, outBuffer, &memRequirements);

    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memRequirements.size;
    allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
        physicalDevice,
        memRequirements.memoryTypeBits,
        properties);

    if (vkAllocateMemory(device, &allocInfo, nullptr, &outMemory) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to allocate buffer memory");
    }

    vkBindBufferMemory(device, outBuffer, outMemory, 0);
}

void createImage(VkDevice device,
                 VkPhysicalDevice physicalDevice,
                 uint32_t width,
                 uint32_t height,
                 uint32_t layers,
                 VkFormat format,
                 VkImageUsageFlags usage,
                 VkImageCreateFlags flags,
                 VkImage& outImage,
                 VkDeviceMemory& outMemory) {
    VkImageCreateInfo imageInfo{};
    imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imageInfo.imageType = VK_IMAGE_TYPE_2D;
    imageInfo.extent.width = width;
    imageInfo.extent.height = height;
    imageInfo.extent.depth = 1;
    imageInfo.mipLevels = 1;
    imageInfo.arrayLayers = layers;
    imageInfo.format = format;
    imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
    imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    imageInfo.usage = usage;
    imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    imageInfo.flags = flags;

    if (vkCreateImage(device, &imageInfo, nullptr, &outImage) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create image");
    }

    VkMemoryRequirements memRequirements{};
    vkGetImageMemoryRequirements(device, outImage, &memRequirements);

    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memRequirements.size;
    allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
        physicalDevice,
        memRequirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

    if (vkAllocateMemory(device, &allocInfo, nullptr, &outMemory) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to allocate image memory");
    }

    vkBindImageMemory(device, outImage, outMemory, 0);
}

} // namespace

void ShadowSystem::createPerImageBuffers(uint32_t swapchainImageCount) {
    m_shadowDataBuffers.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_shadowDataMemories.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_shadowDataMapped.resize(swapchainImageCount, nullptr);
    m_sunLocalIndirectBuffers.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_sunLocalIndirectMemories.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_sunLocalOriginsBuffers.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_sunLocalOriginsMemories.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_sunLocalUploadBuffers.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_sunLocalUploadMemories.resize(swapchainImageCount, VK_NULL_HANDLE);
    m_sunLocalUploadMapped.resize(swapchainImageCount, nullptr);

    const VkDeviceSize dataSize = sizeof(ShadowGPUData);
    const VkDeviceSize sunIndirectBytes =
        sizeof(VkDrawIndexedIndirectCommand) * SUN_LOCAL_INDIRECT_TOTAL_DRAWS;
    const VkDeviceSize sunOriginsBytes =
        sizeof(glm::vec4) * SUN_LOCAL_INDIRECT_TOTAL_DRAWS;
    const VkDeviceSize sunUploadBytes = sunIndirectBytes + sunOriginsBytes;
    for (uint32_t i = 0; i < swapchainImageCount; ++i) {
        createBuffer(
            m_device,
            m_physicalDevice,
            dataSize,
            VK_BUFFER_USAGE_STORAGE_BUFFER_BIT,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            m_shadowDataBuffers[i],
            m_shadowDataMemories[i]);

        vkMapMemory(m_device, m_shadowDataMemories[i], 0, dataSize, 0, &m_shadowDataMapped[i]);
        std::memset(m_shadowDataMapped[i], 0, static_cast<size_t>(dataSize));

        createBuffer(
            m_device,
            m_physicalDevice,
            sunIndirectBytes,
            VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT,
            VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
            m_sunLocalIndirectBuffers[i],
            m_sunLocalIndirectMemories[i]);
        createBuffer(
            m_device,
            m_physicalDevice,
            sunOriginsBytes,
            VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT,
            VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
            m_sunLocalOriginsBuffers[i],
            m_sunLocalOriginsMemories[i]);
        createBuffer(
            m_device,
            m_physicalDevice,
            sunUploadBytes,
            VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            m_sunLocalUploadBuffers[i],
            m_sunLocalUploadMemories[i]);
        vkMapMemory(
            m_device,
            m_sunLocalUploadMemories[i],
            0,
            sunUploadBytes,
            0,
            &m_sunLocalUploadMapped[i]);
    }

    m_lightTimingImageCount = swapchainImageCount;
    m_querySourceByImage.resize(swapchainImageCount);
    for (auto& perImageSources : m_querySourceByImage) {
        perImageSources.fill(kInvalidSourceIndex);
    }
    m_queryLightCountByImage.assign(swapchainImageCount, 0u);

    const uint32_t queryCount = swapchainImageCount * MAX_POINT_SHADOW_LIGHTS * 2u;
    if (m_gpuTimingEnabled && queryCount > 0u) {
        VkQueryPoolCreateInfo queryInfo{};
        queryInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        queryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
        queryInfo.queryCount = queryCount;
        if (vkCreateQueryPool(m_device, &queryInfo, nullptr, &m_lightTimingQueryPool) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create per-light timestamp query pool");
        }
    }

    // Sun shadow GPU timing: 2 timestamps (begin/end) per swapchain image.
    m_sunTimingImageCount = swapchainImageCount;
    m_sunTimingWritten.assign(swapchainImageCount, false);
    if (m_gpuTimingEnabled && swapchainImageCount > 0u) {
        VkQueryPoolCreateInfo sunQueryInfo{};
        sunQueryInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        sunQueryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
        sunQueryInfo.queryCount = swapchainImageCount * 2u;
        if (vkCreateQueryPool(m_device, &sunQueryInfo, nullptr, &m_sunTimingQueryPool) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create sun timestamp query pool");
        }
    }
}

void ShadowSystem::destroyPerImageBuffers() {
    if (m_sunTimingQueryPool != VK_NULL_HANDLE) {
        vkDestroyQueryPool(m_device, m_sunTimingQueryPool, nullptr);
        m_sunTimingQueryPool = VK_NULL_HANDLE;
    }
    m_sunTimingImageCount = 0u;
    m_sunTimingWritten.clear();

    if (m_lightTimingQueryPool != VK_NULL_HANDLE) {
        vkDestroyQueryPool(m_device, m_lightTimingQueryPool, nullptr);
        m_lightTimingQueryPool = VK_NULL_HANDLE;
    }
    m_lightTimingImageCount = 0u;
    m_querySourceByImage.clear();
    m_queryLightCountByImage.clear();

    for (size_t i = 0; i < m_shadowDataBuffers.size(); ++i) {
        if (i < m_sunLocalUploadMapped.size() && m_sunLocalUploadMapped[i]) {
            vkUnmapMemory(m_device, m_sunLocalUploadMemories[i]);
        }
        if (i < m_sunLocalUploadBuffers.size() && m_sunLocalUploadBuffers[i]) {
            vkDestroyBuffer(m_device, m_sunLocalUploadBuffers[i], nullptr);
        }
        if (i < m_sunLocalUploadMemories.size() && m_sunLocalUploadMemories[i]) {
            vkFreeMemory(m_device, m_sunLocalUploadMemories[i], nullptr);
        }
        if (i < m_sunLocalOriginsBuffers.size() && m_sunLocalOriginsBuffers[i]) {
            vkDestroyBuffer(m_device, m_sunLocalOriginsBuffers[i], nullptr);
        }
        if (i < m_sunLocalOriginsMemories.size() && m_sunLocalOriginsMemories[i]) {
            vkFreeMemory(m_device, m_sunLocalOriginsMemories[i], nullptr);
        }
        if (i < m_sunLocalIndirectBuffers.size() && m_sunLocalIndirectBuffers[i]) {
            vkDestroyBuffer(m_device, m_sunLocalIndirectBuffers[i], nullptr);
        }
        if (i < m_sunLocalIndirectMemories.size() && m_sunLocalIndirectMemories[i]) {
            vkFreeMemory(m_device, m_sunLocalIndirectMemories[i], nullptr);
        }
        if (m_shadowDataMapped[i]) {
            vkUnmapMemory(m_device, m_shadowDataMemories[i]);
        }
        if (m_shadowDataBuffers[i]) {
            vkDestroyBuffer(m_device, m_shadowDataBuffers[i], nullptr);
        }
        if (m_shadowDataMemories[i]) {
            vkFreeMemory(m_device, m_shadowDataMemories[i], nullptr);
        }
    }

    m_shadowDataBuffers.clear();
    m_shadowDataMemories.clear();
    m_shadowDataMapped.clear();
    m_sunLocalIndirectBuffers.clear();
    m_sunLocalIndirectMemories.clear();
    m_sunLocalOriginsBuffers.clear();
    m_sunLocalOriginsMemories.clear();
    m_sunLocalUploadBuffers.clear();
    m_sunLocalUploadMemories.clear();
    m_sunLocalUploadMapped.clear();
}

void ShadowSystem::createShadowImages() {
    createImage(
        m_device,
        m_physicalDevice,
        POINT_SHADOW_MAP_SIZE,
        POINT_SHADOW_MAP_SIZE,
        MAX_POINT_SHADOW_LIGHTS * 6,
        VK_FORMAT_D32_SFLOAT,
        VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT,
        VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT,
        m_pointShadowImage,
        m_pointShadowMemory);

    VkImageViewCreateInfo cubeArrayViewInfo{};
    cubeArrayViewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    cubeArrayViewInfo.image = m_pointShadowImage;
    cubeArrayViewInfo.viewType = VK_IMAGE_VIEW_TYPE_CUBE_ARRAY;
    cubeArrayViewInfo.format = VK_FORMAT_D32_SFLOAT;
    cubeArrayViewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    cubeArrayViewInfo.subresourceRange.baseMipLevel = 0;
    cubeArrayViewInfo.subresourceRange.levelCount = 1;
    cubeArrayViewInfo.subresourceRange.baseArrayLayer = 0;
    cubeArrayViewInfo.subresourceRange.layerCount = MAX_POINT_SHADOW_LIGHTS * 6;
    if (vkCreateImageView(m_device, &cubeArrayViewInfo, nullptr, &m_pointShadowCubeArrayView) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create point shadow cube-array view");
    }

    m_pointShadowLayerViews.resize(MAX_POINT_SHADOW_LIGHTS * 6, VK_NULL_HANDLE);
    for (uint32_t layer = 0; layer < MAX_POINT_SHADOW_LIGHTS * 6; ++layer) {
        VkImageViewCreateInfo layerViewInfo{};
        layerViewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        layerViewInfo.image = m_pointShadowImage;
        layerViewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        layerViewInfo.format = VK_FORMAT_D32_SFLOAT;
        layerViewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
        layerViewInfo.subresourceRange.baseMipLevel = 0;
        layerViewInfo.subresourceRange.levelCount = 1;
        layerViewInfo.subresourceRange.baseArrayLayer = layer;
        layerViewInfo.subresourceRange.layerCount = 1;
        if (vkCreateImageView(m_device, &layerViewInfo, nullptr, &m_pointShadowLayerViews[layer]) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create point shadow layer view");
        }
    }

    createSunShadowImageAndView();

    VkSamplerCreateInfo samplerInfo{};
    samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    samplerInfo.magFilter = VK_FILTER_NEAREST;
    samplerInfo.minFilter = VK_FILTER_NEAREST;
    samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    samplerInfo.mipLodBias = 0.0f;
    samplerInfo.anisotropyEnable = VK_FALSE;
    samplerInfo.maxAnisotropy = 1.0f;
    samplerInfo.compareEnable = VK_TRUE;
    samplerInfo.compareOp = VK_COMPARE_OP_LESS_OR_EQUAL;
    samplerInfo.minLod = 0.0f;
    samplerInfo.maxLod = 1.0f;
    samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_WHITE;
    samplerInfo.unnormalizedCoordinates = VK_FALSE;
    if (vkCreateSampler(m_device, &samplerInfo, nullptr, &m_shadowSampler) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create shadow sampler");
    }
}

void ShadowSystem::createSunShadowImageAndView() {
    createImage(
        m_device,
        m_physicalDevice,
        m_sunRuntimeConfig.mapSize,
        m_sunRuntimeConfig.mapSize,
        m_sunRuntimeConfig.cascadeCount,
        VK_FORMAT_D32_SFLOAT,
        VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT |
            VK_IMAGE_USAGE_SAMPLED_BIT |
            VK_IMAGE_USAGE_TRANSFER_SRC_BIT |
            VK_IMAGE_USAGE_TRANSFER_DST_BIT,
        0,
        m_sunShadowImage,
        m_sunShadowMemory);

    VkImageViewCreateInfo arrayViewInfo{};
    arrayViewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    arrayViewInfo.image = m_sunShadowImage;
    arrayViewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D_ARRAY;
    arrayViewInfo.format = VK_FORMAT_D32_SFLOAT;
    arrayViewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    arrayViewInfo.subresourceRange.baseMipLevel = 0;
    arrayViewInfo.subresourceRange.levelCount = 1;
    arrayViewInfo.subresourceRange.baseArrayLayer = 0;
    arrayViewInfo.subresourceRange.layerCount = m_sunRuntimeConfig.cascadeCount;
    if (vkCreateImageView(m_device, &arrayViewInfo, nullptr, &m_sunShadowArrayView) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create sun shadow array view");
    }

    m_sunShadowLayerViews.resize(m_sunRuntimeConfig.cascadeCount, VK_NULL_HANDLE);
    for (uint32_t layer = 0; layer < m_sunRuntimeConfig.cascadeCount; ++layer) {
        VkImageViewCreateInfo layerViewInfo{};
        layerViewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        layerViewInfo.image = m_sunShadowImage;
        layerViewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        layerViewInfo.format = VK_FORMAT_D32_SFLOAT;
        layerViewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
        layerViewInfo.subresourceRange.baseMipLevel = 0;
        layerViewInfo.subresourceRange.levelCount = 1;
        layerViewInfo.subresourceRange.baseArrayLayer = layer;
        layerViewInfo.subresourceRange.layerCount = 1;
        if (vkCreateImageView(m_device, &layerViewInfo, nullptr, &m_sunShadowLayerViews[layer]) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create sun shadow layer view");
        }
    }

    createSunShadowScrollScratch();
}

void ShadowSystem::createShadowRenderPass() {
    auto createDepthPass = [&](VkAttachmentLoadOp loadOp,
                               VkImageLayout initialLayout,
                               const char* label,
                               VkRenderPass& outPass) {
        VkAttachmentDescription depthAttachment{};
        depthAttachment.format = VK_FORMAT_D32_SFLOAT;
        depthAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
        depthAttachment.loadOp = loadOp;
        depthAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        depthAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        depthAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        depthAttachment.initialLayout = initialLayout;
        depthAttachment.finalLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;

        VkAttachmentReference depthRef{};
        depthRef.attachment = 0;
        depthRef.layout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

        VkSubpassDescription subpass{};
        subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
        subpass.colorAttachmentCount = 0;
        subpass.pDepthStencilAttachment = &depthRef;

        std::array<VkSubpassDependency, 2> deps{};
        deps[0].srcSubpass = VK_SUBPASS_EXTERNAL;
        deps[0].dstSubpass = 0;
        deps[0].srcStageMask =
            (loadOp == VK_ATTACHMENT_LOAD_OP_LOAD)
                ? (VK_PIPELINE_STAGE_TRANSFER_BIT | VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT)
                : VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
        deps[0].srcAccessMask =
            (loadOp == VK_ATTACHMENT_LOAD_OP_LOAD)
                ? (VK_ACCESS_TRANSFER_WRITE_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT)
                : VK_ACCESS_SHADER_READ_BIT;
        deps[0].dstStageMask =
            VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
        deps[0].dstAccessMask =
            VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
            VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;

        deps[1].srcSubpass = 0;
        deps[1].dstSubpass = VK_SUBPASS_EXTERNAL;
        deps[1].srcStageMask =
            VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT |
            VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
        deps[1].srcAccessMask = VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        deps[1].dstStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
        deps[1].dstAccessMask = VK_ACCESS_SHADER_READ_BIT;

        VkRenderPassCreateInfo rpInfo{};
        rpInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
        rpInfo.attachmentCount = 1;
        rpInfo.pAttachments = &depthAttachment;
        rpInfo.subpassCount = 1;
        rpInfo.pSubpasses = &subpass;
        rpInfo.dependencyCount = static_cast<uint32_t>(deps.size());
        rpInfo.pDependencies = deps.data();

        if (vkCreateRenderPass(m_device, &rpInfo, nullptr, &outPass) != VK_SUCCESS) {
            throw std::runtime_error(label);
        }
    };

    createDepthPass(
        VK_ATTACHMENT_LOAD_OP_CLEAR,
        VK_IMAGE_LAYOUT_UNDEFINED,
        "ShadowSystem: failed to create shadow render pass",
        m_shadowRenderPass);
    createDepthPass(
        VK_ATTACHMENT_LOAD_OP_LOAD,
        VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
        "ShadowSystem: failed to create shadow load render pass",
        m_shadowLoadRenderPass);
}

void ShadowSystem::createShadowFramebuffers() {
    m_shadowFramebuffers.resize(MAX_POINT_SHADOW_LIGHTS * 6, VK_NULL_HANDLE);

    for (uint32_t layer = 0; layer < MAX_POINT_SHADOW_LIGHTS * 6; ++layer) {
        VkImageView attachment = m_pointShadowLayerViews[layer];

        VkFramebufferCreateInfo fbInfo{};
        fbInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        fbInfo.renderPass = m_shadowRenderPass;
        fbInfo.attachmentCount = 1;
        fbInfo.pAttachments = &attachment;
        fbInfo.width = POINT_SHADOW_MAP_SIZE;
        fbInfo.height = POINT_SHADOW_MAP_SIZE;
        fbInfo.layers = 1;

        if (vkCreateFramebuffer(m_device, &fbInfo, nullptr, &m_shadowFramebuffers[layer]) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create shadow framebuffer");
        }
    }

    createSunShadowFramebuffers();
}

void ShadowSystem::createSunShadowFramebuffers() {
    m_sunShadowFramebuffers.resize(m_sunRuntimeConfig.cascadeCount, VK_NULL_HANDLE);
    m_sunShadowLoadFramebuffers.resize(m_sunRuntimeConfig.cascadeCount, VK_NULL_HANDLE);
    for (uint32_t layer = 0; layer < m_sunRuntimeConfig.cascadeCount; ++layer) {
        VkImageView sunAttachment = m_sunShadowLayerViews[layer];
        VkFramebufferCreateInfo sunFbInfo{};
        sunFbInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        sunFbInfo.renderPass = m_shadowRenderPass;
        sunFbInfo.attachmentCount = 1;
        sunFbInfo.pAttachments = &sunAttachment;
        sunFbInfo.width = m_sunRuntimeConfig.mapSize;
        sunFbInfo.height = m_sunRuntimeConfig.mapSize;
        sunFbInfo.layers = 1;
        if (vkCreateFramebuffer(m_device, &sunFbInfo, nullptr, &m_sunShadowFramebuffers[layer]) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create sun shadow framebuffer");
        }

        VkFramebufferCreateInfo sunLoadFbInfo = sunFbInfo;
        sunLoadFbInfo.renderPass = m_shadowLoadRenderPass;
        if (vkCreateFramebuffer(m_device, &sunLoadFbInfo, nullptr, &m_sunShadowLoadFramebuffers[layer]) != VK_SUCCESS) {
            throw std::runtime_error("ShadowSystem: failed to create sun shadow load framebuffer");
        }
    }
}

void ShadowSystem::createSunShadowScrollScratch() {
    destroySunShadowScrollScratch();
    createImage(
        m_device,
        m_physicalDevice,
        m_sunRuntimeConfig.mapSize,
        m_sunRuntimeConfig.mapSize,
        1u,
        VK_FORMAT_D32_SFLOAT,
        VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT,
        0,
        m_sunShadowScrollScratchImage,
        m_sunShadowScrollScratchMemory);
    m_sunShadowScrollScratchLayout = VK_IMAGE_LAYOUT_UNDEFINED;
}

void ShadowSystem::destroySunShadowScrollScratch() {
    if (m_sunShadowScrollScratchImage != VK_NULL_HANDLE) {
        vkDestroyImage(m_device, m_sunShadowScrollScratchImage, nullptr);
        m_sunShadowScrollScratchImage = VK_NULL_HANDLE;
    }
    if (m_sunShadowScrollScratchMemory != VK_NULL_HANDLE) {
        vkFreeMemory(m_device, m_sunShadowScrollScratchMemory, nullptr);
        m_sunShadowScrollScratchMemory = VK_NULL_HANDLE;
    }
    m_sunShadowScrollScratchLayout = VK_IMAGE_LAYOUT_UNDEFINED;
}

void ShadowSystem::createShadowPipelines() {
    const std::vector<char> terrainVertCode = VulkanHelpers::readFile("shaders/shadow/point_shadow_terrain.vert.spv");
    const std::vector<char> fragCode = VulkanHelpers::readFile("shaders/shadow/point_shadow_depth.frag.spv");

    VkShaderModule terrainVert = VulkanHelpers::createShaderModule(m_device, terrainVertCode);
    VkShaderModule frag = VulkanHelpers::createShaderModule(m_device, fragCode);

    VkPipelineShaderStageCreateInfo terrainStages[2]{};
    terrainStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    terrainStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
    terrainStages[0].module = terrainVert;
    terrainStages[0].pName = "main";
    terrainStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    terrainStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    terrainStages[1].module = frag;
    terrainStages[1].pName = "main";

    VkVertexInputBindingDescription binding{};
    binding.binding = 0;
    binding.stride = sizeof(Vertex);
    binding.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    VkVertexInputAttributeDescription attr{};
    attr.location = 0;
    attr.binding = 0;
    attr.format = VK_FORMAT_R32_UINT;
    attr.offset = offsetof(Vertex, packed);

    VkPipelineVertexInputStateCreateInfo vertexInput{};
    vertexInput.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInput.vertexBindingDescriptionCount = 1;
    vertexInput.pVertexBindingDescriptions = &binding;
    vertexInput.vertexAttributeDescriptionCount = 1;
    vertexInput.pVertexAttributeDescriptions = &attr;

    VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
    inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    inputAssembly.primitiveRestartEnable = VK_FALSE;

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

    VkPipelineViewportStateCreateInfo viewportState{};
    viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewportState.viewportCount = 1;
    viewportState.pViewports = &viewport;
    viewportState.scissorCount = 1;
    viewportState.pScissors = &scissor;

    VkPipelineRasterizationStateCreateInfo raster{};
    raster.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    raster.depthClampEnable = VK_FALSE;
    raster.rasterizerDiscardEnable = VK_FALSE;
    raster.polygonMode = VK_POLYGON_MODE_FILL;
    raster.cullMode = VK_CULL_MODE_NONE;
    raster.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    raster.depthBiasEnable = VK_TRUE;
    raster.depthBiasConstantFactor = 1.35f;
    raster.depthBiasSlopeFactor = 1.75f;
    raster.lineWidth = 1.0f;

    VkPipelineMultisampleStateCreateInfo msaa{};
    msaa.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    msaa.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
    msaa.sampleShadingEnable = VK_FALSE;

    VkPipelineDepthStencilStateCreateInfo depthStencil{};
    depthStencil.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
    depthStencil.depthTestEnable = VK_TRUE;
    depthStencil.depthWriteEnable = VK_TRUE;
    depthStencil.depthCompareOp = VK_COMPARE_OP_LESS_OR_EQUAL;
    depthStencil.depthBoundsTestEnable = VK_FALSE;
    depthStencil.stencilTestEnable = VK_FALSE;

    VkPipelineColorBlendStateCreateInfo blend{};
    blend.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    blend.logicOpEnable = VK_FALSE;
    blend.attachmentCount = 0;
    blend.pAttachments = nullptr;

    const std::array<VkDynamicState, 3> dynamicStates = {
        VK_DYNAMIC_STATE_VIEWPORT,
        VK_DYNAMIC_STATE_SCISSOR,
        VK_DYNAMIC_STATE_DEPTH_BIAS
    };
    VkPipelineDynamicStateCreateInfo dynamicInfo{};
    dynamicInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamicInfo.dynamicStateCount = static_cast<uint32_t>(dynamicStates.size());
    dynamicInfo.pDynamicStates = dynamicStates.data();

    VkPushConstantRange pushRange{};
    pushRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
    pushRange.offset = 0;
    pushRange.size = sizeof(ShadowPushConstants);

    VkPipelineLayoutCreateInfo terrainLayoutInfo{};
    terrainLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    terrainLayoutInfo.setLayoutCount = 1;
    terrainLayoutInfo.pSetLayouts = &m_mainDescriptorSetLayout;
    terrainLayoutInfo.pushConstantRangeCount = 1;
    terrainLayoutInfo.pPushConstantRanges = &pushRange;
    if (vkCreatePipelineLayout(m_device, &terrainLayoutInfo, nullptr, &m_terrainShadowPipelineLayout) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create terrain shadow pipeline layout");
    }

    VkGraphicsPipelineCreateInfo terrainPipeInfo{};
    terrainPipeInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    terrainPipeInfo.stageCount = 2;
    terrainPipeInfo.pStages = terrainStages;
    terrainPipeInfo.pVertexInputState = &vertexInput;
    terrainPipeInfo.pInputAssemblyState = &inputAssembly;
    terrainPipeInfo.pViewportState = &viewportState;
    terrainPipeInfo.pRasterizationState = &raster;
    terrainPipeInfo.pMultisampleState = &msaa;
    terrainPipeInfo.pDepthStencilState = &depthStencil;
    terrainPipeInfo.pColorBlendState = &blend;
    terrainPipeInfo.pDynamicState = &dynamicInfo;
    terrainPipeInfo.layout = m_terrainShadowPipelineLayout;
    terrainPipeInfo.renderPass = m_shadowRenderPass;
    terrainPipeInfo.subpass = 0;

    if (vkCreateGraphicsPipelines(m_device, VK_NULL_HANDLE, 1, &terrainPipeInfo, nullptr, &m_terrainShadowPipeline) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create terrain shadow pipeline");
    }

    // Sun shadow pipeline: same vertex shader, directional depth fragment, dynamic viewport/scissor
    const std::vector<char> sunFragCode = VulkanHelpers::readFile("shaders/shadow/directional_shadow_depth.frag.spv");
    VkShaderModule sunFrag = VulkanHelpers::createShaderModule(m_device, sunFragCode);

    VkPipelineShaderStageCreateInfo sunStages[2]{};
    sunStages[0] = terrainStages[0]; // same vertex shader
    sunStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    sunStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    sunStages[1].module = sunFrag;
    sunStages[1].pName = "main";

    // Sun shadow uses runtime directional-map size viewport
    VkViewport sunViewport{};
    sunViewport.x = 0.0f;
    sunViewport.y = 0.0f;
    sunViewport.width = static_cast<float>(m_sunRuntimeConfig.mapSize);
    sunViewport.height = static_cast<float>(m_sunRuntimeConfig.mapSize);
    sunViewport.minDepth = 0.0f;
    sunViewport.maxDepth = 1.0f;

    VkRect2D sunScissor{};
    sunScissor.offset = {0, 0};
    sunScissor.extent = {m_sunRuntimeConfig.mapSize, m_sunRuntimeConfig.mapSize};

    VkPipelineViewportStateCreateInfo sunViewportState{};
    sunViewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    sunViewportState.viewportCount = 1;
    sunViewportState.pViewports = &sunViewport;
    sunViewportState.scissorCount = 1;
    sunViewportState.pScissors = &sunScissor;

    VkGraphicsPipelineCreateInfo sunPipeInfo = terrainPipeInfo;
    sunPipeInfo.pStages = sunStages;
    sunPipeInfo.pViewportState = &sunViewportState;

    if (vkCreateGraphicsPipelines(m_device, VK_NULL_HANDLE, 1, &sunPipeInfo, nullptr, &m_sunShadowPipeline) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to create sun shadow pipeline");
    }

    vkDestroyShaderModule(m_device, sunFrag, nullptr);

    vkDestroyShaderModule(m_device, terrainVert, nullptr);
    vkDestroyShaderModule(m_device, frag, nullptr);
}

void ShadowSystem::transitionImagesToSampled(VkCommandPool commandPool, VkQueue queue) {
    VkCommandBufferAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocInfo.commandPool = commandPool;
    allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    allocInfo.commandBufferCount = 1;

    VkCommandBuffer cmd = VK_NULL_HANDLE;
    if (vkAllocateCommandBuffers(m_device, &allocInfo, &cmd) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to allocate transition command buffer");
    }

    VkCommandBufferBeginInfo beginInfo{};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkBeginCommandBuffer(cmd, &beginInfo);

    std::array<VkImageMemoryBarrier2, 2> barriers{};
    barriers[0].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    barriers[0].srcStageMask = VK_PIPELINE_STAGE_2_NONE;
    barriers[0].srcAccessMask = VK_ACCESS_2_NONE;
    barriers[0].dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
    barriers[0].dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barriers[0].oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    barriers[0].newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    barriers[0].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barriers[0].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barriers[0].image = m_pointShadowImage;
    barriers[0].subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    barriers[0].subresourceRange.baseMipLevel = 0;
    barriers[0].subresourceRange.levelCount = 1;
    barriers[0].subresourceRange.baseArrayLayer = 0;
    barriers[0].subresourceRange.layerCount = MAX_POINT_SHADOW_LIGHTS * 6;

    barriers[1] = barriers[0];
    barriers[1].image = m_sunShadowImage;
    barriers[1].subresourceRange.layerCount = m_sunRuntimeConfig.cascadeCount;

    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.imageMemoryBarrierCount = static_cast<uint32_t>(barriers.size());
    depInfo.pImageMemoryBarriers = barriers.data();
    vkCmdPipelineBarrier2(cmd, &depInfo);

    vkEndCommandBuffer(cmd);

    VkSubmitInfo submitInfo{};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &cmd;
    vkQueueSubmit(queue, 1, &submitInfo, VK_NULL_HANDLE);
    vkQueueWaitIdle(queue);

    vkFreeCommandBuffers(m_device, commandPool, 1, &cmd);
}

void ShadowSystem::transitionSunImageToSampled(VkCommandPool commandPool, VkQueue queue) {
    VkCommandBufferAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocInfo.commandPool = commandPool;
    allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    allocInfo.commandBufferCount = 1;

    VkCommandBuffer cmd = VK_NULL_HANDLE;
    if (vkAllocateCommandBuffers(m_device, &allocInfo, &cmd) != VK_SUCCESS) {
        throw std::runtime_error("ShadowSystem: failed to allocate sun-transition command buffer");
    }

    VkCommandBufferBeginInfo beginInfo{};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkBeginCommandBuffer(cmd, &beginInfo);

    VkImageMemoryBarrier2 barrier{};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    barrier.srcStageMask = VK_PIPELINE_STAGE_2_NONE;
    barrier.srcAccessMask = VK_ACCESS_2_NONE;
    barrier.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
    barrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    barrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    barrier.newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.image = m_sunShadowImage;
    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    barrier.subresourceRange.baseMipLevel = 0;
    barrier.subresourceRange.levelCount = 1;
    barrier.subresourceRange.baseArrayLayer = 0;
    barrier.subresourceRange.layerCount = m_sunRuntimeConfig.cascadeCount;

    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.imageMemoryBarrierCount = 1;
    depInfo.pImageMemoryBarriers = &barrier;
    vkCmdPipelineBarrier2(cmd, &depInfo);

    vkEndCommandBuffer(cmd);

    VkSubmitInfo submitInfo{};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &cmd;
    vkQueueSubmit(queue, 1, &submitInfo, VK_NULL_HANDLE);
    vkQueueWaitIdle(queue);

    vkFreeCommandBuffers(m_device, commandPool, 1, &cmd);
}

````

## src\rendering\lighting\ShadowDiagnostics.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <limits>
#include <vector>

namespace {

constexpr uint32_t kInvalidSourceIndex = std::numeric_limits<uint32_t>::max();
constexpr uint32_t kPointShadowDepthComparesPerSample = 25u; // 5 taps * 5 sub-samples

} // namespace

void ShadowSystem::collectGpuTimings(uint32_t imageIndex) {
    // Always collect sun timing first (even if point lights are empty).
    collectSunGpuTiming(imageIndex);

    // Keep terrain pass cost fresh even when point-light timing is unavailable.
    m_frameDiagnostics.terrainPassGpuMs = m_lastTerrainPassGpuMs;

    if (!m_initialized ||
        m_lightTimingQueryPool == VK_NULL_HANDLE ||
        imageIndex >= m_lightTimingImageCount ||
        imageIndex >= m_queryLightCountByImage.size() ||
        imageIndex >= m_querySourceByImage.size()) {
        return;
    }

    const uint32_t lightCount = m_queryLightCountByImage[imageIndex];
    auto clearPointShadowFrameDiagnostics = [this]() {
        m_frameDiagnostics.totalShadowGpuMs = 0.0f;
        m_frameDiagnostics.avgShadowGpuMsPerLight = 0.0f;
        m_frameDiagnostics.terrainMsPerMegaShadowSample = 0.0f;
        m_frameDiagnostics.totalPointShadowSamples = 0u;
        m_frameDiagnostics.totalPointLightEvaluations = 0u;
        m_frameDiagnostics.totalPointShadowFullyOccluded = 0u;
        m_frameDiagnostics.totalPointShadowLitContrib = 0u;
        m_frameDiagnostics.totalEstimatedShadowDepthCompareOps = 0u;
        m_frameDiagnostics.detailedCountersEnabled = m_detailedDiagnosticsEnabled;
    };

    if (lightCount == 0u) {
        clearPointShadowFrameDiagnostics();
        return;
    }

    // Do not ever let diagnostics become a hidden frame stall. The frame-image
    // fence normally means these queries are ready, but WITH_AVAILABILITY makes
    // the collector safe if it is called earlier during future refactors or in
    // perf mode. If any timestamp pair is not ready, keep the last published
    // diagnostics and try again on a later image reuse.
    std::vector<uint64_t> queryData(lightCount * 2u * 2u, 0u);
    const uint32_t queryBase = imageIndex * (MAX_POINT_SHADOW_LIGHTS * 2u);
    VkResult res = vkGetQueryPoolResults(
        m_device,
        m_lightTimingQueryPool,
        queryBase,
        lightCount * 2u,
        static_cast<VkDeviceSize>(queryData.size() * sizeof(uint64_t)),
        queryData.data(),
        sizeof(uint64_t) * 2u,
        VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);

    if (res != VK_SUCCESS && res != VK_NOT_READY) {
        return;
    }

    for (uint32_t query = 0; query < lightCount * 2u; ++query) {
        if (queryData[query * 2u + 1u] == 0u) {
            return;
        }
    }

    clearPointShadowFrameDiagnostics();

    const ShadowGPUData* shadowData = nullptr;
    if (imageIndex < m_shadowDataMapped.size()) {
        shadowData = reinterpret_cast<const ShadowGPUData*>(m_shadowDataMapped[imageIndex]);
    }

    uint32_t timedLights = 0u;
    for (uint32_t slot = 0; slot < lightCount; ++slot) {
        const uint32_t source = m_querySourceByImage[imageIndex][slot];
        if (source == kInvalidSourceIndex || source >= m_lightDiagnostics.size()) {
            continue;
        }

        const uint64_t start = queryData[(slot * 2u + 0u) * 2u];
        const uint64_t end = queryData[(slot * 2u + 1u) * 2u];
        float gpuMs = 0.0f;
        if (end > start && m_timestampPeriod > 0.0f) {
            gpuMs = static_cast<float>(
                static_cast<double>(end - start) *
                static_cast<double>(m_timestampPeriod) / 1'000'000.0);
        }

        auto& diag = m_lightDiagnostics[source];
        ++timedLights;
        m_frameDiagnostics.totalShadowGpuMs += gpuMs;
        diag.gpuShadowMs = gpuMs;
        if (diag.gpuShadowMsAvg <= 0.0001f) {
            diag.gpuShadowMsAvg = gpuMs;
        } else {
            diag.gpuShadowMsAvg = diag.gpuShadowMsAvg * 0.85f + gpuMs * 0.15f;
        }
        diag.terrainPassGpuMs = m_lastTerrainPassGpuMs;

        if (shadowData) {
            const glm::uvec4 c = shadowData->pointShadowDiag[slot];
            diag.pointShadowSamples = c.x;
            diag.pointLightEvaluations = c.y;
            diag.pointShadowFullyOccluded = c.z;
            diag.pointShadowLitContrib = c.w;
            m_frameDiagnostics.totalPointShadowSamples += c.x;
            m_frameDiagnostics.totalPointLightEvaluations += c.y;
            m_frameDiagnostics.totalPointShadowFullyOccluded += c.z;
            m_frameDiagnostics.totalPointShadowLitContrib += c.w;
        } else {
            diag.pointShadowSamples = 0u;
            diag.pointLightEvaluations = 0u;
            diag.pointShadowFullyOccluded = 0u;
            diag.pointShadowLitContrib = 0u;
        }

        diag.estShadowDepthCompareOps =
            static_cast<uint64_t>(diag.pointShadowSamples) * kPointShadowDepthComparesPerSample;
        m_frameDiagnostics.totalEstimatedShadowDepthCompareOps += diag.estShadowDepthCompareOps;

        if (diag.pointShadowSamples > 0u) {
            const float invSamples = 1.0f / static_cast<float>(diag.pointShadowSamples);
            diag.shadowOccludedRatio = static_cast<float>(diag.pointShadowFullyOccluded) * invSamples;
            diag.shadowLitRatio = static_cast<float>(diag.pointShadowLitContrib) * invSamples;
            diag.evalToSampleRatio = static_cast<float>(diag.pointLightEvaluations) * invSamples;
            diag.gpuMsPerMegaShadowSample =
                gpuMs * 1'000'000.0f / static_cast<float>(diag.pointShadowSamples);
        } else {
            diag.shadowOccludedRatio = 0.0f;
            diag.shadowLitRatio = 0.0f;
            diag.evalToSampleRatio = 0.0f;
            diag.gpuMsPerMegaShadowSample = 0.0f;
        }
    }

    if (timedLights > 0u) {
        m_frameDiagnostics.avgShadowGpuMsPerLight =
            m_frameDiagnostics.totalShadowGpuMs / static_cast<float>(timedLights);
    }

    const float terrainPassMs = std::max(0.0f, m_lastTerrainPassGpuMs);
    if (m_frameDiagnostics.totalPointShadowSamples > 0u) {
        m_frameDiagnostics.terrainMsPerMegaShadowSample =
            terrainPassMs * 1'000'000.0f /
            static_cast<float>(m_frameDiagnostics.totalPointShadowSamples);
    } else {
        m_frameDiagnostics.terrainMsPerMegaShadowSample = 0.0f;
    }
    constexpr float kShadowSamplingWeight = 0.65f;
    constexpr float kLightingEvalWeight = 0.35f;
    const float totalSamples =
        static_cast<float>(std::max<uint64_t>(m_frameDiagnostics.totalPointShadowSamples, 1u));
    const float totalEvals =
        static_cast<float>(std::max<uint64_t>(m_frameDiagnostics.totalPointLightEvaluations, 1u));

    for (uint32_t slot = 0; slot < lightCount; ++slot) {
        const uint32_t source = m_querySourceByImage[imageIndex][slot];
        if (source == kInvalidSourceIndex || source >= m_lightDiagnostics.size()) {
            continue;
        }

        auto& diag = m_lightDiagnostics[source];
        const float sampleShare = static_cast<float>(diag.pointShadowSamples) / totalSamples;
        const float evalShare = static_cast<float>(diag.pointLightEvaluations) / totalEvals;
        const float weightedShare = sampleShare * kShadowSamplingWeight + evalShare * kLightingEvalWeight;

        diag.estTerrainLightingShareMs = terrainPassMs * weightedShare;
        diag.estShadowSamplingMs = terrainPassMs * sampleShare * kShadowSamplingWeight;
        diag.estShadowOffDeltaMs = diag.gpuShadowMs + diag.estShadowSamplingMs;
        diag.estLightOffDeltaMs = diag.estShadowOffDeltaMs +
                                  diag.estTerrainLightingShareMs;
    }
}

void ShadowSystem::setFrameGpuPassCosts(float terrainPassGpuMs) {
    m_lastTerrainPassGpuMs = std::max(terrainPassGpuMs, 0.0f);
    m_frameDiagnostics.terrainPassGpuMs = m_lastTerrainPassGpuMs;
}

const ShadowSystem::LightDiagnostics* ShadowSystem::getLightDiagnostics(uint32_t sourceLightIndex) const {
    if (sourceLightIndex >= m_lightDiagnostics.size()) {
        return nullptr;
    }
    return &m_lightDiagnostics[sourceLightIndex];
}

// ═══════════════════════════════════════════════════════════════════════
// Sun shadow diagnostics — GPU readback + rolling window
// ═══════════════════════════════════════════════════════════════════════

void ShadowSystem::collectSunGpuTiming(uint32_t imageIndex) {
    // Read GPU timestamps for the sun shadow pass, then push the
    // completed frame sample into the rolling history. Query reads must be
    // availability-checked so a diagnostic sample never fabricates a 0.0ms
    // shadow pass just because the timestamp pair was not ready yet.
    float gpuMs = 0.0f;

    if (m_sunTimingQueryPool != VK_NULL_HANDLE &&
        imageIndex < m_sunTimingImageCount &&
        imageIndex < m_sunTimingWritten.size() &&
        m_sunTimingWritten[imageIndex]) {

        uint64_t queryData[4] = {};
        VkResult res = vkGetQueryPoolResults(
            m_device,
            m_sunTimingQueryPool,
            imageIndex * 2u,
            2u,
            sizeof(queryData),
            queryData,
            sizeof(uint64_t) * 2u,
            VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);

        if (res != VK_SUCCESS && res != VK_NOT_READY) {
            return;
        }
        if (queryData[1] == 0u || queryData[3] == 0u) {
            return;
        }

        const uint64_t start = queryData[0];
        const uint64_t end = queryData[2];
        if (end > start && m_timestampPeriod > 0.0f) {
            gpuMs = static_cast<float>(
                static_cast<double>(end - start) *
                static_cast<double>(m_timestampPeriod) / 1'000'000.0);
        }
    }

    m_sunCurrentFrame.gpuRenderMs = gpuMs;
    // VP compute was already filled in updateForFrame; terrain+record in recordShadowPasses.
    // cpuTotalMs should include VP compute as well.
    m_sunCurrentFrame.cpuTotalMs += m_sunCurrentFrame.cpuVpComputeMs;

    pushSunSample(m_sunCurrentFrame);
}

void ShadowSystem::pushSunSample(const SunShadowFrameSample& sample) {
    SunShadowTimedSample ts;
    ts.timestamp = std::chrono::steady_clock::now();
    ts.data = sample;
    m_sunSampleHistory.push_back(ts);

    // Evict samples older than the window
    const auto cutoff = ts.timestamp -
        std::chrono::milliseconds(static_cast<int64_t>(m_sunDiagnostics.windowSeconds * 1000.0f));
    while (!m_sunSampleHistory.empty() && m_sunSampleHistory.front().timestamp < cutoff) {
        m_sunSampleHistory.pop_front();
    }

    recomputeSunWindowStats();
}

void ShadowSystem::recomputeSunWindowStats() {
    const uint32_t n = static_cast<uint32_t>(m_sunSampleHistory.size());
    m_sunDiagnostics.sampleCount = n;

    if (n == 0u) {
        m_sunDiagnostics.latest = {};
        m_sunDiagnostics.avgCpuVpComputeMs = 0.0f;
        m_sunDiagnostics.avgCpuTerrainGatherMs = 0.0f;
        m_sunDiagnostics.avgCpuWorldGatherMs = 0.0f;
        m_sunDiagnostics.avgCpuTerrainHashMs = 0.0f;
        m_sunDiagnostics.avgCpuCacheDecisionMs = 0.0f;
        m_sunDiagnostics.avgCpuCommandRecordMs = 0.0f;
        m_sunDiagnostics.avgCpuTotalMs = 0.0f;
        m_sunDiagnostics.maxCpuVpComputeMs = 0.0f;
        m_sunDiagnostics.maxCpuTerrainGatherMs = 0.0f;
        m_sunDiagnostics.maxCpuWorldGatherMs = 0.0f;
        m_sunDiagnostics.maxCpuTerrainHashMs = 0.0f;
        m_sunDiagnostics.maxCpuCacheDecisionMs = 0.0f;
        m_sunDiagnostics.maxCpuCommandRecordMs = 0.0f;
        m_sunDiagnostics.maxCpuTotalMs = 0.0f;
        m_sunDiagnostics.avgGpuRenderMs = 0.0f;
        m_sunDiagnostics.maxGpuRenderMs = 0.0f;
        m_sunDiagnostics.minGpuRenderMs = 0.0f;
        m_sunDiagnostics.avgDrawCallCount = 0.0f;
        m_sunDiagnostics.avgApiDrawCallCount = 0.0f;
        m_sunDiagnostics.avgTerrainChunks = 0.0f;
        m_sunDiagnostics.avgGatherCandidateChunks = 0.0f;
        m_sunDiagnostics.avgAcceptedChunks = 0.0f;
        m_sunDiagnostics.avgRenderedFrameDrawCalls = 0.0f;
        m_sunDiagnostics.avgRenderedFrameApiDrawCalls = 0.0f;
        m_sunDiagnostics.avgRenderedFrameGpuMs = 0.0f;
        m_sunDiagnostics.avgCascadesRendered = 0.0f;
        m_sunDiagnostics.avgCascadesReused = 0.0f;
        m_sunDiagnostics.renderCachePrecheckHits = 0u;
        m_sunDiagnostics.gatherCacheHits = 0u;
        m_sunDiagnostics.gatherCacheMisses = 0u;
        m_sunDiagnostics.reusedFrames = 0u;
        m_sunDiagnostics.renderedFrames = 0u;
        return;
    }

    m_sunDiagnostics.latest = m_sunSampleHistory.back().data;

    float sumVp = 0.0f, sumGather = 0.0f, sumWorldGather = 0.0f;
    float sumHash = 0.0f, sumCacheDecision = 0.0f;
    float sumRecord = 0.0f, sumTotal = 0.0f;
    float sumGpu = 0.0f;
    float maxVp = 0.0f, maxGather = 0.0f, maxWorldGather = 0.0f;
    float maxHash = 0.0f, maxCacheDecision = 0.0f;
    float maxRecord = 0.0f, maxTotal = 0.0f;
    float maxGpu = 0.0f;
    float minGpu = std::numeric_limits<float>::max();
    float sumDraws = 0.0f, sumApiDraws = 0.0f, sumChunks = 0.0f;
    float sumCandidates = 0.0f, sumAccepted = 0.0f;
    float sumRenderedDraws = 0.0f, sumRenderedApiDraws = 0.0f, sumRenderedGpu = 0.0f;
    float sumCascadesRendered = 0.0f, sumCascadesReused = 0.0f;
    uint32_t reused = 0u, rendered = 0u;
    uint32_t precheckHits = 0u, gatherHits = 0u, gatherMisses = 0u;

    for (const auto& s : m_sunSampleHistory) {
        const auto& d = s.data;
        sumVp += d.cpuVpComputeMs;
        sumGather += d.cpuTerrainGatherMs;
        sumWorldGather += d.cpuWorldGatherMs;
        sumHash += d.cpuTerrainHashMs;
        sumCacheDecision += d.cpuCacheDecisionMs;
        sumRecord += d.cpuCommandRecordMs;
        sumTotal += d.cpuTotalMs;
        sumGpu += d.gpuRenderMs;
        sumDraws += static_cast<float>(d.drawCallCount);
        sumApiDraws += static_cast<float>(d.apiDrawCallCount);
        sumChunks += static_cast<float>(d.terrainChunksGathered);
        sumCandidates += static_cast<float>(d.bboxCandidateChunks);
        sumAccepted += static_cast<float>(d.acceptedChunkCount);
        sumCascadesRendered += static_cast<float>(d.cascadesRendered);
        sumCascadesReused += static_cast<float>(d.cascadesReused);

        maxVp = std::max(maxVp, d.cpuVpComputeMs);
        maxGather = std::max(maxGather, d.cpuTerrainGatherMs);
        maxWorldGather = std::max(maxWorldGather, d.cpuWorldGatherMs);
        maxHash = std::max(maxHash, d.cpuTerrainHashMs);
        maxCacheDecision = std::max(maxCacheDecision, d.cpuCacheDecisionMs);
        maxRecord = std::max(maxRecord, d.cpuCommandRecordMs);
        maxTotal = std::max(maxTotal, d.cpuTotalMs);
        maxGpu = std::max(maxGpu, d.gpuRenderMs);
        if (!d.wasReused) {
            minGpu = std::min(minGpu, d.gpuRenderMs);
            sumRenderedDraws += static_cast<float>(d.drawCallCount);
            sumRenderedApiDraws += static_cast<float>(d.apiDrawCallCount);
            sumRenderedGpu += d.gpuRenderMs;
        }

        if (d.wasReused) ++reused; else ++rendered;
        if (d.renderCachePrecheckHit) ++precheckHits;
        if (d.gatherCacheHit) ++gatherHits;
        if (d.usedLocalTerrainGather && !d.renderCachePrecheckHit && !d.gatherCacheHit) ++gatherMisses;
    }

    const float inv = 1.0f / static_cast<float>(n);
    m_sunDiagnostics.avgCpuVpComputeMs = sumVp * inv;
    m_sunDiagnostics.avgCpuTerrainGatherMs = sumGather * inv;
    m_sunDiagnostics.avgCpuWorldGatherMs = sumWorldGather * inv;
    m_sunDiagnostics.avgCpuTerrainHashMs = sumHash * inv;
    m_sunDiagnostics.avgCpuCacheDecisionMs = sumCacheDecision * inv;
    m_sunDiagnostics.avgCpuCommandRecordMs = sumRecord * inv;
    m_sunDiagnostics.avgCpuTotalMs = sumTotal * inv;
    m_sunDiagnostics.avgGpuRenderMs = sumGpu * inv;
    m_sunDiagnostics.maxGpuRenderMs = maxGpu;
    m_sunDiagnostics.minGpuRenderMs = (minGpu < std::numeric_limits<float>::max()) ? minGpu : 0.0f;
    m_sunDiagnostics.maxCpuVpComputeMs = maxVp;
    m_sunDiagnostics.maxCpuTerrainGatherMs = maxGather;
    m_sunDiagnostics.maxCpuWorldGatherMs = maxWorldGather;
    m_sunDiagnostics.maxCpuTerrainHashMs = maxHash;
    m_sunDiagnostics.maxCpuCacheDecisionMs = maxCacheDecision;
    m_sunDiagnostics.maxCpuCommandRecordMs = maxRecord;
    m_sunDiagnostics.maxCpuTotalMs = maxTotal;
    m_sunDiagnostics.avgDrawCallCount = sumDraws * inv;
    m_sunDiagnostics.avgApiDrawCallCount = sumApiDraws * inv;
    m_sunDiagnostics.avgTerrainChunks = sumChunks * inv;
    m_sunDiagnostics.avgGatherCandidateChunks = sumCandidates * inv;
    m_sunDiagnostics.avgAcceptedChunks = sumAccepted * inv;
    m_sunDiagnostics.avgRenderedFrameDrawCalls =
        (rendered > 0u) ? (sumRenderedDraws / static_cast<float>(rendered)) : 0.0f;
    m_sunDiagnostics.avgRenderedFrameApiDrawCalls =
        (rendered > 0u) ? (sumRenderedApiDraws / static_cast<float>(rendered)) : 0.0f;
    m_sunDiagnostics.avgRenderedFrameGpuMs =
        (rendered > 0u) ? (sumRenderedGpu / static_cast<float>(rendered)) : 0.0f;
    m_sunDiagnostics.avgCascadesRendered = sumCascadesRendered * inv;
    m_sunDiagnostics.avgCascadesReused = sumCascadesReused * inv;
    m_sunDiagnostics.renderCachePrecheckHits = precheckHits;
    m_sunDiagnostics.gatherCacheHits = gatherHits;
    m_sunDiagnostics.gatherCacheMisses = gatherMisses;
    m_sunDiagnostics.reusedFrames = reused;
    m_sunDiagnostics.renderedFrames = rendered;
}

````

## src\world\WorldRendering.cpp

Description: No CC-DESC found.

````cpp
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include <iostream>
#include <algorithm>
#include <array>
#include <cmath>
#include <chrono>
#include <limits>

// gatherDrawCommands(), gatherDrawCommandsInSphere(),
// enqueueMeshForUpload() extracted from World.cpp

// Helper: Calculate chunk AABB from chunk coordinate
static AABB calculateChunkAABB(const glm::ivec3& chunkCoord) {
    glm::vec3 minWorld = WorldConfig::microVoxelToWorld(WorldConfig::chunkToMicroVoxel(chunkCoord));
    glm::vec3 maxWorld = minWorld + glm::vec3(WorldConfig::CHUNK_SIZE_M, WorldConfig::CHUNK_HEIGHT_M, WorldConfig::CHUNK_SIZE_M);
    return AABB{minWorld, maxWorld};
}

static bool sphereIntersectsAABB(const glm::vec3& center, float radiusSq, const AABB& aabb) {
    const float cx = std::max(aabb.min.x, std::min(center.x, aabb.max.x));
    const float cy = std::max(aabb.min.y, std::min(center.y, aabb.max.y));
    const float cz = std::max(aabb.min.z, std::min(center.z, aabb.max.z));
    const glm::vec3 d = center - glm::vec3(cx, cy, cz);
    return glm::dot(d, d) <= radiusSq;
}

static uint32_t sunCascadeMaskForChunkCenter(const glm::vec3& centerWorld,
                                             const glm::mat4* cascadeVPs,
                                             uint32_t cascadeCount,
                                             uint32_t* outInnerRejected = nullptr) {
    constexpr float kHalfChunkX = WorldConfig::CHUNK_SIZE_M * 0.5f;
    constexpr float kHalfChunkY = WorldConfig::CHUNK_HEIGHT_M * 0.5f;
    constexpr float kHalfChunkZ = WorldConfig::CHUNK_SIZE_M * 0.5f;

    std::array<float, World::SUN_GATHER_DIAG_MAX_CASCADES> clipCx{};
    std::array<float, World::SUN_GATHER_DIAG_MAX_CASCADES> clipCy{};
    std::array<float, World::SUN_GATHER_DIAG_MAX_CASCADES> clipCz{};
    std::array<float, World::SUN_GATHER_DIAG_MAX_CASCADES> clipHx{};
    std::array<float, World::SUN_GATHER_DIAG_MAX_CASCADES> clipHy{};
    std::array<float, World::SUN_GATHER_DIAG_MAX_CASCADES> clipHz{};
    const uint32_t evalCount = std::min<uint32_t>(
        cascadeCount, World::SUN_GATHER_DIAG_MAX_CASCADES);
    for (uint32_t c = 0; c < evalCount; ++c) {
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

    uint32_t mask = 0u;
    uint32_t innerRejected = 0u;
    constexpr float kCascadeBlendFrac = 0.12f;
    constexpr float kInnerCascadeScale = 1.0f - kCascadeBlendFrac;
    for (uint32_t c = 0; c < evalCount; ++c) {
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
        mask |= (1u << c);
    }
    if (outInnerRejected) {
        *outInnerRejected += innerRejected;
    }
    return mask;
}

uint32_t World::gatherDrawCommands(const glm::mat4& viewProj,
                                    VkDrawIndexedIndirectCommand* outCmds,
                                    glm::vec4* outOrigins,
                                    uint32_t maxDraws,
                                    uint64_t deviceTimeline) {
    // Delegate to ChunkRenderSystem
    return m_renderSystem.gatherDrawCommands(
        m_registry,
        m_registryMutex,
        viewProj,
        outCmds,
        outOrigins,
        maxDraws,
        deviceTimeline);
}

uint32_t World::gatherDrawCommandsInSphere(const glm::vec3& center,
                                           float radius,
                                           VkDrawIndexedIndirectCommand* outCmds,
                                           glm::vec4* outOrigins,
                                           uint32_t maxDraws,
                                           uint64_t deviceTimeline) {
    if (!outCmds || !outOrigins || maxDraws == 0u || radius <= 0.0f) {
        return 0u;
    }

    // Build a tight chunk-coordinate range from the light sphere.
    const glm::vec3 minWorld = center - glm::vec3(radius);
    const glm::vec3 maxWorld = center + glm::vec3(radius);
    const glm::ivec3 minChunk = WorldConfig::microVoxelToChunk(WorldConfig::worldToMicroVoxel(minWorld));
    const glm::ivec3 maxChunk = WorldConfig::microVoxelToChunk(WorldConfig::worldToMicroVoxel(maxWorld));
    const float radiusSq = radius * radius;

    // Direct hashmap lookup over the chunk-coord bbox in deterministic
    // (z, y, x) order. For a typical 5 m point-light radius the bbox is
    // 1×1×1 chunks (chunks are 32 m × 128 m × 32 m), so this is ~1 lookup
    // instead of iterating every loaded chunk in the world. The (z, y, x)
    // walk order matches the prior sorted-candidate order exactly, so the
    // downstream shadow-cache terrain hash is bit-identical to the prior
    // implementation — no visual or cache-invalidation change.
    //
    // Locks are taken separately (chunkState first, released, then registry)
    // to match the rest of the engine's lock-order discipline and avoid
    // introducing a new nested-lock pair.
    struct CandidateChunk {
        entt::entity entity{entt::null};
        glm::ivec3 coord{0};
    };
    // Stack-friendly small buffer; orb radii in this engine are small so the
    // bbox is almost always 1-8 chunks. Falls back to heap if it grows.
    constexpr size_t kInlineCap = 32;
    std::array<CandidateChunk, kInlineCap> inlineCandidates{};
    std::vector<CandidateChunk> overflowCandidates;
    size_t candidateCount = 0;
    auto pushCandidate = [&](entt::entity e, const glm::ivec3& c) {
        if (candidateCount < kInlineCap) {
            inlineCandidates[candidateCount] = CandidateChunk{e, c};
        } else {
            if (overflowCandidates.empty()) overflowCandidates.reserve(64);
            overflowCandidates.push_back(CandidateChunk{e, c});
        }
        ++candidateCount;
    };
    auto getCandidate = [&](size_t i) -> const CandidateChunk& {
        return (i < kInlineCap) ? inlineCandidates[i] : overflowCandidates[i - kInlineCap];
    };

    {
        std::shared_lock stateLock(m_chunkStateMutex);
        for (int z = minChunk.z; z <= maxChunk.z; ++z) {
            for (int y = minChunk.y; y <= maxChunk.y; ++y) {
                for (int x = minChunk.x; x <= maxChunk.x; ++x) {
                    const glm::ivec3 coord(x, y, z);
                    auto it = m_chunkEntityMap.find(coord);
                    if (it == m_chunkEntityMap.end()) continue;
                    pushCandidate(it->second, coord);
                }
            }
        }
    }

    uint32_t drawCount = 0u;
    bool truncated = false;
    {
        std::shared_lock regLock(m_registryMutex);
        for (size_t ci = 0; ci < candidateCount; ++ci) {
            if (drawCount >= maxDraws) {
                truncated = true;
                break;
            }
            const CandidateChunk& candidate = getCandidate(ci);
            const entt::entity entity = candidate.entity;
            if (!m_registry.valid(entity)) continue;
            if (!m_registry.all_of<ChunkState, MeshHandle, Chunk>(entity)) continue;

            const auto& state = m_registry.get<ChunkState>(entity);
            const auto& mesh = m_registry.get<MeshHandle>(entity);
            const auto& chunk = m_registry.get<Chunk>(entity);

            if (!chunk.isVisible) continue;
            if (state.state != ChunkState::State::Ready) continue;
            if (!mesh.isValid()) continue;
            if (mesh.getTotalIndexCount() == 0) continue;
            if (mesh.gpuReadyValue > deviceTimeline) continue;

            const AABB chunkBounds = calculateChunkAABB(candidate.coord);
            if (!sphereIntersectsAABB(center, radiusSq, chunkBounds)) continue;

            for (uint8_t sc = 0; sc < mesh.subChunkCount && drawCount < maxDraws; ++sc) {
                const auto& subChunk = mesh.subChunks[sc];
                if (subChunk.indexCount == 0) continue;

                VkDrawIndexedIndirectCommand& cmd = outCmds[drawCount];
                cmd.indexCount = subChunk.indexCount;
                cmd.instanceCount = 1u;
                cmd.firstIndex = subChunk.firstIndex;
                cmd.vertexOffset = subChunk.vertexOffset;
                cmd.firstInstance = 0u;

                outOrigins[drawCount] = glm::vec4(
                    static_cast<float>(candidate.coord.x),
                    static_cast<float>(candidate.coord.y),
                    static_cast<float>(candidate.coord.z),
                    0.0f);
                ++drawCount;
            }
        }
    }

    static bool warnedSphereTruncationOnce = false;
    if (truncated && !warnedSphereTruncationOnce) {
        std::cout << "[World] gatherDrawCommandsInSphere truncated at " << maxDraws
                  << " draws; consider increasing shadow local draw capacity." << std::endl;
        warnedSphereTruncationOnce = true;
    }

    return drawCount;
}

uint32_t World::gatherDrawCommandsForSunCascades(
    const glm::vec3& cameraPos,
    const glm::vec3& sunDir,
    const glm::mat4* cascadeVPs,
    const float* cascadeHalfExtents,
    uint32_t cascadeCount,
    VkDrawIndexedIndirectCommand* outCmds,
    glm::vec4* outOrigins,
    uint16_t* outCascadeMasks,
    uint32_t maxDraws,
    uint64_t deviceTimeline,
    SunCascadeGatherDiagnostics* diagnostics,
    int32_t extraChunkPadding,
    bool includeZeroMaskCandidates) {
    using Clock = std::chrono::high_resolution_clock;
    const auto totalStart = Clock::now();

    if (diagnostics) {
        *diagnostics = SunCascadeGatherDiagnostics{};
        diagnostics->cascadeCount = cascadeCount;
        diagnostics->maxDraws = maxDraws;
    }

    auto finishDiagnostics = [&]() {
        if (diagnostics) {
            diagnostics->totalMs = std::chrono::duration<float, std::milli>(
                Clock::now() - totalStart).count();
        }
    };

    if (!outCmds || !outOrigins || !outCascadeMasks ||
        !cascadeVPs || !cascadeHalfExtents ||
        cascadeCount == 0u || maxDraws == 0u) {
        finishDiagnostics();
        return 0u;
    }

    // ── Walk bounding box ───────────────────────────────────────────
    // The largest cascade's clip volume in world XZ is roughly
    // [cameraXZ ± halfExtent] expanded by chunk-height * |shear|
    // (caster reach along the sun direction). Walking only chunks
    // inside that box eliminates the huge spherical scan that the
    // old single-radius gather performed at km-scale cascades.
    float maxHalfExtent = 0.0f;
    for (uint32_t c = 0; c < cascadeCount; ++c) {
        maxHalfExtent = std::max(maxHalfExtent, cascadeHalfExtents[c]);
    }
    const float sinEl = std::max(-sunDir.y, 0.05f);
    const float invSin = 1.0f / sinEl;
    const float shearX = std::abs(sunDir.x) * invSin;
    const float shearZ = std::abs(sunDir.z) * invSin;
    const float shearMax = std::max(shearX, shearZ);
    // Cap caster reach so very low sun (long shears) doesn't blow up the
    // walk bounding box past sane limits.
    const float casterReach = std::min(
        WorldConfig::CHUNK_HEIGHT_M * shearMax,
        std::max(maxHalfExtent * 2.0f, 768.0f));
    const float padding = WorldConfig::CHUNK_SIZE_M * 2.0f;
    const float halfX = maxHalfExtent + casterReach + padding;
    const float halfZ = maxHalfExtent + casterReach + padding;

    const glm::vec3 minWorld(cameraPos.x - halfX, -10000.0f, cameraPos.z - halfZ);
    const glm::vec3 maxWorld(cameraPos.x + halfX,  10000.0f, cameraPos.z + halfZ);
    glm::ivec3 minChunk = WorldConfig::microVoxelToChunk(WorldConfig::worldToMicroVoxel(minWorld));
    glm::ivec3 maxChunk = WorldConfig::microVoxelToChunk(WorldConfig::worldToMicroVoxel(maxWorld));
    const int32_t padChunks = std::max<int32_t>(extraChunkPadding, 0);
    minChunk.x -= padChunks;
    minChunk.z -= padChunks;
    maxChunk.x += padChunks;
    maxChunk.z += padChunks;

    if (diagnostics) {
        diagnostics->minChunk = minChunk;
        diagnostics->maxChunk = maxChunk;
        diagnostics->maxHalfExtent = maxHalfExtent;
        diagnostics->sinElevation = sinEl;
        diagnostics->shearX = shearX;
        diagnostics->shearZ = shearZ;
        diagnostics->shearMax = shearMax;
        diagnostics->casterReach = casterReach;
        diagnostics->padding = padding;
        diagnostics->halfX = halfX;
        diagnostics->halfZ = halfZ;
    }

    struct CandidateChunk {
        entt::entity entity{entt::null};
        glm::ivec3 coord{0};
    };
    std::vector<CandidateChunk> candidates;
    const auto stateScanStart = Clock::now();
    {
        std::shared_lock stateLock(m_chunkStateMutex);
        if (diagnostics) {
            diagnostics->loadedChunkMapSize = static_cast<uint32_t>(
                std::min<size_t>(m_chunkEntityMap.size(), std::numeric_limits<uint32_t>::max()));
        }
        candidates.reserve(m_chunkEntityMap.size());
        for (const auto& [coord, entity] : m_chunkEntityMap) {
            if (coord.x < minChunk.x || coord.x > maxChunk.x ||
                coord.z < minChunk.z || coord.z > maxChunk.z) {
                continue;
            }
            candidates.push_back(CandidateChunk{entity, coord});
        }
    }
    if (diagnostics) {
        diagnostics->stateMapScanMs = std::chrono::duration<float, std::milli>(
            Clock::now() - stateScanStart).count();
        diagnostics->bboxCandidateChunks = static_cast<uint32_t>(
            std::min<size_t>(candidates.size(), std::numeric_limits<uint32_t>::max()));
    }
    if (diagnostics) {
        diagnostics->candidateSortMs = 0.0f;
    }

    uint32_t drawCount = 0u;
    bool truncated = false;
    const auto registryStart = Clock::now();
    {
        std::shared_lock regLock(m_registryMutex);
        for (const CandidateChunk& candidate : candidates) {
            if (drawCount >= maxDraws) { truncated = true; break; }
            if (diagnostics) ++diagnostics->visitedCandidateChunks;

            const entt::entity entity = candidate.entity;
            if (!m_registry.valid(entity)) {
                if (diagnostics) ++diagnostics->invalidEntityRejects;
                continue;
            }
            if (!m_registry.all_of<ChunkState, MeshHandle, Chunk>(entity)) {
                if (diagnostics) ++diagnostics->missingComponentRejects;
                continue;
            }

            const auto& state = m_registry.get<ChunkState>(entity);
            const auto& mesh = m_registry.get<MeshHandle>(entity);
            const auto& chunk = m_registry.get<Chunk>(entity);
            if (!chunk.isVisible) {
                if (diagnostics) ++diagnostics->invisibleRejects;
                continue;
            }
            if (state.state != ChunkState::State::Ready) {
                if (diagnostics) ++diagnostics->notReadyRejects;
                continue;
            }
            if (!mesh.isValid()) {
                if (diagnostics) ++diagnostics->invalidMeshRejects;
                continue;
            }
            if (mesh.getTotalIndexCount() == 0) {
                if (diagnostics) ++diagnostics->emptyMeshRejects;
                continue;
            }
            if (mesh.gpuReadyValue > deviceTimeline) {
                if (diagnostics) ++diagnostics->uploadPendingRejects;
                continue;
            }

            const glm::vec3 centerWorld(
                (candidate.coord.x + 0.5f) * WorldConfig::CHUNK_SIZE_M,
                (candidate.coord.y + 0.5f) * WorldConfig::CHUNK_HEIGHT_M,
                (candidate.coord.z + 0.5f) * WorldConfig::CHUNK_SIZE_M);
            const uint32_t mask = sunCascadeMaskForChunkCenter(
                centerWorld,
                cascadeVPs,
                cascadeCount,
                diagnostics ? &diagnostics->cascadeInnerCullRejects : nullptr);
            if (mask == 0u) {
                if (diagnostics) ++diagnostics->cascadeCullRejects;
                if (!includeZeroMaskCandidates) {
                    continue;
                }
            }
            if (diagnostics && mask != 0u) {
                ++diagnostics->acceptedChunks;
                const uint32_t diagCascadeCount = std::min<uint32_t>(
                    cascadeCount, SUN_GATHER_DIAG_MAX_CASCADES);
                for (uint32_t c = 0; c < diagCascadeCount; ++c) {
                    if ((mask & (1u << c)) != 0u) {
                        ++diagnostics->cascadeChunkHits[c];
                    }
                }
            }

            for (uint8_t sc = 0; sc < mesh.subChunkCount && drawCount < maxDraws; ++sc) {
                const auto& subChunk = mesh.subChunks[sc];
                if (subChunk.indexCount == 0) continue;

                VkDrawIndexedIndirectCommand& cmd = outCmds[drawCount];
                cmd.indexCount = subChunk.indexCount;
                cmd.instanceCount = 1u;
                cmd.firstIndex = subChunk.firstIndex;
                cmd.vertexOffset = subChunk.vertexOffset;
                cmd.firstInstance = 0u;
                outOrigins[drawCount] = glm::vec4(
                    static_cast<float>(candidate.coord.x),
                    static_cast<float>(candidate.coord.y),
                    static_cast<float>(candidate.coord.z),
                    0.0f);
                outCascadeMasks[drawCount] = static_cast<uint16_t>(mask);
                if (diagnostics) {
                    const uint32_t diagCascadeCount = std::min<uint32_t>(
                        cascadeCount, SUN_GATHER_DIAG_MAX_CASCADES);
                    for (uint32_t c = 0; c < diagCascadeCount; ++c) {
                        if ((mask & (1u << c)) != 0u) {
                            ++diagnostics->cascadeDrawHits[c];
                        }
                    }
                }
                ++drawCount;
            }
        }
    }
    if (diagnostics) {
        diagnostics->registryWalkMs = std::chrono::duration<float, std::milli>(
            Clock::now() - registryStart).count();
        diagnostics->truncated = truncated;
        diagnostics->emittedDraws = drawCount;
    }

    static bool warnedCascadeTruncationOnce = false;
    if (truncated && !warnedCascadeTruncationOnce) {
        std::cout << "[World] gatherDrawCommandsForSunCascades truncated at "
                  << maxDraws << " draws." << std::endl;
        warnedCascadeTruncationOnce = true;
    }

    finishDiagnostics();
    return drawCount;
}

uint32_t World::gatherDrawCommandsForSunCascadeChunk(
    const glm::ivec3& chunkCoord,
    const glm::mat4* cascadeVPs,
    uint32_t cascadeCount,
    VkDrawIndexedIndirectCommand* outCmds,
    glm::vec4* outOrigins,
    uint16_t* outCascadeMasks,
    uint32_t maxDraws,
    uint64_t deviceTimeline,
    bool includeZeroMaskCandidate) {
    if (!outCmds || !outOrigins || !outCascadeMasks ||
        !cascadeVPs || cascadeCount == 0u || maxDraws == 0u) {
        return 0u;
    }

    entt::entity entity = entt::null;
    {
        std::shared_lock stateLock(m_chunkStateMutex);
        auto it = m_chunkEntityMap.find(chunkCoord);
        if (it == m_chunkEntityMap.end()) {
            return 0u;
        }
        entity = it->second;
    }

    std::shared_lock regLock(m_registryMutex);
    if (!m_registry.valid(entity)) return 0u;
    if (!m_registry.all_of<ChunkState, MeshHandle, Chunk>(entity)) return 0u;

    const auto& state = m_registry.get<ChunkState>(entity);
    const auto& mesh = m_registry.get<MeshHandle>(entity);
    const auto& chunk = m_registry.get<Chunk>(entity);
    if (!chunk.isVisible) return 0u;
    if (state.state != ChunkState::State::Ready) return 0u;
    if (!mesh.isValid()) return 0u;
    if (mesh.getTotalIndexCount() == 0) return 0u;
    if (mesh.gpuReadyValue > deviceTimeline) return 0u;

    const glm::vec3 centerWorld(
        (chunkCoord.x + 0.5f) * WorldConfig::CHUNK_SIZE_M,
        (chunkCoord.y + 0.5f) * WorldConfig::CHUNK_HEIGHT_M,
        (chunkCoord.z + 0.5f) * WorldConfig::CHUNK_SIZE_M);
    const uint32_t mask = sunCascadeMaskForChunkCenter(centerWorld, cascadeVPs, cascadeCount);
    if (mask == 0u && !includeZeroMaskCandidate) {
        return 0u;
    }

    uint32_t drawCount = 0u;
    for (uint8_t sc = 0; sc < mesh.subChunkCount && drawCount < maxDraws; ++sc) {
        const auto& subChunk = mesh.subChunks[sc];
        if (subChunk.indexCount == 0) continue;

        VkDrawIndexedIndirectCommand& cmd = outCmds[drawCount];
        cmd.indexCount = subChunk.indexCount;
        cmd.instanceCount = 1u;
        cmd.firstIndex = subChunk.firstIndex;
        cmd.vertexOffset = subChunk.vertexOffset;
        cmd.firstInstance = 0u;
        outOrigins[drawCount] = glm::vec4(
            static_cast<float>(chunkCoord.x),
            static_cast<float>(chunkCoord.y),
            static_cast<float>(chunkCoord.z),
            0.0f);
        outCascadeMasks[drawCount] = static_cast<uint16_t>(mask);
        ++drawCount;
    }
    return drawCount;
}

void World::enqueueMeshForUpload(entt::entity entity,
                                  MeshData&& mesh,
                                  bool fromTerrainEdit,
                                  std::shared_ptr<ChunkVersionState> versionState,
                                  uint32_t version,
                                  ChunkDebugAttribution debugInfo) {
    m_uploadSystem.enqueueMeshForUpload(entity, std::move(mesh), fromTerrainEdit, versionState, version,
                                        std::chrono::steady_clock::time_point{}, debugInfo);
}

void World::enqueueMeshForUpload(entt::entity entity,
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
                                  ChunkDebugAttribution debugInfo) {
    m_uploadSystem.enqueueMeshForUpload(entity, std::move(subChunks), mainSubChunkCount, fromTerrainEdit,
                                        versionState, version, tightMin, tightMax, hasTight,
                                        isRemesh, batchId, std::chrono::steady_clock::time_point{},
                                        debugInfo);
}

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
    rendering/lighting/ShadowMapRendering.cpp
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
