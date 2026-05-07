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
        return 0;
    }

    const auto now = std::chrono::steady_clock::now();
    const size_t pending = m_pendingTextureDirtyChunks.size();
    const size_t immediateCap = static_cast<size_t>(std::max(0, m_immediateTextureDirtyChunkCap));

    if (!force && pending > immediateCap) {
        // Debounce repeated large brush stamps, but never delay the first dirty
        // wave.  The old path initialized m_lastTextureDirtyFlushTime and
        // returned 0, so a max-radius click could be authored in <0.1 ms while
        // its visible material rebake was not even scheduled until a later
        // timer/release flush.
        if (m_lastTextureDirtyFlushTime.time_since_epoch().count() != 0) {
            const float elapsed = std::chrono::duration<float>(
                now - m_lastTextureDirtyFlushTime).count();
            if (elapsed < m_textureDirtyFlushIntervalSec) {
                return 0;
            }
        }
    }

    TerrainEdit::TerrainEditOverlayStore::ChunkSet toFlush;
    toFlush.swap(m_pendingTextureDirtyChunks);

    // Material-only rebuild request. World::markTextureMaterialsDirty is kept
    // responsible for scheduler ownership, but we debounce repeated large calls
    // here so brush spam cannot enqueue a full remesh/upload wave every 50 ms.
    // The first dirty wave is still sent immediately so the screen catches up
    // as soon as the remesh/upload/finalize pipeline can process it.
    m_world->markTextureMaterialsDirty(toFlush);
    m_lastTextureDirtyFlushTime = now;
    return toFlush.size();
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

    const int lod = 0;
    const auto cfg = getStore().getLODConfig(lod);
    if (!cfg.enabled) {
        m_lastFacesCollected = 0;
        m_lastFacesUnchanged = 0;
        m_lastCellsPainted = 0;
        m_lastCellsCascaded = 0;
        return;
    }

    using Clock = std::chrono::steady_clock;
    const auto t0 = Clock::now();

    const uint8_t sourceFace = getHitFaceId();
    const float invVoxel = static_cast<float>(WorldConfig::VOXELS_PER_METER);

    // The ray hit is a floating point position on a large greedy quad.  Anchor
    // the authoring stamp to the exact owning LOD0 voxel face centre so the
    // brush, store, and rebake path all share one deterministic coordinate
    // contract:
    //     owning solid voxel + face id -> snapped face centre.
    const glm::vec3 insidePos = m_hitPos - m_hitNormal * 0.01f;
    const glm::ivec3 ownerVoxel(
        static_cast<int>(std::floor(insidePos.x * invVoxel)),
        static_cast<int>(std::floor(insidePos.y * invVoxel)),
        static_cast<int>(std::floor(insidePos.z * invVoxel)));
    const glm::vec3 stampCenterWorld = faceCenterWorld(ownerVoxel, sourceFace);

    // One interactive click becomes one deferred 3D surface-volume stamp.
    // During chunk material rebake, every exposed face whose face centre is
    // inside this sphere/box can sample the stamp.  This keeps O(1) authoring
    // while fixing large brushes on uneven terrain: the brush is no longer
    // locked to only the height/plane where the mouse was clicked.
    const TextureOverlay::SurfaceBrushShape stampShape = (m_shape == BrushShape::Sphere)
        ? TextureOverlay::SurfaceBrushShape::Disc
        : TextureOverlay::SurfaceBrushShape::Rect;
    const uint32_t stampOrder = getStore().appendSurfacePaintStamp(
        stampCenterWorld,
        m_radiusVoxelsLod0,
        stampShape,
        m_textureType,
        m_variant,
        sourceFace);

    const auto tStore = Clock::now();

    std::vector<glm::ivec3> visualChunks;
    int touchedChunkCount = 0;
    if (m_world && stampOrder != 0u) {
        const glm::vec3 centerVoxel = stampCenterWorld * invVoxel;
        const float r = static_cast<float>(std::max(1, m_radiusVoxelsLod0)) + 2.0f;

        const glm::ivec3 mn(
            static_cast<int>(std::floor(centerVoxel.x - r)),
            static_cast<int>(std::floor(centerVoxel.y - r)),
            static_cast<int>(std::floor(centerVoxel.z - r)));
        const glm::ivec3 mx(
            static_cast<int>(std::ceil(centerVoxel.x + r)),
            static_cast<int>(std::ceil(centerVoxel.y + r)),
            static_cast<int>(std::ceil(centerVoxel.z + r)));

        const glm::ivec3 c0 = WorldConfig::microVoxelToChunk(mn);
        const glm::ivec3 c1 = WorldConfig::microVoxelToChunk(mx);

        const int minX = std::min(c0.x, c1.x);
        const int maxX = std::max(c0.x, c1.x);
        const int minY = std::min(c0.y, c1.y);
        const int maxY = std::max(c0.y, c1.y);
        const int minZ = std::min(c0.z, c1.z);
        const int maxZ = std::max(c0.z, c1.z);
        const int64_t reserveCount =
            static_cast<int64_t>(maxX - minX + 1) *
            static_cast<int64_t>(maxY - minY + 1) *
            static_cast<int64_t>(maxZ - minZ + 1);
        if (reserveCount > 0 && reserveCount <= 4096) {
            visualChunks.reserve(static_cast<size_t>(reserveCount));
        }

        // Dirty the complete 3D stamp volume.  The previous face-plane dirty
        // clamp only rebuilt the chunk slice on the clicked plane, so surfaces
        // above/below the hit height could correctly match the brush but never
        // receive a material rebake.
        for (int z = minZ; z <= maxZ; ++z) {
            for (int y = minY; y <= maxY; ++y) {
                for (int x = minX; x <= maxX; ++x) {
                    const glm::ivec3 chunkCoord(x, y, z);
                    m_pendingTextureDirtyChunks.insert(chunkCoord);
                    visualChunks.push_back(chunkCoord);
                }
            }
        }
        touchedChunkCount = static_cast<int>(visualChunks.size());
    }

    const auto tTouched = Clock::now();

    // First click/stamp must schedule the visual material rebake immediately.
    // Only held-repeat stamps are debounced, because those are the ones that
    // can create redundant remesh waves while the user drags or holds LMB.
    const bool forceVisualFlush = !m_holdRepeatStarted;
    const size_t flushed = flushPendingTextureDirtyChunks(forceVisualFlush);
    const auto tSchedule = Clock::now();

    const int logicalChanged = (stampOrder != 0u) ? 1 : 0;
    m_lastPaintMs = std::chrono::duration<float, std::milli>(tSchedule - t0).count();
    m_lastFacesCollected = logicalChanged;  // now means stamp commands authored
    m_lastFacesUnchanged = 0;
    m_lastCellsPainted  = logicalChanged;
    m_lastCellsCascaded = 0;
    m_lastPaintLod      = lod;
    m_lastPaintWorldCenter = stampCenterWorld;
    m_lastPaintRadiusM = static_cast<float>(m_radiusVoxelsLod0) * WorldConfig::VOXEL_SIZE_M;
    m_previewStartTime = tSchedule;

    PaintOpDiagnostic diag{};
    diag.radiusVoxels = m_radiusVoxelsLod0;
    diag.shape = static_cast<int>(m_shape);
    diag.material = static_cast<int>(m_textureType);
    diag.variant = static_cast<int>(m_variant);
    diag.paintLod = lod;
    diag.facesCollected = logicalChanged;
    diag.facesUnchanged = 0;
    diag.cellsPainted = logicalChanged;
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
    diag.center = stampCenterWorld;
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
    if (ImGui::SliderInt("Radius (LOD-0 voxels)", &radius, 1, 256)) {
        setBrushRadiusVoxels(radius);
    }
    const int side = radius * 2 + 1;
    const int approxCells = (m_shape == BrushShape::Sphere)
        ? static_cast<int>(3.14159265f * radius * radius)
        : side * side;
    ImGui::TextDisabled("  -> %.3f m at LOD 0, approx %d face cells, repeat %.0f ms",
                        radius * WorldConfig::VOXEL_SIZE_M,
                        std::max(1, approxCells),
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

    const size_t activeIdx = static_cast<size_t>(m_textureType);
    ImGui::TextColored(kHeading, "Type Color Parameters");
    ImGui::SetNextItemWidth(220.0f);
    ImGui::ColorEdit3("Base", &m_typeBaseColors[activeIdx].x);
    ImGui::SetNextItemWidth(220.0f);
    ImGui::ColorEdit3("Highlight", &m_typeHighlightColors[activeIdx].x);
    ImGui::SetNextItemWidth(220.0f);
    ImGui::ColorEdit3("Shadow", &m_typeShadowColors[activeIdx].x);
    ImGui::SetNextItemWidth(220.0f);
    ImGui::ColorEdit3("Accent", &m_typeAccentColors[activeIdx].x);

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
    uint32_t ver = TEXTURE_OVERLAY_VERSION;
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
    return f.good();
}

