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


## include\world\World.h

Description: No CC-DESC found. C++ class 'BufferSuballocator'.

````cpp
#pragma once

// GPT-DESC: Declares the World facade and preserves public World type aliases.

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
#include "world/WorldSnapshotTypes.h"
#include "world/WorldEditArtifactTypes.h"
#include "world/WorldTopologyTypes.h"
#include "world/WorldStreamingTypes.h"
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

    // ---- World data types live in focused headers. ----
    // These aliases preserve historical `World::*` qualified names.
    using EditCollisionEntry  = WorldData::EditCollisionEntry;
    using SnapshotInfo        = WorldData::SnapshotInfo;
    using TerrainBoxRecord    = WorldData::TerrainBoxRecord;
    using MeshTopologyChange  = WorldData::MeshTopologyChange;
    using EditArtifactKey     = WorldData::EditArtifactKey;
    using EditArtifactKeyHash = WorldData::EditArtifactKeyHash;
    using EditArtifact        = WorldData::EditArtifact;
    using StreamingMetrics    = WorldData::StreamingMetrics;

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

    const std::vector<SnapshotInfo>& getSnapshotInfos() const { return m_snapshotInfos; }
    const std::vector<TerrainBoxRecord>& getTerrainBoxes() const { return m_terrainBoxes; }
    uint64_t getTerrainBoxRevision() const { return m_terrainBoxRevision; }
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

    using PendingMeshBufferFree = WorldData::PendingMeshBufferFree;
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

## src\world\WorldDebugMetrics.cpp

Description: No CC-DESC found.

````cpp
// WorldDebugMetrics.cpp — Debug info assembly + finalize diagnostics report
// Pure read-only aggregation of metrics from World subsystems.
// Extracted from World.cpp to reduce god-file size without changing behavior.

#include "world/World.h"
#include "ui/InGameDebug.h"
#include "world/chunks/core/Chunk.h"
#include "vulkan/BufferSuballocator.h"
#include <iomanip>
#include <sstream>
#include <chrono>

