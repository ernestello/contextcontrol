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


## src\ui\debug_menu\world\TexturePaintTool.cpp

Description: No CC-DESC found. C++ struct 'PaintFaceKey'.

````cpp
// GPT-DESC: Implements texture paint brush authoring, diagnostics, and debounced material rebake scheduling.
#include "ui/debug_menu/world/TexturePaintTool.h"

#include <algorithm>
#include <cmath>
#include <string>
#include <climits>
#include <cstdio>
#include <queue>
#include <unordered_set>

#include <imgui.h>
#include <glm/gtc/matrix_transform.hpp>

#include "world/World.h"
#include "world/edit/TerrainEditTypes.h"

using TextureOverlay::TextureOverlayStore;
using TextureOverlay::TextureType;
using TextureOverlay::LODTextureConfig;
using TextureOverlay::VoxelTextureData;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

namespace {

constexpr float PREVIEW_FADE_SEC = 1.5f;
constexpr int MAX_SURFACE_BRUSH_FACES = 300000;
constexpr int64_t MAX_SURFACE_BRUSH_SCAN_VOXELS = 16000000;

// Same screen-ray construction as TerrainEditTool.
glm::vec3 screenToWorldRay(double mouseX, double mouseY,
                           int viewportW, int viewportH,
                           const glm::mat4& invViewProj) {
    const float ndcX =  (2.0f * static_cast<float>(mouseX)) / static_cast<float>(viewportW) - 1.0f;
    const float ndcY = 1.0f - (2.0f * static_cast<float>(mouseY)) / static_cast<float>(viewportH);
    glm::vec4 nearP = invViewProj * glm::vec4(ndcX, ndcY, 1.0f, 1.0f);
    glm::vec4 farP  = invViewProj * glm::vec4(ndcX, ndcY, 0.01f, 1.0f);
    nearP /= nearP.w;
    farP  /= farP.w;
    return glm::normalize(glm::vec3(farP) - glm::vec3(nearP));
}

const ImVec4 kAccent = ImVec4(0.95f, 0.55f, 0.10f, 1.0f);
const ImVec4 kHeading = ImVec4(0.55f, 0.85f, 1.0f, 1.0f);

ImU32 colorFromVec3(const glm::vec3& c, uint8_t alpha = 220u) {
    const uint8_t r = static_cast<uint8_t>(std::clamp(c.x, 0.0f, 1.0f) * 255.0f);
    const uint8_t g = static_cast<uint8_t>(std::clamp(c.y, 0.0f, 1.0f) * 255.0f);
    const uint8_t b = static_cast<uint8_t>(std::clamp(c.z, 0.0f, 1.0f) * 255.0f);
    return IM_COL32(r, g, b, alpha);
}

glm::ivec3 faceNormalI(uint8_t face) {
    switch (face % 6u) {
        case 0: return glm::ivec3(-1, 0, 0);
        case 1: return glm::ivec3( 1, 0, 0);
        case 2: return glm::ivec3(0, -1, 0);
        case 3: return glm::ivec3(0,  1, 0);
        case 4: return glm::ivec3(0, 0, -1);
        default: return glm::ivec3(0, 0,  1);
    }
}

glm::vec3 faceCenterWorld(const glm::ivec3& voxel, uint8_t face) {
    glm::vec3 p = glm::vec3(voxel) + glm::vec3(0.5f);
    p += glm::vec3(faceNormalI(face)) * 0.5f;
    return p * WorldConfig::VOXEL_SIZE_M;
}

struct PaintFaceKey {
    glm::ivec3 voxel{0};
    uint8_t face{0};
    bool operator==(const PaintFaceKey& other) const noexcept {
        return voxel == other.voxel && face == other.face;
    }
};

struct PaintFaceKeyHash {
    size_t operator()(const PaintFaceKey& key) const noexcept {
        uint64_t h = static_cast<uint32_t>(key.voxel.x) * 73856093ull;
        h ^= static_cast<uint32_t>(key.voxel.y) * 19349663ull;
        h ^= static_cast<uint32_t>(key.voxel.z) * 83492791ull;
        h ^= static_cast<uint64_t>(key.face) * 2654435761ull;
        h ^= h >> 33;
        return static_cast<size_t>(h);
    }
};

} // namespace

TextureOverlayStore& TexturePaintTool::getStore() {
    static TextureOverlayStore fallback;
    return m_world ? m_world->getTextureMaterialStore() : fallback;
}

const TextureOverlayStore& TexturePaintTool::getStore() const {
    static TextureOverlayStore fallback;
    return m_world ? m_world->getTextureMaterialStore() : fallback;
}

float TexturePaintTool::getPaintRepeatIntervalSec() const {
    const int radius = std::max(1, m_radiusVoxelsLod0);
    const int side = radius * 2 + 1;
    const int64_t approxFaceCells = (m_shape == BrushShape::Sphere)
        ? static_cast<int64_t>(3.14159265f * static_cast<float>(radius) * static_cast<float>(radius))
        : static_cast<int64_t>(side) * static_cast<int64_t>(side);

    // Small brushes feel like a normal paint brush. Huge brushes become
    // stamp tools; repeating them at 20 Hz creates identical store writes and
    // remesh storms with no visual benefit.
    if (approxFaceCells <= 4096)   return 0.05f;
    if (approxFaceCells <= 16384)  return 0.08f;
    if (approxFaceCells <= 65536)  return 0.12f;
    if (approxFaceCells <= 180000) return 0.18f;
    return 0.25f;
}

size_t TexturePaintTool::flushPendingTextureDirtyChunks(bool force) {
    if (!m_world || m_pendingTextureDirtyChunks.empty()) {
        return 0u;
    }

    const auto now = std::chrono::steady_clock::now();
    if (!force && m_lastTextureDirtyFlushTime.time_since_epoch().count() != 0) {
        const float elapsed = std::chrono::duration<float>(now - m_lastTextureDirtyFlushTime).count();
        if (elapsed < m_textureDirtyFlushIntervalSec &&
            static_cast<int>(m_pendingTextureDirtyChunks.size()) > m_immediateTextureDirtyChunkCap) {
            return 0u;
        }
    }

    TerrainEdit::TerrainEditOverlayStore::ChunkSet chunks;
    chunks.swap(m_pendingTextureDirtyChunks);
    const size_t flushed = chunks.size();

    // Canonical texture paint is baked into normal chunk material data.
    // This is deliberately not the live-stamp path: once face cells are written,
    // only the touched chunks are remeshed so Vertex::material becomes the same
    // fast path as default terrain material.
    m_world->markTextureMaterialsDirty(chunks);
    m_lastTextureDirtyFlushTime = now;
    return flushed;
}

void TexturePaintTool::recordPaintDiagnostic(const PaintOpDiagnostic& diag) {
    PaintOpDiagnostic stored = diag;
    stored.serial = ++m_paintDiagSerial;

    m_paintDiagHistory[static_cast<size_t>(m_paintDiagWriteIndex)] = stored;
    m_paintDiagWriteIndex = (m_paintDiagWriteIndex + 1) % PAINT_DIAG_HISTORY;
    if (m_paintDiagCount < PAINT_DIAG_HISTORY) {
        ++m_paintDiagCount;
    }
}

