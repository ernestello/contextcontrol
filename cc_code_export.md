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


## src\core\engine\Engine.cpp

Description: No CC-DESC found. C++ struct 'ChunkCoordKey'.

````cpp
#include "core/engine/Engine.h"
#include "core/WindowIcon.h"
#include "ui/EngineInterface.h"
#include "ui/debug_menu/world/ChunkMinimapWindow.h"
#include "ui/debug_menu/profiling/FPSProfilerWindow.h"
#include "vulkan/FrameGraph.h"
#include "core/TimeManager.h"
#include "rendering/sky/CloudSystem.h"
#include "world/chunks/core/ChunkJobs.h"  // For job system types
#include "world/config/WorldConfig.h"  // For CHUNK_SIZE_M
#include "world/chunks/core/Chunk.h"  // For AABB component
#include "physics/PhysicsWorld.h"
#include "player/PlayerController.h"
#include "player/PlayerCamera.h"
#include "world/config/MapConfig.h"
#include <imgui.h>
#include <imgui_impl_glfw.h>
#include <imgui_impl_vulkan.h>
#include <stdexcept>
#include <iostream>
#include "debug/TerminalLogConfig.h"
#include <cstring>
#include <array>
#include <algorithm>
#include <iomanip>
#include <sstream>
#include <cmath>
#include <limits>
#include <thread>
#include <unordered_map>

#ifdef _WIN32
#define NOMINMAX
#include <Windows.h>   // timeBeginPeriod / timeEndPeriod for precise sleep
#pragma comment(lib, "winmm.lib")
#endif

#ifndef VULKANAS_USE_FRAMEGRAPH_BARRIERS
#define VULKANAS_USE_FRAMEGRAPH_BARRIERS 1
#endif

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#include <glm/gtc/matrix_transform.hpp>

// C-nits 8: Job system now owned by World, not global

namespace {

struct ChunkCoordKey {
    int32_t x = 0;
    int32_t y = 0;
    int32_t z = 0;

    bool operator==(const ChunkCoordKey& other) const {
        return x == other.x && y == other.y && z == other.z;
    }
};

struct ChunkCoordKeyHasher {
    size_t operator()(const ChunkCoordKey& key) const noexcept {
        const uint64_t ux = static_cast<uint64_t>(static_cast<uint32_t>(key.x));
        const uint64_t uy = static_cast<uint64_t>(static_cast<uint32_t>(key.y));
        const uint64_t uz = static_cast<uint64_t>(static_cast<uint32_t>(key.z));
        uint64_t h = ux * 73856093ull;
        h ^= uy * 19349663ull;
        h ^= uz * 83492791ull;
        return static_cast<size_t>(h);
    }
};

bool chunkCoordLess(const glm::ivec3& a, const glm::ivec3& b) {
    if (a.x != b.x) return a.x < b.x;
    if (a.y != b.y) return a.y < b.y;
    return a.z < b.z;
}

void sortAndUniqueChunkCoords(std::vector<glm::ivec3>& coords) {
    std::sort(coords.begin(), coords.end(), chunkCoordLess);
    coords.erase(std::unique(coords.begin(), coords.end(),
                             [](const glm::ivec3& a, const glm::ivec3& b) {
                                 return a.x == b.x && a.y == b.y && a.z == b.z;
                             }),
                 coords.end());
}

std::string formatSunCacheMissMask(uint32_t mask) {
    if (mask == 0u) {
        return "-";
    }

    std::ostringstream out;
    bool first = true;
    auto append = [&](uint32_t bit, const char* name) {
        if ((mask & bit) == 0u) {
            return;
        }
        if (!first) {
            out << "|";
        }
        out << name;
        first = false;
    };

    append(ShadowSystem::SUN_CACHE_MISS_INVALID, "Invalid");
    append(ShadowSystem::SUN_CACHE_MISS_CONTEXT, "Context");
    append(ShadowSystem::SUN_CACHE_MISS_SUN_DIR, "SunDir");
    append(ShadowSystem::SUN_CACHE_MISS_CASCADE_COUNT, "CascadeCount");
    append(ShadowSystem::SUN_CACHE_MISS_VP_HASH, "VPHash");
    append(ShadowSystem::SUN_CACHE_MISS_TERRAIN_SIG, "TerrainSig");
    append(ShadowSystem::SUN_CACHE_MISS_CAMERA_CHUNK, "CameraChunk");
    append(ShadowSystem::SUN_CACHE_MISS_UPLOAD_TIMELINE, "UploadTimeline");
    append(ShadowSystem::SUN_CACHE_MISS_CASCADE_EXTENTS, "CascadeExtents");
    append(ShadowSystem::SUN_CACHE_MISS_MESH_REVISION, "MeshRevision");

    const uint32_t known =
        ShadowSystem::SUN_CACHE_MISS_INVALID |
        ShadowSystem::SUN_CACHE_MISS_CONTEXT |
        ShadowSystem::SUN_CACHE_MISS_SUN_DIR |
        ShadowSystem::SUN_CACHE_MISS_CASCADE_COUNT |
        ShadowSystem::SUN_CACHE_MISS_VP_HASH |
        ShadowSystem::SUN_CACHE_MISS_TERRAIN_SIG |
        ShadowSystem::SUN_CACHE_MISS_CAMERA_CHUNK |
        ShadowSystem::SUN_CACHE_MISS_UPLOAD_TIMELINE |
        ShadowSystem::SUN_CACHE_MISS_CASCADE_EXTENTS |
        ShadowSystem::SUN_CACHE_MISS_MESH_REVISION;
    const uint32_t unknown = mask & ~known;
    if (unknown != 0u) {
        if (!first) {
            out << "|";
        }
        out << "0x" << std::hex << unknown << std::dec;
    }

    return out.str();
}

int detectWindowMonitorHz(GLFWwindow* window) {
    if (!window) {
        return 60;
    }

    if (GLFWmonitor* fullscreenMonitor = glfwGetWindowMonitor(window)) {
        if (const GLFWvidmode* mode = glfwGetVideoMode(fullscreenMonitor)) {
            return mode->refreshRate > 0 ? mode->refreshRate : 60;
        }
    }

    int winX = 0;
    int winY = 0;
    int winW = 0;
    int winH = 0;
    glfwGetWindowPos(window, &winX, &winY);
    glfwGetWindowSize(window, &winW, &winH);

    const int winCenterX = winX + winW / 2;
    const int winCenterY = winY + winH / 2;

    int monCount = 0;
    GLFWmonitor** monitors = glfwGetMonitors(&monCount);
    for (int i = 0; i < monCount; ++i) {
        int mx = 0;
        int my = 0;
        glfwGetMonitorPos(monitors[i], &mx, &my);
        const GLFWvidmode* vm = glfwGetVideoMode(monitors[i]);
        if (vm &&
            winCenterX >= mx && winCenterX < mx + vm->width &&
            winCenterY >= my && winCenterY < my + vm->height) {
            return vm->refreshRate > 0 ? vm->refreshRate : 60;
        }
    }

    return 60;
}

} // namespace

Engine::Engine(int width,
               int height,
               const char* title,
               bool gameplayOnlyMode,
               bool perfMode,
               StartupTerrainPreset startupTerrainPreset)
: m_width(width),
  m_height(height),
  m_title(title),
  m_window(nullptr),
  m_gameplayOnlyMode(gameplayOnlyMode || perfMode),
  m_perfMode(perfMode),
  m_startupTerrainPreset(startupTerrainPreset),
  m_perfOverlayEnabled(perfMode)
{
    // Initialize geometry
    m_cubeMesh = Mesh::createCube();
    
    // Connect TimeManager to LightingSettings
    m_lighting.setTimeManager(&m_timeManager);
    
    initWindow();
    initVulkan();
}

Engine::~Engine(){
    cleanup();
}

void Engine::run(){
    mainLoop();
}

void Engine::initWindow(){
    if (!glfwInit())
        throw std::runtime_error("Failed to init GLFW");

    glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
    
    // Get primary monitor info for default sizing
    GLFWmonitor* primaryMonitor = glfwGetPrimaryMonitor();
    const GLFWvidmode* mode = primaryMonitor ? glfwGetVideoMode(primaryMonitor) : nullptr;
    
    // Create window with requested size if provided, otherwise fallback.
    m_windowedWidth = (m_width > 0) ? m_width : 1600;
    m_windowedHeight = (m_height > 0) ? m_height : 900;
    if (mode) {
        m_windowedPosX = (mode->width - m_windowedWidth) / 2;
        m_windowedPosY = (mode->height - m_windowedHeight) / 2;
    }
    
    // Normal decorated window (not borderless) unless performance mode wants
    // exclusive fullscreen from the start.
    glfwWindowHint(GLFW_DECORATED, m_perfMode ? GLFW_FALSE : GLFW_TRUE);
    
    // Main editor starts maximized; gameplay-only instance uses explicit window size.
    glfwWindowHint(GLFW_MAXIMIZED, m_gameplayOnlyMode ? GLFW_FALSE : GLFW_TRUE);

    if (m_perfMode && primaryMonitor && mode) {
        const int fullscreenWidth = (m_width > 0) ? m_width : mode->width;
        const int fullscreenHeight = (m_height > 0) ? m_height : mode->height;
        glfwWindowHint(GLFW_REFRESH_RATE, mode->refreshRate);
        m_window = glfwCreateWindow(fullscreenWidth, fullscreenHeight, m_title, primaryMonitor, nullptr);
        m_width = fullscreenWidth;
        m_height = fullscreenHeight;
        m_isFullscreen = true;
        std::cout << "[Engine] Performance mode enabled (" << m_width << "x" << m_height
                  << " @ " << mode->refreshRate << " Hz, exclusive fullscreen)" << std::endl;
    } else {
        // Create windowed window (pass nullptr for monitor = windowed mode)
        m_window = glfwCreateWindow(m_windowedWidth, m_windowedHeight, m_title, nullptr, nullptr);
        // Update dimensions (will be updated by framebuffer callback once maximized)
        m_width = m_windowedWidth;
        m_height = m_windowedHeight;
        m_isFullscreen = false;
    }
    if(!m_window) throw std::runtime_error("Failed to create GLFW window");

    setEngineWindowIcon(m_window);

    glfwSetWindowUserPointer(m_window, this);
    
    m_cursorManager.loadDefault();
    
    // Framebuffer resize callback
    auto framebufferResizeCallback = [](GLFWwindow* window, int w, int h){
        auto app = reinterpret_cast<Engine*>(glfwGetWindowUserPointer(window));
        app->m_framebufferResized = true;
    };
    glfwSetFramebufferSizeCallback(m_window, framebufferResizeCallback);
    
    // Initialize input system (handles mouse callback and cursor capture)
    m_input.setCursorManager(&m_cursorManager);
    m_input.init(m_window);
    if (m_gameplayOnlyMode) {
        m_input.setDebugWindowsVisible(false);
        m_world.getDebugOverlay().setUseEngineInterface(false);
    }
    if (m_perfMode) {
        m_input.setDebugWindowsAllowed(false);
        m_world.getDebugOverlay().setDebugUiVisible(false);
    }
}

// createInstance(), setupDebugMessenger(), pickPhysicalDevice(),
// createLogicalDevice(), createSurface() → EngineVulkanInit.cpp

// initVulkan() → EngineVulkanInit.cpp
// getStartupTerrainPresetName(), getStartupTerrainPresetSummary(),
// applyStartupTerrainPreset() → EngineSubsystemInit.cpp

void Engine::updatePerfOverlayStats(const InGameDebug::DebugInfo::CPUBreakdown& breakdown) {
    m_lastPerfBreakdown = breakdown;

    const float frameMs = std::max(0.0f, breakdown.totalFrameMs);
    const float fps = (frameMs > 0.001f) ? (1000.0f / frameMs) : 0.0f;
    constexpr float alpha = 0.08f;
    auto smooth = [alpha](float current, float sample) {
        return (current <= 0.001f) ? sample : (current * (1.0f - alpha) + sample * alpha);
    };

    m_perfOverlayAvgFps = smooth(m_perfOverlayAvgFps, fps);
    m_perfOverlayAvgFrameMs = smooth(m_perfOverlayAvgFrameMs, frameMs);
    m_perfOverlayAvgCpuWorkMs = smooth(m_perfOverlayAvgCpuWorkMs, breakdown.cpuWorkMs);
    m_perfOverlayAvgWorldMs = smooth(m_perfOverlayAvgWorldMs, breakdown.worldUpdateMs);
    m_perfOverlayAvgRenderMs = smooth(m_perfOverlayAvgRenderMs, breakdown.renderMs);
    m_perfOverlayAvgCullingMs = smooth(m_perfOverlayAvgCullingMs, breakdown.cullingMs);
}

void Engine::recordFrameBottleneckSample(const InGameDebug::DebugInfo::CPUBreakdown& breakdown) {
    FrameBottleneckSample sample{};
    sample.frame = m_frameCounter;
    sample.totalFrameMs = breakdown.totalFrameMs;
    sample.cpuWorkMs = breakdown.cpuWorkMs;
    sample.gpuFrameMs = static_cast<float>(m_lastGpuFrameMs);
    sample.glfwPollMs = breakdown.glfwPollMs;
    sample.fenceWaitMs = breakdown.fenceWaitMs;
    sample.presentWaitMs = breakdown.presentWaitMs;
    sample.readbackMs = breakdown.readbackMs;
    sample.uploadSetupMs = breakdown.uploadSetupMs;
    sample.worldUpdateMs = breakdown.worldUpdateMs;
    sample.chunkLoadingMs = breakdown.chunkLoadingMs;
    sample.meshingMs = breakdown.meshingMs;
    sample.uploadMs = breakdown.uploadMs;
    sample.collisionMs = breakdown.collisionMs;
    sample.finalizeMs = breakdown.finalizeMs;
    sample.cullingCpuMs = breakdown.cullingMs;
    sample.uploadSubmitMs = breakdown.uploadSubmitMs;
    sample.renderMs = breakdown.renderMs;
    sample.imguiMs = breakdown.imguiMs;
    sample.imguiInterfaceMs = m_imguiInterfaceMs;
    sample.imguiVramMs = m_imguiVramMs;
    sample.imguiCloudMs = m_imguiCloudMs;
    sample.imguiMinimapMs = m_imguiMinimapMs;
    sample.imguiPerfMs = m_imguiPerfMs;
    sample.imguiToolMs = m_imguiToolMs;
    sample.imguiEndFrameMs = m_imguiEndFrameMs;
    const auto& toolsTiming = m_world.getDebugOverlay().getLastToolPanelTiming();
    sample.toolsPanelTotalMs = toolsTiming.totalMs;
    sample.toolsCursorMs = toolsTiming.cursorMs;
    sample.toolsTerrainMs = toolsTiming.terrainMs;
    sample.toolsTextureMs = toolsTiming.texturePaintMs;
    sample.cmdRecordMs = breakdown.cmdRecordMs;
    sample.otherMs = breakdown.otherMs;
    sample.gpuInitialCullMs = static_cast<float>(m_gpuInitialCullMs);
    sample.gpuCullingTotalMs = static_cast<float>(m_cullingTotalMs);
    sample.gpuTerrainMs = static_cast<float>(m_terrainLightingMs);
    sample.activeCullingSlots = m_gpuCulling.getActiveSlotCount();
    sample.gpuCullingEnabled = m_gpuCullingEnabled;
    sample.gpuCullingReady = m_gpuCulling.isReady();
    sample.meshTopologyRevision = m_world.getMeshTopologyVersion();

    GPUCullingSystem::DebugStats cullStats{};
    if (m_gpuCulling.isReady()) {
        cullStats = m_gpuCulling.getDebugStats();
    }
    sample.visibleDraws = cullStats.visibleDraws;
    sample.frustumPassed = cullStats.frustumPassed;
    sample.chunksReady = cullStats.chunksReady;
    sample.hiZOccluded = cullStats.hiZOccluded;

    const auto& shadowFrame = m_shadowSystem.getFrameDiagnostics();
    sample.gpuPointShadowMs = shadowFrame.totalShadowGpuMs;
    sample.selectedShadowLights = shadowFrame.selectedShadowLights;
    sample.eligibleShadowLights = shadowFrame.eligibleShadowLights;

    const auto& sunDiag = m_shadowSystem.getSunShadowDiagnostics();
    const auto& sun = sunDiag.latest;
    sample.gpuSunShadowMs = sun.gpuRenderMs;
    sample.sunCpuTotalMs = sun.cpuTotalMs;
    sample.sunCpuGatherMs = sun.cpuTerrainGatherMs;
    sample.sunCpuHashMs = sun.cpuTerrainHashMs;
    sample.sunCpuCommandMs = sun.cpuCommandRecordMs;
    sample.sunDrawCalls = sun.drawCallCount;
    sample.sunAcceptedChunks = sun.acceptedChunkCount;
    sample.sunCandidateChunks = sun.bboxCandidateChunks;
    sample.sunRenderCacheMissMask = sun.renderCacheMissMask;
    sample.sunGatherCacheMissMask = sun.gatherCacheMissMask;
    sample.sunRenderCacheHit = sun.renderCacheHit;
    sample.sunGatherCacheHit = sun.gatherCacheHit;

    m_frameBottleneckHistory[m_frameBottleneckWriteIdx] = sample;
    m_frameBottleneckWriteIdx = (m_frameBottleneckWriteIdx + 1u) % FRAME_BOTTLENECK_HISTORY;
    m_frameBottleneckCount = std::min(m_frameBottleneckCount + 1u, FRAME_BOTTLENECK_HISTORY);
}

