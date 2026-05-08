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

## src\rendering\culling\GPUCullingSlots.cpp

Description: No CC-DESC found.

````cpp
#include "rendering/culling/GPUCullingSystem.h"
#include "rendering/common/VulkanHelpers.h"
#include <stdexcept>
#include <iostream>
#include <cstring>
#include <cmath>
#include <algorithm>

// Number of debug stats (must match shader)
constexpr uint32_t DEBUG_STATS_COUNT = 16;

void GPUCullingSystem::createBuffers() {
    // All draws buffer (persistent, all chunk data)
    {
        VkDeviceSize size = sizeof(ChunkDrawData) * m_maxChunks;
        
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_allDrawsBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create all draws buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_allDrawsBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_allDrawsMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate all draws buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_allDrawsBuffer, m_allDrawsMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_allDrawsBuffer, "GPUCull_AllDrawsBuffer");
        
        std::cout << "[GPUCullingSystem] All draws buffer: " << (size / 1024.0f / 1024.0f) << " MB" << std::endl;
    }
    
    // Visible draws buffer (output, compacted)
    // Each chunk can produce up to GPU_MAX_SUBCHUNKS draws
    {
        VkDeviceSize size = sizeof(VkDrawIndexedIndirectCommand) * m_maxChunks * GPU_MAX_SUBCHUNKS;
        
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_visibleDrawsBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create visible draws buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_visibleDrawsBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_visibleDrawsMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate visible draws buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_visibleDrawsBuffer, m_visibleDrawsMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_visibleDrawsBuffer, "GPUCull_VisibleDrawsBuffer");
    }
    
    // Draw count buffer (atomic counter)
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = sizeof(uint32_t);
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_drawCountBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create draw count buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_drawCountBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_drawCountMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate draw count buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_drawCountBuffer, m_drawCountMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_drawCountBuffer, "GPUCull_DrawCountBuffer");
    }

    // Frustum-passed indices buffer (stage1 output, stage2 input)
    {
        VkDeviceSize size = sizeof(uint32_t) * m_maxChunks;

        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_frustumPassedIndicesBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create frustum-passed indices buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_frustumPassedIndicesBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_frustumPassedIndicesMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate frustum-passed indices memory!");
        }

        vkBindBufferMemory(m_device, m_frustumPassedIndicesBuffer, m_frustumPassedIndicesMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_frustumPassedIndicesBuffer, "GPUCull_FrustumPassedIndices");
    }

    // Frustum-passed count buffer (stage1 atomic counter)
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = sizeof(uint32_t);
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_frustumPassedCountBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create frustum-passed count buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_frustumPassedCountBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_frustumPassedCountMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate frustum-passed count memory!");
        }

        vkBindBufferMemory(m_device, m_frustumPassedCountBuffer, m_frustumPassedCountMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_frustumPassedCountBuffer, "GPUCull_FrustumPassedCount");
    }

    // Stage-2 dispatch args buffer (written by compute, consumed by vkCmdDispatchIndirect).
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = sizeof(VkDispatchIndirectCommand);
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_frustumDispatchArgsBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create frustum dispatch args buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_frustumDispatchArgsBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_frustumDispatchArgsMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate frustum dispatch args memory!");
        }

        vkBindBufferMemory(m_device, m_frustumDispatchArgsBuffer, m_frustumDispatchArgsMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_frustumDispatchArgsBuffer, "GPUCull_FrustumDispatchArgs");
    }

    // Phase A — temporal-coherence visibility mask (one bit per slot, persistent).
    // Cleared once at creation; thereafter maintained by the cull shaders.
    // Phase C — on slot free/reuse/upload we now ALSO explicitly enqueue a word-clear
    // (m_pendingMaskWordClears) drained in recordCulling. The hiZGraceTimeline mechanism
    // remains the primary guard against death-spiral occlusion of fresh data; explicit
    // mask clearing is a belt-and-suspenders invariant that keeps the temporal-skip path
    // honest even outside the grace window.
    {
        const VkDeviceSize words = (static_cast<VkDeviceSize>(m_maxChunks) + 31u) / 32u;
        const VkDeviceSize size = words * sizeof(uint32_t);
        m_prevVisibleMaskSize = size;

        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_prevVisibleMaskBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create prev-visible mask buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_prevVisibleMaskBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_prevVisibleMaskMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate prev-visible mask memory!");
        }

        vkBindBufferMemory(m_device, m_prevVisibleMaskBuffer, m_prevVisibleMaskMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER,
                                     (uint64_t)m_prevVisibleMaskBuffer,
                                     "GPUCull_PrevVisibleMask");
    }
    
    // Visible origins buffer (output, for shader)
    // Each chunk can produce up to GPU_MAX_SUBCHUNKS draws, each needs an origin
    if (m_externalOriginsBuffer != VK_NULL_HANDLE) {
        // Use external buffer (Engine's m_chunkOriginsBuffer) - don't create our own
        m_visibleOriginsBuffer = m_externalOriginsBuffer;
        m_visibleOriginsMemory = VK_NULL_HANDLE;  // We don't own the memory
        m_usingExternalOriginsBuffer = true;
        std::cout << "[GPUCullingSystem] Using external origins buffer" << std::endl;
    } else {
        // Create our own buffer
        VkDeviceSize size = sizeof(glm::vec4) * m_maxChunks * GPU_MAX_SUBCHUNKS;
        
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_visibleOriginsBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create visible origins buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_visibleOriginsBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_visibleOriginsMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate visible origins buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_visibleOriginsBuffer, m_visibleOriginsMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_visibleOriginsBuffer, "GPUCull_VisibleOriginsBuffer");
        m_usingExternalOriginsBuffer = false;
    }

    // Active slot-indirection buffer (GPU device-local)
    {
        VkDeviceSize size = sizeof(uint32_t) * m_maxChunks;

        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_activeIndicesBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create active indices buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_activeIndicesBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_activeIndicesMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate active indices buffer memory!");
        }

        vkBindBufferMemory(m_device, m_activeIndicesBuffer, m_activeIndicesMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_activeIndicesBuffer, "GPUCull_ActiveIndicesBuffer");
    }

    // Active slot-indirection staging buffer (host-visible, host-coherent)
    {
        VkDeviceSize size = sizeof(uint32_t) * m_maxChunks;

        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = size;
        bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_activeIndicesStagingBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create active indices staging buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_activeIndicesStagingBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_activeIndicesStagingMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate active indices staging memory!");
        }

        vkBindBufferMemory(m_device, m_activeIndicesStagingBuffer, m_activeIndicesStagingMemory, 0);

        void* mapped = nullptr;
        if (vkMapMemory(m_device, m_activeIndicesStagingMemory, 0, size, 0, &mapped) != VK_SUCCESS) {
            throw std::runtime_error("Failed to map active indices staging memory!");
        }
        m_activeIndicesStagingMapped = static_cast<uint32_t*>(mapped);
        memset(m_activeIndicesStagingMapped, 0, static_cast<size_t>(size));

        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_activeIndicesStagingBuffer, "GPUCull_ActiveIndicesStaging");
    }
    
    // Readback buffer for draw count (host-visible, coherent)
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = sizeof(uint32_t);
        bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_readbackBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create readback buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_readbackBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, 
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_readbackMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate readback buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_readbackBuffer, m_readbackMemory, 0);
        
        // Persistently map the readback buffer
        void* mapped;
        if (vkMapMemory(m_device, m_readbackMemory, 0, sizeof(uint32_t), 0, &mapped) != VK_SUCCESS) {
            throw std::runtime_error("Failed to map readback buffer!");
        }
        m_readbackMapped = static_cast<uint32_t*>(mapped);
        *m_readbackMapped = 0;
        
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_readbackBuffer, "GPUCull_ReadbackBuffer");
    }
    
    // Debug stats buffer (GPU-side)
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = sizeof(uint32_t) * DEBUG_STATS_COUNT;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_debugStatsBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create debug stats buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_debugStatsBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_debugStatsMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate debug stats buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_debugStatsBuffer, m_debugStatsMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_debugStatsBuffer, "GPUCull_DebugStatsBuffer");
    }
    
    // Debug stats readback buffer (host-visible)
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = sizeof(uint32_t) * DEBUG_STATS_COUNT;
        bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        
        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_debugStatsReadbackBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create debug stats readback buffer!");
        }
        
        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_debugStatsReadbackBuffer, &memReq);
        
        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, 
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
        
        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_debugStatsReadbackMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate debug stats readback buffer memory!");
        }
        
        vkBindBufferMemory(m_device, m_debugStatsReadbackBuffer, m_debugStatsReadbackMemory, 0);
        
        void* mapped;
        if (vkMapMemory(m_device, m_debugStatsReadbackMemory, 0, sizeof(uint32_t) * DEBUG_STATS_COUNT, 0, &mapped) != VK_SUCCESS) {
            throw std::runtime_error("Failed to map debug stats readback buffer!");
        }
        m_debugStatsMapped = static_cast<uint32_t*>(mapped);
        memset(m_debugStatsMapped, 0, sizeof(uint32_t) * DEBUG_STATS_COUNT);
        
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_debugStatsReadbackBuffer, "GPUCull_DebugStatsReadback");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Hi-Z blink log buffers (per-frame GPU ring + host-visible readback)
    // ─────────────────────────────────────────────────────────────────────────
    // Layout: [count u32][dropped u32][_pad u32][_pad u32]  +  CAPACITY * HiZBlinkEvent
    constexpr VkDeviceSize kBlinkHeaderSize = sizeof(uint32_t) * 4;
    const VkDeviceSize kBlinkLogSize =
        kBlinkHeaderSize + sizeof(HiZBlinkEvent) * HIZ_BLINK_LOG_GPU_CAPACITY;

    // GPU-side (device-local)
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = kBlinkLogSize;
        bufferInfo.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT
                         | VK_BUFFER_USAGE_TRANSFER_SRC_BIT
                         | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_hiZBlinkLogBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create HiZ blink log buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_hiZBlinkLogBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_hiZBlinkLogMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate HiZ blink log memory!");
        }

        vkBindBufferMemory(m_device, m_hiZBlinkLogBuffer, m_hiZBlinkLogMemory, 0);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_hiZBlinkLogBuffer, "GPUCull_HiZBlinkLog");
    }

    // Host-visible readback
    {
        VkBufferCreateInfo bufferInfo{};
        bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufferInfo.size = kBlinkLogSize;
        bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
        bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

        if (vkCreateBuffer(m_device, &bufferInfo, nullptr, &m_hiZBlinkLogReadbackBuffer) != VK_SUCCESS) {
            throw std::runtime_error("Failed to create HiZ blink log readback buffer!");
        }

        VkMemoryRequirements memReq;
        vkGetBufferMemoryRequirements(m_device, m_hiZBlinkLogReadbackBuffer, &memReq);

        VkMemoryAllocateInfo allocInfo{};
        allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocInfo.allocationSize = memReq.size;
        allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
            m_physicalDevice, memReq.memoryTypeBits,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);

        if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_hiZBlinkLogReadbackMemory) != VK_SUCCESS) {
            throw std::runtime_error("Failed to allocate HiZ blink log readback memory!");
        }

        vkBindBufferMemory(m_device, m_hiZBlinkLogReadbackBuffer, m_hiZBlinkLogReadbackMemory, 0);

        void* mapped = nullptr;
        if (vkMapMemory(m_device, m_hiZBlinkLogReadbackMemory, 0, kBlinkLogSize, 0, &mapped) != VK_SUCCESS) {
            throw std::runtime_error("Failed to map HiZ blink log readback buffer!");
        }
        m_hiZBlinkLogMapped = static_cast<uint8_t*>(mapped);
        std::memset(m_hiZBlinkLogMapped, 0, static_cast<size_t>(kBlinkLogSize));

        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_BUFFER, (uint64_t)m_hiZBlinkLogReadbackBuffer, "GPUCull_HiZBlinkLogReadback");
    }
}

