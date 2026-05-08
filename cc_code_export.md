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


## src\core\engine\EngineCleanup.cpp

Description: No CC-DESC found.

````cpp
// EngineCleanup.cpp - Vulkan resource lifecycle management extracted from Engine.cpp
// Contains: cleanup, cleanupSwapchain, recreateSwapchain, toggleFullscreen,
//           toggleGPUCulling, createTimestampQueryPool, destroyTimestampQueryPool

#include "core/engine/Engine.h"
#include "ui/EngineInterface.h"
#include "ui/debug_menu/IconManagerForDebug.h"
#include "ui/debug_menu/world/ChunkMinimapWindow.h"
#include "world/chunks/core/Chunk.h"  // For MeshHandle, AABB
#include <exception>
#include <iostream>

void Engine::cleanup(){
    // Save all debug window settings before destroying anything
    saveSettings();

    // wait
    vkDeviceWaitIdle(m_device);

    // Destroy gameplay window before other Vulkan resources
    m_gameplayPixelPass.cleanup();
    m_gameplayTJunctionFix.cleanup();
    m_gameplayWindowSwapchainGeneration = 0;
    m_gameplayPixelPassSwapchainGeneration = 0;
    if (m_gameplayWindow) {
        m_input.setGameplayWindow(nullptr);
        m_gameplayWindow->destroy(m_instance, m_device);
        m_gameplayWindow.reset();
    }

    // Minimap texture descriptors are owned by ImGui contexts, so release the
    // texture resources before shutting ImGui down.
    m_world.getDebugOverlay().getChunkMinimapWindow().cleanupVulkanResources();
    IconManagerForDebug::instance().cleanup();
    m_imgui.cleanup(m_device);  // Cleanup ImGui before destroying Vulkan resources
    
    // Cleanup physics and player systems
    m_player.shutdown();
    m_physics.shutdown();
    
    // Cleanup cloud system
    m_cloudSystem.cleanup(m_device);
    
    // Cleanup celestial system
    m_celestialSystem.cleanup(m_device);
    
    // Cleanup star field system
    m_starSystem.cleanup(m_device);
    
    // Cleanup sky gradient system
    m_skySystem.cleanup(m_device);
    
    // Cleanup light glow system
    m_lightGlowSystem.cleanup(m_device);
    
    // Cleanup shadow system (point shadow maps + metadata buffers)
    m_shadowSystem.cleanup();

    // Cleanup T-junction fix system
    m_tjunctionFix.cleanup();
    m_pixelPass.cleanup();
    
    // Cleanup Hi-Z depth pyramid
    m_hiZPyramid.cleanup();

    destroyTimestampQueryPool();

    Pipeline::destroyFramebuffers(m_device, m_swapchainFramebuffers);
    if (m_graphicsPipeline) vkDestroyPipeline(m_device, m_graphicsPipeline, nullptr);
    if (m_graphicsPipelineDepthLoad) vkDestroyPipeline(m_device, m_graphicsPipelineDepthLoad, nullptr);
    if (m_depthPrePassPipeline) vkDestroyPipeline(m_device, m_depthPrePassPipeline, nullptr);
    if (m_pipelineLayout) vkDestroyPipelineLayout(m_device, m_pipelineLayout, nullptr);
    if (m_dccmPipeline) vkDestroyPipeline(m_device, m_dccmPipeline, nullptr);
    if (m_dccmPipelineDepthLoad) vkDestroyPipeline(m_device, m_dccmPipelineDepthLoad, nullptr);
    if (m_dccmPipelineLayout) vkDestroyPipelineLayout(m_device, m_dccmPipelineLayout, nullptr);
    
    // GPT_CHANGE: Save and destroy pipeline cache
    if (m_pipelineCache != VK_NULL_HANDLE) {
        Pipeline::savePipelineCache(m_device, m_pipelineCache);
        vkDestroyPipelineCache(m_device, m_pipelineCache, nullptr);
    }
    
    if (m_renderPass) vkDestroyRenderPass(m_device, m_renderPass, nullptr);
    if (m_renderPassDepthPrepass) vkDestroyRenderPass(m_device, m_renderPassDepthPrepass, nullptr);
    if (m_renderPassDepthLoad) vkDestroyRenderPass(m_device, m_renderPassDepthLoad, nullptr);
    if (m_uiRenderPass) vkDestroyRenderPass(m_device, m_uiRenderPass, nullptr);

    // GPT_CHANGE: Cleanup depth resources
    if (m_depthView) vkDestroyImageView(m_device, m_depthView, nullptr);
    if (m_depthImage) vkDestroyImage(m_device, m_depthImage, nullptr);
    if (m_depthMemory) vkFreeMemory(m_device, m_depthMemory, nullptr);

    for (auto view : m_swapchainImageViews) vkDestroyImageView(m_device, view, nullptr);
    vkDestroySwapchainKHR(m_device, m_swapchain, nullptr);

    if (m_descriptorPool) vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
    if (m_descriptorSetLayout) vkDestroyDescriptorSetLayout(m_device, m_descriptorSetLayout, nullptr);

    // GPT_CHANGE: Unmap memory before freeing
    for (size_t i = 0; i < m_uniformBuffers.size(); ++i){
        if (m_uniformMapped[i]) {
            vkUnmapMemory(m_device, m_uniformBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_uniformBuffers[i], nullptr);
        vkFreeMemory(m_device, m_uniformBuffersMemory[i], nullptr);
    }
    
    // Cleanup lighting buffers
    for (size_t i = 0; i < m_lightingBuffers.size(); ++i){
        if (m_lightingMapped[i]) {
            vkUnmapMemory(m_device, m_lightingBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_lightingBuffers[i], nullptr);
        vkFreeMemory(m_device, m_lightingBuffersMemory[i], nullptr);
    }
    
    // Cleanup clustered lighting buffers
    m_clusteredLighting.cleanup();
    
    // Cleanup camera buffers
    for (size_t i = 0; i < m_cameraBuffers.size(); ++i){
        if (m_cameraMapped[i]) {
            vkUnmapMemory(m_device, m_cameraBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_cameraBuffers[i], nullptr);
        vkFreeMemory(m_device, m_cameraBuffersMemory[i], nullptr);
    }
    
    // Cleanup AO buffers
    for (size_t i = 0; i < m_aoBuffers.size(); ++i){
        if (m_aoMapped[i]) {
            vkUnmapMemory(m_device, m_aoBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_aoBuffers[i], nullptr);
        vkFreeMemory(m_device, m_aoBuffersMemory[i], nullptr);
    }

    // Cleanup sparse texture-material overlay buffers
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
    m_materialOverlayBuffers.clear();
    m_materialOverlayBuffersMemory.clear();
    m_materialOverlayMapped.clear();
    m_materialOverlayImageDirty.clear();
    m_materialOverlayImageDirtySlots.clear();
    m_materialOverlayTable.clear();
    m_materialOverlayCapacity = 0;
    m_materialOverlayCount = 0;
    m_materialOverlayMaxProbe = 0;
    m_materialOverlayBufferSize = 0;
    m_materialOverlayNeedsRebuild = true;
    m_materialOverlayLastGeneration = 0;
    // PHASE B7: Cleanup indirect buffer
    if (m_indirectBuffer) vkDestroyBuffer(m_device, m_indirectBuffer, nullptr);
    if (m_indirectMemory) vkFreeMemory(m_device, m_indirectMemory, nullptr);
    if (m_chunkOriginsBuffer) vkDestroyBuffer(m_device, m_chunkOriginsBuffer, nullptr);
    if (m_chunkOriginsMemory) vkFreeMemory(m_device, m_chunkOriginsMemory, nullptr);
    
    // Cleanup minimap culling readback
    m_minimapReadback.cleanup();
    
    // GPT_CHANGE: Free buffer slices before destroying allocators
    if (m_cubeVB.isValid()) {
        m_vbAllocator.free(m_cubeVB);
    }
    if (m_cubeIB.isValid()) {
        m_ibAllocator.free(m_cubeIB);
    }
    
    // Cleanup world mesh resources
    auto& registry = m_world.getRegistry();
    auto view = registry.view<MeshHandle>();
    for (auto entity : view) {
        auto& mesh = view.get<MeshHandle>(entity);
        std::vector<BufferSlice> vbSlices;
        std::vector<BufferSlice> ibSlices;
        mesh.collectBufferSlices(vbSlices, ibSlices);
        if (!vbSlices.empty()) m_vbAllocator.freeBatch(vbSlices.data(), vbSlices.size());
        if (!ibSlices.empty()) m_ibAllocator.freeBatch(ibSlices.data(), ibSlices.size());
    }
    
    // GPT_CHANGE: Destroy allocators
    m_vbAllocator.destroy(m_device);
    m_ibAllocator.destroy(m_device);
    for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        m_uploadArenas[i].destroy(m_device);
    }

    // GPT_CHANGE: Cleanup per-frame synchronization objects
    for (size_t i = 0; i < m_frames.size(); ++i){
        vkDestroySemaphore(m_device, m_frames[i].imageAvailable, nullptr);
        vkDestroySemaphore(m_device, m_frames[i].renderFinishedMain, nullptr);
        vkDestroySemaphore(m_device, m_frames[i].renderFinishedGameplay, nullptr);
        vkDestroyFence(m_device, m_frames[i].inFlight, nullptr);
    }
    
    // C3.2: Cleanup timeline semaphores
    if (m_uploadTimeline) vkDestroySemaphore(m_device, m_uploadTimeline, nullptr);
    if (m_hiZTimeline) vkDestroySemaphore(m_device, m_hiZTimeline, nullptr);

    m_parallelRecorder.cleanup();
    if (m_commandPool) vkDestroyCommandPool(m_device, m_commandPool, nullptr);
    vkDestroyDevice(m_device, nullptr);
    vkDestroySurfaceKHR(m_instance, m_surface, nullptr);
    
    // Destroy debug messenger before instance
    VulkanContext::destroyDebugMessenger(m_instance, m_debugMessenger);
    
    vkDestroyInstance(m_instance, nullptr);
    m_cursorManager.cleanup();
    if (m_window) glfwDestroyWindow(m_window);
    glfwTerminate();
}

void Engine::toggleFullscreen() {
    if (m_isFullscreen) {
        // Switch to windowed mode
        glfwSetWindowMonitor(m_window, nullptr, m_windowedPosX, m_windowedPosY, 
                            m_windowedWidth, m_windowedHeight, GLFW_DONT_CARE);
        m_isFullscreen = false;
        std::cout << "[Engine] Switched to windowed mode (" << m_windowedWidth << "x" << m_windowedHeight << ")\n";
    } else {
        // Save current window position and size before going fullscreen
        glfwGetWindowPos(m_window, &m_windowedPosX, &m_windowedPosY);
        glfwGetWindowSize(m_window, &m_windowedWidth, &m_windowedHeight);
        
        // Find which monitor the window is currently on (not always primary)
        GLFWmonitor* targetMonitor = glfwGetPrimaryMonitor();
        int winX, winY, winW, winH;
        glfwGetWindowPos(m_window, &winX, &winY);
        glfwGetWindowSize(m_window, &winW, &winH);
        int cx = winX + winW / 2, cy = winY + winH / 2;
        int monCount = 0;
        GLFWmonitor** monitors = glfwGetMonitors(&monCount);
        for (int i = 0; i < monCount; ++i) {
            int mx, my;
            glfwGetMonitorPos(monitors[i], &mx, &my);
            const GLFWvidmode* vm = glfwGetVideoMode(monitors[i]);
            if (vm && cx >= mx && cx < mx + vm->width && cy >= my && cy < my + vm->height) {
                targetMonitor = monitors[i];
                break;
            }
        }
        
        // Switch to exclusive fullscreen on the detected monitor
        const GLFWvidmode* mode = glfwGetVideoMode(targetMonitor);
        glfwSetWindowMonitor(m_window, targetMonitor, 0, 0, 
                            mode->width, mode->height, mode->refreshRate);
        m_isFullscreen = true;
        std::cout << "[Engine] Switched to fullscreen mode (" << mode->width << "x" << mode->height 
                  << " @ " << mode->refreshRate << " Hz)\n";
    }
    
    // Update internal dimensions
    glfwGetFramebufferSize(m_window, &m_width, &m_height);
    
    // Recreate swapchain for new window size
    recreateSwapchain();
}

void Engine::toggleGPUCulling() {
    const bool beforeGpuMode = m_gpuCullingEnabled;
    const bool afterGpuMode = !m_gpuCullingEnabled;
    beginGModeGeometryDiffCapture(beforeGpuMode, afterGpuMode);
    m_gpuCullingEnabled = afterGpuMode;
    std::cout << "[Engine] GPU culling " << (m_gpuCullingEnabled ? "ENABLED" : "DISABLED")
              << " | geometry diff capture #" << m_gModeGeometryToggleSerial
              << " (" << GPUCullingSystem::cullingModeName(beforeGpuMode)
              << " -> " << GPUCullingSystem::cullingModeName(afterGpuMode) << ")"
              << std::endl;
}

void Engine::createTimestampQueryPool() {
    destroyTimestampQueryPool();

    if (m_device == VK_NULL_HANDLE || m_swapchainImages.empty()) {
        return;
    }

    if (m_perfMode) {
        return;
    }

    if (m_deviceProperties.limits.timestampComputeAndGraphics == VK_FALSE) {
        std::cout << "[Timing] GPU timestamps not supported on this device; skipping instrumentation." << std::endl;
        return;
    }

    VkQueryPoolCreateInfo queryInfo{};
    queryInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
    queryInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
    // TIMESTAMPS_PER_IMAGE queries per swapchain image for frame/culling/terrain/cube timing
    queryInfo.queryCount = static_cast<uint32_t>(m_swapchainImages.size() * TIMESTAMPS_PER_IMAGE);

    if (queryInfo.queryCount == 0) {
        return;
    }

    if (vkCreateQueryPool(m_device, &queryInfo, nullptr, &m_timestampQueryPool) != VK_SUCCESS) {
        std::cerr << "Failed to create timestamp query pool; GPU frame timing unavailable." << std::endl;
        m_timestampQueryPool = VK_NULL_HANDLE;
    }
}

void Engine::destroyTimestampQueryPool() {
    if (m_timestampQueryPool != VK_NULL_HANDLE) {
        vkDestroyQueryPool(m_device, m_timestampQueryPool, nullptr);
        m_timestampQueryPool = VK_NULL_HANDLE;
    }
}

void Engine::syncGameplayTJunctionFix(bool forceRecreate) {
    if (!m_gameplayWindow || !m_gameplayWindow->isOpen() ||
        m_gameplayWindow->getSwapchain() == VK_NULL_HANDLE ||
        m_gameplayWindow->getImageViews().empty()) {
        if (m_gameplayTJunctionFix.isReady()) {
            m_gameplayTJunctionFix.cleanup();
        }
        m_gameplayWindowSwapchainGeneration = 0;
        syncHiZTarget(forceRecreate);
        return;
    }

    const uint64_t generation = m_gameplayWindow->getSwapchainGeneration();
    if (!forceRecreate &&
        m_gameplayTJunctionFix.isReady() &&
        generation == m_gameplayWindowSwapchainGeneration) {
        m_gameplayTJunctionFix.setEnabled(m_tjunctionFix.isEnabled());
        m_gameplayTJunctionFix.setDepthThreshold(m_tjunctionFix.getDepthThreshold());
        syncHiZTarget(false);
        return;
    }

    try {
        if (m_gameplayTJunctionFix.isReady()) {
            m_gameplayTJunctionFix.recreate(
                m_gameplayWindow->getFormat(),
                m_depthFormat,
                m_gameplayWindow->getExtent(),
                m_gameplayWindow->getImageViews());
        } else {
            m_gameplayTJunctionFix.init(
                m_device,
                m_physicalDevice,
                m_gameplayWindow->getFormat(),
                m_depthFormat,
                m_gameplayWindow->getExtent(),
                m_gameplayWindow->getImageViews());
        }

        m_gameplayTJunctionFix.setEnabled(m_tjunctionFix.isEnabled());
        m_gameplayTJunctionFix.setDepthThreshold(m_tjunctionFix.getDepthThreshold());
        m_gameplayWindowSwapchainGeneration = generation;
        syncHiZTarget(true);
    } catch (const std::exception& ex) {
        std::cerr << "[Engine] Failed to initialize detached T-junction fix: "
                  << ex.what() << std::endl;

        if (m_gameplayTJunctionFix.isReady()) {
            m_gameplayTJunctionFix.cleanup();
        }
        m_gameplayWindowSwapchainGeneration = 0;

        if (m_gameplayWindow) {
            m_input.setGameplayWindow(nullptr);
            m_gameplayWindow->destroy(m_instance, m_device);
            m_gameplayWindow.reset();
        }

        m_gameplaySeparated = false;
        if (m_input.areDebugWindowsVisible() && m_world.getDebugOverlay().isUsingEngineInterface()) {
            m_world.getDebugOverlay().getEngineInterface()
                .setGameplayState(EngineInterface::GameplayState::Embedded);
        }
        syncHiZTarget(true);
    }
}

void Engine::syncGameplayPixelPass(bool forceRecreate) {
    if (!m_gameplayWindow || !m_gameplayWindow->isOpen() ||
        m_gameplayWindow->getSwapchain() == VK_NULL_HANDLE ||
        m_gameplayWindow->getImageViews().empty()) {
        if (m_gameplayPixelPass.isReady()) {
            m_gameplayPixelPass.cleanup();
        }
        m_gameplayPixelPassSwapchainGeneration = 0;
        return;
    }

    const uint64_t generation = m_gameplayWindow->getSwapchainGeneration();
    if (!forceRecreate &&
        m_gameplayPixelPass.isReady() &&
        generation == m_gameplayPixelPassSwapchainGeneration) {
        m_gameplayPixelPass.getSettings() = m_pixelPass.getSettings();
        return;
    }

    try {
        if (m_gameplayPixelPass.isReady()) {
            m_gameplayPixelPass.recreate(
                m_gameplayWindow->getFormat(),
                m_depthFormat,
                m_gameplayWindow->getExtent(),
                m_gameplayWindow->getImageViews());
        } else {
            m_gameplayPixelPass.init(
                m_device,
                m_physicalDevice,
                m_gameplayWindow->getFormat(),
                m_depthFormat,
                m_gameplayWindow->getExtent(),
                m_gameplayWindow->getImageViews());
        }

        m_gameplayPixelPass.getSettings() = m_pixelPass.getSettings();
        m_gameplayPixelPassSwapchainGeneration = generation;
    } catch (const std::exception& ex) {
        std::cerr << "[Engine] Failed to initialize detached retro pixel pass: "
                  << ex.what() << std::endl;

        if (m_gameplayPixelPass.isReady()) {
            m_gameplayPixelPass.cleanup();
        }
        if (m_gameplayTJunctionFix.isReady()) {
            m_gameplayTJunctionFix.cleanup();
        }
        m_gameplayWindowSwapchainGeneration = 0;
        m_gameplayPixelPassSwapchainGeneration = 0;

        if (m_gameplayWindow) {
            m_input.setGameplayWindow(nullptr);
            m_gameplayWindow->destroy(m_instance, m_device);
            m_gameplayWindow.reset();
        }

        m_gameplaySeparated = false;
        if (m_input.areDebugWindowsVisible() && m_world.getDebugOverlay().isUsingEngineInterface()) {
            m_world.getDebugOverlay().getEngineInterface()
                .setGameplayState(EngineInterface::GameplayState::Embedded);
        }
        syncHiZTarget(true);
    }
}

void Engine::syncHiZTarget(bool forceRecreate) {
    if (!m_hiZPyramid.isReady()) {
        return;
    }

    VkImageView targetDepthView = m_depthView;
    uint32_t targetWidth = m_swapchainExtent.width;
    uint32_t targetHeight = m_swapchainExtent.height;

    const bool useGameplayTarget =
        m_gameplaySeparated &&
        m_gameplayWindow &&
        m_gameplayWindow->isOpen() &&
        m_gameplayWindow->getSwapchain() != VK_NULL_HANDLE;
    const bool useGameplayPixelPass =
        useGameplayTarget &&
        m_gameplayPixelPass.isReady() &&
        m_gameplayPixelPass.getSettings().enabled;
    const bool useMainPixelPass =
        !useGameplayTarget &&
        m_pixelPass.isReady() &&
        m_pixelPass.getSettings().enabled;
    if (useGameplayPixelPass) {
        targetDepthView = m_gameplayPixelPass.getOffscreenDepthView();
        targetWidth = m_gameplayWindow->getExtent().width;
        targetHeight = m_gameplayWindow->getExtent().height;
    } else if (useGameplayTarget) {
        targetDepthView = m_gameplayWindow->getDepthView();
        targetWidth = m_gameplayWindow->getExtent().width;
        targetHeight = m_gameplayWindow->getExtent().height;
    } else if (useMainPixelPass) {
        targetDepthView = m_pixelPass.getOffscreenDepthView();
        targetWidth = m_swapchainExtent.width;
        targetHeight = m_swapchainExtent.height;
    }

    if (targetDepthView == VK_NULL_HANDLE || targetWidth == 0 || targetHeight == 0) {
        return;
    }

    auto previousPowerOfTwo = [](uint32_t v) {
        if (v <= 1u) {
            return 1u;
        }
        v |= v >> 1u;
        v |= v >> 2u;
        v |= v >> 4u;
        v |= v >> 8u;
        v |= v >> 16u;
        return (v >> 1u) + 1u;
    };

    const bool sizeMismatch =
        m_hiZPyramid.getWidth() != previousPowerOfTwo(targetWidth) ||
        m_hiZPyramid.getHeight() != previousPowerOfTwo(targetHeight);

    if (forceRecreate || sizeMismatch) {
        vkDeviceWaitIdle(m_device);
        m_hiZPyramid.resize(targetDepthView, targetWidth, targetHeight);
        m_gpuCulling.bindHiZPyramid(m_hiZPyramid.getImageView(), m_hiZPyramid.getSampler());
    } else {
        m_hiZPyramid.updateDepthSource(targetDepthView);
    }
}

// GPT_CHANGE: Cleanup swapchain-dependent resources including descriptors and UBOs
void Engine::cleanupSwapchain() {
    destroyTimestampQueryPool();

    // Cleanup parallel recorder (per-slot command pools are swapchain-dependent)
    m_parallelRecorder.cleanup();

    // Free command buffers (must happen before destroying pool-dependent resources)
    if (!m_commandBuffers.empty() && m_commandPool != VK_NULL_HANDLE) {
        vkFreeCommandBuffers(m_device, m_commandPool, static_cast<uint32_t>(m_commandBuffers.size()), m_commandBuffers.data());
        m_commandBuffers.clear();
    }
    
    // GPT_CHANGE: Clear image fence tracking
    m_imageInFlight.clear();

    // Destroy framebuffers
    for (auto framebuffer : m_swapchainFramebuffers) {
        vkDestroyFramebuffer(m_device, framebuffer, nullptr);
    }
    m_swapchainFramebuffers.clear();

    // Destroy depth resources
    if (m_depthView) vkDestroyImageView(m_device, m_depthView, nullptr);
    if (m_depthImage) vkDestroyImage(m_device, m_depthImage, nullptr);
    if (m_depthMemory) vkFreeMemory(m_device, m_depthMemory, nullptr);
    m_depthView = VK_NULL_HANDLE;
    m_depthImage = VK_NULL_HANDLE;
    m_depthMemory = VK_NULL_HANDLE;

    // Cleanup cloud system (destroy pipeline, no descriptor pool yet)
    m_cloudSystem.recreate(m_device, VK_NULL_HANDLE, m_swapchainExtent, VK_NULL_HANDLE);
    
    // Cleanup celestial system (destroy pipeline, no descriptor pool yet)
    m_celestialSystem.recreate(m_device, VK_NULL_HANDLE, m_swapchainExtent, VK_NULL_HANDLE);
    
    // Cleanup star field system (destroy pipeline, no descriptor pool yet)
    m_starSystem.recreate(m_device, VK_NULL_HANDLE, m_swapchainExtent, VK_NULL_HANDLE);
    
    // Cleanup sky gradient system (destroy pipeline, no descriptor pool yet)
    m_skySystem.recreate(m_device, VK_NULL_HANDLE, m_swapchainExtent, VK_NULL_HANDLE);
    
    // Cleanup light glow system (destroy pipeline, no descriptor pool yet)
    m_lightGlowSystem.recreate(m_device, VK_NULL_HANDLE, m_swapchainExtent, VK_NULL_HANDLE);
    
    // Destroy pipeline and layout
    if (m_graphicsPipeline) vkDestroyPipeline(m_device, m_graphicsPipeline, nullptr);
    if (m_graphicsPipelineDepthLoad) vkDestroyPipeline(m_device, m_graphicsPipelineDepthLoad, nullptr);
    if (m_depthPrePassPipeline) vkDestroyPipeline(m_device, m_depthPrePassPipeline, nullptr);
    if (m_pipelineLayout) vkDestroyPipelineLayout(m_device, m_pipelineLayout, nullptr);
    m_graphicsPipeline = VK_NULL_HANDLE;
    m_graphicsPipelineDepthLoad = VK_NULL_HANDLE;
    m_depthPrePassPipeline = VK_NULL_HANDLE;
    m_pipelineLayout = VK_NULL_HANDLE;
    
    // Destroy DCCM pipeline
    if (m_dccmPipeline) vkDestroyPipeline(m_device, m_dccmPipeline, nullptr);
    if (m_dccmPipelineDepthLoad) vkDestroyPipeline(m_device, m_dccmPipelineDepthLoad, nullptr);
    if (m_dccmPipelineLayout) vkDestroyPipelineLayout(m_device, m_dccmPipelineLayout, nullptr);
    m_dccmPipeline = VK_NULL_HANDLE;
    m_dccmPipelineDepthLoad = VK_NULL_HANDLE;
    m_dccmPipelineLayout = VK_NULL_HANDLE;

    // Destroy render pass
    if (m_renderPass) vkDestroyRenderPass(m_device, m_renderPass, nullptr);
    if (m_renderPassDepthPrepass) vkDestroyRenderPass(m_device, m_renderPassDepthPrepass, nullptr);
    if (m_renderPassDepthLoad) vkDestroyRenderPass(m_device, m_renderPassDepthLoad, nullptr);
    if (m_uiRenderPass) vkDestroyRenderPass(m_device, m_uiRenderPass, nullptr);
    m_renderPass = VK_NULL_HANDLE;
    m_renderPassDepthPrepass = VK_NULL_HANDLE;
    m_renderPassDepthLoad = VK_NULL_HANDLE;
    m_uiRenderPass = VK_NULL_HANDLE;

    // Destroy image views
    for (auto imageView : m_swapchainImageViews) {
        vkDestroyImageView(m_device, imageView, nullptr);
    }
    m_swapchainImageViews.clear();

    // Destroy swapchain
    vkDestroySwapchainKHR(m_device, m_swapchain, nullptr);
    m_swapchain = VK_NULL_HANDLE;

    // Destroy descriptor pool (this automatically frees all descriptor sets)
    vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
    m_descriptorPool = VK_NULL_HANDLE;
    m_descriptorSets.clear();

    // Cleanup uniform buffers (unmap, destroy, free memory)
    for (size_t i = 0; i < m_uniformBuffers.size(); i++) {
        if (m_uniformMapped[i]) {
            vkUnmapMemory(m_device, m_uniformBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_uniformBuffers[i], nullptr);
        vkFreeMemory(m_device, m_uniformBuffersMemory[i], nullptr);
    }
    m_uniformBuffers.clear();
    m_uniformBuffersMemory.clear();
    m_uniformMapped.clear();

    // Cleanup lighting buffers
    for (size_t i = 0; i < m_lightingBuffers.size(); i++) {
        if (m_lightingMapped[i]) {
            vkUnmapMemory(m_device, m_lightingBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_lightingBuffers[i], nullptr);
        vkFreeMemory(m_device, m_lightingBuffersMemory[i], nullptr);
    }
    m_lightingBuffers.clear();
    m_lightingBuffersMemory.clear();
    m_lightingMapped.clear();

    // Cleanup clustered lighting buffers
    m_clusteredLighting.cleanup();

    // Cleanup camera buffers
    for (size_t i = 0; i < m_cameraBuffers.size(); i++) {
        if (m_cameraMapped[i]) {
            vkUnmapMemory(m_device, m_cameraBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_cameraBuffers[i], nullptr);
        vkFreeMemory(m_device, m_cameraBuffersMemory[i], nullptr);
    }
    m_cameraBuffers.clear();
    m_cameraBuffersMemory.clear();
    m_cameraMapped.clear();
    
    // Cleanup AO buffers
    for (size_t i = 0; i < m_aoBuffers.size(); i++) {
        if (m_aoMapped[i]) {
            vkUnmapMemory(m_device, m_aoBuffersMemory[i]);
        }
        vkDestroyBuffer(m_device, m_aoBuffers[i], nullptr);
        vkFreeMemory(m_device, m_aoBuffersMemory[i], nullptr);
    }
    m_aoBuffers.clear();
    m_aoBuffersMemory.clear();
    m_aoMapped.clear();

    // Cleanup sparse texture-material overlay buffers
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
    m_materialOverlayBuffers.clear();
    m_materialOverlayBuffersMemory.clear();
    m_materialOverlayMapped.clear();
    m_materialOverlayImageDirty.clear();
    m_materialOverlayBufferSize = 0;
    m_materialOverlayNeedsRebuild = true;
}

// GPT_CHANGE: Full swapchain recreation implementation for resize/out-of-date
void Engine::recreateSwapchain() {
    // Handle minimized window - wait until window is restored
    int width = 0, height = 0;
    glfwGetFramebufferSize(m_window, &width, &height);
    while (width == 0 || height == 0) {
        glfwGetFramebufferSize(m_window, &width, &height);
        glfwWaitEvents();
    }

    // Wait for device to be idle before cleanup
    vkDeviceWaitIdle(m_device);

    // Cleanup old swapchain resources
    cleanupSwapchain();

    // Recreate swapchain and dependent resources in order
    createSwapchain();
    createImageViews();
    createDepthResources();
    
    createRenderPass();
    if (m_gameplayWindow) {
        m_gameplayWindow->setRenderPass(m_renderPass);
    }
    createGraphicsPipeline();
    createFramebuffers();
    buildFramePassDescriptors();
    
    // Recreate per-swapchain-image resources (UBOs and descriptors)
    createUniformBuffers();      // Resize UBOs to match new swapchain image count
    createLightingBuffers();     // Recreate lighting buffers
    createClusterBuffers();      // Recreate clustered lighting buffers
    createCameraBuffers();       // Recreate camera buffers
    createAOBuffers();           // Recreate AO buffers
    createMaterialOverlayBuffers(); // Recreate texture overlay SSBOs
    m_shadowSystem.recreatePerImageResources(static_cast<uint32_t>(m_swapchainImages.size()));
    createDescriptorPool();      // Recreate pool sized for new image count
    createDescriptorSets();      // Allocate new descriptor sets
    // Phase D — re-bind GPU visible-origins buffer into slot 1 of the bindless
    // ChunkOrigins[3] SSBO array. Fresh descriptor sets already have slots 0 and 2.
    bindGpuVisibleOriginsToSlot1();

    createCommandBuffers();      // Re-record command buffers with new descriptors
    createTimestampQueryPool();  // Recreate timestamp queries for new swapchain size
    
    // Re-init parallel recorder with new swapchain image count
    m_parallelRecorder.init(m_device, m_physicalDevice,
                            static_cast<uint32_t>(m_swapchainImages.size()));
    
    // Recreate cloud system with new render pass, extent, and descriptor pool
    m_cloudSystem.recreate(m_device, m_renderPass, m_swapchainExtent, m_descriptorPool);
    m_cloudSystem.setLightingBuffers(m_device, m_lightingBuffers);
    
    // Recreate celestial system with new render pass, extent, and descriptor pool
    m_celestialSystem.recreate(m_device, m_renderPass, m_swapchainExtent, m_descriptorPool);
    m_celestialSystem.setLightingBuffers(m_device, m_lightingBuffers);
    
    // Recreate star field system with new render pass, extent, and descriptor pool
    m_starSystem.recreate(m_device, m_renderPass, m_swapchainExtent, m_descriptorPool);
    
    // Recreate sky gradient system with new render pass, extent, and descriptor pool
    m_skySystem.recreate(m_device, m_renderPass, m_swapchainExtent, m_descriptorPool);
    
    // Recreate light glow system with new render pass, extent, and descriptor pool
    m_lightGlowSystem.recreate(m_device, m_renderPass, m_swapchainExtent, m_descriptorPool);
    
    // Recreate T-junction fix system with new swapchain
    m_tjunctionFix.recreate(m_swapchainImageFormat, m_depthFormat, m_swapchainExtent,
                            m_swapchainImageViews);
    m_pixelPass.recreate(m_swapchainImageFormat, m_depthFormat, m_swapchainExtent,
                         m_swapchainImageViews);
    syncGameplayTJunctionFix(true);
    syncGameplayPixelPass(true);
    
    // Detached gameplay owns the active Hi-Z target when separated.
    syncHiZTarget(true);
    
    // GPT_CHANGE B7: Resize image fence tracking to match new swapchain
    m_imageInFlight.resize(m_swapchainImages.size(), VK_NULL_HANDLE);

    m_frameGraphColorLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    m_frameGraphDepthLayout = VK_IMAGE_LAYOUT_UNDEFINED;
}

````

## src\core\engine\EngineSubsystemInit.cpp

Description: No CC-DESC found.

````cpp
// EngineSubsystemInit.cpp — Terrain preset configuration and per-frame uniform
// buffer updates extracted from Engine.cpp.
// Contains: applyStartupTerrainPreset(), updateLightingUniforms(),
//           updateCameraUniforms(), updateAOUniforms(), updateClusterData().

#include "core/engine/Engine.h"
#include "rendering/lighting/AOSettings.h"
#include "world/config/WorldConfig.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <stdexcept>

#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#include <glm/gtc/matrix_transform.hpp>

// ═══════════════════════════════════════════════════════════════════════════════
// Startup terrain preset helpers
// ═══════════════════════════════════════════════════════════════════════════════

const char* Engine::getStartupTerrainPresetName() const {
    switch (m_startupTerrainPreset) {
        case StartupTerrainPreset::PerfVoxel4Lod20:
            return "voxel4x20";
        case StartupTerrainPreset::PerfVoxel20DccmFar:
            return "hybrid2";
        case StartupTerrainPreset::Default:
        default:
            return "default";
    }
}

const char* Engine::getStartupTerrainPresetSummary() const {
    switch (m_startupTerrainPreset) {
        case StartupTerrainPreset::PerfVoxel4Lod20:
            return "LOD0-3 voxel, 20 rings each";
        case StartupTerrainPreset::PerfVoxel20DccmFar:
            return "LOD0 voxel 0-20, LOD1 DCCM far";
        case StartupTerrainPreset::Default:
        default:
            return "Current world defaults";
    }
}

void Engine::applyStartupTerrainPreset() {
    ChunkManager* chunkManager = m_world.getChunkManager();
    if (!chunkManager) {
        return;
    }

    if (m_startupTerrainPreset == StartupTerrainPreset::Default) {
        m_anyLODUsesVoxel = m_world.anyLODUsesType(TerrainType::Voxel);
        m_anyLODUsesDCCM = m_world.anyLODUsesType(TerrainType::DCCM);
        return;
    }

    std::cout << "[Engine] Applying startup terrain preset '" << getStartupTerrainPresetName()
              << "' (" << getStartupTerrainPresetSummary() << ")" << std::endl;

    chunkManager->setLODEnabled(true);
    chunkManager->setRenderDistanceRings(80);

    switch (m_startupTerrainPreset) {
        case StartupTerrainPreset::PerfVoxel4Lod20:
            chunkManager->setLODRingThresholds(20, 40, 60, 80);
            m_world.setTerrainTypesForStartup({
                TerrainType::Voxel,
                TerrainType::Voxel,
                TerrainType::Voxel,
                TerrainType::Voxel,
                TerrainType::Voxel
            });
            break;
        case StartupTerrainPreset::PerfVoxel20DccmFar:
            chunkManager->setLODRingThresholds(std::vector<int>{20});
            if (m_world.getDCCMTerrainLoader() == nullptr) {
                std::cout << "[Engine] WARNING: hybrid2 preset requested but no DCCM terrain is loaded. "
                             "Falling back to voxel terrain for all LODs." << std::endl;
                m_world.setTerrainTypesForStartup({
                    TerrainType::Voxel,
                    TerrainType::Voxel,
                    TerrainType::Voxel,
                    TerrainType::Voxel,
                    TerrainType::Voxel
                });
            } else {
                m_world.setTerrainTypesForStartup({
                    TerrainType::Voxel,
                    TerrainType::DCCM,
                    TerrainType::DCCM,
                    TerrainType::DCCM,
                    TerrainType::DCCM
                });
            }
            break;
        case StartupTerrainPreset::Default:
        default:
            break;
    }

    m_anyLODUsesVoxel = m_world.anyLODUsesType(TerrainType::Voxel);
    m_anyLODUsesDCCM = m_world.anyLODUsesType(TerrainType::DCCM);

    const auto thresholds = chunkManager->getLODRingThresholds();
    std::cout << "[Engine] Terrain preset ready: renderDistance=" << chunkManager->getRenderDistanceRings()
              << " rings, thresholds=[" << thresholds[0] << ", " << thresholds[1]
              << ", " << thresholds[2] << ", " << thresholds[3] << "]" << std::endl;

    if (m_perfMode && chunkManager->isPaused()) {
        chunkManager->setPaused(false);
        std::cout << "[Engine] Performance mode auto-started chunk generation" << std::endl;
    }
}

void Engine::startChunkGeneration() {
    ChunkManager* chunkManager = m_world.getChunkManager();
    if (!chunkManager) {
        return;
    }

    if (chunkManager->isPaused()) {
        chunkManager->setPaused(false);
        std::cout << "[Engine] Terrain generation started" << std::endl;
    }
}

namespace {
constexpr uint32_t kMaterialOverlayInitialCapacity = 1u << 16;
constexpr uint32_t kMaterialOverlayMaxCapacity = 1u << 24;

uint32_t nextPow2u32(uint32_t v) {
    if (v <= 1u) return 1u;
    --v;
    v |= v >> 1;
    v |= v >> 2;
    v |= v >> 4;
    v |= v >> 8;
    v |= v >> 16;
    return v + 1u;
}

uint32_t chooseMaterialOverlayCapacity(size_t activeCells) {
    if (activeCells == 0u) {
        return kMaterialOverlayInitialCapacity;
    }

    // Keep open addressing below roughly 70% load. This keeps the fragment
    // shader's probe count stable without reserving memory for the whole world.
    const size_t wanted = std::max<size_t>(
        kMaterialOverlayInitialCapacity,
        (activeCells * 10u + 6u) / 7u);
    const size_t clamped = std::min<size_t>(wanted, kMaterialOverlayMaxCapacity);
    return nextPow2u32(static_cast<uint32_t>(clamped));
}

inline uint32_t hashMaterialOverlayKey(int32_t x, int32_t y, int32_t z, uint32_t face) {
    uint32_t h = static_cast<uint32_t>(x) * 0x9E3779B9u;
    h ^= static_cast<uint32_t>(y) * 0x85EBCA6Bu;
    h ^= static_cast<uint32_t>(z) * 0xC2B2AE35u;
    h ^= (face & 0x7u) * 0x27D4EB2Du;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    return h;
}
} // namespace

namespace {
constexpr uint32_t kMaterialOverlayLiveStampCapacity = 64u;
constexpr uint32_t kMaterialOverlayLiveStampCellStride = 3u;
constexpr uint32_t kMaterialOverlayLiveStampCellCapacity =
    kMaterialOverlayLiveStampCapacity * kMaterialOverlayLiveStampCellStride;

uint32_t materialOverlayFloatBitsU32(float value) {
    uint32_t bits = 0u;
    std::memcpy(&bits, &value, sizeof(bits));
    return bits;
}

int32_t materialOverlayFloatBitsI32(float value) {
    uint32_t bits = materialOverlayFloatBitsU32(value);
    return static_cast<int32_t>(bits);
}
} // namespace

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

    // Binding 10 keeps the old GLSL shape:
    //   header + MaterialOverlayCell cells[]
    //
    // The first fixed cells[] range is now reserved for a bounded live-stamp
    // overlay. The old sparse hash table starts after that prefix. This avoids
    // changing descriptor layouts while giving texture paint a one-frame visual
    // path independent of chunk remesh/upload/swap.
    m_materialOverlayBufferSize =
        sizeof(MaterialOverlayHeaderGPU) +
        static_cast<VkDeviceSize>(
            kMaterialOverlayLiveStampCellCapacity + m_materialOverlayCapacity) *
            sizeof(MaterialOverlayCellGPU);

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

void Engine::refreshMaterialOverlayDescriptors() {
    if (m_descriptorSets.empty()) {
        return;
    }

    const uint32_t imageCount = static_cast<uint32_t>(m_descriptorSets.size());
    for (uint32_t i = 0; i < imageCount; ++i) {
        if (i >= m_materialOverlayBuffers.size() ||
            m_materialOverlayBuffers[i] == VK_NULL_HANDLE) {
            continue;
        }

        VkDescriptorBufferInfo materialOverlayInfo{};
        materialOverlayInfo.buffer = m_materialOverlayBuffers[i];
        materialOverlayInfo.offset = 0;
        materialOverlayInfo.range = m_materialOverlayBufferSize;

        VkWriteDescriptorSet write{};
        write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        write.dstSet = m_descriptorSets[i];
        write.dstBinding = 10;
        write.dstArrayElement = 0;
        write.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        write.descriptorCount = 1;
        write.pBufferInfo = &materialOverlayInfo;

        vkUpdateDescriptorSets(m_device, 1, &write, 0, nullptr);
    }
}

void Engine::ensureMaterialOverlayCapacity(size_t activeLOD0Cells) {
    const uint32_t wantedCapacity = chooseMaterialOverlayCapacity(activeLOD0Cells);
    if (m_materialOverlayCapacity >= wantedCapacity &&
        !m_materialOverlayBuffers.empty()) {
        return;
    }

    vkDeviceWaitIdle(m_device);
    m_materialOverlayCapacity = wantedCapacity;
    createMaterialOverlayBuffers();
    refreshMaterialOverlayDescriptors();
    m_materialOverlayNeedsRebuild = true;
    m_materialOverlayLastGeneration = 0;
}

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

    std::vector<TextureOverlay::TextureOverlayStore::SurfacePaintStamp> liveStamps;
    textureStore.exportLiveSurfacePaintStamps(
        liveStamps,
        static_cast<size_t>(kMaterialOverlayLiveStampCapacity));

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

        auto applyCell = [&](int32_t x, int32_t y, int32_t z,
                             uint32_t face, uint32_t material,
                             std::vector<uint32_t>* changedSlots = nullptr) -> uint32_t {
            if (m_materialOverlayCapacity == 0u) {
                return UINT32_MAX;
            }

            const uint32_t faceKey = face & 0x7u;
            uint32_t idx = hashMaterialOverlayKey(x, y, z, faceKey) & mask;

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

            std::fill(m_materialOverlayImageDirty.begin(), m_materialOverlayImageDirty.end(), 1u);
            for (auto& q : m_materialOverlayImageDirtySlots) q.clear();
        } else {
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
    auto* rawCells = reinterpret_cast<MaterialOverlayCellGPU*>(base + sizeof(MaterialOverlayHeaderGPU));
    auto* liveCells = rawCells;
    auto* dstCells = rawCells + kMaterialOverlayLiveStampCellCapacity;

    header->capacityMask = m_materialOverlayCapacity - 1u;
    header->count = m_materialOverlayCount;
    header->maxProbe = m_materialOverlayMaxProbe;
    header->_pad = static_cast<uint32_t>(std::min<size_t>(
        liveStamps.size(),
        static_cast<size_t>(kMaterialOverlayLiveStampCapacity)));

    std::memset(
        liveCells,
        0,
        static_cast<size_t>(kMaterialOverlayLiveStampCellCapacity) * sizeof(MaterialOverlayCellGPU));

    for (uint32_t i = 0; i < header->_pad; ++i) {
        const auto& stamp = liveStamps[i];
        const uint32_t baseCell = i * kMaterialOverlayLiveStampCellStride;

        liveCells[baseCell + 0].x = materialOverlayFloatBitsI32(stamp.centerVoxelLod0.x);
        liveCells[baseCell + 0].y = materialOverlayFloatBitsI32(stamp.centerVoxelLod0.y);
        liveCells[baseCell + 0].z = materialOverlayFloatBitsI32(stamp.centerVoxelLod0.z);
        liveCells[baseCell + 0].face = materialOverlayFloatBitsU32(
            static_cast<float>(std::max(1, stamp.radiusVoxelsLod0)));
        liveCells[baseCell + 0].material = stamp.order;

        liveCells[baseCell + 1].x = stamp.bboxMinLod0.x;
        liveCells[baseCell + 1].y = stamp.bboxMinLod0.y;
        liveCells[baseCell + 1].z = stamp.bboxMinLod0.z;
        liveCells[baseCell + 1].face = static_cast<uint32_t>(stamp.shape);
        liveCells[baseCell + 1].material = static_cast<uint32_t>(stamp.sourceFace);

        liveCells[baseCell + 2].x = stamp.bboxMaxLod0.x;
        liveCells[baseCell + 2].y = stamp.bboxMaxLod0.y;
        liveCells[baseCell + 2].z = stamp.bboxMaxLod0.z;
        liveCells[baseCell + 2].face = static_cast<uint32_t>(stamp.type);
        liveCells[baseCell + 2].material = static_cast<uint32_t>(stamp.variant & 0x7u);
    }

    if (m_materialOverlayImageDirty[imageIndex]) {
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

    for (uint32_t slot : q) {
        if (slot >= m_materialOverlayCapacity) continue;
        dstCells[slot] = m_materialOverlayTable[slot];
    }
    q.clear();
}

// ═══════════════════════════════════════════════════════════════════════════════
// Per-frame uniform buffer updates
// ═══════════════════════════════════════════════════════════════════════════════

void Engine::updateLightingUniforms(uint32_t currentImage,
                                    const std::vector<PointLight>& transientLights,
                                    const std::vector<glm::vec4>& transientPulseData) {
    // Gather per-light preset indices from placed objects.
    std::vector<uint32_t> lightPresetIndex(m_lighting.activePointLights, 0u);
    for (const auto& [objId, obj] : m_objectManager.getAllObjects()) {
        if (obj.type == PlacedObjectType::LightOrb &&
                   obj.light.lightIndex < lightPresetIndex.size()) {
            lightPresetIndex[obj.light.lightIndex] = obj.light.pulsePresetIndex;
        }
    }

    auto& gpuData = *m_lightingStaging;
    std::memset(&gpuData, 0, sizeof(LightingSettings::GPULightingData));
    // Always use the smooth lighting direction from LightingSettings — never
    // the shadow system's quantized/clamped sun direction.  The shadow
    // system's sunDir (with its 1° quantum snap and 13° elevation floor) is
    // published only through shadow.sunDirTexelSize.xyz for receiver-push
    // alignment; feeding it into lighting.sunDirection causes ndotl to jump
    // in discrete steps whenever realtime celestials are active.
    const glm::vec3 directionalLightDir = m_lighting.currentLight.direction;
    gpuData.sunDirection = glm::vec4(glm::normalize(directionalLightDir), m_lighting.currentLight.intensity);
    gpuData.sunColor = glm::vec4(m_lighting.currentLight.color, m_lighting.currentLight.ambientStrength);
    gpuData.skyColor = glm::vec4(m_lighting.currentSkyColor, m_lighting.fogDensity);
    gpuData.time = m_lighting.totalTime;

    const uint32_t baseLightCount = std::min(
        m_lighting.activePointLights,
        static_cast<uint32_t>(m_lighting.pointLights.size()));
    const uint32_t transientCount = static_cast<uint32_t>(transientLights.size());
    const uint32_t combinedCount = baseLightCount + transientCount;
    const uint32_t maxLights = std::min<uint32_t>(MAX_SHADER_LIGHTS, MAX_POINT_LIGHTS);

    std::vector<PointLight> resolvedLights;
    std::vector<glm::vec4> resolvedPulse;
    resolvedLights.reserve(maxLights);
    resolvedPulse.reserve(maxLights);

    auto appendResolvedLight = [&](const PointLight& light,
                                   const glm::vec4& pulseData) {
        if (resolvedLights.size() >= maxLights) {
            return;
        }
        resolvedLights.push_back(light);
        resolvedPulse.push_back(pulseData);
    };

    auto computeBasePulseData = [&](uint32_t baseIndex) -> glm::vec4 {
        const auto& preset = m_pulsePresets.getPreset(
            baseIndex < lightPresetIndex.size() ? lightPresetIndex[baseIndex] : 0u);
        const float speed = preset.speed;
        const float strength = preset.strength;
        const float sharpness = preset.sharpness;
        const float flickerAmount = preset.flickerAmount;
        const float flickerSpeed = preset.flickerSpeed;

        const float breathCycle = std::sin(m_lighting.totalTime * speed) * 0.5f + 0.5f;
        const float quantized = std::floor(breathCycle * 8.0f) / 8.0f;
        const float square = breathCycle > 0.5f ? 1.0f : 0.0f;
        const float pulse = glm::mix(quantized, square, sharpness);
        const float pulseStrength = glm::clamp(pulse * strength, 0.0f, 1.0f);
        const float breathScale = 1.0f + pulseStrength * 0.45f;
        return glm::vec4(pulseStrength, breathScale, flickerAmount, flickerSpeed);
    };

    auto appendCombinedBySource = [&](uint32_t sourceIndex) {
        if (sourceIndex >= combinedCount) {
            return;
        }
        if (sourceIndex < baseLightCount) {
            appendResolvedLight(
                m_lighting.pointLights[sourceIndex],
                computeBasePulseData(sourceIndex));
            return;
        }

        const uint32_t transientIndex = sourceIndex - baseLightCount;
        if (transientIndex >= transientLights.size()) {
            return;
        }
        const glm::vec4 pulse = (transientIndex < transientPulseData.size())
            ? transientPulseData[transientIndex]
            : glm::vec4(0.0f);
        appendResolvedLight(
            transientLights[transientIndex],
            pulse);
    };

    const auto& remap = m_shadowSystem.getActiveLightRemap();
    if (m_shadowSystem.isReady()) {
        for (uint32_t slot = 0; slot < remap.size() && resolvedLights.size() < maxLights; ++slot) {
            appendCombinedBySource(remap[slot]);
        }
    } else {
        for (uint32_t i = 0; i < baseLightCount && resolvedLights.size() < maxLights; ++i) {
            appendResolvedLight(m_lighting.pointLights[i], computeBasePulseData(i));
        }
        for (uint32_t i = 0; i < transientCount && resolvedLights.size() < maxLights; ++i) {
            const glm::vec4 pulse = (i < transientPulseData.size())
                ? transientPulseData[i]
                : glm::vec4(0.0f);
            appendResolvedLight(transientLights[i], pulse);
        }
    }

    gpuData.numPointLights = static_cast<uint32_t>(resolvedLights.size());
    for (uint32_t i = 0; i < gpuData.numPointLights; ++i) {
        const PointLight& src = resolvedLights[i];
        gpuData.pointLights[i].positionRadius = glm::vec4(src.position, src.radius);
        gpuData.pointLights[i].colorIntensity = glm::vec4(src.color, src.intensity);
        gpuData.lightPulseData[i] = resolvedPulse[i];
    }

    memcpy(m_lightingMapped[currentImage], &gpuData, sizeof(LightingSettings::GPULightingData));
}

void Engine::updateCameraUniforms(uint32_t currentImage) {
    struct CameraData {
        glm::vec3 cameraPos;
        float time;  // Synced with light glow animation
    } cameraData;
    
    cameraData.cameraPos = m_camera.getState().position;
    cameraData.time = static_cast<float>(glfwGetTime());
    
    memcpy(m_cameraMapped[currentImage], &cameraData, sizeof(CameraData));
}

void Engine::updateAOUniforms(uint32_t currentImage) {
    const AOSettings& aoSettings = m_world.getDebugOverlay().getAOSettings();
    auto gpuData = aoSettings.getGPUData();
    memcpy(m_aoMapped[currentImage], &gpuData, sizeof(AOSettings::GPUAOData));
}

void Engine::updateClusterData(uint32_t currentImage,
                               const glm::mat4& viewCameraRel,
                               const glm::mat4& proj,
                               const VkRect2D& gameplayRect) {
    // Extract light positions and radii from the staging buffer (already
    // filled by updateLightingUniforms) so the cluster system sees the
    // same resolved, remapped light order the fragment shader will use.
    const auto& gpu = *m_lightingStaging;
    const uint32_t nLights = std::min(gpu.numPointLights, 32u);

    glm::vec3 positions[32];
    float     radii[32];
    for (uint32_t i = 0; i < nLights; ++i) {
        positions[i] = glm::vec3(gpu.pointLights[i].positionRadius);
        radii[i]     = gpu.pointLights[i].positionRadius.w;
    }

    const CameraState& cam = m_camera.getState();
    uint32_t targetWidth = m_swapchainExtent.width;
    uint32_t targetHeight = m_swapchainExtent.height;
    float viewportOffsetX = static_cast<float>(gameplayRect.offset.x);
    float viewportOffsetY = static_cast<float>(gameplayRect.offset.y);
    float viewportWidth = static_cast<float>(gameplayRect.extent.width);
    float viewportHeight = static_cast<float>(gameplayRect.extent.height);

    if (m_gameplaySeparated && m_gameplayWindow && m_gameplayWindow->isOpen()) {
        const VkExtent2D detachedExtent = m_gameplayWindow->getExtent();
        targetWidth = detachedExtent.width;
        targetHeight = detachedExtent.height;
        viewportOffsetX = 0.0f;
        viewportOffsetY = 0.0f;
        viewportWidth = static_cast<float>(detachedExtent.width);
        viewportHeight = static_cast<float>(detachedExtent.height);
    }

    m_clusteredLighting.update(
        currentImage,
        viewCameraRel,
        proj,
        cam.position,
        targetWidth,
        targetHeight,
        viewportOffsetX,
        viewportOffsetY,
        viewportWidth,
        viewportHeight,
        positions,
        radii,
        nLights);
}

````

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
    
    // Wait for the fence that last used this image (if any). Keep this timer
    // honest: it is acquire + image-fence wait only, not GPU timestamp readback.
    const bool imageHadPreviousFrame = (m_imageInFlight[imageIndex] != VK_NULL_HANDLE);
    if (imageHadPreviousFrame) {
        vkWaitForFences(m_device, 1, &m_imageInFlight[imageIndex], VK_TRUE, UINT64_MAX);
    }
    auto afterImageWait = std::chrono::high_resolution_clock::now();
    m_presentWaitMs = std::chrono::duration<float, std::milli>(afterImageWait - beforeAcquire).count();

    // Timestamp collection is now non-blocking for engine timings and made
    // availability-checked for shadow timings. Collect in perf mode too;
    // otherwise the perf overlay and bottleneck reports show stale GPU data
    // exactly in the mode where we need trustworthy measurements most.
    if (imageHadPreviousFrame) {
        collectTimestampResults(imageIndex);
        m_shadowSystem.collectGpuTimings(imageIndex);
    }
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

## src\core\engine\EngineVulkanInit.cpp

Description: No CC-DESC found.

````cpp
// EngineVulkanInit.cpp — Vulkan instance, device, swapchain, pipeline, buffer,
// and descriptor creation extracted from Engine.cpp.
// Contains: initVulkan(), createInstance() through createSyncObjects().

#include "core/engine/Engine.h"
#include "vulkan/VulkanContext.h"
#include "vulkan/Buffers.h"
#include "vulkan/Swapchain.h"
#include "vulkan/Pipeline.h"
#include "vulkan/Sync.h"
#include "rendering/common/Renderer.h"
#include "rendering/common/VulkanHelpers.h"
#include "ui/debug_menu/IconManagerForDebug.h"
#include "world/config/WorldConfig.h"
#include <stdexcept>
#include <iostream>
#include <cstring>
#include <array>

// ═══════════════════════════════════════════════════════════════════════════════
// Thin Vulkan wrappers (delegate to VulkanContext namespace)
// ═══════════════════════════════════════════════════════════════════════════════

void Engine::createInstance(){
    VulkanContext::createInstance(m_instance);
}

void Engine::setupDebugMessenger() {
    VulkanContext::setupDebugMessenger(m_instance, m_debugMessenger);
}

void Engine::pickPhysicalDevice(){
    VulkanContext::pickPhysicalDevice(m_instance, m_physicalDevice, m_deviceProperties, m_timestampPeriod, m_deviceCapabilities);
}

void Engine::createLogicalDevice(){
    VulkanContext::createLogicalDevice(m_physicalDevice, m_surface, m_device, m_graphicsQueue, m_presentQueue);
}

void Engine::createSurface(){
    VulkanContext::createSurface(m_instance, m_window, m_surface);
}

// ═══════════════════════════════════════════════════════════════════════════════
// initVulkan — Full Vulkan + subsystem initialization sequence
// ═══════════════════════════════════════════════════════════════════════════════

void Engine::initVulkan(){
    createInstance();
    setupDebugMessenger();
    createSurface();
    pickPhysicalDevice();
    createLogicalDevice();

    createSwapchain();
    createImageViews();
    createDepthResources();
    createRenderPass();
    createDescriptorSetLayout();
    createPipelineCache();
    createGraphicsPipeline();
    createFramebuffers();
    buildFramePassDescriptors();
    createCommandPool();
    
    // Initialize allocators after command pool
    for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        m_uploadArenas[i].init(m_device, m_physicalDevice, 128 * 1024 * 1024); // 128 MiB per frame
        
        char name[64];
        snprintf(name, sizeof(name), "StagingArena[%d]", i);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_uploadArenas[i].vkBuffer(), name);
    }
    
    // Phase C: Right-sized buffers for 4 GB VRAM GPUs
    // 512 MiB VB supports ~10M material-aware terrain vertices.
    // IB sized to 384 MiB: with 8-byte vertices and 2-byte indices, a greedy-meshed
    // face uses 32B VB + 12B IB. Keep the larger IB because old+new LOD meshes
    // can coexist briefly during swaps.
    // Previous 256 MiB IB hit 99.6% usage and caused allocation failures during
    // LOD transitions (old + new meshes coexist via PendingMeshHandle).
    m_vbAllocator.init(m_device, m_physicalDevice, BufferKind::Vertex, 512 * 1024 * 1024); // 512 MiB for VB
    m_ibAllocator.init(m_device, m_physicalDevice, BufferKind::Index, 384 * 1024 * 1024);   // 384 MiB for IB
    m_uploader.init(m_device, m_commandPool, m_graphicsQueue);
    
    createVertexBuffer();
    createIndexBuffer();
    createIndirectBuffer();
    createUniformBuffers();
    createLightingBuffers();
    createClusterBuffers();
    createCameraBuffers();
    createAOBuffers();
    createMaterialOverlayBuffers();
    m_shadowSystem.init(m_device, m_physicalDevice, m_descriptorSetLayout,
                        m_commandPool, m_graphicsQueue,
                        static_cast<uint32_t>(m_swapchainImages.size()),
                        !m_perfMode);
    // Upload the sky-vis heightmap from the world's already-loaded sampler.
    // Must happen BEFORE createDescriptorSets so the descriptor binding 9
    // points at content rather than a zero-filled image (zeros are fine but
    // the upload also publishes the world bounds to ShadowGPUData).
    m_shadowSystem.uploadSkyHeightmap(m_world.getHeightmapSampler());
    createDescriptorPool();
    
    createDescriptorSets();
    createCommandBuffers();
    createSyncObjects();
    createTimestampQueryPool();
    
    // Initialize parallel command recorder (per-slot secondary pools/buffers)
    m_parallelRecorder.init(m_device, m_physicalDevice,
                            static_cast<uint32_t>(m_swapchainImages.size()));
    
    // Initialize ImGui system
    m_imgui.init(m_window, m_instance, m_physicalDevice, m_device, m_graphicsQueue,
                 m_renderPass, m_pipelineCache, m_commandPool, m_surface,
                 static_cast<uint32_t>(m_swapchainImages.size()));
    m_imgui.initGameplayOverlay(m_instance, m_physicalDevice, m_device, m_graphicsQueue,
                                m_renderPass, m_pipelineCache, m_commandPool, m_surface,
                                static_cast<uint32_t>(m_swapchainImages.size()));
    
    // Initialize cloud rendering system
    m_cloudSystem.init(m_device, m_physicalDevice, m_renderPass, m_swapchainExtent, m_descriptorPool);
    m_cloudSystem.setLightingBuffers(m_device, m_lightingBuffers);
    
    // Initialize celestial rendering system (sun and moon)
    m_celestialSystem.init(m_device, m_physicalDevice, m_renderPass, m_swapchainExtent, m_descriptorPool);
    m_celestialSystem.setLightingBuffers(m_device, m_lightingBuffers);
    
    // Initialize light glow rendering system (point light halos)
    m_lightGlowSystem.init(m_device, m_physicalDevice, m_renderPass, m_swapchainExtent, m_descriptorPool);
    
    // Initialize star field rendering system (pixelated twinkling stars)
    m_starSystem.init(m_device, m_physicalDevice, m_renderPass, m_swapchainExtent, m_descriptorPool);
    
    // Initialize sky gradient rendering system (Sega-style pixel art sky)
    m_skySystem.init(m_device, m_physicalDevice, m_renderPass, m_swapchainExtent, m_descriptorPool);
    
    // Initialize GPU-driven frustum culling system
    std::cout << "[Engine] Initializing GPU culling system..." << std::endl;
    std::cout << "[Engine] m_chunkOriginsBuffer handle: " << (uint64_t)m_chunkOriginsBuffer << std::endl;
    m_gpuCulling.init(m_device, m_physicalDevice, 65536, m_chunkOriginsBuffer);
    std::cout << "[Engine] GPU culling visibleOriginsBuffer handle: " << (uint64_t)m_gpuCulling.getVisibleOriginsBuffer() << std::endl;
    std::cout << "[Engine] GPU culling system initialized (disabled by default, press G to toggle)" << std::endl;

    // Phase D — wire the GPU visible-origins buffer into slot 1 of the bindless
    // ChunkOrigins[3] SSBO array (set=0, binding=1, dstArrayElement=1). One-time write.
    bindGpuVisibleOriginsToSlot1();
    
    // Initialize Hi-Z depth pyramid for occlusion culling
    std::cout << "[Engine] Initializing Hi-Z depth pyramid..." << std::endl;
    m_hiZPyramid.init(m_device, m_physicalDevice, m_depthView, m_swapchainExtent.width, m_swapchainExtent.height);
    m_gpuCulling.bindHiZPyramid(m_hiZPyramid.getImageView(), m_hiZPyramid.getSampler());
    m_world.getDebugOverlay().getHiZDebugWindow().setHiZPyramid(&m_hiZPyramid);
    m_world.getDebugOverlay().getHiZDebugWindow().setGPUCullingSystem(&m_gpuCulling);
    std::cout << "[Engine] Hi-Z depth pyramid initialized" << std::endl;
    
    // Initialize T-junction fix system (post-process for greedy meshing artifacts)
    std::cout << "[Engine] Initializing T-junction fix system..." << std::endl;
    m_tjunctionFix.init(m_device, m_physicalDevice, m_swapchainImageFormat, m_depthFormat,
                        m_swapchainExtent, m_swapchainImageViews);
    std::cout << "[Engine] T-junction fix system initialized (press T to toggle)" << std::endl;

    // Initialize retro pixel pass system (post-process pixelation filter)
    m_pixelPass.init(m_device, m_physicalDevice, m_swapchainImageFormat, m_depthFormat,
                     m_swapchainExtent, m_swapchainImageViews);
    std::cout << "[Engine] Retro pixel pass system initialized" << std::endl;
    
    if (!m_perfMode) {
        // Initialize minimap culling readback (debug feature)
        std::cout << "[Engine] Initializing minimap culling readback..." << std::endl;
        m_minimapReadback.init(m_device, m_physicalDevice, 65536);
        std::cout << "[Engine] Minimap culling readback initialized" << std::endl;
    }
    
    // Connect GPU culling system to World for persistent slot allocation
    m_world.setGPUCullingSystem(&m_gpuCulling);

    // Runtime shader source editing and hot reload
    if (!m_perfMode) {
        initShaderHotReload();
    }
    
    // Wire up debug overlay windows to engine subsystems (extracted to EngineDebugWiring.cpp)
    initDebugWiring();

    applyStartupTerrainPreset();
    
    if (!m_perfMode) {
        // Initialize minimap Vulkan texture resources (for texture-based rendering)
        m_world.getDebugOverlay().getChunkMinimapWindow().initVulkanResources(
            m_device, m_physicalDevice, m_commandPool, m_graphicsQueue);

        // Load debug-window icon textures (replaces emoji icons in toolbar/headers).
        IconManagerForDebug::instance().init(
            m_device, m_physicalDevice, m_commandPool, m_graphicsQueue);
    }
    
    // Initialize physics system
    std::cout << "[Engine] Initializing physics..." << std::endl;
    m_physics.init();
    std::cout << "[Engine] Physics initialized" << std::endl;
    
    // Pre-deserialize collision shapes NOW that Jolt is initialized
    // This makes runtime collision loading INSTANT (no BVH rebuild)
    m_world.preDeserializeCollisionShapes();
    
    // Connect physics to voxel world for per-chunk mesh collision
    m_world.setPhysicsWorld(&m_physics);
    std::cout << "[Engine] Physics connected to voxel world (per-chunk collision enabled)" << std::endl;
    
    // Initialize player at terrain center, at a reasonable height
    auto terrainDims = m_world.getTerrainDimensions();
    float chunkSizeM = WorldConfig::CHUNK_SIZE / static_cast<float>(WorldConfig::VOXELS_PER_METER);
    float spawnX, spawnZ;
    if (terrainDims.chunksX > 0 && terrainDims.chunksZ > 0) {
        spawnX = (terrainDims.chunksX / 2) * chunkSizeM + chunkSizeM * 0.5f;
        spawnZ = (terrainDims.chunksZ / 2) * chunkSizeM + chunkSizeM * 0.5f;
    } else {
        spawnX = 400.0f;
        spawnZ = 400.0f;
    }
    float spawnY = 150.0f;  // High enough to see terrain
    
    m_player.init(&m_physics, glm::vec3(spawnX, spawnY, spawnZ));
    m_playerCamera.init(70.0f, 0.1f);  // 70 degree FOV, 0.1 mouse sensitivity
    
    // Initialize camera controller at spawn position
    m_camera.init(m_width, m_height);
    m_camera.setPosition(glm::vec3(spawnX, spawnY, spawnZ));
    m_camera.setOrientation(-180.0f, -45.0f);  // Looking toward origin, angled down
    m_camera.setSpeed(50.0f);  // 50 m/s for free camera
    
    std::cout << "[Engine] Player spawned at (" << spawnX << ", " << spawnY << ", " << spawnZ << ")" << std::endl;
    std::cout << "[Engine] Press P to toggle between free camera and player mode" << std::endl;
    
    // Cache timeline semaphore extension function pointer (resolved once, used every frame)
    m_vkGetSemaphoreCounterValueKHR = reinterpret_cast<PFN_vkGetSemaphoreCounterValueKHR>(
        vkGetDeviceProcAddr(m_device, "vkGetSemaphoreCounterValueKHR"));

    // Restore saved settings from previous session
    loadSettings();
}

// ═══════════════════════════════════════════════════════════════════════════════
// Swapchain, render pass, and pipeline creation
// ═══════════════════════════════════════════════════════════════════════════════

void Engine::createSwapchain(){
    const bool detachedGameplayOwnsPresentation =
        m_gameplaySeparated &&
        m_gameplayWindow &&
        m_gameplayWindow->isOpen();
    const bool mainSwapchainVSync = detachedGameplayOwnsPresentation ? false : m_vsyncEnabled;
    auto result = Swapchain::createSwapchain(m_device, m_physicalDevice, m_surface, m_window, mainSwapchainVSync);
    m_swapchain = result.swapchain;
    m_swapchainImages = std::move(result.images);
    m_swapchainImageFormat = result.imageFormat;
    m_swapchainExtent = result.extent;
}

void Engine::createImageViews(){
    m_swapchainImageViews = Swapchain::createImageViews(m_device, m_swapchainImages, m_swapchainImageFormat);
}

void Engine::createDepthResources(){
    auto result = Swapchain::createDepthResources(m_device, m_physicalDevice, m_swapchainExtent, m_depthFormat);
    m_depthImage = result.image;
    m_depthMemory = result.memory;
    m_depthView = result.view;
}

void Engine::createRenderPass(){
    m_renderPass = Pipeline::createRenderPass(m_device, m_swapchainImageFormat, m_depthFormat);
    m_renderPassDepthPrepass = Pipeline::createDepthPrepassRenderPass(m_device, m_swapchainImageFormat, m_depthFormat);
    m_renderPassDepthLoad = Pipeline::createDepthLoadRenderPass(m_device, m_swapchainImageFormat, m_depthFormat);
    m_uiRenderPass = Pipeline::createUIRenderPass(m_device, m_swapchainImageFormat, m_depthFormat);
}

void Engine::createDescriptorSetLayout(){
    m_descriptorSetLayout = Pipeline::createDescriptorSetLayout(m_device);
}

void Engine::createPipelineCache() {
    m_pipelineCache = Pipeline::createPipelineCache(m_device);
}

void Engine::createGraphicsPipeline(){
    auto result = Pipeline::createGraphicsPipeline(
        m_device, m_renderPass, m_descriptorSetLayout, m_pipelineCache, m_swapchainExtent);
    m_graphicsPipeline = result.pipeline;
    m_pipelineLayout = result.layout;

    auto depthLoadResult = Pipeline::createGraphicsPipeline(
        m_device, m_renderPassDepthLoad, m_descriptorSetLayout, m_pipelineCache, m_swapchainExtent,
        "shaders/terrain/cube.vert.spv",
        "shaders/terrain/cube.frag.spv",
        VK_COMPARE_OP_GREATER_OR_EQUAL,
        VK_FALSE,
        m_pipelineLayout);
    m_graphicsPipelineDepthLoad = depthLoadResult.pipeline;

    m_depthPrePassPipeline = Pipeline::createDepthPrePassPipeline(
        m_device, m_renderPassDepthPrepass, m_pipelineLayout, m_pipelineCache, m_swapchainExtent,
        "shaders/terrain/cube_zonly.vert.spv");

    // Create DCCM terrain pipeline (same descriptor set layout, different shaders)
    auto dccmResult = Pipeline::createGraphicsPipeline(
        m_device, m_renderPass, m_descriptorSetLayout, m_pipelineCache, m_swapchainExtent,
        "shaders/terrain/dccm_terrain.vert.spv",
        "shaders/terrain/dccm_terrain.frag.spv");
    m_dccmPipeline = dccmResult.pipeline;
    m_dccmPipelineLayout = dccmResult.layout;

    auto dccmDepthLoadResult = Pipeline::createGraphicsPipeline(
        m_device, m_renderPassDepthLoad, m_descriptorSetLayout, m_pipelineCache, m_swapchainExtent,
        "shaders/terrain/dccm_terrain.vert.spv",
        "shaders/terrain/dccm_terrain.frag.spv",
        VK_COMPARE_OP_GREATER_OR_EQUAL,
        VK_FALSE,
        m_dccmPipelineLayout);
    m_dccmPipelineDepthLoad = dccmDepthLoadResult.pipeline;
}

void Engine::createFramebuffers(){
    std::cout << "[Engine] createFramebuffers using depthView: " << (void*)m_depthView << std::endl;
    m_swapchainFramebuffers = Pipeline::createFramebuffers(
        m_device, m_renderPass, m_swapchainImageViews, m_depthView, m_swapchainExtent);
}

// ═══════════════════════════════════════════════════════════════════════════════
// Command pool, buffers, descriptors, and sync objects
// ═══════════════════════════════════════════════════════════════════════════════

void Engine::createCommandPool(){
    auto result = Sync::createCommandPool(m_device, m_physicalDevice);
    m_commandPool = result.commandPool;
    m_uploadCmds = result.uploadCmds;
}

void Engine::createVertexBuffer(){
    m_cubeVB = Buffers::createVertexBuffer(m_cubeMesh, m_vbAllocator, m_uploader, m_uploadArenas[0]);
}

void Engine::createIndexBuffer(){
    m_cubeIB = Buffers::createIndexBuffer(m_cubeMesh, m_ibAllocator, m_uploader, m_uploadArenas[0]);
}

void Engine::createIndirectBuffer(){
    auto result = Buffers::createIndirectBuffer(m_device, m_physicalDevice, MAX_INDIRECT_DRAWS);
    m_indirectBuffer = result.indirectBuffer;
    m_indirectMemory = result.indirectMemory;
    m_chunkOriginsBuffer = result.chunkOriginsBuffer;
    m_chunkOriginsMemory = result.chunkOriginsMemory;
}

void Engine::createUniformBuffers(){
    auto result = Buffers::createUniformBuffers(m_device, m_physicalDevice, 
        static_cast<uint32_t>(m_swapchainImages.size()));
    m_uniformBuffers = std::move(result.buffers);
    m_uniformBuffersMemory = std::move(result.memories);
    m_uniformMapped = std::move(result.mappedPtrs);
}

void Engine::createLightingBuffers(){
    auto result = Buffers::createLightingBuffers(m_device, m_physicalDevice,
        static_cast<uint32_t>(m_swapchainImages.size()));
    m_lightingBuffers = std::move(result.buffers);
    m_lightingBuffersMemory = std::move(result.memories);
    m_lightingMapped = std::move(result.mappedPtrs);

    // Heap-allocate the staging buffer for GPULightingData (~200KB with 4096 lights)
    if (!m_lightingStaging) {
        m_lightingStaging = std::make_unique<LightingSettings::GPULightingData>();
    }
    memset(m_lightingStaging.get(), 0, sizeof(LightingSettings::GPULightingData));
}

void Engine::createClusterBuffers(){
    m_clusteredLighting.init(m_device, m_physicalDevice,
        static_cast<uint32_t>(m_swapchainImages.size()));
}

void Engine::createCameraBuffers(){
    auto result = Buffers::createCameraBuffers(m_device, m_physicalDevice,
        static_cast<uint32_t>(m_swapchainImages.size()));
    m_cameraBuffers = std::move(result.buffers);
    m_cameraBuffersMemory = std::move(result.memories);
    m_cameraMapped = std::move(result.mappedPtrs);
}

void Engine::createAOBuffers(){
    auto result = Buffers::createAOBuffers(m_device, m_physicalDevice,
        static_cast<uint32_t>(m_swapchainImages.size()));
    m_aoBuffers = std::move(result.buffers);
    m_aoBuffersMemory = std::move(result.memories);
    m_aoMapped = std::move(result.mappedPtrs);
}

void Engine::createDescriptorPool(){
    m_descriptorPool = Renderer::createDescriptorPool(
        m_device, static_cast<uint32_t>(m_swapchainImages.size()));
}

void Engine::createDescriptorSets(){
    m_descriptorSets = Renderer::createDescriptorSets(
        m_device, m_descriptorPool, m_descriptorSetLayout,
        m_uniformBuffers, m_chunkOriginsBuffer,
        m_lightingBuffers, m_cameraBuffers, m_aoBuffers,
        m_shadowSystem.getShadowDataBuffers(),
        m_shadowSystem.getSunLocalOriginsBuffers(),
        m_shadowSystem.getSunShadowDescriptor(),
        m_shadowSystem.getPointShadowDescriptor(),
        m_clusteredLighting.getBuffers(),
        m_clusteredLighting.getBufferSize(),
        m_shadowSystem.getSkyHeightmapDescriptor(),
        m_materialOverlayBuffers,
        m_materialOverlayBufferSize,
        static_cast<uint32_t>(m_swapchainImages.size()));
}

void Engine::refreshShadowDescriptorsForImage(uint32_t imageIndex) {
    if (imageIndex >= m_descriptorSets.size()) {
        return;
    }
    const auto& shadowBuffers = m_shadowSystem.getShadowDataBuffers();
    if (imageIndex >= shadowBuffers.size()) {
        return;
    }

    VkDescriptorBufferInfo shadowInfo{};
    shadowInfo.buffer = shadowBuffers[imageIndex];
    shadowInfo.offset = 0;
    shadowInfo.range = sizeof(ShadowSystem::ShadowGPUData);

    VkDescriptorImageInfo sunInfo = m_shadowSystem.getSunShadowDescriptor();
    VkDescriptorImageInfo pointInfo = m_shadowSystem.getPointShadowDescriptor();

    std::array<VkWriteDescriptorSet, 3> writes{};

    writes[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[0].dstSet = m_descriptorSets[imageIndex];
    writes[0].dstBinding = 5;
    writes[0].dstArrayElement = 0;
    writes[0].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    writes[0].descriptorCount = 1;
    writes[0].pBufferInfo = &shadowInfo;

    writes[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[1].dstSet = m_descriptorSets[imageIndex];
    writes[1].dstBinding = 6;
    writes[1].dstArrayElement = 0;
    writes[1].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    writes[1].descriptorCount = 1;
    writes[1].pImageInfo = &sunInfo;

    writes[2].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[2].dstSet = m_descriptorSets[imageIndex];
    writes[2].dstBinding = 7;
    writes[2].dstArrayElement = 0;
    writes[2].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    writes[2].descriptorCount = 1;
    writes[2].pImageInfo = &pointInfo;

    vkUpdateDescriptorSets(m_device, static_cast<uint32_t>(writes.size()), writes.data(), 0, nullptr);
}

void Engine::bindGpuVisibleOriginsToSlot1() {
    // Phase D — one-time write of array slot 1 (GPU culling visible-origins buffer)
    // for the bindless ChunkOrigins[2] SSBO array at set=0, binding=1. Slot 0 was
    // written at descriptor-set creation with the static origins buffer.
    VkBuffer visibleOrigins = m_gpuCulling.getVisibleOriginsBuffer();
    if (visibleOrigins == VK_NULL_HANDLE) return;
    VkDescriptorBufferInfo info{};
    info.buffer = visibleOrigins;
    info.offset = 0;
    info.range = VK_WHOLE_SIZE;
    std::vector<VkWriteDescriptorSet> writes(m_descriptorSets.size());
    for (size_t i = 0; i < m_descriptorSets.size(); ++i) {
        writes[i].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[i].dstSet = m_descriptorSets[i];
        writes[i].dstBinding = 1;
        writes[i].dstArrayElement = 1;  // slot 1 — GPU visible origins
        writes[i].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        writes[i].descriptorCount = 1;
        writes[i].pBufferInfo = &info;
    }
    vkUpdateDescriptorSets(m_device, static_cast<uint32_t>(writes.size()), writes.data(), 0, nullptr);
}

void Engine::createCommandBuffers(){
    m_commandBuffers = Sync::createCommandBuffers(
        m_device, m_commandPool, static_cast<uint32_t>(m_swapchainFramebuffers.size()));
}

void Engine::createSyncObjects(){
    auto result = Sync::createSyncObjects(m_device, MAX_FRAMES_IN_FLIGHT);
    m_frames.resize(MAX_FRAMES_IN_FLIGHT);
    for (size_t i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        m_frames[i].imageAvailable = result.frames[i].imageAvailable;
        m_frames[i].renderFinishedMain = result.frames[i].renderFinishedMain;
        m_frames[i].renderFinishedGameplay = result.frames[i].renderFinishedGameplay;
        m_frames[i].inFlight = result.frames[i].inFlight;
    }
    m_imageInFlight.resize(m_swapchainImages.size(), VK_NULL_HANDLE);
    m_uploadTimeline = result.uploadTimeline;
    m_hiZTimeline = result.hiZTimeline;
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

## src\core\engine\EngineSettingsPersistence.cpp

Description: No CC-DESC found.

````cpp
// EngineSettingsPersistence.cpp - Save/load/reset engine settings
// Stub implementations — settings persistence not yet implemented.

#include "core/engine/Engine.h"

void Engine::loadSettings() {
    // TODO: load settings from file
}

void Engine::saveSettings() {
    // TODO: save settings to file
}

void Engine::resetSettings() {
    // TODO: reset settings to defaults
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
constexpr float kHiZMaxCameraRotationDegrees = 12.0f;
constexpr float kHiZMaxCameraTranslationMeters = 2.0f;

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
        constexpr float kMotionPadPosThresholdM    = 0.05f;
        constexpr float kMotionPadAngleThresholdR  = 0.008727f; // ~0.5 degrees
        constexpr float kMotionPadHighPosThresholdM   = 0.20f;
        constexpr float kMotionPadHighAngleThresholdR = 0.034907f; // ~2.0 degrees

        bool cameraMovedFast = false;
        uint32_t hiZMotionPaddingTexels = 0;
        if (m_prevHiZFrameValid) {
            const CameraState& camState = m_camera.getState();
            const glm::vec3 curFront = glm::normalize(camState.front);

            const float posDelta   = glm::length(camState.position - m_prevHiZCameraPos);
            // dot clamped to [-1,1] to protect acos from NaN on denormals
            const float cosAngle   = glm::clamp(glm::dot(curFront, m_prevHiZCameraFront), -1.0f, 1.0f);
            const float angleDelta = std::acos(cosAngle);

            cameraMovedFast = (posDelta > kPosThresholdM) || (angleDelta > kAngleThresholdR);
            if (temporalHiZViable) {
                if (posDelta > kMotionPadHighPosThresholdM || angleDelta > kMotionPadHighAngleThresholdR) {
                    hiZMotionPaddingTexels = 2u;
                } else if (posDelta > kMotionPadPosThresholdM || angleDelta > kMotionPadAngleThresholdR) {
                    hiZMotionPaddingTexels = 1u;
                }
            }
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
                                   captureDebugStats, suppressTemporalCoherence, captureHiZBlinkLog,
                                   hiZMotionPaddingTexels);

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

## src\core\engine\EngineShaderHotReload.cpp

Description: No CC-DESC found.

````cpp
#include "core/engine/Engine.h"

#include <iostream>
#include <sstream>

namespace {

std::string joinShaderList(const std::vector<std::string>& shaders) {
    std::ostringstream out;
    for (std::size_t i = 0; i < shaders.size(); ++i) {
        if (i > 0) {
            out << ", ";
        }
        out << shaders[i];
    }
    return out.str();
}

} // namespace

void Engine::initShaderHotReload() {
    m_shaderHotReload.initialize();
    registerShaderUsageManifest();

    if (m_shaderHotReload.isAvailable()) {
        std::cout << "[Engine] Shader hot reload ready (" << m_shaderHotReload.getEntries().size()
                  << " source files)" << std::endl;
    } else {
        std::cout << "[Engine] Shader hot reload disabled: "
                  << m_shaderHotReload.getLastStatus() << std::endl;
    }
}

void Engine::registerShaderUsageManifest() {
    using ReloadPolicy = ShaderHotReloadService::ReloadPolicy;

    m_shaderHotReload.clearUsageManifest();

    auto reg = [this](const char* displayPath, ReloadPolicy policy, const char* owner) {
        m_shaderHotReload.registerReferencedShader(displayPath, policy, owner);
    };

    reg("shaders/terrain/cube.vert", ReloadPolicy::Swapchain, "Terrain");
    reg("shaders/terrain/cube.frag", ReloadPolicy::Swapchain, "Terrain");
    reg("shaders/terrain/dccm_terrain.vert", ReloadPolicy::Swapchain, "Terrain");
    reg("shaders/terrain/dccm_terrain.frag", ReloadPolicy::Swapchain, "Terrain");
    reg("shaders/terrain/placed_cube.vert", ReloadPolicy::Swapchain, "PlacedCube");

    reg("shaders/sky/sky.vert", ReloadPolicy::Swapchain, "SkySystem");
    reg("shaders/sky/sky.frag", ReloadPolicy::Swapchain, "SkySystem");
    reg("shaders/sky/cloud.vert", ReloadPolicy::Swapchain, "CloudSystem");
    reg("shaders/sky/cloud.frag", ReloadPolicy::Swapchain, "CloudSystem");
    reg("shaders/sky/celestial.vert", ReloadPolicy::Swapchain, "CelestialSystem");
    reg("shaders/sky/celestial.frag", ReloadPolicy::Swapchain, "CelestialSystem");
    reg("shaders/sky/star.vert", ReloadPolicy::Swapchain, "StarSystem");
    reg("shaders/sky/star.frag", ReloadPolicy::Swapchain, "StarSystem");

    reg("shaders/lighting/light_glow.vert", ReloadPolicy::Swapchain, "LightGlowSystem");
    reg("shaders/lighting/light_glow.frag", ReloadPolicy::Swapchain, "LightGlowSystem");

    reg("shaders/culling/depth_reduce.comp", ReloadPolicy::Culling, "HiZPyramid");
    reg("shaders/culling/frustum_filter.comp", ReloadPolicy::Culling, "GPUCullingSystem");
    reg("shaders/culling/frustum_cull.comp", ReloadPolicy::Culling, "GPUCullingSystem");
    reg("shaders/culling/frustum_dispatch.comp", ReloadPolicy::Culling, "GPUCullingSystem");

    reg("shaders/tjunctionfix/tjunction_fix.vert", ReloadPolicy::Swapchain, "TJunctionFixSystem");
    reg("shaders/tjunctionfix/tjunction_fix.frag", ReloadPolicy::Swapchain, "TJunctionFixSystem");

    reg("shaders/shadow/point_shadow_terrain.vert", ReloadPolicy::Shadow, "ShadowSystem");
    reg("shaders/shadow/point_shadow_cube.vert", ReloadPolicy::Shadow, "ShadowSystem");
    reg("shaders/shadow/point_shadow_depth.frag", ReloadPolicy::Shadow, "ShadowSystem");

    m_shaderHotReload.applyUsageManifest();
}

void Engine::rebuildCullingShaders() {
    m_hiZPyramid.reloadComputeShader();
    m_gpuCulling.reloadShaders();
    m_gpuCulling.bindHiZPyramid(m_hiZPyramid.getImageView(), m_hiZPyramid.getSampler());
}

void Engine::rebuildShadowSystem() {
    if (m_shadowSystem.isReady()) {
        m_shadowSystem.cleanup();
    }

    m_shadowSystem.init(m_device,
                        m_physicalDevice,
                        m_descriptorSetLayout,
                        m_commandPool,
                        m_graphicsQueue,
                        static_cast<uint32_t>(m_swapchainImages.size()),
                        !m_perfMode);
}

void Engine::processShaderHotReload() {
    m_shaderHotReload.tick();

    const auto request = m_shaderHotReload.consumePendingReload();
    if (!request.hasCompiledShaders() && !request.requiresWork()) {
        return;
    }

    if (!request.compiledShaders.empty()) {
        std::cout << "[Engine] Compiled shaders: "
                  << joinShaderList(request.compiledShaders) << std::endl;
    }

    if (!request.requiresWork()) {
        return;
    }

    if (!request.liveReloadShaders.empty()) {
        std::cout << "[Engine] Applying live shader reload for: "
                  << joinShaderList(request.liveReloadShaders) << std::endl;
    }

    if (request.rebuildCullingShaders || request.rebuildShadowSystem) {
        vkDeviceWaitIdle(m_device);
    }

    if (request.rebuildCullingShaders) {
        rebuildCullingShaders();
    }

    if (request.rebuildShadowSystem) {
        rebuildShadowSystem();
    }

    if (request.recreateSwapchain) {
        recreateSwapchain();
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
    # core/engine/ - Engine class facade + domain split files
    core/engine/Engine.cpp
    core/engine/lifecycle/EngineLifecycle.cpp
    core/engine/window/EngineWindow.cpp
    core/engine/diagnostics/EnginePerfDiagnostics.cpp
    core/engine/diagnostics/EngineGModeDiagnostics.cpp
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