std::string TexturePaintTool::buildPaintDiagnosticsReport() const {
    std::string out;
    out.reserve(32768);

    auto appendLine = [&](const char* text) {
        out += text;
        out += '\n';
    };

    struct VisualStatus {
        float firstMs{0.0f};
        float completeMs{0.0f};
        int totalChunks{0};
        int readyChunks{0};
        int pendingChunks{0};
        uint64_t uploadBytes{0};
        bool complete{false};
    };

    auto sameCoord = [](const glm::ivec3& a, const glm::ivec3& b) {
        return a.x == b.x && a.y == b.y && a.z == b.z;
    };

    auto computeVisualStatus = [&](const PaintOpDiagnostic& d) {
        VisualStatus s{};
        s.totalChunks = static_cast<int>(d.visualChunks.size());
        s.pendingChunks = s.totalChunks;

        if (!m_world || d.visualChunks.empty() || d.visualStartSec <= 0.0) {
            return s;
        }

        std::vector<uint8_t> seen(d.visualChunks.size(), 0u);
        const auto& history = m_world->getChunkVisualHistory();
        const size_t scanCount = std::min(history.count, World::ChunkVisualHistory::CAPACITY);

        bool anyReady = false;
        for (size_t i = 0; i < scanCount; ++i) {
            const auto& e = history.getFromEnd(i);
            const double entrySec = static_cast<double>(e.timestampSec);

            // ChunkVisualHistory stores timestampSec as float seconds from the
            // same steady_clock epoch. Keep a small negative tolerance so float
            // quantization does not hide first-frame completions.
            if (entrySec + 0.020 < d.visualStartSec) {
                continue;
            }

            for (size_t chunkIdx = 0; chunkIdx < d.visualChunks.size(); ++chunkIdx) {
                if (seen[chunkIdx] != 0u || !sameCoord(e.chunkCoord, d.visualChunks[chunkIdx])) {
                    continue;
                }

                seen[chunkIdx] = 1u;
                ++s.readyChunks;
                s.uploadBytes += e.uploadBytes;

                float ms = static_cast<float>((entrySec - d.visualStartSec) * 1000.0);
                if (ms < 0.0f) ms = 0.0f;
                if (!anyReady) {
                    s.firstMs = ms;
                    s.completeMs = ms;
                    anyReady = true;
                } else {
                    s.firstMs = std::min(s.firstMs, ms);
                    s.completeMs = std::max(s.completeMs, ms);
                }
                break;
            }
        }

        s.pendingChunks = std::max(0, s.totalChunks - s.readyChunks);
        s.complete = s.totalChunks > 0 && s.readyChunks >= s.totalChunks;
        return s;
    };

    appendLine("VulkanVX Texture Paint Brush Diagnostics");
    appendLine("serial,total_ms,collect_ms,store_ms,cascade_ms,touched_ms,schedule_ms,"
               "visual_stamp,visual_first_ms,visual_complete_ms,visual_chunks_total,"
               "visual_chunks_ready,visual_chunks_pending,visual_complete,visual_upload_bytes,"
               "radius_voxels,radius_m,shape,material,variant,lod,faces,changed,unchanged,"
               "cascaded,touched_chunks,flushed_chunks,pending_chunks_after,hold_repeat,"
               "center_x,center_y,center_z");

    char line[1024];
    for (int i = 0; i < m_paintDiagCount; ++i) {
        const int idx = (m_paintDiagWriteIndex - m_paintDiagCount + i + PAINT_DIAG_HISTORY)
            % PAINT_DIAG_HISTORY;
        const PaintOpDiagnostic& d = m_paintDiagHistory[static_cast<size_t>(idx)];
        const VisualStatus visual = computeVisualStatus(d);

        std::snprintf(
            line, sizeof(line),
            "%llu,%.4f,%.4f,%.4f,%.4f,%.4f,%.4f,%u,%.4f,%.4f,%d,%d,%d,%d,%llu,%d,%.4f,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%.4f,%.4f,%.4f",
            static_cast<unsigned long long>(d.serial),
            d.totalMs,
            d.collectMs,
            d.storeMs,
            d.cascadeMs,
            d.touchedMs,
            d.scheduleMs,
            d.visualStampOrder,
            visual.firstMs,
            visual.completeMs,
            visual.totalChunks,
            visual.readyChunks,
            visual.pendingChunks,
            visual.complete ? 1 : 0,
            static_cast<unsigned long long>(visual.uploadBytes),
            d.radiusVoxels,
            d.radiusM,
            d.shape,
            d.material,
            d.variant,
            d.paintLod,
            d.facesCollected,
            d.cellsPainted,
            d.facesUnchanged,
            d.cellsCascaded,
            d.touchedChunks,
            d.flushedChunks,
            d.pendingChunksAfter,
            d.holdRepeat ? 1 : 0,
            d.center.x,
            d.center.y,
            d.center.z);
        appendLine(line);
    }

    return out;
}


// ---------------------------------------------------------------------------
// Hit / LOD resolution
// ---------------------------------------------------------------------------

void TexturePaintTool::resolveHitLOD() {
    m_hasPlacementContext = false;
    m_hitLod = 0;
    m_hitVoxelSizeM = WorldConfig::VOXEL_SIZE_M;
    if (!m_world || !m_hasHit) return;

    const auto ctx = m_world->getTerrainEditPlacementContext(m_hitPos);
    if (!ctx.valid) return;

    m_hasPlacementContext = true;
    m_hitLod        = ctx.previewLodLevel;
    m_hitVoxelSizeM = ctx.voxelSizeM;
}

int TexturePaintTool::getActivePaintLOD() const {
    if (m_lodMode == LODMode::Manual) return m_manualLod;
    return m_hasPlacementContext ? m_hitLod : 0;
}

int TexturePaintTool::getHitNormalAxis() const {
    const glm::vec3 a = glm::abs(m_hitNormal);
    if (a.y >= a.x && a.y >= a.z) return 1;
    if (a.x >= a.y && a.x >= a.z) return 0;
    return 2;
}

uint8_t TexturePaintTool::getHitFaceId() const {
    const int axis = getHitNormalAxis();
    const float sign = (axis == 0) ? m_hitNormal.x
                     : (axis == 1) ? m_hitNormal.y
                                   : m_hitNormal.z;
    return static_cast<uint8_t>(axis * 2 + (sign >= 0.0f ? 1 : 0));
}

bool TexturePaintTool::isSolidLOD0(const glm::ivec3& voxelCoord) const {
    if (!m_world) return false;
    const auto& field = m_world->getTerrainFieldSource();
    const int32_t scale = TerrainEdit::EDIT_CELLS_PER_VOXEL;
    const TerrainEdit::GridCoord sampleCoord(
        voxelCoord.x * scale + scale / 2,
        voxelCoord.y * scale + scale / 2,
        voxelCoord.z * scale + scale / 2);
    return field.sample(sampleCoord).value.solid;
}