uint32_t GPUCullingSystem::allocateSlot(bool active) {
    std::lock_guard<std::mutex> lock(m_slotMutex);

    if (m_freeSlots.empty()) {
        return UINT32_MAX;
    }

    uint32_t slot = m_freeSlots.back();
    m_freeSlots.pop_back();
    m_slotOccupied[slot] = true;

    // Reused slots must not inherit the previous chunk's material-overlay hint.
    if (slot < m_slotMaterialOverlayHints.size()) {
        m_slotMaterialOverlayHints[slot] = 0u;
        m_pendingMaterialOverlayHintUpdates[slot] = 0u;
    }

    if (active) {
        // Add to compact active-slot list (for indirection dispatch).
        m_slotToActiveIndex[slot] = static_cast<uint32_t>(m_activeSlots.size());
        m_activeSlots.push_back(slot);
        m_activeIndicesDirty = true;
        m_activeSlotCount.fetch_add(1, std::memory_order_relaxed);
    } else {
        m_slotToActiveIndex[slot] = UINT32_MAX;
    }

    // Cancel any pending invalidation for this slot — it's being reused
    m_pendingInvalidations.erase(slot);

    // Reused slots must start with a clean CPU-side debug state.
    if (slot < m_editWatchStates.size()) {
        m_editWatchStates[slot] = EditWatchSlotState{};
    }
    m_editWatchedSlots.erase(
        std::remove(m_editWatchedSlots.begin(), m_editWatchedSlots.end(), slot),
        m_editWatchedSlots.end());

    // Update high-water mark: shader must iterate at least up to slot+1
    uint32_t prevHWM = m_highWaterMark.load(std::memory_order_relaxed);
    if (slot + 1 > prevHWM) {
        m_highWaterMark.store(slot + 1, std::memory_order_relaxed);
    }

    return slot;
}

