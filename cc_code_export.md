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
                                      bool hiZBlinkLogEnabled,
                                      uint32_t hiZMotionPaddingTexels) {
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
        std::min<uint32_t>(hiZMotionPaddingTexels, 3u));

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

## include\rendering\culling\GPUCullingSystem.h

Description: No CC-DESC found. C++ struct 'alignas'.

````cpp
#pragma once
// GPT-DESC: Declares GPU-driven chunk culling buffers, push constants, and diagnostics.

#include <vulkan/vulkan.h>
#include <glm/glm.hpp>
#include <vector>
#include <cstdint>
#include <atomic>
#include <shared_mutex>
#include <mutex>
#include <unordered_set>
#include <unordered_map>
#include <entt/entt.hpp>

// Must match MAX_SUBCHUNKS in Chunk.h
constexpr uint32_t GPU_MAX_SUBCHUNKS = 64;

// Maximum visible draws output from GPU culling
// Must match MAX_INDIRECT_DRAWS in Engine.h
constexpr uint32_t GPU_CULLING_MAX_DRAWS = 65536;

/**
 * Single subchunk draw command (matches VkDrawIndexedIndirectCommand layout)
 * 20 bytes, padded to 32 for GPU alignment
 */
struct alignas(16) SubChunkDraw {
    uint32_t indexCount;
    uint32_t instanceCount;
    uint32_t firstIndex;
    int32_t  vertexOffset;
    uint32_t firstInstance;  // Placeholder, overwritten by compute shader
    uint32_t _pad0;
    uint32_t _pad1;
    uint32_t _pad2;
};

static_assert(sizeof(SubChunkDraw) == 32, "SubChunkDraw must be 32 bytes");

/**
 * GPU-side chunk draw data structure with multiple subchunk support
 * This is uploaded once per chunk when mesh becomes ready, not per-frame
 * Must match the GLSL struct layout exactly (std430)
 * 
 * A chunk may have 1-N subchunks (split due to 65535 vertex limit)
 * All subchunks share the same AABB and origin - we cull once, emit all.
 */
struct alignas(16) ChunkDrawData {
    // Draw commands for each subchunk
    SubChunkDraw draws[GPU_MAX_SUBCHUNKS];
    
    // AABB (32 bytes) - shared by all subchunks
    glm::vec4 aabbMin;  // xyz = min, w unused
    glm::vec4 aabbMax;  // xyz = max, w unused
    
    // Chunk metadata (16 bytes)
    glm::vec4 origin;   // xyz = origin, w = gpuReadyValue as float bits
    
    // Subchunk count (16 bytes for alignment)
    uint32_t subChunkCount;  // How many subchunks are valid
    // Hi-Z grace: when currentTimeline < hiZGraceTimeline, skip Hi-Z occlusion test
    // for this slot. Topology edits get a short grace window to populate the
    // depth pyramid with their true depth before Hi-Z activates. Initial loads
    // and LOD representation swaps stay immediately occludable.
    uint32_t hiZGraceTimeline;
    uint32_t _pad1;
    uint32_t _pad2;
};

// 64 * 32 (draws) + 32 (aabb) + 16 (origin) + 16 (subChunkCount + hiZGrace + pad)
// = 2048 + 64 = 2112 bytes per slot. Per-chunk GPU footprint grows ~4x but stays
// well within budget (8192 chunks * 2112 = 17.3 MB).
static_assert(sizeof(ChunkDrawData) == 2112, "ChunkDrawData size changed — update GPU_MAX_SUBCHUNKS or this assertion");

/**
 * Push constants for frustum + occlusion culling compute shader
 * Layout must match GLSL std430 exactly
 */
struct CullPushConstants {
    // Current frame VP matrix (64 bytes @ offset 0)
    // Used for current-frame frustum tests.
    glm::mat4 viewProj;
    
    // Metadata (16 bytes @ offset 64)
    uint32_t totalDraws;          // Number of chunks to process
    uint32_t currentTimeline;     // GPU timeline for ready check
    uint32_t hiZEnabled;          // 1 = Hi-Z occlusion culling active
    uint32_t debugEnabled;        // 1 = write debug stats atomics

    // Hi-Z pyramid info (16 bytes @ offset 80)
    // x = mip0 width, y = mip0 height, z = mip levels, w = unused
    glm::vec4 hiZPyramidInfo;

    // Viewport UV transform (16 bytes @ offset 96)
    // uv = viewportOffset + localUv * viewportScale
    // localUv is NDC mapped into [0,1] as if rendering full-screen.
    // x = offsetU, y = offsetV, z = scaleU, w = scaleV
    glm::vec4 viewportUvTransform;

    // Previous frame's VP matrix for Hi-Z projection (64 bytes @ offset 112)
    // The depth pyramid is built from the previous frame's depth buffer,
    // so Hi-Z occlusion must project AABBs with the matching viewProj.
    glm::mat4 prevViewProj;

    // Temporal-coherence info (16 bytes @ offset 176)
    // x = temporalCoherenceEnabled (1 = chunks visible last frame skip Hi-Z this frame)
    // y = motion frame (1 = temporal visibility reuse disabled by the caller;
    //     Hi-Z still compares in previous-frame projection space)
    // z = Hi-Z blink log enabled (debug/event instrumentation only)
    // w = extra Hi-Z screen-rect padding in texels for motion-sensitive frames
    // Hi-Z is an optimization: skipping it can never produce wrong rendering, only a
    // few extra draws that the depth test handles. So this is a pure-perf win on
    // temporally stable views.
    glm::uvec4 temporalInfo;
};                                // Total = 192 bytes

static_assert(sizeof(CullPushConstants) == 192, "CullPushConstants must be 192 bytes");

/**
 * GPUCullingSystem - Manages GPU-driven frustum culling
 * 
 * Responsibilities:
 * - Maintain persistent GPU buffer of all chunk draw data
 * - Run compute shader each frame for frustum culling
 * - Output compacted draw commands for vkCmdDrawIndexedIndirectCount
 * - Free-list allocation for draw slots
 * 
 * This replaces CPU-side gatherDrawCommands() with zero CPU-GPU sync culling.
 */
class GPUCullingSystem {
public:
    GPUCullingSystem() = default;
    ~GPUCullingSystem();
    
    // Non-copyable
    GPUCullingSystem(const GPUCullingSystem&) = delete;
    GPUCullingSystem& operator=(const GPUCullingSystem&) = delete;
    
    /**
     * Initialize the system with Vulkan resources
     * @param device Logical device
     * @param physicalDevice Physical device for memory allocation
     * @param maxChunks Maximum number of chunks to support
     * @param externalOriginsBuffer Optional external buffer for visible origins output
     *        If provided, compute shader will write to this buffer instead of creating its own.
     *        This allows sharing the buffer with the rendering descriptor set.
     */
    void init(VkDevice device, VkPhysicalDevice physicalDevice, uint32_t maxChunks, 
              VkBuffer externalOriginsBuffer = VK_NULL_HANDLE);
    
    /**
     * Cleanup all Vulkan resources
     */
    void cleanup();

    /**
     * Rebuild culling compute shader modules and pipelines while preserving
     * persistent chunk buffers and slot state. Caller should ensure the device
     * is idle before invoking this.
     */
    void reloadShaders();
    
    /**
     * Check if the system is initialized and ready
     */
    bool isReady() const { return m_initialized; }
    
    /**
     * Allocate a slot for a new chunk's draw data.
     * Inactive slots are useful for PendingMeshHandle uploads: data can be
     * staged now, but the slot will not be culled/drawn until activateSlot().
     * @return Slot index, or UINT32_MAX if full
     */
    uint32_t allocateSlot(bool active = true);

    /**
     * Make a previously allocated inactive slot visible to GPU culling.
     * Safe to call on an already-active slot.
     */
    void activateSlot(uint32_t slotIndex);

    /**
     * Atomically activate replacement slots and free retired slots.
     * Used by batched LOD swaps so the active culling list never observes
     * both the old mesh slot and its replacement as drawable.
     */
    void activateSlotsAndFreeSlots(const uint32_t* slotsToActivate,
                                   size_t activateCount,
                                   const uint32_t* slotsToFree,
                                   size_t freeCount);
    
    /**
     * Free a slot when chunk is destroyed.
     * Queues the slot for GPU-side invalidation (subChunkCount zeroed)
     * on the next recordCulling() call, preventing stale draw data.
     * @param slotIndex Slot to free
     */
    void freeSlot(uint32_t slotIndex);

    /**
     * Batch-free multiple slots. Single lock, single HWM recalculation.
     * @param slots Array of slot indices to free
     * @param count Number of slots
     */
    void freeSlots(const uint32_t* slots, size_t count);

    /**
     * CPU-side instrumentation hook for per-slot debug diagnostics.
     * Called by the upload system whenever chunk draw data is recorded for a slot.
     */
    void noteChunkDrawDataUpload(uint32_t slotIndex,
                                 const ChunkDrawData& drawData,
                                 bool fromTerrainEdit,
                                 bool replacesExistingMesh);

    /**
     * Per-slot material-overlay hint for the terrain fragment shader.
     * When true, GPU culling writes visibleOrigins.w = 1 for this slot so
     * cube.vert/cube.frag allow the expensive material-overlay table lookup.
     * When false, visibleOrigins.w = 0 and cube.frag returns the packed
     * mesh material immediately. This avoids per-fragment random SSBO probes
     * for the overwhelming majority of unpainted chunks.
     */
    void setSlotMaterialOverlayHint(uint32_t slotIndex, bool hasOverlay);

    /**
     * Clear all material-overlay hints. Used after a full material-overlay
     * rebuild/clear before re-enabling only the chunks that still have paint.
     */
    void clearAllMaterialOverlayHints();
    
    /**
     * Bind the Hi-Z depth pyramid for occlusion culling.
     * Must be called after init() and whenever the pyramid is resized.
     * @param pyramidView    Full mip-chain image view of the Hi-Z pyramid
     * @param pyramidSampler Min-reduction sampler for the pyramid
     */
    void bindHiZPyramid(VkImageView pyramidView, VkSampler pyramidSampler);
    
    /**
     * Record compute dispatch for frustum + occlusion culling
     * Call this before the draw pass
     * @param cmd Command buffer to record into
     * @param viewProj View-projection matrix for frustum extraction + screen-space AABB
     * @param currentTimeline Current GPU timeline value
     * @param chunkCount Number of active chunks to process (0 = use internal active count)
     * @param pyramidWidth  Hi-Z pyramid mip0 width  (0 = no Hi-Z)
     * @param pyramidHeight Hi-Z pyramid mip0 height (0 = no Hi-Z)
     * @param pyramidMips   Hi-Z pyramid mip count   (0 = no Hi-Z)
     * @param prevViewProj  Previous frame's viewProj (for Hi-Z pyramid projection)
     * @param viewportOffsetX Normalized viewport offset U in [0,1]
     * @param viewportOffsetY Normalized viewport offset V in [0,1]
     * @param viewportScaleX  Normalized viewport width in [0,1]
     * @param viewportScaleY  Normalized viewport height in [0,1]
     * @param disableTemporalCoherenceForFrame When true, keep Hi-Z active but
     *        disable temporal visibility reuse for this dispatch.
     * @param hiZBlinkLogEnabled When true, write per-occlusion debug events.
     * @param hiZMotionPaddingTexels Extra conservative Hi-Z rect padding while moving.
     */
    void recordCulling(VkCommandBuffer cmd, const glm::mat4& viewProj,
                       uint64_t currentTimeline, uint32_t chunkCount = 0,
                       uint32_t pyramidWidth = 0, uint32_t pyramidHeight = 0,
                       uint32_t pyramidMips = 0,
                       const glm::mat4& prevViewProj = glm::mat4(0.0f),
                       float viewportOffsetX = 0.0f, float viewportOffsetY = 0.0f,
                       float viewportScaleX = 1.0f, float viewportScaleY = 1.0f,
                       bool debugEnabled = true,
                       bool disableTemporalCoherenceForFrame = false,
                       bool hiZBlinkLogEnabled = true,
                       uint32_t hiZMotionPaddingTexels = 0);
    
    /**
     * Record memory barriers before draw pass
     * Ensures compute writes are visible to indirect draw
     */
    void recordBarriersBeforeDraw(VkCommandBuffer cmd);
    
    /**
     * Record memory barriers before next frame's culling
     * Ensures previous frame's draw is complete
     */
    void recordBarriersBeforeCull(VkCommandBuffer cmd);
    
    // --- Accessors ---
    VkBuffer getVisibleDrawsBuffer() const { return m_visibleDrawsBuffer; }
    VkBuffer getDrawCountBuffer() const { return m_drawCountBuffer; }
    VkBuffer getVisibleOriginsBuffer() const { return m_visibleOriginsBuffer; }
    VkBuffer getAllDrawsBuffer() const { return m_allDrawsBuffer; }
    
    uint32_t getActiveSlotCount() const { return m_activeSlotCount.load(std::memory_order_relaxed); }
    
    uint32_t getMaxDraws() const { return GPU_CULLING_MAX_DRAWS; }  // Max possible visible draws
    
    /**
     * Get visible draw count from previous frame (async readback)
     * Returns 0 until a draw-count sample readback completes.
     */
    uint32_t getLastVisibleDrawCount() const { return m_lastVisibleDrawCount.load(std::memory_order_relaxed); }
    
    /**
     * Debug stats from culling (updated via readback)
     */
    struct DebugStats {
        uint32_t chunksProcessed;      // [0] Chunks that passed bounds check (CPU-derived)
        uint32_t chunksReady;          // [1] Chunks ready (has subchunks + timeline)
        uint32_t frustumPassed;        // [2] Passed frustum test (visible)
        uint32_t totalThreads;         // [3] Total shader threads dispatched (CPU-derived)
        uint32_t boundsCheckFailed;    // [4] Threads that failed bounds check (CPU-derived)
        uint32_t zeroSubchunks;        // [5] Chunks with subChunkCount=0
        uint32_t notReady;             // [6] Chunks not ready (timeline)
        uint32_t hiZOccluded;          // [7] Chunks occluded by Hi-Z test
        uint32_t hiZTested;            // [8] Chunks that entered Hi-Z test (CPU-derived)
        uint32_t hiZNearPlaneFail;     // [9] Chunks that bailed due to near-plane crossing
        uint32_t pyramidNonZero;       // [10] Chunks where pyramidDepth > 0.0 (pyramid has data)
        uint32_t pyramidAllZero;       // [11] Chunks where pyramidDepth == 0.0 (pyramid empty/sky)
        uint32_t degenerateUV;         // [12] Chunks with degenerate UV coordinates
        uint32_t holeRecoveryFail;     // [13] Chunks where hole recovery failed
        uint32_t hiZDepthTestVisible;  // [14] Chunks visible after Hi-Z depth test
        uint32_t visibleDraws;         // [15] Visible draw commands emitted
    };
    DebugStats getDebugStats() const;

    /**
     * Record compute->transfer barrier used by culling readbacks.
     * Call once before any readback copy commands for the culling pass.
     */
    void recordReadbackBarrier(VkCommandBuffer cmd);
    
    /**
     * Record copy of draw count to readback buffer
     * Call after recordReadbackBarrier()
     */
    void recordDrawCountReadback(VkCommandBuffer cmd);
    