void World::assembleDebugInfo(const UpdateTimings& timings,
                              BufferSuballocator* vbAllocator,
                              BufferSuballocator* ibAllocator,
                              float cpuFrameMs,
                              float gpuFrameMs) {
    const bool statsOpen = m_inGameDebug->isStatsWindowOpen();
    const bool workersOpen = m_inGameDebug->isWorkersWindowOpen();
    const bool vramOpen = m_inGameDebug->isChunkVramWindowOpen();
    if (!statsOpen && !workersOpen && !vramOpen) {
        return;
    }

    InGameDebug::DebugInfo info;
    // Always keep these running counters current for VRAM window header.
    info.gpu.totalChunks = m_statsChunksWithMesh.load(std::memory_order_relaxed);
    info.gpu.totalSubChunks = m_statsTotalSubChunks.load(std::memory_order_relaxed);
    info.gpu.splitChunks = m_statsSplitChunks.load(std::memory_order_relaxed);
    info.gpu.seamChunks = m_statsSeamSubChunks.load(std::memory_order_relaxed);

    if (statsOpen) {
        // Update in-game stats display using atomic counters (O(1) instead of O(N)).
        const int loadingCount = m_loadingCount.load(std::memory_order_relaxed);
        const int meshingCount = m_meshingCount.load(std::memory_order_relaxed);
        const int readyCount = m_readyCount.load(std::memory_order_relaxed);

        const auto debugInfo = m_chunkManager->getDebugInfo();
        info.worldName = m_worldName;
        info.generationDate = m_worldGenerationDate;
        info.completedRing = debugInfo.completedRing;
        info.currentRing = debugInfo.currentRing;
        info.currentRingProgress = debugInfo.currentRingProgress;
        info.currentRingTotal = debugInfo.currentRingTotal;
        info.facingDirection = debugInfo.facingDirection;
        info.cameraYaw = m_lastCameraYaw;  // Real-time yaw for live facing display.
        info.loadingChunks = loadingCount;
        info.meshingChunks = meshingCount;
        info.readyChunks = readyCount;

        // Add GPU metrics.
        info.gpu.uploadQueueSize = m_streamingMetrics.uploadQueueSize;
        info.gpu.meshesUploadedTotal = static_cast<uint32_t>(m_streamingMetrics.meshesUploaded.load(std::memory_order_relaxed));
        info.gpu.uploadUtilization = (static_cast<float>(info.gpu.uploadQueueSize) / info.gpu.uploadQueueCapacity) * 100.0f;
        if (info.gpu.uploadUtilization > 100.0f) info.gpu.uploadUtilization = 100.0f;

        // VRAM limiting info (from ChunkRenderSystem).
        info.gpu.vramLimitingEnabled = m_renderSystem.isVramLimitingEnabled();
        info.gpu.vramBudgetBytes = m_renderSystem.getVramBudgetBytes();
        info.gpu.currentVramUsage = m_renderSystem.getCurrentVramUsage();

        // Calculate VRAM usage from allocators.
        if (vbAllocator && ibAllocator) {
            info.gpu.totalCapacityBytes = vbAllocator->getTotalCapacity() + ibAllocator->getTotalCapacity();
            info.gpu.usedVramBytes = vbAllocator->getAllocatedBytes() + ibAllocator->getAllocatedBytes();
            info.gpu.vramUtilization = (info.gpu.totalCapacityBytes > 0)
                ? (static_cast<float>(info.gpu.usedVramBytes) / info.gpu.totalCapacityBytes) * 100.0f
                : 0.0f;
            if (info.gpu.vramUtilization > 100.0f) info.gpu.vramUtilization = 100.0f;

            // Buffer allocator detailed stats.
            info.gpu.vbTotalBytes = vbAllocator->getTotalCapacity();
            info.gpu.vbUsedBytes = vbAllocator->getAllocatedBytes();
            info.gpu.ibTotalBytes = ibAllocator->getTotalCapacity();
            info.gpu.ibUsedBytes = ibAllocator->getAllocatedBytes();

            // Note: voxel memory is no longer tracked (VoxelStore removed with precomputed meshes).
            info.gpu.voxelMemoryBytes = 0;
            info.gpu.voxelPoolCapacity = 0;
        }

        // Add main thread metrics.
        info.mainThread.cpuFrameMs = cpuFrameMs;
        info.mainThread.gpuFrameMs = gpuFrameMs;
        info.mainThread.cpuUtilization = (cpuFrameMs / info.mainThread.targetFrameMs) * 100.0f;
        info.mainThread.gpuUtilization = (gpuFrameMs / info.mainThread.targetFrameMs) * 100.0f;

        // CPU breakdown (reuse worldUpdateEnd from debug timing above).
        info.cpuBreakdown.chunkLoadingMs = std::chrono::duration<float, std::milli>(timings.chunkLoadEnd - timings.chunkLoadStart).count();
        info.cpuBreakdown.meshingMs = std::chrono::duration<float, std::milli>(timings.meshingEnd - timings.meshingStart).count();
        info.cpuBreakdown.uploadMs = std::chrono::duration<float, std::milli>(timings.uploadEnd - timings.uploadStart).count();
        info.cpuBreakdown.collisionMs = std::chrono::duration<float, std::milli>(timings.collisionEnd - timings.collisionStart).count();
        info.cpuBreakdown.finalizeMs = std::chrono::duration<float, std::milli>(timings.finalizeEnd - timings.finalizeStart).count();
        info.cpuBreakdown.worldUpdateMs = std::chrono::duration<float, std::milli>(timings.worldUpdateEnd - timings.startTime).count();

        // Note: renderMs and otherMs will be populated by Engine.
        if (info.mainThread.cpuUtilization > 100.0f) info.mainThread.cpuUtilization = 100.0f;
        if (info.mainThread.gpuUtilization > 100.0f) info.mainThread.gpuUtilization = 100.0f;

        // Culling stats (set by Engine).
        info.culling.gpuCullingEnabled = m_cullingStats.gpuCullingEnabled;
        info.culling.gpuCullingReady = m_cullingStats.gpuCullingReady;
        info.culling.totalChunksInCulling = m_cullingStats.totalChunksInCulling;
        info.culling.visibleDrawCalls = m_cullingStats.visibleDrawCalls;
        info.culling.culledDrawCalls = m_cullingStats.culledDrawCalls;
        info.culling.frustumPassed = m_cullingStats.frustumPassed;
        info.culling.cullingDispatchMs = m_cullingStats.cullingDispatchMs;
        info.culling.totalCullingMs = m_cullingStats.totalCullingMs;
    }

    if (workersOpen) {
        // Add job system worker stats.
        const auto& jobMetrics = m_jobSystem.getMetrics();
        info.workers.resize(jobMetrics.workerStats.size());
        info.workerCount = jobMetrics.workerStats.size();
        info.totalWorkerJobs = 0;
        info.totalWorkerSteals = 0;
        info.totalWorkerQueueSize = 0;

        uint64_t maxQueueSize = 1;
        for (size_t i = 0; i < jobMetrics.workerStats.size(); ++i) {
            const uint64_t qSize = jobMetrics.workerStats[i].currentQueueSize.load(std::memory_order_relaxed);
            if (qSize > maxQueueSize) maxQueueSize = qSize;
        }
        for (size_t i = 0; i < jobMetrics.workerStats.size(); ++i) {
            info.workers[i].jobsExecuted = jobMetrics.workerStats[i].jobsExecuted.load(std::memory_order_relaxed);
            info.workers[i].jobsStolen = jobMetrics.workerStats[i].jobsStolen.load(std::memory_order_relaxed);
            info.workers[i].queueSize = jobMetrics.workerStats[i].currentQueueSize.load(std::memory_order_relaxed);
            info.workers[i].utilizationPercent = (static_cast<float>(info.workers[i].queueSize) / maxQueueSize) * 100.0f;

            // Accumulate totals.
            info.totalWorkerJobs += info.workers[i].jobsExecuted;
            info.totalWorkerSteals += info.workers[i].jobsStolen;
            info.totalWorkerQueueSize += info.workers[i].queueSize;
        }
    }
    
    m_inGameDebug->update(info);
    
    // Note: minimap camera info is now set by Engine with actual camera parameters
}