void GPUCullingSystem::activateSlot(uint32_t slotIndex) {
    if (slotIndex >= m_maxChunks) return;

    std::lock_guard<std::mutex> lock(m_slotMutex);
    if (!m_slotOccupied[slotIndex]) return;
    if (m_slotToActiveIndex[slotIndex] != UINT32_MAX) return;

    m_slotToActiveIndex[slotIndex] = static_cast<uint32_t>(m_activeSlots.size());
    m_activeSlots.push_back(slotIndex);
    m_activeIndicesDirty = true;
    m_activeSlotCount.fetch_add(1, std::memory_order_relaxed);
    m_pendingMaskWordClears.insert(slotIndex / 32u);

    if (slotIndex < m_slotMaterialOverlayHints.size()) {
        m_pendingMaterialOverlayHintUpdates[slotIndex] = m_slotMaterialOverlayHints[slotIndex];
    }
}

void GPUCullingSystem::activateSlotsAndFreeSlots(const uint32_t* slotsToActivate,
                                                 size_t activateCount,
                                                 const uint32_t* slotsToFree,
                                                 size_t freeCount) {
    if (activateCount == 0 && freeCount == 0) return;
    if ((activateCount > 0 && slotsToActivate == nullptr) ||
        (freeCount > 0 && slotsToFree == nullptr)) {
        return;
    }

    std::lock_guard<std::mutex> lock(m_slotMutex);

    for (size_t i = 0; i < freeCount; ++i) {
        uint32_t s = slotsToFree[i];
        if (s >= m_maxChunks) continue;
        if (!m_slotOccupied[s]) continue;

        m_freeSlots.push_back(s);
        m_slotOccupied[s] = false;

        uint32_t activeIdx = m_slotToActiveIndex[s];
        if (activeIdx != UINT32_MAX) {
            uint32_t lastSlot = m_activeSlots.back();
            m_activeSlots[activeIdx] = lastSlot;
            m_slotToActiveIndex[lastSlot] = activeIdx;
            m_activeSlots.pop_back();
            m_slotToActiveIndex[s] = UINT32_MAX;
            m_activeIndicesDirty = true;
        }

        m_pendingInvalidations.insert(s);
        m_pendingMaskWordClears.insert(s / 32u);
        if (s < m_slotMaterialOverlayHints.size()) {
            m_slotMaterialOverlayHints[s] = 0u;
            m_pendingMaterialOverlayHintUpdates[s] = 0u;
        }
    }

    for (size_t i = 0; i < activateCount; ++i) {
        uint32_t s = slotsToActivate[i];
        if (s >= m_maxChunks) continue;
        if (!m_slotOccupied[s]) continue;
        if (m_slotToActiveIndex[s] != UINT32_MAX) continue;

        m_slotToActiveIndex[s] = static_cast<uint32_t>(m_activeSlots.size());
        m_activeSlots.push_back(s);
        m_activeIndicesDirty = true;
        m_pendingMaskWordClears.insert(s / 32u);
        if (s < m_slotMaterialOverlayHints.size()) {
            m_pendingMaterialOverlayHintUpdates[s] = m_slotMaterialOverlayHints[s];
        }
    }

    m_activeSlotCount.store(static_cast<uint32_t>(m_activeSlots.size()), std::memory_order_relaxed);

    if (freeCount > 0) {
        uint32_t hwm = m_highWaterMark.load(std::memory_order_relaxed);
        while (hwm > 0 && !m_slotOccupied[hwm - 1]) {
            --hwm;
        }
        m_highWaterMark.store(hwm, std::memory_order_relaxed);
    }
}

void GPUCullingSystem::freeSlot(uint32_t slotIndex) {
    if (slotIndex >= m_maxChunks) return;

    std::lock_guard<std::mutex> lock(m_slotMutex);
    if (!m_slotOccupied[slotIndex]) return;

    m_freeSlots.push_back(slotIndex);
    m_slotOccupied[slotIndex] = false;

    // Remove from compact active-slot list via swap-remove.
    uint32_t activeIdx = m_slotToActiveIndex[slotIndex];
    if (activeIdx != UINT32_MAX) {
        uint32_t lastSlot = m_activeSlots.back();
        m_activeSlots[activeIdx] = lastSlot;
        m_slotToActiveIndex[lastSlot] = activeIdx;
        m_activeSlots.pop_back();
        m_slotToActiveIndex[slotIndex] = UINT32_MAX;
        m_activeIndicesDirty = true;
        m_activeSlotCount.fetch_sub(1, std::memory_order_relaxed);
    }

    m_pendingInvalidations.insert(slotIndex);
    // Phase C: clear the temporal-coherence visibility bit for this slot so the
    // next chunk that reuses the slot is guaranteed to be re-tested by Hi-Z.
    m_pendingMaskWordClears.insert(slotIndex / 32u);

    if (slotIndex < m_slotMaterialOverlayHints.size()) {
        m_slotMaterialOverlayHints[slotIndex] = 0u;
        m_pendingMaterialOverlayHintUpdates[slotIndex] = 0u;
    }

    // Shrink HWM using bitset — O(gap) walk, no heap allocation
    uint32_t hwm = m_highWaterMark.load(std::memory_order_relaxed);
    if (slotIndex + 1 == hwm) {
        while (hwm > 0 && !m_slotOccupied[hwm - 1]) {
            --hwm;
        }
        m_highWaterMark.store(hwm, std::memory_order_relaxed);
    }
}