    /**
     * Record clearing of debug stats to 0 before culling
     * Call before recordCulling(); synchronization is handled in recordCulling()
     */
    void recordClearDebugStats(VkCommandBuffer cmd);
    
    /**
     * Record copy of debug stats to readback buffer
     * Call after recordReadbackBarrier()
     */
    void recordDebugStatsReadback(VkCommandBuffer cmd);
    
    /**
     * Read back the draw count from GPU (call after fence wait)
     * This updates getLastVisibleDrawCount() and drains the Hi-Z blink log
     * into the CPU-side accumulating ring (see getHiZBlinkLog()).
     */
    void updateDrawCountFromReadback();

    // ─────────────────────────────────────────────────────────────────
    //  Hi-Z blink log (per-chunk Hi-Z occlusion events)
    // ─────────────────────────────────────────────────────────────────
    // The GPU shader writes one entry per chunk that gets culled by Hi-Z
    // each frame. CPU drains them into a ring on every readback so the
    // Hi-Z debug window can show what's blinking and why (grace expired,
    // pyramid depth too aggressive, etc.). Per-frame GPU buffer is small
    // (HIZ_BLINK_LOG_GPU_CAPACITY); CPU ring keeps the last N events.

    /// One Hi-Z occlusion event captured by the shader. 48 bytes, std430.
    struct HiZBlinkEvent {
        uint32_t chunkIdx;            // Slot index
        uint32_t currentTimeline;     // Cull-frame's currentTimeline (push-const)
        uint32_t hiZGraceTimeline;    // Slot's hiZGraceTimeline at cull time
        uint32_t subChunkCount;       // Slot's subChunkCount at cull time (sanity)
        float    chunkOriginX;        // origin.xyz at cull time (chunk coord, integer-valued)
        float    chunkOriginY;
        float    chunkOriginZ;
        float    nearestDepth;        // Reversed-Z: chunk's nearest (max) depth in NDC
        float    pyramidDepth;        // Min depth sampled from the Hi-Z pyramid (0 = no data)
        float    mipLevel;            // Hi-Z mip the test sampled at
        uint32_t _pad0;
        uint32_t _pad1;
    };
    static_assert(sizeof(HiZBlinkEvent) == 48, "HiZBlinkEvent must be 48 bytes");

    /// Per-frame GPU ring capacity (entries). Excess events are dropped (counted).
    static constexpr uint32_t HIZ_BLINK_LOG_GPU_CAPACITY = 1024;
    /// CPU-side accumulating ring capacity.
    static constexpr uint32_t HIZ_BLINK_LOG_CPU_CAPACITY = 8192;

    /// Snapshot of CPU-side accumulating ring (oldest -> newest).
    struct HiZBlinkLogSnapshot {
        std::vector<HiZBlinkEvent> events;
        uint64_t totalCaptured = 0;   // Lifetime counter
        uint64_t totalDroppedGpu = 0; // Lifetime sum of per-frame drops (GPU ring overflow)
        uint64_t totalDroppedCpu = 0; // Lifetime sum of CPU ring evictions
        uint32_t lastFrameCount = 0;  // Events captured by GPU on the most recent frame
        uint32_t lastFrameDropped = 0;// Events the GPU had to drop on the most recent frame
    };

    /// Snapshot the CPU ring (cheap copy under lock). Pauses-aware: even when
    /// the caller "pauses" the UI the GPU keeps writing; pause is a UI concern.
    HiZBlinkLogSnapshot getHiZBlinkLog() const;

    /// Discard all CPU-side accumulated events (does not affect GPU ring).
    void clearHiZBlinkLog();

    /// Toggle CPU-side draining. When paused the GPU still emits events but the
    /// CPU snapshot freezes (use Clear to release the frozen contents).
    void setHiZBlinkLogPaused(bool paused) { m_hiZBlinkLogPaused.store(paused, std::memory_order_relaxed); }

    /// Enable/disable temporal-coherence Hi-Z skip (Phase A).
    /// When enabled, chunks whose visibility bit was set last frame skip the Hi-Z
    /// occlusion test (still subject to frustum + grace gating). Pure perf win:
    /// can never produce wrong rendering, only at most a small overdraw burst on
    /// chunks that just became occluded. Default: enabled.
    void setTemporalCoherenceEnabled(bool enabled) { m_temporalCoherenceEnabled.store(enabled, std::memory_order_relaxed); }
    bool isTemporalCoherenceEnabled() const { return m_temporalCoherenceEnabled.load(std::memory_order_relaxed); }

    /// Set how often (in cull frames) the temporal visibility mask is wiped to
    /// force a full Hi-Z re-test of every chunk. Lower = safer, higher = faster.
    /// Clamped to [1, 256]. interval=1 effectively disables the optimization.
    void setTemporalRevalidateInterval(uint32_t framesPerReset) {
        m_temporalRevalidateInterval = std::max<uint32_t>(1, std::min<uint32_t>(framesPerReset, 256));
    }
    uint32_t getTemporalRevalidateInterval() const { return m_temporalRevalidateInterval; }
    bool isHiZBlinkLogPaused() const { return m_hiZBlinkLogPaused.load(std::memory_order_relaxed); }

    /// Record clearing of the GPU blink log (count/dropped only) before culling.
    void recordClearHiZBlinkLog(VkCommandBuffer cmd);

    /// Record copy of the GPU blink log to the host-visible readback buffer.
    /// Call after recordReadbackBarrier().
    void recordHiZBlinkLogReadback(VkCommandBuffer cmd);

    // ─────────────────────────────────────────────────────────────────
    //  Terrain-edit visibility transition diagnostics
    // ─────────────────────────────────────────────────────────────────
    // Tracks chunks recently touched by terrain edits and reports when a chunk
    // transitions from drawn -> not drawn (or recovers), including the reason.
    // This replaces the old blink-log-focused UI with state-based analysis.

    enum class EditVisibilityState : uint8_t {
        Unknown = 0,
        VisibleNoHiZ = 1,         // Drawn; Hi-Z disabled this frame
        VisibleHiZGrace = 2,      // Drawn; Hi-Z enabled but grace is active
        VisibleHiZPassed = 3,     // Drawn; Hi-Z active and test passed
        NotDrawnSlotInactive = 4, // Slot freed or inactive
        NotDrawnZeroSubChunks = 5,// subChunkCount == 0
        NotDrawnNoValidDraws = 6, // subChunkCount > 0 but all draws have indexCount == 0
        NotDrawnNotReady = 7,     // gpuReadyValue > currentTimeline
        NotDrawnFrustum = 8,      // Rejected by frustum culling
        NotDrawnHiZOccluded = 9   // Rejected by Hi-Z occlusion
    };

    struct EditVisibilityTrackedChunk {
        uint32_t slot = UINT32_MAX;
        int32_t chunkX = 0;
        int32_t chunkY = 0;
        int32_t chunkZ = 0;
        EditVisibilityState state = EditVisibilityState::Unknown;
        bool drawn = false;
        bool fromTerrainEdit = false;
        bool replacesExistingMesh = false;
        bool hiZEnabled = false;
        bool hiZActive = false;
        bool frustumPassed = false;
        bool ready = false;
        uint32_t subChunkCount = 0;
        uint32_t validDrawCount = 0;
        uint32_t currentTimeline = 0;
        uint32_t gpuReadyTimeline = 0;
        uint32_t hiZGraceTimeline = 0;
        int32_t graceDelta = 0;
        float nearestDepth = 0.0f; // valid when state == NotDrawnHiZOccluded
        float pyramidDepth = 0.0f; // valid when state == NotDrawnHiZOccluded
        float mipLevel = 0.0f;     // valid when state == NotDrawnHiZOccluded
        uint64_t uploadSerial = 0;
        uint64_t editUploadSerial = 0;
        uint32_t watchFramesRemaining = 0;
    };

    struct EditVisibilityEvent {
        uint64_t sequence = 0;
        uint64_t dispatchSerial = 0;
        uint32_t slot = UINT32_MAX;
        int32_t chunkX = 0;
        int32_t chunkY = 0;
        int32_t chunkZ = 0;
        EditVisibilityState previousState = EditVisibilityState::Unknown;
        EditVisibilityState newState = EditVisibilityState::Unknown;
        bool fromTerrainEdit = false;
        bool replacesExistingMesh = false;
        bool drawnBefore = false;
        bool drawnAfter = false;
        uint32_t currentTimeline = 0;
        uint32_t gpuReadyTimeline = 0;
        uint32_t hiZGraceTimeline = 0;
        int32_t graceDelta = 0;
        bool hiZEnabled = false;
        bool hiZActive = false;
        bool frustumPassed = false;
        bool ready = false;
        float nearestDepth = 0.0f;
        float pyramidDepth = 0.0f;
        float mipLevel = 0.0f;
        uint64_t editUploadSerial = 0;
    };

    struct EditVisibilitySnapshot {
        std::vector<EditVisibilityTrackedChunk> trackedChunks;
        std::vector<EditVisibilityEvent> events;
        uint64_t totalDropEvents = 0;
        uint64_t totalRecoveryEvents = 0;
        uint64_t lastDispatchSerial = 0;
    };

    EditVisibilitySnapshot getEditVisibilitySnapshot() const;
    void clearEditVisibilitySnapshot();
    static const char* editVisibilityStateName(EditVisibilityState state);
    static bool editVisibilityStateIsDrawn(EditVisibilityState state);

    // Geometry diff around GPU-culling toggle (G key): compares visible chunk
    // sets before vs after mode switch and attaches edit-state reason data.
    struct GModeGeometryDiffRecord {
        int32_t chunkX = 0;
        int32_t chunkY = 0;
        int32_t chunkZ = 0;
        bool visibleBefore = false;
        bool visibleAfter = false;
        bool hasTrackedState = false;
        EditVisibilityState trackedState = EditVisibilityState::Unknown;
        bool fromTerrainEdit = false;
        bool replacesExistingMesh = false;
        bool hiZEnabled = false;
        bool hiZActive = false;
        bool frustumPassed = false;
        bool ready = false;
        uint32_t currentTimeline = 0;
        uint32_t gpuReadyTimeline = 0;
        uint32_t hiZGraceTimeline = 0;
        int32_t graceDelta = 0;
        float nearestDepth = 0.0f;
        float pyramidDepth = 0.0f;
        float mipLevel = 0.0f;
        uint64_t editUploadSerial = 0;
    };

    struct GModeGeometryDiffEvent {
        uint64_t sequence = 0;
        uint64_t toggleSerial = 0;
        bool beforeGpuMode = false;
        bool afterGpuMode = false;
        int32_t chunkX = 0;
        int32_t chunkY = 0;
        int32_t chunkZ = 0;
        bool visibleBefore = false;
        bool visibleAfter = false;
        bool hasTrackedState = false;
        EditVisibilityState trackedState = EditVisibilityState::Unknown;
        bool fromTerrainEdit = false;
        bool replacesExistingMesh = false;
        bool hiZEnabled = false;
        bool hiZActive = false;
        bool frustumPassed = false;
        bool ready = false;
        uint32_t currentTimeline = 0;
        uint32_t gpuReadyTimeline = 0;
        uint32_t hiZGraceTimeline = 0;
        int32_t graceDelta = 0;
        float nearestDepth = 0.0f;
        float pyramidDepth = 0.0f;
        float mipLevel = 0.0f;
        uint64_t editUploadSerial = 0;
    };

    struct GModeGeometryDiffSnapshot {
        bool captureActive = false;
        uint64_t captureToggleSerial = 0;
        bool captureBeforeGpuMode = false;
        bool captureAfterGpuMode = false;
        uint32_t captureFramesRemaining = 0;
        bool lastCaptureTimedOut = false;
        uint64_t lastToggleSerial = 0;
        uint32_t lastDiffCount = 0;
        uint64_t totalEvents = 0;
        std::vector<GModeGeometryDiffEvent> events;
    };

    void setGModeGeometryDiffCaptureState(bool active,
                                          uint64_t toggleSerial,
                                          bool beforeGpuMode,
                                          bool afterGpuMode,
                                          uint32_t framesRemaining,
                                          bool timedOut = false);
    void recordGModeGeometryDiff(uint64_t toggleSerial,
                                 bool beforeGpuMode,
                                 bool afterGpuMode,
                                 const std::vector<GModeGeometryDiffRecord>& records,
                                 bool timedOut);
    GModeGeometryDiffSnapshot getGModeGeometryDiffSnapshot() const;
    void clearGModeGeometryDiffSnapshot();
    static const char* cullingModeName(bool gpuMode);

    void resetAllSlots();

private:
    void createBuffers();
    void createFrustumCullPipeline();
    void createFrustumDescriptorSets();
    
    void extractFrustumPlanes(const glm::mat4& viewProj, glm::vec4 outPlanes[6]);
    
    VkDevice m_device{VK_NULL_HANDLE};
    VkPhysicalDevice m_physicalDevice{VK_NULL_HANDLE};
    uint32_t m_maxChunks{0};
    
    // GPU buffers
    VkBuffer m_allDrawsBuffer{VK_NULL_HANDLE};       // All chunk draw data (persistent)
    VkDeviceMemory m_allDrawsMemory{VK_NULL_HANDLE};
    
    VkBuffer m_visibleDrawsBuffer{VK_NULL_HANDLE};   // Compacted visible draws (per-frame output)
    VkDeviceMemory m_visibleDrawsMemory{VK_NULL_HANDLE};
    
    VkBuffer m_drawCountBuffer{VK_NULL_HANDLE};      // Atomic counter for visible count
    VkDeviceMemory m_drawCountMemory{VK_NULL_HANDLE};
    
    VkBuffer m_visibleOriginsBuffer{VK_NULL_HANDLE}; // Chunk origins for visible draws
    VkDeviceMemory m_visibleOriginsMemory{VK_NULL_HANDLE};
    VkBuffer m_externalOriginsBuffer{VK_NULL_HANDLE}; // External origins buffer (if provided)
    bool m_usingExternalOriginsBuffer{false};          // Whether we're using external buffer

    // Compact active slot index list for dispatch indirection.
    // GPU reads this to avoid iterating high-water holes.
    VkBuffer m_activeIndicesBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_activeIndicesMemory{VK_NULL_HANDLE};
    VkBuffer m_activeIndicesStagingBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_activeIndicesStagingMemory{VK_NULL_HANDLE};
    uint32_t* m_activeIndicesStagingMapped{nullptr};

    // Frustum-pass output: compact list of frustum-ready chunk slots.
    VkBuffer m_frustumPassedIndicesBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_frustumPassedIndicesMemory{VK_NULL_HANDLE};
    VkBuffer m_frustumPassedCountBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_frustumPassedCountMemory{VK_NULL_HANDLE};
    // Stage-2 dispatch args generated on GPU from frustumPassedCount.
    VkBuffer m_frustumDispatchArgsBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_frustumDispatchArgsMemory{VK_NULL_HANDLE};

