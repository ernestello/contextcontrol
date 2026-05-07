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


## src\core\engine\EngineRenderLoop.cpp

Description: No CC-DESC found.

````cpp
// EngineRenderLoop.cpp - Per-frame rendering orchestrator
// Contains: drawFrame, buildFramePassDescriptors, compileFrameGraph
//
// Extracted into separate files:
//   EngineTimestamps.cpp       — collectTimestampResults
//   EngineShadowPass.cpp       — recordShadowRenderPasses, updateShadowsForFrame
//   EngineDepthPrePass.cpp     — recordInitialGPUCulling, recordDepthPrePassAndHiZ, recordPostRenderHiZBuild
//   EngineGameplayRendering.cpp — renderPerfOverlay, pollAndPrepareGameplayWindow,
//                                  recordGameplayWindowUIPass, recordGameplayOverlayFrame
//   EngineCommandBuffer.cpp    — recordCommandBuffer, recordVoxelOpaquePass,
//                                  prepareFramePassBarriers, finalizeFramePassResources

#include "core/engine/Engine.h"
#include "ui/EngineInterface.h"
#include "ui/debug_menu/world/ChunkDebugWindow.h"
#include "ui/debug_menu/world/ChunkMinimapWindow.h"
#include "ui/debug_menu/world/ChunkVramWindow.h"
#include "ui/debug_menu/gameplay/CursorPlaceTool.h"
#include "ui/debug_menu/rendering/DirectionalShadowWindow.h"
#include "ui/debug_menu/rendering/HiZDebugWindow.h"
#include "ui/debug_menu/world/TerrainEditTool.h"
#include "ui/debug_menu/world/TexturePaintTool.h"
#include "debug/TerminalLogConfig.h"
#include <imgui.h>
#include <imgui_impl_glfw.h>
#include <imgui_impl_vulkan.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <thread>

#ifdef _WIN32
#include <intrin.h>
#endif

// Get CPU brand string (cached, computed once) — used by drawFrame stats reporting
static const char* getCPUBrandString() {
    static char brand[49] = {};
    static bool done = false;
    if (!done) {
        done = true;
#ifdef _WIN32
        int regs[4];
        __cpuid(regs, 0x80000000);
        if (static_cast<unsigned>(regs[0]) >= 0x80000004u) {
            __cpuid(reinterpret_cast<int*>(brand +  0), 0x80000002);
            __cpuid(reinterpret_cast<int*>(brand + 16), 0x80000003);
            __cpuid(reinterpret_cast<int*>(brand + 32), 0x80000004);
            brand[48] = 0;
            // Trim leading spaces
            char* p = brand;
            while (*p == ' ') ++p;
            if (p != brand) std::memmove(brand, p, std::strlen(p) + 1);
        } else {
            std::snprintf(brand, sizeof(brand), "Unknown CPU");
        }
#else
        std::snprintf(brand, sizeof(brand), "Unknown CPU");
#endif
    }
    return brand;
}

// renderPerfOverlay → moved to EngineGameplayRendering.cpp

void Engine::buildFramePassDescriptors() {
    const auto worldPassKinds = m_world.enumerateFramePasses();
    EngineFrameGraph::buildFramePassDescriptors(m_framePassDescriptors, worldPassKinds);
    compileFrameGraph();
}

void Engine::compileFrameGraph() {
    EngineFrameGraph::compileFrameGraph(m_frameGraph, m_framePassDescriptors, m_compiledFramePasses);
}

// prepareFramePassBarriers  → moved to EngineCommandBuffer.cpp
// finalizeFramePassResources → moved to EngineCommandBuffer.cpp

// recordVoxelOpaquePass → moved to EngineCommandBuffer.cpp

// recordCommandBuffer → moved to EngineCommandBuffer.cpp

// collectTimestampResults → moved to EngineTimestamps.cpp

void Engine::drawFrame(){
    PerFrame& frame = m_frames[m_currentFrame];
    m_gameplayOverlayFrameActive = false;

    const bool gameplayDetached =
        m_gameplaySeparated &&
        m_gameplayWindow &&
        m_gameplayWindow->isOpen();

    int gameplayViewportW = static_cast<int>(m_swapchainExtent.width);
    int gameplayViewportH = static_cast<int>(m_swapchainExtent.height);
    float gameplayViewportOffsetX = 0.0f;
    float gameplayViewportOffsetY = 0.0f;

    if (gameplayDetached && m_gameplayWindow) {
        // Detached gameplay renders into its own swapchain, so every camera
        // consumer (projection, frustum, Hi-Z UVs, tools, clusters) must use
        // that target's real aspect instead of the main editor swapchain.
        const VkExtent2D gameplayExtent = m_gameplayWindow->getExtent();
        gameplayViewportW = std::max(1, static_cast<int>(gameplayExtent.width));
        gameplayViewportH = std::max(1, static_cast<int>(gameplayExtent.height));
    } else if (m_input.areDebugWindowsVisible() &&
               m_world.getDebugOverlay().isUsingEngineInterface()) {
        auto& ui = m_world.getDebugOverlay().getEngineInterface();
        if (ui.hasGameplayViewport()) {
            gameplayViewportW = std::max(1, ui.getGameplayViewportWidth());
            gameplayViewportH = std::max(1, ui.getGameplayViewportHeight());
            ImVec2 viewportPos = ui.getGameplayViewportPos();
            gameplayViewportOffsetX = viewportPos.x;
            gameplayViewportOffsetY = viewportPos.y;
        }
    }
    VkRect2D gameplayRect{};
    gameplayRect.offset = {0, 0};
    gameplayRect.extent = m_swapchainExtent;
    {
        const int32_t targetW = gameplayDetached
            ? std::max(1, gameplayViewportW)
            : std::max(1, static_cast<int32_t>(m_swapchainExtent.width));
        const int32_t targetH = gameplayDetached
            ? std::max(1, gameplayViewportH)
            : std::max(1, static_cast<int32_t>(m_swapchainExtent.height));

        const int32_t rectX = gameplayDetached
            ? 0
            : std::clamp(static_cast<int32_t>(std::floor(gameplayViewportOffsetX)), 0, std::max(0, targetW - 1));
        const int32_t rectY = gameplayDetached
            ? 0
            : std::clamp(static_cast<int32_t>(std::floor(gameplayViewportOffsetY)), 0, std::max(0, targetH - 1));
        const int32_t maxRectW = std::max(1, targetW - rectX);
        const int32_t maxRectH = std::max(1, targetH - rectY);
        const int32_t rectW = std::clamp(gameplayViewportW, 1, maxRectW);
        const int32_t rectH = std::clamp(gameplayViewportH, 1, maxRectH);
        gameplayRect.offset = {rectX, rectY};
        gameplayRect.extent = {static_cast<uint32_t>(rectW), static_cast<uint32_t>(rectH)};
    }

    // Update Hi-Z debug window viewport so the preview crops to the gameplay region
    if (gameplayDetached) {
        m_world.getDebugOverlay().getHiZDebugWindow().setViewportUV(0.0f, 0.0f, 1.0f, 1.0f);
    } else if (m_swapchainExtent.width > 0 && m_swapchainExtent.height > 0) {
        const float invW = 1.0f / static_cast<float>(m_swapchainExtent.width);
        const float invH = 1.0f / static_cast<float>(m_swapchainExtent.height);
        m_world.getDebugOverlay().getHiZDebugWindow().setViewportUV(
            static_cast<float>(gameplayRect.offset.x) * invW,
            static_cast<float>(gameplayRect.offset.y) * invH,
            static_cast<float>(gameplayRect.offset.x + gameplayRect.extent.width) * invW,
            static_cast<float>(gameplayRect.offset.y + gameplayRect.extent.height) * invH);
    }

    // The camera projection must be recomputed from the exact gameplay render
    // rectangle every frame. When the scene is embedded in an ImGui viewport or
    // detached into a second swapchain, using the main swapchain aspect makes
    // GPU frustum culling, Hi-Z projection, ray tools, and debug frustum cones
    // disagree with the pixels that are actually rendered.
    m_camera.updateMatrices(std::max(1, static_cast<int>(gameplayRect.extent.width)),
                            std::max(1, static_cast<int>(gameplayRect.extent.height)));

    // NOTE: Fence wait and arena reset now done in mainLoop before World::update
    // This ensures upload queue processes with a clean arena

    // Determine whether ImGui is needed this frame.
    // ImGui is required when debug windows are visible OR the HUD minimap is rendering.
    // When neither is active, skip the entire ImGui frame cycle to save CPU overhead.
    const bool debugVisible = !m_perfMode && m_input.areDebugWindowsVisible();
    const bool engineInterfaceActive = m_world.getDebugOverlay().isUsingEngineInterface();
    const bool hudMinimapVisible = !m_perfMode &&
        m_world.getDebugOverlay().getChunkMinimapWindow().isHUDMinimapEnabled();
    auto& cursorTool = m_world.getDebugOverlay().getCursorPlaceTool();
    auto& terrainEditTool = m_world.getDebugOverlay().getTerrainEditTool();
    auto& texturePaintTool = m_world.getDebugOverlay().getTexturePaintTool();
    const bool toolOverlayVisible = !m_perfMode &&
        m_input.isCursorEnabled() &&
        (cursorTool.isActive() || terrainEditTool.isActive() || texturePaintTool.isActive());
    const bool perfOverlayVisible = m_perfMode && m_perfOverlayEnabled;
    const bool gameplayStatsStripVisible =
        !m_perfMode &&
        gameplayDetached &&
        engineInterfaceActive;
    const bool gameplayOverlayRequested = gameplayDetached &&
        (hudMinimapVisible || toolOverlayVisible || gameplayStatsStripVisible);
    m_imguiFrameActive = debugVisible ||
                         (!gameplayDetached && hudMinimapVisible) ||
                         (!gameplayDetached && toolOverlayVisible) ||
                         perfOverlayVisible;

    auto imguiStart = std::chrono::high_resolution_clock::now();
    if (m_imguiFrameActive) {
        m_imgui.beginFrame();
    }
    m_imguiInterfaceMs = 0.0f;
    m_imguiVramMs = 0.0f;
    m_imguiCloudMs = 0.0f;
    m_imguiMinimapMs = 0.0f;
    m_imguiPerfMs = 0.0f;
    m_imguiToolMs = 0.0f;
    m_imguiEndFrameMs = 0.0f;
    
    // Update camera position only when Stats window is actually open.
    if (m_imguiFrameActive && m_world.getDebugOverlay().isStatsWindowOpen()) {
        m_world.getDebugOverlay().getChunkDebugWindow().setCameraPosition(m_camera.getState().position);
    }

    std::vector<PointLight> transientToolLights;
    std::vector<glm::vec4> transientToolPulseData;
    transientToolLights.reserve(4);
    transientToolPulseData.reserve(4);
    m_lightGlowSystem.clearPreviewLights();

    auto pushTransientToolLight = [&](const glm::vec3& position,
                                      float radius,
                                      float intensity,
                                      const glm::vec3& color,
                                      const glm::vec4& glowPulseData,
                                      const glm::vec4& shaderPulseData) {
        PointLight light;
        light.position = position;
        light.radius = std::max(radius, 0.1f);
        light.color = color;
        light.intensity = std::max(intensity, 0.0f);
        light.castsShadow = 1u;
        light.shadowRadius = light.radius;
        light.shadowIntensityScale = 1.0f;
        light.gridStepOverride = 0.0f;

        transientToolLights.push_back(light);
        transientToolPulseData.push_back(shaderPulseData);
        m_lightGlowSystem.addPreviewLight(
            light.position,
            light.radius,
            light.intensity,
            light.color,
            glowPulseData);
    };

    // Update cursor place tool with mouse-based raycast
    {
        const bool terrainEditActive = terrainEditTool.isActive();
        const bool texturePaintActive = texturePaintTool.isActive();
        GLFWwindow* gameplayInputWindow = gameplayDetached ? m_gameplayWindow->getHandle() : m_window;
        const int toolViewportW = gameplayDetached
            ? std::max(1, static_cast<int>(m_gameplayWindow->getExtent().width))
            : gameplayViewportW;
        const int toolViewportH = gameplayDetached
            ? std::max(1, static_cast<int>(m_gameplayWindow->getExtent().height))
            : gameplayViewportH;
        const float toolViewportOffsetX = gameplayDetached ? 0.0f : gameplayViewportOffsetX;
        const float toolViewportOffsetY = gameplayDetached ? 0.0f : gameplayViewportOffsetY;
        if (!terrainEditActive && !texturePaintActive &&
            cursorTool.isActive() && m_input.isCursorEnabled()) {
            double mouseX, mouseY;
            glfwGetCursorPos(gameplayInputWindow, &mouseX, &mouseY);
            const auto& cam = m_camera.getState();
            bool leftClick = glfwGetMouseButton(gameplayInputWindow, GLFW_MOUSE_BUTTON_LEFT) == GLFW_PRESS;
            double localMouseX = mouseX - static_cast<double>(toolViewportOffsetX);
            double localMouseY = mouseY - static_cast<double>(toolViewportOffsetY);
            const bool insideViewport =
                localMouseX >= 0.0 && localMouseX < static_cast<double>(toolViewportW) &&
                localMouseY >= 0.0 && localMouseY < static_cast<double>(toolViewportH);
            if (!insideViewport) {
                leftClick = false;
            }
            localMouseX = std::clamp(localMouseX, 0.0, std::max(0.0, static_cast<double>(toolViewportW) - 1.0));
            localMouseY = std::clamp(localMouseY, 0.0, std::max(0.0, static_cast<double>(toolViewportH) - 1.0));

            cursorTool.update(localMouseX, localMouseY,
                              toolViewportW,
                              toolViewportH,
                              cam.view, cam.proj, cam.position, leftClick,
                              toolViewportOffsetX, toolViewportOffsetY);

            // Feed preview glow to LightGlowSystem (real-time animated orb preview)
            if (cursorTool.getMode() == CursorPlaceTool::PlaceMode::LightOrb && cursorTool.hasPreview()) {
                const auto& preset = m_pulsePresets.getPreset(cursorTool.getLightPulsePresetIndex());
                float currentTime = m_lighting.totalTime;

                // Compute pulse data using same formula as Engine::updateLightingUniforms
                float breathCycle = std::sin(currentTime * preset.speed) * 0.5f + 0.5f;
                float quantized = std::floor(breathCycle * 8.0f) / 8.0f;
                float square = breathCycle > 0.5f ? 1.0f : 0.0f;
                float pulse = glm::mix(quantized, square, preset.sharpness);
                float pulseStrength = glm::clamp(pulse * preset.strength, 0.0f, 1.0f);

                glm::vec4 glowPulseData(breathCycle, pulseStrength,
                                        preset.flickerAmount, preset.flickerSpeed);
                glm::vec4 shaderPulseData(
                    pulseStrength,
                    1.0f + pulseStrength * 0.3f,
                    preset.flickerAmount,
                    preset.flickerSpeed);

                pushTransientToolLight(
                    cursorTool.getPreviewPosition(),
                    cursorTool.getLightRadius(),
                    cursorTool.getLightIntensity(),
                    cursorTool.getLightColor(),
                    glowPulseData,
                    shaderPulseData);
            }
        }

        if (terrainEditTool.isActive() && m_input.isCursorEnabled()) {
            double mouseX, mouseY;
            glfwGetCursorPos(gameplayInputWindow, &mouseX, &mouseY);
            const auto& cam = m_camera.getState();
            bool leftClick = glfwGetMouseButton(gameplayInputWindow, GLFW_MOUSE_BUTTON_LEFT) == GLFW_PRESS;
            double localMouseX = mouseX - static_cast<double>(toolViewportOffsetX);
            double localMouseY = mouseY - static_cast<double>(toolViewportOffsetY);
            const bool insideViewport =
                localMouseX >= 0.0 && localMouseX < static_cast<double>(toolViewportW) &&
                localMouseY >= 0.0 && localMouseY < static_cast<double>(toolViewportH);
            if (!insideViewport) {
                leftClick = false;
            }
            localMouseX = std::clamp(localMouseX, 0.0, std::max(0.0, static_cast<double>(toolViewportW) - 1.0));
            localMouseY = std::clamp(localMouseY, 0.0, std::max(0.0, static_cast<double>(toolViewportH) - 1.0));

            terrainEditTool.update(localMouseX, localMouseY,
                                   toolViewportW,
                                   toolViewportH,
                                   cam.view, cam.proj, cam.position, leftClick,
                                   toolViewportOffsetX, toolViewportOffsetY);
        }

        if (!terrainEditActive && texturePaintActive && m_input.isCursorEnabled()) {
            double mouseX, mouseY;
            glfwGetCursorPos(gameplayInputWindow, &mouseX, &mouseY);
            const auto& cam = m_camera.getState();
            bool leftClick = glfwGetMouseButton(gameplayInputWindow, GLFW_MOUSE_BUTTON_LEFT) == GLFW_PRESS;
            double localMouseX = mouseX - static_cast<double>(toolViewportOffsetX);
            double localMouseY = mouseY - static_cast<double>(toolViewportOffsetY);
            const bool insideViewport =
                localMouseX >= 0.0 && localMouseX < static_cast<double>(toolViewportW) &&
                localMouseY >= 0.0 && localMouseY < static_cast<double>(toolViewportH);
            if (!insideViewport) {
                leftClick = false;
            }
            localMouseX = std::clamp(localMouseX, 0.0, std::max(0.0, static_cast<double>(toolViewportW) - 1.0));
            localMouseY = std::clamp(localMouseY, 0.0, std::max(0.0, static_cast<double>(toolViewportH) - 1.0));

            texturePaintTool.update(localMouseX, localMouseY,
                                    toolViewportW,
                                    toolViewportH,
                                    cam.view, cam.proj, cam.position, leftClick,
                                    toolViewportOffsetX, toolViewportOffsetY);
        }

        if (cursorTool.isHandOrbToolActive()) {
            const auto& cam = m_camera.getState();
            constexpr float kHandForward = 0.42f;
            constexpr float kHandSide = 0.22f;
            constexpr float kHandDown = 0.16f;

            const glm::vec3 handBase =
                cam.position +
                cam.front * kHandForward -
                cam.up * kHandDown;
            const glm::vec4 handGlowPulseData(0.0f, 0.0f, 0.0f, 0.0f);
            const glm::vec4 handShaderPulseData(0.0f, 1.0f, 0.0f, 0.0f);

            if (cursorTool.handOrbUsesRight()) {
                const auto& right = cursorTool.getRightHandOrbSettings();
                pushTransientToolLight(
                    handBase + cam.right * kHandSide,
                    right.radius,
                    right.intensity,
                    right.color,
                    handGlowPulseData,
                    handShaderPulseData);
            }
            if (cursorTool.handOrbUsesLeft()) {
                const auto& left = cursorTool.getLeftHandOrbSettings();
                pushTransientToolLight(
                    handBase - cam.right * kHandSide,
                    left.radius,
                    left.intensity,
                    left.color,
                    handGlowPulseData,
                    handShaderPulseData);
            }
        }
    }
    
    // Feed gameplay stats to EngineInterface for bottom bar
    if (engineInterfaceActive) {
        auto& ui = m_world.getDebugOverlay().getEngineInterface();
        EngineInterface::GameplayStats stats{};
        const double screenFrameMs = (m_lastScreenFrameMs > 0.0) ? m_lastScreenFrameMs : m_lastActualFrameMs;
        stats.screenFps = static_cast<float>(screenFrameMs > 0.0 ? 1000.0 / screenFrameMs : 0.0);
        stats.gpuFps = static_cast<float>(m_lastGpuFrameMs > 0.0 ? 1000.0 / m_lastGpuFrameMs : 0.0);
        stats.gpuMs = static_cast<float>(m_lastGpuFrameMs);
        stats.cpuMs = static_cast<float>(m_lastCpuWorkMs);
        // Real VRAM from allocators
        stats.vbAllocatedBytes = m_vbAllocator.getAllocatedBytes();
        stats.vbCapacityBytes  = m_vbAllocator.getTotalCapacity();
        stats.ibAllocatedBytes = m_ibAllocator.getAllocatedBytes();
        stats.ibCapacityBytes  = m_ibAllocator.getTotalCapacity();
        // Staging arenas (both frames)
        uint64_t stagingTotal = 0;
        for (auto& arena : m_uploadArenas)
            if (arena.isValid()) stagingTotal += arena.getCapacity();
        stats.stagingCapacityBytes = stagingTotal;
        // Total estimated VRAM = mesh pools + staging + GPU culling fixed overhead
        // GPU culling: ~13 MiB (allDraws + visibleDraws + origins + activeIndices + frustum buffers + readback + debug)
        constexpr uint64_t GPU_CULLING_OVERHEAD = 13u * 1024u * 1024u;
        stats.totalVramBytes = stats.vbCapacityBytes + stats.ibCapacityBytes
                             + stats.stagingCapacityBytes + GPU_CULLING_OVERHEAD;
        // Culling stats
        const auto cullDebugStats = m_gpuCulling.getDebugStats();
        stats.visibleChunks = m_perfMode ? m_gpuCulling.getLastVisibleDrawCount()
                                         : cullDebugStats.visibleDraws;
        stats.totalChunks   = m_gpuCulling.getActiveSlotCount();
        // Hardware
        std::strncpy(stats.gpuName, m_deviceProperties.deviceName, sizeof(stats.gpuName) - 1);
        std::strncpy(stats.cpuName, getCPUBrandString(), sizeof(stats.cpuName) - 1);
        stats.cpuCores = static_cast<int>(std::thread::hardware_concurrency());
        ui.setGameplayStats(stats);
    }

    // Render debug overlays (only when ImGui frame is active)
    if (m_imguiFrameActive) {
        auto dbgT0 = std::chrono::high_resolution_clock::now();
        float dbgInterfaceMs = 0.0f, dbgVramMs = 0.0f, dbgCloudMs = 0.0f;
        float dbgMinimapMs = 0.0f, dbgPerfMs = 0.0f, dbgToolMs = 0.0f, dbgEndMs = 0.0f;
        if (debugVisible) {
            // Set registry for ChunkVramWindow before rendering (needed when embedded in control panel)
            m_world.getDebugOverlay().getChunkVramWindow().setRegistry(&m_world.getRegistry());
            auto t0 = std::chrono::high_resolution_clock::now();
            m_world.getDebugOverlay().render();
            auto t1 = std::chrono::high_resolution_clock::now();
            m_world.getDebugOverlay().renderChunkVramWindow(m_world.getRegistry());
            auto t2 = std::chrono::high_resolution_clock::now();
            m_world.getDebugOverlay().renderCloudDebug(&m_cloudSystem, m_device, m_physicalDevice);
            auto t3 = std::chrono::high_resolution_clock::now();
            dbgInterfaceMs = std::chrono::duration<float, std::milli>(t1 - t0).count();
            dbgVramMs = std::chrono::duration<float, std::milli>(t2 - t1).count();
            dbgCloudMs = std::chrono::duration<float, std::milli>(t3 - t2).count();

            // Check if user pressed "Reset Settings" button
            auto& ui = m_world.getDebugOverlay().getEngineInterface();
            if (ui.isResetSettingsRequested()) {
                resetSettings();
                ui.clearResetSettingsRequest();
            }
        }

        auto tMinimap = std::chrono::high_resolution_clock::now();
        if (!gameplayDetached) {
            // HUD minimap renders independently of debug windows visibility.
            m_world.getDebugOverlay().getChunkMinimapWindow().renderHUDMinimap(
                gameplayViewportOffsetX, gameplayViewportOffsetY,
                static_cast<float>(gameplayViewportW), static_cast<float>(gameplayViewportH));
        }

        auto tPerf = std::chrono::high_resolution_clock::now();
        dbgMinimapMs = std::chrono::duration<float, std::milli>(tPerf - tMinimap).count();

        renderPerfOverlay(gameplayViewportOffsetX,
                          gameplayViewportOffsetY,
                          gameplayViewportW,
                          gameplayViewportH);

        auto tTool = std::chrono::high_resolution_clock::now();
        dbgPerfMs = std::chrono::duration<float, std::milli>(tTool - tPerf).count();

        // Render cursor tool preview overlay (drawn on top of everything)
        if (!gameplayDetached) {
            const bool terrainEditActive = terrainEditTool.isActive();
            const bool texturePaintActive = texturePaintTool.isActive();
            if (!terrainEditActive && !texturePaintActive &&
                cursorTool.isActive() && m_input.isCursorEnabled()) {
                const auto& cam = m_camera.getState();
                cursorTool.renderPreviewOverlay(cam.viewProj,
                                                 gameplayViewportW,
                                                 gameplayViewportH,
                                                 gameplayViewportOffsetX,
                                                 gameplayViewportOffsetY);
            }
            if (terrainEditTool.isActive() && m_input.isCursorEnabled()) {
                const auto& cam = m_camera.getState();
                terrainEditTool.renderPreviewOverlay(cam.viewProj,
                                                     gameplayViewportW,
                                                     gameplayViewportH,
                                                     gameplayViewportOffsetX,
                                                     gameplayViewportOffsetY);
            }
            if (!terrainEditActive && texturePaintActive && m_input.isCursorEnabled()) {
                const auto& cam = m_camera.getState();
                texturePaintTool.renderPreviewOverlay(cam.viewProj,
                                                      gameplayViewportW,
                                                      gameplayViewportH,
                                                      gameplayViewportOffsetX,
                                                      gameplayViewportOffsetY);
            }

            // Sun-shadow cascade visualization (no-op unless enabled in panel).
            {
                const auto& cam = m_camera.getState();
                m_world.getDebugOverlay().getDirectionalShadowWindow().renderCascadeOverlay(
                    cam.viewProj,
                    gameplayViewportW,
                    gameplayViewportH,
                    gameplayViewportOffsetX,
                    gameplayViewportOffsetY);
            }
        }

        auto tEnd = std::chrono::high_resolution_clock::now();
        dbgToolMs = std::chrono::duration<float, std::milli>(tEnd - tTool).count();

        // Finalize ImGui rendering
        m_imgui.endFrame();
        auto tDone = std::chrono::high_resolution_clock::now();
        dbgEndMs = std::chrono::duration<float, std::milli>(tDone - tEnd).count();
        m_imguiInterfaceMs = dbgInterfaceMs;
        m_imguiVramMs = dbgVramMs;
        m_imguiCloudMs = dbgCloudMs;
        m_imguiMinimapMs = dbgMinimapMs;
        m_imguiPerfMs = dbgPerfMs;
        m_imguiToolMs = dbgToolMs;
        m_imguiEndFrameMs = dbgEndMs;

        float totalDbgMs = std::chrono::duration<float, std::milli>(tDone - dbgT0).count();
        if (totalDbgMs > 3.0f) {
            static uint32_t imguiSpikeCount = 0;
            ++imguiSpikeCount;
            if (TerminalLogConfig::imguiSpikes && (imguiSpikeCount <= 20 || (imguiSpikeCount % 30) == 0)) {
                std::cout << "[IMGUI SPIKE #" << imguiSpikeCount
                    << "] total=" << totalDbgMs
                    << "ms  interface=" << dbgInterfaceMs
                    << "  vram=" << dbgVramMs
                    << "  cloud=" << dbgCloudMs
                    << "  minimap=" << dbgMinimapMs
                    << "  perf=" << dbgPerfMs
                    << "  tool=" << dbgToolMs
                    << "  endFrame=" << dbgEndMs
                    << std::endl;
            }
        }
    }
    auto imguiEnd = std::chrono::high_resolution_clock::now();
    m_imguiMs = m_imguiFrameActive
        ? std::chrono::duration<float, std::milli>(imguiEnd - imguiStart).count()
        : 0.0f;

    // Step 2: Acquire next swapchain image
    auto beforeAcquire = std::chrono::high_resolution_clock::now();
    uint32_t imageIndex;
    VkResult result = vkAcquireNextImageKHR(m_device, m_swapchain, UINT64_MAX, 
                                            frame.imageAvailable, VK_NULL_HANDLE, &imageIndex);

    if (result == VK_ERROR_OUT_OF_DATE_KHR) {
        recreateSwapchain(); // GPT_CHANGE: Call recreateSwapchain on acquire path
        return;
    } else if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR) {
        throw std::runtime_error("failed to acquire swap chain image!");
    }

    m_frameGraphColorLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    
    // GPT_CHANGE: Wait for the fence that last used this image (if any)
    if (m_imageInFlight[imageIndex] != VK_NULL_HANDLE) {
        vkWaitForFences(m_device, 1, &m_imageInFlight[imageIndex], VK_TRUE, UINT64_MAX);
        if (!m_perfMode) {
            collectTimestampResults(imageIndex);
            m_shadowSystem.collectGpuTimings(imageIndex);
        }
    }
    auto afterImageWait = std::chrono::high_resolution_clock::now();
    m_presentWaitMs = std::chrono::duration<float, std::milli>(afterImageWait - beforeAcquire).count();
    // Track command recording + submit + present (everything after acquire)
    auto cmdRecordStart = std::chrono::high_resolution_clock::now();
    // Mark this image as now being in use by this frame's fence
    m_imageInFlight[imageIndex] = frame.inFlight;

    // Step 3: Update uniform buffer and record command buffer
    // PHASE B7: Use identity model matrix (chunks are already in world space)
    glm::mat4 model = glm::mat4(1.0f);
    
    // Get view and projection from camera controller
    const CameraState& camState = m_camera.getState();
    const glm::mat4& proj = camState.proj;
    
    // === CAMERA-RELATIVE RENDERING ===
    // Improves floating-point precision at large world coordinates by:
    // 1. Passing camera position to shader separately
    // 2. Using only the rotation part of the view matrix
    // 3. Shader subtracts camera position before transformation
    //
    // This keeps vertex math in small local coordinates instead of large world coordinates.
    // Note: This does NOT fix T-junction cracks from greedy meshing (use T-junction fix system for that).
    
    // Start with the original view matrix
    glm::mat4 viewCameraRelative = camState.view;
    
    // Zero the translation (column 3, rows 0-2 contain the translation in view space)
    // In GLM's column-major layout: mat[col][row]
    // The translation is encoded in column 3 (mat[3][0], mat[3][1], mat[3][2])
    viewCameraRelative[3][0] = 0.0f;
    viewCameraRelative[3][1] = 0.0f;
    viewCameraRelative[3][2] = 0.0f;
    
    // Camera position as vec4 (w=0 for proper subtraction, not used as homogeneous point)
    glm::vec4 cameraPos = glm::vec4(camState.position, 0.0f);

    memcpy(m_uniformMapped[imageIndex], &model, sizeof(glm::mat4));
    memcpy(reinterpret_cast<char*>(m_uniformMapped[imageIndex]) + sizeof(glm::mat4), &viewCameraRelative, sizeof(glm::mat4));
    memcpy(reinterpret_cast<char*>(m_uniformMapped[imageIndex]) + sizeof(glm::mat4)*2, &proj, sizeof(glm::mat4));
    memcpy(reinterpret_cast<char*>(m_uniformMapped[imageIndex]) + sizeof(glm::mat4)*3, &cameraPos, sizeof(glm::vec4));
    
    // Sync per-object properties from ObjectManager to rendering systems
    // Must happen BEFORE updateLightingUniforms so terrain shaders see fresh data
    for (const auto& [objId, obj] : m_objectManager.getAllObjects()) {
        if (obj.type == PlacedObjectType::LightOrb && obj.light.lightIndex < m_lighting.activePointLights) {
            auto& pl = m_lighting.pointLights[obj.light.lightIndex];
            pl.color = glm::vec3(obj.light.colorR, obj.light.colorG, obj.light.colorB);
            pl.radius = obj.light.radius;
            pl.intensity = obj.light.intensity;
        }
    }

    // ── Shadow budget selection (→ EngineShadowPass.cpp) ──────────────────
    updateShadowsForFrame(imageIndex, transientToolLights, camState.position, camState.front);
    refreshShadowDescriptorsForImage(imageIndex);

    // Update lighting and camera uniform buffers (now uses synced pointLights)
    updateLightingUniforms(imageIndex, transientToolLights, transientToolPulseData);
    updateCameraUniforms(imageIndex);
    updateAOUniforms(imageIndex);
    syncMaterialOverlayForImage(imageIndex);
    updateClusterData(imageIndex, viewCameraRelative, proj, gameplayRect);
    
    // ── Gameplay window poll + close handling + acquire (→ EngineGameplayRendering.cpp) ──
    if (!pollAndPrepareGameplayWindow()) {
        return;
    }

    // ── Gameplay overlay frame (minimap/cursor tool in detached window) ──────
    recordGameplayOverlayFrame(gameplayOverlayRequested);

    recordCommandBuffer(imageIndex, static_cast<uint32_t>(m_currentFrame), camState.view, proj, gameplayRect);

    // C3.2: Wait on upload + Hi-Z timeline semaphores (GPU-side ordering)
    // If gameplay window acquired, also wait on its image-available semaphore
    const bool gpWait = m_gameplayWindowAcquired && m_gameplayWindow;
    VkSemaphore waitSemaphores[4] = { frame.imageAvailable, m_uploadTimeline, m_hiZTimeline, VK_NULL_HANDLE };
    VkPipelineStageFlags waitStages[4] = { 
        VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,  // Wait for swapchain image
        VK_PIPELINE_STAGE_VERTEX_INPUT_BIT |            // Ensure vertex/index fetch sees uploads
        VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,            // Ensure indirect command reads wait, too
        // Hi-Z: previous frame's pyramid build must finish before this frame starts ANY work.
        // The pyramid build READS the depth buffer (as a sampled image in compute), while this
        // frame's depth prepass CLEARS it (via oldLayout=UNDEFINED transition at fragment tests).
        // Using ALL_COMMANDS ensures no race between pyramid read and depth clear.
        VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
        VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT   // Gameplay window render target must be ready
    };
    uint64_t waitValues[4] = { 0, m_uploadTimelineValue, m_hiZTimelineValue, 0 };
    uint32_t waitCount = 3;
    if (gpWait) {
        waitSemaphores[3] = m_gameplayWindow->getImageAvailableSemaphore();
        waitCount = 4;
    }
    
    // Signal: renderFinished (binary) + hiZTimeline (timeline, incremented)
    VkSemaphore signalSemaphores[3] = {
        frame.renderFinishedMain,
        m_hiZTimeline,
        frame.renderFinishedGameplay
    };
    uint64_t signalValues[3] = { 0, m_hiZTimelineValue + 1, 0 };
    uint32_t signalCount = gpWait ? 3u : 2u;

    VkTimelineSemaphoreSubmitInfo timelineInfo{};
    timelineInfo.sType = VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO;
    timelineInfo.waitSemaphoreValueCount = waitCount;
    timelineInfo.pWaitSemaphoreValues = waitValues;
    timelineInfo.signalSemaphoreValueCount = signalCount;
    timelineInfo.pSignalSemaphoreValues = signalValues;

    VkSubmitInfo submitInfo{};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.pNext = &timelineInfo;  // C3.2: Timeline semaphore info
    submitInfo.waitSemaphoreCount = waitCount;
    submitInfo.pWaitSemaphores = waitSemaphores;
    submitInfo.pWaitDstStageMask = waitStages;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &m_commandBuffers[imageIndex]; // Per-swapchain-image command buffer
    submitInfo.signalSemaphoreCount = signalCount;
    submitInfo.pSignalSemaphores = signalSemaphores;

    // Reset fence right before submit to minimize window where it's unsignaled
    vkResetFences(m_device, 1, &frame.inFlight);
    
    static int submitFrameNum = 0;
    submitFrameNum++;
    bool submitLog = (submitFrameNum <= 5);
    
    if (submitLog) std::cout << "[Engine] Frame " << submitFrameNum << ": Submitting to queue..." << std::endl;
    
    VkResult submitResult = vkQueueSubmit(m_graphicsQueue, 1, &submitInfo, frame.inFlight);
    
    if (submitLog) std::cout << "[Engine] Frame " << submitFrameNum << ": Submit returned " << submitResult << std::endl;
    if (submitResult == VK_SUCCESS) {
        // Advance Hi-Z timeline so the next frame's culling waits for this frame's pyramid build
        m_hiZTimelineValue++;
    } else {
        std::cerr << "[Error] Graphics queue submit failed with result: " << submitResult << std::endl;
        
        if (submitResult == VK_ERROR_DEVICE_LOST) {
            throw std::runtime_error("Vulkan device lost - cannot recover");
        }
        
        // Submit failed but fence was already reset - we need to signal it to prevent deadlock.
        // Do a minimal empty submit just to signal the fence.
        VkSubmitInfo emptySubmit{};
        emptySubmit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
        VkResult recoveryResult = vkQueueSubmit(m_graphicsQueue, 1, &emptySubmit, frame.inFlight);
        if (recoveryResult != VK_SUCCESS) {
            // If even the empty submit fails, we're in serious trouble
            std::cerr << "[Error] Recovery submit also failed, device may be lost" << std::endl;
            throw std::runtime_error("Vulkan device in unrecoverable state");
        }
        
        // Skip presentation for this frame since we didn't render anything
        m_currentFrame = (m_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        return;
    }

    // Step 5: Present. When gameplay is separated, batch both swapchains into
    // a single vkQueuePresentKHR call so the driver can schedule them without
    // double-blocking on separate VBlank intervals.
    const bool gpPresent = m_gameplayWindowAcquired && m_gameplayWindow && m_gameplayWindow->isOpen();
    if (submitLog) std::cout << "[Engine] Frame " << submitFrameNum << ": Presenting..." << std::endl;

    if (gpPresent) {
        // Batched present: gameplay + main in one call to avoid serial VBlank waits
        VkSwapchainKHR swapchains[2] = { m_gameplayWindow->getSwapchain(), m_swapchain };
        uint32_t imageIndices[2] = { m_gameplayWindow->getCurrentImageIndex(), imageIndex };
        VkSemaphore waitSems[2] = { frame.renderFinishedGameplay, frame.renderFinishedMain };
        VkResult presentResults[2] = { VK_SUCCESS, VK_SUCCESS };

        VkPresentInfoKHR batchPresent{};
        batchPresent.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
        batchPresent.waitSemaphoreCount = 2;
        batchPresent.pWaitSemaphores = waitSems;
        batchPresent.swapchainCount = 2;
        batchPresent.pSwapchains = swapchains;
        batchPresent.pImageIndices = imageIndices;
        batchPresent.pResults = presentResults;

        result = vkQueuePresentKHR(m_presentQueue, &batchPresent);

        // Track gameplay present timing
        if (presentResults[0] == VK_SUCCESS) {
            const double presentNow = glfwGetTime();
            if (m_lastGameplayPresentTimestamp > 0.0) {
                m_lastScreenFrameMs = (presentNow - m_lastGameplayPresentTimestamp) * 1000.0;
            }
            m_lastGameplayPresentTimestamp = presentNow;
        } else {
            m_lastScreenFrameMs = 0.0;
            m_lastGameplayPresentTimestamp = 0.0;
            if (presentResults[0] == VK_ERROR_OUT_OF_DATE_KHR || presentResults[0] == VK_SUBOPTIMAL_KHR) {
                m_gameplayWindow->markNeedsRecreate();
            }
        }

        // Handle main swapchain out-of-date
        if (presentResults[1] == VK_ERROR_OUT_OF_DATE_KHR || presentResults[1] == VK_SUBOPTIMAL_KHR || m_framebufferResized) {
            m_framebufferResized = false;
            recreateSwapchain();
        }
    } else {
        m_lastScreenFrameMs = m_lastActualFrameMs;
        m_lastGameplayPresentTimestamp = 0.0;

        VkSwapchainKHR presentSwapchain = m_swapchain;
        uint32_t presentImageIndex = imageIndex;

        VkPresentInfoKHR presentInfo{};
        presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
        presentInfo.waitSemaphoreCount = 1;
        presentInfo.pWaitSemaphores = &frame.renderFinishedMain;
        presentInfo.swapchainCount = 1;
        presentInfo.pSwapchains = &presentSwapchain;
        presentInfo.pImageIndices = &presentImageIndex;

        result = vkQueuePresentKHR(m_presentQueue, &presentInfo);

        if (submitLog) std::cout << "[Engine] Frame " << submitFrameNum << ": Present returned " << result << std::endl;
    
        // Handle swapchain out-of-date or resize
        if (result == VK_ERROR_OUT_OF_DATE_KHR || result == VK_SUBOPTIMAL_KHR || m_framebufferResized){
            m_framebufferResized = false;
            recreateSwapchain();
        } else if (result != VK_SUCCESS) {
            throw std::runtime_error("failed to present swap chain image!");
        }
    }

    // Step 6: Advance to next frame
    m_cmdRecordMs = std::chrono::duration<float, std::milli>(
        std::chrono::high_resolution_clock::now() - cmdRecordStart).count();
    m_currentFrame = (m_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
}

````

## src\core\engine\EngineTimestamps.cpp

Description: No CC-DESC found.

````cpp
// EngineTimestamps.cpp - Timestamp query pool management and per-frame GPU timing collection
// Contains: collectTimestampResults

#include "core/engine/Engine.h"
#include <cstdint>

// GPT_CHANGE: Refactored drawFrame with clear per-frame synchronization flow
void Engine::collectTimestampResults(uint32_t imageIndex) {
    if (m_timestampQueryPool == VK_NULL_HANDLE) {
        return;
    }

    if (imageIndex >= m_swapchainImages.size()) {
        return;
    }

    // Query layout per image:
    // 0=frameStart, 1=afterInitialCull, 2=depthPrepassStart, 3=depthPrepassEnd,
    // 4=afterHiZBuild, 5=afterFinalCull, 6=terrainStart, 7=terrainEnd, 8=frameEnd
    uint32_t queryBase = imageIndex * TIMESTAMPS_PER_IMAGE;

    // Never block the CPU for timing data. A blocking timestamp query can turn
    // cheap GPU work into a 10ms+ CPU readback stall. WITH_AVAILABILITY gives us
    // one availability word per query; if any query is not ready, keep the last
    // published timing values and try again on a later frame.
    uint64_t queryData[TIMESTAMPS_PER_IMAGE * 2] = {};
    VkResult res = vkGetQueryPoolResults(
        m_device,
        m_timestampQueryPool,
        queryBase,
        TIMESTAMPS_PER_IMAGE,
        sizeof(queryData),
        queryData,
        sizeof(uint64_t) * 2,
        VK_QUERY_RESULT_64_BIT | VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);

    if (res != VK_SUCCESS && res != VK_NOT_READY) {
        return;
    }

    for (uint32_t i = 0; i < TIMESTAMPS_PER_IMAGE; ++i) {
        if (queryData[i * 2 + 1] == 0u) {
            return;
        }
    }

    uint64_t timestamps[TIMESTAMPS_PER_IMAGE] = {};
    for (uint32_t i = 0; i < TIMESTAMPS_PER_IMAGE; ++i) {
        timestamps[i] = queryData[i * 2];
    }

    // Convert timestamp deltas to milliseconds. Individual ranges may be
    // intentionally absent in some culling modes; non-monotonic pairs report 0.
    auto ticksToMs = [this](uint64_t start, uint64_t end) -> double {
        if (end > start && m_timestampPeriod > 0.0f) {
            return static_cast<double>(end - start) * static_cast<double>(m_timestampPeriod) / 1'000'000.0;
        }
        return 0.0;
    };

    // Total frame time (start to end)
    if (timestamps[8] > timestamps[0]) {
        m_lastGpuFrameMs = ticksToMs(timestamps[0], timestamps[8]);
        m_lastUncappedFps = m_lastGpuFrameMs > 0.0 ? 1000.0 / m_lastGpuFrameMs : 0.0;
    }

    HiZPyramid::DiagnosticsMode submittedMode = HiZPyramid::DiagnosticsMode::FrustumOnly;
    if (imageIndex < m_hiZTimingModeByImage.size()) {
        submittedMode = m_hiZTimingModeByImage[imageIndex];
    }

    m_gpuInitialCullMs = ticksToMs(timestamps[0], timestamps[1]);
    m_gpuDepthPrepassMs = ticksToMs(timestamps[2], timestamps[3]);
    m_gpuHiZBuildMs = ticksToMs(timestamps[3], timestamps[4]);
    m_gpuFinalCullMs = ticksToMs(timestamps[4], timestamps[5]);

    m_lastCollectedTemporalHiZ = (submittedMode == HiZPyramid::DiagnosticsMode::TemporalHiZ);
    m_lastCollectedCurrentFrameHiZ =
        (m_gpuDepthPrepassMs > 0.0) ||
        (m_gpuHiZBuildMs > 0.0) ||
        (m_gpuFinalCullMs > 0.0);

    // The current query layout has no separate temporal/incremental Hi-Z query
    // pair. Keep this field honest until a dedicated timestamp range is added.
    m_gpuHiZIncrementalMs = 0.0;

    // Culling dispatch time shown in the generic debug UI keeps the first pass,
    // while total culling reflects both culling dispatches. Depth prepass and
    // Hi-Z build remain separate GPU costs and are not folded into culling.
    m_cullingDispatchMs = m_gpuInitialCullMs;
    m_cullingTotalMs = m_gpuInitialCullMs + m_gpuFinalCullMs;

    // Main lit terrain timing.
    m_terrainLightingMs = ticksToMs(timestamps[6], timestamps[7]);
    m_shadowSystem.setFrameGpuPassCosts(
        static_cast<float>(m_terrainLightingMs));
}

````

## src\core\engine\EngineCommandBuffer.cpp

Description: No CC-DESC found.

````cpp
// EngineCommandBuffer.cpp - Vulkan command buffer recording
// GPT-DESC: Records frame command buffers and keeps temporal Hi-Z conservative near occluders.
// Contains: recordCommandBuffer, recordVoxelOpaquePass,
//           prepareFramePassBarriers, finalizeFramePassResources

#include "core/engine/Engine.h"
#include "ui/debug_menu/world/ChunkMinimapWindow.h"
#include <imgui_impl_vulkan.h>
#include <array>
#include <chrono>
#include <cmath>

#ifndef VULKANAS_USE_FRAMEGRAPH_BARRIERS
#define VULKANAS_USE_FRAMEGRAPH_BARRIERS 1
#endif

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#include <glm/gtc/matrix_transform.hpp>

namespace {
// Previous-frame Hi-Z stays stable during normal movement when the shader keeps
// the occlusion test fully in previous-frame projection space. Huge turns or
// teleports still fall back because most temporal samples become unrelated.
constexpr float kHiZMaxCameraRotationDegrees = 30.0f;
constexpr float kHiZMaxCameraTranslationMeters = 8.0f;

uint32_t previousPowerOfTwo(uint32_t v) {
    if (v <= 1u) {
        return 1u;
    }
    v |= v >> 1u;
    v |= v >> 2u;
    v |= v >> 4u;
    v |= v >> 8u;
    v |= v >> 16u;
    return (v >> 1u) + 1u;
}
}

void Engine::prepareFramePassBarriers(const FrameGraphCompiledPass& pass,
                                      uint32_t imageIndex,
                                      std::vector<VkImageMemoryBarrier2>& imageBarriers,
                                      std::vector<VkBufferMemoryBarrier2>& bufferBarriers) {
    // When gameplay is separated, the 3D pass renders into the gameplay
    // window's images, so barriers must target those instead.
    const bool isSep = m_gameplaySeparated
                    && m_gameplayWindow && m_gameplayWindow->isOpen()
                    && m_gameplayWindowAcquired;
    VkImage colorImg = isSep ? m_gameplayWindow->getImages()[m_gameplayWindow->getCurrentImageIndex()]
                             : m_swapchainImages[imageIndex];
    VkImage depthImg = isSep ? m_gameplayWindow->getDepthImage()
                             : m_depthImage;
    EngineFrameGraph::prepareFramePassBarriers(
        pass, m_frameGraph, imageIndex,
        colorImg, depthImg,
        m_indirectBuffer, m_indirectDrawCount,
        m_frameGraphColorLayout, m_frameGraphDepthLayout,
        imageBarriers, bufferBarriers);
}

void Engine::finalizeFramePassResources(const FrameGraphCompiledPass& pass) {
    EngineFrameGraph::finalizeFramePassResources(pass, m_frameGraph, m_frameGraphColorLayout, m_frameGraphDepthLayout);
}

void Engine::recordVoxelOpaquePass(VkCommandBuffer cmd, uint32_t imageIndex, uint32_t currentFrame, const glm::mat4& view, const glm::mat4& proj, const VkRect2D& gameplayRect, bool useDepthPrepass) {
    EngineFrameGraph::FrameGraphContext ctx{};
    ctx.device = m_device;
    ctx.frameGraph = &m_frameGraph;
    ctx.passDescriptors = &m_framePassDescriptors;
    ctx.compiledPasses = &m_compiledFramePasses;
    ctx.colorLayout = &m_frameGraphColorLayout;
    ctx.depthLayout = &m_frameGraphDepthLayout;
    ctx.graphicsPipeline = useDepthPrepass ? m_graphicsPipelineDepthLoad : m_graphicsPipeline;
    ctx.pipelineLayout = m_pipelineLayout;
    ctx.dccmPipeline = useDepthPrepass ? m_dccmPipelineDepthLoad : m_dccmPipeline;
    ctx.dccmPipelineLayout = m_dccmPipelineLayout;
    ctx.useDepthPrepass = useDepthPrepass;
    ctx.anyLODUsesVoxel = m_anyLODUsesVoxel;
    ctx.anyLODUsesDCCM = m_anyLODUsesDCCM;
    ctx.descriptorSets = &m_descriptorSets;
    ctx.indirectBuffer = m_indirectBuffer;
    ctx.indirectDrawCount = m_indirectDrawCount;
    ctx.renderPass = useDepthPrepass ? m_renderPassDepthLoad : m_renderPass;
    
    // GPU culling state (Phase 1)
    bool isGPUCulling = (m_indirectDrawCount == UINT32_MAX) && m_gpuCullingEnabled && m_gpuCulling.isReady();
    ctx.useGPUCulling = isGPUCulling;
    if (isGPUCulling) {
        ctx.gpuVisibleDrawsBuffer = m_gpuCulling.getVisibleDrawsBuffer();
        ctx.gpuDrawCountBuffer = m_gpuCulling.getDrawCountBuffer();
        ctx.gpuOriginsBuffer = m_gpuCulling.getVisibleOriginsBuffer();
        ctx.gpuMaxDraws = m_gpuCulling.getMaxDraws();
    }
    
    ctx.vbAllocator = &m_vbAllocator;
    ctx.ibAllocator = &m_ibAllocator;
    ctx.cubeVB = m_cubeVB;
    ctx.cubeIB = m_cubeIB;
    ctx.cubeIndexCount = static_cast<uint32_t>(m_cubeMesh.indices.size());
    ctx.cloudSystem = &m_cloudSystem;
    ctx.celestialSystem = &m_celestialSystem;
    ctx.lightGlowSystem = &m_lightGlowSystem;
    ctx.starSystem = &m_starSystem;
    ctx.skySystem = &m_skySystem;
    ctx.lighting = &m_lighting;
    ctx.objectManager = &m_objectManager;
    ctx.pulseLibrary = &m_pulsePresets;
    ctx.cameraPos = m_camera.getState().position;
    ctx.currentFrame = currentFrame;
    ctx.timestampQueryPool = m_timestampQueryPool;
    ctx.timestampBase = imageIndex * TIMESTAMPS_PER_IMAGE;
    ctx.parallelRecorder = &m_parallelRecorder;
    // When gameplay is separated, render 3D directly into the gameplay window's
    // framebuffers instead of the main swapchain.
    const bool isSeparated = m_gameplaySeparated
                          && m_gameplayWindow && m_gameplayWindow->isOpen()
                          && m_gameplayWindowAcquired;
    if (isSeparated) {
        auto& gw = *m_gameplayWindow;
        ctx.framebuffers = const_cast<std::vector<VkFramebuffer>*>(&gw.getFramebuffers());
        ctx.swapchainImages = const_cast<std::vector<VkImage>*>(&gw.getImages());
        ctx.depthImage = gw.getDepthImage();
        ctx.swapchainExtent = gw.getExtent();
        ctx.gameplayScissor = {{0, 0}, gw.getExtent()};
        ctx.imguiFrameActive = m_gameplayOverlayFrameActive;
        ctx.framebufferIndex = gw.getCurrentImageIndex();
        ctx.tjunctionFix = m_gameplayTJunctionFix.isReady() ? &m_gameplayTJunctionFix : nullptr;
        ctx.pixelPass = m_gameplayPixelPass.isReady() ? &m_gameplayPixelPass : nullptr;
    } else {
        ctx.swapchainImages = &m_swapchainImages;
        ctx.framebuffers = &m_swapchainFramebuffers;
        ctx.depthImage = m_depthImage;
        ctx.swapchainExtent = m_swapchainExtent;
        ctx.gameplayScissor = gameplayRect;
        ctx.imguiFrameActive = m_imguiFrameActive;
        ctx.tjunctionFix = &m_tjunctionFix;
        ctx.pixelPass = &m_pixelPass;
    }

    EngineFrameGraph::recordVoxelOpaquePass(cmd, ctx, imageIndex, view, proj);
}

// PHASE B7: Record command buffer dynamically each frame with MDI
void Engine::recordCommandBuffer(uint32_t imageIndex, uint32_t frameIndex, const glm::mat4& view, const glm::mat4& proj, const VkRect2D& gameplayRect) {
    VkCommandBuffer cmd = m_commandBuffers[imageIndex];
    const bool isSeparated = m_gameplaySeparated
                          && m_gameplayWindow && m_gameplayWindow->isOpen()
                          && m_gameplayWindowAcquired;
    const bool useGameplayOverlayDrawData =
        isSeparated &&
        m_gameplayOverlayFrameActive &&
        m_imgui.hasGameplayOverlayContext();

    (void)frameIndex;

    // Reset and begin command buffer
    vkResetCommandBuffer(cmd, 0);

    VkCommandBufferBeginInfo beginInfo{};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;

    if (vkBeginCommandBuffer(cmd, &beginInfo) != VK_SUCCESS)
        throw std::runtime_error("Failed to begin recording command buffer!");

    // Timestamp query layout per image:
    // 0=frameStart, 1=afterInitialCull, 2=depthPrepassStart, 3=depthPrepassEnd,
    // 4=afterHiZBuild, 5=afterFinalCull, 6=terrainStart, 7=terrainEnd, 8=frameEnd
    uint32_t timestampBase = imageIndex * TIMESTAMPS_PER_IMAGE;
    if (m_timestampQueryPool != VK_NULL_HANDLE) {
        vkCmdResetQueryPool(cmd, m_timestampQueryPool, timestampBase, TIMESTAMPS_PER_IMAGE);
        vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, m_timestampQueryPool, timestampBase + 0);  // frameStart
    }

    if (m_hiZTimingModeByImage.size() != m_swapchainImages.size()) {
        m_hiZTimingModeByImage.assign(m_swapchainImages.size(), HiZPyramid::DiagnosticsMode::FrustumOnly);
    }

    float cpuInitialCullRecordMs = 0.0f;

    glm::mat4 viewProj = proj * view;
    const CameraState& camState = m_camera.getState();
    const glm::vec3 currentCameraPos = camState.position;
    const glm::vec3 currentCameraFront = glm::normalize(camState.front);

    // Convert gameplay viewport (pixel space) to normalized UV transform.
    // Hi-Z is built from the active depth target, so culling samples in full
    // render-target UVs even when gameplay renders into a sub-viewport.
    const VkExtent2D cullingExtent = isSeparated ? m_gameplayWindow->getExtent() : m_swapchainExtent;
    const float invSwapW = (cullingExtent.width > 0) ? (1.0f / static_cast<float>(cullingExtent.width)) : 0.0f;
    const float invSwapH = (cullingExtent.height > 0) ? (1.0f / static_cast<float>(cullingExtent.height)) : 0.0f;
    const float viewportOffsetX = isSeparated ? 0.0f : static_cast<float>(gameplayRect.offset.x) * invSwapW;
    const float viewportOffsetY = isSeparated ? 0.0f : static_cast<float>(gameplayRect.offset.y) * invSwapH;
    const float viewportScaleX = isSeparated ? 1.0f : static_cast<float>(gameplayRect.extent.width) * invSwapW;
    const float viewportScaleY = isSeparated ? 1.0f : static_cast<float>(gameplayRect.extent.height) * invSwapH;
    const glm::vec4 viewportUvTransform(viewportOffsetX, viewportOffsetY, viewportScaleX, viewportScaleY);
    constexpr float viewportEpsilon = 1e-6f;

    const uint32_t expectedHiZW = previousPowerOfTwo(std::max(1u, cullingExtent.width));
    const uint32_t expectedHiZH = previousPowerOfTwo(std::max(1u, cullingExtent.height));

    // Check temporal Hi-Z viability — uses previous frame's pyramid for
    // occlusion culling. When stale, falls back to frustum-only culling.
    // The post-render Hi-Z rebuild ensures the pyramid self-corrects each frame.
    bool temporalHiZViable = false;
    bool usedTemporalHiZ = false;
    // Always compute camera delta for diagnostics (even when temporal isn't checked)
    {
        const float frontDot = std::clamp(glm::dot(currentCameraFront, m_prevHiZCameraFront), -1.0f, 1.0f);
        m_lastFrameRotationDeg = glm::degrees(std::acos(frontDot));
        m_lastFrameTranslation = glm::length(currentCameraPos - m_prevHiZCameraPos);
    }
    if (m_gpuCullingEnabled && m_gpuCulling.isReady() && m_hiZPyramid.isReady() && m_prevHiZFrameValid) {
        const float frontDot = std::clamp(glm::dot(currentCameraFront, m_prevHiZCameraFront), -1.0f, 1.0f);
        const float rotationDegrees = glm::degrees(std::acos(frontDot));
        const float translationMeters = glm::length(currentCameraPos - m_prevHiZCameraPos);
        const glm::vec4 viewportDelta = glm::abs(viewportUvTransform - m_prevHiZViewportUvTransform);
        const float maxViewportDelta = std::max(
            std::max(viewportDelta.x, viewportDelta.y),
            std::max(viewportDelta.z, viewportDelta.w));
        const bool pyramidMatchesTarget =
            m_hiZPyramid.getWidth() == expectedHiZW &&
            m_hiZPyramid.getHeight() == expectedHiZH &&
            m_hiZPyramid.getMipLevels() > 0u;

        temporalHiZViable =
            pyramidMatchesTarget &&
            rotationDegrees <= kHiZMaxCameraRotationDegrees &&
            translationMeters <= kHiZMaxCameraTranslationMeters &&
            maxViewportDelta <= viewportEpsilon;
    }

    // ── Initial GPU frustum + occlusion culling ───────────────────────────
    // Temporal Hi-Z or frustum-only. Post-render pyramid rebuild ensures
    // temporal data is always fresh for the next frame.
    recordInitialGPUCulling(cmd, imageIndex, viewProj,
                            viewportOffsetX, viewportOffsetY, viewportScaleX, viewportScaleY,
                            temporalHiZViable,
                            usedTemporalHiZ,
                            cpuInitialCullRecordMs);

    // Phase D — bindless ChunkOrigins selection is now done per-draw via
    // a 4-byte VS push constant (see Pipeline::createDescriptorSetLayout / pipeline layout).
    // No per-frame vkUpdateDescriptorSets here anymore; the actual push happens at
    // each terrain draw site after binding the pipeline (EngineFrameGraph + the inline
    // fallback path in this file).
    (void)imageIndex;

    // Upload minimap texture if dirty (deferred from ImGui phase to avoid vkQueueWaitIdle stall)
    m_world.getDebugOverlay().getChunkMinimapWindow().recordTextureUpload(cmd);

    // ── Shadow pass ────────────────────────────────────────────────────────
    // Render realtime point-light shadow maps before the main frame pass.
    recordShadowRenderPasses(cmd, imageIndex);

    // Write dummy timestamps for the removed same-frame Hi-Z slots (prepass/build/finalCull)
    if (m_timestampQueryPool != VK_NULL_HANDLE) {
        for (uint32_t query = 2; query <= 5; ++query) {
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, m_timestampQueryPool, timestampBase + query);
        }
    }

    if (imageIndex < m_hiZTimingModeByImage.size()) {
        m_hiZTimingModeByImage[imageIndex] =
            usedTemporalHiZ ? HiZPyramid::DiagnosticsMode::TemporalHiZ
                            : HiZPyramid::DiagnosticsMode::FrustumOnly;
    }

    if (useGameplayOverlayDrawData) {
        m_imgui.setGameplayOverlayContextCurrent();
    } else {
        m_imgui.setMainContextCurrent();
    }

#if VULKANAS_USE_FRAMEGRAPH_BARRIERS
    std::vector<VkImageMemoryBarrier2> imageBarriers;
    std::vector<VkBufferMemoryBarrier2> bufferBarriers;
    bool executedPass = false;

    for (const FrameGraphCompiledPass& compiledPass : m_compiledFramePasses) {
        if (!compiledPass.descriptor || !compiledPass.descriptor->enabled) {
            continue;
        }

        // When separated, only VoxelOpaque targets the gameplay window.
        // Skip other passes' barriers to avoid transitioning the gameplay
        // window image away from PRESENT_SRC_KHR before present.
        if (isSeparated && compiledPass.descriptor->kind != FramePassKind::VoxelOpaque) {
            continue;
        }

        prepareFramePassBarriers(compiledPass, imageIndex, imageBarriers, bufferBarriers);

        if (!imageBarriers.empty() || !bufferBarriers.empty()) {
            VkDependencyInfo depInfo{};
            depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            depInfo.imageMemoryBarrierCount = static_cast<uint32_t>(imageBarriers.size());
            depInfo.pImageMemoryBarriers = imageBarriers.empty() ? nullptr : imageBarriers.data();
            depInfo.bufferMemoryBarrierCount = static_cast<uint32_t>(bufferBarriers.size());
            depInfo.pBufferMemoryBarriers = bufferBarriers.empty() ? nullptr : bufferBarriers.data();
            depInfo.memoryBarrierCount = 0;
            depInfo.pMemoryBarriers = nullptr;
            vkCmdPipelineBarrier2(cmd, &depInfo);
        }

        switch (compiledPass.descriptor->kind) {
            case FramePassKind::VoxelOpaque:
                recordVoxelOpaquePass(cmd, imageIndex, static_cast<uint32_t>(m_currentFrame), view, proj, gameplayRect, false);
                executedPass = true;
                break;
            default:
                break;
        }

        finalizeFramePassResources(compiledPass);
    }

    if (!executedPass) {
        recordVoxelOpaquePass(cmd, imageIndex, static_cast<uint32_t>(m_currentFrame), view, proj, gameplayRect, false);
        m_frameGraphColorLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
        m_frameGraphDepthLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    }
#else
    VkRenderPassBeginInfo renderPassInfo{};
    renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    renderPassInfo.renderPass = m_renderPass;
    renderPassInfo.framebuffer = m_swapchainFramebuffers[imageIndex];
    renderPassInfo.renderArea.offset = { 0, 0 };
    renderPassInfo.renderArea.extent = m_swapchainExtent;

    std::array<VkClearValue, 2> clearValues{};
    // Use current sky color instead of black
    clearValues[0].color = { {
        m_lighting.currentSkyColor.r,
        m_lighting.currentSkyColor.g,
        m_lighting.currentSkyColor.b,
        1.0f
    } };
    // Reversed-Z: clear to 0.0 (far plane), near plane is 1.0
    clearValues[1].depthStencil = { 0.0f, 0 };
    renderPassInfo.clearValueCount = static_cast<uint32_t>(clearValues.size());
    renderPassInfo.pClearValues = clearValues.data();

    vkCmdBeginRenderPass(cmd, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);

    VkViewport gameplayViewport{};
    gameplayViewport.x = static_cast<float>(gameplayRect.offset.x);
    gameplayViewport.y = static_cast<float>(gameplayRect.offset.y + static_cast<int32_t>(gameplayRect.extent.height));
    gameplayViewport.width = static_cast<float>(gameplayRect.extent.width);
    gameplayViewport.height = -static_cast<float>(gameplayRect.extent.height);
    gameplayViewport.minDepth = 0.0f;
    gameplayViewport.maxDepth = 1.0f;
    vkCmdSetViewport(cmd, 0, 1, &gameplayViewport);
    vkCmdSetScissor(cmd, 0, 1, &gameplayRect);

    // Render twinkling star field FIRST (sky background, no depth write)
    m_starSystem.render(cmd, static_cast<uint32_t>(m_currentFrame), view, proj, m_cameraPos, m_lighting.timeOfDay, m_lighting.totalTime);

    // Bind shared vertex/index buffers once
    VkBuffer pooledVB = m_vbAllocator.getPrimaryBuffer();
    VkBuffer pooledIB = m_ibAllocator.getPrimaryBuffer();
    VkDeviceSize vbOffset = 0;

    // Check if using GPU culling (UINT32_MAX marker) or CPU culling
    bool useGPUCulling = (m_indirectDrawCount == UINT32_MAX) && m_gpuCulling.isReady();

    // Lambda to draw all chunks with currently bound pipeline
    auto drawAllChunks = [&]() {
        if (useGPUCulling) {
            vkCmdBindVertexBuffers(cmd, 0, 1, &pooledVB, &vbOffset);
            vkCmdBindIndexBuffer(cmd, pooledIB, 0, VK_INDEX_TYPE_UINT16);
            vkCmdDrawIndexedIndirectCount(
                cmd,
                m_gpuCulling.getVisibleDrawsBuffer(), 0,
                m_gpuCulling.getDrawCountBuffer(), 0,
                m_gpuCulling.getMaxDraws(),
                sizeof(VkDrawIndexedIndirectCommand));
        } else if (m_indirectDrawCount > 0) {
            vkCmdBindVertexBuffers(cmd, 0, 1, &pooledVB, &vbOffset);
            vkCmdBindIndexBuffer(cmd, pooledIB, 0, VK_INDEX_TYPE_UINT16);
            vkCmdDrawIndexedIndirect(cmd, m_indirectBuffer, 0, m_indirectDrawCount, sizeof(VkDrawIndexedIndirectCommand));
        } else {
            VkBuffer vertexBuffers[] = { m_cubeVB.buffer };
            VkDeviceSize offsets[] = { m_cubeVB.offset };
            vkCmdBindVertexBuffers(cmd, 0, 1, vertexBuffers, offsets);
            vkCmdBindIndexBuffer(cmd, m_cubeIB.buffer, m_cubeIB.offset, VK_INDEX_TYPE_UINT16);
            vkCmdDrawIndexed(cmd, static_cast<uint32_t>(m_cubeMesh.indices.size()), 1, 0, 0, 0);
        }
    };

    // Two-pass rendering: voxel pipeline (discards face==6), then DCCM pipeline (discards face!=6)
    // When only one type is active, the other pass is skipped entirely
    if (m_anyLODUsesVoxel) {
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_graphicsPipeline);
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_pipelineLayout, 0, 1, &m_descriptorSets[imageIndex], 0, nullptr);
        // Phase D: select ChunkOrigins[1] (GPU visible) when GPU culling is on, else [0] (static).
        const uint32_t originsIndex = useGPUCulling ? 1u : 0u;
        vkCmdPushConstants(cmd, m_pipelineLayout, VK_SHADER_STAGE_VERTEX_BIT, 0, sizeof(uint32_t), &originsIndex);
        drawAllChunks();
    }
    if (m_anyLODUsesDCCM && m_dccmPipeline) {
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_dccmPipeline);
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, m_dccmPipelineLayout, 0, 1, &m_descriptorSets[imageIndex], 0, nullptr);
        const uint32_t originsIndex = useGPUCulling ? 1u : 0u;
        vkCmdPushConstants(cmd, m_dccmPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT, 0, sizeof(uint32_t), &originsIndex);
        drawAllChunks();
    }

    // Render celestial objects (sun and moon) before clouds
    m_celestialSystem.render(cmd, static_cast<uint32_t>(m_currentFrame), view, proj, m_cameraPos, m_lighting.timeOfDay);

    // Render volumetric clouds (transparent, after opaque geometry)
    // Use the SAME view and projection as terrain to ensure identical camera behavior
    // Note: clouds use per-frame descriptor sets (MAX_FRAMES=3), not per-image
    m_cloudSystem.render(cmd, static_cast<uint32_t>(m_currentFrame), view, proj, m_cameraPos, static_cast<float>(glfwGetTime()), m_lighting.timeOfDay);

    // Update and render light glows (additive blending for point lights)
    // ObjectManager sync already done in drawFrame() before updateLightingUniforms
    // Use m_lighting.totalTime so pulse animation syncs with terrain breathCycle
    m_lightGlowSystem.updateInstanceData(m_lighting.pointLights, m_cameraPos,
                                         &m_objectManager, &m_pulsePresets, m_lighting.totalTime);
    m_lightGlowSystem.render(cmd, static_cast<uint32_t>(m_currentFrame), view, proj, m_cameraPos, m_lighting.totalTime, m_lighting.activePointLights);

    // Render ImGui UI overlay (skip GPU recording when no ImGui frame was begun)
    if (m_imguiFrameActive) {
        VkViewport fullViewport{};
        fullViewport.x = 0.0f;
        fullViewport.y = static_cast<float>(m_swapchainExtent.height);
        fullViewport.width = static_cast<float>(m_swapchainExtent.width);
        fullViewport.height = -static_cast<float>(m_swapchainExtent.height);
        fullViewport.minDepth = 0.0f;
        fullViewport.maxDepth = 1.0f;
        VkRect2D fullScissor{};
        fullScissor.offset = {0, 0};
        fullScissor.extent = m_swapchainExtent;
        vkCmdSetViewport(cmd, 0, 1, &fullViewport);
        vkCmdSetScissor(cmd, 0, 1, &fullScissor);
        ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), cmd);
    }

    vkCmdEndRenderPass(cmd);
#endif

    m_imgui.setMainContextCurrent();

    // ── ImGui-only pass for main window when gameplay is separated (→ EngineGameplayRendering.cpp) ──
    recordGameplayWindowUIPass(cmd, imageIndex);

    // ── Temporal Hi-Z pyramid build end-of-frame (→ EngineDepthPrePass.cpp) ──
    recordPostRenderHiZBuild(cmd);

    // Timestamp 4: frameEnd (after main render pass)
    if (m_timestampQueryPool != VK_NULL_HANDLE) {
        vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, m_timestampQueryPool, timestampBase + 8);
    }

    m_cpuInitialCullRecordMs = cpuInitialCullRecordMs;
    m_cpuDepthPrepassRecordMs = 0.0f;
    m_cpuHiZBuildRecordMs = 0.0f;
    m_cpuFinalCullRecordMs = 0.0f;
    m_cpuHiZIncrementalRecordMs = 0.0f;

    if (vkEndCommandBuffer(cmd) != VK_SUCCESS)
        throw std::runtime_error("Failed to record command buffer!");
}

````

## src\core\engine\EngineGameplayRendering.cpp

Description: No CC-DESC found.

````cpp
// EngineGameplayRendering.cpp - Gameplay window management, ImGui UI passes, perf overlay
// Contains: renderPerfOverlay, pollAndPrepareGameplayWindow, recordGameplayOverlayFrame,
//           recordGameplayWindowUIPass

#include "core/engine/Engine.h"
#include "ui/EngineInterface.h"
#include "ui/debug_menu/world/ChunkMinimapWindow.h"
#include "ui/debug_menu/gameplay/CursorPlaceTool.h"
#include "ui/debug_menu/rendering/DirectionalShadowWindow.h"
#include "ui/debug_menu/world/TerrainEditTool.h"
#include <imgui.h>
#include <imgui_impl_glfw.h>
#include <imgui_impl_vulkan.h>
#include <array>
#include <cstring>

// ── renderPerfOverlay ───────────────────────────────────────────────────────────────────────
void Engine::renderPerfOverlay(float gameplayViewportOffsetX,
                               float gameplayViewportOffsetY,
                               int gameplayViewportW,
                               int gameplayViewportH) {
    if (!m_perfMode || !m_perfOverlayEnabled) {
        return;
    }

    const float usedMeshVramMiB =
        static_cast<float>(m_vbAllocator.getAllocatedBytes() + m_ibAllocator.getAllocatedBytes()) /
        (1024.0f * 1024.0f);
    const float totalMeshVramMiB =
        static_cast<float>(m_vbAllocator.getTotalCapacity() + m_ibAllocator.getTotalCapacity()) /
        (1024.0f * 1024.0f);

    ImGui::SetNextWindowPos(
        ImVec2(gameplayViewportOffsetX + 16.0f, gameplayViewportOffsetY + 16.0f),
        ImGuiCond_Always);
    ImGui::SetNextWindowBgAlpha(0.38f);

    ImGuiWindowFlags flags =
        ImGuiWindowFlags_NoDecoration |
        ImGuiWindowFlags_AlwaysAutoResize |
        ImGuiWindowFlags_NoSavedSettings |
        ImGuiWindowFlags_NoFocusOnAppearing |
        ImGuiWindowFlags_NoNav |
        ImGuiWindowFlags_NoMove |
        ImGuiWindowFlags_NoInputs;

    if (!ImGui::Begin("##PerfOverlay", nullptr, flags)) {
        ImGui::End();
        return;
    }

    ImGui::TextColored(ImVec4(0.65f, 0.95f, 0.65f, 1.0f), "PERF  %s", getStartupTerrainPresetName());
    ImGui::Separator();
    ImGui::Text("Avg FPS:   %.1f", m_perfOverlayAvgFps);
    ImGui::Text("Frame:     %.2f ms", m_perfOverlayAvgFrameMs);
    ImGui::Text("CPU work:  %.2f ms", m_perfOverlayAvgCpuWorkMs);
    ImGui::Text("World:     %.2f ms", m_perfOverlayAvgWorldMs);
    ImGui::Text("Render:    %.2f ms", m_perfOverlayAvgRenderMs);
    ImGui::Text("Culling:   %.2f ms", m_perfOverlayAvgCullingMs);
    ImGui::Text("Chunks:    %u / %u vis", m_perfOverlayVisibleChunks, m_perfOverlayTotalChunks);
    ImGui::Text("Ready:     %d", m_world.getReadyCount());
    ImGui::Text("Load/Mesh: %d / %d", m_world.getLoadingCount(), m_world.getMeshingCount());
    ImGui::Text("Mesh VRAM: %.1f / %.1f MiB", usedMeshVramMiB, totalMeshVramMiB);
    ImGui::Text("Viewport:  %dx%d", gameplayViewportW, gameplayViewportH);
    ImGui::Separator();
    ImGui::TextDisabled("%s", getStartupTerrainPresetSummary());
    ImGui::End();
}

// ── pollAndPrepareGameplayWindow ───────────────────────────────────────────────
// Polls gameplay window events and acquires its next image.
// Returns false if the window was closed and drawFrame should early-return
// (swapchain recreation has already been triggered).
bool Engine::pollAndPrepareGameplayWindow() {
    m_gameplayWindowAcquired = false;
    if (!m_gameplayWindow) {
        return true;
    }

    m_gameplayWindow->setVSync(m_vsyncEnabled);
    m_gameplayWindow->pollEvents(m_physicalDevice, m_device);

    if (!m_gameplayWindow->isOpen()) {
        // User closed the gameplay window via its close button
        vkDeviceWaitIdle(m_device);
        m_gameplayPixelPass.cleanup();
        m_gameplayTJunctionFix.cleanup();
        m_gameplayWindowSwapchainGeneration = 0;
        m_gameplayPixelPassSwapchainGeneration = 0;
        m_input.setGameplayWindow(nullptr);
        m_gameplayWindow->destroy(m_instance, m_device);
        m_gameplayWindow.reset();
        m_gameplaySeparated = false;
        m_gameplayOverlayFrameActive = false;
        syncHiZTarget(true);
        // Sync UI state back to Embedded
        if (m_input.areDebugWindowsVisible() && m_world.getDebugOverlay().isUsingEngineInterface()) {
            m_world.getDebugOverlay().getEngineInterface().setGameplayState(EngineInterface::GameplayState::Embedded);
        }
        recreateSwapchain();
        return false;  // Signal drawFrame to early-return
    }

    syncGameplayTJunctionFix();
    syncGameplayPixelPass();
    if (m_gameplayWindow) {
        m_gameplayWindowAcquired = m_gameplayWindow->acquireNextImage(m_device);
    }
    return true;
}

// ── recordGameplayOverlayFrame ─────────────────────────────────────────────────
// Renders ImGui/tool overlay into the gameplay window framebuffer (separated mode).
void Engine::recordGameplayOverlayFrame(bool gameplayOverlayRequested) {
    if (!gameplayOverlayRequested || !m_gameplayWindow || !m_gameplayWindowAcquired) {
        return;
    }

    const bool gameplayDetached =
        m_gameplaySeparated &&
        m_gameplayWindow &&
        m_gameplayWindow->isOpen();
    const bool gameplayStatsStripVisible =
        !m_perfMode &&
        gameplayDetached &&
        m_world.getDebugOverlay().isUsingEngineInterface();

    const VkExtent2D overlayExtent = m_gameplayWindow->getExtent();
    double overlayMouseX = 0.0;
    double overlayMouseY = 0.0;
    bool overlayMouseValid = false;
    if (m_input.isCursorEnabled()) {
        glfwGetCursorPos(m_gameplayWindow->getHandle(), &overlayMouseX, &overlayMouseY);
        overlayMouseX = std::clamp(overlayMouseX, 0.0,
                                   std::max(0.0, static_cast<double>(overlayExtent.width) - 1.0));
        overlayMouseY = std::clamp(overlayMouseY, 0.0,
                                   std::max(0.0, static_cast<double>(overlayExtent.height) - 1.0));
        overlayMouseValid = true;
    }

    m_imgui.beginGameplayOverlayFrame(
        static_cast<int>(overlayExtent.width),
        static_cast<int>(overlayExtent.height),
        std::max(0.001f, static_cast<float>(m_lastCpuFrameMs) / 1000.0f),
        overlayMouseX,
        overlayMouseY,
        overlayMouseValid);

    m_world.getDebugOverlay().getChunkMinimapWindow().renderHUDMinimap(
        0.0f,
        0.0f,
        static_cast<float>(overlayExtent.width),
        static_cast<float>(overlayExtent.height));

    const auto& cam = m_camera.getState();
    auto& cursorTool = m_world.getDebugOverlay().getCursorPlaceTool();
    auto& terrainEditTool = m_world.getDebugOverlay().getTerrainEditTool();
    const bool terrainEditActive = terrainEditTool.isActive();
    if (!terrainEditActive && cursorTool.isActive() && m_input.isCursorEnabled()) {
        cursorTool.renderPreviewOverlay(cam.viewProj,
                                        static_cast<int>(overlayExtent.width),
                                        static_cast<int>(overlayExtent.height),
                                        0.0f,
                                        0.0f);
    }
    if (terrainEditTool.isActive() && m_input.isCursorEnabled()) {
        terrainEditTool.renderPreviewOverlay(cam.viewProj,
                                             static_cast<int>(overlayExtent.width),
                                             static_cast<int>(overlayExtent.height),
                                             0.0f,
                                             0.0f);
    }

    // Sun-shadow cascade visualization (no-op unless enabled in panel).
    m_world.getDebugOverlay().getDirectionalShadowWindow().renderCascadeOverlay(
        cam.viewProj,
        static_cast<int>(overlayExtent.width),
        static_cast<int>(overlayExtent.height),
        0.0f,
        0.0f);
    if (gameplayStatsStripVisible) {
        m_world.getDebugOverlay().getEngineInterface().renderGameplayStatsStrip(
            ImVec2(0.0f, 0.0f),
            ImVec2(static_cast<float>(overlayExtent.width),
                   static_cast<float>(overlayExtent.height)),
            ImGui::GetForegroundDrawList());
    }

    m_imgui.endGameplayOverlayFrame();
    m_gameplayOverlayFrameActive = true;
}

// ── recordGameplayWindowUIPass ─────────────────────────────────────────────────
// Records the ImGui-only render pass for the main swapchain image when gameplay
// is separated into its own window. The 3D voxel pass rendered directly into the
// gameplay window — the main swapchain still needs a clear + ImGui pass.
void Engine::recordGameplayWindowUIPass(VkCommandBuffer cmd, uint32_t imageIndex) {
    const bool isSeparated = m_gameplaySeparated
                          && m_gameplayWindow && m_gameplayWindow->isOpen()
                          && m_gameplayWindowAcquired;
    if (!isSeparated) {
        return;
    }

    VkRenderPassBeginInfo uiPassInfo{};
    uiPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    uiPassInfo.renderPass = m_renderPass;
    uiPassInfo.framebuffer = m_swapchainFramebuffers[imageIndex];
    uiPassInfo.renderArea.offset = {0, 0};
    uiPassInfo.renderArea.extent = m_swapchainExtent;

    std::array<VkClearValue, 2> uiClear{};
    uiClear[0].color = {{0.12f, 0.12f, 0.12f, 1.0f}};
    uiClear[1].depthStencil = {0.0f, 0};
    uiPassInfo.clearValueCount = static_cast<uint32_t>(uiClear.size());
    uiPassInfo.pClearValues = uiClear.data();

    vkCmdBeginRenderPass(cmd, &uiPassInfo, VK_SUBPASS_CONTENTS_INLINE);

    VkViewport fullViewport{};
    fullViewport.x = 0.0f;
    fullViewport.y = static_cast<float>(m_swapchainExtent.height);
    fullViewport.width = static_cast<float>(m_swapchainExtent.width);
    fullViewport.height = -static_cast<float>(m_swapchainExtent.height);
    fullViewport.minDepth = 0.0f;
    fullViewport.maxDepth = 1.0f;
    VkRect2D fullScissor{};
    fullScissor.offset = {0, 0};
    fullScissor.extent = m_swapchainExtent;
    vkCmdSetViewport(cmd, 0, 1, &fullViewport);
    vkCmdSetScissor(cmd, 0, 1, &fullScissor);
    if (m_imguiFrameActive) {
        ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), cmd);
    }

    vkCmdEndRenderPass(cmd);
}

````

## src\core\engine\EngineDepthPrePass.cpp

Description: No CC-DESC found.

````cpp
// EngineDepthPrePass.cpp - GPU culling and temporal Hi-Z pyramid build
// Contains: recordInitialGPUCulling, recordPostRenderHiZBuild

#include "core/engine/Engine.h"
#include <chrono>
#include <algorithm>
#include <array>

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#include <glm/gtc/matrix_transform.hpp>

void Engine::recordInitialGPUCulling(
    VkCommandBuffer cmd, uint32_t imageIndex,
    const glm::mat4& viewProj,
    float viewportOffsetX, float viewportOffsetY,
    float viewportScaleX, float viewportScaleY,
    bool temporalHiZViable,
    bool& usedTemporalHiZ,
    float& cpuMs)
{
    using clock = std::chrono::high_resolution_clock;
    const uint32_t timestampBase = imageIndex * TIMESTAMPS_PER_IMAGE;

    auto cpuInitialCullStart = clock::now();
    if (m_gpuCullingEnabled && m_gpuCulling.isReady()) {
        // Suppress temporal-coherence skip when visibility is likely stale:
        //   a) Topology-edit uploads this frame — newly exposed chunks need a
        //      fresh Hi-Z test to avoid staying culled via stale visibility bits.
        //   b) Any meaningful camera movement — the previous visible set is not
        //      safe around high-parallax overhang disocclusions.
        constexpr float kPosThresholdM   = 0.02f;    // metres
        constexpr float kAngleThresholdR = 0.004363f; // ~0.25 degrees in radians

        bool cameraMovedFast = false;
        if (m_prevHiZFrameValid) {
            const CameraState& camState = m_camera.getState();
            const glm::vec3 curFront = glm::normalize(camState.front);

            const float posDelta   = glm::length(camState.position - m_prevHiZCameraPos);
            // dot clamped to [-1,1] to protect acos from NaN on denormals
            const float cosAngle   = glm::clamp(glm::dot(curFront, m_prevHiZCameraFront), -1.0f, 1.0f);
            const float angleDelta = std::acos(cosAngle);

            cameraMovedFast = (posDelta > kPosThresholdM) || (angleDelta > kAngleThresholdR);
        }

        const bool suppressTemporalCoherence = m_world.hadEditUploadsThisFrame() || cameraMovedFast;

        const auto& debugOverlay = m_world.getDebugOverlay();
        const bool hiZDebugOpen = debugOverlay.isHiZWindowOpen();
        const bool captureDebugStats = !m_perfMode &&
            (hiZDebugOpen || debugOverlay.isStatsWindowOpen() || debugOverlay.isFPSWindowOpen());
        const bool captureHiZBlinkLog = !m_perfMode && hiZDebugOpen;

        // Temporal Hi-Z (preferred) or frustum-only fallback.
        if (captureDebugStats) {
            m_gpuCulling.recordClearDebugStats(cmd);
        }
        if (captureHiZBlinkLog) {
            m_gpuCulling.recordClearHiZBlinkLog(cmd);
        }

        uint32_t hizW = 0, hizH = 0, hizMips = 0;

        if (temporalHiZViable) {
            usedTemporalHiZ = true;
            VkImageMemoryBarrier2 hiZSync{};
            hiZSync.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
            hiZSync.srcStageMask  = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
            hiZSync.srcAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
            hiZSync.dstStageMask  = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
            hiZSync.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
            hiZSync.oldLayout     = VK_IMAGE_LAYOUT_GENERAL;
            hiZSync.newLayout     = VK_IMAGE_LAYOUT_GENERAL;
            hiZSync.image         = m_hiZPyramid.getImage();
            hiZSync.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, VK_REMAINING_MIP_LEVELS, 0, 1 };

            VkDependencyInfo hiZDep{};
            hiZDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            hiZDep.imageMemoryBarrierCount = 1;
            hiZDep.pImageMemoryBarriers    = &hiZSync;
            vkCmdPipelineBarrier2(cmd, &hiZDep);

            hizW = m_hiZPyramid.getWidth();
            hizH = m_hiZPyramid.getHeight();
            hizMips = m_hiZPyramid.getMipLevels();
        }

        m_gpuCulling.recordCulling(cmd, viewProj, m_frameCullingTimelineValue, m_gpuCullingChunkCount,
                                   hizW, hizH, hizMips, m_prevViewProj,
                                   viewportOffsetX, viewportOffsetY, viewportScaleX, viewportScaleY,
                                   captureDebugStats, suppressTemporalCoherence, captureHiZBlinkLog);

        if (m_timestampQueryPool != VK_NULL_HANDLE) {
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, m_timestampQueryPool, timestampBase + 1);
        }

        const bool samplePerfDrawCount = shouldSamplePerfOverlayDrawCount();
        const bool captureDrawCountSample = samplePerfDrawCount;
        const bool captureMinimapReadback = !m_perfMode && m_minimapReadback.isEnabled();
        if (captureDrawCountSample || captureDebugStats || captureHiZBlinkLog || captureMinimapReadback) {
            m_gpuCulling.recordReadbackBarrier(cmd);
        }

        if (captureDrawCountSample) {
            m_gpuCulling.recordDrawCountReadback(cmd);
        }

        if (captureDebugStats) {
            m_gpuCulling.recordDebugStatsReadback(cmd);
        }
        if (captureHiZBlinkLog) {
            m_gpuCulling.recordHiZBlinkLogReadback(cmd);
        }
        if (captureMinimapReadback) {
            m_minimapReadback.recordReadback(cmd, static_cast<uint32_t>(m_currentFrame),
                                             m_gpuCulling.getVisibleOriginsBuffer(),
                                             m_gpuCulling.getDrawCountBuffer());
        }

        m_gpuCulling.recordBarriersBeforeDraw(cmd);

        // Update temporal state for next frame.
        const CameraState& camState = m_camera.getState();
        m_prevViewProj = viewProj;
        m_prevHiZCameraPos = camState.position;
        m_prevHiZCameraFront = glm::normalize(camState.front);
        m_prevHiZViewportUvTransform = glm::vec4(viewportOffsetX, viewportOffsetY, viewportScaleX, viewportScaleY);
        m_prevHiZFrameValid = true;
    } else {
        // No GPU culling - write dummy timestamp for culling phase
        if (m_timestampQueryPool != VK_NULL_HANDLE) {
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, m_timestampQueryPool, timestampBase + 1);
        }
    }
    cpuMs = std::chrono::duration<float, std::milli>(clock::now() - cpuInitialCullStart).count();
}

void Engine::recordPostRenderHiZBuild(VkCommandBuffer cmd) {
    if (!m_hiZPyramid.isReady()) {
        return;
    }
    // Always rebuild the pyramid from the FULL scene depth, even when
    // same-frame Hi-Z was used. The mid-frame pyramid was built from a
    // depth prepass that only contains a subset of chunks. The temporal
    // path in the NEXT frame reads this pyramid and needs complete coverage
    // — otherwise it sees zero-depth holes causing occlusion failures.

    const bool isSeparated = m_gameplaySeparated
                          && m_gameplayWindow && m_gameplayWindow->isOpen()
                          && m_gameplayWindowAcquired;

    TJunctionFixSystem* activeTJunctionFix =
        isSeparated
            ? (m_gameplayTJunctionFix.isReady() ? &m_gameplayTJunctionFix : nullptr)
            : &m_tjunctionFix;
    RetroPixelPassSystem* activePixelPass =
        isSeparated
            ? (m_gameplayPixelPass.isReady() ? &m_gameplayPixelPass : nullptr)
            : &m_pixelPass;
    const bool usePixelPass =
        activePixelPass && activePixelPass->isReady() && activePixelPass->getSettings().enabled;
    const bool useTJunction =
        activeTJunctionFix && activeTJunctionFix->isEnabled() && activeTJunctionFix->isReady() && !usePixelPass;
    VkImage   depthImg;
    VkImageView depthView;

    if (usePixelPass) {
        depthImg  = activePixelPass->getOffscreenDepthImage();
        depthView = activePixelPass->getOffscreenDepthView();
    } else if (useTJunction) {
        depthImg  = activeTJunctionFix->getOffscreenDepthImage();
        depthView = activeTJunctionFix->getOffscreenDepthView();
    } else if (isSeparated) {
        depthImg  = m_gameplayWindow->getDepthImage();
        depthView = m_gameplayWindow->getDepthView();
    } else {
        depthImg  = m_depthImage;
        depthView = m_depthView;
    }

    // Update pyramid's depth source if it changed
    m_hiZPyramid.updateDepthSource(depthView);

    if (usePixelPass || useTJunction) {
        // Final post-process paths sample the offscreen depth in a fragment shader,
        // so by the time we rebuild Hi-Z here the image is already in
        // SHADER_READ_ONLY_OPTIMAL. We only need to order the fragment reads
        // before the compute reads used by the pyramid build.
        VkImageMemoryBarrier2 depthBarrier{};
        depthBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        depthBarrier.srcStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
        depthBarrier.srcAccessMask = VK_ACCESS_2_SHADER_READ_BIT;
        depthBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
        depthBarrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
        depthBarrier.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        depthBarrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        depthBarrier.image = depthImg;
        depthBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
        depthBarrier.subresourceRange.baseMipLevel = 0;
        depthBarrier.subresourceRange.levelCount = 1;
        depthBarrier.subresourceRange.baseArrayLayer = 0;
        depthBarrier.subresourceRange.layerCount = 1;

        VkDependencyInfo depthDep{};
        depthDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        depthDep.imageMemoryBarrierCount = 1;
        depthDep.pImageMemoryBarriers = &depthBarrier;
        vkCmdPipelineBarrier2(cmd, &depthDep);
    } else {
        // Standard path: transition main depth from attachment to shader read
        VkImageMemoryBarrier2 depthBarrier{};
        depthBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        depthBarrier.srcStageMask = VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
        depthBarrier.srcAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        depthBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
        depthBarrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
        depthBarrier.oldLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
        depthBarrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        depthBarrier.image = depthImg;
        depthBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
        depthBarrier.subresourceRange.baseMipLevel = 0;
        depthBarrier.subresourceRange.levelCount = 1;
        depthBarrier.subresourceRange.baseArrayLayer = 0;
        depthBarrier.subresourceRange.layerCount = 1;

        VkDependencyInfo depthDep{};
        depthDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        depthDep.imageMemoryBarrierCount = 1;
        depthDep.pImageMemoryBarriers = &depthBarrier;
        vkCmdPipelineBarrier2(cmd, &depthDep);
    }

    // Build the Hi-Z pyramid (batched compute dispatches with internal barriers)
    m_hiZPyramid.recordBuildPyramid(cmd);

    // CRITICAL: Flush pyramid writes to device memory so the NEXT frame's temporal
    // culling can read them. Without this barrier, storage writes from the pyramid
    // build may not be visible across frame submissions even with timeline semaphores.
    VkImageMemoryBarrier2 pyramidFlush{};
    pyramidFlush.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    pyramidFlush.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    pyramidFlush.srcAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
    pyramidFlush.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    pyramidFlush.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    pyramidFlush.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
    pyramidFlush.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    pyramidFlush.image = m_hiZPyramid.getImage();
    pyramidFlush.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    pyramidFlush.subresourceRange.baseMipLevel = 0;
    pyramidFlush.subresourceRange.levelCount = VK_REMAINING_MIP_LEVELS;
    pyramidFlush.subresourceRange.baseArrayLayer = 0;
    pyramidFlush.subresourceRange.layerCount = 1;

    // Restore depth layout for the next frame's depth pass.
    // Hi-Z build samples depth in SHADER_READ_ONLY_OPTIMAL, but the next
    // render pass expects DEPTH_STENCIL_ATTACHMENT_OPTIMAL.
    VkImageMemoryBarrier2 depthRestore{};
    depthRestore.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    depthRestore.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    depthRestore.srcAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    depthRestore.dstStageMask = VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT |
                                VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
    depthRestore.dstAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
                                 VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
    depthRestore.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    depthRestore.newLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    depthRestore.image = depthImg;
    depthRestore.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
    depthRestore.subresourceRange.baseMipLevel = 0;
    depthRestore.subresourceRange.levelCount = 1;
    depthRestore.subresourceRange.baseArrayLayer = 0;
    depthRestore.subresourceRange.layerCount = 1;

    std::array<VkImageMemoryBarrier2, 2> postBuildBarriers = {pyramidFlush, depthRestore};
    VkDependencyInfo depthRestoreDep{};
    depthRestoreDep.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depthRestoreDep.imageMemoryBarrierCount = static_cast<uint32_t>(postBuildBarriers.size());
    depthRestoreDep.pImageMemoryBarriers = postBuildBarriers.data();
    vkCmdPipelineBarrier2(cmd, &depthRestoreDep);
}

````

## src\core\engine\EngineShadowPass.cpp

Description: No CC-DESC found.

````cpp
// EngineShadowPass.cpp - Shadow light budget selection and shadow render pass recording
// Contains: updateShadowsForFrame, recordShadowRenderPasses

#include "core/engine/Engine.h"

void Engine::updateShadowsForFrame(uint32_t imageIndex,
                                    const std::vector<PointLight>& transientLights,
                                    const glm::vec3& camPos,
                                    const glm::vec3& camFront) {
    // Detailed shadow counters use shader atomics and are expensive.
    // Keep them explicit/opt-in via Object Manager window to avoid skewing runtime FPS.
    const bool detailedShadowDiagnosticsEnabled =
        !m_perfMode &&
        m_input.areDebugWindowsVisible() &&
        m_world.getDebugOverlay()
            .getObjectManagerWindow()
            .isDetailedShadowDiagnosticsEnabled();
    m_shadowSystem.setDetailedDiagnosticsEnabled(detailedShadowDiagnosticsEnabled);

    // Select and update active realtime shadow-casting lights for this frame.
    m_shadowSystem.updateForFrame(imageIndex, m_lighting, transientLights, camPos, camFront);
}

void Engine::recordShadowRenderPasses(VkCommandBuffer cmd, uint32_t imageIndex) {
    ShadowSystem::DrawContext shadowCtx{};
    shadowCtx.terrainDescriptorSet = m_descriptorSets[imageIndex];
    shadowCtx.terrainVertexBuffer = m_vbAllocator.getPrimaryBuffer();
    shadowCtx.terrainIndexBuffer = m_ibAllocator.getPrimaryBuffer();
    shadowCtx.indirectBuffer = m_indirectBuffer;
    shadowCtx.indirectDrawCount = (m_indirectDrawCount == UINT32_MAX) ? 0u : m_indirectDrawCount;
    shadowCtx.world = &m_world;
    shadowCtx.uploadTimelineValue = m_uploadTimelineValue;
    shadowCtx.terrainEditRevision = m_world.getTerrainBoxRevision();
    shadowCtx.terrainMeshRevision = m_world.getMeshTopologyVersion();
    shadowCtx.objectManager = &m_objectManager;

    bool useGPUShadowCulling = (m_indirectDrawCount == UINT32_MAX) && m_gpuCullingEnabled && m_gpuCulling.isReady();
    shadowCtx.useGPUCulling = useGPUShadowCulling;
    if (useGPUShadowCulling) {
        shadowCtx.gpuVisibleDrawsBuffer = m_gpuCulling.getVisibleDrawsBuffer();
        shadowCtx.gpuDrawCountBuffer = m_gpuCulling.getDrawCountBuffer();
        shadowCtx.gpuMaxDraws = m_gpuCulling.getMaxDraws();
    }

    m_shadowSystem.recordShadowPasses(cmd, imageIndex, shadowCtx);
}

````

## src\core\engine\EngineDebugWiring.cpp

Description: No CC-DESC found.

````cpp
// EngineDebugWiring.cpp - Debug UI wiring extracted from Engine::initVulkan()
// Connects debug overlay windows to engine subsystems via callbacks.

#include "core/engine/Engine.h"
#include "rendering/common/VulkanHelpers.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/Chunk.h"
#include <iostream>
#include <algorithm>
#include <cstdlib>

void Engine::setGameplaySeparated(bool separate) {
    if (separate == m_gameplaySeparated) {
        return;
    }

    auto& engineUi = m_world.getDebugOverlay().getEngineInterface();
    m_gameplaySeparated = separate;

    if (separate) {
        int width = 1280;
        int height = 720;
        vkDeviceWaitIdle(m_device);
        const QueueFamilyIndices queueFamilies =
            VulkanHelpers::findQueueFamilies(m_physicalDevice, m_surface);
        if (!queueFamilies.presentFamily.has_value()) {
            m_gameplaySeparated = false;
            engineUi.setGameplayState(EngineInterface::GameplayState::Embedded);
            std::cout << "[Engine] Failed to find a present queue family for detached gameplay window"
                      << std::endl;
            return;
        }
        m_gameplayWindow = std::make_unique<GameplayWindow>();
        if (!m_gameplayWindow->create(m_instance, m_physicalDevice, m_device,
                                      queueFamilies.presentFamily.value(),
                                      width, height,
                                      m_renderPass, m_swapchainImageFormat,
                                      m_depthFormat, m_vsyncEnabled, "Gameplay")) {
            m_input.setGameplayWindow(nullptr);
            m_gameplaySeparated = false;
            engineUi.setGameplayState(EngineInterface::GameplayState::Embedded);
            m_gameplayWindow.reset();
            std::cout << "[Engine] Failed to create gameplay window" << std::endl;
        } else {
            m_input.setGameplayWindow(m_gameplayWindow->getHandle());
            syncGameplayTJunctionFix(true);
            syncHiZTarget(true);
            recreateSwapchain();
            engineUi.setGameplayState(EngineInterface::GameplayState::Separated);
            if (m_gameplayWindow) {
                std::cout << "[Engine] Gameplay window opened ("
                          << width << "x" << height << ")" << std::endl;
            }
        }
    } else {
        m_gameplayTJunctionFix.cleanup();
        m_gameplayWindowSwapchainGeneration = 0;
        if (m_gameplayWindow) {
            vkDeviceWaitIdle(m_device);
            m_input.setGameplayWindow(nullptr);
            m_gameplayWindow->destroy(m_instance, m_device);
            m_gameplayWindow.reset();
            std::cout << "[Engine] Gameplay window closed" << std::endl;
        }
        syncHiZTarget(true);
        recreateSwapchain();
        engineUi.setGameplayState(EngineInterface::GameplayState::Embedded);
    }
}

void Engine::initDebugWiring() {
    if (!m_gameplayOnlyMode) {
        auto& ui = m_world.getDebugOverlay().getEngineInterface();
        ui.setGameplaySeparateCallback([this](bool separate) {
            setGameplaySeparated(separate);
        });

        ui.setGameplayFullscreenCallback([this](bool fullscreen) {
            std::cout << "[Engine] Gameplay fullscreen mode "
                      << (fullscreen ? "ENABLED" : "DISABLED") << std::endl;
        });
    }

    // Hook up debug windows to engine state
    m_world.getDebugOverlay().setLightingSettings(&m_lighting);
    m_world.getDebugOverlay().setTimeManager(&m_timeManager);
    m_world.getDebugOverlay().getShaderHotReloadWindow().setShaderService(&m_shaderHotReload);
    m_world.getDebugOverlay().getCursorSettingsWindow().setCursorManager(&m_cursorManager);
    m_world.getDebugOverlay().getRenderSettingsWindow().setIsFullscreen(&m_isFullscreen);
    m_world.getDebugOverlay().getRenderSettingsWindow().setToggleFullscreenCallback([this]() { toggleFullscreen(); });
    m_world.getDebugOverlay().getRenderSettingsWindow().setResetChunkGenerationCallback([this]() {
        // Wait for all GPU work to complete before destroying resources.
        // resetChunkGeneration frees VB/IB slices and GPU culling slots,
        // which in-flight frames may still reference.
        vkDeviceWaitIdle(m_device);
        m_world.resetChunkGeneration();
    });
    m_world.getDebugOverlay().getRenderSettingsWindow().setApplyLODIncrementalCallback([this](int newRenderDist) {
        // No vkDeviceWaitIdle needed: incremental LOD uses the frame-budgeted
        // remesh pipeline which handles GPU resource replacement automatically.
        m_world.applyLODChangesIncrementally(newRenderDist);
    });
    m_world.getDebugOverlay().getRenderSettingsWindow().setGPUCullingEnabled(&m_gpuCullingEnabled);
    m_world.getDebugOverlay().getRenderSettingsWindow().setGameplaySeparated(&m_gameplaySeparated);
    
    // Wire up VSync toggle
    m_world.getDebugOverlay().getRenderSettingsWindow().setVsyncEnabled(&m_vsyncEnabled);
    m_world.getDebugOverlay().getRenderSettingsWindow().setSetVsyncCallback([this](bool enabled) {
        m_vsyncEnabled = enabled;
        if (m_gameplayWindow) {
            m_gameplayWindow->setVSync(enabled);
        }
        std::cout << "[Engine] VSync " << (enabled ? "ON (FIFO/MAILBOX)" : "OFF (IMMEDIATE)") << std::endl;
        recreateSwapchain();
    });
    
    // Wire up per-LOD terrain type configuration
    m_world.getDebugOverlay().getRenderSettingsWindow().setWorld(&m_world);
    m_world.getDebugOverlay().getRenderSettingsWindow().setTerrainTypeChangedCallback([this](int lodLevel, TerrainType type) {
        std::cout << "[Engine] LOD " << lodLevel << " terrain type changed to: " 
                  << (type == TerrainType::DCCM ? "DCCM" : "Voxel") << std::endl;
        m_world.setTerrainTypeForLOD(lodLevel, type);
        // Update rendering flags
        m_anyLODUsesVoxel = m_world.anyLODUsesType(TerrainType::Voxel);
        m_anyLODUsesDCCM = m_world.anyLODUsesType(TerrainType::DCCM);
    });
    
    // Wire up per-band data LOD override (Voxel terrain only)
    m_world.getDebugOverlay().getRenderSettingsWindow().setDataLODChangedCallback([this](int band, int dataLOD) {
        std::cout << "[Engine] Band " << band << " data LOD changed to: " << dataLOD << std::endl;
        m_world.setDataLODForBand(band, dataLOD);
    });
    
    // Wire ObjectManager and PulsePresetLibrary to CursorPlaceTool
    m_world.getDebugOverlay().getCursorPlaceTool().setPulsePresetLibrary(&m_pulsePresets);
    m_world.getDebugOverlay().getCursorPlaceTool().setObjectManager(&m_objectManager);
    
    // Wire ObjectManager and PulsePresetLibrary to ObjectManagerWindow
    m_world.getDebugOverlay().getObjectManagerWindow().setObjectManager(&m_objectManager);
    m_world.getDebugOverlay().getObjectManagerWindow().setPulsePresetLibrary(&m_pulsePresets);
    m_world.getDebugOverlay().getObjectManagerWindow().setLightGlowSystem(&m_lightGlowSystem);
    m_world.getDebugOverlay().getObjectManagerWindow().setShadowSystem(&m_shadowSystem);
    m_world.getDebugOverlay().getDirectionalShadowWindow().setShadowSystem(&m_shadowSystem);
    m_world.getDebugOverlay().getDirectionalShadowWindow().setLightingSettings(&m_lighting);
    m_world.getDebugOverlay().getSkyEnclosureWindow().setShadowSystem(&m_shadowSystem);
    m_shadowSystem.setTimeManager(&m_timeManager);
    // Wire delete callback for ObjectManagerWindow
    // When an object is deleted via the inspector, we must remove it from the
    // actual rendering systems AND fix up indices for remaining objects.
    m_world.getDebugOverlay().getObjectManagerWindow().setDeleteCallback(
        [this](uint32_t objId, const PlacedObject& obj) {
            if (obj.type == PlacedObjectType::LightOrb) {
                uint32_t removedIdx = obj.light.lightIndex;
                m_lighting.removePointLight(removedIdx);
                
                // Fix up lightIndex for all remaining light orbs
                // (removePointLight erases from vector, shifting subsequent indices down)
                for (auto& [id, other] : m_objectManager.getAllObjectsMutable()) {
                    if (other.type == PlacedObjectType::LightOrb && id != objId
                        && other.light.lightIndex > removedIdx) {
                        other.light.lightIndex--;
                    }
                }
                std::cout << "[Engine] Deleted light orb #" << objId 
                          << " (lightIndex=" << removedIdx << ")" << std::endl;
            }
        }
    );
    
    // Hook up controls window to input/camera/player systems
    m_world.getDebugOverlay().getControlsWindow().setEngineInput(&m_input);
    m_world.getDebugOverlay().getControlsWindow().setCameraController(&m_camera);
    m_world.getDebugOverlay().getControlsWindow().setPlayerController(&m_player);
    m_world.getDebugOverlay().getControlsWindow().setPlayerCamera(&m_playerCamera);
    
    // Set up light spawn callback for ChunkVramWindow (light at chunk center above terrain)
    m_world.getDebugOverlay().getChunkVramWindow().setAddLightAtChunkCallback(
        [this](entt::entity entity, const glm::ivec3& chunkCoord) {
            // Calculate chunk center in world coordinates (meters)
            float chunkCenterX = (chunkCoord.x + 0.5f) * WorldConfig::CHUNK_SIZE_M;
            float chunkCenterZ = (chunkCoord.z + 0.5f) * WorldConfig::CHUNK_SIZE_M;
            
            // Get terrain height from chunk's AABB - place light exactly at terrain surface
            float terrainHeight = 10.0f;  // Default fallback
            if (m_world.getRegistry().valid(entity) && m_world.getRegistry().all_of<AABB>(entity)) {
                const auto& aabb = m_world.getRegistry().get<AABB>(entity);
                terrainHeight = aabb.max.y;  // Exactly at terrain surface (top of mesh)
            }
            
            // Create a new point light at chunk center, above terrain
            PointLight light;
            light.position = glm::vec3(chunkCenterX, terrainHeight, chunkCenterZ);
            light.radius = 32.0f;  // 32 meter radius to cover chunk (32m chunk size at 4 vox/m)
            light.color = glm::vec3(1.0f, 0.9f, 0.3f);  // Warm yellow color
            light.intensity = 2.5f;
            
            uint32_t lightIdx = m_lighting.addPointLight(light);
            m_objectManager.addLightOrb(light.position, light.radius, light.intensity,
                                        light.color, 0, lightIdx);
            std::cout << "[Engine] Added light at chunk (" << chunkCoord.x << "," << chunkCoord.z 
                      << ") -> world pos (" << light.position.x << ", " << light.position.y 
                      << ", " << light.position.z << ")" << std::endl;
        }
    );
    
    // Set up light spawn callback in ChunkDebugWindow
    m_world.getDebugOverlay().getChunkDebugWindow().setAddLightCallback(
        [this](const glm::vec3& cameraPos) {
            // Create a new point light at actual camera position
            PointLight light;
            light.position = m_camera.getState().position;  // Use camera controller position
            light.radius = 5.0f;  // 5 meter radius
            light.color = glm::vec3(1.0f, 0.9f, 0.3f);  // Warm yellow color
            light.intensity = 2.0f;
            
            uint32_t lightIdx = m_lighting.addPointLight(light);
            
            // Register in ObjectManager with default lamp preset
            m_objectManager.addLightOrb(light.position, light.radius, light.intensity,
                                        light.color, 0, lightIdx);
            
            std::cout << "[Engine] Added light at (" << light.position.x << ", " 
                      << light.position.y << ", " << light.position.z << ")" << std::endl;
        }
    );
    m_world.getDebugOverlay().getChunkDebugWindow().setBottleneckReportCallback(
        [this]() {
            return generateFrameBottleneckReport();
        }
    );
    
    // Set up Cursor Place Tool callbacks
    m_world.getDebugOverlay().getCursorPlaceTool().setPlaceLightCallback(
        [this](const glm::vec3& position) {
            PointLight light;
            light.position = position;
            // Use color/radius/intensity from CursorPlaceTool
            auto& tool = m_world.getDebugOverlay().getCursorPlaceTool();
            light.radius = tool.getLightRadius();
            light.color = tool.getLightColor();
            light.intensity = tool.getLightIntensity();
            
            uint32_t lightIdx = m_lighting.addPointLight(light);
            
            // Register in ObjectManager with pulse preset
            uint32_t presetIdx = tool.getLightPulsePresetIndex();
            m_objectManager.addLightOrb(position, light.radius, light.intensity,
                                        light.color, presetIdx, lightIdx);
            
            std::cout << "[Engine] Cursor tool placed light at (" << light.position.x << ", " 
                      << light.position.y << ", " << light.position.z 
                      << ") preset=" << m_pulsePresets.getPreset(presetIdx).name << std::endl;
        }
    );
    
    // Wire raycast function for mouse-based placing
    m_world.getDebugOverlay().getCursorPlaceTool().setRaycastFunc(
        [this](const glm::vec3& origin, const glm::vec3& direction, float maxDist,
               glm::vec3& outPos, glm::vec3& outNormal) -> bool {
            auto result = m_physics.raycast(origin, direction, maxDist);
            if (result.hit) {
                outPos = result.position;
                outNormal = result.normal;
            }
            return result.hit;
        }
    );

    // Terrain edit tool uses the same world raycast path as placement.
    m_world.getDebugOverlay().getTerrainEditTool().setRaycastFunc(
        [this](const glm::vec3& origin, const glm::vec3& direction, float maxDist,
               glm::vec3& outPos, glm::vec3& outNormal) -> bool {
            auto result = m_physics.raycast(origin, direction, maxDist);
            if (result.hit) {
                outPos = result.position;
                outNormal = result.normal;
            }
            return result.hit;
        }
    );

    m_world.getDebugOverlay().getTerrainEditTool().setApplyEditCallback(
        [this](const glm::vec3& minCorner, const glm::vec3& maxCorner, bool additive, float snapStep, int brushShape) {
            if (m_world.applyTerrainBoxEdit(minCorner, maxCorner, additive, snapStep, brushShape)) {
                std::cout << "[Engine] Terrain " << (additive ? "build" : "dig")
                          << " edit applied to snapshot '" << m_world.getActiveSnapshotName()
                          << "'" << std::endl;
            } else {
                std::cout << "[Engine] Terrain edit skipped or failed" << std::endl;
            }
        }
    );

    m_world.getDebugOverlay().getTerrainEditTool().setWorld(&m_world);

    // Texture paint tool uses identical world-raycast path as terrain editing.
    m_world.getDebugOverlay().getTexturePaintTool().setWorld(&m_world);
    m_world.getDebugOverlay().getTexturePaintTool().setRaycastFunc(
        [this](const glm::vec3& origin, const glm::vec3& direction, float maxDist,
               glm::vec3& outPos, glm::vec3& outNormal) -> bool {
            auto result = m_physics.raycast(origin, direction, maxDist);
            if (result.hit) {
                outPos = result.position;
                outNormal = result.normal;
            }
            return result.hit;
        }
    );
}

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

## include\core\engine\EngineTypes.h

Description: No CC-DESC found. C++ struct 'PerFrame'.

````cpp
#pragma once

#include <vulkan/vulkan.h>

// Per-frame synchronization (using per-swapchain-image command buffers)
struct PerFrame {
    VkSemaphore imageAvailable;
    VkSemaphore renderFinishedMain;
    VkSemaphore renderFinishedGameplay;
    VkFence inFlight;
};

enum class StartupTerrainPreset {
    Default = 0,
    PerfVoxel4Lod20,
    PerfVoxel20DccmFar
};

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

## src\world\WorldUpdate.cpp

Description: No CC-DESC found.

````cpp
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "world/config/WorldConfig.h"
#include "world/chunks/core/ChunkJobs.h"
#include "vulkan/BufferSuballocator.h"
#include "vulkan/UploadArena.h"
#include "rendering/common/VulkanHelpers.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <iostream>
#include <algorithm>
#include <cmath>
#include <chrono>

// update(), updateChunkLoader(), updateMarkDirtyOnGeneration()
// See also: WorldUpdateLODScan.cpp, WorldUpdateMeshing.cpp, WorldUpdateFinalize.cpp

void World::update(float deltaTime, const glm::vec3& cameraPos,
                   float cameraYaw,
                   BufferSuballocator* vbAllocator,
                   BufferSuballocator* ibAllocator,
                   UploadArena* uploadArena,
                   ResourceUploader* uploader,
                   uint64_t uploadReadyValue,
                   float cpuFrameMs,
                   float gpuFrameMs,
                   uint64_t deviceTimeline) {
    // Timing for CPU breakdown display
    auto startTime = std::chrono::high_resolution_clock::now();

    // System 1: Chunk loader (create/destroy/remesh chunks in circular area)
    auto chunkLoadStart = std::chrono::high_resolution_clock::now();
    updateChunkLoader(deltaTime, cameraPos, cameraYaw);
    auto chunkLoadEnd = std::chrono::high_resolution_clock::now();

    // System 2: Mark newly generated chunks as dirty
    updateMarkDirtyOnGeneration();

    // System 3: Meshing system (kick off jobs for dirty chunks)
    auto meshingStart = std::chrono::high_resolution_clock::now();
    updateMeshingSystem();
    auto meshingEnd = std::chrono::high_resolution_clock::now();

    // System 3b: Terrain edit re-mesh (greedy-mesh dirty edited chunks)
    m_editRemeshScheduler.processRemeshQueue(this, /*budget=*/0);

    // System 4: Upload queue (upload finished meshes to GPU)
    auto uploadStart = std::chrono::high_resolution_clock::now();
    m_hadUploadsThisFrame = false;
    m_hadEditUploadsThisFrame = false;
    // Tick down the post-edit Hi-Z cooldown so temporal-visibility reuse stays
    // suppressed for a few frames after each edit upload, not just upload frame.
    if (m_hiZEditCooldown > 0) { --m_hiZEditCooldown; }
    if (vbAllocator && ibAllocator && uploadArena && uploader) {
        // Smooth bulk streaming uploads instead of draining every finished mesh
        // in one frame. The previous unlimited path could feed finalize with
        // 200-350 chunks per frame, adding CPU time even when the camera was
        // stable. Keep burst recovery high after teleports, but use a bounded
        // steady-state budget so looking at a small area does not inherit the
        // full streaming backlog cost. Edit/late uploads below remain immediate.
        constexpr size_t kSteadyStreamingUploadBudget = 96;
        constexpr size_t kBurstStreamingUploadBudget = 256;
        const size_t streamingUploadBudget =
            (m_burstRecoveryFrames > 0)
                ? kBurstStreamingUploadBudget
                : kSteadyStreamingUploadBudget;

        size_t uploaded = updateUploadQueueSystem(
            vbAllocator,
            ibAllocator,
            uploadArena,
            uploader,
            uploadReadyValue,
            streamingUploadBudget,
            /*terrainEditOnly=*/false);
        m_hadUploadsThisFrame = (uploaded > 0);
        if (m_uploadSystem.consumeRemeshUploadCount() > 0) {
            m_hadEditUploadsThisFrame = true;
            m_hiZEditCooldown = 8;  // suppress temporal skip for 8 more frames after last topology edit
        }
    }
    auto uploadEnd = std::chrono::high_resolution_clock::now();

    // System 5: Finalize queue (mark chunks as Ready after upload)
    // Reset per-frame diagnostics, then let processFinalizeQueue + processLODSwaps populate it
    m_currentFinalizeDiag = FinalizeDiagFrame{};
    m_currentFinalizeDiag.frameNumber = m_finalizeDiagFrameCounter++;
    auto finalizeStart = std::chrono::high_resolution_clock::now();
    processFinalizeQueue();

    // System 5b: LOD batch swap (atomically swap staged meshes when batch is complete)
    if (vbAllocator && ibAllocator) {
        processLODSwaps(vbAllocator, ibAllocator, deviceTimeline);
        processSoloPendingSwaps(vbAllocator, ibAllocator, deviceTimeline);
        processDeferredMeshBufferFrees(vbAllocator, ibAllocator);
    }

    // Late visual catch-up: pick up edit remesh jobs that finished mid-frame,
    // then immediately upload/finalize them instead of waiting a whole frame.
    if (vbAllocator && ibAllocator && uploadArena && uploader) {
        auto lateFlushStart = std::chrono::high_resolution_clock::now();
        const size_t lateEditUploadsQueued = m_editRemeshScheduler.flushReadyCompletions(this);
        auto lateUploadStart = std::chrono::high_resolution_clock::now();
        m_currentFinalizeDiag.lateFlushMs +=
            std::chrono::duration<float, std::milli>(lateUploadStart - lateFlushStart).count();
        size_t lateUploaded = 0;
        if (lateEditUploadsQueued > 0) {
            lateUploaded = updateUploadQueueSystem(
                vbAllocator,
                ibAllocator,
                uploadArena,
                uploader,
                uploadReadyValue,
                lateEditUploadsQueued,
                /*terrainEditOnly=*/true);
            auto lateUploadEnd = std::chrono::high_resolution_clock::now();
            m_currentFinalizeDiag.lateUploadMs +=
                std::chrono::duration<float, std::milli>(lateUploadEnd - lateUploadStart).count();
        }
        m_hadUploadsThisFrame = m_hadUploadsThisFrame || (lateUploaded > 0);
        if (m_uploadSystem.consumeRemeshUploadCount() > 0) {
            m_hadEditUploadsThisFrame = true;
            m_hiZEditCooldown = 8;  // suppress temporal skip for 8 more frames after last topology edit
        }
        if (lateUploaded > 0 || m_uploadSystem.getFinalizeQueueSize() > 0) {
            auto lateFinalizeStart = std::chrono::high_resolution_clock::now();
            processFinalizeQueue();
            auto lateFinalizeEnd = std::chrono::high_resolution_clock::now();
            m_currentFinalizeDiag.lateFinalizeMs +=
                std::chrono::duration<float, std::milli>(lateFinalizeEnd - lateFinalizeStart).count();
        }
        if (lateUploaded > 0 || m_uploadSystem.getFinalizeQueueSize() > 0) {
            auto lateSwapStart = std::chrono::high_resolution_clock::now();
            processLODSwaps(vbAllocator, ibAllocator, deviceTimeline);
            processSoloPendingSwaps(vbAllocator, ibAllocator, deviceTimeline);
            processDeferredMeshBufferFrees(vbAllocator, ibAllocator);
            auto lateSwapEnd = std::chrono::high_resolution_clock::now();
            m_currentFinalizeDiag.lateSwapMs +=
                std::chrono::duration<float, std::milli>(lateSwapEnd - lateSwapStart).count();
        }
    }
    auto finalizeEnd = std::chrono::high_resolution_clock::now();
    m_currentFinalizeDiag.totalMs = std::chrono::duration<float, std::milli>(finalizeEnd - finalizeStart).count();

    // Update LOD switch progress tracker (must run after both processLODSwaps passes)
    updateLODSwitchDiag();

    // System 6: Deferred collision building
    auto collisionStart = std::chrono::high_resolution_clock::now();
    m_collisionSystem.processPendingCollisions(m_registry, m_registryMutex);
    auto collisionEnd = std::chrono::high_resolution_clock::now();

    // Detect chunks that have render geometry but no physics collider — see
    // World::scanForGhostGeometry for the rationale and which silent drops it
    // catches. Self-throttled internally.
    scanForGhostGeometry();

    if (m_lastEditDiag.valid) {
        m_lastEditDiag.pendingRemeshChunks =
            static_cast<uint32_t>(std::min<size_t>(m_editRemeshScheduler.pendingCount(), UINT32_MAX));
        m_lastEditDiag.pendingUploadChunks = m_uploadSystem.getQueueSize();
        m_lastEditDiag.pendingFinalizeChunks =
            static_cast<uint32_t>(std::min<size_t>(m_uploadSystem.getFinalizeQueueSize(), UINT32_MAX));
        m_lastEditDiag.visualPendingChunks =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisualChunks.size(), UINT32_MAX));
        m_lastEditDiag.visualPendingEdits =
            static_cast<uint32_t>(std::min<size_t>(m_pendingEditVisuals.size(), UINT32_MAX));
        m_lastEditDiag.asyncFinalizeMs =
            std::chrono::duration<float, std::milli>(finalizeEnd - finalizeStart).count();
        m_lastEditDiag.asyncFinalizeCount = m_currentFinalizeDiag.finalizeCount;
        m_lastEditDiag.asyncLodSwapEntityCount = m_currentFinalizeDiag.lodSwapEntityCount;
        m_lastEditDiag.asyncLodSwapFreeMs = m_currentFinalizeDiag.lodSwapFreeMs;
    }

    // Copy edit-path collision timing into the edit diagnostics struct
    {
        float editCollMs = m_collisionSystem.consumeLastEditCollisionMs();
        if (editCollMs > 0.0f && m_lastEditDiag.valid) {
            m_lastEditDiag.collisionBvhMs = editCollMs;
            m_lastEditDiag.collisionTotalMs = std::chrono::duration<float, std::milli>(collisionEnd - collisionStart).count();
            m_lastEditDiag.grandTotalMs = m_lastEditDiag.applyTotalMs + m_lastEditDiag.remeshTotalMs + m_lastEditDiag.collisionBvhMs;
        }
    }

    // Store in ring buffer only if there was actual finalize/LOD-swap work.
    // Idle frames would overwrite useful data in the fixed-size ring buffer,
    // making the report appear empty once the world reaches steady state.
    if (m_currentFinalizeDiag.finalizeCount > 0 || m_currentFinalizeDiag.lodSwapEntityCount > 0) {
        if (m_finalizeDiagHistory.size() < FINALIZE_DIAG_CAPACITY) {
            m_finalizeDiagHistory.push_back(m_currentFinalizeDiag);
        } else {
            m_finalizeDiagHistory[m_finalizeDiagWriteIdx] = m_currentFinalizeDiag;
        }
        m_finalizeDiagWriteIdx = (m_finalizeDiagWriteIdx + 1) % FINALIZE_DIAG_CAPACITY;
    }

    auto worldUpdateEnd = std::chrono::high_resolution_clock::now();

    m_lastUpdateBreakdown.chunkLoadingMs =
        std::chrono::duration<float, std::milli>(chunkLoadEnd - chunkLoadStart).count();
    m_lastUpdateBreakdown.meshingMs =
        std::chrono::duration<float, std::milli>(meshingEnd - meshingStart).count();
    m_lastUpdateBreakdown.uploadMs =
        std::chrono::duration<float, std::milli>(uploadEnd - uploadStart).count();
    m_lastUpdateBreakdown.collisionMs =
        std::chrono::duration<float, std::milli>(collisionEnd - collisionStart).count();
    m_lastUpdateBreakdown.finalizeMs =
        std::chrono::duration<float, std::milli>(finalizeEnd - finalizeStart).count();
    m_lastUpdateBreakdown.worldUpdateMs =
        std::chrono::duration<float, std::milli>(worldUpdateEnd - startTime).count();

    // Periodic buffer and chunk statistics
    static int statsCounter = 0;
    if (++statsCounter % 6000 == 0) {
        if (vbAllocator && ibAllocator) {
            // Stats tracking (no logging)
        }
    }

    // Update in-game debug display (delegated to WorldDebugMetrics.cpp)
    UpdateTimings timings;
    timings.startTime = startTime;
    timings.chunkLoadStart = chunkLoadStart;
    timings.chunkLoadEnd = chunkLoadEnd;
    timings.meshingStart = meshingStart;
    timings.meshingEnd = meshingEnd;
    timings.uploadStart = uploadStart;
    timings.uploadEnd = uploadEnd;
    timings.collisionStart = collisionStart;
    timings.collisionEnd = collisionEnd;
    timings.finalizeStart = finalizeStart;
    timings.finalizeEnd = finalizeEnd;
    timings.worldUpdateEnd = worldUpdateEnd;
    assembleDebugInfo(timings, vbAllocator, ibAllocator, cpuFrameMs, gpuFrameMs);
}

void World::updateChunkLoader(float deltaTime, const glm::vec3& cameraPos, float cameraYaw) {
    // Update camera position for background thread
    m_lastCameraPos = cameraPos;
    m_lastCameraYaw = cameraYaw;  // For minimap view cone
    
    // Ring-based chunk management: get chunks to create/destroy
    std::vector<ChunkManager::ChunkCreateRequest> chunksToCreate;
    std::vector<glm::ivec3> chunksToDestroy;
    
    // Check buffer capacity to prevent crashes (cached — avoids per-frame mutex lock)
    bool bufferLimitReached = false;
    if (m_vbAllocator && m_ibAllocator) {
        static int bufferCheckCounter = 0;
        static float cachedVbUtil = 0.0f;
        static float cachedIbUtil = 0.0f;
        if (++bufferCheckCounter >= 10) { // Check every ~0.17s at 60fps
            bufferCheckCounter = 0;
            auto vbTotal = m_vbAllocator->getTotalCapacity();
            auto ibTotal = m_ibAllocator->getTotalCapacity();
            if (vbTotal > 0 && ibTotal > 0) {
                cachedVbUtil = static_cast<float>(m_vbAllocator->getAllocatedBytes()) / vbTotal;
                cachedIbUtil = static_cast<float>(m_ibAllocator->getAllocatedBytes()) / ibTotal;
            }
        }
        if (cachedVbUtil > 0.80f || cachedIbUtil > 0.80f) {
            bufferLimitReached = true;
        }
    }
    
    std::shared_lock setLock(m_chunkSetMutex);
    m_chunkManager->update(deltaTime, cameraPos, m_readyChunkSet, m_existingChunkSet, chunksToCreate, chunksToDestroy, bufferLimitReached);
    setLock.unlock();
    
    bool centerChanged = m_chunkManager->wasCenterChanged();

    // Detect large teleport (explosion knockback, etc.)
    // moveDist > 5 triggers burst recovery: accelerated LOD scans and
    // full upload throughput so the world fills in as fast as possible.
    int moveDist = 0;
    if (centerChanged) {
        glm::ivec3 newCenter = m_chunkManager->getCenterChunk();
        glm::ivec3 prevCenter = m_chunkManager->getPreviousCenter();
        moveDist = std::max(std::abs(newCenter.x - prevCenter.x),
                            std::abs(newCenter.z - prevCenter.z));
        if (moveDist > 5) {
            m_burstRecoveryFrames = 10;
        }
    }

    // Burst recovery still accelerates LOD scanning after teleports, but the
    // upload/finalize path now always drains at full throughput.

    // Queue destructions for background thread — BATCHED (single lock instead of per-coord)
    if (!chunksToDestroy.empty()) {
        m_lifecycleManager.queueDestructions(chunksToDestroy);
        m_lodSystem.clearDesiredLODs(chunksToDestroy);
    }

    // On center change, purge any pending creations that are now out of range.
    // Without this, chunks queued during forward movement stay in the lifecycle
    // manager's creation queue even after the player reverses direction, and
    // get created as orphans that never appear in a destroy sweep.
    if (centerChanged) {
        glm::ivec3 newCenter = m_chunkManager->getCenterChunk();
        int renderDist = m_chunkManager->getEffectiveRenderDistance();
        auto purgedCreates = m_lifecycleManager.purgeCreationQueue(
            [&](const glm::ivec3& coord) {
                int ring = m_chunkManager->calculateRingNumber(coord, newCenter);
                return ring >= renderDist;
            });
        if (!purgedCreates.empty()) {
            std::lock_guard lock(m_pendingChunksMutex);
            for (const auto& coord : purgedCreates) {
                m_pendingChunks.erase(coord);
            }
        }
        if (!purgedCreates.empty()) {
            m_chunkManager->cancelPendingCreates(purgedCreates);
        }

        // If center moved back toward previously-queued destroys, cancel those
        // obsolete destruction requests before worker thread executes them.
        auto purgedDestroys = m_lifecycleManager.purgeDestructionQueue(
            [&](const glm::ivec3& coord) {
                int ring = m_chunkManager->calculateRingNumber(coord, newCenter);
                return ring < renderDist;
            });
        if (!purgedDestroys.empty()) {
            m_chunkManager->cancelPendingDestroys(purgedDestroys);
        }
    }

    // Out-of-range sweep: SKIPPED — ChunkManager::update already produced
    // outChunksToDestroy via its trailing-edge pass, and the lifecycle
    // manager purge above cleans stale creation queue entries.
    // A second full O(N) scan of ~26K entries added ~0.9ms for no benefit.

    // Queue creations — BATCHED (single lock each for pending check + LOD + lifecycle)
    if (!chunksToCreate.empty()) {
        // Batch LOD updates (1 lock instead of N)
        std::vector<std::pair<glm::ivec3, int>> lodEntries;
        lodEntries.reserve(chunksToCreate.size());
        for (const auto& req : chunksToCreate) {
            lodEntries.push_back({req.coord, req.lodLevel});
        }
        m_lodSystem.setDesiredLODs(lodEntries);

        // Batch pending check + mark (1 lock instead of 2N)
        std::vector<glm::ivec3> nonPendingCoords;
        nonPendingCoords.reserve(chunksToCreate.size());
        {
            std::lock_guard lock(m_pendingChunksMutex);
            for (const auto& req : chunksToCreate) {
                if (m_pendingChunks.find(req.coord) == m_pendingChunks.end()) {
                    nonPendingCoords.push_back(req.coord);
                    m_pendingChunks.insert(req.coord);
                }
            }
        }

        // Batch lifecycle queue (1 lock instead of N)
        if (!nonPendingCoords.empty()) {
            m_lifecycleManager.queueCreations(nonPendingCoords);
        }
    }

    updateLODTransitions(deltaTime, centerChanged);

    // Wake up background thread if there's work
    if (!chunksToCreate.empty() || !chunksToDestroy.empty()) {
        m_lifecycleManager.wakeUp();
    }
}

void World::updateMarkDirtyOnGeneration() {
    // Job pipeline handles state transitions automatically via dependency chain
}

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

## include\world\WorldDiagnostics.h

Description: No CC-DESC found. C++ struct 'CullingStats'.

````cpp
#pragma once

// =============================================================================
// WorldDiagnostics.h — Extracted diagnostic / history / stats structs from
// World.h to keep the main world header focused on the public API.
//
// All types here are exposed through the World class as type aliases so that
// existing call sites (`World::TerrainEditDiag`, `World::LODSwitchDiag`, etc.)
// continue to compile unchanged. New code may reference them directly via
// `WorldDiag::*`.
// =============================================================================

#include <glm/glm.hpp>

#include <chrono>
#include <cstdint>
#include <string>
#include <vector>

#include "world/WorldTypes.h"

namespace WorldDiag {

// --- Culling statistics (populated by Engine, displayed in debug HUD) ---
struct CullingStats {
    bool gpuCullingEnabled = false;
    bool gpuCullingReady = false;
    uint32_t totalChunksInCulling = 0;
    uint32_t visibleDrawCalls = 0;
    uint32_t culledDrawCalls = 0;
    uint32_t frustumPassed = 0;  // Chunks that passed frustum culling
    // GPU timing (ms)
    float cullingDispatchMs = 0.0f; // Frustum culling compute
    float totalCullingMs = 0.0f;    // Total culling overhead
};

// --- Terrain edit diagnostics (per-step ms timings) ---
struct TerrainEditDiag {
    uint64_t editId{0};

    // applyTerrainBoxEdit step
    float cellWriteMs{0.0f};      // Cell loop (overlay writes)
    float invalidateMs{0.0f};     // invalidateEditArtifacts
    float chunkMarkMs{0.0f};      // markChunksDirty
    float inlineRemeshMs{0.0f};   // processRemeshQueue(dispatchOnly=true) inline call
    float boxListMs{0.0f};        // Box list update / snapshot dirty flag
    float applyTotalMs{0.0f};     // Total applyTerrainBoxEdit
    uint64_t changedCells{0};
    uint64_t totalCells{0};

    // processRemeshQueue step
    float dispatchDrainMs{0.0f};  // inline drainCompletions during dispatch
    float dispatchYRangeMs{0.0f}; // getEditVoxelYRange total across chunks
    float dispatchHeightMs{0.0f}; // getHeightRangeForChunk total across chunks
    uint32_t dispatchInFlightSkip{0}; // chunks skipped due to inFlight
    uint32_t editJobsInFlight{0};     // m_inFlightCount snapshot at end of dispatch
    float meshMs{0.0f};           // Greedy mesher (meshChunk)
    float collisionEnqueueMs{0.0f}; // enqueueEditCollision
    float gpuUploadEnqueueMs{0.0f}; // enqueueMeshForUpload
    float remeshTotalMs{0.0f};    // Total processRemeshQueue
    uint32_t chunksRemeshed{0};
    uint32_t vertexCount{0};
    uint32_t indexCount{0};

    // processPendingCollisions step (edit path only)
    float collisionBvhMs{0.0f};   // Jolt BVH creation
    float collisionTotalMs{0.0f}; // Total edit collision processing

    // Async pipeline state that affects when edits become visible.
    // These are not folded into grandTotalMs because they are later-frame
    // pipeline stages rather than synchronous apply/remesh work.
    uint32_t pendingRemeshChunks{0};   // dirty + in-flight edit chunks
    uint32_t pendingUploadChunks{0};   // ChunkUploadSystem queue depth
    uint32_t pendingFinalizeChunks{0}; // finalize queue depth
    uint32_t visualPendingChunks{0};   // edit chunks still waiting to appear
    uint32_t visualPendingEdits{0};    // edits that still have unseen chunks
    float asyncFinalizeMs{0.0f};       // finalize + LOD swap stage this frame
    uint32_t asyncFinalizeCount{0};    // chunks finalized this frame
    uint32_t asyncLodSwapEntityCount{0}; // LOD swap entities this frame
    float asyncLodSwapFreeMs{0.0f};    // deferred frees during LOD swap

    // End-to-end visual latency: edit start until the updated chunk mesh
    // is finalized and can appear on screen.
    float visualFirstChunkMs{0.0f};
    float visualCompleteMs{0.0f};
    uint32_t visualChunksTotal{0};
    uint32_t visualChunksReady{0};
    uint32_t visualChunksSuperseded{0};
    bool visualComplete{false};
    uint64_t visualUploadBytes{0};
    uint32_t visualArtifactBuilds{0};
    uint32_t visualArtifactCacheHits{0};
    uint32_t visualPrecomputedLoads{0};
    uint32_t visualCollisionBaseCache{0};
    uint32_t visualCollisionEditPacked{0};
    uint32_t visualCollisionArtifactRefresh{0};
    uint32_t visualCollisionExistingEdit{0};
    uint32_t visualGpuResidentChunks{0};
    uint32_t visualArtifactResidentChunks{0};
    uint32_t visualMonolithicChunks{0};
    uint32_t visualPagedChunks{0};
    uint32_t visualDirtyPages{0};
    uint32_t visualRebuiltPages{0};
    uint32_t visualResidentPages{0};
    uint32_t visualEvictedPages{0};

    // Grand total across all steps
    float grandTotalMs{0.0f};
    bool valid{false};            // True when at least one edit has been timed

    // Overlay fill list sizes (for monitoring deferred fill accumulation)
    uint32_t sphereFillCount{0};
    uint32_t boxFillCount{0};
    uint32_t cylinderFillCount{0};
    uint32_t brickCount{0};

    // Edit position in world-space (for world overlay)
    glm::vec3 editCenter{0.0f};
    float editSize{0.0f};
};

// Rolling statistics over recent edits
struct TerrainEditStats {
    static constexpr size_t CAPACITY = 64;
    float applyHistory[CAPACITY]{};
    float remeshHistory[CAPACITY]{};
    float grandHistory[CAPACITY]{};
    float cellWriteHistory[CAPACITY]{};
    float collEnqueueHistory[CAPACITY]{};
    size_t count{0};
    size_t writeIdx{0};

    // Running aggregates
    float avgApplyMs{0.0f};
    float avgRemeshMs{0.0f};
    float avgGrandMs{0.0f};
    float maxApplyMs{0.0f};
    float maxRemeshMs{0.0f};
    float maxGrandMs{0.0f};
    float maxCellWriteMs{0.0f};
    float maxCollEnqueueMs{0.0f};

    void push(const TerrainEditDiag& d) {
        applyHistory[writeIdx]  = d.applyTotalMs;
        remeshHistory[writeIdx] = d.remeshTotalMs;
        grandHistory[writeIdx]  = d.grandTotalMs;
        cellWriteHistory[writeIdx] = d.cellWriteMs;
        collEnqueueHistory[writeIdx] = d.collisionEnqueueMs;
        writeIdx = (writeIdx + 1) % CAPACITY;
        if (count < CAPACITY) ++count;
        recompute();
    }
    void recompute() {
        float sumA = 0, sumR = 0, sumG = 0;
        float mxA = 0, mxR = 0, mxG = 0;
        float mxCW = 0, mxCE = 0;
        for (size_t i = 0; i < count; ++i) {
            sumA += applyHistory[i]; if (applyHistory[i] > mxA) mxA = applyHistory[i];
            sumR += remeshHistory[i]; if (remeshHistory[i] > mxR) mxR = remeshHistory[i];
            sumG += grandHistory[i]; if (grandHistory[i] > mxG) mxG = grandHistory[i];
            if (cellWriteHistory[i] > mxCW) mxCW = cellWriteHistory[i];
            if (collEnqueueHistory[i] > mxCE) mxCE = collEnqueueHistory[i];
        }
        const float n = static_cast<float>(count);
        avgApplyMs = (count > 0) ? sumA / n : 0;
        avgRemeshMs = (count > 0) ? sumR / n : 0;
        avgGrandMs = (count > 0) ? sumG / n : 0;
        maxApplyMs = mxA; maxRemeshMs = mxR; maxGrandMs = mxG;
        maxCellWriteMs = mxCW; maxCollEnqueueMs = mxCE;
    }
};

// --- Terrain edit history (individual edit log entries) ---
struct TerrainEditHistoryEntry {
    uint64_t editId{0};
    float applyMs{0.0f};
    float remeshMs{0.0f};
    float grandMs{0.0f};
    uint32_t chunksRemeshed{0};
    uint32_t vertexCount{0};
    uint64_t changedCells{0};
    float visualFirstChunkMs{0.0f};
    float visualCompleteMs{0.0f};
    uint32_t visualChunksTotal{0};
    uint32_t visualChunksReady{0};
    uint32_t visualChunksSuperseded{0};
    bool visualComplete{false};
    uint64_t visualUploadBytes{0};
    uint32_t visualArtifactBuilds{0};
    uint32_t visualArtifactCacheHits{0};
    uint32_t visualPrecomputedLoads{0};
    uint32_t visualCollisionBaseCache{0};
    uint32_t visualCollisionEditPacked{0};
    uint32_t visualCollisionArtifactRefresh{0};
    uint32_t visualCollisionExistingEdit{0};
    uint32_t visualGpuResidentChunks{0};
    uint32_t visualArtifactResidentChunks{0};
    uint32_t visualMonolithicChunks{0};
    uint32_t visualPagedChunks{0};
    uint32_t visualDirtyPages{0};
    uint32_t visualRebuiltPages{0};
    uint32_t visualResidentPages{0};
    uint32_t visualEvictedPages{0};
    glm::vec3 editCenter{0.0f};
    float editSize{0.0f};
    float timestampSec{0.0f};  // seconds since engine start
};

struct TerrainEditHistory {
    static constexpr size_t CAPACITY = 128;
    TerrainEditHistoryEntry entries[CAPACITY]{};
    size_t count{0};
    size_t writeIdx{0};
    uint64_t totalCount{0};

    void push(const TerrainEditHistoryEntry& e) {
        entries[writeIdx] = e;
        writeIdx = (writeIdx + 1) % CAPACITY;
        if (count < CAPACITY) ++count;
        ++totalCount;
    }

    // Iterate entries from newest to oldest
    const TerrainEditHistoryEntry& getFromEnd(size_t reverseIdx) const {
        size_t idx = (writeIdx + CAPACITY - 1 - reverseIdx) % CAPACITY;
        return entries[idx];
    }
};

// --- Load management snapshot for HUD / per-edit attribution ---
struct LoadManagementDiag {
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
    bool bufferPressure{false};
};

// --- Per-chunk visual history (upload → finalize) ---
struct ChunkVisualHistoryEntry {
    uint64_t sequence{0};
    glm::ivec3 chunkCoord{0};
    int lodLevel{0};
    int meshLodLevel{-1};   // Actual LOD the mesher ran at; -1 if not from edit pipeline.
    uint64_t vramBytes{0};
    uint32_t vertexCount{0};
    uint32_t indexCount{0};
    float pipelineMs{0.0f};  // Upload enqueue -> finalize ready
    float visibleMs{0.0f};   // Edit apply -> finalize ready (or pipelineMs for non-edits)
    bool fromEdit{false};
    uint64_t editId{0};
    uint32_t consecutiveReloads{0};  // Consecutive [Load] on this chunk since last [Edit] (0 for [Edit] entries)
    float timestampSec{0.0f};
    uint64_t uploadBytes{0};
    uint64_t artifactGeneration{0};
    ChunkArtifactSource artifactSource{ChunkArtifactSource::Unknown};
    ChunkCollisionSource collisionSource{ChunkCollisionSource::Unknown};
    ChunkResidencyKind residency{ChunkResidencyKind::Unknown};
    ChunkWorkModel workModel{ChunkWorkModel::Unknown};
    uint8_t meshMode{0xFF};
    uint16_t subChunkCount{0};
    uint16_t dirtyPages{0};
    uint16_t rebuiltPages{0};
    uint16_t residentPages{0};
    uint16_t evictedPages{0};
    bool artifactCacheHit{false};
    bool artifactCacheResident{false};
    bool fromLodBatch{false};

    // Per-stage breakdown (edit chunks only, 0 for non-edits)
    float waitDispatchMs{0.0f};  // editStart -> dispatch
    float waitJobMs{0.0f};       // dispatch -> jobStart (queue wait)
    float meshMs{0.0f};          // jobStart -> meshDone
    float waitDrainMs{0.0f};     // meshDone -> drainTime
    float uploadMs{0.0f};        // drainTime -> finalizeTime
    bool isFastMode{false};

    // Mesh sub-stage breakdown (from TerrainEditMesher::MeshStats)
    float cacheBuildMs{0.0f};
    float greedyMeshMs{0.0f};
    float postProcessMs{0.0f};
    float downsampleMs{0.0f};            // LOD downsample loop (LOD>0 + overlay only)
    uint8_t downsampleCacheState{0};     // 0=miss, 1=full hit, 2=partial hit
    // Tier B Phase 1 scaffolding (band-Y plumbing; meshing not yet clipped).
    int      bandLocalYMin{-1};
    int      bandLocalYMax{-1};
    bool     bandActive{false};
    uint32_t bandFacesEmitted{0};
    uint32_t cacheVoxels{0};
    uint32_t solidVoxels{0};
    uint32_t facesEmitted{0};
    int scanYRange{0};
    int cacheDimXZ{0};
    bool adaptiveEnabled{false};
    uint32_t adaptiveLeafRegions{0};
    uint32_t adaptiveSplitRegions{0};
    uint32_t adaptiveMaxDepth{0};
    uint32_t adaptivePeakRegionVoxels{0};
    uint32_t adaptivePeakYRange{0};
    uint64_t adaptiveWorkVoxels{0};
    uint64_t monolithicWorkVoxels{0};

    // Overlay state at time of edit (for diagnosing fill accumulation)
    uint32_t sphereFills{0};
    uint32_t boxFills{0};
    uint32_t cylinderFills{0};
    uint32_t bricks{0};

    // Load-management snapshot at dispatch time (explains long apply->dispatch delays)
    int loadBaseRenderDist{0};
    int loadEffectiveRenderDist{0};
    int loadExtensionRings{0};
    float loadMeasuredThroughput{0.0f};
    uint32_t loadPendingCreates{0};
    uint32_t loadPendingDestroys{0};
    uint32_t loadLodRemeshQueue{0};
    uint32_t loadPendingLodRemeshes{0};
    uint32_t loadEditRemeshPending{0};
    uint32_t loadUploadQueue{0};
    uint32_t loadFinalizeQueue{0};
    uint32_t loadInFlightSkips{0};
    bool loadBufferPressure{false};

    // In-flight job count snapshot at dispatch time
    uint32_t loadEditJobsInFlight{0};
};

struct ChunkVisualHistory {
    static constexpr size_t CAPACITY = 1024;
    ChunkVisualHistoryEntry entries[CAPACITY]{};
    size_t count{0};
    size_t writeIdx{0};
    uint64_t totalCount{0};

    void push(const ChunkVisualHistoryEntry& e) {
        entries[writeIdx] = e;
        writeIdx = (writeIdx + 1) % CAPACITY;
        if (count < CAPACITY) ++count;
        ++totalCount;
    }

    const ChunkVisualHistoryEntry& getFromEnd(size_t reverseIdx) const {
        size_t idx = (writeIdx + CAPACITY - 1 - reverseIdx) % CAPACITY;
        return entries[idx];
    }
};

// --- Per-chunk visual error history ---
struct ChunkVisualErrorEntry {
    uint64_t sequence{0};
    bool hasChunkCoord{false};
    glm::ivec3 chunkCoord{0};
    int lodLevel{-1};
    uint32_t batchId{0};
    uint32_t expectedVersion{0};
    uint32_t actualVersion{0};
    uint32_t uploadQueue{0};
    uint32_t finalizeQueue{0};
    uint32_t pendingEditRemesh{0};
    uint32_t pendingLodRemesh{0};
    std::string stage;
    std::string reason;
    float timestampSec{0.0f};
    uint64_t uploadBytes{0};
    uint64_t artifactGeneration{0};
    ChunkArtifactSource artifactSource{ChunkArtifactSource::Unknown};
    ChunkCollisionSource collisionSource{ChunkCollisionSource::Unknown};
    ChunkResidencyKind residency{ChunkResidencyKind::Unknown};
    ChunkWorkModel workModel{ChunkWorkModel::Unknown};
    uint8_t meshMode{0xFF};
    uint16_t subChunkCount{0};
    uint16_t dirtyPages{0};
    uint16_t rebuiltPages{0};
    uint16_t residentPages{0};
    uint16_t evictedPages{0};
    bool artifactCacheHit{false};
    bool artifactCacheResident{false};
    bool fromLodBatch{false};
};

struct ChunkVisualErrorHistory {
    static constexpr size_t CAPACITY = 1024;
    ChunkVisualErrorEntry entries[CAPACITY]{};
    size_t count{0};
    size_t writeIdx{0};
    uint64_t totalCount{0};

    void push(const ChunkVisualErrorEntry& e) {
        entries[writeIdx] = e;
        writeIdx = (writeIdx + 1) % CAPACITY;
        if (count < CAPACITY) ++count;
        ++totalCount;
    }

    const ChunkVisualErrorEntry& getFromEnd(size_t reverseIdx) const {
        size_t idx = (writeIdx + CAPACITY - 1 - reverseIdx) % CAPACITY;
        return entries[idx];
    }
};

// --- Finalize diagnostics (for debugging world update spikes) ---
struct FinalizeDiagFrame {
    uint64_t frameNumber{0};
    float totalMs{0.0f};

    // processFinalizeQueue breakdown
    uint32_t finalizeCount{0};           // entities drained from queue
    float drainMs{0.0f};                 // queue drain (no locks)
    float regLockWaitMs{0.0f};           // time WAITING for registry unique_lock
    float regLockHeldMs{0.0f};           // time HOLDING registry lock (validate+set state)
    float stateMapLockMs{0.0f};          // m_chunkStateMutex lock+work
    float readySetLockMs{0.0f};          // m_chunkSetMutex lock+work
    float notifyMs{0.0f};               // notifyChunksCreated (m_pendingOpsMutex)
    float clearPendingMs{0.0f};          // m_pendingChunksMutex lock+work
    float visualReadyMs{0.0f};           // noteChunkVisualReady + history/hole tracking
    float lodMismatchMs{0.0f};           // data-LOD mismatch requeue/error attribution
    float collisionRefreshMs{0.0f};      // refreshEditedChunkCollisionFromArtifact
    float inFlightClearMs{0.0f};         // clear ChunkVersionState::inFlight
    float topologyRecordMs{0.0f};        // recordMeshTopologyChanges

    // processLODSwaps breakdown
    uint32_t lodSwapBatchCount{0};       // number of LOD batches processed
    uint32_t lodSwapEntityCount{0};      // total entities swapped
    float lodSwapLockWaitMs{0.0f};       // time WAITING for registry lock
    float lodSwapLockHeldMs{0.0f};       // time HOLDING registry lock
    float lodSwapFreeMs{0.0f};           // deferred buffer/slot frees
    uint32_t lodSwapFreeQueuedCount{0};  // old mesh buffer frees enqueued this frame
    uint32_t lodSwapFreeDrainedCount{0}; // old mesh buffer frees drained this frame
    uint32_t lodSwapFreeBacklog{0};      // remaining old mesh buffer frees after this frame

    // Late visual catch-up in World::update(), included in totalMs.
    float lateFlushMs{0.0f};             // edit scheduler flushReadyCompletions
    float lateUploadMs{0.0f};            // updateUploadQueueSystem inside finalize window
    float lateFinalizeMs{0.0f};          // second processFinalizeQueue pass
    float lateSwapMs{0.0f};              // second LOD/solo swap pass
};

// --- LOD Switch diagnostics (populated by setDataLODForBand + worldUpdate) ---
struct LODSwitchDiag {
    bool active{false};
    int band{0};
    int oldDataLOD{0};
    int newDataLOD{0};
    std::chrono::steady_clock::time_point startTime{};

    // Initial scan counts (set once by setDataLODForBand)
    uint32_t totalChunksInBand{0};       // total chunks scanned in this band
    uint32_t totalChunksQueued{0};        // Ready chunks queued for remesh
    uint32_t deferredChunks{0};           // non-Ready at switch time
    uint32_t deferredLoading{0};
    uint32_t deferredMeshing{0};
    uint32_t deferredOther{0};
    uint32_t skippedAlreadyCorrect{0};    // chunks already at target data LOD
    uint32_t skippedDCCM{0};              // DCCM chunks (always LOD 0)
    uint32_t batchesCreated{0};
    float setupMs{0.0f};                  // time to run setDataLODForBand scan
    uint32_t cancelledOldBatches{0};       // batches cancelled from previous switch

    // Updated each frame while active
    uint32_t chunksSwappedTotal{0};
    uint32_t lastFrameSwapped{0};
    uint32_t activeBatches{0};
    uint32_t pendingRemeshes{0};
    uint32_t peakActiveBatches{0};
    uint32_t lodRemeshQueueSize{0};       // m_lodSystem queue
    uint32_t uploadQueueSize{0};          // upload system queue
    uint32_t finalizeQueueSize{0};        // finalize queue
    float elapsedMs{0.0f};
    float completedMs{0.0f};              // >0 when pipeline drained
    uint64_t uploadedBytesTotal{0};
    uint32_t readyVisualEntries{0};
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

    // Post-completion audit (runs once after completedMs is set)
    bool auditDone{false};
    uint32_t auditStuckChunks{0};         // chunks still at wrong dataLod
    uint32_t auditStuckNotReady{0};       // stuck + not-Ready state
    uint32_t auditStuckReady{0};          // stuck + Ready (should have been caught)
    float auditMs{0.0f};                  // when audit was performed

    // Frame history for sparkline (last 120 frames)
    static constexpr size_t SPARKLINE_SIZE = 120;
    uint32_t sparkline[SPARKLINE_SIZE]{};
    size_t sparklineIdx{0};

    // Error tracking
    uint32_t errFilteredByDrain{0};       // chunks dropped by drain filter (now fixed)
    uint32_t errInvalidEntities{0};       // entities gone during batch swap
    uint32_t errMissingPending{0};        // missing PendingMeshHandle at swap
    uint32_t errMismatchedBatch{0};       // PendingMeshHandle belonged to wrong batch
    uint32_t errTotalFromSwaps{0};        // sum from all processLODSwaps calls
};

// --- Per-frame breakdown for HUD ---
struct LastUpdateBreakdown {
    float worldUpdateMs = 0.0f;
    float chunkLoadingMs = 0.0f;
    float meshingMs = 0.0f;
    float uploadMs = 0.0f;
    float collisionMs = 0.0f;
    float finalizeMs = 0.0f;
};

} // namespace WorldDiag

````

## src\rendering\culling\GPUCullingReadback.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/culling/GPUCullingSystem.h"
#include <cstring>
#include <algorithm>
#include <unordered_map>

// Number of debug stats (must match shader)
constexpr uint32_t DEBUG_STATS_COUNT = 16;
constexpr uint32_t FRUSTUM_LOCAL_SIZE_X = 64;

void GPUCullingSystem::recordReadbackBarrier(VkCommandBuffer cmd) {
    if (!m_initialized) return;

    // transfer/compute writes must complete before transfer reads.
    // This also covers zero-dispatch frames where counters were reset via fills.
    VkMemoryBarrier2 memBarrier{};
    memBarrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
    memBarrier.srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    memBarrier.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT | VK_ACCESS_2_SHADER_WRITE_BIT;
    memBarrier.dstStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
    memBarrier.dstAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;

    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.memoryBarrierCount = 1;
    depInfo.pMemoryBarriers = &memBarrier;

    vkCmdPipelineBarrier2(cmd, &depInfo);
}

void GPUCullingSystem::recordDrawCountReadback(VkCommandBuffer cmd) {
    if (!m_initialized || !m_readbackBuffer) return;

    VkBufferCopy copyRegion{};
    copyRegion.srcOffset = 0;
    copyRegion.dstOffset = 0;
    copyRegion.size = sizeof(uint32_t);
    
    vkCmdCopyBuffer(cmd, m_drawCountBuffer, m_readbackBuffer, 1, &copyRegion);
    m_drawCountReadbackPending = true;
}

void GPUCullingSystem::updateDrawCountFromReadback() {
    if (!m_initialized) return;

    if (m_drawCountReadbackPending && m_readbackMapped) {
        m_lastVisibleDrawCount.store(*m_readbackMapped, std::memory_order_relaxed);
    }
    m_drawCountReadbackPending = false;
    const bool hiZBlinkLogReadbackPending = m_hiZBlinkLogReadbackPending;
    m_hiZBlinkLogReadbackPending = false;

    uint32_t frameCount = 0;
    uint32_t frameDropped = 0;
    const uint8_t* frameSrc = nullptr;
    std::unordered_map<uint32_t, HiZBlinkEvent> frameHiZOcclusionBySlot;

    if (hiZBlinkLogReadbackPending && m_hiZBlinkLogMapped) {
        uint32_t header[2];
        std::memcpy(header, m_hiZBlinkLogMapped, sizeof(header));
        frameCount = std::min<uint32_t>(header[0], HIZ_BLINK_LOG_GPU_CAPACITY);
        frameDropped = header[1];
        frameSrc = m_hiZBlinkLogMapped + sizeof(uint32_t) * 4;
        frameHiZOcclusionBySlot.reserve(frameCount);
        for (uint32_t i = 0; i < frameCount; ++i) {
            HiZBlinkEvent ev;
            std::memcpy(&ev, frameSrc + i * sizeof(HiZBlinkEvent), sizeof(HiZBlinkEvent));
            // One entry per chunk per frame in current shader path. Keep first on collisions.
            frameHiZOcclusionBySlot.emplace(ev.chunkIdx, ev);
        }
    }

    // Drain Hi-Z blink log into the CPU-side accumulating ring (legacy log path).
    if (hiZBlinkLogReadbackPending &&
        m_hiZBlinkLogMapped &&
        !m_hiZBlinkLogPaused.load(std::memory_order_relaxed)) {
        std::lock_guard<std::mutex> lock(m_hiZBlinkLogMutex);
        m_hiZBlinkLogLastFrameCount = frameCount;
        m_hiZBlinkLogLastFrameDropped = frameDropped;
        m_hiZBlinkLogTotalDroppedGpu += frameDropped;

        if (frameCount > 0) {
            if (m_hiZBlinkLogRing.size() < HIZ_BLINK_LOG_CPU_CAPACITY) {
                m_hiZBlinkLogRing.resize(HIZ_BLINK_LOG_CPU_CAPACITY);
            }

            for (uint32_t i = 0; i < frameCount; ++i) {
                HiZBlinkEvent ev;
                std::memcpy(&ev, frameSrc + i * sizeof(HiZBlinkEvent), sizeof(HiZBlinkEvent));
                if (m_hiZBlinkLogRingFull) {
                    ++m_hiZBlinkLogTotalDroppedCpu;
                }
                m_hiZBlinkLogRing[m_hiZBlinkLogRingHead] = ev;
                m_hiZBlinkLogRingHead = (m_hiZBlinkLogRingHead + 1) % HIZ_BLINK_LOG_CPU_CAPACITY;
                if (m_hiZBlinkLogRingHead == 0) m_hiZBlinkLogRingFull = true;
                ++m_hiZBlinkLogTotalCaptured;
            }
        }
    }

    {
        std::lock_guard<std::mutex> lock(m_slotMutex);

        if (!m_pendingEditDispatch.tracked.empty()) {
            const uint64_t dispatchSerial = m_pendingEditDispatch.dispatchSerial;
            m_lastTrackedEditChunks.clear();
            m_lastTrackedEditChunks.reserve(m_pendingEditDispatch.tracked.size());

            for (const PendingTrackedEditChunk& tracked : m_pendingEditDispatch.tracked) {
                const auto occIt = frameHiZOcclusionBySlot.find(tracked.slot);
                const bool hiZOccluded = (occIt != frameHiZOcclusionBySlot.end());

                EditVisibilityState state = EditVisibilityState::Unknown;
                if (!tracked.slotOccupied) {
                    state = EditVisibilityState::NotDrawnSlotInactive;
                } else if (tracked.subChunkCount == 0u) {
                    state = EditVisibilityState::NotDrawnZeroSubChunks;
                } else if (tracked.validDrawCount == 0u) {
                    state = EditVisibilityState::NotDrawnNoValidDraws;
                } else if (!tracked.ready) {
                    state = EditVisibilityState::NotDrawnNotReady;
                } else if (!tracked.frustumPassed) {
                    state = EditVisibilityState::NotDrawnFrustum;
                } else if (tracked.hiZActive && hiZOccluded) {
                    state = EditVisibilityState::NotDrawnHiZOccluded;
                } else if (!tracked.hiZEnabled) {
                    state = EditVisibilityState::VisibleNoHiZ;
                } else if (!tracked.hiZActive) {
                    state = EditVisibilityState::VisibleHiZGrace;
                } else {
                    state = EditVisibilityState::VisibleHiZPassed;
                }

                const bool drawn = editVisibilityStateIsDrawn(state);
                const int32_t graceDelta = static_cast<int32_t>(tracked.currentTimeline)
                                         - static_cast<int32_t>(tracked.hiZGraceTimeline);

                EditVisibilityTrackedChunk chunkState;
                chunkState.slot = tracked.slot;
                chunkState.chunkX = tracked.chunkX;
                chunkState.chunkY = tracked.chunkY;
                chunkState.chunkZ = tracked.chunkZ;
                chunkState.state = state;
                chunkState.drawn = drawn;
                chunkState.fromTerrainEdit = tracked.fromTerrainEdit;
                chunkState.replacesExistingMesh = tracked.replacesExistingMesh;
                chunkState.hiZEnabled = tracked.hiZEnabled;
                chunkState.hiZActive = tracked.hiZActive;
                chunkState.frustumPassed = tracked.frustumPassed;
                chunkState.ready = tracked.ready;
                chunkState.subChunkCount = tracked.subChunkCount;
                chunkState.validDrawCount = tracked.validDrawCount;
                chunkState.currentTimeline = tracked.currentTimeline;
                chunkState.gpuReadyTimeline = tracked.gpuReadyTimeline;
                chunkState.hiZGraceTimeline = tracked.hiZGraceTimeline;
                chunkState.graceDelta = graceDelta;
                chunkState.uploadSerial = tracked.uploadSerial;
                chunkState.editUploadSerial = tracked.editUploadSerial;
                chunkState.watchFramesRemaining = tracked.watchFramesRemaining;
                if (hiZOccluded) {
                    chunkState.nearestDepth = occIt->second.nearestDepth;
                    chunkState.pyramidDepth = occIt->second.pyramidDepth;
                    chunkState.mipLevel = occIt->second.mipLevel;
                }
                m_lastTrackedEditChunks.push_back(chunkState);

                if (tracked.slot >= m_editWatchStates.size()) {
                    continue;
                }

                EditWatchSlotState& watch = m_editWatchStates[tracked.slot];
                const bool prevKnown = watch.lastDrawnKnown;
                const bool prevDrawn = watch.lastDrawn;
                const EditVisibilityState prevState = watch.lastState;

                const bool transition = prevKnown && (prevDrawn != drawn);
                // Stability rule:
                // - Always keep transition records.
                // - Also keep every NotDrawn sample so logs stay populated with
                //   the current reason instead of disappearing between transitions.
                const bool shouldEmitEvent = transition || !drawn;

                if (shouldEmitEvent) {
                    EditVisibilityEvent event;
                    event.sequence = ++m_editVisibilityEventSerial;
                    event.dispatchSerial = dispatchSerial;
                    event.slot = tracked.slot;
                    event.chunkX = tracked.chunkX;
                    event.chunkY = tracked.chunkY;
                    event.chunkZ = tracked.chunkZ;
                    event.previousState = prevKnown ? prevState : state;
                    event.newState = state;
                    event.fromTerrainEdit = tracked.fromTerrainEdit;
                    event.replacesExistingMesh = tracked.replacesExistingMesh;
                    event.drawnBefore = prevKnown ? prevDrawn : drawn;
                    event.drawnAfter = drawn;
                    event.currentTimeline = tracked.currentTimeline;
                    event.gpuReadyTimeline = tracked.gpuReadyTimeline;
                    event.hiZGraceTimeline = tracked.hiZGraceTimeline;
                    event.graceDelta = graceDelta;
                    event.hiZEnabled = tracked.hiZEnabled;
                    event.hiZActive = tracked.hiZActive;
                    event.frustumPassed = tracked.frustumPassed;
                    event.ready = tracked.ready;
                    event.editUploadSerial = tracked.editUploadSerial;
                    if (hiZOccluded) {
                        event.nearestDepth = occIt->second.nearestDepth;
                        event.pyramidDepth = occIt->second.pyramidDepth;
                        event.mipLevel = occIt->second.mipLevel;
                    }

                    if (m_editVisibilityEvents.size() >= EDIT_VISIBILITY_EVENT_CAPACITY) {
                        m_editVisibilityEvents.erase(m_editVisibilityEvents.begin());
                    }
                    m_editVisibilityEvents.push_back(event);

                    if (transition && prevDrawn && !drawn) {
                        ++m_editVisibilityDropEvents;
                    } else if (transition && !prevDrawn && drawn) {
                        ++m_editVisibilityRecoveryEvents;
                    }
                }

                watch.lastDrawnKnown = true;
                watch.lastDrawn = drawn;
                watch.lastState = state;
            }

            m_editVisibilityLastProcessedDispatchSerial = dispatchSerial;
        } else {
            m_lastTrackedEditChunks.clear();
        }

        m_pendingEditDispatch = PendingEditDispatchContext{};
    }
}

void GPUCullingSystem::recordClearHiZBlinkLog(VkCommandBuffer cmd) {
    if (!m_initialized || !m_hiZBlinkLogBuffer) return;
    // Clear only the header (count + dropped + 2 pads = 16 bytes); event payload is
    // overwritten in-place by the shader and only entries up to count are valid.
    vkCmdFillBuffer(cmd, m_hiZBlinkLogBuffer, 0, sizeof(uint32_t) * 4, 0);
    // Synchronization is performed by recordCulling()'s pre-cull transfer->compute barrier.
}

void GPUCullingSystem::recordHiZBlinkLogReadback(VkCommandBuffer cmd) {
    if (!m_initialized || !m_hiZBlinkLogReadbackBuffer) return;
    // The compute->transfer barrier must be emitted by recordReadbackBarrier().
    VkBufferCopy copyRegion{};
    copyRegion.srcOffset = 0;
    copyRegion.dstOffset = 0;
    copyRegion.size = sizeof(uint32_t) * 4
                    + sizeof(HiZBlinkEvent) * HIZ_BLINK_LOG_GPU_CAPACITY;
    vkCmdCopyBuffer(cmd, m_hiZBlinkLogBuffer, m_hiZBlinkLogReadbackBuffer, 1, &copyRegion);
    m_hiZBlinkLogReadbackPending = true;
}

GPUCullingSystem::HiZBlinkLogSnapshot GPUCullingSystem::getHiZBlinkLog() const {
    HiZBlinkLogSnapshot out;
    std::lock_guard<std::mutex> lock(m_hiZBlinkLogMutex);
    out.totalCaptured = m_hiZBlinkLogTotalCaptured;
    out.totalDroppedGpu = m_hiZBlinkLogTotalDroppedGpu;
    out.totalDroppedCpu = m_hiZBlinkLogTotalDroppedCpu;
    out.lastFrameCount = m_hiZBlinkLogLastFrameCount;
    out.lastFrameDropped = m_hiZBlinkLogLastFrameDropped;

    if (m_hiZBlinkLogRing.empty()) return out;

    if (!m_hiZBlinkLogRingFull) {
        out.events.assign(m_hiZBlinkLogRing.begin(),
                          m_hiZBlinkLogRing.begin() + m_hiZBlinkLogRingHead);
    } else {
        out.events.reserve(HIZ_BLINK_LOG_CPU_CAPACITY);
        out.events.insert(out.events.end(),
                          m_hiZBlinkLogRing.begin() + m_hiZBlinkLogRingHead,
                          m_hiZBlinkLogRing.end());
        out.events.insert(out.events.end(),
                          m_hiZBlinkLogRing.begin(),
                          m_hiZBlinkLogRing.begin() + m_hiZBlinkLogRingHead);
    }
    return out;
}

void GPUCullingSystem::clearHiZBlinkLog() {
    std::lock_guard<std::mutex> lock(m_hiZBlinkLogMutex);
    m_hiZBlinkLogRingHead = 0;
    m_hiZBlinkLogRingFull = false;
    // Note: leaves lifetime counters intact so totals keep growing.
}

void GPUCullingSystem::recordClearDebugStats(VkCommandBuffer cmd) {
    if (!m_initialized || !m_debugStatsBuffer) return;
    
    // Clear debug stats to 0 before culling
    vkCmdFillBuffer(cmd, m_debugStatsBuffer, 0, sizeof(uint32_t) * DEBUG_STATS_COUNT, 0);
    // Synchronization is performed by recordCulling(), which issues a transfer->compute
    // barrier after all pre-cull fills (debug stats, pending invalidations, draw count reset).
}

void GPUCullingSystem::recordDebugStatsReadback(VkCommandBuffer cmd) {
    if (!m_initialized || !m_debugStatsReadbackBuffer) return;

    // The compute->transfer barrier is emitted in recordReadbackBarrier().
    // Copy debug stats to readback buffer
    VkBufferCopy copyRegion{};
    copyRegion.srcOffset = 0;
    copyRegion.dstOffset = 0;
    copyRegion.size = sizeof(uint32_t) * DEBUG_STATS_COUNT;
    
    vkCmdCopyBuffer(cmd, m_debugStatsBuffer, m_debugStatsReadbackBuffer, 1, &copyRegion);

    // Avoid hot shader atomics for counters that already exist as GPU counters.
    VkBufferCopy frustumCountCopy{};
    frustumCountCopy.srcOffset = 0;
    frustumCountCopy.dstOffset = sizeof(uint32_t) * 2;
    frustumCountCopy.size = sizeof(uint32_t);
    vkCmdCopyBuffer(cmd, m_frustumPassedCountBuffer, m_debugStatsReadbackBuffer, 1, &frustumCountCopy);

    VkBufferCopy visibleCountCopy{};
    visibleCountCopy.srcOffset = 0;
    visibleCountCopy.dstOffset = sizeof(uint32_t) * 15;
    visibleCountCopy.size = sizeof(uint32_t);
    vkCmdCopyBuffer(cmd, m_drawCountBuffer, m_debugStatsReadbackBuffer, 1, &visibleCountCopy);
}

GPUCullingSystem::DebugStats GPUCullingSystem::getDebugStats() const {
    DebugStats stats{};

    const uint32_t dispatchChunks = m_lastDispatchChunkCount.load(std::memory_order_relaxed);
    const uint32_t totalThreads =
        (dispatchChunks == 0u) ? 0u
                               : ((dispatchChunks + (FRUSTUM_LOCAL_SIZE_X - 1u)) / FRUSTUM_LOCAL_SIZE_X) * FRUSTUM_LOCAL_SIZE_X;

    // Deterministic counters: avoid per-thread GPU atomics for these values.
    stats.chunksProcessed = dispatchChunks;
    stats.totalThreads = totalThreads;
    stats.boundsCheckFailed = (totalThreads > dispatchChunks) ? (totalThreads - dispatchChunks) : 0u;

    if (m_debugStatsMapped) {
        stats.frustumPassed = m_debugStatsMapped[2];
        stats.zeroSubchunks = m_debugStatsMapped[5];
        stats.notReady = m_debugStatsMapped[6];
        stats.hiZOccluded = m_debugStatsMapped[7];
        stats.hiZNearPlaneFail = m_debugStatsMapped[9];
        stats.pyramidNonZero = m_debugStatsMapped[10];
        stats.pyramidAllZero = m_debugStatsMapped[11];
        stats.degenerateUV = m_debugStatsMapped[12];
        stats.holeRecoveryFail = m_debugStatsMapped[13];
        stats.hiZDepthTestVisible = m_debugStatsMapped[14];
        stats.visibleDraws = m_debugStatsMapped[15];
    }
    const uint32_t unavailable = stats.zeroSubchunks + stats.notReady;
    stats.chunksReady = (dispatchChunks > unavailable) ? (dispatchChunks - unavailable) : 0u;

    const uint32_t hiZEnabled = m_lastDispatchHiZEnabled.load(std::memory_order_relaxed);
    stats.hiZTested = hiZEnabled ? stats.frustumPassed : 0u;

    return stats;
}

GPUCullingSystem::EditVisibilitySnapshot GPUCullingSystem::getEditVisibilitySnapshot() const {
    EditVisibilitySnapshot out;
    std::lock_guard<std::mutex> lock(m_slotMutex);
    out.trackedChunks = m_lastTrackedEditChunks;
    out.events = m_editVisibilityEvents;
    out.totalDropEvents = m_editVisibilityDropEvents;
    out.totalRecoveryEvents = m_editVisibilityRecoveryEvents;
    out.lastDispatchSerial = m_editVisibilityLastProcessedDispatchSerial;
    return out;
}

void GPUCullingSystem::clearEditVisibilitySnapshot() {
    std::lock_guard<std::mutex> lock(m_slotMutex);
    m_lastTrackedEditChunks.clear();
    m_editVisibilityEvents.clear();
    m_editVisibilityDropEvents = 0;
    m_editVisibilityRecoveryEvents = 0;
    m_editVisibilityLastProcessedDispatchSerial = 0;
    for (uint32_t slot : m_editWatchedSlots) {
        if (slot < m_editWatchStates.size()) {
            m_editWatchStates[slot].lastDrawnKnown = false;
            m_editWatchStates[slot].lastDrawn = false;
            m_editWatchStates[slot].lastState = EditVisibilityState::Unknown;
        }
    }
}

void GPUCullingSystem::setGModeGeometryDiffCaptureState(bool active,
                                                        uint64_t toggleSerial,
                                                        bool beforeGpuMode,
                                                        bool afterGpuMode,
                                                        uint32_t framesRemaining,
                                                        bool timedOut) {
    std::lock_guard<std::mutex> lock(m_slotMutex);
    m_gModeGeometryCaptureActive = active;
    m_gModeGeometryCaptureToggleSerial = toggleSerial;
    m_gModeGeometryCaptureBeforeGpu = beforeGpuMode;
    m_gModeGeometryCaptureAfterGpu = afterGpuMode;
    m_gModeGeometryCaptureFramesRemaining = framesRemaining;
    if (active) {
        m_gModeGeometryLastCaptureTimedOut = false;
    } else if (timedOut) {
        m_gModeGeometryLastCaptureTimedOut = true;
    }
}

void GPUCullingSystem::recordGModeGeometryDiff(uint64_t toggleSerial,
                                               bool beforeGpuMode,
                                               bool afterGpuMode,
                                               const std::vector<GModeGeometryDiffRecord>& records,
                                               bool timedOut) {
    std::lock_guard<std::mutex> lock(m_slotMutex);

    m_gModeGeometryCaptureActive = false;
    m_gModeGeometryCaptureToggleSerial = toggleSerial;
    m_gModeGeometryCaptureBeforeGpu = beforeGpuMode;
    m_gModeGeometryCaptureAfterGpu = afterGpuMode;
    m_gModeGeometryCaptureFramesRemaining = 0;
    m_gModeGeometryLastCaptureTimedOut = timedOut;
    m_gModeGeometryLastToggleSerial = toggleSerial;
    m_gModeGeometryLastDiffCount = static_cast<uint32_t>(records.size());

    m_gModeGeometryDiffTotalEvents += static_cast<uint64_t>(records.size());
    for (const GModeGeometryDiffRecord& rec : records) {
        if (m_gModeGeometryDiffEvents.size() >= G_MODE_DIFF_EVENT_CAPACITY) {
            m_gModeGeometryDiffEvents.erase(m_gModeGeometryDiffEvents.begin());
        }

        GModeGeometryDiffEvent ev;
        ev.sequence = ++m_gModeGeometryDiffEventSerial;
        ev.toggleSerial = toggleSerial;
        ev.beforeGpuMode = beforeGpuMode;
        ev.afterGpuMode = afterGpuMode;
        ev.chunkX = rec.chunkX;
        ev.chunkY = rec.chunkY;
        ev.chunkZ = rec.chunkZ;
        ev.visibleBefore = rec.visibleBefore;
        ev.visibleAfter = rec.visibleAfter;
        ev.hasTrackedState = rec.hasTrackedState;
        ev.trackedState = rec.trackedState;
        ev.fromTerrainEdit = rec.fromTerrainEdit;
        ev.replacesExistingMesh = rec.replacesExistingMesh;
        ev.hiZEnabled = rec.hiZEnabled;
        ev.hiZActive = rec.hiZActive;
        ev.frustumPassed = rec.frustumPassed;
        ev.ready = rec.ready;
        ev.currentTimeline = rec.currentTimeline;
        ev.gpuReadyTimeline = rec.gpuReadyTimeline;
        ev.hiZGraceTimeline = rec.hiZGraceTimeline;
        ev.graceDelta = rec.graceDelta;
        ev.nearestDepth = rec.nearestDepth;
        ev.pyramidDepth = rec.pyramidDepth;
        ev.mipLevel = rec.mipLevel;
        ev.editUploadSerial = rec.editUploadSerial;
        m_gModeGeometryDiffEvents.push_back(ev);
    }
}

GPUCullingSystem::GModeGeometryDiffSnapshot GPUCullingSystem::getGModeGeometryDiffSnapshot() const {
    GModeGeometryDiffSnapshot out;
    std::lock_guard<std::mutex> lock(m_slotMutex);
    out.captureActive = m_gModeGeometryCaptureActive;
    out.captureToggleSerial = m_gModeGeometryCaptureToggleSerial;
    out.captureBeforeGpuMode = m_gModeGeometryCaptureBeforeGpu;
    out.captureAfterGpuMode = m_gModeGeometryCaptureAfterGpu;
    out.captureFramesRemaining = m_gModeGeometryCaptureFramesRemaining;
    out.lastCaptureTimedOut = m_gModeGeometryLastCaptureTimedOut;
    out.lastToggleSerial = m_gModeGeometryLastToggleSerial;
    out.lastDiffCount = m_gModeGeometryLastDiffCount;
    out.totalEvents = m_gModeGeometryDiffTotalEvents;
    out.events = m_gModeGeometryDiffEvents;
    return out;
}

void GPUCullingSystem::clearGModeGeometryDiffSnapshot() {
    std::lock_guard<std::mutex> lock(m_slotMutex);
    m_gModeGeometryDiffEvents.clear();
    m_gModeGeometryDiffEventSerial = 0;
    m_gModeGeometryDiffTotalEvents = 0;
    m_gModeGeometryCaptureActive = false;
    m_gModeGeometryCaptureToggleSerial = 0;
    m_gModeGeometryCaptureBeforeGpu = false;
    m_gModeGeometryCaptureAfterGpu = false;
    m_gModeGeometryCaptureFramesRemaining = 0;
    m_gModeGeometryLastCaptureTimedOut = false;
    m_gModeGeometryLastToggleSerial = 0;
    m_gModeGeometryLastDiffCount = 0;
}

const char* GPUCullingSystem::editVisibilityStateName(EditVisibilityState state) {
    switch (state) {
    case EditVisibilityState::VisibleNoHiZ: return "Visible.NoHiZ";
    case EditVisibilityState::VisibleHiZGrace: return "Visible.Grace";
    case EditVisibilityState::VisibleHiZPassed: return "Visible.HiZPass";
    case EditVisibilityState::NotDrawnSlotInactive: return "NotDrawn.SlotInactive";
    case EditVisibilityState::NotDrawnZeroSubChunks: return "NotDrawn.ZeroSubchunks";
    case EditVisibilityState::NotDrawnNoValidDraws: return "NotDrawn.NoValidDraws";
    case EditVisibilityState::NotDrawnNotReady: return "NotDrawn.NotReady";
    case EditVisibilityState::NotDrawnFrustum: return "NotDrawn.Frustum";
    case EditVisibilityState::NotDrawnHiZOccluded: return "NotDrawn.HiZOccluded";
    case EditVisibilityState::Unknown:
    default:
        return "Unknown";
    }
}

bool GPUCullingSystem::editVisibilityStateIsDrawn(EditVisibilityState state) {
    return state == EditVisibilityState::VisibleNoHiZ
        || state == EditVisibilityState::VisibleHiZGrace
        || state == EditVisibilityState::VisibleHiZPassed;
}

const char* GPUCullingSystem::cullingModeName(bool gpuMode) {
    return gpuMode ? "GPU" : "CPU";
}

````

## src\rendering\culling\GPUCullingSystem.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/culling/GPUCullingSystem.h"
#include "rendering/common/VulkanHelpers.h"
#include <stdexcept>
#include <iostream>
#include <fstream>
#include <cstring>
#include <string>
#include <cmath>
#include <algorithm>

// Number of debug stats (must match shader)
constexpr uint32_t DEBUG_STATS_COUNT = 16;
constexpr uint32_t FRUSTUM_LOCAL_SIZE_X = 64;

namespace {

bool isOutsidePlane(const glm::vec4& plane, const glm::vec3& aabbMin, const glm::vec3& aabbMax) {
    const glm::vec3 testPoint(
        plane.x >= 0.0f ? aabbMax.x : aabbMin.x,
        plane.y >= 0.0f ? aabbMax.y : aabbMin.y,
        plane.z >= 0.0f ? aabbMax.z : aabbMin.z);
    return glm::dot(glm::vec3(plane), testPoint) + plane.w < 0.0f;
}

bool aabbPassesFrustumPlanes(const glm::vec4 planes[6], const glm::vec3& aabbMin, const glm::vec3& aabbMax) {
    for (int i = 0; i < 6; ++i) {
        if (isOutsidePlane(planes[i], aabbMin, aabbMax)) {
            return false;
        }
    }
    return true;
}

} // namespace

GPUCullingSystem::~GPUCullingSystem() {
    cleanup();
}

void GPUCullingSystem::init(VkDevice device, VkPhysicalDevice physicalDevice, uint32_t maxChunks,
                            VkBuffer externalOriginsBuffer) {
    if (m_initialized) {
        cleanup();
    }
    
    m_device = device;
    m_physicalDevice = physicalDevice;
    m_maxChunks = maxChunks;
    m_externalOriginsBuffer = externalOriginsBuffer;
    
    // Initialize free list with all slots available
    m_freeSlots.reserve(maxChunks);
    for (uint32_t i = maxChunks; i > 0; --i) {
        m_freeSlots.push_back(i - 1);  // Push in reverse so slot 0 is allocated first
    }
    m_slotOccupied.assign(maxChunks, false);
    m_activeSlots.clear();
    m_activeSlots.reserve(maxChunks);
    m_slotToActiveIndex.assign(maxChunks, UINT32_MAX);
    m_pendingInvalidations.clear();
    m_pendingInvalidations.reserve(maxChunks);
    m_pendingMaskWordClears.clear();
    m_pendingMaskWordClears.reserve((maxChunks + 31u) / 32u);
    m_slotMaterialOverlayHints.assign(maxChunks, 0u);
    m_pendingMaterialOverlayHintUpdates.clear();
    m_pendingMaterialOverlayHintUpdates.reserve(1024);
    m_activeIndicesDirty = true;
    m_drawCountReadbackPending = false;

    m_editWatchStates.assign(maxChunks, EditWatchSlotState{});
    m_editWatchedSlots.clear();
    m_pendingEditDispatch = PendingEditDispatchContext{};
    m_lastTrackedEditChunks.clear();
    m_editVisibilityEvents.clear();
    m_editVisibilityDispatchSerial = 0;
    m_editVisibilityLastProcessedDispatchSerial = 0;
    m_editVisibilityEventSerial = 0;
    m_editVisibilityUploadSerial = 0;
    m_editVisibilityDropEvents = 0;
    m_editVisibilityRecoveryEvents = 0;
    m_gModeGeometryDiffEvents.clear();
    m_gModeGeometryDiffEventSerial = 0;
    m_gModeGeometryDiffTotalEvents = 0;
    m_gModeGeometryCaptureActive = false;
    m_gModeGeometryCaptureToggleSerial = 0;
    m_gModeGeometryCaptureBeforeGpu = false;
    m_gModeGeometryCaptureAfterGpu = false;
    m_gModeGeometryCaptureFramesRemaining = 0;
    m_gModeGeometryLastCaptureTimedOut = false;
    m_gModeGeometryLastToggleSerial = 0;
    m_gModeGeometryLastDiffCount = 0;
    
    createBuffers();
    createFrustumCullPipeline();
    createFrustumDescriptorSets();
    
    m_initialized = true;
    
    std::cout << "[GPUCullingSystem] Initialized with capacity for " << maxChunks << " chunks" << std::endl;
}

void GPUCullingSystem::cleanup() {
    if (!m_initialized) return;
    
    vkDeviceWaitIdle(m_device);
    
    // Destroy frustum culling pipelines
    if (m_frustumFilterPipeline) vkDestroyPipeline(m_device, m_frustumFilterPipeline, nullptr);
    if (m_frustumPipeline) vkDestroyPipeline(m_device, m_frustumPipeline, nullptr);
    if (m_frustumDispatchPipeline) vkDestroyPipeline(m_device, m_frustumDispatchPipeline, nullptr);
    if (m_frustumPipelineLayout) vkDestroyPipelineLayout(m_device, m_frustumPipelineLayout, nullptr);
    if (m_frustumFilterShader) vkDestroyShaderModule(m_device, m_frustumFilterShader, nullptr);
    if (m_frustumCullShader) vkDestroyShaderModule(m_device, m_frustumCullShader, nullptr);
    if (m_frustumDispatchShader) vkDestroyShaderModule(m_device, m_frustumDispatchShader, nullptr);
    m_frustumFilterPipeline = VK_NULL_HANDLE;
    m_frustumPipeline = VK_NULL_HANDLE;
    m_frustumDispatchPipeline = VK_NULL_HANDLE;
    m_frustumPipelineLayout = VK_NULL_HANDLE;
    m_frustumFilterShader = VK_NULL_HANDLE;
    m_frustumCullShader = VK_NULL_HANDLE;
    m_frustumDispatchShader = VK_NULL_HANDLE;
    
    // Destroy descriptors
    if (m_descriptorPool) vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
    if (m_frustumDescriptorSetLayout) vkDestroyDescriptorSetLayout(m_device, m_frustumDescriptorSetLayout, nullptr);
    m_descriptorPool = VK_NULL_HANDLE;
    m_frustumDescriptorSetLayout = VK_NULL_HANDLE;
    m_frustumDescriptorSet = VK_NULL_HANDLE;
    
    // Destroy buffers
    if (m_allDrawsBuffer) vkDestroyBuffer(m_device, m_allDrawsBuffer, nullptr);
    if (m_allDrawsMemory) vkFreeMemory(m_device, m_allDrawsMemory, nullptr);
    
    if (m_visibleDrawsBuffer) vkDestroyBuffer(m_device, m_visibleDrawsBuffer, nullptr);
    if (m_visibleDrawsMemory) vkFreeMemory(m_device, m_visibleDrawsMemory, nullptr);
    
    if (m_drawCountBuffer) vkDestroyBuffer(m_device, m_drawCountBuffer, nullptr);
    if (m_drawCountMemory) vkFreeMemory(m_device, m_drawCountMemory, nullptr);

    if (m_frustumPassedIndicesBuffer) vkDestroyBuffer(m_device, m_frustumPassedIndicesBuffer, nullptr);
    if (m_frustumPassedIndicesMemory) vkFreeMemory(m_device, m_frustumPassedIndicesMemory, nullptr);
    if (m_frustumPassedCountBuffer) vkDestroyBuffer(m_device, m_frustumPassedCountBuffer, nullptr);
    if (m_frustumPassedCountMemory) vkFreeMemory(m_device, m_frustumPassedCountMemory, nullptr);
    if (m_frustumDispatchArgsBuffer) vkDestroyBuffer(m_device, m_frustumDispatchArgsBuffer, nullptr);
    if (m_frustumDispatchArgsMemory) vkFreeMemory(m_device, m_frustumDispatchArgsMemory, nullptr);
    if (m_prevVisibleMaskBuffer) vkDestroyBuffer(m_device, m_prevVisibleMaskBuffer, nullptr);
    if (m_prevVisibleMaskMemory) vkFreeMemory(m_device, m_prevVisibleMaskMemory, nullptr);
    m_frustumPassedIndicesBuffer = VK_NULL_HANDLE;
    m_frustumPassedIndicesMemory = VK_NULL_HANDLE;
    m_frustumPassedCountBuffer = VK_NULL_HANDLE;
    m_frustumPassedCountMemory = VK_NULL_HANDLE;
    m_frustumDispatchArgsBuffer = VK_NULL_HANDLE;
    m_frustumDispatchArgsMemory = VK_NULL_HANDLE;
    m_prevVisibleMaskBuffer = VK_NULL_HANDLE;
    m_prevVisibleMaskMemory = VK_NULL_HANDLE;
    m_prevVisibleMaskSize = 0;
    m_temporalFrameCounter = 0;
    
    // Only destroy visible origins buffer if we own it (not external)
    if (!m_usingExternalOriginsBuffer) {
        if (m_visibleOriginsBuffer) vkDestroyBuffer(m_device, m_visibleOriginsBuffer, nullptr);
        if (m_visibleOriginsMemory) vkFreeMemory(m_device, m_visibleOriginsMemory, nullptr);
    }
    m_visibleOriginsBuffer = VK_NULL_HANDLE;
    m_visibleOriginsMemory = VK_NULL_HANDLE;

    // Destroy active-index buffers
    if (m_activeIndicesStagingMapped) {
        vkUnmapMemory(m_device, m_activeIndicesStagingMemory);
        m_activeIndicesStagingMapped = nullptr;
    }
    if (m_activeIndicesStagingBuffer) vkDestroyBuffer(m_device, m_activeIndicesStagingBuffer, nullptr);
    if (m_activeIndicesStagingMemory) vkFreeMemory(m_device, m_activeIndicesStagingMemory, nullptr);
    if (m_activeIndicesBuffer) vkDestroyBuffer(m_device, m_activeIndicesBuffer, nullptr);
    if (m_activeIndicesMemory) vkFreeMemory(m_device, m_activeIndicesMemory, nullptr);
    m_activeIndicesStagingBuffer = VK_NULL_HANDLE;
    m_activeIndicesStagingMemory = VK_NULL_HANDLE;
    m_activeIndicesBuffer = VK_NULL_HANDLE;
    m_activeIndicesMemory = VK_NULL_HANDLE;
    
    // Destroy readback buffer
    if (m_readbackMapped) {
        vkUnmapMemory(m_device, m_readbackMemory);
        m_readbackMapped = nullptr;
    }
    if (m_readbackBuffer) vkDestroyBuffer(m_device, m_readbackBuffer, nullptr);
    if (m_readbackMemory) vkFreeMemory(m_device, m_readbackMemory, nullptr);
    m_readbackBuffer = VK_NULL_HANDLE;
    m_readbackMemory = VK_NULL_HANDLE;
    
    // Destroy debug stats buffers
    if (m_debugStatsBuffer) vkDestroyBuffer(m_device, m_debugStatsBuffer, nullptr);
    if (m_debugStatsMemory) vkFreeMemory(m_device, m_debugStatsMemory, nullptr);
    m_debugStatsBuffer = VK_NULL_HANDLE;
    m_debugStatsMemory = VK_NULL_HANDLE;
    
    if (m_debugStatsMapped) {
        vkUnmapMemory(m_device, m_debugStatsReadbackMemory);
        m_debugStatsMapped = nullptr;
    }
    if (m_debugStatsReadbackBuffer) vkDestroyBuffer(m_device, m_debugStatsReadbackBuffer, nullptr);
    if (m_debugStatsReadbackMemory) vkFreeMemory(m_device, m_debugStatsReadbackMemory, nullptr);
    m_debugStatsReadbackBuffer = VK_NULL_HANDLE;
    m_debugStatsReadbackMemory = VK_NULL_HANDLE;

    // Hi-Z blink log
    if (m_hiZBlinkLogMapped) {
        vkUnmapMemory(m_device, m_hiZBlinkLogReadbackMemory);
        m_hiZBlinkLogMapped = nullptr;
    }
    if (m_hiZBlinkLogReadbackBuffer) vkDestroyBuffer(m_device, m_hiZBlinkLogReadbackBuffer, nullptr);
    if (m_hiZBlinkLogReadbackMemory) vkFreeMemory(m_device, m_hiZBlinkLogReadbackMemory, nullptr);
    if (m_hiZBlinkLogBuffer) vkDestroyBuffer(m_device, m_hiZBlinkLogBuffer, nullptr);
    if (m_hiZBlinkLogMemory) vkFreeMemory(m_device, m_hiZBlinkLogMemory, nullptr);
    m_hiZBlinkLogReadbackBuffer = VK_NULL_HANDLE;
    m_hiZBlinkLogReadbackMemory = VK_NULL_HANDLE;
    m_hiZBlinkLogBuffer = VK_NULL_HANDLE;
    m_hiZBlinkLogMemory = VK_NULL_HANDLE;
    {
        std::lock_guard<std::mutex> lock(m_hiZBlinkLogMutex);
        m_hiZBlinkLogRing.clear();
        m_hiZBlinkLogRingHead = 0;
        m_hiZBlinkLogRingFull = false;
        m_hiZBlinkLogTotalCaptured = 0;
        m_hiZBlinkLogTotalDroppedGpu = 0;
        m_hiZBlinkLogTotalDroppedCpu = 0;
        m_hiZBlinkLogLastFrameCount = 0;
        m_hiZBlinkLogLastFrameDropped = 0;
    }
    
    
    m_initialized = false;
    m_freeSlots.clear();
    m_slotOccupied.clear();
    m_activeSlots.clear();
    m_slotToActiveIndex.clear();
    m_activeIndicesDirty = false;
    m_pendingInvalidations.clear();
    m_pendingMaskWordClears.clear();
    m_activeSlotCount = 0;
    m_highWaterMark = 0;
    m_drawCountReadbackPending = false;
    m_editWatchStates.clear();
    m_editWatchedSlots.clear();
    m_pendingEditDispatch = PendingEditDispatchContext{};
    m_lastTrackedEditChunks.clear();
    m_editVisibilityEvents.clear();
    m_editVisibilityDispatchSerial = 0;
    m_editVisibilityLastProcessedDispatchSerial = 0;
    m_editVisibilityEventSerial = 0;
    m_editVisibilityUploadSerial = 0;
    m_editVisibilityDropEvents = 0;
    m_editVisibilityRecoveryEvents = 0;
    m_gModeGeometryDiffEvents.clear();
    m_gModeGeometryDiffEventSerial = 0;
    m_gModeGeometryDiffTotalEvents = 0;
    m_gModeGeometryCaptureActive = false;
    m_gModeGeometryCaptureToggleSerial = 0;
    m_gModeGeometryCaptureBeforeGpu = false;
    m_gModeGeometryCaptureAfterGpu = false;
    m_gModeGeometryCaptureFramesRemaining = 0;
    m_gModeGeometryLastCaptureTimedOut = false;
    m_gModeGeometryLastToggleSerial = 0;
    m_gModeGeometryLastDiffCount = 0;
}

void GPUCullingSystem::reloadShaders() {
    if (!m_initialized) {
        return;
    }

    if (m_frustumFilterPipeline) vkDestroyPipeline(m_device, m_frustumFilterPipeline, nullptr);
    if (m_frustumPipeline) vkDestroyPipeline(m_device, m_frustumPipeline, nullptr);
    if (m_frustumDispatchPipeline) vkDestroyPipeline(m_device, m_frustumDispatchPipeline, nullptr);
    if (m_frustumPipelineLayout) vkDestroyPipelineLayout(m_device, m_frustumPipelineLayout, nullptr);
    if (m_frustumFilterShader) vkDestroyShaderModule(m_device, m_frustumFilterShader, nullptr);
    if (m_frustumCullShader) vkDestroyShaderModule(m_device, m_frustumCullShader, nullptr);
    if (m_frustumDispatchShader) vkDestroyShaderModule(m_device, m_frustumDispatchShader, nullptr);
    if (m_descriptorPool) vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
    if (m_frustumDescriptorSetLayout) vkDestroyDescriptorSetLayout(m_device, m_frustumDescriptorSetLayout, nullptr);

    m_frustumFilterPipeline = VK_NULL_HANDLE;
    m_frustumPipeline = VK_NULL_HANDLE;
    m_frustumDispatchPipeline = VK_NULL_HANDLE;
    m_frustumPipelineLayout = VK_NULL_HANDLE;
    m_frustumFilterShader = VK_NULL_HANDLE;
    m_frustumCullShader = VK_NULL_HANDLE;
    m_frustumDispatchShader = VK_NULL_HANDLE;
    m_descriptorPool = VK_NULL_HANDLE;
    m_frustumDescriptorSetLayout = VK_NULL_HANDLE;
    m_frustumDescriptorSet = VK_NULL_HANDLE;
    m_hiZBound = false;

    createFrustumCullPipeline();
    createFrustumDescriptorSets();

    std::cout << "[GPUCullingSystem] Reloaded culling compute shaders" << std::endl;
}

void GPUCullingSystem::createFrustumCullPipeline() {
    auto loadSpv = [&](const std::vector<std::string>& paths, const char* shaderLabel) -> std::vector<char> {
        std::vector<char> code;
        for (const auto& path : paths) {
            std::ifstream file(path, std::ios::ate | std::ios::binary);
            if (!file.is_open()) continue;

            size_t fileSize = static_cast<size_t>(file.tellg());
            code.resize(fileSize);
            file.seekg(0);
            file.read(code.data(), fileSize);
            file.close();

            std::cout << "[GPUCullingSystem] Loaded " << shaderLabel << " shader from: " << path << std::endl;
            return code;
        }
        throw std::runtime_error(std::string("Failed to load ") + shaderLabel + " shader!");
    };

    const std::vector<std::string> filterShaderPaths = {
        "shaders/culling/frustum_filter.comp.spv",
        "frustum_filter.comp.spv",
        "../../../shaders/culling/frustum_filter.comp.spv"
    };
    const std::vector<std::string> emitShaderPaths = {
        "shaders/culling/frustum_cull.comp.spv",
        "frustum_cull.comp.spv",
        "../../../shaders/culling/frustum_cull.comp.spv",
        "shaders/terrain/frustum_cull.comp.spv"  // Legacy fallback
    };
    const std::vector<std::string> dispatchShaderPaths = {
        "shaders/culling/frustum_dispatch.comp.spv",
        "frustum_dispatch.comp.spv",
        "../../../shaders/culling/frustum_dispatch.comp.spv"
    };

    const std::vector<char> filterCode = loadSpv(filterShaderPaths, "frustum_filter.comp.spv");
    const std::vector<char> emitCode = loadSpv(emitShaderPaths, "frustum_cull.comp.spv");
    const std::vector<char> dispatchCode = loadSpv(dispatchShaderPaths, "frustum_dispatch.comp.spv");

    VkShaderModuleCreateInfo shaderInfo{};
    shaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;

    shaderInfo.codeSize = filterCode.size();
    shaderInfo.pCode = reinterpret_cast<const uint32_t*>(filterCode.data());
    if (vkCreateShaderModule(m_device, &shaderInfo, nullptr, &m_frustumFilterShader) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum filter shader module!");
    }

    shaderInfo.codeSize = emitCode.size();
    shaderInfo.pCode = reinterpret_cast<const uint32_t*>(emitCode.data());
    if (vkCreateShaderModule(m_device, &shaderInfo, nullptr, &m_frustumCullShader) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum emit shader module!");
    }

    shaderInfo.codeSize = dispatchCode.size();
    shaderInfo.pCode = reinterpret_cast<const uint32_t*>(dispatchCode.data());
    if (vkCreateShaderModule(m_device, &shaderInfo, nullptr, &m_frustumDispatchShader) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum dispatch shader module!");
    }

    // Create descriptor set layout:
    // 0 allDraws, 1 visibleDraws, 2 drawCount, 3 visibleOrigins, 4 debugStats,
    // 5 hiZ sampler, 6 activeIndices, 7 frustumPassedIndices, 8 frustumPassedCount,
    // 9 hiZBlinkLog, 10 frustumDispatchArgs, 11 prevVisibleMask (Phase A)
    std::array<VkDescriptorSetLayoutBinding, 12> bindings{};

    for (uint32_t b : {0u, 1u, 2u, 3u, 4u, 6u, 7u, 8u, 9u, 10u, 11u}) {
        bindings[b].binding = b;
        bindings[b].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        bindings[b].descriptorCount = 1;
        bindings[b].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    }

    bindings[5].binding = 5;
    bindings[5].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindings[5].descriptorCount = 1;
    bindings[5].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;

    VkDescriptorSetLayoutCreateInfo layoutInfo{};
    layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    layoutInfo.bindingCount = static_cast<uint32_t>(bindings.size());
    layoutInfo.pBindings = bindings.data();
    if (vkCreateDescriptorSetLayout(m_device, &layoutInfo, nullptr, &m_frustumDescriptorSetLayout) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum culling descriptor set layout!");
    }

    // Push constant range
    VkPushConstantRange pushRange{};
    pushRange.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    pushRange.offset = 0;
    pushRange.size = sizeof(CullPushConstants);

    // Create shared pipeline layout
    VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
    pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pipelineLayoutInfo.setLayoutCount = 1;
    pipelineLayoutInfo.pSetLayouts = &m_frustumDescriptorSetLayout;
    pipelineLayoutInfo.pushConstantRangeCount = 1;
    pipelineLayoutInfo.pPushConstantRanges = &pushRange;
    if (vkCreatePipelineLayout(m_device, &pipelineLayoutInfo, nullptr, &m_frustumPipelineLayout) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum culling pipeline layout!");
    }

    VkComputePipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
    pipelineInfo.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    pipelineInfo.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
    pipelineInfo.stage.pName = "main";
    pipelineInfo.layout = m_frustumPipelineLayout;

    // Stage 1: frustum filter
    pipelineInfo.stage.module = m_frustumFilterShader;
    if (vkCreateComputePipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &m_frustumFilterPipeline) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum filter compute pipeline!");
    }

    // Stage 2: occlusion + draw emission
    pipelineInfo.stage.module = m_frustumCullShader;
    if (vkCreateComputePipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &m_frustumPipeline) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum emit compute pipeline!");
    }

    // Stage 3: prepare vkCmdDispatchIndirect args from frustumPassedCount
    pipelineInfo.stage.module = m_frustumDispatchShader;
    if (vkCreateComputePipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &m_frustumDispatchPipeline) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create frustum dispatch-args compute pipeline!");
    }

    VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_PIPELINE, (uint64_t)m_frustumFilterPipeline, "GPUCull_FrustumFilterPipeline");
    VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_PIPELINE, (uint64_t)m_frustumPipeline, "GPUCull_FrustumEmitPipeline");
    VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_PIPELINE, (uint64_t)m_frustumDispatchPipeline, "GPUCull_FrustumDispatchPipeline");
}

void GPUCullingSystem::createFrustumDescriptorSets() {
    // Create descriptor pool (11 storage buffers + 1 combined image sampler)
    std::array<VkDescriptorPoolSize, 2> poolSizes{};
    poolSizes[0].type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    poolSizes[0].descriptorCount = 11;  // 11 storage buffers (incl. dispatch args + prevVisibleMask)
    poolSizes[1].type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    poolSizes[1].descriptorCount = 1;  // 1 Hi-Z pyramid sampler
    
    VkDescriptorPoolCreateInfo poolInfo{};
    poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    poolInfo.poolSizeCount = static_cast<uint32_t>(poolSizes.size());
    poolInfo.pPoolSizes = poolSizes.data();
    poolInfo.maxSets = 1;  // Frustum set only
    
    if (vkCreateDescriptorPool(m_device, &poolInfo, nullptr, &m_descriptorPool) != VK_SUCCESS) {
        throw std::runtime_error("Failed to create culling descriptor pool!");
    }
    
    // Allocate frustum descriptor set
    VkDescriptorSetAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    allocInfo.descriptorPool = m_descriptorPool;
    allocInfo.descriptorSetCount = 1;
    allocInfo.pSetLayouts = &m_frustumDescriptorSetLayout;
    
    if (vkAllocateDescriptorSets(m_device, &allocInfo, &m_frustumDescriptorSet) != VK_SUCCESS) {
        throw std::runtime_error("Failed to allocate frustum culling descriptor set!");
    }
    
    // Update frustum descriptor set with buffer bindings
    std::array<VkDescriptorBufferInfo, 11> bufferInfos{};
    
    bufferInfos[0].buffer = m_allDrawsBuffer;
    bufferInfos[0].offset = 0;
    bufferInfos[0].range = sizeof(ChunkDrawData) * m_maxChunks;
    
    bufferInfos[1].buffer = m_visibleDrawsBuffer;
    bufferInfos[1].offset = 0;
    bufferInfos[1].range = sizeof(VkDrawIndexedIndirectCommand) * m_maxChunks * GPU_MAX_SUBCHUNKS;
    
    bufferInfos[2].buffer = m_drawCountBuffer;
    bufferInfos[2].offset = 0;
    bufferInfos[2].range = sizeof(uint32_t);
    
    bufferInfos[3].buffer = m_visibleOriginsBuffer;
    bufferInfos[3].offset = 0;
    bufferInfos[3].range = VK_WHOLE_SIZE;
    
    bufferInfos[4].buffer = m_debugStatsBuffer;
    bufferInfos[4].offset = 0;
    bufferInfos[4].range = sizeof(uint32_t) * DEBUG_STATS_COUNT;

    bufferInfos[5].buffer = m_activeIndicesBuffer;
    bufferInfos[5].offset = 0;
    bufferInfos[5].range = sizeof(uint32_t) * m_maxChunks;

    bufferInfos[6].buffer = m_frustumPassedIndicesBuffer;
    bufferInfos[6].offset = 0;
    bufferInfos[6].range = sizeof(uint32_t) * m_maxChunks;

    bufferInfos[7].buffer = m_frustumPassedCountBuffer;
    bufferInfos[7].offset = 0;
    bufferInfos[7].range = sizeof(uint32_t);

    bufferInfos[8].buffer = m_hiZBlinkLogBuffer;
    bufferInfos[8].offset = 0;
    bufferInfos[8].range = VK_WHOLE_SIZE;

    bufferInfos[9].buffer = m_frustumDispatchArgsBuffer;
    bufferInfos[9].offset = 0;
    bufferInfos[9].range = sizeof(VkDispatchIndirectCommand);

    bufferInfos[10].buffer = m_prevVisibleMaskBuffer;
    bufferInfos[10].offset = 0;
    bufferInfos[10].range = m_prevVisibleMaskSize;

    std::array<VkWriteDescriptorSet, 11> writes{};
    const uint32_t storageBindings[11] = {0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11};
    for (uint32_t i = 0; i < 11; ++i) {
        writes[i].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[i].dstSet = m_frustumDescriptorSet;
        writes[i].dstBinding = storageBindings[i];
        writes[i].dstArrayElement = 0;
        writes[i].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        writes[i].descriptorCount = 1;
        writes[i].pBufferInfo = &bufferInfos[i];
    }
    
    vkUpdateDescriptorSets(m_device, static_cast<uint32_t>(writes.size()), writes.data(), 0, nullptr);
}

void GPUCullingSystem::extractFrustumPlanes(const glm::mat4& vp, glm::vec4 outPlanes[6]) {
    // Extract frustum planes from view-projection matrix
    // GLM is column-major, so vp[col][row]
    
    // Left:   row3 + row0
    outPlanes[0] = glm::vec4(vp[0][3] + vp[0][0], vp[1][3] + vp[1][0], vp[2][3] + vp[2][0], vp[3][3] + vp[3][0]);
    // Right:  row3 - row0
    outPlanes[1] = glm::vec4(vp[0][3] - vp[0][0], vp[1][3] - vp[1][0], vp[2][3] - vp[2][0], vp[3][3] - vp[3][0]);
    // Bottom: row3 + row1
    outPlanes[2] = glm::vec4(vp[0][3] + vp[0][1], vp[1][3] + vp[1][1], vp[2][3] + vp[2][1], vp[3][3] + vp[3][1]);
    // Top:    row3 - row1
    outPlanes[3] = glm::vec4(vp[0][3] - vp[0][1], vp[1][3] - vp[1][1], vp[2][3] - vp[2][1], vp[3][3] - vp[3][1]);
    // Near:   row3 + row2
    outPlanes[4] = glm::vec4(vp[0][3] + vp[0][2], vp[1][3] + vp[1][2], vp[2][3] + vp[2][2], vp[3][3] + vp[3][2]);
    // Far:    row3 - row2
    outPlanes[5] = glm::vec4(vp[0][3] - vp[0][2], vp[1][3] - vp[1][2], vp[2][3] - vp[2][2], vp[3][3] - vp[3][2]);
    
    // Normalize planes
    for (int i = 0; i < 6; ++i) {
        float len = glm::length(glm::vec3(outPlanes[i]));
        if (len > 1e-6f) {
            outPlanes[i] /= len;
        }
    }
}

void GPUCullingSystem::recordCulling(VkCommandBuffer cmd, const glm::mat4& viewProj,
                                      uint64_t currentTimeline, uint32_t chunkCount,
                                      uint32_t pyramidWidth, uint32_t pyramidHeight,
                                      uint32_t pyramidMips,
                                      const glm::mat4& prevViewProj,
                                      float viewportOffsetX, float viewportOffsetY,
                                      float viewportScaleX, float viewportScaleY,
                                      bool debugEnabled,
                                      bool disableTemporalCoherenceForFrame,
                                      bool hiZBlinkLogEnabled) {
    if (!m_initialized) {
        m_lastDispatchChunkCount.store(0, std::memory_order_relaxed);
        m_lastDispatchHiZEnabled.store(0, std::memory_order_relaxed);
        return;
    }

    std::vector<uint32_t> pendingInvalidations;
    std::vector<uint32_t> pendingMaskWordClears;
    std::vector<std::pair<uint32_t, uint32_t>> pendingMaterialOverlayHintUpdates;
    uint32_t activeCount = 0;
    bool uploadActiveIndices = false;
    {
        std::lock_guard<std::mutex> lock(m_slotMutex);

        activeCount = static_cast<uint32_t>(m_activeSlots.size());
        pendingInvalidations.reserve(m_pendingInvalidations.size());
        for (uint32_t slotIdx : m_pendingInvalidations) pendingInvalidations.push_back(slotIdx);
        m_pendingInvalidations.clear();

        pendingMaskWordClears.reserve(m_pendingMaskWordClears.size());
        for (uint32_t wordIdx : m_pendingMaskWordClears) pendingMaskWordClears.push_back(wordIdx);
        m_pendingMaskWordClears.clear();

        pendingMaterialOverlayHintUpdates.reserve(m_pendingMaterialOverlayHintUpdates.size());
        for (const auto& [slotIdx, hint] : m_pendingMaterialOverlayHintUpdates) {
            pendingMaterialOverlayHintUpdates.emplace_back(slotIdx, hint);
        }
        m_pendingMaterialOverlayHintUpdates.clear();

        if (m_activeIndicesDirty) {
            if (activeCount > 0 && m_activeIndicesStagingMapped) {
                memcpy(m_activeIndicesStagingMapped, m_activeSlots.data(), sizeof(uint32_t) * activeCount);
            }
            m_activeIndicesDirty = false;
            uploadActiveIndices = true;
        }
    }

    // Use provided chunk count or default to the full active count.
    // Clamp to active count to avoid reading beyond the compact index list.
    uint32_t totalChunks = (chunkCount > 0) ? std::min(chunkCount, activeCount) : activeCount;

    const uint32_t hiZEnabled = (m_hiZBound && pyramidWidth > 0 && pyramidMips > 0) ? 1u : 0u;
    m_lastDispatchChunkCount.store(totalChunks, std::memory_order_relaxed);
    m_lastDispatchHiZEnabled.store(hiZEnabled, std::memory_order_relaxed);

    // Process pending slot invalidations: zero subChunkCount in GPU buffer
    // for freed slots so the shader skips them instead of reading stale data.
    for (uint32_t slotIdx : pendingInvalidations) {
        VkDeviceSize offset = slotIdx * sizeof(ChunkDrawData) + offsetof(ChunkDrawData, subChunkCount);
        vkCmdFillBuffer(cmd, m_allDrawsBuffer, offset, sizeof(uint32_t), 0);
    }

    // Process pending material-overlay hint updates. This writes ChunkDrawData._pad1
    // before the culling shader emits visibleOrigins.w. Reuses the transfer->compute
    // barrier below, so no extra synchronization is needed.
    for (const auto& [slotIdx, hint] : pendingMaterialOverlayHintUpdates) {
        if (slotIdx >= m_maxChunks) continue;
        VkDeviceSize offset = slotIdx * sizeof(ChunkDrawData) + offsetof(ChunkDrawData, _pad1);
        vkCmdFillBuffer(cmd, m_allDrawsBuffer, offset, sizeof(uint32_t), hint != 0u ? 1u : 0u);
    }

    // Phase C \u2014 explicit visibility-mask invalidations.
    // For each enqueued mask word (slot/32), zero the entire 32-bit word in the
    // prev-visible mask. Worst case this re-tests up to 31 unrelated chunks via Hi-Z
    // next frame (correct behavior, never wrong rendering). Cheap: one fill per unique
    // word, deduplicated by the unordered_set, and merged into the existing
    // transfer-\u003Ecompute barrier below.
    if (m_prevVisibleMaskBuffer != VK_NULL_HANDLE && m_prevVisibleMaskSize > 0 &&
        !pendingMaskWordClears.empty()) {
        const VkDeviceSize maskWords = m_prevVisibleMaskSize / sizeof(uint32_t);
        for (uint32_t wordIdx : pendingMaskWordClears) {
            if (wordIdx >= maskWords) continue;
            VkDeviceSize offset = static_cast<VkDeviceSize>(wordIdx) * sizeof(uint32_t);
            vkCmdFillBuffer(cmd, m_prevVisibleMaskBuffer, offset, sizeof(uint32_t), 0);
        }
    }

    // Phase A — periodic temporal visibility mask reset.
    // Wipe the mask every Nth cull so chunks that became occluded eventually get
    // re-tested by Hi-Z (otherwise a temporally-skipped chunk could remain marked
    // visible forever if nothing forced a re-evaluation). The reset is a single
    // small device-local fill; cost is negligible relative to the Hi-Z work it saves.
    const bool temporalEnabled =
        m_temporalCoherenceEnabled.load(std::memory_order_relaxed) &&
        !disableTemporalCoherenceForFrame;
    bool didResetMask = false;
    if (m_prevVisibleMaskBuffer != VK_NULL_HANDLE && m_prevVisibleMaskSize > 0) {
        const uint32_t interval = std::max<uint32_t>(1u, m_temporalRevalidateInterval);
        // Always clear on the first frame (counter == 0) so we start from a known state
        // (memory contents are undefined right after allocation).
        const bool firstFrame = (m_temporalFrameCounter == 0);
        if (!temporalEnabled || firstFrame || (m_temporalFrameCounter % interval) == 0) {
            vkCmdFillBuffer(cmd, m_prevVisibleMaskBuffer, 0, m_prevVisibleMaskSize, 0);
            didResetMask = true;
        }
        ++m_temporalFrameCounter;
    }

    if (uploadActiveIndices && activeCount > 0) {
        VkBufferCopy copyRegion{};
        copyRegion.srcOffset = 0;
        copyRegion.dstOffset = 0;
        copyRegion.size = sizeof(uint32_t) * activeCount;
        vkCmdCopyBuffer(cmd, m_activeIndicesStagingBuffer, m_activeIndicesBuffer, 1, &copyRegion);
    }
    
    // If we invalidated any slots, barrier before compute reads the buffer
    // (The fill-then-barrier-then-dispatch chain ensures zeroed data is visible)
    
    // Reset stage counters to 0
    vkCmdFillBuffer(cmd, m_frustumPassedCountBuffer, 0, sizeof(uint32_t), 0);
    vkCmdFillBuffer(cmd, m_drawCountBuffer, 0, sizeof(uint32_t), 0);
    
    // Barrier: ensure fill is complete before compute reads/writes
    VkMemoryBarrier2 memBarrier{};
    memBarrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
    memBarrier.srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
    memBarrier.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
    memBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    memBarrier.dstAccessMask = VK_ACCESS_2_SHADER_READ_BIT | VK_ACCESS_2_SHADER_WRITE_BIT;
    
    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.memoryBarrierCount = 1;
    depInfo.pMemoryBarriers = &memBarrier;
    
    vkCmdPipelineBarrier2(cmd, &depInfo);
    
    // Set push constants with current/previous projection + Hi-Z data.
    CullPushConstants pushData{};
    pushData.viewProj = viewProj;
    pushData.totalDraws = totalChunks;
    pushData.currentTimeline = static_cast<uint32_t>(currentTimeline);
    pushData.hiZEnabled = hiZEnabled;
    pushData.debugEnabled = debugEnabled ? 1u : 0u;
    pushData.hiZPyramidInfo = glm::vec4(
        static_cast<float>(pyramidWidth),
        static_cast<float>(pyramidHeight),
        static_cast<float>(pyramidMips),
        0.0f);
    pushData.viewportUvTransform = glm::vec4(
        viewportOffsetX,
        viewportOffsetY,
        viewportScaleX,
        viewportScaleY);
    // Previous frame's VP for Hi-Z projection (pyramid was built from prev frame's depth)
    // If no previous VP available (first frame), fall back to current VP
    pushData.prevViewProj = (prevViewProj[0][0] == 0.0f && prevViewProj[1][1] == 0.0f) 
                            ? viewProj : prevViewProj;

    // Phase A — temporal coherence enable (effective only when not on a reset frame).
    // y marks motion/edit frames for diagnostics/future tuning; motion safety comes
    // from disabling the visible-bit skip while keeping Hi-Z in previous-frame space.
    pushData.temporalInfo = glm::uvec4(
        (temporalEnabled && !didResetMask) ? 1u : 0u,
        disableTemporalCoherenceForFrame ? 1u : 0u,
        hiZBlinkLogEnabled ? 1u : 0u,
        0u);

    if (hiZBlinkLogEnabled) {
        glm::vec4 frustumPlanes[6];
        extractFrustumPlanes(viewProj, frustumPlanes);

        std::lock_guard<std::mutex> lock(m_slotMutex);

        PendingEditDispatchContext dispatchCtx;
        dispatchCtx.dispatchSerial = ++m_editVisibilityDispatchSerial;
        dispatchCtx.tracked.reserve(m_editWatchedSlots.size());

        size_t writeIdx = 0;
        const uint32_t timelineU32 = static_cast<uint32_t>(currentTimeline);
        for (size_t i = 0; i < m_editWatchedSlots.size(); ++i) {
            const uint32_t slot = m_editWatchedSlots[i];
            if (slot >= m_editWatchStates.size()) {
                continue;
            }

            EditWatchSlotState& watch = m_editWatchStates[slot];
            if (!watch.hasMetadata) {
                continue;
            }

            const bool slotOccupied = (slot < m_slotOccupied.size()) ? m_slotOccupied[slot] : false;
            if (watch.watchFramesRemaining == 0u && !slotOccupied) {
                continue;
            }

            PendingTrackedEditChunk tracked;
            tracked.slot = slot;
            tracked.chunkX = watch.chunkX;
            tracked.chunkY = watch.chunkY;
            tracked.chunkZ = watch.chunkZ;
            tracked.fromTerrainEdit = watch.fromTerrainEdit;
            tracked.replacesExistingMesh = watch.replacesExistingMesh;
            tracked.subChunkCount = watch.subChunkCount;
            tracked.validDrawCount = watch.validDrawCount;
            tracked.currentTimeline = timelineU32;
            tracked.gpuReadyTimeline = watch.gpuReadyTimeline;
            tracked.hiZGraceTimeline = watch.hiZGraceTimeline;
            tracked.slotOccupied = slotOccupied;
            tracked.ready = slotOccupied && (watch.gpuReadyTimeline <= timelineU32);
            tracked.hiZEnabled = (hiZEnabled != 0u);
            tracked.hiZActive = tracked.hiZEnabled && slotOccupied && (timelineU32 >= watch.hiZGraceTimeline);
            tracked.uploadSerial = watch.uploadSerial;
            tracked.editUploadSerial = watch.editUploadSerial;
            tracked.watchFramesRemaining = watch.watchFramesRemaining;
            tracked.frustumPassed = slotOccupied &&
                                    aabbPassesFrustumPlanes(frustumPlanes,
                                                            watch.aabbMin, watch.aabbMax);
            dispatchCtx.tracked.push_back(tracked);

            if (watch.watchFramesRemaining > 0u) {
                --watch.watchFramesRemaining;
            }

            m_editWatchedSlots[writeIdx++] = slot;
        }
        m_editWatchedSlots.resize(writeIdx);
        m_pendingEditDispatch = std::move(dispatchCtx);
    } else {
        std::lock_guard<std::mutex> lock(m_slotMutex);
        m_pendingEditDispatch = PendingEditDispatchContext{};
    }
    
    // Stage 1 dispatch size (stage 2 dispatch size is generated on GPU).
    uint32_t workgroupCount = (totalChunks + (FRUSTUM_LOCAL_SIZE_X - 1u)) / FRUSTUM_LOCAL_SIZE_X;
    // Bind descriptor set once for all culling stages.
    vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_frustumPipelineLayout, 0,
                            1, &m_frustumDescriptorSet, 0, nullptr);

    // Stage 1: frustum filter -> compact candidate chunk slots.
    if (workgroupCount > 0) {
        vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_frustumFilterPipeline);
        vkCmdPushConstants(cmd, m_frustumPipelineLayout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(CullPushConstants), &pushData);
        vkCmdDispatch(cmd, workgroupCount, 1, 1);
    }

    // Make stage-1 writes (candidate list/count, debug stats) visible to dispatch-arg prep.
    VkMemoryBarrier2 stageBarrier{};
    stageBarrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
    stageBarrier.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    stageBarrier.srcAccessMask = VK_ACCESS_2_SHADER_WRITE_BIT;
    stageBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    stageBarrier.dstAccessMask = VK_ACCESS_2_SHADER_READ_BIT | VK_ACCESS_2_SHADER_WRITE_BIT;

    VkDependencyInfo stageDepInfo{};
    stageDepInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    stageDepInfo.memoryBarrierCount = 1;
    stageDepInfo.pMemoryBarriers = &stageBarrier;
    vkCmdPipelineBarrier2(cmd, &stageDepInfo);

    // Stage 2 dispatch args (group count) from frustumPassedCount buffer.
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_frustumDispatchPipeline);
    vkCmdDispatch(cmd, 1, 1, 1);

    // Make dispatch args visible to vkCmdDispatchIndirect and keep stage-1 outputs visible to stage 2.
    VkMemoryBarrier2 dispatchBarrier{};
    dispatchBarrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
    dispatchBarrier.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    dispatchBarrier.srcAccessMask = VK_ACCESS_2_SHADER_WRITE_BIT;
    dispatchBarrier.dstStageMask = VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    dispatchBarrier.dstAccessMask = VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT | VK_ACCESS_2_SHADER_READ_BIT;

    VkDependencyInfo dispatchDepInfo{};
    dispatchDepInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    dispatchDepInfo.memoryBarrierCount = 1;
    dispatchDepInfo.pMemoryBarriers = &dispatchBarrier;
    vkCmdPipelineBarrier2(cmd, &dispatchDepInfo);

    // Stage 3: Hi-Z occlusion + draw emission from compact candidate list.
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_frustumPipeline);
    vkCmdPushConstants(cmd, m_frustumPipelineLayout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(CullPushConstants), &pushData);
    vkCmdDispatchIndirect(cmd, m_frustumDispatchArgsBuffer, 0);
}

void GPUCullingSystem::recordBarriersBeforeDraw(VkCommandBuffer cmd) {
    // Barrier: ensure transfer/compute writes are visible to indirect draw.
    // Covers zero-dispatch frames where counters are only reset via vkCmdFillBuffer.
    VkMemoryBarrier2 memBarrier{};
    memBarrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
    memBarrier.srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT | VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    memBarrier.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT | VK_ACCESS_2_SHADER_WRITE_BIT;
    memBarrier.dstStageMask = VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT | VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT;
    memBarrier.dstAccessMask = VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT | VK_ACCESS_2_SHADER_READ_BIT;
    
    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.memoryBarrierCount = 1;
    depInfo.pMemoryBarriers = &memBarrier;
    
    vkCmdPipelineBarrier2(cmd, &depInfo);
}

void GPUCullingSystem::recordBarriersBeforeCull(VkCommandBuffer cmd) {
    // Barrier: ensure previous frame's draw is complete before culling
    VkMemoryBarrier2 memBarrier{};
    memBarrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER_2;
    memBarrier.srcStageMask = VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT | VK_PIPELINE_STAGE_2_VERTEX_SHADER_BIT;
    memBarrier.srcAccessMask = VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT | VK_ACCESS_2_SHADER_READ_BIT;
    memBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_2_TRANSFER_BIT;
    memBarrier.dstAccessMask = VK_ACCESS_2_SHADER_READ_BIT | VK_ACCESS_2_SHADER_WRITE_BIT | VK_ACCESS_2_TRANSFER_WRITE_BIT;
    
    VkDependencyInfo depInfo{};
    depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    depInfo.memoryBarrierCount = 1;
    depInfo.pMemoryBarriers = &memBarrier;
    
    vkCmdPipelineBarrier2(cmd, &depInfo);
}

void GPUCullingSystem::bindHiZPyramid(VkImageView pyramidView, VkSampler pyramidSampler) {
    if (!m_initialized || !m_frustumDescriptorSet) {
        return;
    }

    if (pyramidView == VK_NULL_HANDLE || pyramidSampler == VK_NULL_HANDLE) {
        m_hiZBound = false;
        return;
    }

    // Update binding 5 with the Hi-Z pyramid.
    VkDescriptorImageInfo imageInfo{};
    imageInfo.sampler = pyramidSampler;
    imageInfo.imageView = pyramidView;
    imageInfo.imageLayout = VK_IMAGE_LAYOUT_GENERAL;  // Pyramid stays in GENERAL after compute writes.

    VkWriteDescriptorSet write{};
    write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    write.dstSet = m_frustumDescriptorSet;
    write.dstBinding = 5;
    write.dstArrayElement = 0;
    write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    write.descriptorCount = 1;
    write.pImageInfo = &imageInfo;

    vkUpdateDescriptorSets(m_device, 1, &write, 0, nullptr);

    m_hiZBound = true;
    std::cout << "[GPUCullingSystem] Hi-Z pyramid bound for occlusion culling" << std::endl;
}

void GPUCullingSystem::setSlotMaterialOverlayHint(uint32_t slotIndex, bool hasOverlay) {
    if (slotIndex >= m_maxChunks) return;

    std::lock_guard<std::mutex> lock(m_slotMutex);
    if (slotIndex >= m_slotMaterialOverlayHints.size()) return;

    const uint32_t value = hasOverlay ? 1u : 0u;
    if (m_slotMaterialOverlayHints[slotIndex] == value &&
        m_pendingMaterialOverlayHintUpdates.find(slotIndex) == m_pendingMaterialOverlayHintUpdates.end()) {
        return;
    }

    m_slotMaterialOverlayHints[slotIndex] = value;

    // m_slotOccupied is true for both active and inactive pending LOD slots.
    // Queue the fill now; recordCulling() will write ChunkDrawData._pad1 before
    // the culling shader emits visibleOrigins.w.
    if (slotIndex < m_slotOccupied.size() && m_slotOccupied[slotIndex]) {
        m_pendingMaterialOverlayHintUpdates[slotIndex] = value;
    }
}

void GPUCullingSystem::clearAllMaterialOverlayHints() {
    std::lock_guard<std::mutex> lock(m_slotMutex);
    if (m_slotMaterialOverlayHints.empty()) return;

    for (uint32_t slot = 0; slot < static_cast<uint32_t>(m_slotMaterialOverlayHints.size()); ++slot) {
        if (m_slotMaterialOverlayHints[slot] == 0u) {
            continue;
        }
        m_slotMaterialOverlayHints[slot] = 0u;
        if (slot < m_slotOccupied.size() && m_slotOccupied[slot]) {
            m_pendingMaterialOverlayHintUpdates[slot] = 0u;
        }
    }
}

````

## src\rendering\culling\HiZPyramidDiagnostics.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/culling/HiZPyramid.h"
#include "rendering/common/VulkanHelpers.h"
#include "vulkan/VulkanContext.h"
#include <imgui.h>
#include <imgui_impl_vulkan.h>
#include <stdexcept>
#include <iostream>
#include <fstream>
#include <cstring>
#include <algorithm>
#include <cmath>
#include <string>
#include <sstream>
#include <iomanip>

// Diagnostics functions - extracted from HiZPyramid.cpp

// ─────────────────────────────────────────────────────────────
//  Debug info
// ─────────────────────────────────────────────────────────────

HiZPyramid::DebugInfo HiZPyramid::getDebugInfo() const {
    DebugInfo info{};
    info.pyramidWidth = m_pyramidWidth;
    info.pyramidHeight = m_pyramidHeight;
    info.mipLevels = m_mipLevels;
    info.swapchainWidth = m_swapchainWidth;
    info.swapchainHeight = m_swapchainHeight;
    info.initialized = m_initialized;
    return info;
}

void HiZPyramid::pushDiagnosticsSample(const DiagnosticsSample& sample) {
    m_diagnosticsHistory.push_back(sample);

    while (!m_diagnosticsHistory.empty()) {
        const bool tooManySamples = m_diagnosticsHistory.size() > MAX_DIAGNOSTIC_SAMPLES;
        const bool tooOld =
            (sample.timestampSeconds - m_diagnosticsHistory.front().timestampSeconds) > MAX_DIAGNOSTIC_HISTORY_SECONDS;
        if (!tooManySamples && !tooOld) {
            break;
        }
        m_diagnosticsHistory.pop_front();
    }

    detectCorruption(sample);
}

HiZPyramid::DiagnosticsSummary HiZPyramid::summarizeDiagnostics(double windowSeconds) const {
    DiagnosticsSummary summary{};
    summary.windowSeconds = windowSeconds;

    if (m_diagnosticsHistory.empty()) {
        return summary;
    }

    const double newestTimestamp = m_diagnosticsHistory.back().timestampSeconds;
    const double cutoffTimestamp = newestTimestamp - std::max(windowSeconds, 0.0);

    double totalFrustumPassed = 0.0;
    double totalHiZOccluded = 0.0;
    double totalHiZNearPlaneFail = 0.0;
    double totalPyramidSamples = 0.0;
    double totalPyramidNonZero = 0.0;

    for (const DiagnosticsSample& sample : m_diagnosticsHistory) {
        if (sample.timestampSeconds < cutoffTimestamp) {
            continue;
        }

        summary.sampleCount++;
        switch (sample.mode) {
            case DiagnosticsMode::SameFrameHiZ:
                summary.sameFrameHiZCount++;
                break;
            case DiagnosticsMode::TemporalHiZ:
                summary.temporalHiZCount++;
                break;
            case DiagnosticsMode::FrustumOnly:
            default:
                summary.frustumOnlyCount++;
                break;
        }

        summary.avgCpuFrameMs += sample.cpuFrameMs;
        summary.avgCpuWorkMs += sample.cpuWorkMs;
        summary.avgCpuCullingSetupMs += sample.cpuCullingSetupMs;
        summary.avgCpuCmdRecordMs += sample.cpuCmdRecordMs;
        summary.avgCpuInitialCullRecordMs += sample.cpuInitialCullRecordMs;
        summary.avgCpuDepthPrepassRecordMs += sample.cpuDepthPrepassRecordMs;
        summary.avgCpuHiZBuildRecordMs += sample.cpuHiZBuildRecordMs;
        summary.avgCpuFinalCullRecordMs += sample.cpuFinalCullRecordMs;
        summary.avgCpuHiZIncrementalRecordMs += sample.cpuHiZIncrementalRecordMs;

        summary.avgGpuFrameMs += sample.gpuFrameMs;
        summary.avgGpuInitialCullMs += sample.gpuInitialCullMs;
        summary.avgGpuDepthPrepassMs += sample.gpuDepthPrepassMs;
        summary.avgGpuHiZBuildMs += sample.gpuHiZBuildMs;
        summary.avgGpuFinalCullMs += sample.gpuFinalCullMs;
        summary.avgGpuTerrainMs += sample.gpuTerrainMs;
        summary.avgGpuHiZIncrementalMs += sample.gpuHiZIncrementalMs;

        totalFrustumPassed += static_cast<double>(sample.frustumPassed);
        totalHiZOccluded += static_cast<double>(sample.hiZOccluded);
        totalHiZNearPlaneFail += static_cast<double>(sample.hiZNearPlaneFail);
        totalPyramidNonZero += static_cast<double>(sample.pyramidNonZero);
        totalPyramidSamples += static_cast<double>(sample.pyramidNonZero + sample.pyramidAllZero);
    }

    if (summary.sampleCount == 0) {
        return summary;
    }

    const double sampleCount = static_cast<double>(summary.sampleCount);
    summary.avgCpuFrameMs /= sampleCount;
    summary.avgCpuWorkMs /= sampleCount;
    summary.avgCpuCullingSetupMs /= sampleCount;
    summary.avgCpuCmdRecordMs /= sampleCount;
    summary.avgCpuInitialCullRecordMs /= sampleCount;
    summary.avgCpuDepthPrepassRecordMs /= sampleCount;
    summary.avgCpuHiZBuildRecordMs /= sampleCount;
    summary.avgCpuFinalCullRecordMs /= sampleCount;
    summary.avgCpuHiZIncrementalRecordMs /= sampleCount;

    summary.avgGpuFrameMs /= sampleCount;
    summary.avgGpuInitialCullMs /= sampleCount;
    summary.avgGpuDepthPrepassMs /= sampleCount;
    summary.avgGpuHiZBuildMs /= sampleCount;
    summary.avgGpuFinalCullMs /= sampleCount;
    summary.avgGpuTerrainMs /= sampleCount;
    summary.avgGpuHiZIncrementalMs /= sampleCount;

    summary.avgFrustumPassed = totalFrustumPassed / sampleCount;
    summary.avgHiZOccluded = totalHiZOccluded / sampleCount;
    summary.avgHiZNearPlaneFail = totalHiZNearPlaneFail / sampleCount;
    summary.occlusionRatePercent =
        totalFrustumPassed > 0.0 ? (100.0 * totalHiZOccluded / totalFrustumPassed) : 0.0;
    summary.pyramidNonZeroRatePercent =
        totalPyramidSamples > 0.0 ? (100.0 * totalPyramidNonZero / totalPyramidSamples) : 0.0;

    return summary;
}

std::string HiZPyramid::buildDiagnosticsReport(double windowSeconds) const {
    const DiagnosticsSummary summary = summarizeDiagnostics(windowSeconds);

    std::ostringstream ss;
    ss << std::fixed << std::setprecision(3);
    ss << "=== Hi-Z DIAGNOSTICS REPORT ===\n";
    ss << "Window: " << summary.windowSeconds << " s\n";
    ss << "Samples: " << summary.sampleCount << "\n";

    if (summary.sampleCount == 0) {
        ss << "No Hi-Z diagnostic samples captured yet.\n";
        return ss.str();
    }

    const double totalSamples = static_cast<double>(summary.sampleCount);
    ss << "Modes:\n";
    ss << "  Same-frame Hi-Z: " << summary.sameFrameHiZCount
       << " (" << (100.0 * static_cast<double>(summary.sameFrameHiZCount) / totalSamples) << "%)\n";
    ss << "  Temporal Hi-Z:   " << summary.temporalHiZCount
       << " (" << (100.0 * static_cast<double>(summary.temporalHiZCount) / totalSamples) << "%)\n";
    ss << "  Frustum-only:    " << summary.frustumOnlyCount
       << " (" << (100.0 * static_cast<double>(summary.frustumOnlyCount) / totalSamples) << "%)\n";

    ss << "\nCPU averages:\n";
    ss << "  Frame total:            " << summary.avgCpuFrameMs << " ms\n";
    ss << "  CPU work:               " << summary.avgCpuWorkMs << " ms\n";
    ss << "  Culling setup:          " << summary.avgCpuCullingSetupMs << " ms\n";
    ss << "  Command record total:   " << summary.avgCpuCmdRecordMs << " ms\n";
    ss << "  Initial cull record:    " << summary.avgCpuInitialCullRecordMs << " ms\n";
    ss << "  Depth prepass record:   " << summary.avgCpuDepthPrepassRecordMs << " ms\n";
    ss << "  Hi-Z build record:      " << summary.avgCpuHiZBuildRecordMs << " ms\n";
    ss << "  Final cull record:      " << summary.avgCpuFinalCullRecordMs << " ms\n";
    ss << "  Hi-Z extra record:      " << summary.avgCpuHiZIncrementalRecordMs << " ms\n";

    ss << "\nGPU averages (completed frames):\n";
    ss << "  Frame total:            " << summary.avgGpuFrameMs << " ms\n";
    ss << "  Initial cull:           " << summary.avgGpuInitialCullMs << " ms\n";
    ss << "  Depth prepass:          " << summary.avgGpuDepthPrepassMs << " ms\n";
    ss << "  Hi-Z build:             " << summary.avgGpuHiZBuildMs << " ms\n";
    ss << "  Final cull:             " << summary.avgGpuFinalCullMs << " ms\n";
    ss << "  Terrain shading:        " << summary.avgGpuTerrainMs << " ms\n";
    ss << "  Hi-Z extra GPU cost:    " << summary.avgGpuHiZIncrementalMs << " ms\n";

    ss << "\nCulling effectiveness:\n";
    ss << "  Avg frustum passed:     " << summary.avgFrustumPassed << "\n";
    ss << "  Avg Hi-Z occluded:      " << summary.avgHiZOccluded << "\n";
    ss << "  Avg near-plane bail:    " << summary.avgHiZNearPlaneFail << "\n";
    ss << "  Occlusion rate:         " << summary.occlusionRatePercent << " %\n";
    ss << "  Pyramid non-zero rate:  " << summary.pyramidNonZeroRatePercent << " %\n";

    return ss.str();
}

// ─────────────────────────────────────────────────────────────
//  Corruption detection
// ─────────────────────────────────────────────────────────────

void HiZPyramid::detectCorruption(const DiagnosticsSample& sample) {
    const uint32_t totalPyramidSamples = sample.pyramidNonZero + sample.pyramidAllZero;
    const float pyramidNonZeroPct = (totalPyramidSamples > 0)
        ? (100.0f * static_cast<float>(sample.pyramidNonZero) / static_cast<float>(totalPyramidSamples))
        : 0.0f;
    const float occlusionPct = (sample.frustumPassed > 0)
        ? (100.0f * static_cast<float>(sample.hiZOccluded) / static_cast<float>(sample.frustumPassed))
        : 0.0f;

    auto emitEvent = [&](CorruptionEvent::Reason reason) {
        CorruptionEvent event{};
        event.timestampSeconds = sample.timestampSeconds;
        event.mode = sample.mode;
        event.reason = reason;
        event.pyramidNonZeroPercent = pyramidNonZeroPct;
        event.prevPyramidNonZeroPercent = m_prevPyramidNonZeroPercent;
        event.occlusionRatePercent = occlusionPct;
        event.prevOcclusionRatePercent = m_prevOcclusionRatePercent;
        event.cameraRotationDeg = sample.cameraRotationDeg;
        event.cameraTranslation = sample.cameraTranslation;
        event.cameraYaw = sample.cameraYaw;
        event.cameraPitch = sample.cameraPitch;
        event.gpuFrameMs = sample.gpuFrameMs;
        event.frustumPassed = sample.frustumPassed;
        event.hiZOccluded = sample.hiZOccluded;
        event.hiZNearPlaneFail = sample.hiZNearPlaneFail;
        event.pyramidNonZero = sample.pyramidNonZero;
        event.pyramidAllZero = sample.pyramidAllZero;
        event.degenerateUV = sample.degenerateUV;
        event.holeRecoveryFail = sample.holeRecoveryFail;
        event.hiZDepthTestVisible = sample.hiZDepthTestVisible;
        event.frameInFlightIndex = sample.frameInFlightIndex;
        std::memcpy(event.prevVPDiag, sample.prevVPDiag, sizeof(event.prevVPDiag));
        std::memcpy(event.viewportUvTransform, sample.viewportUvTransform, sizeof(event.viewportUvTransform));

        m_corruptionLog.push_back(event);

        // Trim old events
        while (!m_corruptionLog.empty()) {
            const bool tooMany = m_corruptionLog.size() > MAX_CORRUPTION_EVENTS;
            const bool tooOld = (sample.timestampSeconds - m_corruptionLog.front().timestampSeconds)
                                > MAX_CORRUPTION_HISTORY_SECONDS;
            if (!tooMany && !tooOld) break;
            m_corruptionLog.pop_front();
        }
    };

    // Only run detections when we have a previous frame to compare against
    // and modes that actually use the pyramid (not frustum-only)
    const bool pyramidActive = (sample.mode != DiagnosticsMode::FrustumOnly);

    if (pyramidActive) {
        // Detection 1: Pyramid completely empty when it shouldn't be.
        // Only fire if the Hi-Z test actually ran (totalPyramidSamples > 0); when
        // hiZEnabled=0 (frustum-only frame due to edit) both stats are 0 which is
        // not a corruption — it just means no chunks were Hi-Z tested.
        if (totalPyramidSamples > 0 && sample.pyramidNonZero == 0) {
            emitEvent(CorruptionEvent::PyramidEmpty);
        }

        // Detection 2: Pyramid non-zero % dropped sharply (>25 percentage points in one frame).
        // Gated on totalPyramidSamples > 0 to avoid false positives from frustum-only
        // edit frames where no Hi-Z testing runs (pyramidNonZeroPct would be 0/0 = 0%).
        if (totalPyramidSamples > 0 &&
            m_prevPyramidNonZeroPercent >= 0.0f && m_prevPyramidNonZeroPercent > 20.0f) {
            const float drop = m_prevPyramidNonZeroPercent - pyramidNonZeroPct;
            if (drop > 25.0f) {
                emitEvent(CorruptionEvent::PyramidNonZeroDrop);
            }
        }

        // Detection 3: Occlusion rate spiked abnormally (>40pp jump in one frame)
        if (m_prevOcclusionRatePercent >= 0.0f && sample.frustumPassed > 10) {
            const float jump = occlusionPct - m_prevOcclusionRatePercent;
            if (jump > 40.0f) {
                emitEvent(CorruptionEvent::OcclusionRateSpike);
            }
        }

        // Detection 4: Occlusion rate dropped to near zero when previously healthy
        if (m_prevOcclusionRatePercent > 15.0f && occlusionPct < 2.0f && sample.frustumPassed > 10) {
            emitEvent(CorruptionEvent::OcclusionRateDrop);
        }
    }

    // Only update the running pyramid NZ baseline from frames where Hi-Z actually ran.
    // Frustum-only frames (totalPyramidSamples==0) should not reset the baseline to 0%.
    if (totalPyramidSamples > 0) {
        m_prevPyramidNonZeroPercent = pyramidNonZeroPct;
    }
    m_prevOcclusionRatePercent = occlusionPct;
}

uint32_t HiZPyramid::getCorruptionCount(double windowSeconds) const {
    if (m_corruptionLog.empty()) return 0;

    const double newest = m_corruptionLog.back().timestampSeconds;
    const double cutoff = newest - std::max(windowSeconds, 0.0);
    uint32_t count = 0;
    for (const CorruptionEvent& e : m_corruptionLog) {
        if (e.timestampSeconds >= cutoff) count++;
    }
    return count;
}

std::string HiZPyramid::buildCorruptionReport(double windowSeconds) const {
    std::ostringstream ss;
    ss << std::fixed << std::setprecision(2);
    ss << "=== Hi-Z CORRUPTION DIAGNOSTICS (last " << windowSeconds << "s) ===\n";

    if (m_corruptionLog.empty()) {
        ss << "No corruption events detected.\n";
        return ss.str();
    }

    const double newest = m_corruptionLog.back().timestampSeconds;
    const double cutoff = newest - std::max(windowSeconds, 0.0);

    uint32_t count = 0;
    uint32_t countByReason[4] = {};

    for (const CorruptionEvent& e : m_corruptionLog) {
        if (e.timestampSeconds < cutoff) continue;
        count++;
        if (e.reason < 4) countByReason[e.reason]++;
    }

    ss << "Events: " << count << "\n";
    ss << "  Pyramid non-zero DROP:  " << countByReason[0] << "\n";
    ss << "  Occlusion rate SPIKE:   " << countByReason[1] << "\n";
    ss << "  Occlusion rate DROP:    " << countByReason[2] << "\n";
    ss << "  Pyramid EMPTY:          " << countByReason[3] << "\n\n";

    // List recent events (last 20)
    ss << "--- Recent events (newest first) ---\n";
    int listed = 0;
    for (auto it = m_corruptionLog.rbegin(); it != m_corruptionLog.rend() && listed < 20; ++it) {
        if (it->timestampSeconds < cutoff) continue;
        listed++;

        const char* modeStr = "Frustum";
        if (it->mode == DiagnosticsMode::TemporalHiZ) modeStr = "Temporal";
        else if (it->mode == DiagnosticsMode::SameFrameHiZ) modeStr = "SameFrame";

        ss << "#" << listed << " [" << std::setprecision(1)
           << (it->timestampSeconds - newest) << "s ago] "
           << CorruptionEvent::reasonString(it->reason) << "\n";
        ss << std::setprecision(2);
        ss << "  Mode: " << modeStr
           << "  Rot: " << it->cameraRotationDeg << " deg"
           << "  Trans: " << it->cameraTranslation << " m"
           << "  Yaw: " << it->cameraYaw << "  Pitch: " << it->cameraPitch << "\n";
        ss << "  Frame-in-flight: " << it->frameInFlightIndex << "\n";
        ss << "  PyramidNZ: " << it->pyramidNonZeroPercent << "% (prev " << it->prevPyramidNonZeroPercent << "%)"
           << "  OccRate: " << it->occlusionRatePercent << "% (prev " << it->prevOcclusionRatePercent << "%)\n";
        ss << "  Frustum: " << it->frustumPassed
           << "  Occluded: " << it->hiZOccluded
           << "  NearFail: " << it->hiZNearPlaneFail
           << "  DegenUV: " << it->degenerateUV
           << "  HoleFail: " << it->holeRecoveryFail
           << "  DepthVisible: " << it->hiZDepthTestVisible << "\n";
        ss << "  NZ: " << it->pyramidNonZero
           << "  Zero: " << it->pyramidAllZero
           << "  GPU: " << it->gpuFrameMs << "ms\n";
        ss << "  prevVP diag: ["
           << it->prevVPDiag[0] << ", " << it->prevVPDiag[1] << ", "
           << it->prevVPDiag[2] << ", " << it->prevVPDiag[3] << "]\n";
        ss << "  viewport UV: ["
           << it->viewportUvTransform[0] << ", " << it->viewportUvTransform[1] << ", "
           << it->viewportUvTransform[2] << ", " << it->viewportUvTransform[3] << "]\n";
    }

    // Add Vulkan validation messages if any
    const auto& validationMsgs = VulkanContext::getValidationMessages();
    if (!validationMsgs.empty()) {
        ss << "\n=== VULKAN VALIDATION MESSAGES (last " << validationMsgs.size() << ") ===\n";
        int msgNum = 0;
        for (auto it = validationMsgs.rbegin(); it != validationMsgs.rend() && msgNum < 30; ++it, ++msgNum) {
            const char* severityStr = "INFO";
            switch (it->severity) {
                case VulkanContext::ValidationMessage::Severity::Error: severityStr = "ERROR"; break;
                case VulkanContext::ValidationMessage::Severity::Warning: severityStr = "WARNING"; break;
                case VulkanContext::ValidationMessage::Severity::Info: severityStr = "INFO"; break;
                case VulkanContext::ValidationMessage::Severity::Verbose: severityStr = "VERBOSE"; break;
            }
            ss << "#" << (msgNum + 1) << " [" << std::setprecision(2) << it->timestamp << "s] "
               << severityStr;
            if (!it->messageId.empty()) {
                ss << " (" << it->messageId << ")";
            }
            ss << "\n  " << it->message << "\n";
        }
    }

    return ss.str();
}

std::string HiZPyramid::buildCorruptionReportLastN(size_t maxEvents) const {
    std::ostringstream ss;
    ss << std::fixed << std::setprecision(2);
    ss << "=== Hi-Z CORRUPTION DIAGNOSTICS (last " << maxEvents << " events) ===\n";

    if (m_corruptionLog.empty()) {
        ss << "No corruption events detected.\n";
        return ss.str();
    }

    const double newest = m_corruptionLog.back().timestampSeconds;

    // Count per-reason across all collected events (no time cutoff)
    uint32_t countByReason[4] = {};
    for (const CorruptionEvent& e : m_corruptionLog) {
        if (e.reason < 4) countByReason[e.reason]++;
    }
    ss << "Total log size: " << m_corruptionLog.size() << "\n";
    ss << "  Pyramid non-zero DROP:  " << countByReason[0] << "\n";
    ss << "  Occlusion rate SPIKE:   " << countByReason[1] << "\n";
    ss << "  Occlusion rate DROP:    " << countByReason[2] << "\n";
    ss << "  Pyramid EMPTY:          " << countByReason[3] << "\n\n";

    // List the last maxEvents events (newest first, no time cutoff)
    ss << "--- Last " << maxEvents << " events (newest first) ---\n";
    int listed = 0;
    for (auto it = m_corruptionLog.rbegin();
         it != m_corruptionLog.rend() && static_cast<size_t>(listed) < maxEvents;
         ++it) {
        ++listed;

        const char* modeStr = "Frustum";
        if (it->mode == DiagnosticsMode::TemporalHiZ) modeStr = "Temporal";
        else if (it->mode == DiagnosticsMode::SameFrameHiZ) modeStr = "SameFrame";

        ss << "#" << listed << " [" << std::setprecision(1)
           << (it->timestampSeconds - newest) << "s ago] "
           << CorruptionEvent::reasonString(it->reason) << "\n";
        ss << std::setprecision(2);
        ss << "  Mode: " << modeStr
           << "  Rot: " << it->cameraRotationDeg << " deg"
           << "  Trans: " << it->cameraTranslation << " m"
           << "  Yaw: " << it->cameraYaw << "  Pitch: " << it->cameraPitch << "\n";
        ss << "  Frame-in-flight: " << it->frameInFlightIndex << "\n";
        ss << "  PyramidNZ: " << it->pyramidNonZeroPercent << "% (prev " << it->prevPyramidNonZeroPercent << "%)"
           << "  OccRate: " << it->occlusionRatePercent << "% (prev " << it->prevOcclusionRatePercent << "%)\n";
        ss << "  Frustum: " << it->frustumPassed
           << "  Occluded: " << it->hiZOccluded
           << "  NearFail: " << it->hiZNearPlaneFail
           << "  DegenUV: " << it->degenerateUV
           << "  HoleFail: " << it->holeRecoveryFail
           << "  DepthVisible: " << it->hiZDepthTestVisible << "\n";
        ss << "  NZ: " << it->pyramidNonZero
           << "  Zero: " << it->pyramidAllZero
           << "  GPU: " << it->gpuFrameMs << "ms\n";
        ss << "  prevVP diag: ["
           << it->prevVPDiag[0] << ", " << it->prevVPDiag[1] << ", "
           << it->prevVPDiag[2] << ", " << it->prevVPDiag[3] << "]\n";
        ss << "  viewport UV: ["
           << it->viewportUvTransform[0] << ", " << it->viewportUvTransform[1] << ", "
           << it->viewportUvTransform[2] << ", " << it->viewportUvTransform[3] << "]\n";
    }

    return ss.str();
}

````

## src\rendering\lighting\ShadowDiagnostics.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/lighting/ShadowSystem.h"

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <limits>
#include <vector>

namespace {

constexpr uint32_t kInvalidSourceIndex = std::numeric_limits<uint32_t>::max();
constexpr uint32_t kPointShadowDepthComparesPerSample = 25u; // 5 taps * 5 sub-samples

} // namespace

void ShadowSystem::collectGpuTimings(uint32_t imageIndex) {
    // Always collect sun timing first (even if point lights are empty).
    collectSunGpuTiming(imageIndex);

    if (!m_initialized ||
        m_lightTimingQueryPool == VK_NULL_HANDLE ||
        imageIndex >= m_lightTimingImageCount ||
        imageIndex >= m_queryLightCountByImage.size() ||
        imageIndex >= m_querySourceByImage.size()) {
        return;
    }

    const uint32_t lightCount = m_queryLightCountByImage[imageIndex];
    m_frameDiagnostics.terrainPassGpuMs = m_lastTerrainPassGpuMs;
    m_frameDiagnostics.totalShadowGpuMs = 0.0f;
    m_frameDiagnostics.avgShadowGpuMsPerLight = 0.0f;
    m_frameDiagnostics.terrainMsPerMegaShadowSample = 0.0f;
    m_frameDiagnostics.totalPointShadowSamples = 0u;
    m_frameDiagnostics.totalPointLightEvaluations = 0u;
    m_frameDiagnostics.totalPointShadowFullyOccluded = 0u;
    m_frameDiagnostics.totalPointShadowLitContrib = 0u;
    m_frameDiagnostics.totalEstimatedShadowDepthCompareOps = 0u;
    m_frameDiagnostics.detailedCountersEnabled = m_detailedDiagnosticsEnabled;
    if (lightCount == 0u) {
        return;
    }

    std::vector<uint64_t> timestamps(lightCount * 2u, 0u);
    const uint32_t queryBase = imageIndex * (MAX_POINT_SHADOW_LIGHTS * 2u);
    VkResult res = vkGetQueryPoolResults(
        m_device,
        m_lightTimingQueryPool,
        queryBase,
        lightCount * 2u,
        static_cast<VkDeviceSize>(timestamps.size() * sizeof(uint64_t)),
        timestamps.data(),
        sizeof(uint64_t),
        VK_QUERY_RESULT_64_BIT);

    if (res != VK_SUCCESS) {
        return;
    }

    const ShadowGPUData* shadowData = nullptr;
    if (imageIndex < m_shadowDataMapped.size()) {
        shadowData = reinterpret_cast<const ShadowGPUData*>(m_shadowDataMapped[imageIndex]);
    }

    uint32_t timedLights = 0u;
    for (uint32_t slot = 0; slot < lightCount; ++slot) {
        const uint32_t source = m_querySourceByImage[imageIndex][slot];
        if (source == kInvalidSourceIndex || source >= m_lightDiagnostics.size()) {
            continue;
        }

        const uint64_t start = timestamps[slot * 2u + 0u];
        const uint64_t end = timestamps[slot * 2u + 1u];
        float gpuMs = 0.0f;
        if (end > start && m_timestampPeriod > 0.0f) {
            gpuMs = static_cast<float>(
                static_cast<double>(end - start) *
                static_cast<double>(m_timestampPeriod) / 1'000'000.0);
        }

        auto& diag = m_lightDiagnostics[source];
        ++timedLights;
        m_frameDiagnostics.totalShadowGpuMs += gpuMs;
        diag.gpuShadowMs = gpuMs;
        if (diag.gpuShadowMsAvg <= 0.0001f) {
            diag.gpuShadowMsAvg = gpuMs;
        } else {
            diag.gpuShadowMsAvg = diag.gpuShadowMsAvg * 0.85f + gpuMs * 0.15f;
        }
        diag.terrainPassGpuMs = m_lastTerrainPassGpuMs;

        if (shadowData) {
            const glm::uvec4 c = shadowData->pointShadowDiag[slot];
            diag.pointShadowSamples = c.x;
            diag.pointLightEvaluations = c.y;
            diag.pointShadowFullyOccluded = c.z;
            diag.pointShadowLitContrib = c.w;
            m_frameDiagnostics.totalPointShadowSamples += c.x;
            m_frameDiagnostics.totalPointLightEvaluations += c.y;
            m_frameDiagnostics.totalPointShadowFullyOccluded += c.z;
            m_frameDiagnostics.totalPointShadowLitContrib += c.w;
        } else {
            diag.pointShadowSamples = 0u;
            diag.pointLightEvaluations = 0u;
            diag.pointShadowFullyOccluded = 0u;
            diag.pointShadowLitContrib = 0u;
        }

        diag.estShadowDepthCompareOps =
            static_cast<uint64_t>(diag.pointShadowSamples) * kPointShadowDepthComparesPerSample;
        m_frameDiagnostics.totalEstimatedShadowDepthCompareOps += diag.estShadowDepthCompareOps;

        if (diag.pointShadowSamples > 0u) {
            const float invSamples = 1.0f / static_cast<float>(diag.pointShadowSamples);
            diag.shadowOccludedRatio = static_cast<float>(diag.pointShadowFullyOccluded) * invSamples;
            diag.shadowLitRatio = static_cast<float>(diag.pointShadowLitContrib) * invSamples;
            diag.evalToSampleRatio = static_cast<float>(diag.pointLightEvaluations) * invSamples;
            diag.gpuMsPerMegaShadowSample =
                gpuMs * 1'000'000.0f / static_cast<float>(diag.pointShadowSamples);
        } else {
            diag.shadowOccludedRatio = 0.0f;
            diag.shadowLitRatio = 0.0f;
            diag.evalToSampleRatio = 0.0f;
            diag.gpuMsPerMegaShadowSample = 0.0f;
        }
    }

    if (timedLights > 0u) {
        m_frameDiagnostics.avgShadowGpuMsPerLight =
            m_frameDiagnostics.totalShadowGpuMs / static_cast<float>(timedLights);
    }

    const float terrainPassMs = std::max(0.0f, m_lastTerrainPassGpuMs);
    if (m_frameDiagnostics.totalPointShadowSamples > 0u) {
        m_frameDiagnostics.terrainMsPerMegaShadowSample =
            terrainPassMs * 1'000'000.0f /
            static_cast<float>(m_frameDiagnostics.totalPointShadowSamples);
    } else {
        m_frameDiagnostics.terrainMsPerMegaShadowSample = 0.0f;
    }
    constexpr float kShadowSamplingWeight = 0.65f;
    constexpr float kLightingEvalWeight = 0.35f;
    const float totalSamples =
        static_cast<float>(std::max<uint64_t>(m_frameDiagnostics.totalPointShadowSamples, 1u));
    const float totalEvals =
        static_cast<float>(std::max<uint64_t>(m_frameDiagnostics.totalPointLightEvaluations, 1u));

    for (uint32_t slot = 0; slot < lightCount; ++slot) {
        const uint32_t source = m_querySourceByImage[imageIndex][slot];
        if (source == kInvalidSourceIndex || source >= m_lightDiagnostics.size()) {
            continue;
        }

        auto& diag = m_lightDiagnostics[source];
        const float sampleShare = static_cast<float>(diag.pointShadowSamples) / totalSamples;
        const float evalShare = static_cast<float>(diag.pointLightEvaluations) / totalEvals;
        const float weightedShare = sampleShare * kShadowSamplingWeight + evalShare * kLightingEvalWeight;

        diag.estTerrainLightingShareMs = terrainPassMs * weightedShare;
        diag.estShadowSamplingMs = terrainPassMs * sampleShare * kShadowSamplingWeight;
        diag.estShadowOffDeltaMs = diag.gpuShadowMs + diag.estShadowSamplingMs;
        diag.estLightOffDeltaMs = diag.estShadowOffDeltaMs +
                                  diag.estTerrainLightingShareMs;
    }
}

void ShadowSystem::setFrameGpuPassCosts(float terrainPassGpuMs) {
    m_lastTerrainPassGpuMs = std::max(terrainPassGpuMs, 0.0f);
    m_frameDiagnostics.terrainPassGpuMs = m_lastTerrainPassGpuMs;
}

const ShadowSystem::LightDiagnostics* ShadowSystem::getLightDiagnostics(uint32_t sourceLightIndex) const {
    if (sourceLightIndex >= m_lightDiagnostics.size()) {
        return nullptr;
    }
    return &m_lightDiagnostics[sourceLightIndex];
}

// ═══════════════════════════════════════════════════════════════════════
// Sun shadow diagnostics — GPU readback + rolling window
// ═══════════════════════════════════════════════════════════════════════

void ShadowSystem::collectSunGpuTiming(uint32_t imageIndex) {
    // Read GPU timestamps for the sun shadow pass, then push the
    // completed frame sample into the rolling history.
    float gpuMs = 0.0f;

    if (m_sunTimingQueryPool != VK_NULL_HANDLE &&
        imageIndex < m_sunTimingImageCount &&
        imageIndex < m_sunTimingWritten.size() &&
        m_sunTimingWritten[imageIndex]) {

        std::array<uint64_t, 2> ts{};
        VkResult res = vkGetQueryPoolResults(
            m_device,
            m_sunTimingQueryPool,
            imageIndex * 2u,
            2u,
            sizeof(ts),
            ts.data(),
            sizeof(uint64_t),
            VK_QUERY_RESULT_64_BIT);

        if (res == VK_SUCCESS && ts[1] > ts[0] && m_timestampPeriod > 0.0f) {
            gpuMs = static_cast<float>(
                static_cast<double>(ts[1] - ts[0]) *
                static_cast<double>(m_timestampPeriod) / 1'000'000.0);
        }
    }

    m_sunCurrentFrame.gpuRenderMs = gpuMs;
    // VP compute was already filled in updateForFrame; terrain+record in recordShadowPasses.
    // cpuTotalMs should include VP compute as well.
    m_sunCurrentFrame.cpuTotalMs += m_sunCurrentFrame.cpuVpComputeMs;

    pushSunSample(m_sunCurrentFrame);
}

void ShadowSystem::pushSunSample(const SunShadowFrameSample& sample) {
    SunShadowTimedSample ts;
    ts.timestamp = std::chrono::steady_clock::now();
    ts.data = sample;
    m_sunSampleHistory.push_back(ts);

    // Evict samples older than the window
    const auto cutoff = ts.timestamp -
        std::chrono::milliseconds(static_cast<int64_t>(m_sunDiagnostics.windowSeconds * 1000.0f));
    while (!m_sunSampleHistory.empty() && m_sunSampleHistory.front().timestamp < cutoff) {
        m_sunSampleHistory.pop_front();
    }

    recomputeSunWindowStats();
}

void ShadowSystem::recomputeSunWindowStats() {
    const uint32_t n = static_cast<uint32_t>(m_sunSampleHistory.size());
    m_sunDiagnostics.sampleCount = n;

    if (n == 0u) {
        m_sunDiagnostics.latest = {};
        m_sunDiagnostics.avgCpuVpComputeMs = 0.0f;
        m_sunDiagnostics.avgCpuTerrainGatherMs = 0.0f;
        m_sunDiagnostics.avgCpuWorldGatherMs = 0.0f;
        m_sunDiagnostics.avgCpuTerrainHashMs = 0.0f;
        m_sunDiagnostics.avgCpuCacheDecisionMs = 0.0f;
        m_sunDiagnostics.avgCpuCommandRecordMs = 0.0f;
        m_sunDiagnostics.avgCpuTotalMs = 0.0f;
        m_sunDiagnostics.maxCpuVpComputeMs = 0.0f;
        m_sunDiagnostics.maxCpuTerrainGatherMs = 0.0f;
        m_sunDiagnostics.maxCpuWorldGatherMs = 0.0f;
        m_sunDiagnostics.maxCpuTerrainHashMs = 0.0f;
        m_sunDiagnostics.maxCpuCacheDecisionMs = 0.0f;
        m_sunDiagnostics.maxCpuCommandRecordMs = 0.0f;
        m_sunDiagnostics.maxCpuTotalMs = 0.0f;
        m_sunDiagnostics.avgGpuRenderMs = 0.0f;
        m_sunDiagnostics.maxGpuRenderMs = 0.0f;
        m_sunDiagnostics.minGpuRenderMs = 0.0f;
        m_sunDiagnostics.avgDrawCallCount = 0.0f;
        m_sunDiagnostics.avgApiDrawCallCount = 0.0f;
        m_sunDiagnostics.avgTerrainChunks = 0.0f;
        m_sunDiagnostics.avgGatherCandidateChunks = 0.0f;
        m_sunDiagnostics.avgAcceptedChunks = 0.0f;
        m_sunDiagnostics.avgRenderedFrameDrawCalls = 0.0f;
        m_sunDiagnostics.avgRenderedFrameApiDrawCalls = 0.0f;
        m_sunDiagnostics.avgRenderedFrameGpuMs = 0.0f;
        m_sunDiagnostics.avgCascadesRendered = 0.0f;
        m_sunDiagnostics.avgCascadesReused = 0.0f;
        m_sunDiagnostics.renderCachePrecheckHits = 0u;
        m_sunDiagnostics.gatherCacheHits = 0u;
        m_sunDiagnostics.gatherCacheMisses = 0u;
        m_sunDiagnostics.reusedFrames = 0u;
        m_sunDiagnostics.renderedFrames = 0u;
        return;
    }

    m_sunDiagnostics.latest = m_sunSampleHistory.back().data;

    float sumVp = 0.0f, sumGather = 0.0f, sumWorldGather = 0.0f;
    float sumHash = 0.0f, sumCacheDecision = 0.0f;
    float sumRecord = 0.0f, sumTotal = 0.0f;
    float sumGpu = 0.0f;
    float maxVp = 0.0f, maxGather = 0.0f, maxWorldGather = 0.0f;
    float maxHash = 0.0f, maxCacheDecision = 0.0f;
    float maxRecord = 0.0f, maxTotal = 0.0f;
    float maxGpu = 0.0f;
    float minGpu = std::numeric_limits<float>::max();
    float sumDraws = 0.0f, sumApiDraws = 0.0f, sumChunks = 0.0f;
    float sumCandidates = 0.0f, sumAccepted = 0.0f;
    float sumRenderedDraws = 0.0f, sumRenderedApiDraws = 0.0f, sumRenderedGpu = 0.0f;
    float sumCascadesRendered = 0.0f, sumCascadesReused = 0.0f;
    uint32_t reused = 0u, rendered = 0u;
    uint32_t precheckHits = 0u, gatherHits = 0u, gatherMisses = 0u;

    for (const auto& s : m_sunSampleHistory) {
        const auto& d = s.data;
        sumVp += d.cpuVpComputeMs;
        sumGather += d.cpuTerrainGatherMs;
        sumWorldGather += d.cpuWorldGatherMs;
        sumHash += d.cpuTerrainHashMs;
        sumCacheDecision += d.cpuCacheDecisionMs;
        sumRecord += d.cpuCommandRecordMs;
        sumTotal += d.cpuTotalMs;
        sumGpu += d.gpuRenderMs;
        sumDraws += static_cast<float>(d.drawCallCount);
        sumApiDraws += static_cast<float>(d.apiDrawCallCount);
        sumChunks += static_cast<float>(d.terrainChunksGathered);
        sumCandidates += static_cast<float>(d.bboxCandidateChunks);
        sumAccepted += static_cast<float>(d.acceptedChunkCount);
        sumCascadesRendered += static_cast<float>(d.cascadesRendered);
        sumCascadesReused += static_cast<float>(d.cascadesReused);

        maxVp = std::max(maxVp, d.cpuVpComputeMs);
        maxGather = std::max(maxGather, d.cpuTerrainGatherMs);
        maxWorldGather = std::max(maxWorldGather, d.cpuWorldGatherMs);
        maxHash = std::max(maxHash, d.cpuTerrainHashMs);
        maxCacheDecision = std::max(maxCacheDecision, d.cpuCacheDecisionMs);
        maxRecord = std::max(maxRecord, d.cpuCommandRecordMs);
        maxTotal = std::max(maxTotal, d.cpuTotalMs);
        maxGpu = std::max(maxGpu, d.gpuRenderMs);
        if (!d.wasReused) {
            minGpu = std::min(minGpu, d.gpuRenderMs);
            sumRenderedDraws += static_cast<float>(d.drawCallCount);
            sumRenderedApiDraws += static_cast<float>(d.apiDrawCallCount);
            sumRenderedGpu += d.gpuRenderMs;
        }

        if (d.wasReused) ++reused; else ++rendered;
        if (d.renderCachePrecheckHit) ++precheckHits;
        if (d.gatherCacheHit) ++gatherHits;
        if (d.usedLocalTerrainGather && !d.renderCachePrecheckHit && !d.gatherCacheHit) ++gatherMisses;
    }

    const float inv = 1.0f / static_cast<float>(n);
    m_sunDiagnostics.avgCpuVpComputeMs = sumVp * inv;
    m_sunDiagnostics.avgCpuTerrainGatherMs = sumGather * inv;
    m_sunDiagnostics.avgCpuWorldGatherMs = sumWorldGather * inv;
    m_sunDiagnostics.avgCpuTerrainHashMs = sumHash * inv;
    m_sunDiagnostics.avgCpuCacheDecisionMs = sumCacheDecision * inv;
    m_sunDiagnostics.avgCpuCommandRecordMs = sumRecord * inv;
    m_sunDiagnostics.avgCpuTotalMs = sumTotal * inv;
    m_sunDiagnostics.avgGpuRenderMs = sumGpu * inv;
    m_sunDiagnostics.maxGpuRenderMs = maxGpu;
    m_sunDiagnostics.minGpuRenderMs = (minGpu < std::numeric_limits<float>::max()) ? minGpu : 0.0f;
    m_sunDiagnostics.maxCpuVpComputeMs = maxVp;
    m_sunDiagnostics.maxCpuTerrainGatherMs = maxGather;
    m_sunDiagnostics.maxCpuWorldGatherMs = maxWorldGather;
    m_sunDiagnostics.maxCpuTerrainHashMs = maxHash;
    m_sunDiagnostics.maxCpuCacheDecisionMs = maxCacheDecision;
    m_sunDiagnostics.maxCpuCommandRecordMs = maxRecord;
    m_sunDiagnostics.maxCpuTotalMs = maxTotal;
    m_sunDiagnostics.avgDrawCallCount = sumDraws * inv;
    m_sunDiagnostics.avgApiDrawCallCount = sumApiDraws * inv;
    m_sunDiagnostics.avgTerrainChunks = sumChunks * inv;
    m_sunDiagnostics.avgGatherCandidateChunks = sumCandidates * inv;
    m_sunDiagnostics.avgAcceptedChunks = sumAccepted * inv;
    m_sunDiagnostics.avgRenderedFrameDrawCalls =
        (rendered > 0u) ? (sumRenderedDraws / static_cast<float>(rendered)) : 0.0f;
    m_sunDiagnostics.avgRenderedFrameApiDrawCalls =
        (rendered > 0u) ? (sumRenderedApiDraws / static_cast<float>(rendered)) : 0.0f;
    m_sunDiagnostics.avgRenderedFrameGpuMs =
        (rendered > 0u) ? (sumRenderedGpu / static_cast<float>(rendered)) : 0.0f;
    m_sunDiagnostics.avgCascadesRendered = sumCascadesRendered * inv;
    m_sunDiagnostics.avgCascadesReused = sumCascadesReused * inv;
    m_sunDiagnostics.renderCachePrecheckHits = precheckHits;
    m_sunDiagnostics.gatherCacheHits = gatherHits;
    m_sunDiagnostics.gatherCacheMisses = gatherMisses;
    m_sunDiagnostics.reusedFrames = reused;
    m_sunDiagnostics.renderedFrames = rendered;
}

````

## src\ui\debug_menu\profiling\FPSProfilerWindow.cpp

Description: No CC-DESC found.

````cpp
#include "ui/debug_menu/profiling/FPSProfilerWindow.h"
#include <algorithm>
#include <cstdio>
#include <cmath>

FPSProfilerWindow::FPSProfilerWindow()
    : DebugWindowBase("FPS Profiler")
{
    m_currentFpsHistory.fill(0.0f);
    m_realFpsHistory.fill(0.0f);
}

int FPSProfilerWindow::getTargetFPS() const {
    switch (m_limitMode) {
        case FPSLimitMode::Unlimited:    return 0;
        case FPSLimitMode::VSync:        return 0;  // VSync handles pacing via swapchain
        case FPSLimitMode::MatchMonitor: return m_monitorHz;
        case FPSLimitMode::FPS_30:       return 30;
        case FPSLimitMode::FPS_60:       return 60;
        case FPSLimitMode::FPS_90:       return 90;
        case FPSLimitMode::FPS_120:      return 120;
        case FPSLimitMode::FPS_144:      return 144;
        case FPSLimitMode::FPS_165:      return 165;
        case FPSLimitMode::FPS_240:      return 240;
        case FPSLimitMode::Custom:       return m_customFPS;
        default:                         return 0;
    }
}

void FPSProfilerWindow::update(float totalFrameMs, float gpuFrameMs, int monitorHz, bool vsync) {
    m_monitorHz = monitorHz > 0 ? monitorHz : 60;
    m_vsync = vsync;

    // Screen FPS = 1000 / wall-clock frame time (includes vsync wait / limiter sleep)
    float currentFps = (totalFrameMs > 0.001f) ? 1000.0f / totalFrameMs : 0.0f;
    // GPU FPS = 1000 / GPU frame time (what the GPU could sustain uncapped)
    float realFps    = (gpuFrameMs > 0.001f)   ? 1000.0f / gpuFrameMs   : 0.0f;

    // EMA smoothing (alpha ~0.08 = stable display, minimal jitter)
    constexpr float ALPHA = 0.08f;
    m_smoothCurrentFps = m_smoothCurrentFps * (1.0f - ALPHA) + currentFps * ALPHA;
    m_smoothRealFps    = m_smoothRealFps    * (1.0f - ALPHA) + realFps    * ALPHA;

    // Push into ring buffer
    m_currentFpsHistory[m_historyIdx] = m_smoothCurrentFps;
    m_realFpsHistory[m_historyIdx]    = m_smoothRealFps;
    m_historyIdx = (m_historyIdx + 1) % HISTORY_SIZE;
    if (m_historyIdx == 0) m_historyFull = true;
}

void FPSProfilerWindow::render() {
    if (shouldRenderEmbedded()) {
        renderContentInternal();
        return;
    }
    if (!beginWindow(ImGuiWindowFlags_NoTitleBar)) {
        endWindow();
        return;
    }
    renderContentInternal();
    endWindow();
}

void FPSProfilerWindow::renderContent() {
    renderContentInternal();
}

void FPSProfilerWindow::renderContentInternal() {
    char buf[80];

    // Main FPS: actual on-screen frame rate (measured frame-to-frame, includes limiter sleep)
    int target = getTargetFPS();
    float fpsRef = (target > 0) ? static_cast<float>(target) : static_cast<float>(m_monitorHz);
    ImVec4 fpsCol;
    if (m_smoothCurrentFps >= fpsRef * 0.95f)
        fpsCol = ImVec4(0.4f, 1.0f, 0.4f, 1.0f);   // green: on target
    else if (m_smoothCurrentFps >= fpsRef * 0.8f)
        fpsCol = ImVec4(1.0f, 0.8f, 0.3f, 1.0f);   // yellow: slightly below
    else
        fpsCol = ImVec4(1.0f, 0.4f, 0.4f, 1.0f);   // red: significantly below
    snprintf(buf, sizeof(buf), "Screen: %3.0f FPS", m_smoothCurrentFps);
    ImGui::TextColored(fpsCol, "%s", buf);

    // Secondary info line: monitor Hz + GPU-only capacity
    ImGui::SameLine();
    snprintf(buf, sizeof(buf), "  %d Hz  GPU cap: %.0f FPS", m_monitorHz, m_smoothRealFps);
    ImGui::TextDisabled("%s", buf);

    // --- FPS Limiter Mode Selector ---
    ImGui::Separator();
    {
        static const char* modeLabels[] = {
            "Unlimited",
            "VSync",
            "Match Monitor",
            "30 FPS",
            "60 FPS",
            "90 FPS",
            "120 FPS",
            "144 FPS",
            "165 FPS",
            "240 FPS",
            "Custom"
        };
        int modeIdx = static_cast<int>(m_limitMode);
        ImGui::SetNextItemWidth(130);
        if (ImGui::Combo("FPS Limit", &modeIdx, modeLabels, static_cast<int>(FPSLimitMode::COUNT))) {
            m_limitMode = static_cast<FPSLimitMode>(modeIdx);
        }
        // Show custom input when Custom is selected
        if (m_limitMode == FPSLimitMode::Custom) {
            ImGui::SameLine();
            ImGui::SetNextItemWidth(50);
            ImGui::InputInt("##customfps", &m_customFPS, 0, 0);
            if (m_customFPS < 10) m_customFPS = 10;
            if (m_customFPS > 1000) m_customFPS = 1000;
        }
        // Show effective target
        int target = getTargetFPS();
        if (target > 0) {
            ImGui::SameLine();
            ImGui::TextDisabled("(%d)", target);
        }
    }

    // --- Mini scrolling graph ---
    // Compute visible range from history
    size_t count = m_historyFull ? HISTORY_SIZE : m_historyIdx;
    if (count < 2) return;

    // Build contiguous plot buffer (oldest → newest)
    float plotCurrent[HISTORY_SIZE];
    float plotReal[HISTORY_SIZE];
    float lo = 1e9f, hi = 0.0f;
    for (size_t i = 0; i < count; ++i) {
        size_t idx = m_historyFull ? (m_historyIdx + i) % HISTORY_SIZE : i;
        plotCurrent[i] = m_currentFpsHistory[idx];
        plotReal[i]    = m_realFpsHistory[idx];
        float mx = std::max(plotCurrent[i], plotReal[i]);
        float mn = std::min(plotCurrent[i], plotReal[i]);
        if (mx > hi) hi = mx;
        if (mn < lo) lo = mn;
    }

    // Smooth graph scale (avoid flicker)
    float targetMin = std::max(0.0f, std::floor((lo - 5.0f) / 10.0f) * 10.0f);
    float targetMax = std::ceil((hi + 10.0f) / 10.0f) * 10.0f;
    m_graphMin = m_graphMin * 0.95f + targetMin * 0.05f;
    m_graphMax = m_graphMax * 0.95f + targetMax * 0.05f;

    // Draw graph at compact size
    ImVec2 graphSize(180, 40);
    char overlay[32];
    snprintf(overlay, sizeof(overlay), "%.0f", m_smoothRealFps);

    // Real FPS line (blue) overlaid on current FPS line (green)
    ImGui::PushStyleColor(ImGuiCol_PlotLines, ImVec4(0.4f, 1.0f, 0.4f, 0.6f));
    ImGui::PlotLines("##fpsCurrent", plotCurrent, static_cast<int>(count), 0,
                     nullptr, m_graphMin, m_graphMax, graphSize);
    ImGui::PopStyleColor();

    // Overlay real FPS on same area
    ImVec2 cursorBefore = ImGui::GetCursorPos();
    ImGui::SetCursorPos(ImVec2(cursorBefore.x, cursorBefore.y - graphSize.y - ImGui::GetStyle().ItemSpacing.y));
    ImGui::PushStyleColor(ImGuiCol_PlotLines, ImVec4(0.5f, 0.8f, 1.0f, 0.9f));
    ImGui::PushStyleColor(ImGuiCol_FrameBg, ImVec4(0, 0, 0, 0)); // transparent bg for overlay
    ImGui::PlotLines("##fpsReal", plotReal, static_cast<int>(count), 0,
                     overlay, m_graphMin, m_graphMax, graphSize);
    ImGui::PopStyleColor(2);
}

````

## src\ui\debug_menu\profiling\DebugControlPanel.cpp

Description: No CC-DESC found.

````cpp
#include "ui/debug_menu/profiling/DebugControlPanel.h"
#include "ui/style/EngineTheme.h"
#include "ui/ImGuiAutoSize.h"
#include <algorithm>

DebugControlPanel::DebugControlPanel() {
    // Default styling already set in header
}

void DebugControlPanel::registerWindow(const std::string& name, DebugWindowBase* window, const std::string& icon) {
    // Check if already registered
    if (findWindow(name) != nullptr) {
        return;
    }
    
    WindowEntry entry;
    entry.name = name;
    entry.icon = icon;
    entry.window = window;
    entry.isPoppedOut = false;
    entry.isSelected = m_windows.empty(); // First window is selected by default
    
    // Hide the original window since we'll manage rendering
    if (window) {
        window->setVisible(false);
    }
    
    m_windows.push_back(entry);
    
    if (entry.isSelected) {
        m_selectedIndex = static_cast<int>(m_windows.size()) - 1;
    }
}

void DebugControlPanel::registerCustomWindow(const std::string& name, std::function<void()> renderFunc, const std::string& icon) {
    if (findWindow(name) != nullptr) {
        return;
    }
    
    WindowEntry entry;
    entry.name = name;
    entry.icon = icon;
    entry.window = nullptr;
    entry.renderContent = renderFunc;
    entry.isPoppedOut = false;
    entry.isSelected = m_windows.empty();
    
    m_windows.push_back(entry);
    
    if (entry.isSelected) {
        m_selectedIndex = static_cast<int>(m_windows.size()) - 1;
    }
}

void DebugControlPanel::unregisterWindow(const std::string& name) {
    auto it = std::find_if(m_windows.begin(), m_windows.end(),
        [&name](const WindowEntry& e) { return e.name == name; });
    
    if (it != m_windows.end()) {
        // Restore original visibility if it was a DebugWindowBase
        if (it->window) {
            it->window->setVisible(true);
        }
        m_windows.erase(it);
        
        // Adjust selected index
        if (m_selectedIndex >= static_cast<int>(m_windows.size())) {
            m_selectedIndex = std::max(0, static_cast<int>(m_windows.size()) - 1);
        }
    }
}

void DebugControlPanel::selectTab(const std::string& name) {
    for (size_t i = 0; i < m_windows.size(); ++i) {
        if (m_windows[i].name == name) {
            m_selectedIndex = static_cast<int>(i);
            m_windows[i].isSelected = true;
        } else {
            m_windows[i].isSelected = false;
        }
    }
}

void DebugControlPanel::popOutWindow(const std::string& name) {
    WindowEntry* entry = findWindow(name);
    if (entry && !entry->isPoppedOut) {
        entry->isPoppedOut = true;
    }
}

void DebugControlPanel::dockWindow(const std::string& name) {
    WindowEntry* entry = findWindow(name);
    if (entry && entry->isPoppedOut) {
        entry->isPoppedOut = false;
        // Select this tab when docking back
        selectTab(name);
    }
}

bool DebugControlPanel::hasFloatingWindows() const {
    for (const auto& entry : m_windows) {
        if (entry.isPoppedOut) return true;
    }
    return false;
}

DebugControlPanel::WindowEntry* DebugControlPanel::findWindow(const std::string& name) {
    auto it = std::find_if(m_windows.begin(), m_windows.end(),
        [&name](const WindowEntry& e) { return e.name == name; });
    return (it != m_windows.end()) ? &(*it) : nullptr;
}

void DebugControlPanel::render() {
    if (!m_isVisible && !hasFloatingWindows()) {
        return;
    }
    
    // Render the main control panel (if visible)
    if (m_isVisible) {
        renderControlPanel();
    }
    
    // Render any popped-out floating windows
    for (auto& entry : m_windows) {
        if (entry.isPoppedOut) {
            renderFloatingWindow(entry);
        }
    }
}

void DebugControlPanel::renderControlPanel() {
    // Position upper-left
    ImGuiAutoSize::positionUpperLeft(10.0f);
    
    ImGui::SetNextWindowBgAlpha(EngineTheme::kPanelAlpha);
    
    // Use auto-sizing with constraints
    ImGui::SetNextWindowSizeConstraints(ImVec2(350, 200), ImVec2(900, 800));
    
    ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoSavedSettings 
                                  | ImGuiWindowFlags_NoFocusOnAppearing
                                  | ImGuiWindowFlags_AlwaysAutoResize;
    
    const bool windowOpen = ImGui::Begin("Debug Control Panel", &m_isVisible, window_flags);
    DebugWindowScrollLock::restore("Debug Control Panel");
    if (windowOpen) {
        // ═══════════════════════════════════════════════════════════════════════
        // TOP BAR - "Menus" dropdown + "Tools" button + Pop Out
        // ═══════════════════════════════════════════════════════════════════════
        
        ImGui::BeginGroup();
        
        // Count non-popped-out windows for display
        int visibleTabs = 0;
        for (const auto& entry : m_windows) {
            if (!entry.isPoppedOut) visibleTabs++;
        }
        
        // --- "Menus" dropdown combo ---
        {
            // Build current selection label
            std::string currentLabel = "Select...";
            if (m_selectedIndex >= 0 && m_selectedIndex < static_cast<int>(m_windows.size()) 
                && !m_windows[m_selectedIndex].isPoppedOut) {
                auto& sel = m_windows[m_selectedIndex];
                currentLabel = sel.icon.empty() ? sel.name : (sel.icon + " " + sel.name);
            }
            
            ImGui::SetNextItemWidth(200.0f);
            if (ImGui::BeginCombo("##MenusDropdown", currentLabel.c_str())) {
                for (size_t i = 0; i < m_windows.size(); ++i) {
                    auto& entry = m_windows[i];
                    if (entry.isPoppedOut) continue;
                    
                    std::string label = entry.icon.empty() ? entry.name : (entry.icon + " " + entry.name);
                    bool isSelected = (static_cast<int>(i) == m_selectedIndex);
                    
                    if (ImGui::Selectable(label.c_str(), isSelected)) {
                        m_selectedIndex = static_cast<int>(i);
                        m_showingTool = false;  // Switch back to menu content
                    }
                    if (isSelected) {
                        ImGui::SetItemDefaultFocus();
                    }
                }
                ImGui::EndCombo();
            }
        }
        
        // --- "Tools" button ---
        ImGui::SameLine();
        {
            bool toolActive = m_showingTool;
            if (toolActive) {
                ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.2f, 0.7f, 0.9f, 0.80f));
                ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.8f, 1.0f, 0.90f));
            } else {
                ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.2f, 0.5f, 0.7f, 0.80f));
                ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.6f, 0.8f, 0.90f));
            }
            
            if (ImGui::Button("\xF0\x9F\x94\xA8 Tools")) {
                m_showingTool = !m_showingTool;
            }
            ImGui::PopStyleColor(2);
        }
        
        // --- Pop Out button ---
        if (!m_showingTool && m_selectedIndex >= 0 && m_selectedIndex < static_cast<int>(m_windows.size())) {
            auto& selectedEntry = m_windows[m_selectedIndex];
            if (!selectedEntry.isPoppedOut) {
                ImGui::SameLine();
                
                ImGui::PushStyleColor(ImGuiCol_Button, m_popOutButtonColor);
                ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.9f, 0.7f, 0.3f, 0.90f));
                
                if (ImGui::Button("Pop Out")) {
                    selectedEntry.isPoppedOut = true;
                    
                    // Select next available tab
                    for (size_t j = 0; j < m_windows.size(); ++j) {
                        if (!m_windows[j].isPoppedOut && j != static_cast<size_t>(m_selectedIndex)) {
                            m_selectedIndex = static_cast<int>(j);
                            break;
                        }
                    }
                }
                
                ImGui::PopStyleColor(2);
                
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Open in separate window");
                }
            }
        }
        
        ImGui::EndGroup();
        
        ImGui::Separator();
        
        // ═══════════════════════════════════════════════════════════════════════
        // CONTENT AREA - Render selected window or tools content
        // ═══════════════════════════════════════════════════════════════════════
        
        if (m_showingTool) {
            // Render tools content
            ImGuiWindowFlags contentFlags = ImGuiWindowFlags_HorizontalScrollbar;
            const bool toolsOpen = ImGui::BeginChild("##control_panel_tools_scroll", ImVec2(0.0f, 0.0f), false, contentFlags);
            DebugWindowScrollLock::restore("Debug Control Panel/Tools");
            if (toolsOpen) {
                if (m_toolRenderFunc) {
                    m_toolRenderFunc();
                } else {
                    ImGui::TextDisabled("No tools registered");
                }
            }
            DebugWindowScrollLock::capture("Debug Control Panel/Tools");
            ImGui::EndChild();
        } else if (m_selectedIndex >= 0 && m_selectedIndex < static_cast<int>(m_windows.size())) {
            auto& selectedEntry = m_windows[m_selectedIndex];
            if (!selectedEntry.isPoppedOut) {
                // Render content directly for proper auto-sizing
                ImGuiWindowFlags contentFlags = ImGuiWindowFlags_HorizontalScrollbar;
                const bool contentOpen = ImGui::BeginChild("##control_panel_content_scroll", ImVec2(0.0f, 0.0f), false, contentFlags);
                DebugWindowScrollLock::restore("Debug Control Panel/Content");
                if (contentOpen) {
                    renderWindowContent(selectedEntry);
                }
                DebugWindowScrollLock::capture("Debug Control Panel/Content");
                ImGui::EndChild();
            } else {
                // Selected window is popped out - show placeholder
                ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.6f, 1.0f), 
                    "'%s' is in a separate window", selectedEntry.name.c_str());
                
                if (ImGui::Button("Bring Back")) {
                    selectedEntry.isPoppedOut = false;
                }
            }
        } else if (visibleTabs == 0) {
            ImGui::TextColored(ImVec4(0.6f, 0.6f, 0.6f, 1.0f), 
                "All windows are popped out");
        }
    }
    DebugWindowScrollLock::capture("Debug Control Panel");
    ImGui::End();
}

void DebugControlPanel::renderFloatingWindow(WindowEntry& entry) {
    if (!entry.isPoppedOut) return;
    
    std::string windowTitle = entry.icon.empty() 
        ? entry.name + " (Floating)" 
        : entry.icon + " " + entry.name + " (Floating)";
    
    ImGui::SetNextWindowBgAlpha(EngineTheme::kPanelAlpha);
    
    // Special handling for Minimap - larger constraints and position in upper-right
    if (entry.name == "Minimap") {
        ImGuiIO& io = ImGui::GetIO();
        float padding = 10.0f;
        // Position at right edge using pivot (1.0, 0.0) = top-right corner of window
        ImGui::SetNextWindowPos(ImVec2(io.DisplaySize.x - padding, padding), ImGuiCond_FirstUseEver, ImVec2(1.0f, 0.0f));
        ImGui::SetNextWindowSizeConstraints(ImVec2(300, 300), ImVec2(1200, 1200));
    } else {
        ImGui::SetNextWindowSizeConstraints(ImVec2(300, 150), ImVec2(1000, 900));
    }
    
    ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoSavedSettings 
                                  | ImGuiWindowFlags_NoFocusOnAppearing
                                  | ImGuiWindowFlags_AlwaysAutoResize;
    
    bool windowOpen = true;
    const bool beginOpen = ImGui::Begin(windowTitle.c_str(), &windowOpen, window_flags);
    DebugWindowScrollLock::restore(windowTitle.c_str());
    if (beginOpen) {
        // ═══════════════════════════════════════════════════════════════════════
        // DOCK BUTTON - To return to control panel
        // ═══════════════════════════════════════════════════════════════════════
        
        ImGui::PushStyleColor(ImGuiCol_Button, m_dockButtonColor);
        ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(0.3f, 0.8f, 0.5f, 0.90f));
        
        if (ImGui::Button("Dock")) {
            entry.isPoppedOut = false;
            selectTab(entry.name);
        }
        
        ImGui::PopStyleColor(2);
        
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("Return to Control Panel");
        }
        
        ImGui::SameLine();
        ImGui::TextColored(ImVec4(0.7f, 0.7f, 0.7f, 1.0f), "| %s", entry.name.c_str());
        
        ImGui::Separator();
        
        // ═══════════════════════════════════════════════════════════════════════
        // CONTENT - Render directly (no BeginChild) for auto-sizing
        // ═══════════════════════════════════════════════════════════════════════
        ImGuiWindowFlags contentFlags = ImGuiWindowFlags_HorizontalScrollbar;
        const bool floatingContentOpen = ImGui::BeginChild("##floating_content_scroll", ImVec2(0.0f, 0.0f), false, contentFlags);
        const std::string floatingContentKey = windowTitle + "/Content";
        DebugWindowScrollLock::restore(floatingContentKey.c_str());
        if (floatingContentOpen) {
            renderWindowContent(entry);
        }
        DebugWindowScrollLock::capture(floatingContentKey.c_str());
        ImGui::EndChild();
    }
    DebugWindowScrollLock::capture(windowTitle.c_str());
    ImGui::End();
    
    // If user closed the floating window via X button, dock it back
    if (!windowOpen) {
        entry.isPoppedOut = false;
    }
}

void DebugControlPanel::renderWindowContent(WindowEntry& entry) {
    if (entry.renderContent) {
        // Custom render function provided
        entry.renderContent();
    } else if (entry.window) {
        // Use the window's renderContent() method for embedded rendering
        entry.window->renderContent();
    } else {
        ImGui::TextColored(ImVec4(0.8f, 0.4f, 0.4f, 1.0f), "No content renderer defined");
    }
}

````

## src\ui\debug_menu\world\ChunkVramWindow.cpp

Description: No CC-DESC found.

````cpp
#include "ui/debug_menu/world/ChunkVramWindow.h"
#include "ui/style/EngineTheme.h"
#include "world/World.h"
#include "world/chunks/core/Chunk.h"
#include "rendering/common/Mesh.h"
#include <imgui.h>
#include <algorithm>
#include <array>
#include <cstdarg>
#include <cctype>
#include <cstring>
#include <cstdio>
#include <set>
#include <string>
#include <unordered_map>

#include "ChunkVramWindow_Internal.h"

// NOTE: anonymous-namespace helpers (appendFormat, chunkMeshModeName,
//       isTrueLoadEntry, visualEntryLabel, adaptiveSavedPercent,
//       appendStageAttributionLine, appendAdaptiveLine,
//       appendVisualHistoryEntryText, appendVisualErrorEntryText,
//       tryParseChunkCoordText, normalizeChunkSearchText, chunkMatchesSearch)
//       live in ChunkVramWindow_Internal.h.
// NOTE: text-builder methods (buildChunkDiagnosticsText, buildChunkVisualHistoryText,
//       buildChunkTimelineText, buildChunkFlowSummary) live in ChunkVramWindow_Text.cpp.

ChunkVramWindow::ChunkVramWindow()
    : DebugWindowBase("Chunk VRAM Usage")
{
}

void ChunkVramWindow::render() {
    // This version does nothing - use renderWithRegistry instead
}

void ChunkVramWindow::renderContent() {
    if (m_currentRegistry) {
        renderContentInternal();
    } else {
        ImGui::Text("Registry not available");
    }
}

void ChunkVramWindow::renderWithRegistry(const entt::registry& registry) {
    // Store registry for renderContent (needed even when not visible, for control panel)
    m_currentRegistry = &registry;
    
    // Always render detail window if a chunk is selected (even when main window is in control panel)
    if (m_selectedChunkIndex >= 0) {
        renderChunkDetailWindow(m_selectedChunkIndex, m_selectedChunkInfo);
    }
    
    // If window is managed by control panel (not visible), don't render standalone window
    if (!isVisible()) return;
    
    if (isEmbedded()) {
        // Embedded in control panel - just render content
        renderContentInternal();
        return;
    }
    
    ImGui::SetNextWindowPos(ImVec2(450, 10), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowSize(ImVec2(500, 500), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowBgAlpha(EngineTheme::kPanelAlpha);
    
    ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoFocusOnAppearing;
    
    const bool windowOpen = ImGui::Begin("Chunk VRAM Usage", nullptr, window_flags);
    DebugWindowScrollLock::restore("Chunk VRAM Usage");
    if (windowOpen) {
        renderContentInternal();
    }
    DebugWindowScrollLock::capture("Chunk VRAM Usage");
    ImGui::End();
}

ChunkVramWindow::ChunkVramInfo ChunkVramWindow::buildChunkVramInfo(const ChunkEntry& chunk) const {
    int32_t lodLevel = 0;
    if (m_currentRegistry) {
        auto chunkView = m_currentRegistry->view<const Chunk>();
        if (chunkView.contains(chunk.entity)) {
            lodLevel = chunkView.get<Chunk>(chunk.entity).lodLevel;
        }

        auto meshView = m_currentRegistry->view<const MeshHandle>();
        if (meshView.contains(chunk.entity)) {
            const auto& mesh = meshView.get<MeshHandle>(chunk.entity);
            return ChunkVramInfo(
                chunk.coord,
                chunk.vramBytes,
                mesh.getTotalVertexCount(),
                mesh.getTotalIndexCount(),
                mesh.getTotalVertexBytes(),
                mesh.getTotalIndexBytes(),
                lodLevel,
                chunk.entity);
        }
    }

    return ChunkVramInfo(
        chunk.coord,
        chunk.vramBytes,
        static_cast<uint32_t>(chunk.vramBytes > 0 ? (chunk.vramBytes * 2 / 3) / 4 : 0),
        0,
        0,
        0,
        lodLevel,
        chunk.entity);
}

bool ChunkVramWindow::selectChunkByCoord(const glm::ivec3& coord) {
    for (size_t i = 0; i < m_cachedChunks.size(); ++i) {
        const auto& chunk = m_cachedChunks[i];
        if (chunk.coord != coord) {
            continue;
        }

        m_selectedChunkIndex = static_cast<int>(i) + 1;
        m_selectedChunkInfo = buildChunkVramInfo(chunk);
        char status[128];
        std::snprintf(
            status,
            sizeof(status),
            "Found chunk #%d at (%d, %d, %d)",
            m_selectedChunkIndex,
            coord.x,
            coord.y,
            coord.z);
        m_chunkCoordSearchStatus = status;
        return true;
    }

    char status[128];
    std::snprintf(
        status,
        sizeof(status),
        "No loaded chunk at (%d, %d, %d)",
        coord.x,
        coord.y,
        coord.z);
    m_chunkCoordSearchStatus = status;
    return false;
}


void ChunkVramWindow::renderContentInternal() {
    if (!m_currentRegistry) return;
    const entt::registry& registry = *m_currentRegistry;
    
    // Chunk statistics at the top
    ImGui::Text("===== CHUNK STATISTICS =====");
    ImGui::Text("Total Chunks: %llu", m_totalChunks);
    uint32_t wholeChunks = static_cast<uint32_t>(m_totalChunks) - m_splitChunks;
    ImGui::Text("  Split main meshes: %u | Whole: %u", m_splitChunks, wholeChunks);
    ImGui::Text("  Seam SubChunks: %u", m_seamChunks);
    ImGui::Text("Draw Calls: %u total", m_totalSubChunks);
    
    ImGui::Separator();
    ImGui::Text("===== PER-CHUNK VRAM =====");
    ImGui::Separator();
    
    // Throttled entity scan: only rebuild every VRAM_SCAN_INTERVAL frames
    // Avoids O(N) registry iteration + sort of ~26K chunks every frame
    if (++m_vramScanCounter >= VRAM_SCAN_INTERVAL || m_cachedChunks.empty()) {
        m_vramScanCounter = 0;
        m_cachedChunks.clear();
        m_cachedTotalVram = 0;
        
        auto view = registry.view<const ChunkCoord, const MeshHandle, const Chunk>();
        for (auto entity : view) {
            const auto& coord = view.get<ChunkCoord>(entity);
            const auto& mesh = view.get<MeshHandle>(entity);
            const auto& chunk = view.get<Chunk>(entity);
            
            if (mesh.isValid()) {
                uint64_t chunkVram = mesh.getTotalVramBytes();
                ChunkEntry entry;
                entry.entity = entity;
                entry.coord = glm::ivec3(coord.x, coord.y, coord.z);
                entry.vramBytes = chunkVram;
                entry.isVisible = chunk.isVisible;
                entry.subChunkCount = mesh.subChunkCount;
                entry.mainSubChunkCount = mesh.mainSubChunkCount;
                m_cachedChunks.push_back(entry);
                m_cachedTotalVram += chunkVram;
            }
        }
        
        // Sort by VRAM usage (largest first)
        std::sort(m_cachedChunks.begin(), m_cachedChunks.end(), [](const ChunkEntry& a, const ChunkEntry& b) {
            return a.vramBytes > b.vramBytes;
        });
    }
    
    // Use cached data for display
    const auto& chunks = m_cachedChunks;
    uint64_t totalVram = m_cachedTotalVram;
    
    // Display summary
    ImGui::Text("Total Chunks: %zu", chunks.size());
    ImGui::Text("Total VRAM: %.2f MB", totalVram / (1024.0 * 1024.0));
    
    if (!chunks.empty()) {
        ImGui::Text("Avg per Chunk: %.2f KB", (totalVram / chunks.size()) / 1024.0);
    }

    ImGui::Spacing();
    ImGui::Text("Chunk Search:");
    ImGui::SetNextItemWidth(180.0f);
    const bool coordEdited = ImGui::InputTextWithHint(
        "##chunkCoordSearch",
        "x,y,z  e.g. 79,0,11",
        m_chunkCoordSearch,
        sizeof(m_chunkCoordSearch));
    const std::string normalizedSearch = normalizeChunkSearchText(m_chunkCoordSearch);
    std::vector<size_t> filteredChunkRows;
    if (!normalizedSearch.empty()) {
        filteredChunkRows.reserve(chunks.size());
        for (size_t i = 0; i < chunks.size(); ++i) {
            if (chunkMatchesSearch(chunks[i].coord, m_chunkCoordSearch)) {
                filteredChunkRows.push_back(i);
            }
        }
    }

    glm::ivec3 parsedCoord(0);
    const bool hasExactCoord = tryParseChunkCoordText(m_chunkCoordSearch, parsedCoord);
    if (coordEdited || (!normalizedSearch.empty() && hasExactCoord)) {
        if (hasExactCoord) {
            selectChunkByCoord(parsedCoord);
        } else if (!normalizedSearch.empty()) {
            char status[128];
            std::snprintf(
                status,
                sizeof(status),
                "Showing %zu matching loaded chunks",
                filteredChunkRows.size());
            m_chunkCoordSearchStatus = status;
        } else {
            m_chunkCoordSearchStatus.clear();
        }
    } else if (normalizedSearch.empty()) {
        m_chunkCoordSearchStatus.clear();
    }
    ImGui::SameLine();
    ImGui::SetNextItemWidth(70.0f);
    ImGui::InputInt("##chunkTimelineCopyCount", &m_chunkTimelineCopyCount, 1, 8);
    m_chunkTimelineCopyCount = std::max(m_chunkTimelineCopyCount, 1);
    ImGui::SameLine();
    if (ImGui::Button("Copy Selected History")) {
        if (m_selectedChunkIndex >= 0) {
            const std::string text = buildChunkTimelineText(
                m_selectedChunkInfo.coord,
                static_cast<size_t>(m_chunkTimelineCopyCount));
            ImGui::SetClipboardText(text.empty() ? "No related chunk actions recorded.\n" : text.c_str());
        } else {
            ImGui::SetClipboardText("No chunk selected.\n");
        }
    }
    if (!m_chunkCoordSearchStatus.empty()) {
        ImGui::TextDisabled("%s", m_chunkCoordSearchStatus.c_str());
    } else if (!normalizedSearch.empty()) {
        ImGui::TextDisabled("Showing %zu matching loaded chunks", filteredChunkRows.size());
    }
    
    ImGui::Separator();

    const bool showVisualHistory = (m_world != nullptr && m_world->getChunkVisualHistory().count > 0);
    const float historyHeight = showVisualHistory ? 165.0f : 0.0f;

    // Display chunk list in scrollable region with ListClipper
    // Without ListClipper, 25600 chunks × ~20 ImGui calls = 500K+ widgets per frame
    const float reservedBottomHeight = historyHeight + (showVisualHistory ? 16.0f : 0.0f);
    const float listHeight = std::max(120.0f, ImGui::GetContentRegionAvail().y - reservedBottomHeight);
    const bool listOpen = ImGui::BeginChild("ChunkList", ImVec2(0, listHeight), true);
    DebugWindowScrollLock::restore("Chunk VRAM Usage/ChunkList");
    if (listOpen) {
        const bool useFilteredRows = !normalizedSearch.empty();
        const int rowCount = useFilteredRows
            ? static_cast<int>(filteredChunkRows.size())
            : static_cast<int>(chunks.size());
        ImGuiListClipper clipper;
        clipper.Begin(rowCount);
        while (clipper.Step()) {
            for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++) {
                const size_t chunkRow = useFilteredRows
                    ? filteredChunkRows[static_cast<size_t>(row)]
                    : static_cast<size_t>(row);
                const auto& chunk = chunks[chunkRow];
                const int chunkNumber = static_cast<int>(chunkRow) + 1;
                const float kb = chunk.vramBytes / 1024.0f;

                ImGui::PushID(chunkNumber);

                if (ImGui::SmallButton("Copy")) {
                    const ChunkVramInfo info = buildChunkVramInfo(chunk);
                    const std::string text = buildChunkDiagnosticsText(chunkNumber, info);
                    ImGui::SetClipboardText(text.c_str());
                }
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Copy this chunk's diagnostics");
                }
                ImGui::SameLine();

                if (m_world) {
                    const char* buttonLabel = chunk.isVisible ? "Hide" : "Show";
                    const ImVec4 buttonColor = chunk.isVisible ? ImVec4(0.8f, 0.3f, 0.3f, 1.0f) : ImVec4(0.3f, 0.8f, 0.3f, 1.0f);
                    ImGui::PushStyleColor(ImGuiCol_Button, buttonColor);
                    if (ImGui::SmallButton(buttonLabel)) {
                        m_world->setChunkVisible(chunk.entity, !chunk.isVisible);
                    }
                    ImGui::PopStyleColor();
                    ImGui::SameLine();
                }

                const uint8_t subChunkCount = chunk.subChunkCount;
                const uint8_t mainSubChunkCount = chunk.mainSubChunkCount;
                const char* visMarker = chunk.isVisible ? "" : "[HIDDEN] ";
                if (mainSubChunkCount > 1) {
                    ImGui::TextColored(ImVec4(1.0f, 0.9f, 0.2f, 1.0f), "%s#%3d %dM+%dS (%3d,%3d,%3d): %6.2f KB",
                        visMarker, chunkNumber, static_cast<int>(mainSubChunkCount), static_cast<int>(subChunkCount - mainSubChunkCount),
                        chunk.coord.x, chunk.coord.y, chunk.coord.z, kb);
                } else if (subChunkCount > 1) {
                    ImGui::TextColored(ImVec4(0.6f, 0.9f, 1.0f, 1.0f), "%s#%3d 1M+%dS (%3d,%3d,%3d): %6.2f KB",
                        visMarker, chunkNumber, static_cast<int>(subChunkCount - 1),
                        chunk.coord.x, chunk.coord.y, chunk.coord.z, kb);
                } else {
                    ImGui::Text("%s#%3d (%3d,%3d,%3d): %6.2f KB",
                        visMarker, chunkNumber, chunk.coord.x, chunk.coord.y, chunk.coord.z, kb);
                }

                ImGui::SameLine();
                ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(1.0f, 0.9f, 0.2f, 1.0f));
                ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(1.0f, 1.0f, 0.4f, 1.0f));
                ImGui::PushStyleColor(ImGuiCol_ButtonActive, ImVec4(0.9f, 0.8f, 0.1f, 1.0f));
                ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0.0f, 0.0f, 0.0f, 1.0f));
                if (ImGui::SmallButton("L")) {
                    if (m_addLightCallback) {
                        m_addLightCallback(chunk.entity, chunk.coord);
                    }
                }
                ImGui::PopStyleColor(4);
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Add light above chunk terrain");
                }

                ImGui::SameLine();
                if (ImGui::SmallButton("?")) {
                    m_selectedChunkIndex = chunkNumber;
                    m_selectedChunkInfo = buildChunkVramInfo(chunk);
                }
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Click for detailed breakdown");
                }

                const std::string flowSummary = buildChunkFlowSummary(chunk.coord);
                if (!flowSummary.empty()) {
                    ImGui::SameLine();
                    ImGui::TextDisabled("%s", flowSummary.c_str());
                }

                ImGui::PopID();
            }
        }
    }
    DebugWindowScrollLock::capture("Chunk VRAM Usage/ChunkList");
    ImGui::EndChild();

    if (showVisualHistory) {
        const auto& history = m_world->getChunkVisualHistory();
        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Text("===== VISUAL UPDATE HISTORY =====");
        ImGui::Text("Recent chunk meshes as they became visible on screen");
        m_visualHistoryCopyCount = std::max(m_visualHistoryCopyCount, 1);
        ImGui::SetNextItemWidth(80.0f);
        ImGui::InputInt("##chunkVisualCopyCount", &m_visualHistoryCopyCount, 1, 16);
        m_visualHistoryCopyCount = std::max(m_visualHistoryCopyCount, 1);
        ImGui::SameLine();
        if (ImGui::Button("Copy Last")) {
            const std::string text = buildChunkVisualHistoryText(static_cast<size_t>(m_visualHistoryCopyCount));
            ImGui::SetClipboardText(text.c_str());
        }
        ImGui::SameLine();
        ImGui::TextDisabled("latest history entries");
        const bool historyOpen = ImGui::BeginChild("ChunkVisualHistory", ImVec2(0, historyHeight), true);
        DebugWindowScrollLock::restore("Chunk VRAM Usage/ChunkVisualHistory");

        if (historyOpen) {
            const size_t showCount = std::min<size_t>(history.count, 64);
            for (size_t i = 0; i < showCount; ++i) {
                const auto& entry = history.getFromEnd(i);
                const float latencyMs = entry.visibleMs;
                const ImVec4 col = (latencyMs < 16.0f) ? ImVec4(0.4f, 1.0f, 0.4f, 1.0f)
                               : (latencyMs < 100.0f) ? ImVec4(1.0f, 1.0f, 0.3f, 1.0f)
                                                      : ImVec4(1.0f, 0.35f, 0.3f, 1.0f);
                ImGui::PushID(static_cast<int>(entry.sequence));
                if (ImGui::SmallButton("Copy")) {
                    std::string text;
                    appendVisualHistoryEntryText(text, entry, /*includeCoord=*/true);
                    ImGui::SetClipboardText(text.c_str());
                }
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Copy this history entry");
                }
                ImGui::SameLine();
                if (ImGui::SmallButton("Chunk")) {
                    const std::string text = buildChunkTimelineText(
                        entry.chunkCoord,
                        static_cast<size_t>(m_chunkTimelineCopyCount));
                    ImGui::SetClipboardText(text.empty() ? "No related chunk actions recorded.\n" : text.c_str());
                    selectChunkByCoord(entry.chunkCoord);
                }
                if (ImGui::IsItemHovered()) {
                    ImGui::SetTooltip("Copy related actions for this chunk and open its details");
                }
                ImGui::SameLine();
                ImGui::TextColored(
                    col,
                    "#%llu [%s] (%3d,%3d,%3d) LOD%d  %6.1f ms shown  |  pipe %6.1f ms  |  %7.2f KB",
                    static_cast<unsigned long long>(entry.sequence),
                    visualEntryLabel(entry),
                    entry.chunkCoord.x, entry.chunkCoord.y, entry.chunkCoord.z,
                    entry.lodLevel,
                    entry.visibleMs,
                    entry.pipelineMs,
                    entry.vramBytes / 1024.0f);
                if (entry.fromEdit && (entry.waitDispatchMs > 0 || entry.meshMs > 0)) {
                    ImGui::SameLine();
                    ImGui::TextDisabled(" %s", entry.isFastMode ? "F" : "Q");
                    if (ImGui::IsItemHovered()) {
                        std::string tooltip;
                        appendFormat(
                            tooltip,
                            "apply->dispatch: %.1f ms\n"
                            "queue wait:      %.1f ms\n"
                            "mesh:            %.1f ms\n"
                            "  cache build:   %.1f ms\n"
                            "  greedy mesh:   %.1f ms\n"
                            "  post-process:  %.1f ms\n"
                            "drain wait:      %.1f ms\n"
                            "upload+finalize: %.1f ms\n"
                            "mode: %s\n"
                            "---\n"
                            "cache: %uK voxels (%uK solid)\n"
                            "faces: %u  |  Yrange: %d  |  dim: %d\n"
                            "adaptive: %s  |  leaves %u  split %u  depth %u\n"
                            "work: %lluK adapt / %lluK mono  (%.0f%% saved)\n"
                            "peak leaf: %uK vox  |  peak Y: %d\n"
                            "load: RD %d/%d (+%d)  thr %.0f\n"
                            "create/destroy: %u / %u\n"
                            "lodQ/pendingLOD: %u / %u\n"
                            "editQ/upQ/finQ: %u / %u / %u\n"
                            "in-flight requeues: %u\n"
                            "buffer pressure: %s\n"
                            "---\n"
                            "overlay: %u spheres, %u boxes, %u cyl, %u bricks",
                            entry.waitDispatchMs,
                            entry.waitJobMs,
                            entry.meshMs,
                            entry.cacheBuildMs,
                            entry.greedyMeshMs,
                            entry.postProcessMs,
                            entry.waitDrainMs,
                            entry.uploadMs,
                            entry.isFastMode ? "FAST (no dedup)" : "QUALITY",
                            entry.cacheVoxels / 1000,
                            entry.solidVoxels / 1000,
                            entry.facesEmitted,
                            entry.scanYRange,
                            entry.cacheDimXZ,
                            entry.adaptiveEnabled ? "on" : "off",
                            entry.adaptiveLeafRegions,
                            entry.adaptiveSplitRegions,
                            entry.adaptiveMaxDepth,
                            static_cast<unsigned long long>(entry.adaptiveWorkVoxels / 1000),
                            static_cast<unsigned long long>(entry.monolithicWorkVoxels / 1000),
                            adaptiveSavedPercent(entry),
                            entry.adaptivePeakRegionVoxels / 1000,
                            static_cast<int>(entry.adaptivePeakYRange),
                            entry.loadEffectiveRenderDist,
                            entry.loadBaseRenderDist,
                            entry.loadExtensionRings,
                            entry.loadMeasuredThroughput,
                            entry.loadPendingCreates,
                            entry.loadPendingDestroys,
                            entry.loadLodRemeshQueue,
                            entry.loadPendingLodRemeshes,
                            entry.loadEditRemeshPending,
                            entry.loadUploadQueue,
                            entry.loadFinalizeQueue,
                            entry.loadInFlightSkips,
                            entry.loadBufferPressure ? "yes" : "no",
                            entry.sphereFills,
                            entry.boxFills,
                            entry.cylinderFills,
                            entry.bricks);
                        ImGui::SetTooltip("%s", tooltip.c_str());
                    }
                }
                ImGui::PopID();
            }
        }
        DebugWindowScrollLock::capture("Chunk VRAM Usage/ChunkVisualHistory");
        ImGui::EndChild();
    }
}

void ChunkVramWindow::renderChunkDetailWindow(int chunkIndex, const ChunkVramInfo& chunkInfo) {
    ImGui::SetNextWindowPos(ImVec2(860, 10), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowSize(ImVec2(400, 450), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowBgAlpha(EngineTheme::kPanelAlpha);
    
    ImGuiWindowFlags window_flags = ImGuiWindowFlags_NoFocusOnAppearing;
    
    char windowTitle[128];
    snprintf(windowTitle, sizeof(windowTitle), "Chunk #%d Details", chunkIndex);
    
    bool isOpen = true;
    const bool windowOpen = ImGui::Begin(windowTitle, &isOpen, window_flags);
    DebugWindowScrollLock::restore(windowTitle);
    if (windowOpen) {
        ImGui::Text("===== CHUNK BREAKDOWN =====");
        ImGui::Separator();
        
        ImGui::Text("Coordinates: (%d, %d, %d)", chunkInfo.coord.x, chunkInfo.coord.y, chunkInfo.coord.z);
        
        // LOD Level info
        const char* lodNames[] = {"LOD 0 (Full Detail)", "LOD 1 (Half)", "LOD 2 (Quarter)", "LOD 3 (Low)"};
        int lodIndex = std::min(std::max(chunkInfo.lodLevel, 0), 3);
        ImGui::TextColored(ImVec4(0.4f, 0.8f, 1.0f, 1.0f), "LOD Level: %d - %s", chunkInfo.lodLevel, lodNames[lodIndex]);
        
        // SubChunk info - check if chunk is split into multiple draw calls
        if (m_currentRegistry && chunkInfo.entity != entt::null) {
            auto meshView = m_currentRegistry->view<const MeshHandle>();
            if (meshView.contains(chunkInfo.entity)) {
                const auto& mesh = meshView.get<MeshHandle>(chunkInfo.entity);
                uint8_t seamCount = mesh.subChunkCount - mesh.mainSubChunkCount;
                
                if (mesh.mainSubChunkCount > 1) {
                    // Multiple main SubChunks = was split due to vertex limit
                    ImGui::TextColored(ImVec4(1.0f, 0.9f, 0.2f, 1.0f), 
                        "SubChunks: %d main (split) + %d seam", 
                        (int)mesh.mainSubChunkCount, (int)seamCount);
                } else if (seamCount > 0) {
                    // Single main + seams
                    ImGui::TextColored(ImVec4(0.6f, 0.9f, 1.0f, 1.0f), 
                        "SubChunks: 1 main + %d seam (LOD boundary)", 
                        (int)seamCount);
                } else {
                    // Single draw call, no seams
                    ImGui::TextColored(ImVec4(0.3f, 1.0f, 0.3f, 1.0f), "SubChunks: 1 (single draw call)");
                }
                
                // Show individual SubChunk details if more than one
                if (mesh.subChunkCount > 1) {
                    ImGui::Indent();
                    for (uint8_t s = 0; s < mesh.subChunkCount; ++s) {
                        const auto& sc = mesh.subChunks[s];
                        const char* type = (s < mesh.mainSubChunkCount) ? "Main" : "Seam";
                        ImGui::Text("  %s %d: %u indices, offset %d", 
                                   type, (int)(s+1), sc.indexCount, sc.vertexOffset);
                    }
                    ImGui::Unindent();
                }
            }
        }
        
        ImGui::Text("Total VRAM: %.2f KB (%.2f MB)", 
                    chunkInfo.vramBytes / 1024.0f, 
                    chunkInfo.vramBytes / (1024.0f * 1024.0f));
        
        ImGui::Separator();
        
        // Seam mesh info (if entity is valid and we have registry)
        if (m_currentRegistry && chunkInfo.entity != entt::null) {
            auto seamView = m_currentRegistry->view<const SeamMeshHandles>();
            if (seamView.contains(chunkInfo.entity)) {
                const auto& seams = seamView.get<SeamMeshHandles>(chunkInfo.entity);
                
                ImGui::Text("SEAM MESHES (LOD Boundary):");
                ImGui::Indent();
                
                const char* edgeNames[] = {"West (-X)", "East (+X)", "South (-Z)", "North (+Z)"};
                uint64_t totalSeamVram = 0;
                int activeSeams = 0;
                
                for (int i = 0; i < 4; ++i) {
                    if (seams.edges[i].isValid()) {
                        uint64_t seamSize = seams.edges[i].vb.size + seams.edges[i].ib.size;
                        totalSeamVram += seamSize;
                        activeSeams++;
                        ImGui::TextColored(ImVec4(0.3f, 1.0f, 0.3f, 1.0f), 
                            "%s: %.2f KB (%u verts, %u idx)", 
                            edgeNames[i], seamSize / 1024.0f,
                            static_cast<uint32_t>(seams.edges[i].vb.size / 4),
                            seams.edges[i].getTotalIndexCount());
                    } else {
                        ImGui::TextColored(ImVec4(0.5f, 0.5f, 0.5f, 1.0f), "%s: (none)", edgeNames[i]);
                    }
                }
                
                if (activeSeams > 0) {
                    ImGui::Spacing();
                    ImGui::Text("Total Seam VRAM: %.2f KB", totalSeamVram / 1024.0f);
                }
                
                ImGui::Unindent();
                ImGui::Separator();
            }
        }
        
        ImGui::Text("MEMORY BREAKDOWN:");
        ImGui::Spacing();
        
        // Vertex buffer breakdown (packed vertices: 4 bytes each)
        ImGui::Text("Vertex Buffer:");
        ImGui::Indent();
        ImGui::Text("Size: %.2f KB", chunkInfo.vertexBufferBytes / 1024.0f);
        ImGui::Text("Vertices: %u", chunkInfo.vertexCount);
        ImGui::Text("Bytes per Vertex: 4 (packed format)");
        ImGui::Text("Format: X(8)|Y(10)|Z(8)|face(3)|AO(3) bits");
        ImGui::Unindent();
        
        ImGui::Spacing();
        
        // Index buffer breakdown (16-bit indices: 2 bytes each)
        uint32_t bytesPerIndex = chunkInfo.indexCount > 0 ? 
            static_cast<uint32_t>(chunkInfo.indexBufferBytes / chunkInfo.indexCount) : 2;
        ImGui::Text("Index Buffer:");
        ImGui::Indent();
        ImGui::Text("Size: %.2f KB", chunkInfo.indexBufferBytes / 1024.0f);
        ImGui::Text("Indices: %u", chunkInfo.indexCount);
        ImGui::Text("Bytes per Index: %u (%s)", bytesPerIndex, 
                    bytesPerIndex == 2 ? "uint16_t" : "uint32_t");
        ImGui::Text("Triangles: %u", chunkInfo.indexCount / 3);
        ImGui::Text("Quads (approx): %u", chunkInfo.indexCount / 6);
        ImGui::Unindent();
        
        ImGui::Spacing();
        ImGui::Separator();
        
        // Memory efficiency analysis
        ImGui::Text("ANALYSIS:");
        ImGui::Spacing();
        
        float vbPercent = chunkInfo.vramBytes > 0 ? (chunkInfo.vertexBufferBytes * 100.0f / chunkInfo.vramBytes) : 0;
        float ibPercent = chunkInfo.vramBytes > 0 ? (chunkInfo.indexBufferBytes * 100.0f / chunkInfo.vramBytes) : 0;
        
        ImGui::Text("Vertex Buffer: %.1f%%", vbPercent);
        ImGui::ProgressBar(vbPercent / 100.0f, ImVec2(-1, 0));
        
        ImGui::Text("Index Buffer: %.1f%%", ibPercent);
        ImGui::ProgressBar(ibPercent / 100.0f, ImVec2(-1, 0));
        
        ImGui::Spacing();
        ImGui::Separator();
        
        // Geometry complexity
        ImGui::Text("GEOMETRY COMPLEXITY:");
        ImGui::Spacing();
        
        // Vertex reuse: ideal is 4 verts per 6 indices (2 triangles = 1 quad)
        // Perfect reuse ratio = 4/6 = 0.667
        float vertsPerIndex = chunkInfo.indexCount > 0 ? 
            (float)chunkInfo.vertexCount / chunkInfo.indexCount : 0;
        float reuseRatio = vertsPerIndex * 6.0f; // Normalized to quads (6 indices per quad)
        
        ImGui::Text("Vertices per Quad: %.2f (ideal: 4.0)", reuseRatio);
        ImGui::Text("Index Efficiency: %.1f%%", reuseRatio > 0 ? (4.0f / reuseRatio) * 100.0f : 0);
        
        ImGui::Spacing();
        
        // Warnings for large chunks
        if (chunkInfo.vertexCount > 60000) {
            ImGui::TextColored(ImVec4(1.0f, 0.3f, 0.3f, 1.0f), "! Very high vertex count (may split)");
        } else if (chunkInfo.vertexCount > 40000) {
            ImGui::TextColored(ImVec4(1.0f, 0.6f, 0.0f, 1.0f), "! High vertex count");
        }
        
        if (chunkInfo.indexCount > 180000) {
            ImGui::TextColored(ImVec4(1.0f, 0.3f, 0.3f, 1.0f), "! Very high triangle count");
        } else if (chunkInfo.indexCount > 100000) {
            ImGui::TextColored(ImVec4(1.0f, 0.6f, 0.0f, 1.0f), "! High triangle count");
        }
        
        // Show if this is likely a split mesh
        if (chunkInfo.vramBytes > 400 * 1024) {
            ImGui::TextColored(ImVec4(0.5f, 0.8f, 1.0f, 1.0f), 
                "Note: Large chunk, may have been split into sub-meshes");
        }

        if (m_world) {
            const auto& history = m_world->getChunkVisualHistory();
            if (history.count > 0) {
                ImGui::Spacing();
                ImGui::Separator();
                ImGui::Text("RECENT VISUAL UPDATES:");
                const bool detailHistoryOpen = ImGui::BeginChild("##chunkVisualDetail", ImVec2(0, 110), true);
                DebugWindowScrollLock::restore("Chunk Details/RecentVisual");
                if (detailHistoryOpen) {
                    size_t shown = 0;
                    const size_t scanCount = std::min<size_t>(history.count, 64);
                    for (size_t i = 0; i < scanCount && shown < 8; ++i) {
                        const auto& entry = history.getFromEnd(i);
                        if (entry.chunkCoord != chunkInfo.coord) {
                            continue;
                        }
                        const ImVec4 col = (entry.visibleMs < 16.0f) ? ImVec4(0.4f, 1.0f, 0.4f, 1.0f)
                                     : (entry.visibleMs < 100.0f) ? ImVec4(1.0f, 1.0f, 0.3f, 1.0f)
                                                                  : ImVec4(1.0f, 0.35f, 0.3f, 1.0f);
                        ImGui::TextColored(
                            col,
                            "#%llu [%s] shown %.1f ms | pipe %.1f ms | LOD %d",
                            static_cast<unsigned long long>(entry.sequence),
                            visualEntryLabel(entry),
                            entry.visibleMs,
                            entry.pipelineMs,
                            entry.lodLevel);
                        ++shown;
                    }
                    if (shown == 0) {
                        ImGui::TextDisabled("No recent visual updates recorded for this chunk.");
                    }
                }
                DebugWindowScrollLock::capture("Chunk Details/RecentVisual");
                ImGui::EndChild();
            }

            // ===== RELATED CHUNK ACTIONS =====
            // Merged timeline of visual history entries + visual error entries
            // (including [GModeDiff] entries pushed from updateGModeGeometryDiffCapture).
            // Lets the user inspect per-chunk attribution for missing/added geometry
            // without having to copy the timeline to the clipboard.
            {
                struct InlineEvent {
                    float timestampSec{0.0f};
                    uint64_t sequence{0};
                    bool isError{false};
                    const World::ChunkVisualHistoryEntry* historyEntry{nullptr};
                    const World::ChunkVisualErrorEntry* errorEntry{nullptr};
                };

                std::vector<InlineEvent> events;
                const auto& historyRef = m_world->getChunkVisualHistory();
                events.reserve(historyRef.count);
                for (size_t i = 0; i < historyRef.count; ++i) {
                    const auto& entry = historyRef.getFromEnd(i);
                    if (entry.chunkCoord == chunkInfo.coord) {
                        events.push_back({entry.timestampSec, entry.sequence, false, &entry, nullptr});
                    }
                }
                const auto& errorsRef = m_world->getChunkVisualErrorHistory();
                for (size_t i = 0; i < errorsRef.count; ++i) {
                    const auto& entry = errorsRef.getFromEnd(i);
                    if (entry.hasChunkCoord && entry.chunkCoord == chunkInfo.coord) {
                        events.push_back({entry.timestampSec, entry.sequence, true, nullptr, &entry});
                    }
                }

                ImGui::Spacing();
                ImGui::Separator();
                ImGui::Text("RELATED CHUNK ACTIONS:");
                ImGui::TextDisabled("Merged history + errors (incl. [GModeDiff]) for this chunk");

                const bool relatedOpen = ImGui::BeginChild("##chunkRelatedActions", ImVec2(0, 180), true);
                DebugWindowScrollLock::restore("Chunk Details/RelatedActions");
                if (relatedOpen) {
                    if (events.empty()) {
                        ImGui::TextDisabled("No related chunk actions recorded.");
                    } else {
                        std::sort(events.begin(), events.end(),
                            [](const InlineEvent& a, const InlineEvent& b) {
                                if (a.timestampSec != b.timestampSec) return a.timestampSec < b.timestampSec;
                                if (a.isError != b.isError) return a.isError < b.isError;
                                return a.sequence < b.sequence;
                            });

                        constexpr size_t kMaxInlineEvents = 24;
                        const size_t firstEvent = (events.size() > kMaxInlineEvents)
                            ? (events.size() - kMaxInlineEvents) : 0;

                        if (firstEvent > 0) {
                            ImGui::TextDisabled("(%zu older events hidden)", firstEvent);
                        }

                        for (size_t i = firstEvent; i < events.size(); ++i) {
                            const auto& ev = events[i];
                            if (ev.isError) {
                                const auto& e = *ev.errorEntry;
                                const bool isGModeDiff = (e.stage == "GModeDiff");
                                const ImVec4 col = isGModeDiff
                                    ? ImVec4(1.0f, 0.55f, 0.95f, 1.0f)   // magenta for G-mode diffs
                                    : ImVec4(1.0f, 0.45f, 0.35f, 1.0f);  // red-ish for other errors
                                ImGui::TextColored(
                                    col,
                                    "#%llu [%s] %s LOD%d  ver=%u->%u",
                                    static_cast<unsigned long long>(e.sequence),
                                    e.stage.c_str(),
                                    e.reason.c_str(),
                                    e.lodLevel,
                                    e.expectedVersion,
                                    e.actualVersion);
                            } else {
                                const auto& h = *ev.historyEntry;
                                const ImVec4 col = (h.visibleMs < 16.0f) ? ImVec4(0.4f, 1.0f, 0.4f, 1.0f)
                                               : (h.visibleMs < 100.0f) ? ImVec4(1.0f, 1.0f, 0.3f, 1.0f)
                                                                        : ImVec4(1.0f, 0.6f, 0.3f, 1.0f);
                                ImGui::TextColored(
                                    col,
                                    "#%llu [%s] shown %.1f ms | pipe %.1f ms | LOD%d",
                                    static_cast<unsigned long long>(h.sequence),
                                    visualEntryLabel(h),
                                    h.visibleMs,
                                    h.pipelineMs,
                                    h.lodLevel);
                            }
                        }
                    }
                }
                DebugWindowScrollLock::capture("Chunk Details/RelatedActions");
                ImGui::EndChild();
            }
        }

        ImGui::Spacing();
        ImGui::Separator();
        ImGui::Spacing();

        // Copy chunk details to clipboard
        if (ImGui::Button("Copy Details", ImVec2(-1.0f, 25.0f))) {
            const std::string text = buildChunkDiagnosticsText(chunkIndex, chunkInfo);
            ImGui::SetClipboardText(text.c_str());
        }
        if (ImGui::Button("Copy History", ImVec2(-1.0f, 25.0f))) {
            const std::string text = buildChunkTimelineText(
                chunkInfo.coord,
                static_cast<size_t>(m_chunkTimelineCopyCount));
            ImGui::SetClipboardText(text.empty() ? "No related chunk actions recorded.\n" : text.c_str());
        }
        ImGui::TextDisabled("Details + related history can be copied to clipboard");
    }
    DebugWindowScrollLock::capture(windowTitle);
    ImGui::End();
    
    if (!isOpen) {
        m_selectedChunkIndex = -1;
    }
}

````

## src\vulkan\FrameGraph.cpp

Description: No CC-DESC found.

````cpp
#include "vulkan/FrameGraph.h"
#include "rendering/common/ParallelCommandRecorder.h"
#include "rendering/sky/CloudSystem.h"
#include "rendering/sky/CelestialSystem.h"
#include "rendering/lighting/LightGlowSystem.h"
#include "rendering/sky/StarSystem.h"
#include "rendering/sky/SkySystem.h"
#include "rendering/postprocess/RetroPixelPassSystem.h"
#include "rendering/tjunctionfix/TJunctionFixSystem.h"
#include "vulkan/BufferSuballocator.h"
#include <imgui.h>
#include <imgui_impl_vulkan.h>
#include <GLFW/glfw3.h>
#include <array>
#include <algorithm>
#include <future>
#include <iostream>

namespace EngineFrameGraph {

VkPipelineStageFlags2 chooseSrcStageForLayout(VkImageLayout layout) {
    if (layout == VK_IMAGE_LAYOUT_UNDEFINED || layout == VK_IMAGE_LAYOUT_PRESENT_SRC_KHR) {
        return VK_PIPELINE_STAGE_2_NONE;
    }
    return VK_PIPELINE_STAGE_2_ALL_COMMANDS_BIT;
}

void buildFramePassDescriptors(
    std::vector<FramePassDescriptor>& outDescriptors,
    const std::vector<FramePassKind>& worldPassKinds)
{
    outDescriptors.clear();

    const auto hasPass = [&worldPassKinds](FramePassKind kind) {
        return std::find(worldPassKinds.begin(), worldPassKinds.end(), kind) != worldPassKinds.end();
    };

    // Color attachment descriptor
    FrameAttachmentDescriptor colorAttachment{};
    colorAttachment.type = FrameAttachmentType::Color;
    colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    colorAttachment.initialLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    colorAttachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

    // Depth attachment descriptor
    FrameAttachmentDescriptor depthAttachment{};
    depthAttachment.type = FrameAttachmentType::Depth;
    depthAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    depthAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    depthAttachment.initialLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
    depthAttachment.finalLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

    // Indirect commands attachment descriptor
    FrameAttachmentDescriptor indirectAttachment{};
    indirectAttachment.type = FrameAttachmentType::IndirectCommands;

    // Voxel opaque pass
    FramePassDescriptor voxelPass{};
    voxelPass.kind = FramePassKind::VoxelOpaque;
    voxelPass.name = "VoxelOpaque";
    voxelPass.queue = FrameQueueClass::Graphics;
    voxelPass.enabled = hasPass(FramePassKind::VoxelOpaque);
    voxelPass.attachments = {colorAttachment, depthAttachment, indirectAttachment};
    outDescriptors.push_back(voxelPass);

    // Overlay attachment (load existing color)
    FrameAttachmentDescriptor overlayAttachment = colorAttachment;
    overlayAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD;
    overlayAttachment.initialLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    // UI overlay pass
    FramePassDescriptor uiPass{};
    uiPass.kind = FramePassKind::UiOverlay;
    uiPass.name = "UiOverlay";
    uiPass.queue = FrameQueueClass::Graphics;
    uiPass.enabled = hasPass(FramePassKind::UiOverlay);
    uiPass.attachments = {overlayAttachment};
    outDescriptors.push_back(uiPass);

    // Debug HUD pass
    FramePassDescriptor debugPass{};
    debugPass.kind = FramePassKind::DebugHud;
    debugPass.name = "DebugHud";
    debugPass.queue = FrameQueueClass::Graphics;
    debugPass.enabled = hasPass(FramePassKind::DebugHud);
    debugPass.attachments = {overlayAttachment};
    outDescriptors.push_back(debugPass);
}

void compileFrameGraph(
    FrameGraph& frameGraph,
    const std::vector<FramePassDescriptor>& descriptors,
    std::vector<FrameGraphCompiledPass>& outCompiled)
{
    frameGraph.compile(descriptors, outCompiled);
}

void prepareFramePassBarriers(
    const FrameGraphCompiledPass& pass,
    const FrameGraph& frameGraph,
    uint32_t imageIndex,
    VkImage swapchainImage,
    VkImage depthImage,
    VkBuffer indirectBuffer,
    uint32_t indirectDrawCount,
    VkImageLayout& colorLayout,
    VkImageLayout& depthLayout,
    std::vector<VkImageMemoryBarrier2>& imageBarriers,
    std::vector<VkBufferMemoryBarrier2>& bufferBarriers)
{
    imageBarriers.clear();
    bufferBarriers.clear();

    for (const auto& handle : pass.resources) {
        const FrameGraphResource& resource = frameGraph.getResource(handle);
        switch (resource.type) {
            case FrameAttachmentType::Color: {
                VkImageLayout targetLayout = resource.descriptor.initialLayout;
                if (targetLayout == VK_IMAGE_LAYOUT_UNDEFINED) {
                    targetLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
                }
                if (colorLayout != targetLayout) {
                    VkImageMemoryBarrier2 barrier{};
                    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                    barrier.srcStageMask = chooseSrcStageForLayout(colorLayout);
                    barrier.srcAccessMask = VK_ACCESS_2_NONE;
                    barrier.dstStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
                    barrier.dstAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_2_COLOR_ATTACHMENT_READ_BIT;
                    barrier.oldLayout = colorLayout;
                    barrier.newLayout = targetLayout;
                    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    barrier.image = swapchainImage;
                    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                    barrier.subresourceRange.baseMipLevel = 0;
                    barrier.subresourceRange.levelCount = 1;
                    barrier.subresourceRange.baseArrayLayer = 0;
                    barrier.subresourceRange.layerCount = 1;
                    imageBarriers.push_back(barrier);
                    colorLayout = targetLayout;
                }
                break;
            }
            case FrameAttachmentType::Depth: {
                VkImageLayout targetLayout = resource.descriptor.initialLayout;
                if (targetLayout == VK_IMAGE_LAYOUT_UNDEFINED) {
                    targetLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
                }
                if (depthLayout != targetLayout) {
                    VkImageMemoryBarrier2 barrier{};
                    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
                    barrier.srcStageMask = chooseSrcStageForLayout(depthLayout);
                    barrier.srcAccessMask = VK_ACCESS_2_NONE;
                    barrier.dstStageMask = VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
                    barrier.dstAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
                    barrier.oldLayout = depthLayout;
                    barrier.newLayout = targetLayout;
                    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    barrier.image = depthImage;
                    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
                    barrier.subresourceRange.baseMipLevel = 0;
                    barrier.subresourceRange.levelCount = 1;
                    barrier.subresourceRange.baseArrayLayer = 0;
                    barrier.subresourceRange.layerCount = 1;
                    imageBarriers.push_back(barrier);
                    depthLayout = targetLayout;
                }
                break;
            }
            case FrameAttachmentType::IndirectCommands: {
                if (indirectDrawCount > 0) {
                    VkBufferMemoryBarrier2 barrier{};
                    barrier.sType = VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER_2;
                    barrier.srcStageMask = VK_PIPELINE_STAGE_2_TRANSFER_BIT;
                    barrier.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
                    barrier.dstStageMask = VK_PIPELINE_STAGE_2_DRAW_INDIRECT_BIT;
                    barrier.dstAccessMask = VK_ACCESS_2_INDIRECT_COMMAND_READ_BIT;
                    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                    barrier.buffer = indirectBuffer;
                    barrier.offset = 0;
                    barrier.size = VK_WHOLE_SIZE;
                    bufferBarriers.push_back(barrier);
                }
                break;
            }
            default:
                break;
        }
    }
}

void finalizeFramePassResources(
    const FrameGraphCompiledPass& pass,
    const FrameGraph& frameGraph,
    VkImageLayout& colorLayout,
    VkImageLayout& depthLayout)
{
    for (const auto& handle : pass.resources) {
        const FrameGraphResource& resource = frameGraph.getResource(handle);
        VkImageLayout finalLayout = resource.descriptor.finalLayout;
        if (finalLayout == VK_IMAGE_LAYOUT_UNDEFINED) {
            continue;
        }
        switch (resource.type) {
            case FrameAttachmentType::Color:
                colorLayout = finalLayout;
                break;
            case FrameAttachmentType::Depth:
                depthLayout = finalLayout;
                break;
            default:
                break;
        }
    }
}

void recordVoxelOpaquePass(
    VkCommandBuffer cmd,
    const FrameGraphContext& ctx,
    uint32_t imageIndex,
    const glm::mat4& view,
    const glm::mat4& proj)
{
    bool usePixelPass = ctx.pixelPass &&
                        ctx.pixelPass->isReady() &&
                        ctx.pixelPass->getSettings().enabled;
    bool useTJunctionFix = ctx.tjunctionFix &&
                           ctx.tjunctionFix->isEnabled() &&
                           ctx.tjunctionFix->isReady() &&
                           !usePixelPass;
    const bool useFinalPostProcess = usePixelPass || useTJunctionFix;
    
    // Resolve actual render pass and framebuffer (differs when T-junction fix is active)
    VkRenderPass actualRenderPass;
    VkFramebuffer actualFramebuffer;
    const uint32_t fbIndex = (ctx.framebufferIndex != UINT32_MAX) ? ctx.framebufferIndex : imageIndex;
    if (usePixelPass) {
        actualRenderPass = ctx.useDepthPrepass
            ? ctx.pixelPass->getOffscreenDepthLoadRenderPass()
            : ctx.pixelPass->getOffscreenRenderPass();
        actualFramebuffer = ctx.pixelPass->getOffscreenFramebuffer(fbIndex);
    } else if (useTJunctionFix) {
        actualRenderPass = ctx.useDepthPrepass
            ? ctx.tjunctionFix->getOffscreenDepthLoadRenderPass()
            : ctx.tjunctionFix->getOffscreenRenderPass();
        actualFramebuffer = ctx.tjunctionFix->getOffscreenFramebuffer(fbIndex);
    } else {
        actualRenderPass = ctx.renderPass;
        actualFramebuffer = (*ctx.framebuffers)[fbIndex];
    }

    VkRenderPassBeginInfo renderPassInfo{};
    renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    renderPassInfo.renderPass = actualRenderPass;
    renderPassInfo.framebuffer = actualFramebuffer;
    renderPassInfo.renderArea.offset = {0, 0};
    renderPassInfo.renderArea.extent = ctx.swapchainExtent;

    std::array<VkClearValue, 2> clearValues{};
    clearValues[0].color = {{
        ctx.lighting->currentSkyColor.r,
        ctx.lighting->currentSkyColor.g,
        ctx.lighting->currentSkyColor.b,
        1.0f
    }};
    clearValues[1].depthStencil = {0.0f, 0};
    renderPassInfo.clearValueCount = static_cast<uint32_t>(clearValues.size());
    renderPassInfo.pClearValues = clearValues.data();

    // Precompute common viewport/scissor state
    VkRect2D gameplayScissor = ctx.gameplayScissor;
    if (gameplayScissor.extent.width == 0 || gameplayScissor.extent.height == 0) {
        gameplayScissor.offset = {0, 0};
        gameplayScissor.extent = ctx.swapchainExtent;
    }

    VkViewport gameplayViewport{};
    gameplayViewport.x = static_cast<float>(gameplayScissor.offset.x);
    gameplayViewport.y = static_cast<float>(gameplayScissor.offset.y + static_cast<int32_t>(gameplayScissor.extent.height));
    gameplayViewport.width = static_cast<float>(gameplayScissor.extent.width);
    gameplayViewport.height = -static_cast<float>(gameplayScissor.extent.height);
    gameplayViewport.minDepth = 0.0f;
    gameplayViewport.maxDepth = 1.0f;

    // ── Parallel secondary command buffer path ──────────────────────
    // Requires ParallelCommandRecorder with per-slot command pools.
    // Slot 0 (worker): Sky + Stars
    // Slot 1 (main):   Terrain + timestamps
    // Slot 2 (worker): Celestials + Clouds + Light glows
    // Slot 3 (main):   ImGui (only when no T-junction fix)
    if (ctx.parallelRecorder && ctx.parallelRecorder->isInitialized()) {
        auto* recorder = ctx.parallelRecorder;

        // CPU-side prep: update light glow instance data before workers launch.
        // Writes to system-local buffers, reads shared lighting state (safe).
        ctx.lightGlowSystem->updateInstanceData(ctx.lighting->pointLights,
                                                ctx.cameraPos,
                                                ctx.objectManager, ctx.pulseLibrary,
                                                ctx.lighting->totalTime);

        // Capture glfwGetTime once for consistent cloud timing across threads
        const float currentTime = static_cast<float>(glfwGetTime());

        // Begin render pass for secondary command buffers
        vkCmdBeginRenderPass(cmd, &renderPassInfo, VK_SUBPASS_CONTENTS_SECONDARY_COMMAND_BUFFERS);

        // ── Worker A: Sky + Stars (Slot 0) ──
        auto futureA = std::async(std::launch::async, [&, imageIndex]() {
            auto secCmd = recorder->beginSecondary(0, imageIndex, actualRenderPass, actualFramebuffer);
            vkCmdSetViewport(secCmd, 0, 1, &gameplayViewport);
            vkCmdSetScissor(secCmd, 0, 1, &gameplayScissor);
            ctx.skySystem->render(secCmd, ctx.currentFrame, view, proj, ctx.cameraPos, *ctx.lighting);
            ctx.starSystem->render(secCmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                                   ctx.lighting->timeOfDay, ctx.lighting->totalTime);
            recorder->endSecondary(0, imageIndex);
        });

        // ── Worker B: Celestials + Clouds + Light glows (Slot 2) ──
        auto futureB = std::async(std::launch::async, [&, imageIndex, currentTime]() {
            auto secCmd = recorder->beginSecondary(2, imageIndex, actualRenderPass, actualFramebuffer);
            vkCmdSetViewport(secCmd, 0, 1, &gameplayViewport);
            vkCmdSetScissor(secCmd, 0, 1, &gameplayScissor);
            ctx.celestialSystem->render(secCmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                                        ctx.lighting->timeOfDay);
            ctx.cloudSystem->render(secCmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                                    currentTime, ctx.lighting->timeOfDay);
            ctx.lightGlowSystem->render(secCmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                                        ctx.lighting->totalTime, ctx.lighting->activePointLights);
            recorder->endSecondary(2, imageIndex);
        });

        // ── Main thread: Terrain (Slot 1) ──
        {
            const bool havePassTimestamps = (ctx.timestampQueryPool != VK_NULL_HANDLE);
            auto secCmd = recorder->beginSecondary(1, imageIndex, actualRenderPass, actualFramebuffer);
            vkCmdSetViewport(secCmd, 0, 1, &gameplayViewport);
            vkCmdSetScissor(secCmd, 0, 1, &gameplayScissor);

            VkBuffer pooledVB = ctx.vbAllocator->getPrimaryBuffer();
            VkBuffer pooledIB = ctx.ibAllocator->getPrimaryBuffer();
            VkDeviceSize vbOffset = 0;

            auto drawAllChunks = [&]() {
                vkCmdBindVertexBuffers(secCmd, 0, 1, &pooledVB, &vbOffset);
                vkCmdBindIndexBuffer(secCmd, pooledIB, 0, VK_INDEX_TYPE_UINT16);
                if (ctx.useGPUCulling && ctx.gpuVisibleDrawsBuffer != VK_NULL_HANDLE) {
                    vkCmdDrawIndexedIndirectCount(secCmd, ctx.gpuVisibleDrawsBuffer, 0,
                                                  ctx.gpuDrawCountBuffer, 0, ctx.gpuMaxDraws,
                                                  sizeof(VkDrawIndexedIndirectCommand));
                } else if (ctx.indirectDrawCount > 0) {
                    vkCmdDrawIndexedIndirect(secCmd, ctx.indirectBuffer, 0,
                                             ctx.indirectDrawCount, sizeof(VkDrawIndexedIndirectCommand));
                } else {
                    VkBuffer vertexBuffers[] = { ctx.cubeVB.buffer };
                    VkDeviceSize offsets[] = { ctx.cubeVB.offset };
                    vkCmdBindVertexBuffers(secCmd, 0, 1, vertexBuffers, offsets);
                    vkCmdBindIndexBuffer(secCmd, ctx.cubeIB.buffer, ctx.cubeIB.offset, VK_INDEX_TYPE_UINT16);
                    vkCmdDrawIndexed(secCmd, ctx.cubeIndexCount, 1, 0, 0, 0);
                }
            };

            if (havePassTimestamps) {
                vkCmdWriteTimestamp(secCmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                                   ctx.timestampQueryPool, ctx.timestampBase + 6);
            }
            if (ctx.anyLODUsesVoxel) {
                vkCmdBindPipeline(secCmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.graphicsPipeline);
                vkCmdBindDescriptorSets(secCmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.pipelineLayout,
                                       0, 1, &(*ctx.descriptorSets)[imageIndex], 0, nullptr);
                const uint32_t originsIdx = ctx.useGPUCulling ? 1u : 0u;
                vkCmdPushConstants(secCmd, ctx.pipelineLayout, VK_SHADER_STAGE_VERTEX_BIT,
                                   0, sizeof(uint32_t), &originsIdx);
                drawAllChunks();
            }
            if (ctx.anyLODUsesDCCM && ctx.dccmPipeline) {
                vkCmdBindPipeline(secCmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.dccmPipeline);
                vkCmdBindDescriptorSets(secCmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.dccmPipelineLayout,
                                       0, 1, &(*ctx.descriptorSets)[imageIndex], 0, nullptr);
                const uint32_t originsIdx = ctx.useGPUCulling ? 1u : 0u;
                vkCmdPushConstants(secCmd, ctx.dccmPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT,
                                   0, sizeof(uint32_t), &originsIdx);
                drawAllChunks();
            }
            if (havePassTimestamps) {
                vkCmdWriteTimestamp(secCmd, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                                   ctx.timestampQueryPool, ctx.timestampBase + 7);
            }

            // Post-terrain callback (e.g. texel splatting splat draw)
            if (ctx.postTerrainCallback) {
                ctx.postTerrainCallback(secCmd, imageIndex);
            }

            recorder->endSecondary(1, imageIndex);
        }

        // Wait for worker threads to finish recording
        futureA.get();
        futureB.get();

        // Determine how many secondaries to execute
        uint32_t numSecondaries = 3;
        // ImGui secondary (Slot 3) — only when no T-junction fix and ImGui is active
        if (!useFinalPostProcess && ctx.imguiFrameActive) {
            auto secCmd = recorder->beginSecondary(3, imageIndex, actualRenderPass, actualFramebuffer);
            VkViewport fullViewport{};
            fullViewport.x = 0.0f;
            fullViewport.y = static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.width = static_cast<float>(ctx.swapchainExtent.width);
            fullViewport.height = -static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.minDepth = 0.0f;
            fullViewport.maxDepth = 1.0f;
            VkRect2D fullScissor{};
            fullScissor.offset = {0, 0};
            fullScissor.extent = ctx.swapchainExtent;
            vkCmdSetViewport(secCmd, 0, 1, &fullViewport);
            vkCmdSetScissor(secCmd, 0, 1, &fullScissor);
            ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), secCmd);
            recorder->endSecondary(3, imageIndex);
            numSecondaries = 4;
        }

        // Execute all secondary command buffers in rendering order
        VkCommandBuffer secondaries[4];
        secondaries[0] = recorder->getCmd(0, imageIndex); // sky + stars
        secondaries[1] = recorder->getCmd(1, imageIndex); // terrain
        secondaries[2] = recorder->getCmd(2, imageIndex); // celestials + clouds + glows
        if (numSecondaries == 4) {
            secondaries[3] = recorder->getCmd(3, imageIndex); // imgui
        }
        vkCmdExecuteCommands(cmd, numSecondaries, secondaries);

        vkCmdEndRenderPass(cmd);

    } else {
        // ── Fallback: inline recording (no parallel recorder available) ──
        vkCmdBeginRenderPass(cmd, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);
        vkCmdSetViewport(cmd, 0, 1, &gameplayViewport);
        vkCmdSetScissor(cmd, 0, 1, &gameplayScissor);

        const bool havePassTimestamps = (ctx.timestampQueryPool != VK_NULL_HANDLE);

        ctx.skySystem->render(cmd, ctx.currentFrame, view, proj, ctx.cameraPos, *ctx.lighting);
        ctx.starSystem->render(cmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                              ctx.lighting->timeOfDay, ctx.lighting->totalTime);

        VkBuffer pooledVB = ctx.vbAllocator->getPrimaryBuffer();
        VkBuffer pooledIB = ctx.ibAllocator->getPrimaryBuffer();
        VkDeviceSize vbOffset = 0;

        auto drawAllChunks = [&]() {
            vkCmdBindVertexBuffers(cmd, 0, 1, &pooledVB, &vbOffset);
            vkCmdBindIndexBuffer(cmd, pooledIB, 0, VK_INDEX_TYPE_UINT16);
            if (ctx.useGPUCulling && ctx.gpuVisibleDrawsBuffer != VK_NULL_HANDLE) {
                vkCmdDrawIndexedIndirectCount(cmd, ctx.gpuVisibleDrawsBuffer, 0,
                                              ctx.gpuDrawCountBuffer, 0, ctx.gpuMaxDraws,
                                              sizeof(VkDrawIndexedIndirectCommand));
            } else if (ctx.indirectDrawCount > 0) {
                vkCmdDrawIndexedIndirect(cmd, ctx.indirectBuffer, 0,
                                         ctx.indirectDrawCount, sizeof(VkDrawIndexedIndirectCommand));
            } else {
                VkBuffer vertexBuffers[] = { ctx.cubeVB.buffer };
                VkDeviceSize offsets[] = { ctx.cubeVB.offset };
                vkCmdBindVertexBuffers(cmd, 0, 1, vertexBuffers, offsets);
                vkCmdBindIndexBuffer(cmd, ctx.cubeIB.buffer, ctx.cubeIB.offset, VK_INDEX_TYPE_UINT16);
                vkCmdDrawIndexed(cmd, ctx.cubeIndexCount, 1, 0, 0, 0);
            }
        };

        if (havePassTimestamps) {
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, ctx.timestampQueryPool, ctx.timestampBase + 6);
        }
        if (ctx.anyLODUsesVoxel) {
            vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.graphicsPipeline);
            vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.pipelineLayout, 0, 1,
                                   &(*ctx.descriptorSets)[imageIndex], 0, nullptr);
            const uint32_t originsIdx = ctx.useGPUCulling ? 1u : 0u;
            vkCmdPushConstants(cmd, ctx.pipelineLayout, VK_SHADER_STAGE_VERTEX_BIT,
                               0, sizeof(uint32_t), &originsIdx);
            drawAllChunks();
        }
        if (ctx.anyLODUsesDCCM && ctx.dccmPipeline) {
            vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.dccmPipeline);
            vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, ctx.dccmPipelineLayout, 0, 1,
                                   &(*ctx.descriptorSets)[imageIndex], 0, nullptr);
            const uint32_t originsIdx = ctx.useGPUCulling ? 1u : 0u;
            vkCmdPushConstants(cmd, ctx.dccmPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT,
                               0, sizeof(uint32_t), &originsIdx);
            drawAllChunks();
        }
        if (havePassTimestamps) {
            vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, ctx.timestampQueryPool, ctx.timestampBase + 7);
        }

        // Post-terrain callback (e.g. texel splatting splat draw)
        if (ctx.postTerrainCallback) {
            ctx.postTerrainCallback(cmd, imageIndex);
        }

        ctx.celestialSystem->render(cmd, ctx.currentFrame, view, proj, ctx.cameraPos, ctx.lighting->timeOfDay);
        ctx.cloudSystem->render(cmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                               static_cast<float>(glfwGetTime()), ctx.lighting->timeOfDay);

        ctx.lightGlowSystem->updateInstanceData(ctx.lighting->pointLights, ctx.cameraPos,
                                                ctx.objectManager, ctx.pulseLibrary,
                                                ctx.lighting->totalTime);
        ctx.lightGlowSystem->render(cmd, ctx.currentFrame, view, proj, ctx.cameraPos,
                                   ctx.lighting->totalTime, ctx.lighting->activePointLights);

        if (!useFinalPostProcess && ctx.imguiFrameActive) {
            VkViewport fullViewport{};
            fullViewport.x = 0.0f;
            fullViewport.y = static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.width = static_cast<float>(ctx.swapchainExtent.width);
            fullViewport.height = -static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.minDepth = 0.0f;
            fullViewport.maxDepth = 1.0f;
            VkRect2D fullScissor{};
            fullScissor.offset = {0, 0};
            fullScissor.extent = ctx.swapchainExtent;
            vkCmdSetViewport(cmd, 0, 1, &fullViewport);
            vkCmdSetScissor(cmd, 0, 1, &fullScissor);
            ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), cmd);
        }

        vkCmdEndRenderPass(cmd);
    }

    // T-junction fix pass (always inline on primary CB, separate render pass)
    if (usePixelPass) {
        ctx.pixelPass->recordPass(cmd, fbIndex, ctx.swapchainExtent);
        if (ctx.imguiFrameActive) {
            VkViewport fullViewport{};
            fullViewport.x = 0.0f;
            fullViewport.y = static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.width = static_cast<float>(ctx.swapchainExtent.width);
            fullViewport.height = -static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.minDepth = 0.0f;
            fullViewport.maxDepth = 1.0f;
            VkRect2D fullScissor{};
            fullScissor.offset = {0, 0};
            fullScissor.extent = ctx.swapchainExtent;
            vkCmdSetViewport(cmd, 0, 1, &fullViewport);
            vkCmdSetScissor(cmd, 0, 1, &fullScissor);
            ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), cmd);
        }
        vkCmdEndRenderPass(cmd);
    } else if (useTJunctionFix) {
        ctx.tjunctionFix->recordFixPass(cmd, fbIndex, ctx.swapchainExtent);
        if (ctx.imguiFrameActive) {
            VkViewport fullViewport{};
            fullViewport.x = 0.0f;
            fullViewport.y = static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.width = static_cast<float>(ctx.swapchainExtent.width);
            fullViewport.height = -static_cast<float>(ctx.swapchainExtent.height);
            fullViewport.minDepth = 0.0f;
            fullViewport.maxDepth = 1.0f;
            VkRect2D fullScissor{};
            fullScissor.offset = {0, 0};
            fullScissor.extent = ctx.swapchainExtent;
            vkCmdSetViewport(cmd, 0, 1, &fullViewport);
            vkCmdSetScissor(cmd, 0, 1, &fullScissor);
            ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), cmd);
        }
        vkCmdEndRenderPass(cmd);
    }
}

} // namespace EngineFrameGraph

````

## include\vulkan\FrameGraph.h

Description: No CC-DESC found. C++ class 'CloudSystem'.

````cpp
#pragma once

#include <vulkan/vulkan.h>
#include <glm/glm.hpp>
#include <vector>
#include <cstdint>
#include <functional>
#include "rendering/lighting/LightingSettings.h"
#include "vulkan/FramePassTypes.h"

// Forward declarations
class CloudSystem;
class CelestialSystem;
class LightGlowSystem;
class StarSystem;
class SkySystem;
class TJunctionFixSystem;
class RetroPixelPassSystem;
class BufferSuballocator;
class ObjectManager;
class LightPulsePresetLibrary;
class ParallelCommandRecorder;
#include "vulkan/BufferSuballocator.h"

// Resource handle for frame graph
using FrameResourceHandle = uint32_t;
constexpr FrameResourceHandle kInvalidResourceHandle = ~0u;

// Frame graph resource descriptor
struct FrameGraphResource {
    FrameAttachmentType type{FrameAttachmentType::None};
    FrameAttachmentDescriptor descriptor;
};

// Compiled frame pass with resource handles
struct FrameGraphCompiledPass {
    FramePassKind kind{FramePassKind::VoxelOpaque};
    const char* name{nullptr};
    bool enabled{false};
    std::vector<FrameResourceHandle> resources;
    const FramePassDescriptor* descriptor{nullptr};  // Pointer to original descriptor
};

// Frame graph - manages render pass dependencies and resource transitions
class FrameGraph {
public:
    void compile(const std::vector<FramePassDescriptor>& descriptors,
                 std::vector<FrameGraphCompiledPass>& outCompiled) {
        m_resources.clear();
        outCompiled.clear();
        m_descriptors = descriptors;  // Store for pointer access
        
        for (size_t i = 0; i < m_descriptors.size(); ++i) {
            const auto& desc = m_descriptors[i];
            FrameGraphCompiledPass compiled;
            compiled.kind = desc.kind;
            compiled.name = desc.name;
            compiled.enabled = desc.enabled;
            compiled.descriptor = &m_descriptors[i];  // Point to stored descriptor
            
            for (const auto& attachment : desc.attachments) {
                FrameResourceHandle handle = static_cast<FrameResourceHandle>(m_resources.size());
                FrameGraphResource resource;
                resource.type = attachment.type;
                resource.descriptor = attachment;
                m_resources.push_back(resource);
                compiled.resources.push_back(handle);
            }
            
            outCompiled.push_back(compiled);
        }
    }
    
    const FrameGraphResource& getResource(FrameResourceHandle handle) const {
        return m_resources[handle];
    }
    
private:
    std::vector<FrameGraphResource> m_resources;
    std::vector<FramePassDescriptor> m_descriptors;  // Stored descriptors for pointer stability
};

namespace EngineFrameGraph {

    // Context struct to hold all state needed for frame graph operations
    struct FrameGraphContext {
        // Device handles
        VkDevice device;
        
        // Frame graph state
        FrameGraph* frameGraph;
        std::vector<FramePassDescriptor>* passDescriptors;
        std::vector<FrameGraphCompiledPass>* compiledPasses;
        
        // Layout tracking
        VkImageLayout* colorLayout;
        VkImageLayout* depthLayout;
        
        // Swapchain resources
        std::vector<VkImage>* swapchainImages;
        std::vector<VkFramebuffer>* framebuffers;
        VkImage depthImage;
        VkExtent2D swapchainExtent;
        VkRect2D gameplayScissor;
        
        // Pipeline state
        VkRenderPass renderPass;
        VkPipeline graphicsPipeline;
        VkPipelineLayout pipelineLayout;
        VkPipeline dccmPipeline;            // DCCM terrain pipeline
        VkPipelineLayout dccmPipelineLayout; // DCCM terrain pipeline layout
        bool useDepthPrepass = false;       // Terrain shading should load prepass depth
        bool anyLODUsesVoxel;               // Whether any LOD uses voxel pipeline
        bool anyLODUsesDCCM;                // Whether any LOD uses DCCM pipeline
        std::vector<VkDescriptorSet>* descriptorSets;
        
        // Indirect drawing
        VkBuffer indirectBuffer;
        uint32_t indirectDrawCount;
        
        // GPU culling (Phase 1)
        bool useGPUCulling = false;
        VkBuffer gpuVisibleDrawsBuffer = VK_NULL_HANDLE;    // Output from compute shader
        VkBuffer gpuDrawCountBuffer = VK_NULL_HANDLE;       // Atomic count from compute shader
        VkBuffer gpuOriginsBuffer = VK_NULL_HANDLE;         // Chunk origins for visible chunks
        uint32_t gpuMaxDraws = 0;                           // Max draws for vkCmdDrawIndexedIndirectCount
        
        // Buffer allocators
        BufferSuballocator* vbAllocator;
        BufferSuballocator* ibAllocator;
        
        // Fallback mesh
        BufferSlice cubeVB;
        BufferSlice cubeIB;
        uint32_t cubeIndexCount;
        
        // Rendering systems
        CloudSystem* cloudSystem;
        CelestialSystem* celestialSystem;
        LightGlowSystem* lightGlowSystem;
        StarSystem* starSystem;
        SkySystem* skySystem;
        TJunctionFixSystem* tjunctionFix;
        RetroPixelPassSystem* pixelPass;
        LightingSettings* lighting;
        ObjectManager* objectManager = nullptr;
        LightPulsePresetLibrary* pulseLibrary = nullptr;
        
        // Camera state
        glm::vec3 cameraPos;
        
        // Frame info
        uint32_t currentFrame;
        
        // Timestamp query pool
        VkQueryPool timestampQueryPool;
        uint32_t timestampBase = 0;

        // ImGui state — skip GPU draw data recording when no ImGui frame was begun
        bool imguiFrameActive = false;

        // Parallel command recorder for secondary CB recording
        ParallelCommandRecorder* parallelRecorder = nullptr;

        // Override framebuffer index (for gameplay window rendering).
        // When != UINT32_MAX, use this index for framebuffer lookup instead of imageIndex.
        uint32_t framebufferIndex = UINT32_MAX;

        // Optional callback invoked after terrain draws but before celestials/clouds.
        // Used by texel splatting and other post-terrain effects.
        // Parameters: (VkCommandBuffer cmd, uint32_t imageIndex)
        std::function<void(VkCommandBuffer, uint32_t)> postTerrainCallback;
    };

    // Build frame pass descriptors based on world passes
    void buildFramePassDescriptors(
        std::vector<FramePassDescriptor>& outDescriptors,
        const std::vector<FramePassKind>& worldPassKinds);

    // Compile the frame graph
    void compileFrameGraph(
        FrameGraph& frameGraph,
        const std::vector<FramePassDescriptor>& descriptors,
        std::vector<FrameGraphCompiledPass>& outCompiled);

    // Prepare barriers for a frame pass
    void prepareFramePassBarriers(
        const FrameGraphCompiledPass& pass,
        const FrameGraph& frameGraph,
        uint32_t imageIndex,
        VkImage swapchainImage,
        VkImage depthImage,
        VkBuffer indirectBuffer,
        uint32_t indirectDrawCount,
        VkImageLayout& colorLayout,
        VkImageLayout& depthLayout,
        std::vector<VkImageMemoryBarrier2>& imageBarriers,
        std::vector<VkBufferMemoryBarrier2>& bufferBarriers);

    // Finalize frame pass resources (update layout tracking)
    void finalizeFramePassResources(
        const FrameGraphCompiledPass& pass,
        const FrameGraph& frameGraph,
        VkImageLayout& colorLayout,
        VkImageLayout& depthLayout);

    // Record the voxel opaque render pass
    void recordVoxelOpaquePass(
        VkCommandBuffer cmd,
        const FrameGraphContext& ctx,
        uint32_t imageIndex,
        const glm::mat4& view,
        const glm::mat4& proj);

    // Helper to choose source stage for layout transitions
    VkPipelineStageFlags2 chooseSrcStageForLayout(VkImageLayout layout);

} // namespace EngineFrameGraph

````

## src\vulkan\Sync.cpp

Description: No CC-DESC found.

````cpp
#include "vulkan/Sync.h"
#include <stdexcept>
#include <cstdio>

namespace Sync {

CommandPoolResult createCommandPool(VkDevice device, VkPhysicalDevice physicalDevice) {
    CommandPoolResult result{};
    
    // Pick graphics queue family
    uint32_t queueFamilyCount = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, nullptr);
    std::vector<VkQueueFamilyProperties> queueFamilies(queueFamilyCount);
    vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilies.data());
    
    uint32_t graphicsFamily = 0;
    for (uint32_t i = 0; i < queueFamilyCount; ++i) {
        if (queueFamilies[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) {
            graphicsFamily = i;
            break;
        }
    }

    VkCommandPoolCreateInfo poolInfo{};
    poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    poolInfo.queueFamilyIndex = graphicsFamily;
    poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;

    if (vkCreateCommandPool(device, &poolInfo, nullptr, &result.commandPool) != VK_SUCCESS) {
        throw std::runtime_error("failed to create command pool!");
    }
    
    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_COMMAND_POOL, (uint64_t)result.commandPool, "MainCommandPool");
    
    // Allocate per-frame upload command buffers
    VkCommandBufferAllocateInfo uploadAllocInfo{};
    uploadAllocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    uploadAllocInfo.commandPool = result.commandPool;
    uploadAllocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    uploadAllocInfo.commandBufferCount = MAX_FRAMES_IN_FLIGHT;
    
    if (vkAllocateCommandBuffers(device, &uploadAllocInfo, result.uploadCmds.data()) != VK_SUCCESS) {
        throw std::runtime_error("failed to allocate upload command buffers!");
    }
    
    // Name them for debugging
    for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        char name[64];
        snprintf(name, sizeof(name), "UploadCommandBuffer[%d]", i);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_COMMAND_BUFFER, (uint64_t)result.uploadCmds[i], name);
    }
    
    return result;
}

SyncObjectsResult createSyncObjects(VkDevice device, size_t swapchainImageCount) {
    SyncObjectsResult result{};
    result.frames.resize(MAX_FRAMES_IN_FLIGHT);
    result.imageInFlight.resize(swapchainImageCount, VK_NULL_HANDLE);

    VkSemaphoreCreateInfo semaphoreInfo{};
    semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
    
    // Create timeline semaphore for upload ordering
    VkSemaphoreTypeCreateInfo timelineCreateInfo{};
    timelineCreateInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO;
    timelineCreateInfo.semaphoreType = VK_SEMAPHORE_TYPE_TIMELINE;
    timelineCreateInfo.initialValue = 0;
    
    VkSemaphoreCreateInfo timelineSemaphoreInfo{};
    timelineSemaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
    timelineSemaphoreInfo.pNext = &timelineCreateInfo;
    
    if (vkCreateSemaphore(device, &timelineSemaphoreInfo, nullptr, &result.uploadTimeline) != VK_SUCCESS) {
        throw std::runtime_error("failed to create upload timeline semaphore!");
    }
    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_SEMAPHORE, (uint64_t)result.uploadTimeline, "UploadTimeline");

    // Timeline semaphore for cross-frame Hi-Z pyramid synchronization.
    // Ensures Frame N's pyramid build completes before Frame N+1's culling reads it.
    if (vkCreateSemaphore(device, &timelineSemaphoreInfo, nullptr, &result.hiZTimeline) != VK_SUCCESS) {
        throw std::runtime_error("failed to create Hi-Z timeline semaphore!");
    }
    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_SEMAPHORE, (uint64_t)result.hiZTimeline, "HiZTimeline");
    
    VkFenceCreateInfo fenceInfo{};
    fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT; // Start signaled so first frame doesn't wait

    for (size_t i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        if (vkCreateSemaphore(device, &semaphoreInfo, nullptr, &result.frames[i].imageAvailable) != VK_SUCCESS ||
            vkCreateSemaphore(device, &semaphoreInfo, nullptr, &result.frames[i].renderFinishedMain) != VK_SUCCESS ||
            vkCreateSemaphore(device, &semaphoreInfo, nullptr, &result.frames[i].renderFinishedGameplay) != VK_SUCCESS ||
            vkCreateFence(device, &fenceInfo, nullptr, &result.frames[i].inFlight) != VK_SUCCESS) {
            throw std::runtime_error("failed to create synchronization objects for a frame!");
        }
        
        // Name sync objects for debugging
        char semName1[64], semName2[64], semName3[64], fenceName[64];
        snprintf(semName1, sizeof(semName1), "ImageAvailableSemaphore[%zu]", i);
        snprintf(semName2, sizeof(semName2), "RenderFinishedMainSemaphore[%zu]", i);
        snprintf(semName3, sizeof(semName3), "RenderFinishedGameplaySemaphore[%zu]", i);
        snprintf(fenceName, sizeof(fenceName), "InFlightFence[%zu]", i);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_SEMAPHORE, (uint64_t)result.frames[i].imageAvailable, semName1);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_SEMAPHORE, (uint64_t)result.frames[i].renderFinishedMain, semName2);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_SEMAPHORE, (uint64_t)result.frames[i].renderFinishedGameplay, semName3);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_FENCE, (uint64_t)result.frames[i].inFlight, fenceName);
    }
    
    return result;
}

std::vector<VkCommandBuffer> createCommandBuffers(VkDevice device, VkCommandPool commandPool, uint32_t count) {
    std::vector<VkCommandBuffer> commandBuffers(count);

    VkCommandBufferAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocInfo.commandPool = commandPool;
    allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    allocInfo.commandBufferCount = count;

    if (vkAllocateCommandBuffers(device, &allocInfo, commandBuffers.data()) != VK_SUCCESS) {
        throw std::runtime_error("failed to allocate command buffers!");
    }
    
    // Name command buffers for debugging
    for (uint32_t i = 0; i < count; ++i) {
        char name[64];
        snprintf(name, sizeof(name), "CommandBuffer[%u]", i);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_COMMAND_BUFFER, (uint64_t)commandBuffers[i], name);
    }
    
    return commandBuffers;
}

void destroySyncObjects(VkDevice device, std::vector<FrameData>& frames, VkSemaphore uploadTimeline, VkSemaphore hiZTimeline) {
    for (auto& frame : frames) {
        if (frame.imageAvailable) vkDestroySemaphore(device, frame.imageAvailable, nullptr);
        if (frame.renderFinishedMain) vkDestroySemaphore(device, frame.renderFinishedMain, nullptr);
        if (frame.renderFinishedGameplay) vkDestroySemaphore(device, frame.renderFinishedGameplay, nullptr);
        if (frame.inFlight) vkDestroyFence(device, frame.inFlight, nullptr);
    }
    if (uploadTimeline) vkDestroySemaphore(device, uploadTimeline, nullptr);
    if (hiZTimeline) vkDestroySemaphore(device, hiZTimeline, nullptr);
}

} // namespace Sync

````

## include\vulkan\Sync.h

Description: No CC-DESC found. C++ struct 'FrameData'.

````cpp
#pragma once

#include <vulkan/vulkan.h>
#include <vector>
#include <array>
#include "rendering/common/VulkanHelpers.h"

namespace Sync {

    constexpr int MAX_FRAMES_IN_FLIGHT = 3;

    // Per-frame synchronization data
    struct FrameData {
        VkSemaphore imageAvailable = VK_NULL_HANDLE;
        VkSemaphore renderFinishedMain = VK_NULL_HANDLE;
        VkSemaphore renderFinishedGameplay = VK_NULL_HANDLE;
        VkFence inFlight = VK_NULL_HANDLE;
    };

    // Result struct for createSyncObjects
    struct SyncObjectsResult {
        std::vector<FrameData> frames;
        std::vector<VkFence> imageInFlight;
        VkSemaphore uploadTimeline;
        VkSemaphore hiZTimeline;  // Cross-frame Hi-Z pyramid synchronization
    };

    // Result struct for createCommandPool
    struct CommandPoolResult {
        VkCommandPool commandPool;
        std::array<VkCommandBuffer, MAX_FRAMES_IN_FLIGHT> uploadCmds;
    };

    // Creates the command pool and upload command buffers
    CommandPoolResult createCommandPool(VkDevice device, VkPhysicalDevice physicalDevice);

    // Creates frame synchronization objects (semaphores, fences, timeline semaphore)
    SyncObjectsResult createSyncObjects(VkDevice device, size_t swapchainImageCount);

    // Allocates command buffers for rendering
    std::vector<VkCommandBuffer> createCommandBuffers(VkDevice device, VkCommandPool commandPool, uint32_t count);

    // Cleanup functions
    void destroySyncObjects(VkDevice device, std::vector<FrameData>& frames, VkSemaphore uploadTimeline, VkSemaphore hiZTimeline = VK_NULL_HANDLE);

} // namespace Sync

````

## src\vulkan\Pipeline.cpp

Description: No CC-DESC found.

````cpp
#include "vulkan/Pipeline.h"
#include "rendering/common/VulkanHelpers.h"
#include "rendering/common/Mesh.h"
#include <stdexcept>
#include <iostream>
#include <fstream>
#include <array>
#include <cstdio>
#include <cstddef>

namespace Pipeline {

namespace {

VkRenderPass createTerrainRenderPassInternal(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat,
    VkAttachmentLoadOp colorLoadOp,
    VkAttachmentStoreOp colorStoreOp,
    VkImageLayout colorInitialLayout,
    VkImageLayout colorFinalLayout,
    VkAttachmentLoadOp depthLoadOp,
    VkAttachmentStoreOp depthStoreOp,
    VkImageLayout depthInitialLayout,
    VkImageLayout depthFinalLayout,
    const char* debugName) {
    VkAttachmentDescription colorAttachment{};
    colorAttachment.format = colorFormat;
    colorAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
    colorAttachment.loadOp = colorLoadOp;
    colorAttachment.storeOp = colorStoreOp;
    colorAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    colorAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    colorAttachment.initialLayout = colorInitialLayout;
    colorAttachment.finalLayout = colorFinalLayout;

    VkAttachmentDescription depthAttachment{};
    depthAttachment.format = depthFormat;
    depthAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
    depthAttachment.loadOp = depthLoadOp;
    depthAttachment.storeOp = depthStoreOp;
    depthAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    depthAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    depthAttachment.initialLayout = depthInitialLayout;
    depthAttachment.finalLayout = depthFinalLayout;

    VkAttachmentReference colorAttachmentRef{};
    colorAttachmentRef.attachment = 0;
    colorAttachmentRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkAttachmentReference depthAttachmentRef{};
    depthAttachmentRef.attachment = 1;
    depthAttachmentRef.layout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &colorAttachmentRef;
    subpass.pDepthStencilAttachment = &depthAttachmentRef;

    VkSubpassDependency dependency{};
    dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
    dependency.dstSubpass = 0;
    // COMPUTE_SHADER_BIT: the post-render Hi-Z pyramid build reads the depth
    // buffer via compute at the end of the previous frame.  Without this bit
    // the render pass can begin clearing depth while that compute read is
    // still in flight, producing zero texels in the pyramid (corruption).
    dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT
                            | VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT
                            | VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
    dependency.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
    dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT | VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
    dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;

    std::array<VkAttachmentDescription, 2> attachments = { colorAttachment, depthAttachment };
    VkRenderPassCreateInfo renderPassInfo{};
    renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    renderPassInfo.attachmentCount = static_cast<uint32_t>(attachments.size());
    renderPassInfo.pAttachments = attachments.data();
    renderPassInfo.subpassCount = 1;
    renderPassInfo.pSubpasses = &subpass;
    renderPassInfo.dependencyCount = 1;
    renderPassInfo.pDependencies = &dependency;

    VkRenderPass renderPass;
    if (vkCreateRenderPass(device, &renderPassInfo, nullptr, &renderPass) != VK_SUCCESS)
        throw std::runtime_error("failed to create render pass!");

    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_RENDER_PASS, (uint64_t)renderPass, debugName);

    return renderPass;
}

} // namespace

VkRenderPass createRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat) {
    return createTerrainRenderPassInternal(
        device,
        colorFormat,
        depthFormat,
        VK_ATTACHMENT_LOAD_OP_CLEAR,
        VK_ATTACHMENT_STORE_OP_STORE,
        VK_IMAGE_LAYOUT_UNDEFINED,
        VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
        VK_ATTACHMENT_LOAD_OP_CLEAR,
        VK_ATTACHMENT_STORE_OP_STORE,
        VK_IMAGE_LAYOUT_UNDEFINED,
        VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
        "MainRenderPass");
}

VkRenderPass createDepthPrepassRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat) {
    return createTerrainRenderPassInternal(
        device,
        colorFormat,
        depthFormat,
        VK_ATTACHMENT_LOAD_OP_DONT_CARE,
        VK_ATTACHMENT_STORE_OP_DONT_CARE,
        VK_IMAGE_LAYOUT_UNDEFINED,
        VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
        VK_ATTACHMENT_LOAD_OP_CLEAR,
        VK_ATTACHMENT_STORE_OP_STORE,
        VK_IMAGE_LAYOUT_UNDEFINED,
        VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
        "DepthPrepassRenderPass");
}

VkRenderPass createDepthLoadRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat) {
    return createTerrainRenderPassInternal(
        device,
        colorFormat,
        depthFormat,
        VK_ATTACHMENT_LOAD_OP_CLEAR,
        VK_ATTACHMENT_STORE_OP_STORE,
        VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
        VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
        VK_ATTACHMENT_LOAD_OP_LOAD,
        VK_ATTACHMENT_STORE_OP_STORE,
        VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
        VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
        "DepthLoadRenderPass");
}

VkRenderPass createUIRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat) {
    
    // Color attachment - LOAD to preserve existing content (SVO output)
    VkAttachmentDescription colorAttachment{};
    colorAttachment.format = colorFormat;
    colorAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
    colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD;  // Preserve SVO output
    colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    colorAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    colorAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    colorAttachment.initialLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;  // After blit
    colorAttachment.finalLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;

    // Depth attachment - we don't need it for UI but the framebuffer has it
    VkAttachmentDescription depthAttachment{};
    depthAttachment.format = depthFormat;
    depthAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
    depthAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;  // Don't need depth for UI
    depthAttachment.storeOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    depthAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    depthAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    depthAttachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    depthAttachment.finalLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

    VkAttachmentReference colorAttachmentRef{};
    colorAttachmentRef.attachment = 0;
    colorAttachmentRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkAttachmentReference depthAttachmentRef{};
    depthAttachmentRef.attachment = 1;
    depthAttachmentRef.layout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;

    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &colorAttachmentRef;
    subpass.pDepthStencilAttachment = &depthAttachmentRef;

    VkSubpassDependency dependency{};
    dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
    dependency.dstSubpass = 0;
    dependency.srcStageMask = VK_PIPELINE_STAGE_TRANSFER_BIT;  // Wait for blit
    dependency.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

    std::array<VkAttachmentDescription, 2> attachments = { colorAttachment, depthAttachment };
    VkRenderPassCreateInfo renderPassInfo{};
    renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    renderPassInfo.attachmentCount = static_cast<uint32_t>(attachments.size());
    renderPassInfo.pAttachments = attachments.data();
    renderPassInfo.subpassCount = 1;
    renderPassInfo.pSubpasses = &subpass;
    renderPassInfo.dependencyCount = 1;
    renderPassInfo.pDependencies = &dependency;

    VkRenderPass renderPass;
    if (vkCreateRenderPass(device, &renderPassInfo, nullptr, &renderPass) != VK_SUCCESS)
        throw std::runtime_error("failed to create UI render pass!");
    
    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_RENDER_PASS, (uint64_t)renderPass, "UIRenderPass");
    
    return renderPass;
}

VkDescriptorSetLayout createDescriptorSetLayout(VkDevice device) {
    std::array<VkDescriptorSetLayoutBinding, 11> bindings{};

    // Binding 0: UBO (matrices)
    bindings[0].binding = 0;
    bindings[0].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    bindings[0].descriptorCount = 1;
    bindings[0].stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
    bindings[0].pImmutableSamplers = nullptr;

    // Binding 1: Storage buffer ARRAY (chunk origins) — Phase D bindless.
    // Array index 0 = static CPU-mode origins; 1 = GPU culling visible-origins output;
    // 2 = sun-shadow local indirect origins.
    // Selected per-draw via the VS push constant `originsIndex`. PARTIALLY_BOUND_BIT lets us
    // initialise only slot 0 at descriptor-set creation and write slot 1 after the GPU culling
    // system finishes its own init (which happens later in initVulkan).
    bindings[1].binding = 1;
    bindings[1].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bindings[1].descriptorCount = 3;
    bindings[1].stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
    bindings[1].pImmutableSamplers = nullptr;

    // Binding 2: Lighting data storage buffer (SSBO for >32 light capacity)
    bindings[2].binding = 2;
    bindings[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bindings[2].descriptorCount = 1;
    bindings[2].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[2].pImmutableSamplers = nullptr;

    // Binding 3: Camera data uniform buffer
    bindings[3].binding = 3;
    bindings[3].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    bindings[3].descriptorCount = 1;
    bindings[3].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[3].pImmutableSamplers = nullptr;

    // Binding 4: AO settings uniform buffer
    bindings[4].binding = 4;
    bindings[4].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    bindings[4].descriptorCount = 1;
    bindings[4].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[4].pImmutableSamplers = nullptr;

    // Binding 5: Shadow metadata storage buffer
    bindings[5].binding = 5;
    bindings[5].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bindings[5].descriptorCount = 1;
    bindings[5].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[5].pImmutableSamplers = nullptr;

    // Binding 6: Sun shadow sampler (kept for compatibility)
    bindings[6].binding = 6;
    bindings[6].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindings[6].descriptorCount = 1;
    bindings[6].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[6].pImmutableSamplers = nullptr;

    // Binding 7: Point shadow cube-array sampler
    bindings[7].binding = 7;
    bindings[7].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindings[7].descriptorCount = 1;
    bindings[7].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[7].pImmutableSamplers = nullptr;

    // Binding 8: Clustered lighting bitmask storage buffer
    bindings[8].binding = 8;
    bindings[8].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bindings[8].descriptorCount = 1;
    bindings[8].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[8].pImmutableSamplers = nullptr;

    // Binding 9: Sky-vis heightmap (sun-independent zenith-occlusion source)
    bindings[9].binding = 9;
    bindings[9].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindings[9].descriptorCount = 1;
    bindings[9].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[9].pImmutableSamplers = nullptr;

    // Binding 10: Sparse texture-material overlay sampled by voxel terrain.
    bindings[10].binding = 10;
    bindings[10].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bindings[10].descriptorCount = 1;
    bindings[10].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[10].pImmutableSamplers = nullptr;

    VkDescriptorSetLayoutCreateInfo layoutInfo{};
    layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    layoutInfo.bindingCount = static_cast<uint32_t>(bindings.size());
    layoutInfo.pBindings = bindings.data();

    // Phase D — attach per-binding flags so binding 1 (the bindless SSBO array)
    // can be partially bound (only slot 0 written at init; slot 1 written later).
    std::array<VkDescriptorBindingFlags, 11> bindingFlags{};
    bindingFlags[1] = VK_DESCRIPTOR_BINDING_PARTIALLY_BOUND_BIT;
    VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlagsInfo{};
    bindingFlagsInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_BINDING_FLAGS_CREATE_INFO;
    bindingFlagsInfo.bindingCount = static_cast<uint32_t>(bindingFlags.size());
    bindingFlagsInfo.pBindingFlags = bindingFlags.data();
    layoutInfo.pNext = &bindingFlagsInfo;

    VkDescriptorSetLayout layout;
    if (vkCreateDescriptorSetLayout(device, &layoutInfo, nullptr, &layout) != VK_SUCCESS)
        throw std::runtime_error("failed to create descriptor set layout!");
    
    return layout;
}

VkPipelineCache createPipelineCache(VkDevice device) {
    VkPipelineCacheCreateInfo createInfo{};
    createInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO;
    
    // Try to load cache from file
    std::vector<char> cacheData;
    const char* cacheFilename = "pipeline_cache.bin";
    
    std::ifstream cacheFile(cacheFilename, std::ios::binary | std::ios::ate);
    if (cacheFile.is_open()) {
        size_t fileSize = (size_t)cacheFile.tellg();
        cacheData.resize(fileSize);
        cacheFile.seekg(0);
        cacheFile.read(cacheData.data(), fileSize);
        cacheFile.close();
        
        createInfo.initialDataSize = cacheData.size();
        createInfo.pInitialData = cacheData.data();
        std::cout << "Loaded pipeline cache (" << fileSize << " bytes)" << std::endl;
    }
    
    VkPipelineCache cache;
    if (vkCreatePipelineCache(device, &createInfo, nullptr, &cache) != VK_SUCCESS) {
        throw std::runtime_error("failed to create pipeline cache!");
    }
    
    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_PIPELINE_CACHE, (uint64_t)cache, "MainPipelineCache");
    
    return cache;
}

void savePipelineCache(VkDevice device, VkPipelineCache cache, const char* filename) {
    if (cache == VK_NULL_HANDLE) return;
    
    // Get cache data size
    size_t cacheSize = 0;
    vkGetPipelineCacheData(device, cache, &cacheSize, nullptr);
    
    if (cacheSize > 0) {
        std::vector<char> cacheData(cacheSize);
        if (vkGetPipelineCacheData(device, cache, &cacheSize, cacheData.data()) == VK_SUCCESS) {
            std::ofstream cacheFile(filename, std::ios::binary);
            if (cacheFile.is_open()) {
                cacheFile.write(cacheData.data(), cacheSize);
                cacheFile.close();
                std::cout << "Saved pipeline cache (" << cacheSize << " bytes)" << std::endl;
            }
        }
    }
}

GraphicsPipelineResult createGraphicsPipeline(
    VkDevice device,
    VkRenderPass renderPass,
    VkDescriptorSetLayout descriptorSetLayout,
    VkPipelineCache pipelineCache,
    VkExtent2D extent,
    const char* vertShaderPath,
    const char* fragShaderPath,
    VkCompareOp depthCompareOp,
    VkBool32 depthWriteEnable,
    VkPipelineLayout existingLayout) {
    
    GraphicsPipelineResult result{};
    
    std::vector<char> vertCode = VulkanHelpers::readFile(vertShaderPath);
    std::vector<char> fragCode = VulkanHelpers::readFile(fragShaderPath);

    VkShaderModule vertShaderModule = VulkanHelpers::createShaderModule(device, vertCode);
    VkShaderModule fragShaderModule = VulkanHelpers::createShaderModule(device, fragCode);

    VkPipelineShaderStageCreateInfo vertStageInfo{};
    vertStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    vertStageInfo.stage = VK_SHADER_STAGE_VERTEX_BIT;
    vertStageInfo.module = vertShaderModule;
    vertStageInfo.pName = "main";

    VkPipelineShaderStageCreateInfo fragStageInfo{};
    fragStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    fragStageInfo.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    fragStageInfo.module = fragShaderModule;
    fragStageInfo.pName = "main";

    VkPipelineShaderStageCreateInfo shaderStages[] = { vertStageInfo, fragStageInfo };

    // Vertex input: packed geometry word + tiny procedural-material word.
    VkVertexInputBindingDescription bindingDesc{};
    bindingDesc.binding = 0;
    bindingDesc.stride = sizeof(Vertex);
    bindingDesc.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    std::array<VkVertexInputAttributeDescription, 2> attributeDesc{};
    attributeDesc[0].binding = 0;
    attributeDesc[0].location = 0;
    attributeDesc[0].format = VK_FORMAT_R32_UINT;
    attributeDesc[0].offset = offsetof(Vertex, packed);
    attributeDesc[1].binding = 0;
    attributeDesc[1].location = 1;
    attributeDesc[1].format = VK_FORMAT_R32_UINT;
    attributeDesc[1].offset = offsetof(Vertex, material);

    VkPipelineVertexInputStateCreateInfo vertexInputInfo{};
    vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInputInfo.vertexBindingDescriptionCount = 1;
    vertexInputInfo.pVertexBindingDescriptions = &bindingDesc;
    vertexInputInfo.vertexAttributeDescriptionCount = static_cast<uint32_t>(attributeDesc.size());
    vertexInputInfo.pVertexAttributeDescriptions = attributeDesc.data();

    VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
    inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    inputAssembly.primitiveRestartEnable = VK_FALSE;

    VkViewport viewport{};
    viewport.x = 0.0f;
    viewport.y = (float)extent.height;  // Start from bottom for Vulkan Y-flip
    viewport.width = (float)extent.width;
    viewport.height = -(float)extent.height;  // Negative height flips Y-axis
    viewport.minDepth = 0.0f;
    viewport.maxDepth = 1.0f;

    VkRect2D scissor{};
    scissor.offset = {0, 0};
    scissor.extent = extent;

    VkPipelineViewportStateCreateInfo viewportState{};
    viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewportState.viewportCount = 1;
    viewportState.pViewports = &viewport;
    viewportState.scissorCount = 1;
    viewportState.pScissors = &scissor;

    VkDynamicState dynamicStates[] = {
        VK_DYNAMIC_STATE_VIEWPORT,
        VK_DYNAMIC_STATE_SCISSOR
    };
    VkPipelineDynamicStateCreateInfo dynamicState{};
    dynamicState.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamicState.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
    dynamicState.pDynamicStates = dynamicStates;

    VkPipelineRasterizationStateCreateInfo rasterizer{};

    rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rasterizer.pNext = nullptr;
    rasterizer.depthClampEnable = VK_FALSE;
    rasterizer.rasterizerDiscardEnable = VK_FALSE;
    rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
    rasterizer.lineWidth = 1.0f;
    rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;
    rasterizer.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    rasterizer.depthBiasEnable = VK_FALSE;

    VkPipelineMultisampleStateCreateInfo multisampling{};
    multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisampling.sampleShadingEnable = VK_FALSE;
    multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    VkPipelineColorBlendAttachmentState colorBlendAttachment{};
    colorBlendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | 
                                          VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    colorBlendAttachment.blendEnable = VK_FALSE;

    VkPipelineColorBlendStateCreateInfo colorBlending{};
    colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlending.logicOpEnable = VK_FALSE;
    colorBlending.attachmentCount = 1;
    colorBlending.pAttachments = &colorBlendAttachment;

    VkPipelineDepthStencilStateCreateInfo depthStencil{};
    depthStencil.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
    depthStencil.depthTestEnable = VK_TRUE;
    depthStencil.depthWriteEnable = depthWriteEnable;
    // Reversed-Z: use GREATER (near=1.0, far=0.0), or GREATER_OR_EQUAL for z-prepass main pass
    depthStencil.depthCompareOp = depthCompareOp;
    depthStencil.depthBoundsTestEnable = VK_FALSE;
    depthStencil.stencilTestEnable = VK_FALSE;

    if (existingLayout != VK_NULL_HANDLE) {
        result.layout = existingLayout;
    } else {
        // Phase D — VS push constant: uint originsIndex selects which SSBO from the
        // bindless ChunkOrigins array (binding 1) terrain shaders read for instance origins.
        VkPushConstantRange pcRange{};
        pcRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
        pcRange.offset = 0;
        pcRange.size = sizeof(uint32_t);

        VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
        pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        pipelineLayoutInfo.setLayoutCount = 1;
        pipelineLayoutInfo.pSetLayouts = &descriptorSetLayout;
        pipelineLayoutInfo.pushConstantRangeCount = 1;
        pipelineLayoutInfo.pPushConstantRanges = &pcRange;

        if (vkCreatePipelineLayout(device, &pipelineLayoutInfo, nullptr, &result.layout) != VK_SUCCESS)
            throw std::runtime_error("failed to create pipeline layout!");
    }

    VkGraphicsPipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount = 2;
    pipelineInfo.pStages = shaderStages;
    pipelineInfo.pVertexInputState = &vertexInputInfo;
    pipelineInfo.pInputAssemblyState = &inputAssembly;
    pipelineInfo.pViewportState = &viewportState;
    pipelineInfo.pRasterizationState = &rasterizer;
    pipelineInfo.pMultisampleState = &multisampling;
    pipelineInfo.pDepthStencilState = &depthStencil;
    pipelineInfo.pColorBlendState = &colorBlending;
    pipelineInfo.pDynamicState = &dynamicState;
    pipelineInfo.layout = result.layout;
    pipelineInfo.renderPass = renderPass;
    pipelineInfo.subpass = 0;

    if (vkCreateGraphicsPipelines(device, pipelineCache, 1, &pipelineInfo, nullptr, &result.pipeline) != VK_SUCCESS)
        throw std::runtime_error("failed to create graphics pipeline!");

    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_PIPELINE, (uint64_t)result.pipeline, "MainGraphicsPipeline");
    if (existingLayout == VK_NULL_HANDLE) {
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_PIPELINE_LAYOUT, (uint64_t)result.layout, "MainPipelineLayout");
    }

    vkDestroyShaderModule(device, fragShaderModule, nullptr);
    vkDestroyShaderModule(device, vertShaderModule, nullptr);
    
    return result;
}

VkPipeline createDepthPrePassPipeline(
    VkDevice device,
    VkRenderPass renderPass,
    VkPipelineLayout pipelineLayout,
    VkPipelineCache pipelineCache,
    VkExtent2D extent,
    const char* vertShaderPath) {
    
    std::vector<char> vertCode = VulkanHelpers::readFile(vertShaderPath);
    VkShaderModule vertShaderModule = VulkanHelpers::createShaderModule(device, vertCode);

    // Vertex-only pipeline (no fragment shader)
    VkPipelineShaderStageCreateInfo vertStageInfo{};
    vertStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    vertStageInfo.stage = VK_SHADER_STAGE_VERTEX_BIT;
    vertStageInfo.module = vertShaderModule;
    vertStageInfo.pName = "main";

    // Depth only consumes the geometry word; stride still matches Vertex.
    VkVertexInputBindingDescription bindingDesc{};
    bindingDesc.binding = 0;
    bindingDesc.stride = sizeof(Vertex);
    bindingDesc.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    std::array<VkVertexInputAttributeDescription, 1> attributeDesc{};
    attributeDesc[0].binding = 0;
    attributeDesc[0].location = 0;
    attributeDesc[0].format = VK_FORMAT_R32_UINT;
    attributeDesc[0].offset = offsetof(Vertex, packed);

    VkPipelineVertexInputStateCreateInfo vertexInputInfo{};
    vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInputInfo.vertexBindingDescriptionCount = 1;
    vertexInputInfo.pVertexBindingDescriptions = &bindingDesc;
    vertexInputInfo.vertexAttributeDescriptionCount = static_cast<uint32_t>(attributeDesc.size());
    vertexInputInfo.pVertexAttributeDescriptions = attributeDesc.data();

    VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
    inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    inputAssembly.primitiveRestartEnable = VK_FALSE;

    VkViewport viewport{};
    viewport.x = 0.0f;
    viewport.y = (float)extent.height;
    viewport.width = (float)extent.width;
    viewport.height = -(float)extent.height;
    viewport.minDepth = 0.0f;
    viewport.maxDepth = 1.0f;

    VkRect2D scissor{};
    scissor.offset = {0, 0};
    scissor.extent = extent;

    VkPipelineViewportStateCreateInfo viewportState{};
    viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewportState.viewportCount = 1;
    viewportState.pViewports = &viewport;
    viewportState.scissorCount = 1;
    viewportState.pScissors = &scissor;

    VkDynamicState dynamicStates[] = {
        VK_DYNAMIC_STATE_VIEWPORT,
        VK_DYNAMIC_STATE_SCISSOR
    };
    VkPipelineDynamicStateCreateInfo dynamicState{};
    dynamicState.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamicState.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
    dynamicState.pDynamicStates = dynamicStates;

    VkPipelineRasterizationStateCreateInfo rasterizer{};
    rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rasterizer.pNext = nullptr;
    rasterizer.depthClampEnable = VK_FALSE;
    rasterizer.rasterizerDiscardEnable = VK_FALSE;
    rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
    rasterizer.lineWidth = 1.0f;
    rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;
    rasterizer.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    rasterizer.depthBiasEnable = VK_FALSE;

    VkPipelineMultisampleStateCreateInfo multisampling{};
    multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisampling.sampleShadingEnable = VK_FALSE;
    multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    // No color writes — depth-only pass
    VkPipelineColorBlendAttachmentState colorBlendAttachment{};
    colorBlendAttachment.colorWriteMask = 0; // All channels disabled
    colorBlendAttachment.blendEnable = VK_FALSE;

    VkPipelineColorBlendStateCreateInfo colorBlending{};
    colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlending.logicOpEnable = VK_FALSE;
    colorBlending.attachmentCount = 1;
    colorBlending.pAttachments = &colorBlendAttachment;

    // Reversed-Z: GREATER + depth write enabled to populate depth buffer
    VkPipelineDepthStencilStateCreateInfo depthStencil{};
    depthStencil.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
    depthStencil.depthTestEnable = VK_TRUE;
    depthStencil.depthWriteEnable = VK_TRUE;
    depthStencil.depthCompareOp = VK_COMPARE_OP_GREATER;
    depthStencil.depthBoundsTestEnable = VK_FALSE;
    depthStencil.stencilTestEnable = VK_FALSE;

    VkGraphicsPipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount = 1; // Vertex only — no fragment shader
    pipelineInfo.pStages = &vertStageInfo;
    pipelineInfo.pVertexInputState = &vertexInputInfo;
    pipelineInfo.pInputAssemblyState = &inputAssembly;
    pipelineInfo.pViewportState = &viewportState;
    pipelineInfo.pRasterizationState = &rasterizer;
    pipelineInfo.pMultisampleState = &multisampling;
    pipelineInfo.pDepthStencilState = &depthStencil;
    pipelineInfo.pColorBlendState = &colorBlending;
    pipelineInfo.pDynamicState = &dynamicState;
    pipelineInfo.layout = pipelineLayout;
    pipelineInfo.renderPass = renderPass;
    pipelineInfo.subpass = 0;

    VkPipeline pipeline;
    if (vkCreateGraphicsPipelines(device, pipelineCache, 1, &pipelineInfo, nullptr, &pipeline) != VK_SUCCESS)
        throw std::runtime_error("failed to create depth pre-pass pipeline!");

    VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_PIPELINE, (uint64_t)pipeline, "DepthPrePassPipeline");

    vkDestroyShaderModule(device, vertShaderModule, nullptr);
    
    return pipeline;
}

std::vector<VkFramebuffer> createFramebuffers(
    VkDevice device,
    VkRenderPass renderPass,
    const std::vector<VkImageView>& imageViews,
    VkImageView depthView,
    VkExtent2D extent) {
    
    std::vector<VkFramebuffer> framebuffers(imageViews.size());
    
    for (size_t i = 0; i < imageViews.size(); ++i) {
        std::array<VkImageView, 2> attachments = {
            imageViews[i],
            depthView
        };

        VkFramebufferCreateInfo framebufferInfo{};
        framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        framebufferInfo.renderPass = renderPass;
        framebufferInfo.attachmentCount = static_cast<uint32_t>(attachments.size());
        framebufferInfo.pAttachments = attachments.data();
        framebufferInfo.width = extent.width;
        framebufferInfo.height = extent.height;
        framebufferInfo.layers = 1;

        if (vkCreateFramebuffer(device, &framebufferInfo, nullptr, &framebuffers[i]) != VK_SUCCESS)
            throw std::runtime_error("failed to create framebuffer!");
        
        char name[64];
        snprintf(name, sizeof(name), "Framebuffer[%zu]", i);
        VulkanHelpers::setObjectName(device, VK_OBJECT_TYPE_FRAMEBUFFER, (uint64_t)framebuffers[i], name);
    }
    
    return framebuffers;
}

void destroyFramebuffers(VkDevice device, std::vector<VkFramebuffer>& framebuffers) {
    for (auto fb : framebuffers) {
        vkDestroyFramebuffer(device, fb, nullptr);
    }
    framebuffers.clear();
}

} // namespace Pipeline

````

## include\vulkan\Pipeline.h

Description: No CC-DESC found. C++ struct 'GraphicsPipelineResult'.

````cpp
#pragma once

#include <vulkan/vulkan.h>
#include <vector>
#include <string>

/**
 * @brief Pipeline creation and management utilities.
 * 
 * This namespace contains functions for creating Vulkan pipeline objects:
 * - Render pass creation
 * - Descriptor set layout creation
 * - Pipeline cache management
 * - Graphics pipeline creation
 * - Framebuffer creation
 * 
 * These objects define how rendering is performed and how
 * shader resources are bound.
 */
namespace Pipeline {

/**
 * @brief Creates a render pass with color and depth attachments.
 * 
 * @param device Logical device
 * @param colorFormat Swapchain image format
 * @param depthFormat Depth buffer format
 * @return VkRenderPass The created render pass
 */
VkRenderPass createRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat);

/**
 * @brief Creates a terrain depth pre-pass render pass.
 *
 * Color is ignored and left in COLOR_ATTACHMENT_OPTIMAL for the subsequent
 * shaded terrain pass. Depth is cleared and preserved.
 */
VkRenderPass createDepthPrepassRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat);

/**
 * @brief Creates the main terrain render pass variant that preserves the
 * depth written by a prior z-prepass.
 *
 * Color is cleared for the shaded pass; depth is loaded and preserved.
 */
VkRenderPass createDepthLoadRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat);

/**
 * @brief Creates a UI-only render pass that preserves existing framebuffer content.
 * 
 * This render pass uses LOAD_OP_LOAD to keep existing content (e.g., SVO output)
 * and renders UI on top with alpha blending.
 * 
 * @param device Logical device
 * @param colorFormat Swapchain image format
 * @param depthFormat Depth buffer format
 * @return VkRenderPass The created UI render pass
 */
VkRenderPass createUIRenderPass(
    VkDevice device,
    VkFormat colorFormat,
    VkFormat depthFormat);

/**
 * @brief Result struct for graphics pipeline creation.
 */
struct GraphicsPipelineResult {
    VkPipeline pipeline{VK_NULL_HANDLE};
    VkPipelineLayout layout{VK_NULL_HANDLE};
};

/**
 * @brief Creates the main descriptor set layout.
 * 
 * Layout bindings:
 * - 0: UBO (matrices) - vertex stage
 * - 1: Storage buffer (chunk origins) - vertex stage
 * - 2: Lighting data UBO - fragment stage
 * - 3: Camera data UBO - fragment stage
 * - 4: AO settings UBO - fragment stage
 * - 5: Shadow metadata SSBO - fragment stage
 * - 6: Sun shadow map sampler - fragment stage
 * - 7: Point shadow cube-array sampler - fragment stage
 * 
 * @param device Logical device
 * @return VkDescriptorSetLayout The created descriptor set layout
 */
VkDescriptorSetLayout createDescriptorSetLayout(VkDevice device);

/**
 * @brief Creates or loads a pipeline cache.
 * 
 * Attempts to load from "pipeline_cache.bin" if it exists.
 * 
 * @param device Logical device
 * @return VkPipelineCache The created pipeline cache
 */
VkPipelineCache createPipelineCache(VkDevice device);

/**
 * @brief Saves the pipeline cache to disk.
 * 
 * @param device Logical device
 * @param cache Pipeline cache to save
 * @param filename Output filename (default: "pipeline_cache.bin")
 */
void savePipelineCache(
    VkDevice device,
    VkPipelineCache cache,
    const char* filename = "pipeline_cache.bin");

/**
 * @brief Creates the main graphics pipeline.
 * 
 * @param device Logical device
 * @param renderPass Render pass
 * @param descriptorSetLayout Descriptor set layout
 * @param pipelineCache Pipeline cache (can be VK_NULL_HANDLE)
 * @param extent Swapchain extent (for viewport/scissor)
 * @param vertShaderPath Path to vertex shader SPIR-V
 * @param fragShaderPath Path to fragment shader SPIR-V
 * @param depthCompareOp Depth comparison operator (default: GREATER for reversed-Z)
 * @param depthWriteEnable Whether to write to depth buffer (default: TRUE)
 * @param existingLayout Optional compatible pipeline layout to reuse
 * @return GraphicsPipelineResult The created pipeline and layout
 */
GraphicsPipelineResult createGraphicsPipeline(
    VkDevice device,
    VkRenderPass renderPass,
    VkDescriptorSetLayout descriptorSetLayout,
    VkPipelineCache pipelineCache,
    VkExtent2D extent,
    const char* vertShaderPath = "shaders/terrain/cube.vert.spv",
    const char* fragShaderPath = "shaders/terrain/cube.frag.spv",
    VkCompareOp depthCompareOp = VK_COMPARE_OP_GREATER,
    VkBool32 depthWriteEnable = VK_TRUE,
    VkPipelineLayout existingLayout = VK_NULL_HANDLE);

/**
 * @brief Creates a depth-only pre-pass pipeline (no fragment shader, no color writes).
 * 
 * Uses the same vertex format and descriptor set layout as the main terrain pipeline.
 * Populates the depth buffer so the main pass can use GREATER_OR_EQUAL + no depth writes
 * to eliminate overdraw.
 * 
 * @param device Logical device
 * @param renderPass Render pass (must be compatible with main render pass)
 * @param pipelineLayout Existing pipeline layout to reuse
 * @param pipelineCache Pipeline cache (can be VK_NULL_HANDLE)
 * @param extent Swapchain extent (for viewport/scissor)
 * @param vertShaderPath Path to z-only vertex shader SPIR-V
 * @return VkPipeline The created depth pre-pass pipeline
 */
VkPipeline createDepthPrePassPipeline(
    VkDevice device,
    VkRenderPass renderPass,
    VkPipelineLayout pipelineLayout,
    VkPipelineCache pipelineCache,
    VkExtent2D extent,
    const char* vertShaderPath = "shaders/terrain/cube_zonly.vert.spv");

/**
 * @brief Creates framebuffers for the swapchain.
 * 
 * @param device Logical device
 * @param renderPass Render pass
 * @param imageViews Swapchain image views
 * @param depthView Depth buffer image view
 * @param extent Swapchain extent
 * @return std::vector<VkFramebuffer> The created framebuffers
 */
std::vector<VkFramebuffer> createFramebuffers(
    VkDevice device,
    VkRenderPass renderPass,
    const std::vector<VkImageView>& imageViews,
    VkImageView depthView,
    VkExtent2D extent);

/**
 * @brief Destroys framebuffers.
 * 
 * @param device Logical device
 * @param framebuffers Framebuffers to destroy (will be cleared)
 */
void destroyFramebuffers(VkDevice device, std::vector<VkFramebuffer>& framebuffers);

} // namespace Pipeline

````

## src\vulkan\UploadArena.cpp

Description: No CC-DESC found.

````cpp
#include "vulkan/UploadArena.h"
#include "rendering/common/VulkanHelpers.h"
#include "core/CommonMath.h"
#include <stdexcept>
#include <iostream>
#include <cstring>

// GPT_CHANGE: Upload arena implementation - linear bump allocator for staging

void UploadArena::init(VkDevice device, VkPhysicalDevice physicalDevice, VkDeviceSize bytes) {
    m_device = device;
    m_physicalDevice = physicalDevice;
    m_size = bytes;
    m_head = 0;
    
    // Create staging buffer (HOST_VISIBLE | COHERENT | TRANSFER_SRC)
    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = m_size;
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    
    if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_buffer) != VK_SUCCESS) {
        throw std::runtime_error("failed to create upload arena buffer!");
    }
    
    // Allocate HOST_VISIBLE | COHERENT memory
    VkMemoryRequirements memRequirements;
    vkGetBufferMemoryRequirements(m_device, m_buffer, &memRequirements);
    
    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memRequirements.size;
    allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
        m_physicalDevice,
        memRequirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
    );
    
    if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_memory) != VK_SUCCESS) {
        throw std::runtime_error("failed to allocate upload arena memory!");
    }
    
    vkBindBufferMemory(m_device, m_buffer, m_memory, 0);
    
    // Persistently map
    if (vkMapMemory(m_device, m_memory, 0, m_size, 0, &m_mapped) != VK_SUCCESS) {
        throw std::runtime_error("failed to map upload arena memory!");
    }
    
    std::cout << "[UploadArena] Created staging arena: " << (m_size / 1024 / 1024) << " MiB" << std::endl;
}

void UploadArena::destroy(VkDevice device) {
    if (m_mapped) {
        vkUnmapMemory(device, m_memory);
        m_mapped = nullptr;
    }
    
    // Free any remaining temp buffers
    for (const auto& temp : m_temps) {
        if (temp.buffer != VK_NULL_HANDLE) {
            vkDestroyBuffer(device, temp.buffer, nullptr);
        }
        if (temp.memory != VK_NULL_HANDLE) {
            vkFreeMemory(device, temp.memory, nullptr);
        }
    }
    m_temps.clear();
    
    if (m_buffer != VK_NULL_HANDLE) {
        vkDestroyBuffer(device, m_buffer, nullptr);
        m_buffer = VK_NULL_HANDLE;
    }
    if (m_memory != VK_NULL_HANDLE) {
        vkFreeMemory(device, m_memory, nullptr);
        m_memory = VK_NULL_HANDLE;
    }
}

void UploadArena::reset() {
    m_head = 0;
}

UploadSpan UploadArena::allocate(VkDeviceSize size, VkDeviceSize alignment) {
    VkDeviceSize alignedHead = alignUp(m_head, alignment);
    VkDeviceSize end = alignedHead + size;
    
    if (end <= m_size) {
        // Happy path: fits in arena
        UploadSpan span;
        span.buffer = m_buffer;
        span.offset = alignedHead;
        span.ptr = static_cast<char*>(m_mapped) + alignedHead;
        span.size = size;
        
        m_head = end;
        return span;
    }
    
    // Overflow: fallback to one-off allocation (rare)
    std::cerr << "[UploadArena] WARNING: Arena overflow (" << (size / 1024) 
              << " KiB requested, " << ((m_size - m_head) / 1024) 
              << " KiB remaining). Using fallback allocation." << std::endl;
    
    return allocateFallback(size, alignment);
}

UploadSpan UploadArena::allocateFallback(VkDeviceSize size, VkDeviceSize alignment) {
    // Create one-off staging buffer
    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = size;
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    
    VkBuffer fallbackBuffer;
    if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &fallbackBuffer) != VK_SUCCESS) {
        throw std::runtime_error("failed to create fallback staging buffer!");
    }
    
    VkMemoryRequirements memRequirements;
    vkGetBufferMemoryRequirements(m_device, fallbackBuffer, &memRequirements);
    
    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memRequirements.size;
    allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
        m_physicalDevice,
        memRequirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT
    );
    
    VkDeviceMemory fallbackMemory;
    if (vkAllocateMemory(m_device, &allocInfo, nullptr, &fallbackMemory) != VK_SUCCESS) {
        throw std::runtime_error("failed to allocate fallback staging memory!");
    }
    
    vkBindBufferMemory(m_device, fallbackBuffer, fallbackMemory, 0);
    
    void* mappedPtr;
    if (vkMapMemory(m_device, fallbackMemory, 0, size, 0, &mappedPtr) != VK_SUCCESS) {
        throw std::runtime_error("failed to map fallback staging memory!");
    }
    
    UploadSpan span;
    span.buffer = fallbackBuffer;
    span.offset = 0;
    span.ptr = mappedPtr;
    span.size = size;
    
    // Track for deferred cleanup once the fence is signaled
    m_temps.push_back({fallbackBuffer, fallbackMemory, m_currentBatchFence});
    
    return span;
}

void UploadArena::drainTemps(uint64_t lastSignaledValue) {
    // Free any temp buffers whose fence has been signaled
    auto it = m_temps.begin();
    while (it != m_temps.end()) {
        if (it->fenceValue <= lastSignaledValue) {
            if (it->buffer != VK_NULL_HANDLE) {
                vkDestroyBuffer(m_device, it->buffer, nullptr);
            }
            if (it->memory != VK_NULL_HANDLE) {
                vkFreeMemory(m_device, it->memory, nullptr);
            }
            it = m_temps.erase(it);
        } else {
            ++it;
        }
    }
}

````

## include\vulkan\UploadArena.h

Description: No CC-DESC found. C++ struct 'UploadSpan'.

````cpp
#pragma once

#include <vulkan/vulkan.h>
#include <cstdint>
#include <vector>

// GPT_CHANGE: Upload (staging) arena for per-frame CPU→GPU transfers
// Linear bump allocator with persistently mapped HOST_VISIBLE|COHERENT memory

struct UploadSpan {
    VkBuffer buffer;
    VkDeviceSize offset;
    void* ptr;
    VkDeviceSize size;
};

class UploadArena {
public:
    UploadArena() = default;
    
    // Initialize with given size (default: 16 MiB)
    void init(VkDevice device, VkPhysicalDevice physicalDevice, VkDeviceSize bytes);
    
    // Destroy resources
    void destroy(VkDevice device);
    
    // Reset allocation head to 0 (call at frame begin after fence wait)
    void reset();
    
    // Allocate staging memory, returns mapped pointer + buffer info
    // Falls back to one-off allocation if arena is full (logs warning)
    UploadSpan allocate(VkDeviceSize size, VkDeviceSize alignment = 256);
    
    // Set the current batch fence value (for fallback tracking)
    void setBatchFenceValue(uint64_t value) { m_currentBatchFence = value; }
    
    // Get the underlying VkBuffer for copy operations
    VkBuffer vkBuffer() const { return m_buffer; }
    
    // Check if initialized
    bool isValid() const { return m_buffer != VK_NULL_HANDLE; }

    // Resource tracking
    VkDeviceSize getCapacity() const { return m_size; }
    VkDeviceSize getUsedBytes() const { return m_head; }
    
    // Free all temps whose fence value has been signaled
    void drainTemps(uint64_t lastSignaledValue);

private:
    VkBuffer m_buffer{VK_NULL_HANDLE};
    VkDeviceMemory m_memory{VK_NULL_HANDLE};
    void* m_mapped{nullptr};
    VkDeviceSize m_size{0};
    VkDeviceSize m_head{0};
    
    VkDevice m_device{VK_NULL_HANDLE};
    VkPhysicalDevice m_physicalDevice{VK_NULL_HANDLE};
    
    uint64_t m_currentBatchFence{0};  // Current batch's fence value for fallback tracking
    
    // Fallback temp tracking
    struct TempBuffer {
        VkBuffer buffer;
        VkDeviceMemory memory;
        uint64_t fenceValue;
    };
    std::vector<TempBuffer> m_temps;
    
    // Helper for fallback one-off allocations
    UploadSpan allocateFallback(VkDeviceSize size, VkDeviceSize alignment);
};

````

## src\core\Jobs.cpp

Description: No CC-DESC found.

````cpp
#include "core/Jobs.h"

#include <algorithm>
#include <chrono>
#include <iostream>
#include <iterator>

namespace {
constexpr std::chrono::microseconds kWaitInterval{50};
}

Job::Job(Fn fnPtr, int dependencyCount, void* userData) noexcept
    : fn(fnPtr), user(userData), deps(dependencyCount) {}

JobHandle JobCtx::make(Job::Fn fn, void* user, int deps) {
    if (!system) {
        return {};
    }
    return system->make(fn, user, deps);
}

void JobCtx::schedule(const JobHandle& job) {
    if (!system) {
        return;
    }
    system->schedule(job);
}

JobHandle JobCtx::submit(Job::Fn fn, int deps, void* user) {
    if (!system) {
        return {};
    }
    return system->submit(fn, deps, user);
}

void JobCtx::wait(const JobHandle& handle) {
    if (!system) {
        return;
    }
    system->waitFor(handle, workerIndex);
}

JobSystem::JobSystem() {
    // Stress test: use ALL available threads for maximum parallelism
    auto concurrency = std::max(1u, std::thread::hardware_concurrency());
    workers.reserve(concurrency);
    for (std::size_t i = 0; i < concurrency; ++i) {
        workers.emplace_back(std::make_unique<Worker>());
    }
    threads.reserve(concurrency);
    
    // Initialize per-worker metrics
    m_metrics.workerStats.resize(concurrency);
    
    std::cout << "[JobSystem] Initialized with " << concurrency << " worker threads (STRESS TEST MODE)" << std::endl;

    for (std::size_t i = 0; i < concurrency; ++i) {
        threads.emplace_back([this, i]() {
            workerLoop(i);
        });
    }
}

JobSystem::~JobSystem() {
    shuttingDown.store(true, std::memory_order_release);
    workCv.notify_all();
    for (auto& thread : threads) {
        if (thread.joinable()) {
            thread.join();
        }
    }
}

JobHandle JobSystem::make(Job::Fn fn, void* user, int initialDeps) {
    auto job = std::make_shared<Job>(fn, initialDeps, user);
    return job;
}

JobHandle JobSystem::makeWithPriority(Job::Fn fn, void* user, int initialDeps, int priority) {
    auto job = std::make_shared<Job>(fn, initialDeps, user);
    job->priority = priority;
    return job;
}

void JobSystem::schedule(const JobHandle& job) {
    if (!job) {
        return;
    }

    bool expected = false;
    job->ready.compare_exchange_strong(expected, true, std::memory_order_acq_rel);

    if (job->deps.load(std::memory_order_acquire) == 0) {
        enqueue(job);
    }
}

JobHandle JobSystem::submit(Job::Fn fn, int deps, void* user) {
    auto job = make(fn, user, deps);
    schedule(job);
    return job;
}

void JobSystem::addDependency(const JobHandle& child, const JobHandle& parent) {
    if (!child || !parent) {
        return;
    }

    if (child == parent) {
        return;
    }

    std::scoped_lock lock(child->mutex, parent->mutex);

    if (child->completed) {
        return;
    }

    if (parent->completed) {
        return;
    }

    parent->dependents.emplace_back(child);
    child->deps.fetch_add(1, std::memory_order_acq_rel);
}

void JobSystem::wait(const JobHandle& handle) {
    waitFor(handle, std::nullopt);
}

void JobSystem::workerLoop(std::size_t index) {
    JobCtx ctx{this, index, {}};

    while (true) {
        JobHandle job;
        if (tryPopLocal(index, job) || trySteal(index, job)) {
            ctx.current = job;
            executeJob(job, ctx);
            ctx.current.reset();
            continue;
        }

        std::unique_lock lock(workMutex);
        workCv.wait(lock, [this]() {
            return shuttingDown.load(std::memory_order_acquire) || hasWork();
        });
        if (shuttingDown.load(std::memory_order_acquire) && !hasWork()) {
            break;
        }
    }
}

void JobSystem::executeJob(const JobHandle& job, JobCtx& ctx) {
    if (!job) {
        return;
    }

    auto startTime = std::chrono::steady_clock::now();

    if (job->fn) {
        job->fn(ctx, job->user);
    }

    auto endTime = std::chrono::steady_clock::now();
    auto durationUs = std::chrono::duration_cast<std::chrono::microseconds>(endTime - startTime).count();
    m_metrics.totalJobTimeUs.fetch_add(durationUs, std::memory_order_relaxed);
    m_metrics.totalJobsExecuted.fetch_add(1, std::memory_order_relaxed);
    
    // Track per-worker job execution
    if (ctx.workerIndex < m_metrics.workerStats.size()) {
        m_metrics.workerStats[ctx.workerIndex].jobsExecuted.fetch_add(1, std::memory_order_relaxed);
    }

    std::vector<std::weak_ptr<Job>> dependents;
    {
        std::lock_guard lock(job->mutex);
        dependents = job->dependents;
        job->dependents.clear();
        job->completed = true;
        job->cv.notify_all();
    }

    for (auto& weak : dependents) {
        if (auto child = weak.lock()) {
            int oldDeps = child->deps.fetch_sub(1, std::memory_order_acq_rel);
            if (oldDeps == 1) {
                enqueue(child);
            }
        }
    }
}

void JobSystem::enqueue(const JobHandle& job) {
    if (!job) {
        return;
    }

    if (!job->ready.load(std::memory_order_acquire)) {
        return;
    }

    if (job->queued.exchange(true, std::memory_order_acq_rel)) {
        return;
    }

    auto target = nextWorker.fetch_add(1, std::memory_order_relaxed);
    auto index = target % workers.size();

    {
        std::lock_guard lock(workers[index]->mutex);
        
        // Priority-based insertion: higher priority (lower distance) goes to front
        // Use binary search for O(log N) insertion instead of linear scan
        auto& queue = workers[index]->queue;
        auto insertPos = std::lower_bound(queue.begin(), queue.end(), job,
            [](const JobHandle& existing, const JobHandle& newJob) {
                return existing->priority > newJob->priority;
            });
        queue.insert(insertPos, job);
        
        // Track peak queue depth
        auto queueDepth = queue.size();
        auto currentPeak = m_metrics.peakQueueDepth.load(std::memory_order_relaxed);
        while (queueDepth > currentPeak) {
            if (m_metrics.peakQueueDepth.compare_exchange_weak(currentPeak, queueDepth, 
                                                                std::memory_order_relaxed)) {
                break;
            }
        }
    }

    workCv.notify_one();
}

bool JobSystem::tryPopLocal(std::size_t workerIndex, JobHandle& job) {
    auto& worker = *workers[workerIndex];
    std::lock_guard lock(worker.mutex);
    const auto count = worker.queue.size();
    
    // Update current queue size metric
    if (workerIndex < m_metrics.workerStats.size()) {
        m_metrics.workerStats[workerIndex].currentQueueSize.store(count, std::memory_order_relaxed);
    }
    
    for (std::size_t i = 0; i < count; ++i) {
        auto candidate = worker.queue.front();
        worker.queue.pop_front();
        if (candidate && candidate->ready.load(std::memory_order_acquire) &&
            candidate->deps.load(std::memory_order_acquire) == 0) {
            candidate->queued.store(false, std::memory_order_release);
            job = std::move(candidate);
            return true;
        }
        worker.queue.push_back(std::move(candidate));
    }
    return false;
}

bool JobSystem::trySteal(std::size_t thiefIndex, JobHandle& job) {
    const auto workerCount = workers.size();
    for (std::size_t offset = 1; offset < workerCount; ++offset) {
        const auto victimIndex = (thiefIndex + offset) % workerCount;
        auto& victim = *workers[victimIndex];
        std::lock_guard lock(victim.mutex);
        const auto count = victim.queue.size();
        for (std::size_t i = 0; i < count; ++i) {
            auto candidate = victim.queue.back();
            victim.queue.pop_back();
            if (candidate && candidate->ready.load(std::memory_order_acquire) &&
                candidate->deps.load(std::memory_order_acquire) == 0) {
                candidate->queued.store(false, std::memory_order_release);
                job = std::move(candidate);
                m_metrics.totalSteals.fetch_add(1, std::memory_order_relaxed);
                
                // Track per-worker steals
                if (thiefIndex < m_metrics.workerStats.size()) {
                    m_metrics.workerStats[thiefIndex].jobsStolen.fetch_add(1, std::memory_order_relaxed);
                }
                
                return true;
            }
            victim.queue.push_front(std::move(candidate));
        }
    }
    return false;
}

bool JobSystem::hasWork() const {
    for (const auto& worker : workers) {
        std::scoped_lock lock(worker->mutex);
        if (!worker->queue.empty()) {
            return true;
        }
    }
    return false;
}

void JobSystem::waitFor(const JobHandle& handle, std::optional<std::size_t> workerIndex) {
    if (!handle) {
        return;
    }

    while (true) {
        {
            std::unique_lock lock(handle->mutex);
            if (handle->completed) {
                break;
            }
        }

        JobHandle job;
        bool worked = false;
        if (workerIndex) {
            if (tryPopLocal(*workerIndex, job) || trySteal(*workerIndex, job)) {
                JobCtx ctx{this, *workerIndex, job};
                executeJob(job, ctx);
                worked = true;
            }
        } else {
            const auto workerCount = workers.size();
            for (std::size_t idx = 0; idx < workerCount && !worked; ++idx) {
                if (tryPopLocal(idx, job)) {
                    JobCtx ctx{this, idx, job};
                    executeJob(job, ctx);
                    worked = true;
                    break;
                }
                if (trySteal(idx, job)) {
                    JobCtx ctx{this, idx, job};
                    executeJob(job, ctx);
                    worked = true;
                    break;
                }
            }
        }

        if (worked) {
            continue;
        }

        std::unique_lock lock(handle->mutex);
        if (handle->completed) {
            break;
        }
        handle->cv.wait_for(lock, kWaitInterval);
    }
}

````

## include\core\Jobs.h

Description: No CC-DESC found. C++ class 'JobSystem'.

````cpp
#pragma once

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <deque>
#include <memory>
#include <mutex>
#include <optional>
#include <thread>
#include <vector>

class JobSystem;

struct JobCtx;

struct Job : public std::enable_shared_from_this<Job> {
    using Fn = void(*)(JobCtx&, void*);

    Fn fn{nullptr};
    void* user{nullptr};
    std::atomic<int> deps{0};
    std::atomic<bool> ready{false};
    std::atomic<bool> queued{false};
    int priority{0}; // Higher priority = executed first (distance-based for chunks)

    Job() = default;
    Job(Fn fnPtr, int dependencyCount, void* userData) noexcept;

private:
    friend class JobSystem;
    friend struct JobCtx;

    std::mutex mutex;
    std::condition_variable cv;
    std::vector<std::weak_ptr<Job>> dependents;
    bool completed{false};
};

using JobHandle = std::shared_ptr<Job>;

struct JobCtx {
    JobSystem* system{nullptr};
    std::size_t workerIndex{0};
    JobHandle current;

    JobHandle make(Job::Fn fn, void* user = nullptr, int deps = 0);
    void schedule(const JobHandle& job);
    JobHandle submit(Job::Fn fn, int deps = 0, void* user = nullptr);
    void wait(const JobHandle& handle);
};

struct JobSystemMetrics {
    std::atomic<uint64_t> totalJobsExecuted{0};
    std::atomic<uint64_t> totalSteals{0};
    std::atomic<uint64_t> totalJobTimeUs{0};
    std::atomic<uint64_t> peakQueueDepth{0};
    
    // Per-worker metrics
    struct WorkerStats {
        std::atomic<uint64_t> jobsExecuted{0};
        std::atomic<uint64_t> jobsStolen{0};
        std::atomic<uint64_t> currentQueueSize{0};
        
        WorkerStats() = default;
        WorkerStats(const WorkerStats&) : jobsExecuted{0}, jobsStolen{0}, currentQueueSize{0} {}
        WorkerStats& operator=(const WorkerStats&) { return *this; }
    };
    std::vector<WorkerStats> workerStats;
    
    void reset() {
        totalJobsExecuted.store(0, std::memory_order_relaxed);
        totalSteals.store(0, std::memory_order_relaxed);
        totalJobTimeUs.store(0, std::memory_order_relaxed);
        peakQueueDepth.store(0, std::memory_order_relaxed);
        for (auto& worker : workerStats) {
            worker.jobsExecuted.store(0, std::memory_order_relaxed);
            worker.jobsStolen.store(0, std::memory_order_relaxed);
            worker.currentQueueSize.store(0, std::memory_order_relaxed);
        }
    }
};

class JobSystem {
public:
    JobSystem();
    ~JobSystem();

    JobSystem(const JobSystem&) = delete;
    JobSystem& operator=(const JobSystem&) = delete;

    JobHandle make(Job::Fn fn, void* user = nullptr, int initialDeps = 0);
    JobHandle makeWithPriority(Job::Fn fn, void* user, int initialDeps, int priority);
    void schedule(const JobHandle& job);
    JobHandle submit(Job::Fn fn, int deps = 0, void* user = nullptr);
    void addDependency(const JobHandle& child, const JobHandle& parent);
    void wait(const JobHandle& handle);
    
    JobSystemMetrics& getMetrics() { return m_metrics; }
    const JobSystemMetrics& getMetrics() const { return m_metrics; }

private:
    friend struct JobCtx;
    struct Worker {
        std::deque<JobHandle> queue;
        mutable std::mutex mutex;
    };

    void workerLoop(std::size_t index);
    void executeJob(const JobHandle& job, JobCtx& ctx);
    void enqueue(const JobHandle& job);
    bool tryPopLocal(std::size_t workerIndex, JobHandle& job);
    bool trySteal(std::size_t thiefIndex, JobHandle& job);
    bool hasWork() const;
    void waitFor(const JobHandle& handle, std::optional<std::size_t> workerIndex);

    mutable std::mutex workMutex;
    std::condition_variable workCv;

    std::vector<std::unique_ptr<Worker>> workers;
    std::vector<std::thread> threads;

    std::atomic<bool> shuttingDown{false};
    std::atomic<std::size_t> nextWorker{0};
    
    JobSystemMetrics m_metrics;
};

````

## FIND: FRAME BOTTLENECK

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- src/core/engine/Engine.cpp

Occurrence preview:
- src/core/engine/Engine.cpp:426: out << "=== FRAME BOTTLENECK DIAGNOSTICS REPORT ===\n";
- src/core/engine/Engine.cpp:433: out << "No frame bottleneck samples have been recorded yet.\n";


## FIND: FINALIZE DIAGNOSTICS

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- src/world/WorldDebugMetrics.cpp

Occurrence preview:
- src/world/WorldDebugMetrics.cpp:1: // WorldDebugMetrics.cpp — Debug info assembly + finalize diagnostics report
- src/world/WorldDebugMetrics.cpp:206: ss << "=== FINALIZE DIAGNOSTICS REPORT ===\n";


## FIND: GPU frame

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- src/core/engine/Engine.cpp
- src/core/engine/EngineCleanup.cpp
- src/ui/debug_menu/profiling/FPSProfilerWindow.cpp

Occurrence preview:
- src/core/engine/Engine.cpp:470: << " ms | GPU frame: " << last.gpuFrameMs << " ms\n";
- src/core/engine/Engine.cpp:553: {"GPU frame", last.gpuFrameMs},
- src/core/engine/Engine.cpp:579: out << "GPU frame     " << std::setw(8) << avgOf([](const auto& s) { return s.gpuFrameMs; })
- src/core/engine/Engine.cpp:626: << " | GPU frame delta: " << (last.gpuFrameMs - first.gpuFrameMs)
- src/core/engine/EngineCleanup.cpp:309: std::cerr << "Failed to create timestamp query pool; GPU frame timing unavailable." << std::endl;
- src/ui/debug_menu/profiling/FPSProfilerWindow.cpp:36: // GPU FPS = 1000 / GPU frame time (what the GPU could sustain uncapped)


## FIND: Fence wait

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- src/core/engine/Engine.cpp
- src/core/engine/EngineRenderLoop.cpp

Occurrence preview:
- src/core/engine/Engine.cpp:515: {"Fence wait", last.fenceWaitMs},
- src/core/engine/Engine.cpp:581: out << "Fence wait    " << std::setw(8) << avgOf([](const auto& s) { return s.fenceWaitMs; })
- src/core/engine/Engine.cpp:1142: // CPU work = total minus fence waits AND glfwPoll (OS stall, not engine work)
- src/core/engine/EngineRenderLoop.cpp:165: // NOTE: Fence wait and arena reset now done in mainLoop before World::update


## FIND: Active culling slots

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- src/core/engine/Engine.cpp

Occurrence preview:
- src/core/engine/Engine.cpp:622: out << "Active culling slots delta: "


## FIND: visible draws

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/rendering/culling/GPUCullingSystem.h
- include/ui/debug_menu/world/MinimapCullingReadback.h
- src/core/engine/Engine.cpp
- src/rendering/culling/GPUCullingSlots.cpp

Occurrence preview:
- include/rendering/culling/GPUCullingSystem.h:18: // Maximum visible draws output from GPU culling
- include/rendering/culling/GPUCullingSystem.h:289: uint32_t getMaxDraws() const { return GPU_CULLING_MAX_DRAWS; }  // Max possible visible draws
- include/rendering/culling/GPUCullingSystem.h:613: VkBuffer m_visibleDrawsBuffer{VK_NULL_HANDLE};   // Compacted visible draws (per-frame output)
- include/rendering/culling/GPUCullingSystem.h:619: VkBuffer m_visibleOriginsBuffer{VK_NULL_HANDLE}; // Chunk origins for visible draws
- include/ui/debug_menu/world/MinimapCullingReadback.h:46: * @param maxDraws Maximum number of visible draws to readback
- include/ui/debug_menu/world/MinimapCullingReadback.h:83: * @param drawCountBuffer Source buffer containing the number of visible draws
- src/core/engine/Engine.cpp:496: << ", visible draws " << last.visibleDraws
- src/rendering/culling/GPUCullingSlots.cpp:46: // Visible draws buffer (output, compacted)
- src/rendering/culling/GPUCullingSlots.cpp:58: throw std::runtime_error("Failed to create visible draws buffer!");
- src/rendering/culling/GPUCullingSlots.cpp:71: throw std::runtime_error("Failed to allocate visible draws buffer memory!");


## FIND: terrain/light

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/ui/debug_menu/world/TexturePaintTool.h
- src/core/engine/Engine.cpp
- src/world/edit/TextureOverlayStore.cpp

Occurrence preview:
- include/ui/debug_menu/world/TexturePaintTool.h:30: //     empty because it was a terrain/light fragment bottleneck.
- src/core/engine/Engine.cpp:490: out << "GPU: terrain/light " << last.gpuTerrainMs
- src/core/engine/Engine.cpp:554: {"Terrain/light", last.gpuTerrainMs},
- src/core/engine/Engine.cpp:650: out << "- CPU is likely waiting for GPU completion. The expensive work is probably in GPU frame, terrain/light, shadows, or culling, not finalize.\n";
- src/core/engine/Engine.cpp:656: out << "- Terrain/light pass is expensive. If this grows with texture edits, inspect material shader divergence, active draw count, and shadow sampling counters.\n";
- src/world/edit/TextureOverlayStore.cpp:301: // to probe it from the terrain/light pass.
- src/world/edit/TextureOverlayStore.cpp:1016: // table. With millions of brush cells this becomes a terrain/light-pass


## FIND: timestamp

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/core/engine/Engine.h
- include/rendering/lighting/ShadowSystem.h
- include/vulkan/VulkanContext.h
- src/core/engine/Engine.cpp
- src/core/engine/EngineCleanup.cpp
- src/core/engine/EngineDepthPrePass.cpp
- src/core/engine/EngineTimestamps.cpp
- src/rendering/culling/HiZPyramidDiagnostics.cpp
- src/rendering/lighting/ShadowDiagnostics.cpp
- src/rendering/lighting/ShadowMapRendering.cpp
- src/rendering/lighting/ShadowSystemResources.cpp
- src/ui/debug_menu/world/ChunkDebugWindow.cpp
- src/vulkan/VulkanContext.cpp

Occurrence preview:
- include/core/engine/Engine.h:92: void createTimestampQueryPool();
- include/core/engine/Engine.h:93: void destroyTimestampQueryPool();
- include/core/engine/Engine.h:118: void collectTimestampResults(uint32_t imageIndex);
- include/core/engine/Engine.h:281: VkQueryPool m_timestampQueryPool{VK_NULL_HANDLE};
- include/rendering/lighting/ShadowSystem.h:329: float gpuRenderMs{0.0f};          // GPU shadow render pass (timestamp delta)
- include/rendering/lighting/ShadowSystem.h:493: // Read per-light GPU timestamp results for a completed swapchain image.
- include/rendering/lighting/ShadowSystem.h:841: // Per-image timestamp query pool for point-shadow GPU timings.
- include/rendering/lighting/ShadowSystem.h:844: float m_timestampPeriod{0.0f};
- include/vulkan/VulkanContext.h:42: double timestamp = 0.0;
- include/vulkan/VulkanContext.h:117: * including timestamp period for GPU timing.
- include/vulkan/VulkanContext.h:122: * @param timestampPeriod Output: Nanoseconds per timestamp tick
- include/vulkan/VulkanContext.h:129: float& timestampPeriod,
- src/core/engine/Engine.cpp:995: // GPU timing from timestamp queries
- src/core/engine/Engine.cpp:1242: sample.timestampSeconds = glfwGetTime();
- src/core/engine/Engine.cpp:1632: // createTimestampQueryPool(), destroyTimestampQueryPool() → EngineCleanup.cpp
- src/core/engine/Engine.cpp:1636: // collectTimestampResults(), drawFrame() → EngineRenderLoop.cpp
- src/core/engine/EngineCleanup.cpp:3: //           toggleGPUCulling, createTimestampQueryPool, destroyTimestampQueryPool
- src/core/engine/EngineCleanup.cpp:66: destroyTimestampQueryPool();
- src/core/engine/EngineCleanup.cpp:282: void Engine::createTimestampQueryPool() {
- src/core/engine/EngineCleanup.cpp:283: destroyTimestampQueryPool();
- src/core/engine/EngineDepthPrePass.cpp:23: const uint32_t timestampBase = imageIndex * TIMESTAMPS_PER_IMAGE;
- src/core/engine/EngineDepthPrePass.cpp:95: if (m_timestampQueryPool != VK_NULL_HANDLE) {
- src/core/engine/EngineDepthPrePass.cpp:96: vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, m_timestampQueryPool, timestampBase + 1);
- src/core/engine/EngineDepthPrePass.cpp:132: // No GPU culling - write dummy timestamp for culling phase
- src/core/engine/EngineTimestamps.cpp:1: // EngineTimestamps.cpp - Timestamp query pool management and per-frame GPU timing collection
- src/core/engine/EngineTimestamps.cpp:2: // Contains: collectTimestampResults
- src/core/engine/EngineTimestamps.cpp:8: void Engine::collectTimestampResults(uint32_t imageIndex) {
- src/core/engine/EngineTimestamps.cpp:9: if (m_timestampQueryPool == VK_NULL_HANDLE) {
- src/rendering/culling/HiZPyramidDiagnostics.cpp:39: (sample.timestampSeconds - m_diagnosticsHistory.front().timestampSeconds) > MAX_DIAGNOSTIC_HISTORY_SECONDS;
- src/rendering/culling/HiZPyramidDiagnostics.cpp:57: const double newestTimestamp = m_diagnosticsHistory.back().timestampSeconds;
- src/rendering/culling/HiZPyramidDiagnostics.cpp:58: const double cutoffTimestamp = newestTimestamp - std::max(windowSeconds, 0.0);
- src/rendering/culling/HiZPyramidDiagnostics.cpp:67: if (sample.timestampSeconds < cutoffTimestamp) {
- src/rendering/lighting/ShadowDiagnostics.cpp:43: std::vector<uint64_t> timestamps(lightCount * 2u, 0u);
- src/rendering/lighting/ShadowDiagnostics.cpp:50: static_cast<VkDeviceSize>(timestamps.size() * sizeof(uint64_t)),
- src/rendering/lighting/ShadowDiagnostics.cpp:51: timestamps.data(),
- src/rendering/lighting/ShadowDiagnostics.cpp:71: const uint64_t start = timestamps[slot * 2u + 0u];
- src/rendering/lighting/ShadowMapRendering.cpp:1350: // GPU timestamp: begin sun shadow render
- src/rendering/lighting/ShadowMapRendering.cpp:1352: vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
- src/rendering/lighting/ShadowMapRendering.cpp:1862: // GPU timestamp: end sun shadow render (after barrier)
- src/rendering/lighting/ShadowMapRendering.cpp:1864: vkCmdWriteTimestamp(cmd, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
- src/rendering/lighting/ShadowSystemResources.cpp:188: queryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
- src/rendering/lighting/ShadowSystemResources.cpp:191: throw std::runtime_error("ShadowSystem: failed to create per-light timestamp query pool");
- src/rendering/lighting/ShadowSystemResources.cpp:195: // Sun shadow GPU timing: 2 timestamps (begin/end) per swapchain image.
- src/rendering/lighting/ShadowSystemResources.cpp:201: sunQueryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
- src/ui/debug_menu/world/ChunkDebugWindow.cpp:9: // Build timestamp - automatically updated at compile time
- src/vulkan/VulkanContext.cpp:145: double timestamp = std::chrono::duration<double>(now - g_startTime).count();
- src/vulkan/VulkanContext.cpp:148: msg.timestamp = timestamp;
- src/vulkan/VulkanContext.cpp:254: float& timestampPeriod,
- src/vulkan/VulkanContext.cpp:342: timestampPeriod = deviceProperties.limits.timestampPeriod;


## FIND: diagnostics

Matched code files only; file contents were not exported.
Request exact paths or FUNCTION exports from this list in the next cc.ps1 run.

Matched code files:
- include/rendering/culling/GPUCullingSystem.h
- include/rendering/culling/HiZPyramid.h
- include/rendering/lighting/LightGlowSystem.h
- include/rendering/lighting/ShadowSystem.h
- include/ui/debug_menu/rendering/HiZDebugWindow.h
- include/ui/debug_menu/world/TerrainEditTool.h
- include/ui/debug_menu/world/TexturePaintTool.h
- include/world/chunks/core/ChunkManager.h
- include/world/chunks/streaming/ChunkUploadSystem.h
- include/world/edit/TerrainEditOverlayStore.h
- include/world/edit/TerrainEditRemeshScheduler.h
- include/world/vxm/VxmImport.h
- include/world/World.h
- include/world/WorldDiagnostics.h
- shaders/terrain/backupAOupdate.frag
- shaders/terrain/cube.frag
- src/core/engine/Engine.cpp
- src/core/engine/EngineCommandBuffer.cpp
- src/rendering/culling/GPUCullingSystem.cpp
- src/rendering/lighting/ShadowDiagnostics.cpp
- src/rendering/lighting/ShadowSystemUpdate.cpp
- src/ui/debug_menu/profiling/TerminalOutputWindow.cpp
- src/ui/debug_menu/world/ChunkDebugWindow.cpp
- src/ui/debug_menu/world/ChunkVramWindow.cpp
- src/ui/debug_menu/world/ObjectManagerWindow.cpp
- src/ui/debug_menu/world/TexturePaintTool.cpp
- src/vulkan/VulkanContext.cpp
- src/world/chunks/core/ChunkManager.cpp
- src/world/edit/TerrainEditMesher.cpp
- src/world/edit/TerrainEditRemeshScheduler.cpp
- src/world/WorldDebugMetrics.cpp
- src/world/WorldLODSwaps.cpp
- src/world/WorldLODTransitions.cpp
- src/world/WorldRendering.cpp
- src/world/WorldSnapshots.cpp
- src/world/WorldTerrainEditCollision.cpp
- src/world/WorldUpdate.cpp

Occurrence preview:
- include/rendering/culling/GPUCullingSystem.h:2: // GPT-DESC: Declares GPU-driven chunk culling buffers, push constants, and diagnostics.
- include/rendering/culling/GPUCullingSystem.h:207: * CPU-side instrumentation hook for per-slot debug diagnostics.
- include/rendering/culling/GPUCullingSystem.h:428: //  Terrain-edit visibility transition diagnostics
- include/rendering/culling/GPUCullingSystem.h:747: // Terrain-edit visibility diagnostics.
- include/rendering/culling/HiZPyramid.h:135: enum class DiagnosticsMode : uint32_t {
- include/rendering/culling/HiZPyramid.h:141: struct DiagnosticsSample {
- include/rendering/culling/HiZPyramid.h:143: DiagnosticsMode mode = DiagnosticsMode::FrustumOnly;
- include/rendering/culling/HiZPyramid.h:172: // Camera state for corruption diagnostics
- include/rendering/lighting/LightGlowSystem.h:145: const std::vector<OrbDiag>& getOrbDiagnostics() const { return m_orbDiags; }
- include/rendering/lighting/LightGlowSystem.h:150: // Format all orb diagnostics
- include/rendering/lighting/LightGlowSystem.h:151: std::string formatAllDiagnostics() const;
- include/rendering/lighting/ShadowSystem.h:98: glm::vec4 diagConfig{0.0f};  // x=enableDetailedDiagnostics, y=debugMode, z=sunAreaRadius, w=activeElevationFade
- include/rendering/lighting/ShadowSystem.h:140: struct LightDiagnostics {
- include/rendering/lighting/ShadowSystem.h:195: struct FrameDiagnostics {
- include/rendering/lighting/ShadowSystem.h:422: // ── Rolling-window aggregated sun shadow diagnostics ────────────
- include/ui/debug_menu/rendering/HiZDebugWindow.h:47: // Edit-visibility diagnostics UI state.
- include/ui/debug_menu/world/TerrainEditTool.h:9: // Forward declare for diagnostics pointer
- include/ui/debug_menu/world/TexturePaintTool.h:3: // GPT-DESC: Declares texture paint tool state, diagnostics, and debounced material rebake controls.
- include/ui/debug_menu/world/TexturePaintTool.h:177: std::string buildPaintDiagnosticsReport() const;
- include/ui/debug_menu/world/TexturePaintTool.h:239: // Last-paint diagnostics
- include/ui/debug_menu/world/TexturePaintTool.h:245: std::string m_lastDiagnosticsExport;
- include/world/chunks/core/ChunkManager.h:212: * Reset the effective-distance warmup timer used by diagnostics.
- include/world/chunks/streaming/ChunkUploadSystem.h:81: // Optional diagnostics callback for upload/finalize/LOD pipeline drops.
- include/world/edit/TerrainEditOverlayStore.h:90: /// Deferred fill list sizes (for diagnostics).
- include/world/edit/TerrainEditRemeshScheduler.h:141: /** Per-chunk timing record for pipeline breakdown diagnostics. */
- include/world/vxm/VxmImport.h:18: // Returns true on success; call error() for diagnostics.
- include/world/World.h:25: #include "world/WorldDiagnostics.h"
- include/world/World.h:66: // ---- Diagnostic / stats / history types live in WorldDiagnostics.h ----
- include/world/World.h:91: // --- Terrain edit diagnostics: see WorldDiag::TerrainEditDiag / TerrainEditStats ---
- include/world/World.h:103: // --- Load management diagnostics: see WorldDiag::LoadManagementDiag ---
- include/world/WorldDiagnostics.h:4: // WorldDiagnostics.h — Extracted diagnostic / history / stats structs from
- include/world/WorldDiagnostics.h:37: // --- Terrain edit diagnostics (per-step ms timings) ---
- include/world/WorldDiagnostics.h:408: // --- Finalize diagnostics (for debugging world update spikes) ---
- include/world/WorldDiagnostics.h:445: // --- LOD Switch diagnostics (populated by setDataLODForBand + worldUpdate) ---
- shaders/terrain/backupAOupdate.frag:77: // Shadow data SSBO (sun + point light shadow matrices and config + diagnostics)
- shaders/terrain/backupAOupdate.frag:87: vec4 diagConfig;             // x=enableDetailedDiagnostics
- shaders/terrain/cube.frag:78: // Shadow data SSBO (sun + point light shadow matrices and config + diagnostics)
- shaders/terrain/cube.frag:88: vec4 diagConfig;             // x=enableDetailedDiagnostics
- src/core/engine/Engine.cpp:359: const auto& shadowFrame = m_shadowSystem.getFrameDiagnostics();
- src/core/engine/Engine.cpp:364: const auto& sunDiag = m_shadowSystem.getSunShadowDiagnostics();
- src/core/engine/Engine.cpp:426: out << "=== FRAME BOTTLENECK DIAGNOSTICS REPORT ===\n";
- src/core/engine/Engine.cpp:785: // still recorded in m_lastCpuFrameMs for diagnostics.
- src/core/engine/EngineCommandBuffer.cpp:177: m_hiZTimingModeByImage.assign(m_swapchainImages.size(), HiZPyramid::DiagnosticsMode::FrustumOnly);
- src/core/engine/EngineCommandBuffer.cpp:208: // Always compute camera delta for diagnostics (even when temporal isn't checked)
- src/core/engine/EngineCommandBuffer.cpp:266: usedTemporalHiZ ? HiZPyramid::DiagnosticsMode::TemporalHiZ
- src/core/engine/EngineCommandBuffer.cpp:267: : HiZPyramid::DiagnosticsMode::FrustumOnly;
- src/rendering/culling/GPUCullingSystem.cpp:713: // y marks motion/edit frames for diagnostics/future tuning; motion safety comes
- src/rendering/lighting/ShadowDiagnostics.cpp:29: m_frameDiagnostics.terrainPassGpuMs = m_lastTerrainPassGpuMs;
- src/rendering/lighting/ShadowDiagnostics.cpp:30: m_frameDiagnostics.totalShadowGpuMs = 0.0f;
- src/rendering/lighting/ShadowDiagnostics.cpp:31: m_frameDiagnostics.avgShadowGpuMsPerLight = 0.0f;
- src/rendering/lighting/ShadowDiagnostics.cpp:32: m_frameDiagnostics.terrainMsPerMegaShadowSample = 0.0f;
- src/rendering/lighting/ShadowSystemUpdate.cpp:80: if (m_lightDiagnostics.size() < lightCount) {
- src/rendering/lighting/ShadowSystemUpdate.cpp:81: m_lightDiagnostics.resize(lightCount);
- src/rendering/lighting/ShadowSystemUpdate.cpp:82: } else if (m_lightDiagnostics.size() > lightCount) {
- src/rendering/lighting/ShadowSystemUpdate.cpp:83: m_lightDiagnostics.resize(lightCount);
- src/ui/debug_menu/profiling/TerminalOutputWindow.cpp:40: // --- Runtime diagnostics ---
- src/ui/debug_menu/profiling/TerminalOutputWindow.cpp:41: ImGui::TextColored(ImVec4(0.5f, 0.9f, 1.0f, 1.0f), "Runtime Diagnostics");
- src/ui/debug_menu/world/ChunkDebugWindow.cpp:215: // Finalize diagnostics
- src/ui/debug_menu/world/ChunkDebugWindow.cpp:217: if (ImGui::CollapsingHeader("Finalize Diagnostics")) {
- src/ui/debug_menu/world/ChunkVramWindow.cpp:25: // NOTE: text-builder methods (buildChunkDiagnosticsText, buildChunkVisualHistoryText,
- src/ui/debug_menu/world/ChunkVramWindow.cpp:298: const std::string text = buildChunkDiagnosticsText(chunkNumber, info);
- src/ui/debug_menu/world/ChunkVramWindow.cpp:302: ImGui::SetTooltip("Copy this chunk's diagnostics");
- src/ui/debug_menu/world/ChunkVramWindow.cpp:833: const std::string text = buildChunkDiagnosticsText(chunkIndex, chunkInfo);
- src/ui/debug_menu/world/ObjectManagerWindow.cpp:12: if (mask == ShadowSystem::LightDiagnostics::CULL_NONE) {
- src/ui/debug_menu/world/ObjectManagerWindow.cpp:38: if ((mask & ShadowSystem::LightDiagnostics::CULL_CASTS_SHADOW_DISABLED) != 0u) {
- src/ui/debug_menu/world/ObjectManagerWindow.cpp:41: if ((mask & ShadowSystem::LightDiagnostics::CULL_INVALID_RADIUS_OR_INTENSITY) != 0u) {
- src/ui/debug_menu/world/ObjectManagerWindow.cpp:44: if ((mask & ShadowSystem::LightDiagnostics::CULL_BEHIND_CAMERA) != 0u) {
- src/ui/debug_menu/world/TexturePaintTool.cpp:1: // GPT-DESC: Implements texture paint brush authoring, diagnostics, and debounced material rebake scheduling.
- src/ui/debug_menu/world/TexturePaintTool.cpp:169: std::string TexturePaintTool::buildPaintDiagnosticsReport() const {
- src/ui/debug_menu/world/TexturePaintTool.cpp:245: appendLine("VulkanVX Texture Paint Brush Diagnostics");
- src/ui/debug_menu/world/TexturePaintTool.cpp:1029: // ---------------- Last paint diagnostics ----------------
- src/vulkan/VulkanContext.cpp:377: deviceFeatures.fragmentStoresAndAtomics = VK_TRUE;  // Required for shadow diagnostics atomics
- src/world/chunks/core/ChunkManager.cpp:368: // diagnostics, but movement no longer shrinks render distance to satisfy a
- src/world/edit/TerrainEditMesher.cpp:655: // Tier B Phase 1: record requested band so diagnostics can compare against
- src/world/edit/TerrainEditMesher.cpp:1109: // Count solid voxels in the cache for diagnostics
- src/world/edit/TerrainEditRemeshScheduler.cpp:870: // Record per-chunk timing for pipeline breakdown diagnostics
- src/world/WorldDebugMetrics.cpp:1: // WorldDebugMetrics.cpp — Debug info assembly + finalize diagnostics report
- src/world/WorldDebugMetrics.cpp:206: ss << "=== FINALIZE DIAGNOSTICS REPORT ===\n";
- src/world/WorldLODSwaps.cpp:1: // WorldLODSwaps.cpp — LOD mesh release/reload, LOD batch swap processing, diagnostics
- src/world/WorldLODSwaps.cpp:380: // Accumulate swap errors into LOD switch diagnostics
- src/world/WorldLODSwaps.cpp:570: // Capture visual-ready diagnostics before removing PendingMeshHandle.
- src/world/WorldLODSwaps.cpp:721: r += "=== LOD Switch Diagnostics ===\n";
- src/world/WorldLODTransitions.cpp:1098: // Capture visual-ready diagnostics before removing PendingMeshHandle.
- src/world/WorldLODTransitions.cpp:1306: r += "=== LOD Switch Diagnostics ===\n";
- src/world/WorldRendering.cpp:251: SunCascadeGatherDiagnostics* diagnostics,
- src/world/WorldRendering.cpp:257: if (diagnostics) {
- src/world/WorldRendering.cpp:258: *diagnostics = SunCascadeGatherDiagnostics{};
- src/world/WorldRendering.cpp:259: diagnostics->cascadeCount = cascadeCount;
- src/world/WorldSnapshots.cpp:872: // Reset diagnostics for this edit
- src/world/WorldSnapshots.cpp:957: // Record deferred fill list sizes for diagnostics.
- src/world/WorldTerrainEditCollision.cpp:281: // explicit geometry-diff diagnostics or proactive ghost-geometry audits.
- src/world/WorldTerrainEditCollision.cpp:560: // Capture overlay fill state at edit time for diagnostics
- src/world/WorldUpdate.cpp:84: // Reset per-frame diagnostics, then let processFinalizeQueue + processLODSwaps populate it
- src/world/WorldUpdate.cpp:174: // Copy edit-path collision timing into the edit diagnostics struct