std::string World::generateFinalizeDiagReport(float spikeThresholdMs) const {
    std::ostringstream ss;
    ss << std::fixed << std::setprecision(3);

    // Collect frames from ring buffer in chronological order
    size_t count = m_finalizeDiagHistory.size();
    if (count == 0) {
        ss << "No finalize diagnostic data recorded yet.\n";
        return ss.str();
    }

    // Build ordered list (oldest first)
    std::vector<const FinalizeDiagFrame*> ordered;
    ordered.reserve(count);
    if (count < FINALIZE_DIAG_CAPACITY) {
        for (size_t i = 0; i < count; ++i)
            ordered.push_back(&m_finalizeDiagHistory[i]);
    } else {
        for (size_t i = 0; i < count; ++i)
            ordered.push_back(&m_finalizeDiagHistory[(m_finalizeDiagWriteIdx + i) % count]);
    }

    // Filter: only frames with actual work (finalize or LOD swap)
    std::vector<const FinalizeDiagFrame*> active;
    active.reserve(ordered.size());
    for (auto* f : ordered) {
        if (f->finalizeCount > 0 ||
            f->lodSwapEntityCount > 0 ||
            f->lodSwapFreeMs > 0.0001f ||
            f->lodSwapFreeQueuedCount > 0 ||
            f->lodSwapFreeDrainedCount > 0) {
            active.push_back(f);
        }
    }

    // Summary statistics (only active frames)
    float totalMs = 0, maxMs = 0, minMs = 1e9f;
    int spikeCount = 0;
    float spikeTotal = 0;
    float totalSwpFree = 0;
    float totalLateUpload = 0;
    float totalVisualReady = 0;
    uint64_t totalFreeQueued = 0;
    uint64_t totalFreeDrained = 0;
    uint32_t maxFreeBacklog = 0;
    uint32_t lastFreeBacklog = 0;
    for (auto* f : active) {
        totalMs += f->totalMs;
        maxMs = std::max(maxMs, f->totalMs);
        minMs = std::min(minMs, f->totalMs);
        totalSwpFree += f->lodSwapFreeMs;
        totalLateUpload += f->lateUploadMs;
        totalVisualReady += f->visualReadyMs;
        totalFreeQueued += f->lodSwapFreeQueuedCount;
        totalFreeDrained += f->lodSwapFreeDrainedCount;
        maxFreeBacklog = std::max(maxFreeBacklog, f->lodSwapFreeBacklog);
        lastFreeBacklog = f->lodSwapFreeBacklog;
        if (f->totalMs >= spikeThresholdMs) {
            ++spikeCount;
            spikeTotal += f->totalMs;
        }
    }

    ss << "=== FINALIZE DIAGNOSTICS REPORT ===\n";
    ss << "Total frames: " << count << " | Active (non-zero): " << active.size() << "\n";
    ss << "Spike threshold: " << spikeThresholdMs << " ms\n";
    if (!active.empty()) {
        ss << "Avg finalize:    " << (totalMs / active.size()) << " ms\n";
        ss << "Min finalize:    " << minMs << " ms\n";
        ss << "Max finalize:    " << maxMs << " ms\n";
        ss << "Avg SwpFree:     " << (totalSwpFree / active.size()) << " ms\n";
        ss << "Avg LateUpload:  " << (totalLateUpload / active.size()) << " ms\n";
        ss << "Avg VisualReady: " << (totalVisualReady / active.size()) << " ms\n";
        if (totalFreeQueued > 0 || totalFreeDrained > 0 || maxFreeBacklog > 0) {
            ss << "LOD free queue:  queued " << totalFreeQueued
               << " | drained " << totalFreeDrained
               << " | max backlog " << maxFreeBacklog
               << " | last backlog " << lastFreeBacklog << "\n";
        }
    }
    ss << "Spikes (>=" << spikeThresholdMs << "ms): " << spikeCount << " / " << active.size()
       << " (" << (active.empty() ? 0.0f : 100.0f * spikeCount / active.size()) << "%)\n";
    if (spikeCount > 0) {
        ss << "Avg spike:       " << (spikeTotal / spikeCount) << " ms\n";
    }
    ss << "\n";

    int lateUploadSpikeCount = 0;
    int swapFreeSpikeCount = 0;
    int visualSpikeCount = 0;
    int lateFinalizeSpikeCount = 0;
    int lockStateSpikeCount = 0;
    int mixedSpikeCount = 0;
    for (auto* f : active) {
        if (f->totalMs < spikeThresholdMs) continue;

        const float lockStateMs =
            f->regLockHeldMs +
            f->regLockWaitMs +
            f->stateMapLockMs +
            f->readySetLockMs +
            f->notifyMs +
            f->clearPendingMs;
        float bestMs = f->lateUploadMs;
        int* bestCount = &lateUploadSpikeCount;
        if (f->lodSwapFreeMs > bestMs) {
            bestMs = f->lodSwapFreeMs;
            bestCount = &swapFreeSpikeCount;
        }
        if (f->visualReadyMs > bestMs) {
            bestMs = f->visualReadyMs;
            bestCount = &visualSpikeCount;
        }
        if (f->lateFinalizeMs > bestMs) {
            bestMs = f->lateFinalizeMs;
            bestCount = &lateFinalizeSpikeCount;
        }
        if (lockStateMs > bestMs) {
            bestMs = lockStateMs;
            bestCount = &lockStateSpikeCount;
        }

        if (bestMs >= spikeThresholdMs * 0.35f) {
            ++(*bestCount);
        } else {
            ++mixedSpikeCount;
        }
    }

    if (spikeCount > 0) {
        ss << "=== SPIKE CAUSE SUMMARY ===\n";
        ss << "Late upload:     " << lateUploadSpikeCount << "\n";
        ss << "LOD swap frees:  " << swapFreeSpikeCount << "\n";
        ss << "Visual ready:    " << visualSpikeCount << "\n";
        ss << "Late finalize:   " << lateFinalizeSpikeCount << "\n";
        ss << "Locks/state:     " << lockStateSpikeCount << "\n";
        ss << "Mixed/other:     " << mixedSpikeCount << "\n\n";
    }

    // Condensed spike table: only show columns with non-negligible values
    int printed = 0;
    ss << "=== SPIKE DETAILS (newest first, max 50) ===\n";
    ss << "Frame     | Total   | FnlCnt | SwpEnt | LateUp | LateFin | Visual | CollRf | Topo  | RegH  | RegW  | State | Ready | SwpFree\n";
    ss << "----------|---------|--------|--------|--------|---------|--------|--------|-------|-------|-------|-------|-------|--------\n";

    for (int i = static_cast<int>(active.size()) - 1; i >= 0 && printed < 50; --i) {
        auto* f = active[i];
        if (f->totalMs < spikeThresholdMs) continue;

        ss << std::setw(9) << f->frameNumber << " | "
           << std::setw(7) << f->totalMs << " | "
           << std::setw(6) << f->finalizeCount << " | "
           << std::setw(6) << f->lodSwapEntityCount << " | "
           << std::setw(6) << f->lateUploadMs << " | "
           << std::setw(7) << f->lateFinalizeMs << " | "
           << std::setw(6) << f->visualReadyMs << " | "
           << std::setw(6) << f->collisionRefreshMs << " | "
           << std::setw(5) << f->topologyRecordMs << " | "
           << std::setw(5) << f->regLockHeldMs << " | "
           << std::setw(5) << f->regLockWaitMs << " | "
           << std::setw(5) << f->stateMapLockMs << " | "
           << std::setw(5) << f->readySetLockMs << " | "
           << std::setw(7) << f->lodSwapFreeMs << "\n";
        ++printed;
    }

    if (printed == 0) {
        ss << "(no spikes above threshold)\n";
    }

    // Recent active frames from the window (newest first).  The cause summary
    // above carries the whole-window picture without flooding clipboard logs.
    static constexpr int MAX_ACTIVE_ROWS = 120;
    int activePrinted = 0;
    ss << "\n=== RECENT ACTIVE FRAMES (newest first, max " << MAX_ACTIVE_ROWS
       << " of " << active.size() << ") ===\n";
    ss << "Frame     | Total   | FnlCnt | SwpEnt | LateUp | LateFin | Visual | CollRf | Topo  | RegH  | RegW  | State | Ready | SwpFree\n";
    ss << "----------|---------|--------|--------|--------|---------|--------|--------|-------|-------|-------|-------|-------|--------\n";

    for (int i = static_cast<int>(active.size()) - 1; i >= 0 && activePrinted < MAX_ACTIVE_ROWS; --i) {
        auto* f = active[i];
        const char* marker = (f->totalMs >= spikeThresholdMs) ? "*" : " ";
        ss << marker
           << std::setw(8) << f->frameNumber << " | "
           << std::setw(7) << f->totalMs << " | "
           << std::setw(6) << f->finalizeCount << " | "
           << std::setw(6) << f->lodSwapEntityCount << " | "
           << std::setw(6) << f->lateUploadMs << " | "
           << std::setw(7) << f->lateFinalizeMs << " | "
           << std::setw(6) << f->visualReadyMs << " | "
           << std::setw(6) << f->collisionRefreshMs << " | "
           << std::setw(5) << f->topologyRecordMs << " | "
           << std::setw(5) << f->regLockHeldMs << " | "
           << std::setw(5) << f->regLockWaitMs << " | "
           << std::setw(5) << f->stateMapLockMs << " | "
           << std::setw(5) << f->readySetLockMs << " | "
           << std::setw(7) << f->lodSwapFreeMs << "\n";
        ++activePrinted;
    }
    if (static_cast<int>(active.size()) > activePrinted) {
        ss << "... " << (active.size() - static_cast<size_t>(activePrinted))
           << " older active frames omitted; see spike cause summary above ...\n";
    }

    ss << "\nColumn legend:\n";
    ss << "  Total    = total finalize+LODswaps time\n";
    ss << "  FnlCnt   = entities finalized this frame\n";
    ss << "  SwpEnt   = LOD swap entities swapped\n";
    ss << "  LateUp   = late upload catch-up inside the finalize window\n";
    ss << "  LateFin  = second finalize pass after late uploads (overlaps other finalize breakdown columns)\n";
    ss << "  Visual   = noteChunkVisualReady history/attribution/hole-tracker work\n";
    ss << "  CollRf   = edited-collision refresh attempts during finalize\n";
    ss << "  Topo     = mesh topology change recording\n";
    ss << "  RegH     = finalize registry lock held\n";
    ss << "  RegW     = finalize registry lock wait/contention\n";
    ss << "  State    = chunk state map lock/work\n";
    ss << "  Ready    = ready chunk set lock/work\n";
    ss << "  SwpFree  = GPU culling slot frees + budgeted old mesh buffer frees\n";
    ss << "  * = spike frame\n";

    return ss.str();
}

