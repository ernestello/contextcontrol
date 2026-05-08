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


## src\world\edit\TerrainEditMesher.cpp

Description: No CC-DESC found. C++ struct 'BaseCacheKey'.

````cpp
// GPT-DESC: Runtime greedy mesher for edited terrain chunks and baked voxel-face materials.
#include "world/edit/TerrainEditMesher.h"
#include "world/edit/VoxelBaseSampler.h"
#include "world/config/WorldConfig.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <iostream>
#include <mutex>
#include <shared_mutex>
#include <unordered_map>

namespace TerrainEdit {

// ---------------------------------------------------------------------------
// Incremental base cache: stores the heightmap-only solid data so that
// subsequent remeshes of the same chunk skip the heightmap column fill.
// Thread-safe (multiple background mesher threads read/write concurrently).
// ---------------------------------------------------------------------------

struct BaseCacheKey {
    int chunkX, chunkZ;
    int baseVoxelX, baseVoxelZ;
    int cacheMinY, cacheDimXZ, cacheDimY;

    bool operator==(const BaseCacheKey& o) const {
        return chunkX == o.chunkX && chunkZ == o.chunkZ &&
               baseVoxelX == o.baseVoxelX && baseVoxelZ == o.baseVoxelZ &&
               cacheMinY == o.cacheMinY && cacheDimXZ == o.cacheDimXZ &&
               cacheDimY == o.cacheDimY;
    }
};

struct BaseCacheKeyHash {
    size_t operator()(const BaseCacheKey& k) const {
        // Simple hash combine
        size_t h = std::hash<int>{}(k.chunkX);
        h ^= std::hash<int>{}(k.chunkZ) + 0x9e3779b9 + (h << 6) + (h >> 2);
        h ^= std::hash<int>{}(k.cacheMinY) + 0x9e3779b9 + (h << 6) + (h >> 2);
        h ^= std::hash<int>{}(k.cacheDimY) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};

struct BaseCacheEntry {
    std::vector<uint8_t> heightmapBase;   // Pure heightmap data (no overlay)
    std::vector<uint8_t> appliedResult;   // heightmap + overlay combined
    size_t overlayGeneration = 0;         // Generation when appliedResult was built
};

static std::shared_mutex s_baseCacheMutex;
static std::unordered_map<BaseCacheKey, BaseCacheEntry, BaseCacheKeyHash> s_baseCache;
// Raised from 256 to 4096: under intense LOD spam, 4 workers × multiple LODs ×
// multiple chunks blew through 256 entries quickly, triggering s_baseCache.clear()
// and forcing 10M-voxel native cache rebuilds (216 ms each). 4096 entries at
// ~50-200 KB each = ~200-800 MB worst case but typical working set is far smaller.
static constexpr size_t BASE_CACHE_MAX_ENTRIES = 4096;

// ---------------------------------------------------------------------------
// Tier A.1 — downsample cache. Stores the result of the expensive overlay-aware
// LOD downsample loop (LOD>0 only). Keyed per (chunk, lod) plus the geometry
// params that determine the cache layout. On overlay-generation mismatch with a
// known dirty AABB, we copy the cached cacheData and re-downsample only LOD
// voxels intersecting the dirty region (region recompute). Without a dirty
// AABB we fall back to a full downsample.
// ---------------------------------------------------------------------------
struct DownsampleCacheKey {
    int chunkX, chunkY, chunkZ, lodLevel;
    int cacheBaseX, cacheBaseZ, cacheMinY, cacheDimXZ, cacheDimY;

