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


## shaders\common\terrain_materials.glsl

Description: No CC-DESC found. C++ struct 'MaterialOverlayCell'.

````glsl
// GPT-DESC: Provides voxel terrain material overlay lookup and procedural material color sampling.

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

const uint MATERIAL_PACKED_BIT = 0x80000000u;
const uint MATERIAL_LAYOUT_V2_BIT = 0x00001000u;

vec3 materialBase(uint type) {
    if (type == 1u) return vec3(0.225, 0.155, 0.115); // mud
    if (type == 2u) return vec3(0.620, 0.510, 0.360); // drysand
    if (type == 3u) return vec3(0.770, 0.620, 0.420); // sand
    if (type == 4u) return vec3(0.225, 0.155, 0.115); // dirt, same family as mud
    return vec3(0.185, 0.350, 0.115);                 // grass
}

vec3 materialHi(uint type) {
    if (type == 1u) return vec3(0.315, 0.235, 0.175);
    if (type == 2u) return vec3(0.785, 0.675, 0.490);
    if (type == 3u) return vec3(0.900, 0.770, 0.545);
    if (type == 4u) return vec3(0.315, 0.235, 0.175);
    return vec3(0.340, 0.535, 0.175);
}

vec3 materialLo(uint type) {
    if (type == 1u) return vec3(0.070, 0.050, 0.040);
    if (type == 2u) return vec3(0.385, 0.295, 0.205);
    if (type == 3u) return vec3(0.570, 0.430, 0.275);
    if (type == 4u) return vec3(0.070, 0.050, 0.040);
    return vec3(0.070, 0.155, 0.050);
}

vec3 materialAccent(uint type) {
    if (type == 1u) return vec3(0.180, 0.118, 0.090);
    if (type == 2u) return vec3(0.585, 0.470, 0.325);
    if (type == 3u) return vec3(0.815, 0.670, 0.440);
    if (type == 4u) return vec3(0.180, 0.118, 0.090);
    return vec3(0.235, 0.430, 0.120);
}

vec2 materialHash22(vec2 p) {
    return vec2(
        hash12(p),
        hash12(p + vec2(37.71, 17.17)));
}

float materialSignedNoise(vec2 p) {
    return hash12(p) * 2.0 - 1.0;
}

float materialValueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = hash12(i);
    float b = hash12(i + vec2(1.0, 0.0));
    float c = hash12(i + vec2(0.0, 1.0));
    float d = hash12(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

vec2 materialRotate(vec2 p, float a) {
    float c = cos(a);
    float s = sin(a);
    return mat2(c, -s, s, c) * p;
}

vec2 materialNaturalCoord(vec2 materialPx, vec2 voxelCoord, uint type, uint variant) {
    vec2 p = materialPx;
    float angle = (float(type) - 2.0) * 0.145;
    vec2 rotated = materialRotate(p, angle);
    float warpX = materialValueNoise(p * 0.034 + vec2(float(type) * 17.0, 11.0)) - 0.5;
    float warpY = materialValueNoise(p * 0.041 + vec2(29.0, float(type) * 13.0)) - 0.5;
    float smallX = materialValueNoise(p * 0.115 + vec2(float(variant) * 3.0, 47.0)) - 0.5;
    float smallY = materialValueNoise(p * 0.127 + vec2(53.0, float(variant) * 5.0)) - 0.5;
    return rotated + vec2(warpX, warpY) * 5.5 + vec2(smallX, smallY) * 1.4;
}

float materialPixelLine(vec2 p, float period, float skew, float phase) {
    float v = fract((p.x + p.y * skew + phase) / max(period, 0.001));
    return 1.0 - abs(v - 0.5) * 2.0;
}

float materialBrokenLine(vec2 p, float period, float skew, float phase, vec2 breakSeed) {
    float line = materialPixelLine(p, period, skew, phase);
    float broken = hash12(floor(p * 0.25) + breakSeed);
    return line * smoothstep(0.22, 0.78, broken);
}

float materialCellCrackMask(vec2 pixelCoord,
                            float density,
                            float thinWidth,
                            float wideWidth,
                            float variantPhase) {
    vec2 p = pixelCoord * density;
    vec2 baseCell = floor(p);
    vec2 f = fract(p);

    float nearest = 16.0;
    float secondNearest = 16.0;

    for (int cy = -1; cy <= 1; ++cy) {
        for (int cx = -1; cx <= 1; ++cx) {
            vec2 g = vec2(float(cx), float(cy));
            vec2 o = materialHash22(baseCell + g + vec2(variantPhase, variantPhase * 0.37));
            o = o * 0.80 + vec2(0.10);
            vec2 r = g + o - f;
            float d = dot(r, r);
            if (d < nearest) {
                secondNearest = nearest;
                nearest = d;
            } else if (d < secondNearest) {
                secondNearest = d;
            }
        }
    }

    float border = secondNearest - nearest;
    return 1.0 - smoothstep(thinWidth, wideWidth, border);
}

float materialDryCrackMask(vec2 pixelCoord, float variantPhase) {
    // Voronoi-boundary cracks. The feature grid is not used as color blocks;
    // only cell borders become thin dry crack lines.
    return materialCellCrackMask(pixelCoord, 0.105, 0.016, 0.086, variantPhase);
}

float materialBranchCrack(vec2 p, uint variant, uint face) {
    vec2 q = materialNaturalCoord(p, floor(p / 16.0), 2u, variant);
    float a = materialPixelLine(q, 19.0, 0.70, float(variant) * 7.0 + float(face) * 3.0);
    float b = materialPixelLine(q.yx, 23.0, -0.52, float(variant) * 11.0);
    float c = materialPixelLine(q + vec2(9.0, 3.0), 31.0, 0.18, float(face) * 5.0);
    return max(smoothstep(0.972, 0.998, a), max(smoothstep(0.975, 0.998, b), smoothstep(0.986, 0.999, c)));
}

float materialBladeStroke(vec2 p, vec2 base, float height, float lean, float width) {
    float t = clamp((base.y - p.y) / max(height, 0.001), 0.0, 1.0);
    float yMask = step(base.y - height, p.y) * step(p.y, base.y);
    float centerX = base.x + lean * t;
    return step(abs(p.x - centerX), width) * yMask;
}

vec3 materialGrassBladeMasks(vec2 pixelCoord, uint variant, uint face) {
    vec2 cellSize = vec2(4.0, 4.0);
    vec2 cell = floor(pixelCoord / cellSize);
    vec3 masks = vec3(0.0);

    for (int gy = -1; gy <= 1; ++gy) {
        for (int gx = -1; gx <= 1; ++gx) {
            vec2 c = cell + vec2(float(gx), float(gy));
            vec2 h2 = materialHash22(c + vec2(float(variant) * 11.0, float(face) * 17.0));
            float h3 = hash12(c + vec2(71.0 + float(variant) * 3.0, 19.0 + float(face)));
            float h4 = hash12(c + vec2(13.0, 97.0 + float(variant) * 5.0));
            float bladeEnabled = step(0.10, h4);
            vec2 base = c * cellSize + vec2(0.45 + h2.x * 3.10,
                                            2.70 + h2.y * 4.20);
            float height = 2.25 + h3 * 5.75;
            float lean = (floor(hash12(c + vec2(43.0, 7.0)) * 5.0) - 2.0) * 0.58;
            float blade = materialBladeStroke(pixelCoord, base, height, lean, 0.48) * bladeEnabled;
            float tone = hash12(c + vec2(151.0, 29.0 + float(face) * 3.0));

            masks.x = max(masks.x, blade * step(0.67, tone));                         // light blades
            masks.y = max(masks.y, blade * step(0.34, tone) * (1.0 - step(0.67, tone))); // mid blades
            masks.z = max(masks.z, blade * (1.0 - step(0.34, tone)));                 // dark blades
        }
    }

    vec2 largeCell = floor(pixelCoord / vec2(8.0));
    vec2 largeLocal = mod(pixelCoord, 8.0);
    float tuft = step(0.74, hash12(largeCell + vec2(float(variant) * 5.0, 83.0 + float(face))));
    float tuftStem = step(abs(largeLocal.x - (2.0 + hash12(largeCell + vec2(5.0, 11.0)) * 4.0)), 0.50) *
                     step(2.0, largeLocal.y) * step(largeLocal.y, 7.0);
    masks.y = max(masks.y, tuft * tuftStem * 0.85);
    masks.z = max(masks.z, tuft * tuftStem * step(0.55, hash12(largeCell + vec2(37.0, 17.0))) * 0.55);

    return masks;
}

vec3 materialMudSpotField(vec2 pixelCoord,
                          float gridSize,
                          vec2 salt,
                          float density,
                          float radiusScale) {
    vec2 cell = floor(pixelCoord / gridSize);
    vec3 masks = vec3(0.0);

    for (int gy = -1; gy <= 1; ++gy) {
        for (int gx = -1; gx <= 1; ++gx) {
            vec2 c = cell + vec2(float(gx), float(gy));
            float chance = hash12(c + salt);
            float spotEnabled = step(chance, density);
            vec2 h2 = materialHash22(c + salt + vec2(31.0, 7.0));
            vec2 center = (c + vec2(0.18) + h2 * 0.64) * gridSize;
            vec2 radius = vec2(1.6 + hash12(c + salt + vec2(5.0, 71.0)) * 3.6,
                               1.4 + hash12(c + salt + vec2(83.0, 13.0)) * 3.1) * radiusScale;
            vec2 d = (pixelCoord - center) / max(radius, vec2(0.001));
            float dist2 = dot(d, d);
            float blob = spotEnabled * (1.0 - smoothstep(0.62, 1.08, dist2));
            float core = spotEnabled * (1.0 - smoothstep(0.18, 0.58, dist2));
            float rim = spotEnabled * smoothstep(0.38, 0.62, dist2) * (1.0 - smoothstep(0.72, 1.12, dist2));
            float pore = blob * step(0.70, hash12(floor(pixelCoord * 0.72) + c + salt));

            masks.x = max(masks.x, blob);
            masks.y = max(masks.y, rim);
            masks.z = max(masks.z, max(core * 0.75, pore));
        }
    }

    return masks;
}

uint materialDefaultEdge(uint type) {
    if (type == 0u) return 1u; // leafy grass
    if (type == 1u) return 2u; // sloppy mud
    return 3u;                 // dry dirt/sand/drysand
}

uint packVoxelMaterial(uint type, uint variant, uint edge, uint resLog2) {
    return MATERIAL_PACKED_BIT |
           MATERIAL_LAYOUT_V2_BIT |
           (type & 0x7u) |
           ((variant & 0x7u) << 3u) |
           ((edge & 0x3u) << 6u) |
           ((resLog2 & 0xFu) << 8u);
}

void unpackVoxelMaterial(uint material,
                         out uint type,
                         out uint variant,
                         out uint edge,
                         out uint resLog2) {
    if ((material & MATERIAL_LAYOUT_V2_BIT) != 0u) {
        type = material & 0x7u;
        variant = (material >> 3u) & 0x7u;
        edge = (material >> 6u) & 0x3u;
        resLog2 = clamp((material >> 8u) & 0xFu, 1u, 10u);
    } else {
        // Backward-compatible read for already-baked 4-type materials.
        // Old type 2 is intentionally interpreted as DrySand after the split.
        type = material & 0x3u;
        variant = (material >> 2u) & 0x7u;
        edge = (material >> 5u) & 0x3u;
        resLog2 = clamp((material >> 7u) & 0xFu, 1u, 10u);
    }
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
    const uint MATERIAL_LIVE_STAMP_CAPACITY = 64u;
    const uint MATERIAL_LIVE_STAMP_CELL_STRIDE = 3u;
    const uint MATERIAL_LIVE_STAMP_CELL_CAPACITY =
        MATERIAL_LIVE_STAMP_CAPACITY * MATERIAL_LIVE_STAMP_CELL_STRIDE;

    uint cleanFallbackMaterial = fallbackMaterial & ~MATERIAL_OVERLAY_CHUNK_HINT_BIT;

    uint baselineMaterial = cleanFallbackMaterial;
    if ((baselineMaterial & MATERIAL_PACKED_BIT) == 0u) {
        uint type = (face == 3u) ? 0u : 4u; // top grass, side/bottom minecraftish dirt

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
        baselineMaterial = packVoxelMaterial(type, variant, edge, resLog2);
    }

    // Instant texture-brush path:
    // Binding 10 still has the old layout, but cells[] may begin with a tiny
    // fixed live-stamp prefix. The final material-bake path normally returns
    // zero live stamps, but this path stays layout-correct for diagnostics.
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
            bool boxStamp = (c1.face & 0xFFu) != 0u;
            bool inside = false;
            float edgeDistance = 9999.0;
            if (!boxStamp) {
                float dist = length(d);
                inside = dist <= radius;
                edgeDistance = radius - dist;
            } else {
                float boxDist = max(max(abs(d.x), abs(d.y)), abs(d.z));
                inside = boxDist <= radius;
                edgeDistance = radius - boxDist;
            }

            if (!inside) {
                continue;
            }

            uint type = c2.face & 0x7u;
            uint variantSeed = c2.material & 0x7u;
            uint variant = (variantSeed + (hashMaterialOverlayKey(voxel, queryFace) & 0x7u)) & 0x7u;
            uint edge = (edgeDistance <= 2.25) ? materialDefaultEdge(type) : 0u;
            uint resLog2 = 4u;
            return packVoxelMaterial(type, variant, edge, resLog2);
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
    if ((material & MATERIAL_PACKED_BIT) == 0u) {
        return fallbackColor;
    }

    uint type;
    uint variant;
    uint edge;
    uint resLog2;
    unpackVoxelMaterial(material, type, variant, edge, resLog2);
    float res = float(1u << resLog2);

    vec2 faceCoord = faceCell(worldPos * 4.0, face);
    vec2 materialPx = floor(faceCoord * res + vec2(0.0005));
    vec2 materialFaceCenter = (materialPx + vec2(0.5)) / res;
    vec2 voxelCoord = floor(materialFaceCenter);
    vec2 naturalPx = materialNaturalCoord(materialPx, voxelCoord, type, variant);
    vec2 seed = materialPx
              + vec2(float(variant) * 19.0 + float(type) * 113.0,
                     float(face) * 29.0);

    float n0 = hash12(seed);
    float n1 = hash12(seed + vec2(13.7, 91.3));
    float n2 = hash12(seed * vec2(0.73, 1.19) + vec2(47.3, 11.5));
    float n3 = hash12(seed + vec2(211.0, 53.0));

    vec3 base = materialBase(type);
    vec3 hi = materialHi(type);
    vec3 lo = materialLo(type);
    vec3 accent = materialAccent(type);
    vec3 color = base;

    if (type == 0u) {
        // Grass: procedural pixel-art blade clusters. No sine/line warp, so it
        // avoids the old wavy contours and reads as little flat grass strokes.
        vec3 blades = materialGrassBladeMasks(materialPx, variant, face);
        float lowPatch = materialValueNoise(materialPx * 0.095 + vec2(float(variant) * 3.0, 17.0));
        float tuftPatch = materialValueNoise(materialPx * 0.210 + vec2(41.0, float(face) * 5.0));
        float cutPixel = step(0.975, hash12(seed + vec2(9.0, 71.0)));

        color = mix(color, lo, smoothstep(0.18, 0.72, 1.0 - lowPatch) * 0.13);
        color = mix(color, accent, smoothstep(0.42, 0.88, tuftPatch) * 0.08);
        color = mix(color, hi, blades.x * 0.42);
        color = mix(color, accent, blades.y * 0.32);
        color = mix(color, lo, blades.z * 0.48);
        color = mix(color, hi, cutPixel * blades.x * 0.06);
    } else if (type == 1u) {
        // Mud: many wet spots with internal pores/rims instead of long wavy
        // ripples. Spots overlap at two scales for denser muddy variety.
        vec3 bigSpots = materialMudSpotField(materialPx,
                                             11.0,
                                             vec2(float(variant) * 9.0, 17.0 + float(face) * 3.0),
                                             0.78,
                                             1.10);
        vec3 smallSpots = materialMudSpotField(materialPx + vec2(3.0, 5.0),
                                               6.0,
                                               vec2(73.0 + float(face) * 5.0, float(variant) * 13.0),
                                               0.64,
                                               0.82);
        float clayFine = materialValueNoise(materialPx * 0.330 + vec2(73.0, float(variant) * 5.0));
        float clayPlate = materialValueNoise(materialPx * 0.115 + vec2(29.0, float(face) * 6.0));
        float spot = max(bigSpots.x, smallSpots.x * 0.82);
        float rim = max(bigSpots.y, smallSpots.y * 0.75);
        float core = max(bigSpots.z, smallSpots.z);
        float poreDark = spot * step(0.875, hash12(floor(materialPx * 0.90) + vec2(float(variant) * 5.0, 113.0)));
        float wetHighlight = rim * step(0.54, hash12(floor(materialPx * 0.50) + vec2(37.0, float(face) * 7.0)));
        float clayFleck = step(0.935, hash12(seed + vec2(13.0, 109.0))) * smoothstep(0.30, 0.90, clayFine);
        float softCrease = materialCellCrackMask(materialPx + vec2(17.0, 5.0),
                                                 0.070,
                                                 0.030,
                                                 0.145,
                                                 float(variant) * 5.0 + float(face));
        softCrease *= (1.0 - spot * 0.55) * smoothstep(0.36, 0.88, 1.0 - clayPlate);

        color = mix(color, hi, smoothstep(0.52, 0.90, clayFine) * 0.11);
        color = mix(color, accent, smoothstep(0.58, 0.90, clayPlate) * (1.0 - spot) * 0.12);
        color = mix(color, lo, spot * 0.30);
        color = mix(color, vec3(0.060, 0.043, 0.035), core * 0.24);
        color = mix(color, lo * 0.76, softCrease * 0.40);
        color = mix(color, vec3(0.050, 0.037, 0.032), poreDark * 0.32);
        color = mix(color, accent, rim * 0.16);
        color = mix(color, hi, wetHighlight * 0.075);
        color = mix(color, hi, clayFleck * 0.045);
    } else if (type == 2u) {
        // Dry sand: brittle clay-sand crust with varied plate sizes. Cracks are
        // present but not mechanically identical triangles.
        float plateTone = materialValueNoise(naturalPx * 0.050 + vec2(23.0, float(variant) * 3.0));
        float chalk = materialValueNoise(naturalPx * 0.155 + vec2(float(face) * 5.0, 71.0));
        float crackLarge = materialDryCrackMask(naturalPx * 0.62 + vec2(17.0, float(variant) * 4.0),
                                                float(variant) * 9.0 + float(face));
        float crackMid = materialDryCrackMask(naturalPx * 1.10 + vec2(43.0, 9.0),
                                              float(variant) * 5.0 + 31.0);
        float branch = materialBranchCrack(naturalPx * 0.82 + vec2(7.0, 13.0), variant, face);
        float largeWeight = smoothstep(0.20, 0.88, plateTone);
        float cracks = clamp(crackLarge * (0.42 + largeWeight * 0.32) +
                             crackMid * (0.18 + (1.0 - largeWeight) * 0.24) +
                             branch * 0.30,
                             0.0,
                             1.0);
        color += vec3(materialSignedNoise(seed + vec2(5.0, 19.0)) * 0.045);
        color = mix(color, hi, smoothstep(0.54, 0.90, chalk) * 0.16);
        color = mix(color, accent, smoothstep(0.62, 0.95, plateTone) * 0.10);
        color = mix(color, lo * 0.66, cracks * 0.78);
    } else if (type == 3u) {
        // Sand: simple warm wind bands with low grit. It should read plain and
        // soft so placed pebbles/grass later have room to speak.
        float flow = materialValueNoise(naturalPx * 0.052 + vec2(float(variant) * 4.0, 19.0));
        vec2 sandPx = materialRotate(naturalPx, (flow - 0.5) * 0.52 + float(variant) * 0.045);
        sandPx.x += sin(sandPx.y * 0.070 + flow * 6.2831) * (1.4 + flow * 1.6);
        float dune = 0.5 + 0.5 * sin(sandPx.x * 0.235 + flow * 2.0);
        float softBand = smoothstep(0.54, 0.88, dune);
        float lee = smoothstep(0.64, 0.96, 1.0 - dune);
        color += vec3(materialSignedNoise(seed + vec2(17.0, 43.0)) * 0.035);
        color = mix(color, hi, softBand * 0.15);
        color = mix(color, lo, lee * 0.10);
        color = mix(color, accent, smoothstep(0.92, 0.995, n3) * 0.08);
    } else {
        // Dirt: the dry/cracked sibling of mud, using the exact mud palette.
        // Big readable fissures carry the identity; speckles stay secondary.
        float plate = materialValueNoise(naturalPx * 0.050 + vec2(float(variant) * 5.0, 23.0));
        float clay = materialValueNoise(naturalPx * 0.145 + vec2(37.0, float(face) * 7.0));
        float dust = materialValueNoise(naturalPx * 0.300 + vec2(83.0, float(variant) * 3.0));
        vec2 dirtPx = materialRotate(naturalPx, (plate - 0.5) * 0.40 + float(variant) * 0.035);
        vec3 dirtCrackColor = vec3(0.125, 0.088, 0.064);

        float crackLarge = materialCellCrackMask(dirtPx + vec2(17.0, float(variant) * 4.0),
                                                 0.070,
                                                 0.040,
                                                 0.220,
                                                 float(variant) * 9.0 + float(face));
        float crackMid = materialCellCrackMask(dirtPx * 1.28 + vec2(43.0, 9.0),
                                               0.095,
                                               0.032,
                                               0.170,
                                               float(variant) * 5.0 + 31.0);
        float crackJagged = materialCellCrackMask(dirtPx * vec2(0.74, 1.36) + vec2(91.0, 37.0),
                                                  0.120,
                                                  0.024,
                                                  0.135,
                                                  float(face) * 7.0 + float(variant) * 3.0);
        float branchA = smoothstep(0.80, 0.992,
            materialBrokenLine(dirtPx + vec2(3.0, 11.0),
                               15.0 + clay * 8.0,
                               0.26 + plate * 0.20,
                               float(variant) * 7.0,
                               vec2(73.0, 19.0 + float(face) * 11.0)));
        float branchB = smoothstep(0.84, 0.996,
            materialBrokenLine(dirtPx.yx + vec2(7.0, 13.0),
                               24.0 + plate * 9.0,
                               -0.18,
                               float(face) * 5.0,
                               vec2(31.0, float(variant) * 13.0 + 97.0)));
        float branchC = smoothstep(0.82, 0.997,
            materialBrokenLine(dirtPx + vec2(19.0, 4.0),
                               11.0 + dust * 6.0,
                               -0.35 + plate * 0.16,
                               float(variant) * 3.0 + float(face),
                               vec2(149.0, 53.0)));
        float largeWeight = smoothstep(0.22, 0.86, plate);
        float cracks = clamp(crackLarge * (0.56 + largeWeight * 0.34) +
                             crackMid * (0.30 + (1.0 - largeWeight) * 0.22) +
                             crackJagged * 0.24 +
                             max(max(branchA * 0.50, branchB * 0.38), branchC * 0.30),
                             0.0,
                             1.0);
        float crumb = step(0.925, hash12(seed + vec2(17.0, 113.0))) *
                      smoothstep(0.34, 0.92, dust) * (1.0 - cracks * 0.72);

        color += vec3(materialSignedNoise(seed + vec2(73.0, 29.0)) * 0.014);
        color = mix(color, hi, smoothstep(0.52, 0.90, dust) * 0.11);
        color = mix(color, accent, smoothstep(0.56, 0.92, plate) * 0.14);
        color = mix(color, materialBase(1u) * 0.78, smoothstep(0.18, 0.72, 1.0 - clay) * 0.16);
        color = mix(color, hi, crumb * 0.05);
        color = mix(color, dirtCrackColor, cracks * 0.82);
    }

    if (edge != 0u) {
        vec2 transitionPx = naturalPx +
            vec2(float(type) * 13.0 + float(edge) * 19.0,
                 float(variant) * 17.0 + float(face) * 7.0);
        float broad = materialValueNoise(
            transitionPx * 0.075 + vec2(float(variant) * 2.7, float(face) * 3.1));
        float mid = materialValueNoise(
            transitionPx * 0.18 + vec2(float(edge) * 11.0, float(type) * 7.0));
        float edgeNoise = hash12(
            materialPx + vec2(211.0, 53.0) +
            vec2(float(edge) * 17.0, float(type) * 29.0));
        float island = smoothstep(0.54, 0.86, broad + (mid - 0.5) * 0.24);
        float broken = materialBrokenLine(transitionPx + vec2(broad * 9.0, mid * 7.0),
                                          7.0 + broad * 8.0,
                                          -0.32 + mid * 0.64,
                                          float(variant) * 7.0 + float(face) * 3.0,
                                          vec2(float(type) * 31.0 + 5.0,
                                               float(edge) * 47.0 + float(face) * 3.0));
        float strands = smoothstep(0.54, 0.98, broken) * smoothstep(0.24, 0.78, mid);
        float flecks = step(0.955, edgeNoise) * (0.45 + 0.55 * broad);
        float transition = clamp(max(island * 0.56, strands * 0.50) +
                                 flecks * 0.18,
                                 0.0,
                                 1.0);
        transition *= 0.62 + 0.20 * n1;

        if (edge == 1u) {          // leafy grass transition
            float leafSpike = smoothstep(0.40, 0.94,
                materialBrokenLine(naturalPx + vec2(edgeNoise * 3.0, 0.0),
                                   4.0 + edgeNoise * 2.5,
                                   0.45,
                                   float(variant) * 7.0,
                                   vec2(5.0, 89.0 + float(face) * 7.0)));
            vec3 leafColor = mix(materialLo(0u), materialHi(0u), 0.65 + 0.25 * n0);
            float leafMask = clamp(transition * (0.45 + 0.55 * leafSpike) +
                                   flecks * 0.10,
                                   0.0,
                                   1.0);
            color = mix(color, materialLo(0u), leafMask * step(0.78, edgeNoise) * 0.18);
            color = mix(color, leafColor, leafMask * 0.46);
        } else if (edge == 2u) {   // sloppy/liquid mud transition
            float smear = smoothstep(0.36, 0.96,
                materialBrokenLine(naturalPx.yx,
                                   6.0 + edgeNoise * 4.0,
                                   -0.16,
                                   float(variant) * 9.0,
                                   vec2(41.0 + float(face) * 5.0, 2.0)));
            float mudMask = clamp(transition * (0.48 + 0.32 * smear), 0.0, 1.0);
            color = mix(color, materialLo(1u), mudMask * (0.34 + 0.28 * n1));
            color = mix(color, materialAccent(1u), mudMask * smear * 0.12);
            color = mix(color, vec3(0.070, 0.050, 0.042),
                        mudMask * step(0.92, n2) * 0.28);
        } else {                   // dry crumble / cracked sand-dirt transition
            float crumb = smoothstep(0.28, 0.82, mid + edgeNoise * 0.25);
            float crack = materialDryCrackMask(naturalPx + vec2(float(variant) * 3.0), float(face) + 5.0);
            vec3 dryHi = (type == 3u) ? materialHi(3u) : materialHi(2u);
            vec3 dryLo = (type == 4u) ? materialLo(4u) : materialLo(2u);
            float dryMask = clamp(transition * (0.52 + 0.30 * crumb), 0.0, 1.0);
            color = mix(color, dryHi, dryMask * crumb * 0.16);
            color = mix(color, dryLo, dryMask * step(0.74, n1) * 0.24);
            color = mix(color, materialLo(2u) * 0.64, dryMask * crack * 0.28);
        }
    }

    return clamp(color, vec3(0.02), vec3(1.0));
}

````

## shaders\common\dither_utils.glsl

Description: No CC-DESC found.

````glsl
// ═══════════════════════════════════════════════════════════════════════════
// dither_utils.glsl — Shared dither, hash, noise, quantization, and
//                     geometry helpers for the retro voxel rendering pipeline.
// ═══════════════════════════════════════════════════════════════════════════
#ifndef DITHER_UTILS_GLSL
#define DITHER_UTILS_GLSL

// Bayer 8x8 dither matrix for detailed atmospheric scattering
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
    int index = y * 8 + x;
    
    return bayerMatrix[index] / 64.0;
}

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Retro quantized lighting - converts smooth lighting to discrete bands
// This creates a classic pixel art / PSX look
float quantizeLight(float value, int levels) {
    // Clamp to valid range
    value = clamp(value, 0.0, 1.0);
    
    // Quantize to discrete levels
    float step = 1.0 / float(levels - 1);
    return floor(value / step + 0.5) * step;
}