    // Temporal-coherence visibility mask (Phase A persistent cull state).
    // One bit per slot: set = chunk passed Hi-Z last frame, clear = was culled.
    // Read by both filter (clears bit on frustum-out) and cull (skips Hi-Z when set
    // AND past grace window). Persistent across frames — never reset on CPU,
    // self-heals via grace timeline on slot reuse.
    VkBuffer m_prevVisibleMaskBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_prevVisibleMaskMemory{VK_NULL_HANDLE};
    VkDeviceSize m_prevVisibleMaskSize{0};
    
    // Readback buffer for draw count (host-visible)
    VkBuffer m_readbackBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_readbackMemory{VK_NULL_HANDLE};
    uint32_t* m_readbackMapped{nullptr};
    std::atomic<uint32_t> m_lastVisibleDrawCount{0};
    
    // Debug stats buffer for culling analysis
    VkBuffer m_debugStatsBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_debugStatsMemory{VK_NULL_HANDLE};
    VkBuffer m_debugStatsReadbackBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_debugStatsReadbackMemory{VK_NULL_HANDLE};
    uint32_t* m_debugStatsMapped{nullptr};

    // Hi-Z blink log (per-frame GPU ring + host-visible readback).
    // Layout in GPU buffer: [count u32][dropped u32][_pad u32][_pad u32]
    //                       then HIZ_BLINK_LOG_GPU_CAPACITY * HiZBlinkEvent.
    VkBuffer m_hiZBlinkLogBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_hiZBlinkLogMemory{VK_NULL_HANDLE};
    VkBuffer m_hiZBlinkLogReadbackBuffer{VK_NULL_HANDLE};
    VkDeviceMemory m_hiZBlinkLogReadbackMemory{VK_NULL_HANDLE};
    uint8_t* m_hiZBlinkLogMapped{nullptr};

    // CPU-side ring + lifetime counters. Mutable to allow snapshot under const.
    mutable std::mutex m_hiZBlinkLogMutex;
    std::vector<HiZBlinkEvent> m_hiZBlinkLogRing;  // size up to HIZ_BLINK_LOG_CPU_CAPACITY
    size_t m_hiZBlinkLogRingHead{0};               // Next write index (wraps)
    bool m_hiZBlinkLogRingFull{false};
    uint64_t m_hiZBlinkLogTotalCaptured{0};
    uint64_t m_hiZBlinkLogTotalDroppedGpu{0};
    uint64_t m_hiZBlinkLogTotalDroppedCpu{0};
    uint32_t m_hiZBlinkLogLastFrameCount{0};
    uint32_t m_hiZBlinkLogLastFrameDropped{0};
    std::atomic<bool> m_hiZBlinkLogPaused{false};
    bool m_hiZBlinkLogReadbackPending{false};

    // Phase A — temporal coherence toggle (default on, can be disabled via debug UI).
    // The persistent visibility mask is reset every m_temporalRevalidateInterval
    // frames so chunks that became occluded eventually get re-tested by Hi-Z.
    // Default to revalidating every frame: the Hi-Z test is cheaper than drawing
    // stale visible chunks, and it avoids "looking down at two chunks, drawing
    // the old around-me set" overdraw. Raising this is a pure perf/overdraw tradeoff.
    std::atomic<bool> m_temporalCoherenceEnabled{true};
    uint32_t m_temporalFrameCounter{0};
    uint32_t m_temporalRevalidateInterval{1};
    
    // Two-stage compute pipelines:
    // 1) frustum filter -> compact frustum-passed chunk slots
    // 2) occlusion + draw emission from compact list
    VkShaderModule m_frustumFilterShader{VK_NULL_HANDLE};
    VkShaderModule m_frustumCullShader{VK_NULL_HANDLE};
    VkShaderModule m_frustumDispatchShader{VK_NULL_HANDLE};
    VkPipelineLayout m_frustumPipelineLayout{VK_NULL_HANDLE};
    VkPipeline m_frustumFilterPipeline{VK_NULL_HANDLE};
    VkPipeline m_frustumPipeline{VK_NULL_HANDLE};
    VkPipeline m_frustumDispatchPipeline{VK_NULL_HANDLE};
    
    // Frustum descriptors
    VkDescriptorSetLayout m_frustumDescriptorSetLayout{VK_NULL_HANDLE};
    VkDescriptorPool m_descriptorPool{VK_NULL_HANDLE};
    VkDescriptorSet m_frustumDescriptorSet{VK_NULL_HANDLE};
    
    // Hi-Z pyramid binding state
    bool m_hiZBound{false};
    
    // Free-list for slot allocation
    std::vector<uint32_t> m_freeSlots;
    std::vector<bool> m_slotOccupied;  // Bitset for O(1) HWM recalculation
    std::vector<uint32_t> m_activeSlots;         // Compact list of currently active slots
    std::vector<uint32_t> m_slotToActiveIndex;   // slot -> index in m_activeSlots (or UINT32_MAX)
    bool m_activeIndicesDirty{false};            // Active list changed; upload before next dispatch
    std::atomic<uint32_t> m_activeSlotCount{0};
    std::atomic<uint32_t> m_highWaterMark{0};  // One past highest active slot index
    // Last dispatch metadata used to derive deterministic debug counters on CPU.
    std::atomic<uint32_t> m_lastDispatchChunkCount{0};
    std::atomic<uint32_t> m_lastDispatchHiZEnabled{0};
    bool m_drawCountReadbackPending{false};
    std::unordered_set<uint32_t> m_pendingInvalidations;  // Slots to zero subChunkCount on GPU

    // Per-slot material overlay hints. CPU owns the authoritative vector;
    // recordCulling() drains pending changes and writes them into ChunkDrawData._pad1
    // using vkCmdFillBuffer before the culling compute shader reads allDraws.
    // This reuses existing padding, so ChunkDrawData's GPU ABI and size stay fixed.
    std::vector<uint32_t> m_slotMaterialOverlayHints;
    std::unordered_map<uint32_t, uint32_t> m_pendingMaterialOverlayHintUpdates;

    // Phase C — explicit visibility-mask invalidations.
    // On freeSlot/upload/edit we enqueue the mask WORD index containing the slot;
    // recordCulling drains the set and clears the entire 32-bit word with vkCmdFillBuffer.
    // Clearing 32 bits at a time is conservative: at worst, up to 31 unrelated chunks in
    // the same word will be re-tested by Hi-Z next frame (correctness preserved — Hi-Z is
    // an optimization, not required). Avoids any per-bit RMW on the GPU.
    // This complements (but does NOT replace) the hiZGraceTimeline mechanism, which still
    // forces re-test for the freshly uploaded slot itself; explicit clearing tightens the
    // invariant so we never temporally-skip on a stale bit even outside the grace window.
    std::unordered_set<uint32_t> m_pendingMaskWordClears;
    mutable std::mutex m_slotMutex;

    // Terrain-edit visibility diagnostics.
    static constexpr uint32_t EDIT_VISIBILITY_WATCH_FRAMES = 360;
    static constexpr size_t EDIT_VISIBILITY_EVENT_CAPACITY = 8192;

    struct EditWatchSlotState {
        bool hasMetadata = false;
        bool fromTerrainEdit = false;
        bool replacesExistingMesh = false;
        glm::vec3 aabbMin{0.0f};
        glm::vec3 aabbMax{0.0f};
        int32_t chunkX = 0;
        int32_t chunkY = 0;
        int32_t chunkZ = 0;
        uint32_t subChunkCount = 0;
        uint32_t validDrawCount = 0;
        uint32_t gpuReadyTimeline = 0;
        uint32_t hiZGraceTimeline = 0;
        uint64_t uploadSerial = 0;
        uint64_t editUploadSerial = 0;
        uint32_t watchFramesRemaining = 0;
        bool lastDrawnKnown = false;
        bool lastDrawn = false;
        EditVisibilityState lastState = EditVisibilityState::Unknown;
    };

    struct PendingTrackedEditChunk {
        uint32_t slot = UINT32_MAX;
        int32_t chunkX = 0;
        int32_t chunkY = 0;
        int32_t chunkZ = 0;
        bool fromTerrainEdit = false;
        bool replacesExistingMesh = false;
        uint32_t subChunkCount = 0;
        uint32_t validDrawCount = 0;
        uint32_t currentTimeline = 0;
        uint32_t gpuReadyTimeline = 0;
        uint32_t hiZGraceTimeline = 0;
        bool slotOccupied = false;
        bool ready = false;
        bool frustumPassed = false;
        bool hiZEnabled = false;
        bool hiZActive = false;
        uint64_t uploadSerial = 0;
        uint64_t editUploadSerial = 0;
        uint32_t watchFramesRemaining = 0;
    };

    struct PendingEditDispatchContext {
        uint64_t dispatchSerial = 0;
        std::vector<PendingTrackedEditChunk> tracked;
    };

    std::vector<EditWatchSlotState> m_editWatchStates;
    std::vector<uint32_t> m_editWatchedSlots;
    PendingEditDispatchContext m_pendingEditDispatch;
    std::vector<EditVisibilityTrackedChunk> m_lastTrackedEditChunks;
    std::vector<EditVisibilityEvent> m_editVisibilityEvents;
    uint64_t m_editVisibilityDispatchSerial = 0;
    uint64_t m_editVisibilityLastProcessedDispatchSerial = 0;
    uint64_t m_editVisibilityEventSerial = 0;
    uint64_t m_editVisibilityUploadSerial = 0;
    uint64_t m_editVisibilityDropEvents = 0;
    uint64_t m_editVisibilityRecoveryEvents = 0;

    static constexpr size_t G_MODE_DIFF_EVENT_CAPACITY = 8192;
    std::vector<GModeGeometryDiffEvent> m_gModeGeometryDiffEvents;
    uint64_t m_gModeGeometryDiffEventSerial = 0;
    uint64_t m_gModeGeometryDiffTotalEvents = 0;
    bool m_gModeGeometryCaptureActive = false;
    uint64_t m_gModeGeometryCaptureToggleSerial = 0;
    bool m_gModeGeometryCaptureBeforeGpu = false;
    bool m_gModeGeometryCaptureAfterGpu = false;
    uint32_t m_gModeGeometryCaptureFramesRemaining = 0;
    bool m_gModeGeometryLastCaptureTimedOut = false;
    uint64_t m_gModeGeometryLastToggleSerial = 0;
    uint32_t m_gModeGeometryLastDiffCount = 0;
    
    bool m_initialized{false};
};

````

## src\rendering\culling\HiZPyramid.cpp

Description: No CC-DESC found. C++ struct 'DepthReducePushConstants'.

````cpp
#include "rendering/culling/HiZPyramid.h"
#include "rendering/common/VulkanHelpers.h"
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

// Push constants for the depth reduce compute shader
struct DepthReducePushConstants {
    uint32_t imageWidth;   // Mip N width
    uint32_t imageHeight;  // Mip N height
    uint32_t levelMask;    // Bits 0..3 = write mip N+1..N+4
    uint32_t _pad0;
};
static_assert(sizeof(DepthReducePushConstants) == 16, "DepthReducePushConstants must be 16 bytes");

// ─────────────────────────────────────────────────────────────
//  Lifecycle
// ─────────────────────────────────────────────────────────────

HiZPyramid::~HiZPyramid() {
    cleanup();
}

void HiZPyramid::init(VkDevice device, VkPhysicalDevice physicalDevice,
                       VkImageView depthImageView,
                       uint32_t swapchainWidth, uint32_t swapchainHeight) {
    if (m_initialized) {
        cleanup();
    }

    m_device = device;
    m_physicalDevice = physicalDevice;
    m_depthImageView = depthImageView;
    m_swapchainWidth = swapchainWidth;
    m_swapchainHeight = swapchainHeight;

    // Pyramid dimensions: previousPow2 ensures all reductions are at most 2x2 (conservative)
    m_pyramidWidth = previousPow2(swapchainWidth);
    m_pyramidHeight = previousPow2(swapchainHeight);
    m_mipLevels = calculateMipLevels(m_pyramidWidth, m_pyramidHeight);

    if (m_mipLevels > MAX_MIP_LEVELS) {
        m_mipLevels = MAX_MIP_LEVELS;
    }

    std::cout << "[HiZPyramid] Creating depth pyramid: " << m_pyramidWidth << "x" << m_pyramidHeight
              << " (" << m_mipLevels << " mip levels) from " << swapchainWidth << "x" << swapchainHeight
              << " depth buffer" << std::endl;

    // Create resources (order matters)
    if (!m_pipelineCreated) {
        createSampler();
        createComputePipeline();
        m_pipelineCreated = true;
    }

    createPyramidImage();
    createMipViews();
    createDescriptorResources();

    m_initialized = true;
    std::cout << "[HiZPyramid] ✓ Depth pyramid ready" << std::endl;
}

void HiZPyramid::cleanup() {
    if (!m_device) return;

    vkDeviceWaitIdle(m_device);

    destroyPyramidImage();

    // Pipeline resources (only destroyed on full cleanup, not resize)
    if (m_reducePipeline) { vkDestroyPipeline(m_device, m_reducePipeline, nullptr); m_reducePipeline = VK_NULL_HANDLE; }
    if (m_reducePipelineLayout) { vkDestroyPipelineLayout(m_device, m_reducePipelineLayout, nullptr); m_reducePipelineLayout = VK_NULL_HANDLE; }
    if (m_reduceShader) { vkDestroyShaderModule(m_device, m_reduceShader, nullptr); m_reduceShader = VK_NULL_HANDLE; }
    if (m_reduceDescriptorLayout) { vkDestroyDescriptorSetLayout(m_device, m_reduceDescriptorLayout, nullptr); m_reduceDescriptorLayout = VK_NULL_HANDLE; }
    if (m_depthSampler) { vkDestroySampler(m_device, m_depthSampler, nullptr); m_depthSampler = VK_NULL_HANDLE; }
    if (m_reduceSampler) { vkDestroySampler(m_device, m_reduceSampler, nullptr); m_reduceSampler = VK_NULL_HANDLE; }
    if (m_vizSampler) { vkDestroySampler(m_device, m_vizSampler, nullptr); m_vizSampler = VK_NULL_HANDLE; }

    // Remove ImGui texture registration
    if (m_imguiTextureRegistered && m_imguiDescriptorSet) {
        ImGui_ImplVulkan_RemoveTexture(m_imguiDescriptorSet);
        m_imguiDescriptorSet = VK_NULL_HANDLE;
        m_imguiTextureRegistered = false;
    }

    m_pipelineCreated = false;
    m_initialized = false;
}

void HiZPyramid::resize(VkImageView depthImageView, uint32_t swapchainWidth, uint32_t swapchainHeight) {
    if (!m_pipelineCreated) {
        // First time — do full init
        init(m_device, m_physicalDevice, depthImageView, swapchainWidth, swapchainHeight);
        return;
    }

    m_depthImageView = depthImageView;
    m_swapchainWidth = swapchainWidth;
    m_swapchainHeight = swapchainHeight;

    uint32_t newWidth = previousPow2(swapchainWidth);
    uint32_t newHeight = previousPow2(swapchainHeight);
    uint32_t newMips = calculateMipLevels(newWidth, newHeight);
    if (newMips > MAX_MIP_LEVELS) newMips = MAX_MIP_LEVELS;

    if (newWidth == m_pyramidWidth && newHeight == m_pyramidHeight) {
        // Same size — just update the depth image view reference and rebuild descriptors
        destroyPyramidImage();
        m_pyramidWidth = newWidth;
        m_pyramidHeight = newHeight;
        m_mipLevels = newMips;
        createPyramidImage();
        createMipViews();
        createDescriptorResources();
        m_initialized = true;
        return;
    }

    // Different size — recreate pyramid
    destroyPyramidImage();
    m_pyramidWidth = newWidth;
    m_pyramidHeight = newHeight;
    m_mipLevels = newMips;

    std::cout << "[HiZPyramid] Resizing depth pyramid: " << m_pyramidWidth << "x" << m_pyramidHeight
              << " (" << m_mipLevels << " mip levels)" << std::endl;

    createPyramidImage();
    createMipViews();
    createDescriptorResources();
    m_initialized = true;
}