int TexturePaintTool::collectExposedSurfaceFacesLOD0(
    std::vector<TextureOverlayStore::SurfaceFaceStamp>& outFaces) const {
    outFaces.clear();
    if (!m_world || !m_hasHit || m_radiusVoxelsLod0 <= 0) return 0;

    const float invVoxel = static_cast<float>(WorldConfig::VOXELS_PER_METER);
    const glm::vec3 insidePos = m_hitPos - m_hitNormal * 0.01f;
    const glm::ivec3 startVoxel(
        static_cast<int>(std::floor(insidePos.x * invVoxel)),
        static_cast<int>(std::floor(insidePos.y * invVoxel)),
        static_cast<int>(std::floor(insidePos.z * invVoxel)));
    const uint8_t startFace = getHitFaceId();

    auto isExposed = [&](const glm::ivec3& voxel, uint8_t face) {
        return isSolidLOD0(voxel) && !isSolidLOD0(voxel + faceNormalI(face));
    };

    const float radiusM = static_cast<float>(m_radiusVoxelsLod0) * WorldConfig::VOXEL_SIZE_M;
    const float radiusSq = radiusM * radiusM;

    auto insideBrush = [&](const glm::ivec3& voxel, uint8_t face) {
        const glm::vec3 center = faceCenterWorld(voxel, face);
        const glm::vec3 d = center - m_hitPos;

        if (m_shape == BrushShape::Sphere) {
            return glm::dot(d, d) <= radiusSq + 1e-5f;
        }

        return std::abs(d.x) <= radiusM + 1e-5f &&
               std::abs(d.y) <= radiusM + 1e-5f &&
               std::abs(d.z) <= radiusM + 1e-5f;
    };

    std::unordered_set<PaintFaceKey, PaintFaceKeyHash> emitted;
    emitted.reserve(static_cast<size_t>(std::min(
        MAX_SURFACE_BRUSH_FACES,
        std::max(64, m_radiusVoxelsLod0 * m_radiusVoxelsLod0 * 8))));

    auto appendFace = [&](const glm::ivec3& voxel, uint8_t face) -> bool {
        PaintFaceKey key{voxel, static_cast<uint8_t>(face % 6u)};
        if (emitted.find(key) != emitted.end()) {
            return true;
        }

        emitted.insert(key);
        outFaces.push_back(TextureOverlayStore::SurfaceFaceStamp{voxel, key.face});
        return static_cast<int>(outFaces.size()) < MAX_SURFACE_BRUSH_FACES;
    };

    // Exact small/medium-brush path:
    // sample local occupancy once, then collect every exposed face whose face
    // center is inside the 3D brush volume. This catches top, bottom, and
    // vertical voxel faces equally.
    {
        const int pad = m_radiusVoxelsLod0 + 2;
        const glm::ivec3 scanMin = startVoxel - glm::ivec3(pad);
        const glm::ivec3 scanMax = startVoxel + glm::ivec3(pad);
        const int dimX = scanMax.x - scanMin.x + 1;
        const int dimY = scanMax.y - scanMin.y + 1;
        const int dimZ = scanMax.z - scanMin.z + 1;
        const int64_t volume =
            static_cast<int64_t>(dimX) *
            static_cast<int64_t>(dimY) *
            static_cast<int64_t>(dimZ);

        if (dimX > 2 && dimY > 2 && dimZ > 2 &&
            volume > 0 && volume <= MAX_SURFACE_BRUSH_SCAN_VOXELS) {
            std::vector<uint8_t> solid(static_cast<size_t>(volume), 0u);

            auto indexOf = [&](const glm::ivec3& voxel) -> size_t {
                const int x = voxel.x - scanMin.x;
                const int y = voxel.y - scanMin.y;
                const int z = voxel.z - scanMin.z;
                return static_cast<size_t>((z * dimY + y) * dimX + x);
            };

            for (int z = scanMin.z; z <= scanMax.z; ++z) {
                for (int y = scanMin.y; y <= scanMax.y; ++y) {
                    for (int x = scanMin.x; x <= scanMax.x; ++x) {
                        const glm::ivec3 voxel(x, y, z);
                        solid[indexOf(voxel)] = isSolidLOD0(voxel) ? 1u : 0u;
                    }
                }
            }

            auto solidAt = [&](const glm::ivec3& voxel) -> bool {
                return solid[indexOf(voxel)] != 0u;
            };

            outFaces.reserve(static_cast<size_t>(std::min(
                MAX_SURFACE_BRUSH_FACES,
                std::max(64, m_radiusVoxelsLod0 * m_radiusVoxelsLod0 * 8))));

            const glm::ivec3 candidateMin = scanMin + glm::ivec3(1);
            const glm::ivec3 candidateMax = scanMax - glm::ivec3(1);

            for (int z = candidateMin.z; z <= candidateMax.z; ++z) {
                for (int y = candidateMin.y; y <= candidateMax.y; ++y) {
                    for (int x = candidateMin.x; x <= candidateMax.x; ++x) {
                        const glm::ivec3 voxel(x, y, z);
                        if (!solidAt(voxel)) {
                            continue;
                        }

                        for (uint8_t face = 0; face < 6; ++face) {
                            if (solidAt(voxel + faceNormalI(face)) ||
                                !insideBrush(voxel, face)) {
                                continue;
                            }

                            if (!appendFace(voxel, face)) {
                                return static_cast<int>(outFaces.size());
                            }
                        }
                    }
                }
            }

            return static_cast<int>(outFaces.size());
        }
    }

    // Large top-hit acceleration:
    // use heightmap columns to emit obvious top faces and simple heightmap
    // cliff sides. Do NOT return after this path. Edited voxel geometry and
    // non-heightmap vertical faces still need the connected crawl below.
    if (startFace == 3u) {
        const auto& heightmap = m_world->getHeightmapSampler();
        if (heightmap.isLoaded()) {
            const int pad = m_radiusVoxelsLod0 + 1;
            constexpr int kEditedSurfaceSearch = 24;

            outFaces.reserve(static_cast<size_t>(std::min(
                MAX_SURFACE_BRUSH_FACES,
                std::max(64, m_radiusVoxelsLod0 * m_radiusVoxelsLod0 * 8))));

            auto tryAddTopFace = [&](int x, int z, int centerY) -> bool {
                for (int delta = 0; delta <= kEditedSurfaceSearch; ++delta) {
                    const int signs[2] = {1, -1};
                    for (int signIdx = 0; signIdx < (delta == 0 ? 1 : 2); ++signIdx) {
                        const int y = centerY + delta * signs[signIdx];
                        const glm::ivec3 voxel(x, y, z);
                        if (!insideBrush(voxel, startFace) ||
                            !isExposed(voxel, startFace)) {
                            continue;
                        }
                        return appendFace(voxel, startFace);
                    }
                }
                return true;
            };

            auto addFaceIfValid = [&](const glm::ivec3& voxel, uint8_t face) -> bool {
                if (!insideBrush(voxel, face) || !isExposed(voxel, face)) {
                    return true;
                }
                return appendFace(voxel, face);
            };

            struct SideNeighbor {
                int dx;
                int dz;
                uint8_t face;
            };
            constexpr SideNeighbor sideNeighbors[4] = {
                {-1,  0, 0u},
                { 1,  0, 1u},
                { 0, -1, 4u},
                { 0,  1, 5u},
            };

            for (int z = startVoxel.z - pad; z <= startVoxel.z + pad; ++z) {
                for (int x = startVoxel.x - pad; x <= startVoxel.x + pad; ++x) {
                    if (static_cast<int>(outFaces.size()) >= MAX_SURFACE_BRUSH_FACES) {
                        return static_cast<int>(outFaces.size());
                    }

                    const int height = heightmap.getHeightAtVoxel(x, z);
                    const int heightTopY = height - 1;

                    bool keepGoing = tryAddTopFace(x, z, heightTopY);
                    if (!keepGoing) {
                        return static_cast<int>(outFaces.size());
                    }

                    if (std::abs(startVoxel.y - heightTopY) > kEditedSurfaceSearch) {
                        keepGoing = tryAddTopFace(x, z, startVoxel.y);
                        if (!keepGoing) {
                            return static_cast<int>(outFaces.size());
                        }
                    }

                    // Heightmap cliff/wall faces where this column is taller
                    // than a neighboring column.
                    for (const SideNeighbor& n : sideNeighbors) {
                        const int neighborHeight = heightmap.getHeightAtVoxel(x + n.dx, z + n.dz);
                        if (height <= neighborHeight) {
                            continue;
                        }

                        const int y0 = std::max(neighborHeight, startVoxel.y - pad);
                        const int y1 = std::min(height - 1, startVoxel.y + pad);
                        for (int y = y0; y <= y1; ++y) {
                            const glm::ivec3 voxel(x, y, z);
                            if (!addFaceIfValid(voxel, n.face)) {
                                return static_cast<int>(outFaces.size());
                            }
                        }
                    }
                }
            }
        }
    }

    // Connected exposed-surface fallback:
    // essential after the large heightmap path. The heightmap path is only an
    // acceleration for obvious top/cliff faces; this crawl catches edited voxel
    // walls, cut sides, holes, caves, and arbitrary vertical faces connected to
    // the hit surface.
    if (!isExposed(startVoxel, startFace) || !insideBrush(startVoxel, startFace)) {
        return static_cast<int>(outFaces.size());
    }

    std::queue<PaintFaceKey> pending;
    std::unordered_set<PaintFaceKey, PaintFaceKeyHash> visited;
    visited.reserve(static_cast<size_t>(std::min(
        MAX_SURFACE_BRUSH_FACES,
        std::max(64, m_radiusVoxelsLod0 * m_radiusVoxelsLod0 * 8))));

    const PaintFaceKey start{startVoxel, startFace};
    pending.push(start);
    visited.insert(start);

    constexpr glm::ivec3 kVoxelNeighbors[7] = {
        glm::ivec3(0, 0, 0),
        glm::ivec3( 1, 0, 0), glm::ivec3(-1, 0, 0),
        glm::ivec3( 0, 1, 0), glm::ivec3( 0,-1, 0),
        glm::ivec3( 0, 0, 1), glm::ivec3( 0, 0,-1),
    };

    while (!pending.empty() && static_cast<int>(outFaces.size()) < MAX_SURFACE_BRUSH_FACES) {
        const PaintFaceKey current = pending.front();
        pending.pop();

        if (!isExposed(current.voxel, current.face) ||
            !insideBrush(current.voxel, current.face)) {
            continue;
        }

        if (!appendFace(current.voxel, current.face)) {
            return static_cast<int>(outFaces.size());
        }

        for (const glm::ivec3& dv : kVoxelNeighbors) {
            const glm::ivec3 voxel = current.voxel + dv;
            for (uint8_t face = 0; face < 6; ++face) {
                PaintFaceKey next{voxel, face};
                if (visited.find(next) != visited.end()) continue;
                if (!insideBrush(voxel, face)) continue;
                if (!isExposed(voxel, face)) continue;
                visited.insert(next);
                pending.push(next);
            }
        }
    }

    return static_cast<int>(outFaces.size());
}