std::string Engine::generateFrameBottleneckReport() const {
    std::vector<FrameBottleneckSample> samples;
    samples.reserve(m_frameBottleneckCount);
    if (m_frameBottleneckCount > 0u) {
        const size_t start = (m_frameBottleneckWriteIdx + FRAME_BOTTLENECK_HISTORY -
                              m_frameBottleneckCount) % FRAME_BOTTLENECK_HISTORY;
        for (size_t i = 0; i < m_frameBottleneckCount; ++i) {
            samples.push_back(m_frameBottleneckHistory[(start + i) % FRAME_BOTTLENECK_HISTORY]);
        }
    }

    const auto textureStats = m_world.getTextureMaterialStore().getStats();
    const auto loadDiag = m_world.getLoadManagementDiag();
    const size_t runtimeVoxelChunks = m_world.getRuntimeVoxelChunkCoords().size();
    const uint64_t meshTopologyRevision = m_world.getMeshTopologyVersion();
    const auto& sunShadowCfg = m_shadowSystem.getSunShadowConfig();

    const auto& finalizeHistory = m_world.getFinalizeDiagHistory();
    size_t finalizeActiveFrames = 0u;
    double finalizeTotal = 0.0;
    float finalizeMax = 0.0f;
    uint64_t finalizeItems = 0u;
    uint64_t finalizeSwapEntities = 0u;
    for (const auto& frame : finalizeHistory) {
        const bool active = frame.totalMs > 0.0f ||
                            frame.finalizeCount > 0u ||
                            frame.lodSwapEntityCount > 0u;
        if (!active) {
            continue;
        }
        ++finalizeActiveFrames;
        finalizeTotal += frame.totalMs;
        finalizeMax = std::max(finalizeMax, frame.totalMs);
        finalizeItems += frame.finalizeCount;
        finalizeSwapEntities += frame.lodSwapEntityCount;
    }
    const float finalizeAvg = finalizeActiveFrames > 0u
        ? static_cast<float>(finalizeTotal / static_cast<double>(finalizeActiveFrames))
        : 0.0f;

    std::ostringstream out;
    out << std::fixed << std::setprecision(3);
    out << "=== FRAME BOTTLENECK DIAGNOSTICS REPORT ===\n";
    out << "Frame samples: " << samples.size() << " / " << FRAME_BOTTLENECK_HISTORY
        << " | Current frame counter: " << m_frameCounter << "\n";
    out << "Note: finalize is only upload finalization + LOD swap cleanup. It does not include "
           "GPU wait, command recording, shadow rendering, main terrain lighting, or present wait.\n\n";

    if (samples.empty()) {
        out << "No frame bottleneck samples have been recorded yet.\n";
        return out.str();
    }

    const auto& first = samples.front();
    const auto& last = samples.back();
    auto avgOf = [&](auto getter) -> float {
        double total = 0.0;
        for (const auto& sample : samples) {
            total += static_cast<double>(getter(sample));
        }
        return static_cast<float>(total / static_cast<double>(samples.size()));
    };
    auto maxOf = [&](auto getter) -> float {
        float value = 0.0f;
        for (const auto& sample : samples) {
            value = std::max(value, static_cast<float>(getter(sample)));
        }
        return value;
    };
    auto score = [](const FrameBottleneckSample& sample) -> float {
        return std::max(sample.totalFrameMs,
                        std::max(sample.cpuWorkMs, sample.gpuFrameMs));
    };

    size_t slowFrames = 0u;
    for (const auto& sample : samples) {
        if (sample.totalFrameMs >= 16.67f ||
            sample.cpuWorkMs >= 16.67f ||
            sample.gpuFrameMs >= 16.67f) {
            ++slowFrames;
        }
    }

    out << "=== CURRENT SNAPSHOT ===\n";
    out << "Total: " << last.totalFrameMs
        << " ms | CPU work: " << last.cpuWorkMs
        << " ms | GPU frame: " << last.gpuFrameMs << " ms\n";
    out << "CPU waits: fence " << last.fenceWaitMs
        << " ms, present/acquire " << last.presentWaitMs
        << " ms, glfwPoll " << last.glfwPollMs << " ms\n";
    out << "CPU work: render " << last.renderMs
        << " ms (cmd " << last.cmdRecordMs << ", imgui " << last.imguiMs << ")"
        << ", world " << last.worldUpdateMs
        << " ms, culling setup " << last.cullingCpuMs
        << " ms, finalize " << last.finalizeMs << " ms\n";
    out << "ImGui phases: interface " << last.imguiInterfaceMs
        << " ms, vram " << last.imguiVramMs
        << " ms, cloud " << last.imguiCloudMs
        << " ms, minimap " << last.imguiMinimapMs
        << " ms, perf " << last.imguiPerfMs
        << " ms, tool/overlay " << last.imguiToolMs
        << " ms, endFrame " << last.imguiEndFrameMs << " ms\n";
    out << "Tools panel: total " << last.toolsPanelTotalMs
        << " ms | cursor " << last.toolsCursorMs
        << " ms, terrain " << last.toolsTerrainMs
        << " ms, texture paint " << last.toolsTextureMs << " ms\n";
    out << "GPU: terrain/light " << last.gpuTerrainMs
        << " ms, cull " << last.gpuCullingTotalMs
        << " ms, point shadows " << last.gpuPointShadowMs
        << " ms, sun shadow " << last.gpuSunShadowMs << " ms\n";
    out << "Culling slots: active " << last.activeCullingSlots
        << ", ready " << last.chunksReady
        << ", visible draws " << last.visibleDraws
        << ", frustum passed " << last.frustumPassed
        << ", HiZ occluded " << last.hiZOccluded
        << " | mode " << (last.gpuCullingEnabled ? "GPU" : "CPU")
        << (last.gpuCullingReady ? " ready" : " not-ready") << "\n";
    out << "Shadows: point selected " << last.selectedShadowLights
        << "/" << last.eligibleShadowLights
        << ", sun draw calls " << last.sunDrawCalls
        << ", sun candidates " << last.sunCandidateChunks
        << ", sun accepted " << last.sunAcceptedChunks
        << ", debug mode " << sunShadowCfg.debugMode
        << ", render miss " << formatSunCacheMissMask(last.sunRenderCacheMissMask)
        << ", gather miss " << formatSunCacheMissMask(last.sunGatherCacheMissMask) << "\n\n";

    struct SectionValue {
        const char* name;
        float value;
    };
    std::vector<SectionValue> cpuSections = {
        {"Fence wait", last.fenceWaitMs},
        {"Present/acquire", last.presentWaitMs},
        {"glfwPoll", last.glfwPollMs},
        {"Render total", last.renderMs},
        {"Command record", last.cmdRecordMs},
        {"ImGui/debug UI", last.imguiMs},
        {"ImGui interface", last.imguiInterfaceMs},
        {"ImGui tools/overlay", last.imguiToolMs},
        {"ImGui endFrame", last.imguiEndFrameMs},
        {"ImGui VRAM", last.imguiVramMs},
        {"ImGui minimap", last.imguiMinimapMs},
        {"Tools panel total", last.toolsPanelTotalMs},
        {"Tool: texture paint", last.toolsTextureMs},
        {"Tool: terrain edit", last.toolsTerrainMs},
        {"Tool: cursor", last.toolsCursorMs},
        {"World update", last.worldUpdateMs},
        {"Chunk loading", last.chunkLoadingMs},
        {"Meshing", last.meshingMs},
        {"Upload", last.uploadMs},
        {"Collision", last.collisionMs},
        {"Finalize", last.finalizeMs},
        {"Culling CPU", last.cullingCpuMs},
        {"Readback", last.readbackMs},
        {"Upload submit", last.uploadSubmitMs},
        {"Other", last.otherMs},
    };
    std::sort(cpuSections.begin(), cpuSections.end(),
              [](const SectionValue& a, const SectionValue& b) {
                  return a.value > b.value;
              });
    out << "=== CURRENT CPU SUSPECTS ===\n";
    for (size_t i = 0; i < std::min<size_t>(cpuSections.size(), 10u); ++i) {
        out << std::setw(18) << std::left << cpuSections[i].name << std::right
            << " " << std::setw(8) << cpuSections[i].value << " ms\n";
    }
    out << "\n";

    std::vector<SectionValue> gpuSections = {
        {"GPU frame", last.gpuFrameMs},
        {"Terrain/light", last.gpuTerrainMs},
        {"Point shadows", last.gpuPointShadowMs},
        {"Sun shadow", last.gpuSunShadowMs},
        {"GPU culling", last.gpuCullingTotalMs},
        {"Initial cull", last.gpuInitialCullMs},
    };
    std::sort(gpuSections.begin(), gpuSections.end(),
              [](const SectionValue& a, const SectionValue& b) {
                  return a.value > b.value;
              });
    out << "=== CURRENT GPU SUSPECTS ===\n";
    for (const auto& section : gpuSections) {
        out << std::setw(18) << std::left << section.name << std::right
            << " " << std::setw(8) << section.value << " ms\n";
    }
    out << "\n";

    out << "=== ROLLING WINDOW SUMMARY ===\n";
    out << "Slow frames (>=16.67ms CPU/GPU/total): " << slowFrames
        << " / " << samples.size() << "\n";
    out << "Metric            Avg      Max\n";
    out << "Total frame   " << std::setw(8) << avgOf([](const auto& s) { return s.totalFrameMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.totalFrameMs; }) << "\n";
    out << "CPU work      " << std::setw(8) << avgOf([](const auto& s) { return s.cpuWorkMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.cpuWorkMs; }) << "\n";
    out << "GPU frame     " << std::setw(8) << avgOf([](const auto& s) { return s.gpuFrameMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuFrameMs; }) << "\n";
    out << "Fence wait    " << std::setw(8) << avgOf([](const auto& s) { return s.fenceWaitMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.fenceWaitMs; }) << "\n";
    out << "Present wait  " << std::setw(8) << avgOf([](const auto& s) { return s.presentWaitMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.presentWaitMs; }) << "\n";
    out << "Render CPU    " << std::setw(8) << avgOf([](const auto& s) { return s.renderMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.renderMs; }) << "\n";
    out << "World update  " << std::setw(8) << avgOf([](const auto& s) { return s.worldUpdateMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.worldUpdateMs; }) << "\n";
    out << "Finalize      " << std::setw(8) << avgOf([](const auto& s) { return s.finalizeMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.finalizeMs; }) << "\n";
    out << "GPU terrain   " << std::setw(8) << avgOf([](const auto& s) { return s.gpuTerrainMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuTerrainMs; }) << "\n";
    out << "GPU culling   " << std::setw(8) << avgOf([](const auto& s) { return s.gpuCullingTotalMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuCullingTotalMs; }) << "\n";
    out << "Point shadows " << std::setw(8) << avgOf([](const auto& s) { return s.gpuPointShadowMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuPointShadowMs; }) << "\n";
    out << "Sun shadow    " << std::setw(8) << avgOf([](const auto& s) { return s.gpuSunShadowMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuSunShadowMs; }) << "\n\n";

    out << "=== EDIT / RESOURCE GROWTH CHECKS ===\n";
    out << "Texture material store: cells " << textureStats.totalCells
        << ", bricks " << textureStats.totalBricks
        << ", stamps " << textureStats.surfaceStampCount
        << ", generation " << textureStats.generation << "\n";
    out << "Texture cells by LOD:";
    const size_t lodCount = sizeof(textureStats.cellsByLOD) / sizeof(textureStats.cellsByLOD[0]);
    for (size_t lod = 0; lod < lodCount; ++lod) {
        if (textureStats.cellsByLOD[lod] == 0u && textureStats.bricksByLOD[lod] == 0u) {
            continue;
        }
        out << " L" << lod << "=" << textureStats.cellsByLOD[lod]
            << "c/" << textureStats.bricksByLOD[lod] << "b";
    }
    out << "\n";
    out << "Material overlay GPU: active " << m_materialOverlayCount
        << " / capacity " << m_materialOverlayCapacity
        << " | max probe " << m_materialOverlayMaxProbe
        << " | buffer " << (static_cast<double>(m_materialOverlayBufferSize) / (1024.0 * 1024.0))
        << " MiB per image\n";
    out << "Runtime voxel chunks: " << runtimeVoxelChunks
        << " | mesh topology revision: " << meshTopologyRevision
        << " (window delta " << (last.meshTopologyRevision - first.meshTopologyRevision) << ")\n";
    out << "Active culling slots delta: "
        << static_cast<int64_t>(last.activeCullingSlots) - static_cast<int64_t>(first.activeCullingSlots)
        << " | visible draw delta: "
        << static_cast<int64_t>(last.visibleDraws) - static_cast<int64_t>(first.visibleDraws)
        << " | GPU frame delta: " << (last.gpuFrameMs - first.gpuFrameMs)
        << " ms | CPU work delta: " << (last.cpuWorkMs - first.cpuWorkMs) << " ms\n\n";

    out << "=== CURRENT QUEUES / STREAMING ===\n";
    out << "Render dist base/effective: " << loadDiag.baseRenderDist
        << "/" << loadDiag.effectiveRenderDist
        << " | extension rings " << loadDiag.extensionRings
        << " | throughput " << loadDiag.measuredThroughput
        << " | buffer pressure " << (loadDiag.bufferPressure ? "yes" : "no") << "\n";
    out << "Pending creates " << loadDiag.pendingCreates
        << ", destroys " << loadDiag.pendingDestroys
        << ", LOD remesh queue " << loadDiag.lodRemeshQueue
        << ", pending LOD remeshes " << loadDiag.pendingLodRemeshes
        << ", edit remesh pending " << loadDiag.editRemeshPending
        << ", upload queue " << loadDiag.uploadQueue
        << ", finalize queue " << loadDiag.finalizeQueue << "\n";
    out << "Finalize history active frames " << finalizeActiveFrames
        << ", avg " << finalizeAvg
        << " ms, max " << finalizeMax
        << " ms, finalized items " << finalizeItems
        << ", LOD swap entities " << finalizeSwapEntities << "\n\n";

    out << "=== AUTOMATIC READ ===\n";
    if (last.fenceWaitMs > 2.0f && last.gpuFrameMs > 8.0f) {
        out << "- CPU is likely waiting for GPU completion. The expensive work is probably in GPU frame, terrain/light, shadows, or culling, not finalize.\n";
    }
    if (last.presentWaitMs > 2.0f && last.presentWaitMs > last.cpuWorkMs) {
        out << "- Present/acquire wait dominates. Check VSync, swapchain pacing, detached gameplay window pacing, or monitor refresh changes.\n";
    }
    if (last.gpuTerrainMs > 4.0f) {
        out << "- Terrain/light pass is expensive. If this grows with texture edits, inspect material shader divergence, active draw count, and shadow sampling counters.\n";
    }
    if (last.gpuPointShadowMs > 2.0f || last.gpuSunShadowMs > 2.0f) {
        out << "- Shadow rendering is expensive. Repeated MeshRevision/TerrainSig/UploadTimeline misses after texture-only edits mean the shadow cache is being invalidated by non-geometric changes.\n";
    }
    if (sunShadowCfg.debugMode != 0) {
        out << "- Sun shadow debug visualization is enabled. If the terrain looks like shadow/cascade tiles, turn Debug Vis off in the Sun Shadow panel.\n";
    }
    if (last.toolsPanelTotalMs > 2.0f) {
        out << "- Tools panel UI is expensive. The per-tool line above shows whether Cursor, Terrain, or Texture Paint is responsible.\n";
    }
    if (last.activeCullingSlots > last.visibleDraws * 8u && last.activeCullingSlots > 1000u) {
        out << "- Many active GPU culling slots are offscreen. Offscreen terrain can still cost culling/shadow-gather work even when main camera visibility is low.\n";
    }
    if (textureStats.totalCells > 0u && last.meshTopologyRevision != first.meshTopologyRevision) {
        out << "- Texture material cells exist and mesh topology changed during the sample window. If no geometry edit happened, that is suspicious and should correlate with shadow cache misses.\n";
    }
    if (last.finalizeMs < 1.0f && last.totalFrameMs > 8.0f) {
        out << "- Finalize is not the bottleneck in the latest sample. Use the CPU/GPU suspect tables above.\n";
    }
    out << "\n";

    std::vector<FrameBottleneckSample> topFrames = samples;
    std::sort(topFrames.begin(), topFrames.end(),
              [&](const FrameBottleneckSample& a, const FrameBottleneckSample& b) {
                  return score(a) > score(b);
              });
    if (topFrames.size() > 20u) {
        topFrames.resize(20u);
    }
    out << "=== TOP BOTTLENECK FRAMES ===\n";
    out << "Frame | Score  | Total  | CPU    | GPU    | Fence | Pres  | Rend  | Cmd   | World | Fin   | GTerr | GCull | PShad | SShad | Slots | Vis | TopoRev | SunMiss\n";
    out << "------|--------|--------|--------|--------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-----|---------|--------\n";
    for (const auto& sample : topFrames) {
        out << std::setw(5) << sample.frame << " | "
            << std::setw(6) << score(sample) << " | "
            << std::setw(6) << sample.totalFrameMs << " | "
            << std::setw(6) << sample.cpuWorkMs << " | "
            << std::setw(6) << sample.gpuFrameMs << " | "
            << std::setw(5) << sample.fenceWaitMs << " | "
            << std::setw(5) << sample.presentWaitMs << " | "
            << std::setw(5) << sample.renderMs << " | "
            << std::setw(5) << sample.cmdRecordMs << " | "
            << std::setw(5) << sample.worldUpdateMs << " | "
            << std::setw(5) << sample.finalizeMs << " | "
            << std::setw(5) << sample.gpuTerrainMs << " | "
            << std::setw(5) << sample.gpuCullingTotalMs << " | "
            << std::setw(5) << sample.gpuPointShadowMs << " | "
            << std::setw(5) << sample.gpuSunShadowMs << " | "
            << std::setw(5) << sample.activeCullingSlots << " | "
            << std::setw(3) << sample.visibleDraws << " | "
            << std::setw(7) << sample.meshTopologyRevision << " | "
            << formatSunCacheMissMask(sample.sunRenderCacheMissMask) << "\n";
    }
    out << "\n";

    out << "=== RECENT FRAME SAMPLES (newest first) ===\n";
    out << "Frame | Total  | CPU    | GPU    | Fence | Pres  | Rend  | Cmd   | World | Fin   | GTerr | GCull | PShad | SShad | Slots | Vis | TopoRev | SunMiss\n";
    out << "------|--------|--------|--------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-----|---------|--------\n";
    const size_t recentCount = std::min<size_t>(samples.size(), 40u);
    for (size_t i = 0; i < recentCount; ++i) {
        const auto& sample = samples[samples.size() - 1u - i];
        out << std::setw(5) << sample.frame << " | "
            << std::setw(6) << sample.totalFrameMs << " | "
            << std::setw(6) << sample.cpuWorkMs << " | "
            << std::setw(6) << sample.gpuFrameMs << " | "
            << std::setw(5) << sample.fenceWaitMs << " | "
            << std::setw(5) << sample.presentWaitMs << " | "
            << std::setw(5) << sample.renderMs << " | "
            << std::setw(5) << sample.cmdRecordMs << " | "
            << std::setw(5) << sample.worldUpdateMs << " | "
            << std::setw(5) << sample.finalizeMs << " | "
            << std::setw(5) << sample.gpuTerrainMs << " | "
            << std::setw(5) << sample.gpuCullingTotalMs << " | "
            << std::setw(5) << sample.gpuPointShadowMs << " | "
            << std::setw(5) << sample.gpuSunShadowMs << " | "
            << std::setw(5) << sample.activeCullingSlots << " | "
            << std::setw(3) << sample.visibleDraws << " | "
            << std::setw(7) << sample.meshTopologyRevision << " | "
            << formatSunCacheMissMask(sample.sunRenderCacheMissMask) << "\n";
    }

    return out.str();
}

bool Engine::shouldSamplePerfOverlayDrawCount() const {
    return m_perfMode &&
           m_perfOverlayEnabled &&
           ((m_frameCounter % PERF_OVERLAY_DRAWCOUNT_SAMPLE_INTERVAL) == 0);
}

