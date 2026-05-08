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

## shaders\terrain\cube.vert

Description: No CC-DESC found.

````glsl
#version 450

// Compact vertex input: geometry word + procedural material word.
// Bit layout: X(8) | Y(10) | Z(8) | face(3) | AO(3) = 32 bits total in uint32
layout(location = 0) in uint inPacked;
layout(location = 1) in uint inMaterial;

// Use invariant to ensure bit-exact gl_Position for shared vertices
invariant gl_Position;

// Output to fragment shader
layout(location = 0) out vec3 fragWorldPos;
layout(location = 1) out vec3 fragNormal;
layout(location = 2) out vec3 fragColor;
layout(location = 3) out float fragAOLevel;
layout(location = 4) out vec2 fragUV;           // UV within face (0-1)
layout(location = 5) out flat vec3 fragChunkOrigin; // Chunk origin for voxel coord calculation
layout(location = 6) out flat uint fragFace;    // Face ID
layout(location = 7) out flat vec3 fragFlatPos; // Provoking vertex pos (constant per tri)
layout(location = 8) out flat float fragFlatAO; // Provoking vertex AO (constant per tri)
layout(location = 9) out flat uint fragMaterial;

layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 model;
    mat4 view;      // Camera-relative view matrix (translation zeroed)
    mat4 proj;
    vec4 cameraPos; // Camera world position for camera-relative rendering
} ubo;

// Bindless ChunkOrigins SSBO array (Phase D): slot 0 = static CPU-mode origins,
// slot 1 = GPU culling visible-origins output. Selected per-draw via push constant.
layout(set = 0, binding = 1) readonly buffer ChunkOrigins {
    vec4 origins[];
} chunkOrigins[3];

layout(push_constant) uniform OriginsPC {
    uint originsIndex;
} pcOrigins;

// Unpack position from uint32.
// X: bits 0-7 (8 bits, 0-128 vertex positions -> 0-32m at 4 vox/m)
// Y: bits 8-17 (10 bits, direct voxel Y coordinate)
// Z: bits 18-25 (8 bits, 0-128 vertex positions -> 0-32m at 4 vox/m)
// Face: bits 26-28 (3 bits)
// AO: bits 29-31 (3 bits)
vec3 unpackPosition(uint packed) {
    
    uint x = (packed >> 0) & 0xFFu;    // 8 bits (0-128)
    uint y = (packed >> 8) & 0x3FFu;   // 10 bits (0-1023)
    uint z = (packed >> 18) & 0xFFu;   // 8 bits (0-128)
    
    // Convert back to meters (chunk-relative)
    // Scale: 4 voxels per meter (0.25m per voxel)
    float fx = float(x) / 4.0;         // 0-32m with 128 steps
    float fy = float(y) / 4.0;
    float fz = float(z) / 4.0;         // 0-32m with 128 steps
    
    return vec3(fx, fy, fz);
}

float unpackAOLevel(uint packed) {
    uint ao = (packed >> 29) & 0x7u;  // 3 bits for AO (bits 29-31)
    return float(ao) / 3.0;
}

// Get face normal vector for lighting
vec3 getFaceNormal(uint packed) {
    uint face = (packed >> 26) & 0x7u;  // 3 bits (bits 26-28)
    
    // Return normal vector for each face
    if (face == 0u) return vec3(-1.0, 0.0, 0.0);  // -X
    if (face == 1u) return vec3(1.0, 0.0, 0.0);   // +X
    if (face == 2u) return vec3(0.0, -1.0, 0.0);  // -Y
    if (face == 3u) return vec3(0.0, 1.0, 0.0);   // +Y
    if (face == 4u) return vec3(0.0, 0.0, -1.0);  // -Z
    if (face == 5u) return vec3(0.0, 0.0, 1.0);   // +Z
    return vec3(0.0, 1.0, 0.0);  // fallback up
}

// Get base voxel color (can be replaced with texture lookups later)
vec3 getVoxelColor(uint packed) {
    uint face = (packed >> 26) & 0x7u;  // 3 bits (bits 26-28)

    // Simple base colors - grass green for most faces, brown for bottom
    if (face == 2u) return vec3(0.4, 0.2, 0.1);   // -Y (bottom): brown dirt
    return vec3(0.3, 0.6, 0.2);  // All other faces: grass green
}

void getTangents(uint face, out vec3 t1, out vec3 t2) {
    if (face <= 1u) { // X faces (0, 1)
        t1 = vec3(0.0, 0.0, 1.0); // Z is U
        t2 = vec3(0.0, 1.0, 0.0); // Y is V
    } else if (face <= 3u) { // Y faces (2, 3)
        t1 = vec3(1.0, 0.0, 0.0); // X is U
        t2 = vec3(0.0, 0.0, 1.0); // Z is V
    } else { // Z faces (4, 5)
        t1 = vec3(1.0, 0.0, 0.0); // X is U
        t2 = vec3(0.0, 1.0, 0.0); // Y is V
    }
}