````

## src\world\lod\WorldLODConfig.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Owns World LOD/data-LOD configuration changes and per-band mesh reload helpers.
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


void World::setTerrainTypeForLOD(int lodLevel, TerrainType type) {
    if (lodLevel < 0 || lodLevel >= MAX_LOD_LEVELS) return;
    TerrainType oldType = m_terrainTypePerLOD[lodLevel];
    if (oldType == type) return;
    
    m_terrainTypePerLOD[lodLevel] = type;
    std::cout << "[World] LOD " << lodLevel << " terrain type changed to: " 
              << (type == TerrainType::DCCM ? "DCCM" : "Voxel") << std::endl;
    
    // ---------------------------------------------------------------
    // FULL PIPELINE DRAIN before releasing/reloading meshes.
    // Without this, in-flight uploads and LOD batches from the OLD
    // terrain type can arrive AFTER releaseMeshesForLOD clears the
    // handles, installing stale mesh data and leaking GPU culling
    // slots.  This matches what applyLODChangesIncrementally does.
    // ---------------------------------------------------------------
    m_lifecycleManager.pauseAndDrain();
    auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
        [](const glm::ivec3&) { return true; });
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
    m_uploadSystem.clearQueue();
    
    // Cancel all in-flight LOD transition batches and clean up their
    // PendingMeshHandle GPU resources (buffers + culling slots).
    auto cancelledBatchIds = m_chunkManager->cancelAllBatches();
    if (!cancelledBatchIds.empty()) {
        std::string reason = "DataLodSwitchCancelledBatches count=" +
            std::to_string(cancelledBatchIds.size());
        noteChunkVisualError(
            nullptr,
            lodLevel,
            "LODTransition",
            reason.c_str(),
            0,
            static_cast<uint32_t>(cancelledBatchIds.size()),
            0);
        cleanupStalePendingMeshHandles();
    }
    // Drop stale remesh queue entries carrying cancelled batch IDs.
    m_lodSystem.clearAllPending();
    m_pendingLODRemeshes.clear();
    
    // Purge pending LOD remeshes for this LOD level so stale remesh
    // requests don't re-queue jobs for the old terrain type.
    m_pendingLODRemeshes.erase(
        std::remove_if(m_pendingLODRemeshes.begin(), m_pendingLODRemeshes.end(),
            [lodLevel](const ChunkManager::ChunkRemeshRequest& req) {
                return req.newLOD == lodLevel;
            }),
        m_pendingLODRemeshes.end());
    
    // Regenerate chunks at this LOD level - release old meshes and reload with new terrain type
    releaseMeshesForLOD(lodLevel);
    reloadMeshesForLOD(lodLevel);
    
    // Resume background thread
    m_lifecycleManager.resume();
}