// SEGA-STYLE 2x2 Dither pattern - chunky, low-res look
// Classic 2x2 checkerboard pattern like Genesis/Saturn era games
float segaDither2x2(vec2 screenPos) {
    // Simple 2x2 checkerboard - just 2 values (0 or 1)
    int x = int(mod(screenPos.x, 2.0));
    int y = int(mod(screenPos.y, 2.0));
    // Checkerboard: alternating 0 and 1
    return float((x + y) % 2);
}

// 4x4 Bayer matrix dither for more granular AO transitions
// Returns value 0-15 normalized to 0-1
float bayerDither4x4(vec2 screenPos) {
    int x = int(mod(screenPos.x, 4.0));
    int y = int(mod(screenPos.y, 4.0));
    
    // Classic Bayer 4x4 matrix
    const int bayerMatrix[16] = int[](
         0,  8,  2, 10,
        12,  4, 14,  6,
         3, 11,  1,  9,
        15,  7, 13,  5
    );
    
    return float(bayerMatrix[y * 4 + x]) / 16.0;
}

// 16x16 ordered dither — true 16×16 Bayer matrix (256 thresholds).
// Reverse-bit construction: finest spatial level (2×2) gets highest
// bit-weight so adjacent cells differ by up to 75%, eliminating the
// 8×8 macro-block and row-banding artifacts of the standard recursion.
// Period = 16 cells = exactly one 0.25m voxel face, 1 cell per pixel.
float bayerDither16x16(vec2 pos) {
    uint x = uint(mod(pos.x, 16.0));
    uint y = uint(mod(pos.y, 16.0));
    const uint b2[4] = uint[](0u, 2u, 3u, 1u);
    uint value = b2[(y & 1u) * 2u + (x & 1u)] * 64u
              + b2[((y >> 1u) & 1u) * 2u + ((x >> 1u) & 1u)] * 16u
              + b2[((y >> 2u) & 1u) * 2u + ((x >> 2u) & 1u)] * 4u
              + b2[((y >> 3u) & 1u) * 2u + ((x >> 3u) & 1u)];
    return float(value) / 256.0;
}