void main() {
    // Unpack integer coordinates directly
    uint ux = (inPacked >> 0) & 0xFFu;    // 8 bits (0-128)
    uint uy = (inPacked >> 8) & 0x3FFu;   // 10 bits (0-1023)
    uint uz = (inPacked >> 18) & 0xFFu;   // 8 bits (0-128)
    uint face = (inPacked >> 26) & 0x7u;

    // Get chunk origin from storage buffer using gl_InstanceIndex (set via firstInstance).
    // origins.w is a per-draw material-overlay hint in GPU-culling mode:
    //   0 = this chunk has no texture paint, skip overlay hash lookup in cube.frag
    //   1 = this chunk may have texture paint, allow exact overlay lookup
    // CPU-culling mode historically writes w=0, so keep it exact by forcing the hint on
    // when originsIndex==0.
    vec4 chunkOriginData = chunkOrigins[pcOrigins.originsIndex].origins[gl_InstanceIndex];
    precise ivec3 chunkCoord = ivec3(chunkOriginData.xyz);
    const uint MATERIAL_OVERLAY_CHUNK_HINT_BIT = 0x40000000u;
    uint materialOverlayHintBit =
        (pcOrigins.originsIndex == 0u || chunkOriginData.w > 0.5)
            ? MATERIAL_OVERLAY_CHUNK_HINT_BIT
            : 0u;

    // WATERTIGHT RENDERING FIX:
    // Compute ABSOLUTE world voxel coordinates first, then convert to float ONCE.
    int worldVoxelX = chunkCoord.x * 128 + int(ux);
    int worldVoxelY = chunkCoord.y * 512 + int(uy);
    int worldVoxelZ = chunkCoord.z * 128 + int(uz);

    // Convert integer voxel to world position (meters)
    // 0.25 = 1/4 meters per voxel
    precise vec3 worldPos = vec3(float(worldVoxelX), float(worldVoxelY), float(worldVoxelZ)) * 0.25;

    vec3 faceNormal = getFaceNormal(inPacked);

    // Chunk-relative position for UV calculation
    vec3 chunkRelPos = vec3(float(ux), float(uy), float(uz)) * 0.25;

    // Calculate UV within voxel for top face
    vec2 uv = vec2(0.0);
    if (face == 3u) { // +Y top face
        uv = fract(chunkRelPos.xz * 4.0);
    }

    // Output data for fragment shader
    fragWorldPos = worldPos;
    fragNormal = faceNormal;
    fragColor = getVoxelColor(inPacked);
    fragAOLevel = unpackAOLevel(inPacked);
    fragUV = uv;
    fragChunkOrigin = vec3(chunkCoord * ivec3(128, 512, 128)) * 0.25;
    fragFace = face;
    fragFlatPos = worldPos;
    fragFlatAO = unpackAOLevel(inPacked);
    fragMaterial = (inMaterial & ~MATERIAL_OVERLAY_CHUNK_HINT_BIT) | materialOverlayHintBit;
    // === CAMERA-RELATIVE RENDERING ===
    // Subtract camera position to improve precision for distant geometry
    precise vec3 cameraRelativePos = worldPos - ubo.cameraPos.xyz;

    // Transform to clip space
    gl_Position = ubo.proj * ubo.view * vec4(cameraRelativePos, 1.0);
}

````

## shaders\terrain\dccm_terrain.vert

Description: No CC-DESC found.

````glsl
#version 450

layout(location = 0) in uint inPacked;

invariant gl_Position;

layout(location = 0) out vec3 fragWorldPos;
layout(location = 1) out float fragAOLevel;
layout(location = 2) out flat uint fragFace;
layout(location = 3) out flat vec3 fragFlatPos;
layout(location = 4) out flat float fragFlatAO;
layout(location = 5) out flat vec3 fragChunkOrigin;

layout(set = 0, binding = 0) uniform UniformBufferObject {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 cameraPos;
} ubo;

layout(set = 0, binding = 1) readonly buffer ChunkOrigins {
    vec4 origins[];
} chunkOrigins[3];

layout(push_constant) uniform OriginsPC {
    uint originsIndex;
} pcOrigins;

float unpackAOLevel(uint packed) {
    uint ao = (packed >> 29) & 0x7u;
    return float(ao) / 3.0;
}

void main() {
    uint ux = (inPacked >> 0) & 0xFFu;
    uint uy = (inPacked >> 8) & 0x3FFu;
    uint uz = (inPacked >> 18) & 0xFFu;
    uint face = (inPacked >> 26) & 0x7u;

    precise ivec3 chunkCoord = ivec3(chunkOrigins[pcOrigins.originsIndex].origins[gl_InstanceIndex].xyz);

    int worldVoxelX = chunkCoord.x * 128 + int(ux);
    int worldVoxelY = chunkCoord.y * 512 + int(uy);
    int worldVoxelZ = chunkCoord.z * 128 + int(uz);

    precise vec3 worldPos = vec3(float(worldVoxelX), float(worldVoxelY), float(worldVoxelZ)) * 0.25;

    fragWorldPos = worldPos;
    fragAOLevel = unpackAOLevel(inPacked);
    fragFace = face;
    fragFlatPos = worldPos;
    fragFlatAO = fragAOLevel;
    fragChunkOrigin = vec3(chunkCoord * ivec3(128, 512, 128)) * 0.25;

    precise vec3 cameraRelativePos = worldPos - ubo.cameraPos.xyz;
    gl_Position = ubo.proj * ubo.view * vec4(cameraRelativePos, 1.0);
}

````

## Folder tree: shaders\common\

```text
clustered_lighting.glsl
dither_utils.glsl
shadow_sampling.glsl
sky_enclosure.glsl
```

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
    world/update/WorldUpdateLoop.cpp
    world/update/WorldChunkLoader.cpp
    world/update/WorldMeshingDispatch.cpp
    world/jobs/WorldChunkJobScheduling.cpp
    world/upload/WorldUploadQueue.cpp
    world/finalize/WorldFinalizeQueue.cpp
    world/finalize/WorldTopologyChanges.cpp
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
    world/edit/dccm/TerrainEditDCCMMesher.cpp
    world/edit/dccm/DCCMHeightAnalysis.cpp
    world/edit/dccm/DCCMBoundaryRepair.cpp
    world/edit/dccm/DCCMFeatureMesh.cpp
    world/edit/dccm/DCCMWeldCleanup.cpp
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