    bool operator==(const DownsampleCacheKey& o) const {
        return chunkX == o.chunkX && chunkY == o.chunkY && chunkZ == o.chunkZ &&
               lodLevel == o.lodLevel &&
               cacheBaseX == o.cacheBaseX && cacheBaseZ == o.cacheBaseZ &&
               cacheMinY == o.cacheMinY &&
               cacheDimXZ == o.cacheDimXZ && cacheDimY == o.cacheDimY;
    }
};
struct DownsampleCacheKeyHash {
    size_t operator()(const DownsampleCacheKey& k) const {
        size_t h = std::hash<int>{}(k.chunkX);
        h ^= std::hash<int>{}(k.chunkY) + 0x9e3779b9 + (h << 6) + (h >> 2);
        h ^= std::hash<int>{}(k.chunkZ) + 0x9e3779b9 + (h << 6) + (h >> 2);
        h ^= std::hash<int>{}(k.lodLevel) + 0x9e3779b9 + (h << 6) + (h >> 2);
        h ^= std::hash<int>{}(k.cacheMinY) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};
struct DownsampleCacheEntry {
    std::vector<uint8_t> cacheData;
    size_t overlayGeneration{0};
};
static std::shared_mutex s_downsampleCacheMutex;
static std::unordered_map<DownsampleCacheKey, DownsampleCacheEntry,
                          DownsampleCacheKeyHash> s_downsampleCache;
static constexpr size_t DOWNSAMPLE_CACHE_MAX_ENTRIES = 8192;

inline bool wasCancelled(const RemeshCancellationToken* token) {
    return token && token->isCancelled();
}

// ---------------------------------------------------------------------------
// Post-process: vertex dedup + Forsyth triangle reorder + fetch reorder
// ---------------------------------------------------------------------------

static constexpr int RT_VERTEX_CACHE_SIZE = 32;
static constexpr size_t MAX_VERTS_PER_SUBMESH = 65535;
static constexpr size_t MAX_RUNTIME_SUBMESHES = 64; // Keep in sync with MAX_SUBCHUNKS in Chunk.h
// FAST-path compaction is only worth it for truly huge edit meshes. Medium
// meshes were paying 15-50 ms of dedup work to save only a few milliseconds of
// upload, which inflated shown latency during normal brush use. Prefer raw
// uploads until the mesh is so large that it would explode submesh count or
// memory pressure.
static constexpr size_t FAST_COMPACT_VERT_THRESHOLD = 450000;
static constexpr size_t FAST_COMPACT_INDEX_THRESHOLD = 700000;
static constexpr size_t FAST_COMPACT_SUBMESH_THRESHOLD = 8;

static void optimizeTriangleOrderRT(TerrainEditMesher::MeshResult& mesh) {
    if (mesh.empty()) return;
    const size_t triCount = mesh.indices.size() / 3;
    if (triCount <= 1) return;
    const size_t vertCount = mesh.vertices.size();

    std::vector<std::vector<uint32_t>> vertToTris(vertCount);
    for (size_t t = 0; t < triCount; ++t) {
        vertToTris[mesh.indices[t*3+0]].push_back(static_cast<uint32_t>(t));
        vertToTris[mesh.indices[t*3+1]].push_back(static_cast<uint32_t>(t));
        vertToTris[mesh.indices[t*3+2]].push_back(static_cast<uint32_t>(t));
    }

    std::vector<uint32_t> cache(RT_VERTEX_CACHE_SIZE, UINT32_MAX);
    std::vector<bool> inCache(vertCount, false);
    int cachePos = 0;

    auto pushCache = [&](uint32_t v) {
        if (inCache[v]) return;
        uint32_t evicted = cache[cachePos];
        if (evicted != UINT32_MAX) inCache[evicted] = false;
        cache[cachePos] = v;
        inCache[v] = true;
        cachePos = (cachePos + 1) % RT_VERTEX_CACHE_SIZE;
    };

    std::vector<bool> emitted(triCount, false);
    std::vector<uint32_t> newIndices;
    newIndices.reserve(mesh.indices.size());

    auto emitTri = [&](size_t t) {
        emitted[t] = true;
        uint32_t v0 = mesh.indices[t*3+0], v1 = mesh.indices[t*3+1], v2 = mesh.indices[t*3+2];
        newIndices.push_back(v0); newIndices.push_back(v1); newIndices.push_back(v2);
        pushCache(v0); pushCache(v1); pushCache(v2);
    };

    emitTri(0);
    for (size_t ec = 1; ec < triCount; ) {
        size_t bestTri = SIZE_MAX;
        int bestScore = -1;
        for (int ci = 0; ci < RT_VERTEX_CACHE_SIZE && bestScore < 3; ++ci) {
            uint32_t cv = cache[ci];
            if (cv == UINT32_MAX) continue;
            for (uint32_t t : vertToTris[cv]) {
                if (emitted[t]) continue;
                int score = 0;
                if (inCache[mesh.indices[t*3+0]]) ++score;
                if (inCache[mesh.indices[t*3+1]]) ++score;
                if (inCache[mesh.indices[t*3+2]]) ++score;
                if (score > bestScore) { bestScore = score; bestTri = t; }
                if (score == 3) break;
            }
        }
        if (bestTri == SIZE_MAX) {
            for (size_t t = 0; t < triCount; ++t)
                if (!emitted[t]) { bestTri = t; break; }
        }
        emitTri(bestTri);
        ++ec;
    }
    mesh.indices = std::move(newIndices);
}

static void deduplicateMeshVertices(TerrainEditMesher::MeshResult& mesh) {
    if (mesh.empty()) return;

    const size_t origCount = mesh.vertices.size();

    struct VertexKey {
        uint32_t packed{0};
        uint32_t material{0};
        bool operator==(const VertexKey& o) const noexcept {
            return packed == o.packed && material == o.material;
        }
    };
    struct VertexKeyHash {
        size_t operator()(const VertexKey& k) const noexcept {
            uint64_t h = static_cast<uint64_t>(k.packed);
            h ^= static_cast<uint64_t>(k.material) + 0x9e3779b97f4a7c15ull + (h << 6) + (h >> 2);
            return static_cast<size_t>(h);
        }
    };

    std::unordered_map<VertexKey, uint32_t, VertexKeyHash> seen;
    seen.reserve(origCount);
    std::vector<Vertex> newVerts;
    newVerts.reserve(origCount);
    std::vector<uint32_t> remap(origCount);

    for (size_t i = 0; i < origCount; ++i) {
        VertexKey key{mesh.vertices[i].packed, mesh.vertices[i].material};
        auto [it, inserted] = seen.try_emplace(key, static_cast<uint32_t>(newVerts.size()));
        if (inserted) newVerts.push_back(mesh.vertices[i]);
        remap[i] = it->second;
    }
    for (auto& idx : mesh.indices)
        idx = remap[idx];
    mesh.vertices = std::move(newVerts);
}

static void deduplicateAndReorderMesh(TerrainEditMesher::MeshResult& mesh) {
    if (mesh.empty()) return;

    deduplicateMeshVertices(mesh);

    // Forsyth triangle reorder
    optimizeTriangleOrderRT(mesh);

    // Vertex fetch reorder: sequential IDs in draw order
    const size_t vc = mesh.vertices.size();
    std::vector<uint32_t> fetchRemap(vc, UINT32_MAX);
    std::vector<Vertex> ordered;
    ordered.reserve(vc);
    uint32_t nextId = 0;
    for (auto& idx : mesh.indices) {
        if (fetchRemap[idx] == UINT32_MAX) {
            fetchRemap[idx] = nextId++;
            ordered.push_back(mesh.vertices[idx]);
        }
        idx = fetchRemap[idx];
    }
    for (size_t i = 0; i < vc; ++i) {
        if (fetchRemap[i] == UINT32_MAX)
            ordered.push_back(mesh.vertices[i]);
    }
    mesh.vertices = std::move(ordered);
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

bool TerrainEditMesher::isSolid(const TerrainFieldSource& field,
                                 int wx, int wy, int wz) {
    // Convert world-voxel coordinate to edit grid coordinate.
    // One voxel = 0.25 m = 8 edit cells, but the base sampler in
    // TerrainFieldSource already operates in edit-grid space
    // (EDIT_CELLS_PER_VOXEL cells per voxel).
    // We query at the cell centre of the voxel.
    const int32_t cellX = wx * EDIT_CELLS_PER_VOXEL + EDIT_CELLS_PER_VOXEL / 2;
    const int32_t cellY = wy * EDIT_CELLS_PER_VOXEL + EDIT_CELLS_PER_VOXEL / 2;
    const int32_t cellZ = wz * EDIT_CELLS_PER_VOXEL + EDIT_CELLS_PER_VOXEL / 2;
    const FieldSample s = field.sample(GridCoord(cellX, cellY, cellZ));
    return s.value.solid;
}

static float materialHash01(int x, int z, uint32_t salt)
{
    uint32_t h = static_cast<uint32_t>(x) * 0x9E3779B9u;
    h ^= static_cast<uint32_t>(z) * 0x85EBCA6Bu;
    h ^= salt * 0xC2B2AE35u;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    return static_cast<float>(h & 0x00FFFFFFu) * (1.0f / 16777216.0f);
}

static uint32_t canonicalBlockMaterial(int wx, int wy, int wz, uint8_t face,
                                       uint16_t pixelsPerVoxel)
{
    // Same material layer as painted faces: not a fragment-time overlay.
    // Top faces get terrain-biome materials; side/bottom faces default to dirt.
    // Variant is intentionally fixed to 0 so greedy meshing can merge broad
    // faces. The shader still produces per-texel variation from world position.
    uint8_t type = (face == 3u) ? 0u : 2u; // top = grass, sides/bottom = dirt

    if (face == 3u) {
        const float worldY = static_cast<float>(wy) * WorldConfig::VOXEL_SIZE_M;
        const float biomeNoise = materialHash01(wx >> 2, wz >> 2, 71u);
        if (worldY < 5.0f) {
            type = 3u; // sand-like low areas
        } else if (biomeNoise > 0.82f) {
            type = 1u; // muddy patches
        }
    }

    return VertexPacking::packMaterial(type, 0u, 0u, pixelsPerVoxel);
}

// ---------------------------------------------------------------------------
// AO (matches the offline tool: side + corner heuristic)
// ---------------------------------------------------------------------------

TerrainEditMesher::FaceAO TerrainEditMesher::calcAO(
        const TerrainFieldSource& field,
        int wx, int wy, int wz,
        int axis, int direction)
{
    auto solid = [&](int dx, int dy, int dz) -> bool {
        return isSolid(field, wx + dx, wy + dy, wz + dz);
    };

    auto vertexAO = [](bool s1, bool s2, bool corner) -> uint8_t {
        if (s1 && s2) return 3;
        if (s1 || s2) return corner ? 2u : 1u;
        return corner ? 1u : 0u;
    };

    FaceAO ao;

    if (axis == 0) { // X-axis faces (u=Z, v=Y)
        int nx = (direction == 1) ? 1 : -1;
        ao.bl = vertexAO(solid(nx, 0, -1), solid(nx, -1,  0), solid(nx, -1, -1));
        ao.br = vertexAO(solid(nx, 0,  1), solid(nx, -1,  0), solid(nx, -1,  1));
        ao.tr = vertexAO(solid(nx, 0,  1), solid(nx,  1,  0), solid(nx,  1,  1));
        ao.tl = vertexAO(solid(nx, 0, -1), solid(nx,  1,  0), solid(nx,  1, -1));
    } else if (axis == 1) { // Y-axis faces (u=X, v=Z)
        int ny = (direction == 1) ? 1 : -1;
        ao.bl = vertexAO(solid(-1, ny, 0), solid(0, ny, -1), solid(-1, ny, -1));
        ao.br = vertexAO(solid( 1, ny, 0), solid(0, ny, -1), solid( 1, ny, -1));
        ao.tr = vertexAO(solid( 1, ny, 0), solid(0, ny,  1), solid( 1, ny,  1));
        ao.tl = vertexAO(solid(-1, ny, 0), solid(0, ny,  1), solid(-1, ny,  1));
    } else { // Z-axis faces (u=X, v=Y)
        int nz = (direction == 1) ? 1 : -1;
        ao.bl = vertexAO(solid(-1, 0, nz), solid(0, -1, nz), solid(-1, -1, nz));
        ao.br = vertexAO(solid( 1, 0, nz), solid(0, -1, nz), solid( 1, -1, nz));
        ao.tr = vertexAO(solid( 1, 0, nz), solid(0,  1, nz), solid( 1,  1, nz));
        ao.tl = vertexAO(solid(-1, 0, nz), solid(0,  1, nz), solid(-1,  1, nz));
    }
    return ao;
}

// ---------------------------------------------------------------------------
// Quad emission (matches offline tool format exactly)
// ---------------------------------------------------------------------------

void TerrainEditMesher::addQuad(MeshResult& out,
                                int axis, int slicePos,
                                int u, int v,
                                int quadW, int quadH,
                                int direction, uint8_t face,
                                uint32_t material,
                                const FaceAO& ao)
{
    uint16_t x0, y0, z0;
    uint16_t x1, y1, z1;
    uint16_t x2, y2, z2;
    uint16_t x3, y3, z3;

    if (axis == 0) { // X face
        uint16_t x = static_cast<uint16_t>(slicePos + (direction == 1 ? 1 : 0));
        x0 = x; y0 = static_cast<uint16_t>(v);             z0 = static_cast<uint16_t>(u);
        x1 = x; y1 = static_cast<uint16_t>(v);             z1 = static_cast<uint16_t>(u + quadW);
        x2 = x; y2 = static_cast<uint16_t>(v + quadH);     z2 = static_cast<uint16_t>(u + quadW);
        x3 = x; y3 = static_cast<uint16_t>(v + quadH);     z3 = static_cast<uint16_t>(u);
    } else if (axis == 1) { // Y face
        uint16_t y = static_cast<uint16_t>(slicePos + (direction == 1 ? 1 : 0));
        x0 = static_cast<uint16_t>(u);         y0 = y; z0 = static_cast<uint16_t>(v);
        x1 = static_cast<uint16_t>(u + quadW); y1 = y; z1 = static_cast<uint16_t>(v);
        x2 = static_cast<uint16_t>(u + quadW); y2 = y; z2 = static_cast<uint16_t>(v + quadH);
        x3 = static_cast<uint16_t>(u);         y3 = y; z3 = static_cast<uint16_t>(v + quadH);
    } else { // Z face
        uint16_t z = static_cast<uint16_t>(slicePos + (direction == 1 ? 1 : 0));
        x0 = static_cast<uint16_t>(u);         y0 = static_cast<uint16_t>(v);         z0 = z;
        x1 = static_cast<uint16_t>(u + quadW); y1 = static_cast<uint16_t>(v);         z1 = z;
        x2 = static_cast<uint16_t>(u + quadW); y2 = static_cast<uint16_t>(v + quadH); z2 = z;
        x3 = static_cast<uint16_t>(u);         y3 = static_cast<uint16_t>(v + quadH); z3 = z;
    }

    // Pack vertices into engine format: X(8)|Y(10)|Z(8)|face(3)|AO(3)
    // Y is stored directly (0-512 range fits in 10 bits)
    auto pack = [face, material](uint16_t px, uint16_t py, uint16_t pz, uint8_t aoVal) -> Vertex {
        uint32_t packed =
            (static_cast<uint32_t>(px)       <<  0) |
            (static_cast<uint32_t>(py)       <<  8) |
            (static_cast<uint32_t>(pz)       << 18) |
            (static_cast<uint32_t>(face & 7) << 26) |
            (static_cast<uint32_t>(aoVal & 7) << 29);
        return {packed, material};
    };

    auto baseIdx = static_cast<uint32_t>(out.vertices.size());
    out.vertices.push_back(pack(x0, y0, z0, ao.bl));
    out.vertices.push_back(pack(x1, y1, z1, ao.br));
    out.vertices.push_back(pack(x2, y2, z2, ao.tr));
    out.vertices.push_back(pack(x3, y3, z3, ao.tl));

    // Update tight AABB in chunk-local METRES (matching precomputed pipeline).
    // The vertex shader reconstructs: worldVoxel = chunkCoord*(128,512,128) + (ux, uy, uz)
    // then metres = worldVoxel * 0.25.  In chunk-local space that's just (px, py, pz) * 0.25.
    constexpr float VS = 0.25f; // WorldConfig::VOXEL_SIZE_M
    auto updateBB = [&](uint16_t px, uint16_t py, uint16_t pz) {
        glm::vec3 m(float(px) * VS, float(py) * VS, float(pz) * VS);
        out.aabbMin = glm::min(out.aabbMin, m);
        out.aabbMax = glm::max(out.aabbMax, m);
    };
    updateBB(x0, y0, z0);
    updateBB(x1, y1, z1);
    updateBB(x2, y2, z2);
    updateBB(x3, y3, z3);

    // Winding order + AO-based diagonal flip (matching offline tool)
    bool useReversedWinding = (axis == 2) ? (direction == 0) : (direction == 1);
    bool flipDiag = (ao.bl + ao.tr) > (ao.br + ao.tl);

    if (useReversedWinding) {
        if (flipDiag) {
            out.indices.push_back(baseIdx + 0); out.indices.push_back(baseIdx + 3); out.indices.push_back(baseIdx + 1);
            out.indices.push_back(baseIdx + 1); out.indices.push_back(baseIdx + 3); out.indices.push_back(baseIdx + 2);
        } else {
            out.indices.push_back(baseIdx + 0); out.indices.push_back(baseIdx + 2); out.indices.push_back(baseIdx + 1);
            out.indices.push_back(baseIdx + 0); out.indices.push_back(baseIdx + 3); out.indices.push_back(baseIdx + 2);
        }
    } else {
        if (flipDiag) {
            out.indices.push_back(baseIdx + 0); out.indices.push_back(baseIdx + 1); out.indices.push_back(baseIdx + 3);
            out.indices.push_back(baseIdx + 1); out.indices.push_back(baseIdx + 2); out.indices.push_back(baseIdx + 3);
        } else {
            out.indices.push_back(baseIdx + 0); out.indices.push_back(baseIdx + 1); out.indices.push_back(baseIdx + 2);
            out.indices.push_back(baseIdx + 0); out.indices.push_back(baseIdx + 2); out.indices.push_back(baseIdx + 3);
        }
    }
}

// ---------------------------------------------------------------------------
// Cache-based AO (matches the offline tool: side + corner heuristic)
// ---------------------------------------------------------------------------

TerrainEditMesher::FaceAO TerrainEditMesher::calcAOCached(
        const SolidCache& cache,
        int wx, int wy, int wz,
        int axis, int direction)
{
    // All AO probes are within the 1-voxel padded cache bounds —
    // skip bounds checks for ~8× fewer branches per face.
    auto solid = [&](int dx, int dy, int dz) -> bool {
        return cache.getUnchecked(wx + dx, wy + dy, wz + dz);
    };

    auto vertexAO = [](bool s1, bool s2, bool corner) -> uint8_t {
        if (s1 && s2) return 3;
        if (s1 || s2) return corner ? 2u : 1u;
        return corner ? 1u : 0u;
    };

    FaceAO ao;

    if (axis == 0) {
        int nx = (direction == 1) ? 1 : -1;
        ao.bl = vertexAO(solid(nx, 0, -1), solid(nx, -1,  0), solid(nx, -1, -1));
        ao.br = vertexAO(solid(nx, 0,  1), solid(nx, -1,  0), solid(nx, -1,  1));
        ao.tr = vertexAO(solid(nx, 0,  1), solid(nx,  1,  0), solid(nx,  1,  1));
        ao.tl = vertexAO(solid(nx, 0, -1), solid(nx,  1,  0), solid(nx,  1, -1));
    } else if (axis == 1) {
        int ny = (direction == 1) ? 1 : -1;
        ao.bl = vertexAO(solid(-1, ny, 0), solid(0, ny, -1), solid(-1, ny, -1));
        ao.br = vertexAO(solid( 1, ny, 0), solid(0, ny, -1), solid( 1, ny, -1));
        ao.tr = vertexAO(solid( 1, ny, 0), solid(0, ny,  1), solid( 1, ny,  1));
        ao.tl = vertexAO(solid(-1, ny, 0), solid(0, ny,  1), solid(-1, ny,  1));
    } else {
        int nz = (direction == 1) ? 1 : -1;
        ao.bl = vertexAO(solid(-1, 0, nz), solid(0, -1, nz), solid(-1, -1, nz));
        ao.br = vertexAO(solid( 1, 0, nz), solid(0, -1, nz), solid( 1, -1, nz));
        ao.tr = vertexAO(solid( 1, 0, nz), solid(0,  1, nz), solid( 1,  1, nz));
        ao.tl = vertexAO(solid(-1, 0, nz), solid(0,  1, nz), solid(-1,  1, nz));
    }
    return ao;
}

// ---------------------------------------------------------------------------
// Build solid cache — single pass over heightmap + single-lock overlay apply
// ---------------------------------------------------------------------------

std::vector<uint8_t> TerrainEditMesher::buildSolidCache(
        const TerrainFieldSource& field,
        const HeightmapBaseSampler& heightmap,
        int baseVoxelX, int baseVoxelZ,
        int cacheMinY, int cacheDimXZ, int cacheDimY,
        const RemeshCancellationToken* cancelToken)
{
    const size_t totalSize = static_cast<size_t>(cacheDimY) * cacheDimXZ * cacheDimXZ;

    const int chunkX = floorDiv(baseVoxelX + 1, 128);  // +1 compensates for the -1 padding
    const int chunkZ = floorDiv(baseVoxelZ + 1, 128);
    const BaseCacheKey key{chunkX, chunkZ, baseVoxelX, baseVoxelZ, cacheMinY, cacheDimXZ, cacheDimY};

    const auto* overlay = field.getOverlay();
    const size_t currentGen = (overlay && overlay->hasAnyEdits())
        ? overlay->getOverlayGeneration() : 0;

    // --- Three-tier cache lookup ---
    {
        std::shared_lock<std::shared_mutex> rlock(s_baseCacheMutex);
        auto it = s_baseCache.find(key);
        if (it != s_baseCache.end()) {
            const auto& entry = it->second;

            // Full hit: overlay generation matches → skip ALL work.
            if (entry.overlayGeneration == currentGen
                && entry.appliedResult.size() == totalSize) {
                return entry.appliedResult;
            }

            // Partial hit: heightmap base is valid, re-apply overlay only.
            if (entry.heightmapBase.size() == totalSize) {
                std::vector<uint8_t> cache = entry.heightmapBase;
                rlock.unlock();

                if (overlay && overlay->hasAnyEdits()) {
                    overlay->bulkApplyToSolidCache(
                        cache.data(), baseVoxelX, baseVoxelZ,
                        cacheMinY, cacheDimXZ, cacheDimY, cancelToken);
                    if (wasCancelled(cancelToken)) {
                        return {};
                    }
                }

                // Update the applied result for next time.
                {
                    std::unique_lock<std::shared_mutex> wlock(s_baseCacheMutex);
                    auto& e = s_baseCache[key];
                    e.appliedResult = cache;
                    e.overlayGeneration = currentGen;
                }
                return cache;
            }
        }
    }

    // Full miss: build from heightmap.
    std::vector<uint8_t> cache(totalSize, 0);

    const int mapW = heightmap.getMapWidth();
    const int mapH = heightmap.getMapHeight();

    for (int cz = 0; cz < cacheDimXZ; ++cz) {
        if (wasCancelled(cancelToken)) {
            return {};
        }
        const int wz = baseVoxelZ + cz;
        for (int cx = 0; cx < cacheDimXZ; ++cx) {
            const int wx = baseVoxelX + cx;

            int height = 0;
            if (wx >= 0 && wx < mapW && wz >= 0 && wz < mapH) {
                height = heightmap.getHeightAtVoxel(wx, wz);
            }

            const int fillMinY = cacheMinY;
            const int fillMaxY = std::min(height, cacheMinY + cacheDimY);
            for (int wy = fillMinY; wy < fillMaxY; ++wy) {
                const int cy = wy - cacheMinY;
                cache[static_cast<size_t>((cy * cacheDimXZ + cz) * cacheDimXZ + cx)] = 1;
            }
        }
    }

    // Save heightmap base, then apply overlay for the applied result.
    std::vector<uint8_t> heightmapBase = cache;

    if (overlay && overlay->hasAnyEdits()) {
        overlay->bulkApplyToSolidCache(
            cache.data(), baseVoxelX, baseVoxelZ,
            cacheMinY, cacheDimXZ, cacheDimY, cancelToken);
        if (wasCancelled(cancelToken)) {
            return {};
        }
    }

    // Store both layers in cache.
    {
        std::unique_lock<std::shared_mutex> wlock(s_baseCacheMutex);
        if (s_baseCache.size() >= BASE_CACHE_MAX_ENTRIES) {
            // Evict ~half the entries instead of clear(); keeps recent working
            // set warm so worker threads under spam don't all hit cold misses.
            const size_t target = BASE_CACHE_MAX_ENTRIES / 2;
            size_t toRemove = s_baseCache.size() - target;
            for (auto it = s_baseCache.begin(); it != s_baseCache.end() && toRemove > 0; ) {
                it = s_baseCache.erase(it);
                --toRemove;
            }
        }
        auto& entry = s_baseCache[key];
        entry.heightmapBase = std::move(heightmapBase);
        entry.appliedResult = cache;
        entry.overlayGeneration = currentGen;
    }

    return cache;
}


// ---------------------------------------------------------------------------
// Build solid cache for 3D voxel base (overhangs / floating islands)
// ---------------------------------------------------------------------------

std::vector<uint8_t> TerrainEditMesher::buildSolidCacheVoxelBase(
        const TerrainFieldSource& field,
        const VoxelBaseSampler& voxelBase,
        int baseVoxelX, int baseVoxelZ,
        int cacheMinY, int cacheDimXZ, int cacheDimY)
{
    const size_t totalSize = static_cast<size_t>(cacheDimY) * cacheDimXZ * cacheDimXZ;
    std::vector<uint8_t> cache(totalSize, 0);

    // Bulk-apply the voxel base store (solid/air overrides from base_voxels.bin).
    voxelBase.getStore().bulkApplyToSolidCache(
        cache.data(), baseVoxelX, baseVoxelZ,
        cacheMinY, cacheDimXZ, cacheDimY);

    // Layer edit overlay on top (snapshot edits).
    const auto* overlay = field.getOverlay();
    if (overlay && overlay->hasAnyEdits()) {
        overlay->bulkApplyToSolidCache(
            cache.data(), baseVoxelX, baseVoxelZ,
            cacheMinY, cacheDimXZ, cacheDimY);
    }

    return cache;
}

// ---------------------------------------------------------------------------
// Main greedy meshing entry point
// ---------------------------------------------------------------------------

TerrainEditMesher::MeshResult TerrainEditMesher::meshChunk(
        const TerrainFieldSource& field,
        const glm::ivec3& chunkCoord,
        int chunkSizeVoxels,
        int chunkHeightVoxels,
        float /*voxelSize*/,
        int hintMinY,
        int hintMaxY,
        const HeightmapBaseSampler* heightmap,
        int lodLevel,
        bool skipPostProcess,
        bool skipAmbientOcclusion,
        const RemeshCancellationToken* cancelToken,
        const VoxelBaseSampler* voxelBase,
        const glm::ivec3* editDirtyVoxelMin,
        const glm::ivec3* editDirtyVoxelMax,
        int bandLocalYMin,
        int bandLocalYMax)
{
    using Clock = std::chrono::steady_clock;
    MeshResult result;
    // Tier B Phase 1: record requested band so diagnostics can compare against
    // the eventually-wired band-clipped path. No-op on the meshing loop today.
    result.stats.bandLocalYMin = bandLocalYMin;
    result.stats.bandLocalYMax = bandLocalYMax;
    result.stats.bandActive    = false;

    // LOD downsampling: at LOD > 0, operate on a coarser voxel grid.
    // step = 2^lodLevel.  The mesher runs at lodRes = chunkSize/step resolution,
    // then vertex positions are scaled back to native range after meshing.
    const int step = (lodLevel > 0) ? (1 << lodLevel) : 1;
    const int nativeChunkSize = chunkSizeVoxels;
    const int nativeChunkHeight = chunkHeightVoxels;
    const bool hasHeightHints = !(hintMinY == -1 && hintMaxY == -1);
    if (step > 1) {
        chunkSizeVoxels /= step;
        chunkHeightVoxels /= step;
        // Convert height hints from native to LOD voxel units
        if (hasHeightHints) {
            hintMinY = floorDiv(hintMinY, step);
            hintMaxY = floorDiv(hintMaxY + step - 1, step);
        }
    }

    // World-voxel origin of this chunk (in LOD voxel units at LOD > 0)
    const int baseX = chunkCoord.x * chunkSizeVoxels;
    const int baseY = chunkCoord.y * chunkHeightVoxels;
    const int baseZ = chunkCoord.z * chunkSizeVoxels;

    // Determine vertical extent of solid voxels in absolute world-voxel units.
    int minY, maxY;
    if (hasHeightHints) {
        minY = std::max(baseY, hintMinY - 1);
        maxY = std::min(baseY + chunkHeightVoxels - 1, hintMaxY + 1);
    } else {
        // Fallback: scan at native resolution, convert to LOD units.
        const int nBaseX = chunkCoord.x * nativeChunkSize;
        const int nBaseY = chunkCoord.y * nativeChunkHeight;
        const int nBaseZ = chunkCoord.z * nativeChunkSize;
        int nMinY = nBaseY + nativeChunkHeight;
        int nMaxY = nBaseY - 1;
        for (int z = 0; z < nativeChunkSize; ++z) {
            if (wasCancelled(cancelToken)) {
                return result;
            }
            for (int x = 0; x < nativeChunkSize; ++x) {
                for (int y = 0; y < nativeChunkHeight; ++y) {
                    const int wy = nBaseY + y;
                    if (isSolid(field, nBaseX + x, wy, nBaseZ + z)) {
                        nMinY = std::min(nMinY, wy);
                        nMaxY = std::max(nMaxY, wy);
                    }
                }
            }
        }
        if (nMaxY < nMinY) {
            return result;
        }
        minY = floorDiv(nMinY, step);
        maxY = floorDiv(nMaxY + step - 1, step);
    }

    if (minY > maxY) {
        return result;
    }

    const int scanMinY = std::max(baseY, minY);
    const int scanMaxY = std::min(baseY + chunkHeightVoxels - 1, maxY);
    if (scanMinY > scanMaxY) {
        return result;
    }
    const int scanMinLocalY = scanMinY - baseY;
    const int scanMaxLocalY = scanMaxY - baseY;

    // --- Build solid cache with 1-voxel padding for neighbor/AO lookups ---
    auto tCacheBuild = Clock::now();
    const int cacheDimXZ = chunkSizeVoxels + 2;
    // Quantize cache Y range so the downsample-cache key (LOD>0) and the
    // base-cache key (LOD0) remain stable across edits that nudge scanMinY
    // by a few voxels. Without this, every brush click that grows or shrinks
    // the solid extent shifts cacheMinY/cacheDimY → key MISS → 5-30 ms of
    // recompute per chunk per click. Quantum of 16 LOD-voxels means the cache
    // only changes shape when terrain Y extent crosses a 16-voxel boundary.
    constexpr int kCacheYQuantize = 16;
    auto floorQuant = [&](int v) {
        return (v >= 0)
            ? (v / kCacheYQuantize) * kCacheYQuantize
            : -(((-v + kCacheYQuantize - 1) / kCacheYQuantize) * kCacheYQuantize);
    };
    auto ceilQuant = [&](int v) {
        return (v >= 0)
            ? ((v + kCacheYQuantize - 1) / kCacheYQuantize) * kCacheYQuantize
            : -((-v / kCacheYQuantize) * kCacheYQuantize);
    };
    const int cacheMinY  = floorQuant(scanMinY - 1);
    const int cacheMaxY  = ceilQuant(scanMaxY + 2);
    const int cacheDimY  = cacheMaxY - cacheMinY;
    const int cacheBaseX = baseX - 1;
    const int cacheBaseZ = baseZ - 1;

    std::vector<uint8_t> cacheData;

    if (step > 1) {
        // LOD > 0: compute downsampled solid cache.
        // Two paths depending on whether overlay edits exist:
        //   - No overlay: O(LOD_voxels) from heightmap directly (~0.1 ms)
        //   - Overlay:    build native cache + optimized downsample (~14 ms)
        const auto* overlay = field.getOverlay();
        const bool hasOverlay = overlay && overlay->hasAnyEdits();

        if (!hasOverlay && heightmap && heightmap->isLoaded()) {
            // FAST PATH: no overlay edits, compute LOD directly from heightmap.
            // Matches offline LOD: solid = (nativeBaseWY < baseHeight) at corner.
            cacheData.resize(static_cast<size_t>(cacheDimY) * cacheDimXZ * cacheDimXZ, 0);
            for (int lcz = 0; lcz < cacheDimXZ; ++lcz) {
                if (wasCancelled(cancelToken)) {
                    return result;
                }
                for (int lcx = 0; lcx < cacheDimXZ; ++lcx) {
                    const int nativeBaseWX = (cacheBaseX + lcx) * step;
                    const int nativeBaseWZ = (cacheBaseZ + lcz) * step;
                    const int baseHeight = heightmap->getHeightAtVoxel(nativeBaseWX, nativeBaseWZ);
                    for (int lcy = 0; lcy < cacheDimY; ++lcy) {
                        const int nativeBaseWY = (cacheMinY + lcy) * step;
                        if (nativeBaseWY < baseHeight)
                            cacheData[static_cast<size_t>((lcy * cacheDimXZ + lcz) * cacheDimXZ + lcx)] = 1;
                    }
                }
            }
        } else {
            // OVERLAY PATH: build native cache then downsample.
            // Optimization: removed per-native-voxel heightmap comparison
            // from downsample loop.  Uses pure occupancy count + single
            // heightmap check per LOD voxel for boundary alignment.
            const int nBaseX = chunkCoord.x * nativeChunkSize;
            const int nBaseZ = chunkCoord.z * nativeChunkSize;
            const int nPad = step;
            const int nDimXZ = nativeChunkSize + 2 * nPad;
            const int nCacheBaseX = nBaseX - nPad;
            const int nCacheBaseZ = nBaseZ - nPad;
            // Quantize Y range to multiples of (step * 16) so that small
            // changes in height hints between edits don't invalidate the
            // buildSolidCache key.  Without this, every edit changes
            // nCacheMinY → full cache miss → 30 ms rebuild.
            const int yQuantize = step * 16;
            const int rawNMinY = scanMinY * step - nPad;
            const int rawNMaxY = (scanMaxY + 1) * step + nPad;
            const int nCacheMinY = (rawNMinY < 0)
                ? -(((-rawNMinY + yQuantize - 1) / yQuantize) * yQuantize)
                : (rawNMinY / yQuantize) * yQuantize;
            const int nCacheMaxY = ((rawNMaxY + yQuantize - 1) / yQuantize) * yQuantize;
            const int nDimY = nCacheMaxY - nCacheMinY;

            std::vector<uint8_t> nativeCacheData;
            if (heightmap && heightmap->isLoaded()) {
                nativeCacheData = buildSolidCache(field, *heightmap,
                                                  nCacheBaseX, nCacheBaseZ,
                                                  nCacheMinY, nDimXZ, nDimY, cancelToken);
                if (wasCancelled(cancelToken)) {
                    return result;
                }
            } else if (voxelBase && voxelBase->isLoaded()) {
                nativeCacheData = buildSolidCacheVoxelBase(field, *voxelBase,
                                                           nCacheBaseX, nCacheBaseZ,
                                                           nCacheMinY, nDimXZ, nDimY);
                if (wasCancelled(cancelToken)) {
                    return result;
                }
            } else {
                nativeCacheData.resize(static_cast<size_t>(nDimY) * nDimXZ * nDimXZ, 0);
                for (int cz = 0; cz < nDimXZ; ++cz) {
                    if (wasCancelled(cancelToken)) {
                        return result;
                    }
                    for (int cx = 0; cx < nDimXZ; ++cx) {
                        const int wx = nCacheBaseX + cx;
                        const int wz = nCacheBaseZ + cz;
                        for (int cy = 0; cy < nDimY; ++cy) {
                            const int wy = nCacheMinY + cy;
                            if (isSolid(field, wx, wy, wz))
                                nativeCacheData[static_cast<size_t>((cy * nDimXZ + cz) * nDimXZ + cx)] = 1;
                        }
                    }
                }
            }

            // Downsample: count solid native voxels per LOD block.
            // All downsample coordinates are within the padded native cache,
            // so we use raw array access (no bounds checks).
            //
            // Tier A.1 cache: keyed by (chunk, lod, padding params), invalidated
            // by overlay generation. Three outcomes:
            //   FULL HIT  — overlayGen matches → reuse cacheData entirely (skip work).
            //   PARTIAL   — overlayGen mismatch + dirty AABB known → copy old
            //               cacheData, recompute only LOD voxels intersecting the
            //               (dirty AABB / step), expanded by 1 for AO border.
            //   MISS      — recompute every LOD voxel.
            const auto tDownsampleStart = Clock::now();
            const auto* overlayPtr = field.getOverlay();
            const size_t currentOverlayGen = (overlayPtr && overlayPtr->hasAnyEdits())
                ? overlayPtr->getOverlayGeneration() : 0;
            const DownsampleCacheKey dsKey{chunkCoord.x, chunkCoord.y, chunkCoord.z, lodLevel,
                                           cacheBaseX, cacheBaseZ, cacheMinY,
                                           cacheDimXZ, cacheDimY};

            int dirtyLodMinX = 0, dirtyLodMaxX = cacheDimXZ - 1;
            int dirtyLodMinY = 0, dirtyLodMaxY = cacheDimY  - 1;
            int dirtyLodMinZ = 0, dirtyLodMaxZ = cacheDimXZ - 1;
            const bool hasDirtyAabb = (editDirtyVoxelMin && editDirtyVoxelMax);
            int dirtyLocalMinX = dirtyLodMinX, dirtyLocalMaxX = dirtyLodMaxX;
            int dirtyLocalMinY = dirtyLodMinY, dirtyLocalMaxY = dirtyLodMaxY;
            int dirtyLocalMinZ = dirtyLodMinZ, dirtyLocalMaxZ = dirtyLodMaxZ;
            if (hasDirtyAabb) {
                // Convert native dirty voxel AABB → LOD voxel range, expand
                // by 1 LOD voxel for AO border (since neighbouring solidity
                // affects this LOD voxel's downsample result via padding cell).
                const int lminX = floorDiv(editDirtyVoxelMin->x, step) - 1;
                const int lmaxX = floorDiv(editDirtyVoxelMax->x, step) + 1;
                const int lminY = floorDiv(editDirtyVoxelMin->y, step) - 1;
                const int lmaxY = floorDiv(editDirtyVoxelMax->y, step) + 1;
                const int lminZ = floorDiv(editDirtyVoxelMin->z, step) - 1;
                const int lmaxZ = floorDiv(editDirtyVoxelMax->z, step) + 1;
                dirtyLocalMinX = std::max(0, lminX - cacheBaseX);
                dirtyLocalMaxX = std::min(cacheDimXZ - 1, lmaxX - cacheBaseX);
                dirtyLocalMinY = std::max(0, lminY - cacheMinY);
                dirtyLocalMaxY = std::min(cacheDimY  - 1, lmaxY - cacheMinY);
                dirtyLocalMinZ = std::max(0, lminZ - cacheBaseZ);
                dirtyLocalMaxZ = std::min(cacheDimXZ - 1, lmaxZ - cacheBaseZ);
            }

            bool didRecomputeFull = true;
            bool runDownsampleLoop = true;
            const size_t expectedSize = static_cast<size_t>(cacheDimY) * cacheDimXZ * cacheDimXZ;
            {
                std::shared_lock<std::shared_mutex> rlock(s_downsampleCacheMutex);
                auto it = s_downsampleCache.find(dsKey);
                if (it != s_downsampleCache.end() && it->second.cacheData.size() == expectedSize) {
                    if (it->second.overlayGeneration == currentOverlayGen) {
                        // FULL HIT — reuse cached data wholesale, skip loop entirely.
                        cacheData = it->second.cacheData;
                        result.stats.downsampleCacheState = 1;
                        didRecomputeFull = false;
                        runDownsampleLoop = false;
                    } else if (hasDirtyAabb &&
                               dirtyLocalMinX <= dirtyLocalMaxX &&
                               dirtyLocalMinY <= dirtyLocalMaxY &&
                               dirtyLocalMinZ <= dirtyLocalMaxZ) {
                        // PARTIAL HIT — copy old, recompute dirty region only.
                        cacheData = it->second.cacheData;
                        result.stats.downsampleCacheState = 2;
                        didRecomputeFull = false;
                        dirtyLodMinX = dirtyLocalMinX; dirtyLodMaxX = dirtyLocalMaxX;
                        dirtyLodMinY = dirtyLocalMinY; dirtyLodMaxY = dirtyLocalMaxY;
                        dirtyLodMinZ = dirtyLocalMinZ; dirtyLodMaxZ = dirtyLocalMaxZ;
                    }
                }
            }
            if (didRecomputeFull) {
                cacheData.assign(expectedSize, 0);
            }

            const int nativeSamplesPerLodVoxel = step * step * step;
            const int solidThreshold = nativeSamplesPerLodVoxel / 2;
            const int stepSq = step * step;

            // ---------- Edit-aware partial-block disambiguation ----------
            // A partial-fill LOD voxel (sc != 0 && sc != stepXY*stepZ) can occur
            // either because the unedited heightmap varies across the block
            // (no edit — must keep offline corner rule for boundary alignment
            // with neighboring precomputed terrain.bin LODs), or because an
            // overlay edit altered some native cells in the block (must use
            // the actual majority so the edit is faithfully visualized).
            //
            // We disambiguate by reconstructing what the count WOULD be in the
            // absence of edits. Heightmap-only solid count for native cell
            // (wx, wy, wz) = (wy < heightmap(wx, wz)). Pre-fetch the step*step
            // heightmap values per (lcx, lcz) once; the inner Y loop then sums
            // clamp(h - nativeBaseWY, 0, step) — O(step^2) per LOD voxel.
            //
            // When sc == expectedUneditedSc the block is unedited → offline
            // corner rule. Otherwise → majority of actual count.
            const bool useEditAwareRule = heightmap && heightmap->isLoaded();
            std::vector<int> heightSamples;
            if (useEditAwareRule) {
                heightSamples.resize(static_cast<size_t>(stepSq));
            }

            if (runDownsampleLoop) {
            for (int lcz = dirtyLodMinZ; lcz <= dirtyLodMaxZ; ++lcz) {
                if (wasCancelled(cancelToken)) {
                    return result;
                }
                for (int lcx = dirtyLodMinX; lcx <= dirtyLodMaxX; ++lcx) {
                    const int lodWX = cacheBaseX + lcx;
                    const int lodWZ = cacheBaseZ + lcz;
                    const int nativeBaseWX = lodWX * step;
                    const int nativeBaseWZ = lodWZ * step;

                    // Pre-fetch the heightmap samples covering this LOD column
                    // once per (lcx, lcz) so the inner Y loop has no overlay-
                    // store / heightmap calls. This is the unedited reference.
                    if (useEditAwareRule) {
                        for (int dz = 0; dz < step; ++dz) {
                            for (int dx = 0; dx < step; ++dx) {
                                heightSamples[dz * step + dx] =
                                    heightmap->getHeightAtVoxel(
                                        nativeBaseWX + dx,
                                        nativeBaseWZ + dz);
                            }
                        }
                    }

                    for (int lcy = dirtyLodMinY; lcy <= dirtyLodMaxY; ++lcy) {
                        const int lodWY = cacheMinY + lcy;
                        const int nativeBaseWY = lodWY * step;

                        // Count solid native voxels using raw array access
                        int sc = 0;
                        for (int dy = 0; dy < step; ++dy) {
                            const int cy = nativeBaseWY + dy - nCacheMinY;
                            for (int dz = 0; dz < step; ++dz) {
                                const int cz = nativeBaseWZ + dz - nCacheBaseZ;
                                const size_t rowBase = static_cast<size_t>((cy * nDimXZ + cz) * nDimXZ
                                                                          + (nativeBaseWX - nCacheBaseX));
                                for (int dx = 0; dx < step; ++dx) {
                                    sc += nativeCacheData[rowBase + dx];
                                }
                            }
                        }

                        bool solid;
                        if (sc == 0) {
                            solid = false;
                        } else if (sc == nativeSamplesPerLodVoxel) {
                            solid = true;
                        } else if (useEditAwareRule) {
                            // Compute the unedited reference count from the
                            // pre-fetched heightmap samples. Each native column
                            // contributes clamp(h - nativeBaseWY, 0, step).
                            int expectedUneditedSc = 0;
                            for (int i = 0; i < stepSq; ++i) {
                                const int delta = heightSamples[i] - nativeBaseWY;
                                if (delta >= step) {
                                    expectedUneditedSc += step;
                                } else if (delta > 0) {
                                    expectedUneditedSc += delta;
                                }
                            }

                            if (sc == expectedUneditedSc) {
                                // Unedited: keep the offline corner rule so
                                // this LOD chunk seam-matches neighboring
                                // unedited chunks loaded from terrain.bin.
                                solid = nativeBaseWY < heightSamples[0];
                            } else {
                                // Edited block: flip the unedited base only
                                // when the edit delta crosses half the LOD
                                // voxel's native volume. This makes the LOD
                                // silhouette/volume track the edit faithfully
                                // (no per-face fattening from a single added
                                // native cell). Sub-LOD-resolution edits
                                // (delta below half threshold) keep the base
                                // result and may not appear at coarse LODs —
                                // unavoidable cost of volume fidelity.
                                const bool baseSolid = nativeBaseWY < heightSamples[0];
                                const int delta = sc - expectedUneditedSc;
                                if (delta >= solidThreshold) {
                                    solid = true;   // BUILD majority
                                } else if (-delta >= solidThreshold) {
                                    solid = false;  // DIG majority
                                } else {
                                    solid = baseSolid;  // Sub-threshold: keep base
                                }
                            }
                        } else {
                            solid = sc >= solidThreshold;
                        }

                        // Tier A.1: write unconditionally so PARTIAL HIT path
                        // can clear LOD voxels that were solid in the cached
                        // copy but became air after the edit (and vice versa).
                        cacheData[static_cast<size_t>((lcy * cacheDimXZ + lcz) * cacheDimXZ + lcx)] =
                            solid ? 1 : 0;
                    }
                }
            }
            }  // end if (runDownsampleLoop)

            // Tier A.1: store recomputed cacheData under the new overlayGen.
            // Skip on FULL HIT — cache already holds an identical copy.
            if (runDownsampleLoop) {
                std::unique_lock<std::shared_mutex> wlock(s_downsampleCacheMutex);
                if (s_downsampleCache.size() >= DOWNSAMPLE_CACHE_MAX_ENTRIES) {
                    // Evict half instead of clear() — same reasoning as s_baseCache.
                    const size_t target = DOWNSAMPLE_CACHE_MAX_ENTRIES / 2;
                    size_t toRemove = s_downsampleCache.size() - target;
                    for (auto it = s_downsampleCache.begin();
                         it != s_downsampleCache.end() && toRemove > 0; ) {
                        it = s_downsampleCache.erase(it);
                        --toRemove;
                    }
                }
                auto& entry = s_downsampleCache[dsKey];
                entry.cacheData = cacheData;
                entry.overlayGeneration = currentOverlayGen;
            }
            result.stats.downsampleMs = std::chrono::duration<float, std::milli>(
                Clock::now() - tDownsampleStart).count();
        }
    } else if (heightmap && heightmap->isLoaded()) {
        // LOD 0 fast path: build cache from heightmap + overlay
        cacheData = buildSolidCache(field, *heightmap,
                                    cacheBaseX, cacheBaseZ,
                                    cacheMinY, cacheDimXZ, cacheDimY, cancelToken);
        if (wasCancelled(cancelToken)) {
            return result;
        }
    } else if (voxelBase && voxelBase->isLoaded()) {
        // LOD 0 fast path: build cache from 3D voxel base + overlay
        cacheData = buildSolidCacheVoxelBase(field, *voxelBase,
                                             cacheBaseX, cacheBaseZ,
                                             cacheMinY, cacheDimXZ, cacheDimY);
        if (wasCancelled(cancelToken)) {
            return result;
        }
    } else {
        // LOD 0 fallback: build cache via isSolid()
        cacheData.resize(static_cast<size_t>(cacheDimY) * cacheDimXZ * cacheDimXZ, 0);
        for (int cz = 0; cz < cacheDimXZ; ++cz) {
            if (wasCancelled(cancelToken)) {
                return result;
            }
            for (int cx = 0; cx < cacheDimXZ; ++cx) {
                const int wx = cacheBaseX + cx;
                const int wz = cacheBaseZ + cz;
                for (int cy = 0; cy < cacheDimY; ++cy) {
                    const int wy = cacheMinY + cy;
                    if (isSolid(field, wx, wy, wz)) {
                        cacheData[static_cast<size_t>((cy * cacheDimXZ + cz) * cacheDimXZ + cx)] = 1;
                    }
                }
            }
        }
    }

    SolidCache cache{};
    cache.data  = cacheData.data();
    cache.baseX = cacheBaseX;
    cache.baseZ = cacheBaseZ;
    cache.minY  = cacheMinY;
    cache.dimXZ = cacheDimXZ;
    cache.dimY  = cacheDimY;

    auto tCacheDone = Clock::now();

    // Count solid voxels in the cache for diagnostics
    uint32_t solidCount = 0;
    for (uint8_t b : cacheData) solidCount += b;
    result.stats.cacheVoxels = static_cast<uint32_t>(cacheData.size());
    result.stats.solidVoxels = solidCount;
    result.stats.scanYRange  = scanMaxY - scanMinY + 1;
    result.stats.cacheDimXZ  = cacheDimXZ;
    result.stats.monolithicWorkVoxels = static_cast<uint64_t>(chunkSizeVoxels)
        * static_cast<uint64_t>(chunkSizeVoxels)
        * static_cast<uint64_t>(std::max(result.stats.scanYRange, 0));

    // --- Pre-compute per-Y-level occupancy and per-column Y bounds ---
    // Used to skip entirely-air Y layers and tighten face scan per column.
    const int yLevels = scanMaxLocalY - scanMinLocalY + 1;

    // Per-column (x,z) min/max local Y of solid voxels.
    // Layout: colMinY[z * chunkSizeVoxels + x], indexed by chunk-local coords.
    const size_t colCount = static_cast<size_t>(chunkSizeVoxels) * chunkSizeVoxels;
    std::vector<int> colMinY(colCount, scanMaxLocalY + 1);
    std::vector<int> colMaxY(colCount, scanMinLocalY - 1);

    for (int cz = 1; cz <= chunkSizeVoxels; ++cz) {
        if (wasCancelled(cancelToken)) {
            return result;
        }
        const int lz = cz - 1;   // chunk-local z
        for (int cx = 1; cx <= chunkSizeVoxels; ++cx) {
            const int lx = cx - 1; // chunk-local x
            const size_t colIdx = static_cast<size_t>(lz * chunkSizeVoxels + lx);
            for (int ly = scanMinLocalY; ly <= scanMaxLocalY; ++ly) {
                // Cache coords: cy = (baseY + ly) - cacheMinY, cacheZ = cz, cacheX = cx
                const int cy = baseY + ly - cacheMinY;
                if (cacheData[static_cast<size_t>((cy * cacheDimXZ + cz) * cacheDimXZ + cx)] != 0) {
                    if (ly < colMinY[colIdx]) colMinY[colIdx] = ly;
                    if (ly > colMaxY[colIdx]) colMaxY[colIdx] = ly;
                }
            }
        }
    }

    // ---- Adaptive localized meshing ----
    // Heavy edited chunks keep one shared solid cache, but greedy meshing
    // now subdivides expensive regions into smaller X/Y/Z work units.

    uint32_t facesEmitted = 0;

    constexpr uint8_t FACE_NEG_X = 0;
    constexpr uint8_t FACE_POS_X = 1;
    constexpr uint8_t FACE_NEG_Y = 2;
    constexpr uint8_t FACE_POS_Y = 3;
    constexpr uint8_t FACE_NEG_Z = 4;
    constexpr uint8_t FACE_POS_Z = 5;

    const auto* textureStore = field.getTextureMaterialStore();
    const bool hasTextureMaterials = textureStore &&
        textureStore->hasSurfaceTexturesInBox(
            glm::ivec3(baseX, baseY, baseZ),
            glm::ivec3(baseX + chunkSizeVoxels,
                       baseY + chunkHeightVoxels,
                       baseZ + chunkSizeVoxels),
            lodLevel);
    const uint16_t texturePixelsPerVoxel = textureStore
        ? textureStore->getLODConfig(lodLevel).pixelsPerVoxel
        : uint16_t{16};
    auto sampleFaceMaterial = [&](int wx, int wy, int wz, uint8_t face) -> uint32_t {
        const uint32_t baseMaterial =
            canonicalBlockMaterial(wx, wy, wz, face, texturePixelsPerVoxel);

        if (!textureStore || !hasTextureMaterials) {
            return baseMaterial;
        }

        const auto tex = textureStore->getSurfaceTexture(glm::ivec3(wx, wy, wz), lodLevel, face);
        if (tex.isEmpty()) {
            return baseMaterial;
        }

        // Brush paint now lives in the same baked material word as the block
        // texture. It replaces the canonical face material for this voxel face;
        // it is not drawn as a second render-time overlay layer.
        // Variant stays procedural in the shader from world position. Keeping
        // it out of the mesh key lets broad regions merge by material class
        // instead of exploding into one quad per hashed face.
        return VertexPacking::packMaterial(
            static_cast<uint8_t>(tex.getType()),
            0u,
            tex.getEdgeMask(),
            texturePixelsPerVoxel);
    };

    struct SurfaceFace {
        int16_t u{0};
        int16_t v{0};
        FaceAO ao{};
        uint32_t material{0};
    };

    struct MeshingRegion {
        int minX{0};
        int maxX{0}; // exclusive
        int minY{0}; // inclusive
        int maxY{0}; // inclusive
        int minZ{0};
        int maxZ{0}; // exclusive
        int depth{0};
    };

    auto computeRegionYBounds = [&](const MeshingRegion& region,
                                    int& outMinY,
                                    int& outMaxY) -> bool {
        outMinY = region.maxY;
        outMaxY = region.minY - 1;
        for (int lz = region.minZ; lz < region.maxZ; ++lz) {
            for (int lx = region.minX; lx < region.maxX; ++lx) {
                const size_t colIdx = static_cast<size_t>(lz * chunkSizeVoxels + lx);
                const int colLo = std::max(colMinY[colIdx], region.minY);
                const int colHi = std::min(colMaxY[colIdx], region.maxY);
                if (colLo > colHi) {
                    continue;
                }
                if (colLo < outMinY) outMinY = colLo;
                if (colHi > outMaxY) outMaxY = colHi;
            }
        }
        return outMaxY >= outMinY;
    };

    auto meshRegionLeaf = [&](const MeshingRegion& region) {
        if (region.minX >= region.maxX || region.minZ >= region.maxZ || region.minY > region.maxY) {
            return;
        }

        const int regionWidth = region.maxX - region.minX;
        const int regionDepth = region.maxZ - region.minZ;
        const int regionHeight = region.maxY - region.minY + 1;
        if (regionWidth <= 0 || regionDepth <= 0 || regionHeight <= 0) {
            return;
        }

        ++result.stats.adaptiveLeafRegions;
        result.stats.adaptiveMaxDepth = std::max(
            result.stats.adaptiveMaxDepth,
            static_cast<uint32_t>(region.depth));
        const uint32_t regionWork = static_cast<uint32_t>(regionWidth * regionDepth * regionHeight);
        result.stats.adaptiveWorkVoxels += static_cast<uint64_t>(regionWork);
        result.stats.adaptivePeakRegionVoxels = std::max(
            result.stats.adaptivePeakRegionVoxels,
            regionWork);
        result.stats.adaptivePeakYRange = std::max(
            result.stats.adaptivePeakYRange,
            static_cast<uint32_t>(regionHeight));

        std::vector<std::vector<SurfaceFace>> faceBins[6];
        faceBins[FACE_NEG_X].resize(regionWidth);
        faceBins[FACE_POS_X].resize(regionWidth);
        faceBins[FACE_NEG_Y].resize(regionHeight);
        faceBins[FACE_POS_Y].resize(regionHeight);
        faceBins[FACE_NEG_Z].resize(regionDepth);
        faceBins[FACE_POS_Z].resize(regionDepth);

        for (int lz = region.minZ; lz < region.maxZ; ++lz) {
            if (wasCancelled(cancelToken)) {
                return;
            }
            for (int lx = region.minX; lx < region.maxX; ++lx) {
                const size_t colIdx = static_cast<size_t>(lz * chunkSizeVoxels + lx);
                const int yStart = std::max(colMinY[colIdx], region.minY);
                const int yEnd = std::min(colMaxY[colIdx], region.maxY);
                if (yStart > yEnd) {
                    continue;
                }

                for (int ly = yStart; ly <= yEnd; ++ly) {
                    const int wx = baseX + lx;
                    const int wy = baseY + ly;
                    const int wz = baseZ + lz;

                    if (!cache.getUnchecked(wx, wy, wz)) {
                        continue;
                    }

                    const int localX = lx - region.minX;
                    const int localY = ly - region.minY;
                    const int localZ = lz - region.minZ;

                    if (!cache.getUnchecked(wx - 1, wy, wz)) {
                        faceBins[FACE_NEG_X][localX].push_back({static_cast<int16_t>(localZ),
                                                                static_cast<int16_t>(localY),
                                                                skipAmbientOcclusion ? FaceAO{} : calcAOCached(cache, wx, wy, wz, 0, 0),
                                                                sampleFaceMaterial(wx, wy, wz, FACE_NEG_X)});
                        ++facesEmitted;
                    }
                    if (!cache.getUnchecked(wx + 1, wy, wz)) {
                        faceBins[FACE_POS_X][localX].push_back({static_cast<int16_t>(localZ),
                                                                static_cast<int16_t>(localY),
                                                                skipAmbientOcclusion ? FaceAO{} : calcAOCached(cache, wx, wy, wz, 0, 1),
                                                                sampleFaceMaterial(wx, wy, wz, FACE_POS_X)});
                        ++facesEmitted;
                    }
                    if (!cache.getUnchecked(wx, wy - 1, wz)) {
                        faceBins[FACE_NEG_Y][localY].push_back({static_cast<int16_t>(localX),
                                                                static_cast<int16_t>(localZ),
                                                                skipAmbientOcclusion ? FaceAO{} : calcAOCached(cache, wx, wy, wz, 1, 0),
                                                                sampleFaceMaterial(wx, wy, wz, FACE_NEG_Y)});
                        ++facesEmitted;
                    }
                    if (!cache.getUnchecked(wx, wy + 1, wz)) {
                        faceBins[FACE_POS_Y][localY].push_back({static_cast<int16_t>(localX),
                                                                static_cast<int16_t>(localZ),
                                                                skipAmbientOcclusion ? FaceAO{} : calcAOCached(cache, wx, wy, wz, 1, 1),
                                                                sampleFaceMaterial(wx, wy, wz, FACE_POS_Y)});
                        ++facesEmitted;
                    }
                    if (!cache.getUnchecked(wx, wy, wz - 1)) {
                        faceBins[FACE_NEG_Z][localZ].push_back({static_cast<int16_t>(localX),
                                                                static_cast<int16_t>(localY),
                                                                skipAmbientOcclusion ? FaceAO{} : calcAOCached(cache, wx, wy, wz, 2, 0),
                                                                sampleFaceMaterial(wx, wy, wz, FACE_NEG_Z)});
                        ++facesEmitted;
                    }
                    if (!cache.getUnchecked(wx, wy, wz + 1)) {
                        faceBins[FACE_POS_Z][localZ].push_back({static_cast<int16_t>(localX),
                                                                static_cast<int16_t>(localY),
                                                                skipAmbientOcclusion ? FaceAO{} : calcAOCached(cache, wx, wy, wz, 2, 1),
                                                                sampleFaceMaterial(wx, wy, wz, FACE_POS_Z)});
                        ++facesEmitted;
                    }
                }
            }
        }

        const size_t maxSliceSize = std::max({
            static_cast<size_t>(regionDepth) * regionHeight,
            static_cast<size_t>(regionWidth) * regionHeight,
            static_cast<size_t>(regionWidth) * regionDepth});
        std::vector<uint8_t> mask(maxSliceSize, 0);
        std::vector<FaceAO> aoMask(maxSliceSize);
        std::vector<uint32_t> materialMask(maxSliceSize, 0u);
        std::vector<uint8_t> visited(maxSliceSize, 0);

        for (int faceId = 0; faceId < 6; ++faceId) {
            if (wasCancelled(cancelToken)) {
                return;
            }

            const int axis = faceId / 2;
            const int dir = faceId % 2;
            const uint8_t face = static_cast<uint8_t>(faceId);

            int uDim = 0;
            int uOffset = 0;
            int vOffset = 0;
            if (axis == 0) {
                uDim = regionDepth;
                uOffset = region.minZ;
                vOffset = region.minY;
            } else if (axis == 1) {
                uDim = regionWidth;
                uOffset = region.minX;
                vOffset = region.minZ;
            } else {
                uDim = regionWidth;
                uOffset = region.minX;
                vOffset = region.minY;
            }

            for (size_t sliceIdx = 0; sliceIdx < faceBins[faceId].size(); ++sliceIdx) {
                if (wasCancelled(cancelToken)) {
                    return;
                }

                auto& entries = faceBins[faceId][sliceIdx];
                if (entries.empty()) {
                    continue;
                }

                const int n = (axis == 0)
                    ? region.minX + static_cast<int>(sliceIdx)
                    : (axis == 1)
                        ? region.minY + static_cast<int>(sliceIdx)
                        : region.minZ + static_cast<int>(sliceIdx);

                int minV = INT_MAX;
                int maxV = INT_MIN;
                for (const auto& sf : entries) {
                    const size_t idx = static_cast<size_t>(sf.u + sf.v * uDim);
                    mask[idx] = 1;
                    aoMask[idx] = sf.ao;
                    materialMask[idx] = sf.material;
                    if (sf.v < minV) minV = sf.v;
                    if (sf.v > maxV) maxV = sf.v;
                }

                const int mergeVLimit = maxV + 1;
                for (int j = minV; j <= maxV; ++j) {
                    for (int i = 0; i < uDim; ++i) {
                        const int idx = i + j * uDim;
                        if (!mask[static_cast<size_t>(idx)] || visited[static_cast<size_t>(idx)]) continue;

                        const FaceAO curAO = aoMask[static_cast<size_t>(idx)];
                        const uint32_t curMaterial = materialMask[static_cast<size_t>(idx)];
                        const int planeBl = static_cast<int>(curAO.bl);
                        const int planeDu = static_cast<int>(curAO.br) - planeBl;
                        const int planeDv = static_cast<int>(curAO.tl) - planeBl;
                        const bool planeValid = static_cast<int>(curAO.tr) == (planeBl + planeDu + planeDv);

                        auto matchesPlane = [&](const FaceAO& ao, int relU, int relV) -> bool {
                            const int base = planeBl + planeDu * relU + planeDv * relV;
                            const int expectedBl = base;
                            const int expectedBr = base + planeDu;
                            const int expectedTl = base + planeDv;
                            const int expectedTr = base + planeDu + planeDv;

                            auto inRange = [](int v) { return v >= 0 && v <= 3; };
                            if (!inRange(expectedBl) || !inRange(expectedBr)
                                || !inRange(expectedTl) || !inRange(expectedTr)) {
                                return false;
                            }

                            return static_cast<int>(ao.bl) == expectedBl
                                && static_cast<int>(ao.br) == expectedBr
                                && static_cast<int>(ao.tl) == expectedTl
                                && static_cast<int>(ao.tr) == expectedTr;
                        };

                        auto growExact = [&]() -> std::pair<int, int> {
                            int qwLocal = 1;
                            while (i + qwLocal < uDim) {
                                const int ni = idx + qwLocal;
                                if (!mask[static_cast<size_t>(ni)] || visited[static_cast<size_t>(ni)]
                                    || aoMask[static_cast<size_t>(ni)] != curAO
                                    || materialMask[static_cast<size_t>(ni)] != curMaterial) {
                                    break;
                                }
                                ++qwLocal;
                            }

                            int qhLocal = 1;
                            bool canExpand = true;
                            while (canExpand && j + qhLocal < mergeVLimit) {
                                for (int di = 0; di < qwLocal; ++di) {
                                    const int ci = (i + di) + (j + qhLocal) * uDim;
                                    if (!mask[static_cast<size_t>(ci)] || visited[static_cast<size_t>(ci)]
                                        || aoMask[static_cast<size_t>(ci)] != curAO
                                        || materialMask[static_cast<size_t>(ci)] != curMaterial) {
                                        canExpand = false;
                                        break;
                                    }
                                }
                                if (canExpand) ++qhLocal;
                            }
                            return {qwLocal, qhLocal};
                        };

                        auto growAffine = [&]() -> std::pair<int, int> {
                            if (!planeValid) {
                                return {1, 1};
                            }

                            int qwLocal = 1;
                            while (i + qwLocal < uDim) {
                                const int ni = idx + qwLocal;
                                if (!mask[static_cast<size_t>(ni)] || visited[static_cast<size_t>(ni)]
                                    || !matchesPlane(aoMask[static_cast<size_t>(ni)], qwLocal, 0)
                                    || materialMask[static_cast<size_t>(ni)] != curMaterial) {
                                    break;
                                }
                                ++qwLocal;
                            }

                            int qhLocal = 1;
                            bool canExpand = true;
                            while (canExpand && j + qhLocal < mergeVLimit) {
                                for (int di = 0; di < qwLocal; ++di) {
                                    const int ci = (i + di) + (j + qhLocal) * uDim;
                                    if (!mask[static_cast<size_t>(ci)] || visited[static_cast<size_t>(ci)]
                                        || !matchesPlane(aoMask[static_cast<size_t>(ci)], di, qhLocal)
                                        || materialMask[static_cast<size_t>(ci)] != curMaterial) {
                                        canExpand = false;
                                        break;
                                    }
                                }
                                if (canExpand) ++qhLocal;
                            }
                            return {qwLocal, qhLocal};
                        };

                        auto [exactW, exactH] = growExact();
                        auto [affineW, affineH] = growAffine();
                        const int exactArea = exactW * exactH;
                        const int affineArea = affineW * affineH;
                        const bool useAffine = planeValid && affineArea > exactArea && affineArea > 1;
                        const int qw = useAffine ? affineW : exactW;
                        const int qh = useAffine ? affineH : exactH;

                        FaceAO emitAO = curAO;
                        if (useAffine) {
                            const int bl = planeBl;
                            const int br = planeBl + planeDu * qw;
                            const int tl = planeBl + planeDv * qh;
                            const int tr = planeBl + planeDu * qw + planeDv * qh;
                            emitAO.bl = static_cast<uint8_t>(std::clamp(bl, 0, 3));
                            emitAO.br = static_cast<uint8_t>(std::clamp(br, 0, 3));
                            emitAO.tl = static_cast<uint8_t>(std::clamp(tl, 0, 3));
                            emitAO.tr = static_cast<uint8_t>(std::clamp(tr, 0, 3));
                        }

                        for (int dj = 0; dj < qh; ++dj) {
                            for (int di = 0; di < qw; ++di) {
                                visited[static_cast<size_t>((i + di) + (j + dj) * uDim)] = true;
                            }
                        }

                        addQuad(result, axis, n, uOffset + i, vOffset + j, qw, qh, dir, face, curMaterial, emitAO);
                    }
                }

                for (int j = minV; j <= maxV; ++j) {
                    std::fill_n(&mask[static_cast<size_t>(j * uDim)], uDim, uint8_t(0));
                    std::fill_n(&materialMask[static_cast<size_t>(j * uDim)], uDim, uint32_t(0));
                    std::fill_n(&visited[static_cast<size_t>(j * uDim)], uDim, uint8_t(0));
                }
            }
        }
    };

    constexpr int ADAPTIVE_TARGET_XZ = 32;
    constexpr int ADAPTIVE_TARGET_Y = 128;
    constexpr int ADAPTIVE_MIN_XZ = 8;
    constexpr int ADAPTIVE_MIN_Y = 32;
    constexpr int MAX_ADAPTIVE_DEPTH = 3;

    auto shouldSplitRegion = [&](const MeshingRegion& region) -> bool {
        if (region.depth >= MAX_ADAPTIVE_DEPTH) {
            return false;
        }

        const int sizeX = region.maxX - region.minX;
        const int sizeZ = region.maxZ - region.minZ;
        const int yRange = region.maxY - region.minY + 1;
        const bool splitX = sizeX > ADAPTIVE_TARGET_XZ;
        const bool splitZ = sizeZ > ADAPTIVE_TARGET_XZ;
        const bool splitY = yRange > ADAPTIVE_TARGET_Y;
        if (!splitX && !splitZ && !splitY) {
            return false;
        }

        const int64_t approxWork = static_cast<int64_t>(sizeX) * sizeZ * yRange;
        return approxWork > (static_cast<int64_t>(ADAPTIVE_TARGET_XZ) * ADAPTIVE_TARGET_XZ * 96)
            || splitY;
    };

    const bool useAdaptivePartition = step == 1
        && (result.stats.scanYRange > 96 || solidCount > 350000u);

    const MeshingRegion rootRegion{
        0,
        chunkSizeVoxels,
        scanMinLocalY,
        scanMaxLocalY,
        0,
        chunkSizeVoxels,
        0};

    if (useAdaptivePartition) {
        auto processRegion = [&](auto&& self, const MeshingRegion& region) -> void {
            if (wasCancelled(cancelToken)) {
                return;
            }

            int actualMinY = region.minY;
            int actualMaxY = region.maxY;
            if (!computeRegionYBounds(region, actualMinY, actualMaxY)) {
                return;
            }

            MeshingRegion tightened = region;
            tightened.minY = actualMinY;
            tightened.maxY = actualMaxY;

            if (!shouldSplitRegion(tightened)) {
                meshRegionLeaf(tightened);
                return;
            }

            const int sizeX = tightened.maxX - tightened.minX;
            const int sizeZ = tightened.maxZ - tightened.minZ;
            const int yRange = tightened.maxY - tightened.minY + 1;

            const bool splitX = sizeX > ADAPTIVE_TARGET_XZ && sizeX > ADAPTIVE_MIN_XZ;
            const bool splitZ = sizeZ > ADAPTIVE_TARGET_XZ && sizeZ > ADAPTIVE_MIN_XZ;
            const bool splitY = yRange > ADAPTIVE_TARGET_Y && yRange > ADAPTIVE_MIN_Y;

            if (!splitX && !splitZ && !splitY) {
                meshRegionLeaf(tightened);
                return;
            }

            result.stats.adaptiveEnabled = true;
            ++result.stats.adaptiveSplitRegions;
            result.stats.adaptiveMaxDepth = std::max(
                result.stats.adaptiveMaxDepth,
                static_cast<uint32_t>(tightened.depth + 1));

            const int xMid = splitX ? (tightened.minX + sizeX / 2) : tightened.maxX;
            const int zMid = splitZ ? (tightened.minZ + sizeZ / 2) : tightened.maxZ;
            const int yMid = splitY ? (tightened.minY + yRange / 2) : tightened.maxY;

            const int xParts = splitX ? 2 : 1;
            const int zParts = splitZ ? 2 : 1;
            const int yParts = splitY ? 2 : 1;

            for (int yi = 0; yi < yParts; ++yi) {
                const int childMinY = splitY ? (yi == 0 ? tightened.minY : yMid + 1) : tightened.minY;
                const int childMaxY = splitY ? (yi == 0 ? yMid : tightened.maxY) : tightened.maxY;
                if (childMinY > childMaxY) {
                    continue;
                }

                for (int zi = 0; zi < zParts; ++zi) {
                    const int childMinZ = splitZ ? (zi == 0 ? tightened.minZ : zMid) : tightened.minZ;
                    const int childMaxZ = splitZ ? (zi == 0 ? zMid : tightened.maxZ) : tightened.maxZ;
                    if (childMinZ >= childMaxZ) {
                        continue;
                    }

                    for (int xi = 0; xi < xParts; ++xi) {
                        const int childMinX = splitX ? (xi == 0 ? tightened.minX : xMid) : tightened.minX;
                        const int childMaxX = splitX ? (xi == 0 ? xMid : tightened.maxX) : tightened.maxX;
                        if (childMinX >= childMaxX) {
                            continue;
                        }

                        self(self, MeshingRegion{
                            childMinX,
                            childMaxX,
                            childMinY,
                            childMaxY,
                            childMinZ,
                            childMaxZ,
                            tightened.depth + 1});
                    }
                }
            }
        };

        processRegion(processRegion, rootRegion);
    } else {
        meshRegionLeaf(rootRegion);
    }

    // LOD vertex rescaling: expand LOD-resolution positions to native range
    auto tGreedyDone = Clock::now();
    if (step > 1 && !result.empty()) {
        for (auto& v : result.vertices) {
            uint32_t p = v.packed;
            uint32_t x  = p & 0xFFu;
            uint32_t ys = (p >> 8) & 0x3FFu;   // stored Y = actual Y (10 bits)
            uint32_t z  = (p >> 18) & 0xFFu;
            uint32_t hi = p & 0xFC000000u;      // face(3) + AO(3)

            x  *= static_cast<uint32_t>(step);
            ys *= static_cast<uint32_t>(step);  // scale Y directly
            z  *= static_cast<uint32_t>(step);

            v.packed = (x & 0xFFu) | ((ys & 0x3FFu) << 8) | ((z & 0xFFu) << 18) | hi;
        }
        const float fStep = static_cast<float>(step);
        result.aabbMin *= fStep;
        result.aabbMax *= fStep;
    }

    if (!result.empty()) {
        const float lodVoxelSize = 0.25f * static_cast<float>(step);
        const float pad = lodVoxelSize * 0.5f;
        result.aabbMin -= pad;
        result.aabbMax += pad;
    }

    // Post-process:
    //  - Quality mode does full dedup + reorder for best draw efficiency.
    //  - Fast edit mode usually skips it for latency, but very large edited
    //    meshes still get a lightweight dedup pass so upload/finalize does
    //    not dominate the visible latency.
    auto tPostStart = Clock::now();
    if (!skipPostProcess) {
        deduplicateAndReorderMesh(result);
    } else {
        const size_t rawLinearSubmeshEstimate =
            (result.vertices.size() + MAX_VERTS_PER_SUBMESH - 1) / MAX_VERTS_PER_SUBMESH;
        const bool shouldFastCompact =
            rawLinearSubmeshEstimate > FAST_COMPACT_SUBMESH_THRESHOLD ||
            result.vertices.size() >= FAST_COMPACT_VERT_THRESHOLD ||
            result.indices.size() >= FAST_COMPACT_INDEX_THRESHOLD;
        if (shouldFastCompact) {
        deduplicateMeshVertices(result);
        }
    }
    auto tPostDone = Clock::now();

    auto toMs = [](auto a, auto b) {
        return std::chrono::duration<float, std::milli>(b - a).count();
    };
    result.stats.cacheBuildMs  = toMs(tCacheBuild, tCacheDone);
    result.stats.greedyMeshMs  = toMs(tCacheDone, tGreedyDone);
    result.stats.postProcessMs = toMs(tPostStart, tPostDone);
    result.stats.facesEmitted  = facesEmitted;

    return result;
}

// ---------------------------------------------------------------------------
// Split a large mesh into GPU-uploadable sub-meshes (each ≤ 65535 verts)
// ---------------------------------------------------------------------------
static uint32_t decodePackedX(const Vertex& v) {
    return v.packed & 0xFFu;
}

static uint32_t decodePackedZ(const Vertex& v) {
    return (v.packed >> 18) & 0xFFu;
}

static uint32_t decodePackedY(const Vertex& v) {
    return (v.packed >> 8) & 0x3FFu;
}

struct OctRegion {
    int minX{0};
    int maxX{128};
    int minY{0};
    int maxY{512};
    int minZ{0};
    int maxZ{128};
};

static std::vector<TerrainEditMesher::SubMesh> splitLinearlyToSubMeshes(
        const TerrainEditMesher::MeshResult& mesh)
{
    std::vector<TerrainEditMesher::SubMesh> out;
    if (mesh.empty()) return out;

    if (mesh.vertices.size() <= MAX_VERTS_PER_SUBMESH) {
        TerrainEditMesher::SubMesh sub;
        sub.vertices = mesh.vertices;
        sub.indices.reserve(mesh.indices.size());
        for (uint32_t idx : mesh.indices)
            sub.indices.push_back(static_cast<uint16_t>(idx));
        out.push_back(std::move(sub));
        return out;
    }

    TerrainEditMesher::SubMesh current;
    std::unordered_map<uint32_t, uint32_t> remap;

    auto finalize = [&]() {
        if (!current.vertices.empty()) {
            out.push_back(std::move(current));
            current = TerrainEditMesher::SubMesh{};
            remap.clear();
        }
    };

    for (size_t i = 0; i + 2 < mesh.indices.size(); i += 3) {
        const uint32_t i0 = mesh.indices[i + 0];
        const uint32_t i1 = mesh.indices[i + 1];
        const uint32_t i2 = mesh.indices[i + 2];

        size_t newVerts = 0;
        if (remap.find(i0) == remap.end()) ++newVerts;
        if (remap.find(i1) == remap.end()) ++newVerts;
        if (remap.find(i2) == remap.end()) ++newVerts;

        if (current.vertices.size() + newVerts > MAX_VERTS_PER_SUBMESH) {
            finalize();
        }

        auto addVert = [&](uint32_t oldIdx) -> uint16_t {
            auto it = remap.find(oldIdx);
            if (it != remap.end())
                return static_cast<uint16_t>(it->second);
            const uint32_t newIdx = static_cast<uint32_t>(current.vertices.size());
            current.vertices.push_back(mesh.vertices[oldIdx]);
            remap[oldIdx] = newIdx;
            return static_cast<uint16_t>(newIdx);
        };

        current.indices.push_back(addVert(i0));
        current.indices.push_back(addVert(i1));
        current.indices.push_back(addVert(i2));
    }

    finalize();
    return out;
}

static std::array<TerrainEditMesher::MeshResult, 8> splitMeshIntoOctants(
        const TerrainEditMesher::MeshResult& mesh,
        const OctRegion& region)
{
    struct CellBuilder {
        TerrainEditMesher::MeshResult mesh;
        std::unordered_map<uint32_t, uint32_t> remap;
    };

    std::vector<CellBuilder> cells(8);
    const int midX = region.minX + (region.maxX - region.minX) / 2;
    const int midY = region.minY + (region.maxY - region.minY) / 2;
    const int midZ = region.minZ + (region.maxZ - region.minZ) / 2;

    for (size_t i = 0; i + 2 < mesh.indices.size(); i += 3) {
        const uint32_t i0 = mesh.indices[i + 0];
        const uint32_t i1 = mesh.indices[i + 1];
        const uint32_t i2 = mesh.indices[i + 2];

        const Vertex& v0 = mesh.vertices[i0];
        const Vertex& v1 = mesh.vertices[i1];
        const Vertex& v2 = mesh.vertices[i2];

        const float centroidX =
            (static_cast<float>(decodePackedX(v0)) +
             static_cast<float>(decodePackedX(v1)) +
             static_cast<float>(decodePackedX(v2))) / 3.0f;
        const float centroidY =
            (static_cast<float>(decodePackedY(v0)) +
             static_cast<float>(decodePackedY(v1)) +
             static_cast<float>(decodePackedY(v2))) / 3.0f;
        const float centroidZ =
            (static_cast<float>(decodePackedZ(v0)) +
             static_cast<float>(decodePackedZ(v1)) +
             static_cast<float>(decodePackedZ(v2))) / 3.0f;

        const bool east = centroidX >= static_cast<float>(midX);
        const bool top = centroidY >= static_cast<float>(midY);
        const bool north = centroidZ >= static_cast<float>(midZ);
        const int octant = (east ? 1 : 0) + (top ? 2 : 0) + (north ? 4 : 0);
        CellBuilder& cell = cells[static_cast<size_t>(octant)];

        auto addVert = [&](uint32_t oldIdx) -> uint32_t {
            auto it = cell.remap.find(oldIdx);
            if (it != cell.remap.end()) {
                return it->second;
            }
            const uint32_t newIdx = static_cast<uint32_t>(cell.mesh.vertices.size());
            cell.mesh.vertices.push_back(mesh.vertices[oldIdx]);
            cell.remap[oldIdx] = newIdx;
            return newIdx;
        };

        cell.mesh.indices.push_back(addVert(i0));
        cell.mesh.indices.push_back(addVert(i1));
        cell.mesh.indices.push_back(addVert(i2));
    }

    std::array<TerrainEditMesher::MeshResult, 8> out{};
    for (int q = 0; q < 8; ++q) {
        out[static_cast<size_t>(q)] = std::move(cells[static_cast<size_t>(q)].mesh);
    }
    return out;
}

static bool canSplitOctants(const OctRegion& region) {
    return (region.maxX - region.minX) > 1 &&
           (region.maxY - region.minY) > 1 &&
           (region.maxZ - region.minZ) > 1;
}

static std::array<OctRegion, 8> childOctants(const OctRegion& region) {
    const int midX = region.minX + (region.maxX - region.minX) / 2;
    const int midY = region.minY + (region.maxY - region.minY) / 2;
    const int midZ = region.minZ + (region.maxZ - region.minZ) / 2;
    std::array<OctRegion, 8> out{};
    for (int oct = 0; oct < 8; ++oct) {
        const bool east = (oct & 1) != 0;
        const bool top = (oct & 2) != 0;
        const bool north = (oct & 4) != 0;
        out[static_cast<size_t>(oct)] = OctRegion{
            east ? midX : region.minX,
            east ? region.maxX : midX,
            top ? midY : region.minY,
            top ? region.maxY : midY,
            north ? midZ : region.minZ,
            north ? region.maxZ : midZ
        };
    }
    return out;
}

static std::vector<TerrainEditMesher::SubMesh> splitMeshOctreeSelective(
        const TerrainEditMesher::MeshResult& mesh,
        const OctRegion& region,
        size_t remainingBudget)
{
    std::vector<TerrainEditMesher::SubMesh> out;

    if (mesh.empty()) {
        return out;
    }

    // Leaf: fits in one submesh or can't subdivide further
    if (mesh.vertices.size() <= MAX_VERTS_PER_SUBMESH || !canSplitOctants(region)) {
        return splitLinearlyToSubMeshes(mesh);
    }

    auto childRegions = childOctants(region);
    auto childMeshes = splitMeshIntoOctants(mesh, region);

    int nonEmptyCount = 0;
    for (int q = 0; q < 8; ++q) {
        if (!childMeshes[static_cast<size_t>(q)].empty()) ++nonEmptyCount;
    }
    if (nonEmptyCount == 0) {
        return out;
    }

    for (int q = 0; q < 8; ++q) {
        auto& childMesh = childMeshes[static_cast<size_t>(q)];
        if (childMesh.empty()) continue;

        const size_t budgetForChild = (remainingBudget > out.size())
            ? (remainingBudget - out.size()) : 0;

        std::vector<TerrainEditMesher::SubMesh> childSubs;
        if (childMesh.vertices.size() > MAX_VERTS_PER_SUBMESH &&
            canSplitOctants(childRegions[static_cast<size_t>(q)]) &&
            budgetForChild >= 2) {
            childSubs = splitMeshOctreeSelective(
                childMesh,
                childRegions[static_cast<size_t>(q)],
                budgetForChild);
            // If recursion produced nothing useful, fall back to linear
            if (childSubs.empty()) {
                childSubs = splitLinearlyToSubMeshes(childMesh);
            }
        } else {
            childSubs = splitLinearlyToSubMeshes(childMesh);
        }

        for (auto& sub : childSubs) {
            out.push_back(std::move(sub));
        }

        if (remainingBudget > 0 && out.size() >= remainingBudget) {
            break;
        }
    }

    return out;
}

// Merge two submeshes into one. Requires combined vertex count ≤ 65535.
static void mergeSubMesh(TerrainEditMesher::SubMesh& dst,
                         TerrainEditMesher::SubMesh&& src)
{
    const uint16_t base = static_cast<uint16_t>(dst.vertices.size());
    dst.vertices.insert(dst.vertices.end(),
                        std::make_move_iterator(src.vertices.begin()),
                        std::make_move_iterator(src.vertices.end()));
    dst.indices.reserve(dst.indices.size() + src.indices.size());
    for (uint16_t idx : src.indices) {
        dst.indices.push_back(static_cast<uint16_t>(idx + base));
    }
}

// Consolidate a submesh list that exceeds budget by merging the smallest
// pairs until we fit within MAX_RUNTIME_SUBMESHES.
static void consolidateSubmeshes(std::vector<TerrainEditMesher::SubMesh>& subs,
                                 size_t maxCount)
{
    while (subs.size() > maxCount) {
        // Find the two smallest submeshes that can be merged (combined ≤ 65535)
        size_t bestA = SIZE_MAX, bestB = SIZE_MAX;
        size_t bestCombined = SIZE_MAX;
        for (size_t i = 0; i < subs.size(); ++i) {
            for (size_t j = i + 1; j < subs.size(); ++j) {
                size_t combined = subs[i].vertices.size() + subs[j].vertices.size();
                if (combined <= MAX_VERTS_PER_SUBMESH && combined < bestCombined) {
                    bestA = i;
                    bestB = j;
                    bestCombined = combined;
                }
            }
        }
        if (bestA == SIZE_MAX) {
            // No mergeable pair found — can't reduce further
            break;
        }
        mergeSubMesh(subs[bestA], std::move(subs[bestB]));
        subs.erase(subs.begin() + static_cast<ptrdiff_t>(bestB));
    }
}

std::vector<TerrainEditMesher::SubMesh> TerrainEditMesher::splitToSubMeshes(
        const MeshResult& mesh)
{
    std::vector<SubMesh> out;
    if (mesh.empty()) return out;

    auto linearSubs = splitLinearlyToSubMeshes(mesh);
    if (linearSubs.size() <= MAX_RUNTIME_SUBMESHES) {
        return linearSubs;
    }

    // Only try octree fallback when the minimal linear path still exceeds the
    // runtime submesh budget. Subchunks are not culled independently, so extra
    // spatial splits hurt upload/render cost without giving visibility wins.
    auto octreeSubs = splitMeshOctreeSelective(
        mesh,
        OctRegion{
            0,
            WorldConfig::CHUNK_SIZE,
            0,
            WorldConfig::CHUNK_HEIGHT,
            0,
            WorldConfig::CHUNK_SIZE
        },
        MAX_RUNTIME_SUBMESHES);

    // Pick whichever produced fewer, then merge to fit budget.
    auto& best = (octreeSubs.size() > 0 && octreeSubs.size() < linearSubs.size())
        ? octreeSubs : linearSubs;

    std::cout << "[TerrainEditMesher] WARNING: Mesh with " << mesh.vertices.size()
              << " vertices produced " << best.size()
              << " submeshes (max " << MAX_RUNTIME_SUBMESHES
              << "). Consolidating by merging smallest pairs." << std::endl;

    consolidateSubmeshes(best, MAX_RUNTIME_SUBMESHES);

    if (best.size() > MAX_RUNTIME_SUBMESHES) {
        std::cerr << "[TerrainEditMesher] CRITICAL: Could not consolidate to "
                  << MAX_RUNTIME_SUBMESHES << " submeshes (still "
                  << best.size() << "). ChunkUploadSystem will TRUNCATE \u2014 "
                  << "expect stretched/missing triangles. Bump MAX_SUBCHUNKS "
                  << "(Chunk.h / GPUCullingSystem.h / shader #defines / this file)."
                  << std::endl;
    }

#ifndef NDEBUG
    // Post-condition: every submesh must have all indices in-range.
    for (size_t s = 0; s < best.size(); ++s) {
        const auto& sub = best[s];
        const uint32_t vc = static_cast<uint32_t>(sub.vertices.size());
        for (uint16_t idx : sub.indices) {
            if (static_cast<uint32_t>(idx) >= vc) {
                std::cerr << "[TerrainEditMesher] CRITICAL: submesh " << s
                          << " has out-of-range index " << static_cast<uint32_t>(idx)
                          << " (vertex count " << vc << ")." << std::endl;
                break;
            }
        }
    }
#endif

    return best;
}

} // namespace TerrainEdit

````

## include\world\edit\TerrainEditMesher.h

Description: No CC-DESC found. C++ class 'TerrainEditMesher'.

````cpp
#pragma once

#include "world/edit/TerrainEditTypes.h"
#include "world/edit/TerrainFieldSource.h"
#include "world/edit/HeightmapBaseSampler.h"
#include "rendering/common/Mesh.h"
#include <vector>
#include <cstdint>
#include <glm/glm.hpp>

// Forward declaration — full header included in TerrainEditMesher.cpp
namespace TerrainEdit { class VoxelBaseSampler; }

namespace TerrainEdit {

/**
 * Runtime greedy mesher for edited terrain chunks.
 *
 * Takes a TerrainFieldSource (base heightmap + sparse edit overlay) and produces
 * greedy-meshed vertex/index data compatible with the engine's packed
 * geometry + procedural material vertex format and SubChunk pipeline.
 *
 * Design priorities (in order):
 *   1. Correctness — watertight meshes matching the packed vertex format
 *   2. Speed — greedy merge identical faces, minimal allocations
 *   3. Quality — per-vertex AO matching the offline tool's approach
 */
class TerrainEditMesher {
public:
    struct MeshStats {
        float cacheBuildMs{0.0f};    // solid cache construction
        float greedyMeshMs{0.0f};   // face detection + greedy merge + AO
        float postProcessMs{0.0f};  // dedup/reorder/fast-compaction (usually 0 in fast mode)
        float downsampleMs{0.0f};    // LOD downsample loop (LOD>0 + overlay only)
        uint8_t downsampleCacheState{0}; // 0=miss, 1=full hit (no work), 2=partial (region recompute)
        uint32_t cacheVoxels{0};     // total voxels in solid cache
        uint32_t solidVoxels{0};     // solid voxels in cache
        uint32_t facesEmitted{0};    // quads emitted before merge
        int scanYRange{0};           // scanMaxY - scanMinY + 1
        int cacheDimXZ{0};           // cache XZ dimension (with padding)
        bool adaptiveEnabled{false}; // true when the chunk was actually split
        uint32_t adaptiveLeafRegions{0};
        uint32_t adaptiveSplitRegions{0};
        uint32_t adaptiveMaxDepth{0};
        uint32_t adaptivePeakRegionVoxels{0};
        uint32_t adaptivePeakYRange{0};
        uint64_t adaptiveWorkVoxels{0};   // sum of leaf region volumes
        uint64_t monolithicWorkVoxels{0}; // equivalent single-region volume