void Engine::mainLoop(){
    double lastTime = glfwGetTime();
    
    // Pre-allocated buffers for CPU culling path (avoid per-frame allocation)
    std::vector<VkDrawIndexedIndirectCommand> cpuCullDrawCmds(MAX_INDIRECT_DRAWS);
    std::vector<glm::vec4> cpuCullChunkOrigins(MAX_INDIRECT_DRAWS);
    
    // Track actual frame-to-frame time (includes limiter sleep) for real FPS display
    auto prevFrameStart = std::chrono::high_resolution_clock::now();
    float actualFrameMs = 16.67f; // Bootstrap value
    
#ifdef _WIN32
    // Set Windows timer resolution to 1ms for precise sleep_for / frame limiting.
    // Without this, sleep granularity is ~15ms which destroys frame pacing.
    timeBeginPeriod(1);
#endif
    
    while (!glfwWindowShouldClose(m_window)){
        auto frameStart = std::chrono::high_resolution_clock::now();
        actualFrameMs = std::chrono::duration<float, std::milli>(frameStart - prevFrameStart).count();
        prevFrameStart = frameStart;
        m_lastActualFrameMs = actualFrameMs;
        
        glfwPollEvents();
        auto afterGlfwPoll = std::chrono::high_resolution_clock::now();
        if (!m_perfMode) {
            processShaderHotReload();
        }
        compileFrameGraph();
        
        // Calculate delta time (clamped to prevent CPU-spike cascading)
        double currentTime = glfwGetTime();
        float rawDeltaTime = static_cast<float>(currentTime - lastTime);
        lastTime = currentTime;
        m_lastCpuFrameMs = static_cast<double>(rawDeltaTime) * 1000.0;
        // Clamp to 100ms (10 FPS floor) — prevents a single stall frame from
        // corrupting speed estimation, throughput windows, and adaptive distance.
        // Physics/rendering sees at most 100ms step; the real wall-clock gap is
        // still recorded in m_lastCpuFrameMs for diagnostics.
        float deltaTime = std::min(rawDeltaTime, 0.1f);
        
        auto afterPollEvents = std::chrono::high_resolution_clock::now();
        
        // Update time system (authoritative source for game time)
        m_timeManager.update(deltaTime);
        
        // Update lighting system (day/night cycle - reads from TimeManager)
        m_lighting.updateLighting(deltaTime);
        
        // Poll input and apply lighting hotkeys
        InputState inputState = m_input.pollInput(deltaTime);
        m_imgui.setCursorEnabled(m_input.isCursorEnabled());
        m_input.applyLightingHotkeys(inputState, m_lighting, m_timeManager);
        if (inputState.startTerrainGeneration) {
            startChunkGeneration();
        }
        if (inputState.toggleGameplaySeparation) {
            setGameplaySeparated(!m_gameplaySeparated);
        }
        
        // Handle G key for GPU culling toggle (simple debounce)
        static bool gKeyHeld = false;
        bool gPressed = m_input.isKeyPressed(GLFW_KEY_G);
        if (gPressed && !gKeyHeld) {
            toggleGPUCulling();
        }
        gKeyHeld = gPressed;
        
        // Handle T key for T-junction fix toggle (simple debounce)
        static bool tKeyHeld = false;
        bool tPressed = m_input.isKeyPressed(GLFW_KEY_T);
        if (tPressed && !tKeyHeld) {
            bool enabled = !m_tjunctionFix.isEnabled();
            m_tjunctionFix.setEnabled(enabled);
            std::cout << "[Engine] T-junction fix " << (enabled ? "ENABLED" : "DISABLED") << std::endl;
        }
        tKeyHeld = tPressed;
        
        int gameplayViewportWidth = static_cast<int>(m_swapchainExtent.width);
        int gameplayViewportHeight = static_cast<int>(m_swapchainExtent.height);
        const bool gameplayDetached = m_gameplaySeparated
                                   && m_gameplayWindow
                                   && m_gameplayWindow->isOpen();
        if (gameplayDetached) {
            gameplayViewportWidth = std::max(1, static_cast<int>(m_gameplayWindow->getExtent().width));
            gameplayViewportHeight = std::max(1, static_cast<int>(m_gameplayWindow->getExtent().height));
        } else if (m_input.areDebugWindowsVisible() && m_world.getDebugOverlay().isUsingEngineInterface()) {
            auto& ui = m_world.getDebugOverlay().getEngineInterface();
            if (ui.hasGameplayViewport()) {
                gameplayViewportWidth = std::max(1, ui.getGameplayViewportWidth());
                gameplayViewportHeight = std::max(1, ui.getGameplayViewportHeight());
            }
        }

        // Update input, camera, and player using gameplay viewport dimensions.
        m_input.update(inputState, deltaTime, m_camera, m_player, m_playerCamera,
                       gameplayViewportWidth, gameplayViewportHeight);

        // Keep debug collector gating in sync with runtime debug visibility (Ctrl+7).
        m_world.getDebugOverlay().setDebugUiVisible(m_input.areDebugWindowsVisible());
        
        auto afterInput = std::chrono::high_resolution_clock::now();
        
        // Update physics simulation
        m_physics.update(deltaTime);
        
        auto afterPhysics = std::chrono::high_resolution_clock::now();
        
        // Update dynamic cloud system (movement, merging, evolution)
        // Pass camera position so clouds follow the player
        auto beforeClouds = std::chrono::high_resolution_clock::now();
        const glm::vec3& cameraPos = m_camera.getState().position;
        m_cloudSystem.updateClouds(m_device, deltaTime, static_cast<float>(currentTime), cameraPos);
        auto afterClouds = std::chrono::high_resolution_clock::now();
        
        // Get camera state for rendering and world update
        const CameraState& camState = m_camera.getState();
        
        // Wait for previous frame's work to complete
        auto beforeFence = std::chrono::high_resolution_clock::now();
        PerFrame& frame = m_frames[m_currentFrame];
        vkWaitForFences(m_device, 1, &frame.inFlight, VK_TRUE, UINT64_MAX);
        auto afterFence = std::chrono::high_resolution_clock::now();
        
        // Update GPU culling stats from previous frame's readback (after fence ensures data is ready)
        if (m_gpuCullingEnabled && m_gpuCulling.isReady()) {
            if (!m_perfMode || m_perfOverlayEnabled) {
                m_gpuCulling.updateDrawCountFromReadback();
            }

            if (!m_perfMode) {
                // Process minimap readback if enabled (1-frame delayed data)
                // Use current frame index - we just waited on this frame's fence, so its readback is ready
                // Additional safety: only process if minimap window has been properly initialized with World
                if (m_minimapReadback.isEnabled() && m_minimapReadback.isReady()) {
                    m_minimapReadback.processReadback(static_cast<uint32_t>(m_currentFrame));
                    updateGpuVisibleChunkSnapshot();
                    // Pass readback data to minimap window only if it's safe to do so
                    auto& minimap = m_world.getDebugOverlay().getChunkMinimapWindow();
                    minimap.setVisibleChunks(m_minimapReadback.getVisibleChunkKeys());
                    minimap.setGPUReadbackAvailable(true);
                }
            }
        }
        auto afterReadback = std::chrono::high_resolution_clock::now();
        
        // C3.2: Reset upload arena for this frame (fence ensures previous frame's upload finished)
        m_uploadArenas[m_currentFrame].reset();
        
        // C3.2: Begin per-frame upload command buffer (no CPU wait needed)
        VkCommandBuffer uploadCmd = m_uploadCmds[m_currentFrame];
        vkResetCommandBuffer(uploadCmd, 0);
        VkCommandBufferBeginInfo uploadBeginInfo{};
        uploadBeginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        uploadBeginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        vkBeginCommandBuffer(uploadCmd, &uploadBeginInfo);
        
        // Begin upload batch
        m_uploader.beginBatch(uploadCmd);
        
        // C3.2: Compute batch timeline value for this frame
        const uint64_t batchValue = m_uploadTimelineValue + 1;

        // Cull / draw gating: by the time this frame's graphics CB executes on
        // GPU, the graphics submit's wait on m_uploadTimeline (at the post-
        // upload-submit m_uploadTimelineValue) guarantees every slot write up
        // to batchValue is complete. So the shader's gpuReadyValue gate can
        // safely treat batchValue as "currently signaled" — eliminates the
        // post-edit invisibility flicker under GPU culling.
        m_frameCullingTimelineValue = batchValue;
        
        // Set batch fence value for fallback tracking
        m_uploadArenas[m_currentFrame].setBatchFenceValue(batchValue);
        
        auto afterUploadSetup = std::chrono::high_resolution_clock::now();
        
        // Update world - full terrain system with allocators
        m_world.update(deltaTime, cameraPos, camState.yaw, &m_vbAllocator, &m_ibAllocator, &m_uploadArenas[m_currentFrame], &m_uploader, batchValue,
                       static_cast<float>(m_lastCpuFrameMs), static_cast<float>(m_lastGpuFrameMs),
                       m_frameVisibleUploadTimelineValue);
        
        // C3.3: Query actual signaled timeline value for world gating
        uint64_t lastSignaled = 0;
        if (m_vkGetSemaphoreCounterValueKHR) {
            m_vkGetSemaphoreCounterValueKHR(m_device, m_uploadTimeline, &lastSignaled);
        }
        m_frameVisibleUploadTimelineValue =
            m_world.hadUploadsThisFrame() ? batchValue : lastSignaled;

        // Drain completed fallback temp buffers
        m_uploadArenas[m_currentFrame].drainTemps(lastSignaled);
        
        // Use camera's pre-computed view-projection matrix for culling
        const glm::mat4& viewProj = camState.viewProj;
        
        // Update minimap with view-projection matrix for accurate frustum culling
        if (!m_perfMode) {
            auto& minimapWindow = m_world.getDebugOverlay().getChunkMinimapWindow();
            minimapWindow.setViewProjMatrix(viewProj);
            
            // Update minimap with actual camera parameters for 1:1 frustum visualization
            float aspect = static_cast<float>(gameplayViewportWidth) / static_cast<float>(gameplayViewportHeight);
            minimapWindow.setCameraInfo(cameraPos, camState.yaw, camState.pitch, camState.fovDegrees,
                                        camState.nearPlane, camState.farPlane, aspect);
            
            // Enable/disable minimap GPU readback based on user checkbox.
            // Also force readback during active G-mode diff capture when the
            // target mode is GPU (needed for deterministic after-toggle snapshot).
            const bool diagnosticReadback =
                m_gModeGeometryDiffCapture.active &&
                m_gModeGeometryDiffCapture.afterGpuMode;
            const bool minimapPanelOpen = m_world.getDebugOverlay().isMinimapWindowOpen();
            bool wantsReadback = (minimapWindow.wantsGPUReadback(minimapPanelOpen) && m_gpuCullingEnabled)
                              || diagnosticReadback;
            m_minimapReadback.setEnabled(wantsReadback);
            if (!wantsReadback) {
                minimapWindow.setGPUReadbackAvailable(false);
            }
        } else {
            m_minimapReadback.setEnabled(false);
        }
        
        // Choose between CPU and GPU culling paths
        auto beforeCulling = std::chrono::high_resolution_clock::now();
        if (m_gpuCullingEnabled && m_gpuCulling.isReady()) {
            // GPU culling path: Phase 2 uses persistent storage
            // Chunk data is uploaded incrementally in ChunkUploadSystem
            // No need to rebuild entire buffer every frame
            // Dispatch over compact active-slot indices (no high-water holes).
            m_gpuCullingChunkCount = m_gpuCulling.getActiveSlotCount();
            
            // m_indirectDrawCount will be determined by GPU (read from drawCountBuffer)
            // Set a marker value so recordCommandBuffer knows to use GPU path
            m_indirectDrawCount = UINT32_MAX;  // Special value = use GPU culling
            
            auto gpuDebugStats = m_gpuCulling.getDebugStats();
            // Keep draw count GPU-resident by default. In non-perf mode we use the
            // debug-stats readback field populated by the cull shader; in perf mode
            // we keep a sparse sampled value.
            const uint32_t sampledVisible = m_gpuCulling.getLastVisibleDrawCount();
            const uint32_t visibleDraws = m_perfMode ? sampledVisible : gpuDebugStats.visibleDraws;
            World::CullingStats stats;
            stats.gpuCullingEnabled = true;
            stats.gpuCullingReady = true;
            stats.totalChunksInCulling = m_gpuCullingChunkCount;
            stats.visibleDrawCalls = visibleDraws;
            stats.culledDrawCalls = (m_gpuCullingChunkCount > visibleDraws) ? (m_gpuCullingChunkCount - visibleDraws) : 0;
            stats.frustumPassed = gpuDebugStats.frustumPassed;
            // GPU timing from timestamp queries
            stats.cullingDispatchMs = static_cast<float>(m_cullingDispatchMs);
            stats.totalCullingMs = static_cast<float>(m_cullingTotalMs);
            m_world.setCullingStats(stats);
            m_perfOverlayVisibleChunks = visibleDraws;
            m_perfOverlayTotalChunks = m_gpuCullingChunkCount;
        } else {
            // CPU culling path: use pre-allocated buffers (no per-frame allocation)
            m_indirectDrawCount = m_world.gatherDrawCommands(
                viewProj,
                cpuCullDrawCmds.data(),
                cpuCullChunkOrigins.data(),
                MAX_INDIRECT_DRAWS,
                m_frameCullingTimelineValue);

            updateCpuVisibleChunkSnapshot(cpuCullChunkOrigins.data(), m_indirectDrawCount);
            
            if (m_indirectDrawCount > 0) {
                // Upload draw commands
                VkDeviceSize uploadSize = m_indirectDrawCount * sizeof(VkDrawIndexedIndirectCommand);
                UploadRequest indirectReq;
                indirectReq.src = cpuCullDrawCmds.data();
                indirectReq.size = uploadSize;
                indirectReq.dst = BufferSlice{m_indirectBuffer, 0, uploadSize};
                m_uploader.recordCopy(indirectReq, m_uploadArenas[m_currentFrame]);
                
                // Upload chunk origins
                VkDeviceSize originsSize = m_indirectDrawCount * sizeof(glm::vec4);
                UploadRequest originsReq;
                originsReq.src = cpuCullChunkOrigins.data();
                originsReq.size = originsSize;
                originsReq.dst = BufferSlice{m_chunkOriginsBuffer, 0, originsSize};
                m_uploader.recordCopy(originsReq, m_uploadArenas[m_currentFrame]);
            }
            
            // Update culling stats for debug display (CPU path)
            // Use GPU system's slot count as "total" even when using CPU culling
            uint32_t totalChunks = m_gpuCulling.getActiveSlotCount();
            World::CullingStats stats;
            stats.gpuCullingEnabled = m_gpuCullingEnabled;
            stats.gpuCullingReady = m_gpuCulling.isReady();
            stats.totalChunksInCulling = totalChunks;
            stats.visibleDrawCalls = m_indirectDrawCount;
            stats.culledDrawCalls = (totalChunks > m_indirectDrawCount) ? (totalChunks - m_indirectDrawCount) : 0;
            m_world.setCullingStats(stats);
            m_perfOverlayVisibleChunks = m_indirectDrawCount;
            m_perfOverlayTotalChunks = totalChunks;
        }

        updateGModeGeometryDiffCapture();
        
        // C3.2: End upload batch (emits barrier, submission below)
        bool hasUploadWork = m_uploader.hasBatchCopies();
        m_uploader.endBatch();
        
        auto afterCulling = std::chrono::high_resolution_clock::now();
        
        // C3.2: End and submit upload command buffer with timeline signal
        // OPTIMIZATION: Skip submit entirely when no copies were recorded this frame.
        // This avoids an unnecessary vkQueueSubmit + timeline semaphore signal per idle frame.
        vkEndCommandBuffer(uploadCmd);
        
        if (hasUploadWork) {
            // Compute the next timeline value for this upload
            uint64_t nextTimelineValue = m_uploadTimelineValue + 1;
            
            VkTimelineSemaphoreSubmitInfo timelineInfo{};
            timelineInfo.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
            timelineInfo.signalSemaphoreValueCount = 1;
            timelineInfo.pSignalSemaphoreValues = &nextTimelineValue;
            
            VkSubmitInfo uploadSubmit{};
            uploadSubmit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
            uploadSubmit.pNext = &timelineInfo;
            uploadSubmit.commandBufferCount = 1;
            uploadSubmit.pCommandBuffers = &uploadCmd;  // C3.2: Use per-frame command buffer
            uploadSubmit.signalSemaphoreCount = 1;
            uploadSubmit.pSignalSemaphores = &m_uploadTimeline;
            
            VkResult uploadResult = vkQueueSubmit(m_graphicsQueue, 1, &uploadSubmit, VK_NULL_HANDLE);
            if (uploadResult != VK_SUCCESS) {
                std::cerr << "[Error] Upload queue submit failed with result: " << uploadResult << std::endl;
                if (uploadResult == VK_ERROR_DEVICE_LOST) {
                    throw std::runtime_error("Vulkan device lost during upload - cannot recover");
                }
                // Skip this frame's rendering if upload failed
                continue;
            }
            // Only increment timeline value after successful submit
            m_uploadTimelineValue = nextTimelineValue;
        }
        
        // C3.2: Log submit with timeline value
        static int frameNum = 0;
        if (frameNum % 60 == 0) {
            static int uploadLogFrame = 0;
            if (++uploadLogFrame % 3000 == 0) {
                std::cout << "[C3.2] Upload submit (frame " << frameNum 
                          << ", timeline=" << m_uploadTimelineValue << ")" << std::endl;
            }
        }
        frameNum++;
        
        auto afterUploadSubmit = std::chrono::high_resolution_clock::now();
        
        // C3.2: Graphics submit will wait on timeline (no CPU wait)
        // Then record and submit graphics work
        auto renderStart = std::chrono::high_resolution_clock::now();
        try {
            drawFrame();
            m_frameCounter++; // Increment frame counter for debug window
        } catch (const std::exception& e) {
            std::cerr << "[Engine] EXCEPTION in drawFrame: " << e.what() << std::endl;
            throw;
        }
        auto renderEnd = std::chrono::high_resolution_clock::now();
        
        // Build comprehensive CPU breakdown
        auto frameEnd = std::chrono::high_resolution_clock::now();
        
        InGameDebug::DebugInfo::CPUBreakdown breakdown;
        breakdown.pollEventsMs = std::chrono::duration<float, std::milli>(afterPollEvents - frameStart).count();
        breakdown.inputMs = std::chrono::duration<float, std::milli>(afterInput - afterPollEvents).count();
        breakdown.physicsMs = std::chrono::duration<float, std::milli>(afterPhysics - afterInput).count();
        breakdown.cloudsMs = std::chrono::duration<float, std::milli>(afterClouds - beforeClouds).count();
        breakdown.fenceWaitMs = std::chrono::duration<float, std::milli>(afterFence - beforeFence).count();
        breakdown.readbackMs = std::chrono::duration<float, std::milli>(afterReadback - afterFence).count();
        breakdown.uploadSetupMs = std::chrono::duration<float, std::milli>(afterUploadSetup - afterReadback).count();
        breakdown.cullingMs = std::chrono::duration<float, std::milli>(afterCulling - beforeCulling).count();
        breakdown.uploadSubmitMs = std::chrono::duration<float, std::milli>(afterUploadSubmit - afterCulling).count();
        breakdown.renderMs = std::chrono::duration<float, std::milli>(renderEnd - renderStart).count();
        breakdown.presentWaitMs = m_presentWaitMs;  // VSync wait hiding inside drawFrame
        breakdown.imguiMs = m_imguiMs;              // ImGui + debug overlay
        breakdown.cmdRecordMs = m_cmdRecordMs;      // Command recording + submit + present
        breakdown.totalFrameMs = std::chrono::duration<float, std::milli>(frameEnd - frameStart).count();
        
        // World sub-sections (already set in World::update -> DebugInfo)
        const auto& worldBd = m_world.getLastUpdateBreakdown();
        breakdown.worldUpdateMs = worldBd.worldUpdateMs;
        breakdown.chunkLoadingMs = worldBd.chunkLoadingMs;
        breakdown.meshingMs = worldBd.meshingMs;
        breakdown.uploadMs = worldBd.uploadMs;
        breakdown.collisionMs = worldBd.collisionMs;
        breakdown.finalizeMs = worldBd.finalizeMs;
        
        // glfwPoll is the raw OS message pump time (DWM compositor sync)
        breakdown.glfwPollMs = std::chrono::duration<float, std::milli>(afterGlfwPoll - frameStart).count();
        // CPU work = total minus fence waits AND glfwPoll (OS stall, not engine work)
        breakdown.cpuWorkMs = breakdown.totalFrameMs - breakdown.fenceWaitMs - breakdown.presentWaitMs - breakdown.glfwPollMs;
        if (breakdown.cpuWorkMs < 0.0f) breakdown.cpuWorkMs = 0.0f;
        m_lastCpuWorkMs = breakdown.cpuWorkMs;
        
        // Other = unaccounted remainder
        // Note: presentWaitMs is already included inside renderMs (measured within drawFrame),
        // so we must NOT add it again here or we double-count and hide real work in otherMs.
        float accounted = breakdown.pollEventsMs + breakdown.inputMs + breakdown.physicsMs
                        + breakdown.cloudsMs
                        + breakdown.fenceWaitMs + breakdown.readbackMs + breakdown.uploadSetupMs
                        + breakdown.worldUpdateMs + breakdown.cullingMs + breakdown.uploadSubmitMs
                        + breakdown.renderMs;
        breakdown.otherMs = breakdown.totalFrameMs - accounted;
        if (breakdown.otherMs < 0.0f) breakdown.otherMs = 0.0f;

        // ── CPU frame spike detector ──────────────────────────────────────
        // Logs the full CPU breakdown whenever cpuWorkMs exceeds the spike
        // threshold. cpuWorkMs now EXCLUDES glfwPoll (OS message pump stall)
        // so it only fires on real engine work spikes.
        {
            constexpr float kCpuSpikeThresholdMs = 8.0f;
            if (breakdown.cpuWorkMs > kCpuSpikeThresholdMs) {
                static uint32_t spikeCount = 0;
                ++spikeCount;
                if (TerminalLogConfig::cpuSpikes) {
                const float preInputMs = breakdown.pollEventsMs - breakdown.glfwPollMs;
                // Rate-limit: print at most every 30 spikes to avoid spam
                if (spikeCount <= 20 || (spikeCount % 30) == 0) {
                    std::cout << "[CPU SPIKE #" << spikeCount << "] cpuWork=" << breakdown.cpuWorkMs
                        << "ms  total=" << breakdown.totalFrameMs
                        << "  glfwPoll=" << breakdown.glfwPollMs
                        << "  preInput=" << preInputMs
                        << "  input=" << breakdown.inputMs
                        << "  phys=" << breakdown.physicsMs
                        << "  clouds=" << breakdown.cloudsMs
                        << "  fence=" << breakdown.fenceWaitMs
                        << "  rdbk=" << breakdown.readbackMs
                        << "  upSetup=" << breakdown.uploadSetupMs
                        << "  world=" << breakdown.worldUpdateMs
                        << " (load=" << breakdown.chunkLoadingMs
                        << " mesh=" << breakdown.meshingMs
                        << " up=" << breakdown.uploadMs
                        << " coll=" << breakdown.collisionMs
                        << " fin=" << breakdown.finalizeMs << ")"
                        << "  cull=" << breakdown.cullingMs
                        << "  upSub=" << breakdown.uploadSubmitMs
                        << "  render=" << breakdown.renderMs
                        << " (imgui=" << breakdown.imguiMs
                        << " cmd=" << breakdown.cmdRecordMs
                        << " present=" << breakdown.presentWaitMs << ")"
                        << "  other=" << breakdown.otherMs
                        << std::endl;
                }
                } // TerminalLogConfig::cpuSpikes
            }
        }

        recordFrameBottleneckSample(breakdown);

        if (m_perfMode && m_perfOverlayEnabled) {
            updatePerfOverlayStats(breakdown);
        }

        if (!m_perfMode) {
            // Update CPU breakdown display
            m_world.getDebugOverlay().updateCPUBreakdown(breakdown);
            // Update FPS profiler window
            const bool gameplayDetached =
                m_gameplaySeparated &&
                m_gameplayWindow &&
                m_gameplayWindow->isOpen();
            GLFWwindow* gameplayRefreshWindow = gameplayDetached
                ? m_gameplayWindow->getHandle()
                : m_window;
            const int monitorHz = detectWindowMonitorHz(gameplayRefreshWindow);
            const float screenFrameMs = static_cast<float>(
                (gameplayDetached && m_lastScreenFrameMs > 0.0)
                    ? m_lastScreenFrameMs
                    : actualFrameMs);
            m_world.getDebugOverlay().getFPSProfilerWindow().update(
                screenFrameMs,
                static_cast<float>(m_lastGpuFrameMs),
                monitorHz,
                m_vsyncEnabled);
            
            // Only the main window's monitor changes require recreating the
            // main swapchain. Detached gameplay uses its own surface.
            if (!gameplayDetached &&
                m_lastMonitorHz != 0 &&
                monitorHz != m_lastMonitorHz) {
                std::cout << "[Engine] Monitor changed: " << m_lastMonitorHz << " Hz -> " << monitorHz << " Hz, recreating swapchain" << std::endl;
                recreateSwapchain();
            }
            m_lastMonitorHz = monitorHz;
        }

        if (m_hiZPyramid.isReady() && m_world.getDebugOverlay().isHiZWindowOpen()) {
            const auto hiZStats = m_gpuCulling.getDebugStats();
            HiZPyramid::DiagnosticsSample sample{};
            sample.timestampSeconds = glfwGetTime();
            sample.mode = m_lastCollectedTemporalHiZ
                    ? HiZPyramid::DiagnosticsMode::TemporalHiZ
                    : HiZPyramid::DiagnosticsMode::FrustumOnly;

            sample.cpuFrameMs = breakdown.totalFrameMs;
            sample.cpuWorkMs = breakdown.cpuWorkMs;
            sample.cpuCullingSetupMs = breakdown.cullingMs;
            sample.cpuCmdRecordMs = breakdown.cmdRecordMs;
            sample.cpuInitialCullRecordMs = m_cpuInitialCullRecordMs;
            sample.cpuDepthPrepassRecordMs = m_cpuDepthPrepassRecordMs;
            sample.cpuHiZBuildRecordMs = m_cpuHiZBuildRecordMs;
            sample.cpuFinalCullRecordMs = m_cpuFinalCullRecordMs;
            sample.cpuHiZIncrementalRecordMs = m_cpuHiZIncrementalRecordMs;

            sample.gpuFrameMs = static_cast<float>(m_lastGpuFrameMs);
            sample.gpuInitialCullMs = static_cast<float>(m_gpuInitialCullMs);
            sample.gpuDepthPrepassMs = static_cast<float>(m_gpuDepthPrepassMs);
            sample.gpuHiZBuildMs = static_cast<float>(m_gpuHiZBuildMs);
            sample.gpuFinalCullMs = static_cast<float>(m_gpuFinalCullMs);
            sample.gpuTerrainMs = static_cast<float>(m_terrainLightingMs);
            sample.gpuHiZIncrementalMs = static_cast<float>(m_gpuHiZIncrementalMs);

            sample.frustumPassed = hiZStats.frustumPassed;
            sample.hiZOccluded = hiZStats.hiZOccluded;
            sample.hiZNearPlaneFail = hiZStats.hiZNearPlaneFail;
            sample.pyramidNonZero = hiZStats.pyramidNonZero;
            sample.pyramidAllZero = hiZStats.pyramidAllZero;
            sample.degenerateUV = hiZStats.degenerateUV;
            sample.holeRecoveryFail = hiZStats.holeRecoveryFail;
            sample.hiZDepthTestVisible = hiZStats.hiZDepthTestVisible;

            // Camera state for corruption diagnostics
            sample.cameraRotationDeg = m_lastFrameRotationDeg;
            sample.cameraTranslation = m_lastFrameTranslation;
            const CameraState& diagCamState = m_camera.getState();
            sample.cameraYaw = diagCamState.yaw;
            sample.cameraPitch = diagCamState.pitch;

            // Frame identification for cross-frame-in-flight correlation
            sample.frameInFlightIndex = static_cast<uint32_t>(m_currentFrame);

            // VP matrix fingerprint (diagonal of prevViewProj used for this frame's cull)
            sample.prevVPDiag[0] = m_prevViewProj[0][0];
            sample.prevVPDiag[1] = m_prevViewProj[1][1];
            sample.prevVPDiag[2] = m_prevViewProj[2][2];
            sample.prevVPDiag[3] = m_prevViewProj[3][3];

            // Viewport UV transform sent to shader (store last used values)
            sample.viewportUvTransform[0] = m_prevHiZViewportUvTransform.x;
            sample.viewportUvTransform[1] = m_prevHiZViewportUvTransform.y;
            sample.viewportUvTransform[2] = m_prevHiZViewportUvTransform.z;
            sample.viewportUvTransform[3] = m_prevHiZViewportUvTransform.w;

            m_hiZPyramid.pushDiagnosticsSample(sample);
        }
        
        // --- Frame Rate Limiter ---
        // Read target from FPS profiler debug window (0 = unlimited)
        if (!m_perfMode) {
            auto& fpsWindow = m_world.getDebugOverlay().getFPSProfilerWindow();
            
            // Handle VSync toggle: if user selected VSync mode, enable it; otherwise disable
            bool wantsVSync = fpsWindow.wantsVSync();
            if (wantsVSync != m_vsyncEnabled) {
                m_vsyncEnabled = wantsVSync;
                std::cout << "[Engine] VSync " << (m_vsyncEnabled ? "ENABLED" : "DISABLED")
                          << " (user selected " << (m_vsyncEnabled ? "VSync" : "non-VSync") << " mode)" << std::endl;
                recreateSwapchain();
            }
            
            int targetFPS = fpsWindow.getTargetFPS();
            if (targetFPS > 0) {
                // Frame limiter: two-stage sleep, no spin-wait.
                // Stage 1: sleep most of the remaining time (leave 0.3ms for stage 2).
                // Stage 2: sleep the remainder in a tight short sleep.
                // With timeBeginPeriod(1) the OS sleep granularity is ~1ms, so
                // two short sleeps land within ~0.5ms of target — good enough for
                // display pacing. A spin-wait would peg a CPU core 100% every frame
                // and causes OS-scheduler preemption spikes whenever other windows
                // (VS Code, terminal, volume OSD, etc.) steal the CPU mid-spin.
                using clock = std::chrono::high_resolution_clock;
                double targetFrameMs = 1000.0 / static_cast<double>(targetFPS);
                
                auto now = clock::now();
                double elapsedMs = std::chrono::duration<double, std::milli>(now - frameStart).count();
                double remainMs = targetFrameMs - elapsedMs;
                
                if (remainMs > 1.3) {
                    // Stage 1: sleep all but the last 0.3ms
                    std::this_thread::sleep_for(std::chrono::microseconds(
                        static_cast<int64_t>((remainMs - 0.3) * 1000.0)));
                }
                // Stage 2: one more short sleep to consume the tail without spinning
                now = clock::now();
                remainMs = targetFrameMs - std::chrono::duration<double, std::milli>(now - frameStart).count();
                if (remainMs > 0.05) {
                    std::this_thread::sleep_for(std::chrono::microseconds(
                        static_cast<int64_t>(remainMs * 1000.0)));
                }
            }
        }
        
        // Simple FPS counter with metrics
        m_frameCount++;
        if (currentTime - m_lastFpsTime >= FPS_LOG_INTERVAL_SECONDS) {
            m_frameCount = 0;
            m_lastFpsTime = currentTime;
        }
    }
#ifdef _WIN32
    timeEndPeriod(1);
#endif
    std::cout << "[Engine] Main loop exited - window closed or error" << std::endl;
    vkDeviceWaitIdle(m_device);
}