// 64x64 ordered-dither Bayer matrix — 6-bit recursive construction.
// Produces values in [0, 4095/4096], tiling every 64 world cells.
// Used for the sun-shadow backface dither on vertical faces;
// finer than 16x16 so the stipple grain is a quarter the visual size.
float bayerDither64x64(vec2 pos) {
    uint x = uint(mod(pos.x, 64.0));
    uint y = uint(mod(pos.y, 64.0));
    const uint b2[4] = uint[](0u, 2u, 3u, 1u);
    uint value = b2[(y & 1u) * 2u + (x & 1u)] * 1024u
              + b2[((y >> 1u) & 1u) * 2u + ((x >> 1u) & 1u)] * 256u
              + b2[((y >> 2u) & 1u) * 2u + ((x >> 2u) & 1u)] * 64u
              + b2[((y >> 3u) & 1u) * 2u + ((x >> 3u) & 1u)] * 16u
              + b2[((y >> 4u) & 1u) * 2u + ((x >> 4u) & 1u)] * 4u
              + b2[((y >> 5u) & 1u) * 2u + ((x >> 5u) & 1u)];
    return float(value) / 4096.0;
}

// Pixelation helper - snaps screen coords to virtual pixel grid
vec2 pixelateCoords(vec2 screenPos, float pixelSize) {
    return floor(screenPos / pixelSize) * pixelSize;
}

// Grid snap to cell centers with a configurable pre-snap cell offset.
// offsetCells=0.5 shifts the phase by half a cell before snapping.
vec3 snapGridCenterOffset(vec3 pos, float gridSize, float offsetCells) {
    float g = max(gridSize, 0.000001);
    // Phase offset on Y only – keeps horizontal (XZ) grid aligned with voxel edges
    vec3 off = vec3(0.0, g * offsetCells, 0.0);
    return (floor((pos + off) / g + 0.01) + 0.5) * g;
}

// Deterministic stepped band transition for pixel-art lighting.
// Uses lo/mid/hi levels near boundaries instead of dithering.
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

// Animated world-grid noise cell (matches light/shadow snap grid).
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

// Per-cell held random noise with smooth interpolation between holds.
// Hold speed varies per pixel cell to reduce repetitive flicker.
float bandBlendHeldNoiseCell(vec2 cellCoord,
                             float time,
                             float minSpeed,
                             float maxSpeed) {
    vec2 cellBase = floor(cellCoord + 0.01);
    float holdSpeed = mix(minSpeed, maxSpeed,
                          hash12(cellBase + vec2(29.1, 83.7)));
    float phase = hash12(cellBase + vec2(11.7, 57.9)) * 24.0;
    float t = time * max(holdSpeed, 0.001) + phase;

    float frame = floor(t);
    float blend = fract(t);
    blend = blend * blend * (3.0 - 2.0 * blend);

    float a0 = hash12(cellBase + vec2(frame * 5.73, frame * 2.11) + vec2(13.0, 47.0));
    float a1 = hash12(cellBase + vec2((frame + 1.0) * 5.73, (frame + 1.0) * 2.11) + vec2(13.0, 47.0));
    float b0 = hash12(cellBase * 0.71 + vec2(frame * 1.91, frame * 4.37) + vec2(61.0, 23.0));
    float b1 = hash12(cellBase * 0.71 + vec2((frame + 1.0) * 1.91, (frame + 1.0) * 4.37) + vec2(61.0, 23.0));

    float heldA = mix(a0, a1, blend);
    float heldB = mix(b0, b1, blend);
    return clamp(heldA * 0.68 + heldB * 0.32, 0.0, 1.0);
}

// Face-aware 2D cell coordinate from 3D pixel position.
// Picks the two axes that actually vary across the face, so vertical
// surfaces don't collapse to identical hash seeds along columns.
//   face 0,1 (±X) → YZ
//   face 2,3 (±Y) → XZ
//   face 4,5 (±Z) → XY
vec2 faceCell(vec3 pixPos, uint face) {
    if (face >= 6u) return pixPos.xz;  // DCCM terrain top-surface
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

#endif // DITHER_UTILS_GLSL

````

## shaders\common\shadow_sampling.glsl

Description: No CC-DESC found.

````glsl
// ═══════════════════════════════════════════════════════════════════════════
// shadow_sampling.glsl — Shadow resolve, sun shadow, point shadow PCF,
//                        retro dithered point shadow with grid-sampled edges.
//
// Requires: dither_utils.glsl (bayerDither8x8, hash12, bandBlendHeldNoiseCell,
//           bandBlendNoiseCell, snapGridCenterOffset, faceCell, buildFaceTangents)
// Requires: UBO/SSBO bindings for shadow, lighting, sunShadowMap, pointShadowMaps
// ═══════════════════════════════════════════════════════════════════════════
#ifndef SHADOW_SAMPLING_GLSL
#define SHADOW_SAMPLING_GLSL

float resolveBinaryShadowFromNeighborhood5(float v0,
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

vec3 snapShadowLookupPos(vec3 worldPos) {
    return snapGridCenterOffset(worldPos, LIGHT_GRID_CELL_SIZE, LIGHT_GRID_PHASE_OFFSET_CELLS);
}

uint getSunCascadeCount() {
    return uint(clamp(shadow.shadowConfig2.x + 0.5, 1.0, 6.0));
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

float sampleSunShadowCascade(vec3 sampleWorldPos, uint cascadeIndex, bool snapUv) {
    vec2 uv;
    float depth;
    if (!sunCascadeSampleParams(sampleWorldPos, cascadeIndex, uv, depth)) {
        return 1.0;
    }

    // Per-cascade snap gate: snapping UV to texel center defeats hardware
    // PCF and exposes the cascade's world-space texel directly on the
    // receiver. That's intentional for the HD ring (texel ≈ 0.0156 m ≤
    // light-grid cell) where it produces crisp pixel-art shadow edges.
    // For far cascades at high cascadeScale the texel grows to >1 m —
    // snapping there causes the chaotic outlines and triangle staircase
    // artifacts. Fall back to natural PCF on those cascades for a clean
    // smoothed silhouette that doesn't change shape with cascade count
    // or scale.
    float cascadeTexelMeters = shadow.sunCascadeParams[cascadeIndex].y;
    bool fineEnoughToSnap = cascadeTexelMeters <= LIGHT_GRID_CELL_SIZE * 1.5;
    if (snapUv && fineEnoughToSnap) {
        float mapSize = max(shadow.shadowConfig.z, 1.0);
        uv = (floor(uv * mapSize) + 0.5) / mapSize;
    }

    return texture(sunShadowMap, vec4(uv, float(cascadeIndex), depth));
}

float sampleSunShadowAt(vec3 sampleWorldPos) {
    float horizontalDistance = length(sampleWorldPos.xz - camera.cameraPos.xz);
    uint cascade = chooseSunCascadeForSample(sampleWorldPos, horizontalDistance);
    float vis = sampleSunShadowCascade(sampleWorldPos, cascade, true);

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
                float farVis = sampleSunShadowCascade(sampleWorldPos, cascade + 1u, true);
                vis = mix(vis, farVis, t);
            }
        }
    }
    return vis;
}

float sampleSunShadowAtNoSnap(vec3 sampleWorldPos) {
    float horizontalDistance = length(sampleWorldPos.xz - camera.cameraPos.xz);
    uint cascade = chooseSunCascadeForSample(sampleWorldPos, horizontalDistance);
    float vis = sampleSunShadowCascade(sampleWorldPos, cascade, false);

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
                float farVis = sampleSunShadowCascade(sampleWorldPos, cascade + 1u, false);
                vis = mix(vis, farVis, t);
            }
        }
    }
    return vis;
}