        // Tier B Phase 1 scaffolding (no behavior change yet).
        // bandLocalYMin/Max are the chunk-local Y range the caller asked us
        // to remesh; -1/-1 = full chunk (current behavior). Future Phase 2
        // will clip the greedy face loop to this band and splice with a
        // cached out-of-band mesh.
        int      bandLocalYMin{-1};
        int      bandLocalYMax{-1};
        bool     bandActive{false};       // true if Phase 2 actually clipped
        uint32_t bandFacesEmitted{0};     // faces emitted inside the band
    };

    struct MeshResult {
        std::vector<Vertex>   vertices;
        std::vector<uint32_t> indices;
        glm::vec3 aabbMin{1e10f};
        glm::vec3 aabbMax{-1e10f};
        MeshStats stats;
        bool empty() const { return vertices.empty(); }
    };

    /**
     * Split a mesh with uint32_t indices into multiple GPU-uploadable
     * sub-meshes. The runtime path prefers spatial grid splits (2x2, 3x3,
     * 4x4) before falling back to linear triangle-order splitting so heavily
     * edited chunks keep a stable silhouette while staying under the 16-bit
     * index limit.
     */
    struct SubMesh {
        std::vector<Vertex>   vertices;
        std::vector<uint16_t> indices;
    };
    static std::vector<SubMesh> splitToSubMeshes(const MeshResult& mesh);