bool TextureOverlayStore::loadFromFile(const char* path) {
    std::ifstream f(path, std::ios::binary);
    if (!f.is_open()) return false;

    uint32_t magic = 0, ver = 0;
    f.read(reinterpret_cast<char*>(&magic), sizeof(magic));
    f.read(reinterpret_cast<char*>(&ver), sizeof(ver));
    if (magic != TEXTURE_OVERLAY_MAGIC || ver > TEXTURE_OVERLAY_VERSION) return false;

    uint32_t lodCount = 0;
    f.read(reinterpret_cast<char*>(&lodCount), sizeof(lodCount));
    if (lodCount > LOD_COUNT) return false;

    std::unique_lock lock(m_mutex);
    for (auto& m : m_lodMaps) m.clear();
    m_brickCountsByLOD.fill(0u);
    m_cellCountsByLOD.fill(0u);

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

    // Deferred material stamps are bounded 3D surface volumes.  They are still
    // O(1) to author, but during chunk rebake every real exposed face whose
    // centre lies inside this volume may sample the latest stamp.  This fixes
    // large brushes over uneven terrain: top faces, side faces, and cut faces
    // at different heights are all covered by the same brush volume instead of
    // only the clicked face plane.
    //
    // The +2 voxel pad keeps chunk indexing conservative at exact integer face
    // planes and LOD boundaries.  sampleSurfacePaintStampsLocked() performs the
    // exact sphere/box test before returning material.
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

    // No GPU overlay upload: chunk material rebake consumes the stamp store.
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
    out.clear();
    if (maxStamps == 0u) {
        return 0u;
    }

    std::shared_lock lock(m_mutex);
    const size_t total = m_surfacePaintStamps.size();
    const size_t count = std::min(total, maxStamps);
    const size_t first = total - count;

    out.reserve(count);
    for (size_t i = first; i < total; ++i) {
        out.push_back(m_surfacePaintStamps[i]);
    }
    return out.size();
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

    const int step = (lod > 0) ? (1 << lod) : 1;
    const int halfStep = step / 2;
    const glm::ivec3 lod0Base = lodToLOD0(lodCoord, lod);
    const glm::ivec3 lod0Sample = lod0Base + glm::ivec3(halfStep);
    const glm::ivec3 chunk = WorldConfig::microVoxelToChunk(lod0Sample);

    auto it = m_surfacePaintStampChunkIndex.find(chunk);
    if (it == m_surfacePaintStampChunkIndex.end()) {
        return {};
    }

    const uint8_t queryFace = static_cast<uint8_t>(face % 6u);
    const int queryAxis = queryFace / 2;
    const float querySign = (queryFace & 1u) ? 1.0f : -1.0f;

    glm::vec3 faceCenter = glm::vec3(lod0Sample) + glm::vec3(0.5f);
    faceCenter[queryAxis] += querySign * 0.5f;

    const auto& candidates = it->second;
    for (auto rit = candidates.rbegin(); rit != candidates.rend(); ++rit) {
        const uint32_t stampIndex = *rit;
        if (stampIndex >= m_surfacePaintStamps.size()) {
            continue;
        }

        const SurfacePaintStamp& stamp = m_surfacePaintStamps[stampIndex];
        if (!surfaceStampTouchesBox(stamp, lod0Sample, lod0Sample)) {
            continue;
        }

        // Large paint brushes must behave like 3D surface-volume brushes, not
        // face-plane decals.  The mesher only asks for real exposed faces, so
        // testing the exposed face centre against the bounded stamp volume is
        // enough to cover uneven terrain while avoiding the old through-slab
        // vertical projection artifact.
        const glm::vec3 d = faceCenter - stamp.centerVoxelLod0;
        const float lodPad = std::max(1.0f, 0.51f * static_cast<float>(std::max(1, step)));
        const float radius = static_cast<float>(stamp.radiusVoxelsLod0) + lodPad;

        bool inside = false;
        if (stamp.shape == SurfaceBrushShape::Disc) {
            inside = glm::dot(d, d) <= radius * radius;
        } else {
            auto absf = [](float v) { return v < 0.0f ? -v : v; };
            inside = absf(d.x) <= radius &&
                     absf(d.y) <= radius &&
                     absf(d.z) <= radius;
        }

        if (!inside) {
            continue;
        }

        return VoxelTextureData(
            stamp.type,
            variedVariant(lod0Sample, queryFace, stamp.type, stamp.variant),
            0u,
            queryFace);
    }

    return {};
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

    comp.collVerts   = comp.currentArtifact.vertices;
    comp.collIndices = comp.currentArtifact.indices;

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

        ChunkDebugAttribution debugInfo{};
        debugInfo.artifactSource = ChunkArtifactSource::RuntimeEditBuild;
        debugInfo.collisionSource = ChunkCollisionSource::EditMeshPacked;
        debugInfo.workModel = ChunkWorkModel::MonolithicChunk;
        debugInfo.meshMode = static_cast<uint8_t>(ChunkMeshMode::MonolithicEdited);
        debugInfo.artifactCacheResident = true;
        debugInfo.fromTerrainEdit = true;
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

            {
                auto t0Upload = Clock::now();
                world->enqueueMeshForUpload(
                    comp.entity,
                    std::vector<MeshData>{},
                    /*mainSubChunkCount=*/0,
                    /*fromTerrainEdit=*/true,
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
        diag.vertexCount += static_cast<uint32_t>(comp.collVerts.size());
        diag.indexCount  += static_cast<uint32_t>(comp.collIndices.size());

        {
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
                /*fromTerrainEdit=*/true,
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

## shaders\terrain\cube.frag

Description: No CC-DESC found. C++ struct 'PointLight'.

````glsl
#version 450

// Input from vertex shader
layout(location = 0) in vec3 fragWorldPos;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec3 fragColor;
layout(location = 3) in float fragAOLevel;
layout(location = 4) in vec2 fragUV;
layout(location = 5) in flat vec3 fragChunkOrigin;  // Chunk origin for voxel coord calculation
layout(location = 6) in flat uint fragFace;
layout(location = 7) in flat vec3 fragFlatPos;  // Provoking vertex pos (constant per tri)
layout(location = 8) in flat float fragFlatAO;  // Provoking vertex AO (constant per tri)
layout(location = 9) in flat uint fragMaterial;

// Output
layout(location = 0) out vec4 outColor;

// Point light structure definition
struct PointLight {
    vec4 positionRadius;  // xyz = position, w = radius
    vec4 colorIntensity;  // xyz = color, w = intensity
};

const uint MAX_SHADER_LIGHTS = 32u;
const uint MAX_SUN_SHADOW_CASCADES = 6u;
const float LIGHT_GRID_WORLD_METERS = 0.250;
const float LIGHT_GRID_CELLS = 16.0;
const float LIGHT_GRID_CELL_SIZE = LIGHT_GRID_WORLD_METERS / LIGHT_GRID_CELLS; // 1/64m
const float LIGHT_GRID_PHASE_OFFSET_CELLS = 0.5;
const float MIN_LIGHT_BRIGHTNESS = 0.0030;
const float MIN_SHADOW_BRIGHTNESS = 0.0200;

// Lighting data storage buffer (SSBO for >32 light capacity)
layout(std430, set = 0, binding = 2) readonly buffer LightingData {
    // Directional light (sun/moon)
    vec4 sunDirection;      // xyz = direction, w = intensity
    vec4 sunColor;          // xyz = color, w = ambient strength

    // Sky/atmosphere
    vec4 skyColor;          // xyz = sky color, w = fog density

    // Point lights
    PointLight pointLights[4096];

    uint numPointLights;
    float time;
    uint _pad0;
    uint _pad1;

    // Per-light pulse data (synced with glow billboard system on CPU)
    // x = pulseStrength, y = breathScale, z = flickerAmount, w = flickerSpeed
    vec4 lightPulseData[4096];
} lighting;

// Camera position for fog calculation
layout(set = 0, binding = 3) uniform CameraData {
    vec3 cameraPos;
} camera;

// AO settings for real-time adjustment
layout(set = 0, binding = 4) uniform AOData {
    // Voxel AO (7 vec4s)
    vec4 brightnessLevels;    // x=L0, y=L1, z=L2, w=L3
    vec4 shadowTint0;         // xyz=tint0, w=aoPowerCurve
    vec4 shadowTint1;         // xyz=tint1, w=aoPixelSize
    vec4 shadowTint2;         // xyz=tint2, w=bandThreshold1
    vec4 shadowTint3;         // xyz=tint3, w=bandThreshold2
    vec4 ditherWarmTint;      // xyz=ditherWarmTint, w=debugFlags
    vec4 scatterAmounts;      // x=light, y=medium, z=dark, w=unused
    // DCCM AO (5 vec4s)
    vec4 dccmBrightness;      // 4 brightness bands
    vec4 dccmShadowTintCfg;   // xyz=tint, w=powerCurve
    vec4 dccmConfig;          // x=pixelSize, y=ditherStrength, z=wireThickness, w=flags
    vec4 dccmFillCol;         // xyz=fill color
    vec4 dccmLineCol;         // xyz=line color
} ao;

// Shadow data SSBO (sun + point light shadow matrices and config + diagnostics)
layout(std430, set = 0, binding = 5) buffer ShadowData {
    mat4 sunLightVP[MAX_SUN_SHADOW_CASCADES];
    vec4 sunCascadeParams[MAX_SUN_SHADOW_CASCADES]; // x=halfExtent, y=texelMeters
    vec4 sunDirTexelSize;            // xyz=sun direction (towards ground), w=world-space texel footprint
    vec4 shadowConfig;           // x=sunEnabled, y=pointEnabled, z=sunMapSize, w=pointMapSize
    vec4 shadowConfig2;          // x=sunCascadeCount, y=cascadeBlendFrac, z=maxCastRadius
    vec4 pointShadowInfo[32];    // x=near, y=far, z=radius, w=enabled
    // x=pointShadowSamples, y=lightEvalFragments, z=fullyOccludedFragments, w=litContribFragments
    uvec4 pointShadowDiag[32];
    vec4 diagConfig;             // x=enableDetailedDiagnostics
    // Sky enclosure: x=intensity, y=minAmbient, z=probeMaxHeight (m), w=mode (0=off,1=on,2=visualize)
    vec4 skyEnclosureParams;
    // Sky-vis static heightmap mapping:
    // x=worldOriginXMeters, y=worldOriginZMeters,
    // z=metersPerTexel (square), w=valueScaleToWorldYMeters (0 = disabled)
    vec4 skyHeightmapInfo;
} shadow;

// Shadow map samplers (hardware depth comparison, nearest filter for pixel art)
layout(set = 0, binding = 6) uniform sampler2DArrayShadow sunShadowMap;
layout(set = 0, binding = 7) uniform samplerCubeArrayShadow pointShadowMaps;

// Clustered lighting bitmask SSBO
layout(std430, set = 0, binding = 8) readonly buffer ClusterData {
    uvec4 clusterGridDims;   // x=tilesX, y=tilesY, z=numSlices, w=totalClusters
    vec4  clusterZParams;    // x=near, y=far, z=logRatio, w=sliceScale
    vec4  clusterTileDims;   // x=tileSizeX, y=tileSizeY, z=screenW, w=screenH
    uint  clusterLightMasks[];
} clusters;

// Sky-vis static heightmap (sun-independent zenith occlusion source).
// Stored as R16F voxel-height values; multiply by shadow.skyHeightmapInfo.w to get world Y in meters.
layout(set = 0, binding = 9) uniform sampler2D skyHeightmap;

struct MaterialOverlayCell {
    int x;
    int y;
    int z;
    uint face;
    uint material;
};

// Sparse LOD0 voxel-face material edits. Capacity is a power-of-two open
// addressing table built on the CPU; material == 0 marks an empty slot.
layout(std430, set = 0, binding = 10) readonly buffer MaterialOverlayData {
    uint capacityMask;
    uint count;
    uint maxProbe;
    uint _pad;
    MaterialOverlayCell cells[];
} materialOverlay;

// ═══════════════════════════════════════════════════════════════════════════
// Shared includes: dither/hash/noise utilities, lighting, shadow sampling
// ═══════════════════════════════════════════════════════════════════════════
#include "../common/dither_utils.glsl"
#include "../common/shadow_sampling.glsl"
#include "../common/clustered_lighting.glsl"
#include "../common/sky_enclosure.glsl"

vec3 materialBase(uint type) {
    if (type == 1u) return vec3(0.33, 0.24, 0.20); // mud
    if (type == 2u) return vec3(0.50, 0.38, 0.28); // dirt
    if (type == 3u) return vec3(0.79, 0.64, 0.43); // sand
    return vec3(0.30, 0.55, 0.16);                 // grass
}

vec3 materialHi(uint type) {
    if (type == 1u) return vec3(0.48, 0.37, 0.29);
    if (type == 2u) return vec3(0.66, 0.52, 0.39);
    if (type == 3u) return vec3(0.94, 0.82, 0.58);
    return vec3(0.52, 0.72, 0.22);
}

vec3 materialLo(uint type) {
    if (type == 1u) return vec3(0.18, 0.12, 0.10);
    if (type == 2u) return vec3(0.30, 0.22, 0.16);
    if (type == 3u) return vec3(0.58, 0.44, 0.28);
    return vec3(0.16, 0.29, 0.08);
}

vec3 materialAccent(uint type) {
    if (type == 1u) return vec3(0.13, 0.10, 0.09);
    if (type == 2u) return vec3(0.22, 0.20, 0.18);
    if (type == 3u) return vec3(0.68, 0.55, 0.36);
    return vec3(0.60, 0.82, 0.24);
}

uint hashMaterialOverlayKey(ivec3 voxel, uint face) {
    uint h = uint(voxel.x) * 0x9E3779B9u;
    h ^= uint(voxel.y) * 0x85EBCA6Bu;
    h ^= uint(voxel.z) * 0xC2B2AE35u;
    h ^= (face & 0x7u) * 0x27D4EB2Du;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    return h;
}
uint lookupMaterialOverlay(vec3 worldPos, vec3 normal, uint face, uint fallbackMaterial) {
    const uint MATERIAL_OVERLAY_CHUNK_HINT_BIT = 0x40000000u;
    const uint MATERIAL_PACKED_BIT = 0x80000000u;
    const uint MATERIAL_LIVE_STAMP_CAPACITY = 64u;
    const uint MATERIAL_LIVE_STAMP_CELL_STRIDE = 3u;
    const uint MATERIAL_LIVE_STAMP_CELL_CAPACITY =
        MATERIAL_LIVE_STAMP_CAPACITY * MATERIAL_LIVE_STAMP_CELL_STRIDE;

    uint cleanFallbackMaterial = fallbackMaterial & ~MATERIAL_OVERLAY_CHUNK_HINT_BIT;

    uint baselineMaterial = cleanFallbackMaterial;
    if ((baselineMaterial & MATERIAL_PACKED_BIT) == 0u) {
        uint type = (face == 3u) ? 0u : 2u;

        float h = worldPos.y;
        float biomeNoise = hash12(floor(worldPos.xz * 0.0625));
        if (face == 3u) {
            if (h < 5.0) {
                type = 3u;
            } else if (biomeNoise > 0.82) {
                type = 1u;
            }
        }

        uint variant = uint(hash12(floor(worldPos.xz * 0.25) + vec2(float(face) * 13.0, 71.0)) * 8.0) & 0x7u;
        uint edge = 0u;
        uint resLog2 = 4u;
        baselineMaterial = MATERIAL_PACKED_BIT | type | (variant << 2u) | (edge << 5u) | (resLog2 << 7u);
    }

    // Instant texture-brush path:
    //
    // Binding 10 still has the old layout, but cells[] now begins with a tiny
    // fixed live-stamp prefix. Each big brush click uploads one compact stamp;
    // the shader tests newest stamps first, then falls back to baked material.
    const uint liveStampCount = min(materialOverlay._pad, MATERIAL_LIVE_STAMP_CAPACITY);
    if (liveStampCount > 0u) {
        ivec3 voxel = ivec3(floor(worldPos * 4.0 - normal * 0.01));

        const uint queryFace = face & 0x7u;
        const uint queryAxis = queryFace / 2u;
        const float querySign = ((queryFace & 1u) != 0u) ? 1.0 : -1.0;

        vec3 faceCenter = vec3(voxel) + vec3(0.5);
        faceCenter[int(queryAxis)] += querySign * 0.5;

        for (uint reverseIdx = 0u; reverseIdx < liveStampCount; ++reverseIdx) {
            const uint stampIdx = liveStampCount - 1u - reverseIdx;
            const uint cellBase = stampIdx * MATERIAL_LIVE_STAMP_CELL_STRIDE;

            MaterialOverlayCell c0 = materialOverlay.cells[cellBase + 0u];
            MaterialOverlayCell c1 = materialOverlay.cells[cellBase + 1u];
            MaterialOverlayCell c2 = materialOverlay.cells[cellBase + 2u];

            ivec3 bboxMin = ivec3(c1.x, c1.y, c1.z);
            ivec3 bboxMax = ivec3(c2.x, c2.y, c2.z);
            if (voxel.x < bboxMin.x || voxel.y < bboxMin.y || voxel.z < bboxMin.z ||
                voxel.x > bboxMax.x || voxel.y > bboxMax.y || voxel.z > bboxMax.z) {
                continue;
            }

            vec3 center = vec3(
                intBitsToFloat(c0.x),
                intBitsToFloat(c0.y),
                intBitsToFloat(c0.z));
            float radius = uintBitsToFloat(c0.face) + 1.0;

            vec3 d = faceCenter - center;
            bool inside = false;
            if ((c1.face & 0xFFu) == 0u) {
                inside = dot(d, d) <= radius * radius;
            } else {
                inside = abs(d.x) <= radius &&
                         abs(d.y) <= radius &&
                         abs(d.z) <= radius;
            }

            if (!inside) {
                continue;
            }

            uint type = c2.face & 0x3u;
            uint variantSeed = c2.material & 0x7u;
            uint variant = (variantSeed + (hashMaterialOverlayKey(voxel, queryFace) & 0x7u)) & 0x7u;
            uint edge = 0u;
            uint resLog2 = 4u;
            return MATERIAL_PACKED_BIT | type | (variant << 2u) | (edge << 5u) | (resLog2 << 7u);
        }
    }

    if (materialOverlay.count == 0u || materialOverlay.capacityMask == 0u) {
        return baselineMaterial;
    }

    uint capacity = materialOverlay.capacityMask + 1u;

    if ((materialOverlay.count * 4u) > (capacity * 3u)) {
        return baselineMaterial;
    }

    if ((fallbackMaterial & MATERIAL_OVERLAY_CHUNK_HINT_BIT) == 0u) {
        return baselineMaterial;
    }

    const uint probeLimit = min(materialOverlay.maxProbe, 8u);

    ivec3 voxel = ivec3(floor(worldPos * 4.0 - normal * 0.01));
    uint idx = hashMaterialOverlayKey(voxel, face) & materialOverlay.capacityMask;

    for (uint probe = 0u; probe <= probeLimit; ++probe) {
        MaterialOverlayCell cell =
            materialOverlay.cells[MATERIAL_LIVE_STAMP_CELL_CAPACITY + idx];

        if (cell.material == 0u) {
            return baselineMaterial;
        }

        if (cell.x == voxel.x &&
            cell.y == voxel.y &&
            cell.z == voxel.z &&
            (cell.face & 0x7u) == (face & 0x7u)) {
            return cell.material;
        }

        uint residentIdeal = hashMaterialOverlayKey(
            ivec3(cell.x, cell.y, cell.z),
            cell.face & 0x7u) & materialOverlay.capacityMask;
        uint residentProbe = (idx - residentIdeal) & materialOverlay.capacityMask;
        if (residentProbe < probe) {
            return baselineMaterial;
        }

        idx = (idx + 1u) & materialOverlay.capacityMask;
    }

    return baselineMaterial;
}

vec3 sampleVoxelMaterial(vec3 worldPos, uint face, uint material, vec3 fallbackColor) {
    if ((material & 0x80000000u) == 0u) {
        return fallbackColor;
    }

    uint type = material & 0x3u;
    uint variant = (material >> 2) & 0x7u;
    uint edge = (material >> 5) & 0x3u;
    uint resLog2 = clamp((material >> 7) & 0xFu, 1u, 10u);
    float res = float(1u << resLog2);

    vec2 faceCoord = faceCell(worldPos * 4.0 + vec3(0.0001), face);
    vec2 voxelCoord = floor(faceCoord);
    vec2 uv = fract(faceCoord);
    vec2 texel = floor(uv * res);
    vec2 seed = voxelCoord * vec2(37.0, 71.0)
              + texel
              + vec2(float(variant) * 19.0 + float(type) * 113.0,
                     float(face) * 29.0);

    float n0 = hash12(seed);
    float n1 = hash12(seed + vec2(13.7, 91.3));
    float n2 = hash12(floor(texel * 0.25) + voxelCoord + vec2(float(type) * 17.0, float(variant) * 43.0));

    vec3 base = materialBase(type);
    vec3 hi = materialHi(type);
    vec3 lo = materialLo(type);
    vec3 accent = materialAccent(type);
    vec3 color = base;

    if (type == 0u) {
        float blade = step(0.56, n0) * step(0.22, fract((texel.y + n2 * 5.0) * 0.5));
        float darkBlade = step(0.78, n1);
        color = mix(color, hi, blade * 0.55);
        color = mix(color, lo, darkBlade * 0.35);
        color = mix(color, accent, step(0.92, n2) * 0.25);
    } else if (type == 1u) {
        float wet = smoothstep(0.28, 0.86, n2);
        float puddle = step(0.82, n0) * step(0.45, n1);
        color = mix(color, lo, wet * 0.45);
        color = mix(color, hi, (1.0 - wet) * 0.20);
        color = mix(color, vec3(0.10, 0.08, 0.07), puddle * 0.45);
    } else if (type == 2u) {
        float grain = (n0 - 0.5) * 0.20;
        float pebble = step(0.90, n1);
        float grassBit = step(0.965, n2);
        color = color + vec3(grain);
        color = mix(color, accent, pebble * 0.35);
        color = mix(color, materialHi(0u), grassBit * 0.30);
    } else {
        float fine = (n0 - 0.5) * 0.16;
        float dune = smoothstep(0.35, 0.95, n2) * 0.18;
        float speck = step(0.91, n1);
        color = color + vec3(fine + dune);
        color = mix(color, lo, speck * 0.22);
    }

    if (edge != 0u) {
        float d = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
        float edgeBand = 1.0 - smoothstep(0.035, 0.16, d);
        float ragged = step(0.48, hash12(seed + vec2(211.0, 53.0)));
        if (edge == 1u) {          // leafy/poky
            color = mix(color, materialHi(0u), edgeBand * ragged * 0.45);
        } else if (edge == 2u) {   // sloppy/muddy
            color = mix(color, materialLo(1u), edgeBand * (0.45 + 0.35 * n1));
        } else {                   // grainy/sandy/dirt scatter
            color = mix(color, materialHi(3u), edgeBand * ragged * 0.35);
            color = mix(color, materialLo(2u), edgeBand * step(0.78, n1) * 0.25);
        }
    }

    return clamp(color, vec3(0.02), vec3(1.0));
}

float sampleAOPixelCenter(float aoValue, vec2 faceCoord, vec2 cellCenterCoord) {
    vec2 faceDx = dFdx(faceCoord);
    vec2 faceDy = dFdy(faceCoord);
    float aoDx = dFdx(aoValue);
    float aoDy = dFdy(aoValue);

    vec2 deltaFace = cellCenterCoord - faceCoord;
    float det = faceDx.x * faceDy.y - faceDx.y * faceDy.x;
    if (abs(det) < 1e-6) {
        return aoValue;
    }

    vec2 deltaScreen = vec2(
        (deltaFace.x * faceDy.y - deltaFace.y * faceDy.x) / det,
        (faceDx.x * deltaFace.y - faceDx.y * deltaFace.x) / det);
    return aoValue + aoDx * deltaScreen.x + aoDy * deltaScreen.y;
}

void main() {
    vec3 normal = normalize(fragNormal);
    uint material = lookupMaterialOverlay(fragWorldPos, normal, fragFace, fragMaterial);
    vec3 baseColor = sampleVoxelMaterial(fragWorldPos, fragFace, material, fragColor);
    
    uint face = fragFace;
    if (face == 6u) {
        discard;
    }

    // === VOXEL TERRAIN ===
    // Keep AO driven by the mesher's interpolated per-vertex field only.
    // That preserves the "tri-side" wedge created by the chosen quad diagonal,
    // while the shader just pixelates and bands it on a world-aligned grid.
    float AO_PIXEL_SIZE = ao.shadowTint1.w;
    float aoPixelRes = clamp(ao.scatterAmounts.w, 1.0, 64.0);

    vec2 faceVoxelCoord = faceCell(fragWorldPos * 4.0 + vec3(0.0001), face);
    vec2 voxelPixelCoord = floor(faceVoxelCoord * aoPixelRes + 0.0001);
    vec2 voxelPixelCenter = (voxelPixelCoord + 0.5) / aoPixelRes;

    float aoInterp = clamp(fragAOLevel, 0.0, 1.0);
    float aoCell = clamp(sampleAOPixelCenter(aoInterp, faceVoxelCoord, voxelPixelCenter), 0.0, 1.0);
    float aoInset = mix(0.010, 0.032, smoothstep(0.35, 1.0, aoCell));
    float aoLevel = max(aoCell - aoInset, 0.0);
    float originalAO = aoLevel;
    vec3 aoMul = vec3(1.0);

    if (aoLevel > 0.0) {
        float aoPowerCurve = max(ao.shadowTint0.w, 0.0001);
        aoLevel = pow(aoLevel, aoPowerCurve);

        float brightnessBands[4] = float[](
            ao.brightnessLevels.x,
            ao.brightnessLevels.y,
            ao.brightnessLevels.z,
            ao.brightnessLevels.w
        );
        vec3 shadowTints[4] = vec3[](
            ao.shadowTint0.xyz,
            ao.shadowTint1.xyz,
            ao.shadowTint2.xyz,
            ao.shadowTint3.xyz
        );
        vec3 ditherWarmTint = ao.ditherWarmTint.xyz;

        float bandThreshold1 = max(ao.shadowTint2.w, 0.0001);
        float bandThreshold2 = max(ao.shadowTint3.w, bandThreshold1 + 0.0001);
        float midRange = max(bandThreshold2 - bandThreshold1, 0.0001);
        float darkRange = max(1.0 - bandThreshold2, 0.0001);

        float scaledAO;
        if (aoLevel < bandThreshold1) {
            scaledAO = aoLevel / bandThreshold1;
        } else if (aoLevel < bandThreshold2) {
            scaledAO = 1.0 + (aoLevel - bandThreshold1) / midRange;
        } else {
            scaledAO = 2.0 + (aoLevel - bandThreshold2) / darkRange;
        }
        scaledAO = clamp(scaledAO, 0.0, 3.0);

        int lowerBand = int(floor(scaledAO));
        int upperBand = min(lowerBand + 1, 3);
        float bandPosition = fract(scaledAO);

        float ordered = bayerDither8x8(mod(voxelPixelCoord, 8.0));
        float chunky = segaDither2x2(voxelPixelCoord);
        float scatterStrength = ao.scatterAmounts.x;
        if (aoLevel >= bandThreshold2) {
            scatterStrength = ao.scatterAmounts.z;
        } else if (aoLevel >= bandThreshold1) {
            scatterStrength = ao.scatterAmounts.y;
        }
        float scatter = (hash12(voxelPixelCoord + vec2(17.0, 59.0)) - 0.5) * scatterStrength;
        float transition = clamp(ordered + scatter, 0.0, 1.0);
        int selectedBand = (transition < bandPosition) ? upperBand : lowerBand;

        float brightness = brightnessBands[selectedBand];
        vec3 tint = shadowTints[selectedBand];

        float warmMask = step(0.5, chunky) * smoothstep(0.32, 1.0, aoLevel);
        tint *= mix(vec3(1.0), ditherWarmTint, warmMask * 0.35);

        // Apply the stylized shading wherever the mesher actually says AO exists,
        // including the lighter one-side corners that were getting suppressed.
        float aoCoverage = smoothstep(0.01, 0.16, originalAO);
        aoMul = mix(vec3(1.0), vec3(brightness) * tint, aoCoverage);
        baseColor *= aoMul;
    }
    
    // Start with ambient lighting - base illumination for all faces.
    // Modulate by "sky enclosure" so deep pits / cave mouths / behind-wall
    // pixels genuinely darken (places sky light cannot reach). Open ground
    // is unaffected because raw enclosure ≈ 0 there → multiplier = 1.
    // The same multiplier is applied to the sun-direct contribution below
    // so a fragment that is geometrically buried gets darkened regardless
    // of whether the sun shadow map happens to cover it (this fixes the
    // sun-area-fade leak that lit caves in the distance).
    // Compute sun shadow up-front so the enclosure can react to actual sun
    // reach ("shadows dissolve from light"). Cheap: same shadow tap we'd do
    // anyway, just hoisted above the visualize/ambient block.
    float sunIntensity = shadow.shadowConfig.x;
    float sunShadow = 1.0;
    if (lighting.sunDirection.w > 0.0 && sunIntensity > 0.001) {
        float rawShadow = sampleSunShadow(fragWorldPos, normal);
        float sunAreaRadius = shadow.diagConfig.z;
        if (sunAreaRadius > 0.0) {
            float horizDist = length(fragWorldPos.xz - camera.cameraPos.xz);
            float fade = smoothstep(sunAreaRadius * 0.85, sunAreaRadius, horizDist);
            rawShadow = mix(rawShadow, 1.0, fade);
        }
        sunShadow = 1.0 - sunIntensity * (1.0 - rawShadow);
    }
    float sunReach = sunShadow * lighting.sunDirection.w;

    // Ambient enclosure: NO dissolve → deep holes go fully dark at night /
    // when no sun rays hit them, exactly as before.
    float ambientEnclosure = computeSkyEnclosure(fragWorldPos, normal, 0.0);
    vec3 finalColor = baseColor * lighting.sunColor.xyz * lighting.sunColor.w * ambientEnclosure;
    if (skyEnclosureVisualizeEnabled()) {
        outColor = vec4(skyEnclosureDebugColor(fragWorldPos, normal, sunReach), 1.0);
        return;
    }

    // Add directional light (sun/moon) with face-aware adjustment
    // Since we have proper AO now, we use softer directional shading
    // to avoid side faces being too dark
    if (lighting.sunDirection.w > 0.0) {
        // Sun shadow debug visualization (diagConfig.y > 0)
        vec4 sunDebug = debugSunShadow(fragWorldPos, normal);
        if (sunDebug.a > 0.5) {
            outColor = sunDebug;
            return;
        }

        vec3 sunDir = normalize(lighting.sunDirection.xyz);
        float rawDiffuse = max(dot(normal, -sunDir), 0.0);

        // Lift the minimum diffuse to 0.5 so side faces aren't too dark
        // This gives a more stylized, cartoon look that works with our AO
        float liftedDiffuse = mix(0.5, 1.0, rawDiffuse);

        // Still apply quantization for retro look
        float diffuse = quantizeLight(liftedDiffuse, 5);

        // Sun-direct enclosure: dissolved by sunReach. Where the sun hits,
        // the geometric darkness softens (red → yellow / green) which
        // simulates light dispelling shadow without losing the deep-hole
        // darkness in unreached areas.
        float sunEnclosure = computeSkyEnclosure(fragWorldPos, normal, sunReach);
        finalColor += baseColor * lighting.sunColor.xyz * diffuse * lighting.sunDirection.w * sunShadow * sunEnclosure;
    }
    
    // Add point lights (accumulate-then-quantize for organic light blending)
    float totalBrightnessRawForAO = 0.0;

    // Night-only cosmetic: screen-space edge dithering fades out during day
    // where ambient sunlight makes dissolve patterns look unnatural.
    float nightStrength = 1.0 - smoothstep(0.08, 0.45, lighting.sunDirection.w);

    {
        vec3  totalLightContrib = vec3(0.0);
        vec3  totalLightContribRaw = vec3(0.0);
        float totalBrightness  = 0.0;
        float totalBrightnessRaw = 0.0;
        float peakBrightnessRaw = 0.0;
        float shadowEvidence = 0.0;
        float pulseEvidence = 0.0;
        bool diagEnabled = (shadow.diagConfig.x > 0.5);
        uvec2 diagPix = uvec2(gl_FragCoord.xy);
        bool diagSample = diagEnabled && ((diagPix.x & 7u) == 0u) && ((diagPix.y & 7u) == 0u);
        const uint DIAG_SCALE = 64u;
        // --- Clustered lighting: fetch bitmask for this fragment's cluster ---
        uint clTileX = min(uint(gl_FragCoord.x / clusters.clusterTileDims.x), clusters.clusterGridDims.x - 1u);
        uint clTileY = min(uint(gl_FragCoord.y / clusters.clusterTileDims.y), clusters.clusterGridDims.y - 1u);
        float clViewDist = length(fragWorldPos - camera.cameraPos);
        uint clSlice = clamp(uint(log(clViewDist / clusters.clusterZParams.x) * clusters.clusterZParams.w),
                             0u, clusters.clusterGridDims.z - 1u);
        uint clIdx = (clTileY * clusters.clusterGridDims.x + clTileX) * clusters.clusterGridDims.z + clSlice;
        uint lightMask = (clIdx < clusters.clusterGridDims.w) ? clusters.clusterLightMasks[clIdx] : 0u;

        while (lightMask != 0u) {
            uint i = findLSB(lightMask);
            lightMask &= lightMask - 1u; // clear lowest set bit

            vec4 contrib = calculatePointLightSmooth(fragWorldPos, normal, baseColor, i, face);
            if (contrib.a <= MIN_LIGHT_BRIGHTNESS) continue;
            if (diagSample && i < 32u) {
                atomicAdd(shadow.pointShadowDiag[i].y, DIAG_SCALE);
            }
            totalLightContribRaw += contrib.rgb;
            totalBrightnessRaw += contrib.a;
            peakBrightnessRaw = max(peakBrightnessRaw, contrib.a);
            float pulseStrengthNow = clamp(lighting.lightPulseData[i].x, 0.0, 1.0);
            float breathPulseNow = clamp((lighting.lightPulseData[i].y - 1.0) / 0.45, 0.0, 1.0);
            pulseEvidence = max(pulseEvidence, max(pulseStrengthNow, breathPulseNow));

            // Check point light shadow before expensive light calculation
            float ptShadow = 1.0;
            if (shadow.shadowConfig.y > 0.0 &&
                i < 32u &&
                shadow.pointShadowInfo[i].w > 0.0 &&
                contrib.a >= MIN_SHADOW_BRIGHTNESS) {
                if (diagSample) {
                    atomicAdd(shadow.pointShadowDiag[i].x, DIAG_SCALE);
                }
                ptShadow = samplePointShadow(fragWorldPos, normal, i);
                shadowEvidence = max(shadowEvidence, 1.0 - ptShadow);
                if (ptShadow <= 0.0) {
                    if (diagSample) {
                        atomicAdd(shadow.pointShadowDiag[i].z, DIAG_SCALE);
                    }
                    continue; // Fully shadowed, skip this light
                }
            }

            totalLightContrib += contrib.rgb * ptShadow;
            totalBrightness  += contrib.a * ptShadow;
            if (diagSample && i < 32u) {
                atomicAdd(shadow.pointShadowDiag[i].w, DIAG_SCALE);
            }
        }

        totalBrightnessRawForAO = totalBrightness;

        if (totalBrightnessRaw > 0.001) {
            // Bring back pixel-art pulse styling, but only near strong orb cores.
            float clampedBright = clamp(totalBrightness, 0.0, 2.0);
            float clampedBrightRaw = clamp(totalBrightnessRaw, 0.0, 2.0);
            bool verticalFaceLighting = (face != 2u && face != 3u);
            float shadowOcclusion = 0.0;
            if (clampedBrightRaw > 0.001) {
                shadowOcclusion = clamp(1.0 - (clampedBright / clampedBrightRaw), 0.0, 1.0);
            }

            // Light-eats-shadow: only RECEIVED light (post shadow-map)
            // washes out shadows. Stepped for retro feel — bright light
            // punches through cast shadows in discrete bands, not smooth.
            {
                float brightNorm = clamp(clampedBright / 2.0, 0.0, 1.0);
                // 3-level step: shadow fully eaten above ~0.33 brightness
                float shadowEat = floor(brightNorm * 3.0 + 0.5) / 3.0;
                float shadowRetain = 1.0 - shadowEat;

                totalLightContrib = mix(totalLightContribRaw, totalLightContrib, shadowRetain);
                clampedBright = mix(clampedBrightRaw, clampedBright, shadowRetain);
                shadowEvidence *= shadowRetain;
                shadowOcclusion *= shadowRetain;
            }

            if (clampedBright > 0.0001) {
                const float LIGHT_BANDS = 8.0;
                const float MAX_STYLIZED_BRIGHT = 2.0;
                if (verticalFaceLighting) {
                    // Stabilize vertical bands against tiny per-fragment shadow variance.
                    clampedBright = floor(clampedBright * 256.0 + 0.5) / 256.0;
                }

                // Slightly bias high brightness upward so bright bands feel more dominant
                // without breaking accumulate-then-quantize light merging.
                float brightNorm = clamp(clampedBright / MAX_STYLIZED_BRIGHT, 0.0, 1.0);
                float brightBoost = smoothstep(0.35, 1.0, brightNorm) * 0.24;
                float bandInput = min(clampedBright * (1.0 + brightBoost), MAX_STYLIZED_BRIGHT);

                // Keep 8 visible light bands (excluding pure darkness), then handle
                // the final dissolve-to-dark separately so there is no dead strip.
                float scaledRaw = (bandInput / MAX_STYLIZED_BRIGHT) * LIGHT_BANDS;
                float quantScaled = clamp(max(scaledRaw, 1.0), 1.0, LIGHT_BANDS);
                float baseBandIdx = floor(quantScaled - 1.0);
                float bandFrac = fract(quantScaled);

                float loBand = ((baseBandIdx + 1.0) / LIGHT_BANDS) * MAX_STYLIZED_BRIGHT;
                float hiBand = min(((baseBandIdx + 2.0) / LIGHT_BANDS) * MAX_STYLIZED_BRIGHT,
                                   MAX_STYLIZED_BRIGHT);
                float shadowMask = max(shadowOcclusion, shadowEvidence);

                // When multiple lights have different shadow states (one shadows
                // while another illuminates), preserve dithered band transitions
                // so color morphing between differently-colored lights works.
                float shadowConflict = clamp(shadowEvidence - shadowOcclusion, 0.0, 1.0);
                float shadowEdgeNoiseGate = 1.0 - smoothstep(0.02, 0.20, shadowMask) * (1.0 - shadowConflict);

                // World-space pixel cell for band transitions:
                // 1/64m cells => 16x16 grid per 0.250m terrain piece.
                // Use the same phase/snap as point-light evaluation to prevent broken pixels.
                vec3 pixWorldBand = snapGridCenterOffset(
                    fragWorldPos,
                    LIGHT_GRID_CELL_SIZE,
                    LIGHT_GRID_PHASE_OFFSET_CELLS);
                vec2 cellCoord = floor(faceCell(pixWorldBand, face) / LIGHT_GRID_CELL_SIZE + 0.01);

                // ── Pixel-art diffusion at band edges ──────────────────────
                float diffZoneWidth = 0.125;
                float diffStart = 0.5 - diffZoneWidth;
                float diffEnd   = 0.5 + diffZoneWidth;

                float naturalBand;
                if (bandFrac < diffStart) {
                    naturalBand = loBand;
                } else if (bandFrac > diffEnd) {
                    naturalBand = hiBand;
                } else {
                    float t = (bandFrac - diffStart) / (diffEnd - diffStart);

                    vec2 orderedCell = mod(mod(cellCoord, 8.0) + 8.0, 8.0);
                    float ordered = bayerDither8x8(orderedCell);

                    float heldNoise = bandBlendHeldNoiseCell(
                        cellCoord + vec2(13.7, 29.3),
                        lighting.time,
                        0.22,
                        0.58);
                    float driftNoise = bandBlendNoiseCell(
                        cellCoord * 1.19 + vec2(5.1, 47.3),
                        lighting.time,
                        0.48);
                    float randPattern = clamp(heldNoise * 0.72 + driftNoise * 0.28, 0.0, 1.0);
                    float localBias = (hash12(cellCoord + vec2(3.7, 91.1)) - 0.5) * 0.05;
                    float shimmerAmp = 0.09;
                    float shimmeredT = clamp(
                        t + localBias + (randPattern - 0.5) * shimmerAmp,
                        0.0,
                        1.0);

                    float transitionMask = clamp(ordered * 0.62 + randPattern * 0.38, 0.0, 1.0);
                    naturalBand = (transitionMask < shimmeredT) ? hiBand : loBand;
                }
                float stableBand = quantizeBandsStepped(
                    bandInput,
                    LIGHT_BANDS,
                    MAX_STYLIZED_BRIGHT,
                    0.0,
                    0.08);
                // With dithered binary shadows, shadow edges are already
                // retro-patterned; only deep full-shadow needs band stabilization.
                float stableShadowWeight = smoothstep(0.45, 0.85, shadowMask) * (1.0 - shadowConflict);
                naturalBand = mix(naturalBand, stableBand, stableShadowWeight);

                // ── Dither the darkest band toward the darkness boundary ─────
                const float SCREEN_DITHER_BAND_LIMIT = 2.0;
                float ditherPixSize = max(AO_PIXEL_SIZE * 1.5, 3.0);
                float shadowAwareNight = nightStrength * shadowEdgeNoiseGate;
                if (shadowAwareNight > 0.001 && scaledRaw < SCREEN_DITHER_BAND_LIMIT && naturalBand > 0.0) {
                    float ditherT = 1.0 - (scaledRaw / SCREEN_DITHER_BAND_LIMIT);
                    ditherT = ditherT * ditherT * (3.0 - 2.0 * ditherT);
                    ditherT *= shadowAwareNight;

                    vec2 ditherCell = floor(gl_FragCoord.xy / ditherPixSize);
                    float bayer = bayerDither4x4(ditherCell);
                    float breathe = sin(lighting.time * 0.25 + bayer * 3.14159) * 0.015;
                    float threshold = clamp(bayer + breathe, 0.0, 1.0);

                    float coverage = ditherT * 0.65;
                    if (threshold < coverage) {
                        naturalBand = 0.0;
                    }
                }

                // Replace the previous gap before darkness with sparse dark pixels
                // that get denser toward the edge, then converge to full dark.
                float darkScatter = (1.0 - smoothstep(0.08, 1.55, scaledRaw)) * shadowEdgeNoiseGate;
                if (darkScatter > 0.001 && naturalBand > 0.0001) {
                    vec2 orderedCell = mod(mod(cellCoord, 8.0) + 8.0, 8.0);
                    float ordered = bayerDither8x8(orderedCell);
                    float darkNoiseA = bandBlendNoiseCell(cellCoord + vec2(23.7, 79.1), lighting.time + 1.3, 0.30);
                    float darkNoiseB = bandBlendNoiseCell(cellCoord * 1.33 + vec2(61.9, 5.7), lighting.time, 0.19);
                    float darkPattern = clamp(ordered * 0.50 + darkNoiseA * 0.35 + darkNoiseB * 0.15, 0.0, 1.0);

                    float scatterCoverage = darkScatter * darkScatter * 0.90;
                    float scatterMask = smoothstep(1.0 - scatterCoverage - 0.11,
                                                   1.0 - scatterCoverage + 0.11,
                                                   darkPattern);
                    float darknessStrength = mix(0.28, 0.95, darkScatter);
                    naturalBand = mix(naturalBand, 0.0, scatterMask * darknessStrength);
                }

                // Very outermost edge should end in solid darkness (no lingering dither).
                float hardDarkEdge = 1.0 - smoothstep(0.015, 0.11, scaledRaw);
                if (hardDarkEdge > 0.001) {
                    naturalBand *= (1.0 - hardDarkEdge);
                    if (scaledRaw < 0.02) {
                        naturalBand = 0.0;
                    }
                }

                float scale = naturalBand / max(clampedBright, 0.001);
                totalLightContrib *= scale;
            }

            finalColor += totalLightContrib;
        }
    }

    // AO wash-out from light orbs — aggressive stepped recovery.
    // Point light brightness determines how much AO darkening to undo.
    // Uses sqrt curve so even moderate light visibly eats into AO shadows,
    // matching the retro feel where light orbs punch through dark corners.
    if (totalBrightnessRawForAO > 0.001 && any(lessThan(aoMul, vec3(0.999)))) {
        float bandForAO = floor(clamp(totalBrightnessRawForAO / 2.0, 0.0, 1.0) * 8.0);
        // Sqrt curve: band 1→35%, band 2→50%, band 4→71%, band 8→100%
        // Much more responsive than linear (band 1 was only 12.5% before).
        float aoFade = sqrt(clamp(bandForAO / 8.0, 0.0, 1.0));
        if (aoFade > 0.0) {
            vec3 aoRecovery = vec3(1.0) / max(aoMul, vec3(0.001));
            finalColor *= mix(vec3(1.0), aoRecovery, aoFade);
        }
    }
    
    // Apply simple distance fog
    float fogDensity = lighting.skyColor.w;
    if (fogDensity > 0.0) {
        float distance = length(fragWorldPos - camera.cameraPos);
        float fogFactor = exp(-fogDensity * distance);
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        
        vec3 skyColor = lighting.skyColor.xyz;
        finalColor = mix(skyColor, finalColor, fogFactor);
    }
    
    // DEBUG: Extract debug flags from ao.ditherWarmTint.w (bit-cast from uint)
    uint debugFlags = floatBitsToUint(ao.ditherWarmTint.w);
    const uint DEBUG_SHOW_CHUNK_BOUNDS = 1u;
    const uint DEBUG_SHOW_FACE_BOUNDS = 2u;
    
    // DEBUG: Highlight chunk boundaries to identify crack locations
    if ((debugFlags & DEBUG_SHOW_CHUNK_BOUNDS) != 0u) {
        vec3 chunkRelPos = fragWorldPos - fragChunkOrigin;
        
        // Distance from each chunk edge (chunk is 32m)
        float edgeX_min = chunkRelPos.x;
        float edgeX_max = 32.0 - chunkRelPos.x;
        float edgeZ_min = chunkRelPos.z;
        float edgeZ_max = 32.0 - chunkRelPos.z;
        
        float minEdgeDist = min(min(edgeX_min, edgeX_max), min(edgeZ_min, edgeZ_max));
        
        // Color code based on edge proximity (within 0.5m of edge)
        if (minEdgeDist < 0.5) {
            float intensity = 1.0 - (minEdgeDist / 0.5);
            
            // Determine which edge we're near for color coding
            vec3 edgeColor = vec3(1.0, 0.0, 0.0);  // Default red
            
            if (edgeX_min < 0.25) edgeColor = vec3(1.0, 0.0, 0.0);      // -X edge: Red
            else if (edgeX_max < 0.25) edgeColor = vec3(0.0, 1.0, 0.0); // +X edge: Green
            else if (edgeZ_min < 0.25) edgeColor = vec3(0.0, 0.0, 1.0); // -Z edge: Blue
            else if (edgeZ_max < 0.25) edgeColor = vec3(1.0, 1.0, 0.0); // +Z edge: Yellow
            
            finalColor = mix(finalColor, edgeColor, intensity * 0.8);
        }
        
        // VERY CLOSE to edge (within 0.1m / ~half voxel) - mark as bright magenta
        if (minEdgeDist < 0.1) {
            finalColor = mix(finalColor, vec3(1.0, 0.0, 1.0), 0.7);  // Magenta
        }
    }
    
    // DEBUG: Highlight face/quad boundaries within chunks (greedy meshing boundaries)
    // These appear at fractional voxel positions where merged quads meet
    if ((debugFlags & DEBUG_SHOW_FACE_BOUNDS) != 0u) {
        vec3 chunkRelPos = fragWorldPos - fragChunkOrigin;
        
        // Check distance to any voxel grid line (0.25m spacing)
        // Faces should align to voxel boundaries
        vec3 voxelPos = chunkRelPos * 4.0;  // Convert to voxel units
        vec3 fracPart = fract(voxelPos);
        
        // Distance to nearest voxel boundary
        vec3 distToBoundary = min(fracPart, 1.0 - fracPart);
        float minDist = min(distToBoundary.x, min(distToBoundary.y, distToBoundary.z));
        
        // Highlight pixels very close to voxel boundaries (potential crack source)
        if (minDist < 0.05) {
            // Cyan for voxel boundaries
            float intensity = 1.0 - (minDist / 0.05);
            finalColor = mix(finalColor, vec3(0.0, 1.0, 1.0), intensity * 0.6);
        }
    }
    
    // Output final color
    outColor = vec4(finalColor, 1.0);
}


````

## shaders\terrain\dccm_terrain.frag

Description: No CC-DESC found. C++ struct 'PointLight'.

````glsl
#version 450

layout(location = 0) in vec3 fragWorldPos;
layout(location = 1) in float fragAOLevel;
layout(location = 2) in flat uint fragFace;
layout(location = 3) in flat vec3 fragFlatPos;
layout(location = 4) in flat float fragFlatAO;
layout(location = 5) in flat vec3 fragChunkOrigin;

layout(location = 0) out vec4 outColor;

struct PointLight {
    vec4 positionRadius;
    vec4 colorIntensity;
};

const uint MAX_SHADER_LIGHTS = 32u;
const uint MAX_SUN_SHADOW_CASCADES = 6u;
const float LIGHT_GRID_WORLD_METERS = 0.250;
const float LIGHT_GRID_CELLS = 16.0;
const float LIGHT_GRID_CELL_SIZE = LIGHT_GRID_WORLD_METERS / LIGHT_GRID_CELLS; // 1/64m
const float LIGHT_GRID_PHASE_OFFSET_CELLS = 0.5;
const float MIN_LIGHT_BRIGHTNESS = 0.0030;
const float MIN_SHADOW_BRIGHTNESS = 0.0200;
const float HIGH_QUALITY_SHADOW_BRIGHTNESS = 0.1800;
const float FAST_STYLIZATION_BRIGHTNESS = 0.1400;

layout(std430, set = 0, binding = 2) readonly buffer LightingData {
    vec4 sunDirection;
    vec4 sunColor;
    vec4 skyColor;
    PointLight pointLights[4096];
    uint numPointLights;
    float time;
    uint _pad0;
    uint _pad1;

    vec4 lightPulseData[4096];
} lighting;

layout(set = 0, binding = 3) uniform CameraData {
    vec3 cameraPos;
} camera;

layout(set = 0, binding = 4) uniform AOData {
    vec4 brightnessLevels;
    vec4 shadowTint0;
    vec4 shadowTint1;
    vec4 shadowTint2;
    vec4 shadowTint3;
    vec4 ditherWarmTint;
    vec4 scatterAmounts;
    vec4 dccmBrightness;
    vec4 dccmShadowTintCfg;
    vec4 dccmConfig;
    vec4 dccmFillCol;
    vec4 dccmLineCol;
} ao;

layout(std430, set = 0, binding = 5) buffer ShadowData {
    mat4 sunLightVP[MAX_SUN_SHADOW_CASCADES];
    vec4 sunCascadeParams[MAX_SUN_SHADOW_CASCADES]; // x=halfExtent, y=texelMeters
    vec4 sunDirTexelSize;            // xyz=sun direction (towards ground), w=world-space texel footprint
    vec4 shadowConfig;
    vec4 shadowConfig2;          // x=sunCascadeCount, y=cascadeBlendFrac, z=maxCastRadius
    vec4 pointShadowInfo[32];
    // x=pointShadowSamples, y=lightEvalFragments, z=fullyOccludedFragments, w=litContribFragments
    uvec4 pointShadowDiag[32];
    vec4 diagConfig;             // x=enableDetailedDiagnostics
    // Sky enclosure: x=intensity, y=minAmbient, z=probeMaxHeight (m), w=mode (0=off,1=on,2=visualize)
    vec4 skyEnclosureParams;
    // Sky-vis static heightmap mapping:
    // x=worldOriginXMeters, y=worldOriginZMeters,
    // z=metersPerTexel (square), w=valueScaleToWorldYMeters (0 = disabled)
    vec4 skyHeightmapInfo;
} shadow;

layout(set = 0, binding = 6) uniform sampler2DArrayShadow sunShadowMap;
layout(set = 0, binding = 7) uniform samplerCubeArrayShadow pointShadowMaps;

// Clustered lighting bitmask SSBO
layout(std430, set = 0, binding = 8) readonly buffer ClusterData {
    uvec4 clusterGridDims;   // x=tilesX, y=tilesY, z=numSlices, w=totalClusters
    vec4  clusterZParams;    // x=near, y=far, z=logRatio, w=sliceScale
    vec4  clusterTileDims;   // x=tileSizeX, y=tileSizeY, z=screenW, w=screenH
    uint  clusterLightMasks[];
} clusters;

// Sky-vis static heightmap (sun-independent zenith occlusion source).
// Stored as R16F voxel-height values; multiply by shadow.skyHeightmapInfo.w to get world Y in meters.
layout(set = 0, binding = 9) uniform sampler2D skyHeightmap;

float quantizeLight(float value, int levels) {
    value = clamp(value, 0.0, 1.0);
    float step = 1.0 / float(levels - 1);
    return floor(value / step + 0.5) * step;
}

float bayerDither4x4(vec2 screenPos) {
    int x = int(mod(screenPos.x, 4.0));
    int y = int(mod(screenPos.y, 4.0));
    const int bayerMatrix[16] = int[](
         0,  8,  2, 10,
        12,  4, 14,  6,
         3, 11,  1,  9,
        15,  7, 13,  5
    );
    return float(bayerMatrix[y * 4 + x]) / 16.0;
}

float bayerDither8x8(vec2 screenPos) {
    const float bayerMatrix[64] = float[](
        0.0,  32.0,  8.0, 40.0,  2.0, 34.0, 10.0, 42.0,
        48.0, 16.0, 56.0, 24.0, 50.0, 18.0, 58.0, 26.0,
        12.0, 44.0,  4.0, 36.0, 14.0, 46.0,  6.0, 38.0,
        60.0, 28.0, 52.0, 20.0, 62.0, 30.0, 54.0, 22.0,
        3.0,  35.0, 11.0, 43.0,  1.0, 33.0,  9.0, 41.0,
        51.0, 19.0, 59.0, 27.0, 49.0, 17.0, 57.0, 25.0,
        15.0, 47.0,  7.0, 39.0, 13.0, 45.0,  5.0, 37.0,
        63.0, 31.0, 55.0, 23.0, 61.0, 29.0, 53.0, 21.0
    );

    int x = int(mod(screenPos.x, 8.0));
    int y = int(mod(screenPos.y, 8.0));
    return bayerMatrix[y * 8 + x] / 64.0;
}

vec2 pixelateCoords(vec2 screenPos, float pixelSize) {
    return floor(screenPos / pixelSize) * pixelSize;
}

// Grid snap to cell centers with a configurable pre-snap cell offset.
// offsetCells=0.5 shifts the phase by half a cell before snapping.
vec3 snapGridCenterOffset(vec3 pos, float gridSize, float offsetCells) {
    float g = max(gridSize, 0.000001);
    // Keep horizontal XZ grid aligned with voxel/cube shading; phase-shift Y only.
    vec3 off = vec3(0.0, g * offsetCells, 0.0);
    return (floor((pos + off) / g + 0.01) + 0.5) * g;
}

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float quantizeBandsStepped(float value,
                           float bands,
                           float maxValue,
                           float edgeBias,
                           float blendWidth) {
    float safeBands = max(bands, 1.0);
    float safeMax = max(maxValue, 0.0001);
    float v = clamp(value, 0.0, safeMax);
    float scaled = (v / safeMax) * safeBands;
    float baseBand = floor(scaled);
    float frac = fract(scaled);

    float lo = (baseBand / safeBands) * safeMax;
    float hi = min(((baseBand + 1.0) / safeBands) * safeMax, safeMax);

    float fracBiased = clamp(frac - edgeBias, 0.0, 1.0);
    float mid = (lo + hi) * 0.5;
    float width = clamp(blendWidth, 0.01, 0.45);
    float transitionStart = 0.5 - width;
    float transitionEnd = 0.5 + width;

    if (fracBiased <= transitionStart) return lo;
    if (fracBiased >= transitionEnd) return hi;
    return mid;
}

float bandBlendNoiseCell(vec2 cellCoord, float time, float speed) {
    vec2 cellBase = floor(cellCoord + 0.01);
    vec2 cellJitter = vec2(
        hash12(cellBase + vec2(11.3, 47.7)),
        hash12(cellBase + vec2(73.1, 19.9))
    ) - vec2(0.5);
    vec2 cell = cellBase + cellJitter * 0.85 + vec2(17.0, 59.0);
    float t = time * max(speed, 0.001);
    float frame = floor(t);
    float blend = fract(t);
    blend = blend * blend * (3.0 - 2.0 * blend);

    float n0 = hash12(cell + vec2(frame, frame * 0.37));
    float n1 = hash12(cell + vec2(frame + 1.0, (frame + 1.0) * 0.37));
    float n2 = hash12(cell * 0.73 + vec2(frame * 1.13, frame * 0.29));
    float n3 = hash12(cell * 0.73 + vec2((frame + 1.0) * 1.13, (frame + 1.0) * 0.29));

    float a = mix(n0, n1, blend);
    float b = mix(n2, n3, blend);
    return clamp(a * 0.66 + b * 0.34, 0.0, 1.0);
}

float quantizeBandsPixelBlend(float value,
                              float bands,
                              float maxValue,
                              float edgeBias,
                              float blendWidth,
                              vec2 blendCellCoord,
                              float time,
                              float animSpeed) {
    float safeBands = max(bands, 1.0);
    float safeMax = max(maxValue, 0.0001);
    float v = clamp(value, 0.0, safeMax);
    float scaled = (v / safeMax) * safeBands;
    float baseBand = floor(scaled);
    float frac = fract(scaled);

    float lo = (baseBand / safeBands) * safeMax;
    float hi = min(((baseBand + 1.0) / safeBands) * safeMax, safeMax);
    float fracBiased = clamp(frac - edgeBias, 0.0, 1.0);

    float width = clamp(blendWidth, 0.01, 0.45);
    float transitionStart = 0.5 - width;
    float transitionEnd = 0.5 + width;
    if (fracBiased <= transitionStart) return lo;
    if (fracBiased >= transitionEnd) return hi;

    float transitionT = (fracBiased - transitionStart)
                      / max(transitionEnd - transitionStart, 0.0001);
    float noiseT = bandBlendNoiseCell(blendCellCoord, time, animSpeed);
    return (transitionT >= noiseT) ? hi : lo;
}

vec2 faceCell(vec3 pixPos, uint face) {
    // DCCM terrain uses face==6; treat it like top-surface mapping (XZ).
    if (face == 6u) return pixPos.xz;
    if (face <= 1u) return pixPos.yz;
    if (face <= 3u) return pixPos.xz;
    return pixPos.xy;
}

vec3 dominantAxisMask(vec3 n) {
    vec3 a = abs(n);
    if (a.x >= a.y && a.x >= a.z) return vec3(1.0, 0.0, 0.0);
    if (a.y >= a.x && a.y >= a.z) return vec3(0.0, 1.0, 0.0);
    return vec3(0.0, 0.0, 1.0);
}

void buildFaceTangents(vec3 n, out vec3 t1, out vec3 t2) {
    vec3 m = dominantAxisMask(n);
    if (m.x > 0.5) {
        t1 = vec3(0.0, 1.0, 0.0);
        t2 = vec3(0.0, 0.0, 1.0);
    } else if (m.y > 0.5) {
        t1 = vec3(1.0, 0.0, 0.0);
        t2 = vec3(0.0, 0.0, 1.0);
    } else {
        t1 = vec3(1.0, 0.0, 0.0);
        t2 = vec3(0.0, 1.0, 0.0);
    }
}

float resolveBinaryShadowFromNeighborhood(float v0,
                                          float v1,
                                          float v2,
                                          float v3,
                                          float v4) {
    // Majority-cleanup keeps pixel-art silhouettes coherent by filling
    // tiny holes and removing one-pixel outliers before soft blending.
    float litVotes =
        step(0.5, v0) +
        step(0.5, v1) +
        step(0.5, v2) +
        step(0.5, v3) +
        step(0.5, v4);
    float centerLit = step(0.5, v0);
    if (litVotes <= 1.0) return 0.0;
    if (litVotes >= 4.0) return 1.0;
    if (centerLit < 0.5 && litVotes >= 3.0) return 1.0;
    if (centerLit > 0.5 && litVotes <= 2.0) return 0.0;

    float visibility = v0 * 0.5 + (v1 + v2 + v3 + v4) * 0.125;
    float voteVisibility = litVotes / 5.0;
    float blended = mix(visibility, voteVisibility, 0.45);
    return smoothstep(0.30, 0.70, blended);
}


// ═════════════════════════════════════════════════════════════════════════
// Smooth point light — accumulate-then-quantize (synced with cube.frag)
// ═════════════════════════════════════════════════════════════════════════

vec4 calculatePointLightSmooth(vec3 worldPos,
                               vec3 normal,
                               vec3 baseColor,
                               vec3 lightPos,
                               float lightRadius,
                               vec3 lightColor,
                               float lightIntensity,
                               float pulseStrength,
                               float breathScale,
                               uint lightIndex) {
    if (breathScale < 0.01) breathScale = 1.0;

    // Treat the configured light radius as the actual orb radius so the
    // band shells intersect terrain and placed geometry as true spheres.
    float effectiveRadius = max(lightRadius * breathScale, 0.0001);
    vec3 rawLightVec = lightPos - worldPos;
    float rawLenSq = dot(rawLightVec, rawLightVec);
    float effectiveRadiusSq = effectiveRadius * effectiveRadius;
    if (rawLenSq <= 0.00000001 || rawLenSq > effectiveRadiusSq) return vec4(0.0);
    vec3 rawLightDir = rawLightVec * inversesqrt(rawLenSq);
    float diffuse = max(dot(normal, rawLightDir), 0.0);
    if (diffuse <= 0.0) return vec4(0.0);

    const float pixelGrid = LIGHT_GRID_CELL_SIZE;
    vec3 pixLight = snapGridCenterOffset(lightPos, pixelGrid, LIGHT_GRID_PHASE_OFFSET_CELLS);
    vec3 pixWorld = snapGridCenterOffset(worldPos, pixelGrid, LIGHT_GRID_PHASE_OFFSET_CELLS);

    // ── Orb distance on the snapped retro grid ─────────────────────
    vec3  lightVec  = pixLight - pixWorld;
    float lightLenSq = dot(lightVec, lightVec);
    if (lightLenSq <= 0.00000001 || lightLenSq > effectiveRadiusSq) return vec4(0.0);
    float distance3D = sqrt(lightLenSq);
    float dist01 = clamp(distance3D / effectiveRadius, 0.0, 1.0);
    float brightness = pow(1.0 - dist01, 2.5);
    brightness *= (1.0 + pulseStrength * (1.0 - dist01) * 0.30);
    if (brightness <= 0.0005) return vec4(0.0);

    // Baseline instability remains active even for steady pulse profiles.
    {
        const float shimmerAmount = 0.24;
        float shimmerSpeed = 0.82
                           + hash12(vec2(float(lightIndex) * 0.73, 4.91)) * 0.28;

        vec2 fc = floor(faceCell(pixWorld, fragFace) / pixelGrid + 0.01);
        float edgeBoost = 0.25 + dist01 * 0.75;
        vec2 shimmerSeed = fc + vec2(float(lightIndex) * 7.3, float(lightIndex) * 3.9);
        float shimmerNoise = bandBlendNoiseCell(
            shimmerSeed,
            lighting.time + dist01 * 0.31,
            shimmerSpeed * 0.43) * 2.0 - 1.0;
        float shimmerMag = shimmerAmount * edgeBoost * 0.24;
        brightness *= (1.0 + shimmerNoise * shimmerMag);

        brightness = max(brightness, 0.0);
    }
    brightness *= lightIntensity;

    float visibleBrightness = brightness * diffuse;
    vec3 colorContrib = baseColor * lightColor * visibleBrightness;
    return vec4(colorContrib, brightness);
}

vec3 snapShadowLookupPos(vec3 worldPos) {
    return snapGridCenterOffset(worldPos, LIGHT_GRID_CELL_SIZE, LIGHT_GRID_PHASE_OFFSET_CELLS);
}

uint getSunCascadeCount() {
    return uint(clamp(shadow.shadowConfig2.x + 0.5, 1.0, float(MAX_SUN_SHADOW_CASCADES)));
}

float getSunCascadeBlendFraction() {
    return clamp(shadow.shadowConfig2.y, 0.0, 0.45);
}

uint chooseSunCascade(float horizontalDistance) {
    uint count = getSunCascadeCount();
    for (uint i = 0u; i < count; ++i) {
        if (horizontalDistance <= shadow.sunCascadeParams[i].x) {
            return i;
        }
    }
    return count - 1u;
}

bool sunCascadeSampleParams(vec3 sampleWorldPos,
                            uint cascadeIndex,
                            out vec2 uv,
                            out float depth) {
    vec4 clip = shadow.sunLightVP[cascadeIndex] * vec4(sampleWorldPos, 1.0);
    vec3 ndc = clip.xyz / clip.w;
    uv = ndc.xy * 0.5 + 0.5;
    depth = ndc.z;

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 ||
        depth > 1.0 || depth < 0.0) {
        return false;
    }
    return true;
}

uint chooseSunCascadeForSample(vec3 sampleWorldPos, float horizontalDistance) {
    uint count = getSunCascadeCount();
    uint cascade = chooseSunCascade(horizontalDistance);
    for (uint step = 0u; step < count; ++step) {
        uint c = min(cascade + step, count - 1u);
        vec2 uv;
        float depth;
        if (sunCascadeSampleParams(sampleWorldPos, c, uv, depth)) {
            return c;
        }
        if (c == count - 1u) {
            break;
        }
    }
    return count - 1u;
}

float sampleSunShadowCascade(vec3 sampleWorldPos, uint cascadeIndex) {
    vec2 uv;
    float depth;
    if (!sunCascadeSampleParams(sampleWorldPos, cascadeIndex, uv, depth)) {
        return 1.0;
    }

    // Per-cascade snap gate: only snap when the cascade's world-space
    // texel is fine enough that the snap doesn't quantize the receiver
    // visibly. Far cascades (large texelMeters at high cascadeScale)
    // would otherwise expose 1 m+ shadow texels directly on the ground,
    // producing the triangle staircase silhouettes and shape changes
    // that vary with cascade count/scale.
    float cascadeTexelMeters = shadow.sunCascadeParams[cascadeIndex].y;
    if (cascadeTexelMeters <= LIGHT_GRID_CELL_SIZE * 1.5) {
        float mapSize = max(shadow.shadowConfig.z, 1.0);
        uv = (floor(uv * mapSize) + 0.5) / mapSize;
    }
    return texture(sunShadowMap, vec4(uv, float(cascadeIndex), depth));
}

float sampleSunShadowAt(vec3 sampleWorldPos) {
    float horizontalDistance = length(sampleWorldPos.xz - camera.cameraPos.xz);
    uint cascade = chooseSunCascadeForSample(sampleWorldPos, horizontalDistance);
    float vis = sampleSunShadowCascade(sampleWorldPos, cascade);

    uint count = getSunCascadeCount();
    if (cascade + 1u < count) {
        float blendFrac = getSunCascadeBlendFraction();
        float cascadeRadius = shadow.sunCascadeParams[cascade].x;
        float blendStart = cascadeRadius * (1.0 - blendFrac);
        if (horizontalDistance > blendStart) {
            vec4 farClip = shadow.sunLightVP[cascade + 1u] * vec4(sampleWorldPos, 1.0);
            vec3 farNdc = farClip.xyz / farClip.w;
            bool farValid =
                farNdc.x >= -1.0 && farNdc.x <= 1.0 &&
                farNdc.y >= -1.0 && farNdc.y <= 1.0 &&
                farNdc.z >= 0.0 && farNdc.z <= 1.0;
            if (farValid) {
                float denom = max(cascadeRadius - blendStart, 0.0001);
                float t = clamp((horizontalDistance - blendStart) / denom, 0.0, 1.0);
                float farVis = sampleSunShadowCascade(sampleWorldPos, cascade + 1u);
                vis = mix(vis, farVis, t);
            }
        }
    }
    return vis;
}

float sampleSunShadowAtNoSnap(vec3 sampleWorldPos) {
    float horizontalDistance = length(sampleWorldPos.xz - camera.cameraPos.xz);
    uint cascade = chooseSunCascadeForSample(sampleWorldPos, horizontalDistance);

    vec2 uv;
    float depth;
    if (!sunCascadeSampleParams(sampleWorldPos, cascade, uv, depth)) {
        return 1.0;
    }
    float vis = texture(sunShadowMap, vec4(uv, float(cascade), depth));

    uint count = getSunCascadeCount();
    if (cascade + 1u < count) {
        float blendFrac = getSunCascadeBlendFraction();
        float cascadeRadius = shadow.sunCascadeParams[cascade].x;
        float blendStart = cascadeRadius * (1.0 - blendFrac);
        if (horizontalDistance > blendStart) {
            vec4 farClip = shadow.sunLightVP[cascade + 1u] * vec4(sampleWorldPos, 1.0);
            vec3 farNdc = farClip.xyz / farClip.w;
            bool farValid =
                farNdc.x >= -1.0 && farNdc.x <= 1.0 &&
                farNdc.y >= -1.0 && farNdc.y <= 1.0 &&
                farNdc.z >= 0.0 && farNdc.z <= 1.0;
            if (farValid) {
                float denom = max(cascadeRadius - blendStart, 0.0001);
                float t = clamp((horizontalDistance - blendStart) / denom, 0.0, 1.0);
                vec2 farUv = farNdc.xy * 0.5 + 0.5;
                float farDepth = farNdc.z;
                float farVis = texture(sunShadowMap, vec4(farUv, float(cascade + 1u), farDepth));
                vis = mix(vis, farVis, t);
            }
        }
    }
    return vis;
}

vec3 snapToFaceCell(vec3 pos, uint face) {
    float g = LIGHT_GRID_CELL_SIZE;
    vec3 s = pos;
    if (face <= 1u) {
        s.z = (floor(pos.z / g + 0.01) + 0.5) * g;
    } else if (face <= 3u || face == 6u) {
        s.x = (floor(pos.x / g + 0.01) + 0.5) * g;
        s.z = (floor(pos.z / g + 0.01) + 0.5) * g;
    } else {
        s.x = (floor(pos.x / g + 0.01) + 0.5) * g;
    }
    return s;
}

float sampleSunShadow(vec3 worldPos, vec3 worldNormal) {
    if (shadow.shadowConfig.x <= 0.0) return 1.0;

    vec3 normalN = normalize(worldNormal);
    vec3 sunDir = shadow.sunDirTexelSize.xyz;
    float ndotl = dot(normalN, -sunDir);
    float horizontalDistance = length(worldPos.xz - camera.cameraPos.xz);
    uint activeCascade = chooseSunCascadeForSample(worldPos, horizontalDistance);

    const float kFadeStart = -0.08;
    const float kFadeEnd   =  0.16;
    float backfaceFade = smoothstep(kFadeStart, kFadeEnd, ndotl);
    if (backfaceFade <= 0.0) return 0.0;

    uint derivedFace;
    vec3 an = abs(normalN);
    if (an.x >= an.y && an.x >= an.z) derivedFace = (normalN.x > 0.0) ? 0u : 1u;
    else if (an.y >= an.x && an.y >= an.z) derivedFace = (normalN.y > 0.0) ? 2u : 3u;
    else derivedFace = (normalN.z > 0.0) ? 4u : 5u;

    bool isVerticalFace = (derivedFace <= 1u || derivedFace >= 4u);

    // ── Vertical-face cast-shadow fade ──────────────────────────────
    // When the sun is nearly tangential to a vertical face the shear
    // projection becomes ill-conditioned and small casters render
    // glitched bands. Mirror the side-face self-shadow fade: as
    // |sunDir · faceNormal| approaches zero, the cast shadow on this
    // face fades out smoothly. Horizontal faces are unaffected.
    float castFade = 1.0;
    if (isVerticalFace) {
        float sunFaceCos = abs(dot(sunDir, normalN));
        castFade = smoothstep(0.05, 0.16, sunFaceCos);
    }

    // Push along SURFACE NORMAL, not along -sunDir. Sun-direction push
    // at low elevation is mostly horizontal — it walks the sample into
    // adjacent walls ("shadow under wall") and shifts silhouettes with
    // azimuth ("shape breaks as sun rotates / detaches at low sun").
    // Normal-direction push keeps the sample anchored to the same
    // surface point regardless of sun direction.
    float g = LIGHT_GRID_CELL_SIZE;
    float cascadeTexel = shadow.sunCascadeParams[activeCascade].y;
    uint cascadeCount = getSunCascadeCount();
    if (activeCascade + 1u < cascadeCount) {
        float blendFrac = getSunCascadeBlendFraction();
        float cascadeRadius = shadow.sunCascadeParams[activeCascade].x;
        float blendStart = cascadeRadius * (1.0 - blendFrac);
        if (horizontalDistance > blendStart) {
            cascadeTexel = max(cascadeTexel, shadow.sunCascadeParams[activeCascade + 1u].y);
        }
    }
    float baseTexel = shadow.sunCascadeParams[0].y;
    float biasTexel = min(max(baseTexel, cascadeTexel * 0.25), baseTexel * 2.0);
    float slope = clamp(1.0 / max(ndotl, 0.1), 1.0, 4.0);
    float pushAlongNormal = max(g, biasTexel * 0.85) * slope;

    if (isVerticalFace) {
        // Snap Y as well as the in-plane horizontal axis: shadow UV is
        // (worldX + Y*shearX, worldZ + Y*shearZ), so Y appears in BOTH
        // UV components. If Y is left continuous, fragments within one
        // wall cell sweep ~g*|shear| shadow texels and the cell fills
        // only partially -> jagged outline. Snapping Y collapses each
        // wall cell to one shadow-UV lookup (matches top-face behaviour).
        vec3 snapped = snapToFaceCell(worldPos, derivedFace);
        snapped.y = (floor(worldPos.y / g + 0.01) + 0.5) * g;
        vec3 pushed = snapped + normalN * pushAlongNormal;
        return sampleSunShadowAtNoSnap(pushed) * backfaceFade * castFade;
    }

    vec3 samplePos = worldPos + normalN * pushAlongNormal;
    return sampleSunShadowAt(samplePos) * backfaceFade;
}


float samplePointShadowPCF(vec3 dirN, uint lightIndex, float compareDepth, float mapSize) {
    vec3 up = (abs(dirN.y) < 0.99) ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, dirN));
    vec3 bitangent = cross(dirN, tangent);

    // Smaller kernel: keeps retro hard edges but cuts heavy depth-compare cost.
    float angleStep = 2.0 / mapSize;
    float radius = angleStep * 1.25;
    const vec2 pcfOffsets[4] = vec2[](
        vec2( 1.0,  0.0),
        vec2(-1.0,  0.0),
        vec2( 0.0,  1.0),
        vec2( 0.0, -1.0)
    );

    float sum = texture(pointShadowMaps, vec4(dirN, float(lightIndex)), compareDepth);
    for (int i = 0; i < 4; ++i) {
        vec2 o = pcfOffsets[i] * radius;
        vec3 sampleDir = normalize(dirN + tangent * o.x + bitangent * o.y);
        sum += texture(pointShadowMaps, vec4(sampleDir, float(lightIndex)), compareDepth);
    }
    return sum / 5.0;
}

float samplePointShadowSingle(vec3 dirN, uint lightIndex, float compareDepth) {
    return texture(pointShadowMaps, vec4(dirN, float(lightIndex)), compareDepth);
}

float samplePointShadowAt(vec3 sampleWorldPos,
                          vec3 worldNormalN,
                          vec3 lightPos,
                          uint lightIndex,
                          float farPlane,
                          float mapSize,
                          bool usePCF) {
    vec3 dir = sampleWorldPos - lightPos;
    float distSq = dot(dir, dir);
    float farPlaneSq = farPlane * farPlane;
    if (distSq <= 0.00000001 || distSq >= farPlaneSq) return 1.0;

    float invDist = inversesqrt(distSq);
    float dist = distSq * invDist;
    vec3 dirN = dir * invDist;

    // World-space receiver bias derived from cubemap texel footprint.
    // Prevents lit-edge acne where contact seams quantize to the occluder depth.
    float ndotl = max(dot(worldNormalN, -dirN), 0.0);
    float slope = 1.0 - ndotl;
    float dist01 = clamp(dist / farPlane, 0.0, 1.0);
    float texelWorld = farPlane / max(mapSize, 1.0);
    const float SHADOW_LOOKUP_GRID = LIGHT_GRID_CELL_SIZE;

    // Extra bias for grazing angles (slope~1) to prevent self-shadowing
    // on vertical faces while still catching cast shadows from other objects.
    float worldBias = texelWorld * (1.15 + slope * 3.50);
    worldBias = max(worldBias, SHADOW_LOOKUP_GRID * 0.12);
    worldBias += SHADOW_LOOKUP_GRID * 0.03 * (1.0 - dist01);

    float compareDepth = clamp((dist - worldBias) / farPlane, 0.0, 1.0);
    return usePCF
        ? samplePointShadowPCF(dirN, lightIndex, compareDepth, mapSize)
        : samplePointShadowSingle(dirN, lightIndex, compareDepth);
}

float samplePointShadow(vec3 worldPos,
                        vec3 worldNormal,
                        vec3 lightPos,
                        uint lightIndex,
                        float farPlane,
                        float mapSize,
                        bool highQuality) {
    if (farPlane < 0.01) return 1.0;
    vec3 normalN = normalize(worldNormal);
    bool verticalFace = (abs(normalN.y) < 0.5);

    const float SHADOW_LOOKUP_GRID = LIGHT_GRID_CELL_SIZE;

    if (verticalFace) {
        // Vertical faces: evaluate a small neighborhood and resolve to smooth
        // visibility to avoid contact-line outlines and one-pixel glitches.
        const float VERT_RECEIVER_PUSH = SHADOW_LOOKUP_GRID * 1.5;
        const float VERT_KERNEL_RADIUS = SHADOW_LOOKUP_GRID * 0.95;
        vec3 snapped = snapShadowLookupPos(worldPos);
        vec3 center = snapped;
        center += normalN * VERT_RECEIVER_PUSH;
        center.y = snapped.y + SHADOW_LOOKUP_GRID * 0.35;
        vec3 t1, t2;
        buildFaceTangents(normalN, t1, t2);

        float v0 = samplePointShadowAt(center, normalN, lightPos, lightIndex, farPlane, mapSize, true);
        float v1 = samplePointShadowAt(center + t1 * VERT_KERNEL_RADIUS, normalN, lightPos, lightIndex, farPlane, mapSize, false);
        float v2 = samplePointShadowAt(center - t1 * VERT_KERNEL_RADIUS, normalN, lightPos, lightIndex, farPlane, mapSize, false);
        float v3 = samplePointShadowAt(center + t2 * VERT_KERNEL_RADIUS, normalN, lightPos, lightIndex, farPlane, mapSize, false);
        float v4 = samplePointShadowAt(center - t2 * VERT_KERNEL_RADIUS, normalN, lightPos, lightIndex, farPlane, mapSize, false);
        return resolveBinaryShadowFromNeighborhood(v0, v1, v2, v3, v4);
    }

    const float SHADOW_RECEIVER_PUSH = SHADOW_LOOKUP_GRID * 0.22;
    const float SHADOW_KERNEL_RADIUS = SHADOW_LOOKUP_GRID * 0.92;
    vec3 center = snapShadowLookupPos(worldPos + normalN * SHADOW_RECEIVER_PUSH);

    vec3 t1, t2;
    buildFaceTangents(normalN, t1, t2);

    bool usePCF = true;
    float v0 = samplePointShadowAt(center, normalN, lightPos, lightIndex, farPlane, mapSize, usePCF);
    float sampleRadius = highQuality ? SHADOW_KERNEL_RADIUS : (SHADOW_KERNEL_RADIUS * 0.88);
    float v1 = samplePointShadowAt(center + t1 * sampleRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v2 = samplePointShadowAt(center - t1 * sampleRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v3 = samplePointShadowAt(center + t2 * sampleRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v4 = samplePointShadowAt(center - t2 * sampleRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float resolved = resolveBinaryShadowFromNeighborhood(v0, v1, v2, v3, v4);
    if (!highQuality) {
        return resolved;
    }

    float visibility = v0 * 0.5 + (v1 + v2 + v3 + v4) * 0.125;

    float litVotes =
        step(0.5, v0) +
        step(0.5, v1) +
        step(0.5, v2) +
        step(0.5, v3) +
        step(0.5, v4);
    float voteVisibility = litVotes / 5.0;
    float blended = mix(visibility, voteVisibility, 0.25);
    float smoothVisibility = smoothstep(0.14, 0.90, blended);
    return mix(smoothVisibility, resolved, 0.60);
}

// ─── Sky enclosure (inlined copy of shaders/common/sky_enclosure.glsl) ─────
// "Deeper = darker" geometric enclosure, INDEPENDENT of any light source.
// Sun adds light back via the normal lighting path; this term only removes
// ambient contribution as a function of how surrounded the fragment is.
//   (A) Sky-march column — march upward, find first "sky visible" height.
//       Enclosure ratio = how deep we are inside terrain.
//   (B) Y-cavity — sun-independent screen-space derivative term for shallow
//       depressions the march resolution misses.
// Probe sampler: depth<0 → LIT (above map), depth>1 or uv-out → next cascade,
// exhausted → assume open sky → LIT. NEVER exclude probes.
const int   DCCM_SKY_MARCH_STEPS = 12;
const float DCCM_SKY_NORMAL_LIFT = 0.05;

float dccmSampleProbeShadow(vec3 pos) {
    uint count = getSunCascadeCount();
    for (uint i = 0u; i < count; ++i) {
        vec4 clip = shadow.sunLightVP[i] * vec4(pos, 1.0);
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = ndc.xy * 0.5 + 0.5;
        float depth = ndc.z;
        if (depth < 0.0) return 1.0;
        if (depth > 1.0) continue;
        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) continue;
        float mapSize = max(shadow.shadowConfig.z, 1.0);
        uv = (floor(uv * mapSize) + 0.5) / mapSize;
        return texture(sunShadowMap, vec4(uv, float(i), depth));
    }
    return 1.0;
}

float dccmSkyMarchEnclosure(vec3 origin, float maxH) {
    float stepM = maxH / float(DCCM_SKY_MARCH_STEPS);
    float occlusionLength = 0.0;
    for (int i = 0; i < DCCM_SKY_MARCH_STEPS; ++i) {
        float h = (float(i) + 0.5) * stepM;
        vec3 probe = origin + vec3(0.0, h, 0.0);
        float vis = dccmSampleProbeShadow(probe);
        if (vis > 0.5) {
            return clamp(occlusionLength / maxH, 0.0, 1.0);
        }
        occlusionLength += stepM;
    }
    return 1.0;
}

float dccmYCavityTerm(vec3 worldPos, vec3 N) {
    // Reject side faces and geometry edges (corner-flicker guard).
    float floorWeight = clamp(N.y, 0.0, 1.0);
    if (floorWeight < 0.5) return 0.0;
    vec2 dy = vec2(dFdx(worldPos.y), dFdy(worldPos.y));
    vec2 dPx = vec2(dFdx(worldPos.x), dFdy(worldPos.x));
    vec2 dPz = vec2(dFdx(worldPos.z), dFdy(worldPos.z));
    float pixelMeters = max(length(vec2(length(dPx), length(dPz))), 1e-4);
    if (pixelMeters > 0.25) return 0.0; // edge fragment
    float slopePerMeter = length(dy) / pixelMeters;
    return clamp(slopePerMeter * floorWeight, 0.0, 1.0);
}

// Sun-independent zenith term using the static heightmap upload.
// Floor-faces only (walls would sample at their own XZ producing chaos);
// uses NEAREST + heightmap-texel-snap so each 2.5 m cell = one flat band.
float dccmHeightmapZenithTerm(vec3 worldPos, vec3 N, float maxH) {
    float scale = shadow.skyHeightmapInfo.w;
    if (scale <= 0.0) return 0.0;
    float metersPerTexel = max(shadow.skyHeightmapInfo.z, 1e-4);
    vec2 texSize = vec2(textureSize(skyHeightmap, 0));
    vec2 worldSpan = texSize * metersPerTexel;

    // Snap to heightmap-texel center → one constant surfaceY per cell.
    vec2 texelXZ = (floor(worldPos.xz / metersPerTexel) + 0.5) * metersPerTexel;
    vec2 uv = (texelXZ - shadow.skyHeightmapInfo.xy) / worldSpan;
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 0.0;
    float surfaceY = texture(skyHeightmap, uv).r * scale;
    // Stable voxel quantization — see sky_enclosure.glsl for full rationale.
    const float DCCM_VOXEL_M = 0.25;
    const float DCCM_INV_VOXEL = 1.0 / DCCM_VOXEL_M;
    const float DCCM_DEAD_VOX = 2.0;
    float surfaceVox = floor(surfaceY * DCCM_INV_VOXEL + 0.5);
    float fragVox    = floor((worldPos.y - DCCM_VOXEL_M * 0.5) * DCCM_INV_VOXEL);
    float depthVox   = max(0.0, surfaceVox - fragVox - DCCM_DEAD_VOX);
    return clamp(depthVox * DCCM_VOXEL_M / maxH, 0.0, 1.0);
}

float computeSkyEnclosureRaw(vec3 worldPos, vec3 N) {
    float maxH = max(shadow.skyEnclosureParams.z, 0.5);

    // Heightmap zenith term — true geometric depth below original surface.
    // The old sky-march term was removed because it sampled the sun shadow
    // map and therefore confused sun-shadow with sky-block (triangle
    // artifacts on walls; false depth on sun-shadowed flat ground).
    float zenith = dccmHeightmapZenithTerm(worldPos, N, maxH);

    float cavityGain = clamp(shadow.shadowConfig2.w, 0.0, 1.0);
    float cavity = dccmYCavityTerm(worldPos, N) * cavityGain;

    return clamp(zenith + cavity * (1.0 - zenith), 0.0, 1.0);
}

float applySunDissolve(float raw, float sunReach) {
    const float SUN_DISSOLVE_STRENGTH = 0.7;
    return raw * (1.0 - clamp(sunReach, 0.0, 1.0) * SUN_DISSOLVE_STRENGTH);
}

float computeSkyEnclosure(vec3 worldPos, vec3 N, float sunReach) {
    float mode = shadow.skyEnclosureParams.w;
    if (mode < 0.5) return 1.0;

    float intensity  = max(shadow.skyEnclosureParams.x, 0.0);
    float minAmbient = clamp(shadow.skyEnclosureParams.y, 0.0, 1.0);

    float raw = computeSkyEnclosureRaw(worldPos, N);
    raw = applySunDissolve(raw, sunReach);

    float k = max(intensity, 0.05) * 3.0;
    float occ = 1.0 - exp(-raw * k);

    // Single-quantization (input already voxel-stepped); see sky_enclosure.glsl.
    return mix(1.0, minAmbient, occ);
}

float computeSkyEnclosure(vec3 worldPos, vec3 N) {
    return computeSkyEnclosure(worldPos, N, 0.0);
}

bool skyEnclosureVisualizeEnabled() {
    return shadow.skyEnclosureParams.w > 1.5;
}

vec3 skyEnclosureDebugColor(vec3 worldPos, vec3 N, float sunReach) {
    float raw = computeSkyEnclosureRaw(worldPos, N);
    raw = applySunDissolve(raw, sunReach);
    vec3 lo = vec3(0.10, 0.95, 0.20);
    vec3 mid = vec3(0.95, 0.85, 0.10);
    vec3 hi = vec3(0.95, 0.10, 0.10);
    return raw < 0.5
        ? mix(lo, mid, raw * 2.0)
        : mix(mid, hi, (raw - 0.5) * 2.0);
}

vec3 skyEnclosureDebugColor(vec3 worldPos, vec3 N) {
    return skyEnclosureDebugColor(worldPos, N, 0.0);
}

void main() {
    if (fragFace != 6u) {
        discard;
    }

    vec3 fillColor = ao.dccmFillCol.xyz;
    vec3 lineColor = ao.dccmLineCol.xyz;
    float contourThickness = ao.dccmLineCol.w;

    float pixelSize = max(ao.dccmConfig.x, 1.0);
    float slopeStrength = ao.dccmConfig.y;
    float contourSpacing = ao.dccmConfig.z;

    // Pixelate screen coords for retro chunky look
    vec2 screenCoord = gl_FragCoord.xy;
    if (pixelSize > 1.0) {
        screenCoord = floor(screenCoord / pixelSize) * pixelSize;
    }

    uint flags = floatBitsToUint(ao.dccmConfig.w);
    bool slopeOn = (flags & 1u) != 0u;
    bool contourOn = (flags & 2u) != 0u;
    bool heightOn = (flags & 4u) != 0u;
    bool bakedAOOn = (flags & 8u) != 0u;
    bool debugTri = (flags & 16u) != 0u;
    bool sideFaceDarkOn = (flags & 32u) != 0u;

    if (debugTri) {
        float seed = dot(fragFlatPos, vec3(127.1, 311.7, 74.7));
        vec3 triColor = vec3(
            fract(sin(seed) * 43758.5453),
            fract(sin(seed * 1.13 + 3.71) * 22578.1459),
            fract(sin(seed * 0.97 + 7.13) * 19642.3217)
        );
        outColor = vec4(triColor * 0.6 + 0.3, 1.0);
        return;
    }

    vec3 dpdx = dFdx(fragWorldPos);
    vec3 dpdy = dFdy(fragWorldPos);
    vec3 triNormal = normalize(cross(dpdx, dpdy));
    if (triNormal.y < 0.0) triNormal = -triNormal;

    vec3 shadingNormal = triNormal;

    float slopeFactor = clamp(1.0 - shadingNormal.y, 0.0, 1.0);

    vec3 shaded = fillColor;

    if (heightOn) {
        float h0 = ao.dccmBrightness.x;
        float h1 = ao.dccmBrightness.y;
        float h2 = ao.dccmBrightness.z;
        float bandStrength = ao.dccmBrightness.w;

        vec3 col0 = fillColor * vec3(0.72, 0.82, 0.64);
        vec3 col1 = fillColor * vec3(0.92, 1.00, 0.90);
        vec3 col2 = fillColor * vec3(1.10, 1.18, 0.96);
        vec3 col3 = fillColor * vec3(1.28, 1.34, 1.10);

        float worldH = fragWorldPos.y;
        float t01 = smoothstep(h0 - 0.6, h0 + 0.6, worldH);
        float t12 = smoothstep(h1 - 0.6, h1 + 0.6, worldH);
        float t23 = smoothstep(h2 - 0.6, h2 + 0.6, worldH);

        vec3 heightCol = mix(col0, col1, t01);
        heightCol = mix(heightCol, col2, t12);
        heightCol = mix(heightCol, col3, t23);

        shaded = mix(shaded, heightCol, clamp(bandStrength, 0.0, 1.0));
    }

    if (slopeOn) {
        float powerCurve = ao.dccmShadowTintCfg.w;
        float curvedSlope = pow(slopeFactor, powerCurve);
        float slopeStrengthLocal = slopeStrength;
        float slopeDarkening = 1.0 - curvedSlope * slopeStrengthLocal * 0.56;
        slopeDarkening = clamp(slopeDarkening, 0.45, 1.0);

        vec3 slopeTint = ao.dccmShadowTintCfg.xyz;
        vec3 tintApply = mix(vec3(1.0), slopeTint, curvedSlope * clamp(slopeStrengthLocal, 0.0, 3.0));

        shaded *= slopeDarkening * tintApply;
    }

    float savedAoMul = 1.0; // Tracks AO darkening multiplier for light-based recovery

    if (bakedAOOn) {
        // Volume-aware AO: prefer interpolated AO and blend with slope/height cues.
        // This avoids patchy per-triangle color islands from provoking-vertex AO.
        float aoInterp = clamp(fragAOLevel, 0.0, 1.0);

        float h0 = ao.dccmBrightness.x;
        float h2 = max(ao.dccmBrightness.z, h0 + 0.001);
        float height01 = clamp((fragWorldPos.y - h0) / (h2 - h0), 0.0, 1.0);
        float valleyOcclusion = 1.0 - height01;

        float slopeOcclusion = smoothstep(0.18, 0.92, slopeFactor);
        float volumeAO = clamp(aoInterp * 0.55 + valleyOcclusion * 0.20 + slopeOcclusion * 0.25, 0.0, 1.0);

        float aoMul = 1.0 - pow(volumeAO, 1.25) * 0.34;
        aoMul = clamp(aoMul, 0.58, 1.0);
        shaded *= aoMul;
        savedAoMul = aoMul;
    }

    if (contourOn && contourSpacing > 0.0) {
        float contourVal = mod(fragWorldPos.y, contourSpacing);
        float contourEdge = min(contourVal, contourSpacing - contourVal);

        // Treat contourThickness as width along the terrain surface (meters), then
        // convert to an equivalent Y-threshold. Using a raw Y-threshold causes the
        // same setting to look thicker on shallow slopes and thinner on steep slopes.
        float lineWidthSurface = max(contourThickness, 0.000625);
        float slopeSpan = sqrt(max(1.0 - shadingNormal.y * shadingNormal.y, 0.0));

        // Flat horizontal faces have no well-defined contour crossings; drawing there
        // fills whole regions with lines, so suppress near-flat surfaces.
        bool isFlat = slopeSpan < 0.01;

        if (!isFlat) {
            float lineThickY = lineWidthSurface * slopeSpan;
            if (contourEdge < lineThickY) {
                shaded = lineColor;
            }
        }
    }

    if (sideFaceDarkOn) {
        float sideFaceShade = clamp(ao.dccmFillCol.w, 0.35, 1.0);
        float verticality = smoothstep(0.30, 0.92, 1.0 - abs(shadingNormal.y));
        shaded *= mix(1.0, sideFaceShade, verticality);
    }

    // Compute sun shadow up-front so enclosure can dissolve where sun reaches.
    float sunIntensity = shadow.shadowConfig.x;
    float sunShadow = 1.0;
    if (lighting.sunDirection.w > 0.0 && sunIntensity > 0.001) {
        float rawShadow = sampleSunShadow(fragWorldPos, shadingNormal);
        float sunAreaRadius = shadow.diagConfig.z;
        if (sunAreaRadius > 0.0) {
            float horizDist = length(fragWorldPos.xz - camera.cameraPos.xz);
            float fade = smoothstep(sunAreaRadius * 0.85, sunAreaRadius, horizDist);
            rawShadow = mix(rawShadow, 1.0, fade);
        }
        sunShadow = 1.0 - sunIntensity * (1.0 - rawShadow);
    }
    float sunReach = sunShadow * lighting.sunDirection.w;

    // Ambient enclosure: full (sunReach=0) so deep holes still go fully dark.
    float ambientEnclosure = computeSkyEnclosure(fragWorldPos, shadingNormal, 0.0);
    vec3 finalColor = shaded * lighting.sunColor.xyz * lighting.sunColor.w * ambientEnclosure;
    if (skyEnclosureVisualizeEnabled()) {
        outColor = vec4(skyEnclosureDebugColor(fragWorldPos, shadingNormal, sunReach), 1.0);
        return;
    }

    if (lighting.sunDirection.w > 0.0) {
        vec3 sunDir = normalize(lighting.sunDirection.xyz);
        float diffuse = max(dot(shadingNormal, -sunDir), 0.0);
        // Stepped inter-band transition (no gradients/dithering).
        diffuse = quantizeBandsStepped(diffuse, 5.0, 1.0, 0.0, 0.10);
        float lit = mix(0.52, 1.0, diffuse);

        // Sun-direct enclosure: dissolved by sunReach so light visibly
        // dispels the deep darkness (red → yellow) where sun rays hit,
        // while unreached areas keep the deep darkness.
        float sunEnclosure = computeSkyEnclosure(fragWorldPos, shadingNormal, sunReach);
        finalColor += shaded * lighting.sunColor.xyz * lit * lighting.sunDirection.w * sunShadow * sunEnclosure;
    }

    // Add point lights (accumulate-then-quantize for organic light blending)
    float totalBrightnessRawForAO = 0.0;

    {
        vec3  totalLightContrib = vec3(0.0);
        float totalBrightness  = 0.0;
        float totalBrightnessRaw = 0.0;
        float shadowEvidence = 0.0;
        float pulseEvidence = 0.0;
        bool diagEnabled = (shadow.diagConfig.x > 0.5);
        uvec2 diagPix = uvec2(gl_FragCoord.xy);
        bool diagSample = diagEnabled && ((diagPix.x & 7u) == 0u) && ((diagPix.y & 7u) == 0u);
        bool pointShadowsEnabled = (shadow.shadowConfig.y > 0.0);
        float pointShadowMapSize = max(shadow.shadowConfig.w, 1.0);
        const uint DIAG_SCALE = 64u;
        // --- Clustered lighting: fetch bitmask for this fragment's cluster ---
        uint clTileX = min(uint(gl_FragCoord.x / clusters.clusterTileDims.x), clusters.clusterGridDims.x - 1u);
        uint clTileY = min(uint(gl_FragCoord.y / clusters.clusterTileDims.y), clusters.clusterGridDims.y - 1u);
        float clViewDist = length(fragWorldPos - camera.cameraPos);
        uint clSlice = clamp(uint(log(clViewDist / clusters.clusterZParams.x) * clusters.clusterZParams.w),
                             0u, clusters.clusterGridDims.z - 1u);
        uint clIdx = (clTileY * clusters.clusterGridDims.x + clTileX) * clusters.clusterGridDims.z + clSlice;
        uint lightMask = (clIdx < clusters.clusterGridDims.w) ? clusters.clusterLightMasks[clIdx] : 0u;

        while (lightMask != 0u) {
            uint i = findLSB(lightMask);
            lightMask &= lightMask - 1u; // clear lowest set bit

            vec4 posRadius = lighting.pointLights[i].positionRadius;
            vec4 colorIntensity = lighting.pointLights[i].colorIntensity;
            vec2 pulseData = lighting.lightPulseData[i].xy;
            vec3 lightPos = posRadius.xyz;
            float lightRadius = posRadius.w;
            vec3 lightColor = colorIntensity.xyz;
            float lightIntensity = colorIntensity.w;
            float pulseStrength = pulseData.x;
            float breathScale = pulseData.y;
            if (breathScale < 0.01) breathScale = 1.0;

            vec4 contrib = calculatePointLightSmooth(
                fragWorldPos,
                shadingNormal,
                shaded,
                lightPos,
                lightRadius,
                lightColor,
                lightIntensity,
                pulseStrength,
                breathScale,
                i);
            if (contrib.a <= MIN_LIGHT_BRIGHTNESS) continue;
            if (diagSample) {
                atomicAdd(shadow.pointShadowDiag[i].y, DIAG_SCALE);
            }
            totalBrightnessRaw += contrib.a;
            float pulseStrengthNow = clamp(pulseStrength, 0.0, 1.0);
            float breathPulseNow = clamp((pulseData.y - 1.0) / 0.45, 0.0, 1.0);
            pulseEvidence = max(pulseEvidence, max(pulseStrengthNow, breathPulseNow));

            float pointShadow = 1.0;
            float shadowFarPlane = shadow.pointShadowInfo[i].y;
            if (pointShadowsEnabled &&
                shadow.pointShadowInfo[i].w > 0.0 &&
                contrib.a >= MIN_SHADOW_BRIGHTNESS) {
                if (diagSample) {
                    atomicAdd(shadow.pointShadowDiag[i].x, DIAG_SCALE);
                }
                bool highQualityShadow = (contrib.a >= HIGH_QUALITY_SHADOW_BRIGHTNESS);
                pointShadow = samplePointShadow(
                    fragWorldPos,
                    shadingNormal,
                    lightPos,
                    i,
                    shadowFarPlane,
                    pointShadowMapSize,
                    highQualityShadow);
                shadowEvidence = max(shadowEvidence, 1.0 - pointShadow);
                if (pointShadow <= 0.0) {
                    if (diagSample) {
                        atomicAdd(shadow.pointShadowDiag[i].z, DIAG_SCALE);
                    }
                    continue;
                }
            }

            totalLightContrib += contrib.rgb * pointShadow;
            totalBrightness  += contrib.a * pointShadow;
            if (diagSample) {
                atomicAdd(shadow.pointShadowDiag[i].w, DIAG_SCALE);
            }
        }

        totalBrightnessRawForAO = totalBrightness;

        if (totalBrightnessRaw > 0.001) {
            float clampedBright = clamp(totalBrightness, 0.0, 2.0);
            float clampedBrightRaw = clamp(totalBrightnessRaw, 0.0, 2.0);
            float shadowOcclusion = 0.0;
            if (clampedBrightRaw > 0.001) {
                shadowOcclusion = clamp(1.0 - (clampedBright / clampedBrightRaw), 0.0, 1.0);
            }

            if (clampedBright > 0.0001) {
                const float LIGHT_BANDS = 8.0;
                const float MAX_STYLIZED_BRIGHT = 2.0;
                float brightNorm = clamp(clampedBright / MAX_STYLIZED_BRIGHT, 0.0, 1.0);
                float brightBoost = smoothstep(0.35, 1.0, brightNorm) * 0.24;
                float bandInput = min(clampedBright * (1.0 + brightBoost), MAX_STYLIZED_BRIGHT);

                // Fade shadow-only edge treatment from the visible stepped band
                // field, not from blocked raw light. This keeps cross-light
                // washout aligned to the real orb rings and avoids leaking the
                // caster's hidden light back into its own ground shadow.
                float visibleShadowBand = quantizeBandsStepped(
                    bandInput,
                    LIGHT_BANDS,
                    MAX_STYLIZED_BRIGHT,
                    0.0,
                    0.08);
                float shadowRetain = clamp(
                    1.0 - (visibleShadowBand / MAX_STYLIZED_BRIGHT),
                    0.0,
                    1.0);
                shadowEvidence *= shadowRetain;
                shadowOcclusion *= shadowRetain;

                if (clampedBright < FAST_STYLIZATION_BRIGHTNESS) {
                    float fastBand = quantizeBandsStepped(
                        clampedBright,
                        LIGHT_BANDS,
                        MAX_STYLIZED_BRIGHT,
                        0.0,
                        0.08);
                    float fastScale = fastBand / max(clampedBright, 0.001);
                    totalLightContrib *= fastScale;
                } else {
                    float stableBand = quantizeBandsStepped(
                        bandInput,
                        LIGHT_BANDS,
                        MAX_STYLIZED_BRIGHT,
                        0.0,
                        0.08);

                    // Keep DCCM ground shadows clean: use the same stable stepped
                    // band field for the visible light and the shadow suppression
                    // logic. This removes the dotted/glitter edge artifact and
                    // stops shadow bands from drifting away from the actual orb
                    // band shape on the terrain.
                    float scale = stableBand / max(clampedBright, 0.001);
                    totalLightContrib *= scale;
                }
            }

            finalColor += totalLightContrib;
        }
    }

    // AO wash-out from light orbs — aggressive stepped recovery.
    // Sqrt curve makes even moderate light visibly eat into AO shadows.
    if (totalBrightnessRawForAO > 0.001 && savedAoMul < 0.999) {
        float bandForAO = floor(clamp(totalBrightnessRawForAO / 2.0, 0.0, 1.0) * 8.0);
        float aoFade = sqrt(clamp(bandForAO / 8.0, 0.0, 1.0));
        if (aoFade > 0.0) {
            float aoRecovery = 1.0 / max(savedAoMul, 0.001);
            finalColor *= mix(1.0, aoRecovery, aoFade);
        }
    }

    float fogDensity = lighting.skyColor.w;
    if (fogDensity > 0.0) {
        float distance = length(fragWorldPos - camera.cameraPos);
        float fogFactor = clamp(exp(-fogDensity * distance), 0.0, 1.0);
        finalColor = mix(lighting.skyColor.xyz, finalColor, fogFactor);
    }

    // ── Sun shadow debug visualization ────────────────────────────────
    // diagConfig.y == 5 → tint terrain by which cascade samples it.
    // Palette MUST match shaders/common/shadow_sampling.glsl
    // cascadeDebugColor() and DirectionalShadowWindow.cpp cascadeColor()
    // so wireframe boxes and on-terrain coloring use the SAME color.
    if (lighting.sunDirection.w > 0.0 && shadow.diagConfig.y > 4.5 && shadow.diagConfig.y < 5.5) {
        float horizontalDistance = length(fragWorldPos.xz - camera.cameraPos.xz);
        uint cascade = chooseSunCascadeForSample(fragWorldPos, horizontalDistance);
        vec3 tint;
        if      (cascade == 0u) tint = vec3(1.00, 0.25, 0.25);
        else if (cascade == 1u) tint = vec3(1.00, 0.65, 0.20);
        else if (cascade == 2u) tint = vec3(1.00, 1.00, 0.30);
        else if (cascade == 3u) tint = vec3(0.30, 1.00, 0.40);
        else if (cascade == 4u) tint = vec3(0.30, 0.85, 1.00);
        else                    tint = vec3(1.00, 0.40, 1.00);
        vec3 sunDir = shadow.sunDirTexelSize.xyz;
        float diffuse = max(dot(shadingNormal, -sunDir), 0.0);
        float lit = mix(0.55, 1.0, diffuse);
        outColor = vec4(tint * lit, 1.0);
        return;
    }

    outColor = vec4(finalColor, 1.0);
}

````

## FUNCTION src/world/World.cpp :: World::markTextureMaterialsDirty

Source: src/world/World.cpp lines 511-525

````cpp
void World::markTextureMaterialsDirty(const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks) {
    if (touchedChunks.empty()) return;

    // Texture paint changes material only, not occupancy/collision.
    //
    // Route it through a dedicated material-dirty queue instead of the normal
    // terrain-edit dirty queue. The normal fast-edit path intentionally bumps
    // chunk versions to cancel obsolete topology work; for texture paint spam
    // that cancellation policy can invalidate the very uploads that would make
    // the brush visible. Material dirties are coalesced until the current
    // visual upload/swap for a chunk has landed, then the latest stamp state is
    // rebaked once.
    markRuntimeVoxelChunks(touchedChunks);
    m_editRemeshScheduler.markMaterialChunksDirty(touchedChunks);
}
````


## FUNCTION src/world/World*.cpp :: World::beginTerrainEditVisualTracking

Resolved FUNCTION target to 13 candidate files. Exporting only matching function bodies.

Source: src/world/WorldTerrainEditCollision.cpp lines 240-264

````cpp
void World::beginTerrainEditVisualTracking(
    uint64_t editId,
    const TerrainEdit::TerrainEditOverlayStore::ChunkSet& touchedChunks,
    const std::chrono::steady_clock::time_point& startTime)
{
    PendingEditVisualAggregate agg{};
    agg.startTime = startTime;
    agg.totalChunks = static_cast<uint32_t>(std::min<size_t>(touchedChunks.size(), UINT32_MAX));
    m_pendingEditVisuals[editId] = agg;

    for (const auto& chunkCoord : touchedChunks) {
        auto existing = m_pendingEditVisualChunks.find(chunkCoord);
        if (existing != m_pendingEditVisualChunks.end() && existing->second.editId != editId) {
            auto oldAggIt = m_pendingEditVisuals.find(existing->second.editId);
            if (oldAggIt != m_pendingEditVisuals.end() &&
                (oldAggIt->second.readyChunks + oldAggIt->second.supersededChunks) < oldAggIt->second.totalChunks) {
                ++oldAggIt->second.supersededChunks;
                syncTerrainEditVisualState(existing->second.editId, /*eraseIfComplete=*/true);
            }
        }
        m_pendingEditVisualChunks[chunkCoord] = PendingEditVisualChunk{editId, startTime};
    }

    syncTerrainEditVisualState(editId, /*eraseIfComplete=*/false);
}
````


## FUNCTION src/world/World*.cpp :: World::noteChunkVisualReady

Resolved FUNCTION target to 13 candidate files. Exporting only matching function bodies.

Source: src/world/WorldTerrainEditCollision.cpp lines 323-718

````cpp
void World::noteChunkVisualReady(
    const glm::ivec3& coord,
    const std::chrono::steady_clock::time_point& uploadEnqueueTime,
    const std::chrono::steady_clock::time_point& finalizeTime,
    int lodLevel,
    uint64_t vramBytes,
    uint32_t vertexCount,
    uint32_t indexCount,
    const ChunkDebugAttribution* debugInfo)
{
    const float pipelineMs = (uploadEnqueueTime == std::chrono::steady_clock::time_point{})
        ? 0.0f
        : std::chrono::duration<float, std::milli>(finalizeTime - uploadEnqueueTime).count();

    auto mergeMissingDebug = [](ChunkDebugAttribution& dst, const ChunkDebugAttribution& src) {
        if (dst.artifactSource == ChunkArtifactSource::Unknown &&
            src.artifactSource != ChunkArtifactSource::Unknown) {
            dst.artifactSource = src.artifactSource;
        }
        if (dst.collisionSource == ChunkCollisionSource::Unknown &&
            src.collisionSource != ChunkCollisionSource::Unknown) {
            dst.collisionSource = src.collisionSource;
        }
        if (dst.residency == ChunkResidencyKind::Unknown &&
            src.residency != ChunkResidencyKind::Unknown) {
            dst.residency = src.residency;
        }
        if (dst.workModel == ChunkWorkModel::Unknown &&
            src.workModel != ChunkWorkModel::Unknown) {
            dst.workModel = src.workModel;
        }
        if (dst.meshMode == 0xFF && src.meshMode != 0xFF) {
            dst.meshMode = src.meshMode;
        }
        if (dst.subChunkCount == 0 && src.subChunkCount != 0) {
            dst.subChunkCount = src.subChunkCount;
        }
        if (dst.dirtyPages == 0 && src.dirtyPages != 0) {
            dst.dirtyPages = src.dirtyPages;
        }
        if (dst.rebuiltPages == 0 && src.rebuiltPages != 0) {
            dst.rebuiltPages = src.rebuiltPages;
        }
        if (dst.residentPages == 0 && src.residentPages != 0) {
            dst.residentPages = src.residentPages;
        }
        if (dst.evictedPages == 0 && src.evictedPages != 0) {
            dst.evictedPages = src.evictedPages;
        }
        if (dst.artifactGeneration == 0 && src.artifactGeneration != 0) {
            dst.artifactGeneration = src.artifactGeneration;
        }
        if (dst.uploadBytes == 0 && src.uploadBytes != 0) {
            dst.uploadBytes = src.uploadBytes;
        }
        dst.artifactCacheHit = dst.artifactCacheHit || src.artifactCacheHit;
        dst.artifactCacheResident = dst.artifactCacheResident || src.artifactCacheResident;
        dst.fromLodBatch = dst.fromLodBatch || src.fromLodBatch;
        dst.fromTerrainEdit = dst.fromTerrainEdit || src.fromTerrainEdit;
    };

    auto inferWorkModel = [](uint8_t meshMode) {
        if (meshMode == static_cast<uint8_t>(ChunkMeshMode::PagedEditable) ||
            meshMode == static_cast<uint8_t>(ChunkMeshMode::PagedConsolidating)) {
            return ChunkWorkModel::PagedLocal;
        }
        if (meshMode != 0xFF) {
            return ChunkWorkModel::MonolithicChunk;
        }
        return ChunkWorkModel::Unknown;
    };

    ChunkDebugAttribution resolvedDebug = debugInfo ? *debugInfo : ChunkDebugAttribution{};
    bool gpuResident = false;
    bool pendingBatchResident = false;
    const bool needsRegistryProbe =
        resolvedDebug.meshMode == 0xFF ||
        resolvedDebug.subChunkCount == 0 ||
        resolvedDebug.uploadBytes == 0 ||
        resolvedDebug.residency == ChunkResidencyKind::Unknown ||
        resolvedDebug.residency == ChunkResidencyKind::PendingBatch;
    if (needsRegistryProbe) {
        const entt::entity entity = findChunk(coord);
        if (entity != entt::null) {
            std::shared_lock regLock(m_registryMutex);
            if (m_registry.valid(entity)) {
                if (m_registry.all_of<Chunk>(entity)) {
                    const auto& chunk = m_registry.get<Chunk>(entity);
                    if (resolvedDebug.meshMode == 0xFF) {
                        resolvedDebug.meshMode = static_cast<uint8_t>(chunk.meshMode);
                    }
                }
                if (m_registry.all_of<MeshHandle>(entity)) {
                    const auto& mesh = m_registry.get<MeshHandle>(entity);
                    gpuResident = true;
                    if (resolvedDebug.subChunkCount == 0) {
                        resolvedDebug.subChunkCount = mesh.subChunkCount;
                    }
                    if (resolvedDebug.uploadBytes == 0) {
                        resolvedDebug.uploadBytes = mesh.vb.size + mesh.ib.size;
                    }
                }
                if (m_registry.all_of<PendingMeshHandle>(entity)) {
                    const auto& pending = m_registry.get<PendingMeshHandle>(entity);
                    pendingBatchResident = true;
                    mergeMissingDebug(resolvedDebug, pending.debugInfo);
                }
            }
        }
    }

    if (resolvedDebug.collisionSource == ChunkCollisionSource::Unknown) {
        if (lodLevel > 0) {
            resolvedDebug.collisionSource = ChunkCollisionSource::None;
        } else {
            auto collisionIt = m_chunkCollisionSources.find(coord);
            if (collisionIt != m_chunkCollisionSources.end()) {
                resolvedDebug.collisionSource = collisionIt->second;
            }
        }
    }
    if (resolvedDebug.artifactGeneration == 0 &&
        resolvedDebug.artifactSource != ChunkArtifactSource::PrecomputedTerrain) {
        const int effectiveLod = getEffectiveLODForChunk(coord, lodLevel);
        const TerrainType terrainType = getTerrainTypeForChunk(coord, lodLevel);
        const uint64_t artifactGen = getEditArtifactGeneration(coord, terrainType, effectiveLod);
        if (artifactGen != 0) {
            resolvedDebug.artifactGeneration = artifactGen;
            resolvedDebug.artifactCacheResident = true;
        }
    }
    if (resolvedDebug.workModel == ChunkWorkModel::Unknown) {
        resolvedDebug.workModel = inferWorkModel(resolvedDebug.meshMode);
    }
    if (resolvedDebug.residency == ChunkResidencyKind::Unknown ||
        resolvedDebug.residency == ChunkResidencyKind::PendingBatch) {
        resolvedDebug.residency = deriveChunkResidencyKind(
            gpuResident,
            resolvedDebug.artifactCacheResident,
            pendingBatchResident && !gpuResident);
    }
    if (resolvedDebug.uploadBytes == 0) {
        resolvedDebug.uploadBytes = vramBytes;
    }

    ChunkVisualHistoryEntry visualEntry{};
    visualEntry.sequence = m_chunkVisualHistory.totalCount + 1;
    visualEntry.chunkCoord = coord;
    visualEntry.lodLevel = lodLevel;
    visualEntry.vramBytes = vramBytes;
    visualEntry.vertexCount = vertexCount;
    visualEntry.indexCount = indexCount;
    visualEntry.pipelineMs = pipelineMs;
    visualEntry.visibleMs = pipelineMs;
    visualEntry.timestampSec = std::chrono::duration<float>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
    visualEntry.uploadBytes = resolvedDebug.uploadBytes;
    visualEntry.artifactGeneration = resolvedDebug.artifactGeneration;
    visualEntry.artifactSource = resolvedDebug.artifactSource;
    visualEntry.collisionSource = resolvedDebug.collisionSource;
    visualEntry.residency = resolvedDebug.residency;
    visualEntry.workModel = resolvedDebug.workModel;
    visualEntry.meshMode = resolvedDebug.meshMode;
    visualEntry.subChunkCount = resolvedDebug.subChunkCount;
    visualEntry.dirtyPages = resolvedDebug.dirtyPages;
    visualEntry.rebuiltPages = resolvedDebug.rebuiltPages;
    visualEntry.residentPages = resolvedDebug.residentPages;
    visualEntry.evictedPages = resolvedDebug.evictedPages;
    visualEntry.artifactCacheHit = resolvedDebug.artifactCacheHit;
    visualEntry.artifactCacheResident = resolvedDebug.artifactCacheResident;
    visualEntry.fromLodBatch = resolvedDebug.fromLodBatch;

    auto pendingIt = m_pendingEditVisualChunks.find(coord);
    if (pendingIt != m_pendingEditVisualChunks.end() &&
        uploadEnqueueTime >= pendingIt->second.startTime) {
        visualEntry.fromEdit = true;
        visualEntry.editId = pendingIt->second.editId;
        visualEntry.visibleMs = std::chrono::duration<float, std::milli>(
            finalizeTime - pendingIt->second.startTime).count();

        // Populate per-stage breakdown from the remesh scheduler's timing record
        TerrainEdit::TerrainEditRemeshScheduler::ChunkTimingRecord timing;
        if (m_editRemeshScheduler.consumeChunkTiming(coord, timing)) {
            const auto editStart = pendingIt->second.startTime;
            visualEntry.waitDispatchMs = std::chrono::duration<float, std::milli>(
                timing.dispatchTime - editStart).count();
            visualEntry.waitJobMs = std::chrono::duration<float, std::milli>(
                timing.jobStartTime - timing.dispatchTime).count();
            visualEntry.meshMs = std::chrono::duration<float, std::milli>(
                timing.meshDoneTime - timing.jobStartTime).count();
            visualEntry.waitDrainMs = std::chrono::duration<float, std::milli>(
                timing.drainTime - timing.meshDoneTime).count();
            visualEntry.uploadMs = std::chrono::duration<float, std::milli>(
                finalizeTime - timing.drainTime).count();
            visualEntry.isFastMode = timing.isFastMode;
            visualEntry.meshLodLevel = timing.meshLodLevel;

            // Mesh sub-stage breakdown + workload from MeshStats
            visualEntry.cacheBuildMs  = timing.meshStats.cacheBuildMs;
            visualEntry.greedyMeshMs  = timing.meshStats.greedyMeshMs;
            visualEntry.postProcessMs = timing.meshStats.postProcessMs;
            visualEntry.downsampleMs  = timing.meshStats.downsampleMs;
            visualEntry.downsampleCacheState = timing.meshStats.downsampleCacheState;
            visualEntry.bandLocalYMin = timing.meshStats.bandLocalYMin;
            visualEntry.bandLocalYMax = timing.meshStats.bandLocalYMax;
            visualEntry.bandActive    = timing.meshStats.bandActive;
            visualEntry.bandFacesEmitted = timing.meshStats.bandFacesEmitted;
            visualEntry.cacheVoxels   = timing.meshStats.cacheVoxels;
            visualEntry.solidVoxels   = timing.meshStats.solidVoxels;
            visualEntry.facesEmitted  = timing.meshStats.facesEmitted;
            visualEntry.scanYRange    = timing.meshStats.scanYRange;
            visualEntry.cacheDimXZ    = timing.meshStats.cacheDimXZ;
            visualEntry.adaptiveEnabled = timing.meshStats.adaptiveEnabled;
            visualEntry.adaptiveLeafRegions = timing.meshStats.adaptiveLeafRegions;
            visualEntry.adaptiveSplitRegions = timing.meshStats.adaptiveSplitRegions;
            visualEntry.adaptiveMaxDepth = timing.meshStats.adaptiveMaxDepth;
            visualEntry.adaptivePeakRegionVoxels = timing.meshStats.adaptivePeakRegionVoxels;
            visualEntry.adaptivePeakYRange = timing.meshStats.adaptivePeakYRange;
            visualEntry.adaptiveWorkVoxels = timing.meshStats.adaptiveWorkVoxels;
            visualEntry.monolithicWorkVoxels = timing.meshStats.monolithicWorkVoxels;

            visualEntry.loadBaseRenderDist = timing.loadSnapshot.baseRenderDist;
            visualEntry.loadEffectiveRenderDist = timing.loadSnapshot.effectiveRenderDist;
            visualEntry.loadExtensionRings = timing.loadSnapshot.extensionRings;
            visualEntry.loadMeasuredThroughput = timing.loadSnapshot.measuredThroughput;
            visualEntry.loadPendingCreates = timing.loadSnapshot.pendingCreates;
            visualEntry.loadPendingDestroys = timing.loadSnapshot.pendingDestroys;
            visualEntry.loadLodRemeshQueue = timing.loadSnapshot.lodRemeshQueue;
            visualEntry.loadPendingLodRemeshes = timing.loadSnapshot.pendingLodRemeshes;
            visualEntry.loadEditRemeshPending = timing.loadSnapshot.editRemeshPending;
            visualEntry.loadUploadQueue = timing.loadSnapshot.uploadQueue;
            visualEntry.loadFinalizeQueue = timing.loadSnapshot.finalizeQueue;
            visualEntry.loadInFlightSkips = timing.loadSnapshot.inFlightSkips;
            visualEntry.loadBufferPressure = timing.loadSnapshot.bufferPressure;
            visualEntry.loadEditJobsInFlight = timing.loadSnapshot.editJobsInFlight;
        }

        // Capture overlay fill state at edit time for diagnostics
        visualEntry.sphereFills   = m_lastEditDiag.sphereFillCount;
        visualEntry.boxFills      = m_lastEditDiag.boxFillCount;
        visualEntry.cylinderFills = m_lastEditDiag.cylinderFillCount;
        visualEntry.bricks        = m_lastEditDiag.brickCount;

        auto aggIt = m_pendingEditVisuals.find(pendingIt->second.editId);
        if (aggIt != m_pendingEditVisuals.end()) {
            if (aggIt->second.readyChunks == 0) {
                aggIt->second.visualFirstChunkMs = visualEntry.visibleMs;
            }
            aggIt->second.visualCompleteMs =
                std::max(aggIt->second.visualCompleteMs, visualEntry.visibleMs);
            ++aggIt->second.readyChunks;
            aggIt->second.uploadBytes += visualEntry.uploadBytes;
            if (visualEntry.artifactSource == ChunkArtifactSource::RuntimeEditBuild) {
                ++aggIt->second.artifactBuilds;
            } else if (visualEntry.artifactSource == ChunkArtifactSource::EditArtifactCache ||
                       visualEntry.artifactSource == ChunkArtifactSource::DeferredArtifactCache) {
                ++aggIt->second.artifactCacheHits;
            } else if (visualEntry.artifactSource == ChunkArtifactSource::PrecomputedTerrain) {
                ++aggIt->second.precomputedLoads;
            }
            if (visualEntry.collisionSource == ChunkCollisionSource::BaseCollisionCache) {
                ++aggIt->second.collisionBaseCache;
            } else if (visualEntry.collisionSource == ChunkCollisionSource::EditMeshPacked) {
                ++aggIt->second.collisionEditPacked;
            } else if (visualEntry.collisionSource == ChunkCollisionSource::ArtifactRefresh) {
                ++aggIt->second.collisionArtifactRefresh;
            } else if (visualEntry.collisionSource == ChunkCollisionSource::ExistingEditedCollision) {
                ++aggIt->second.collisionExistingEdit;
            }
            if (visualEntry.residency == ChunkResidencyKind::GPUResident ||
                visualEntry.residency == ChunkResidencyKind::GPUAndArtifactCache) {
                ++aggIt->second.gpuResidentChunks;
            }
            if (visualEntry.artifactCacheResident) {
                ++aggIt->second.artifactResidentChunks;
            }
            if (visualEntry.workModel == ChunkWorkModel::PagedLocal) {
                ++aggIt->second.pagedChunks;
            } else {
                ++aggIt->second.monolithicChunks;
            }
            aggIt->second.dirtyPages += visualEntry.dirtyPages;
            aggIt->second.rebuiltPages += visualEntry.rebuiltPages;
            aggIt->second.residentPages += visualEntry.residentPages;
            aggIt->second.evictedPages += visualEntry.evictedPages;
            syncTerrainEditVisualState(pendingIt->second.editId, /*eraseIfComplete=*/true);
        }
        m_pendingEditVisualChunks.erase(pendingIt);
    }

    if (m_lodSwitchDiag.active &&
        m_lodSwitchDiag.completedMs == 0.0f &&
        visualEntry.lodLevel == m_lodSwitchDiag.band) {
        ++m_lodSwitchDiag.readyVisualEntries;
        m_lodSwitchDiag.uploadedBytesTotal += visualEntry.uploadBytes;
        if (visualEntry.artifactSource == ChunkArtifactSource::RuntimeEditBuild) {
            ++m_lodSwitchDiag.artifactBuilds;
        } else if (visualEntry.artifactSource == ChunkArtifactSource::EditArtifactCache ||
                   visualEntry.artifactSource == ChunkArtifactSource::DeferredArtifactCache) {
            ++m_lodSwitchDiag.artifactCacheHits;
        } else if (visualEntry.artifactSource == ChunkArtifactSource::PrecomputedTerrain) {
            ++m_lodSwitchDiag.precomputedLoads;
        }
        if (visualEntry.collisionSource == ChunkCollisionSource::BaseCollisionCache) {
            ++m_lodSwitchDiag.collisionBaseCache;
        } else if (visualEntry.collisionSource == ChunkCollisionSource::EditMeshPacked) {
            ++m_lodSwitchDiag.collisionEditPacked;
        } else if (visualEntry.collisionSource == ChunkCollisionSource::ArtifactRefresh) {
            ++m_lodSwitchDiag.collisionArtifactRefresh;
        } else if (visualEntry.collisionSource == ChunkCollisionSource::ExistingEditedCollision) {
            ++m_lodSwitchDiag.collisionExistingEdit;
        }
        if (visualEntry.residency == ChunkResidencyKind::GPUResident ||
            visualEntry.residency == ChunkResidencyKind::GPUAndArtifactCache) {
            ++m_lodSwitchDiag.gpuResidentChunks;
        }
        if (visualEntry.artifactCacheResident) {
            ++m_lodSwitchDiag.artifactResidentChunks;
        }
        if (visualEntry.workModel == ChunkWorkModel::PagedLocal) {
            ++m_lodSwitchDiag.pagedChunks;
        } else {
            ++m_lodSwitchDiag.monolithicChunks;
        }
        m_lodSwitchDiag.dirtyPages += visualEntry.dirtyPages;
        m_lodSwitchDiag.rebuiltPages += visualEntry.rebuiltPages;
        m_lodSwitchDiag.residentPages += visualEntry.residentPages;
        m_lodSwitchDiag.evictedPages += visualEntry.evictedPages;
    }

    const bool loadLikeEntry = !visualEntry.fromEdit &&
        !(!visualEntry.fromLodBatch &&
          visualEntry.artifactSource == ChunkArtifactSource::RuntimeEditBuild &&
          visualEntry.collisionSource == ChunkCollisionSource::EditMeshPacked &&
          visualEntry.meshMode != static_cast<uint8_t>(ChunkMeshMode::MonolithicPristine));
    const bool precomputedSource =
        visualEntry.artifactSource == ChunkArtifactSource::PrecomputedTerrain;
    const bool runtimeVoxelNeedsNonPrecomputed =
        precomputedSource && chunkNeedsRuntimeVoxel(coord);

    if (visualEntry.fromEdit) {
        m_chunkLoadTrackers[coord] = ChunkLoadTracker{visualEntry.editId, 0};
    } else if (loadLikeEntry) {
        auto trackerIt = m_chunkLoadTrackers.find(coord);
        if (trackerIt != m_chunkLoadTrackers.end() && trackerIt->second.editId != 0) {
            trackerIt->second.loadCount += 1;
            visualEntry.editId = trackerIt->second.editId;
            visualEntry.consecutiveReloads = trackerIt->second.loadCount;
        }

        if (runtimeVoxelNeedsNonPrecomputed) {
            std::string reason = "RuntimeVoxelChunkLoadedPrecomputed";
            if (visualEntry.editId != 0) {
                reason += " edit=" + std::to_string(visualEntry.editId);
            }
            noteChunkVisualError(
                &coord,
                lodLevel,
                "VisualReady",
                reason.c_str(),
                0,
                0,
                0,
                &resolvedDebug);

            // Flag this as a visual hole — runtime voxel chunk got precomputed data
            m_chunkHoleTracker.flagHole(coord, reason);
        }
    }

    m_chunkVisualHistory.push(visualEntry);

    // Record VisualReady event and try to resolve holes if correct source loaded
    {
        ChunkHoleEvent holeEv;
        holeEv.type = ChunkHoleEvent::Type::MeshLoaded;
        holeEv.timestampSec = ChunkHoleEvent::nowSec();
        holeEv.toLOD = lodLevel;
        holeEv.vertexCount = visualEntry.vertexCount;
        holeEv.indexCount = visualEntry.indexCount;
        holeEv.subChunkCount = visualEntry.subChunkCount;
        holeEv.artifactSource = visualEntry.artifactSource;
        holeEv.detail = "VisualReady";
        m_chunkHoleTracker.recordMeshLoadedAndResolve(
            coord,
            std::move(holeEv),
            !runtimeVoxelNeedsNonPrecomputed);
    }

    if (m_lastEditDiag.editId != 0) {
        m_lastEditDiag.visualPendingChunks =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisualChunks.size(), UINT32_MAX));
        m_lastEditDiag.visualPendingEdits =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisuals.size(), UINT32_MAX));
    }
}
````