// ---------------------------------------------------------------------------
// Per-frame update
// ---------------------------------------------------------------------------

void TexturePaintTool::update(double mouseX, double mouseY,
                              int viewportW, int viewportH,
                              const glm::mat4& view, const glm::mat4& proj,
                              const glm::vec3& cameraPos,
                              bool leftClickPressed,
                              float viewportOffsetX, float viewportOffsetY) {
    if (!m_active || !m_raycastFn) {
        flushPendingTextureDirtyChunks(true);
        m_hasHit = false;
        m_hasPlacementContext = false;
        m_leftClickWasPressed = false;
        return;
    }

    const glm::mat4 invViewProj = glm::inverse(proj * view);
    const glm::vec3 rayDir = screenToWorldRay(mouseX, mouseY,
                                              viewportW, viewportH,
                                              invViewProj);

    glm::vec3 hitPos, hitNormal;
    m_hasHit = m_raycastFn(cameraPos, rayDir, 10000.0f, hitPos, hitNormal);

    if (m_hasHit) {
        m_hitPos = hitPos;
        m_hitNormal = hitNormal;
        resolveHitLOD();

        // Cache preview screen position for label rendering.
        const glm::vec4 clip = (proj * view) * glm::vec4(m_hitPos, 1.0f);
        if (clip.w > 0.01f) {
            const glm::vec3 ndc = glm::vec3(clip) / clip.w;
            m_previewScreenX = viewportOffsetX + (ndc.x * 0.5f + 0.5f) * static_cast<float>(viewportW);
            m_previewScreenY = viewportOffsetY + (1.0f - (ndc.y * 0.5f + 0.5f)) * static_cast<float>(viewportH);
        }
    }

    // Click + auto-repeat. The repeat interval now scales with brush footprint:
    // small brushes behave like paint; huge brushes behave like stamps so they
    // do not enqueue 20 huge authoring/rebake waves per second.
    if (leftClickPressed && m_hasHit) {
        const auto now = std::chrono::steady_clock::now();
        if (!m_leftClickWasPressed) {
            m_holdRepeatStarted = false;
            place();
            m_lastPlaceTime = now;
            m_holdStartTime = now;
        } else {
            const float held = std::chrono::duration<float>(now - m_holdStartTime).count();
            if (held >= 0.15f) {
                const float since = std::chrono::duration<float>(now - m_lastPlaceTime).count();
                const float repeatInterval = getPaintRepeatIntervalSec();
                if (since >= repeatInterval) {
                    m_holdRepeatStarted = true;
                    place();
                    m_lastPlaceTime = now;
                }
            }
        }

        // During long holds, flush large pending rebakes at a fixed low rate.
        // Small strokes flush immediately inside place().
        flushPendingTextureDirtyChunks(false);
    } else {
        // Mouse release / no hit: force the latest authored material to be
        // scheduled for rebake. This keeps visual latency bounded without
        // flooding remesh while the brush is actively spamming.
        if (m_leftClickWasPressed) {
            flushPendingTextureDirtyChunks(true);
        } else {
            flushPendingTextureDirtyChunks(false);
        }
        m_holdRepeatStarted = false;
    }

    m_leftClickWasPressed = leftClickPressed;
}

// ---------------------------------------------------------------------------
// Apply paint
// ---------------------------------------------------------------------------

void TexturePaintTool::place() {
    if (!m_hasHit) return;

    constexpr int lod = 0;
    TextureOverlayStore& store = getStore();
    const auto cfg = store.getLODConfig(lod);
    if (!cfg.enabled) {
        m_lastFacesCollected = 0;
        m_lastFacesUnchanged = 0;
        m_lastCellsPainted = 0;
        m_lastCellsCascaded = 0;
        return;
    }

    // Texture painting is authored world state. If the user paints while
    // viewing base terrain, branch into a normal editable snapshot first.
    if (m_world && !m_world->hasActiveEditableSnapshot()) {
        if (!m_world->createSnapshot()) {
            return;
        }
    }

    using Clock = std::chrono::steady_clock;
    const auto t0 = Clock::now();

    // Canonical fast path:
    // Store one permanent bounded surface-volume stamp instead of expanding a
    // max-radius brush into 200k+ per-face cells on the UI thread. The chunk
    // material rebake resolves this stamp through TextureOverlayStore::getSurfaceTexture()
    // while emitting the real exposed terrain faces, so lower LODs sample the
    // same canonical brush volume instead of relying on lossy LOD0 face mapping.
    const TextureOverlay::SurfaceBrushShape stampShape =
        (m_shape == BrushShape::Box)
            ? TextureOverlay::SurfaceBrushShape::Rect
            : TextureOverlay::SurfaceBrushShape::Disc;
    const uint8_t sourceFace = getHitFaceId();
    const uint32_t stampOrder = store.appendSurfacePaintStamp(
        m_hitPos,
        m_radiusVoxelsLod0,
        stampShape,
        m_textureType,
        m_variant,
        sourceFace);
    const auto tStore = Clock::now();

    if (stampOrder == 0u) {
        m_lastPaintMs = std::chrono::duration<float, std::milli>(tStore - t0).count();
        m_lastFacesCollected = 0;
        m_lastFacesUnchanged = 0;
        m_lastCellsPainted = 0;
        m_lastCellsCascaded = 0;
        m_lastPaintLod = lod;
        m_lastPaintWorldCenter = m_hitPos;
        m_lastPaintRadiusM = static_cast<float>(m_radiusVoxelsLod0) * WorldConfig::VOXEL_SIZE_M;
        m_previewStartTime = tStore;
        return;
    }

    // Dirty only the chunk material bake domain touched by the brush volume.
    // The extra pad does not paint extra texels; it only guarantees that coarse
    // LOD face cells and exact chunk-border faces that intersect the brush are
    // rebaked too.
    const int maxLodStep = 1 << std::max(0, TextureOverlayStore::LOD_COUNT - 1);
    const int dirtyPadVoxels = std::max(2, maxLodStep);
    const float invVoxel = static_cast<float>(WorldConfig::VOXELS_PER_METER);
    const glm::vec3 centerVoxel = m_hitPos * invVoxel;
    const float radiusVoxels = static_cast<float>(std::max(1, m_radiusVoxelsLod0));
    const float dirtyRadius = radiusVoxels + static_cast<float>(dirtyPadVoxels);

    const glm::ivec3 minVoxel(
        static_cast<int>(std::floor(centerVoxel.x - dirtyRadius)),
        static_cast<int>(std::floor(centerVoxel.y - dirtyRadius)),
        static_cast<int>(std::floor(centerVoxel.z - dirtyRadius)));
    const glm::ivec3 maxVoxel(
        static_cast<int>(std::ceil(centerVoxel.x + dirtyRadius)),
        static_cast<int>(std::ceil(centerVoxel.y + dirtyRadius)),
        static_cast<int>(std::ceil(centerVoxel.z + dirtyRadius)));

    const glm::ivec3 c0 = WorldConfig::microVoxelToChunk(minVoxel);
    const glm::ivec3 c1 = WorldConfig::microVoxelToChunk(maxVoxel);

    const glm::ivec3 minChunk(
        std::min(c0.x, c1.x),
        std::min(c0.y, c1.y),
        std::min(c0.z, c1.z));
    const glm::ivec3 maxChunk(
        std::max(c0.x, c1.x),
        std::max(c0.y, c1.y),
        std::max(c0.z, c1.z));

    TerrainEdit::TerrainEditOverlayStore::ChunkSet touchedChunks;
    if (m_world) {
        touchedChunks = m_world->collectExistingChunksInRange(minChunk, maxChunk);
    }

    std::vector<glm::ivec3> visualChunks;
    visualChunks.reserve(touchedChunks.size());
    for (const glm::ivec3& chunk : touchedChunks) {
        visualChunks.push_back(chunk);
        m_pendingTextureDirtyChunks.insert(chunk);
    }

    const int touchedChunkCount = static_cast<int>(touchedChunks.size());
    const auto tTouched = Clock::now();

    const bool immediateFlush =
        touchedChunkCount <= m_immediateTextureDirtyChunkCap ||
        !m_holdRepeatStarted;
    const size_t flushed = flushPendingTextureDirtyChunks(immediateFlush);
    const auto tSchedule = Clock::now();

    if (m_world) {
        m_world->markSnapshotDirty();
    }

    const int64_t side = static_cast<int64_t>(std::max(1, m_radiusVoxelsLod0)) * 2 + 1;
    const int64_t estimatedSurfaceCells64 = (m_shape == BrushShape::Sphere)
        ? static_cast<int64_t>(3.14159265 *
            static_cast<double>(m_radiusVoxelsLod0) *
            static_cast<double>(m_radiusVoxelsLod0))
        : side * side;
    const int estimatedSurfaceCells =
        static_cast<int>(std::min<int64_t>(std::max<int64_t>(1, estimatedSurfaceCells64), INT_MAX));

    m_lastPaintMs = std::chrono::duration<float, std::milli>(tSchedule - t0).count();
    m_lastFacesCollected = std::max(1, estimatedSurfaceCells);
    m_lastFacesUnchanged = 0;
    m_lastCellsPainted  = std::max(1, estimatedSurfaceCells);
    m_lastCellsCascaded = 0;
    m_lastPaintLod      = lod;
    m_lastPaintWorldCenter = m_hitPos;
    m_lastPaintRadiusM = static_cast<float>(m_radiusVoxelsLod0) * WorldConfig::VOXEL_SIZE_M;
    m_previewStartTime = tSchedule;

    PaintOpDiagnostic diag{};
    diag.radiusVoxels = m_radiusVoxelsLod0;
    diag.shape = static_cast<int>(m_shape);
    diag.material = static_cast<int>(m_textureType);
    diag.variant = static_cast<int>(m_variant);
    diag.paintLod = lod;
    diag.facesCollected = m_lastFacesCollected;
    diag.facesUnchanged = 0;
    diag.cellsPainted = m_lastCellsPainted;
    diag.cellsCascaded = 0;
    diag.touchedChunks = touchedChunkCount;
    diag.flushedChunks = static_cast<int>(flushed);
    diag.pendingChunksAfter = static_cast<int>(m_pendingTextureDirtyChunks.size());
    diag.collectMs = 0.0f;
    diag.storeMs = std::chrono::duration<float, std::milli>(tStore - t0).count();
    diag.cascadeMs = 0.0f;
    diag.touchedMs = std::chrono::duration<float, std::milli>(tTouched - tStore).count();
    diag.scheduleMs = std::chrono::duration<float, std::milli>(tSchedule - tTouched).count();
    diag.totalMs = m_lastPaintMs;
    diag.visualStartSec = std::chrono::duration<double>(t0.time_since_epoch()).count();
    diag.visualStampOrder = stampOrder;
    diag.visualChunks = std::move(visualChunks);
    diag.radiusM = m_lastPaintRadiusM;
    diag.center = m_hitPos;
    diag.holdRepeat = m_holdRepeatStarted;
    recordPaintDiagnostic(diag);
}