    /**
     * Mesh one chunk from the merged terrain field at native (0.25 m) voxel resolution.
     *
     * @param field             Merged terrain field source (base + overlay)
     * @param chunkCoord        Chunk coordinate in World chunk space (e.g. 0..159)
     * @param chunkSizeVoxels   Number of voxels per chunk edge XZ (128)
     * @param chunkHeightVoxels Number of voxels per chunk height Y (512)
     * @param voxelSize         Size of one voxel in meters (0.25)
     * @param hintMinY          Optional lower bound of solid voxels (from heightmap).
     *                          –1 means "unknown — the mesher must scan".
     * @param hintMaxY          Optional upper bound (same convention).
     */
    static MeshResult meshChunk(const TerrainFieldSource& field,
                                const glm::ivec3& chunkCoord,
                                int chunkSizeVoxels = 128,
                                int chunkHeightVoxels = 512,
                                float voxelSize = 0.25f,
                                int hintMinY = -1,
                                int hintMaxY = -1,
                                const HeightmapBaseSampler* heightmap = nullptr,
                                int lodLevel = 0,
                                bool skipPostProcess = false,
                                bool skipAmbientOcclusion = false,
                                const RemeshCancellationToken* cancelToken = nullptr,
                                const VoxelBaseSampler* voxelBase = nullptr,
                                // Optional edit-dirty world-voxel AABB for the LOD>0 incremental
                                // downsample fast path. {INT_MIN, INT_MAX} = sentinel "unknown,
                                // do full downsample". Components are inclusive native voxel coords.
                                const glm::ivec3* editDirtyVoxelMin = nullptr,
                                const glm::ivec3* editDirtyVoxelMax = nullptr,
                                // Tier B Phase 1 scaffolding: chunk-local Y band the caller
                                // would prefer the greedy loop to focus on. -1/-1 = full chunk
                                // (current behavior, splice not yet wired). Phase 2 will use
                                // these to clip the face emission loop and splice the result
                                // with a per-chunk face cache.
                                int bandLocalYMin = -1,
                                int bandLocalYMax = -1);

private:
    struct FaceAO {
        uint8_t bl{0}, br{0}, tr{0}, tl{0};
        bool operator==(const FaceAO& o) const {
            return bl == o.bl && br == o.br && tr == o.tr && tl == o.tl;
        }
        bool operator!=(const FaceAO& o) const { return !(*this == o); }
    };

    /**
     * Internal: query whether a voxel is solid at world voxel coordinate.
     * Wraps TerrainFieldSource::sample, converting voxel coords to grid coords.
     */
    static bool isSolid(const TerrainFieldSource& field,
                        int worldVoxelX, int worldVoxelY, int worldVoxelZ);

    /// Calculate ambient occlusion for one face vertex quartet.
    static FaceAO calcAO(const TerrainFieldSource& field,
                         int wx, int wy, int wz,
                         int axis, int direction);

    // --- Cache-based fast paths (no locks, no hashing) ---

    /**
     * Pre-built solid cache covering [baseX, baseX+dimXZ) × [minY, minY+dimY) × [baseZ, baseZ+dimXZ).
     * Stores 1 = solid, 0 = air.  Index: ((y-minY)*dimXZ + (z-baseZ))*dimXZ + (x-baseX).
     */
    struct SolidCache {
        const uint8_t* data;
        int baseX, baseZ, minY;
        int dimXZ, dimY;

        inline bool get(int wx, int wy, int wz) const {
            const int cx = wx - baseX;
            const int cy = wy - minY;
            const int cz = wz - baseZ;
            if (cx < 0 || cx >= dimXZ || cy < 0 || cy >= dimY || cz < 0 || cz >= dimXZ)
                return false;
            return data[static_cast<size_t>((cy * dimXZ + cz) * dimXZ + cx)] != 0;
        }

        /// Unchecked access — caller guarantees coords are within the padded cache.
        /// Saves ~6 comparisons per call vs get().
        inline bool getUnchecked(int wx, int wy, int wz) const {
            return data[static_cast<size_t>(((wy - minY) * dimXZ + (wz - baseZ)) * dimXZ + (wx - baseX))] != 0;
        }
    };

    /// Build the solid cache for a chunk and its 1-voxel border (heightmap path).
    static std::vector<uint8_t> buildSolidCache(
        const TerrainFieldSource& field,
        const HeightmapBaseSampler& heightmap,
        int baseVoxelX, int baseVoxelZ,
        int cacheMinY, int cacheDimXZ, int cacheDimY,
        const RemeshCancellationToken* cancelToken = nullptr);

    /// Build solid cache when a 3D voxel base is used instead of a heightmap.
    /// Starts all-air, bulk-applies voxelBase store, then bulk-applies edit overlay.
    static std::vector<uint8_t> buildSolidCacheVoxelBase(
        const TerrainFieldSource& field,
        const VoxelBaseSampler& voxelBase,
        int baseVoxelX, int baseVoxelZ,
        int cacheMinY, int cacheDimXZ, int cacheDimY);

    /// Cache-based AO calculation (no locks).
    static FaceAO calcAOCached(const SolidCache& cache,
                               int wx, int wy, int wz,
                               int axis, int direction);

    /// Mesh a single Y-band sub-region using a pre-built solid cache.
    /// The band covers Y ∈ [bandMinY, bandMaxY] inclusive.
    static void meshBandRegion(MeshResult& result,
                               const SolidCache& cache,
                               int chunkSizeVoxels,
                               int chunkHeightVoxels,
                               int baseX, int baseZ,
                               int bandMinY, int bandMaxY);

    /// Emit a greedy-merged quad into the result.
    static void addQuad(MeshResult& out,
                        int axis, int slicePos,
                        int u, int v,
                        int quadW, int quadH,
                        int direction, uint8_t face,
                        uint32_t material,
                        const FaceAO& ao);
};

} // namespace TerrainEdit

````

## include\rendering\common\Mesh.h

Description: No CC-DESC found. C++ struct 'Vertex'.

````cpp
#pragma once

#include <glm/glm.hpp>
#include <vector>
#include <cstdint>

// Compact terrain vertex format.
// Bit packing: X(8 bits) + Y(10 bits) + Z(8 bits) + face(3 bits) + AO(3 bits) = 32 bits
// - X: 8 bits = 0-128 (vertex positions for 128 voxels need 129 values)
// - Y: 10 bits = 0-1023 (supports vertical chunk stacks without height-1 offset seams)
// - Z: 8 bits = 0-128 (vertex positions for 128 voxels need 129 values)
// - Face: 3 bits = 6 directions
// - AO: 3 bits = ambient occlusion level
//
// Material is intentionally tiny metadata, not per-voxel bitmap data. Generated
// terrain can carry a fallback 32-bit procedural-material word per vertex, while
// texture-paint edits normally arrive through the sparse material overlay SSBO.
struct Vertex {
    uint32_t packed{0};
    uint32_t material{0};
};

namespace VertexPacking {
    inline uint32_t textureResolutionLog2(uint16_t pixelsPerVoxel) {
        uint32_t p = pixelsPerVoxel;
        if (p < 2u) p = 2u;
        if (p > 1024u) p = 1024u;
        uint32_t log2 = 1u;
        while ((1u << log2) < p && log2 < 10u) {
            ++log2;
        }
        return log2;
    }

    inline uint32_t packMaterial(uint8_t type,
                                 uint8_t variant,
                                 uint8_t edgeMask,
                                 uint16_t pixelsPerVoxel) {
        return 0x80000000u |
               ((static_cast<uint32_t>(type) & 0x3u) << 0) |
               ((static_cast<uint32_t>(variant) & 0x7u) << 2) |
               ((static_cast<uint32_t>(edgeMask) & 0x3u) << 5) |
               ((textureResolutionLog2(pixelsPerVoxel) & 0xFu) << 7);
    }

    // Pack chunk-relative position into uint32.
    // Bit layout: X(8) | Y(10) | Z(8) | face(3) | AO(3) = 32 bits total
    inline Vertex packChunkRelativeVertex(float chunkRelX,
                                          float chunkRelY,
                                          float chunkRelZ,
                                          uint8_t faceDir,
                                          uint8_t extras = 0) {
        // X: 0-8m -> 0-128 (8 bits) - 128 voxels need 129 vertex positions
        uint32_t x = static_cast<uint32_t>(glm::clamp(chunkRelX * (128.0f / 8.0f), 0.0f, 128.0f));
        // Y: Store direct voxel coordinate (0-1023) to avoid height-1 seams on stacked Y chunks
        uint32_t y = static_cast<uint32_t>(glm::clamp(chunkRelY * 4.0f, 0.0f, 1023.0f));
        // Z: 0-8m -> 0-128 (8 bits) - 128 voxels need 129 vertex positions
        uint32_t z = static_cast<uint32_t>(glm::clamp(chunkRelZ * (128.0f / 8.0f), 0.0f, 128.0f));
        // Face: 0-7 (3 bits)
        uint32_t face = static_cast<uint32_t>(faceDir) & 0x7;
        // Extras: 3-bit payload for AO (bits 29-31)
        uint32_t extraBits = static_cast<uint32_t>(extras) & 0x7;
        
        // Pack into 32 bits: X(bits 0-7) | Y(bits 8-17) | Z(bits 18-25) | face(bits 26-28) | AO(bits 29-31)
        uint32_t packed = (x << 0) | (y << 8) | (z << 18) | (face << 26) | (extraBits << 29);
        
        return {packed, 0u};
    }
    
    // Face normal indices
    enum FaceDirection : uint8_t {
        FACE_NEG_X = 0,
        FACE_POS_X = 1,
        FACE_NEG_Y = 2,
        FACE_POS_Y = 3,
        FACE_NEG_Z = 4,
        FACE_POS_Z = 5
    };
    
    // Pack color and face direction into 16-bit integer
    // [0-9]   Color index (10 bits, 0-1023) - 10-bit palette for better color fidelity
    // [10-12] Face normal (3 bits, 0-5)
    // [13-15] Spare (3 bits)
    inline uint16_t packData(uint16_t colorIndex, uint8_t faceDir) {
        uint16_t color = colorIndex & 0x3FF;  // 10 bits
        uint16_t face = static_cast<uint16_t>(faceDir) & 0x7;  // 3 bits
        
        return (color << 0) | (face << 10);
    }
    
    // Color palette: map RGB colors to 10-bit indices (1024 colors)
    // Returns index into a 1024-color palette (better than 8-bit)
    inline uint16_t rgbToColorIndex(const glm::vec3& rgb) {
        // 10-bit palette: 4-3-3 bit RGB encoding
        // R: 4 bits (16 levels), G: 3 bits (8 levels), B: 3 bits (8 levels)
        uint16_t r = static_cast<uint16_t>(rgb.r * 15.0f) & 0xF;
        uint16_t g = static_cast<uint16_t>(rgb.g * 7.0f) & 0x7;
        uint16_t b = static_cast<uint16_t>(rgb.b * 7.0f) & 0x7;
        
        return (r << 6) | (g << 3) | b;
    }
    
    // Determine face direction from axis and direction
    inline uint8_t getFaceDirection(int axis, int dir) {
        // axis: 0=X, 1=Y, 2=Z
        // dir: -1=negative, +1=positive
        return static_cast<uint8_t>(axis * 2 + (dir > 0 ? 1 : 0));
    }
}

class Mesh {
public:
    std::vector<Vertex> vertices;
    std::vector<uint16_t> indices;

    Mesh() = default;
    Mesh(const std::vector<Vertex>& verts, const std::vector<uint16_t>& inds)
        : vertices(verts), indices(inds) {}

    // Factory methods for common shapes
    static Mesh createCube();
};

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

## src\world\WorldLODSwaps.cpp

Description: No CC-DESC found.

````cpp
// WorldLODSwaps.cpp — LOD mesh release/reload, LOD batch swap processing, diagnostics
// Extracted from WorldLODTransitions.cpp to reduce compilation unit size.

#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "world/ChunkHoleTracker.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <iostream>
#include <algorithm>
#include <chrono>
#include <unordered_map>
#include <string>

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
                if (meshHandle.isValid()) {
                    chunksToRelease.push_back(entity);
                    totalVertices += meshHandle.getTotalVertexBytes();
                    totalIndices += meshHandle.getTotalIndexBytes();
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
                meshHandle.collectBufferSlices(vbSlices, ibSlices);
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
    {
        std::unique_lock regLock(m_registryMutex);
        auto view = m_registry.view<PendingMeshHandle>();
        for (auto entity : view) {
            ++pendingCount;
            auto& pending = view.get<PendingMeshHandle>(entity);
            std::vector<BufferSlice> vbSlices;
            std::vector<BufferSlice> ibSlices;
            pending.handle.collectBufferSlices(vbSlices, ibSlices);
            if (m_vbAllocator && !vbSlices.empty()) {
                m_vbAllocator->freeBatch(vbSlices.data(), vbSlices.size());
            }
            if (m_ibAllocator && !ibSlices.empty()) {
                m_ibAllocator->freeBatch(ibSlices.data(), ibSlices.size());
            }
            if (m_gpuCulling && pending.handle.gpuCullingSlot != UINT32_MAX) {
                m_gpuCulling->freeSlot(pending.handle.gpuCullingSlot);
            }
        }
        // Remove all PendingMeshHandle components in one pass
        m_registry.clear<PendingMeshHandle>();
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

size_t World::processLODSwaps(BufferSuballocator* vbAllocator,
                               BufferSuballocator* ibAllocator) {
    using Clock = std::chrono::high_resolution_clock;
    auto& diag = m_currentFinalizeDiag;

    if (!m_chunkManager) return 0;

    size_t totalSwapped = 0;

    struct DeferredFree {
        std::vector<BufferSlice> vbs;
        std::vector<BufferSlice> ibs;
        uint32_t gpuCullingSlot{UINT32_MAX};
    };
    struct CollisionRefreshRequest {
        entt::entity entity{entt::null};
        glm::ivec3 coord{0};
        int lodLevel{0};
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
    std::vector<SwapVisualReady> visualReadyEntries;

    // Process up to 3 completed batches per frame — during CPU spikes batches
    // pile up and processing only 1/frame creates visible LOD transition gaps.
    // 3 batches keeps swap cost bounded while draining the backlog faster.
    static constexpr int MAX_LOD_SWAPS_PER_FRAME = 3;
    for (int swapIter = 0; swapIter < MAX_LOD_SWAPS_PER_FRAME; ++swapIter) {
    if (LODTransitionBatch* batch = m_chunkManager->getCompletedBatch()) {
        uint32_t batchId = batch->batchId;
        size_t batchSwapped = 0;
        size_t batchInvalidEntities = 0;
        size_t batchMissingPending = 0;
        size_t batchMismatchedPending = 0;
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
                const uint64_t pendingVramBytes = pending.handle.getTotalVramBytes();
                const uint32_t pendingVertexCount = pending.handle.getTotalVertexCount();
                const uint32_t pendingIndexCount = pending.handle.getTotalIndexCount();

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
                    oldHandle.collectBufferSlices(df.vbs, df.ibs);
                    df.gpuCullingSlot = oldHandle.gpuCullingSlot;
                    deferredFrees.push_back(df);
                }

                meshStatsAdd(pending.handle);
                m_registry.emplace_or_replace<MeshHandle>(entity, pending.handle);
                m_registry.remove<PendingMeshHandle>(entity);

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
                        pending.debugInfo
                    });
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

        // Accumulate swap errors into LOD switch diagnostics
        if (m_lodSwitchDiag.active) {
            m_lodSwitchDiag.errInvalidEntities += static_cast<uint32_t>(batchInvalidEntities);
            m_lodSwitchDiag.errMissingPending += static_cast<uint32_t>(batchMissingPending);
            m_lodSwitchDiag.errMismatchedBatch += static_cast<uint32_t>(batchMismatchedPending);
            m_lodSwitchDiag.errTotalFromSwaps += static_cast<uint32_t>(
                batchInvalidEntities + batchMissingPending + batchMismatchedPending);
        }

        m_chunkManager->removeCompletedBatch(batchId);
    } else {
        break;  // No more completed batches
    }
    } // end swapIter loop

    diag.lodSwapEntityCount += static_cast<uint32_t>(totalSwapped);

    // Bump mesh topology version so shadow cache knows geometry changed
    if (totalSwapped > 0)
        m_meshTopologyVersion.fetch_add(1, std::memory_order_relaxed);

    // Phase 2: Free old resources OUTSIDE registry lock using BATCH methods
    // This avoids 128+ individual mutex lock/unlock cycles and multiple coalesce/HWM passes
    {
        auto t = Clock::now();

        // Collect slices and slots into contiguous arrays for batch free
        std::vector<BufferSlice> vbSlices;
        std::vector<BufferSlice> ibSlices;
        std::vector<uint32_t> cullingSlots;
        vbSlices.reserve(deferredFrees.size());
        ibSlices.reserve(deferredFrees.size());
        cullingSlots.reserve(deferredFrees.size());

        for (auto& df : deferredFrees) {
            vbSlices.insert(vbSlices.end(), df.vbs.begin(), df.vbs.end());
            ibSlices.insert(ibSlices.end(), df.ibs.begin(), df.ibs.end());
            if (df.gpuCullingSlot != UINT32_MAX) cullingSlots.push_back(df.gpuCullingSlot);
        }

        // Batch free: single lock + single coalesce per allocator
        if (vbAllocator && !vbSlices.empty())
            vbAllocator->freeBatch(vbSlices.data(), vbSlices.size());
        if (ibAllocator && !ibSlices.empty())
            ibAllocator->freeBatch(ibSlices.data(), ibSlices.size());
        // Batch free: single lock + single HWM recalculation
        if (m_gpuCulling && !cullingSlots.empty())
            m_gpuCulling->freeSlots(cullingSlots.data(), cullingSlots.size());

        diag.lodSwapFreeMs += std::chrono::duration<float, std::milli>(Clock::now() - t).count();
    }

    for (const auto& request : collisionRefreshes) {
        refreshEditedChunkCollisionFromArtifact(request.entity, request.coord, request.lodLevel);
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

    // === ChunkHole tracking: record swap events and detect anomalies ===
    if (!visualReadyEntries.empty()) {
        const float nowSec = ChunkHoleEvent::nowSec();
        std::vector<ChunkHoleTracker::SwappedChunkInfo> swappedInfos;
        swappedInfos.reserve(visualReadyEntries.size());

        for (const auto& entry : visualReadyEntries) {
            // Record LODSwapExecuted event
            ChunkHoleEvent swapEvent{};
            swapEvent.type = ChunkHoleEvent::Type::LODSwapExecuted;
            swapEvent.timestampSec = nowSec;
            swapEvent.toLOD = entry.lodLevel;
            swapEvent.vertexCount = entry.vertexCount;
            swapEvent.indexCount = entry.indexCount;
            swapEvent.vramBytes = entry.vramBytes;
            swapEvent.batchId = entry.debugInfo.fromLodBatch ? 1 : 0;
            swapEvent.artifactSource = entry.debugInfo.artifactSource;
            swapEvent.subChunkCount = static_cast<uint8_t>(entry.debugInfo.subChunkCount);
            m_chunkHoleTracker.recordEvent(entry.coord, std::move(swapEvent));

            // Build info for post-swap anomaly detection
            ChunkHoleTracker::SwappedChunkInfo info{};
            info.coord = entry.coord;
            info.vertexCount = entry.vertexCount;
            info.indexCount = entry.indexCount;
            info.lodLevel = entry.lodLevel;
            info.subChunkCount = static_cast<uint8_t>(entry.debugInfo.subChunkCount);

            // Look up live chunk state for isEmpty / MeshHandle / gpuCulling checks
            {
                std::shared_lock regLock(m_registryMutex);
                entt::entity entity = findChunk(entry.coord);
                if (entity != entt::null && m_registry.valid(entity)) {
                    if (m_registry.all_of<Chunk>(entity)) {
                        const auto& chunk = m_registry.get<Chunk>(entity);
                        info.isEmpty = chunk.isEmpty;
                        info.effectiveDataLod = chunk.effectiveDataLod;
                    }
                    info.hasMeshHandle = m_registry.all_of<MeshHandle>(entity);
                    if (info.hasMeshHandle) {
                        const auto& mh = m_registry.get<MeshHandle>(entity);
                        info.gpuCullingSlot = mh.gpuCullingSlot;
                        info.gpuReadyValue = mh.gpuReadyValue;
                    }
                }
            }
            swappedInfos.push_back(info);
        }

        m_chunkHoleTracker.detectHolesAfterSwap(swappedInfos);
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
                vr.debugInfo.residency = deriveChunkResidencyKind(
                    /*gpuResident=*/true,
                    vr.debugInfo.artifactCacheResident,
                    /*pendingBatch=*/false);
                visualReadyEntries.push_back(std::move(vr));
            }

            m_registry.emplace_or_replace<MeshHandle>(entity, pending.handle);
            m_registry.remove<PendingMeshHandle>(entity);
            ++swapped;
        }
    }

    if (swapped > 0)
        m_meshTopologyVersion.fetch_add(1, std::memory_order_relaxed);