void HiZPyramid::reloadComputeShader() {
    if (!m_device || !m_pipelineCreated) {
        return;
    }

    if (m_descriptorPool) {
        vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
        m_descriptorPool = VK_NULL_HANDLE;
        for (uint32_t i = 0; i < MAX_MIP_LEVELS; ++i) {
            m_mipDescriptorSets[i] = VK_NULL_HANDLE;
        }
    }

    if (m_reducePipeline) { vkDestroyPipeline(m_device, m_reducePipeline, nullptr); m_reducePipeline = VK_NULL_HANDLE; }
    if (m_reducePipelineLayout) { vkDestroyPipelineLayout(m_device, m_reducePipelineLayout, nullptr); m_reducePipelineLayout = VK_NULL_HANDLE; }
    if (m_reduceShader) { vkDestroyShaderModule(m_device, m_reduceShader, nullptr); m_reduceShader = VK_NULL_HANDLE; }
    if (m_reduceDescriptorLayout) { vkDestroyDescriptorSetLayout(m_device, m_reduceDescriptorLayout, nullptr); m_reduceDescriptorLayout = VK_NULL_HANDLE; }

    createComputePipeline();
    if (m_initialized) {
        createDescriptorResources();
    }

    std::cout << "[HiZPyramid] Reloaded depth reduce compute shader" << std::endl;
}


// See also: HiZPyramidResources.cpp, HiZPyramidDiagnostics.cpp

// ─────────────────────────────────────────────────────────────
//  Record pyramid build commands
// ─────────────────────────────────────────────────────────────

void HiZPyramid::updateDepthSource(VkImageView newDepthView) {
    if (!m_initialized || !newDepthView) return;
    if (newDepthView == m_depthImageView) return;  // No change

    m_depthImageView = newDepthView;

    // Update mip 0's source descriptor to point to the new depth view
    VkDescriptorImageInfo srcInfo{};
    srcInfo.sampler = m_reduceSampler;
    srcInfo.imageView = m_depthImageView;
    srcInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    VkWriteDescriptorSet write{};
    write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    write.dstSet = m_mipDescriptorSets[0];
    write.dstBinding = 5;
    write.dstArrayElement = 0;
    write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    write.descriptorCount = 1;
    write.pImageInfo = &srcInfo;

    vkUpdateDescriptorSets(m_device, 1, &write, 0, nullptr);
}

void HiZPyramid::recordBuildPyramid(VkCommandBuffer cmd) {
    if (!m_initialized) return;

    // First build after create/resize: transition image from UNDEFINED -> GENERAL.
    if (!m_pyramidLayoutInitialized) {
        VkImageMemoryBarrier2 barrier{};
        barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        barrier.srcStageMask = VK_PIPELINE_STAGE_2_NONE;
        barrier.srcAccessMask = VK_ACCESS_2_NONE;
        barrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
        barrier.dstAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
        barrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        barrier.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        barrier.image = m_pyramidImage;
        barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        barrier.subresourceRange.baseMipLevel = 0;
        barrier.subresourceRange.levelCount = m_mipLevels;
        barrier.subresourceRange.baseArrayLayer = 0;
        barrier.subresourceRange.layerCount = 1;

        VkDependencyInfo depInfo{};
        depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        depInfo.imageMemoryBarrierCount = 1;
        depInfo.pImageMemoryBarriers = &barrier;

        vkCmdPipelineBarrier2(cmd, &depInfo);

        m_pyramidLayoutInitialized = true;
    } else {
        // Subsequent builds: ensure previous writes (from any earlier build in this
        // or prior submissions) are complete and visible before overwriting.
        // This handles cross-frame synchronization where the timeline semaphore
        // provides execution ordering but explicit barriers are needed for memory
        // visibility of storage image writes.
        VkImageMemoryBarrier2 barrier{};
        barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        barrier.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
        barrier.srcAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
        barrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
        barrier.dstAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
        barrier.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
        barrier.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        barrier.image = m_pyramidImage;
        barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        barrier.subresourceRange.baseMipLevel = 0;
        barrier.subresourceRange.levelCount = m_mipLevels;
        barrier.subresourceRange.baseArrayLayer = 0;
        barrier.subresourceRange.layerCount = 1;

        VkDependencyInfo depInfo{};
        depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        depInfo.imageMemoryBarrierCount = 1;
        depInfo.pImageMemoryBarriers = &barrier;

        vkCmdPipelineBarrier2(cmd, &depInfo);
    }

    // Bind the reduce pipeline once
    vkCmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_reducePipeline);

    // Process mip levels in batches.
    // With a 16x16 workgroup we can reduce up to 5 levels per dispatch:
    // 16x16 -> 8x8 -> 4x4 -> 2x2 -> 1x1.
    uint32_t i = 0;
    while (i < m_mipLevels) {
        uint32_t levelWidth = std::max(1u, m_pyramidWidth >> i);
        uint32_t levelHeight = std::max(1u, m_pyramidHeight >> i);
        uint32_t remainingLevels = m_mipLevels - i;
        uint32_t additionalLevels = (remainingLevels > 1u) ? std::min(4u, remainingLevels - 1u) : 0u;
        uint32_t levelsWritten = 1u + additionalLevels;
        uint32_t levelMask = (additionalLevels == 0u) ? 0u : ((1u << additionalLevels) - 1u);

        // Bind descriptor set for this batch start level.
        vkCmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, m_reducePipelineLayout,
                                0, 1, &m_mipDescriptorSets[i], 0, nullptr);

        // Push constants: base mip dimensions + which higher levels to emit.
        DepthReducePushConstants pushData{};
        pushData.imageWidth = levelWidth;
        pushData.imageHeight = levelHeight;
        pushData.levelMask = levelMask;
        vkCmdPushConstants(cmd, m_reducePipelineLayout, VK_SHADER_STAGE_COMPUTE_BIT,
                           0, sizeof(pushData), &pushData);

        // Dispatch over mip N.
        uint32_t groupsX = (levelWidth + 15) / 16;
        uint32_t groupsY = (levelHeight + 15) / 16;
        vkCmdDispatch(cmd, groupsX, groupsY, 1);

        i += levelsWritten;
        if (i < m_mipLevels) {
            // Next batch samples mip (i - 1), so make just that mip readable.
            uint32_t sourceMipForNextBatch = i - 1u;

            VkImageMemoryBarrier2 barrier{};
            barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
            barrier.srcStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
            barrier.srcAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
            barrier.dstStageMask = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
            barrier.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
            barrier.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
            barrier.newLayout = VK_IMAGE_LAYOUT_GENERAL;
            barrier.image = m_pyramidImage;
            barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            barrier.subresourceRange.baseMipLevel = sourceMipForNextBatch;
            barrier.subresourceRange.levelCount = 1;
            barrier.subresourceRange.baseArrayLayer = 0;
            barrier.subresourceRange.layerCount = 1;

            VkDependencyInfo depInfo{};
            depInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            depInfo.imageMemoryBarrierCount = 1;
            depInfo.pImageMemoryBarriers = &barrier;

            vkCmdPipelineBarrier2(cmd, &depInfo);
        }
    }
}

void HiZPyramid::recordClear(VkCommandBuffer cmd) {
    if (!m_initialized) return;

    // Ensure the image is in GENERAL layout (it is for any frame after the
    // first build). On the first-ever frame we may be called before any build:
    // skip — clearing an undefined image is a no-op for our purposes since the
    // cull shader treats undefined depth as "not occluded" via the temporal
    // viability check.
    if (!m_pyramidLayoutInitialized) return;

    // Order any prior compute writes/reads before the transfer clear, then
    // the clear before the next compute read. We can't predict whether the
    // most recent access was a build (write) or a cull sample (sampled read),
    // so cover both with srcAccessMask.
    VkImageMemoryBarrier2 toTransfer{};
    toTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    toTransfer.srcStageMask  = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    toTransfer.srcAccessMask = VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT | VK_ACCESS_2_SHADER_SAMPLED_READ_BIT;
    toTransfer.dstStageMask  = VK_PIPELINE_STAGE_2_CLEAR_BIT;
    toTransfer.dstAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
    toTransfer.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
    toTransfer.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    toTransfer.image = m_pyramidImage;
    toTransfer.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    toTransfer.subresourceRange.baseMipLevel = 0;
    toTransfer.subresourceRange.levelCount = m_mipLevels;
    toTransfer.subresourceRange.baseArrayLayer = 0;
    toTransfer.subresourceRange.layerCount = 1;

    VkDependencyInfo dep1{};
    dep1.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    dep1.imageMemoryBarrierCount = 1;
    dep1.pImageMemoryBarriers = &toTransfer;
    vkCmdPipelineBarrier2(cmd, &dep1);

    VkClearColorValue clearVal{};
    clearVal.float32[0] = 0.0f;
    clearVal.float32[1] = 0.0f;
    clearVal.float32[2] = 0.0f;
    clearVal.float32[3] = 0.0f;

    VkImageSubresourceRange range{};
    range.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    range.baseMipLevel = 0;
    range.levelCount = m_mipLevels;
    range.baseArrayLayer = 0;
    range.layerCount = 1;

    vkCmdClearColorImage(cmd, m_pyramidImage, VK_IMAGE_LAYOUT_GENERAL,
                         &clearVal, 1, &range);

    VkImageMemoryBarrier2 toCompute{};
    toCompute.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
    toCompute.srcStageMask  = VK_PIPELINE_STAGE_2_CLEAR_BIT;
    toCompute.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
    toCompute.dstStageMask  = VK_PIPELINE_STAGE_2_COMPUTE_SHADER_BIT;
    toCompute.dstAccessMask = VK_ACCESS_2_SHADER_SAMPLED_READ_BIT | VK_ACCESS_2_SHADER_STORAGE_WRITE_BIT;
    toCompute.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
    toCompute.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    toCompute.image = m_pyramidImage;
    toCompute.subresourceRange = range;

    VkDependencyInfo dep2{};
    dep2.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
    dep2.imageMemoryBarrierCount = 1;
    dep2.pImageMemoryBarriers = &toCompute;
    vkCmdPipelineBarrier2(cmd, &dep2);
}

// ─────────────────────────────────────────────────────────────
//  Helpers
// ─────────────────────────────────────────────────────────────

uint32_t HiZPyramid::previousPow2(uint32_t v) {
    // Round down to the largest power of 2 <= v
    // This ensures all mip reductions are exactly 2x (conservative)
    v |= v >> 1;
    v |= v >> 2;
    v |= v >> 4;
    v |= v >> 8;
    v |= v >> 16;
    return (v >> 1) + 1;
}

uint32_t HiZPyramid::calculateMipLevels(uint32_t width, uint32_t height) {
    uint32_t levels = 1;
    uint32_t dim = std::max(width, height);
    while (dim > 1) {
        dim >>= 1;
        levels++;
    }
    return levels;
}

ImTextureID HiZPyramid::getImGuiTextureID() {
    if (!m_initialized || !m_pyramidView || !m_vizSampler) return 0;

    if (!m_imguiTextureRegistered) {
        // Register pyramid mip 0 view with ImGui for display
        // Use GENERAL layout since the pyramid stays in GENERAL after compute writes
        m_imguiDescriptorSet = ImGui_ImplVulkan_AddTexture(
            m_vizSampler, m_mipViews[0], VK_IMAGE_LAYOUT_GENERAL);
        m_imguiTextureRegistered = true;
    }

    return (ImTextureID)m_imguiDescriptorSet;
}

````

## shaders\culling\frustum_cull.comp

Description: No CC-DESC found. C++ struct 'SubChunkDraw'.

````glsl
#version 450
// GPT-DESC: Performs conservative frustum-passed Hi-Z occlusion and draw emission.
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : enable