void GPUCullingSystem::freeSlots(const uint32_t* slots, size_t count) {
    if (count == 0) return;

    std::lock_guard<std::mutex> lock(m_slotMutex);

    uint32_t activeFreed = 0;
    for (size_t i = 0; i < count; ++i) {
        uint32_t s = slots[i];
        if (s >= m_maxChunks) continue;
        if (!m_slotOccupied[s]) continue;

        m_freeSlots.push_back(s);
        m_slotOccupied[s] = false;

        uint32_t activeIdx = m_slotToActiveIndex[s];
        if (activeIdx != UINT32_MAX) {
            uint32_t lastSlot = m_activeSlots.back();
            m_activeSlots[activeIdx] = lastSlot;
            m_slotToActiveIndex[lastSlot] = activeIdx;
            m_activeSlots.pop_back();
            m_slotToActiveIndex[s] = UINT32_MAX;
            m_activeIndicesDirty = true;
            activeFreed++;
        }

        m_pendingInvalidations.insert(s);
        // Phase C: clear visibility bit for freed slot.
        m_pendingMaskWordClears.insert(s / 32u);
        if (s < m_slotMaterialOverlayHints.size()) {
            m_slotMaterialOverlayHints[s] = 0u;
            m_pendingMaterialOverlayHintUpdates[s] = 0u;
        }
    }
    if (activeFreed > 0) {
        m_activeSlotCount.fetch_sub(activeFreed, std::memory_order_relaxed);
    }

    // Shrink HWM using bitset — no heap allocation
    uint32_t hwm = m_highWaterMark.load(std::memory_order_relaxed);
    while (hwm > 0 && !m_slotOccupied[hwm - 1]) {
        --hwm;
    }
    m_highWaterMark.store(hwm, std::memory_order_relaxed);
}

void GPUCullingSystem::noteChunkDrawDataUpload(uint32_t slotIndex,
                                               const ChunkDrawData& drawData,
                                               bool fromTerrainEdit,
                                               bool replacesExistingMesh) {
    if (slotIndex >= m_maxChunks) {
        return;
    }

    std::lock_guard<std::mutex> lock(m_slotMutex);
    if (slotIndex >= m_editWatchStates.size()) {
        return;
    }

    // Full ChunkDrawData uploads overwrite _pad1. Re-apply the authoritative
    // CPU-side material-overlay hint immediately before the next cull dispatch.
    if (slotIndex < m_slotMaterialOverlayHints.size()) {
        m_pendingMaterialOverlayHintUpdates[slotIndex] = m_slotMaterialOverlayHints[slotIndex];
    }

    EditWatchSlotState& watch = m_editWatchStates[slotIndex];
    watch.hasMetadata = true;
    watch.fromTerrainEdit = fromTerrainEdit;
    watch.replacesExistingMesh = replacesExistingMesh;
    watch.aabbMin = drawData.aabbMin;
    watch.aabbMax = drawData.aabbMax;
    watch.chunkX = static_cast<int32_t>(std::lround(drawData.origin.x));
    watch.chunkY = static_cast<int32_t>(std::lround(drawData.origin.y));
    watch.chunkZ = static_cast<int32_t>(std::lround(drawData.origin.z));

    const uint32_t subChunkCount = std::min(drawData.subChunkCount, GPU_MAX_SUBCHUNKS);
    watch.subChunkCount = subChunkCount;

    uint32_t validDrawCount = 0;
    for (uint32_t i = 0; i < subChunkCount; ++i) {
        if (drawData.draws[i].indexCount > 0u) {
            ++validDrawCount;
        }
    }
    watch.validDrawCount = validDrawCount;

    uint32_t gpuReadyTimeline = 0;
    std::memcpy(&gpuReadyTimeline, &drawData.origin.w, sizeof(uint32_t));
    watch.gpuReadyTimeline = gpuReadyTimeline;
    watch.hiZGraceTimeline = drawData.hiZGraceTimeline;

    // Phase C: any upload (initial or edit) replaces the chunk's data — clear its
    // temporal-coherence bit so next frame's cull cannot temporally-skip Hi-Z based on
    // the previous chunk's visibility. hiZGraceTimeline already forces re-test inside
    // the grace window; this guarantees correctness past it as well.
    m_pendingMaskWordClears.insert(slotIndex / 32u);

    watch.uploadSerial = ++m_editVisibilityUploadSerial;
    if (fromTerrainEdit) {
        watch.editUploadSerial = watch.uploadSerial;
        watch.watchFramesRemaining = EDIT_VISIBILITY_WATCH_FRAMES;
        watch.lastDrawnKnown = false;
        watch.lastDrawn = false;
        watch.lastState = EditVisibilityState::Unknown;

        if (std::find(m_editWatchedSlots.begin(), m_editWatchedSlots.end(), slotIndex) == m_editWatchedSlots.end()) {
            m_editWatchedSlots.push_back(slotIndex);
        }
    } else {
        watch.editUploadSerial = 0;
        watch.watchFramesRemaining = 0;
        watch.lastDrawnKnown = false;
        watch.lastDrawn = false;
        watch.lastState = EditVisibilityState::Unknown;
        m_editWatchedSlots.erase(
            std::remove(m_editWatchedSlots.begin(), m_editWatchedSlots.end(), slotIndex),
            m_editWatchedSlots.end());
    }
}

