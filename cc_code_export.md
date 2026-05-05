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
- If this export contains Hash hints, copy the matching HASH: value into function/replace_region patch headers. If no hash hint is present, omit HASH:.
- If critical context is missing, ask only for the exact missing files/functions, one path per line, ending with END.

Default CMake build, when applicable: cmake --build build --config Release -j


## include\core\engine\Engine.h

Description: No CC-DESC found. C++ class 'Engine'.

````cpp
#pragma once

#include <vulkan/vulkan.h>
#include <GLFW/glfw3.h>
#include <glm/glm.hpp>
#include <vector>
#include <array>
#include <memory>
#include <unordered_map>
#include <string>
#include "core/engine/EngineTypes.h"
#include "vulkan/VulkanContext.h"
#include "vulkan/Buffers.h"
#include "vulkan/Swapchain.h"
#include "vulkan/Pipeline.h"
#include "vulkan/Sync.h"
#include "rendering/common/Renderer.h"
#include "vulkan/FrameGraph.h"
#include "input/InputManager.h"
#include "core/EngineImGui.h"
#include "input/CameraController.h"
#include "core/TimeManager.h"
#include "ui/cursor/CursorManager.h"
#include "rendering/common/Mesh.h"
#include "vulkan/UploadArena.h"
#include "vulkan/BufferSuballocator.h"
#include "rendering/common/VulkanHelpers.h"
#include "rendering/sky/CloudSystem.h"
#include "rendering/sky/CelestialSystem.h"
#include "rendering/lighting/LightGlowSystem.h"
#include "rendering/lighting/ShadowSystem.h"
#include "rendering/lighting/ClusteredLightingSystem.h"
#include "rendering/sky/StarSystem.h"
#include "rendering/sky/SkySystem.h"
#include "rendering/culling/GPUCullingSystem.h"
#include "rendering/culling/HiZPyramid.h"
#include "rendering/tjunctionfix/TJunctionFixSystem.h"
#include "rendering/postprocess/RetroPixelPassSystem.h"
#include "rendering/hotreload/ShaderHotReloadService.h"
#include "rendering/common/ParallelCommandRecorder.h"
#include "ui/debug_menu/world/MinimapCullingReadback.h"
#include "world/World.h"
#include "rendering/lighting/LightingSettings.h"
#include "rendering/lighting/AOSettings.h"
#include "rendering/lighting/LightPulsePreset.h"
#include "core/GameplayWindow.h"
#include "world/config/ObjectManager.h"
#include "physics/PhysicsWorld.h"
#include "player/PlayerController.h"
#include "player/PlayerCamera.h"

class Engine {
public:
    Engine(int width = 800,
           int height = 600,
           const char* title = "Vulkan Engine",
           bool gameplayOnlyMode = false,
           bool perfMode = false,
           StartupTerrainPreset startupTerrainPreset = StartupTerrainPreset::Default);
    ~Engine();

    void run();

    void saveSettings();
    void loadSettings();
    void resetSettings();

    // Phase E1 instrumentation: inspect currently scheduled frame passes
    const std::vector<FramePassDescriptor>& getFramePassDescriptors() const { return m_framePassDescriptors; }

private:
    void initWindow();
    void initVulkan();
    void mainLoop();
    void cleanup();

    // Vulkan setup helpers
    void createInstance();
    void setupDebugMessenger(); // GPT_CHANGE: Debug utilities setup
    void pickPhysicalDevice();
    void createLogicalDevice();
    void createSurface();
    void createSwapchain();
    void createImageViews();
    void createRenderPass();
    void createPipelineCache(); // GPT_CHANGE: Pipeline cache for faster subsequent builds
    void createGraphicsPipeline();
    void createFramebuffers();
    void createCommandPool();
    void createCommandBuffers();
    void createSyncObjects();
    void createTimestampQueryPool();
    void destroyTimestampQueryPool();
    void createDescriptorSetLayout();
    void createVertexBuffer();
    void createIndexBuffer();
    void createUniformBuffers();
    void createDescriptorPool();
    void createDescriptorSets();
    void refreshShadowDescriptorsForImage(uint32_t imageIndex);
    void bindGpuVisibleOriginsToSlot1();  // Phase D: one-time write of binding 1 array element 1
    void createDepthResources(); // GPT_CHANGE: Added depth buffer creation
    void createIndirectBuffer(); // PHASE B7: Indirect draw buffer
    void recreateSwapchain(); // GPT_CHANGE: Swapchain recreation for resize/out-of-date
    void cleanupSwapchain(); // GPT_CHANGE: Helper to cleanup swapchain-dependent resources
    void buildFramePassDescriptors(); // Phase E1: derive render pass descriptors
    void compileFrameGraph(); // Phase E2: build per-frame graph
    void prepareFramePassBarriers(const FrameGraphCompiledPass& pass,
                                  uint32_t imageIndex,
                                  std::vector<VkImageMemoryBarrier2>& imageBarriers,
                                  std::vector<VkBufferMemoryBarrier2>& bufferBarriers);
    void finalizeFramePassResources(const FrameGraphCompiledPass& pass);
    void recordVoxelOpaquePass(VkCommandBuffer cmd, uint32_t imageIndex, uint32_t currentFrame, const glm::mat4& view, const glm::mat4& proj, const VkRect2D& gameplayRect, bool useDepthPrepass);

    // Rendering helpers
    void drawFrame();
    void recordCommandBuffer(uint32_t imageIndex, uint32_t frameIndex, const glm::mat4& view, const glm::mat4& proj, const VkRect2D& gameplayRect);
    void collectTimestampResults(uint32_t imageIndex);

    // ── EngineDepthPrePass.cpp ──────────────────────────────────────────
    // Records initial GPU frustum/temporal-HiZ culling into cmd.
    // Sets useCurrentFrameHiZ=true when the same-frame depth prepass path is taken.
    void recordInitialGPUCulling(VkCommandBuffer cmd, uint32_t imageIndex,
                                  const glm::mat4& viewProj,
                                  float viewportOffsetX, float viewportOffsetY,
                                  float viewportScaleX, float viewportScaleY,
                                  bool temporalHiZViable,
                                  bool& usedTemporalHiZ,
                                  float& cpuMs);
    // Records depth pre-pass render pass, Hi-Z pyramid build, and second (occluded) cull.
    void recordDepthPrePassAndHiZ(VkCommandBuffer cmd, uint32_t imageIndex,
                                   const glm::mat4& viewProj,
                                   const VkRect2D& gameplayRect,
                                   float& cpuPrepassMs, float& cpuHiZMs, float& cpuFinalMs);
    // Records temporal Hi-Z pyramid build from end-of-frame depth (non prepass path).
    void recordPostRenderHiZBuild(VkCommandBuffer cmd);