// ---------------------------------------------------------------------------
// Debug window
// ---------------------------------------------------------------------------

void TexturePaintTool::renderUI() {
    ImGui::TextColored(kAccent, "Texture Paint Brush");
    ImGui::Separator();

    ImGui::Checkbox("Brush Active", &m_active);
    if (!m_active) {
        ImGui::TextDisabled("Enable to paint pixel-art textures onto terrain");
    }

    // ---------------- Brush shape + radius ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "Brush");

    const bool isSphere = (m_shape == BrushShape::Sphere);
    if (isSphere) {
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.26f, 0.59f, 0.98f, 0.80f));
    }
    if (ImGui::Button("Sphere")) m_shape = BrushShape::Sphere;
    if (isSphere) ImGui::PopStyleColor();
    ImGui::SameLine();
    const bool isBox = (m_shape == BrushShape::Box);
    if (isBox) {
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.26f, 0.59f, 0.98f, 0.80f));
    }
    if (ImGui::Button("Box")) m_shape = BrushShape::Box;
    if (isBox) ImGui::PopStyleColor();

    ImGui::SetNextItemWidth(180.0f);
    int radius = m_radiusVoxelsLod0;
    if (ImGui::SliderInt("Radius (LOD-0 voxels)", &radius, 1, 1024)) {
        setBrushRadiusVoxels(radius);
    }
    ImGui::SetNextItemWidth(180.0f);
    radius = m_radiusVoxelsLod0;
    if (ImGui::InputInt("Custom radius (max 10240)", &radius, 64, 512)) {
        setBrushRadiusVoxels(radius);
    }
    radius = m_radiusVoxelsLod0;
    const int64_t side = static_cast<int64_t>(radius) * 2 + 1;
    const int64_t approxCells = (m_shape == BrushShape::Sphere)
        ? static_cast<int64_t>(3.14159265 * static_cast<double>(radius) * static_cast<double>(radius))
        : side * side;
    ImGui::TextDisabled("  -> %.3f m at LOD 0, approx %lld face cells, repeat %.0f ms",
                        radius * WorldConfig::VOXEL_SIZE_M,
                        static_cast<long long>(std::max<int64_t>(1, approxCells)),
                        getPaintRepeatIntervalSec() * 1000.0f);

    // ---------------- Texture type + variant ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "Material");
    static const char* TYPE_NAMES[] = { "Grass", "Mud", "Dirt", "Sand" };
    int typeIdx = static_cast<int>(m_textureType);
    if (ImGui::Combo("Type", &typeIdx, TYPE_NAMES, IM_ARRAYSIZE(TYPE_NAMES))) {
        m_textureType = static_cast<TextureType>(typeIdx);
    }

    int variant = m_variant;
    if (ImGui::SliderInt("Variant seed", &variant, 0, 7)) {
        setVariant(static_cast<uint8_t>(variant));
    }

    const auto& activeStyle = TextureOverlay::TextureBrushStyles::getStyle(m_textureType);
    ImGui::TextColored(kHeading, "Brush Style System");
    ImGui::TextDisabled("Design, preview colors, variants, and edge blending are owned by TextureBrushStyles.");

    auto showStyleColor = [](const char* label, const glm::vec3& color) {
        ImGui::ColorButton(label,
                           ImVec4(color.x, color.y, color.z, 1.0f),
                           ImGuiColorEditFlags_NoTooltip,
                           ImVec2(22.0f, 22.0f));
        ImGui::SameLine();
        ImGui::Text("%s: %.2f %.2f %.2f", label, color.x, color.y, color.z);
    };

    showStyleColor("Base", activeStyle.preview.base);
    showStyleColor("Highlight", activeStyle.preview.highlight);
    showStyleColor("Shadow", activeStyle.preview.shadow);
    showStyleColor("Accent", activeStyle.preview.accent);

    // ---------------- LOD targeting ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "LOD Targeting");

    int modeIdx = static_cast<int>(m_lodMode);
    ImGui::RadioButton("Auto from hit chunk", &modeIdx, 0); ImGui::SameLine();
    ImGui::RadioButton("Manual",              &modeIdx, 1);
    m_lodMode = static_cast<LODMode>(modeIdx);

    if (m_lodMode == LODMode::Manual) {
        int lod = m_manualLod;
        if (ImGui::SliderInt("Target LOD", &lod, 0,
                             TextureOverlayStore::LOD_COUNT - 1)) {
            setManualLOD(lod);
        }
    } else {
        if (m_hasPlacementContext) {
            ImGui::Text("Hit LOD: %d  (voxel %.3f m)", m_hitLod, m_hitVoxelSizeM);
        } else {
            ImGui::TextDisabled("Hover terrain to resolve hit LOD");
        }
    }

    ImGui::Checkbox("Cascade paint to coarser LODs", &m_cascadeToCoarser);
    if (ImGui::IsItemHovered()) {
        ImGui::SetTooltip("When enabled, the same stroke also paints into all "
                          "LOD levels coarser than the source LOD, so the "
                          "texture stays visible after a chunk LOD swap.");
    }
    ImGui::TextDisabled("Shader material preview LOD: %d (final pixels are shaded in terrain)",
                        getPreviewLOD());

    // ---------------- Per-LOD resolution config ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "Per-LOD Texture Resolution");
    ImGui::TextDisabled("Texels per voxel face edge.  Adapt: each LOD step "
                        "is 2x bigger voxels / 1/8 the cells.");

    if (ImGui::BeginTable("perLodCfg", 4,
                          ImGuiTableFlags_BordersInnerV |
                          ImGuiTableFlags_RowBg |
                          ImGuiTableFlags_SizingStretchProp)) {
        ImGui::TableSetupColumn("LOD", ImGuiTableColumnFlags_WidthFixed, 60.0f);
        ImGui::TableSetupColumn("Voxel size", ImGuiTableColumnFlags_WidthFixed, 90.0f);
        ImGui::TableSetupColumn("Resolution", ImGuiTableColumnFlags_WidthStretch);
        ImGui::TableSetupColumn("Enabled", ImGuiTableColumnFlags_WidthFixed, 80.0f);
        ImGui::TableHeadersRow();

        for (int lod = 0; lod < TextureOverlayStore::LOD_COUNT; ++lod) {
            ImGui::TableNextRow();
            ImGui::PushID(lod);

            ImGui::TableSetColumnIndex(0);
            ImGui::Text("LOD %d", lod);

            ImGui::TableSetColumnIndex(1);
            ImGui::Text("%.3f m", WorldConfig::getLODVoxelSizeM(lod));

            ImGui::TableSetColumnIndex(2);
            LODTextureConfig cfg = getStore().getLODConfig(lod);
            int currentIdx = 0;
            for (int i = 0; i < TextureOverlayStore::RES_OPTION_COUNT; ++i) {
                if (TextureOverlayStore::RES_OPTIONS[i] == cfg.pixelsPerVoxel) {
                    currentIdx = i; break;
                }
            }
            ImGui::SetNextItemWidth(-FLT_MIN);
            if (ImGui::Combo("##res",
                             &currentIdx,
                             TextureOverlayStore::RES_OPTION_LABELS,
                             TextureOverlayStore::RES_OPTION_COUNT)) {
                cfg.pixelsPerVoxel = TextureOverlayStore::RES_OPTIONS[currentIdx];
                getStore().setLODConfig(lod, cfg);
            }

            ImGui::TableSetColumnIndex(3);
            bool enabled = cfg.enabled;
            if (ImGui::Checkbox("##en", &enabled)) {
                cfg.enabled = enabled;
                getStore().setLODConfig(lod, cfg);
            }

            ImGui::PopID();
        }
        ImGui::EndTable();
    }

    // ---------------- Live hit preview info ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "Hit Preview");
    if (!m_hasHit) {
        ImGui::TextDisabled("Move mouse over terrain to preview");
    } else {
        ImGui::Text("Hit:    (%.3f, %.3f, %.3f)", m_hitPos.x, m_hitPos.y, m_hitPos.z);
        ImGui::Text("Normal: (%.2f, %.2f, %.2f)", m_hitNormal.x, m_hitNormal.y, m_hitNormal.z);
        const int lod = getActivePaintLOD();
        const auto cfg = getStore().getLODConfig(lod);
        ImGui::Text("Source paint LOD: 0 (%dx%d px/face)%s",
                    getStore().getLODConfig(0).pixelsPerVoxel,
                    getStore().getLODConfig(0).pixelsPerVoxel,
                    getStore().getLODConfig(0).enabled ? "" : "  [DISABLED]");
        ImGui::Text("Render-preview LOD: %d (%dx%d px/face)%s",
                    lod, cfg.pixelsPerVoxel, cfg.pixelsPerVoxel,
                    cfg.enabled ? "" : "  [DISABLED]");
        const int rLod = (m_radiusVoxelsLod0 + (1 << lod) - 1) >> lod;
        ImGui::Text("Effective surface radius: %d voxel%s at LOD %d",
                    rLod, rLod == 1 ? "" : "s", lod);
    }

    // ---------------- Last paint diagnostics ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "Last Paint");
    if (m_lastFacesCollected == 0 && m_lastCellsPainted == 0 && m_lastCellsCascaded == 0) {
        ImGui::TextDisabled("Left click to paint");
    } else {
        ImGui::Text("Surface faces found: %d", m_lastFacesCollected);
        ImGui::Text("Face cells changed: %d (LOD %d)", m_lastCellsPainted, m_lastPaintLod);
        if (m_lastFacesUnchanged > 0) {
            ImGui::TextDisabled("Unchanged/no-op: %d", m_lastFacesUnchanged);
        }
        if (m_cascadeToCoarser) {
            ImGui::Text("Cascaded to coarser: %d cells", m_lastCellsCascaded);
        }
        ImGui::Text("Time: %.3f ms", m_lastPaintMs);
        ImGui::TextDisabled("Pending material-rebake chunks: %zu", m_pendingTextureDirtyChunks.size());
    }

    if (ImGui::TreeNode("Last 50 Brush Edit Diagnostics")) {
        ImGui::TextDisabled("Copy this table when brush spam still tanks FPS.");
        if (ImGui::Button("Copy Last 50 Paint Stats")) {
            m_lastDiagnosticsExport = buildPaintDiagnosticsReport();
            ImGui::SetClipboardText(m_lastDiagnosticsExport.c_str());
        }
        if (!m_lastDiagnosticsExport.empty()) {
            ImGui::SameLine();
            ImGui::TextDisabled("copied %zu bytes", m_lastDiagnosticsExport.size());
        }

        if (ImGui::BeginTable("paintDiagHistory", 9,
                              ImGuiTableFlags_BordersInnerV |
                              ImGuiTableFlags_RowBg |
                              ImGuiTableFlags_SizingStretchProp)) {
            ImGui::TableSetupColumn("#", ImGuiTableColumnFlags_WidthFixed, 38.0f);
            ImGui::TableSetupColumn("total");
            ImGui::TableSetupColumn("collect");
            ImGui::TableSetupColumn("store");
            ImGui::TableSetupColumn("sched");
            ImGui::TableSetupColumn("faces");
            ImGui::TableSetupColumn("changed");
            ImGui::TableSetupColumn("flush");
            ImGui::TableSetupColumn("pending");
            ImGui::TableHeadersRow();

            for (int i = 0; i < m_paintDiagCount; ++i) {
                const int idx = (m_paintDiagWriteIndex - m_paintDiagCount + i + PAINT_DIAG_HISTORY)
                    % PAINT_DIAG_HISTORY;
                const PaintOpDiagnostic& d = m_paintDiagHistory[static_cast<size_t>(idx)];

                ImGui::TableNextRow();
                ImGui::TableSetColumnIndex(0); ImGui::Text("%llu", static_cast<unsigned long long>(d.serial));
                ImGui::TableSetColumnIndex(1); ImGui::Text("%.2f", d.totalMs);
                ImGui::TableSetColumnIndex(2); ImGui::Text("%.2f", d.collectMs);
                ImGui::TableSetColumnIndex(3); ImGui::Text("%.2f", d.storeMs);
                ImGui::TableSetColumnIndex(4); ImGui::Text("%.2f", d.scheduleMs);
                ImGui::TableSetColumnIndex(5); ImGui::Text("%d", d.facesCollected);
                ImGui::TableSetColumnIndex(6); ImGui::Text("%d", d.cellsPainted);
                ImGui::TableSetColumnIndex(7); ImGui::Text("%d", d.flushedChunks);
                ImGui::TableSetColumnIndex(8); ImGui::Text("%d", d.pendingChunksAfter);
            }
            ImGui::EndTable();
        }
        ImGui::TreePop();
    }

    // ---------------- Store stats ----------------
    ImGui::Spacing();
    ImGui::TextColored(kHeading, "Voxel Material Store");
    const auto stats = getStore().getStats();
    ImGui::Text("Total: %zu bricks  %zu cells  (gen %zu)",
                stats.totalBricks, stats.totalCells, stats.generation);
    if (ImGui::TreeNode("Per-LOD breakdown")) {
        for (int lod = 0; lod < TextureOverlayStore::LOD_COUNT; ++lod) {
            ImGui::Text("  LOD %d: %zu bricks  %zu cells",
                        lod, stats.bricksByLOD[lod], stats.cellsByLOD[lod]);
        }
        ImGui::TreePop();
    }

    ImGui::Spacing();
    if (ImGui::Button("Clear All Textures")) {
        getStore().clear();
    }
    ImGui::SameLine();
    if (ImGui::Button("Clear Hit LOD")) {
        getStore().clearLOD(getActivePaintLOD());
    }
    ImGui::SameLine();
    if (ImGui::Button("Copy Last 50 Paint Stats##bottom")) {
        m_lastDiagnosticsExport = buildPaintDiagnosticsReport();
        ImGui::SetClipboardText(m_lastDiagnosticsExport.c_str());
    }
}