void GPUCullingSystem::resetAllSlots() {
    std::lock_guard<std::mutex> lock(m_slotMutex);

    // Rebuild the free list with all slots
    m_freeSlots.clear();
    m_freeSlots.reserve(m_maxChunks);
    for (uint32_t i = m_maxChunks; i > 0; --i) {
        m_freeSlots.push_back(i - 1);
    }
    m_slotOccupied.assign(m_maxChunks, false);
    m_activeSlots.clear();
    m_slotToActiveIndex.assign(m_maxChunks, UINT32_MAX);
    m_activeIndicesDirty = true;

    m_pendingInvalidations.clear();
    m_pendingMaskWordClears.clear();
    m_slotMaterialOverlayHints.assign(m_maxChunks, 0u);
    m_pendingMaterialOverlayHintUpdates.clear();
    m_activeSlotCount.store(0, std::memory_order_relaxed);
    m_highWaterMark.store(0, std::memory_order_relaxed);
    m_lastDispatchChunkCount.store(0, std::memory_order_relaxed);
    m_lastDispatchHiZEnabled.store(0, std::memory_order_relaxed);
    m_lastVisibleDrawCount.store(0, std::memory_order_relaxed);
    m_drawCountReadbackPending = false;
    m_editWatchStates.assign(m_maxChunks, EditWatchSlotState{});
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

## src\rendering\culling\HiZPyramidResources.cpp

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

// Resource creation/destruction - extracted from HiZPyramid.cpp

void HiZPyramid::destroyPyramidImage() {
    if (!m_device) return;

    // Invalidate ImGui texture (will be re-registered on next getImGuiTextureID call)
    if (m_imguiTextureRegistered && m_imguiDescriptorSet) {
        ImGui_ImplVulkan_RemoveTexture(m_imguiDescriptorSet);
        m_imguiDescriptorSet = VK_NULL_HANDLE;
        m_imguiTextureRegistered = false;
    }

    // Destroy descriptor pool (frees all descriptor sets automatically)
    if (m_descriptorPool) {
        vkDestroyDescriptorPool(m_device, m_descriptorPool, nullptr);
        m_descriptorPool = VK_NULL_HANDLE;
        for (uint32_t i = 0; i < MAX_MIP_LEVELS; ++i) {
            m_mipDescriptorSets[i] = VK_NULL_HANDLE;
        }
    }

    // Destroy per-mip views
    for (uint32_t i = 0; i < MAX_MIP_LEVELS; ++i) {
        if (m_mipViews[i]) {
            vkDestroyImageView(m_device, m_mipViews[i], nullptr);
            m_mipViews[i] = VK_NULL_HANDLE;
        }
    }

    // Destroy full mip-chain view
    if (m_pyramidView) { vkDestroyImageView(m_device, m_pyramidView, nullptr); m_pyramidView = VK_NULL_HANDLE; }

    // Destroy image and memory
    if (m_pyramidImage) { vkDestroyImage(m_device, m_pyramidImage, nullptr); m_pyramidImage = VK_NULL_HANDLE; }
    if (m_pyramidMemory) { vkFreeMemory(m_device, m_pyramidMemory, nullptr); m_pyramidMemory = VK_NULL_HANDLE; }

    m_initialized = false;
    m_pyramidLayoutInitialized = false;
}

// ─────────────────────────────────────────────────────────────
//  Image creation
// ─────────────────────────────────────────────────────────────

void HiZPyramid::createPyramidImage() {
    // R32_SFLOAT pyramid: SAMPLED (for culling shader) + STORAGE (for compute writes)
    VkImageCreateInfo imageInfo{};
    imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imageInfo.imageType = VK_IMAGE_TYPE_2D;
    imageInfo.format = VK_FORMAT_R32_SFLOAT;
    imageInfo.extent = { m_pyramidWidth, m_pyramidHeight, 1 };
    imageInfo.mipLevels = m_mipLevels;
    imageInfo.arrayLayers = 1;
    imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
    imageInfo.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_STORAGE_BIT;
    imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

    if (vkCreateImage(m_device, &imageInfo, nullptr, &m_pyramidImage) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create depth pyramid image");
    }

    // Allocate device-local memory
    VkMemoryRequirements memReqs;
    vkGetImageMemoryRequirements(m_device, m_pyramidImage, &memReqs);

    VkMemoryAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memReqs.size;
    allocInfo.memoryTypeIndex = VulkanHelpers::findMemoryType(
        m_physicalDevice, memReqs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

    if (vkAllocateMemory(m_device, &allocInfo, nullptr, &m_pyramidMemory) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to allocate pyramid memory");
    }

    vkBindImageMemory(m_device, m_pyramidImage, m_pyramidMemory, 0);

    VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_IMAGE,
        reinterpret_cast<uint64_t>(m_pyramidImage), "HiZ Depth Pyramid");

    // Newly created image starts undefined until the first build transition.
    m_pyramidLayoutInitialized = false;
}

void HiZPyramid::createMipViews() {
    // Full mip-chain view (for sampling in the culling shader with LOD selection)
    VkImageViewCreateInfo fullViewInfo{};
    fullViewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    fullViewInfo.image = m_pyramidImage;
    fullViewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    fullViewInfo.format = VK_FORMAT_R32_SFLOAT;
    fullViewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    fullViewInfo.subresourceRange.baseMipLevel = 0;
    fullViewInfo.subresourceRange.levelCount = m_mipLevels;
    fullViewInfo.subresourceRange.baseArrayLayer = 0;
    fullViewInfo.subresourceRange.layerCount = 1;

    if (vkCreateImageView(m_device, &fullViewInfo, nullptr, &m_pyramidView) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create full pyramid image view");
    }

    // Per-mip views (for storage image writes in the reduce compute shader)
    for (uint32_t i = 0; i < m_mipLevels; ++i) {
        VkImageViewCreateInfo mipViewInfo{};
        mipViewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        mipViewInfo.image = m_pyramidImage;
        mipViewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        mipViewInfo.format = VK_FORMAT_R32_SFLOAT;
        mipViewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        mipViewInfo.subresourceRange.baseMipLevel = i;
        mipViewInfo.subresourceRange.levelCount = 1;
        mipViewInfo.subresourceRange.baseArrayLayer = 0;
        mipViewInfo.subresourceRange.layerCount = 1;

        if (vkCreateImageView(m_device, &mipViewInfo, nullptr, &m_mipViews[i]) != VK_SUCCESS) {
            throw std::runtime_error("[HiZPyramid] Failed to create mip " + std::to_string(i) + " image view");
        }

        std::string name = "HiZ Mip " + std::to_string(i);
        VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_IMAGE_VIEW,
            reinterpret_cast<uint64_t>(m_mipViews[i]), name.c_str());
    }
}