// Pass 2: Hi-Z occlusion + draw emission for frustum-passed candidates.
// Workgroup size
#define LOCAL_SIZE_X 64
layout(local_size_x = LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

const float HIZ_OCCLUSION_DEPTH_EPSILON = 0.00002;
const float HIZ_HOLE_RELATIVE_THRESHOLD = 0.20;
const float HIZ_HOLE_ABSOLUTE_EPSILON = 0.0001;
const float HIZ_HOLE_RECOVERY_MAX_TEXELS = 12.0;
const float HIZ_RECT_BASE_PADDING_TEXELS = 1.0;
const float HIZ_MOTION_PADDING_MAX_TEXELS = 3.0;

// Constants - must match C++ side
#define MAX_SUBCHUNKS 64
// Must match GPU_CULLING_MAX_DRAWS / MAX_INDIRECT_DRAWS on C++ side.
#define MAX_VISIBLE_DRAWS 65536

// SubChunkDraw - matches C++ struct (32 bytes)
struct SubChunkDraw {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int  vertexOffset;
    uint firstInstance;
    uint _pad0;
    uint _pad1;
    uint _pad2;
};

// ChunkDrawData - matches C++ struct (576 bytes)
struct ChunkDrawData {
    SubChunkDraw draws[MAX_SUBCHUNKS];
    vec4 aabbMin;                        // 16 bytes
    vec4 aabbMax;                        // 16 bytes
    vec4 origin;                         // 16 bytes (xyz = origin, w = gpuReadyValue as float bits)
    uint subChunkCount;                  // 4 bytes
    // Hi-Z grace: skip occlusion test while currentTimeline < hiZGraceTimeline.
    // Topology edits get a short grace window; initial loads and LOD swaps are
    // immediately occludable.
    uint hiZGraceTimeline;
    uint _pad1;
    uint _pad2;
};

// VkDrawIndexedIndirectCommand - matches Vulkan struct (20 bytes, but we use 32 for alignment)
struct DrawCommand {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int  vertexOffset;
    uint firstInstance;
};

// Push constants - must match C++ CullPushConstants (192 bytes)
layout(push_constant) uniform PushConstants {
    mat4 viewProj;          // 64 bytes  (offset 0)   - current frame VP
    uint totalDraws;        // 4 bytes   (offset 64)
    uint currentTimeline;   // 4 bytes   (offset 68)
    uint hiZEnabled;        // 4 bytes   (offset 72)  - 1 = Hi-Z occlusion active
    uint debugEnabled;      // 4 bytes   (offset 76)  - 1 = write debug stats
    vec4 hiZPyramidInfo;    // 16 bytes  (offset 80)  - x=width, y=height, z=mips
    vec4 viewportUvTransform; // 16 bytes (offset 96) - x=offU,y=offV,z=scaleU,w=scaleV
    mat4 prevViewProj;      // 64 bytes  (offset 112) - previous frame's VP for Hi-Z
    uvec4 temporalInfo;     // 16 bytes  (offset 176) - x=temporalCoherenceEnabled, y=motionFrame, z=blinkLog, w=motionPaddingTexels
} pc;

// Descriptor set bindings
layout(set = 0, binding = 0) readonly buffer AllDraws {
    ChunkDrawData chunks[];
} allDraws;

layout(set = 0, binding = 1) writeonly buffer VisibleDraws {
    DrawCommand commands[];
} visibleDraws;

layout(set = 0, binding = 2) buffer DrawCount {
    uint count;
} drawCount;

layout(set = 0, binding = 3) writeonly buffer VisibleOrigins {
    vec4 origins[];
} visibleOrigins;

// Debug stats for culling analysis (binding 4)
// [0] = chunks processed (passed bounds check) [derived on CPU]
// [1] = chunks ready (derived on CPU)
// [2] = frustum passed (copied from frustumPassedCount during readback)
// [3] = total threads dispatched (debug) [derived on CPU]
// [4] = threads that failed bounds check (debug) [derived on CPU]
// [5] = chunks with zero subchunks (debug)
// [6] = chunks not ready (timeline) (debug)
// [7] = chunks occluded by Hi-Z (debug)
// [8] = chunks that entered Hi-Z test (debug) [derived on CPU]
// [9] = chunks that bailed due to near-plane crossing (debug)
// [10] = chunks where pyramidDepth > 0.0 (pyramid has data)
// [11] = chunks where pyramidDepth == 0.0 (pyramid empty/sky)
// [15] = visible draw commands emitted (copied from drawCount during readback)
layout(set = 0, binding = 4) buffer DebugStats {
    uint stats[16];
} debugStats;

// Hi-Z depth pyramid (binding 5) - min-reduction sampler
layout(set = 0, binding = 5) uniform sampler2D hiZPyramid;

// Frustum-pass compact candidate list (binding 7) + count (binding 8)
layout(set = 0, binding = 7) readonly buffer FrustumPassedIndices {
    uint indices[];
} frustumPassedIndices;

layout(set = 0, binding = 8) readonly buffer FrustumPassedCount {
    uint count;
} frustumPassedCount;

// Hi-Z blink log (binding 9): per-frame ring buffer the shader writes one entry to
// every time it culls a chunk via Hi-Z. CPU drains it after the frame and surfaces it
// in the Hi-Z debug window. Capacity must match HIZ_BLINK_LOG_GPU_CAPACITY (C++).
#define HIZ_BLINK_LOG_GPU_CAPACITY 1024u
// Only log occlusion events for chunks whose grace window expired recently.
// Chunks with a grace delta of 400+ are legitimate stable occlusions, not bugs.
// Raise this if you want to capture older chunks too.
#define HIZ_BLINK_LOG_GRACE_THRESHOLD 64u
struct HiZBlinkEvent {
    uint  chunkIdx;
    uint  currentTimeline;
    uint  hiZGraceTimeline;
    uint  subChunkCount;
    float chunkOriginX;
    float chunkOriginY;
    float chunkOriginZ;
    float nearestDepth;
    float pyramidDepth;
    float mipLevel;
    uint  _pad0;
    uint  _pad1;
};
layout(set = 0, binding = 9) buffer HiZBlinkLog {
    uint count;       // atomic write index
    uint dropped;     // events past capacity
    uint _pad0;
    uint _pad1;
    HiZBlinkEvent events[];
} hiZBlinkLog;

// Phase A — persistent visibility mask (one bit per chunk slot).
// Set by this shader when a chunk passes Hi-Z (or temporally skips it).
// Cleared by frustum_filter when the chunk fails frustum/notReady, or by this
// shader when Hi-Z occludes. CPU wipes the entire buffer every N frames so
// chunks that became occluded eventually get re-tested.
layout(set = 0, binding = 11) buffer PrevVisibleMask {
    uint bits[];
} prevVisibleMask;

bool prevVisibleBitSet(uint chunkIdx) {
    uint word = chunkIdx >> 5u;
    uint bit  = 1u << (chunkIdx & 31u);
    return (prevVisibleMask.bits[word] & bit) != 0u;
}

void setPrevVisibleBit(uint chunkIdx) {
    uint word = chunkIdx >> 5u;
    uint bit  = 1u << (chunkIdx & 31u);
    atomicOr(prevVisibleMask.bits[word], bit);
}

void clearPrevVisibleBit(uint chunkIdx) {
    uint word = chunkIdx >> 5u;
    uint mask = ~(1u << (chunkIdx & 31u));
    atomicAnd(prevVisibleMask.bits[word], mask);
}

// Track whether sampled pyramid data looked valid (debug only).
void recordPyramidDepthDebug(float pyramidDepth) {
    if (pc.debugEnabled == 0u) return;
    if (pyramidDepth > 0.0) {
        atomicAdd(debugStats.stats[10], 1);  // pyramidNonZero++
    } else {
        atomicAdd(debugStats.stats[11], 1);  // pyramidAllZero++
    }
}

void accumulatePyramidDepth(float depth,
                            inout uint nonZeroCount,
                            inout float minNonZero,
                            inout float maxNonZero) {
    if (depth <= 0.0) {
        return;
    }
    nonZeroCount++;
    minNonZero = min(minNonZero, depth);
    maxNonZero = max(maxNonZero, depth);
}

bool coherentPyramidDepths(uint nonZeroCount, float minNonZero, float maxNonZero) {
    return nonZeroCount >= 3u &&
           maxNonZero > 0.0 &&
           (maxNonZero - minNonZero) <= max(maxNonZero * HIZ_HOLE_RELATIVE_THRESHOLD,
                                            HIZ_HOLE_ABSOLUTE_EPSILON);
}

bool projectAabbToUvRect(mat4 viewProj,
                         vec3 aabbMin,
                         vec3 aabbMax,
                         out vec4 uvRect,
                         out float nearestDepth) {
    float minX =  1.0 / 0.0;  // +inf
    float minY =  1.0 / 0.0;
    float maxX = -1.0 / 0.0;  // -inf
    float maxY = -1.0 / 0.0;
    nearestDepth = 0.0;        // far in reversed-Z

    for (int i = 0; i < 8; ++i) {
        vec3 corner = vec3(
            ((i & 1) != 0) ? aabbMax.x : aabbMin.x,
            ((i & 2) != 0) ? aabbMax.y : aabbMin.y,
            ((i & 4) != 0) ? aabbMax.z : aabbMin.z);

        vec4 clip = viewProj * vec4(corner, 1.0);

        // If any corner is behind the camera, conservatively keep visible.
        if (clip.w <= 0.0) {
            return false;
        }

        vec3 ndc = clip.xyz / clip.w;

        minX = min(minX, ndc.x);
        maxX = max(maxX, ndc.x);
        minY = min(minY, ndc.y);
        maxY = max(maxY, ndc.y);
        nearestDepth = max(nearestDepth, ndc.z);
    }

    // Convert NDC to UV space [0, 1].
    // NDC x,y are in [-1, 1].
    // The main pipeline uses a Y-flipped viewport (y=height, height=-height)
    // which maps NDC Y=+1 → pixel row 0 (top) and NDC Y=-1 → pixel row H (bottom).
    // Texture UV (0,0) is texel (0,0) = top-left = NDC Y=+1.
    // Therefore:  uvY = (-ndcY) * 0.5 + 0.5   (negate Y before mapping)
    float localMinX = clamp( minX * 0.5 + 0.5, 0.0, 1.0);
    float localMaxX = clamp( maxX * 0.5 + 0.5, 0.0, 1.0);
    // Note: -maxY becomes the min UV, and -minY becomes the max UV
    float localMinY = clamp(-maxY * 0.5 + 0.5, 0.0, 1.0);
    float localMaxY = clamp(-minY * 0.5 + 0.5, 0.0, 1.0);

    // Map local UVs into the actual gameplay viewport region in the full
    // depth texture. This is required when rendering uses a sub-viewport
    // (editor layout, split panes, etc).
    uvRect = vec4(
        clamp(pc.viewportUvTransform.x + localMinX * pc.viewportUvTransform.z, 0.0, 1.0),
        clamp(pc.viewportUvTransform.y + localMinY * pc.viewportUvTransform.w, 0.0, 1.0),
        clamp(pc.viewportUvTransform.x + localMaxX * pc.viewportUvTransform.z, 0.0, 1.0),
        clamp(pc.viewportUvTransform.y + localMaxY * pc.viewportUvTransform.w, 0.0, 1.0));
    return uvRect.x < uvRect.z && uvRect.y < uvRect.w;
}

bool hiZBlinkLogEnabled() {
    return pc.temporalInfo.z != 0u;
}

// Hi-Z occlusion culling test
// Projects the AABB to screen space, selects a mip level where the rect
// fits in ~2x2 texels, and compares against the depth pyramid.
// Returns true if the chunk is FULLY OCCLUDED (should be culled).
//
// Reversed-Z:  near=1.0, far=0.0
// Min-reduction sampler returns the farthest (smallest) depth in a region.
// If the chunk's nearest depth > the pyramid's farthest depth in that region,
// the chunk is behind everything and fully occluded.
//
// Out params expose the values used for the test so the caller can log them
// to the Hi-Z blink log when an occlusion event triggers.
bool hiZOcclude(vec3 aabbMin, vec3 aabbMax,
                out float outNearestDepth,
                out float outPyramidDepth,
                out float outMipLevel) {
    outNearestDepth = 0.0;
    outPyramidDepth = 0.0;
    outMipLevel = 0.0;

    vec4 prevRect;
    float prevNearest = 0.0;
    if (!projectAabbToUvRect(pc.prevViewProj, aabbMin, aabbMax, prevRect, prevNearest)) {
        if (pc.debugEnabled != 0u) {
            atomicAdd(debugStats.stats[9], 1);  // hiZNearPlaneFail++
        }
        return false;
    }

    // The pyramid was built from the previous frame, so both the sampled rect
    // and depth comparison must stay in previous-frame projection space. Mixing
    // current-frame nearest depth with previous-frame Hi-Z makes normal camera
    // rotation/down-up look paths reject real occlusion and draw behind walls.
    vec4 cullRect = prevRect;
    float nearestDepth = prevNearest;

    float uvMinX = cullRect.x;
    float uvMinY = cullRect.y;
    float uvMaxX = cullRect.z;
    float uvMaxY = cullRect.w;

    // Inflate by a tiny screen-space margin before choosing the mip. Exact AABB
    // projection is too brittle for temporal Hi-Z at silhouettes: a far/elevated
    // LOD chunk can expose only a subpixel sliver while the previous frame's
    // pyramid still contains nearby terrain. Padding makes those edge cases
    // conservatively draw instead of punching small holes.
    float padTexels = HIZ_RECT_BASE_PADDING_TEXELS +
                      min(float(pc.temporalInfo.w), HIZ_MOTION_PADDING_MAX_TEXELS);
    float padU = padTexels / max(pc.hiZPyramidInfo.x, 1.0);
    float padV = padTexels / max(pc.hiZPyramidInfo.y, 1.0);
    uvMinX = clamp(uvMinX - padU, 0.0, 1.0);
    uvMaxX = clamp(uvMaxX + padU, 0.0, 1.0);
    uvMinY = clamp(uvMinY - padV, 0.0, 1.0);
    uvMaxY = clamp(uvMaxY + padV, 0.0, 1.0);

    // Degenerate rectangle after clipping -> cannot occlude reliably.
    if (uvMinX >= uvMaxX || uvMinY >= uvMaxY) {
        return false;
    }

    // Select mip level where rect spans ~2 texels.
    // Integer MSB avoids per-thread transcendental log2().
    float rectWidth  = (uvMaxX - uvMinX) * pc.hiZPyramidInfo.x;
    float rectHeight = (uvMaxY - uvMinY) * pc.hiZPyramidInfo.y;
    float maxDim = max(rectWidth, rectHeight);
    uint maxDimInt = uint(max(maxDim, 1.0));
    int mipInt = findMSB(int(maxDimInt));
    mipInt = clamp(mipInt, 0, int(pc.hiZPyramidInfo.z) - 1);
    float mipLevel = float(mipInt);
    outMipLevel = mipLevel;

    // Progressive 4-corner sampling keeps the shader cheap, but voxel crack
    // holes can leave a lone zero-depth texel on one corner. Recover those
    // mixed-zero cases with a single center confirmation at a finer mip.
    float d0 = textureLod(hiZPyramid, vec2(uvMinX, uvMinY), mipLevel).r;
    float d1 = textureLod(hiZPyramid, vec2(uvMaxX, uvMinY), mipLevel).r;
    float d2 = textureLod(hiZPyramid, vec2(uvMinX, uvMaxY), mipLevel).r;
    float d3 = textureLod(hiZPyramid, vec2(uvMaxX, uvMaxY), mipLevel).r;

    uint nonZeroCount = 0u;
    float minNonZero = 1.0;
    float maxNonZero = 0.0;
    accumulatePyramidDepth(d0, nonZeroCount, minNonZero, maxNonZero);
    accumulatePyramidDepth(d1, nonZeroCount, minNonZero, maxNonZero);
    accumulatePyramidDepth(d2, nonZeroCount, minNonZero, maxNonZero);
    accumulatePyramidDepth(d3, nonZeroCount, minNonZero, maxNonZero);

    float pyramidDepth = 0.0;
    if (nonZeroCount == 4u) {
        pyramidDepth = minNonZero;
    } else if (nonZeroCount > 0u && maxDim <= HIZ_HOLE_RECOVERY_MAX_TEXELS) {
        float confirmMipLevel = max(mipLevel - 1.0, 0.0);
        float centerDepth = textureLod(
            hiZPyramid,
            vec2(0.5 * (uvMinX + uvMaxX), 0.5 * (uvMinY + uvMaxY)),
            confirmMipLevel).r;
        accumulatePyramidDepth(centerDepth, nonZeroCount, minNonZero, maxNonZero);
        if (!coherentPyramidDepths(nonZeroCount, minNonZero, maxNonZero)) {
            recordPyramidDepthDebug(nonZeroCount > 0u ? minNonZero : 0.0);
            return false;
        }
        pyramidDepth = minNonZero;
    } else {
        recordPyramidDepthDebug(nonZeroCount > 0u ? minNonZero : 0.0);
        return false;
    }

    recordPyramidDepthDebug(pyramidDepth);
    outNearestDepth = nearestDepth;
    outPyramidDepth = pyramidDepth;
    return nearestDepth < (pyramidDepth - HIZ_OCCLUSION_DEPTH_EPSILON);
}

bool reserveVisibleDraws(uint requested, out uint outBase) {
    outBase = 0u;
    if (requested == 0u) {
        return false;
    }

    uint base = atomicAdd(drawCount.count, requested);
    if (base >= MAX_VISIBLE_DRAWS || requested > (MAX_VISIBLE_DRAWS - base)) {
        atomicMin(drawCount.count, MAX_VISIBLE_DRAWS);
        return false;
    }

    outBase = base;
    return true;
}

void main() {
    uint candidateIdx = gl_GlobalInvocationID.x;

    // Bounds check
    if (candidateIdx >= frustumPassedCount.count) {
        return;
    }

    uint chunkIdx = frustumPassedIndices.indices[candidateIdx];

    uint subChunkCount = allDraws.chunks[chunkIdx].subChunkCount;
    subChunkCount = min(subChunkCount, uint(MAX_SUBCHUNKS));

    // Get world-space AABB
    vec3 aabbMin = allDraws.chunks[chunkIdx].aabbMin.xyz;
    vec3 aabbMax = allDraws.chunks[chunkIdx].aabbMax.xyz;

    // Hi-Z occlusion culling - skip chunks fully hidden behind other geometry.
    // Only run if Hi-Z pyramid is bound, enabled, AND the chunk is past its grace window.
    // Grace prevents the death-spiral: a freshly uploaded/edited chunk would otherwise
    // be tested against a previous-frame pyramid built before the chunk was visible,
    // wrongly classifying it as occluded by the background that showed through; it would
    // then never be drawn, the pyramid would never see it, and the chunk would stay
    // invisible forever.
    uint hiZGraceTimeline = allDraws.chunks[chunkIdx].hiZGraceTimeline;
    bool pastGrace = (pc.currentTimeline >= hiZGraceTimeline);

    // Phase A — temporal-coherence Hi-Z skip.
    // If this chunk passed Hi-Z on the most recent un-reset frame, skip the test
    // entirely and proceed straight to draw emission. Hi-Z is an optimization, not
    // a correctness requirement: skipping it can only cause extra draws (handled by
    // the depth test), never wrong rendering. Periodic CPU mask reset bounds the
    // duration any stale visibility can persist.
    bool temporalSkip = (pc.temporalInfo.x != 0u) && pastGrace && prevVisibleBitSet(chunkIdx);

    bool hiZActive = (pc.hiZEnabled != 0u) && pastGrace && !temporalSkip;
    if (hiZActive) {
        float occNearest = 0.0;
        float occPyramid = 0.0;
        float occMip = 0.0;
        if (hiZOcclude(aabbMin, aabbMax, occNearest, occPyramid, occMip)) {
            clearPrevVisibleBit(chunkIdx);
            if (pc.debugEnabled != 0u) {
                atomicAdd(debugStats.stats[7], 1);  // hiZOccluded++
            }
            if (hiZBlinkLogEnabled()) {
                uint slot = atomicAdd(hiZBlinkLog.count, 1u);
                if (slot < HIZ_BLINK_LOG_GPU_CAPACITY) {
                    vec3 origin = allDraws.chunks[chunkIdx].origin.xyz;
                    hiZBlinkLog.events[slot].chunkIdx         = chunkIdx;
                    hiZBlinkLog.events[slot].currentTimeline  = pc.currentTimeline;
                    hiZBlinkLog.events[slot].hiZGraceTimeline = hiZGraceTimeline;
                    hiZBlinkLog.events[slot].subChunkCount    = subChunkCount;
                    hiZBlinkLog.events[slot].chunkOriginX     = origin.x;
                    hiZBlinkLog.events[slot].chunkOriginY     = origin.y;
                    hiZBlinkLog.events[slot].chunkOriginZ     = origin.z;
                    hiZBlinkLog.events[slot].nearestDepth     = occNearest;
                    hiZBlinkLog.events[slot].pyramidDepth     = occPyramid;
                    hiZBlinkLog.events[slot].mipLevel         = occMip;
                } else {
                    atomicAdd(hiZBlinkLog.dropped, 1u);
                }
            }
            return;  // Culled by occlusion - chunk is behind other geometry
        }
    }

    // Visible — mark for next frame's temporal-skip path.
    // (Includes both genuine Hi-Z passes and temporally-skipped chunks; both should
    // remain marked visible until something explicitly clears them.)
    setPrevVisibleBit(chunkIdx);

    // Count valid subchunk draws first so we reserve output slots once.
    uint validDrawCount = 0u;
    for (uint i = 0u; i < subChunkCount; ++i) {
        if (allDraws.chunks[chunkIdx].draws[i].indexCount != 0u) {
            validDrawCount++;
        }
    }
    if (validDrawCount == 0u) {
        return;
    }

    // Reserve a contiguous block without ever letting the indirect draw count
    // exceed the command/origin buffers consumed by the draw pass.
    uint outBase = 0u;
    if (!reserveVisibleDraws(validDrawCount, outBase)) {
        return;
    }

    // Chunk is visible - emit all its subchunk draw commands.
    vec3 origin = allDraws.chunks[chunkIdx].origin.xyz;
    float materialOverlayHint = float(allDraws.chunks[chunkIdx]._pad1);
    uint emitOffset = 0u;
    for (uint i = 0u; i < subChunkCount; ++i) {
        SubChunkDraw draw = allDraws.chunks[chunkIdx].draws[i];

        // Skip empty draws
        if (draw.indexCount == 0u) {
            continue;
        }

        uint outIdx = outBase + emitOffset;
        emitOffset++;

        // Write draw command
        visibleDraws.commands[outIdx].indexCount = draw.indexCount;
        visibleDraws.commands[outIdx].instanceCount = draw.instanceCount;
        visibleDraws.commands[outIdx].firstIndex = draw.firstIndex;
        visibleDraws.commands[outIdx].vertexOffset = draw.vertexOffset;
        visibleDraws.commands[outIdx].firstInstance = outIdx;  // Use output index for origin lookup

        // Write origin. w carries the per-slot material-overlay hint from ChunkDrawData._pad1.
        visibleOrigins.origins[outIdx] = vec4(origin, materialOverlayHint);
    }
}

````

## shaders\culling\frustum_filter.comp

Description: No CC-DESC found. C++ struct 'SubChunkDraw'.

````glsl
#version 450
// GPT-DESC: Filters active chunk slots through the exact current-frame frustum.
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : enable

#define LOCAL_SIZE_X 64
layout(local_size_x = LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

#define MAX_SUBCHUNKS 64

struct SubChunkDraw {
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int  vertexOffset;
    uint firstInstance;
    uint _pad0;
    uint _pad1;
    uint _pad2;
};

struct ChunkDrawData {
    SubChunkDraw draws[MAX_SUBCHUNKS];
    vec4 aabbMin;
    vec4 aabbMax;
    vec4 origin;
    uint subChunkCount;
    uint hiZGraceTimeline;
    uint _pad1;
    uint _pad2;
};

layout(push_constant) uniform PushConstants {
    mat4 viewProj;
    uint totalDraws;
    uint currentTimeline;
    uint hiZEnabled;
    uint debugEnabled;
    vec4 hiZPyramidInfo;
    vec4 viewportUvTransform;
    mat4 prevViewProj;
    uvec4 temporalInfo;     // x = temporalCoherenceEnabled, y = motionFrame, z = blinkLog, w = motionPaddingTexels
} pc;

layout(set = 0, binding = 0) readonly buffer AllDraws {
    ChunkDrawData chunks[];
} allDraws;

layout(set = 0, binding = 4) buffer DebugStats {
    uint stats[16];
} debugStats;

layout(set = 0, binding = 6) readonly buffer ActiveChunkIndices {
    uint indices[];
} activeChunkIndices;

layout(set = 0, binding = 7) buffer FrustumPassedIndices {
    uint indices[];
} frustumPassedIndices;

layout(set = 0, binding = 8) buffer FrustumPassedCount {
    uint count;
} frustumPassedCount;

// Phase A — persistent visibility mask (one bit per chunk slot).
// We CLEAR the bit on every fail path here so a chunk that just left view (or got
// freed) doesn't keep its "visible" status and bypass Hi-Z when it re-enters.
// We do NOT set the bit here — only the Hi-Z stage decides true visibility.
layout(set = 0, binding = 11) buffer PrevVisibleMask {
    uint bits[];
} prevVisibleMask;

void clearPrevVisibleBit(uint chunkIdx) {
    uint word = chunkIdx >> 5u;
    uint mask = ~(1u << (chunkIdx & 31u));
    atomicAnd(prevVisibleMask.bits[word], mask);
}

bool frustumCull(vec3 aabbMin, vec3 aabbMax) {
    bool outsideLeft = true;
    bool outsideRight = true;
    bool outsideBottom = true;
    bool outsideTop = true;
    bool outsideNear = true;
    bool outsideBehind = true;

    for (int i = 0; i < 8; ++i) {
        vec3 corner = vec3(
            ((i & 1) != 0) ? aabbMax.x : aabbMin.x,
            ((i & 2) != 0) ? aabbMax.y : aabbMin.y,
            ((i & 4) != 0) ? aabbMax.z : aabbMin.z);

        vec4 clip = pc.viewProj * vec4(corner, 1.0);

        outsideLeft   = outsideLeft   && (clip.x < -clip.w);
        outsideRight  = outsideRight  && (clip.x >  clip.w);
        outsideBottom = outsideBottom && (clip.y < -clip.w);
        outsideTop    = outsideTop    && (clip.y >  clip.w);
        // Vulkan clip depth is 0..w. With the reversed-Z infinite projection,
        // z > w means the whole AABB is in front of the near plane.
        outsideNear   = outsideNear   && (clip.z >  clip.w);
        // All corners behind the eye cannot contribute to the screen; without
        // this, negative clip.w can make edge/behind-camera chunks survive the
        // side-plane tests during fast turns.
        outsideBehind = outsideBehind && (clip.w <= 0.0);
    }

    return outsideLeft || outsideRight || outsideBottom || outsideTop || outsideNear || outsideBehind;
}

void main() {
    uint activeIdx = gl_GlobalInvocationID.x;
    if (activeIdx >= pc.totalDraws) {
        return;
    }

    uint chunkIdx = activeChunkIndices.indices[activeIdx];
    uint subChunkCount = min(allDraws.chunks[chunkIdx].subChunkCount, uint(MAX_SUBCHUNKS));

    if (subChunkCount == 0u) {
        clearPrevVisibleBit(chunkIdx);
        if (pc.debugEnabled != 0u) {
            atomicAdd(debugStats.stats[5], 1);  // zeroSubchunks++
        }
        return;
    }

    uint gpuReadyValue = floatBitsToUint(allDraws.chunks[chunkIdx].origin.w);
    if (gpuReadyValue > pc.currentTimeline) {
        clearPrevVisibleBit(chunkIdx);
        if (pc.debugEnabled != 0u) {
            atomicAdd(debugStats.stats[6], 1);  // notReady++
        }
        return;
    }
    vec3 aabbMin = allDraws.chunks[chunkIdx].aabbMin.xyz;
    vec3 aabbMax = allDraws.chunks[chunkIdx].aabbMax.xyz;
    if (frustumCull(aabbMin, aabbMax)) {
        clearPrevVisibleBit(chunkIdx);
        return;
    }

    uint outIdx = atomicAdd(frustumPassedCount.count, 1);
    frustumPassedIndices.indices[outIdx] = chunkIdx;
}

````

## src\ui\debug_menu\rendering\HiZDebugWindow.cpp

Description: No CC-DESC found.

````cpp
#include "ui/debug_menu/rendering/HiZDebugWindow.h"
#include "ui/style/EngineTheme.h"
#include "rendering/culling/GPUCullingSystem.h"
#include <imgui.h>
#include <cstdio>
#include <algorithm>
#include <string>

HiZDebugWindow::HiZDebugWindow()
    : DebugWindowBase("Hi-Z Pyramid")
{
}

void HiZDebugWindow::render() {
    if (!isVisible()) return;

    if (isEmbedded()) {
        renderContentInternal();
        return;
    }

    ImGui::SetNextWindowPos(ImVec2(10, 10), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowSize(ImVec2(320, 300), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowBgAlpha(EngineTheme::kPanelAlpha);

    const bool windowOpen = ImGui::Begin("Hi-Z Depth Pyramid", nullptr, ImGuiWindowFlags_NoSavedSettings);
    DebugWindowScrollLock::restore("Hi-Z Depth Pyramid");
    if (windowOpen) {
        renderContentInternal();
    }
    DebugWindowScrollLock::capture("Hi-Z Depth Pyramid");
    ImGui::End();
}

void HiZDebugWindow::renderContent() {
    renderContentInternal();
}

void HiZDebugWindow::renderContentInternal() {
    if (!m_pyramid) {
        ImGui::TextColored(ImVec4(1.0f, 0.4f, 0.4f, 1.0f), "HiZPyramid not connected");
        return;
    }

    auto info = m_pyramid->getDebugInfo();

    if (!info.initialized) {
        ImGui::TextColored(EngineTheme::kError, "NOT INITIALIZED");
        return;
    }
    ImGui::TextColored(EngineTheme::kOk, "ACTIVE");
    ImGui::SameLine();
    ImGui::Text("Swap %ux%u  Pyr %ux%u  Mips %u",
        info.swapchainWidth, info.swapchainHeight,
        info.pyramidWidth, info.pyramidHeight, info.mipLevels);

    // Per-mip level breakdown
    if (ImGui::TreeNodeEx("Mip Level Details", ImGuiTreeNodeFlags_DefaultOpen)) {
        uint64_t totalBytes = 0;

        ImGui::Columns(3, "mipCols", true);
        ImGui::SetColumnWidth(0, 60.0f);
        ImGui::SetColumnWidth(1, 120.0f);
        ImGui::Text("Level"); ImGui::NextColumn();
        ImGui::Text("Resolution"); ImGui::NextColumn();
        ImGui::Text("Size"); ImGui::NextColumn();
        ImGui::Separator();

        for (uint32_t i = 0; i < info.mipLevels; ++i) {
            uint32_t w = std::max(1u, info.pyramidWidth >> i);
            uint32_t h = std::max(1u, info.pyramidHeight >> i);
            uint64_t mipBytes = static_cast<uint64_t>(w) * h * 4; // R32_SFLOAT = 4 bytes/texel
            totalBytes += mipBytes;

            ImGui::Text("%u", i);
            ImGui::NextColumn();
            ImGui::Text("%u x %u", w, h);
            ImGui::NextColumn();

            if (mipBytes >= 1024 * 1024) {
                ImGui::Text("%.1f MB", mipBytes / (1024.0f * 1024.0f));
            } else if (mipBytes >= 1024) {
                ImGui::Text("%.1f KB", mipBytes / 1024.0f);
            } else {
                ImGui::Text("%llu B", static_cast<unsigned long long>(mipBytes));
            }
            ImGui::NextColumn();
        }

        ImGui::Columns(1);
        ImGui::Separator();

        // Total memory
        if (totalBytes >= 1024 * 1024) {
            ImGui::Text("Total Pyramid Memory: %.2f MB", totalBytes / (1024.0f * 1024.0f));
        } else {
            ImGui::Text("Total Pyramid Memory: %.1f KB", totalBytes / 1024.0f);
        }

        ImGui::TreePop();
    }

    ImGui::Separator();

    // Occlusion culling statistics
    if (m_culling && m_culling->isReady()) {
        auto stats = m_culling->getDebugStats();
        if (ImGui::CollapsingHeader("Occlusion Culling", ImGuiTreeNodeFlags_DefaultOpen)) {
            uint32_t afterOcclusion = (stats.frustumPassed > stats.hiZOccluded) 
                ? (stats.frustumPassed - stats.hiZOccluded) : 0;
            ImGui::Text("Occ:%u  Frust:%u  Vis:%u  Ready:%u",
                stats.hiZOccluded, stats.frustumPassed, afterOcclusion, stats.chunksReady);
            if (stats.frustumPassed > 0) {
                float occlusionRate = 100.0f * stats.hiZOccluded / static_cast<float>(stats.frustumPassed);
                ImGui::SameLine();
                ImGui::TextDisabled("(%.0f%%)", occlusionRate);
            }
            uint32_t hiZActuallyTested = (stats.hiZTested > stats.hiZNearPlaneFail)
                ? (stats.hiZTested - stats.hiZNearPlaneFail) : 0;
            ImGui::Text("Tested:%u  NearBail:%u  Full:%u", stats.hiZTested, stats.hiZNearPlaneFail, hiZActuallyTested);
            if (stats.pyramidNonZero > 0) {
                ImGui::TextColored(EngineTheme::kOk, "PyNZ:%u", stats.pyramidNonZero);
            } else {
                ImGui::TextColored(EngineTheme::kError, "PyNZ:%u EMPTY!", stats.pyramidNonZero);
            }
            ImGui::SameLine();
            ImGui::Text("Zero:%u", stats.pyramidAllZero);
        }
    } else {
        ImGui::TextDisabled("GPU culling not connected");
    }

    if (ImGui::CollapsingHeader("30s Hi-Z Perf")) {
        HiZPyramid::DiagnosticsSummary diag = m_pyramid->summarizeDiagnostics(30.0);
        if (diag.sampleCount > 0) {
            ImGui::Text("%u frames  Occ:%.0f%%  PyNZ:%.0f%%", diag.sampleCount,
                static_cast<float>(diag.occlusionRatePercent),
                static_cast<float>(diag.pyramidNonZeroRatePercent));
            ImGui::Text("Same %.0f%%  Temp %.0f%%  Frust %.0f%%",
                100.0f * static_cast<float>(diag.sameFrameHiZCount) / static_cast<float>(diag.sampleCount),
                100.0f * static_cast<float>(diag.temporalHiZCount) / static_cast<float>(diag.sampleCount),
                100.0f * static_cast<float>(diag.frustumOnlyCount) / static_cast<float>(diag.sampleCount));
            ImGui::Text("GPU %.3f (C%.3f P%.3f B%.3f F%.3f T%.3f)",
                static_cast<float>(diag.avgGpuHiZIncrementalMs),
                static_cast<float>(diag.avgGpuInitialCullMs),
                static_cast<float>(diag.avgGpuDepthPrepassMs),
                static_cast<float>(diag.avgGpuHiZBuildMs),
                static_cast<float>(diag.avgGpuFinalCullMs),
                static_cast<float>(diag.avgGpuTerrainMs));
            ImGui::Text("CPU %.3f (S%.3f C%.3f I%.3f P%.3f B%.3f F%.3f)",
                static_cast<float>(diag.avgCpuHiZIncrementalRecordMs),
                static_cast<float>(diag.avgCpuCullingSetupMs),
                static_cast<float>(diag.avgCpuCmdRecordMs),
                static_cast<float>(diag.avgCpuInitialCullRecordMs),
                static_cast<float>(diag.avgCpuDepthPrepassRecordMs),
                static_cast<float>(diag.avgCpuHiZBuildRecordMs),
                static_cast<float>(diag.avgCpuFinalCullRecordMs));
            if (ImGui::Button("Copy Report")) {
                std::string report = m_pyramid->buildDiagnosticsReport(30.0);
                ImGui::SetClipboardText(report.c_str());
                m_copyFeedbackFrames = 180;
            }
            if (m_copyFeedbackFrames > 0) {
                ImGui::SameLine(); ImGui::TextDisabled("copied!");
                m_copyFeedbackFrames--;
            }
        } else {
            ImGui::TextDisabled("(collecting...)");
        }
    }

    // ── Corruption Diagnostics (last 20s) ──────────────────────────────
    if (m_pyramid) {
        const uint32_t corruptionCount = m_pyramid->getCorruptionCount(20.0);
        char corrLabel[64];
        snprintf(corrLabel, sizeof(corrLabel), "Corruption (%u in 20s)###corr", corruptionCount);
        if (ImGui::CollapsingHeader(corrLabel)) {

            if (corruptionCount > 0) {
                const auto& log = m_pyramid->getCorruptionLog();
                const double newest = log.empty() ? 0.0 : log.back().timestampSeconds;
                const double cutoff = newest - 20.0;

                uint32_t byReason[4] = {};
                for (const auto& e : log) {
                    if (e.timestampSeconds >= cutoff && e.reason < 4)
                        byReason[e.reason]++;
                }
                ImGui::Text("NZDrop:%u OccSpike:%u OccDrop:%u Empty:%u",
                    byReason[0], byReason[1], byReason[2], byReason[3]);

                if (ImGui::TreeNode("Events")) {
                    int shown = 0;
                    for (auto it = log.rbegin(); it != log.rend() && shown < 10; ++it) {
                        if (it->timestampSeconds < cutoff) continue;
                        shown++;
                        const char* modeStr = "Frust";
                        if (it->mode == HiZPyramid::DiagnosticsMode::TemporalHiZ) modeStr = "Temp";
                        else if (it->mode == HiZPyramid::DiagnosticsMode::SameFrameHiZ) modeStr = "Same";
                        float ago = static_cast<float>(newest - it->timestampSeconds);
                        ImGui::TextColored(EngineTheme::kWarning, "[%.1fs] %s FIF:%u %s", ago,
                            HiZPyramid::CorruptionEvent::reasonString(it->reason),
                            it->frameInFlightIndex, modeStr);
                        ImGui::Text("  Rot:%.1f Tr:%.1f Y:%.0f P:%.0f GPU:%.2f",
                            it->cameraRotationDeg, it->cameraTranslation,
                            it->cameraYaw, it->cameraPitch, it->gpuFrameMs);
                        ImGui::Text("  PyNZ:%.0f%%->%.0f%% Occ:%.0f%%->%.0f%%",
                            it->prevPyramidNonZeroPercent, it->pyramidNonZeroPercent,
                            it->prevOcclusionRatePercent, it->occlusionRatePercent);
                        ImGui::TextDisabled("  F:%u O:%u NF:%u DU:%u HF:%u DV:%u NZ:%u Z:%u",
                            it->frustumPassed, it->hiZOccluded, it->hiZNearPlaneFail,
                            it->degenerateUV, it->holeRecoveryFail, it->hiZDepthTestVisible,
                            it->pyramidNonZero, it->pyramidAllZero);
                    }
                    ImGui::TreePop();
                }
                ImGui::SetNextItemWidth(60.0f);
                ImGui::InputInt("##corrN", &m_corrCopyCount, 1, 10);
                if (m_corrCopyCount < 1) m_corrCopyCount = 1;
                ImGui::SameLine();
                if (ImGui::Button("Copy Last N##corr")) {
                    std::string report = m_pyramid->buildCorruptionReportLastN(static_cast<size_t>(m_corrCopyCount));
                    ImGui::SetClipboardText(report.c_str());
                    m_corruptionCopyFrames = 180;
                }                if (m_corruptionCopyFrames > 0) {
                    ImGui::SameLine(); ImGui::TextDisabled("copied!");
                    m_corruptionCopyFrames--;
                }
            } else {
                ImGui::TextColored(EngineTheme::kOk, "None");
            }
        }
    }
    
    // Edit visibility transitions: catches drawn -> not drawn state changes
    // for terrain-edit uploads and reports the reason path.
    renderEditVisibilitySection();
    renderGModeGeometryDiffSection();

    // Pyramid preview image
    if (m_pyramid && m_pyramid->isReady()) {
        if (ImGui::CollapsingHeader("Depth Preview")) {
            ImTextureID texID = m_pyramid->getImGuiTextureID();
            if (texID) {
                float previewWidth = ImGui::GetContentRegionAvail().x;
                float vpPixelW = (m_viewportUV[2] - m_viewportUV[0]) * static_cast<float>(m_pyramid->getWidth());
                float vpPixelH = (m_viewportUV[3] - m_viewportUV[1]) * static_cast<float>(m_pyramid->getHeight());
                float aspect = (vpPixelH > 0.0f) ? (vpPixelW / vpPixelH) : 1.0f;
                float previewHeight = previewWidth / aspect;
                ImGui::Image(texID, ImVec2(previewWidth, previewHeight),
                             ImVec2(m_viewportUV[0], m_viewportUV[1]),
                             ImVec2(m_viewportUV[2], m_viewportUV[3]));
                ImGui::TextDisabled("White=near  Black=far  Rev-Z");
            } else {
                ImGui::TextDisabled("Texture not available");
            }
        }
    }
}

void HiZDebugWindow::renderEditVisibilitySection() {
    if (!m_culling) return;

    const auto snap = m_culling->getEditVisibilitySnapshot();

    char header[192];
    std::snprintf(header, sizeof(header),
                  "Edit Visibility  (tracked:%zu  drops:%llu  recovers:%llu  frame:%llu)###hizeditvis",
                  snap.trackedChunks.size(),
                  static_cast<unsigned long long>(snap.totalDropEvents),
                  static_cast<unsigned long long>(snap.totalRecoveryEvents),
                  static_cast<unsigned long long>(snap.lastDispatchSerial));

    if (!ImGui::CollapsingHeader(header)) return;

    if (ImGui::Button("Clear##hizeditvis")) {
        m_culling->clearEditVisibilitySnapshot();
    }
    ImGui::SameLine();
    ImGui::SetNextItemWidth(92.0f);
    ImGui::InputInt("Copy last N##hizeditvis", &m_editVisibilityCopyCount);
    if (m_editVisibilityCopyCount < 1) m_editVisibilityCopyCount = 1;
    if (m_editVisibilityCopyCount > 10000) m_editVisibilityCopyCount = 10000;

    auto eventMatchesFilters = [&](const GPUCullingSystem::EditVisibilityEvent& ev) -> bool {
        if (m_editVisibilityChunkFilter >= 0 && static_cast<int>(ev.slot) != m_editVisibilityChunkFilter) {
            return false;
        }
        const bool isDrop = ev.drawnBefore && !ev.drawnAfter;
        if (m_editVisibilityOnlyDrops && !isDrop) {
            return false;
        }
        return true;
    };

    if (ImGui::Button("Copy CSV##hizeditvis")) {
        std::string csv =
            "seq,dispatch,slot,chunkX,chunkY,chunkZ,fromState,toState,drawnBefore,drawnAfter,"
            "fromTerrainEdit,replacesExisting,currentTimeline,gpuReadyTimeline,hiZGraceTimeline,"
            "graceDelta,hiZEnabled,hiZActive,frustumPassed,ready,nearestDepth,pyramidDepth,mip,editUploadSerial\n";

        int copied = 0;
        for (auto it = snap.events.rbegin(); it != snap.events.rend() && copied < m_editVisibilityCopyCount; ++it) {
            const auto& ev = *it;
            if (!eventMatchesFilters(ev)) continue;
            char row[512];
            std::snprintf(row, sizeof(row),
                          "%llu,%llu,%u,%d,%d,%d,%s,%s,%u,%u,%u,%u,%u,%u,%u,%d,%u,%u,%u,%u,%.6f,%.6f,%.1f,%llu\n",
                          static_cast<unsigned long long>(ev.sequence),
                          static_cast<unsigned long long>(ev.dispatchSerial),
                          ev.slot, ev.chunkX, ev.chunkY, ev.chunkZ,
                          GPUCullingSystem::editVisibilityStateName(ev.previousState),
                          GPUCullingSystem::editVisibilityStateName(ev.newState),
                          ev.drawnBefore ? 1u : 0u,
                          ev.drawnAfter ? 1u : 0u,
                          ev.fromTerrainEdit ? 1u : 0u,
                          ev.replacesExistingMesh ? 1u : 0u,
                          ev.currentTimeline,
                          ev.gpuReadyTimeline,
                          ev.hiZGraceTimeline,
                          ev.graceDelta,
                          ev.hiZEnabled ? 1u : 0u,
                          ev.hiZActive ? 1u : 0u,
                          ev.frustumPassed ? 1u : 0u,
                          ev.ready ? 1u : 0u,
                          ev.nearestDepth, ev.pyramidDepth, ev.mipLevel,
                          static_cast<unsigned long long>(ev.editUploadSerial));
            csv += row;
            ++copied;
        }

        // Fallback: if there are no historical events yet, copy current tracked
        // rows so clipboard output is still useful and never "mysteriously empty".
        if (copied == 0) {
            for (const auto& c : snap.trackedChunks) {
                if (m_editVisibilityChunkFilter >= 0 && static_cast<int>(c.slot) != m_editVisibilityChunkFilter) {
                    continue;
                }
                if (m_editVisibilityOnlyDrops && c.drawn) {
                    continue;
                }
                char row[512];
                std::snprintf(row, sizeof(row),
                              "0,%llu,%u,%d,%d,%d,%s,%s,%u,%u,%u,%u,%u,%u,%u,%d,%u,%u,%u,%u,%.6f,%.6f,%.1f,%llu\n",
                              static_cast<unsigned long long>(snap.lastDispatchSerial),
                              c.slot, c.chunkX, c.chunkY, c.chunkZ,
                              GPUCullingSystem::editVisibilityStateName(c.state),
                              GPUCullingSystem::editVisibilityStateName(c.state),
                              c.drawn ? 1u : 0u,
                              c.drawn ? 1u : 0u,
                              c.fromTerrainEdit ? 1u : 0u,
                              c.replacesExistingMesh ? 1u : 0u,
                              c.currentTimeline,
                              c.gpuReadyTimeline,
                              c.hiZGraceTimeline,
                              c.graceDelta,
                              c.hiZEnabled ? 1u : 0u,
                              c.hiZActive ? 1u : 0u,
                              c.frustumPassed ? 1u : 0u,
                              c.ready ? 1u : 0u,
                              c.nearestDepth, c.pyramidDepth, c.mipLevel,
                              static_cast<unsigned long long>(c.editUploadSerial));
                csv += row;
                ++copied;
                if (copied >= m_editVisibilityCopyCount) break;
            }
        }

        ImGui::SetClipboardText(csv.c_str());
        m_editVisibilityCopyFrames = 180;
    }
    if (m_editVisibilityCopyFrames > 0) {
        ImGui::SameLine();
        ImGui::TextDisabled("copied!");
        --m_editVisibilityCopyFrames;
    }

    ImGui::SetNextItemWidth(110.0f);
    ImGui::InputInt("Filter slot (-1 = all)##hizeditvis", &m_editVisibilityChunkFilter);
    ImGui::SameLine();
    ImGui::SetNextItemWidth(90.0f);
    ImGui::InputInt("Max rows##hizeditvis", &m_editVisibilityMaxRows);
    if (m_editVisibilityMaxRows < 10) m_editVisibilityMaxRows = 10;
    if (m_editVisibilityMaxRows > 4096) m_editVisibilityMaxRows = 4096;
    ImGui::SameLine();
    ImGui::Checkbox("Drops only##hizeditvis", &m_editVisibilityOnlyDrops);

    if (snap.trackedChunks.empty()) {
        ImGui::TextDisabled("(no tracked edit chunks yet)");
    } else {
        const ImGuiTableFlags trackedFlags = ImGuiTableFlags_Borders
                                           | ImGuiTableFlags_RowBg
                                           | ImGuiTableFlags_SizingFixedFit
                                           | ImGuiTableFlags_ScrollY;
        if (ImGui::BeginTable("hizeditvis_tracked", 8, trackedFlags, ImVec2(0.0f, 180.0f))) {
            ImGui::TableSetupScrollFreeze(0, 1);
            ImGui::TableSetupColumn("slot");
            ImGui::TableSetupColumn("chunk");
            ImGui::TableSetupColumn("state");
            ImGui::TableSetupColumn("ready/frust");
            ImGui::TableSetupColumn("hi-z");
            ImGui::TableSetupColumn("timeline");
            ImGui::TableSetupColumn("draws");
            ImGui::TableSetupColumn("depth");
            ImGui::TableHeadersRow();

            for (const auto& c : snap.trackedChunks) {
                if (m_editVisibilityChunkFilter >= 0 && static_cast<int>(c.slot) != m_editVisibilityChunkFilter) {
                    continue;
                }

                const ImVec4 stateColor = GPUCullingSystem::editVisibilityStateIsDrawn(c.state)
                    ? EngineTheme::kOk
                    : (c.state == GPUCullingSystem::EditVisibilityState::NotDrawnHiZOccluded
                        ? EngineTheme::kWarning
                        : EngineTheme::kError);

                const char* hizStr = (!c.hiZEnabled)
                    ? "off"
                    : (c.hiZActive ? "active" : "grace");

                ImGui::TableNextRow();
                ImGui::TableNextColumn(); ImGui::Text("%u", c.slot);
                ImGui::TableNextColumn(); ImGui::Text("%d,%d,%d", c.chunkX, c.chunkY, c.chunkZ);
                ImGui::TableNextColumn(); ImGui::TextColored(stateColor, "%s",
                    GPUCullingSystem::editVisibilityStateName(c.state));
                ImGui::TableNextColumn(); ImGui::Text("%s / %s",
                    c.ready ? "R" : "N",
                    c.frustumPassed ? "P" : "C");
                ImGui::TableNextColumn(); ImGui::Text("%s", hizStr);
                ImGui::TableNextColumn(); ImGui::Text("%u / %u / %u (%+d)",
                    c.currentTimeline, c.gpuReadyTimeline, c.hiZGraceTimeline, c.graceDelta);
                ImGui::TableNextColumn(); ImGui::Text("%u/%u", c.validDrawCount, c.subChunkCount);
                ImGui::TableNextColumn();
                if (c.state == GPUCullingSystem::EditVisibilityState::NotDrawnHiZOccluded) {
                    ImGui::Text("%.4f / %.4f m%.0f", c.nearestDepth, c.pyramidDepth, c.mipLevel);
                } else {
                    ImGui::TextDisabled("-");
                }
            }
            ImGui::EndTable();
        }
    }

    if (snap.events.empty()) {
        ImGui::TextDisabled("(no historical events yet; live tracked rows above)");
        return;
    }

    const ImGuiTableFlags eventFlags = ImGuiTableFlags_Borders
                                     | ImGuiTableFlags_RowBg
                                     | ImGuiTableFlags_SizingFixedFit
                                     | ImGuiTableFlags_ScrollY;
    if (ImGui::BeginTable("hizeditvis_events", 8, eventFlags, ImVec2(0.0f, 220.0f))) {
        ImGui::TableSetupScrollFreeze(0, 1);
        ImGui::TableSetupColumn("seq");
        ImGui::TableSetupColumn("slot");
        ImGui::TableSetupColumn("chunk");
        ImGui::TableSetupColumn("transition");
        ImGui::TableSetupColumn("new reason");
        ImGui::TableSetupColumn("timeline");
        ImGui::TableSetupColumn("hi-z");
        ImGui::TableSetupColumn("depth");
        ImGui::TableHeadersRow();

        int shown = 0;
        for (auto it = snap.events.rbegin(); it != snap.events.rend() && shown < m_editVisibilityMaxRows; ++it) {
            const auto& ev = *it;
            if (!eventMatchesFilters(ev)) continue;
            ++shown;

            const bool isDrop = ev.drawnBefore && !ev.drawnAfter;
            const ImVec4 transitionColor = isDrop ? EngineTheme::kError : EngineTheme::kOk;
            const char* hizStr = (!ev.hiZEnabled)
                ? "off"
                : (ev.hiZActive ? "active" : "grace");

            ImGui::TableNextRow();
            ImGui::TableNextColumn(); ImGui::Text("%llu", static_cast<unsigned long long>(ev.sequence));
            ImGui::TableNextColumn(); ImGui::Text("%u", ev.slot);
            ImGui::TableNextColumn(); ImGui::Text("%d,%d,%d", ev.chunkX, ev.chunkY, ev.chunkZ);
            ImGui::TableNextColumn();
            ImGui::TextColored(transitionColor, "%s -> %s",
                ev.drawnBefore ? "Drawn" : "NotDrawn",
                ev.drawnAfter ? "Drawn" : "NotDrawn");
            ImGui::TableNextColumn(); ImGui::Text("%s",
                GPUCullingSystem::editVisibilityStateName(ev.newState));
            ImGui::TableNextColumn(); ImGui::Text("%u/%u/%u (%+d)",
                ev.currentTimeline, ev.gpuReadyTimeline, ev.hiZGraceTimeline, ev.graceDelta);
            ImGui::TableNextColumn(); ImGui::Text("%s  %s/%s",
                hizStr, ev.ready ? "R" : "N", ev.frustumPassed ? "P" : "C");
            ImGui::TableNextColumn();
            if (ev.newState == GPUCullingSystem::EditVisibilityState::NotDrawnHiZOccluded) {
                ImGui::Text("%.4f / %.4f m%.0f", ev.nearestDepth, ev.pyramidDepth, ev.mipLevel);
            } else {
                ImGui::TextDisabled("-");
            }
        }
        ImGui::EndTable();
        ImGui::TextDisabled("%d shown / %zu total events", shown, snap.events.size());
    }
}

void HiZDebugWindow::renderGModeGeometryDiffSection() {
    if (!m_culling) return;

    const auto snap = m_culling->getGModeGeometryDiffSnapshot();

    char header[224];
    std::snprintf(header, sizeof(header),
                  "G-Mode Geometry Diff  (events:%zu  lastToggle:%llu  lastDiff:%u)###hizgmodediff",
                  snap.events.size(),
                  static_cast<unsigned long long>(snap.lastToggleSerial),
                  snap.lastDiffCount);
    if (!ImGui::CollapsingHeader(header)) return;

    if (snap.captureActive) {
        ImGui::TextColored(EngineTheme::kWarning, "Capturing toggle #%llu: %s -> %s (%u frames left)",
                           static_cast<unsigned long long>(snap.captureToggleSerial),
                           GPUCullingSystem::cullingModeName(snap.captureBeforeGpuMode),
                           GPUCullingSystem::cullingModeName(snap.captureAfterGpuMode),
                           snap.captureFramesRemaining);
    } else if (snap.lastCaptureTimedOut) {
        ImGui::TextColored(EngineTheme::kWarning, "Last capture timed out; showing latest available diff.");
    } else {
        ImGui::TextDisabled("Idle. Press G to capture CPU vs GPU visible-geometry differences.");
    }

    if (ImGui::Button("Clear##hizgmodediff")) {
        m_culling->clearGModeGeometryDiffSnapshot();
    }
    ImGui::SameLine();
    ImGui::SetNextItemWidth(92.0f);
    ImGui::InputInt("Copy last N##hizgmodediff", &m_gModeDiffCopyCount);
    if (m_gModeDiffCopyCount < 1) m_gModeDiffCopyCount = 1;
    if (m_gModeDiffCopyCount > 10000) m_gModeDiffCopyCount = 10000;

    auto eventMatchesFilters = [&](const GPUCullingSystem::GModeGeometryDiffEvent& ev) -> bool {
        const bool isMissing = ev.visibleBefore && !ev.visibleAfter;
        if (m_gModeDiffOnlyMissing && !isMissing) {
            return false;
        }
        if (m_gModeDiffOnlyTerrain && !ev.fromTerrainEdit) {
            return false;
        }
        return true;
    };

    auto reasonForEvent = [](const GPUCullingSystem::GModeGeometryDiffEvent& ev) -> const char* {
        if (ev.hasTrackedState) {
            return GPUCullingSystem::editVisibilityStateName(ev.trackedState);
        }
        const bool isMissing = ev.visibleBefore && !ev.visibleAfter;
        if (isMissing) {
            return ev.afterGpuMode ? "MissingInGPU.NoEditTrack" : "MissingInCPU.NoEditTrack";
        }
        return ev.afterGpuMode ? "AddedInGPU.NoEditTrack" : "AddedInCPU.NoEditTrack";
    };

    if (ImGui::Button("Copy CSV##hizgmodediff")) {
        std::string csv =
            "seq,toggle,beforeMode,afterMode,change,chunkX,chunkY,chunkZ,reason,hasTrackedState,"
            "fromTerrainEdit,replacesExisting,hiZEnabled,hiZActive,frustumPassed,ready,currentTimeline,"
            "gpuReadyTimeline,hiZGraceTimeline,graceDelta,nearestDepth,pyramidDepth,mip,editUploadSerial\n";

        int copied = 0;
        for (auto it = snap.events.rbegin(); it != snap.events.rend() && copied < m_gModeDiffCopyCount; ++it) {
            const auto& ev = *it;
            if (!eventMatchesFilters(ev)) continue;

            const bool isMissing = ev.visibleBefore && !ev.visibleAfter;
            const char* change = isMissing ? "MissingInAfter" : "AddedInAfter";
            const char* reason = reasonForEvent(ev);

            char row[640];
            std::snprintf(row, sizeof(row),
                          "%llu,%llu,%s,%s,%s,%d,%d,%d,%s,%u,%u,%u,%u,%u,%u,%u,%u,%u,%u,%d,%.6f,%.6f,%.1f,%llu\n",
                          static_cast<unsigned long long>(ev.sequence),
                          static_cast<unsigned long long>(ev.toggleSerial),
                          GPUCullingSystem::cullingModeName(ev.beforeGpuMode),
                          GPUCullingSystem::cullingModeName(ev.afterGpuMode),
                          change,
                          ev.chunkX, ev.chunkY, ev.chunkZ,
                          reason,
                          ev.hasTrackedState ? 1u : 0u,
                          ev.fromTerrainEdit ? 1u : 0u,
                          ev.replacesExistingMesh ? 1u : 0u,
                          ev.hiZEnabled ? 1u : 0u,
                          ev.hiZActive ? 1u : 0u,
                          ev.frustumPassed ? 1u : 0u,
                          ev.ready ? 1u : 0u,
                          ev.currentTimeline,
                          ev.gpuReadyTimeline,
                          ev.hiZGraceTimeline,
                          ev.graceDelta,
                          ev.nearestDepth,
                          ev.pyramidDepth,
                          ev.mipLevel,
                          static_cast<unsigned long long>(ev.editUploadSerial));
            csv += row;
            ++copied;
        }

        ImGui::SetClipboardText(csv.c_str());
        m_gModeDiffCopyFrames = 180;
    }
    if (m_gModeDiffCopyFrames > 0) {
        ImGui::SameLine();
        ImGui::TextDisabled("copied!");
        --m_gModeDiffCopyFrames;
    }

    ImGui::SetNextItemWidth(90.0f);
    ImGui::InputInt("Max rows##hizgmodediff", &m_gModeDiffMaxRows);
    if (m_gModeDiffMaxRows < 10) m_gModeDiffMaxRows = 10;
    if (m_gModeDiffMaxRows > 4096) m_gModeDiffMaxRows = 4096;
    ImGui::SameLine();
    ImGui::Checkbox("Only missing##hizgmodediff", &m_gModeDiffOnlyMissing);
    ImGui::SameLine();
    ImGui::Checkbox("Only terrain edits##hizgmodediff", &m_gModeDiffOnlyTerrain);

    if (snap.events.empty()) {
        ImGui::TextDisabled("(no geometry diffs captured yet)");
        return;
    }

    const ImGuiTableFlags flags = ImGuiTableFlags_Borders
                                | ImGuiTableFlags_RowBg
                                | ImGuiTableFlags_SizingFixedFit
                                | ImGuiTableFlags_ScrollY;
    if (ImGui::BeginTable("hizgmodediff_events", 8, flags, ImVec2(0.0f, 220.0f))) {
        ImGui::TableSetupScrollFreeze(0, 1);
        ImGui::TableSetupColumn("seq");
        ImGui::TableSetupColumn("toggle");
        ImGui::TableSetupColumn("chunk");
        ImGui::TableSetupColumn("change");
        ImGui::TableSetupColumn("reason");
        ImGui::TableSetupColumn("mode");
        ImGui::TableSetupColumn("flags");
        ImGui::TableSetupColumn("timeline/depth");
        ImGui::TableHeadersRow();

        int shown = 0;
        for (auto it = snap.events.rbegin(); it != snap.events.rend() && shown < m_gModeDiffMaxRows; ++it) {
            const auto& ev = *it;
            if (!eventMatchesFilters(ev)) continue;
            ++shown;

            const bool isMissing = ev.visibleBefore && !ev.visibleAfter;
            const char* change = isMissing ? "Missing" : "Added";
            const ImVec4 changeColor = isMissing ? EngineTheme::kError : EngineTheme::kOk;
            const char* reason = reasonForEvent(ev);

            ImGui::TableNextRow();
            ImGui::TableNextColumn(); ImGui::Text("%llu", static_cast<unsigned long long>(ev.sequence));
            ImGui::TableNextColumn(); ImGui::Text("%llu", static_cast<unsigned long long>(ev.toggleSerial));
            ImGui::TableNextColumn(); ImGui::Text("%d,%d,%d", ev.chunkX, ev.chunkY, ev.chunkZ);
            ImGui::TableNextColumn(); ImGui::TextColored(changeColor, "%s", change);
            ImGui::TableNextColumn(); ImGui::Text("%s", reason);
            ImGui::TableNextColumn(); ImGui::Text("%s -> %s",
                GPUCullingSystem::cullingModeName(ev.beforeGpuMode),
                GPUCullingSystem::cullingModeName(ev.afterGpuMode));
            ImGui::TableNextColumn(); ImGui::Text("%s %s/%s%s",
                ev.fromTerrainEdit ? "Edit" : "Any",
                ev.ready ? "R" : "N",
                ev.frustumPassed ? "P" : "C",
                ev.hiZActive ? " HiZ" : "");
            ImGui::TableNextColumn();
            if (ev.hasTrackedState) {
                ImGui::Text("%u/%u/%u (%+d)  %.4f/%.4f m%.0f",
                    ev.currentTimeline, ev.gpuReadyTimeline, ev.hiZGraceTimeline,
                    ev.graceDelta, ev.nearestDepth, ev.pyramidDepth, ev.mipLevel);
            } else {
                ImGui::TextDisabled("-");
            }
        }
        ImGui::EndTable();
        ImGui::TextDisabled("%d shown / %zu total events", shown, snap.events.size());
    }
}

````