## FUNCTION src/world/World*.cpp :: World::syncTerrainEditVisualState

Resolved FUNCTION target to 13 candidate files. Exporting only matching function bodies.

Source: src/world/WorldTerrainEditCollision.cpp lines 181-238

````cpp
void World::syncTerrainEditVisualState(uint64_t editId, bool eraseIfComplete)
{
    auto aggIt = m_pendingEditVisuals.find(editId);
    if (aggIt == m_pendingEditVisuals.end()) {
        if (m_lastEditDiag.editId != 0) {
            m_lastEditDiag.visualPendingChunks =
                static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisualChunks.size(), UINT32_MAX));
            m_lastEditDiag.visualPendingEdits =
                static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisuals.size(), UINT32_MAX));
        }
        return;
    }

    const auto applyVisualFields = [](auto& target, const PendingEditVisualAggregate& agg) {
        target.visualFirstChunkMs = agg.visualFirstChunkMs;
        target.visualCompleteMs = agg.visualCompleteMs;
        target.visualChunksTotal = agg.totalChunks;
        target.visualChunksReady = agg.readyChunks;
        target.visualChunksSuperseded = agg.supersededChunks;
        target.visualComplete = (agg.readyChunks + agg.supersededChunks) >= agg.totalChunks;
        target.visualUploadBytes = agg.uploadBytes;
        target.visualArtifactBuilds = agg.artifactBuilds;
        target.visualArtifactCacheHits = agg.artifactCacheHits;
        target.visualPrecomputedLoads = agg.precomputedLoads;
        target.visualCollisionBaseCache = agg.collisionBaseCache;
        target.visualCollisionEditPacked = agg.collisionEditPacked;
        target.visualCollisionArtifactRefresh = agg.collisionArtifactRefresh;
        target.visualCollisionExistingEdit = agg.collisionExistingEdit;
        target.visualGpuResidentChunks = agg.gpuResidentChunks;
        target.visualArtifactResidentChunks = agg.artifactResidentChunks;
        target.visualMonolithicChunks = agg.monolithicChunks;
        target.visualPagedChunks = agg.pagedChunks;
        target.visualDirtyPages = agg.dirtyPages;
        target.visualRebuiltPages = agg.rebuiltPages;
        target.visualResidentPages = agg.residentPages;
        target.visualEvictedPages = agg.evictedPages;
    };

    if (m_lastEditDiag.editId == editId) {
        applyVisualFields(m_lastEditDiag, aggIt->second);
    }
    if (auto* histEntry = findTerrainEditHistoryEntry(editId)) {
        applyVisualFields(*histEntry, aggIt->second);
    }

    const bool completeNow =
        (aggIt->second.readyChunks + aggIt->second.supersededChunks) >= aggIt->second.totalChunks;
    if (completeNow && eraseIfComplete) {
        m_pendingEditVisuals.erase(aggIt);
    }

    if (m_lastEditDiag.editId != 0) {
        m_lastEditDiag.visualPendingChunks =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisualChunks.size(), UINT32_MAX));
        m_lastEditDiag.visualPendingEdits =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisuals.size(), UINT32_MAX));
    }
}
````