    // ── EngineShadowPass.cpp ────────────────────────────────────────────
    // Selects active shadow-casting lights and records shadow render passes into cmd.
    void recordShadowRenderPasses(VkCommandBuffer cmd, uint32_t imageIndex);
    // Updates shadow system for the frame (light budget selection).
    void updateShadowsForFrame(uint32_t imageIndex,
                                const std::vector<PointLight>& transientLights,
                                const glm::vec3& camPos, const glm::vec3& camFront);

    // ── EngineGameplayRendering.cpp ─────────────────────────────────────
    // Records the ImGui-only render pass for the main swapchain when gameplay is separated.
    void recordGameplayWindowUIPass(VkCommandBuffer cmd, uint32_t imageIndex);
    // Polls gameplay window events and acquires its next image.
    // Returns false when caller (drawFrame) should early-return (window was closed).
    bool pollAndPrepareGameplayWindow();
    // Renders ImGui/tool overlay into the gameplay window framebuffer (separated mode).
    void recordGameplayOverlayFrame(bool gameplayOverlayRequested);
    // Runs the full ImGui frame + tool-update phase for a drawFrame call.
    void runImGuiFrameAndTools(bool gameplayDetached,
                               int gameplayViewportW, int gameplayViewportH,
                               float gameplayViewportOffsetX, float gameplayViewportOffsetY,
                               bool gameplayStatsStripVisible,
                               std::vector<PointLight>& outTransientLights,
                               std::vector<glm::vec4>& outTransientPulseData);
    
    // GPU culling helpers
    void toggleGPUCulling();  // Toggle between CPU and GPU culling
    void beginGModeGeometryDiffCapture(bool beforeGpuMode, bool afterGpuMode);
    void updateGModeGeometryDiffCapture();
    void updateCpuVisibleChunkSnapshot(const glm::vec4* chunkOrigins, uint32_t count);
    void updateGpuVisibleChunkSnapshot();
    std::vector<GPUCullingSystem::GModeGeometryDiffRecord> buildGModeGeometryDiffRecords(
        const std::vector<glm::ivec3>& beforeVisible,
        const std::vector<glm::ivec3>& afterVisible) const;

    void toggleFullscreen();
    void setGameplaySeparated(bool separate);
    void startChunkGeneration();
    void initDebugWiring();  // Debug UI wiring (extracted from initVulkan)
    void initShaderHotReload();
    void registerShaderUsageManifest();
    void processShaderHotReload();
    void rebuildCullingShaders();
    void rebuildShadowSystem();
    void applyStartupTerrainPreset();
    void updatePerfOverlayStats(const InGameDebug::DebugInfo::CPUBreakdown& breakdown);
    void recordFrameBottleneckSample(const InGameDebug::DebugInfo::CPUBreakdown& breakdown);
    std::string generateFrameBottleneckReport() const;
    void renderPerfOverlay(float gameplayViewportOffsetX,
                           float gameplayViewportOffsetY,
                           int gameplayViewportW,
                           int gameplayViewportH);
    bool shouldSamplePerfOverlayDrawCount() const;
    void syncGameplayTJunctionFix(bool forceRecreate = false);
    void syncGameplayPixelPass(bool forceRecreate = false);
    void syncHiZTarget(bool forceRecreate = false);
    const char* getStartupTerrainPresetName() const;
    const char* getStartupTerrainPresetSummary() const;

    // Vulkan setup methods now delegate to EngineVulkanSetup namespace

private:
    int m_width;
    int m_height;
    const char* m_title;
    GLFWwindow* m_window;

    VkInstance m_instance{VK_NULL_HANDLE};
    VkDebugUtilsMessengerEXT m_debugMessenger{VK_NULL_HANDLE}; // GPT_CHANGE: Debug messenger
    VkPhysicalDevice m_physicalDevice{VK_NULL_HANDLE};
    VkPhysicalDeviceProperties m_deviceProperties{};
    VulkanContext::DeviceCapabilities m_deviceCapabilities{};
    VkDevice m_device{VK_NULL_HANDLE};
    VkQueue m_graphicsQueue{VK_NULL_HANDLE};
    VkQueue m_presentQueue{VK_NULL_HANDLE};
    VkSurfaceKHR m_surface{VK_NULL_HANDLE};
    VkSwapchainKHR m_swapchain{VK_NULL_HANDLE};
    std::vector<VkImage> m_swapchainImages;
    VkFormat m_swapchainImageFormat;
    VkExtent2D m_swapchainExtent{};
    std::vector<VkImageView> m_swapchainImageViews;
    VkRenderPass m_renderPass{VK_NULL_HANDLE};
    VkRenderPass m_renderPassDepthPrepass{VK_NULL_HANDLE};
    VkRenderPass m_renderPassDepthLoad{VK_NULL_HANDLE};
    VkRenderPass m_uiRenderPass{VK_NULL_HANDLE};  // UI-only pass with LOAD_OP_LOAD for SVO overlay
    
    VkPipelineLayout m_pipelineLayout{VK_NULL_HANDLE};
    VkPipeline m_graphicsPipeline{VK_NULL_HANDLE};
    VkPipeline m_graphicsPipelineDepthLoad{VK_NULL_HANDLE};
    VkPipeline m_depthPrePassPipeline{VK_NULL_HANDLE};
    VkPipelineCache m_pipelineCache{VK_NULL_HANDLE}; // GPT_CHANGE: Pipeline cache for faster builds
    
    // DCCM terrain pipeline (same descriptor set layout, different shaders)
    VkPipeline m_dccmPipeline{VK_NULL_HANDLE};
    VkPipelineLayout m_dccmPipelineLayout{VK_NULL_HANDLE};
    VkPipeline m_dccmPipelineDepthLoad{VK_NULL_HANDLE};
    // Per-LOD terrain type: true = any LOD uses that type (computed from World config)
    bool m_anyLODUsesVoxel = true;
    bool m_anyLODUsesDCCM = false;
    std::vector<VkFramebuffer> m_swapchainFramebuffers;
    VkCommandPool m_commandPool{VK_NULL_HANDLE};
    std::vector<VkCommandBuffer> m_commandBuffers; // Per-swapchain-image command buffers
    ParallelCommandRecorder m_parallelRecorder; // Per-slot secondary command pools/buffers for parallel recording
    std::vector<VkFence> m_imageInFlight; // GPT_CHANGE: Track which fence is using each swapchain image