// ─────────────────────────────────────────────────────────────
//  Sampler (min-reduction)
// ─────────────────────────────────────────────────────────────

void HiZPyramid::createSampler() {
    // VK_SAMPLER_REDUCTION_MODE_MIN: hardware computes min of 2x2 texels on linear filter
    // This is the core of Hi-Z — each mip level stores the minimum (farthest in reversed-Z)
    // depth in the corresponding region, enabling conservative occlusion testing.
    VkSamplerReductionModeCreateInfo reductionInfo{};
    reductionInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_REDUCTION_MODE_CREATE_INFO;
    reductionInfo.reductionMode = VK_SAMPLER_REDUCTION_MODE_MIN;

    VkSamplerCreateInfo samplerInfo{};
    samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    samplerInfo.pNext = &reductionInfo;
    samplerInfo.magFilter = VK_FILTER_LINEAR;       // LINEAR + MIN reduction = min of 2x2
    samplerInfo.minFilter = VK_FILTER_LINEAR;
    samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;  // Explicit LOD selection
    samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    samplerInfo.anisotropyEnable = VK_FALSE;
    samplerInfo.maxAnisotropy = 1.0f;
    samplerInfo.compareEnable = VK_FALSE;
    samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
    samplerInfo.unnormalizedCoordinates = VK_FALSE;
    samplerInfo.minLod = 0.0f;
    samplerInfo.maxLod = static_cast<float>(MAX_MIP_LEVELS);

    if (vkCreateSampler(m_device, &samplerInfo, nullptr, &m_depthSampler) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create min-reduction sampler");
    }

    VulkanHelpers::setObjectName(m_device, VK_OBJECT_TYPE_SAMPLER,
        reinterpret_cast<uint64_t>(m_depthSampler), "HiZ Min-Reduction Sampler");

    // Visualization sampler: standard linear filtering (no reduction mode)
    // Used only for ImGui debug display of the pyramid
    VkSamplerCreateInfo vizSamplerInfo{};
    vizSamplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    vizSamplerInfo.magFilter = VK_FILTER_LINEAR;
    vizSamplerInfo.minFilter = VK_FILTER_LINEAR;
    vizSamplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    vizSamplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    vizSamplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    vizSamplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    vizSamplerInfo.anisotropyEnable = VK_FALSE;
    vizSamplerInfo.compareEnable = VK_FALSE;
    vizSamplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
    vizSamplerInfo.unnormalizedCoordinates = VK_FALSE;
    vizSamplerInfo.minLod = 0.0f;
    vizSamplerInfo.maxLod = 0.0f;  // Only show mip 0

    if (vkCreateSampler(m_device, &vizSamplerInfo, nullptr, &m_vizSampler) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create visualization sampler");
    }

    // Plain nearest sampler for the reduce compute shader.
    // The shader uses textureGather (bypasses filter), so this just needs
    // to be a valid sampler with correct address mode.
    VkSamplerCreateInfo reduceSamplerInfo{};
    reduceSamplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    reduceSamplerInfo.magFilter = VK_FILTER_NEAREST;
    reduceSamplerInfo.minFilter = VK_FILTER_NEAREST;
    reduceSamplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    reduceSamplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    reduceSamplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    reduceSamplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    reduceSamplerInfo.anisotropyEnable = VK_FALSE;
    reduceSamplerInfo.compareEnable = VK_FALSE;
    reduceSamplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_BLACK;
    reduceSamplerInfo.unnormalizedCoordinates = VK_FALSE;
    reduceSamplerInfo.minLod = 0.0f;
    reduceSamplerInfo.maxLod = static_cast<float>(MAX_MIP_LEVELS);

    if (vkCreateSampler(m_device, &reduceSamplerInfo, nullptr, &m_reduceSampler) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create reduce sampler");
    }
}

// ─────────────────────────────────────────────────────────────
//  Compute pipeline
// ─────────────────────────────────────────────────────────────

void HiZPyramid::createComputePipeline() {
    // Load SPIR-V shader
    auto shaderCode = VulkanHelpers::readFile("shaders/culling/depth_reduce.comp.spv");
    if (shaderCode.empty()) {
        throw std::runtime_error("[HiZPyramid] Failed to load depth_reduce.comp.spv");
    }

    VkShaderModuleCreateInfo moduleInfo{};
    moduleInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    moduleInfo.codeSize = shaderCode.size();
    moduleInfo.pCode = reinterpret_cast<const uint32_t*>(shaderCode.data());

    if (vkCreateShaderModule(m_device, &moduleInfo, nullptr, &m_reduceShader) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create depth reduce shader module");
    }

    // Descriptor set layout:
    //   binding 0..4: storage images (destination mips N..N+4)
    //   binding 5: combined image sampler (source mip or depth buffer)
    VkDescriptorSetLayoutBinding bindings[6]{};

    for (uint32_t b = 0; b < 5; ++b) {
        bindings[b].binding = b;
        bindings[b].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
        bindings[b].descriptorCount = 1;
        bindings[b].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    }

    bindings[5].binding = 5;
    bindings[5].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindings[5].descriptorCount = 1;
    bindings[5].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;

    VkDescriptorSetLayoutCreateInfo layoutInfo{};
    layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    layoutInfo.bindingCount = 6;
    layoutInfo.pBindings = bindings;

    if (vkCreateDescriptorSetLayout(m_device, &layoutInfo, nullptr, &m_reduceDescriptorLayout) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create reduce descriptor set layout");
    }

    // Push constants: destination mip sizes + generation mode.
    VkPushConstantRange pushRange{};
    pushRange.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    pushRange.offset = 0;
    pushRange.size = sizeof(DepthReducePushConstants);

    VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
    pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pipelineLayoutInfo.setLayoutCount = 1;
    pipelineLayoutInfo.pSetLayouts = &m_reduceDescriptorLayout;
    pipelineLayoutInfo.pushConstantRangeCount = 1;
    pipelineLayoutInfo.pPushConstantRanges = &pushRange;

    if (vkCreatePipelineLayout(m_device, &pipelineLayoutInfo, nullptr, &m_reducePipelineLayout) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create reduce pipeline layout");
    }

    // Compute pipeline
    VkComputePipelineCreateInfo pipelineInfo{};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
    pipelineInfo.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    pipelineInfo.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
    pipelineInfo.stage.module = m_reduceShader;
    pipelineInfo.stage.pName = "main";
    pipelineInfo.layout = m_reducePipelineLayout;

    if (vkCreateComputePipelines(m_device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &m_reducePipeline) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create depth reduce compute pipeline");
    }

    std::cout << "[HiZPyramid] ✓ Compute pipeline created" << std::endl;
}

