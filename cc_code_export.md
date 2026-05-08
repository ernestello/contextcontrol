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
    (void)wx;
    (void)wy;
    (void)wz;
    (void)face;
    (void)pixelsPerVoxel;

    // IMPORTANT: 0 means "use the terrain shader's normal/default material path".
    // Do not synthesize grass/mud/dirt/sand here for unpainted faces.
    //
    // Texture-paint material rebakes rebuild whole chunks. If every unpainted
    // face receives a packed material word, partially-brush-touched chunks get
    // their default terrain material replaced too, which is the visible
    // "textures around the brush changed" bug. Painted faces still return a
    // packed material from sampleFaceMaterial(); unpainted faces must stay 0 so
    // the baked mesh renders exactly like normal terrain outside the brush.
    return 0u;
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

## src\world\edit\TerrainEditRemeshScheduler.cpp

Description: No CC-DESC found. C++ struct 'EdgeDef'.

````cpp
#include "world/edit/TerrainEditRemeshScheduler.h"
#include "world/edit/TerrainEditMesher.h"
#include "world/edit/TerrainEditDCCMMesher.h"
#include "world/edit/TerrainFieldSource.h"
#include "world/edit/HeightmapBaseSampler.h"
#include "world/edit/VoxelBaseSampler.h"
#include "world/ChunkHoleTracker.h"
#include "world/World.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/Chunk.h"
#include "world/chunks/core/ChunkJobs.h"
#include "core/Jobs.h"
#include <algorithm>
#include <iostream>
#include <chrono>
#include <map>