    // GPT_CHANGE: Per-frame synchronization (3 frames in flight)
    static constexpr int MAX_FRAMES_IN_FLIGHT = 3;
    std::vector<PerFrame> m_frames;
    size_t m_currentFrame = 0;

    // GPT_CHANGE: Small allocator members
    std::array<UploadArena, MAX_FRAMES_IN_FLIGHT> m_uploadArenas;
    BufferSuballocator m_vbAllocator;
    BufferSuballocator m_ibAllocator;
    ResourceUploader m_uploader;
    
    // C3.2: Per-frame upload command buffers for batched transfers (no CPU wait)
    std::array<VkCommandBuffer, MAX_FRAMES_IN_FLIGHT> m_uploadCmds;
    
    // C3.2: Timeline semaphore for GPU-side upload ordering
    VkSemaphore m_uploadTimeline{VK_NULL_HANDLE};
    uint64_t m_uploadTimelineValue = 0;
    // Truly signaled (CPU-visible) value — used by world.update for
    // processSoloPendingSwaps where we MUST know the GPU has actually finished.
    uint64_t m_frameVisibleUploadTimelineValue = 0;
    // "Will be signaled by the time the graphics CB executes on GPU" value.
    // Equals this frame's batchValue. Used for cull dispatch / CPU draw gating
    // because the graphics submit waits on m_uploadTimeline at m_uploadTimelineValue,
    // so all currently-recorded slot writes are safe to read by execute time.
    // Using lastSignaled here caused a 1-frame post-edit invisibility flicker
    // under GPU culling (G mode): the slot's gpuReadyValue=batchValue was higher
    // than the lagging lastSignaled, so frustum_filter.comp culled the chunk.
    // CPU rendering didn't flicker because the OLD MeshHandle stayed live until
    // processSoloPendingSwaps swapped it.
    uint64_t m_frameCullingTimelineValue = 0;

    // Hi-Z timeline semaphore for GPU-side Hi-Z build ordering
    VkSemaphore m_hiZTimeline{VK_NULL_HANDLE};
    uint64_t m_hiZTimelineValue = 0;
    
    // Cached Vulkan extension function pointer (resolved once in initVulkan)
    PFN_vkGetSemaphoreCounterValueKHR m_vkGetSemaphoreCounterValueKHR{nullptr};

    // GPU timing instrumentation
    VkQueryPool m_timestampQueryPool{VK_NULL_HANDLE};
    float m_timestampPeriod = 0.0f; // nanoseconds per tick
    double m_lastGpuFrameMs = 0.0;
    double m_lastUncappedFps = 0.0;
    double m_lastActualFrameMs = 0.0;
    double m_lastScreenFrameMs = 0.0;
    double m_lastGameplayPresentTimestamp = 0.0;
    double m_lastCpuFrameMs = 0.0;
    double m_lastCpuWorkMs = 0.0;
    
    // drawFrame sub-timers (VSync hiding inside renderMs)
    float m_presentWaitMs = 0.0f;  // vkAcquireNextImageKHR + per-image vkWaitForFences
    float m_cmdRecordMs = 0.0f;    // command recording + ImGui + uniforms
    float m_imguiMs = 0.0f;        // ImGui frame + debug overlay
    float m_imguiInterfaceMs = 0.0f;
    float m_imguiVramMs = 0.0f;
    float m_imguiCloudMs = 0.0f;
    float m_imguiMinimapMs = 0.0f;
    float m_imguiPerfMs = 0.0f;
    float m_imguiToolMs = 0.0f;
    float m_imguiEndFrameMs = 0.0f;
    bool  m_imguiFrameActive = false; // Whether ImGui frame was begun this frame
    bool  m_gameplayOverlayFrameActive = false;

    // Culling GPU timing (updated per frame from timestamp queries)
    // Query indices per image:
    // 0=frameStart, 1=afterInitialCull, 2=depthPrepassStart, 3=depthPrepassEnd,
    // 4=afterHiZBuild, 5=afterFinalCull, 6=terrainStart, 7=terrainEnd, 8=frameEnd
    static constexpr uint32_t TIMESTAMPS_PER_IMAGE = 9;
    double m_cullingDispatchMs = 0.0;
    double m_cullingTotalMs = 0.0;
    double m_terrainLightingMs = 0.0;
    double m_gpuInitialCullMs = 0.0;
    double m_gpuDepthPrepassMs = 0.0;
    double m_gpuHiZBuildMs = 0.0;
    double m_gpuFinalCullMs = 0.0;
    double m_gpuHiZIncrementalMs = 0.0;
    bool m_lastCollectedCurrentFrameHiZ = false;
    bool m_lastCollectedTemporalHiZ = false;
    std::vector<HiZPyramid::DiagnosticsMode> m_hiZTimingModeByImage;
    float m_cpuInitialCullRecordMs = 0.0f;
    float m_cpuDepthPrepassRecordMs = 0.0f;
    float m_cpuHiZBuildRecordMs = 0.0f;
    float m_cpuFinalCullRecordMs = 0.0f;
    float m_cpuHiZIncrementalRecordMs = 0.0f;

    // GPT_CHANGE: Cube buffer slices instead of raw buffers
    BufferSlice m_cubeVB;
    BufferSlice m_cubeIB;
    
    // PHASE B7: Multi-Draw Indirect buffer
    static constexpr uint32_t MAX_INDIRECT_DRAWS = 65536;
    VkBuffer m_indirectBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_indirectMemory{VK_NULL_HANDLE};
    uint32_t m_indirectDrawCount = 0;  // Actual number of draws this frame
    
    // Chunk origins storage buffer (for chunk-relative vertices)
    VkBuffer m_chunkOriginsBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_chunkOriginsMemory{VK_NULL_HANDLE};

    // Geometry
    Mesh m_cubeMesh;
    
    // World and ECS
    World m_world;
    
    // Physics and player systems
    Physics::PhysicsWorld m_physics;
    Player::PlayerController m_player;
    Player::PlayerCamera m_playerCamera;
    
    // Input and camera controllers (de-godified from Engine.cpp)
    EngineInput m_input;
    CameraController m_camera;
    
    // ImGui system
    EngineImGui m_imgui;

    // OS cursor image and hotspot configuration
    CursorManager m_cursorManager;
    
    // Cloud rendering system
    CloudSystem m_cloudSystem;
    
    // Celestial rendering system (sun and moon)
    CelestialSystem m_celestialSystem;
    