    // Phase 3: batch-free old resources outside registry lock.
    {
        std::vector<BufferSlice> vbSlices, ibSlices;
        std::vector<uint32_t> cullingSlots;
        size_t vbSliceCount = 0;
        size_t ibSliceCount = 0;
        for (const auto& df : deferredFrees) {
            vbSliceCount += df.vbs.size();
            ibSliceCount += df.ibs.size();
        }
        vbSlices.reserve(vbSliceCount);
        ibSlices.reserve(ibSliceCount);
        cullingSlots.reserve(deferredFrees.size());
        for (auto& df : deferredFrees) {
            vbSlices.insert(vbSlices.end(), df.vbs.begin(), df.vbs.end());
            ibSlices.insert(ibSlices.end(), df.ibs.begin(), df.ibs.end());
            if (df.gpuCullingSlot != UINT32_MAX)
                cullingSlots.push_back(df.gpuCullingSlot);
        }
        if (!vbSlices.empty())
            vbAllocator->freeBatch(vbSlices.data(), vbSlices.size());
        if (!ibSlices.empty())
            ibAllocator->freeBatch(ibSlices.data(), ibSlices.size());
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

## include\world\chunks\core\Chunk.h

Description: No CC-DESC found. C++ struct 'ChunkCoord'.

````cpp
#pragma once

#include <glm/glm.hpp>
#include <array>
#include <chrono>
#include <cstdint>
#include <functional>
#include <vector>
#include "vulkan/BufferSuballocator.h"
#include "world/WorldTypes.h"
#include "rendering/common/Mesh.h"

// Forward declare Jolt BodyID to avoid heavy include
namespace JPH { class BodyID; }

// Edge indices for seam meshes (must match converter and TerrainFileLoader)
// Duplicated here to avoid circular include
constexpr uint8_t CHUNK_EDGE_NEG_X = 0; // West edge  (-X)
constexpr uint8_t CHUNK_EDGE_POS_X = 1; // East edge  (+X)
constexpr uint8_t CHUNK_EDGE_NEG_Z = 2; // South edge (-Z)
constexpr uint8_t CHUNK_EDGE_POS_Z = 3; // North edge (+Z)
constexpr uint8_t CHUNK_EDGE_COUNT = 4;

/**
 * Core chunk data structures for the ECS world
 * 
 * These are pure data components - no logic here
 * Meshing and terrain generation will be implemented separately
 */

/**
 * Chunk coordinate in world space
 * Single-layer terrain: all chunks at y=0, extending upward to 32m
 * Each chunk is 8m × 32m × 8m in world space (128 × 512 × 128 micro-voxels)
 */
struct ChunkCoord {
    int32_t x;
    int32_t y;
    int32_t z;
    
    ChunkCoord() : x(0), y(0), z(0) {}
    ChunkCoord(int32_t _x, int32_t _y, int32_t _z) : x(_x), y(_y), z(_z) {}
    
    bool operator==(const ChunkCoord& other) const {
        return x == other.x && y == other.y && z == other.z;
    }
    
    bool operator!=(const ChunkCoord& other) const {
        return !(*this == other);
    }
    
    glm::ivec3 toVec3() const {
        return glm::ivec3(x, y, z);
    }
};

struct IVec3Hash {
    std::size_t operator()(const glm::ivec3& value) const noexcept {
        std::size_t hx = std::hash<int32_t>{}(value.x);
        std::size_t hy = std::hash<int32_t>{}(value.y);
        std::size_t hz = std::hash<int32_t>{}(value.z);
        // Use large primes to mix components and reduce collisions
        return hx ^ (hy * 0x9e3779b1u) ^ (hz * 0x7f4a7c15u);
    }
};

/**
 * Chunk lifecycle state machine
 */
struct ChunkState {
    enum class State : uint8_t {
        Unloaded,   // Chunk entity exists but has no data
        Loading,    // Data is being generated/loaded
        Dirty,      // Data changed, needs meshing
        Meshing,    // Mesh is being built on worker thread
        Ready       // Has valid mesh, ready to render
    };
    
    State state{State::Unloaded};
};

/**
 * Axis-Aligned Bounding Box for spatial queries
 * Used for frustum culling and physics
 */
struct AABB {
    glm::vec3 min;
    glm::vec3 max;
    
    AABB() : min(0.0f), max(0.0f) {}
    AABB(const glm::vec3& _min, const glm::vec3& _max) : min(_min), max(_max) {}
    
    bool contains(const glm::vec3& point) const {
        return point.x >= min.x && point.x <= max.x &&
               point.y >= min.y && point.y <= max.y &&
               point.z >= min.z && point.z <= max.z;
    }
    
    bool intersects(const AABB& other) const {
        return (min.x <= other.max.x && max.x >= other.min.x) &&
               (min.y <= other.max.y && max.y >= other.min.y) &&
               (min.z <= other.max.z && max.z >= other.min.z);
    }
    
    glm::vec3 center() const {
        return (min + max) * 0.5f;
    }
    
    glm::vec3 size() const {
        return max - min;
    }
};

/**
 * PHASE 5A: Chunk metadata component
 * Stores additional runtime flags for optimization
 */
enum class ChunkMeshMode : uint8_t {
    MonolithicPristine = 0, // Precomputed or full-chunk runtime mesh
    MonolithicEdited = 1,   // Current edited chunk path (single mesh object)
    PagedEditable = 2,      // Future local-page runtime mesh path
    PagedConsolidating = 3, // Future background repack / merge pass
};

/**
 * Runtime mesh page bounds within a logical 128x128 chunk.
 * X/Z pages are fixed-size for fast dirty mapping; Y is adaptive so the
 * runtime mesher can avoid paying for empty vertical range.
 */
struct ChunkEditPageBounds {
    uint16_t minX{0};
    uint16_t maxX{0};
    uint16_t minY{0};
    uint16_t maxY{0};
    uint16_t minZ{0};
    uint16_t maxZ{0};

    bool isValid() const {
        return minX <= maxX && minY <= maxY && minZ <= maxZ;
    }
};

/**
 * Per-page runtime state for the future editable-chunk mesh pipeline.
 * These are CPU-side scheduling/ownership fields only; GPU resources stay in
 * MeshHandle/PendingMeshHandle until the paged renderer path is implemented.
 */
struct ChunkEditPageRuntime {
    uint16_t pageId{0};
    ChunkEditPageBounds bounds{};
    uint32_t dataGeneration{0};
    uint32_t meshGeneration{0};
    uint32_t lastSurfaceEstimate{0};
    glm::vec3 localAabbMin{1e10f};
    glm::vec3 localAabbMax{-1e10f};
    std::vector<Vertex> cpuVertices;
    std::vector<uint16_t> cpuIndices;
    bool dirtyData{false};
    bool dirtyMesh{false};
    bool uploadPending{false};
    bool resident{false};

    bool hasCpuMesh() const {
        return !cpuVertices.empty() && !cpuIndices.empty();
    }
};

/**
 * ECS component scaffold for the maximum-speed terrain-edit rewrite.
 * A chunk remains a 128x128 logical streaming/save unit, but edited chunks
 * can promote from a single monolithic mesh into many local runtime pages.
 *
 * This component is intentionally compile-safe scaffolding for the rewrite:
 * current runtime behavior stays unchanged until systems start consuming it.
 */
struct ChunkEditRuntime {
    static constexpr uint8_t PAGE_SIZE_XZ_VOXELS = 32;
    static constexpr uint8_t PAGE_HALO_VOXELS = 1;

    ChunkMeshMode targetMode{ChunkMeshMode::MonolithicPristine};
    uint32_t topologyGeneration{0};
    uint32_t dataGeneration{0};
    uint32_t meshGeneration{0};
    uint32_t lastConsolidationGeneration{0};
    bool needsPromotion{false};
    bool needsTopologyRebuild{false};
    std::vector<ChunkEditPageRuntime> pages;
    std::vector<uint16_t> dirtyPageIds;
};

struct Chunk {
    glm::ivec3 chunkPos;
    uint32_t id;
    bool isEmpty;  // True if chunk contains no solid voxels (all air)
    int32_t lodLevel; // Active LOD level (0 = highest detail)
    uint8_t effectiveDataLod{0}; // Effective source/data LOD currently rendered
    ChunkMeshMode meshMode{ChunkMeshMode::MonolithicPristine}; // Runtime mesh representation
    bool isVisible;  // User-controlled visibility toggle for debugging/VRAM management
    uint8_t voxelSeamMask{0};  // Voxel seam mask (tracks seam-edge changes for LOD boundary remesh)
    uint8_t casingSeamMask{0}; // DCCM casing seam mask (tracks which edges have casing for real-time update detection)

    Chunk() : chunkPos(0, 0, 0), id(0), isEmpty(false), lodLevel(0), effectiveDataLod(0), meshMode(ChunkMeshMode::MonolithicPristine), isVisible(true), voxelSeamMask(0), casingSeamMask(0) {}
    Chunk(const glm::ivec3& pos, uint32_t _id)
        : chunkPos(pos), id(_id), isEmpty(false), lodLevel(0), effectiveDataLod(0), meshMode(ChunkMeshMode::MonolithicPristine), isVisible(true), voxelSeamMask(0), casingSeamMask(0) {}
};

/**
 * Draw range for a single SubChunk
 * All SubChunks share the same vertex/index buffer, each has its own range
 */
struct SubChunkDrawRange {
    uint32_t firstIndex{0};   // First index in shared index buffer
    uint32_t indexCount{0};   // Number of indices to draw
    int32_t vertexOffset{0};  // Vertex offset for this SubChunk
    
    bool isValid() const { return indexCount > 0; }
};

// Maximum number of runtime SubChunks per chunk.
// Precomputed terrain file v4 still stores up to 4 main/seam SubChunks per chunk,
// but runtime edited chunks can now spatially split beyond that.
// Bumped 16->64 to handle very dense edits (e.g. heavily carved chunks producing
// >16 sub-meshes after greedy meshing). See also GPU_MAX_SUBCHUNKS in
// rendering/culling/GPUCullingSystem.h and MAX_RUNTIME_SUBMESHES in
// world/edit/TerrainEditMesher.cpp — must stay in sync.
constexpr size_t MAX_SUBCHUNKS = 64;

/**
 * Handle to GPU-side mesh data with SubChunk support
 * Supports multiple draw calls per chunk when geometry exceeds 65535 vertices
 * Backward compatible: single-SubChunk chunks work like legacy MeshHandle
 */
struct MeshHandle {
    BufferSlice vb;           // Shared vertex buffer slice for all SubChunks
    BufferSlice ib;           // Shared index buffer slice for all SubChunks
    VkBuffer sourceBuffer{VK_NULL_HANDLE}; // Underlying buffer for validation
    uint64_t gpuReadyValue{0}; // Timeline value required before drawing
    
    // GPU culling slot index (UINT32_MAX = not using GPU culling)
    uint32_t gpuCullingSlot{UINT32_MAX};
    
    // SubChunk draw ranges - each becomes a separate draw call
    std::array<SubChunkDrawRange, MAX_SUBCHUNKS> subChunks;
    uint8_t subChunkCount{0};      // Total SubChunks (main + seam) - 0 = invalid
    uint8_t mainSubChunkCount{0};  // How many are main mesh SubChunks (split due to vertex limit)
    // Remaining (subChunkCount - mainSubChunkCount) are seam SubChunks
    
    // Legacy accessors for backward compatibility (returns first SubChunk's values)
    uint32_t firstIndex() const { return subChunkCount > 0 ? subChunks[0].firstIndex : 0; }
    uint32_t indexCount() const { return subChunkCount > 0 ? subChunks[0].indexCount : 0; }
    int32_t vertexOffset() const { return subChunkCount > 0 ? subChunks[0].vertexOffset : 0; }

    bool isValid() const {
        return subChunkCount > 0 && vb.isValid() && ib.isValid();
    }

    uint32_t getTotalIndexCount() const {
        uint32_t total = 0;
        for (size_t i = 0; i < subChunkCount; ++i) {
            total += subChunks[i].indexCount;
        }
        return total;
    }

    VkDeviceSize getTotalVertexBytes() const {
        return vb.isValid() ? vb.size : 0;
    }

    VkDeviceSize getTotalIndexBytes() const {
        return ib.isValid() ? ib.size : 0;
    }

    uint64_t getTotalVramBytes() const {
        return static_cast<uint64_t>(getTotalVertexBytes() + getTotalIndexBytes());
    }

    uint32_t getTotalVertexCount() const {
        return static_cast<uint32_t>(getTotalVertexBytes() / sizeof(Vertex));
    }

    void collectBufferSlices(std::vector<BufferSlice>& outVBs,
                             std::vector<BufferSlice>& outIBs) const {
        if (vb.isValid()) {
            outVBs.push_back(vb);
        }

        if (ib.isValid()) {
            outIBs.push_back(ib);
        }
    }

    void clear() {
        vb = BufferSlice{};
        ib = BufferSlice{};
        sourceBuffer = VK_NULL_HANDLE;
        gpuReadyValue = 0;
        for (auto& sc : subChunks) {
            sc = SubChunkDrawRange{};
        }
        subChunkCount = 0;
        mainSubChunkCount = 0;
        gpuCullingSlot = UINT32_MAX;
    }

    // Helper to set single SubChunk (legacy compatibility)
    void setSingleSubChunk(uint32_t _firstIndex, uint32_t _indexCount, int32_t _vertexOffset) {
        subChunks[0].firstIndex = _firstIndex;
        subChunks[0].indexCount = _indexCount;
        subChunks[0].vertexOffset = _vertexOffset;
        subChunkCount = 1;
        mainSubChunkCount = 1;
    }
};

/**
 * Handles to GPU-side seam mesh data for LOD boundaries
 * Each edge has its own mesh that's drawn only when the neighbor has different LOD
 * Uses MeshHandle with SubChunk support
 */
struct SeamMeshHandles {
    std::array<MeshHandle, CHUNK_EDGE_COUNT> edges;
    
    bool hasValidSeam(uint8_t edge) const {
        return edge < CHUNK_EDGE_COUNT && edges[edge].isValid();
    }
    
    void clear() {
        for (auto& edge : edges) {
            edge.clear();
        }
    }
};

/**
 * Staged mesh waiting for atomic LOD batch swap.
 * During LOD transitions, the new mesh is uploaded into PendingMeshHandle
 * while the old MeshHandle keeps rendering.  When ALL chunks in the LOD
 * transition batch are ready, processLODSwaps() atomically moves
 * PendingMeshHandle → MeshHandle for the entire batch in one frame.
 * This eliminates visual holes at LOD boundaries.
 */
struct PendingMeshHandle {
    MeshHandle handle;       // The new mesh (already uploaded to GPU)
    uint32_t batchId{0};     // Which LOD transition batch this belongs to
    std::chrono::steady_clock::time_point uploadEnqueueTime{}; // original upload enqueue for visual timing
    ChunkDebugAttribution debugInfo{};
};

// Forward declaration for Jolt BodyID
namespace JPH { class BodyID; }

/**
 * Physics collision body for chunk terrain
 * Created when chunk mesh is uploaded, removed when chunk is destroyed
 * Only created for chunks near the player (within collision distance)
 */
struct ChunkCollider {
    uint32_t bodyIdIndex{0xFFFFFFFF};  // Jolt BodyID index (0xFFFFFFFF = invalid)
    uint32_t bodyIdSequence{0};         // Jolt BodyID sequence number
    
    bool isValid() const { return bodyIdIndex != 0xFFFFFFFF; }
    void invalidate() { bodyIdIndex = 0xFFFFFFFF; bodyIdSequence = 0; }
    
    // Helper to create from JPH::BodyID
    void setFromBodyId(uint32_t index, uint8_t sequence) {
        bodyIdIndex = index;
        bodyIdSequence = sequence;
    }
};

````

## include\world\chunks\core\ChunkJobs.h

Description: No CC-DESC found. C++ class 'World'.

````cpp
#pragma once

#include "core/Jobs.h"
#include "rendering/common/Mesh.h"
#include "world/chunks/core/Chunk.h"
#include <atomic>
#include <memory>
#include <mutex>
#include <shared_mutex>
#include <unordered_map>
#include <vector>
#include <stack>
#include <entt/entt.hpp>

// Forward declarations
class World;

/**
 * Version state for chunk pipeline cancellation
 */
struct ChunkVersionState {
    std::atomic<uint32_t> version{0};
    std::atomic<bool> inFlight{false};
    std::atomic<bool> pending{false};
    // Edit remesh path can temporarily allow a newer FAST remesh to overlap an
    // older edit remesh for the same chunk so brush bursts do not stall behind
    // one long-running superseded job.
    std::atomic<uint32_t> editRemeshInFlightCount{0};
    std::atomic<uint32_t> editRemeshLatestVersion{0};
};

/**
 * Mesh data container for chunk meshes
 * Uses uint16 indices (supports up to 65k vertices per chunk - sufficient for greedy meshing)
 * OPTIMIZATION: Pre-allocates typical sizes to reduce reallocations
 */
struct MeshData {
    entt::entity entity{entt::null};
    std::vector<Vertex> vertices;
    std::vector<uint16_t> indices;

    MeshData() {
        // OPTIMIZATION: Pre-allocate typical chunk mesh sizes
        // Average chunk: ~800-1200 vertices, ~1200-1800 indices
        // Reserve slightly above average to minimize reallocations
        vertices.reserve(1000);
        indices.reserve(1500);
    }
    
    explicit MeshData(entt::entity e) : entity(e) {
        vertices.reserve(1000);
        indices.reserve(1500);
    }
    
    bool isEmpty() const { return vertices.empty() || indices.empty(); }
};

/**
 * Payload passed through the chunk generation pipeline
 * Supports multiple SubChunks per chunk (v4 format) to stay under 65535 vertex limit
 */
struct ChunkPipelinePayload {
    World* world{nullptr};
    entt::entity entity{entt::null};
    ChunkCoord coord{};
    AABB bounds{};
    std::shared_ptr<ChunkVersionState> versionState;
    uint32_t version{0};
    
    // SubChunks: each stays under 65535 vertices for 16-bit indices
    // Most chunks have 1 SubChunk, complex terrain may need 2-4
    std::vector<MeshData> subChunks;
    uint8_t mainSubChunkCount{0};  // How many subChunks are main mesh (rest are seams)
    
    std::atomic<bool> cancelled{false};
    bool isEmpty{false};  // OPTIMIZATION: Flag for empty chunks (skip meshing)
    int distanceFromPlayer{0}; // Distance from player (for priority scheduling)
    bool fromTerrainEdit{false};
    std::vector<JobHandle> jobHandles;  // Keep jobs alive through pipeline
    int lodLevel{0};       // Active LOD level for this chunk
    
    // Precomputed tight AABB (computed on worker thread to avoid main-thread stall)
    glm::vec3 tightAABBMin{1e10f};
    glm::vec3 tightAABBMax{-1e10f};
    bool hasTightAABB{false};
    
    // LOD transition batch support
    bool isRemesh{false};      // true = LOD remesh (stage to PendingMeshHandle)
    uint32_t batchId{0};       // LOD transition batch ID (0 = none)
    bool affectsShadowGeometry{true};
    ChunkDebugAttribution debugInfo{};
    
    // Center chunk captured at enqueue time — used by LoadPrecomputedMeshJob
    // to compute a stable seam mask even if the player moves between
    // enqueue and job execution on the worker thread.
    glm::ivec3 centerAtEnqueue{0, 0, 0};
    
    /**
     * Reset payload for reuse from pool (avoids re-allocation).
     * Clears all fields to default state while preserving vector capacity.
     */
    void reset() {
        world = nullptr;
        entity = entt::null;
        coord = {};
        bounds = {};
        versionState.reset();
        version = 0;
        subChunks.clear();  // keeps capacity
        mainSubChunkCount = 0;
        cancelled.store(false, std::memory_order_relaxed);
        isEmpty = false;
        distanceFromPlayer = 0;
        fromTerrainEdit = false;
        jobHandles.clear();  // keeps capacity
        lodLevel = 0;
        tightAABBMin = glm::vec3(1e10f);
        tightAABBMax = glm::vec3(-1e10f);
        hasTightAABB = false;
        isRemesh = false;
        batchId = 0;
        affectsShadowGeometry = true;
        debugInfo = {};
        centerAtEnqueue = glm::ivec3(0, 0, 0);
    }
};

/**
 * Thread-safe pool for ChunkPipelinePayload objects.
 * Eliminates per-chunk heap allocations during initial world loading
 * (25,600 chunks × new/delete avoided).
 */
class PayloadPool {
public:
    ChunkPipelinePayload* acquire() {
        std::lock_guard lock(m_mutex);
        if (!m_pool.empty()) {
            auto* p = m_pool.top();
            m_pool.pop();
            p->reset();
            return p;
        }
        return new ChunkPipelinePayload();
    }
    
    void release(ChunkPipelinePayload* p) {
        if (!p) return;
        std::lock_guard lock(m_mutex);
        m_pool.push(p);
    }
    
    ~PayloadPool() {
        while (!m_pool.empty()) {
            delete m_pool.top();
            m_pool.pop();
        }
    }
    
private:
    std::stack<ChunkPipelinePayload*> m_pool;
    std::mutex m_mutex;
};

/**
 * EntityHash for chunk version state map
 */
struct EntityHash {
    std::size_t operator()(entt::entity value) const noexcept;
};

// Upload budget is controlled by ChunkUploadSystem::MAX_UPLOADS_PER_FRAME

/**
 * Chunk version state management (C-nits 8: now takes World reference)
 */
std::shared_ptr<ChunkVersionState> ensureChunkVersionState(class World* world, entt::entity entity);
void removeChunkVersionState(class World* world, entt::entity entity);
bool payloadStillCurrent(const ChunkPipelinePayload* payload);

/**
 * Job functions for precomputed mesh pipeline
 */
void LoadPrecomputedMeshJob(JobCtx& ctx, void* user);
void UploadChunkJob(JobCtx& ctx, void* user);
void FinalizeChunkJob(JobCtx& ctx, void* user);

/**
 * Alternative load job for chunks with terrain edits.
 * Runs the edit mesher (Voxel or DCCM) instead of loading from terrain.bin.
 * Feeds into the same Upload → Finalize pipeline.
 */
void LoadEditMeshJob(JobCtx& ctx, void* user);

````

## include\world\edit\TextureOverlayStore.h

Description: No CC-DESC found. C++ struct 'VoxelTextureData'.

````cpp
#pragma once

// GPT-DESC: Declares sparse texture material storage and deferred/live surface paint stamps.

#include <array>
#include <atomic>
#include <cstdint>
#include <memory>
#include <shared_mutex>
#include <unordered_map>
#include <vector>

#include <glm/glm.hpp>

#include "world/WorldTypes.h"
#include "world/config/WorldConfig.h"

namespace TextureOverlay {

// ---------------------------------------------------------------------------
// Texture material types and per-voxel storage
// ---------------------------------------------------------------------------

enum class TextureType : uint8_t {
    Grass = 0,
    Mud = 1,
    Dirt = 2,
    Sand = 3,
    COUNT = 4
};

// Transition edge style used for material boundaries.
// Stored in VoxelTextureData::edgeMask (2 bits).
enum class TransitionEdgeStyle : uint8_t {
    None = 0,
    Leafy = 1,
    Sloppy = 2,
    Grainy = 3
};

enum class SurfaceBrushShape : uint8_t {
    Disc = 0,
    Rect = 1
};

inline const char* textureTypeName(TextureType t) {
    switch (t) {
        case TextureType::Grass: return "grass";
        case TextureType::Mud:   return "mud";
        case TextureType::Dirt:  return "dirt";
        case TextureType::Sand:  return "sand";
        default: return "?";
    }
}

// Per-voxel texture assignment.  Packed into 1 byte:
//   bit 0..1  textureType (4 types)
//   bit 2..4  variant (8 variants — we only need 5 in practice)
//   bit 5..6  edgeMask (2 bits, reserved for transition flags)
//   bit 7     valid flag (1 = painted, 0 = empty)
//
// The valid flag is essential: without it, "Grass + variant 0" would collide
// with "empty" (both packed=0).  It's the only way to clear a cell back to
// its un-painted state via the same packed byte.
struct VoxelTextureData {
    uint8_t packed{0};
    uint8_t face{3}; // 0..5, same face IDs as the terrain vertex format.

    constexpr VoxelTextureData() = default;
    VoxelTextureData(TextureType type,
                     uint8_t variant,
                     uint8_t edgeMask = 0,
                     uint8_t faceId = 3) {
        packed = static_cast<uint8_t>(
            0x80u |  // valid flag
            (static_cast<uint32_t>(type) & 0x3u) |
            ((variant & 0x7u) << 2) |
            ((edgeMask & 0x3u) << 5));
        face = static_cast<uint8_t>(faceId % 6u);
    }

    TextureType getType() const {
        return static_cast<TextureType>(packed & 0x3u);
    }
    uint8_t getVariant() const {
        return static_cast<uint8_t>((packed >> 2) & 0x7u);
    }
    uint8_t getEdgeMask() const {
        return static_cast<uint8_t>((packed >> 5) & 0x3u);
    }
    uint8_t getFace() const { return face; }
    bool isEmpty() const { return (packed & 0x80u) == 0; }

    VoxelTextureData withEdgeMask(uint8_t edgeMask) const {
        if (isEmpty()) return *this;
        VoxelTextureData out = *this;
        out.packed = static_cast<uint8_t>(
            (out.packed & ~0x60u) | ((edgeMask & 0x3u) << 5));
        return out;
    }
};

// ---------------------------------------------------------------------------
// Per-LOD resolution config
//
// Each LOD level has its own pixels-per-voxel-face value. Selectable from
// 2,4,8,...,1024. The terrain shader uses this to quantize procedural pixel
// detail on a face emitted for that LOD.
// ---------------------------------------------------------------------------

struct LODTextureConfig {
    // Texels per voxel face edge.  Must be a power of two >= 2 and <= 1024.
    // Default cascade: LOD 0 = 16 px, LOD 1 = 8 px, LOD 2 = 4 px, LOD 3 = 2 px
    uint16_t pixelsPerVoxel{4};
    // If false, no painting is authored at this LOD.
    bool enabled{true};
};

// ---------------------------------------------------------------------------
// Sparse storage primitives
// ---------------------------------------------------------------------------

// 8x8x8 brick of texture cells (matches the edit-overlay brick size).
struct TextureBrick {
    static constexpr int SIZE = 8;
    static constexpr int CELLS = SIZE * SIZE * SIZE;

    std::array<VoxelTextureData, CELLS> cells{};
    uint32_t activeCount{0};

    static int toIndex(int lx, int ly, int lz) {
        return lx + ly * SIZE + lz * SIZE * SIZE;
    }
};

// ---------------------------------------------------------------------------
// Sparse, per-LOD voxel-face material store
//
// LOD storage:
//   - One independent BrickMap per LOD level (4 levels).
//   - Coordinates within each LOD's BrickMap are voxel coords *at that LOD*.
//     i.e. an LOD-1 voxel coordinate = lod0Coord >> 1, LOD-2 = lod0Coord >> 2.
//
// Paint operations take the LOD level explicitly so the brush can target the
// LOD that the hit chunk is currently rendered at, and the radius scales
// naturally (the stored cell footprint at LOD-N is 1/(2^N) of the LOD-0
// footprint for the same world distance).
// ---------------------------------------------------------------------------

class TextureOverlayStore {
public:
    struct SurfaceFaceStamp {
        glm::ivec3 lodCoord{0};
        uint8_t face{3};
    };

    // Deferred surface brush command. This is the interactive O(1) authoring path:
    // one huge brush stroke stores one stamp and affected chunks are rebaked later.
    // The mesher resolves stamps through getSurfaceTexture() while emitting real
    // terrain faces, so the click path never expands radius^2 faces.
    struct SurfacePaintStamp {
        glm::vec3 centerVoxelLod0{0.0f};
        glm::ivec3 bboxMinLod0{0};
        glm::ivec3 bboxMaxLod0{0};
        int radiusVoxelsLod0{1};
        SurfaceBrushShape shape{SurfaceBrushShape::Disc};
        TextureType type{TextureType::Grass};
        uint8_t variant{0};
        uint8_t sourceFace{255};
        uint32_t order{0};
    };

    struct GPUCell {
        int32_t x{0};
        int32_t y{0};
        int32_t z{0};
        uint32_t lod{0};
        uint32_t packed{0};
        uint32_t face{3};
    };

    static constexpr int LOD_COUNT = MAX_LOD_LEVELS;
    static constexpr int RES_OPTION_COUNT = 10;
    static const uint16_t RES_OPTIONS[RES_OPTION_COUNT];   // {2,4,8,...,1024}
    static const char*   RES_OPTION_LABELS[RES_OPTION_COUNT];

    TextureOverlayStore();

    // -------- Per-LOD configuration --------
    void setLODConfig(int lod, const LODTextureConfig& cfg);
    LODTextureConfig getLODConfig(int lod) const;

    // -------- Single-cell access (LOD-aware) --------
    // 'lodCoord' is in voxel coords at the requested LOD level.
    void setTexture(const glm::ivec3& lodCoord, int lod, VoxelTextureData data);
    VoxelTextureData getTexture(const glm::ivec3& lodCoord, int lod) const;
    VoxelTextureData getSurfaceTexture(const glm::ivec3& lodCoord,
                                       int lod,
                                       uint8_t face) const;
    bool hasSurfaceTexturesInBox(const glm::ivec3& minLodCoord,
                                 const glm::ivec3& maxExclusiveLodCoord,
                                 int lod) const;
    void clearTexture(const glm::ivec3& lodCoord, int lod);

    // -------- Brush paint operations --------
    // 'centerLod0' is the world-voxel center *at LOD-0*; the paint operation
    // automatically converts to the target LOD's resolution.
    // 'radiusVoxelsLod0' is the brush radius in LOD-0 voxels.
    // Returns the number of LOD cells written.
    int paintSphere(const glm::ivec3& centerLod0,
                    int radiusVoxelsLod0,
                    int lod,
                    TextureType type,
                    uint8_t variant);
    int paintBox(const glm::ivec3& minLod0,
                 const glm::ivec3& maxLod0,
                 int lod,
                 TextureType type,
                 uint8_t variant);

    // Fast surface stamps for texture painting. They write only the face plane
    // under the cursor instead of filling a 3D volume.
    int paintFaceDisc(const glm::ivec3& centerLod0,
                      int radiusVoxelsLod0,
                      int normalAxis,
                      int lod,
                      TextureType type,
                      uint8_t variant,
                      uint8_t face,
                      int maxCells = 300000);
    int paintFaceRect(const glm::ivec3& centerLod0,
                      int radiusVoxelsLod0,
                      int normalAxis,
                      int lod,
                      TextureType type,
                      uint8_t variant,
                      uint8_t face,
                      int maxCells = 300000);
    int paintSurfaceFaces(const std::vector<SurfaceFaceStamp>& faces,
                          int lod,
                          TextureType type,
                          uint8_t variant);

    // Theoretical-minimum interactive brush path. Stores one deferred stamp
    // instead of expanding the brush into per-face cells. Returned value is
    // the monotonically increasing stamp order, or 0 if the stamp was rejected.
    uint32_t appendSurfacePaintStamp(const glm::vec3& centerWorld,
                                     int radiusVoxelsLod0,
                                     SurfaceBrushShape shape,
                                     TextureType type,
                                     uint8_t variant,
                                     uint8_t sourceFace = 255);
    size_t getSurfacePaintStampCount() const;

    // Copy newest deferred paint stamps for the bounded live GPU overlay.
    // Returned stamps stay chronological; the shader scans newest-first.
    size_t exportLiveSurfacePaintStamps(std::vector<SurfacePaintStamp>& out,
                                        size_t maxStamps) const;

    int clearSphere(const glm::ivec3& centerLod0,
                    int radiusVoxelsLod0,
                    int lod);
    int clearBox(const glm::ivec3& minLod0,
                 const glm::ivec3& maxLod0,
                 int lod);

    // Cascade a paint from sourceLod to all coarser LODs (sourceLod+1..MAX).
    // Useful when the user paints at LOD 0 and wants the texture to show up
    // when the chunk later transitions to a coarser LOD.  Returns total cells
    // written across all cascaded LODs.
    int cascadeToCoarserLODs(const glm::ivec3& centerLod0,
                             int radiusVoxelsLod0,
                             int sourceLod,
                             TextureType type,
                             uint8_t variant);
    int cascadeFaceToCoarserLODs(const glm::ivec3& centerLod0,
                                 int radiusVoxelsLod0,
                                 int normalAxis,
                                 int sourceLod,
                                 SurfaceBrushShape shape,
                                 TextureType type,
                                 uint8_t variant,
                                 uint8_t face,
                                 int maxCellsPerLOD = 300000);

    // -------- Stats / state --------
    struct Stats {
        size_t bricksByLOD[LOD_COUNT]{};
        size_t cellsByLOD[LOD_COUNT]{};
        size_t totalBricks{0};
        size_t totalCells{0};
        size_t surfaceStampCount{0};
        size_t generation{0};
    };
    Stats getStats() const;
    size_t exportGPUCells(std::vector<GPUCell>& out, size_t maxCells) const;
    size_t exportGPUCellsForLOD(int lod, std::vector<GPUCell>& out, size_t maxCells) const;
    size_t consumeDirtyGPUCells(std::vector<GPUCell>& out,
                                size_t maxCells,
                                bool& requiresFullUpload);
    bool isEmpty() const;
    void clear();
    void clearLOD(int lod);

    // Monotonically incremented every write — used by mesher caches.
    size_t getGeneration() const {
        return m_generation.load(std::memory_order_acquire);
    }

    // -------- Persistence (binary) --------
    bool saveToFile(const char* path) const;
    bool loadFromFile(const char* path);

    // -------- LOD/coordinate helpers --------
    static glm::ivec3 lod0ToLOD(const glm::ivec3& lod0Coord, int lod) {
        const int shift = lod;
        // Arithmetic shift handles negative coords correctly for power-of-two
        // downsampling — equivalent to floor(coord / 2^lod) for negatives too.
        return glm::ivec3(lod0Coord.x >> shift,
                          lod0Coord.y >> shift,
                          lod0Coord.z >> shift);
    }
    static glm::ivec3 lodToLOD0(const glm::ivec3& lodCoord, int lod) {
        return glm::ivec3(lodCoord.x << lod,
                          lodCoord.y << lod,
                          lodCoord.z << lod);
    }

private:
    struct BrickKey {
        int32_t bx, by, bz;
        bool operator==(const BrickKey& o) const noexcept {
            return bx == o.bx && by == o.by && bz == o.bz;
        }
    };
    struct BrickKeyHash {
        size_t operator()(const BrickKey& k) const noexcept {
            // splitmix-style mix; cheap and good distribution
            uint64_t h = static_cast<uint64_t>(static_cast<uint32_t>(k.bx)) * 73856093ull;
            h ^= static_cast<uint64_t>(static_cast<uint32_t>(k.by)) * 19349663ull;
            h ^= static_cast<uint64_t>(static_cast<uint32_t>(k.bz)) * 83492791ull;
            h ^= h >> 33;
            return static_cast<size_t>(h);
        }
    };
    using BrickMap = std::unordered_map<BrickKey,
                                        std::unique_ptr<TextureBrick>,
                                        BrickKeyHash>;

    static BrickKey voxelToBrick(const glm::ivec3& v);
    static glm::ivec3 voxelLocalInBrick(const glm::ivec3& v);
    static glm::ivec3 encodeFaceCoord(const glm::ivec3& voxelCoord, uint8_t face);
    static glm::ivec3 decodeFaceCoord(const glm::ivec3& storageCoord, uint8_t& face);

    TextureBrick* getOrCreateBrickLocked(BrickMap& map, int lod, BrickKey key);
    TextureBrick* getBrickLocked(BrickMap& map, BrickKey key);
    const TextureBrick* getBrickLocked(const BrickMap& map, BrickKey key) const;

    void writeCellLocked(BrickMap& map, int lod, BrickKey key,
                         const glm::ivec3& local,
                         VoxelTextureData data);

    static uint8_t classifyTransitionEdge(TextureType a,
                                          TextureType b,
                                          const glm::ivec3& lodCoord);
    static uint8_t mergeEdgeStyle(uint8_t lhs, uint8_t rhs);
    void recordDirtyGPUCellLocked(int lod,
                                  const glm::ivec3& lodCoord,
                                  VoxelTextureData data);
    void requestFullGPUUploadLocked();
    void refreshTransitionEdgesAroundCellLocked(BrickMap& map,
                                                int lod,
                                                const glm::ivec3& lodCoord);
    void refreshTransitionEdgesAroundFaceLocked(BrickMap& map,
                                                int lod,
                                                const glm::ivec3& lodCoord,
                                                uint8_t face);

    struct SurfacePaintChunkHash {
        size_t operator()(const glm::ivec3& v) const noexcept {
            uint64_t h = static_cast<uint64_t>(static_cast<uint32_t>(v.x)) * 73856093ull;
            h ^= static_cast<uint64_t>(static_cast<uint32_t>(v.y)) * 19349663ull;
            h ^= static_cast<uint64_t>(static_cast<uint32_t>(v.z)) * 83492791ull;
            h ^= h >> 33;
            return static_cast<size_t>(h);
        }
    };
    void indexSurfacePaintStampLocked(uint32_t stampIndex);
    VoxelTextureData sampleSurfacePaintStampsLocked(const glm::ivec3& lodCoord,
                                                    int lod,
                                                    uint8_t face) const;
    static bool surfaceStampTouchesBox(const SurfacePaintStamp& stamp,
                                       const glm::ivec3& minLod0,
                                       const glm::ivec3& maxLod0);

    bool isLodValid(int lod) const { return lod >= 0 && lod < LOD_COUNT; }

    // One BrickMap per LOD.  Indices 0..MAX_LOD_LEVEL.
    std::array<BrickMap, LOD_COUNT> m_lodMaps;
    std::array<size_t, LOD_COUNT> m_brickCountsByLOD{};
    std::array<size_t, LOD_COUNT> m_cellCountsByLOD{};

    std::array<LODTextureConfig, LOD_COUNT> m_lodConfigs;

    mutable std::shared_mutex m_mutex;
    std::atomic<size_t> m_generation{0};

    // Deferred brush stamps indexed by affected native chunk. The stamp vector
    // is append-only between clears, so chunk index entries can store stable
    // integer indices. Newest stamp wins during mesher material sampling.
    std::vector<SurfacePaintStamp> m_surfacePaintStamps;
    std::unordered_map<glm::ivec3, std::vector<uint32_t>, SurfacePaintChunkHash>
        m_surfacePaintStampChunkIndex;
    uint32_t m_nextSurfacePaintStampOrder{1};

    // Dirty LOD0 face cells consumed by the terrain material overlay SSBO.
    // Full-upload mode is used for clears/config changes and delta overflow.
    std::vector<GPUCell> m_dirtyGPUCells;
    bool m_dirtyGPUFullUpload{false};
};

} // namespace TextureOverlay

````

## src\world\edit\TextureOverlayStore.cpp

Description: No CC-DESC found. C++ struct 'TextureOverlayIvec3Hash'.

````cpp
#include "world/edit/TextureOverlayStore.h"

#include <algorithm>
#include <cstring>
#include <fstream>
#include <cmath>

namespace TextureOverlay {

namespace {
constexpr glm::ivec3 kNeighborDirs[6] = {
    glm::ivec3( 1, 0, 0), glm::ivec3(-1, 0, 0),
    glm::ivec3( 0, 1, 0), glm::ivec3( 0,-1, 0),
    glm::ivec3( 0, 0, 1), glm::ivec3( 0, 0,-1)
};
uint32_t hashPaintCoord(const glm::ivec3& coord, uint8_t face, uint32_t salt) {
    uint32_t h = static_cast<uint32_t>(coord.x) * 0x9E3779B9u;
    h ^= static_cast<uint32_t>(coord.y) * 0x85EBCA6Bu;
    h ^= static_cast<uint32_t>(coord.z) * 0xC2B2AE35u;
    h ^= static_cast<uint32_t>(face) * 0x27D4EB2Du;
    h ^= salt * 0x165667B1u;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    return h;
}

uint8_t variedVariant(const glm::ivec3& coord,
                      uint8_t face,
                      TextureType type,
                      uint8_t seed) {
    const uint32_t h = hashPaintCoord(coord, face, static_cast<uint32_t>(type));
    return static_cast<uint8_t>((seed + (h & 0x7u)) & 0x7u);
}

constexpr uint32_t GPU_OVERLAY_CHUNK_SENTINEL_FACE = 7u;
constexpr uint32_t GPU_OVERLAY_CHUNK_SENTINEL_MATERIAL = 0x40000000u;

glm::ivec3 materialOverlayChunkSentinelCoord(const glm::ivec3& lod0Coord) {
    return WorldConfig::microVoxelToChunk(lod0Coord);
}

TextureOverlayStore::GPUCell makeChunkSentinelGPUCell(const glm::ivec3& lod0Coord) {
    const glm::ivec3 chunk = materialOverlayChunkSentinelCoord(lod0Coord);

    TextureOverlayStore::GPUCell sentinel{};
    sentinel.x = chunk.x;
    sentinel.y = chunk.y;
    sentinel.z = chunk.z;
    sentinel.lod = 0u;
    sentinel.packed = GPU_OVERLAY_CHUNK_SENTINEL_MATERIAL;
    sentinel.face = GPU_OVERLAY_CHUNK_SENTINEL_FACE;
    return sentinel;
}

struct TextureOverlayIvec3Hash {
    size_t operator()(const glm::ivec3& v) const noexcept {
        uint64_t h = static_cast<uint64_t>(static_cast<uint32_t>(v.x)) * 73856093ull;
        h ^= static_cast<uint64_t>(static_cast<uint32_t>(v.y)) * 19349663ull;
        h ^= static_cast<uint64_t>(static_cast<uint32_t>(v.z)) * 83492791ull;
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdull;
        h ^= h >> 33;
        return static_cast<size_t>(h);
    }
};



void facePlaneAxes(uint8_t face, int& uAxis, int& vAxis) {
    face %= 6u;
    if (face <= 1u) {
        uAxis = 1; // Y
        vAxis = 2; // Z
    } else if (face <= 3u) {
        uAxis = 0; // X
        vAxis = 2; // Z
    } else {
        uAxis = 0; // X
        vAxis = 1; // Y
    }
}

using IVec3Hash = TextureOverlayIvec3Hash;

} // namespace

// ---------------------------------------------------------------------------
// Static resolution options (advertised to the UI)
// ---------------------------------------------------------------------------

const uint16_t TextureOverlayStore::RES_OPTIONS[RES_OPTION_COUNT] = {
    2, 4, 8, 16, 32, 64, 128, 256, 512, 1024
};
const char* TextureOverlayStore::RES_OPTION_LABELS[RES_OPTION_COUNT] = {
    "2x2", "4x4", "8x8", "16x16", "32x32", "64x64",
    "128x128", "256x256", "512x512", "1024x1024"
};

// ---------------------------------------------------------------------------
// Construction / config
// ---------------------------------------------------------------------------

TextureOverlayStore::TextureOverlayStore() {
    // Default cascade: finer voxels get more pixels.  At LOD 0 a 0.25m voxel
    // gets 16x16 px (= 64 px/m).  Each coarser LOD halves the pixel density
    // so the perceived texel size on screen stays roughly constant.
    m_lodConfigs[0] = LODTextureConfig{16, true};
    if (LOD_COUNT > 1) m_lodConfigs[1] = LODTextureConfig{8, true};
    if (LOD_COUNT > 2) m_lodConfigs[2] = LODTextureConfig{4, true};
    if (LOD_COUNT > 3) m_lodConfigs[3] = LODTextureConfig{2, true};
    if (LOD_COUNT > 4) m_lodConfigs[4] = LODTextureConfig{2, true};
}

void TextureOverlayStore::setLODConfig(int lod, const LODTextureConfig& cfg) {
    if (!isLodValid(lod)) return;
    std::unique_lock lock(m_mutex);
    LODTextureConfig clamped = cfg;
    // Clamp pixelsPerVoxel to a power of two in [2, 1024].
    uint16_t v = clamped.pixelsPerVoxel;
    if (v < 2) v = 2;
    if (v > 1024) v = 1024;
    // Round down to nearest power of two.
    uint16_t pow2 = 2;
    while ((pow2 << 1) <= v) pow2 <<= 1;
    clamped.pixelsPerVoxel = pow2;
    m_lodConfigs[lod] = clamped;
    if (lod == 0) {
        requestFullGPUUploadLocked();
    }
    m_generation.fetch_add(1, std::memory_order_release);
}

LODTextureConfig TextureOverlayStore::getLODConfig(int lod) const {
    if (!isLodValid(lod)) return {};
    std::shared_lock lock(m_mutex);
    return m_lodConfigs[lod];
}

// ---------------------------------------------------------------------------
// Coordinate helpers
// ---------------------------------------------------------------------------

TextureOverlayStore::BrickKey
TextureOverlayStore::voxelToBrick(const glm::ivec3& v) {
    // Arithmetic shift (>>3) gives floor-divide-by-8 for negative coords too.
    return BrickKey{ v.x >> 3, v.y >> 3, v.z >> 3 };
}

glm::ivec3 TextureOverlayStore::voxelLocalInBrick(const glm::ivec3& v) {
    // & 0x7 wraps correctly for negative coords (two's complement).
    return glm::ivec3(v.x & 0x7, v.y & 0x7, v.z & 0x7);
}

glm::ivec3 TextureOverlayStore::encodeFaceCoord(const glm::ivec3& voxelCoord,
                                                uint8_t face) {
    return glm::ivec3(voxelCoord.x, voxelCoord.y,
                      voxelCoord.z * 6 + static_cast<int>(face % 6u));
}

glm::ivec3 TextureOverlayStore::decodeFaceCoord(const glm::ivec3& storageCoord,
                                                uint8_t& face) {
    int z = storageCoord.z / 6;
    int rem = storageCoord.z % 6;
    if (rem < 0) {
        rem += 6;
        --z;
    }
    face = static_cast<uint8_t>(rem);
    return glm::ivec3(storageCoord.x, storageCoord.y, z);
}

// ---------------------------------------------------------------------------
// Brick access (caller holds appropriate lock)
// ---------------------------------------------------------------------------

TextureBrick*
TextureOverlayStore::getOrCreateBrickLocked(BrickMap& map, int lod, BrickKey key) {
    auto it = map.find(key);
    if (it == map.end()) {
        auto [newIt, _] = map.emplace(key, std::make_unique<TextureBrick>());
        if (isLodValid(lod)) {
            ++m_brickCountsByLOD[lod];
        }
        return newIt->second.get();
    }
    return it->second.get();
}

const TextureBrick*
TextureOverlayStore::getBrickLocked(const BrickMap& map, BrickKey key) const {
    auto it = map.find(key);
    return (it != map.end()) ? it->second.get() : nullptr;
}

TextureBrick*
TextureOverlayStore::getBrickLocked(BrickMap& map, BrickKey key) {
    auto it = map.find(key);
    return (it != map.end()) ? it->second.get() : nullptr;
}

void TextureOverlayStore::writeCellLocked(BrickMap& map,
                                          int lod,
                                          BrickKey key,
                                          const glm::ivec3& local,
                                          VoxelTextureData data) {
    TextureBrick* brick = getBrickLocked(map, key);
    if (!brick) {
        if (data.isEmpty()) {
            return;
        }
        brick = getOrCreateBrickLocked(map, lod, key);
    }
    int idx = TextureBrick::toIndex(local.x, local.y, local.z);
    VoxelTextureData& slot = brick->cells[idx];
    const bool wasActive = !slot.isEmpty();
    const bool willBeActive = !data.isEmpty();
    slot = data;
    if (wasActive && !willBeActive) {
        --brick->activeCount;
        if (isLodValid(lod) && m_cellCountsByLOD[lod] > 0u) {
            --m_cellCountsByLOD[lod];
        }
    } else if (!wasActive && willBeActive) {
        ++brick->activeCount;
        if (isLodValid(lod)) {
            ++m_cellCountsByLOD[lod];
        }
    }
    // Note: empty bricks intentionally retained after an active cell is erased.
    // Paint operations are bursty, and the next stroke usually re-fills the
    // same brick. Missing bricks are not created for empty/no-op writes.
}

uint8_t TextureOverlayStore::classifyTransitionEdge(TextureType a,
                                                    TextureType b,
                                                    const glm::ivec3& lodCoord) {
    if (a == b) {
        return static_cast<uint8_t>(TransitionEdgeStyle::None);
    }

    const bool hasGrass = (a == TextureType::Grass || b == TextureType::Grass);
    const bool hasMud = (a == TextureType::Mud || b == TextureType::Mud);
    const bool hasDirt = (a == TextureType::Dirt || b == TextureType::Dirt);
    const bool hasSand = (a == TextureType::Sand || b == TextureType::Sand);

    // Cheap deterministic coordinate hash for transition variation.
    const uint32_t hx = static_cast<uint32_t>(lodCoord.x) * 0x9E3779B9u;
    const uint32_t hy = static_cast<uint32_t>(lodCoord.y) * 0x85EBCA6Bu;
    const uint32_t hz = static_cast<uint32_t>(lodCoord.z) * 0xC2B2AE35u;
    uint32_t h = hx ^ hy ^ hz;
    h ^= h >> 16;

    if (hasGrass && hasMud) {
        return static_cast<uint8_t>((h & 1u) ? TransitionEdgeStyle::Leafy
                                             : TransitionEdgeStyle::Sloppy);
    }
    if (hasGrass) {
        return static_cast<uint8_t>(TransitionEdgeStyle::Leafy);
    }
    if (hasMud) {
        if (hasSand) {
            return static_cast<uint8_t>((h & 1u) ? TransitionEdgeStyle::Sloppy
                                                 : TransitionEdgeStyle::Grainy);
        }
        if (hasDirt) {
            return static_cast<uint8_t>((h & 3u) == 0u ? TransitionEdgeStyle::Grainy
                                                        : TransitionEdgeStyle::Sloppy);
        }
        return static_cast<uint8_t>(TransitionEdgeStyle::Sloppy);
    }

    return static_cast<uint8_t>(TransitionEdgeStyle::Grainy);
}

uint8_t TextureOverlayStore::mergeEdgeStyle(uint8_t lhs, uint8_t rhs) {
    auto priority = [](uint8_t style) -> uint8_t {
        switch (static_cast<TransitionEdgeStyle>(style & 0x3u)) {
            case TransitionEdgeStyle::Sloppy: return 3u;
            case TransitionEdgeStyle::Leafy:  return 2u;
            case TransitionEdgeStyle::Grainy: return 1u;
            case TransitionEdgeStyle::None:
            default: return 0u;
        }
    };
    return (priority(rhs) >= priority(lhs)) ? (rhs & 0x3u) : (lhs & 0x3u);
}

void TextureOverlayStore::recordDirtyGPUCellLocked(int lod,
                                                   const glm::ivec3& lodCoord,
                                                   VoxelTextureData data) {
    (void)lod;
    (void)lodCoord;
    (void)data;

    // Phase 3 material-bake path:
    //
    // The paint brush must not grow the fragment-time global material overlay.
    // That overlay was the bottleneck: millions of painted voxel-face cells were
    // uploaded into one large random-access SSBO/hash table, then cube.frag had
    // to probe it from the terrain/light pass.
    //
    // Painted material remains canonical in this CPU TextureOverlayStore. The
    // renderable representation is produced by remeshing the touched chunks and
    // packing the material into Vertex::material, which is the shader fast path.
    //
    // Force one empty full-upload so any old/stale overlay cells are removed
    // from the GPU table, but never enqueue per-cell GPU deltas here.
    if (!m_dirtyGPUFullUpload) {
        m_dirtyGPUCells.clear();
        m_dirtyGPUFullUpload = true;
    }
}

void TextureOverlayStore::requestFullGPUUploadLocked() {
    m_dirtyGPUCells.clear();
    m_dirtyGPUFullUpload = true;
}

void TextureOverlayStore::refreshTransitionEdgesAroundCellLocked(BrickMap& map,
                                                                 int lod,
                                                                 const glm::ivec3& lodCoord) {
    TextureBrick* brick = getBrickLocked(map, voxelToBrick(lodCoord));
    if (!brick) return;
    const glm::ivec3 local = voxelLocalInBrick(lodCoord);
    const int idx = TextureBrick::toIndex(local.x, local.y, local.z);
    VoxelTextureData center = brick->cells[idx];
    if (center.isEmpty()) return;

    const TextureType centerType = center.getType();
    uint8_t centerEdge = static_cast<uint8_t>(TransitionEdgeStyle::None);

    for (const glm::ivec3& d : kNeighborDirs) {
        const glm::ivec3 ncoord = lodCoord + d;
        TextureBrick* nbrick = getBrickLocked(map, voxelToBrick(ncoord));
        if (!nbrick) continue;

        const glm::ivec3 nlocal = voxelLocalInBrick(ncoord);
        const int nidx = TextureBrick::toIndex(nlocal.x, nlocal.y, nlocal.z);
        VoxelTextureData neighbor = nbrick->cells[nidx];
        if (neighbor.isEmpty()) continue;

        const TextureType neighborType = neighbor.getType();
        if (neighborType == centerType) continue;

        const uint8_t centerStyle = classifyTransitionEdge(centerType, neighborType, lodCoord);
        centerEdge = mergeEdgeStyle(centerEdge, centerStyle);

        const uint8_t neighborStyle = classifyTransitionEdge(neighborType, centerType, ncoord);
        const uint8_t mergedNeighborEdge = mergeEdgeStyle(neighbor.getEdgeMask(), neighborStyle);
        if (mergedNeighborEdge != neighbor.getEdgeMask()) {
            const VoxelTextureData updated = neighbor.withEdgeMask(mergedNeighborEdge);
            nbrick->cells[nidx] = updated;
            recordDirtyGPUCellLocked(lod, ncoord, updated);
        }
    }

    if (centerEdge != center.getEdgeMask()) {
        const VoxelTextureData updated = center.withEdgeMask(centerEdge);
        brick->cells[idx] = updated;
        recordDirtyGPUCellLocked(lod, lodCoord, updated);
    }
}

void TextureOverlayStore::refreshTransitionEdgesAroundFaceLocked(BrickMap& map,
                                                                 int lod,
                                                                 const glm::ivec3& lodCoord,
                                                                 uint8_t face) {
    face %= 6u;
    const glm::ivec3 storageCoord = encodeFaceCoord(lodCoord, face);
    TextureBrick* brick = getBrickLocked(map, voxelToBrick(storageCoord));
    if (!brick) return;

    const glm::ivec3 local = voxelLocalInBrick(storageCoord);
    const int idx = TextureBrick::toIndex(local.x, local.y, local.z);
    VoxelTextureData center = brick->cells[idx];
    if (center.isEmpty()) return;

    const TextureType centerType = center.getType();
    uint8_t centerEdge = static_cast<uint8_t>(TransitionEdgeStyle::None);

    int uAxis = 0;
    int vAxis = 1;
    facePlaneAxes(face, uAxis, vAxis);
    glm::ivec3 neighborOffsets[4]{};
    neighborOffsets[0][uAxis] =  1;
    neighborOffsets[1][uAxis] = -1;
    neighborOffsets[2][vAxis] =  1;
    neighborOffsets[3][vAxis] = -1;

    for (const glm::ivec3& offset : neighborOffsets) {
        const glm::ivec3 ncoord = lodCoord + offset;
        const glm::ivec3 nstorage = encodeFaceCoord(ncoord, face);
        TextureBrick* nbrick = getBrickLocked(map, voxelToBrick(nstorage));
        if (!nbrick) continue;

        const glm::ivec3 nlocal = voxelLocalInBrick(nstorage);
        const int nidx = TextureBrick::toIndex(nlocal.x, nlocal.y, nlocal.z);
        VoxelTextureData neighbor = nbrick->cells[nidx];
        if (neighbor.isEmpty()) continue;

        const TextureType neighborType = neighbor.getType();
        if (neighborType == centerType) continue;

        const uint8_t centerStyle = classifyTransitionEdge(centerType, neighborType, lodCoord);
        centerEdge = mergeEdgeStyle(centerEdge, centerStyle);

        const uint8_t neighborStyle = classifyTransitionEdge(neighborType, centerType, ncoord);
        const uint8_t mergedNeighborEdge = mergeEdgeStyle(neighbor.getEdgeMask(), neighborStyle);
        if (mergedNeighborEdge != neighbor.getEdgeMask()) {
            const VoxelTextureData updated = neighbor.withEdgeMask(mergedNeighborEdge);
            nbrick->cells[nidx] = updated;
            recordDirtyGPUCellLocked(lod, ncoord, updated);
        }
    }

    if (centerEdge != center.getEdgeMask()) {
        const VoxelTextureData updated = center.withEdgeMask(centerEdge);
        brick->cells[idx] = updated;
        recordDirtyGPUCellLocked(lod, lodCoord, updated);
    }
}

// ---------------------------------------------------------------------------
// Single-cell access
// ---------------------------------------------------------------------------

void TextureOverlayStore::setTexture(const glm::ivec3& lodCoord,
                                     int lod,
                                     VoxelTextureData data) {
    if (!isLodValid(lod) || !m_lodConfigs[lod].enabled) return;
    std::unique_lock lock(m_mutex);
    BrickKey key = voxelToBrick(lodCoord);
    glm::ivec3 local = voxelLocalInBrick(lodCoord);
    writeCellLocked(m_lodMaps[lod], lod, key, local, data);
    if (data.isEmpty()) {
        requestFullGPUUploadLocked();
    } else {
        recordDirtyGPUCellLocked(lod, lodCoord, data);
    }
    auto& map = m_lodMaps[lod];
    if (!data.isEmpty()) {
        refreshTransitionEdgesAroundCellLocked(map, lod, lodCoord);
        for (const glm::ivec3& d : kNeighborDirs) {
            refreshTransitionEdgesAroundCellLocked(map, lod, lodCoord + d);
        }
    } else {
        for (const glm::ivec3& d : kNeighborDirs) {
            refreshTransitionEdgesAroundCellLocked(map, lod, lodCoord + d);
        }
    }
    m_generation.fetch_add(1, std::memory_order_release);
}

VoxelTextureData
TextureOverlayStore::getTexture(const glm::ivec3& lodCoord, int lod) const {
    if (!isLodValid(lod)) return {};
    std::shared_lock lock(m_mutex);
    BrickKey key = voxelToBrick(lodCoord);
    glm::ivec3 local = voxelLocalInBrick(lodCoord);
    const TextureBrick* brick = getBrickLocked(m_lodMaps[lod], key);
    if (!brick) return {};
    return brick->cells[TextureBrick::toIndex(local.x, local.y, local.z)];
}

VoxelTextureData
TextureOverlayStore::getSurfaceTexture(const glm::ivec3& lodCoord,
                                       int lod,
                                       uint8_t face) const {
    if (!isLodValid(lod)) return {};

    const uint8_t queryFace = static_cast<uint8_t>(face % 6u);
    const glm::ivec3 storageCoord = encodeFaceCoord(lodCoord, queryFace);

    std::shared_lock lock(m_mutex);

    // Exact expanded/saved per-face cells win over deferred brush stamps.
    {
        const BrickKey key = voxelToBrick(storageCoord);
        const glm::ivec3 local = voxelLocalInBrick(storageCoord);
        const TextureBrick* brick = getBrickLocked(m_lodMaps[lod], key);
        if (brick) {
            const VoxelTextureData cell =
                brick->cells[TextureBrick::toIndex(local.x, local.y, local.z)];
            if (!cell.isEmpty()) {
                return cell;
            }
        }
    }

    return sampleSurfacePaintStampsLocked(lodCoord, lod, queryFace);
}

bool TextureOverlayStore::hasSurfaceTexturesInBox(const glm::ivec3& minLodCoord,
                                                  const glm::ivec3& maxExclusiveLodCoord,
                                                  int lod) const {
    if (!isLodValid(lod)) return false;
    if (minLodCoord.x >= maxExclusiveLodCoord.x ||
        minLodCoord.y >= maxExclusiveLodCoord.y ||
        minLodCoord.z >= maxExclusiveLodCoord.z) {
        return false;
    }

    const glm::ivec3 storageMin(minLodCoord.x,
                                minLodCoord.y,
                                minLodCoord.z * 6);
    const glm::ivec3 storageMax(maxExclusiveLodCoord.x - 1,
                                maxExclusiveLodCoord.y - 1,
                                (maxExclusiveLodCoord.z - 1) * 6 + 5);
    const BrickKey minBrick = voxelToBrick(storageMin);
    const BrickKey maxBrick = voxelToBrick(storageMax);

    std::shared_lock lock(m_mutex);
    const auto& map = m_lodMaps[lod];
    const int64_t brickVolume =
        int64_t(maxBrick.bx - minBrick.bx + 1) *
        int64_t(maxBrick.by - minBrick.by + 1) *
        int64_t(maxBrick.bz - minBrick.bz + 1);

    if (brickVolume > 0 && static_cast<uint64_t>(brickVolume) < map.size()) {
        for (int32_t bz = minBrick.bz; bz <= maxBrick.bz; ++bz)
        for (int32_t by = minBrick.by; by <= maxBrick.by; ++by)
        for (int32_t bx = minBrick.bx; bx <= maxBrick.bx; ++bx) {
            auto it = map.find(BrickKey{bx, by, bz});
            if (it != map.end() && it->second && it->second->activeCount > 0) {
                return true;
            }
        }
    } else {
        for (const auto& [key, brick] : map) {
            if (!brick || brick->activeCount == 0) {
                continue;
            }
            if (key.bx >= minBrick.bx && key.bx <= maxBrick.bx &&
                key.by >= minBrick.by && key.by <= maxBrick.by &&
                key.bz >= minBrick.bz && key.bz <= maxBrick.bz) {
                return true;
            }
        }
    }

    if (!m_surfacePaintStamps.empty()) {
        const int step = (lod > 0) ? (1 << lod) : 1;
        const glm::ivec3 minLod0 = lodToLOD0(minLodCoord, lod);
        const glm::ivec3 maxLod0 =
            lodToLOD0(maxExclusiveLodCoord - glm::ivec3(1), lod) + glm::ivec3(step - 1);

        for (const SurfacePaintStamp& stamp : m_surfacePaintStamps) {
            if (surfaceStampTouchesBox(stamp, minLod0, maxLod0)) {
                return true;
            }
        }
    }

    return false;
}

void TextureOverlayStore::clearTexture(const glm::ivec3& lodCoord, int lod) {
    setTexture(lodCoord, lod, VoxelTextureData{});
}

// ---------------------------------------------------------------------------
// Brush paint operations
//
// Strategy: we work in the *target LOD's voxel grid*.  The brush bounding box
// is converted from LOD-0 to the target LOD once, and we iterate that
// (smaller) volume directly.  This is dramatically cheaper than the previous
// "iterate every LOD-0 voxel" approach for high LOD levels.
// ---------------------------------------------------------------------------

int TextureOverlayStore::paintSphere(const glm::ivec3& centerLod0,
                                     int radiusVoxelsLod0,
                                     int lod,
                                     TextureType type,
                                     uint8_t variant) {
    if (!isLodValid(lod) || !m_lodConfigs[lod].enabled) return 0;
    if (radiusVoxelsLod0 <= 0) return 0;

    const glm::ivec3 centerLod = lod0ToLOD(centerLod0, lod);
    // Ceiling-divide so a radius that doesn't divide evenly still covers
    // every LOD cell touched by the LOD-0 sphere.
    const int shift = lod;
    const int rLod = (radiusVoxelsLod0 + (1 << shift) - 1) >> shift;
    if (rLod <= 0) return 0;
    const int rSq = rLod * rLod;

    VoxelTextureData data(type, variant);
    int written = 0;

    std::unique_lock lock(m_mutex);
    auto& map = m_lodMaps[lod];

    for (int dz = -rLod; dz <= rLod; ++dz) {
        for (int dy = -rLod; dy <= rLod; ++dy) {
            for (int dx = -rLod; dx <= rLod; ++dx) {
                if (dx*dx + dy*dy + dz*dz > rSq) continue;
                glm::ivec3 v = centerLod + glm::ivec3(dx, dy, dz);
                BrickKey key = voxelToBrick(v);
                glm::ivec3 local = voxelLocalInBrick(v);
                writeCellLocked(map, lod, key, local, data);
                recordDirtyGPUCellLocked(lod, v, data);
                refreshTransitionEdgesAroundCellLocked(map, lod, v);
                for (const glm::ivec3& d : kNeighborDirs) {
                    refreshTransitionEdgesAroundCellLocked(map, lod, v + d);
                }
                ++written;
            }
        }
    }
    if (written) m_generation.fetch_add(1, std::memory_order_release);
    return written;
}

int TextureOverlayStore::paintBox(const glm::ivec3& minLod0,
                                  const glm::ivec3& maxLod0,
                                  int lod,
                                  TextureType type,
                                  uint8_t variant) {
    if (!isLodValid(lod) || !m_lodConfigs[lod].enabled) return 0;

    glm::ivec3 a = lod0ToLOD(minLod0, lod);
    glm::ivec3 b = lod0ToLOD(maxLod0, lod);
    glm::ivec3 mn(std::min(a.x, b.x), std::min(a.y, b.y), std::min(a.z, b.z));
    glm::ivec3 mx(std::max(a.x, b.x), std::max(a.y, b.y), std::max(a.z, b.z));

    VoxelTextureData data(type, variant);
    int written = 0;

    std::unique_lock lock(m_mutex);
    auto& map = m_lodMaps[lod];

    for (int z = mn.z; z <= mx.z; ++z) {
        for (int y = mn.y; y <= mx.y; ++y) {
            for (int x = mn.x; x <= mx.x; ++x) {
                glm::ivec3 v(x, y, z);
                writeCellLocked(map, lod, voxelToBrick(v), voxelLocalInBrick(v), data);
                recordDirtyGPUCellLocked(lod, v, data);
                refreshTransitionEdgesAroundCellLocked(map, lod, v);
                for (const glm::ivec3& d : kNeighborDirs) {
                    refreshTransitionEdgesAroundCellLocked(map, lod, v + d);
                }
                ++written;
            }
        }
    }
    if (written) m_generation.fetch_add(1, std::memory_order_release);
    return written;
}

int TextureOverlayStore::paintFaceDisc(const glm::ivec3& centerLod0,
                                       int radiusVoxelsLod0,
                                       int normalAxis,
                                       int lod,
                                       TextureType type,
                                       uint8_t variant,
                                       uint8_t face,
                                       int maxCells) {
    if (!isLodValid(lod) || !m_lodConfigs[lod].enabled) return 0;
    if (radiusVoxelsLod0 <= 0 || maxCells <= 0) return 0;

    face = static_cast<uint8_t>(face % 6u);
    normalAxis = std::clamp(normalAxis, 0, 2);
    const int uAxis = (normalAxis == 0) ? 1 : 0;
    const int vAxis = (normalAxis == 2) ? 1 : 2;
    const glm::ivec3 centerLod = lod0ToLOD(centerLod0, lod);
    const int rLod = (radiusVoxelsLod0 + (1 << lod) - 1) >> lod;
    if (rLod <= 0) return 0;

    const int rSq = rLod * rLod;
    int candidateCells = 0;
    for (int v = -rLod; v <= rLod; ++v) {
        for (int u = -rLod; u <= rLod; ++u) {
            if (u * u + v * v <= rSq) ++candidateCells;
        }
    }
    if (candidateCells > maxCells) return 0;

    int written = 0;
    std::unique_lock lock(m_mutex);
    auto& map = m_lodMaps[lod];

    for (int v = -rLod; v <= rLod; ++v) {
        for (int u = -rLod; u <= rLod; ++u) {
            if (u * u + v * v > rSq) continue;
            glm::ivec3 coord = centerLod;
            coord[uAxis] += u;
            coord[vAxis] += v;
            VoxelTextureData data(type, variedVariant(coord, face, type, variant), 0, face);
            const glm::ivec3 storageCoord = encodeFaceCoord(coord, face);
            writeCellLocked(map, lod, voxelToBrick(storageCoord), voxelLocalInBrick(storageCoord), data);
            recordDirtyGPUCellLocked(lod, coord, data);
            ++written;
        }
    }

    const int inner = std::max(0, rLod - 1);
    const int innerSq = inner * inner;
    const int outer = rLod + 1;
    const int outerSq = outer * outer;
    for (int v = -outer; v <= outer; ++v) {
        for (int u = -outer; u <= outer; ++u) {
            const int dSq = u * u + v * v;
            if (dSq > outerSq || dSq < innerSq) continue;
            glm::ivec3 coord = centerLod;
            coord[uAxis] += u;
            coord[vAxis] += v;
            refreshTransitionEdgesAroundFaceLocked(map, lod, coord, face);
        }
    }

    if (written) m_generation.fetch_add(1, std::memory_order_release);
    return written;
}

int TextureOverlayStore::paintFaceRect(const glm::ivec3& centerLod0,
                                       int radiusVoxelsLod0,
                                       int normalAxis,
                                       int lod,
                                       TextureType type,
                                       uint8_t variant,
                                       uint8_t face,
                                       int maxCells) {
    if (!isLodValid(lod) || !m_lodConfigs[lod].enabled) return 0;
    if (radiusVoxelsLod0 <= 0 || maxCells <= 0) return 0;

    face = static_cast<uint8_t>(face % 6u);
    normalAxis = std::clamp(normalAxis, 0, 2);
    const int uAxis = (normalAxis == 0) ? 1 : 0;
    const int vAxis = (normalAxis == 2) ? 1 : 2;
    const glm::ivec3 centerLod = lod0ToLOD(centerLod0, lod);
    const int rLod = (radiusVoxelsLod0 + (1 << lod) - 1) >> lod;
    if (rLod <= 0) return 0;

    const int side = rLod * 2 + 1;
    const int64_t candidateCells = static_cast<int64_t>(side) * static_cast<int64_t>(side);
    if (candidateCells > maxCells) return 0;

    int written = 0;
    std::unique_lock lock(m_mutex);
    auto& map = m_lodMaps[lod];

    for (int v = -rLod; v <= rLod; ++v) {
        for (int u = -rLod; u <= rLod; ++u) {
            glm::ivec3 coord = centerLod;
            coord[uAxis] += u;
            coord[vAxis] += v;
            VoxelTextureData data(type, variedVariant(coord, face, type, variant), 0, face);
            const glm::ivec3 storageCoord = encodeFaceCoord(coord, face);
            writeCellLocked(map, lod, voxelToBrick(storageCoord), voxelLocalInBrick(storageCoord), data);
            recordDirtyGPUCellLocked(lod, coord, data);
            ++written;
        }
    }

    const int outer = rLod + 1;
    const int inner = std::max(0, rLod - 1);
    for (int v = -outer; v <= outer; ++v) {
        for (int u = -outer; u <= outer; ++u) {
            if (std::abs(u) < inner && std::abs(v) < inner) continue;
            glm::ivec3 coord = centerLod;
            coord[uAxis] += u;
            coord[vAxis] += v;
            refreshTransitionEdgesAroundFaceLocked(map, lod, coord, face);
        }
    }

    if (written) m_generation.fetch_add(1, std::memory_order_release);
    return written;
}

int TextureOverlayStore::paintSurfaceFaces(const std::vector<SurfaceFaceStamp>& faces,
                                           int lod,
                                           TextureType type,
                                           uint8_t variant) {
    if (!isLodValid(lod) || !m_lodConfigs[lod].enabled || faces.empty()) return 0;

    // Huge brush stamps must be authoring-fast. Transition edge masks are
    // cosmetic material-boundary metadata; refreshing them through an
    // unordered_set for every changed cell is exactly the wrong cost model for
    // 50k-300k cell stamps. Large stamps skip transition refresh in the
    // interactive path. The procedural material remains correct; only fancy
    // edge blending may be absent until a future offline/idle edge pass.
    constexpr size_t kLargeInteractiveStampThreshold = 32768u;
    const bool fastLargeStamp = faces.size() >= kLargeInteractiveStampThreshold;

    int written = 0;

    std::unique_lock lock(m_mutex);
    auto& map = m_lodMaps[lod];

    if (fastLargeStamp) {
        for (const SurfaceFaceStamp& faceStamp : faces) {
            const uint8_t face = static_cast<uint8_t>(faceStamp.face % 6u);
            const VoxelTextureData data(type, variedVariant(faceStamp.lodCoord, face, type, variant), 0, face);
            const glm::ivec3 storageCoord = encodeFaceCoord(faceStamp.lodCoord, face);

            if (const TextureBrick* existingBrick = getBrickLocked(map, voxelToBrick(storageCoord))) {
                const glm::ivec3 local = voxelLocalInBrick(storageCoord);
                const VoxelTextureData existing =
                    existingBrick->cells[TextureBrick::toIndex(local.x, local.y, local.z)];
                if (!existing.isEmpty() &&
                    existing.getType() == data.getType() &&
                    existing.getVariant() == data.getVariant() &&
                    existing.getFace() == data.getFace()) {
                    continue;
                }
            }

            writeCellLocked(map,
                            lod,
                            voxelToBrick(storageCoord),
                            voxelLocalInBrick(storageCoord),
                            data);
            recordDirtyGPUCellLocked(lod, faceStamp.lodCoord, data);
            ++written;
        }

        if (written) m_generation.fetch_add(1, std::memory_order_release);
        return written;
    }

    std::vector<SurfaceFaceStamp> changedFaces;
    changedFaces.reserve(faces.size());

    // Boundary-only transition refresh acceleration. For one brush stroke,
    // every changed face is usually painted to the same material. Interior
    // changed-vs-changed neighbors cannot form a transition boundary, so there
    // is no reason to refresh all 5 cells around every painted face.
    std::unordered_set<glm::ivec3, IVec3Hash> changedStorageCoords;
    changedStorageCoords.reserve(faces.size() * 2u + 16u);

    for (const SurfaceFaceStamp& faceStamp : faces) {
        const uint8_t face = static_cast<uint8_t>(faceStamp.face % 6u);
        const VoxelTextureData data(type, variedVariant(faceStamp.lodCoord, face, type, variant), 0, face);
        const glm::ivec3 storageCoord = encodeFaceCoord(faceStamp.lodCoord, face);

        if (const TextureBrick* existingBrick = getBrickLocked(map, voxelToBrick(storageCoord))) {
            const glm::ivec3 local = voxelLocalInBrick(storageCoord);
            const VoxelTextureData existing =
                existingBrick->cells[TextureBrick::toIndex(local.x, local.y, local.z)];
            if (!existing.isEmpty() &&
                existing.getType() == data.getType() &&
                existing.getVariant() == data.getVariant() &&
                existing.getFace() == data.getFace()) {
                continue;
            }
        }

        writeCellLocked(map,
                        lod,
                        voxelToBrick(storageCoord),
                        voxelLocalInBrick(storageCoord),
                        data);
        recordDirtyGPUCellLocked(lod, faceStamp.lodCoord, data);
        changedFaces.push_back(faceStamp);
        changedStorageCoords.insert(storageCoord);
        ++written;
    }

    for (const SurfaceFaceStamp& faceStamp : changedFaces) {
        const uint8_t face = static_cast<uint8_t>(faceStamp.face % 6u);

        int uAxis = 0;
        int vAxis = 1;
        facePlaneAxes(face, uAxis, vAxis);

        glm::ivec3 neighborOffsets[4]{};
        neighborOffsets[0][uAxis] =  1;
        neighborOffsets[1][uAxis] = -1;
        neighborOffsets[2][vAxis] =  1;
        neighborOffsets[3][vAxis] = -1;

        bool isBoundary = false;
        for (const glm::ivec3& offset : neighborOffsets) {
            const glm::ivec3 neighborStorage =
                encodeFaceCoord(faceStamp.lodCoord + offset, face);
            if (changedStorageCoords.find(neighborStorage) == changedStorageCoords.end()) {
                isBoundary = true;
                break;
            }
        }

        if (!isBoundary) {
            continue;
        }

        refreshTransitionEdgesAroundFaceLocked(map, lod, faceStamp.lodCoord, face);
        for (const glm::ivec3& offset : neighborOffsets) {
            refreshTransitionEdgesAroundFaceLocked(map, lod, faceStamp.lodCoord + offset, face);
        }
    }

    if (written) m_generation.fetch_add(1, std::memory_order_release);
    return written;
}

int TextureOverlayStore::clearSphere(const glm::ivec3& centerLod0,
                                     int radiusVoxelsLod0,
                                     int lod) {
    if (!isLodValid(lod)) return 0;
    if (radiusVoxelsLod0 <= 0) return 0;

    const glm::ivec3 centerLod = lod0ToLOD(centerLod0, lod);
    const int rLod = (radiusVoxelsLod0 + (1 << lod) - 1) >> lod;
    if (rLod <= 0) return 0;
    const int rSq = rLod * rLod;

    int cleared = 0;
    std::unique_lock lock(m_mutex);
    requestFullGPUUploadLocked();
    auto& map = m_lodMaps[lod];
    for (int dz = -rLod; dz <= rLod; ++dz)
    for (int dy = -rLod; dy <= rLod; ++dy)
    for (int dx = -rLod; dx <= rLod; ++dx) {
        if (dx*dx + dy*dy + dz*dz > rSq) continue;
        glm::ivec3 v = centerLod + glm::ivec3(dx, dy, dz);
        writeCellLocked(map, lod, voxelToBrick(v), voxelLocalInBrick(v),
                        VoxelTextureData{});
        for (const glm::ivec3& d : kNeighborDirs) {
            refreshTransitionEdgesAroundCellLocked(map, lod, v + d);
        }
        ++cleared;
    }
    if (cleared) m_generation.fetch_add(1, std::memory_order_release);
    return cleared;
}

int TextureOverlayStore::clearBox(const glm::ivec3& minLod0,
                                  const glm::ivec3& maxLod0,
                                  int lod) {
    if (!isLodValid(lod)) return 0;
    glm::ivec3 a = lod0ToLOD(minLod0, lod);
    glm::ivec3 b = lod0ToLOD(maxLod0, lod);
    glm::ivec3 mn(std::min(a.x, b.x), std::min(a.y, b.y), std::min(a.z, b.z));
    glm::ivec3 mx(std::max(a.x, b.x), std::max(a.y, b.y), std::max(a.z, b.z));

    int cleared = 0;
    std::unique_lock lock(m_mutex);
    requestFullGPUUploadLocked();
    auto& map = m_lodMaps[lod];
    for (int z = mn.z; z <= mx.z; ++z)
    for (int y = mn.y; y <= mx.y; ++y)
    for (int x = mn.x; x <= mx.x; ++x) {
        glm::ivec3 v(x, y, z);
        writeCellLocked(map, lod, voxelToBrick(v), voxelLocalInBrick(v),
                        VoxelTextureData{});
        for (const glm::ivec3& d : kNeighborDirs) {
            refreshTransitionEdgesAroundCellLocked(map, lod, v + d);
        }
        ++cleared;
    }
    if (cleared) m_generation.fetch_add(1, std::memory_order_release);
    return cleared;
}

int TextureOverlayStore::cascadeToCoarserLODs(const glm::ivec3& centerLod0,
                                              int radiusVoxelsLod0,
                                              int sourceLod,
                                              TextureType type,
                                              uint8_t variant) {
    int total = 0;
    for (int l = sourceLod + 1; l < LOD_COUNT; ++l) {
        if (!m_lodConfigs[l].enabled) continue;
        total += paintSphere(centerLod0, radiusVoxelsLod0, l, type, variant);
    }
    return total;
}

int TextureOverlayStore::cascadeFaceToCoarserLODs(const glm::ivec3& centerLod0,
                                                  int radiusVoxelsLod0,
                                                  int normalAxis,
                                                  int sourceLod,
                                                  SurfaceBrushShape shape,
                                                  TextureType type,
                                                  uint8_t variant,
                                                  uint8_t face,
                                                  int maxCellsPerLOD) {
    int total = 0;
    for (int l = sourceLod + 1; l < LOD_COUNT; ++l) {
        if (!m_lodConfigs[l].enabled) continue;
        if (shape == SurfaceBrushShape::Disc) {
            total += paintFaceDisc(centerLod0, radiusVoxelsLod0, normalAxis,
                                   l, type, variant, face, maxCellsPerLOD);
        } else {
            total += paintFaceRect(centerLod0, radiusVoxelsLod0, normalAxis,
                                   l, type, variant, face, maxCellsPerLOD);
        }
    }
    return total;
}

// ---------------------------------------------------------------------------
// Stats / state
// ---------------------------------------------------------------------------

TextureOverlayStore::Stats TextureOverlayStore::getStats() const {
    Stats s{};
    std::shared_lock lock(m_mutex);
    s.generation = m_generation.load(std::memory_order_acquire);
    s.surfaceStampCount = m_surfacePaintStamps.size();
    for (int lod = 0; lod < LOD_COUNT; ++lod) {
        s.bricksByLOD[lod] = m_brickCountsByLOD[lod];
        s.cellsByLOD[lod] = m_cellCountsByLOD[lod];
        s.totalBricks += s.bricksByLOD[lod];
        s.totalCells += s.cellsByLOD[lod];
    }
    return s;
}

size_t TextureOverlayStore::exportGPUCells(std::vector<GPUCell>& out, size_t maxCells) const {
    (void)maxCells;

    // Disabled by design.
    //
    // The old path exported every painted voxel face into a global GPU hash
    // table. With millions of brush cells this becomes a terrain/light-pass
    // tax even when the world itself can render fully textured at native speed.
    //
    // Painting now stays in the CPU sparse store and is made visible by rebaking
    // affected chunks into the compact per-vertex material stream. Returning
    // zero here keeps the shader overlay table empty, so unbaked/normal terrain
    // remains on the procedural/native fast path.
    out.clear();
    return 0;
}

size_t TextureOverlayStore::exportGPUCellsForLOD(int lod,
                                                 std::vector<GPUCell>& out,
                                                 size_t maxCells) const {
    (void)lod;
    (void)maxCells;

    // Same as exportGPUCells(): no per-cell material overlay is uploaded to the
    // GPU. LOD-specific painted data is still stored for persistence/tools and
    // for chunk material rebake, but it must not become a fragment shader hash
    // table.
    out.clear();
    return 0;
}

size_t TextureOverlayStore::consumeDirtyGPUCells(std::vector<GPUCell>& out,
                                                 size_t maxCells,
                                                 bool& requiresFullUpload) {
    out.clear();
    requiresFullUpload = false;

    std::unique_lock lock(m_mutex);
    if (m_dirtyGPUFullUpload || m_dirtyGPUCells.size() > maxCells) {
        requiresFullUpload = true;
        m_dirtyGPUCells.clear();
        m_dirtyGPUFullUpload = false;
        return 0;
    }

    out.swap(m_dirtyGPUCells);
    m_dirtyGPUCells.clear();
    return out.size();
}

bool TextureOverlayStore::isEmpty() const {
    std::shared_lock lock(m_mutex);
    if (!m_surfacePaintStamps.empty()) {
        return false;
    }
    for (size_t cells : m_cellCountsByLOD) {
        if (cells != 0u) return false;
    }
    return true;
}

void TextureOverlayStore::clear() {
    std::unique_lock lock(m_mutex);
    for (auto& map : m_lodMaps) map.clear();
    m_brickCountsByLOD.fill(0u);
    m_cellCountsByLOD.fill(0u);
    m_surfacePaintStamps.clear();
    m_surfacePaintStampChunkIndex.clear();
    m_nextSurfacePaintStampOrder = 1u;
    requestFullGPUUploadLocked();
    m_generation.fetch_add(1, std::memory_order_release);
}

void TextureOverlayStore::clearLOD(int lod) {
    if (!isLodValid(lod)) return;
    std::unique_lock lock(m_mutex);
    m_lodMaps[lod].clear();
    m_brickCountsByLOD[lod] = 0u;
    m_cellCountsByLOD[lod] = 0u;
    if (lod == 0) {
        m_surfacePaintStamps.clear();
        m_surfacePaintStampChunkIndex.clear();
        m_nextSurfacePaintStampOrder = 1u;
    }
    requestFullGPUUploadLocked();
    m_generation.fetch_add(1, std::memory_order_release);
}

// ---------------------------------------------------------------------------
// Persistence
// File layout:
//   magic(4) version(4)
//   For each LOD: pixelsPerVoxel(2) enabled(1) pad(1) brickCount(4)
//     For each brick: key(12) activeCount(4) cells(512)
// ---------------------------------------------------------------------------

static constexpr uint32_t TEXTURE_OVERLAY_MAGIC = 0x54585050; // "TXPP"
static constexpr uint32_t TEXTURE_OVERLAY_VERSION = 3;

bool TextureOverlayStore::saveToFile(const char* path) const {
    std::shared_lock lock(m_mutex);
    std::ofstream f(path, std::ios::binary);
    if (!f.is_open()) return false;

    uint32_t magic = TEXTURE_OVERLAY_MAGIC;
    uint32_t ver = 4u;
    f.write(reinterpret_cast<const char*>(&magic), sizeof(magic));
    f.write(reinterpret_cast<const char*>(&ver), sizeof(ver));

    uint32_t lodCount = LOD_COUNT;
    f.write(reinterpret_cast<const char*>(&lodCount), sizeof(lodCount));

    for (int lod = 0; lod < LOD_COUNT; ++lod) {
        const auto& cfg = m_lodConfigs[lod];
        uint16_t res = cfg.pixelsPerVoxel;
        uint8_t en = cfg.enabled ? 1 : 0;
        uint8_t pad = 0;
        f.write(reinterpret_cast<const char*>(&res), sizeof(res));
        f.write(reinterpret_cast<const char*>(&en),  sizeof(en));
        f.write(reinterpret_cast<const char*>(&pad), sizeof(pad));

        const auto& map = m_lodMaps[lod];
        uint32_t brickCount = static_cast<uint32_t>(map.size());
        f.write(reinterpret_cast<const char*>(&brickCount), sizeof(brickCount));

        for (const auto& [key, brick] : map) {
            f.write(reinterpret_cast<const char*>(&key), sizeof(key));
            f.write(reinterpret_cast<const char*>(&brick->activeCount),
                    sizeof(brick->activeCount));
            f.write(reinterpret_cast<const char*>(brick->cells.data()),
                    sizeof(VoxelTextureData) * TextureBrick::CELLS);
        }
    }

    // Version 4: persist canonical deferred surface stamps. The live GPU stamp
    // buffer is intentionally bounded, but the world/snapshot state must not be.
    // LOD remeshes and snapshot reloads can now resolve the complete paint
    // history from this canonical list instead of only the newest live entries.
    const uint32_t stampCount = (m_surfacePaintStamps.size() > static_cast<size_t>(UINT32_MAX))
        ? UINT32_MAX
        : static_cast<uint32_t>(m_surfacePaintStamps.size());
    f.write(reinterpret_cast<const char*>(&stampCount), sizeof(stampCount));

    for (uint32_t i = 0; i < stampCount; ++i) {
        const SurfacePaintStamp& stamp = m_surfacePaintStamps[i];
        f.write(reinterpret_cast<const char*>(&stamp.centerVoxelLod0.x), sizeof(float));
        f.write(reinterpret_cast<const char*>(&stamp.centerVoxelLod0.y), sizeof(float));
        f.write(reinterpret_cast<const char*>(&stamp.centerVoxelLod0.z), sizeof(float));
        f.write(reinterpret_cast<const char*>(&stamp.bboxMinLod0.x), sizeof(int32_t));
        f.write(reinterpret_cast<const char*>(&stamp.bboxMinLod0.y), sizeof(int32_t));
        f.write(reinterpret_cast<const char*>(&stamp.bboxMinLod0.z), sizeof(int32_t));
        f.write(reinterpret_cast<const char*>(&stamp.bboxMaxLod0.x), sizeof(int32_t));
        f.write(reinterpret_cast<const char*>(&stamp.bboxMaxLod0.y), sizeof(int32_t));
        f.write(reinterpret_cast<const char*>(&stamp.bboxMaxLod0.z), sizeof(int32_t));

        int32_t radius = static_cast<int32_t>(stamp.radiusVoxelsLod0);
        uint8_t shape = static_cast<uint8_t>(stamp.shape);
        uint8_t type = static_cast<uint8_t>(stamp.type);
        uint8_t variant = stamp.variant;
        uint8_t sourceFace = stamp.sourceFace;
        uint32_t order = stamp.order;

        f.write(reinterpret_cast<const char*>(&radius), sizeof(radius));
        f.write(reinterpret_cast<const char*>(&shape), sizeof(shape));
        f.write(reinterpret_cast<const char*>(&type), sizeof(type));
        f.write(reinterpret_cast<const char*>(&variant), sizeof(variant));
        f.write(reinterpret_cast<const char*>(&sourceFace), sizeof(sourceFace));
        f.write(reinterpret_cast<const char*>(&order), sizeof(order));
    }

    return f.good();
}

bool TextureOverlayStore::loadFromFile(const char* path) {
    std::ifstream f(path, std::ios::binary);
    if (!f.is_open()) return false;

    uint32_t magic = 0, ver = 0;
    f.read(reinterpret_cast<char*>(&magic), sizeof(magic));
    f.read(reinterpret_cast<char*>(&ver), sizeof(ver));
    constexpr uint32_t kTextureOverlayMaxReadableVersion = 4u;
    if (magic != TEXTURE_OVERLAY_MAGIC || ver > kTextureOverlayMaxReadableVersion) return false;

    uint32_t lodCount = 0;
    f.read(reinterpret_cast<char*>(&lodCount), sizeof(lodCount));
    if (lodCount > LOD_COUNT) return false;

    std::unique_lock lock(m_mutex);
    for (auto& m : m_lodMaps) m.clear();
    m_brickCountsByLOD.fill(0u);
    m_cellCountsByLOD.fill(0u);
    m_surfacePaintStamps.clear();
    m_surfacePaintStampChunkIndex.clear();
    m_nextSurfacePaintStampOrder = 1u;

    for (uint32_t lod = 0; lod < lodCount; ++lod) {
        uint16_t res = 0; uint8_t en = 0; uint8_t pad = 0;
        f.read(reinterpret_cast<char*>(&res), sizeof(res));
        f.read(reinterpret_cast<char*>(&en),  sizeof(en));
        f.read(reinterpret_cast<char*>(&pad), sizeof(pad));
        m_lodConfigs[lod] = LODTextureConfig{ res, en != 0 };

        uint32_t brickCount = 0;
        f.read(reinterpret_cast<char*>(&brickCount), sizeof(brickCount));
        for (uint32_t b = 0; b < brickCount; ++b) {
            BrickKey key{};
            uint32_t active = 0;
            f.read(reinterpret_cast<char*>(&key), sizeof(key));
            f.read(reinterpret_cast<char*>(&active), sizeof(active));
            auto brick = std::make_unique<TextureBrick>();
            brick->activeCount = active;
            if (ver >= 3) {
                f.read(reinterpret_cast<char*>(brick->cells.data()),
                       sizeof(VoxelTextureData) * TextureBrick::CELLS);
            } else {
                for (auto& cell : brick->cells) {
                    uint8_t packed = 0;
                    f.read(reinterpret_cast<char*>(&packed), sizeof(packed));
                    cell.packed = packed;
                    cell.face = 3;
                }
            }
            uint32_t actualActive = 0u;
            for (const auto& cell : brick->cells) {
                if (!cell.isEmpty()) {
                    ++actualActive;
                }
            }
            brick->activeCount = actualActive;
            ++m_brickCountsByLOD[lod];
            m_cellCountsByLOD[lod] += brick->activeCount;
            m_lodMaps[lod].emplace(key, std::move(brick));
        }
    }

    if (ver >= 4) {
        uint32_t stampCount = 0;
        f.read(reinterpret_cast<char*>(&stampCount), sizeof(stampCount));
        constexpr uint32_t kMaxSerializedSurfacePaintStamps = 1000000u;
        if (stampCount > kMaxSerializedSurfacePaintStamps) {
            return false;
        }

        m_surfacePaintStamps.reserve(stampCount);
        uint32_t maxOrder = 0u;
        for (uint32_t i = 0; i < stampCount; ++i) {
            SurfacePaintStamp stamp{};
            f.read(reinterpret_cast<char*>(&stamp.centerVoxelLod0.x), sizeof(float));
            f.read(reinterpret_cast<char*>(&stamp.centerVoxelLod0.y), sizeof(float));
            f.read(reinterpret_cast<char*>(&stamp.centerVoxelLod0.z), sizeof(float));
            f.read(reinterpret_cast<char*>(&stamp.bboxMinLod0.x), sizeof(int32_t));
            f.read(reinterpret_cast<char*>(&stamp.bboxMinLod0.y), sizeof(int32_t));
            f.read(reinterpret_cast<char*>(&stamp.bboxMinLod0.z), sizeof(int32_t));
            f.read(reinterpret_cast<char*>(&stamp.bboxMaxLod0.x), sizeof(int32_t));
            f.read(reinterpret_cast<char*>(&stamp.bboxMaxLod0.y), sizeof(int32_t));
            f.read(reinterpret_cast<char*>(&stamp.bboxMaxLod0.z), sizeof(int32_t));

            int32_t radius = 1;
            uint8_t shape = 0u;
            uint8_t type = 0u;
            uint8_t variant = 0u;
            uint8_t sourceFace = 3u;
            uint32_t order = 0u;

            f.read(reinterpret_cast<char*>(&radius), sizeof(radius));
            f.read(reinterpret_cast<char*>(&shape), sizeof(shape));
            f.read(reinterpret_cast<char*>(&type), sizeof(type));
            f.read(reinterpret_cast<char*>(&variant), sizeof(variant));
            f.read(reinterpret_cast<char*>(&sourceFace), sizeof(sourceFace));
            f.read(reinterpret_cast<char*>(&order), sizeof(order));

            stamp.radiusVoxelsLod0 = std::max(1, radius);
            stamp.shape = (shape == static_cast<uint8_t>(SurfaceBrushShape::Rect))
                ? SurfaceBrushShape::Rect
                : SurfaceBrushShape::Disc;
            stamp.type = (type < static_cast<uint8_t>(TextureType::COUNT))
                ? static_cast<TextureType>(type)
                : TextureType::Grass;
            stamp.variant = static_cast<uint8_t>(variant & 0x7u);
            stamp.sourceFace = (sourceFace < 6u) ? sourceFace : 3u;
            stamp.order = (order != 0u) ? order : (i + 1u);
            maxOrder = std::max(maxOrder, stamp.order);

            m_surfacePaintStamps.push_back(stamp);
        }

        for (uint32_t i = 0; i < static_cast<uint32_t>(m_surfacePaintStamps.size()); ++i) {
            indexSurfacePaintStampLocked(i);
        }
        m_nextSurfacePaintStampOrder = maxOrder + 1u;
        if (m_nextSurfacePaintStampOrder == 0u) {
            m_nextSurfacePaintStampOrder = 1u;
        }
    }

    requestFullGPUUploadLocked();
    m_generation.fetch_add(1, std::memory_order_release);
    return f.good() || f.eof();
}


// ---------------------------------------------------------------------------
// Deferred surface paint stamps — O(1) interactive brush authoring.
// ---------------------------------------------------------------------------

uint32_t TextureOverlayStore::appendSurfacePaintStamp(const glm::vec3& centerWorld,
                                                     int radiusVoxelsLod0,
                                                     SurfaceBrushShape shape,
                                                     TextureType type,
                                                     uint8_t variant,
                                                     uint8_t sourceFace) {
    if (radiusVoxelsLod0 <= 0) {
        return 0u;
    }

    SurfacePaintStamp stamp{};
    stamp.centerVoxelLod0 = centerWorld * static_cast<float>(WorldConfig::VOXELS_PER_METER);
    stamp.radiusVoxelsLod0 = std::max(1, radiusVoxelsLod0);
    stamp.shape = shape;
    stamp.type = type;
    stamp.variant = static_cast<uint8_t>(variant & 0x7u);
    stamp.sourceFace = (sourceFace < 6u) ? sourceFace : 3u;

    // Deferred material stamps are bounded 3D surface volumes. They are O(1)
    // to author and are consumed by the live material-overlay shader path.
    // The brush must not require a chunk material rebake just to become
    // visible; rebaking is expensive, delayed, and can produce chunk-wide
    // material changes after the instant visual stamp has already appeared.
    //
    // The +2 voxel pad keeps chunk indexing conservative at exact integer face
    // planes and LOD boundaries. sampleSurfacePaintStampsLocked() performs the
    // exact sphere/box test before returning material for any offline/explicit
    // bake path that still asks the CPU store.
    const float r = static_cast<float>(stamp.radiusVoxelsLod0) + 2.0f;
    stamp.bboxMinLod0 = glm::ivec3(
        static_cast<int>(std::floor(stamp.centerVoxelLod0.x - r)),
        static_cast<int>(std::floor(stamp.centerVoxelLod0.y - r)),
        static_cast<int>(std::floor(stamp.centerVoxelLod0.z - r)));
    stamp.bboxMaxLod0 = glm::ivec3(
        static_cast<int>(std::ceil(stamp.centerVoxelLod0.x + r)),
        static_cast<int>(std::ceil(stamp.centerVoxelLod0.y + r)),
        static_cast<int>(std::ceil(stamp.centerVoxelLod0.z + r)));

    std::unique_lock lock(m_mutex);
    if (!m_lodConfigs[0].enabled) {
        return 0u;
    }

    if (m_nextSurfacePaintStampOrder == 0u) {
        m_nextSurfacePaintStampOrder = 1u;
    }
    stamp.order = m_nextSurfacePaintStampOrder++;

    const uint32_t stampIndex = static_cast<uint32_t>(m_surfacePaintStamps.size());
    m_surfacePaintStamps.push_back(stamp);
    indexSurfacePaintStampLocked(stampIndex);

    // Wake the bounded GPU live-stamp upload path without generating per-cell
    // overlay deltas. This keeps painting on the compact shader path and leaves
    // chunk meshes/topology untouched.
    requestFullGPUUploadLocked();
    m_generation.fetch_add(1, std::memory_order_release);
    return stamp.order;
}

size_t TextureOverlayStore::getSurfacePaintStampCount() const {
    std::shared_lock lock(m_mutex);
    return m_surfacePaintStamps.size();
}

size_t TextureOverlayStore::exportLiveSurfacePaintStamps(
    std::vector<SurfacePaintStamp>& out,
    size_t maxStamps) const {
    (void)maxStamps;

    // Final material rendering must not depend on a bounded live-stamp list.
    // The texture brush now writes permanent per-face cells and schedules a
    // material-only chunk rebake, so the visible result is packed into the
    // normal Vertex::material path. Returning zero here disables the old
    // fragment-time live overlay and removes the "oldest strokes disappear
    // after maxStamps" failure mode instead of merely raising the limit.
    out.clear();
    return 0u;
}


void TextureOverlayStore::indexSurfacePaintStampLocked(uint32_t stampIndex) {
    if (stampIndex >= m_surfacePaintStamps.size()) {
        return;
    }

    const SurfacePaintStamp& stamp = m_surfacePaintStamps[stampIndex];
    const glm::ivec3 c0 = WorldConfig::microVoxelToChunk(stamp.bboxMinLod0);
    const glm::ivec3 c1 = WorldConfig::microVoxelToChunk(stamp.bboxMaxLod0);

    for (int z = std::min(c0.z, c1.z); z <= std::max(c0.z, c1.z); ++z) {
        for (int y = std::min(c0.y, c1.y); y <= std::max(c0.y, c1.y); ++y) {
            for (int x = std::min(c0.x, c1.x); x <= std::max(c0.x, c1.x); ++x) {
                m_surfacePaintStampChunkIndex[glm::ivec3(x, y, z)].push_back(stampIndex);
            }
        }
    }
}

bool TextureOverlayStore::surfaceStampTouchesBox(const TextureOverlayStore::SurfacePaintStamp& stamp,
                                                 const glm::ivec3& minLod0,
                                                 const glm::ivec3& maxLod0) {
    return !(stamp.bboxMaxLod0.x < minLod0.x || stamp.bboxMinLod0.x > maxLod0.x ||
             stamp.bboxMaxLod0.y < minLod0.y || stamp.bboxMinLod0.y > maxLod0.y ||
             stamp.bboxMaxLod0.z < minLod0.z || stamp.bboxMinLod0.z > maxLod0.z);
}

VoxelTextureData TextureOverlayStore::sampleSurfacePaintStampsLocked(const glm::ivec3& lodCoord,
                                                                     int lod,
                                                                     uint8_t face) const {
    if (m_surfacePaintStamps.empty()) {
        return {};
    }

    const int step = std::max(1, (lod > 0) ? (1 << lod) : 1);
    const int halfStep = step / 2;
    const glm::ivec3 lod0Base = lodToLOD0(lodCoord, lod);
    const glm::ivec3 lod0Max = lod0Base + glm::ivec3(step - 1);
    const glm::ivec3 lod0Sample = lod0Base + glm::ivec3(halfStep);

    const uint8_t queryFace = static_cast<uint8_t>(face % 6u);
    const int queryAxis = queryFace / 2;
    const float querySign = (queryFace & 1u) ? 1.0f : -1.0f;

    // Query the chunk-index range covered by this LOD face, not only the face
    // center. Coarse LOD faces can straddle a chunk boundary; center-only lookup
    // is why lower LODs sometimes missed paint that was perfect at LOD0.
    glm::ivec3 queryMin = lod0Base;
    glm::ivec3 queryMax = lod0Max;
    if ((queryFace & 1u) != 0u) {
        queryMax[queryAxis] += 1;
    } else {
        queryMin[queryAxis] -= 1;
    }

    const glm::ivec3 chunkMin = WorldConfig::microVoxelToChunk(queryMin);
    const glm::ivec3 chunkMax = WorldConfig::microVoxelToChunk(queryMax);

    auto clampf = [](float v, float lo, float hi) {
        return std::max(lo, std::min(v, hi));
    };

    auto faceCenterPoint = [&]() {
        glm::vec3 p = glm::vec3(lod0Sample) + glm::vec3(0.5f);
        p[queryAxis] += querySign * 0.5f * static_cast<float>(step);
        return p;
    };

    auto stampContainsQueryFace = [&](const SurfacePaintStamp& stamp) -> bool {
        const float radius = static_cast<float>(stamp.radiusVoxelsLod0);

        // LOD0 keeps the exact original authoring rule: the exposed face center
        // must be inside the brush. This removes the old +1 voxel bleed that
        // caused visible material changes around the brush edge.
        if (step == 1) {
            const glm::vec3 p = faceCenterPoint();
            const glm::vec3 d = p - stamp.centerVoxelLod0;
            if (stamp.shape == SurfaceBrushShape::Disc) {
                return glm::dot(d, d) <= radius * radius + 1e-5f;
            }
            return std::abs(d.x) <= radius + 1e-5f &&
                   std::abs(d.y) <= radius + 1e-5f &&
                   std::abs(d.z) <= radius + 1e-5f;
        }

        // Coarser LODs use face-rectangle intersection against the same exact
        // brush volume. This fills the lower-LOD representation whenever the
        // coarse face actually intersects the brush, without inflating the
        // brush radius globally.
        const float plane = (queryFace & 1u)
            ? static_cast<float>(lod0Base[queryAxis] + step)
            : static_cast<float>(lod0Base[queryAxis]);

        if (stamp.shape == SurfaceBrushShape::Disc) {
            glm::vec3 closest = stamp.centerVoxelLod0;
            closest[queryAxis] = plane;
            for (int axis = 0; axis < 3; ++axis) {
                if (axis == queryAxis) {
                    continue;
                }
                closest[axis] = clampf(
                    stamp.centerVoxelLod0[axis],
                    static_cast<float>(lod0Base[axis]),
                    static_cast<float>(lod0Base[axis] + step));
            }
            const glm::vec3 d = closest - stamp.centerVoxelLod0;
            return glm::dot(d, d) <= radius * radius + 1e-5f;
        }

        if (std::abs(stamp.centerVoxelLod0[queryAxis] - plane) > radius + 1e-5f) {
            return false;
        }
        for (int axis = 0; axis < 3; ++axis) {
            if (axis == queryAxis) {
                continue;
            }
            const float a0 = static_cast<float>(lod0Base[axis]);
            const float a1 = static_cast<float>(lod0Base[axis] + step);
            const float b0 = stamp.centerVoxelLod0[axis] - radius;
            const float b1 = stamp.centerVoxelLod0[axis] + radius;
            if (a1 < b0 || b1 < a0) {
                return false;
            }
        }
        return true;
    };

    uint32_t bestOrder = 0u;
    VoxelTextureData best{};

    for (int cz = std::min(chunkMin.z, chunkMax.z); cz <= std::max(chunkMin.z, chunkMax.z); ++cz) {
        for (int cy = std::min(chunkMin.y, chunkMax.y); cy <= std::max(chunkMin.y, chunkMax.y); ++cy) {
            for (int cx = std::min(chunkMin.x, chunkMax.x); cx <= std::max(chunkMin.x, chunkMax.x); ++cx) {
                auto it = m_surfacePaintStampChunkIndex.find(glm::ivec3(cx, cy, cz));
                if (it == m_surfacePaintStampChunkIndex.end()) {
                    continue;
                }

                const auto& candidates = it->second;
                for (auto rit = candidates.rbegin(); rit != candidates.rend(); ++rit) {
                    const uint32_t stampIndex = *rit;
                    if (stampIndex >= m_surfacePaintStamps.size()) {
                        continue;
                    }

                    const SurfacePaintStamp& stamp = m_surfacePaintStamps[stampIndex];
                    if (stamp.order <= bestOrder) {
                        break;
                    }
                    if (!surfaceStampTouchesBox(stamp, queryMin, queryMax)) {
                        continue;
                    }
                    if (!stampContainsQueryFace(stamp)) {
                        continue;
                    }

                    bestOrder = stamp.order;
                    best = VoxelTextureData(
                        stamp.type,
                        variedVariant(lod0Sample, queryFace, stamp.type, stamp.variant),
                        0u,
                        queryFace);
                    break;
                }
            }
        }
    }

    return best;
}

} // namespace TextureOverlay

````

## FIND: Vertex::material

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- src/ui/debug_menu/world/TexturePaintTool.cpp
- src/world/edit/TextureOverlayStore.cpp

Occurrence preview:
- src/ui/debug_menu/world/TexturePaintTool.cpp:141: // only the touched chunks are remeshed so Vertex::material becomes the same
- src/world/edit/TextureOverlayStore.cpp:305: // packing the material into Vertex::material, which is the shader fast path.
- src/world/edit/TextureOverlayStore.cpp:1388: // normal Vertex::material path. Returning zero here disables the old


## FIND: getSurfaceTexture

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/world/edit/TextureOverlayStore.h
- src/ui/debug_menu/world/TexturePaintTool.cpp
- src/world/edit/TerrainEditMesher.cpp
- src/world/edit/TextureOverlayStore.cpp

Occurrence preview:
- include/world/edit/TextureOverlayStore.h:159: // The mesher resolves stamps through getSurfaceTexture() while emitting real
- include/world/edit/TextureOverlayStore.h:197: VoxelTextureData getSurfaceTexture(const glm::ivec3& lodCoord,
- src/ui/debug_menu/world/TexturePaintTool.cpp:723: // material rebake resolves this stamp through TextureOverlayStore::getSurfaceTexture()
- src/world/edit/TerrainEditMesher.cpp:1181: const auto tex = textureStore->getSurfaceTexture(glm::ivec3(wx, wy, wz), lodLevel, face);
- src/world/edit/TextureOverlayStore.cpp:467: TextureOverlayStore::getSurfaceTexture(const glm::ivec3& lodCoord,


## FIND: materialOnly

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/world/edit/TerrainEditRemeshScheduler.h
- src/world/edit/TerrainEditRemeshScheduler.cpp

Occurrence preview:
- include/world/edit/TerrainEditRemeshScheduler.h:179: bool materialOnly = false);
- src/world/edit/TerrainEditRemeshScheduler.cpp:1159: bool materialOnly)
- src/world/edit/TerrainEditRemeshScheduler.cpp:1179: if (materialOnly) {
- src/world/edit/TerrainEditRemeshScheduler.cpp:1188: auto& dst = materialOnly ? m_materialDirty : m_dirty;
- src/world/edit/TerrainEditRemeshScheduler.cpp:1214: if (!materialOnly) {


## FIND: LoadEditMeshJob

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/world/ChunkHoleTracker.h
- include/world/chunks/core/ChunkJobs.h
- src/world/chunks/core/ChunkJobs.cpp
- src/world/World.cpp
- src/world/WorldLODSwaps.cpp
- src/world/WorldLODTransitions.cpp
- src/world/WorldUpdateMeshing.cpp

Occurrence preview:
- include/world/ChunkHoleTracker.h:31: MeshLoaded,             // LoadPrecomputedMeshJob or LoadEditMeshJob completed
- include/world/chunks/core/ChunkJobs.h:192: void LoadEditMeshJob(JobCtx& ctx, void* user);
- src/world/chunks/core/ChunkJobs.cpp:502: // have selected LoadEditMeshJob.  Redirect here to guarantee correct mesh
- src/world/chunks/core/ChunkJobs.cpp:505: LoadEditMeshJob(ctx, user);
- src/world/chunks/core/ChunkJobs.cpp:726: // LoadEditMeshJob — runs edit mesher (Voxel or DCCM) for chunks with overlays
- src/world/chunks/core/ChunkJobs.cpp:729: void LoadEditMeshJob(JobCtx& /*ctx*/, void* user) {
- src/world/World.cpp:242: auto loadJobFn = useEditMesher ? LoadEditMeshJob : LoadPrecomputedMeshJob;
- src/world/WorldLODSwaps.cpp:156: ? LoadEditMeshJob
- src/world/WorldLODTransitions.cpp:573: ? LoadEditMeshJob
- src/world/WorldUpdateMeshing.cpp:171: auto loadJobFn = (useEditMesher && !isDCCM) ? LoadEditMeshJob : LoadPrecomputedMeshJob;


## FIND: enqueueMeshForUpload

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/world/chunks/streaming/ChunkUploadSystem.h
- include/world/World.h
- include/world/WorldDiagnostics.h
- src/world/chunks/core/ChunkJobs.cpp
- src/world/chunks/streaming/ChunkUploadSystem.cpp
- src/world/edit/TerrainEditRemeshScheduler.cpp
- src/world/World.cpp
- src/world/WorldRendering.cpp

Occurrence preview:
- include/world/chunks/streaming/ChunkUploadSystem.h:154: void enqueueMeshForUpload(
- include/world/chunks/streaming/ChunkUploadSystem.h:170: void enqueueMeshForUpload(
- include/world/World.h:301: void enqueueMeshForUpload(entt::entity entity,
- include/world/World.h:315: void enqueueMeshForUpload(entt::entity entity,
- include/world/WorldDiagnostics.h:59: float gpuUploadEnqueueMs{0.0f}; // enqueueMeshForUpload
- src/world/chunks/core/ChunkJobs.cpp:1065: payload->world->enqueueMeshForUpload(
- src/world/chunks/core/ChunkJobs.cpp:1106: payload->world->enqueueMeshForUpload(payload->entity,
- src/world/chunks/streaming/ChunkUploadSystem.cpp:58: void ChunkUploadSystem::enqueueMeshForUpload(
- src/world/chunks/streaming/ChunkUploadSystem.cpp:70: enqueueMeshForUpload(entity, std::move(subChunks), 1, fromTerrainEdit, versionState, version,
- src/world/chunks/streaming/ChunkUploadSystem.cpp:75: void ChunkUploadSystem::enqueueMeshForUpload(
- src/world/edit/TerrainEditRemeshScheduler.cpp:970: world->enqueueMeshForUpload(
- src/world/edit/TerrainEditRemeshScheduler.cpp:1106: world->enqueueMeshForUpload(
- src/world/World.cpp:626: // enqueueMeshForUpload() moved to WorldRendering.cpp
- src/world/WorldRendering.cpp:12: // enqueueMeshForUpload() extracted from World.cpp
- src/world/WorldRendering.cpp:541: void World::enqueueMeshForUpload(entt::entity entity,
- src/world/WorldRendering.cpp:547: m_uploadSystem.enqueueMeshForUpload(entity, std::move(mesh), fromTerrainEdit, versionState, version,
- src/world/WorldRendering.cpp:551: void World::enqueueMeshForUpload(entt::entity entity,


## FIND: storeEditArtifact

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/world/World.h
- src/world/chunks/core/ChunkJobs.cpp
- src/world/edit/TerrainEditRemeshScheduler.cpp
- src/world/World.cpp

Occurrence preview:
- include/world/World.h:493: uint64_t generation{0};  // bumped on every storeEditArtifact
- include/world/World.h:499: void storeEditArtifact(const glm::ivec3& chunkCoord,
- include/world/World.h:1028: uint64_t m_editArtifactGenCounter{0};  // monotonic, bumped per storeEditArtifact
- src/world/chunks/core/ChunkJobs.cpp:947: world->storeEditArtifact(
- src/world/chunks/core/ChunkJobs.cpp:956: world->storeEditArtifact(
- src/world/edit/TerrainEditRemeshScheduler.cpp:885: world->storeEditArtifact(
- src/world/edit/TerrainEditRemeshScheduler.cpp:1512: world->storeEditArtifact(
- src/world/World.cpp:552: void World::storeEditArtifact(const glm::ivec3& chunkCoord,


## FIND: PendingMeshHandle

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/rendering/culling/GPUCullingSystem.h
- include/world/ChunkHoleTracker.h
- include/world/chunks/core/Chunk.h
- include/world/chunks/core/ChunkJobs.h
- include/world/chunks/core/ChunkLODSystem.h
- include/world/chunks/core/ChunkManager.h
- include/world/chunks/core/ChunkManagerTypes.h
- include/world/chunks/streaming/ChunkUploadSystem.h
- include/world/World.h
- include/world/WorldDiagnostics.h
- src/core/engine/EngineSubsystemInit.cpp
- src/core/engine/EngineVulkanInit.cpp
- src/world/chunks/core/ChunkJobs.cpp
- src/world/chunks/streaming/ChunkUploadSystem.cpp
- src/world/edit/TerrainEditRemeshScheduler.cpp
- src/world/WorldChunkCRUD.cpp
- src/world/WorldChunkReset.cpp
- src/world/WorldLODSwaps.cpp
- src/world/WorldLODTransitions.cpp
- src/world/WorldTerrainEditCollision.cpp
- src/world/WorldUpdateFinalize.cpp
- src/world/WorldUpdateLODScan.cpp

Occurrence preview:
- include/rendering/culling/GPUCullingSystem.h:169: * Inactive slots are useful for PendingMeshHandle uploads: data can be
- include/world/ChunkHoleTracker.h:32: UploadCompleted,        // ChunkUploadSystem staged PendingMeshHandle
- include/world/ChunkHoleTracker.h:33: LODSwapExecuted,        // processLODSwaps moved PendingMeshHandle -> MeshHandle
- include/world/chunks/core/Chunk.h:146: * MeshHandle/PendingMeshHandle until the paged renderer path is implemented.
- include/world/chunks/core/Chunk.h:336: * During LOD transitions, the new mesh is uploaded into PendingMeshHandle
- include/world/chunks/core/Chunk.h:339: * PendingMeshHandle → MeshHandle for the entire batch in one frame.
- include/world/chunks/core/Chunk.h:342: struct PendingMeshHandle {
- include/world/chunks/core/ChunkJobs.h:88: bool isRemesh{false};      // true = LOD remesh (stage to PendingMeshHandle)
- include/world/chunks/core/ChunkLODSystem.h:85: * @param isRemesh If true, this is a LOD remesh (stage to PendingMeshHandle)
- include/world/chunks/core/ChunkManager.h:248: * Returns the batchId that should be stored in each chunk's PendingMeshHandle.
- include/world/chunks/core/ChunkManager.h:253: * Signal that one chunk in a batch has its PendingMeshHandle uploaded.
- include/world/chunks/core/ChunkManagerTypes.h:45: std::atomic<uint32_t> chunksReady{0}; // How many have PendingMeshHandle uploaded
- include/world/chunks/streaming/ChunkUploadSystem.h:40: bool isRemesh{false};        // true = LOD remesh (stage to PendingMeshHandle), false = initial load
- include/world/chunks/streaming/ChunkUploadSystem.h:100: * Called by ChunkUploadSystem when a remesh upload stages into PendingMeshHandle.
- include/world/chunks/streaming/ChunkUploadSystem.h:167: * @param isRemesh If true, stages to PendingMeshHandle instead of replacing MeshHandle
- include/world/World.h:312: * @param isRemesh If true, stages to PendingMeshHandle for batch swap
- include/world/World.h:800: * Process LOD batch swaps - atomically swap PendingMeshHandle → MeshHandle
- include/world/World.h:820: * Process solo edit swaps - swap PendingMeshHandle (batchId=0) → MeshHandle
- include/world/World.h:831: * Clean up orphaned PendingMeshHandles from cancelled LOD batches.
- include/world/WorldDiagnostics.h:510: uint32_t errMissingPending{0};        // missing PendingMeshHandle at swap
- include/world/WorldDiagnostics.h:511: uint32_t errMismatchedBatch{0};       // PendingMeshHandle belonged to wrong batch
- src/core/engine/EngineSubsystemInit.cpp:392: if (registry.all_of<PendingMeshHandle>(entity)) {
- src/core/engine/EngineSubsystemInit.cpp:393: applyHandle(registry.get<PendingMeshHandle>(entity).handle);
- src/core/engine/EngineVulkanInit.cpp:81: // LOD transitions (old + new meshes coexist via PendingMeshHandle).
- src/world/chunks/core/ChunkJobs.cpp:704: // the entire batch swaps PendingMeshHandle → MeshHandle.
- src/world/chunks/core/ChunkJobs.cpp:1063: // PendingMeshHandle swap, freezing all chunks in that batch at old LOD.
- src/world/chunks/streaming/ChunkUploadSystem.cpp:630: // PendingMeshHandle so processLODSwaps can swap it in
- src/world/chunks/streaming/ChunkUploadSystem.cpp:635: PendingMeshHandle pending;
- src/world/chunks/streaming/ChunkUploadSystem.cpp:645: // Free any prior PendingMeshHandle's GPU resources
- src/world/chunks/streaming/ChunkUploadSystem.cpp:646: if (registry.all_of<PendingMeshHandle>(req.entity)) {
- src/world/edit/TerrainEditRemeshScheduler.cpp:1256: registry.all_of<PendingMeshHandle>(entity);
- src/world/WorldChunkCRUD.cpp:193: if (m_registry.all_of<PendingMeshHandle>(entity)) {
- src/world/WorldChunkCRUD.cpp:194: const auto& pending = m_registry.get<PendingMeshHandle>(entity);
- src/world/WorldChunkCRUD.cpp:279: // Also clean up staged PendingMeshHandle if present
- src/world/WorldChunkCRUD.cpp:280: if (m_registry.valid(entity) && m_registry.all_of<PendingMeshHandle>(entity)) {
- src/world/WorldChunkReset.cpp:65: // Also collect PendingMeshHandle resources
- src/world/WorldChunkReset.cpp:66: if (m_registry.all_of<PendingMeshHandle>(entity)) {
- src/world/WorldChunkReset.cpp:67: const auto& pending = m_registry.get<PendingMeshHandle>(entity);
- src/world/WorldLODSwaps.cpp:173: void World::cleanupStalePendingMeshHandles() {
- src/world/WorldLODSwaps.cpp:174: // Free GPU resources for ALL entities with a PendingMeshHandle.
- src/world/WorldLODSwaps.cpp:179: auto view = m_registry.view<PendingMeshHandle>();
- src/world/WorldLODSwaps.cpp:182: auto& pending = view.get<PendingMeshHandle>(entity);
- src/world/WorldLODTransitions.cpp:54: // PendingMeshHandle GPU resources (buffers + culling slots).
- src/world/WorldLODTransitions.cpp:67: cleanupStalePendingMeshHandles();
- src/world/WorldLODTransitions.cpp:145: cleanupStalePendingMeshHandles();
- src/world/WorldLODTransitions.cpp:200: // PendingMeshHandle GPU resources.  Without this, old batches from
- src/world/WorldTerrainEditCollision.cpp:425: if (m_registry.all_of<PendingMeshHandle>(entity)) {
- src/world/WorldTerrainEditCollision.cpp:426: const auto& pending = m_registry.get<PendingMeshHandle>(entity);
- src/world/WorldUpdateFinalize.cpp:190: bool hasPendingMeshHandle{false};
- src/world/WorldUpdateFinalize.cpp:233: entry.hasPendingMeshHandle = m_registry.all_of<PendingMeshHandle>(entity);
- src/world/WorldUpdateFinalize.cpp:234: if (entry.hasPendingMeshHandle) {
- src/world/WorldUpdateFinalize.cpp:235: entry.pendingBatchId = m_registry.get<PendingMeshHandle>(entity).batchId;
- src/world/WorldUpdateLODScan.cpp:20: //   2. Clean up orphaned PendingMeshHandles (free GPU resources)
- src/world/WorldUpdateLODScan.cpp:162: if (m_registry.all_of<PendingMeshHandle>(entity)) return;
- src/world/WorldUpdateLODScan.cpp:340: if (m_registry.all_of<PendingMeshHandle>(entity)) continue;
- src/world/WorldUpdateLODScan.cpp:496: if (m_registry.all_of<PendingMeshHandle>(r.entity))