void Engine::updateCpuVisibleChunkSnapshot(const glm::vec4* chunkOrigins, uint32_t count) {
    m_lastCpuVisibleChunks.clear();
    if (!chunkOrigins || count == 0) {
        ++m_lastCpuVisibleSerial;
        return;
    }

    m_lastCpuVisibleChunks.reserve(count);
    for (uint32_t i = 0; i < count; ++i) {
        const glm::vec4& origin = chunkOrigins[i];
        if (!std::isfinite(origin.x) || !std::isfinite(origin.y) || !std::isfinite(origin.z)) {
            continue;
        }
        m_lastCpuVisibleChunks.emplace_back(
            static_cast<int>(std::lround(origin.x)),
            static_cast<int>(std::lround(origin.y)),
            static_cast<int>(std::lround(origin.z)));
    }

    sortAndUniqueChunkCoords(m_lastCpuVisibleChunks);
    ++m_lastCpuVisibleSerial;
}

void Engine::updateGpuVisibleChunkSnapshot() {
    if (!m_minimapReadback.isReady()) {
        return;
    }

    const uint64_t readbackSerial = m_minimapReadback.getResultSerial();
    if (readbackSerial == 0 || readbackSerial == m_lastGpuVisibleSerial) {
        return;
    }

    const auto& coords = m_minimapReadback.getVisibleChunkCoords();
    m_lastGpuVisibleChunks.clear();
    m_lastGpuVisibleChunks.reserve(coords.size());
    for (const auto& c : coords) {
        m_lastGpuVisibleChunks.emplace_back(c.x, c.y, c.z);
    }
    sortAndUniqueChunkCoords(m_lastGpuVisibleChunks);
    m_lastGpuVisibleSerial = readbackSerial;
}

std::vector<GPUCullingSystem::GModeGeometryDiffRecord> Engine::buildGModeGeometryDiffRecords(
    const std::vector<glm::ivec3>& beforeVisible,
    const std::vector<glm::ivec3>& afterVisible) const {
    std::vector<GPUCullingSystem::GModeGeometryDiffRecord> out;
    constexpr size_t kMaxRows = 4096;

    auto editSnapshot = m_gpuCulling.getEditVisibilitySnapshot();
    std::unordered_map<ChunkCoordKey, GPUCullingSystem::EditVisibilityTrackedChunk, ChunkCoordKeyHasher> trackedByCoord;
    trackedByCoord.reserve(editSnapshot.trackedChunks.size());
    for (const auto& tracked : editSnapshot.trackedChunks) {
        ChunkCoordKey key{tracked.chunkX, tracked.chunkY, tracked.chunkZ};
        trackedByCoord[key] = tracked;
    }

    std::vector<glm::ivec3> missing;
    std::vector<glm::ivec3> added;
    missing.reserve(beforeVisible.size());
    added.reserve(afterVisible.size());

    std::set_difference(beforeVisible.begin(), beforeVisible.end(),
                        afterVisible.begin(), afterVisible.end(),
                        std::back_inserter(missing), chunkCoordLess);
    std::set_difference(afterVisible.begin(), afterVisible.end(),
                        beforeVisible.begin(), beforeVisible.end(),
                        std::back_inserter(added), chunkCoordLess);

    auto appendRows = [&](const std::vector<glm::ivec3>& coords,
                          bool visibleBefore,
                          bool visibleAfter) {
        for (const glm::ivec3& coord : coords) {
            if (out.size() >= kMaxRows) {
                return;
            }

            GPUCullingSystem::GModeGeometryDiffRecord row;
            row.chunkX = coord.x;
            row.chunkY = coord.y;
            row.chunkZ = coord.z;
            row.visibleBefore = visibleBefore;
            row.visibleAfter = visibleAfter;

            const ChunkCoordKey key{coord.x, coord.y, coord.z};
            auto trackedIt = trackedByCoord.find(key);
            if (trackedIt != trackedByCoord.end()) {
                const auto& tracked = trackedIt->second;
                row.hasTrackedState = true;
                row.trackedState = tracked.state;
                row.fromTerrainEdit = tracked.fromTerrainEdit;
                row.replacesExistingMesh = tracked.replacesExistingMesh;
                row.hiZEnabled = tracked.hiZEnabled;
                row.hiZActive = tracked.hiZActive;
                row.frustumPassed = tracked.frustumPassed;
                row.ready = tracked.ready;
                row.currentTimeline = tracked.currentTimeline;
                row.gpuReadyTimeline = tracked.gpuReadyTimeline;
                row.hiZGraceTimeline = tracked.hiZGraceTimeline;
                row.graceDelta = tracked.graceDelta;
                row.nearestDepth = tracked.nearestDepth;
                row.pyramidDepth = tracked.pyramidDepth;
                row.mipLevel = tracked.mipLevel;
                row.editUploadSerial = tracked.editUploadSerial;
            }
            out.push_back(row);
        }
    };

    appendRows(missing, true, false);
    appendRows(added, false, true);
    return out;
}

void Engine::beginGModeGeometryDiffCapture(bool beforeGpuMode, bool afterGpuMode) {
    m_gModeGeometryDiffCapture.active = true;
    m_gModeGeometryDiffCapture.beforeGpuMode = beforeGpuMode;
    m_gModeGeometryDiffCapture.afterGpuMode = afterGpuMode;
    m_gModeGeometryDiffCapture.toggleSerial = ++m_gModeGeometryToggleSerial;
    m_gModeGeometryDiffCapture.targetAfterSerial =
        afterGpuMode ? m_lastGpuVisibleSerial : m_lastCpuVisibleSerial;
    m_gModeGeometryDiffCapture.timeoutFrames = afterGpuMode ? 90u : 8u;
    m_gModeGeometryDiffCapture.beforeVisible =
        beforeGpuMode ? m_lastGpuVisibleChunks : m_lastCpuVisibleChunks;
    sortAndUniqueChunkCoords(m_gModeGeometryDiffCapture.beforeVisible);

    m_gpuCulling.setGModeGeometryDiffCaptureState(
        true,
        m_gModeGeometryDiffCapture.toggleSerial,
        beforeGpuMode,
        afterGpuMode,
        m_gModeGeometryDiffCapture.timeoutFrames);

    if (afterGpuMode && m_minimapReadback.isReady()) {
        m_minimapReadback.requestImmediateReadback(MINIMAP_FRAMES_IN_FLIGHT + 4);
    }
}

void Engine::updateGModeGeometryDiffCapture() {
    if (!m_gModeGeometryDiffCapture.active) {
        return;
    }

    if (m_gModeGeometryDiffCapture.timeoutFrames > 0) {
        --m_gModeGeometryDiffCapture.timeoutFrames;
    }

    const bool waitingForGpu = m_gModeGeometryDiffCapture.afterGpuMode;
    const uint64_t currentAfterSerial = waitingForGpu ? m_lastGpuVisibleSerial : m_lastCpuVisibleSerial;
    const bool afterReady = currentAfterSerial > m_gModeGeometryDiffCapture.targetAfterSerial;
    const bool timedOut = !afterReady && (m_gModeGeometryDiffCapture.timeoutFrames == 0);
    if (!afterReady && !timedOut) {
        m_gpuCulling.setGModeGeometryDiffCaptureState(
            true,
            m_gModeGeometryDiffCapture.toggleSerial,
            m_gModeGeometryDiffCapture.beforeGpuMode,
            m_gModeGeometryDiffCapture.afterGpuMode,
            m_gModeGeometryDiffCapture.timeoutFrames);
        if (waitingForGpu && m_minimapReadback.isReady()) {
            m_minimapReadback.requestImmediateReadback(1);
        }
        return;
    }

    const std::vector<glm::ivec3>& afterVisible =
        waitingForGpu ? m_lastGpuVisibleChunks : m_lastCpuVisibleChunks;
    auto records = buildGModeGeometryDiffRecords(
        m_gModeGeometryDiffCapture.beforeVisible,
        afterVisible);

    auto reasonForRecord = [&](const GPUCullingSystem::GModeGeometryDiffRecord& rec) -> const char* {
        if (rec.hasTrackedState) {
            return GPUCullingSystem::editVisibilityStateName(rec.trackedState);
        }
        const bool isMissing = rec.visibleBefore && !rec.visibleAfter;
        if (isMissing) {
            return m_gModeGeometryDiffCapture.afterGpuMode
                ? "MissingInGPU.NoEditTrack"
                : "MissingInCPU.NoEditTrack";
        }
        return m_gModeGeometryDiffCapture.afterGpuMode
            ? "AddedInGPU.NoEditTrack"
            : "AddedInCPU.NoEditTrack";
    };

    // Feed per-chunk missing-geometry reasons into the chunk timeline so
    // VRAM chunk inspection can correlate holes with CPU<->GPU visibility diffs.
    //
    // GHOST-GEOMETRY FILTER: only record events for chunks that DON'T have a
    // valid physics collider. Pristine terrain (loaded from terrain.collision)
    // always has a ChunkCollider attached, so a CPU/GPU visibility diff there is
    // just a normal Hi-Z culling decision and not interesting. Render-only chunks
    // (mesh present but no collider) are the actual "ghost geometry" hazard the
    // user wants flagged.
    auto chunkHasValidCollider = [&](const glm::ivec3& coord) -> bool {
        entt::entity entity = m_world.findChunk(coord);
        if (entity == entt::null) {
            return false;
        }
        std::shared_lock regLock(m_world.registryMutex());
        const auto& registry = m_world.getRegistry();
        if (!registry.valid(entity) || !registry.all_of<ChunkCollider>(entity)) {
            return false;
        }
        return registry.get<ChunkCollider>(entity).isValid();
    };

    for (const auto& rec : records) {
        const bool isMissing = rec.visibleBefore && !rec.visibleAfter;
        if (!isMissing) {
            continue;
        }

        const glm::ivec3 coord(rec.chunkX, rec.chunkY, rec.chunkZ);

        // Skip pristine/properly-collided chunks: only flag potential ghost geometry.
        if (chunkHasValidCollider(coord)) {
            continue;
        }

        const char* baseReason = reasonForRecord(rec);

        char reasonText[320];
        std::snprintf(
            reasonText,
            sizeof(reasonText),
            "%s | %s->%s | edit=%u replace=%u hiz=%u/%u frustum=%u ready=%u tl=%u gpu=%u grace=%d",
            baseReason,
            GPUCullingSystem::cullingModeName(m_gModeGeometryDiffCapture.beforeGpuMode),
            GPUCullingSystem::cullingModeName(m_gModeGeometryDiffCapture.afterGpuMode),
            rec.fromTerrainEdit ? 1u : 0u,
            rec.replacesExistingMesh ? 1u : 0u,
            rec.hiZEnabled ? 1u : 0u,
            rec.hiZActive ? 1u : 0u,
            rec.frustumPassed ? 1u : 0u,
            rec.ready ? 1u : 0u,
            rec.currentTimeline,
            rec.gpuReadyTimeline,
            rec.graceDelta);

        ChunkDebugAttribution dbg{};
        dbg.fromTerrainEdit = rec.fromTerrainEdit;
        dbg.artifactGeneration = rec.editUploadSerial;
        if (rec.fromTerrainEdit) {
            dbg.artifactSource = ChunkArtifactSource::RuntimeEditBuild;
        }

        m_world.appendChunkVisualError(
            &coord,
            /*lodLevel=*/-1,
            "GModeDiff",
            reasonText,
            static_cast<uint32_t>(m_gModeGeometryDiffCapture.toggleSerial & 0xFFFFFFFFull),
            0,
            0,
            &dbg);
    }

    m_gpuCulling.recordGModeGeometryDiff(
        m_gModeGeometryDiffCapture.toggleSerial,
        m_gModeGeometryDiffCapture.beforeGpuMode,
        m_gModeGeometryDiffCapture.afterGpuMode,
        records,
        timedOut);

    m_gModeGeometryDiffCapture = GModeGeometryDiffCaptureState{};
}