    // Light glow rendering system (point light halos)
    LightGlowSystem m_lightGlowSystem;
    
    // Realtime point-light shadow system (terrain + placed cube casters)
    ShadowSystem m_shadowSystem;
    
    // Object manager (tracks all placed objects by chunk)
    ObjectManager m_objectManager;
    
    // Light pulse preset library (built-in + user presets)
    LightPulsePresetLibrary m_pulsePresets;
    
    // Star field rendering system (pixelated twinkling stars)
    StarSystem m_starSystem;
    
    // Sky gradient rendering system (Sega-style pixel art sky)
    SkySystem m_skySystem;
    
    // GPU-driven frustum culling system (replaces CPU gatherDrawCommands when enabled)
    GPUCullingSystem m_gpuCulling;
    bool m_gpuCullingEnabled = true;  // Toggle between CPU/GPU culling (default GPU)
    uint32_t m_gpuCullingChunkCount = 0;  // Number of chunks uploaded for GPU culling
    
    // Hi-Z depth pyramid for occlusion culling
    HiZPyramid m_hiZPyramid;
    glm::mat4 m_prevViewProj{0.0f};  // Previous frame's viewProj for Hi-Z reprojection
    glm::vec3 m_prevHiZCameraPos{0.0f};
    glm::vec3 m_prevHiZCameraFront{0.0f, 0.0f, -1.0f};
    glm::vec4 m_prevHiZViewportUvTransform{0.0f};
    bool m_prevHiZFrameValid{false};
    float m_lastFrameRotationDeg = 0.0f;
    float m_lastFrameTranslation = 0.0f;
    
    // T-junction crack fix system (post-process to fix greedy meshing artifacts)
    TJunctionFixSystem m_tjunctionFix;
    TJunctionFixSystem m_gameplayTJunctionFix;

    // Retro pixel pass system (post-process pixelation)
    RetroPixelPassSystem m_pixelPass;
    RetroPixelPassSystem m_gameplayPixelPass;
    uint64_t m_gameplayPixelPassSwapchainGeneration = 0;

    // Runtime shader editing + hot reload
    ShaderHotReloadService m_shaderHotReload;
    
    // Minimap culling readback (debug feature for visualizing GPU culling results)
    MinimapCullingReadback m_minimapReadback;

    // Last known visible chunk snapshots per culling mode (sorted, unique).
    std::vector<glm::ivec3> m_lastCpuVisibleChunks;
    std::vector<glm::ivec3> m_lastGpuVisibleChunks;
    uint64_t m_lastCpuVisibleSerial{0};
    uint64_t m_lastGpuVisibleSerial{0};

    struct GModeGeometryDiffCaptureState {
        bool active = false;
        bool beforeGpuMode = false;
        bool afterGpuMode = false;
        uint64_t toggleSerial = 0;
        uint64_t targetAfterSerial = 0;
        uint32_t timeoutFrames = 0;
        std::vector<glm::ivec3> beforeVisible;
    };
    GModeGeometryDiffCaptureState m_gModeGeometryDiffCapture;
    uint64_t m_gModeGeometryToggleSerial{0};
    
    // Physics body IDs for placed cubes (objectId -> Jolt BodyID)
    // Enables raycasting against placed cubes for stacking
    std::unordered_map<uint32_t, JPH::BodyID> m_cubePhysicsBodies;
    uint64_t m_lastTerrainBoxRevisionSynced{0};
    
    // Uniform buffers
    std::vector<VkBuffer> m_uniformBuffers;
    std::vector<VkDeviceMemory> m_uniformBuffersMemory;
    std::vector<void*> m_uniformMapped; // GPT_CHANGE: Persistent mapped pointers for UBOs
    
    // Lighting system buffers (SSBO)
    std::vector<VkBuffer> m_lightingBuffers;
    std::vector<VkDeviceMemory> m_lightingBuffersMemory;
    std::vector<void*> m_lightingMapped;
    std::unique_ptr<LightingSettings::GPULightingData> m_lightingStaging;  // Heap staging to avoid 200KB stack alloc
    
    // Clustered lighting (per-tile light bitmasks)
    ClusteredLightingSystem m_clusteredLighting;
    
    // Camera data buffers
    std::vector<VkBuffer> m_cameraBuffers;
    std::vector<VkDeviceMemory> m_cameraBuffersMemory;
    std::vector<void*> m_cameraMapped;
    
    // AO settings buffers
    std::vector<VkBuffer> m_aoBuffers;
    std::vector<VkDeviceMemory> m_aoBuffersMemory;
    std::vector<void*> m_aoMapped;

    // Sparse texture-material overlay. Texture paint is shader data keyed by
    // world voxel face, so material edits do not remesh terrain or affect
    // shadow-casting geometry.
    struct MaterialOverlayHeaderGPU {
        uint32_t capacityMask{0};
        uint32_t count{0};
        uint32_t maxProbe{0};
        uint32_t _pad{0};
    };
    struct MaterialOverlayCellGPU {
        int32_t x{0};
        int32_t y{0};
        int32_t z{0};
        uint32_t face{0};
        uint32_t material{0};
    };
    static_assert(sizeof(MaterialOverlayCellGPU) == 20, "MaterialOverlayCellGPU must match GLSL std430 layout");
    std::vector<VkBuffer> m_materialOverlayBuffers;
    std::vector<VkDeviceMemory> m_materialOverlayBuffersMemory;
    std::vector<void*> m_materialOverlayMapped;
    VkDeviceSize m_materialOverlayBufferSize{0};
    std::vector<MaterialOverlayCellGPU> m_materialOverlayTable;
    uint32_t m_materialOverlayCapacity{0};
    uint32_t m_materialOverlayCount{0};
    uint32_t m_materialOverlayMaxProbe{0};
    size_t m_materialOverlayLastGeneration{0};
    bool m_materialOverlayNeedsRebuild{true};
    std::vector<uint8_t> m_materialOverlayImageDirty;
    // Per-image queue of slot indices changed since each image was last synced.
    // dirty=1 (full re-upload) supersedes any queued slots for that image.
    std::vector<std::vector<uint32_t>> m_materialOverlayImageDirtySlots;

    VkDescriptorSetLayout m_descriptorSetLayout{VK_NULL_HANDLE};
    VkDescriptorPool m_descriptorPool{VK_NULL_HANDLE};
    std::vector<VkDescriptorSet> m_descriptorSets;