// ─────────────────────────────────────────────────────────────
//  Descriptor resources (per mip level)
// ─────────────────────────────────────────────────────────────

void HiZPyramid::createDescriptorResources() {
    // Pool: 5 storage images + 1 combined image sampler per mip level
    VkDescriptorPoolSize poolSizes[2]{};
    poolSizes[0].type = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
    poolSizes[0].descriptorCount = m_mipLevels * 5;
    poolSizes[1].type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    poolSizes[1].descriptorCount = m_mipLevels;

    VkDescriptorPoolCreateInfo poolInfo{};
    poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    poolInfo.poolSizeCount = 2;
    poolInfo.pPoolSizes = poolSizes;
    poolInfo.maxSets = m_mipLevels;
    poolInfo.flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;  // Allow individual set updates

    if (vkCreateDescriptorPool(m_device, &poolInfo, nullptr, &m_descriptorPool) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to create descriptor pool");
    }

    // Allocate one descriptor set per mip level
    std::vector<VkDescriptorSetLayout> layouts(m_mipLevels, m_reduceDescriptorLayout);

    VkDescriptorSetAllocateInfo allocInfo{};
    allocInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    allocInfo.descriptorPool = m_descriptorPool;
    allocInfo.descriptorSetCount = m_mipLevels;
    allocInfo.pSetLayouts = layouts.data();

    if (vkAllocateDescriptorSets(m_device, &allocInfo, m_mipDescriptorSets) != VK_SUCCESS) {
        throw std::runtime_error("[HiZPyramid] Failed to allocate descriptor sets");
    }

    // Update each descriptor set.
    // Set i generates mip i and up to mip i+4 from source i-1 (or depth for i=0).
    for (uint32_t i = 0; i < m_mipLevels; ++i) {
        VkDescriptorImageInfo dstInfos[5]{};
        for (uint32_t k = 0; k < 5; ++k) {
            uint32_t mip = (i + k < m_mipLevels) ? (i + k) : (m_mipLevels - 1);
            dstInfos[k].imageView = m_mipViews[mip];
            dstInfos[k].imageLayout = VK_IMAGE_LAYOUT_GENERAL;
        }

        // Source: the previous mip level (or depth buffer for mip 0)
        VkDescriptorImageInfo srcInfo{};
        srcInfo.sampler = m_reduceSampler;  // Plain nearest sampler — shader uses textureGather + manual min
        if (i == 0) {
            // Mip 0 reads from the main depth buffer
            srcInfo.imageView = m_depthImageView;
            srcInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        } else {
            // Mip N reads from mip N-1
            srcInfo.imageView = m_mipViews[i - 1];
            srcInfo.imageLayout = VK_IMAGE_LAYOUT_GENERAL;
        }

        VkWriteDescriptorSet writes[6]{};
        for (uint32_t b = 0; b < 5; ++b) {
            writes[b].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            writes[b].dstSet = m_mipDescriptorSets[i];
            writes[b].dstBinding = b;
            writes[b].descriptorCount = 1;
            writes[b].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
            writes[b].pImageInfo = &dstInfos[b];
        }

        writes[5].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[5].dstSet = m_mipDescriptorSets[i];
        writes[5].dstBinding = 5;
        writes[5].descriptorCount = 1;
        writes[5].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        writes[5].pImageInfo = &srcInfo;

        vkUpdateDescriptorSets(m_device, 6, writes, 0, nullptr);
    }
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

## shaders\culling\depth_reduce.comp

Description: No CC-DESC found.

````glsl
#version 450

// Depth pyramid reduction shader.
// One dispatch can generate up to 5 mip levels:
//   - mip N       (always)
//   - mip N + 1   (mask bit 0)
//   - mip N + 2   (mask bit 1)
//   - mip N + 3   (mask bit 2)
//   - mip N + 4   (mask bit 3)
//
// 16x16 is intentionally chosen because it can be reduced in shared memory:
// 16x16 -> 8x8 -> 4x4 -> 2x2 -> 1x1 (max 5 levels per pass).
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

// Destination mip levels (may alias at the tail; guarded by levelMask in shader)
layout(binding = 0, r32f) uniform writeonly image2D outMip0;
layout(binding = 1, r32f) uniform writeonly image2D outMip1;
layout(binding = 2, r32f) uniform writeonly image2D outMip2;
layout(binding = 3, r32f) uniform writeonly image2D outMip3;
layout(binding = 4, r32f) uniform writeonly image2D outMip4;

// Source (depth buffer for mip 0, or previous mip level)
layout(binding = 5) uniform sampler2D inImage;

// Push constants: destination level dimensions
layout(push_constant) uniform PushConstants {
    uvec2 imageSize;   // Width/height of mip N
    uint levelMask;    // Bits 0..3 = write mip N+1..N+4
    uint _pad0;
} pc;

shared float sLevel0[16 * 16];
shared float sLevel1[8 * 8];
shared float sLevel2[4 * 4];
shared float sLevel3[2 * 2];

float reduce4IgnoreZero(vec4 d) {
    // Replace zeros with a large sentinel so they lose the min race.
    const float FAR_SENTINEL = 1.0;  // near plane in reversed-Z = largest possible depth
    vec4 safe = vec4(
        d.x > 0.0 ? d.x : FAR_SENTINEL,
        d.y > 0.0 ? d.y : FAR_SENTINEL,
        d.z > 0.0 ? d.z : FAR_SENTINEL,
        d.w > 0.0 ? d.w : FAR_SENTINEL
    );
    float depth = min(min(safe.x, safe.y), min(safe.z, safe.w));

    // If ALL four were zero (genuine sky), restore 0.0
    if (d.x == 0.0 && d.y == 0.0 && d.z == 0.0 && d.w == 0.0) {
        depth = 0.0;
    }
    return depth;
}

bool inBounds(uvec2 p, uvec2 size) {
    return p.x < size.x && p.y < size.y;
}

float buildMip0Depth(uvec2 pos, uvec2 size) {
    // Important: mip 0 is smaller than the source depth image when the pyramid
    // rounds down to the previous power-of-two. Sample in normalized UV space so
    // the whole source depth buffer contributes to the pyramid.
    vec2 uvMin = vec2(pos) / vec2(size);
    vec2 uvMax = vec2(pos + uvec2(1u)) / vec2(size);
    vec2 uv = 0.5 * (uvMin + uvMax);
    ivec2 sourceSize = textureSize(inImage, 0);
    if (sourceSize.x <= 0 || sourceSize.y <= 0) {
        return 0.0;
    }

    vec2 texelUv = 1.0 / vec2(sourceSize);
    vec2 gatherMin = texelUv;
    vec2 gatherMax = vec2(1.0) - texelUv;
    if (gatherMin.x <= gatherMax.x) {
        uv.x = clamp(uv.x, gatherMin.x, gatherMax.x);
    } else {
        uv.x = clamp(uv.x, 0.0, 1.0);
    }
    if (gatherMin.y <= gatherMax.y) {
        uv.y = clamp(uv.y, gatherMin.y, gatherMax.y);
    } else {
        uv.y = clamp(uv.y, 0.0, 1.0);
    }

    vec4 d = textureGather(inImage, uv, 0);
    return reduce4IgnoreZero(d);
}

void main() {
    uvec2 pos = gl_GlobalInvocationID.xy;
    uvec2 local = gl_LocalInvocationID.xy;
    uint localIndex = local.y * 16u + local.x;
    uvec2 size0 = pc.imageSize;
    uvec2 size1 = max(size0 >> 1u, uvec2(1u));
    uvec2 size2 = max(size1 >> 1u, uvec2(1u));
    uvec2 size3 = max(size2 >> 1u, uvec2(1u));
    uvec2 size4 = max(size3 >> 1u, uvec2(1u));

    bool valid0 = inBounds(pos, size0);
    float depth0 = 0.0;

    if (valid0) {
        depth0 = buildMip0Depth(pos, size0);
    }

    // Cache mip N result in shared memory for higher-level reductions.
    sLevel0[localIndex] = depth0;
    if (valid0) {
        imageStore(outMip0, ivec2(pos), vec4(depth0));
    }
    barrier();

    // mip N+1
    if ((pc.levelMask & 0x1u) == 0u) {
        return;
    }

    if ((local.x & 1u) != 0u || (local.y & 1u) != 0u) {
        // Non-writer lanes still participate in barriers below.
    } else {
        uvec2 dst1 = pos >> 1u;
        uint i00 = localIndex;
        uint i10 = i00 + 1u;
        uint i01 = i00 + 16u;
        uint i11 = i01 + 1u;
        float depth1 = reduce4IgnoreZero(vec4(sLevel0[i00], sLevel0[i10], sLevel0[i01], sLevel0[i11]));

        bool valid1 = inBounds(dst1, size1);
        if (valid1) {
            imageStore(outMip1, ivec2(dst1), vec4(depth1));
        } else {
            depth1 = 0.0;
        }

        uint idx1 = (local.y >> 1u) * 8u + (local.x >> 1u);
        sLevel1[idx1] = depth1;
    }
    barrier();

    // mip N+2
    if ((pc.levelMask & 0x2u) == 0u) {
        return;
    }

    if ((local.x & 3u) != 0u || (local.y & 3u) != 0u) {
        // Non-writer lanes still participate in barriers below.
    } else {
        uvec2 dst2 = pos >> 2u;
        uint sx = local.x >> 1u;
        uint sy = local.y >> 1u;
        uint j00 = sy * 8u + sx;
        uint j10 = j00 + 1u;
        uint j01 = j00 + 8u;
        uint j11 = j01 + 1u;
        float depth2 = reduce4IgnoreZero(vec4(sLevel1[j00], sLevel1[j10], sLevel1[j01], sLevel1[j11]));

        bool valid2 = inBounds(dst2, size2);
        if (valid2) {
            imageStore(outMip2, ivec2(dst2), vec4(depth2));
        } else {
            depth2 = 0.0;
        }

        uint idx2 = (local.y >> 2u) * 4u + (local.x >> 2u);
        sLevel2[idx2] = depth2;
    }
    barrier();

    // mip N+3
    if ((pc.levelMask & 0x4u) == 0u) {
        return;
    }

    if ((local.x & 7u) != 0u || (local.y & 7u) != 0u) {
        // Non-writer lanes still participate in barriers below.
    } else {
        uvec2 dst3 = pos >> 3u;
        uint sx = local.x >> 2u;
        uint sy = local.y >> 2u;
        uint k00 = sy * 4u + sx;
        uint k10 = k00 + 1u;
        uint k01 = k00 + 4u;
        uint k11 = k01 + 1u;
        float depth3 = reduce4IgnoreZero(vec4(sLevel2[k00], sLevel2[k10], sLevel2[k01], sLevel2[k11]));

        bool valid3 = inBounds(dst3, size3);
        if (valid3) {
            imageStore(outMip3, ivec2(dst3), vec4(depth3));
        } else {
            depth3 = 0.0;
        }

        uint idx3 = (local.y >> 3u) * 2u + (local.x >> 3u);
        sLevel3[idx3] = depth3;
    }
    barrier();

    // mip N+4
    if ((pc.levelMask & 0x8u) == 0u) {
        return;
    }

    if (local.x != 0u || local.y != 0u) {
        return;
    }

    uvec2 dst4 = pos >> 4u;
    if (!inBounds(dst4, size4)) {
        return;
    }

    float depth4 = reduce4IgnoreZero(vec4(sLevel3[0], sLevel3[1], sLevel3[2], sLevel3[3]));
    imageStore(outMip4, ivec2(dst4), vec4(depth4));
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
    uvec4 temporalInfo;     // 16 bytes  (offset 176) - x=temporalCoherenceEnabled, y=motionFrame, z=blinkLog
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
    float padTexels = HIZ_RECT_BASE_PADDING_TEXELS;
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
