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


## src\world\edit\meshing\greedy\TerrainEditGreedyRegions.cpp

Description: No CC-DESC found. C++ struct 'SurfaceFace'.

````cpp
// GPT-DESC: Emits adaptive greedy mesh regions from a prebuilt terrain solid cache.
#include "../TerrainEditMesherInternal.h"

namespace TerrainEdit {
using namespace MesherInternal;

uint32_t TerrainEditMesher::meshGreedyAdaptiveRegions(
    MeshResult& result,
    const TerrainFieldSource& field,
    const SolidCache& cache,
    const std::vector<uint8_t>& cacheData,
    uint32_t solidCount,
    int chunkSizeVoxels,
    int chunkHeightVoxels,
    int baseX,
    int baseY,
    int baseZ,
    int scanMinLocalY,
    int scanMaxLocalY,
    int cacheDimXZ,
    int cacheMinY,
    int lodLevel,
    bool skipAmbientOcclusion,
    const RemeshCancellationToken* cancelToken)
{
    const size_t colCount = static_cast<size_t>(chunkSizeVoxels) * chunkSizeVoxels;
    std::vector<int> colMinY(colCount, scanMaxLocalY + 1);
    std::vector<int> colMaxY(colCount, scanMinLocalY - 1);

    for (int cz = 1; cz <= chunkSizeVoxels; ++cz) {
        if (wasCancelled(cancelToken)) {
            return 0;
        }
        const int lz = cz - 1;
        for (int cx = 1; cx <= chunkSizeVoxels; ++cx) {
            const int lx = cx - 1;
            const size_t colIdx = static_cast<size_t>(lz * chunkSizeVoxels + lx);
            for (int ly = scanMinLocalY; ly <= scanMaxLocalY; ++ly) {
                const int cy = baseY + ly - cacheMinY;
                if (cacheData[static_cast<size_t>((cy * cacheDimXZ + cz) * cacheDimXZ + cx)] != 0) {
                    if (ly < colMinY[colIdx]) {
                        colMinY[colIdx] = ly;
                    }
                    if (ly > colMaxY[colIdx]) {
                        colMaxY[colIdx] = ly;
                    }
                }
            }
        }
    }

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
        int maxX{0};
        int minY{0};
        int maxY{0};
        int minZ{0};
        int maxZ{0};
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
                if (colLo < outMinY) {
                    outMinY = colLo;
                }
                if (colHi > outMaxY) {
                    outMaxY = colHi;
                }
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
                    if (sf.v < minV) {
                        minV = sf.v;
                    }
                    if (sf.v > maxV) {
                        maxV = sf.v;
                    }
                }

                const int mergeVLimit = maxV + 1;
                for (int j = minV; j <= maxV; ++j) {
                    for (int i = 0; i < uDim; ++i) {
                        const int idx = i + j * uDim;
                        if (!mask[static_cast<size_t>(idx)] || visited[static_cast<size_t>(idx)]) {
                            continue;
                        }

                        const FaceAO curAO = aoMask[static_cast<size_t>(idx)];
                        const uint32_t curMaterial = materialMask[static_cast<size_t>(idx)];
                        const int planeBl = static_cast<int>(curAO.bl);
                        const int planeDu = static_cast<int>(curAO.br) - planeBl;
                        const int planeDv = static_cast<int>(curAO.tl) - planeBl;
                        const bool planeValid =
                            static_cast<int>(curAO.tr) == (planeBl + planeDu + planeDv);

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
                                if (canExpand) {
                                    ++qhLocal;
                                }
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
                                if (canExpand) {
                                    ++qhLocal;
                                }
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

                        addQuad(result, axis, n, uOffset + i, vOffset + j, qw, qh,
                                dir, face, curMaterial, emitAO);
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

    const bool useAdaptivePartition = lodLevel == 0 &&
        (result.stats.scanYRange > 96 || solidCount > 350000u);

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

    return facesEmitted;
}

} // namespace TerrainEdit

````