// Face-aware cell-center snap for pixel-art shadow edges.
// Vertical faces (±X, ±Z): snap only the horizontal in-plane axis.
//   Y is left continuous so the backface fade dither can snap it
//   independently at the finer dither-cell resolution.  The shadow
//   lookup for vertical faces uses sampleSunShadowAtNoSnap() which
//   does not re-snap UV, so Y remaining un-snapped here is safe.
// Horizontal faces (±Y): snap both X and Z (neither affects shadow depth).
vec3 snapToFaceCell(vec3 pos, uint face) {
    float g = LIGHT_GRID_CELL_SIZE;
    vec3 s = pos;
    if (face <= 1u) {       // ±X face: snap Z only (Y changes shadow depth)
        s.z = (floor(pos.z / g + 0.01) + 0.5) * g;
    } else if (face <= 3u) { // ±Y face: snap X, Z
        s.x = (floor(pos.x / g + 0.01) + 0.5) * g;
        s.z = (floor(pos.z / g + 0.01) + 0.5) * g;
    } else {                // ±Z face: snap X only (Y changes shadow depth)
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

    // ── Smooth backface terminator fade ───────────────────────────────
    // No dither: the shadow strength fades continuously as the sun-vs-face
    // angle changes.  A face becomes fully shadowed at ndotl ≤ kFadeStart
    // (sun behind the face by ≥ ~5°) and fully lit at ndotl ≥ kFadeEnd
    // (sun in front by ≥ ~9°).  The width is wide enough to hide caster-
    // boundary aliasing on shear-projected vertical faces — instead of
    // appearing as a hard horizontal line, the shadow edge follows the
    // ndotl gradient and smoothly resolves as the sun moves in azimuth.
    const float kFadeStart = -0.08;
    const float kFadeEnd   =  0.16;
    float backfaceFade = smoothstep(kFadeStart, kFadeEnd, ndotl);
    if (backfaceFade <= 0.0) return 0.0;

    // Derive dominant face from normal (used for cell snap).
    uint derivedFace;
    vec3 an = abs(normalN);
    if (an.x >= an.y && an.x >= an.z) derivedFace = (normalN.x > 0.0) ? 0u : 1u;
    else if (an.y >= an.x && an.y >= an.z) derivedFace = (normalN.y > 0.0) ? 2u : 3u;
    else derivedFace = (normalN.z > 0.0) ? 4u : 5u;

    bool isVerticalFace = (derivedFace <= 1u || derivedFace >= 4u);

    // ── Vertical-face cast-shadow fade ──────────────────────────────
    // When the sun is nearly tangential to a vertical face (sun azimuth
    // sweeps across the face's perpendicular axis) the shear projection
    // becomes ill-conditioned and small casters render glitched bands.
    // Mirrors the side-face self-shadow fade: as |sunDir · faceNormal|
    // approaches zero, the cast shadow on this face fades out smoothly.
    // Horizontal faces are unaffected (sunDir.y always dominates).
    float castFade = 1.0;
    if (isVerticalFace) {
        float sunFaceCos = abs(dot(sunDir, normalN));
        // Fully fade out when the sun grazes within ~9° of the face plane,
        // fully present beyond ~17°. Eliminates the per-azimuth glitch
        // band without darkening normal incidence.
        castFade = smoothstep(0.05, 0.16, sunFaceCos);
    }

    // Push receiver along the SURFACE NORMAL toward the light, not along
    // -sunDir. The sun-direction push at low elevation is almost entirely
    // horizontal — it walks the sample point into adjacent geometry
    // ("shadow under wall" at the base of vertical objects) and shifts
    // the silhouette with azimuth ("shadow detaches" / "shape changes").
    // Normal-direction push is purely along the receiver face, so the
    // sample stays anchored to the same contact point regardless of
    // sun direction — silhouettes are stable as the sun rotates.
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
    // Slope factor 1/max(ndotl, 0.1) bounded; only used to grow the
    // normal push at grazing angles where one shadow texel covers more
    // depth difference along the surface tangent.
    float slope = clamp(1.0 / max(ndotl, 0.1), 1.0, 4.0);
    float pushAlongNormal = max(g, biasTexel * 0.85) * slope;

    if (isVerticalFace) {
        // Vertical faces: snap the in-plane horizontal axis AND Y to the
        // cell grid. Y must be snapped because the shadow UV is
        //   (worldX + Y*shearX, worldZ + Y*shearZ)
        // i.e. Y appears in BOTH UV components via the shear projection.
        // Leaving Y continuous lets fragments inside a single wall cell
        // sweep ~g*|shear| texels of shadow space, so the cell fills only
        // partially and the silhouette goes jagged. Snapping Y collapses
        // the whole wall-cell to one (u,v) lookup -> one binary shadow
        // result per cell, matching top-face behaviour. No UV snap inside
        // the texture lookup (we already snapped in world space).
        float gCell = LIGHT_GRID_CELL_SIZE;
        vec3 snapped = snapToFaceCell(worldPos, derivedFace);
        snapped.y = (floor(worldPos.y / gCell + 0.01) + 0.5) * gCell;
        vec3 pushed  = snapped + normalN * pushAlongNormal;
        return sampleSunShadowAtNoSnap(pushed) * backfaceFade * castFade;
    }

    // Horizontal faces: NO world-cell snap (would cause moiré with the
    // sheared shadow texel grid). Push purely along normal so silhouettes
    // don't shift with sun azimuth.
    vec3 samplePos = worldPos + normalN * pushAlongNormal;
    return sampleSunShadowAt(samplePos) * backfaceFade;
}

float sampleSunShadow(vec3 worldPos) {
    return sampleSunShadow(worldPos, vec3(0.0, 1.0, 0.0));
}

// ── Sun shadow debug visualization ──────────────────────────────────
// diagConfig.y selects the debug mode:
//   1 = shadow UV (snapped texel coordinates as RG)
//   2 = binary shadow value (0=dark, 1=lit)
//   3 = texel grid lines (white = texel boundary)
//   4 = depth value visualized
//   5 = cascade ID (per-cascade tint on terrain)
// Returns vec4(0) when debug is off.

// Fixed per-cascade tint palette. MUST match the C++ side palette in
// DirectionalShadowWindow::renderCascadeOverlay so the in-world wireframe
// box and the on-terrain tint use the SAME color for the same cascade.
vec3 cascadeDebugColor(uint cascadeIndex) {
    if (cascadeIndex == 0u) return vec3(1.00, 0.25, 0.25); // red
    if (cascadeIndex == 1u) return vec3(1.00, 0.65, 0.20); // orange
    if (cascadeIndex == 2u) return vec3(1.00, 1.00, 0.30); // yellow
    if (cascadeIndex == 3u) return vec3(0.30, 1.00, 0.40); // green
    if (cascadeIndex == 4u) return vec3(0.30, 0.85, 1.00); // cyan
    return vec3(1.00, 0.40, 1.00);                          // magenta (>=5)
}

vec4 debugSunShadow(vec3 worldPos, vec3 worldNormal) {
    float debugMode = shadow.diagConfig.y;
    if (debugMode < 0.5) return vec4(0.0);

    vec3 normalN = normalize(worldNormal);
    vec3 sunDir  = shadow.sunDirTexelSize.xyz;

    // Derive face the same way as sampleSunShadow.
    uint face;
    vec3 an = abs(normalN);
    if (an.x >= an.y && an.x >= an.z) face = (normalN.x > 0.0) ? 0u : 1u;
    else if (an.y >= an.x && an.y >= an.z) face = (normalN.y > 0.0) ? 2u : 3u;
    else face = (normalN.z > 0.0) ? 4u : 5u;
    bool isVerticalFace = (face <= 1u || face >= 4u);

    // For UV / depth visualisation we need the EXACT same sample point as
    // gameplay's sampleSunShadow() — otherwise the texel grid view will
    // disagree with the actual shadow (different push direction +
    // missing Y-snap on vertical faces would make the visualization
    // appear shifted and cells partially filled).
    //
    // Replicate the gameplay snap: snap face-plane cell, plus snap Y on
    // vertical faces (Y appears in shadow UV via shear projection).
    float gCell = LIGHT_GRID_CELL_SIZE;
    vec3 snapped = snapToFaceCell(worldPos, face);
    if (isVerticalFace) {
        snapped.y = (floor(worldPos.y / gCell + 0.01) + 0.5) * gCell;
    }
    // Push along surface normal (matches sampleSunShadow); use a small
    // fixed cell-sized push — debug only needs to clear receiver depth.
    vec3 samplePos = snapped + normalN * gCell;

    float horizontalDistance = length(samplePos.xz - camera.cameraPos.xz);
    uint cascade = chooseSunCascade(horizontalDistance);
    vec4 clip = shadow.sunLightVP[cascade] * vec4(samplePos, 1.0);
    vec3 ndc = clip.xyz / clip.w;
    vec2 uv = ndc.xy * 0.5 + 0.5;
    float depth = ndc.z;

    int mode = int(debugMode + 0.5);

    // Mode 1: Shadow UV as color (R=U, G=V).
    if (mode == 1) {
        return vec4(fract(uv * 16.0), 0.0, 1.0);
    }

    // Mode 2: Binary shadow value — go through gameplay path so the
    // debug view is bit-identical to in-game shadow on every face.
    if (mode == 2) {
        float val = sampleSunShadow(worldPos, worldNormal);
        return vec4(vec3(val), 1.0);
    }

    // Mode 3: Face-cell grid overlay with the gameplay shadow value.
    // Calling sampleSunShadow() (not sampleSunShadowAt(samplePos)) makes
    // the per-cell fill identical to gameplay — the cells now fully fill
    // and the silhouette matches the in-game shadow exactly.
    if (mode == 3) {
        vec2 fc = faceCell(worldPos, face);
        vec2 cellPos = fc / LIGHT_GRID_CELL_SIZE;
        vec2 f = fract(cellPos);
        vec2 nearEdge = min(f, 1.0 - f);
        float grid = (nearEdge.x < 0.06 || nearEdge.y < 0.06) ? 1.0 : 0.0;
        float shadowVal = sampleSunShadow(worldPos, worldNormal);
        return vec4(mix(vec3(shadowVal * 0.4), vec3(1.0), grid), 1.0);
    }

    // Mode 4: Depth value visualized.
    if (mode == 4) {
        return vec4(fract(depth * 100.0), fract(depth * 1000.0), depth, 1.0);
    }

    // Mode 5: Cascade ID — tint the terrain by which cascade it samples.
    // Modulated by a quick sun-facing diffuse so 3D shape stays readable.
    if (mode == 5) {
        vec3 tint = cascadeDebugColor(cascade);
        float diffuse = max(dot(normalN, -sunDir), 0.0);
        float lit = mix(0.55, 1.0, diffuse);
        return vec4(tint * lit, 1.0);
    }

    return vec4(1.0, 0.0, 1.0, 1.0); // magenta = unknown mode
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

// Retro dithered shadow: sample a small cell footprint for raw coverage,
// then resolve to binary (fully lit or fully dark) per pixel-cell using
// the same Bayer + noise pattern as light band transitions.
// Shadow edges become scattered retro pixels that merge with light bands.
float samplePointShadow(vec3 worldPos, vec3 worldNormal, uint lightIndex) {
    vec3 lightPos = lighting.pointLights[lightIndex].positionRadius.xyz;
    float farPlane = shadow.pointShadowInfo[lightIndex].y;

    if (farPlane < 0.01) return 1.0;
    float mapSize = max(shadow.shadowConfig.w, 1.0);
    vec3 normalN = normalize(worldNormal);
    bool verticalFace = (abs(normalN.y) < 0.5);

    const float SHADOW_LOOKUP_GRID = LIGHT_GRID_CELL_SIZE;

    // --- Sample center + 4 cardinal neighbors for raw coverage ---
    vec3 snapped = snapShadowLookupPos(worldPos);
    float receiverPush = verticalFace
        ? SHADOW_LOOKUP_GRID * 1.55
        : SHADOW_LOOKUP_GRID * 0.22;
    float kernelRadius = verticalFace
        ? SHADOW_LOOKUP_GRID * 0.78
        : SHADOW_LOOKUP_GRID * 0.74;

    vec3 center = verticalFace
        ? snapped + normalN * receiverPush
        : snapShadowLookupPos(worldPos + normalN * receiverPush);
    if (verticalFace) {
        center.y = snapped.y + SHADOW_LOOKUP_GRID * 0.35;
    }

    vec3 t1, t2;
    buildFaceTangents(normalN, t1, t2);

    float v0 = samplePointShadowAt(center, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v1 = samplePointShadowAt(center + t1 * kernelRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v2 = samplePointShadowAt(center - t1 * kernelRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v3 = samplePointShadowAt(center + t2 * kernelRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);
    float v4 = samplePointShadowAt(center - t2 * kernelRadius, normalN, lightPos, lightIndex, farPlane, mapSize, false);

    // Majority cleanup removes one-cell strays before we quantize the edge.
    float coverage = resolveBinaryShadowFromNeighborhood5(v0, v1, v2, v3, v4);

    // Fully lit or fully shadowed: no dithering needed
    if (coverage >= 0.96) return 1.0;
    if (coverage <= 0.04) return 0.0;

    // --- Shadow edge: dither to binary using same pattern as band transitions ---
    // Derive face axis from normal for faceCell projection
    uint derivedFace;
    vec3 an = abs(normalN);
    if (an.x >= an.y && an.x >= an.z) derivedFace = (normalN.x > 0.0) ? 0u : 1u;
    else if (an.y >= an.x && an.y >= an.z) derivedFace = (normalN.y > 0.0) ? 2u : 3u;
    else derivedFace = (normalN.z > 0.0) ? 4u : 5u;

    vec3 pixWorld = snapGridCenterOffset(
        worldPos, LIGHT_GRID_CELL_SIZE, LIGHT_GRID_PHASE_OFFSET_CELLS);
    vec2 cellCoord = floor(
        faceCell(pixWorld, derivedFace) / LIGHT_GRID_CELL_SIZE + 0.01);

    // Bayer 8x8 ordered dither (same grid as band transitions)
    vec2 orderedCell = mod(mod(cellCoord, 8.0) + 8.0, 8.0);
    float ordered = bayerDither8x8(orderedCell);

    // Static pattern only: stable retro shadow pixels without time-varying
    // shimmer or stray cells popping outside the true silhouette.
    float staticNoise = hash12(cellCoord + vec2(37.7, 53.1));
    float pattern = clamp(ordered * 0.82 + staticNoise * 0.18, 0.0, 1.0);

    float edgeCoverage = smoothstep(0.18, 0.82, coverage);
    return (edgeCoverage > pattern) ? 1.0 : 0.0;
}

#endif // SHADOW_SAMPLING_GLSL

````

## shaders\common\clustered_lighting.glsl

Description: No CC-DESC found.

````glsl
// ═══════════════════════════════════════════════════════════════════════════
// clustered_lighting.glsl — Diffuse calculation and smooth point light
//                           evaluation for accumulate-then-quantize pipeline.
//
// Requires: dither_utils.glsl (quantizeBandsStepped, snapGridCenterOffset)
// Requires: UBO/SSBO bindings for lighting
// ═══════════════════════════════════════════════════════════════════════════
#ifndef CLUSTERED_LIGHTING_GLSL
#define CLUSTERED_LIGHTING_GLSL

// Simple Lambertian diffuse lighting with stepped quantization.
float calculateDiffuse(vec3 normal, vec3 lightDir, vec2 screenPos) {
    float diffuse = max(dot(normal, -lightDir), 0.0);

    // 5 levels with a discrete half-step near boundaries (no gradients/dither).
    return quantizeBandsStepped(diffuse, 4.0, 1.0, 0.0, 0.10);
}

// ═══════════════════════════════════════════════════════════════════════════
// SMOOTH POINT LIGHT — ACCUMULATE-THEN-QUANTIZE
// ═══════════════════════════════════════════════════════════════════════════
//
// Returns smooth (non-quantized) light contribution for accumulation.
// All retro post-processing (8-band quantization, dithering, warm/cool shift)
// is deferred until AFTER all lights are summed, so ring boundaries trace
// the combined light field's isosurfaces (organic blobs) instead of
// individual per-light circles.
//
// Pixel grid: 0.015625 (1/64m) — retro pixel snap.
// Returns vec4(colorContrib.rgb, brightness) for later post-quantization.
// ═══════════════════════════════════════════════════════════════════════════
vec4 calculatePointLightSmooth(vec3 worldPos, vec3 normal, vec3 baseColor, uint lightIndex, uint face) {
    vec3  lightPos       = lighting.pointLights[lightIndex].positionRadius.xyz;
    float lightRadius    = lighting.pointLights[lightIndex].positionRadius.w;
    vec3  lightColor     = lighting.pointLights[lightIndex].colorIntensity.xyz;
    float lightIntensity = lighting.pointLights[lightIndex].colorIntensity.w;

    // Per-light pulse from UBO (CPU-synced with glow billboard)
    float pulseStrength = lighting.lightPulseData[lightIndex].x;
    float breathScale   = lighting.lightPulseData[lightIndex].y;

    if (breathScale < 0.01) breathScale = 1.0;

    float effectiveRadius = max((lightRadius * 0.5) * breathScale, 0.0001);

    // EARLY EXIT: raw distance + facing before any expensive stylization work.
    vec3 rawLightVec = lightPos - worldPos;
    float rawLenSq = dot(rawLightVec, rawLightVec);
    float effectiveRadiusSq = effectiveRadius * effectiveRadius;
    if (rawLenSq <= 0.00000001 || rawLenSq > effectiveRadiusSq) return vec4(0.0);
    vec3 rawLightDir = rawLightVec * inversesqrt(rawLenSq);
    float diffuse = max(dot(normal, rawLightDir), 0.0);
    if (diffuse <= 0.0) return vec4(0.0);

    // ── Pixelate positions: 1/64m grid ──────────
    // Cell boundaries at grid points (aligned with voxel edges every 16 cells),
    // distance computed from cell centers (no half-cell visual ring shift).
    const float pixelGrid = LIGHT_GRID_CELL_SIZE;
    vec3 pixLight = snapGridCenterOffset(lightPos, pixelGrid, LIGHT_GRID_PHASE_OFFSET_CELLS);
    vec3 pixWorld = snapGridCenterOffset(worldPos, pixelGrid, LIGHT_GRID_PHASE_OFFSET_CELLS);

    // ── Height-compensated distance for even ring morphing ────────────
    // Uses XZ (horizontal) distance for ring shape so lights at different
    // elevations project uniform rings that morph/blend evenly on surfaces.
    // Vertical distance attenuates intensity separately.
    vec3  lightVec  = pixLight - pixWorld;
    float distance3D = length(lightVec);
    if (distance3D > effectiveRadius) return vec4(0.0);

    // Diffuse direction on the same snapped grid as distance so
    // band boundaries stay pixel-perfect (no sub-pixel drift on side faces).
    float lightLenSq = dot(lightVec, lightVec);
    if (lightLenSq <= 0.00000001) return vec4(0.0);
    vec3 lightDir = lightVec * inversesqrt(lightLenSq);

    // Keep band radius tied to horizontal ring distance so vertical faces
    // don't pick up spill outside the visible ground light bands.
    float distXZ = length(lightVec.xz);
    float distY  = abs(lightVec.y);
    float ringDist = distXZ + distY * 0.15;
    float dist01 = clamp(ringDist / effectiveRadius, 0.0, 1.0);
    float heightAtten = 1.0 - clamp(distY / effectiveRadius, 0.0, 0.8) * 0.4;

    float brightness = pow(1.0 - dist01, 2.5) * heightAtten;
    brightness *= (1.0 + pulseStrength * (1.0 - dist01) * 0.30);
    if (brightness <= 0.0005) return vec4(0.0);

    brightness = max(brightness, 0.0);

    brightness *= lightIntensity;

    float visibleBrightness = brightness * diffuse;
    vec3 colorContrib = baseColor * lightColor * visibleBrightness;
    return vec4(colorContrib, brightness);
}

#endif // CLUSTERED_LIGHTING_GLSL

````

## shaders\common\sky_enclosure.glsl

Description: No CC-DESC found.

````glsl
// sky_enclosure.glsl
// "Deeper = darker" geometric enclosure, INDEPENDENT of any light source.
// The sun/moon adds light back via the normal lighting path; this term only
// removes ambient sky contribution as a function of how surrounded a fragment
// is by geometry.
//
// Two complementary signals (combined via max):
//
//   (A) Sky-march column — march upward from the surface in N small steps.
//       Find the FIRST height at which the shadow map says "sky visible".
//       The distance from surface to that height = how deep we are inside
//       enclosed geometry. Converts to occlusion via 1 - exp(-d * k).
//       This signal works whether the celestial is sun, moon, or neither
//       (the shadow map keeps the most-recent geometry projection); the
//       geometry encoded there is what matters, not the active light.
//
//   (B) Y-cavity (sun-independent) — screen-space derivative term that
//       fires when neighbouring fragments climb above this one. Catches
//       open-floor depressions that the shadow march cannot reach (e.g.
//       fragment is in a small dip too shallow for the march resolution).
//
// CRITICAL probe-sampler rules (got these wrong twice):
//   - depth < 0  → probe in front of cascade near plane → LIT (clear sky)
//   - depth > 1  → past far plane → continue to next cascade
//   - uv outside → lateral miss → continue to next cascade
//   - exhausted  → assume open sky → LIT
//   NEVER "exclude" probes — that just collapses the average toward zero.
//
// Requirements (must be in scope):
//   - struct shadow with shadowConfig, shadowConfig2, sunDirTexelSize,
//     sunCascadeParams[], sunLightVP[], skyEnclosureParams
//   - sampler2DArrayShadow sunShadowMap
//   - getSunCascadeCount (from shadow_sampling.glsl)
//
// skyEnclosureParams encoding:
//   x = intensity (0..4) — exp curve steepness applied to depth ratio
//   y = minAmbient (0..1)
//   z = probeMaxHeight (m) — max sky-march distance
//   w = mode (0=off, 1=on, 2=on+visualize)
// shadowConfig2.w = cavityGain (0..1) — weight of Y-cavity term

#ifndef SKY_ENCLOSURE_GLSL_INCLUDED
#define SKY_ENCLOSURE_GLSL_INCLUDED

const int   SKY_MARCH_STEPS  = 12;
const float SKY_NORMAL_LIFT  = 0.05;

// Cascade-iterating shadow probe.
//   depth < 0 → above all map content → LIT
//   depth > 1 → past far plane → try next cascade
//   uv out    → lateral miss → try next cascade
//   exhausted → assume open sky → LIT
float sampleProbeShadow(vec3 pos) {
    uint count = getSunCascadeCount();
    for (uint i = 0u; i < count; ++i) {
        vec4 clip = shadow.sunLightVP[i] * vec4(pos, 1.0);
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = ndc.xy * 0.5 + 0.5;
        float depth = ndc.z;
        if (depth < 0.0) return 1.0;
        if (depth > 1.0) continue;
        if (uv.x < 0.0 || uv.x > 1.0 ||
            uv.y < 0.0 || uv.y > 1.0) continue;
        float mapSize = max(shadow.shadowConfig.z, 1.0);
        uv = (floor(uv * mapSize) + 0.5) / mapSize;
        return texture(sunShadowMap, vec4(uv, float(i), depth));
    }
    return 1.0;
}

// March upward from `origin` finding the first probe height at which the
// shadow map reports "sky visible". Returns a 0..1 enclosure ratio:
//   0 = exits to sky immediately (open ground)
//   1 = stays inside terrain through the whole march
// Mid values directly express how deep we are in geometry.
float skyMarchEnclosure(vec3 origin, float maxH) {
    float stepM = maxH / float(SKY_MARCH_STEPS);
    float occlusionLength = 0.0;
    for (int i = 0; i < SKY_MARCH_STEPS; ++i) {
        float h = (float(i) + 0.5) * stepM;
        vec3 probe = origin + vec3(0.0, h, 0.0);
        float vis = sampleProbeShadow(probe);
        if (vis > 0.5) {
            return clamp(occlusionLength / maxH, 0.0, 1.0);
        }
        occlusionLength += stepM;
    }
    return 1.0;
}

// Y-cavity — screen-space derivative term, sun-independent.
// Guarded against geometry-edge pixels (cube-face corners) where dFdx of
// worldPos.y spikes and would otherwise cause one-pixel band flicker.
float yCavityTerm(vec3 worldPos, vec3 N) {
    // Require near-floor normal (>= ~30° above horizontal). Skipping side
    // faces also kills the corner-flicker on vertical walls.
    float floorWeight = clamp(N.y, 0.0, 1.0);
    if (floorWeight < 0.5) return 0.0;
    vec2 dy  = vec2(dFdx(worldPos.y), dFdy(worldPos.y));
    vec2 dPx = vec2(dFdx(worldPos.x), dFdy(worldPos.x));
    vec2 dPz = vec2(dFdx(worldPos.z), dFdy(worldPos.z));
    float pixelMeters = max(length(vec2(length(dPx), length(dPz))), 1e-4);
    // Edge guard: at face-to-face corners worldPos jumps by O(voxel) per
    // pixel → pixelMeters spikes. Reject these so they don't smear a
    // bright cavity flash across the band edge.
    if (pixelMeters > 0.25) return 0.0;
    float slopePerMeter = length(dy) / pixelMeters;
    return clamp(slopePerMeter * floorWeight, 0.0, 1.0);
}

// Heightmap zenith term — TRUE sun-independent enclosure.
// Samples the static world heightmap (uploaded once to binding 9) and
// reports how far below the original surface this fragment lies. This
// catches dug-out holes regardless of sun elevation; the sun shadow march
// still handles natural-feature occlusion at high sun.
//
// Returns a 0..1 ratio of "depth below original surface" / probeMaxHeight.
// 0 = at-or-above original surface (open ground or natural valley)
// 1 = ≥ probeMaxHeight below original surface (deep hole)
float heightmapZenithTerm(vec3 worldPos, vec3 N, float maxH) {
    float scale = shadow.skyHeightmapInfo.w;
    if (scale <= 0.0) return 0.0; // upload not yet performed → disabled

    float metersPerTexel = max(shadow.skyHeightmapInfo.z, 1e-4);
    vec2 texSize = vec2(textureSize(skyHeightmap, 0));
    vec2 worldSpan = texSize * metersPerTexel;

    // Snap to heightmap-texel center so neighbouring voxel columns within
    // one heightmap cell read the SAME texel (NEAREST sampler). Result:
    // each 2.5 m × 2.5 m heightmap cell = one constant enclosure depth.
    vec2 texelXZ = (floor(worldPos.xz / metersPerTexel) + 0.5) * metersPerTexel;
    vec2 uv = (texelXZ - shadow.skyHeightmapInfo.xy) / worldSpan;
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
        return 0.0;
    }

    float surfaceY = texture(skyHeightmap, uv).r * scale;

    // Stable voxel quantization (was source of "hue flicker" between
    // adjacent fragments on the same face):
    //   - worldPos.y for a floor fragment sits EXACTLY on a voxel
    //     boundary; triangle interpolation gives neighbouring pixels
    //     1.999 999 vs 2.000 001 → floor() puts them in different voxel
    //     layers. Subtracting half-voxel biases the sample firmly into
    //     the voxel BELOW the surface, where there is no boundary to
    //     flip across.
    //   - surfaceY is heightmap_value * scale where the heightmap stores
    //     integer voxel counts → surfaceY/VOXEL is integer; round trick
    //     (+0.5 then floor) makes that integer recovery exact.
    const float VOXEL_M = 0.25;
    const float INV_VOXEL = 1.0 / VOXEL_M;
    const float DEAD_VOX = 2.0;             // 2 voxels = 0.5 m dead-zone
    float surfaceVox = floor(surfaceY * INV_VOXEL + 0.5);
    float fragVox    = floor((worldPos.y - VOXEL_M * 0.5) * INV_VOXEL);
    float depthVox   = max(0.0, surfaceVox - fragVox - DEAD_VOX);

    return clamp(depthVox * VOXEL_M / maxH, 0.0, 1.0);
}

// Raw 0..1 enclosure ratio. NOT light-source dependent; never gated by
// time of day.
float computeSkyEnclosureRaw(vec3 worldPos, vec3 N) {
    float maxH = max(shadow.skyEnclosureParams.z, 0.5);

    // Heightmap zenith term — true geometric "how far below original surface".
    // Replaces the old sky-march term (which sampled the sun shadow map and
    // therefore confused sun-shadow with sky-block, producing triangle
    // artifacts on walls and false depth on sun-shadowed ground).
    float zenith = heightmapZenithTerm(worldPos, N, maxH);

    // Cavity adds DEEPER enclosure on slopes/dips where the zenith term has
    // headroom. Additive-in-headroom (instead of max-combine) makes the
    // cavityGain slider visibly deepen darkness.
    float cavityGain = clamp(shadow.shadowConfig2.w, 0.0, 1.0);
    float cavity = yCavityTerm(worldPos, N) * cavityGain;

    return clamp(zenith + cavity * (1.0 - zenith), 0.0, 1.0);
}

// "Shadows dissolve from light": when sun rays actually reach this
// fragment (sunReach > 0), reduce the geometric enclosure proportionally.
// sunReach = sunShadow * sunIntensity (0..1). Strength capped so even
// fully-lit holes keep some enclosure (real depressions stay shaded).
float applySunDissolve(float raw, float sunReach) {
    const float SUN_DISSOLVE_STRENGTH = 0.7; // 0=no dissolve, 1=fully dispels
    return raw * (1.0 - clamp(sunReach, 0.0, 1.0) * SUN_DISSOLVE_STRENGTH);
}

float computeSkyEnclosure(vec3 worldPos, vec3 N, float sunReach) {
    float mode = shadow.skyEnclosureParams.w;
    if (mode < 0.5) return 1.0;

    float intensity  = max(shadow.skyEnclosureParams.x, 0.0);
    float minAmbient = clamp(shadow.skyEnclosureParams.y, 0.0, 1.0);

    float raw = computeSkyEnclosureRaw(worldPos, N);
    raw = applySunDissolve(raw, sunReach);

    // Exponential response so even shallow depth ratios reach near-1.
    // intensity=1 → gentle, intensity=4 → fast saturation toward black.
    float k = max(intensity, 0.05) * 3.0;
    float occ = 1.0 - exp(-raw * k);

    // No second quantization: `raw` is already discretely stepped (one
    // step per voxel layer). Double-quantizing here was the cause of
    // hue flicker as intensity / probeMaxHeight changed — the exp curve
    // maps voxel-step inputs onto output values that drift across the
    // output band centres, making adjacent voxel cells flip between
    // two darkness levels at extreme settings.
    return mix(1.0, minAmbient, occ);
}

// Backward-compat: ambient call sites pass no sunReach → full enclosure.
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

#endif // SKY_ENCLOSURE_GLSL_INCLUDED

````

## src\world\edit\texture\TextureBrushStyles.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Owns texture brush style identity, preview colors, variants, and blend policy.
#include "world/edit/texture/TextureBrushStyles.h"

#include <array>
#include <algorithm>
#include <cstddef>

#include "world/edit/TextureOverlayStore.h"

namespace TextureOverlay {
namespace TextureBrushStyles {
namespace {

constexpr size_t kTextureTypeCount = static_cast<size_t>(TextureType::COUNT);

size_t styleIndex(TextureType type) {
    const size_t idx = static_cast<size_t>(type);
    return (idx < kTextureTypeCount) ? idx : 0u;
}

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

uint8_t edgePriority(TransitionEdgeStyle style) {
    switch (style) {
        case TransitionEdgeStyle::Sloppy: return 3u; // liquid mud edge dominates mixed borders
        case TransitionEdgeStyle::Leafy:  return 2u; // grass blades remain visible over dry scatter
        case TransitionEdgeStyle::Grainy: return 1u; // dirt/sand/drysand dry crumble
        case TransitionEdgeStyle::None:
        default: return 0u;
    }
}

const std::array<TextureBrushStyle, kTextureTypeCount> kStyles{{
    TextureBrushStyle{
        TextureType::Grass,
        "Grass",
        TextureStylePreviewColors{
            glm::vec3(0.185f, 0.350f, 0.115f),
            glm::vec3(0.340f, 0.535f, 0.175f),
            glm::vec3(0.070f, 0.155f, 0.050f),
            glm::vec3(0.235f, 0.430f, 0.120f)},
        TransitionEdgeStyle::Leafy,
        0x31A53B4Du},
    TextureBrushStyle{
        TextureType::Mud,
        "Mud",
        TextureStylePreviewColors{
            glm::vec3(0.225f, 0.155f, 0.115f),
            glm::vec3(0.315f, 0.235f, 0.175f),
            glm::vec3(0.070f, 0.050f, 0.040f),
            glm::vec3(0.180f, 0.118f, 0.090f)},
        TransitionEdgeStyle::Sloppy,
        0x6D3A2F19u},
    TextureBrushStyle{
        TextureType::DrySand,
        "Dry Sand",
        TextureStylePreviewColors{
            glm::vec3(0.620f, 0.510f, 0.360f),
            glm::vec3(0.785f, 0.675f, 0.490f),
            glm::vec3(0.385f, 0.295f, 0.205f),
            glm::vec3(0.585f, 0.470f, 0.325f)},
        TransitionEdgeStyle::Grainy,
        0x9E5B3C27u},
    TextureBrushStyle{
        TextureType::Sand,
        "Sand",
        TextureStylePreviewColors{
            glm::vec3(0.770f, 0.620f, 0.420f),
            glm::vec3(0.900f, 0.770f, 0.545f),
            glm::vec3(0.570f, 0.430f, 0.275f),
            glm::vec3(0.815f, 0.670f, 0.440f)},
        TransitionEdgeStyle::Grainy,
        0xD9B15E31u},
    TextureBrushStyle{
        TextureType::Dirt,
        "Dirt",
        TextureStylePreviewColors{
            glm::vec3(0.225f, 0.155f, 0.115f),
            glm::vec3(0.315f, 0.235f, 0.175f),
            glm::vec3(0.070f, 0.050f, 0.040f),
            glm::vec3(0.180f, 0.118f, 0.090f)},
        TransitionEdgeStyle::Grainy,
        0xA66A3E51u},
}};

TransitionEdgeStyle ownerDefaultEdge(TextureType owner) {
    return getStyle(owner).defaultBlendEdge;
}

} // namespace

const TextureBrushStyle& getStyle(TextureType type) {
    return kStyles[styleIndex(type)];
}

const char* name(TextureType type) {
    return getStyle(type).displayName;
}

uint8_t computeVariant(const glm::ivec3& coord,
                       uint8_t face,
                       TextureType type,
                       uint8_t seed) {
    const TextureBrushStyle& style = getStyle(type);
    const uint32_t h = hashPaintCoord(coord, face, style.variantSalt);
    return static_cast<uint8_t>((seed + (h & 0x7u)) & 0x7u);
}

TransitionEdgeStyle classifyTransitionEdge(TextureType a,
                                           TextureType b,
                                           const glm::ivec3& lodCoord) {
    if (a == b) {
        return TransitionEdgeStyle::None;
    }

    const TextureBrushStyle& sa = getStyle(a);
    const TextureBrushStyle& sb = getStyle(b);
    const uint32_t h = hashPaintCoord(lodCoord, 0u, sa.variantSalt ^ sb.variantSalt);

    // Owner-centric transition policy: each side of a material boundary stores
    // the edge personality of the material it owns. The shader then draws that
    // personality as grass blades, muddy liquid smears, or dry crumbling cracks.
    // The hash only introduces small pair-specific variation; it does not make
    // grass cells randomly become muddy or dry cells randomly become leafy.
    if (a == TextureType::Grass) {
        return TransitionEdgeStyle::Leafy;
    }
    if (a == TextureType::Mud) {
        return TransitionEdgeStyle::Sloppy;
    }

    if (b == TextureType::Mud && (h & 3u) == 0u) {
        // A few dry pixels at mud boundaries darken into damp crumble, but the
        // owning dry material still remains visually dry overall.
        return TransitionEdgeStyle::Sloppy;
    }

    return ownerDefaultEdge(a);
}

TransitionEdgeStyle mergeEdgeStyle(TransitionEdgeStyle lhs,
                                   TransitionEdgeStyle rhs) {
    return edgePriority(rhs) >= edgePriority(lhs) ? rhs : lhs;
}

} // namespace TextureBrushStyles
} // namespace TextureOverlay

````

## FIND: Grass

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/ui/debug_menu/world/TexturePaintTool.h
- include/world/edit/TextureOverlayStore.h
- shaders/common/terrain_materials.glsl
- src/ui/debug_menu/world/texture_paint/TexturePaintToolUI.cpp
- src/world/edit/texture/TextureBrushStyles.cpp
- src/world/edit/texture/TextureOverlayIO.cpp

Occurrence preview:
- include/ui/debug_menu/world/TexturePaintTool.h:191: TextureOverlay::TextureType m_textureType{TextureOverlay::TextureType::Grass};
- include/world/edit/TextureOverlayStore.h:25: Grass = 0,
- include/world/edit/TextureOverlayStore.h:49: case TextureType::Grass:   return "grass";
- include/world/edit/TextureOverlayStore.h:172: TextureType type{TextureType::Grass};
- shaders/common/terrain_materials.glsl:37: return vec3(0.185, 0.350, 0.115);                 // grass
- shaders/common/terrain_materials.glsl:167: vec3 materialGrassBladeMasks(vec2 pixelCoord, uint variant, uint face) {
- shaders/common/terrain_materials.glsl:237: if (type == 0u) return 1u; // leafy grass
- shaders/common/terrain_materials.glsl:293: uint type = (face == 3u) ? 0u : 4u; // top grass, side/bottom minecraftish dirt
- src/ui/debug_menu/world/texture_paint/TexturePaintToolUI.cpp:68: static const char* TYPE_NAMES[] = { "Grass", "Mud", "Dry Sand", "Sand", "Dirt" };
- src/world/edit/texture/TextureBrushStyles.cpp:36: case TransitionEdgeStyle::Leafy:  return 2u; // grass blades remain visible over dry scatter
- src/world/edit/texture/TextureBrushStyles.cpp:45: TextureType::Grass,
- src/world/edit/texture/TextureBrushStyles.cpp:46: "Grass",
- src/world/edit/texture/TextureBrushStyles.cpp:132: // personality as grass blades, muddy liquid smears, or dry crumbling cracks.
- src/world/edit/texture/TextureOverlayIO.cpp:201: : TextureType::Grass;


## FIND: Mud

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/world/edit/TextureOverlayStore.h
- shaders/common/terrain_materials.glsl
- src/ui/debug_menu/world/texture_paint/TexturePaintToolUI.cpp
- src/world/edit/texture/TextureBrushStyles.cpp

Occurrence preview:
- include/world/edit/TextureOverlayStore.h:26: Mud = 1,
- include/world/edit/TextureOverlayStore.h:50: case TextureType::Mud:     return "mud";
- shaders/common/terrain_materials.glsl:33: if (type == 1u) return vec3(0.225, 0.155, 0.115); // mud
- shaders/common/terrain_materials.glsl:36: if (type == 4u) return vec3(0.225, 0.155, 0.115); // dirt, same family as mud
- shaders/common/terrain_materials.glsl:203: vec3 materialMudSpotField(vec2 pixelCoord,
- shaders/common/terrain_materials.glsl:238: if (type == 1u) return 2u; // sloppy mud
- src/ui/debug_menu/world/texture_paint/TexturePaintToolUI.cpp:68: static const char* TYPE_NAMES[] = { "Grass", "Mud", "Dry Sand", "Sand", "Dirt" };
- src/world/edit/texture/TextureBrushStyles.cpp:35: case TransitionEdgeStyle::Sloppy: return 3u; // liquid mud edge dominates mixed borders
- src/world/edit/texture/TextureBrushStyles.cpp:55: TextureType::Mud,
- src/world/edit/texture/TextureBrushStyles.cpp:56: "Mud",
- src/world/edit/texture/TextureBrushStyles.cpp:132: // personality as grass blades, muddy liquid smears, or dry crumbling cracks.


## FIND: MATERIAL_GRASS

No matching code file found for: MATERIAL_GRASS


## FIND: MATERIAL_MUD

No matching code file found for: MATERIAL_MUD