// ---------------------------------------------------------------------------
// World-space brush preview
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// World-space brush preview
// ---------------------------------------------------------------------------

void TexturePaintTool::renderPreviewOverlay(const glm::mat4& viewProj,
                                            int viewportW, int viewportH,
                                            float viewportOffsetX,
                                            float viewportOffsetY) {
    if (!m_active || !m_hasHit) return;

    ImDrawList* dl = ImGui::GetForegroundDrawList();

    const ImU32 col = colorFromVec3(getTypeBaseColor(m_textureType));
    const ImU32 fill = (col & 0x00FFFFFFu) | 0x33000000u;

    const float vpW = static_cast<float>(viewportW);
    const float vpH = static_cast<float>(viewportH);

    auto project = [&](const glm::vec3& wp, ImVec2& out) -> bool {
        glm::vec4 c = viewProj * glm::vec4(wp, 1.0f);
        if (c.w <= 0.01f) return false;
        glm::vec3 ndc = glm::vec3(c) / c.w;
        out.x = viewportOffsetX + (ndc.x * 0.5f + 0.5f) * vpW;
        out.y = viewportOffsetY + (1.0f - (ndc.y * 0.5f + 0.5f)) * vpH;
        return true;
    };

    const int lod = getActivePaintLOD();
    const float voxelSize = WorldConfig::getLODVoxelSizeM(lod);
    const float radiusM = static_cast<float>(m_radiusVoxelsLod0) * WorldConfig::VOXEL_SIZE_M;

    if (m_shape == BrushShape::Sphere) {
        // 3 great circles, like TerrainEditTool's fallback path.
        auto drawCircle = [&](int a0, int a1, int aN) {
            constexpr int SEG = 32;
            ImVec2 pts[SEG]; bool vis[SEG];
            for (int i = 0; i < SEG; ++i) {
                float ang = 6.28318530f * float(i) / float(SEG);
                glm::vec3 p = m_hitPos;
                p[a0] = m_hitPos[a0] + std::cos(ang) * radiusM;
                p[a1] = m_hitPos[a1] + std::sin(ang) * radiusM;
                p[aN] = m_hitPos[aN];
                vis[i] = project(p, pts[i]);
            }
            for (int i = 0; i < SEG; ++i) {
                int j = (i + 1) % SEG;
                if (vis[i] && vis[j]) dl->AddLine(pts[i], pts[j], col, 2.0f);
            }
        };
        drawCircle(0, 1, 2);
        drawCircle(0, 2, 1);
        drawCircle(1, 2, 0);
    } else {
        // Box wireframe
        const glm::vec3 lo = m_hitPos - glm::vec3(radiusM);
        const glm::vec3 hi = m_hitPos + glm::vec3(radiusM);
        const glm::vec3 corners[8] = {
            {lo.x,lo.y,lo.z}, {hi.x,lo.y,lo.z},
            {hi.x,hi.y,lo.z}, {lo.x,hi.y,lo.z},
            {lo.x,lo.y,hi.z}, {hi.x,lo.y,hi.z},
            {hi.x,hi.y,hi.z}, {lo.x,hi.y,hi.z},
        };
        ImVec2 sp[8]; bool vis[8];
        for (int i = 0; i < 8; ++i) vis[i] = project(corners[i], sp[i]);
        constexpr int E[12][2] = {
            {0,1},{1,2},{2,3},{3,0},
            {4,5},{5,6},{6,7},{7,4},
            {0,4},{1,5},{2,6},{3,7}
        };
        for (auto& e : E) {
            if (vis[e[0]] && vis[e[1]]) dl->AddLine(sp[e[0]], sp[e[1]], col, 2.0f);
        }
        // Light fill on the dominant face for readability
        constexpr int F[6][4] = {
            {0,1,2,3},{4,5,6,7},
            {3,2,6,7},{0,1,5,4},
            {0,3,7,4},{1,2,6,5}
        };
        for (auto& f : F) {
            if (vis[f[0]] && vis[f[1]] && vis[f[2]] && vis[f[3]]) {
                dl->AddQuadFilled(sp[f[0]], sp[f[1]], sp[f[2]], sp[f[3]], fill);
            }
        }
    }

    // Mark the hit point
    ImVec2 hitScreen;
    if (project(m_hitPos, hitScreen)) {
        dl->AddCircleFilled(hitScreen, 3.0f, col);
        // Label: type + variant + active LOD + resolution
        const auto cfg = getStore().getLODConfig(lod);
        char label[160];
        std::snprintf(label, sizeof(label),
                      "%s v%u  LOD %d  %dx%d px/face  r=%.3fm  vox=%.3fm",
                      TextureOverlay::textureTypeName(m_textureType),
                      static_cast<unsigned>(m_variant),
                      lod,
                      cfg.pixelsPerVoxel, cfg.pixelsPerVoxel,
                      radiusM,
                      voxelSize);
        dl->AddText(ImVec2(hitScreen.x + 12.0f, hitScreen.y - 8.0f), col, label);
    }

    // Last-paint floating ms readout (fades over PREVIEW_FADE_SEC)
    if (m_lastPaintMs > 0.0f) {
        const float t = std::chrono::duration<float>(
            std::chrono::steady_clock::now() - m_previewStartTime).count();
        if (t < PREVIEW_FADE_SEC) {
            const float a = std::clamp(1.0f - t / PREVIEW_FADE_SEC, 0.0f, 1.0f);
            ImVec2 sp;
            if (project(m_lastPaintWorldCenter, sp)) {
                char ms[64];
                std::snprintf(ms, sizeof(ms), "+%d/%d cells (%.2f ms)",
                              m_lastCellsPainted, m_lastFacesCollected, m_lastPaintMs);
                ImU32 c = (col & 0x00FFFFFFu) |
                          (static_cast<uint32_t>(a * 255.0f) << 24);
                dl->AddText(ImVec2(sp.x, sp.y - 24.0f), c, ms);
            }
        }
    }
}