    // GPT_CHANGE: Depth buffer resources
    VkImage m_depthImage{VK_NULL_HANDLE};
    VkDeviceMemory m_depthMemory{VK_NULL_HANDLE};
    VkImageView m_depthView{VK_NULL_HANDLE};
    VkFormat m_depthFormat{VK_FORMAT_D32_SFLOAT};

    bool m_framebufferResized = false;

    // Phase E1: cached frame pass descriptors (instrumentation only)
    std::vector<FramePassDescriptor> m_framePassDescriptors;
    FrameGraph m_frameGraph;
    std::vector<FrameGraphCompiledPass> m_compiledFramePasses;
    VkImageLayout m_frameGraphColorLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    VkImageLayout m_frameGraphDepthLayout = VK_IMAGE_LAYOUT_UNDEFINED;

    // FPS tracking
    static constexpr double FPS_LOG_INTERVAL_SECONDS = 5.0;  // Log every 5 seconds
    double m_lastFpsTime = 0.0;
    int m_frameCount = 0;
    double m_lastStatsPrintTime = 0.0;
    size_t m_statsFrameCount = 0;
    size_t m_statsUploadBytesAccum = 0;
    
    // Fullscreen state
    bool m_isFullscreen = true;
    bool m_gameplayOnlyMode = false;
    bool m_perfMode = false;
    StartupTerrainPreset m_startupTerrainPreset = StartupTerrainPreset::Default;
    bool m_vsyncEnabled = false;  // VSync OFF by default — uncapped FPS
    int m_lastMonitorHz = 0;     // Track monitor changes for swapchain recreation
    int m_windowedWidth = 1920;
    int m_windowedHeight = 1080;
    int m_windowedPosX = 100;
    int m_windowedPosY = 100;

    // Detached gameplay window (second OS window mirroring 3D scene)
    std::unique_ptr<GameplayWindow> m_gameplayWindow;
    uint64_t m_gameplayWindowSwapchainGeneration = 0;
    bool m_gameplayWindowAcquired = false; // True when this frame acquired a gameplay image
    bool m_gameplaySeparated = false;      // Exposed to RenderSettingsWindow for button disabling

    bool m_perfOverlayEnabled = false;
    static constexpr uint32_t PERF_OVERLAY_DRAWCOUNT_SAMPLE_INTERVAL = 12;
    InGameDebug::DebugInfo::CPUBreakdown m_lastPerfBreakdown{};
    float m_perfOverlayAvgFps = 0.0f;
    float m_perfOverlayAvgFrameMs = 0.0f;
    float m_perfOverlayAvgCpuWorkMs = 0.0f;
    float m_perfOverlayAvgWorldMs = 0.0f;
    float m_perfOverlayAvgRenderMs = 0.0f;
    float m_perfOverlayAvgCullingMs = 0.0f;
    uint32_t m_perfOverlayVisibleChunks = 0;
    uint32_t m_perfOverlayTotalChunks = 0;

    struct FrameBottleneckSample {
        uint32_t frame{0};
        float totalFrameMs{0.0f};
        float cpuWorkMs{0.0f};
        float gpuFrameMs{0.0f};
        float glfwPollMs{0.0f};
        float fenceWaitMs{0.0f};
        float presentWaitMs{0.0f};
        float readbackMs{0.0f};
        float uploadSetupMs{0.0f};
        float worldUpdateMs{0.0f};
        float chunkLoadingMs{0.0f};
        float meshingMs{0.0f};
        float uploadMs{0.0f};
        float collisionMs{0.0f};
        float finalizeMs{0.0f};
        float cullingCpuMs{0.0f};
        float uploadSubmitMs{0.0f};
        float renderMs{0.0f};
        float imguiMs{0.0f};
        float imguiInterfaceMs{0.0f};
        float imguiVramMs{0.0f};
        float imguiCloudMs{0.0f};
        float imguiMinimapMs{0.0f};
        float imguiPerfMs{0.0f};
        float imguiToolMs{0.0f};
        float imguiEndFrameMs{0.0f};
        float toolsPanelTotalMs{0.0f};
        float toolsCursorMs{0.0f};
        float toolsTerrainMs{0.0f};
        float toolsTextureMs{0.0f};
        float cmdRecordMs{0.0f};
        float otherMs{0.0f};
        float gpuInitialCullMs{0.0f};
        float gpuCullingTotalMs{0.0f};
        float gpuTerrainMs{0.0f};
        float gpuPointShadowMs{0.0f};
        float gpuSunShadowMs{0.0f};
        float sunCpuTotalMs{0.0f};
        float sunCpuGatherMs{0.0f};
        float sunCpuHashMs{0.0f};
        float sunCpuCommandMs{0.0f};
        uint32_t activeCullingSlots{0};
        uint32_t visibleDraws{0};
        uint32_t frustumPassed{0};
        uint32_t chunksReady{0};
        uint32_t hiZOccluded{0};
        uint32_t selectedShadowLights{0};
        uint32_t eligibleShadowLights{0};
        uint32_t sunDrawCalls{0};
        uint32_t sunAcceptedChunks{0};
        uint32_t sunCandidateChunks{0};
        uint32_t sunRenderCacheMissMask{0};
        uint32_t sunGatherCacheMissMask{0};
        uint64_t meshTopologyRevision{0};
        bool gpuCullingEnabled{false};
        bool gpuCullingReady{false};
        bool sunRenderCacheHit{false};
        bool sunGatherCacheHit{false};
    };
    static constexpr size_t FRAME_BOTTLENECK_HISTORY = 1200;
    std::array<FrameBottleneckSample, FRAME_BOTTLENECK_HISTORY> m_frameBottleneckHistory{};
    size_t m_frameBottleneckWriteIdx = 0;
    size_t m_frameBottleneckCount = 0;
    
    // Time system (authoritative source for game time)
    TimeManager m_timeManager;
    
    // Lighting system (uses TimeManager for day/night cycle)
    LightingSettings m_lighting;
    
    uint32_t m_frameCounter{0};
    