// cleanup(), toggleFullscreen(), toggleGPUCulling() -> EngineCleanup.cpp

// createSwapchain() through createSyncObjects(), bindGpuVisibleOriginsToSlot1() → EngineVulkanInit.cpp
// updateLightingUniforms(), updateCameraUniforms(), updateAOUniforms(),
// updateClusterData() → EngineSubsystemInit.cpp
// createTimestampQueryPool(), destroyTimestampQueryPool() → EngineCleanup.cpp
// cleanupSwapchain(), recreateSwapchain() → EngineCleanup.cpp
// buildFramePassDescriptors(), compileFrameGraph(), prepareFramePassBarriers(),
// finalizeFramePassResources(), recordVoxelOpaquePass() → EngineRenderLoop.cpp / EngineCommandBuffer.cpp
// collectTimestampResults(), drawFrame() → EngineRenderLoop.cpp

````

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

## src\CMakeLists.txt

Description: No CC-DESC found.

````cmake
# GPT-DESC: Defines VulkanVX source targets and shader build helpers.
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

## FUNCTION src/core/engine/Engine.cpp :: Engine::Engine

Source: src/core/engine/Engine.cpp lines 178-201

````cpp
Engine::Engine(int width,
               int height,
               const char* title,
               bool gameplayOnlyMode,
               bool perfMode,
               StartupTerrainPreset startupTerrainPreset)