````

## include\ui\debug_menu\world\TexturePaintTool.h

Description: No CC-DESC found. C++ class 'World'.

````cpp
#pragma once

// GPT-DESC: Declares texture paint tool state, diagnostics, and debounced material rebake controls.

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <string>
#include <utility>
#include <vector>

#include <glm/glm.hpp>

#include "world/edit/TerrainEditTypes.h"
#include "world/edit/TerrainEditOverlayStore.h"
#include "world/edit/TextureOverlayStore.h"
#include "world/edit/texture/TextureBrushStyles.h"

class World;

// ---------------------------------------------------------------------------
// TexturePaintTool
//
// Paints pixel-art material IDs onto existing voxel faces.
//
// Current render architecture:
//   - The canonical authored paint data stays in TextureOverlayStore.
//   - The old GPU global material-overlay hash table is intentionally kept
//     empty because it was a terrain/light fragment bottleneck.
//   - Visible paint is produced by rebaking touched chunk material words into
//     the normal fast terrain vertex/material path.
//
// This tool therefore treats material rebakes as an asynchronous/debounced
// consequence of brush strokes. Brush authoring must stay responsive even for
// huge radii; chunk rebake scheduling is rate-limited so spam strokes do not
// flood runtime remeshing/upload/finalize.
// ---------------------------------------------------------------------------
class TexturePaintTool {
public:
    enum class BrushShape : int { Sphere = 0, Box = 1 };
    enum class LODMode    : int { AutoFromHit = 0, Manual = 1 };
    static constexpr int MAX_BRUSH_RADIUS_VOXELS = 10240;