void World::setTerrainTypesForStartup(const std::array<TerrainType, MAX_LOD_LEVELS>& types) {
    m_terrainTypePerLOD = types;
    std::cout << "[World] Startup terrain types:";
    for (int lod = 0; lod < MAX_LOD_LEVELS; ++lod) {
        std::cout << " LOD" << lod << "="
                  << (m_terrainTypePerLOD[lod] == TerrainType::DCCM ? "DCCM" : "Voxel");
    }
    std::cout << std::endl;
}


void World::setDataLODForBand(int band, int dataLOD) {
    if (band < 0 || band >= MAX_LOD_LEVELS) return;
    if (dataLOD < 0 || dataLOD >= MAX_LOD_LEVELS) return;
    int oldDataLOD = m_dataLODPerBand[band];
    if (oldDataLOD == dataLOD) return;

    m_dataLODPerBand[band] = dataLOD;
    std::cout << "[World] Band " << band << " data LOD changed: "
              << oldDataLOD << " -> " << dataLOD << std::endl;

    // Only Voxel bands use data LOD override; DCCM always uses LOD 0
    if (m_terrainTypePerLOD[band] == TerrainType::DCCM) return;

    // Same drain/reload cycle as setTerrainTypeForLOD
    m_lifecycleManager.pauseAndDrain();
    auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
        [](const glm::ivec3&) { return true; });
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
    m_uploadSystem.clearQueue();

    auto cancelledBatchIds = m_chunkManager->cancelAllBatches();
    if (!cancelledBatchIds.empty()) {
        std::string reason = "TerrainTypeSwitchCancelledBatches count=" +
            std::to_string(cancelledBatchIds.size());
        noteChunkVisualError(
            nullptr,
            band,
            "LODTransition",
            reason.c_str(),
            0,
            static_cast<uint32_t>(cancelledBatchIds.size()),
            0);
        cleanupStalePendingMeshHandles();
    }
    m_lodSystem.clearAllPending();
    m_pendingLODRemeshes.clear();

    releaseMeshesForLOD(band);
    reloadMeshesForLOD(band);

    m_lifecycleManager.resume();
}


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
                if (meshHandle.vb.isValid() || meshHandle.ib.isValid()) {
                    chunksToRelease.push_back(entity);
                    totalVertices += meshHandle.vb.size;
                    totalIndices += meshHandle.ib.size;
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
                if (meshHandle.vb.isValid()) vbSlices.push_back(meshHandle.vb);
                if (meshHandle.ib.isValid()) ibSlices.push_back(meshHandle.ib);
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

````

## src\world\snapshot\WorldSnapshotIdentity.cpp

Description: No CC-DESC found.

````cpp
// GPT-DESC: Updates World display identity from the active snapshot selection.
#include "WorldSnapshotInternal.h"

using namespace WorldSnapshotInternal;

void World::updateWorldIdentityFromActiveSnapshot() {
    if (m_activeSnapshotIndex >= 0 &&
        m_activeSnapshotIndex < static_cast<int>(m_snapshotInfos.size())) {
        const SnapshotInfo& info = m_snapshotInfos[static_cast<size_t>(m_activeSnapshotIndex)];
        m_worldName = info.displayName;
        m_worldGenerationDate = info.createdAt;
    }
}

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