: m_width(width),
  m_height(height),
  m_title(title),
  m_window(nullptr),
  m_gameplayOnlyMode(gameplayOnlyMode || perfMode),
  m_perfMode(perfMode),
  m_startupTerrainPreset(startupTerrainPreset),
  m_perfOverlayEnabled(perfMode)
{
    // Initialize geometry
    m_cubeMesh = Mesh::createCube();
    
    // Connect TimeManager to LightingSettings
    m_lighting.setTimeManager(&m_timeManager);
    
    initWindow();
    initVulkan();
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::~Engine

Source: src/core/engine/Engine.cpp lines 203-205

````cpp
Engine::~Engine(){
    cleanup();
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::run

Source: src/core/engine/Engine.cpp lines 207-209

````cpp
void Engine::run(){
    mainLoop();
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::initWindow

Source: src/core/engine/Engine.cpp lines 211-280

````cpp
void Engine::initWindow(){
    if (!glfwInit())
        throw std::runtime_error("Failed to init GLFW");

    glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
    
    // Get primary monitor info for default sizing
    GLFWmonitor* primaryMonitor = glfwGetPrimaryMonitor();
    const GLFWvidmode* mode = primaryMonitor ? glfwGetVideoMode(primaryMonitor) : nullptr;
    
    // Create window with requested size if provided, otherwise fallback.
    m_windowedWidth = (m_width > 0) ? m_width : 1600;
    m_windowedHeight = (m_height > 0) ? m_height : 900;
    if (mode) {
        m_windowedPosX = (mode->width - m_windowedWidth) / 2;
        m_windowedPosY = (mode->height - m_windowedHeight) / 2;
    }
    
    // Normal decorated window (not borderless) unless performance mode wants
    // exclusive fullscreen from the start.
    glfwWindowHint(GLFW_DECORATED, m_perfMode ? GLFW_FALSE : GLFW_TRUE);
    
    // Main editor starts maximized; gameplay-only instance uses explicit window size.
    glfwWindowHint(GLFW_MAXIMIZED, m_gameplayOnlyMode ? GLFW_FALSE : GLFW_TRUE);

    if (m_perfMode && primaryMonitor && mode) {
        const int fullscreenWidth = (m_width > 0) ? m_width : mode->width;
        const int fullscreenHeight = (m_height > 0) ? m_height : mode->height;
        glfwWindowHint(GLFW_REFRESH_RATE, mode->refreshRate);
        m_window = glfwCreateWindow(fullscreenWidth, fullscreenHeight, m_title, primaryMonitor, nullptr);
        m_width = fullscreenWidth;
        m_height = fullscreenHeight;
        m_isFullscreen = true;
        std::cout << "[Engine] Performance mode enabled (" << m_width << "x" << m_height
                  << " @ " << mode->refreshRate << " Hz, exclusive fullscreen)" << std::endl;
    } else {
        // Create windowed window (pass nullptr for monitor = windowed mode)
        m_window = glfwCreateWindow(m_windowedWidth, m_windowedHeight, m_title, nullptr, nullptr);
        // Update dimensions (will be updated by framebuffer callback once maximized)
        m_width = m_windowedWidth;
        m_height = m_windowedHeight;
        m_isFullscreen = false;
    }
    if(!m_window) throw std::runtime_error("Failed to create GLFW window");

    setEngineWindowIcon(m_window);

    glfwSetWindowUserPointer(m_window, this);
    
    m_cursorManager.loadDefault();
    
    // Framebuffer resize callback
    auto framebufferResizeCallback = [](GLFWwindow* window, int w, int h){
        auto app = reinterpret_cast<Engine*>(glfwGetWindowUserPointer(window));
        app->m_framebufferResized = true;
    };
    glfwSetFramebufferSizeCallback(m_window, framebufferResizeCallback);
    
    // Initialize input system (handles mouse callback and cursor capture)
    m_input.setCursorManager(&m_cursorManager);
    m_input.init(m_window);
    if (m_gameplayOnlyMode) {
        m_input.setDebugWindowsVisible(false);
        m_world.getDebugOverlay().setUseEngineInterface(false);
    }
    if (m_perfMode) {
        m_input.setDebugWindowsAllowed(false);
        m_world.getDebugOverlay().setDebugUiVisible(false);
    }
}
````


## FUNCTION src/core/engine/Engine.cpp :: detectWindowMonitorHz

Source: src/core/engine/Engine.cpp lines 138-174

````cpp
int detectWindowMonitorHz(GLFWwindow* window) {
    if (!window) {
        return 60;
    }

    if (GLFWmonitor* fullscreenMonitor = glfwGetWindowMonitor(window)) {
        if (const GLFWvidmode* mode = glfwGetVideoMode(fullscreenMonitor)) {
            return mode->refreshRate > 0 ? mode->refreshRate : 60;
        }
    }

    int winX = 0;
    int winY = 0;
    int winW = 0;
    int winH = 0;
    glfwGetWindowPos(window, &winX, &winY);
    glfwGetWindowSize(window, &winW, &winH);

    const int winCenterX = winX + winW / 2;
    const int winCenterY = winY + winH / 2;

    int monCount = 0;
    GLFWmonitor** monitors = glfwGetMonitors(&monCount);
    for (int i = 0; i < monCount; ++i) {
        int mx = 0;
        int my = 0;
        glfwGetMonitorPos(monitors[i], &mx, &my);
        const GLFWvidmode* vm = glfwGetVideoMode(monitors[i]);
        if (vm &&
            winCenterX >= mx && winCenterX < mx + vm->width &&
            winCenterY >= my && winCenterY < my + vm->height) {
            return vm->refreshRate > 0 ? vm->refreshRate : 60;
        }
    }

    return 60;
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::updatePerfOverlayStats

Source: src/core/engine/Engine.cpp lines 289-305

````cpp
void Engine::updatePerfOverlayStats(const InGameDebug::DebugInfo::CPUBreakdown& breakdown) {
    m_lastPerfBreakdown = breakdown;

    const float frameMs = std::max(0.0f, breakdown.totalFrameMs);
    const float fps = (frameMs > 0.001f) ? (1000.0f / frameMs) : 0.0f;
    constexpr float alpha = 0.08f;
    auto smooth = [alpha](float current, float sample) {
        return (current <= 0.001f) ? sample : (current * (1.0f - alpha) + sample * alpha);
    };

    m_perfOverlayAvgFps = smooth(m_perfOverlayAvgFps, fps);
    m_perfOverlayAvgFrameMs = smooth(m_perfOverlayAvgFrameMs, frameMs);
    m_perfOverlayAvgCpuWorkMs = smooth(m_perfOverlayAvgCpuWorkMs, breakdown.cpuWorkMs);
    m_perfOverlayAvgWorldMs = smooth(m_perfOverlayAvgWorldMs, breakdown.worldUpdateMs);
    m_perfOverlayAvgRenderMs = smooth(m_perfOverlayAvgRenderMs, breakdown.renderMs);
    m_perfOverlayAvgCullingMs = smooth(m_perfOverlayAvgCullingMs, breakdown.cullingMs);
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::recordFrameBottleneckSample

Source: src/core/engine/Engine.cpp lines 307-382

````cpp
void Engine::recordFrameBottleneckSample(const InGameDebug::DebugInfo::CPUBreakdown& breakdown) {
    FrameBottleneckSample sample{};
    sample.frame = m_frameCounter;
    sample.totalFrameMs = breakdown.totalFrameMs;
    sample.cpuWorkMs = breakdown.cpuWorkMs;
    sample.gpuFrameMs = static_cast<float>(m_lastGpuFrameMs);
    sample.glfwPollMs = breakdown.glfwPollMs;
    sample.fenceWaitMs = breakdown.fenceWaitMs;
    sample.presentWaitMs = breakdown.presentWaitMs;
    sample.readbackMs = breakdown.readbackMs;
    sample.uploadSetupMs = breakdown.uploadSetupMs;
    sample.worldUpdateMs = breakdown.worldUpdateMs;
    sample.chunkLoadingMs = breakdown.chunkLoadingMs;
    sample.meshingMs = breakdown.meshingMs;
    sample.uploadMs = breakdown.uploadMs;
    sample.collisionMs = breakdown.collisionMs;
    sample.finalizeMs = breakdown.finalizeMs;
    sample.cullingCpuMs = breakdown.cullingMs;
    sample.uploadSubmitMs = breakdown.uploadSubmitMs;
    sample.renderMs = breakdown.renderMs;
    sample.imguiMs = breakdown.imguiMs;
    sample.imguiInterfaceMs = m_imguiInterfaceMs;
    sample.imguiVramMs = m_imguiVramMs;
    sample.imguiCloudMs = m_imguiCloudMs;
    sample.imguiMinimapMs = m_imguiMinimapMs;
    sample.imguiPerfMs = m_imguiPerfMs;
    sample.imguiToolMs = m_imguiToolMs;
    sample.imguiEndFrameMs = m_imguiEndFrameMs;
    const auto& toolsTiming = m_world.getDebugOverlay().getLastToolPanelTiming();
    sample.toolsPanelTotalMs = toolsTiming.totalMs;
    sample.toolsCursorMs = toolsTiming.cursorMs;
    sample.toolsTerrainMs = toolsTiming.terrainMs;
    sample.toolsTextureMs = toolsTiming.texturePaintMs;
    sample.cmdRecordMs = breakdown.cmdRecordMs;
    sample.otherMs = breakdown.otherMs;
    sample.gpuInitialCullMs = static_cast<float>(m_gpuInitialCullMs);
    sample.gpuCullingTotalMs = static_cast<float>(m_cullingTotalMs);
    sample.gpuTerrainMs = static_cast<float>(m_terrainLightingMs);
    sample.activeCullingSlots = m_gpuCulling.getActiveSlotCount();
    sample.gpuCullingEnabled = m_gpuCullingEnabled;
    sample.gpuCullingReady = m_gpuCulling.isReady();
    sample.meshTopologyRevision = m_world.getMeshTopologyVersion();

    GPUCullingSystem::DebugStats cullStats{};
    if (m_gpuCulling.isReady()) {
        cullStats = m_gpuCulling.getDebugStats();
    }
    sample.visibleDraws = cullStats.visibleDraws;
    sample.frustumPassed = cullStats.frustumPassed;
    sample.chunksReady = cullStats.chunksReady;
    sample.hiZOccluded = cullStats.hiZOccluded;

    const auto& shadowFrame = m_shadowSystem.getFrameDiagnostics();
    sample.gpuPointShadowMs = shadowFrame.totalShadowGpuMs;
    sample.selectedShadowLights = shadowFrame.selectedShadowLights;
    sample.eligibleShadowLights = shadowFrame.eligibleShadowLights;

    const auto& sunDiag = m_shadowSystem.getSunShadowDiagnostics();
    const auto& sun = sunDiag.latest;
    sample.gpuSunShadowMs = sun.gpuRenderMs;
    sample.sunCpuTotalMs = sun.cpuTotalMs;
    sample.sunCpuGatherMs = sun.cpuTerrainGatherMs;
    sample.sunCpuHashMs = sun.cpuTerrainHashMs;
    sample.sunCpuCommandMs = sun.cpuCommandRecordMs;
    sample.sunDrawCalls = sun.drawCallCount;
    sample.sunAcceptedChunks = sun.acceptedChunkCount;
    sample.sunCandidateChunks = sun.bboxCandidateChunks;
    sample.sunRenderCacheMissMask = sun.renderCacheMissMask;
    sample.sunGatherCacheMissMask = sun.gatherCacheMissMask;
    sample.sunRenderCacheHit = sun.renderCacheHit;
    sample.sunGatherCacheHit = sun.gatherCacheHit;

    m_frameBottleneckHistory[m_frameBottleneckWriteIdx] = sample;
    m_frameBottleneckWriteIdx = (m_frameBottleneckWriteIdx + 1u) % FRAME_BOTTLENECK_HISTORY;
    m_frameBottleneckCount = std::min(m_frameBottleneckCount + 1u, FRAME_BOTTLENECK_HISTORY);
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::generateFrameBottleneckReport

Source: src/core/engine/Engine.cpp lines 384-740

````cpp
std::string Engine::generateFrameBottleneckReport() const {
    std::vector<FrameBottleneckSample> samples;
    samples.reserve(m_frameBottleneckCount);
    if (m_frameBottleneckCount > 0u) {
        const size_t start = (m_frameBottleneckWriteIdx + FRAME_BOTTLENECK_HISTORY -
                              m_frameBottleneckCount) % FRAME_BOTTLENECK_HISTORY;
        for (size_t i = 0; i < m_frameBottleneckCount; ++i) {
            samples.push_back(m_frameBottleneckHistory[(start + i) % FRAME_BOTTLENECK_HISTORY]);
        }
    }

    const auto textureStats = m_world.getTextureMaterialStore().getStats();
    const auto loadDiag = m_world.getLoadManagementDiag();
    const size_t runtimeVoxelChunks = m_world.getRuntimeVoxelChunkCoords().size();
    const uint64_t meshTopologyRevision = m_world.getMeshTopologyVersion();
    const auto& sunShadowCfg = m_shadowSystem.getSunShadowConfig();

    const auto& finalizeHistory = m_world.getFinalizeDiagHistory();
    size_t finalizeActiveFrames = 0u;
    double finalizeTotal = 0.0;
    float finalizeMax = 0.0f;
    uint64_t finalizeItems = 0u;
    uint64_t finalizeSwapEntities = 0u;
    for (const auto& frame : finalizeHistory) {
        const bool active = frame.totalMs > 0.0f ||
                            frame.finalizeCount > 0u ||
                            frame.lodSwapEntityCount > 0u;
        if (!active) {
            continue;
        }
        ++finalizeActiveFrames;
        finalizeTotal += frame.totalMs;
        finalizeMax = std::max(finalizeMax, frame.totalMs);
        finalizeItems += frame.finalizeCount;
        finalizeSwapEntities += frame.lodSwapEntityCount;
    }
    const float finalizeAvg = finalizeActiveFrames > 0u
        ? static_cast<float>(finalizeTotal / static_cast<double>(finalizeActiveFrames))
        : 0.0f;

    std::ostringstream out;
    out << std::fixed << std::setprecision(3);
    out << "=== FRAME BOTTLENECK DIAGNOSTICS REPORT ===\n";
    out << "Frame samples: " << samples.size() << " / " << FRAME_BOTTLENECK_HISTORY
        << " | Current frame counter: " << m_frameCounter << "\n";
    out << "Note: finalize is only upload finalization + LOD swap cleanup. It does not include "
           "GPU wait, command recording, shadow rendering, main terrain lighting, or present wait.\n\n";

    if (samples.empty()) {
        out << "No frame bottleneck samples have been recorded yet.\n";
        return out.str();
    }

    const auto& first = samples.front();
    const auto& last = samples.back();
    auto avgOf = [&](auto getter) -> float {
        double total = 0.0;
        for (const auto& sample : samples) {
            total += static_cast<double>(getter(sample));
        }
        return static_cast<float>(total / static_cast<double>(samples.size()));
    };
    auto maxOf = [&](auto getter) -> float {
        float value = 0.0f;
        for (const auto& sample : samples) {
            value = std::max(value, static_cast<float>(getter(sample)));
        }
        return value;
    };
    auto score = [](const FrameBottleneckSample& sample) -> float {
        return std::max(sample.totalFrameMs,
                        std::max(sample.cpuWorkMs, sample.gpuFrameMs));
    };

    size_t slowFrames = 0u;
    for (const auto& sample : samples) {
        if (sample.totalFrameMs >= 16.67f ||
            sample.cpuWorkMs >= 16.67f ||
            sample.gpuFrameMs >= 16.67f) {
            ++slowFrames;
        }
    }

    out << "=== CURRENT SNAPSHOT ===\n";
    out << "Total: " << last.totalFrameMs
        << " ms | CPU work: " << last.cpuWorkMs
        << " ms | GPU frame: " << last.gpuFrameMs << " ms\n";
    out << "CPU waits: fence " << last.fenceWaitMs
        << " ms, present/acquire " << last.presentWaitMs
        << " ms, glfwPoll " << last.glfwPollMs << " ms\n";
    out << "CPU work: render " << last.renderMs
        << " ms (cmd " << last.cmdRecordMs << ", imgui " << last.imguiMs << ")"
        << ", world " << last.worldUpdateMs
        << " ms, culling setup " << last.cullingCpuMs
        << " ms, finalize " << last.finalizeMs << " ms\n";
    out << "ImGui phases: interface " << last.imguiInterfaceMs
        << " ms, vram " << last.imguiVramMs
        << " ms, cloud " << last.imguiCloudMs
        << " ms, minimap " << last.imguiMinimapMs
        << " ms, perf " << last.imguiPerfMs
        << " ms, tool/overlay " << last.imguiToolMs
        << " ms, endFrame " << last.imguiEndFrameMs << " ms\n";
    out << "Tools panel: total " << last.toolsPanelTotalMs
        << " ms | cursor " << last.toolsCursorMs
        << " ms, terrain " << last.toolsTerrainMs
        << " ms, texture paint " << last.toolsTextureMs << " ms\n";
    out << "GPU: terrain/light " << last.gpuTerrainMs
        << " ms, cull " << last.gpuCullingTotalMs
        << " ms, point shadows " << last.gpuPointShadowMs
        << " ms, sun shadow " << last.gpuSunShadowMs << " ms\n";
    out << "Culling slots: active " << last.activeCullingSlots
        << ", ready " << last.chunksReady
        << ", visible draws " << last.visibleDraws
        << ", frustum passed " << last.frustumPassed
        << ", HiZ occluded " << last.hiZOccluded
        << " | mode " << (last.gpuCullingEnabled ? "GPU" : "CPU")
        << (last.gpuCullingReady ? " ready" : " not-ready") << "\n";
    out << "Shadows: point selected " << last.selectedShadowLights
        << "/" << last.eligibleShadowLights
        << ", sun draw calls " << last.sunDrawCalls
        << ", sun candidates " << last.sunCandidateChunks
        << ", sun accepted " << last.sunAcceptedChunks
        << ", debug mode " << sunShadowCfg.debugMode
        << ", render miss " << formatSunCacheMissMask(last.sunRenderCacheMissMask)
        << ", gather miss " << formatSunCacheMissMask(last.sunGatherCacheMissMask) << "\n\n";

    struct SectionValue {
        const char* name;
        float value;
    };
    std::vector<SectionValue> cpuSections = {
        {"Fence wait", last.fenceWaitMs},
        {"Present/acquire", last.presentWaitMs},
        {"glfwPoll", last.glfwPollMs},
        {"Render total", last.renderMs},
        {"Command record", last.cmdRecordMs},
        {"ImGui/debug UI", last.imguiMs},
        {"ImGui interface", last.imguiInterfaceMs},
        {"ImGui tools/overlay", last.imguiToolMs},
        {"ImGui endFrame", last.imguiEndFrameMs},
        {"ImGui VRAM", last.imguiVramMs},
        {"ImGui minimap", last.imguiMinimapMs},
        {"Tools panel total", last.toolsPanelTotalMs},
        {"Tool: texture paint", last.toolsTextureMs},
        {"Tool: terrain edit", last.toolsTerrainMs},
        {"Tool: cursor", last.toolsCursorMs},
        {"World update", last.worldUpdateMs},
        {"Chunk loading", last.chunkLoadingMs},
        {"Meshing", last.meshingMs},
        {"Upload", last.uploadMs},
        {"Collision", last.collisionMs},
        {"Finalize", last.finalizeMs},
        {"Culling CPU", last.cullingCpuMs},
        {"Readback", last.readbackMs},
        {"Upload submit", last.uploadSubmitMs},
        {"Other", last.otherMs},
    };
    std::sort(cpuSections.begin(), cpuSections.end(),
              [](const SectionValue& a, const SectionValue& b) {
                  return a.value > b.value;
              });
    out << "=== CURRENT CPU SUSPECTS ===\n";
    for (size_t i = 0; i < std::min<size_t>(cpuSections.size(), 10u); ++i) {
        out << std::setw(18) << std::left << cpuSections[i].name << std::right
            << " " << std::setw(8) << cpuSections[i].value << " ms\n";
    }
    out << "\n";

    std::vector<SectionValue> gpuSections = {
        {"GPU frame", last.gpuFrameMs},
        {"Terrain/light", last.gpuTerrainMs},
        {"Point shadows", last.gpuPointShadowMs},
        {"Sun shadow", last.gpuSunShadowMs},
        {"GPU culling", last.gpuCullingTotalMs},
        {"Initial cull", last.gpuInitialCullMs},
    };
    std::sort(gpuSections.begin(), gpuSections.end(),
              [](const SectionValue& a, const SectionValue& b) {
                  return a.value > b.value;
              });
    out << "=== CURRENT GPU SUSPECTS ===\n";
    for (const auto& section : gpuSections) {
        out << std::setw(18) << std::left << section.name << std::right
            << " " << std::setw(8) << section.value << " ms\n";
    }
    out << "\n";

    out << "=== ROLLING WINDOW SUMMARY ===\n";
    out << "Slow frames (>=16.67ms CPU/GPU/total): " << slowFrames
        << " / " << samples.size() << "\n";
    out << "Metric            Avg      Max\n";
    out << "Total frame   " << std::setw(8) << avgOf([](const auto& s) { return s.totalFrameMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.totalFrameMs; }) << "\n";
    out << "CPU work      " << std::setw(8) << avgOf([](const auto& s) { return s.cpuWorkMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.cpuWorkMs; }) << "\n";
    out << "GPU frame     " << std::setw(8) << avgOf([](const auto& s) { return s.gpuFrameMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuFrameMs; }) << "\n";
    out << "Fence wait    " << std::setw(8) << avgOf([](const auto& s) { return s.fenceWaitMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.fenceWaitMs; }) << "\n";
    out << "Present wait  " << std::setw(8) << avgOf([](const auto& s) { return s.presentWaitMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.presentWaitMs; }) << "\n";
    out << "Render CPU    " << std::setw(8) << avgOf([](const auto& s) { return s.renderMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.renderMs; }) << "\n";
    out << "World update  " << std::setw(8) << avgOf([](const auto& s) { return s.worldUpdateMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.worldUpdateMs; }) << "\n";
    out << "Finalize      " << std::setw(8) << avgOf([](const auto& s) { return s.finalizeMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.finalizeMs; }) << "\n";
    out << "GPU terrain   " << std::setw(8) << avgOf([](const auto& s) { return s.gpuTerrainMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuTerrainMs; }) << "\n";
    out << "GPU culling   " << std::setw(8) << avgOf([](const auto& s) { return s.gpuCullingTotalMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuCullingTotalMs; }) << "\n";
    out << "Point shadows " << std::setw(8) << avgOf([](const auto& s) { return s.gpuPointShadowMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuPointShadowMs; }) << "\n";
    out << "Sun shadow    " << std::setw(8) << avgOf([](const auto& s) { return s.gpuSunShadowMs; })
        << " " << std::setw(8) << maxOf([](const auto& s) { return s.gpuSunShadowMs; }) << "\n\n";

    out << "=== EDIT / RESOURCE GROWTH CHECKS ===\n";
    out << "Texture material store: cells " << textureStats.totalCells
        << ", bricks " << textureStats.totalBricks
        << ", stamps " << textureStats.surfaceStampCount
        << ", generation " << textureStats.generation << "\n";
    out << "Texture cells by LOD:";
    const size_t lodCount = sizeof(textureStats.cellsByLOD) / sizeof(textureStats.cellsByLOD[0]);
    for (size_t lod = 0; lod < lodCount; ++lod) {
        if (textureStats.cellsByLOD[lod] == 0u && textureStats.bricksByLOD[lod] == 0u) {
            continue;
        }
        out << " L" << lod << "=" << textureStats.cellsByLOD[lod]
            << "c/" << textureStats.bricksByLOD[lod] << "b";
    }
    out << "\n";
    out << "Material overlay GPU: active " << m_materialOverlayCount
        << " / capacity " << m_materialOverlayCapacity
        << " | max probe " << m_materialOverlayMaxProbe
        << " | buffer " << (static_cast<double>(m_materialOverlayBufferSize) / (1024.0 * 1024.0))
        << " MiB per image\n";
    out << "Runtime voxel chunks: " << runtimeVoxelChunks
        << " | mesh topology revision: " << meshTopologyRevision
        << " (window delta " << (last.meshTopologyRevision - first.meshTopologyRevision) << ")\n";
    out << "Active culling slots delta: "
        << static_cast<int64_t>(last.activeCullingSlots) - static_cast<int64_t>(first.activeCullingSlots)
        << " | visible draw delta: "
        << static_cast<int64_t>(last.visibleDraws) - static_cast<int64_t>(first.visibleDraws)
        << " | GPU frame delta: " << (last.gpuFrameMs - first.gpuFrameMs)
        << " ms | CPU work delta: " << (last.cpuWorkMs - first.cpuWorkMs) << " ms\n\n";

    out << "=== CURRENT QUEUES / STREAMING ===\n";
    out << "Render dist base/effective: " << loadDiag.baseRenderDist
        << "/" << loadDiag.effectiveRenderDist
        << " | extension rings " << loadDiag.extensionRings
        << " | throughput " << loadDiag.measuredThroughput
        << " | buffer pressure " << (loadDiag.bufferPressure ? "yes" : "no") << "\n";
    out << "Pending creates " << loadDiag.pendingCreates
        << ", destroys " << loadDiag.pendingDestroys
        << ", LOD remesh queue " << loadDiag.lodRemeshQueue
        << ", pending LOD remeshes " << loadDiag.pendingLodRemeshes
        << ", edit remesh pending " << loadDiag.editRemeshPending
        << ", upload queue " << loadDiag.uploadQueue
        << ", finalize queue " << loadDiag.finalizeQueue << "\n";
    out << "Finalize history active frames " << finalizeActiveFrames
        << ", avg " << finalizeAvg
        << " ms, max " << finalizeMax
        << " ms, finalized items " << finalizeItems
        << ", LOD swap entities " << finalizeSwapEntities << "\n\n";

    out << "=== AUTOMATIC READ ===\n";
    if (last.fenceWaitMs > 2.0f && last.gpuFrameMs > 8.0f) {
        out << "- CPU is likely waiting for GPU completion. The expensive work is probably in GPU frame, terrain/light, shadows, or culling, not finalize.\n";
    }
    if (last.presentWaitMs > 2.0f && last.presentWaitMs > last.cpuWorkMs) {
        out << "- Present/acquire wait dominates. Check VSync, swapchain pacing, detached gameplay window pacing, or monitor refresh changes.\n";
    }
    if (last.gpuTerrainMs > 4.0f) {
        out << "- Terrain/light pass is expensive. If this grows with texture edits, inspect material shader divergence, active draw count, and shadow sampling counters.\n";
    }
    if (last.gpuPointShadowMs > 2.0f || last.gpuSunShadowMs > 2.0f) {
        out << "- Shadow rendering is expensive. Repeated MeshRevision/TerrainSig/UploadTimeline misses after texture-only edits mean the shadow cache is being invalidated by non-geometric changes.\n";
    }
    if (sunShadowCfg.debugMode != 0) {
        out << "- Sun shadow debug visualization is enabled. If the terrain looks like shadow/cascade tiles, turn Debug Vis off in the Sun Shadow panel.\n";
    }
    if (last.toolsPanelTotalMs > 2.0f) {
        out << "- Tools panel UI is expensive. The per-tool line above shows whether Cursor, Terrain, or Texture Paint is responsible.\n";
    }
    if (last.activeCullingSlots > last.visibleDraws * 8u && last.activeCullingSlots > 1000u) {
        out << "- Many active GPU culling slots are offscreen. Offscreen terrain can still cost culling/shadow-gather work even when main camera visibility is low.\n";
    }
    if (textureStats.totalCells > 0u && last.meshTopologyRevision != first.meshTopologyRevision) {
        out << "- Texture material cells exist and mesh topology changed during the sample window. If no geometry edit happened, that is suspicious and should correlate with shadow cache misses.\n";
    }
    if (last.finalizeMs < 1.0f && last.totalFrameMs > 8.0f) {
        out << "- Finalize is not the bottleneck in the latest sample. Use the CPU/GPU suspect tables above.\n";
    }
    out << "\n";

    std::vector<FrameBottleneckSample> topFrames = samples;
    std::sort(topFrames.begin(), topFrames.end(),
              [&](const FrameBottleneckSample& a, const FrameBottleneckSample& b) {
                  return score(a) > score(b);
              });
    if (topFrames.size() > 20u) {
        topFrames.resize(20u);
    }
    out << "=== TOP BOTTLENECK FRAMES ===\n";
    out << "Frame | Score  | Total  | CPU    | GPU    | Fence | Pres  | Rend  | Cmd   | World | Fin   | GTerr | GCull | PShad | SShad | Slots | Vis | TopoRev | SunMiss\n";
    out << "------|--------|--------|--------|--------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-----|---------|--------\n";
    for (const auto& sample : topFrames) {
        out << std::setw(5) << sample.frame << " | "
            << std::setw(6) << score(sample) << " | "
            << std::setw(6) << sample.totalFrameMs << " | "
            << std::setw(6) << sample.cpuWorkMs << " | "
            << std::setw(6) << sample.gpuFrameMs << " | "
            << std::setw(5) << sample.fenceWaitMs << " | "
            << std::setw(5) << sample.presentWaitMs << " | "
            << std::setw(5) << sample.renderMs << " | "
            << std::setw(5) << sample.cmdRecordMs << " | "
            << std::setw(5) << sample.worldUpdateMs << " | "
            << std::setw(5) << sample.finalizeMs << " | "
            << std::setw(5) << sample.gpuTerrainMs << " | "
            << std::setw(5) << sample.gpuCullingTotalMs << " | "
            << std::setw(5) << sample.gpuPointShadowMs << " | "
            << std::setw(5) << sample.gpuSunShadowMs << " | "
            << std::setw(5) << sample.activeCullingSlots << " | "
            << std::setw(3) << sample.visibleDraws << " | "
            << std::setw(7) << sample.meshTopologyRevision << " | "
            << formatSunCacheMissMask(sample.sunRenderCacheMissMask) << "\n";
    }
    out << "\n";

    out << "=== RECENT FRAME SAMPLES (newest first) ===\n";
    out << "Frame | Total  | CPU    | GPU    | Fence | Pres  | Rend  | Cmd   | World | Fin   | GTerr | GCull | PShad | SShad | Slots | Vis | TopoRev | SunMiss\n";
    out << "------|--------|--------|--------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-------|-----|---------|--------\n";
    const size_t recentCount = std::min<size_t>(samples.size(), 40u);
    for (size_t i = 0; i < recentCount; ++i) {
        const auto& sample = samples[samples.size() - 1u - i];
        out << std::setw(5) << sample.frame << " | "
            << std::setw(6) << sample.totalFrameMs << " | "
            << std::setw(6) << sample.cpuWorkMs << " | "
            << std::setw(6) << sample.gpuFrameMs << " | "
            << std::setw(5) << sample.fenceWaitMs << " | "
            << std::setw(5) << sample.presentWaitMs << " | "
            << std::setw(5) << sample.renderMs << " | "
            << std::setw(5) << sample.cmdRecordMs << " | "
            << std::setw(5) << sample.worldUpdateMs << " | "
            << std::setw(5) << sample.finalizeMs << " | "
            << std::setw(5) << sample.gpuTerrainMs << " | "
            << std::setw(5) << sample.gpuCullingTotalMs << " | "
            << std::setw(5) << sample.gpuPointShadowMs << " | "
            << std::setw(5) << sample.gpuSunShadowMs << " | "
            << std::setw(5) << sample.activeCullingSlots << " | "
            << std::setw(3) << sample.visibleDraws << " | "
            << std::setw(7) << sample.meshTopologyRevision << " | "
            << formatSunCacheMissMask(sample.sunRenderCacheMissMask) << "\n";
    }

    return out.str();
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::shouldSamplePerfOverlayDrawCount

Source: src/core/engine/Engine.cpp lines 742-746

````cpp
bool Engine::shouldSamplePerfOverlayDrawCount() const {
    return m_perfMode &&
           m_perfOverlayEnabled &&
           ((m_frameCounter % PERF_OVERLAY_DRAWCOUNT_SAMPLE_INTERVAL) == 0);
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::mainLoop

Source: src/core/engine/Engine.cpp lines 748-1358

````cpp
void Engine::mainLoop(){
    double lastTime = glfwGetTime();
    
    // Pre-allocated buffers for CPU culling path (avoid per-frame allocation)
    std::vector<VkDrawIndexedIndirectCommand> cpuCullDrawCmds(MAX_INDIRECT_DRAWS);
    std::vector<glm::vec4> cpuCullChunkOrigins(MAX_INDIRECT_DRAWS);
    
    // Track actual frame-to-frame time (includes limiter sleep) for real FPS display
    auto prevFrameStart = std::chrono::high_resolution_clock::now();
    float actualFrameMs = 16.67f; // Bootstrap value
    
#ifdef _WIN32
    // Set Windows timer resolution to 1ms for precise sleep_for / frame limiting.
    // Without this, sleep granularity is ~15ms which destroys frame pacing.
    timeBeginPeriod(1);
#endif
    
    while (!glfwWindowShouldClose(m_window)){
        auto frameStart = std::chrono::high_resolution_clock::now();
        actualFrameMs = std::chrono::duration<float, std::milli>(frameStart - prevFrameStart).count();
        prevFrameStart = frameStart;
        m_lastActualFrameMs = actualFrameMs;
        
        glfwPollEvents();
        auto afterGlfwPoll = std::chrono::high_resolution_clock::now();
        if (!m_perfMode) {
            processShaderHotReload();
        }
        compileFrameGraph();
        
        // Calculate delta time (clamped to prevent CPU-spike cascading)
        double currentTime = glfwGetTime();
        float rawDeltaTime = static_cast<float>(currentTime - lastTime);
        lastTime = currentTime;
        m_lastCpuFrameMs = static_cast<double>(rawDeltaTime) * 1000.0;
        // Clamp to 100ms (10 FPS floor) — prevents a single stall frame from
        // corrupting speed estimation, throughput windows, and adaptive distance.
        // Physics/rendering sees at most 100ms step; the real wall-clock gap is
        // still recorded in m_lastCpuFrameMs for diagnostics.
        float deltaTime = std::min(rawDeltaTime, 0.1f);
        
        auto afterPollEvents = std::chrono::high_resolution_clock::now();
        
        // Update time system (authoritative source for game time)
        m_timeManager.update(deltaTime);
        
        // Update lighting system (day/night cycle - reads from TimeManager)
        m_lighting.updateLighting(deltaTime);
        
        // Poll input and apply lighting hotkeys
        InputState inputState = m_input.pollInput(deltaTime);
        m_imgui.setCursorEnabled(m_input.isCursorEnabled());
        m_input.applyLightingHotkeys(inputState, m_lighting, m_timeManager);
        if (inputState.startTerrainGeneration) {
            startChunkGeneration();
        }
        if (inputState.toggleGameplaySeparation) {
            setGameplaySeparated(!m_gameplaySeparated);
        }
        
        // Handle G key for GPU culling toggle (simple debounce)
        static bool gKeyHeld = false;
        bool gPressed = m_input.isKeyPressed(GLFW_KEY_G);
        if (gPressed && !gKeyHeld) {
            toggleGPUCulling();
        }
        gKeyHeld = gPressed;
        
        // Handle T key for T-junction fix toggle (simple debounce)
        static bool tKeyHeld = false;
        bool tPressed = m_input.isKeyPressed(GLFW_KEY_T);
        if (tPressed && !tKeyHeld) {
            bool enabled = !m_tjunctionFix.isEnabled();
            m_tjunctionFix.setEnabled(enabled);
            std::cout << "[Engine] T-junction fix " << (enabled ? "ENABLED" : "DISABLED") << std::endl;
        }
        tKeyHeld = tPressed;
        
        int gameplayViewportWidth = static_cast<int>(m_swapchainExtent.width);
        int gameplayViewportHeight = static_cast<int>(m_swapchainExtent.height);
        const bool gameplayDetached = m_gameplaySeparated
                                   && m_gameplayWindow
                                   && m_gameplayWindow->isOpen();
        if (gameplayDetached) {
            gameplayViewportWidth = std::max(1, static_cast<int>(m_gameplayWindow->getExtent().width));
            gameplayViewportHeight = std::max(1, static_cast<int>(m_gameplayWindow->getExtent().height));
        } else if (m_input.areDebugWindowsVisible() && m_world.getDebugOverlay().isUsingEngineInterface()) {
            auto& ui = m_world.getDebugOverlay().getEngineInterface();
            if (ui.hasGameplayViewport()) {
                gameplayViewportWidth = std::max(1, ui.getGameplayViewportWidth());
                gameplayViewportHeight = std::max(1, ui.getGameplayViewportHeight());
            }
        }

        // Update input, camera, and player using gameplay viewport dimensions.
        m_input.update(inputState, deltaTime, m_camera, m_player, m_playerCamera,
                       gameplayViewportWidth, gameplayViewportHeight);

        // Keep debug collector gating in sync with runtime debug visibility (Ctrl+7).
        m_world.getDebugOverlay().setDebugUiVisible(m_input.areDebugWindowsVisible());
        
        auto afterInput = std::chrono::high_resolution_clock::now();
        
        // Update physics simulation
        m_physics.update(deltaTime);
        
        auto afterPhysics = std::chrono::high_resolution_clock::now();
        
        // Update dynamic cloud system (movement, merging, evolution)
        // Pass camera position so clouds follow the player
        auto beforeClouds = std::chrono::high_resolution_clock::now();
        const glm::vec3& cameraPos = m_camera.getState().position;
        m_cloudSystem.updateClouds(m_device, deltaTime, static_cast<float>(currentTime), cameraPos);
        auto afterClouds = std::chrono::high_resolution_clock::now();
        
        // Get camera state for rendering and world update
        const CameraState& camState = m_camera.getState();
        
        // Wait for previous frame's work to complete
        auto beforeFence = std::chrono::high_resolution_clock::now();
        PerFrame& frame = m_frames[m_currentFrame];
        vkWaitForFences(m_device, 1, &frame.inFlight, VK_TRUE, UINT64_MAX);
        auto afterFence = std::chrono::high_resolution_clock::now();
        
        // Update GPU culling stats from previous frame's readback (after fence ensures data is ready)
        if (m_gpuCullingEnabled && m_gpuCulling.isReady()) {
            if (!m_perfMode || m_perfOverlayEnabled) {
                m_gpuCulling.updateDrawCountFromReadback();
            }

            if (!m_perfMode) {
                // Process minimap readback if enabled (1-frame delayed data)
                // Use current frame index - we just waited on this frame's fence, so its readback is ready
                // Additional safety: only process if minimap window has been properly initialized with World
                if (m_minimapReadback.isEnabled() && m_minimapReadback.isReady()) {
                    m_minimapReadback.processReadback(static_cast<uint32_t>(m_currentFrame));
                    updateGpuVisibleChunkSnapshot();
                    // Pass readback data to minimap window only if it's safe to do so
                    auto& minimap = m_world.getDebugOverlay().getChunkMinimapWindow();
                    minimap.setVisibleChunks(m_minimapReadback.getVisibleChunkKeys());
                    minimap.setGPUReadbackAvailable(true);
                }
            }
        }
        auto afterReadback = std::chrono::high_resolution_clock::now();
        
        // C3.2: Reset upload arena for this frame (fence ensures previous frame's upload finished)
        m_uploadArenas[m_currentFrame].reset();
        
        // C3.2: Begin per-frame upload command buffer (no CPU wait needed)
        VkCommandBuffer uploadCmd = m_uploadCmds[m_currentFrame];
        vkResetCommandBuffer(uploadCmd, 0);
        VkCommandBufferBeginInfo uploadBeginInfo{};
        uploadBeginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
        uploadBeginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        vkBeginCommandBuffer(uploadCmd, &uploadBeginInfo);
        
        // Begin upload batch
        m_uploader.beginBatch(uploadCmd);
        
        // C3.2: Compute batch timeline value for this frame
        const uint64_t batchValue = m_uploadTimelineValue + 1;

        // Cull / draw gating: by the time this frame's graphics CB executes on
        // GPU, the graphics submit's wait on m_uploadTimeline (at the post-
        // upload-submit m_uploadTimelineValue) guarantees every slot write up
        // to batchValue is complete. So the shader's gpuReadyValue gate can
        // safely treat batchValue as "currently signaled" — eliminates the
        // post-edit invisibility flicker under GPU culling.
        m_frameCullingTimelineValue = batchValue;
        
        // Set batch fence value for fallback tracking
        m_uploadArenas[m_currentFrame].setBatchFenceValue(batchValue);
        
        auto afterUploadSetup = std::chrono::high_resolution_clock::now();
        
        // Update world - full terrain system with allocators
        m_world.update(deltaTime, cameraPos, camState.yaw, &m_vbAllocator, &m_ibAllocator, &m_uploadArenas[m_currentFrame], &m_uploader, batchValue,
                       static_cast<float>(m_lastCpuFrameMs), static_cast<float>(m_lastGpuFrameMs),
                       m_frameVisibleUploadTimelineValue);
        
        // C3.3: Query actual signaled timeline value for world gating
        uint64_t lastSignaled = 0;
        if (m_vkGetSemaphoreCounterValueKHR) {
            m_vkGetSemaphoreCounterValueKHR(m_device, m_uploadTimeline, &lastSignaled);
        }
        m_frameVisibleUploadTimelineValue =
            m_world.hadUploadsThisFrame() ? batchValue : lastSignaled;

        // Drain completed fallback temp buffers
        m_uploadArenas[m_currentFrame].drainTemps(lastSignaled);
        
        // Use camera's pre-computed view-projection matrix for culling
        const glm::mat4& viewProj = camState.viewProj;
        
        // Update minimap with view-projection matrix for accurate frustum culling
        if (!m_perfMode) {
            auto& minimapWindow = m_world.getDebugOverlay().getChunkMinimapWindow();
            minimapWindow.setViewProjMatrix(viewProj);
            
            // Update minimap with actual camera parameters for 1:1 frustum visualization
            float aspect = static_cast<float>(gameplayViewportWidth) / static_cast<float>(gameplayViewportHeight);
            minimapWindow.setCameraInfo(cameraPos, camState.yaw, camState.pitch, camState.fovDegrees,
                                        camState.nearPlane, camState.farPlane, aspect);
            
            // Enable/disable minimap GPU readback based on user checkbox.
            // Also force readback during active G-mode diff capture when the
            // target mode is GPU (needed for deterministic after-toggle snapshot).
            const bool diagnosticReadback =
                m_gModeGeometryDiffCapture.active &&
                m_gModeGeometryDiffCapture.afterGpuMode;
            const bool minimapPanelOpen = m_world.getDebugOverlay().isMinimapWindowOpen();
            bool wantsReadback = (minimapWindow.wantsGPUReadback(minimapPanelOpen) && m_gpuCullingEnabled)
                              || diagnosticReadback;
            m_minimapReadback.setEnabled(wantsReadback);
            if (!wantsReadback) {
                minimapWindow.setGPUReadbackAvailable(false);
            }
        } else {
            m_minimapReadback.setEnabled(false);
        }
        
        // Choose between CPU and GPU culling paths
        auto beforeCulling = std::chrono::high_resolution_clock::now();
        if (m_gpuCullingEnabled && m_gpuCulling.isReady()) {
            // GPU culling path: Phase 2 uses persistent storage
            // Chunk data is uploaded incrementally in ChunkUploadSystem
            // No need to rebuild entire buffer every frame
            // Dispatch over compact active-slot indices (no high-water holes).
            m_gpuCullingChunkCount = m_gpuCulling.getActiveSlotCount();
            
            // m_indirectDrawCount will be determined by GPU (read from drawCountBuffer)
            // Set a marker value so recordCommandBuffer knows to use GPU path
            m_indirectDrawCount = UINT32_MAX;  // Special value = use GPU culling
            
            auto gpuDebugStats = m_gpuCulling.getDebugStats();
            // Keep draw count GPU-resident by default. In non-perf mode we use the
            // debug-stats readback field populated by the cull shader; in perf mode
            // we keep a sparse sampled value.
            const uint32_t sampledVisible = m_gpuCulling.getLastVisibleDrawCount();
            const uint32_t visibleDraws = m_perfMode ? sampledVisible : gpuDebugStats.visibleDraws;
            World::CullingStats stats;
            stats.gpuCullingEnabled = true;
            stats.gpuCullingReady = true;
            stats.totalChunksInCulling = m_gpuCullingChunkCount;
            stats.visibleDrawCalls = visibleDraws;
            stats.culledDrawCalls = (m_gpuCullingChunkCount > visibleDraws) ? (m_gpuCullingChunkCount - visibleDraws) : 0;
            stats.frustumPassed = gpuDebugStats.frustumPassed;
            // GPU timing from timestamp queries
            stats.cullingDispatchMs = static_cast<float>(m_cullingDispatchMs);
            stats.totalCullingMs = static_cast<float>(m_cullingTotalMs);
            m_world.setCullingStats(stats);
            m_perfOverlayVisibleChunks = visibleDraws;
            m_perfOverlayTotalChunks = m_gpuCullingChunkCount;
        } else {
            // CPU culling path: use pre-allocated buffers (no per-frame allocation)
            m_indirectDrawCount = m_world.gatherDrawCommands(
                viewProj,
                cpuCullDrawCmds.data(),
                cpuCullChunkOrigins.data(),
                MAX_INDIRECT_DRAWS,
                m_frameCullingTimelineValue);

            updateCpuVisibleChunkSnapshot(cpuCullChunkOrigins.data(), m_indirectDrawCount);
            
            if (m_indirectDrawCount > 0) {
                // Upload draw commands
                VkDeviceSize uploadSize = m_indirectDrawCount * sizeof(VkDrawIndexedIndirectCommand);
                UploadRequest indirectReq;
                indirectReq.src = cpuCullDrawCmds.data();
                indirectReq.size = uploadSize;
                indirectReq.dst = BufferSlice{m_indirectBuffer, 0, uploadSize};
                m_uploader.recordCopy(indirectReq, m_uploadArenas[m_currentFrame]);
                
                // Upload chunk origins
                VkDeviceSize originsSize = m_indirectDrawCount * sizeof(glm::vec4);
                UploadRequest originsReq;
                originsReq.src = cpuCullChunkOrigins.data();
                originsReq.size = originsSize;
                originsReq.dst = BufferSlice{m_chunkOriginsBuffer, 0, originsSize};
                m_uploader.recordCopy(originsReq, m_uploadArenas[m_currentFrame]);
            }
            
            // Update culling stats for debug display (CPU path)
            // Use GPU system's slot count as "total" even when using CPU culling
            uint32_t totalChunks = m_gpuCulling.getActiveSlotCount();
            World::CullingStats stats;
            stats.gpuCullingEnabled = m_gpuCullingEnabled;
            stats.gpuCullingReady = m_gpuCulling.isReady();
            stats.totalChunksInCulling = totalChunks;
            stats.visibleDrawCalls = m_indirectDrawCount;
            stats.culledDrawCalls = (totalChunks > m_indirectDrawCount) ? (totalChunks - m_indirectDrawCount) : 0;
            m_world.setCullingStats(stats);
            m_perfOverlayVisibleChunks = m_indirectDrawCount;
            m_perfOverlayTotalChunks = totalChunks;
        }

        updateGModeGeometryDiffCapture();
        
        // C3.2: End upload batch (emits barrier, submission below)
        bool hasUploadWork = m_uploader.hasBatchCopies();
        m_uploader.endBatch();
        
        auto afterCulling = std::chrono::high_resolution_clock::now();
        
        // C3.2: End and submit upload command buffer with timeline signal
        // OPTIMIZATION: Skip submit entirely when no copies were recorded this frame.
        // This avoids an unnecessary vkQueueSubmit + timeline semaphore signal per idle frame.
        vkEndCommandBuffer(uploadCmd);
        
        if (hasUploadWork) {
            // Compute the next timeline value for this upload
            uint64_t nextTimelineValue = m_uploadTimelineValue + 1;
            
            VkTimelineSemaphoreSubmitInfo timelineInfo{};
            timelineInfo.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
            timelineInfo.signalSemaphoreValueCount = 1;
            timelineInfo.pSignalSemaphoreValues = &nextTimelineValue;
            
            VkSubmitInfo uploadSubmit{};
            uploadSubmit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
            uploadSubmit.pNext = &timelineInfo;
            uploadSubmit.commandBufferCount = 1;
            uploadSubmit.pCommandBuffers = &uploadCmd;  // C3.2: Use per-frame command buffer
            uploadSubmit.signalSemaphoreCount = 1;
            uploadSubmit.pSignalSemaphores = &m_uploadTimeline;
            
            VkResult uploadResult = vkQueueSubmit(m_graphicsQueue, 1, &uploadSubmit, VK_NULL_HANDLE);
            if (uploadResult != VK_SUCCESS) {
                std::cerr << "[Error] Upload queue submit failed with result: " << uploadResult << std::endl;
                if (uploadResult == VK_ERROR_DEVICE_LOST) {
                    throw std::runtime_error("Vulkan device lost during upload - cannot recover");
                }
                // Skip this frame's rendering if upload failed
                continue;
            }
            // Only increment timeline value after successful submit
            m_uploadTimelineValue = nextTimelineValue;
        }
        
        // C3.2: Log submit with timeline value
        static int frameNum = 0;
        if (frameNum % 60 == 0) {
            static int uploadLogFrame = 0;
            if (++uploadLogFrame % 3000 == 0) {
                std::cout << "[C3.2] Upload submit (frame " << frameNum 
                          << ", timeline=" << m_uploadTimelineValue << ")" << std::endl;
            }
        }
        frameNum++;
        
        auto afterUploadSubmit = std::chrono::high_resolution_clock::now();
        
        // C3.2: Graphics submit will wait on timeline (no CPU wait)
        // Then record and submit graphics work
        auto renderStart = std::chrono::high_resolution_clock::now();
        try {
            drawFrame();
            m_frameCounter++; // Increment frame counter for debug window
        } catch (const std::exception& e) {
            std::cerr << "[Engine] EXCEPTION in drawFrame: " << e.what() << std::endl;
            throw;
        }
        auto renderEnd = std::chrono::high_resolution_clock::now();
        
        // Build comprehensive CPU breakdown
        auto frameEnd = std::chrono::high_resolution_clock::now();
        
        InGameDebug::DebugInfo::CPUBreakdown breakdown;
        breakdown.pollEventsMs = std::chrono::duration<float, std::milli>(afterPollEvents - frameStart).count();
        breakdown.inputMs = std::chrono::duration<float, std::milli>(afterInput - afterPollEvents).count();
        breakdown.physicsMs = std::chrono::duration<float, std::milli>(afterPhysics - afterInput).count();
        breakdown.cloudsMs = std::chrono::duration<float, std::milli>(afterClouds - beforeClouds).count();
        breakdown.fenceWaitMs = std::chrono::duration<float, std::milli>(afterFence - beforeFence).count();
        breakdown.readbackMs = std::chrono::duration<float, std::milli>(afterReadback - afterFence).count();
        breakdown.uploadSetupMs = std::chrono::duration<float, std::milli>(afterUploadSetup - afterReadback).count();
        breakdown.cullingMs = std::chrono::duration<float, std::milli>(afterCulling - beforeCulling).count();
        breakdown.uploadSubmitMs = std::chrono::duration<float, std::milli>(afterUploadSubmit - afterCulling).count();
        breakdown.renderMs = std::chrono::duration<float, std::milli>(renderEnd - renderStart).count();
        breakdown.presentWaitMs = m_presentWaitMs;  // VSync wait hiding inside drawFrame
        breakdown.imguiMs = m_imguiMs;              // ImGui + debug overlay
        breakdown.cmdRecordMs = m_cmdRecordMs;      // Command recording + submit + present
        breakdown.totalFrameMs = std::chrono::duration<float, std::milli>(frameEnd - frameStart).count();
        
        // World sub-sections (already set in World::update -> DebugInfo)
        const auto& worldBd = m_world.getLastUpdateBreakdown();
        breakdown.worldUpdateMs = worldBd.worldUpdateMs;
        breakdown.chunkLoadingMs = worldBd.chunkLoadingMs;
        breakdown.meshingMs = worldBd.meshingMs;
        breakdown.uploadMs = worldBd.uploadMs;
        breakdown.collisionMs = worldBd.collisionMs;
        breakdown.finalizeMs = worldBd.finalizeMs;
        
        // glfwPoll is the raw OS message pump time (DWM compositor sync)
        breakdown.glfwPollMs = std::chrono::duration<float, std::milli>(afterGlfwPoll - frameStart).count();
        // CPU work = total minus fence waits AND glfwPoll (OS stall, not engine work)
        breakdown.cpuWorkMs = breakdown.totalFrameMs - breakdown.fenceWaitMs - breakdown.presentWaitMs - breakdown.glfwPollMs;
        if (breakdown.cpuWorkMs < 0.0f) breakdown.cpuWorkMs = 0.0f;
        m_lastCpuWorkMs = breakdown.cpuWorkMs;
        
        // Other = unaccounted remainder
        // Note: presentWaitMs is already included inside renderMs (measured within drawFrame),
        // so we must NOT add it again here or we double-count and hide real work in otherMs.
        float accounted = breakdown.pollEventsMs + breakdown.inputMs + breakdown.physicsMs
                        + breakdown.cloudsMs
                        + breakdown.fenceWaitMs + breakdown.readbackMs + breakdown.uploadSetupMs
                        + breakdown.worldUpdateMs + breakdown.cullingMs + breakdown.uploadSubmitMs
                        + breakdown.renderMs;
        breakdown.otherMs = breakdown.totalFrameMs - accounted;
        if (breakdown.otherMs < 0.0f) breakdown.otherMs = 0.0f;

        // ── CPU frame spike detector ──────────────────────────────────────
        // Logs the full CPU breakdown whenever cpuWorkMs exceeds the spike
        // threshold. cpuWorkMs now EXCLUDES glfwPoll (OS message pump stall)
        // so it only fires on real engine work spikes.
        {
            constexpr float kCpuSpikeThresholdMs = 8.0f;
            if (breakdown.cpuWorkMs > kCpuSpikeThresholdMs) {
                static uint32_t spikeCount = 0;
                ++spikeCount;
                if (TerminalLogConfig::cpuSpikes) {
                const float preInputMs = breakdown.pollEventsMs - breakdown.glfwPollMs;
                // Rate-limit: print at most every 30 spikes to avoid spam
                if (spikeCount <= 20 || (spikeCount % 30) == 0) {
                    std::cout << "[CPU SPIKE #" << spikeCount << "] cpuWork=" << breakdown.cpuWorkMs
                        << "ms  total=" << breakdown.totalFrameMs
                        << "  glfwPoll=" << breakdown.glfwPollMs
                        << "  preInput=" << preInputMs
                        << "  input=" << breakdown.inputMs
                        << "  phys=" << breakdown.physicsMs
                        << "  clouds=" << breakdown.cloudsMs
                        << "  fence=" << breakdown.fenceWaitMs
                        << "  rdbk=" << breakdown.readbackMs
                        << "  upSetup=" << breakdown.uploadSetupMs
                        << "  world=" << breakdown.worldUpdateMs
                        << " (load=" << breakdown.chunkLoadingMs
                        << " mesh=" << breakdown.meshingMs
                        << " up=" << breakdown.uploadMs
                        << " coll=" << breakdown.collisionMs
                        << " fin=" << breakdown.finalizeMs << ")"
                        << "  cull=" << breakdown.cullingMs
                        << "  upSub=" << breakdown.uploadSubmitMs
                        << "  render=" << breakdown.renderMs
                        << " (imgui=" << breakdown.imguiMs
                        << " cmd=" << breakdown.cmdRecordMs
                        << " present=" << breakdown.presentWaitMs << ")"
                        << "  other=" << breakdown.otherMs
                        << std::endl;
                }
                } // TerminalLogConfig::cpuSpikes
            }
        }

        recordFrameBottleneckSample(breakdown);

        if (m_perfMode && m_perfOverlayEnabled) {
            updatePerfOverlayStats(breakdown);
        }

        if (!m_perfMode) {
            // Update CPU breakdown display
            m_world.getDebugOverlay().updateCPUBreakdown(breakdown);
            // Update FPS profiler window
            const bool gameplayDetached =
                m_gameplaySeparated &&
                m_gameplayWindow &&
                m_gameplayWindow->isOpen();
            GLFWwindow* gameplayRefreshWindow = gameplayDetached
                ? m_gameplayWindow->getHandle()
                : m_window;
            const int monitorHz = detectWindowMonitorHz(gameplayRefreshWindow);
            const float screenFrameMs = static_cast<float>(
                (gameplayDetached && m_lastScreenFrameMs > 0.0)
                    ? m_lastScreenFrameMs
                    : actualFrameMs);
            m_world.getDebugOverlay().getFPSProfilerWindow().update(
                screenFrameMs,
                static_cast<float>(m_lastGpuFrameMs),
                monitorHz,
                m_vsyncEnabled);
            
            // Only the main window's monitor changes require recreating the
            // main swapchain. Detached gameplay uses its own surface.
            if (!gameplayDetached &&
                m_lastMonitorHz != 0 &&
                monitorHz != m_lastMonitorHz) {
                std::cout << "[Engine] Monitor changed: " << m_lastMonitorHz << " Hz -> " << monitorHz << " Hz, recreating swapchain" << std::endl;
                recreateSwapchain();
            }
            m_lastMonitorHz = monitorHz;
        }

        if (m_hiZPyramid.isReady() && m_world.getDebugOverlay().isHiZWindowOpen()) {
            const auto hiZStats = m_gpuCulling.getDebugStats();
            HiZPyramid::DiagnosticsSample sample{};
            sample.timestampSeconds = glfwGetTime();
            sample.mode = m_lastCollectedTemporalHiZ
                    ? HiZPyramid::DiagnosticsMode::TemporalHiZ
                    : HiZPyramid::DiagnosticsMode::FrustumOnly;

            sample.cpuFrameMs = breakdown.totalFrameMs;
            sample.cpuWorkMs = breakdown.cpuWorkMs;
            sample.cpuCullingSetupMs = breakdown.cullingMs;
            sample.cpuCmdRecordMs = breakdown.cmdRecordMs;
            sample.cpuInitialCullRecordMs = m_cpuInitialCullRecordMs;
            sample.cpuDepthPrepassRecordMs = m_cpuDepthPrepassRecordMs;
            sample.cpuHiZBuildRecordMs = m_cpuHiZBuildRecordMs;
            sample.cpuFinalCullRecordMs = m_cpuFinalCullRecordMs;
            sample.cpuHiZIncrementalRecordMs = m_cpuHiZIncrementalRecordMs;

            sample.gpuFrameMs = static_cast<float>(m_lastGpuFrameMs);
            sample.gpuInitialCullMs = static_cast<float>(m_gpuInitialCullMs);
            sample.gpuDepthPrepassMs = static_cast<float>(m_gpuDepthPrepassMs);
            sample.gpuHiZBuildMs = static_cast<float>(m_gpuHiZBuildMs);
            sample.gpuFinalCullMs = static_cast<float>(m_gpuFinalCullMs);
            sample.gpuTerrainMs = static_cast<float>(m_terrainLightingMs);
            sample.gpuHiZIncrementalMs = static_cast<float>(m_gpuHiZIncrementalMs);

            sample.frustumPassed = hiZStats.frustumPassed;
            sample.hiZOccluded = hiZStats.hiZOccluded;
            sample.hiZNearPlaneFail = hiZStats.hiZNearPlaneFail;
            sample.pyramidNonZero = hiZStats.pyramidNonZero;
            sample.pyramidAllZero = hiZStats.pyramidAllZero;
            sample.degenerateUV = hiZStats.degenerateUV;
            sample.holeRecoveryFail = hiZStats.holeRecoveryFail;
            sample.hiZDepthTestVisible = hiZStats.hiZDepthTestVisible;

            // Camera state for corruption diagnostics
            sample.cameraRotationDeg = m_lastFrameRotationDeg;
            sample.cameraTranslation = m_lastFrameTranslation;
            const CameraState& diagCamState = m_camera.getState();
            sample.cameraYaw = diagCamState.yaw;
            sample.cameraPitch = diagCamState.pitch;

            // Frame identification for cross-frame-in-flight correlation
            sample.frameInFlightIndex = static_cast<uint32_t>(m_currentFrame);

            // VP matrix fingerprint (diagonal of prevViewProj used for this frame's cull)
            sample.prevVPDiag[0] = m_prevViewProj[0][0];
            sample.prevVPDiag[1] = m_prevViewProj[1][1];
            sample.prevVPDiag[2] = m_prevViewProj[2][2];
            sample.prevVPDiag[3] = m_prevViewProj[3][3];

            // Viewport UV transform sent to shader (store last used values)
            sample.viewportUvTransform[0] = m_prevHiZViewportUvTransform.x;
            sample.viewportUvTransform[1] = m_prevHiZViewportUvTransform.y;
            sample.viewportUvTransform[2] = m_prevHiZViewportUvTransform.z;
            sample.viewportUvTransform[3] = m_prevHiZViewportUvTransform.w;

            m_hiZPyramid.pushDiagnosticsSample(sample);
        }
        
        // --- Frame Rate Limiter ---
        // Read target from FPS profiler debug window (0 = unlimited)
        if (!m_perfMode) {
            auto& fpsWindow = m_world.getDebugOverlay().getFPSProfilerWindow();
            
            // Handle VSync toggle: if user selected VSync mode, enable it; otherwise disable
            bool wantsVSync = fpsWindow.wantsVSync();
            if (wantsVSync != m_vsyncEnabled) {
                m_vsyncEnabled = wantsVSync;
                std::cout << "[Engine] VSync " << (m_vsyncEnabled ? "ENABLED" : "DISABLED")
                          << " (user selected " << (m_vsyncEnabled ? "VSync" : "non-VSync") << " mode)" << std::endl;
                recreateSwapchain();
            }
            
            int targetFPS = fpsWindow.getTargetFPS();
            if (targetFPS > 0) {
                // Frame limiter: two-stage sleep, no spin-wait.
                // Stage 1: sleep most of the remaining time (leave 0.3ms for stage 2).
                // Stage 2: sleep the remainder in a tight short sleep.
                // With timeBeginPeriod(1) the OS sleep granularity is ~1ms, so
                // two short sleeps land within ~0.5ms of target — good enough for
                // display pacing. A spin-wait would peg a CPU core 100% every frame
                // and causes OS-scheduler preemption spikes whenever other windows
                // (VS Code, terminal, volume OSD, etc.) steal the CPU mid-spin.
                using clock = std::chrono::high_resolution_clock;
                double targetFrameMs = 1000.0 / static_cast<double>(targetFPS);
                
                auto now = clock::now();
                double elapsedMs = std::chrono::duration<double, std::milli>(now - frameStart).count();
                double remainMs = targetFrameMs - elapsedMs;
                
                if (remainMs > 1.3) {
                    // Stage 1: sleep all but the last 0.3ms
                    std::this_thread::sleep_for(std::chrono::microseconds(
                        static_cast<int64_t>((remainMs - 0.3) * 1000.0)));
                }
                // Stage 2: one more short sleep to consume the tail without spinning
                now = clock::now();
                remainMs = targetFrameMs - std::chrono::duration<double, std::milli>(now - frameStart).count();
                if (remainMs > 0.05) {
                    std::this_thread::sleep_for(std::chrono::microseconds(
                        static_cast<int64_t>(remainMs * 1000.0)));
                }
            }
        }
        
        // Simple FPS counter with metrics
        m_frameCount++;
        if (currentTime - m_lastFpsTime >= FPS_LOG_INTERVAL_SECONDS) {
            m_frameCount = 0;
            m_lastFpsTime = currentTime;
        }
    }
#ifdef _WIN32
    timeEndPeriod(1);
#endif
    std::cout << "[Engine] Main loop exited - window closed or error" << std::endl;
    vkDeviceWaitIdle(m_device);
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::updateCpuVisibleChunkSnapshot

Source: src/core/engine/Engine.cpp lines 1360-1381

````cpp
void Engine::updateCpuVisibleChunkSnapshot(const glm::vec4* chunkOrigins, uint32_t count) {
    m_lastCpuVisibleChunks.clear();
    if (!chunkOrigins || count == 0) {
        ++m_lastCpuVisibleSerial;
        return;
    }

    m_lastCpuVisibleChunks.reserve(count);
    for (uint32_t i = 0; i < count; ++i) {
        const glm::vec4& origin = chunkOrigins[i];
        if (!std::isfinite(origin.x) || !std::isfinite(origin.y) || !std::isfinite(origin.z)) {
            continue;
        }
        m_lastCpuVisibleChunks.emplace_back(
            static_cast<int>(std::lround(origin.x)),
            static_cast<int>(std::lround(origin.y)),
            static_cast<int>(std::lround(origin.z)));
    }

    sortAndUniqueChunkCoords(m_lastCpuVisibleChunks);
    ++m_lastCpuVisibleSerial;
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::updateGpuVisibleChunkSnapshot

Source: src/core/engine/Engine.cpp lines 1383-1401

````cpp
void Engine::updateGpuVisibleChunkSnapshot() {
    if (!m_minimapReadback.isReady()) {
        return;
    }

    const uint64_t readbackSerial = m_minimapReadback.getResultSerial();
    if (readbackSerial == 0 || readbackSerial == m_lastGpuVisibleSerial) {
        return;
    }

    const auto& coords = m_minimapReadback.getVisibleChunkCoords();
    m_lastGpuVisibleChunks.clear();
    m_lastGpuVisibleChunks.reserve(coords.size());
    for (const auto& c : coords) {
        m_lastGpuVisibleChunks.emplace_back(c.x, c.y, c.z);
    }
    sortAndUniqueChunkCoords(m_lastGpuVisibleChunks);
    m_lastGpuVisibleSerial = readbackSerial;
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::buildGModeGeometryDiffRecords

Source: src/core/engine/Engine.cpp lines 1403-1472

````cpp
std::vector<GPUCullingSystem::GModeGeometryDiffRecord> Engine::buildGModeGeometryDiffRecords(
    const std::vector<glm::ivec3>& beforeVisible,
    const std::vector<glm::ivec3>& afterVisible) const {
    std::vector<GPUCullingSystem::GModeGeometryDiffRecord> out;
    constexpr size_t kMaxRows = 4096;

    auto editSnapshot = m_gpuCulling.getEditVisibilitySnapshot();
    std::unordered_map<ChunkCoordKey, GPUCullingSystem::EditVisibilityTrackedChunk, ChunkCoordKeyHasher> trackedByCoord;
    trackedByCoord.reserve(editSnapshot.trackedChunks.size());
    for (const auto& tracked : editSnapshot.trackedChunks) {
        ChunkCoordKey key{tracked.chunkX, tracked.chunkY, tracked.chunkZ};
        trackedByCoord[key] = tracked;
    }

    std::vector<glm::ivec3> missing;
    std::vector<glm::ivec3> added;
    missing.reserve(beforeVisible.size());
    added.reserve(afterVisible.size());

    std::set_difference(beforeVisible.begin(), beforeVisible.end(),
                        afterVisible.begin(), afterVisible.end(),
                        std::back_inserter(missing), chunkCoordLess);
    std::set_difference(afterVisible.begin(), afterVisible.end(),
                        beforeVisible.begin(), beforeVisible.end(),
                        std::back_inserter(added), chunkCoordLess);

    auto appendRows = [&](const std::vector<glm::ivec3>& coords,
                          bool visibleBefore,
                          bool visibleAfter) {
        for (const glm::ivec3& coord : coords) {
            if (out.size() >= kMaxRows) {
                return;
            }

            GPUCullingSystem::GModeGeometryDiffRecord row;
            row.chunkX = coord.x;
            row.chunkY = coord.y;
            row.chunkZ = coord.z;
            row.visibleBefore = visibleBefore;
            row.visibleAfter = visibleAfter;

            const ChunkCoordKey key{coord.x, coord.y, coord.z};
            auto trackedIt = trackedByCoord.find(key);
            if (trackedIt != trackedByCoord.end()) {
                const auto& tracked = trackedIt->second;
                row.hasTrackedState = true;
                row.trackedState = tracked.state;
                row.fromTerrainEdit = tracked.fromTerrainEdit;
                row.replacesExistingMesh = tracked.replacesExistingMesh;
                row.hiZEnabled = tracked.hiZEnabled;
                row.hiZActive = tracked.hiZActive;
                row.frustumPassed = tracked.frustumPassed;
                row.ready = tracked.ready;
                row.currentTimeline = tracked.currentTimeline;
                row.gpuReadyTimeline = tracked.gpuReadyTimeline;
                row.hiZGraceTimeline = tracked.hiZGraceTimeline;
                row.graceDelta = tracked.graceDelta;
                row.nearestDepth = tracked.nearestDepth;
                row.pyramidDepth = tracked.pyramidDepth;
                row.mipLevel = tracked.mipLevel;
                row.editUploadSerial = tracked.editUploadSerial;
            }
            out.push_back(row);
        }
    };

    appendRows(missing, true, false);
    appendRows(added, false, true);
    return out;
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::beginGModeGeometryDiffCapture

Source: src/core/engine/Engine.cpp lines 1474-1496

````cpp
void Engine::beginGModeGeometryDiffCapture(bool beforeGpuMode, bool afterGpuMode) {
    m_gModeGeometryDiffCapture.active = true;
    m_gModeGeometryDiffCapture.beforeGpuMode = beforeGpuMode;
    m_gModeGeometryDiffCapture.afterGpuMode = afterGpuMode;
    m_gModeGeometryDiffCapture.toggleSerial = ++m_gModeGeometryToggleSerial;
    m_gModeGeometryDiffCapture.targetAfterSerial =
        afterGpuMode ? m_lastGpuVisibleSerial : m_lastCpuVisibleSerial;
    m_gModeGeometryDiffCapture.timeoutFrames = afterGpuMode ? 90u : 8u;
    m_gModeGeometryDiffCapture.beforeVisible =
        beforeGpuMode ? m_lastGpuVisibleChunks : m_lastCpuVisibleChunks;
    sortAndUniqueChunkCoords(m_gModeGeometryDiffCapture.beforeVisible);

    m_gpuCulling.setGModeGeometryDiffCaptureState(
        true,
        m_gModeGeometryDiffCapture.toggleSerial,
        beforeGpuMode,
        afterGpuMode,
        m_gModeGeometryDiffCapture.timeoutFrames);

    if (afterGpuMode && m_minimapReadback.isReady()) {
        m_minimapReadback.requestImmediateReadback(MINIMAP_FRAMES_IN_FLIGHT + 4);
    }
}
````


## FUNCTION src/core/engine/Engine.cpp :: Engine::updateGModeGeometryDiffCapture

Source: src/core/engine/Engine.cpp lines 1498-1626

````cpp
void Engine::updateGModeGeometryDiffCapture() {
    if (!m_gModeGeometryDiffCapture.active) {
        return;
    }

    if (m_gModeGeometryDiffCapture.timeoutFrames > 0) {
        --m_gModeGeometryDiffCapture.timeoutFrames;
    }

    const bool waitingForGpu = m_gModeGeometryDiffCapture.afterGpuMode;
    const uint64_t currentAfterSerial = waitingForGpu ? m_lastGpuVisibleSerial : m_lastCpuVisibleSerial;
    const bool afterReady = currentAfterSerial > m_gModeGeometryDiffCapture.targetAfterSerial;
    const bool timedOut = !afterReady && (m_gModeGeometryDiffCapture.timeoutFrames == 0);
    if (!afterReady && !timedOut) {
        m_gpuCulling.setGModeGeometryDiffCaptureState(
            true,
            m_gModeGeometryDiffCapture.toggleSerial,
            m_gModeGeometryDiffCapture.beforeGpuMode,
            m_gModeGeometryDiffCapture.afterGpuMode,
            m_gModeGeometryDiffCapture.timeoutFrames);
        if (waitingForGpu && m_minimapReadback.isReady()) {
            m_minimapReadback.requestImmediateReadback(1);
        }
        return;
    }

    const std::vector<glm::ivec3>& afterVisible =
        waitingForGpu ? m_lastGpuVisibleChunks : m_lastCpuVisibleChunks;
    auto records = buildGModeGeometryDiffRecords(
        m_gModeGeometryDiffCapture.beforeVisible,
        afterVisible);

    auto reasonForRecord = [&](const GPUCullingSystem::GModeGeometryDiffRecord& rec) -> const char* {
        if (rec.hasTrackedState) {
            return GPUCullingSystem::editVisibilityStateName(rec.trackedState);
        }
        const bool isMissing = rec.visibleBefore && !rec.visibleAfter;
        if (isMissing) {
            return m_gModeGeometryDiffCapture.afterGpuMode
                ? "MissingInGPU.NoEditTrack"
                : "MissingInCPU.NoEditTrack";
        }
        return m_gModeGeometryDiffCapture.afterGpuMode
            ? "AddedInGPU.NoEditTrack"
            : "AddedInCPU.NoEditTrack";
    };

    // Feed per-chunk missing-geometry reasons into the chunk timeline so
    // VRAM chunk inspection can correlate holes with CPU<->GPU visibility diffs.
    //
    // GHOST-GEOMETRY FILTER: only record events for chunks that DON'T have a
    // valid physics collider. Pristine terrain (loaded from terrain.collision)
    // always has a ChunkCollider attached, so a CPU/GPU visibility diff there is
    // just a normal Hi-Z culling decision and not interesting. Render-only chunks
    // (mesh present but no collider) are the actual "ghost geometry" hazard the
    // user wants flagged.
    auto chunkHasValidCollider = [&](const glm::ivec3& coord) -> bool {
        entt::entity entity = m_world.findChunk(coord);
        if (entity == entt::null) {
            return false;
        }
        std::shared_lock regLock(m_world.registryMutex());
        const auto& registry = m_world.getRegistry();
        if (!registry.valid(entity) || !registry.all_of<ChunkCollider>(entity)) {
            return false;
        }
        return registry.get<ChunkCollider>(entity).isValid();
    };

    for (const auto& rec : records) {
        const bool isMissing = rec.visibleBefore && !rec.visibleAfter;
        if (!isMissing) {
            continue;
        }

        const glm::ivec3 coord(rec.chunkX, rec.chunkY, rec.chunkZ);

        // Skip pristine/properly-collided chunks: only flag potential ghost geometry.
        if (chunkHasValidCollider(coord)) {
            continue;
        }

        const char* baseReason = reasonForRecord(rec);

        char reasonText[320];
        std::snprintf(
            reasonText,
            sizeof(reasonText),
            "%s | %s->%s | edit=%u replace=%u hiz=%u/%u frustum=%u ready=%u tl=%u gpu=%u grace=%d",
            baseReason,
            GPUCullingSystem::cullingModeName(m_gModeGeometryDiffCapture.beforeGpuMode),
            GPUCullingSystem::cullingModeName(m_gModeGeometryDiffCapture.afterGpuMode),
            rec.fromTerrainEdit ? 1u : 0u,
            rec.replacesExistingMesh ? 1u : 0u,
            rec.hiZEnabled ? 1u : 0u,
            rec.hiZActive ? 1u : 0u,
            rec.frustumPassed ? 1u : 0u,
            rec.ready ? 1u : 0u,
            rec.currentTimeline,
            rec.gpuReadyTimeline,
            rec.graceDelta);

        ChunkDebugAttribution dbg{};
        dbg.fromTerrainEdit = rec.fromTerrainEdit;
        dbg.artifactGeneration = rec.editUploadSerial;
        if (rec.fromTerrainEdit) {
            dbg.artifactSource = ChunkArtifactSource::RuntimeEditBuild;
        }

        m_world.appendChunkVisualError(
            &coord,
            /*lodLevel=*/-1,
            "GModeDiff",
            reasonText,
            static_cast<uint32_t>(m_gModeGeometryDiffCapture.toggleSerial & 0xFFFFFFFFull),
            0,
            0,
            &dbg);
    }

    m_gpuCulling.recordGModeGeometryDiff(
        m_gModeGeometryDiffCapture.toggleSerial,
        m_gModeGeometryDiffCapture.beforeGpuMode,
        m_gModeGeometryDiffCapture.afterGpuMode,
        records,
        timedOut);

    m_gModeGeometryDiffCapture = GModeGeometryDiffCaptureState{};
}
````