namespace TerrainEdit {

namespace {

uint8_t computeDCCMCasingMask(World* world, const glm::ivec3& chunkCoord,
                              const glm::ivec3& center)
{
    if (!world || !world->getChunkManager()) {
        return 0;
    }

    static const glm::ivec3 neighborOffsets[4] = {
        {-1,0,0}, {1,0,0}, {0,0,-1}, {0,0,1}
    };

    uint8_t casingMask = 0;
    for (int edge = 0; edge < 4; ++edge) {
        const glm::ivec3 neighbor = chunkCoord + neighborOffsets[edge];
        const int neighborRing = world->getChunkManager()->calculateRingNumber(neighbor, center);
        const int neighborLOD = world->getChunkManager()->calculateLODFromRing(neighborRing);
        if (world->getTerrainTypeForChunk(neighbor, neighborLOD) != TerrainType::DCCM) {
            casingMask |= (1 << edge);
        }
    }

    return casingMask;
}

void generateDCCMCasingForEdge(int edge,
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
        for (const Vertex& vertex : mainSubChunks[subIndex].vertices) {
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

    auto packVert = [](uint8_t x, uint16_t y, uint8_t z, uint8_t face) -> uint32_t {
        return uint32_t(x)
             | (uint32_t(y)        << 8)
             | (uint32_t(z)        << 18)
             | (uint32_t(face & 7) << 26);
    };

    constexpr uint16_t Y_BASE = 1;
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
        outCasing.vertices.push_back({packVert(x0, Y_BASE, z0, FACE_DCCM_SURFACE)});
        outCasing.vertices.push_back({packVert(x1, Y_BASE, z1, FACE_DCCM_SURFACE)});
        outCasing.vertices.push_back({packVert(x1, h1, z1, FACE_DCCM_SURFACE)});
        outCasing.vertices.push_back({packVert(x0, h0, z0, FACE_DCCM_SURFACE)});

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

bool computeTightAABBFromSubChunks(const std::vector<MeshData>& subChunks,
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

void releaseEditRemeshInFlight(const std::shared_ptr<ChunkVersionState>& versionState,
                               uint32_t version)
{
    if (!versionState) {
        return;
    }

    const uint32_t prev = versionState->editRemeshInFlightCount.fetch_sub(
        1, std::memory_order_acq_rel);
    const uint32_t remaining = (prev > 0) ? (prev - 1) : 0;

    // Only clear the shared inFlight flag when the edit scheduler is still the
    // owner of the latest version token. If another pipeline already claimed a
    // newer version, leave inFlight alone so we do not stomp its ownership.
    const uint32_t currentVersion = versionState->version.load(std::memory_order_acquire);
    const uint32_t latestEditVersion =
        versionState->editRemeshLatestVersion.load(std::memory_order_acquire);
    if (remaining == 0 &&
        currentVersion == latestEditVersion &&
        currentVersion >= version) {
        versionState->inFlight.store(false, std::memory_order_release);
    }
}

constexpr uint16_t kChunkEditPagesPerAxis =
    static_cast<uint16_t>(WorldConfig::CHUNK_SIZE / ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS);
constexpr uint16_t kChunkEditPageCount = kChunkEditPagesPerAxis * kChunkEditPagesPerAxis;
constexpr uint16_t kChunkVoxelHeight =
    static_cast<uint16_t>(WorldConfig::CHUNK_HEIGHT);

void ensurePagedRuntimeScaffold(ChunkEditRuntime& editRuntime)
{
    editRuntime.targetMode = ChunkMeshMode::PagedEditable;
    if (!editRuntime.pages.empty()) {
        return;
    }

    editRuntime.pages.reserve(kChunkEditPageCount);
    for (uint16_t pageZ = 0; pageZ < kChunkEditPagesPerAxis; ++pageZ) {
        for (uint16_t pageX = 0; pageX < kChunkEditPagesPerAxis; ++pageX) {
            ChunkEditPageRuntime page{};
            page.pageId = static_cast<uint16_t>(pageZ * kChunkEditPagesPerAxis + pageX);
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

uint8_t decodePackedX(const Vertex& vertex)
{
    return static_cast<uint8_t>(vertex.packed & 0xFFu);
}

uint16_t decodePackedY(const Vertex& vertex)
{
    return static_cast<uint16_t>((vertex.packed >> 8) & 0x3FFu);
}

uint8_t decodePackedZ(const Vertex& vertex)
{
    return static_cast<uint8_t>((vertex.packed >> 18) & 0xFFu);
}

void offsetPackedVerticesXZ(std::vector<Vertex>& vertices, uint8_t offsetX, uint8_t offsetZ)
{
    if (offsetX == 0 && offsetZ == 0) {
        return;
    }

    for (auto& vertex : vertices) {
        const uint32_t packed = vertex.packed;
        const uint32_t x = (packed & 0xFFu) + offsetX;
        const uint32_t z = ((packed >> 18) & 0xFFu) + offsetZ;
        vertex.packed = (packed & ~((0xFFu << 0) | (0xFFu << 18)))
            | ((x & 0xFFu) << 0)
            | ((z & 0xFFu) << 18);
    }
}

bool computeAABBFromVertices(const std::vector<Vertex>& vertices,
                             glm::vec3& outMin,
                             glm::vec3& outMax)
{
    if (vertices.empty()) {
        outMin = glm::vec3(1e10f);
        outMax = glm::vec3(-1e10f);
        return false;
    }

    constexpr float kVoxelSize = WorldConfig::VOXEL_SIZE_M;
    outMin = glm::vec3(1e10f);
    outMax = glm::vec3(-1e10f);
    for (const auto& vertex : vertices) {
        const glm::vec3 pos(
            static_cast<float>(decodePackedX(vertex)) * kVoxelSize,
            static_cast<float>(decodePackedY(vertex)) * kVoxelSize,
            static_cast<float>(decodePackedZ(vertex)) * kVoxelSize);
        outMin = glm::min(outMin, pos);
        outMax = glm::max(outMax, pos);
    }

    const float pad = kVoxelSize * 0.5f;
    outMin -= glm::vec3(pad);
    outMax += glm::vec3(pad);
    return true;
}

uint16_t pageIdFromTriangle(const Vertex& a, const Vertex& b, const Vertex& c)
{
    const float cx =
        (static_cast<float>(decodePackedX(a)) +
         static_cast<float>(decodePackedX(b)) +
         static_cast<float>(decodePackedX(c))) / 3.0f;
    const float cz =
        (static_cast<float>(decodePackedZ(a)) +
         static_cast<float>(decodePackedZ(b)) +
         static_cast<float>(decodePackedZ(c))) / 3.0f;
    const uint16_t pageX = static_cast<uint16_t>(std::clamp<int>(static_cast<int>(cx) / ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS, 0, kChunkEditPagesPerAxis - 1));
    const uint16_t pageZ = static_cast<uint16_t>(std::clamp<int>(static_cast<int>(cz) / ChunkEditRuntime::PAGE_SIZE_XZ_VOXELS, 0, kChunkEditPagesPerAxis - 1));
    return static_cast<uint16_t>(pageZ * kChunkEditPagesPerAxis + pageX);
}

void storePageCpuMesh(ChunkEditPageRuntime& page,
                      std::vector<Vertex>&& vertices,
                      std::vector<uint16_t>&& indices)
{
    page.cpuVertices = std::move(vertices);
    page.cpuIndices = std::move(indices);
    if (!page.cpuVertices.empty() && !page.cpuIndices.empty()) {
        computeAABBFromVertices(page.cpuVertices, page.localAabbMin, page.localAabbMax);
        uint16_t minY = kChunkVoxelHeight - 1;
        uint16_t maxY = 0;
        for (const auto& vertex : page.cpuVertices) {
            const uint16_t y = decodePackedY(vertex);
            minY = std::min<uint16_t>(minY, y);
            maxY = std::max<uint16_t>(maxY, y);
        }
        page.bounds.minY = minY;
        page.bounds.maxY = maxY;
        page.resident = true;
    } else {
        page.cpuVertices.clear();
        page.cpuIndices.clear();
        page.localAabbMin = glm::vec3(1e10f);
        page.localAabbMax = glm::vec3(-1e10f);
        page.resident = false;
    }
}

void clearPageCpuMesh(ChunkEditPageRuntime& page)
{
    page.cpuVertices.clear();
    page.cpuIndices.clear();
    page.localAabbMin = glm::vec3(1e10f);
    page.localAabbMax = glm::vec3(-1e10f);
    page.resident = false;
}

void partitionMergedMeshIntoPages(const std::vector<Vertex>& vertices,
                                  const std::vector<uint32_t>& indices,
                                  ChunkEditRuntime& editRuntime)
{
    struct PageBuilder {
        std::vector<Vertex> vertices;
        std::vector<uint16_t> indices;
        std::unordered_map<uint32_t, uint16_t> remap;
    };

    std::array<PageBuilder, kChunkEditPageCount> builders;
    for (size_t i = 0; i + 2 < indices.size(); i += 3) {
        const uint32_t i0 = indices[i + 0];
        const uint32_t i1 = indices[i + 1];
        const uint32_t i2 = indices[i + 2];
        if (i0 >= vertices.size() || i1 >= vertices.size() || i2 >= vertices.size()) {
            continue;
        }

        const uint16_t pageId = pageIdFromTriangle(vertices[i0], vertices[i1], vertices[i2]);
        auto& builder = builders[pageId];

        auto addVertex = [&](uint32_t sourceIndex) -> uint16_t {
            auto it = builder.remap.find(sourceIndex);
            if (it != builder.remap.end()) {
                return it->second;
            }

            const uint16_t newIndex = static_cast<uint16_t>(builder.vertices.size());
            builder.vertices.push_back(vertices[sourceIndex]);
            builder.remap.emplace(sourceIndex, newIndex);
            return newIndex;
        };

        builder.indices.push_back(addVertex(i0));
        builder.indices.push_back(addVertex(i1));
        builder.indices.push_back(addVertex(i2));
    }

    for (uint16_t pageId = 0; pageId < editRuntime.pages.size(); ++pageId) {
        auto& page = editRuntime.pages[pageId];
        auto& builder = builders[pageId];
        if (!builder.vertices.empty() && !builder.indices.empty()) {
            storePageCpuMesh(page, std::move(builder.vertices), std::move(builder.indices));
        } else {
            clearPageCpuMesh(page);
        }
        page.dirtyData = false;
        page.dirtyMesh = false;
        page.uploadPending = false;
    }
}

void mergeSubChunksToIndexedMesh(const std::vector<SubChunkMesh>& subChunks,
                                 std::vector<Vertex>& outVertices,
                                 std::vector<uint32_t>& outIndices)
{
    outVertices.clear();
    outIndices.clear();

    for (const auto& subChunk : subChunks) {
        if (subChunk.isEmpty()) {
            continue;
        }

        const uint32_t baseIndex = static_cast<uint32_t>(outVertices.size());
        outVertices.reserve(outVertices.size() + subChunk.vertices.size());
        for (uint32_t packedVertex : subChunk.vertices) {
            outVertices.push_back(Vertex{packedVertex});
        }
        outIndices.reserve(outIndices.size() + subChunk.indices.size());
        for (uint16_t index : subChunk.indices) {
            outIndices.push_back(baseIndex + index);
        }
    }
}

TerrainEditRemeshScheduler::CachedArtifact combinePagedArtifact(
    const ChunkEditRuntime& editRuntime,
    TerrainType terrainType,
    int lodLevel)
{
    TerrainEditRemeshScheduler::CachedArtifact artifact;
    artifact.terrainType = terrainType;
    artifact.lodLevel = lodLevel;
    artifact.isEmpty = true;

    for (const auto& page : editRuntime.pages) {
        if (!page.hasCpuMesh()) {
            continue;
        }

        const uint32_t baseIndex = static_cast<uint32_t>(artifact.vertices.size());
        artifact.vertices.insert(
            artifact.vertices.end(),
            page.cpuVertices.begin(),
            page.cpuVertices.end());
        artifact.indices.reserve(artifact.indices.size() + page.cpuIndices.size());
        for (uint16_t index : page.cpuIndices) {
            artifact.indices.push_back(baseIndex + index);
        }
        artifact.aabbMin = glm::min(artifact.aabbMin, page.localAabbMin);
        artifact.aabbMax = glm::max(artifact.aabbMax, page.localAabbMax);
        artifact.isEmpty = false;
    }

    if (artifact.isEmpty) {
        artifact.aabbMin = glm::vec3(1e10f);
        artifact.aabbMax = glm::vec3(-1e10f);
    }

    return artifact;
}

void applyDirtyPageUpdate(ChunkEditRuntime& editRuntime, const DirtyChunkPages& dirtyPages)
{
    ensurePagedRuntimeScaffold(editRuntime);
    editRuntime.targetMode = ChunkMeshMode::PagedEditable;
    editRuntime.needsPromotion = true;
    editRuntime.needsTopologyRebuild = true;
    ++editRuntime.dataGeneration;

    const size_t pageCount = std::min(dirtyPages.pageIds.size(), dirtyPages.pageBounds.size());
    for (size_t i = 0; i < pageCount; ++i) {
        const uint16_t pageId = dirtyPages.pageIds[i];
        if (pageId >= editRuntime.pages.size()) {
            continue;
        }

        auto& page = editRuntime.pages[pageId];
        const auto& src = dirtyPages.pageBounds[i];
        if (page.dataGeneration == 0 && !page.resident && !page.dirtyData && !page.dirtyMesh) {
            page.bounds.minY = std::min<uint16_t>(src.minY, kChunkVoxelHeight - 1);
            page.bounds.maxY = std::min<uint16_t>(src.maxY, kChunkVoxelHeight - 1);
        } else {
            page.bounds.minY = std::min<uint16_t>(page.bounds.minY, std::min<uint16_t>(src.minY, kChunkVoxelHeight - 1));
            page.bounds.maxY = std::max<uint16_t>(page.bounds.maxY, std::min<uint16_t>(src.maxY, kChunkVoxelHeight - 1));
        }

        page.dataGeneration = editRuntime.dataGeneration;
        page.dirtyData = true;
        page.dirtyMesh = true;
        page.uploadPending = false;
        if (std::find(editRuntime.dirtyPageIds.begin(), editRuntime.dirtyPageIds.end(), pageId) ==
            editRuntime.dirtyPageIds.end()) {
            editRuntime.dirtyPageIds.push_back(pageId);
        }
    }
}

uint16_t countResidentPages(const ChunkEditRuntime& editRuntime)
{
    uint16_t resident = 0;
    for (const auto& page : editRuntime.pages) {
        if (page.resident) {
            ++resident;
        }
    }
    return resident;
}

bool shouldExposePagedEditableMode(TerrainType terrainType,
                                   int lodLevel,
                                   const ChunkEditRuntime* editRuntime = nullptr)
{
    (void)editRuntime;
    if (terrainType != TerrainType::Voxel || lodLevel != 0) {
        return false;
    }

    // Cross-LOD edits may leave a chunk carrying a stale monolithic target
    // from a coarser data LOD. Once we are back at full voxel LOD, that old
    // target must not block promotion into the paged editable path, or the
    // next full-res edit can punch temporary holes until enough nearby edits
    // rebuild the missing local pages.
    return true;
}

void fillPagedDebugInfo(const ChunkEditRuntime& editRuntime, ChunkDebugAttribution& debugInfo)
{
    debugInfo.workModel = ChunkWorkModel::PagedLocal;
    debugInfo.meshMode = static_cast<uint8_t>(ChunkMeshMode::PagedEditable);
    debugInfo.dirtyPages = static_cast<uint16_t>(
        std::min<size_t>(editRuntime.dirtyPageIds.size(), UINT16_MAX));
    debugInfo.residentPages = countResidentPages(editRuntime);
}

void finalizePagedRuntime(ChunkEditRuntime& editRuntime, ChunkDebugAttribution& debugInfo)
{
    fillPagedDebugInfo(editRuntime, debugInfo);
    debugInfo.rebuiltPages = debugInfo.dirtyPages;

    if (debugInfo.dirtyPages == 0) {
        return;
    }

    ++editRuntime.meshGeneration;
    for (const uint16_t pageId : editRuntime.dirtyPageIds) {
        if (pageId >= editRuntime.pages.size()) {
            continue;
        }
        auto& page = editRuntime.pages[pageId];
        page.meshGeneration = editRuntime.meshGeneration;
        page.dirtyData = false;
        page.dirtyMesh = false;
        page.uploadPending = false;
        page.resident = page.hasCpuMesh();
    }
    editRuntime.dirtyPageIds.clear();
    editRuntime.needsPromotion = false;
    debugInfo.residentPages = countResidentPages(editRuntime);
}

void mergePendingDirtyPages(DirtyChunkPages& dst, const DirtyChunkPages& src)
{
    const size_t pageCount = std::min(src.pageIds.size(), src.pageBounds.size());
    for (size_t i = 0; i < pageCount; ++i) {
        const uint16_t pageId = src.pageIds[i];
        const auto existing = std::find(dst.pageIds.begin(), dst.pageIds.end(), pageId);
        if (existing == dst.pageIds.end()) {
            dst.pageIds.push_back(pageId);
            dst.pageBounds.push_back(src.pageBounds[i]);
            continue;
        }

        const size_t existingIdx = static_cast<size_t>(existing - dst.pageIds.begin());
        auto& bounds = dst.pageBounds[existingIdx];
        bounds.minY = std::min(bounds.minY, src.pageBounds[i].minY);
        bounds.maxY = std::max(bounds.maxY, src.pageBounds[i].maxY);
    }
    dst.directPages = static_cast<uint16_t>(
        std::min<size_t>(dst.pageIds.size(), UINT16_MAX));
    dst.haloPages = src.haloPages;
}

} // namespace

// ---------------------------------------------------------------------------
// markChunksDirty — thread-safe
// ---------------------------------------------------------------------------

void TerrainEditRemeshScheduler::markChunksDirty(
    const std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal>& chunks,
    const DirtyChunkPageMap* dirtyChunkPages)
{
    std::lock_guard lock(m_mutex);
    m_dirty.insert(chunks.begin(), chunks.end());
    if (!dirtyChunkPages) {
        return;
    }

    for (const auto& [coord, pages] : *dirtyChunkPages) {
        mergePendingDirtyPages(m_pendingDirtyPages[coord], pages);
    }
}

void TerrainEditRemeshScheduler::markMaterialChunksDirty(
    const std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal>& chunks)
{
    if (chunks.empty()) {
        return;
    }

    std::lock_guard lock(m_mutex);
    m_materialDirty.insert(chunks.begin(), chunks.end());
}


size_t TerrainEditRemeshScheduler::pendingCount() const {
    std::lock_guard lock(m_mutex);
    return m_dirty.size()
         + m_materialDirty.size()
         + m_qualityDirty.size()
         + m_inFlightCount.load(std::memory_order_relaxed);
}

void TerrainEditRemeshScheduler::pushCompletion(CompletedRemesh&& c) {
    std::lock_guard lock(m_completionMutex);
    m_completions.push_back(std::move(c));
}

bool TerrainEditRemeshScheduler::consumeChunkTiming(const glm::ivec3& coord, ChunkTimingRecord& out) {
    std::lock_guard lock(m_timingMutex);
    auto it = m_chunkTimings.find(coord);
    if (it == m_chunkTimings.end()) return false;
    out = it->second;
    m_chunkTimings.erase(it);
    return true;
}

// ---------------------------------------------------------------------------
// Background job payload & function
// ---------------------------------------------------------------------------

struct RemeshJobPayload {
    TerrainEditRemeshScheduler* scheduler;
    entt::entity    entity;
    glm::ivec3      chunkCoord;
    glm::ivec3      centerAtEnqueue;
    std::shared_ptr<ChunkVersionState> versionState;
    uint32_t        version;
    int             currentLodLevel;
    TerrainType     currentTerrainType;
    bool            currentUsesDCCM;
    int             hintMinY;
    int             hintMaxY;
    std::vector<TerrainEditRemeshScheduler::CachedArtifact> deferredTargets;

    // Pointers to engine data (immutable or internally-synchronised)
    const TerrainFieldSource*       fieldSource;
    const HeightmapBaseSampler*     heightmap;
    const VoxelBaseSampler*         voxelBase{nullptr};
    const TerrainEditOverlayStore*  overlay;
    bool                            fastMode{false};
    bool                            materialOnly{false};
    std::chrono::steady_clock::time_point dispatchTime{};
    TerrainEditRemeshScheduler::LoadManagementSnapshot loadSnapshot{};
    // Tier A.1 — union of dirty page voxel AABBs for this chunk. Used by the
    // mesher to do a region-only LOD downsample instead of full chunk. Sentinel
    // hasEditDirtyAabb=false → mesher does full downsample (current behaviour).
    bool                            hasEditDirtyAabb{false};
    glm::ivec3                      editDirtyVoxelMin{0};
    glm::ivec3                      editDirtyVoxelMax{0};
    // Tier B Phase 1 scaffolding — chunk-local Y band derived from edit AABB.
    // -1/-1 = full chunk (no band). Plumbed but not yet acted on by the mesher.
    int                             bandLocalYMin{-1};
    int                             bandLocalYMax{-1};
};

static TerrainEditRemeshScheduler::CachedArtifact buildArtifact(
    const TerrainFieldSource& fieldSource,
    const HeightmapBaseSampler& heightmap,
    const VoxelBaseSampler* voxelBase,
    const TerrainEditOverlayStore* overlay,
    const glm::ivec3& chunkCoord,
    int hintMinY,
    int hintMaxY,
    TerrainType terrainType,
    int lodLevel,
    bool useDCCM,
    bool fastMode = false,
    const RemeshCancellationToken* cancelToken = nullptr,
    const glm::ivec3* editDirtyVoxelMin = nullptr,
    const glm::ivec3* editDirtyVoxelMax = nullptr,
    int bandLocalYMin = -1,
    int bandLocalYMax = -1)
{
    TerrainEditRemeshScheduler::CachedArtifact artifact;
    artifact.terrainType = terrainType;
    artifact.lodLevel = lodLevel;

    if (useDCCM) {
        auto dccm = TerrainEditDCCMMesher::meshChunk(
            heightmap, nullptr,
            chunkCoord.x, chunkCoord.z,
            WorldConfig::CHUNK_SIZE,
            WorldConfig::CHUNK_HEIGHT,
            lodLevel);
        artifact.vertices = std::move(dccm.vertices);
        artifact.indices.assign(dccm.indices.begin(), dccm.indices.end());
        artifact.aabbMin  = dccm.aabbMin;
        artifact.aabbMax  = dccm.aabbMax;
    } else {
        auto voxel = TerrainEditMesher::meshChunk(
            fieldSource, chunkCoord,
            WorldConfig::CHUNK_SIZE,
            WorldConfig::CHUNK_HEIGHT,
            WorldConfig::VOXEL_SIZE_M,
            hintMinY, hintMaxY,
            heightmap.isLoaded() ? &heightmap : nullptr,
            lodLevel,
            /*skipPostProcess=*/fastMode,
            /*skipAmbientOcclusion=*/false,
            cancelToken,
            voxelBase && voxelBase->isLoaded() ? voxelBase : nullptr,
            editDirtyVoxelMin,
            editDirtyVoxelMax,
            bandLocalYMin,
            bandLocalYMax);
        artifact.vertices = std::move(voxel.vertices);
        artifact.indices  = std::move(voxel.indices);
        artifact.aabbMin  = voxel.aabbMin;
        artifact.aabbMax  = voxel.aabbMax;
        artifact.meshStats = voxel.stats;
    }

    artifact.isEmpty = artifact.vertices.empty() || artifact.indices.empty();
    return artifact;
}

static void editRemeshJobFn(JobCtx& /*ctx*/, void* ud) {
    auto* p = static_cast<RemeshJobPayload*>(ud);

    const auto jobStartTime = std::chrono::steady_clock::now();

    // Bail if version was bumped (new edit arrived before we ran).
    if (p->versionState->version.load(std::memory_order_acquire) != p->version) {
        releaseEditRemeshInFlight(p->versionState, p->version);
        p->scheduler->m_inFlightCount.fetch_sub(1, std::memory_order_relaxed);
        delete p;
        return;
    }

    TerrainEditRemeshScheduler::CompletedRemesh comp;
    comp.entity       = p->entity;
    comp.chunkCoord   = p->chunkCoord;
    comp.centerAtEnqueue = p->centerAtEnqueue;
    comp.dispatchTime = p->dispatchTime;
    comp.materialOnly = p->materialOnly;
    comp.jobStartTime = jobStartTime;
    const RemeshCancellationToken cancelToken{&p->versionState->version, p->version};
    comp.currentArtifact = buildArtifact(
        *p->fieldSource,
        *p->heightmap,
        p->voxelBase,
        p->overlay,
        p->chunkCoord,
        p->hintMinY,
        p->hintMaxY,
        p->currentTerrainType,
        p->currentLodLevel,
        p->currentUsesDCCM,
        /*fastMode=*/p->fastMode,
        /*cancelToken=*/&cancelToken,
        p->hasEditDirtyAabb ? &p->editDirtyVoxelMin : nullptr,
        p->hasEditDirtyAabb ? &p->editDirtyVoxelMax : nullptr,
        p->bandLocalYMin,
        p->bandLocalYMax);

    // A newer edit arrived while we were meshing. Drop the obsolete artifact
    // here so it never enters collision/upload/finalize queues.
    if (p->versionState->version.load(std::memory_order_acquire) != p->version) {
        releaseEditRemeshInFlight(p->versionState, p->version);
        p->scheduler->m_inFlightCount.fetch_sub(1, std::memory_order_relaxed);
        delete p;
        return;
    }

    if (!p->materialOnly) {
        comp.collVerts   = comp.currentArtifact.vertices;
        comp.collIndices = comp.currentArtifact.indices;
    }

    // Push the primary mesh immediately so the visual update is not
    // blocked by deferred LOD variant generation.
    comp.versionState = p->versionState;
    comp.version      = p->version;
    comp.isFastMode   = p->fastMode;
    comp.meshDoneTime = std::chrono::steady_clock::now();
    comp.loadSnapshot = p->loadSnapshot;
    p->scheduler->pushCompletion(std::move(comp));

    // Generate deferred LOD/terrain-type variants AFTER pushing the
    // primary completion.
    //
    // Previously this loop ran *synchronously inside the same worker job*,
    // adding ~50 ms × N (typically 3) of cache-rebuild work onto every fast
    // edit job. Under brush spam that single change could turn a 55 ms job
    // into a 200 ms job, saturating the worker pool and producing the
    // 2.5 – 3 s `queue` stalls visible in the chunk visual history.
    //
    // New behavior: push each deferred target into a scheduler-owned queue.
    // `dispatchPendingDeferredLODs()` (called from processRemeshQueue once the
    // brush has been idle for ~200 ms) drains it as separate low-priority
    // worker jobs. This lets active fast edits always pass through workers
    // unobstructed, while the LOD prewarm still happens — just slightly
    // later, when no one is looking.
    if (!p->deferredTargets.empty()) {
        std::vector<TerrainEditRemeshScheduler::PendingDeferredLOD> requests;
        requests.reserve(p->deferredTargets.size());
        for (const auto& deferred : p->deferredTargets) {
            TerrainEditRemeshScheduler::PendingDeferredLOD req;
            req.chunkCoord = p->chunkCoord;
            req.terrainType = deferred.terrainType;
            req.lodLevel = deferred.lodLevel;
            req.hintMinY = p->hintMinY;
            req.hintMaxY = p->hintMaxY;
            req.versionState = p->versionState;
            req.version = p->version;
            req.hasEditDirtyAabb = p->hasEditDirtyAabb;
            req.editDirtyVoxelMin = p->editDirtyVoxelMin;
            req.editDirtyVoxelMax = p->editDirtyVoxelMax;
            requests.push_back(std::move(req));
        }
        std::lock_guard lock(p->scheduler->m_pendingDeferredLODMutex);
        for (auto& req : requests) {
            p->scheduler->m_pendingDeferredLODs.push_back(std::move(req));
        }
    }
    p->scheduler->m_inFlightCount.fetch_sub(1, std::memory_order_relaxed);

    delete p;
}

void TerrainEditRemeshScheduler::pushDeferredArtifact(DeferredArtifactResult&& r) {
    std::lock_guard lock(m_deferredArtifactMutex);
    m_deferredArtifacts.push_back(std::move(r));
}

// ---------------------------------------------------------------------------
// drainCompletions — main thread: enqueue uploads + collision
// ---------------------------------------------------------------------------

size_t TerrainEditRemeshScheduler::drainCompletions(World* world) {
    std::vector<CompletedRemesh> batch;
    {
        std::lock_guard lock(m_completionMutex);
        if (m_completions.empty()) return 0;
        batch.swap(m_completions);
    }

    auto& registry = world->getRegistry();
    auto& diag = world->editDiagMut();

    using Clock = std::chrono::high_resolution_clock;
    float collAccum = 0.0f;
    float gpuAccum  = 0.0f;
    size_t uploadsQueued = 0;

    for (auto& comp : batch) {
        // Stale check (entity destroyed or version bumped since dispatch)
        if (comp.versionState->version.load(std::memory_order_acquire) != comp.version) {
            releaseEditRemeshInFlight(comp.versionState, comp.version);
            continue;
        }

        {
            std::shared_lock regLock(world->registryMutex());
            if (!registry.valid(comp.entity)) {
                releaseEditRemeshInFlight(comp.versionState, comp.version);
                continue;
            }
        }

        // Record per-chunk timing for pipeline breakdown diagnostics
        {
            ChunkTimingRecord timing;
            timing.dispatchTime = comp.dispatchTime;
            timing.jobStartTime = comp.jobStartTime;
            timing.meshDoneTime = comp.meshDoneTime;
            timing.drainTime    = std::chrono::steady_clock::now();
            timing.isFastMode   = comp.isFastMode;
            timing.meshLodLevel = comp.currentArtifact.lodLevel;
            timing.meshStats    = comp.currentArtifact.meshStats;
            timing.loadSnapshot = comp.loadSnapshot;
            std::lock_guard tlock(m_timingMutex);
            m_chunkTimings[comp.chunkCoord] = timing;
        }

        world->storeEditArtifact(
            comp.chunkCoord,
            comp.currentArtifact.terrainType,
            comp.currentArtifact.lodLevel,
            std::vector<Vertex>(comp.currentArtifact.vertices.begin(), comp.currentArtifact.vertices.end()),
            std::vector<uint32_t>(comp.currentArtifact.indices.begin(), comp.currentArtifact.indices.end()),
            comp.currentArtifact.aabbMin,
            comp.currentArtifact.aabbMax,
            comp.currentArtifact.isEmpty);

        const bool uploadFromTerrainEdit = !comp.materialOnly;

        ChunkDebugAttribution debugInfo{};
        debugInfo.artifactSource = ChunkArtifactSource::RuntimeEditBuild;
        debugInfo.collisionSource = comp.materialOnly
            ? ChunkCollisionSource::None
            : ChunkCollisionSource::EditMeshPacked;
        debugInfo.workModel = ChunkWorkModel::MonolithicChunk;
        debugInfo.meshMode = static_cast<uint8_t>(ChunkMeshMode::MonolithicEdited);
        debugInfo.artifactCacheResident = true;
        debugInfo.fromTerrainEdit = uploadFromTerrainEdit;
        debugInfo.artifactGeneration = world->getEditArtifactGeneration(
            comp.chunkCoord,
            comp.currentArtifact.terrainType,
            comp.currentArtifact.lodLevel);
        const glm::ivec3 maskCenter = world->getChunkManager()
            ? world->getChunkManager()->getCenterChunk()
            : comp.centerAtEnqueue;

        if (comp.currentArtifact.isEmpty) {
            {
                std::unique_lock regLock(world->registryMutex());
                if (registry.valid(comp.entity) && registry.all_of<Chunk>(comp.entity)) {
                    auto& chunk = registry.get<Chunk>(comp.entity);
                    const int chunkLodLevel = chunk.lodLevel;
                    chunk.isEmpty = true;
                    if (registry.any_of<ChunkEditRuntime>(comp.entity)) {
                        auto& editRuntime = registry.get<ChunkEditRuntime>(comp.entity);
                        if (shouldExposePagedEditableMode(
                                comp.currentArtifact.terrainType,
                                comp.currentArtifact.lodLevel,
                                &editRuntime)) {
                            ensurePagedRuntimeScaffold(editRuntime);
                            partitionMergedMeshIntoPages(
                                comp.currentArtifact.vertices,
                                comp.currentArtifact.indices,
                                editRuntime);
                            finalizePagedRuntime(editRuntime, debugInfo);
                            chunk.meshMode = ChunkMeshMode::PagedEditable;
                        } else {
                            chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                        }
                    } else {
                        chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                    }
                    // Keep metadata aligned to the chunk's logical LOD band, not the
                    // artifact data LOD. Using artifact LOD here causes periodic LOD
                    // scans to see false mismatches and enqueue endless remesh/swap work.
                    chunk.effectiveDataLod = static_cast<uint8_t>(
                        std::clamp(world->getEffectiveLODForChunk(
                            comp.chunkCoord, chunkLodLevel), 0, 255));
                    if (comp.currentArtifact.terrainType == TerrainType::DCCM) {
                        chunk.voxelSeamMask = 0;
                        chunk.casingSeamMask = computeDCCMCasingMask(
                            world, comp.chunkCoord, maskCenter);
                    } else {
                        chunk.casingSeamMask = 0;
                        // Even empty chunks must carry the expected seam metadata for
                        // reconciliation logic; otherwise they get re-queued forever.
                        chunk.voxelSeamMask = (chunkLodLevel > 0 && world->getChunkManager())
                            ? world->getChunkManager()->getSeamEdgeMask(
                                comp.chunkCoord, maskCenter)
                            : 0;
                    }
                }
            }
            if (!comp.materialOnly) {
                auto t0 = Clock::now();
                world->enqueueEditCollision(
                    comp.entity,
                    comp.chunkCoord,
                    {},
                    {},
                    comp.versionState,
                    comp.version,
                    ChunkCollisionSource::EditMeshPacked);
                collAccum += std::chrono::duration<float, std::milli>(Clock::now() - t0).count();
            }

            {
                auto t0Upload = Clock::now();
                world->enqueueMeshForUpload(
                    comp.entity,
                    std::vector<MeshData>{},
                    /*mainSubChunkCount=*/0,
                    /*fromTerrainEdit=*/uploadFromTerrainEdit,
                    comp.versionState,
                    comp.version,
                    comp.currentArtifact.aabbMin,
                    comp.currentArtifact.aabbMax,
                    /*hasTight=*/false,
                    /*isRemesh=*/false,
                    /*batchId=*/0,
                    debugInfo);
                gpuAccum += std::chrono::duration<float, std::milli>(Clock::now() - t0Upload).count();
            }

            releaseEditRemeshInFlight(comp.versionState, comp.version);

            // Record edit remesh completion for chunk hole tracking
            {
                ChunkHoleEvent ev;
                ev.type = ChunkHoleEvent::Type::EditRemeshCompleted;
                ev.timestampSec = std::chrono::duration<float>(
                    std::chrono::steady_clock::now().time_since_epoch()).count();
                ev.detail = "empty_artifact";
                world->getChunkHoleTracker().recordEvent(
                    ChunkCoord{comp.chunkCoord.x, comp.chunkCoord.y, comp.chunkCoord.z},
                    std::move(ev));
            }
            if (comp.isFastMode) {
                std::lock_guard qlock(m_mutex);
                m_qualityDirty.insert(ChunkCoord{comp.chunkCoord.x, comp.chunkCoord.y, comp.chunkCoord.z});
            }
            ++diag.chunksRemeshed;
            continue;
        }

        // Touch chunk component
        {
            std::unique_lock regLock(world->registryMutex());
            if (registry.valid(comp.entity) && registry.all_of<Chunk>(comp.entity)) {
                auto& chunk = registry.get<Chunk>(comp.entity);
                const int chunkLodLevel = chunk.lodLevel;
                chunk.isEmpty = false;
                if (registry.any_of<ChunkEditRuntime>(comp.entity)) {
                    auto& editRuntime = registry.get<ChunkEditRuntime>(comp.entity);
                    if (shouldExposePagedEditableMode(
                            comp.currentArtifact.terrainType,
                            comp.currentArtifact.lodLevel,
                            &editRuntime)) {
                        ensurePagedRuntimeScaffold(editRuntime);
                        partitionMergedMeshIntoPages(
                            comp.currentArtifact.vertices,
                            comp.currentArtifact.indices,
                            editRuntime);
                        finalizePagedRuntime(editRuntime, debugInfo);
                        chunk.meshMode = ChunkMeshMode::PagedEditable;
                    } else {
                        chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                    }
                } else {
                    chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                }
                // Keep data-LOD metadata tied to the chunk's render LOD band.
                chunk.effectiveDataLod = static_cast<uint8_t>(
                    std::clamp(world->getEffectiveLODForChunk(
                        comp.chunkCoord, chunkLodLevel), 0, 255));
                if (comp.currentArtifact.terrainType == TerrainType::DCCM) {
                    chunk.voxelSeamMask = 0;
                    chunk.casingSeamMask = computeDCCMCasingMask(world, comp.chunkCoord, maskCenter);
                } else {
                    chunk.casingSeamMask = 0;
                    // Match the value the LOD scan compares against, otherwise the
                    // scan re-queues this chunk every frame -> RELOAD STORM / flicker.
                    chunk.voxelSeamMask = (chunkLodLevel > 0 && world->getChunkManager())
                        ? world->getChunkManager()->getSeamEdgeMask(comp.chunkCoord, maskCenter)
                        : 0;
                }
            }
        }

        // Collision — count diagnostic sizes BEFORE std::move empties the vectors.
        ++diag.chunksRemeshed;
        if (!comp.materialOnly) {
            diag.vertexCount += static_cast<uint32_t>(comp.collVerts.size());
            diag.indexCount  += static_cast<uint32_t>(comp.collIndices.size());
        }

        if (!comp.materialOnly) {
            auto t0 = Clock::now();
            world->enqueueEditCollision(comp.entity, comp.chunkCoord,
                                        std::move(comp.collVerts),
                                        std::move(comp.collIndices),
                                        comp.versionState,
                                        comp.version,
                                        ChunkCollisionSource::EditMeshPacked);
            collAccum += std::chrono::duration<float, std::milli>(Clock::now() - t0).count();
        }

        // GPU upload — split into sub-meshes if vertex count exceeds 65535
        std::vector<MeshData> subChunks;
        uint8_t mainSubChunkCount = 0;
        {
            // Build a temporary MeshResult for the split utility
            TerrainEditMesher::MeshResult tmpResult;
            tmpResult.vertices = std::move(comp.currentArtifact.vertices);
            tmpResult.indices  = std::move(comp.currentArtifact.indices);
            tmpResult.aabbMin  = comp.currentArtifact.aabbMin;
            tmpResult.aabbMax  = comp.currentArtifact.aabbMax;

            auto subs = TerrainEditMesher::splitToSubMeshes(tmpResult);
            for (auto& sub : subs) {
                MeshData md(comp.entity);
                md.vertices = std::move(sub.vertices);
                md.indices  = std::move(sub.indices);
                subChunks.push_back(std::move(md));
            }
            mainSubChunkCount = static_cast<uint8_t>(subChunks.size());
        }
        if (comp.currentArtifact.terrainType == TerrainType::DCCM) {
            const uint8_t casingMask = computeDCCMCasingMask(world, comp.chunkCoord, maskCenter);
            for (int edge = 0; edge < CHUNK_EDGE_COUNT; ++edge) {
                if ((casingMask & (1 << edge)) == 0) continue;
                MeshData casingData(comp.entity);
                generateDCCMCasingForEdge(edge, subChunks, mainSubChunkCount, casingData);
                if (!casingData.isEmpty()) {
                    subChunks.push_back(std::move(casingData));
                }
            }
        }
        debugInfo.subChunkCount = static_cast<uint16_t>(subChunks.size());

        glm::vec3 tightMin(1e10f);
        glm::vec3 tightMax(-1e10f);
        const bool hasTight = computeTightAABBFromSubChunks(subChunks, tightMin, tightMax);

        {
            auto t0 = Clock::now();
            world->enqueueMeshForUpload(
                comp.entity,
                std::move(subChunks),
                mainSubChunkCount,
                /*fromTerrainEdit=*/uploadFromTerrainEdit,
                comp.versionState,
                comp.version,
                tightMin,
                tightMax,
                hasTight,
                /*isRemesh=*/false,
                /*batchId=*/0,
                debugInfo);
            ++uploadsQueued;
            gpuAccum += std::chrono::duration<float, std::milli>(Clock::now() - t0).count();
        }

        releaseEditRemeshInFlight(comp.versionState, comp.version);

        // Record edit remesh completion for chunk hole tracking
        {
            ChunkHoleEvent ev;
            ev.type = ChunkHoleEvent::Type::EditRemeshCompleted;
            ev.timestampSec = std::chrono::duration<float>(
                std::chrono::steady_clock::now().time_since_epoch()).count();
            ev.detail = "verts=" + std::to_string(diag.vertexCount)
                      + " idx=" + std::to_string(diag.indexCount);
            world->getChunkHoleTracker().recordEvent(
                ChunkCoord{comp.chunkCoord.x, comp.chunkCoord.y, comp.chunkCoord.z},
                std::move(ev));
        }
        if (comp.isFastMode) {
            std::lock_guard qlock(m_mutex);
            m_qualityDirty.insert(ChunkCoord{comp.chunkCoord.x, comp.chunkCoord.y, comp.chunkCoord.z});
        }
    }

    if (!batch.empty()) {
        diag.collisionEnqueueMs += collAccum;
        diag.gpuUploadEnqueueMs += gpuAccum;
    }
    return uploadsQueued;
}

// ---------------------------------------------------------------------------
// dispatchJobs — main thread: kick background jobs for dirty chunks
// ---------------------------------------------------------------------------

void TerrainEditRemeshScheduler::dispatchJobs(
    World* world,
    const std::vector<ChunkCoord>& chunks,
    size_t budget,
    bool fastMode,
    bool materialOnly)
{
    const auto& fieldSource = world->getTerrainFieldSource();
    const auto& heightmap   = world->getHeightmapSampler();
    const auto& voxelBase   = world->getVoxelBaseSampler();
    auto& registry = world->getRegistry();
    auto& diag = world->editDiagMut();
    diag.chunksRemeshed = 0;
    diag.vertexCount = 0;
    diag.indexCount = 0;

    using DispClk = std::chrono::high_resolution_clock;
    float heightAccum = 0.0f;
    float yRangeAccum = 0.0f;
    uint32_t inFlightSkipCount = 0;
    const auto* overlay = fieldSource.getOverlay();
    const bool hasOverlay = overlay && overlay->hasAnyEdits();

    auto requeueDirtyChunk = [&](const ChunkCoord& coord) {
        std::lock_guard lock(m_mutex);
        if (materialOnly) {
            m_materialDirty.insert(coord);
        } else {
            m_dirty.insert(coord);
        }
    };

    auto requeueDirtyRange = [&](size_t first) {
        std::lock_guard lock(m_mutex);
        auto& dst = materialOnly ? m_materialDirty : m_dirty;
        for (size_t i = first; i < chunks.size(); ++i) {
            dst.insert(chunks[i]);
        }
    };

    size_t dispatched = 0;
    for (const auto& chunkCoord : chunks) {
        if (dispatched >= budget) {
            requeueDirtyRange(dispatched);
            break;
        }

        const glm::ivec3 worldChunkCoord(chunkCoord.x, chunkCoord.y, chunkCoord.z);

        entt::entity entity = world->findChunk(worldChunkCoord);
        if (entity == entt::null) {
            if (materialOnly) {
                continue;
            }
            entity = world->createChunk(worldChunkCoord);
        }
        if (entity == entt::null) continue;

        auto versionState = ensureChunkVersionState(world, entity);
        if (!versionState) continue;

        DirtyChunkPages pendingDirtyPages;
        bool hasPendingDirtyPages = false;
        if (!materialOnly) {
            std::lock_guard lock(m_mutex);
            auto pendingIt = m_pendingDirtyPages.find(chunkCoord);
            if (pendingIt != m_pendingDirtyPages.end()) {
                pendingDirtyPages = pendingIt->second;
                m_pendingDirtyPages.erase(pendingIt);
                hasPendingDirtyPages = !pendingDirtyPages.pageIds.empty();
            }
        }

        const bool anyPipelineInFlight = versionState->inFlight.load(std::memory_order_acquire);
        const uint32_t editJobsInFlight =
            versionState->editRemeshInFlightCount.load(std::memory_order_acquire);

        // Terrain edits may supersede older edit mesh jobs so the newest shape
        // wins. Texture/material-only rebakes must NOT do that: the previous
        // material upload may already be built but not yet swapped to screen.
        // Repeatedly bumping the version here is exactly what makes big brush
        // spam appear to freeze until another tiny edit finally lets one upload
        // survive. Coalesce material-only dirties behind the current visual
        // pipeline and dispatch the newest rebake once the chunk is clear.
        if (anyPipelineInFlight) {
            if (materialOnly || !fastMode || editJobsInFlight == 0) {
                if (fastMode && !materialOnly) {
                    versionState->version.fetch_add(1, std::memory_order_acq_rel);
                }
                {
                    std::lock_guard skipLock(m_skipCountMutex);
                    ++m_inFlightSkipCounts[chunkCoord];
                }
                requeueDirtyChunk(chunkCoord);
                ++inFlightSkipCount;
                continue;
            }
        }

        if (materialOnly) {
            bool hasPendingVisualSwap = false;
            {
                std::shared_lock regLock(world->registryMutex());
                hasPendingVisualSwap =
                    registry.valid(entity) &&
                    registry.all_of<PendingMeshHandle>(entity);
            }
            if (hasPendingVisualSwap) {
                std::lock_guard skipLock(m_skipCountMutex);
                ++m_inFlightSkipCounts[chunkCoord];
                requeueDirtyChunk(chunkCoord);
                ++inFlightSkipCount;
                continue;
            }
        }

        // For fast-mode (new edit/material): bump version to invalidate stale
        // non-visible work. Material-only requests only reach this point when
        // no previous upload/swap for the chunk is pending, so this is a single
        // coalesced latest-version claim instead of a cancellation storm.
        // For quality refinement: reuse current version so a fast upload already
        // in the ChunkUploadSystem pipeline is NOT rejected as stale.
        uint32_t ver;
        if (fastMode) {
            ver = versionState->version.fetch_add(1, std::memory_order_acq_rel) + 1;
        } else {
            ver = versionState->version.load(std::memory_order_acquire);
        }
        versionState->editRemeshLatestVersion.store(ver, std::memory_order_release);
        versionState->editRemeshInFlightCount.fetch_add(1, std::memory_order_acq_rel);
        versionState->inFlight.store(true, std::memory_order_release);

        LoadManagementSnapshot loadSnapshot{};
        {
            const auto worldLoad = world->getLoadManagementDiag();
            loadSnapshot.baseRenderDist = worldLoad.baseRenderDist;
            loadSnapshot.effectiveRenderDist = worldLoad.effectiveRenderDist;
            loadSnapshot.extensionRings = worldLoad.extensionRings;
            loadSnapshot.measuredThroughput = worldLoad.measuredThroughput;
            loadSnapshot.pendingCreates = worldLoad.pendingCreates;
            loadSnapshot.pendingDestroys = worldLoad.pendingDestroys;
            loadSnapshot.lodRemeshQueue = worldLoad.lodRemeshQueue;
            loadSnapshot.pendingLodRemeshes = worldLoad.pendingLodRemeshes;
            loadSnapshot.editRemeshPending = worldLoad.editRemeshPending;
            loadSnapshot.uploadQueue = worldLoad.uploadQueue;
            loadSnapshot.finalizeQueue = worldLoad.finalizeQueue;
            loadSnapshot.bufferPressure = worldLoad.bufferPressure;
        }
        loadSnapshot.editJobsInFlight = static_cast<uint32_t>(
            m_inFlightCount.load(std::memory_order_relaxed));
        {
            std::lock_guard skipLock(m_skipCountMutex);
            auto skipIt = m_inFlightSkipCounts.find(chunkCoord);
            if (skipIt != m_inFlightSkipCounts.end()) {
                loadSnapshot.inFlightSkips = skipIt->second;
                m_inFlightSkipCounts.erase(skipIt);
            }
        }

        int lodLevel = 0;
        {
            std::shared_lock regLock(world->registryMutex());
            if (registry.valid(entity) && registry.all_of<Chunk>(entity)) {
                lodLevel = registry.get<Chunk>(entity).lodLevel;
            }
        }
        TerrainType terrainType = world->getTerrainTypeForChunk(worldChunkCoord, lodLevel);
        int effectiveLOD = world->getEffectiveLODForChunk(worldChunkCoord, lodLevel);
        bool useDCCM = (terrainType == TerrainType::DCCM) && heightmap.isLoaded();
        const bool supportsPagedEditable = !useDCCM &&
            shouldExposePagedEditableMode(terrainType, effectiveLOD);

        {
            std::unique_lock regLock(world->registryMutex());
            if (registry.valid(entity) && registry.all_of<Chunk>(entity)) {
                auto& chunk = registry.get<Chunk>(entity);
                const bool hasEditOwnership = materialOnly ||
                    hasPendingDirtyPages ||
                    registry.any_of<ChunkEditRuntime>(entity) ||
                    (overlay && overlay->hasEditsInChunk(worldChunkCoord));
                if (hasEditOwnership) {
                    auto& editRuntime = registry.get_or_emplace<ChunkEditRuntime>(entity);
                    editRuntime.targetMode = supportsPagedEditable
                        ? ChunkMeshMode::PagedEditable
                        : ChunkMeshMode::MonolithicEdited;

                    if (supportsPagedEditable) {
                        ensurePagedRuntimeScaffold(editRuntime);
                        if (hasPendingDirtyPages) {
                            applyDirtyPageUpdate(editRuntime, pendingDirtyPages);
                        }

                        chunk.meshMode = ChunkMeshMode::PagedEditable;
                    } else {
                        chunk.meshMode = ChunkMeshMode::MonolithicEdited;
                    }
                }
            }
        }

        if (useDCCM) {
            releaseEditRemeshInFlight(versionState, ver);
            continue;
        }

        TerrainType currentTerrainType = TerrainType::Voxel;

        int hintMinY = -1, hintMaxY = -1;
        if (!useDCCM && heightmap.isLoaded()) {
            const int chunkBaseY = worldChunkCoord.y * WorldConfig::CHUNK_HEIGHT;
            if (worldChunkCoord.y < 0) {
                hintMinY = chunkBaseY;
                hintMaxY = chunkBaseY + WorldConfig::CHUNK_HEIGHT - 1;
            } else if (worldChunkCoord.y == 0) {
                const auto tH0 = DispClk::now();
                auto [hMin, hMax] = heightmap.getHeightRangeForChunk(
                    worldChunkCoord.x, worldChunkCoord.z, WorldConfig::CHUNK_SIZE);
                heightAccum += std::chrono::duration<float, std::milli>(DispClk::now() - tH0).count();
                hintMinY = hMin;
                hintMaxY = hMax;
            }

            if (hasOverlay) {
                const auto tY0 = DispClk::now();
                auto [editMinY, editMaxY] = overlay->getEditVoxelYRange(
                    worldChunkCoord.x * WorldConfig::CHUNK_SIZE,
                    worldChunkCoord.z * WorldConfig::CHUNK_SIZE,
                    WorldConfig::CHUNK_SIZE);
                yRangeAccum += std::chrono::duration<float, std::milli>(DispClk::now() - tY0).count();
                if (editMinY <= editMaxY) {
                    if (hintMinY < 0) { hintMinY = editMinY; hintMaxY = editMaxY; }
                    else {
                        if (editMinY < hintMinY) hintMinY = editMinY;
                        if (editMaxY > hintMaxY) hintMaxY = editMaxY;
                    }
                }
            }
        } else if (!useDCCM && voxelBase.isLoaded()) {
            auto [vMin, vMax] = voxelBase.getYRangeForChunk(
                worldChunkCoord.x, worldChunkCoord.z, WorldConfig::CHUNK_SIZE);
            if (vMin <= vMax) { hintMinY = vMin; hintMaxY = vMax; }

            if (hasOverlay) {
                const auto tY0 = DispClk::now();
                auto [editMinY, editMaxY] = overlay->getEditVoxelYRange(
                    worldChunkCoord.x * WorldConfig::CHUNK_SIZE,
                    worldChunkCoord.z * WorldConfig::CHUNK_SIZE,
                    WorldConfig::CHUNK_SIZE);
                yRangeAccum += std::chrono::duration<float, std::milli>(DispClk::now() - tY0).count();
                if (editMinY <= editMaxY) {
                    if (hintMinY < 0) { hintMinY = editMinY; hintMaxY = editMaxY; }
                    else {
                        if (editMinY < hintMinY) hintMinY = editMinY;
                        if (editMaxY > hintMaxY) hintMaxY = editMaxY;
                    }
                }
            }
        }

        auto* payload     = new RemeshJobPayload{};
        payload->scheduler   = this;
        payload->entity      = entity;
        payload->chunkCoord  = worldChunkCoord;
        payload->centerAtEnqueue = world->getChunkManager()
            ? world->getChunkManager()->getCenterChunk()
            : glm::ivec3(0, 0, 0);
        payload->versionState = versionState;
        payload->version     = ver;
        payload->currentLodLevel = effectiveLOD;
        payload->currentTerrainType = currentTerrainType;
        payload->currentUsesDCCM = useDCCM;
        payload->hintMinY    = hintMinY;
        payload->hintMaxY    = hintMaxY;
        payload->fieldSource = &fieldSource;
        payload->heightmap   = &heightmap;
        payload->voxelBase   = voxelBase.isLoaded() ? &voxelBase : nullptr;
        payload->overlay     = fieldSource.getOverlay();
        payload->fastMode    = fastMode && !materialOnly;
        payload->materialOnly = materialOnly;
        payload->dispatchTime = std::chrono::steady_clock::now();
        payload->loadSnapshot = loadSnapshot;

        if (!materialOnly && hasPendingDirtyPages && !pendingDirtyPages.pageBounds.empty()) {
            int wMinX = INT_MAX, wMinY = INT_MAX, wMinZ = INT_MAX;
            int wMaxX = INT_MIN, wMaxY = INT_MIN, wMaxZ = INT_MIN;
            for (const auto& pb : pendingDirtyPages.pageBounds) {
                if (!pb.isValid()) continue;
                wMinX = std::min(wMinX, static_cast<int>(pb.minX));
                wMaxX = std::max(wMaxX, static_cast<int>(pb.maxX));
                wMinY = std::min(wMinY, static_cast<int>(pb.minY));
                wMaxY = std::max(wMaxY, static_cast<int>(pb.maxY));
                wMinZ = std::min(wMinZ, static_cast<int>(pb.minZ));
                wMaxZ = std::max(wMaxZ, static_cast<int>(pb.maxZ));
            }
            if (wMinX <= wMaxX && wMinY <= wMaxY && wMinZ <= wMaxZ) {
                const int chunkBaseVX = worldChunkCoord.x * WorldConfig::CHUNK_SIZE;
                const int chunkBaseVY = worldChunkCoord.y * WorldConfig::CHUNK_HEIGHT;
                const int chunkBaseVZ = worldChunkCoord.z * WorldConfig::CHUNK_SIZE;
                payload->hasEditDirtyAabb = true;
                payload->editDirtyVoxelMin = glm::ivec3(
                    chunkBaseVX + wMinX, chunkBaseVY + wMinY, chunkBaseVZ + wMinZ);
                payload->editDirtyVoxelMax = glm::ivec3(
                    chunkBaseVX + wMaxX, chunkBaseVY + wMaxY, chunkBaseVZ + wMaxZ);
                payload->bandLocalYMin = std::max(0, wMinY - 1);
                payload->bandLocalYMax = std::min(WorldConfig::CHUNK_HEIGHT - 1, wMaxY + 1);
            }
        }

        if (!fastMode && !materialOnly && !useDCCM) {
            for (int altLod = 0; altLod < MAX_LOD_LEVELS; ++altLod) {
                if (altLod == effectiveLOD) {
                    continue;
                }
                if (world->getEditArtifactGeneration(
                        worldChunkCoord,
                        TerrainType::Voxel,
                        altLod) != 0) {
                    continue;
                }

                CachedArtifact deferredTarget{};
                deferredTarget.terrainType = TerrainType::Voxel;
                deferredTarget.lodLevel = altLod;
                payload->deferredTargets.push_back(std::move(deferredTarget));
            }
        }

        m_inFlightCount.fetch_add(1, std::memory_order_relaxed);

        int priority = fastMode
            ? (1000000 - lodLevel * 100000)
            : (500000 - lodLevel * 100000);
        if (materialOnly) {
            priority += 50000;
        }
        JobHandle job = world->getJobSystem().makeWithPriority(
            editRemeshJobFn, payload, 0, priority);
        world->getJobSystem().schedule(job);

        ++dispatched;
    }

    diag.dispatchHeightMs += heightAccum;
    diag.dispatchYRangeMs += yRangeAccum;
    diag.dispatchInFlightSkip += inFlightSkipCount;
    diag.editJobsInFlight = static_cast<uint32_t>(
        m_inFlightCount.load(std::memory_order_relaxed));
}

// ---------------------------------------------------------------------------
// drainDeferredArtifacts — main thread: store LOD variant meshes in cache
// ---------------------------------------------------------------------------

void TerrainEditRemeshScheduler::drainDeferredArtifacts(World* world) {
    std::vector<DeferredArtifactResult> batch;
    {
        std::lock_guard lock(m_deferredArtifactMutex);
        if (m_deferredArtifacts.empty()) return;
        batch.swap(m_deferredArtifacts);
    }

    for (auto& r : batch) {
        world->storeEditArtifact(
            r.chunkCoord,
            r.artifact.terrainType,
            r.artifact.lodLevel,
            std::move(r.artifact.vertices),
            std::move(r.artifact.indices),
            r.artifact.aabbMin,
            r.artifact.aabbMax,
            r.artifact.isEmpty,
            /*deferredBuild=*/true);
    }
}

// ---------------------------------------------------------------------------
// Deferred LOD pre-warm: separate worker job (low priority).
// Runs only when the brush has been idle long enough to not steal worker
// capacity from interactive fast edits.
// ---------------------------------------------------------------------------
struct DeferredLODJobPayload {
    TerrainEditRemeshScheduler* scheduler;
    World* world;
    TerrainEditRemeshScheduler::PendingDeferredLOD req;
};

void deferredLODJobFn(JobCtx& /*ctx*/, void* ud) {
    auto* p = static_cast<DeferredLODJobPayload*>(ud);

    // Drop if a newer edit has already invalidated this version.
    if (p->req.versionState->version.load(std::memory_order_acquire) != p->req.version) {
        p->scheduler->m_inFlightCount.fetch_sub(1, std::memory_order_relaxed);
        delete p;
        return;
    }

    const RemeshCancellationToken cancelToken{
        &p->req.versionState->version, p->req.version};

    const auto& fieldSource = p->world->getTerrainFieldSource();
    const auto& heightmap   = p->world->getHeightmapSampler();
    const auto& voxelBase   = p->world->getVoxelBaseSampler();
    const auto* overlay     = fieldSource.getOverlay();

    auto artifact = buildArtifact(
        fieldSource,
        heightmap,
        &voxelBase,
        overlay,
        p->req.chunkCoord,
        p->req.hintMinY,
        p->req.hintMaxY,
        p->req.terrainType,
        p->req.lodLevel,
        p->req.terrainType == TerrainType::DCCM,
        /*fastMode=*/false,
        &cancelToken,
        p->req.hasEditDirtyAabb ? &p->req.editDirtyVoxelMin : nullptr,
        p->req.hasEditDirtyAabb ? &p->req.editDirtyVoxelMax : nullptr);

    if (p->req.versionState->version.load(std::memory_order_acquire) != p->req.version) {
        // Newer edit invalidated us mid-build — drop the partial result.
        p->scheduler->m_inFlightCount.fetch_sub(1, std::memory_order_relaxed);
        delete p;
        return;
    }

    p->scheduler->pushDeferredArtifact({
        p->req.chunkCoord,
        std::move(artifact),
        p->req.versionState,
        p->req.version
    });

    p->scheduler->m_inFlightCount.fetch_sub(1, std::memory_order_relaxed);
    delete p;
}

void TerrainEditRemeshScheduler::dispatchPendingDeferredLODs(World* world) {
    using Clock = std::chrono::steady_clock;
    // Idle threshold: only kick off LOD pre-warming when the brush has been
    // quiet for this long. Tunable; 200 ms feels imperceptible to users while
    // still giving fast edits clear worker headroom.
    constexpr int64_t IDLE_NS_THRESHOLD = 200LL * 1000LL * 1000LL; // 200 ms
    // Per-frame cap so we don't suddenly flood every worker with deferred
    // LOD work (which would re-create the original problem). The remaining
    // requests stay in the queue and drain over subsequent idle frames.
    constexpr size_t MAX_DEFERRED_LOD_PER_FRAME = 8;

    const int64_t lastFastNs =
        m_lastFastDispatchNs.load(std::memory_order_acquire);
    if (lastFastNs != 0) {
        const int64_t nowNs =
            Clock::now().time_since_epoch().count();
        if (nowNs - lastFastNs < IDLE_NS_THRESHOLD) {
            return; // brush still active — wait
        }
    }

    std::vector<PendingDeferredLOD> batch;
    {
        std::lock_guard lock(m_pendingDeferredLODMutex);
        if (m_pendingDeferredLODs.empty()) return;
        const size_t take = std::min(m_pendingDeferredLODs.size(),
                                     MAX_DEFERRED_LOD_PER_FRAME);
        batch.reserve(take);
        // Drain newest-first so visually-relevant recent edits win when the
        // queue overflows (older requests will be dropped by version-check
        // anyway if they were superseded).
        for (size_t i = 0; i < take; ++i) {
            batch.push_back(std::move(m_pendingDeferredLODs.back()));
            m_pendingDeferredLODs.pop_back();
        }
    }

    for (auto& req : batch) {
        // Skip if already invalidated before we even submit.
        if (!req.versionState ||
            req.versionState->version.load(std::memory_order_acquire) != req.version) {
            continue;
        }
        // Skip if the artifact cache already has this combo (race-safe filter).
        if (world->getEditArtifactGeneration(
                req.chunkCoord, req.terrainType, req.lodLevel) != 0) {
            continue;
        }

        auto* payload = new DeferredLODJobPayload{this, world, std::move(req)};
        m_inFlightCount.fetch_add(1, std::memory_order_relaxed);
        // Lower-priority than fast edits AND than quality refinement so that
        // any incoming brush stroke continues to preempt LOD pre-warm.
        constexpr int DEFERRED_LOD_PRIORITY = 100000;
        JobHandle job = world->getJobSystem().makeWithPriority(
            deferredLODJobFn, payload, 0, DEFERRED_LOD_PRIORITY);
        world->getJobSystem().schedule(job);
    }
}

// ---------------------------------------------------------------------------
// processRemeshQueue — main thread, per-frame
// ---------------------------------------------------------------------------

void TerrainEditRemeshScheduler::processRemeshQueue(World* world, size_t budget, bool dispatchOnly)
{
    using Clock = std::chrono::high_resolution_clock;
    auto& diag = world->editDiagMut();

    const auto tDrainStart = Clock::now();
    drainCompletions(world);
    if (!dispatchOnly) {
        drainDeferredArtifacts(world);
    }
    const auto tDrainEnd = Clock::now();
    diag.dispatchDrainMs = std::chrono::duration<float, std::milli>(tDrainEnd - tDrainStart).count();

    std::vector<ChunkCoord> editDirty;
    std::vector<ChunkCoord> materialDirty;
    std::vector<ChunkCoord> qualityDirty;
    {
        std::lock_guard lock(m_mutex);
        if (m_dirty.empty() && m_materialDirty.empty() && m_qualityDirty.empty()) {
            if (diag.chunksRemeshed > 0) {
                diag.remeshTotalMs = std::chrono::duration<float, std::milli>(
                    tDrainEnd - tDrainStart).count();
                diag.grandTotalMs = diag.applyTotalMs + diag.remeshTotalMs;
            }
            return;
        }

        editDirty.reserve(m_dirty.size());
        for (const auto& c : m_dirty) {
            editDirty.push_back(c);
        }
        m_dirty.clear();

        auto alreadyIn = [](const std::vector<ChunkCoord>& list, const ChunkCoord& c) {
            for (const ChunkCoord& e : list) {
                if (e.x == c.x && e.y == c.y && e.z == c.z) {
                    return true;
                }
            }
            return false;
        };

        materialDirty.reserve(m_materialDirty.size());
        for (const auto& c : m_materialDirty) {
            if (!alreadyIn(editDirty, c)) {
                materialDirty.push_back(c);
            }
        }
        m_materialDirty.clear();

        for (const auto& c : m_qualityDirty) {
            if (!alreadyIn(editDirty, c) && !alreadyIn(materialDirty, c)) {
                qualityDirty.push_back(c);
            }
        }
        m_qualityDirty.clear();
    }

    auto sortByEditCenter = [&](std::vector<ChunkCoord>& dirty) {
        if (dirty.size() <= 3) {
            return;
        }

        const glm::vec3 editCenter = diag.editCenter;
        const float invChunkSize = 1.0f / static_cast<float>(WorldConfig::CHUNK_SIZE);
        std::sort(dirty.begin(), dirty.end(),
            [&](const ChunkCoord& a, const ChunkCoord& b) {
                auto dist2 = [&](const ChunkCoord& c) {
                    const float dx = static_cast<float>(c.x) - editCenter.x * invChunkSize;
                    const float dz = static_cast<float>(c.z) - editCenter.z * invChunkSize;
                    return dx * dx + dz * dz;
                };
                return dist2(a) < dist2(b);
            });
    };

    sortByEditCenter(editDirty);
    sortByEditCenter(materialDirty);

    if (budget == 0) {
        budget = editDirty.size() + materialDirty.size() + qualityDirty.size();
    }

    constexpr size_t MIN_FAST_EDIT_DISPATCH_BUDGET = 64;
    constexpr size_t MIN_MATERIAL_DISPATCH_BUDGET = 128;

    const auto tDispatchStart = Clock::now();

    const size_t editDispatchBudget = editDirty.empty()
        ? size_t(0)
        : std::min(editDirty.size(), std::max(budget, MIN_FAST_EDIT_DISPATCH_BUDGET));
    dispatchJobs(world, editDirty, editDispatchBudget, /*fastMode=*/true, /*materialOnly=*/false);

    const size_t materialDispatchBudget = materialDirty.empty()
        ? size_t(0)
        : std::min(materialDirty.size(), std::max(budget, MIN_MATERIAL_DISPATCH_BUDGET));
    dispatchJobs(world, materialDirty, materialDispatchBudget, /*fastMode=*/true, /*materialOnly=*/true);

    if (!editDirty.empty() || !materialDirty.empty()) {
        m_lastFastDispatchNs.store(
            std::chrono::steady_clock::now().time_since_epoch().count(),
            std::memory_order_release);
    }

    if (!qualityDirty.empty()) {
        const size_t interactiveWave = editDirty.size() + materialDirty.size();
        const bool largeInteractiveWave = interactiveWave >= 16u;
        size_t qualityBudget = largeInteractiveWave
            ? size_t(0)
            : (interactiveWave == 0 ? size_t(12) : size_t(2));
        qualityBudget = std::min(qualityBudget, qualityDirty.size());
        if (qualityBudget > 0) {
            dispatchJobs(world, qualityDirty, qualityBudget, /*fastMode=*/false, /*materialOnly=*/false);
        }
        if (qualityDirty.size() > qualityBudget) {
            std::lock_guard lock(m_mutex);
            for (size_t i = qualityBudget; i < qualityDirty.size(); ++i) {
                m_qualityDirty.insert(qualityDirty[i]);
            }
        }
    }
    const auto tDispatchEnd = Clock::now();

    if (!dispatchOnly) {
        dispatchPendingDeferredLODs(world);
    }

    diag.meshMs = 0.0f;
    diag.remeshTotalMs = std::chrono::duration<float, std::milli>(
        tDispatchEnd - tDrainStart).count();
    diag.grandTotalMs = diag.applyTotalMs + diag.remeshTotalMs;
}

size_t TerrainEditRemeshScheduler::flushReadyCompletions(World* world)
{
    if (!world) {
        return 0;
    }
    const size_t uploadsQueued = drainCompletions(world);
    drainDeferredArtifacts(world);
    return uploadsQueued;
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

## include\world\edit\TerrainEditRemeshScheduler.h

Description: No CC-DESC found. C++ class 'World'.

````cpp
#pragma once
// GPT-DESC: Schedules terrain and material-only chunk remesh work for VulkanVX edits.

#include "world/edit/TerrainEditTypes.h"
#include "world/edit/TerrainEditMesher.h"
#include "world/WorldTypes.h"
#include "rendering/common/Mesh.h"
#include <unordered_set>
#include <unordered_map>
#include <chrono>
#include <mutex>
#include <vector>
#include <memory>
#include <atomic>
#include <glm/glm.hpp>
#include <entt/entt.hpp>

// Forward declarations
class World;
struct ChunkVersionState;
struct JobCtx;

namespace TerrainEdit {

/**
 * TerrainEditRemeshScheduler
 *
 * Collects dirty chunk coordinates after terrain edits and dispatches
 * background remesh jobs via the engine's JobSystem.  Completed meshes
 * are drained on the main thread and funnelled through the existing
 * ChunkUploadSystem so the GPU update path is identical to the
 * precomputed-mesh path.
 *
 * Lifecycle:
 *   1.  applyTerrainBoxEdit()  -> markChunksDirty(touchedChunks)
 *   2.  World::update()        -> processRemeshQueue(world)
 *       a.  drainCompletions() -- picks up finished background meshes (< 1 ms)
 *       b.  dispatchJobs()     -- kicks off new background jobs        (< 1 ms)
 *
 * All heavy meshing (greedy voxel + RTIN DCCM) runs on worker threads.
 * Main-thread cost per frame: ~0.1 ms regardless of edit size.
 */
class TerrainEditRemeshScheduler {
public:
    TerrainEditRemeshScheduler() = default;

    struct LoadManagementSnapshot {
        int baseRenderDist{0};
        int effectiveRenderDist{0};
        int extensionRings{0};
        float measuredThroughput{0.0f};
        uint32_t pendingCreates{0};
        uint32_t pendingDestroys{0};
        uint32_t lodRemeshQueue{0};
        uint32_t pendingLodRemeshes{0};
        uint32_t editRemeshPending{0};
        uint32_t uploadQueue{0};
        uint32_t finalizeQueue{0};
        uint32_t inFlightSkips{0};
        bool bufferPressure{false};
        uint32_t editJobsInFlight{0};
    };

    /**
     * Mark chunks as needing re-mesh after an occupancy/topology edit.
     * Called from applyTerrainBoxEdit() -- may be called from any thread.
     */
    void markChunksDirty(
        const std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal>& chunks,
        const DirtyChunkPageMap* dirtyChunkPages = nullptr);

    /**
     * Mark chunks as needing a material-only visual rebake after texture paint.
     *
     * Unlike terrain edits, these requests are coalesced behind any already
     * visible/in-flight upload for the same chunk. They must not repeatedly bump
     * the chunk version and cancel their own upload path while the brush is
     * being spammed.
     */
    void markMaterialChunksDirty(
        const std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal>& chunks);

    /**
     * Drain completions + dispatch new background jobs.
     *
     * @param world   The world that owns the chunk entities and upload system.
     * @param budget  Max chunks to dispatch per call (0 = unlimited).
     * @param dispatchOnly  If true, skip deferred LOD work (used for inline calls during edits).
     */
    void processRemeshQueue(World* world, size_t budget = 8, bool dispatchOnly = false);
    size_t flushReadyCompletions(World* world);

    /** Number of chunks still waiting for re-mesh (dirty + in-flight). */
    size_t pendingCount() const;

    /** Result of a background remesh job -- pushed from worker threads. */
    struct CachedArtifact {
        TerrainType terrainType{TerrainType::Voxel};
        int lodLevel{0};
        bool isEmpty{true};
        std::vector<Vertex> vertices;
        std::vector<uint32_t> indices;
        glm::vec3 aabbMin{1e10f};
        glm::vec3 aabbMax{-1e10f};
        TerrainEditMesher::MeshStats meshStats;
    };

    struct CompletedRemesh {
        entt::entity    entity;
        glm::ivec3      chunkCoord;
        glm::ivec3      centerAtEnqueue{0, 0, 0};
        CachedArtifact  currentArtifact;
        std::vector<CachedArtifact> deferredArtifacts;
        std::vector<Vertex>   collVerts;
        std::vector<uint32_t> collIndices;
        std::shared_ptr<ChunkVersionState> versionState;
        uint32_t version{0};
        bool isFastMode{false};
        bool materialOnly{false};

        // Per-stage timestamps for pipeline breakdown
        std::chrono::steady_clock::time_point dispatchTime{};
        std::chrono::steady_clock::time_point jobStartTime{};
        std::chrono::steady_clock::time_point meshDoneTime{};
        LoadManagementSnapshot loadSnapshot{};
    };

    /** Push a completed remesh from a worker thread (thread-safe). */
    void pushCompletion(CompletedRemesh&& c);

    /** A deferred artifact built on a worker thread for the cache. */
    struct DeferredArtifactResult {
        glm::ivec3      chunkCoord;
        CachedArtifact  artifact;
        std::shared_ptr<ChunkVersionState> versionState;
        uint32_t        version{0};
    };

    /** Push a deferred LOD artifact from a worker thread (thread-safe). */
    void pushDeferredArtifact(DeferredArtifactResult&& r);

    /** Per-chunk timing record for pipeline breakdown diagnostics. */
    struct ChunkTimingRecord {
        std::chrono::steady_clock::time_point dispatchTime{};
        std::chrono::steady_clock::time_point jobStartTime{};
        std::chrono::steady_clock::time_point meshDoneTime{};
        std::chrono::steady_clock::time_point drainTime{};
        bool isFastMode{false};
        int meshLodLevel{-1};   // Actual LOD the mesher ran at (may differ from chunk.lodLevel via effectiveLOD).
        TerrainEditMesher::MeshStats meshStats;
        LoadManagementSnapshot loadSnapshot;
    };

    /** Consume timing record for a chunk (returns true if found). */
    bool consumeChunkTiming(const glm::ivec3& coord, ChunkTimingRecord& out);

    // --- Deferred LOD pre-warming (queued during fast edits, dispatched once
    //     the brush has been idle long enough to not steal worker capacity). ---
    struct PendingDeferredLOD {
        glm::ivec3 chunkCoord{0};
        TerrainType terrainType{TerrainType::Voxel};
        int lodLevel{0};
        int hintMinY{-1};
        int hintMaxY{-1};
        std::shared_ptr<ChunkVersionState> versionState;
        uint32_t version{0};
        bool hasEditDirtyAabb{false};
        glm::ivec3 editDirtyVoxelMin{0};
        glm::ivec3 editDirtyVoxelMax{0};
    };

private:
    friend void editRemeshJobFn(::JobCtx& ctx, void* ud);
    friend void deferredLODJobFn(::JobCtx& ctx, void* ud);

    void dispatchJobs(World* world,
                      const std::vector<ChunkCoord>& chunks,
                      size_t budget,
                      bool fastMode = true,
                      bool materialOnly = false);
    size_t drainCompletions(World* world);
    void drainDeferredArtifacts(World* world);
    /** If brush is idle (no recent fast-mode dispatch), drain pending
     *  deferred-LOD requests as separate low-priority worker jobs. */
    void dispatchPendingDeferredLODs(World* world);

    mutable std::mutex m_mutex;
    std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal> m_dirty;
    std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal> m_materialDirty;
    std::unordered_set<ChunkCoord, IVec3Hash, IVec3Equal> m_qualityDirty;
    DirtyChunkPageMap m_pendingDirtyPages;

    mutable std::mutex m_completionMutex;
    std::vector<CompletedRemesh> m_completions;

    mutable std::mutex m_deferredArtifactMutex;
    std::vector<DeferredArtifactResult> m_deferredArtifacts;

    std::atomic<size_t> m_inFlightCount{0};
    mutable std::mutex m_skipCountMutex;
    std::unordered_map<ChunkCoord, uint32_t, IVec3Hash, IVec3Equal> m_inFlightSkipCounts;

    mutable std::mutex m_timingMutex;
    std::unordered_map<glm::ivec3, ChunkTimingRecord, IVec3Hash> m_chunkTimings;

    // --- Deferred LOD pre-warming (queued during fast edits, dispatched once
    //     the brush has been idle long enough to not steal worker capacity). ---
    mutable std::mutex m_pendingDeferredLODMutex;
    std::vector<PendingDeferredLOD> m_pendingDeferredLODs;
    // Last fast-mode dispatch time, stored as nanoseconds since steady_clock
    // epoch (atomic int64 -- std::chrono::time_point is not trivially atomic).
    std::atomic<int64_t> m_lastFastDispatchNs{0};
};

} // namespace TerrainEdit

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
constexpr int64_t MAX_INDEXED_SURFACE_STAMP_CHUNKS = 4096;

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

int64_t chunkRangeVolumeInclusive(const glm::ivec3& minChunk,
                                  const glm::ivec3& maxChunk) {
    if (minChunk.x > maxChunk.x ||
        minChunk.y > maxChunk.y ||
        minChunk.z > maxChunk.z) {
        return 0;
    }

    return int64_t(maxChunk.x - minChunk.x + 1) *
           int64_t(maxChunk.y - minChunk.y + 1) *
           int64_t(maxChunk.z - minChunk.z + 1);
}

bool chunkInInclusiveRange(const glm::ivec3& chunk,
                           const glm::ivec3& minChunk,
                           const glm::ivec3& maxChunk) {
    return chunk.x >= minChunk.x && chunk.x <= maxChunk.x &&
           chunk.y >= minChunk.y && chunk.y <= maxChunk.y &&
           chunk.z >= minChunk.z && chunk.z <= maxChunk.z;
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

        const glm::ivec3 rawChunkMin = WorldConfig::microVoxelToChunk(minLod0);
        const glm::ivec3 rawChunkMax = WorldConfig::microVoxelToChunk(maxLod0);
        const glm::ivec3 chunkMin(
            std::min(rawChunkMin.x, rawChunkMax.x),
            std::min(rawChunkMin.y, rawChunkMax.y),
            std::min(rawChunkMin.z, rawChunkMax.z));
        const glm::ivec3 chunkMax(
            std::max(rawChunkMin.x, rawChunkMax.x),
            std::max(rawChunkMin.y, rawChunkMax.y),
            std::max(rawChunkMin.z, rawChunkMax.z));

        auto stampTouches = [&](uint32_t stampIndex) -> bool {
            if (stampIndex >= m_surfacePaintStamps.size()) {
                return false;
            }
            return surfaceStampTouchesBox(m_surfacePaintStamps[stampIndex], minLod0, maxLod0);
        };

        const int64_t chunkVolume = chunkRangeVolumeInclusive(chunkMin, chunkMax);
        if (chunkVolume > 0 &&
            static_cast<uint64_t>(chunkVolume) < m_surfacePaintStampChunkIndex.size()) {
            for (int z = chunkMin.z; z <= chunkMax.z; ++z) {
                for (int y = chunkMin.y; y <= chunkMax.y; ++y) {
                    for (int x = chunkMin.x; x <= chunkMax.x; ++x) {
                        auto it = m_surfacePaintStampChunkIndex.find(glm::ivec3(x, y, z));
                        if (it == m_surfacePaintStampChunkIndex.end()) {
                            continue;
                        }
                        for (uint32_t stampIndex : it->second) {
                            if (stampTouches(stampIndex)) {
                                return true;
                            }
                        }
                    }
                }
            }
        } else {
            for (const auto& [chunk, candidates] : m_surfacePaintStampChunkIndex) {
                if (!chunkInInclusiveRange(chunk, chunkMin, chunkMax)) {
                    continue;
                }
                for (uint32_t stampIndex : candidates) {
                    if (stampTouches(stampIndex)) {
                        return true;
                    }
                }
            }
        }

        for (auto rit = m_unindexedSurfacePaintStampIndices.rbegin();
             rit != m_unindexedSurfacePaintStampIndices.rend();
             ++rit) {
            if (stampTouches(*rit)) {
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
    m_unindexedSurfacePaintStampIndices.clear();
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
        m_unindexedSurfacePaintStampIndices.clear();
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
    m_unindexedSurfacePaintStampIndices.clear();
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

    // Canonical stamp bounds must be tight. They are used for two separate
    // things:
    //   1) stamp chunk-index lookup during material baking
    //   2) choosing which chunks need a canonical material refresh
    //
    // The exact test in sampleSurfacePaintStampsLocked() is still the authority
    // for whether a face is painted. But padding this bbox by +2 voxels makes
    // extra partial chunks enter the material-rebake queue, and those chunks can
    // appear to update around the brush even though no face inside them should
    // change. Use the real brush volume here; coarse-LOD face queries already
    // expand their query rectangle when they need conservative lookup.
    const float r = static_cast<float>(stamp.radiusVoxelsLod0);
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

    // Wake the canonical-store generation without generating per-cell GPU
    // overlay deltas. The fragment-time overlay path remains disabled; the
    // visible result is made permanent by chunk material rebake into
    // Vertex::material for painted faces only.
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
    const glm::ivec3 rawC0 = WorldConfig::microVoxelToChunk(stamp.bboxMinLod0);
    const glm::ivec3 rawC1 = WorldConfig::microVoxelToChunk(stamp.bboxMaxLod0);
    const glm::ivec3 c0(
        std::min(rawC0.x, rawC1.x),
        std::min(rawC0.y, rawC1.y),
        std::min(rawC0.z, rawC1.z));
    const glm::ivec3 c1(
        std::max(rawC0.x, rawC1.x),
        std::max(rawC0.y, rawC1.y),
        std::max(rawC0.z, rawC1.z));

    const int64_t chunkVolume = chunkRangeVolumeInclusive(c0, c1);
    if (chunkVolume <= 0) {
        return;
    }
    if (chunkVolume > MAX_INDEXED_SURFACE_STAMP_CHUNKS) {
        m_unindexedSurfacePaintStampIndices.push_back(stampIndex);
        return;
    }

    for (int z = c0.z; z <= c1.z; ++z) {
        for (int y = c0.y; y <= c1.y; ++y) {
            for (int x = c0.x; x <= c1.x; ++x) {
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

    for (auto rit = m_unindexedSurfacePaintStampIndices.rbegin();
         rit != m_unindexedSurfacePaintStampIndices.rend();
         ++rit) {
        const uint32_t stampIndex = *rit;
        if (stampIndex >= m_surfacePaintStamps.size()) {
            continue;
        }

        const SurfacePaintStamp& stamp = m_surfacePaintStamps[stampIndex];
        if (stamp.order <= bestOrder) {
            continue;
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
    }

    return best;
}

} // namespace TextureOverlay

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

    // Deferred brush stamps indexed by affected native chunk when the affected
    // chunk count is modest. Very large stamps are kept in an unindexed side
    // list so a 10k-radius brush does not expand into millions of index rows.
    // The stamp vector is append-only between clears, so indices stay stable.
    std::vector<SurfacePaintStamp> m_surfacePaintStamps;
    std::unordered_map<glm::ivec3, std::vector<uint32_t>, SurfacePaintChunkHash>
        m_surfacePaintStampChunkIndex;
    std::vector<uint32_t> m_unindexedSurfacePaintStampIndices;
    uint32_t m_nextSurfacePaintStampOrder{1};

    // Dirty LOD0 face cells consumed by the terrain material overlay SSBO.
    // Full-upload mode is used for clears/config changes and delta overflow.
    std::vector<GPUCell> m_dirtyGPUCells;
    bool m_dirtyGPUFullUpload{false};
};

} // namespace TextureOverlay

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