    // Helper methods for lighting
    void createLightingBuffers();
    void createClusterBuffers();
    void createCameraBuffers();
    void createAOBuffers();
    void createMaterialOverlayBuffers();
    void ensureMaterialOverlayCapacity(size_t activeLOD0Cells);
    void refreshMaterialOverlayDescriptors();
    void syncMaterialOverlayForImage(uint32_t imageIndex);
    void updateLightingUniforms(uint32_t currentImage,
                                const std::vector<PointLight>& transientLights,
                                const std::vector<glm::vec4>& transientPulseData);
    void updateCameraUniforms(uint32_t currentImage);
    void updateAOUniforms(uint32_t currentImage);
    void updateClusterData(uint32_t currentImage,
                           const glm::mat4& viewCameraRel,
                           const glm::mat4& proj,
                           const VkRect2D& gameplayRect);
    // ... more members will be in Engine.cpp
};

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

    uint cleanFallbackMaterial = fallbackMaterial & ~MATERIAL_OVERLAY_CHUNK_HINT_BIT;

    // Whole-world baseline texture path:
    // If the mesh already carries a packed material word, keep using it. If it
    // does not, synthesize a deterministic cheap material from the face/world
    // position so the full terrain is textured even when the sparse paint table
    // is deliberately bypassed for performance.
    uint baselineMaterial = cleanFallbackMaterial;
    if ((baselineMaterial & MATERIAL_PACKED_BIT) == 0u) {
        uint type = (face == 3u) ? 0u : 2u; // top = grass, sides/bottom = dirt/stone-like

        // Mild low-altitude sand/mud breakup, purely procedural and coherent.
        float h = worldPos.y;
        float biomeNoise = hash12(floor(worldPos.xz * 0.0625));
        if (face == 3u) {
            if (h < 5.0) {
                type = 3u; // sand-like low areas
            } else if (biomeNoise > 0.82) {
                type = 1u; // muddy patches
            }
        }

        uint variant = uint(hash12(floor(worldPos.xz * 0.25) + vec2(float(face) * 13.0, 71.0)) * 8.0) & 0x7u;
        uint edge = 0u;
        uint resLog2 = 4u; // 16x16 texels per voxel face
        baselineMaterial = MATERIAL_PACKED_BIT | type | (variant << 2u) | (edge << 5u) | (resLog2 << 7u);
    }

    // The old sparse overlay is a global open-addressed SSBO hash table. When
    // it gets dense, the table turns the terrain/light pass into random-memory
    // probe hell. The diagnostics showed ~15.8M / 16.7M active cells and
    // maxProbe=120, which is unrecoverable in a fragment shader.
    if (materialOverlay.count == 0u || materialOverlay.capacityMask == 0u) {
        return baselineMaterial;
    }

    uint capacity = materialOverlay.capacityMask + 1u;

    // Hard safety gate: above 75% load, do not probe the global table at all.
    // This is not a distance/radius cut. It is a data-structure safety cut:
    // the whole world remains textured through baselineMaterial, while exact
    // paint awaits the chunk-local/page-local material system.
    if ((materialOverlay.count * 4u) > (capacity * 3u)) {
        return baselineMaterial;
    }

    // Per-draw chunk hint from cube.vert / GPU culling visibleOrigins.w. If the
    // hint is absent, this draw has no sparse exact paint overlay.
    if ((fallbackMaterial & MATERIAL_OVERLAY_CHUNK_HINT_BIT) == 0u) {
        return baselineMaterial;
    }

    // Bound exact lookup. Never use materialOverlay.maxProbe directly in the
    // fragment path; it is a diagnostic of hash-table health, not a sane shader
    // loop bound. Healthy tables still find normal hits within a few probes.
    const uint probeLimit = min(materialOverlay.maxProbe, 8u);

    ivec3 voxel = ivec3(floor(worldPos * 4.0 - normal * 0.01));
    uint idx = hashMaterialOverlayKey(voxel, face) & materialOverlay.capacityMask;

    for (uint probe = 0u; probe <= probeLimit; ++probe) {
        MaterialOverlayCell cell = materialOverlay.cells[idx];

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

## FUNCTION src/core/engine/Engine*.cpp :: Engine::createMaterialOverlayBuffers

Resolved FUNCTION target to 13 candidate files. Exporting only matching function bodies.

Source: src/core/engine/EngineSubsystemInit.cpp lines 169-238

````cpp
void Engine::createMaterialOverlayBuffers() {
    for (size_t i = 0; i < m_materialOverlayBuffers.size(); ++i) {
        if (i < m_materialOverlayMapped.size() && m_materialOverlayMapped[i]) {
            vkUnmapMemory(m_device, m_materialOverlayBuffersMemory[i]);
        }
        if (m_materialOverlayBuffers[i] != VK_NULL_HANDLE) {
            vkDestroyBuffer(m_device, m_materialOverlayBuffers[i], nullptr);
        }
        if (m_materialOverlayBuffersMemory[i] != VK_NULL_HANDLE) {
            vkFreeMemory(m_device, m_materialOverlayBuffersMemory[i], nullptr);
        }
    }

    const uint32_t imageCount = static_cast<uint32_t>(m_swapchainImages.size());
    m_materialOverlayBuffers.assign(imageCount, VK_NULL_HANDLE);
    m_materialOverlayBuffersMemory.assign(imageCount, VK_NULL_HANDLE);
    m_materialOverlayMapped.assign(imageCount, nullptr);
    m_materialOverlayImageDirty.assign(imageCount, 1u);
    m_materialOverlayImageDirtySlots.clear();
    m_materialOverlayImageDirtySlots.resize(imageCount);

    m_materialOverlayCapacity = nextPow2u32(std::max(
        m_materialOverlayCapacity,
        kMaterialOverlayInitialCapacity));
    m_materialOverlayTable.assign(m_materialOverlayCapacity, MaterialOverlayCellGPU{});
    m_materialOverlayCount = 0;
    m_materialOverlayMaxProbe = 0;
    m_materialOverlayLastGeneration = 0;
    m_materialOverlayNeedsRebuild = true;

    m_materialOverlayBufferSize =
        sizeof(MaterialOverlayHeaderGPU) +
        static_cast<VkDeviceSize>(m_materialOverlayCapacity) * sizeof(MaterialOverlayCellGPU);

    for (uint32_t i = 0; i < imageCount; ++i) {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = m_materialOverlayBufferSize;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_materialOverlayBuffers[i]) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create material overlay buffer");
        }

        VkMemoryRequirements memReq{};
        vkGetBufferMemoryRequirements(m_device, m_materialOverlayBuffers[i], &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice,
            memReq.memoryTypeBits,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_materialOverlayBuffersMemory[i]) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate material overlay buffer memory");
        }

        vkBindBufferMemory(m_device, m_materialOverlayBuffers[i], m_materialOverlayBuffersMemory[i], 0);

        void* mapped = nullptr;
        if (vkMapMemory(m_device, m_materialOverlayBuffersMemory[i], 0, m_materialOverlayBufferSize, 0, &mapped) != VK_SUCCESS) {
            throw std::runtime_error("Failed to map material overlay buffer");
        }
        m_materialOverlayMapped[i] = mapped;
        std::memset(mapped, 0, static_cast<size_t>(m_materialOverlayBufferSize));
    }
}
````