    using RaycastFunc = std::function<bool(const glm::vec3& origin,
                                           const glm::vec3& dir,
                                           float maxDist,
                                           glm::vec3& outPos,
                                           glm::vec3& outNormal)>;

    TexturePaintTool() = default;

    // -------- Engine wiring --------
    void setWorld(World* world)        { m_world = world; }
    void setRaycastFunc(RaycastFunc f) { m_raycastFn = std::move(f); }

    // -------- State accessors --------
    bool isActive() const  { return m_active; }
    void setActive(bool a) { m_active = a; }
    TextureOverlay::TextureOverlayStore& getStore();
    const TextureOverlay::TextureOverlayStore& getStore() const;

    // -------- Per-frame update --------
    // Called from EngineRenderLoop. Performs raycast, updates preview, and
    // triggers paint on left-click with size-aware auto-repeat on hold.
    void update(double mouseX, double mouseY, int viewportW, int viewportH,
                const glm::mat4& view, const glm::mat4& proj,
                const glm::vec3& cameraPos, bool leftClickPressed,
                float viewportOffsetX = 0.0f, float viewportOffsetY = 0.0f);

    // -------- Rendering --------
    // ImGui debug window (full brush + per-LOD config + stats)
    void renderUI();
    // World-space brush preview drawn into the foreground draw list
    void renderPreviewOverlay(const glm::mat4& viewProj,
                              int viewportW, int viewportH,
                              float viewportOffsetX = 0.0f,
                              float viewportOffsetY = 0.0f);

    // -------- Brush parameters (read by UI + paint) --------
    int   getBrushRadiusVoxels() const { return m_radiusVoxelsLod0; }
    void  setBrushRadiusVoxels(int r)  {
        if (r < 1) r = 1;
        if (r > MAX_BRUSH_RADIUS_VOXELS) r = MAX_BRUSH_RADIUS_VOXELS;
        m_radiusVoxelsLod0 = r;
    }

    BrushShape getShape() const { return m_shape; }
    void setShape(BrushShape s) { m_shape = s; }

    TextureOverlay::TextureType getTextureType() const { return m_textureType; }
    void setTextureType(TextureOverlay::TextureType t) { m_textureType = t; }

    glm::vec3 getTypeBaseColor(TextureOverlay::TextureType t) const {
        return TextureOverlay::TextureBrushStyles::getStyle(t).preview.base;
    }
    glm::vec3 getTypeHighlightColor(TextureOverlay::TextureType t) const {
        return TextureOverlay::TextureBrushStyles::getStyle(t).preview.highlight;
    }
    glm::vec3 getTypeShadowColor(TextureOverlay::TextureType t) const {
        return TextureOverlay::TextureBrushStyles::getStyle(t).preview.shadow;
    }
    glm::vec3 getTypeAccentColor(TextureOverlay::TextureType t) const {
        return TextureOverlay::TextureBrushStyles::getStyle(t).preview.accent;
    }

    uint8_t getVariant() const { return m_variant; }
    void setVariant(uint8_t v) { m_variant = v & 0x7u; }

    LODMode getLODMode() const { return m_lodMode; }
    void setLODMode(LODMode m) { m_lodMode = m; }

    int  getManualLOD() const { return m_manualLod; }
    void setManualLOD(int l) {
        if (l < 0) l = 0;
        if (l > TextureOverlay::TextureOverlayStore::LOD_COUNT - 1)
            l = TextureOverlay::TextureOverlayStore::LOD_COUNT - 1;
        m_manualLod = l;
    }

    // Debug preview LOD only; final paint is sampled by the terrain shader.
    int getPreviewLOD() const {
        return (m_lodMode == LODMode::Manual)
            ? m_manualLod
            : (m_hasPlacementContext ? m_hitLod : 0);
    }

    bool getCascadeEnabled() const { return m_cascadeToCoarser; }
    void setCascadeEnabled(bool b) { m_cascadeToCoarser = b; }

private:
    struct PaintOpDiagnostic {
        uint64_t serial{0};

        int radiusVoxels{0};
        int shape{0};
        int material{0};
        int variant{0};
        int paintLod{0};

        int facesCollected{0};
        int facesUnchanged{0};
        int cellsPainted{0};
        int cellsCascaded{0};
        int touchedChunks{0};
        int flushedChunks{0};
        int pendingChunksAfter{0};

        float collectMs{0.0f};
        float storeMs{0.0f};
        float cascadeMs{0.0f};
        float touchedMs{0.0f};
        float scheduleMs{0.0f};
        float totalMs{0.0f};

        // End-to-end visual latency diagnostic support.
        // Texture paint is authored immediately, but visible material still
        // lands later via chunk rebake/upload/finalize. These fields let the
        // export compute author->visible latency by correlating touched chunks
        // against World::ChunkVisualHistory without touching World internals.
        double visualStartSec{0.0};
        uint32_t visualStampOrder{0};
        std::vector<glm::ivec3> visualChunks;

        float radiusM{0.0f};
        glm::vec3 center{0.0f};
        bool holdRepeat{false};
    };

    void place();                  // Apply paint at current preview center
    void resolveHitLOD();          // Update m_hitLod from World context
    int  getActivePaintLOD() const;
    int  getHitNormalAxis() const;
    uint8_t getHitFaceId() const;
    bool isSolidLOD0(const glm::ivec3& voxelCoord) const;
    int collectExposedSurfaceFacesLOD0(
        std::vector<TextureOverlay::TextureOverlayStore::SurfaceFaceStamp>& outFaces) const;

    float getPaintRepeatIntervalSec() const;
    size_t flushPendingTextureDirtyChunks(bool force);
    void recordPaintDiagnostic(const PaintOpDiagnostic& diag);
    std::string buildPaintDiagnosticsReport() const;

    // -------- State --------
    bool m_active{false};

    // Brush params
    int m_radiusVoxelsLod0{4};
    BrushShape m_shape{BrushShape::Sphere};
    TextureOverlay::TextureType m_textureType{TextureOverlay::TextureType::Grass};
    uint8_t m_variant{0};
    // LOD selection
    LODMode m_lodMode{LODMode::AutoFromHit};
    int m_manualLod{0};
    bool m_cascadeToCoarser{false};

    // Hit state
    bool m_hasHit{false};
    glm::vec3 m_hitPos{0.0f};
    glm::vec3 m_hitNormal{0.0f, 1.0f, 0.0f};
    int  m_hitLod{0};
    float m_hitVoxelSizeM{::WorldConfig::VOXEL_SIZE_M};
    bool m_hasPlacementContext{false};

    // Click + repeat
    bool m_leftClickWasPressed{false};
    bool m_holdRepeatStarted{false};
    std::chrono::steady_clock::time_point m_lastPlaceTime{};
    std::chrono::steady_clock::time_point m_holdStartTime{};

    // Material-rebake scheduling. Authored paint is written immediately, but
    // chunk rebakes are debounced/rate-limited so huge brush spam cannot flood
    // the remesh/upload/finalize pipeline.
    TerrainEdit::TerrainEditOverlayStore::ChunkSet m_pendingTextureDirtyChunks;
    std::chrono::steady_clock::time_point m_lastTextureDirtyFlushTime{};
    float m_textureDirtyFlushIntervalSec{0.18f};
    int m_immediateTextureDirtyChunkCap{4};

    // Last-paint diagnostics
    static constexpr int PAINT_DIAG_HISTORY = 50;
    std::array<PaintOpDiagnostic, PAINT_DIAG_HISTORY> m_paintDiagHistory{};
    int m_paintDiagWriteIndex{0};
    int m_paintDiagCount{0};
    uint64_t m_paintDiagSerial{0};
    std::string m_lastDiagnosticsExport;

    int    m_lastFacesCollected{0};
    int    m_lastFacesUnchanged{0};
    int    m_lastCellsPainted{0};
    int    m_lastCellsCascaded{0};
    int    m_lastPaintLod{0};
    float  m_lastPaintMs{0.0f};
    glm::vec3 m_lastPaintWorldCenter{0.0f};
    float  m_lastPaintRadiusM{0.0f};
    std::chrono::steady_clock::time_point m_previewStartTime{};

    // Preview screen position (cached for label drawing)
    float m_previewScreenX{0.0f};
    float m_previewScreenY{0.0f};

    // Engine wiring
    World* m_world{nullptr};
    RaycastFunc m_raycastFn;
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