## FUNCTION src/core/engine/Engine*.cpp :: Engine::syncMaterialOverlayForImage

Resolved FUNCTION target to 13 candidate files. Exporting only matching function bodies.

Source: src/core/engine/EngineSubsystemInit.cpp lines 285-583

````cpp
void Engine::syncMaterialOverlayForImage(uint32_t imageIndex) {
    if (imageIndex >= m_materialOverlayMapped.size() || m_materialOverlayMapped[imageIndex] == nullptr) {
        return;
    }

    auto& textureStore = m_world.getTextureMaterialStore();
    const auto textureStats = textureStore.getStats();
    ensureMaterialOverlayCapacity(textureStats.cellsByLOD[0]);

    if (imageIndex >= m_materialOverlayMapped.size() || m_materialOverlayMapped[imageIndex] == nullptr) {
        return;
    }

    const size_t generation = textureStore.getGeneration();
    const bool genChanged = (generation != m_materialOverlayLastGeneration);

    if (m_materialOverlayNeedsRebuild || genChanged) {
        // Try the delta path first. consumeDirtyGPUCells flips requiresFullUpload
        // when the store has buffered a clear, an unbounded write, or hit its
        // delta cap — in those cases we fall back to a full rebuild.
        std::vector<TextureOverlay::TextureOverlayStore::GPUCell> deltas;
        bool requiresFullUpload = false;
        const size_t deltaBudget = static_cast<size_t>(m_materialOverlayCapacity);
        textureStore.consumeDirtyGPUCells(deltas, deltaBudget, requiresFullUpload);

        const bool doFullRebuild = m_materialOverlayNeedsRebuild || requiresFullUpload;
        const uint16_t pixelsPerVoxel = textureStore.getLODConfig(0).pixelsPerVoxel;
        const uint32_t mask = m_materialOverlayCapacity - 1u;

        auto sameOverlayKey = [](const MaterialOverlayCellGPU& cell,
                                 int32_t x, int32_t y, int32_t z, uint32_t face) {
            return cell.x == x &&
                   cell.y == y &&
                   cell.z == z &&
                   (cell.face & 0x7u) == (face & 0x7u);
        };

        auto probeDistanceForSlot = [&](uint32_t slotIndex,
                                        const MaterialOverlayCellGPU& cell) -> uint32_t {
            const uint32_t ideal = hashMaterialOverlayKey(
                cell.x, cell.y, cell.z, cell.face & 0x7u) & mask;
            return (slotIndex - ideal) & mask;
        };

        auto recordChangedSlot = [](std::vector<uint32_t>* changedSlots, uint32_t slot) {
            if (changedSlots) {
                changedSlots->push_back(slot);
            }
        };


        auto setChunkMaterialOverlayHint = [&](const glm::ivec3& chunkCoord, bool hasOverlay) {
            if (!m_gpuCulling.isReady()) {
                return;
            }

            const entt::entity entity = m_world.findChunk(chunkCoord);
            if (entity == entt::null) {
                return;
            }

            std::shared_lock regLock(m_world.registryMutex());
            const auto& registry = m_world.getRegistry();
            if (!registry.valid(entity)) {
                return;
            }

            auto applyHandle = [&](const MeshHandle& handle) {
                if (handle.gpuCullingSlot != UINT32_MAX) {
                    m_gpuCulling.setSlotMaterialOverlayHint(handle.gpuCullingSlot, hasOverlay);
                }
            };

            if (registry.all_of<MeshHandle>(entity)) {
                applyHandle(registry.get<MeshHandle>(entity));
            }
            if (registry.all_of<PendingMeshHandle>(entity)) {
                applyHandle(registry.get<PendingMeshHandle>(entity).handle);
            }
        };

        auto updateChunkHintFromGPUCell = [&](const TextureOverlay::TextureOverlayStore::GPUCell& cell,
                                              bool hasOverlay) {
            if ((cell.face & 0x7u) != 7u) {
                return;
            }
            setChunkMaterialOverlayHint(glm::ivec3(cell.x, cell.y, cell.z), hasOverlay);
        };

        // Robin Hood open-addressing insert/update.
        //
        // Why this matters:
        //   The old table used ordinary linear probing. At ~80% load the table
        //   produced very long clusters (diagnostics showed maxProbe ~276).
        //   cube.frag had a small fixed probe cap, so some painted voxels were
        //   never found. Removing the cap alone would make every miss extremely
        //   expensive on the GPU.
        //
        // Robin Hood hashing keeps probe lengths much flatter and lets the
        // shader use the resident-probe < search-probe early-out rule. This
        // makes missing entries cheap and keeps painted entries findable.
        auto applyCell = [&](int32_t x, int32_t y, int32_t z,
                             uint32_t face, uint32_t material,
                             std::vector<uint32_t>* changedSlots = nullptr) -> uint32_t {
            if (m_materialOverlayCapacity == 0u) {
                return UINT32_MAX;
            }

            const uint32_t faceKey = face & 0x7u;
            uint32_t idx = hashMaterialOverlayKey(x, y, z, faceKey) & mask;

            // If the table is full, allow updates to existing keys but refuse
            // new inserts. This avoids corrupting the table with a failed
            // Robin Hood insertion that has already swapped entries.
            if (m_materialOverlayCount >= m_materialOverlayCapacity) {
                uint32_t probe = 0u;
                while (probe < m_materialOverlayCapacity) {
                    MaterialOverlayCellGPU& slot = m_materialOverlayTable[idx];

                    if (slot.material == 0u) {
                        return UINT32_MAX;
                    }

                    if (sameOverlayKey(slot, x, y, z, faceKey)) {
                        slot.material = material;
                        if (probe > m_materialOverlayMaxProbe) {
                            m_materialOverlayMaxProbe = probe;
                        }
                        recordChangedSlot(changedSlots, idx);
                        return idx;
                    }

                    const uint32_t residentProbe = probeDistanceForSlot(idx, slot);
                    if (residentProbe < probe) {
                        return UINT32_MAX;
                    }

                    idx = (idx + 1u) & mask;
                    ++probe;
                }
                return UINT32_MAX;
            }

            MaterialOverlayCellGPU incoming{};
            incoming.x = x;
            incoming.y = y;
            incoming.z = z;
            incoming.face = faceKey;
            incoming.material = material;

            uint32_t probe = 0u;
            while (probe < m_materialOverlayCapacity) {
                if (probe > m_materialOverlayMaxProbe) {
                    m_materialOverlayMaxProbe = probe;
                }

                MaterialOverlayCellGPU& slot = m_materialOverlayTable[idx];

                if (slot.material == 0u) {
                    slot = incoming;
                    ++m_materialOverlayCount;
                    recordChangedSlot(changedSlots, idx);
                    return idx;
                }

                if (sameOverlayKey(slot, incoming.x, incoming.y, incoming.z, incoming.face)) {
                    slot.material = incoming.material;
                    recordChangedSlot(changedSlots, idx);
                    return idx;
                }

                const uint32_t residentProbe = probeDistanceForSlot(idx, slot);

                if (residentProbe < probe) {
                    const MaterialOverlayCellGPU displaced = slot;
                    slot = incoming;
                    incoming = displaced;
                    recordChangedSlot(changedSlots, idx);

                    // The displaced resident had residentProbe at this slot.
                    // After advancing to the next slot, its probe distance is
                    // residentProbe + 1.
                    probe = residentProbe;
                }

                idx = (idx + 1u) & mask;
                ++probe;
            }

            return UINT32_MAX;
        };

        if (doFullRebuild) {
            std::fill(m_materialOverlayTable.begin(), m_materialOverlayTable.end(), MaterialOverlayCellGPU{});
            m_materialOverlayCount = 0;
            m_materialOverlayMaxProbe = 0;

            std::vector<TextureOverlay::TextureOverlayStore::GPUCell> cells;
            textureStore.exportGPUCellsForLOD(
                0,
                cells,
                static_cast<size_t>(m_materialOverlayCapacity));

            // Phase 2: rebuild per-culling-slot material-overlay hints from the
            // exported painted-chunk sentinel cells. This makes unpainted chunks
            // skip cube.frag overlay hash probing entirely in GPU-culling mode.
            m_gpuCulling.clearAllMaterialOverlayHints();
            for (const auto& cell : cells) {
                updateChunkHintFromGPUCell(cell, true);
            }

            for (const auto& cell : cells) {
                if (m_materialOverlayCount >= m_materialOverlayCapacity) break;
                const uint8_t type = static_cast<uint8_t>(cell.packed & 0x3u);
                const uint8_t variant = static_cast<uint8_t>((cell.packed >> 2) & 0x7u);
                const uint8_t edge = static_cast<uint8_t>((cell.packed >> 5) & 0x3u);
                const uint32_t material = VertexPacking::packMaterial(type, variant, edge, pixelsPerVoxel);
                applyCell(cell.x, cell.y, cell.z, cell.face & 0x7u, material, nullptr);
            }

            // Full rebuild supersedes any queued per-image deltas.
            std::fill(m_materialOverlayImageDirty.begin(), m_materialOverlayImageDirty.end(), 1u);
            for (auto& q : m_materialOverlayImageDirtySlots) q.clear();
        } else {
            // Apply deltas in place. Robin Hood insertion may move multiple
            // already-existing slots, so every changed slot must be queued for
            // every swapchain image. Queueing only the final inserted slot would
            // make random painted cells disappear on images that did not receive
            // the displaced entries.
            std::vector<uint32_t> changedSlots;
            changedSlots.reserve(std::min<size_t>(deltas.size() * 4u, 65536u));

            for (const auto& cell : deltas) {
                const uint8_t type = static_cast<uint8_t>(cell.packed & 0x3u);
                const uint8_t variant = static_cast<uint8_t>((cell.packed >> 2) & 0x7u);
                const uint8_t edge = static_cast<uint8_t>((cell.packed >> 5) & 0x3u);
                const uint32_t material = VertexPacking::packMaterial(type, variant, edge, pixelsPerVoxel);
                applyCell(cell.x, cell.y, cell.z, cell.face & 0x7u, material, &changedSlots);
                updateChunkHintFromGPUCell(cell, true);
            }

            for (uint32_t slot : changedSlots) {
                if (slot >= m_materialOverlayCapacity) continue;
                for (size_t i = 0; i < m_materialOverlayImageDirtySlots.size(); ++i) {
                    if (m_materialOverlayImageDirty[i]) continue;
                    m_materialOverlayImageDirtySlots[i].push_back(slot);
                }
            }

            // If a per-image queue grows past a fraction of the table, promote
            // that image to a full re-upload — the partial-copy cost crosses
            // over the full-memcpy cost around capacity/16.
            const size_t promoteThreshold = std::max<size_t>(
                static_cast<size_t>(m_materialOverlayCapacity) / 16u, 1024u);
            for (size_t i = 0; i < m_materialOverlayImageDirtySlots.size(); ++i) {
                if (m_materialOverlayImageDirtySlots[i].size() > promoteThreshold) {
                    m_materialOverlayImageDirty[i] = 1u;
                    m_materialOverlayImageDirtySlots[i].clear();
                }
            }
        }

        m_materialOverlayLastGeneration = generation;
        m_materialOverlayNeedsRebuild = false;
    }

    uint8_t* base = static_cast<uint8_t*>(m_materialOverlayMapped[imageIndex]);
    auto* header = reinterpret_cast<MaterialOverlayHeaderGPU*>(base);
    auto* dstCells = reinterpret_cast<MaterialOverlayCellGPU*>(base + sizeof(MaterialOverlayHeaderGPU));

    if (m_materialOverlayImageDirty[imageIndex]) {
        header->capacityMask = m_materialOverlayCapacity - 1u;
        header->count = m_materialOverlayCount;
        header->maxProbe = m_materialOverlayMaxProbe;
        header->_pad = 0;
        std::memcpy(
            dstCells,
            m_materialOverlayTable.data(),
            static_cast<size_t>(m_materialOverlayCapacity) * sizeof(MaterialOverlayCellGPU));
        m_materialOverlayImageDirty[imageIndex] = 0u;
        m_materialOverlayImageDirtySlots[imageIndex].clear();
        return;
    }

    auto& q = m_materialOverlayImageDirtySlots[imageIndex];
    if (q.empty()) return;

    // Header reflects count/maxProbe — keep it current after delta inserts.
    header->capacityMask = m_materialOverlayCapacity - 1u;
    header->count = m_materialOverlayCount;
    header->maxProbe = m_materialOverlayMaxProbe;
    header->_pad = 0;

    for (uint32_t slot : q) {
        if (slot >= m_materialOverlayCapacity) continue;
        dstCells[slot] = m_materialOverlayTable[slot];
    }
    q.clear();
}
````
