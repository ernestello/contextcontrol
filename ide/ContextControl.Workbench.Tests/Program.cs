// CC-DESC: Runs focused smoke checks for deterministic ContextControl file resolution.

using System.Text.Json.Nodes;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

var root = Path.Combine(Path.GetTempPath(), "ContextControlResolverSmoke", Guid.NewGuid().ToString("N"));
try
{
    WriteFile(
        "Views/MainWindow.axaml",
        """
        <Window>
          <Button Classes="cc-prompt-send" Content="Send" Command="{Binding ContextControl.SendCommand}" />
          <TextBox Classes="cc-prompt-input" Watermark="Prompt" />
        </Window>
        """);
    WriteFile(
        "Styles/WorkbenchDesign.axaml",
        """
        <Styles>
          <Style Selector="Button.cc-prompt-send">
            <Setter Property="Background" Value="#18222E" />
          </Style>
        </Styles>
        """);
    WriteFile(
        "Services/LocalLlmService.cs",
        """
        namespace Smoke;
        public sealed class LocalLlmService
        {
            public string OllamaInstallProgressLabel => "Ollama installer download progress";
        }
        """);
    WriteFile(
        "ViewModels/ContextControlViewModel.cs",
        """
        namespace Smoke;
        public sealed class ContextControlViewModel
        {
            public object SendCommand { get; } = new();
        }
        """);

    var rules = ProjectFileRules.Load(root);
    var builder = new ContextSemanticMapBuilder();
    var index = (await builder.BuildIndexAsync(root, "", rules)).Index;
    var resolver = new ContextFileResolverService();

    var sendButton = resolver.Resolve("Change the button send in prompt window to red", index);
    RequireContains(sendButton, "Views/MainWindow.axaml");
    RequireContains(sendButton, "Styles/WorkbenchDesign.axaml");
    RequireNotContains(sendButton, "Services/LocalLlmService.cs");

    var ollama = resolver.Resolve("fix ollama install progress stuck", index);
    RequireContains(ollama, "Services/LocalLlmService.cs");

    var typo = resolver.Resolve("MainWindoiw.axaml.cs", index);
    RequireNotContains(typo, "MainWindoiw.axaml.cs");

    var unknown = resolver.Resolve("make bananas sparkle", index);
    if (!unknown.UsesFindTerms)
    {
        throw new InvalidOperationException("Low-confidence request should fall back to FIND terms.");
    }

    var codexCatalogEntries = CodexInstructionCatalog.SkillbookEntries;
    if (!codexCatalogEntries.Any(entry => entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase))
        || !codexCatalogEntries.Any(entry => entry.Source.Equals("skillflow", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Skillbook should expose both Codex harness instructions and Skillflow phase entries.");
    }

    var skillbook = new SkillbookService(root);
    var skillbookEntries = skillbook.LoadEntries();
    if (!skillbookEntries.Any(entry => entry.Key.Equals("codex-no-repo-navigation", StringComparison.OrdinalIgnoreCase))
        || !skillbookEntries.Any(entry => entry.Key.Equals("skillflow-03-resolve", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Skillbook entries should include visible Codex instructions and Skillflow phases.");
    }

    var codexEntryView = new SkillbookEntryViewModel(skillbookEntries.First(entry => entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase)));
    var skillflowEntryView = new SkillbookEntryViewModel(skillbookEntries.First(entry => entry.Source.Equals("skillflow", StringComparison.OrdinalIgnoreCase)));
    if (!codexEntryView.SectionTitle.Equals("Codex Instructions", StringComparison.Ordinal)
        || !skillflowEntryView.SectionTitle.Equals("Skillflow", StringComparison.Ordinal)
        || codexEntryView.SourceRank >= skillflowEntryView.SourceRank)
    {
        throw new InvalidOperationException("Skillbook view models should expose separate Codex and Skillflow sections in order.");
    }

    var localSkillbookText = skillbook.BuildEnabledInstructionText();
    if (localSkillbookText.Contains("Codex is the reasoning engine inside ContextControl", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Local LLM Skillbook text should not silently absorb Codex-only harness instructions.");
    }

    var codexPrompt = CodexHarnessService.BuildPrompt(new CodexHarnessRequest(
        "find the smallest files for this edit",
        ContextCapsulePhase.FileRequest,
        root,
        [
            new ContextCapsuleAttachment(
                "cc_project_dir.md",
                "dir",
                Path.Combine(root, "cc_project_dir.md"),
                "Views/MainWindow.axaml\nStyles/WorkbenchDesign.axaml",
                true)
        ],
        skillbook.BuildCodexInstructionText(ContextCapsulePhase.FileRequest),
        localSkillbookText));
    RequireTextContains(codexPrompt, "Your working directory is an empty harness folder by design.");
    RequireTextContains(codexPrompt, "Codex no repo navigation");
    RequireTextContains(codexPrompt, "Skillflow phase");
    RequireTextContains(codexPrompt, "Views/MainWindow.axaml");

    var codexPlan = CodexHarnessService.BuildExecutionPlan(root);
    if (!codexPlan.HarnessRoot.EndsWith(Path.Combine(".tmp", "codex-harness"), StringComparison.OrdinalIgnoreCase)
        || !codexPlan.Arguments.Contains("--json", StringComparer.Ordinal)
        || !codexPlan.Arguments.Contains("--ephemeral", StringComparer.Ordinal)
        || !codexPlan.Arguments.Contains("--ignore-rules", StringComparer.Ordinal)
        || !HasAdjacentArguments(codexPlan.Arguments, "--sandbox", "read-only")
        || !HasAdjacentArguments(codexPlan.Arguments, "--ask-for-approval", "never")
        || !HasAdjacentArguments(codexPlan.Arguments, "-C", codexPlan.HarnessRoot))
    {
        throw new InvalidOperationException("Codex harness execution plan should force the optimized read-only CC capsule route.");
    }

    var codexReadyStatus = new CodexAvailabilityResult(true, "Codex CLI ready", "codex-cli test", IsAuthenticated: true);
    if (!codexReadyStatus.Available || !codexReadyStatus.IsAuthenticated || codexReadyStatus.RequiresLogin)
    {
        throw new InvalidOperationException("Codex availability should distinguish installed/authenticated from login-required states.");
    }

    var codexLoginStatus = new CodexAvailabilityResult(true, "Codex login required", "codex-cli test", RequiresLogin: true);
    if (!codexLoginStatus.Available || codexLoginStatus.IsAuthenticated || !codexLoginStatus.RequiresLogin)
    {
        throw new InvalidOperationException("Codex availability should expose login-required setup state for fresh machines.");
    }

    if (!CodexHarnessService.IsLoginRequiredText("an error occurred trying to access token")
        || !CodexHarnessService.IsLoginRequiredText("not logged in")
        || CodexHarnessService.IsLoginRequiredText("max output tokens reached"))
    {
        throw new InvalidOperationException("Codex login error detection should catch auth/token failures without treating ordinary token counts as auth setup.");
    }

    var cancellableProgress = new ChatRequestProgressViewModel("session", "codex file request", isCancellable: true);
    if (!cancellableProgress.IsCancellable)
    {
        throw new InvalidOperationException("Codex chat progress rows should expose cancellation affordances.");
    }

    var diagnosticMessage = new LocalLlmChatMessageViewModel(
        "user",
        "Please use Codex mode.",
        "Codex CLI",
        "codex file request",
        diagnosticPrompt: codexPrompt);
    if (!diagnosticMessage.HasDiagnosticPrompt || diagnosticMessage.IsDiagnosticExpanded)
    {
        throw new InvalidOperationException("Codex chat messages should keep the harness capsule available but collapsed by default.");
    }

    diagnosticMessage.ToggleDiagnostic();
    if (!diagnosticMessage.IsDiagnosticExpanded)
    {
        throw new InvalidOperationException("Codex harness diagnostics should be expandable from chat.");
    }

    var validCodexFileAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        Research scope: prompt send button style
        Views/MainWindow.axaml
        Styles/WorkbenchDesign.axaml
        END
        """);
    if (!validCodexFileAudit.Passed
        || validCodexFileAudit.Level != CodexPhaseAuditLevel.Pass
        || validCodexFileAudit.RequestLines.Count != 2)
    {
        throw new InvalidOperationException("Codex file-request audit should pass clean CC request lists ending with END.");
    }

    var warningCodexFileAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.FileRequest,
        """
        Here are the files:
        Views/MainWindow.axaml
        """);
    if (!warningCodexFileAudit.Passed || warningCodexFileAudit.Level != CodexPhaseAuditLevel.Warning)
    {
        throw new InvalidOperationException("Codex file-request audit should warn when usable request lines include prose or omit END.");
    }

    var invalidCodexFileAudit = CodexPhaseAuditor.Audit(ContextCapsulePhase.FileRequest, "I need to inspect the repo first.");
    if (invalidCodexFileAudit.Passed || invalidCodexFileAudit.Level != CodexPhaseAuditLevel.Error)
    {
        throw new InvalidOperationException("Codex file-request audit should fail responses without usable CC request lines.");
    }

    var validCodexPatchAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        BEGIN CC-REPLACE
        FILE: Views/MainWindow.axaml
        MODE: replace_region
        NAME: send-button
        ---
        <Button Classes="cc-prompt-send red" />
        END CC-REPLACE
        """);
    if (!validCodexPatchAudit.Passed
        || validCodexPatchAudit.Level != CodexPhaseAuditLevel.Pass
        || validCodexPatchAudit.PatchBlockCount != 1)
    {
        throw new InvalidOperationException("Codex patch-write audit should pass valid CC-REPLACE blocks.");
    }

    var needMoreCodexAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        NEED_MORE_CONTEXT
        FUNCTION ViewModels/ContextControlViewModel.cs :: SendAsync
        END
        """);
    if (!needMoreCodexAudit.Passed
        || needMoreCodexAudit.Level != CodexPhaseAuditLevel.Pass
        || needMoreCodexAudit.RequestLines.Count != 1)
    {
        throw new InvalidOperationException("Codex patch-write audit should allow NEED_MORE_CONTEXT plus valid CC request lines.");
    }

    var invalidCodexPatchAudit = CodexPhaseAuditor.Audit(
        ContextCapsulePhase.PatchWrite,
        """
        BEGIN CC-REPLACE
        Views/MainWindow.axaml
        MODE: whole_file
        ---
        <Window />
        END CC-REPLACE
        """);
    if (invalidCodexPatchAudit.Passed || invalidCodexPatchAudit.Level != CodexPhaseAuditLevel.Error)
    {
        throw new InvalidOperationException("Codex patch-write audit should fail malformed CC-REPLACE blocks.");
    }

    var actionClaimAudit = CodexPhaseAuditor.Audit(ContextCapsulePhase.Chat, "I ran rg and edited the file.");
    if (!actionClaimAudit.Passed || actionClaimAudit.Level != CodexPhaseAuditLevel.Warning)
    {
        throw new InvalidOperationException("Codex audit should warn when read-only harness output claims direct repo actions.");
    }

    var fenced = new LocalLlmChatMessageViewModel(
        "assistant",
        """
        ## Result

        This keeps **bold**, _italic_, `inline code`, lists, and tables in the text surface.

        ~~~json
        {"ok": true}
        ~~~

        ````csharp
        public sealed class Demo {}
        ````
        """);
    if (fenced.Snippets.Count != 2
        || fenced.Snippets[0].Language != "json"
        || fenced.Snippets[1].Language != "csharp"
        || fenced.Parts.Count(part => part.IsSnippet) != 2)
    {
        throw new InvalidOperationException("Markdown fence parsing should support tilde and longer backtick fences.");
    }

    var rwkv = LocalLlmService.Catalog.First(model => model.Id.Equals("mollysama/rwkv-7-g1f:1.5b", StringComparison.OrdinalIgnoreCase));
    var rwkvViewModel = new LocalLlmModelViewModel(rwkv);
    rwkvViewModel.ApplyState(
        isInstalled: false,
        isAvailable: false,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: true);
    if (!rwkvViewModel.UsesOllamaPull
        || rwkvViewModel.RequiresManualBackend
        || !rwkvViewModel.CanPull
        || !rwkvViewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Ollama-packaged RWKV catalog models should stay downloadable through Ollama instead of becoming dependency-only ready.");
    }

    var tinySd = LocalLlmService.Catalog.First(model => model.Id.Equals("segmind/tiny-sd", StringComparison.OrdinalIgnoreCase));
    var tinySdViewModel = new LocalLlmModelViewModel(tinySd);
    tinySdViewModel.ApplyState(
        isInstalled: false,
        isAvailable: false,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: false);
    tinySdViewModel.ApplyBackendDependencyState(true);
    if (!tinySdViewModel.IsImageGenerationModel
        || !tinySdViewModel.RequiresManualBackend
        || !tinySdViewModel.DependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase)
        || !tinySdViewModel.CanDownloadBackendModel
        || tinySdViewModel.CanUseManualBackend
        || tinySdViewModel.IsAvailable
        || !tinySdViewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diffusers-backed image models should expose a model download action, but should not be selectable until weights are cached.");
    }

    tinySdViewModel.ApplyBackendModelState(true);
    if (!tinySdViewModel.IsBackendModelReady
        || tinySdViewModel.CanDownloadBackendModel
        || !tinySdViewModel.CanUseManualBackend
        || !tinySdViewModel.IsAvailable
        || !tinySdViewModel.PullButtonLabel.Equals("Ready", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diffusers-backed image models should become ready after their Hugging Face weights are cached.");
    }

    RequireImageDependency("nota-ai/bk-sdm-small", "diffusers");
    RequireImageDependency("SimianLuo/LCM_Dreamshaper_v7", "diffusers");
    RequireImageDependency("stabilityai/sd-turbo", "diffusers");
    RequireImageDependency("black-forest-labs/FLUX.2-klein-4B", "diffusers");
    RequireImageDependency("x/flux1-dev-q4", "stable_diffusion_cpp");
    RequireOllamaImagePlatformGate("x/flux2-klein");
    RequireOllamaImagePlatformGate("x/flux2-klein:9b");
    RequireOllamaImagePlatformGate("x/z-image-turbo");
    RequireOllamaDownload("qwen3:235b");
    RequireOllamaDownload("llama3.1:405b");
    RequireOllamaDownload("devstral-small-2:24b");
    RequireOllamaDownload("qwen2.5vl:3b");
    RequireOllamaDownload("hf.co/ggml-org/Qwen2.5-Omni-7B-GGUF:Q4_K_M");
    RequireBackendDependenciesAutoinstallable();

    var diffusersDependency = new LlmBackendDependencyViewModel(
        "diffusers",
        "Hugging Face Diffusers",
        "image generation library",
        "Python library",
        "Windows, macOS, Linux",
        "Runs local image checkpoints.",
        "Install Python plus diffusers.",
        isRequired: false,
        isRecommended: true);
    diffusersDependency.ApplyStatus(true, "Ready", "External Python", isManaged: false);
    if (diffusersDependency.CanForceInstall
        || diffusersDependency.CanUninstall
        || !diffusersDependency.InstallActionLabel.Equals("External", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("External Python dependencies should not expose destructive repair actions.");
    }

    diffusersDependency.ApplyStatus(true, "Ready", "Managed Python", isManaged: true);
    if (diffusersDependency.CanForceInstall
        || !diffusersDependency.CanUninstall
        || !diffusersDependency.InstallActionLabel.Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Managed Python dependencies should keep the normal uninstall action.");
    }

    var graphRoot = new ProjectNodeViewModel(
        "sample",
        "",
        true,
        "v1",
        [
            new ProjectNodeViewModel(
                "src",
                "src",
                true,
                "v1",
                [
                    new ProjectNodeViewModel(
                        "Controllers",
                        "src/Controllers",
                        true,
                        "v2",
                        [new ProjectNodeViewModel("HomeController.cs", "src/Controllers/HomeController.cs", false, "v3", loc: 31)],
                        fileCount: 1,
                        diskFileCount: 1),
                    new ProjectNodeViewModel("App.config", "src/App.config", false, "v2", loc: 8),
                    new ProjectNodeViewModel("Program.cs", "src/Program.cs", false, "v2", loc: 42)
                ],
                fileCount: 3,
                diskFileCount: 3),
            new ProjectNodeViewModel("README.md", "README.md", false, "v1", loc: 12)
        ],
        fileCount: 4,
        diskFileCount: 4);
    var graph = new ProjectGraphRenderControl
    {
        Items = [graphRoot]
    };
    var exportDetails = new ProjectGraphExportDetails(
        root,
        ".NET | 2/2 visible files match current rules | 3 scanned for setup",
        "2 allowed | 2 LOC | 0 skipped types | 0 skipped folders",
        [
            new ProjectStackSection("Detected Stack", [".NET: C# files"]),
            new ProjectStackSection("Uses", ["Build tool: dotnet"]),
            new ProjectStackSection("Languages", ["C#: 1 files", "Markdown: 1 files"]),
            new ProjectStackSection("Top File Types", [".cs: 1", ".md: 1"]),
            new ProjectStackSection("Autosetup Plan", ["No missing stack rules detected"])
        ]);

    var dot = graph.ExportGraphText("dot", exportDetails);
    RequireTextContains(dot, "digraph ProjectGraph");
    RequireTextContains(dot, "n0 [label=");
    RequireTextContains(dot, "n0 -> n1");
    RequireTextContains(dot, "Project Details");

    var graphMl = graph.ExportGraphText("graphml", exportDetails);
    RequireTextContains(graphMl, "<graph id=\"ProjectGraph\" edgedefault=\"directed\">");
    RequireTextContains(graphMl, "<data key=\"parent\">n0</data>");
    RequireTextContains(graphMl, "<data key=\"projectDetails\">");

    var mermaid = graph.ExportGraphText("mmd", exportDetails);
    RequireTextContains(mermaid, "graph TD");
    RequireTextContains(mermaid, "n0[\"");
    RequireTextContains(mermaid, "n0 --> n1");
    RequireTextContains(mermaid, "%% Project Details");

    var json = JsonNode.Parse(graph.ExportGraphText("json", exportDetails))!.AsObject();
    if (json["graph"]?["roots"]?.AsArray().FirstOrDefault()?.GetValue<string>() != "n0"
        || json["nodes"]?.AsArray().Count < 4
        || json["projectDetails"]?["sections"]?.AsArray().Count < 10)
    {
        throw new InvalidOperationException("Graph JSON export should preserve roots, full node declarations, and scanner details.");
    }

    var graphNodes = json["nodes"]!.AsArray().Select(node => node!.AsObject()).ToArray();
    var controllersY = NodeNumberByPath(graphNodes, "src/Controllers", "y");
    if (NodeNumberByPath(graphNodes, "src/App.config", "y") >= controllersY
        || NodeNumberByPath(graphNodes, "src/Program.cs", "y") >= controllersY)
    {
        throw new InvalidOperationException("Generation-local files should be laid out above directories that continue the graph.");
    }

    Console.WriteLine("ContextFileResolver smoke tests passed.");
}
finally
{
    try
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
    catch
    {
        // Best-effort cleanup only; temp leftovers must not fail resolver checks.
    }
}

void WriteFile(string relativePath, string text)
{
    var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, text);
}

static void RequireContains(ContextFileResolveResult result, string requestLine)
{
    if (!result.RequestLines.Contains(requestLine, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected resolver output to contain '{requestLine}'. Actual: {result.RequestText}");
    }
}

static void RequireNotContains(ContextFileResolveResult result, string requestLine)
{
    if (result.RequestLines.Contains(requestLine, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Resolver output unexpectedly contained '{requestLine}'. Actual: {result.RequestText}");
    }
}

static void RequireTextContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text to contain '{expected}'. Actual: {text}");
    }
}

static bool HasAdjacentArguments(IReadOnlyList<string> arguments, string name, string value)
{
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (arguments[index].Equals(name, StringComparison.Ordinal)
            && arguments[index + 1].Equals(value, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

static double NodeNumberByPath(IReadOnlyList<JsonObject> nodes, string path, string propertyName)
{
    var node = nodes.FirstOrDefault(item => string.Equals(item["path"]?.GetValue<string>(), path, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Expected graph JSON to contain node path '{path}'.");
    return node[propertyName]?.GetValue<double>()
        ?? throw new InvalidOperationException($"Expected graph JSON node '{path}' to contain numeric '{propertyName}'.");
}

static LocalLlmModelViewModel ModelView(string modelId)
{
    var model = LocalLlmService.Catalog.First(item => item.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    var viewModel = new LocalLlmModelViewModel(model);
    viewModel.ApplyState(
        isInstalled: false,
        isAvailable: false,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: false);
    return viewModel;
}

static void RequireImageDependency(string modelId, string dependencyId)
{
    var viewModel = ModelView(modelId);
    if (!viewModel.IsImageGenerationModel
        || !viewModel.RequiresManualBackend
        || !viewModel.DependencyId.Equals(dependencyId, StringComparison.OrdinalIgnoreCase)
        || !viewModel.CanInstallDependency
        || !viewModel.PullButtonLabel.Equals("Install dep", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should install the {dependencyId} image backend, not try an Ollama model pull.");
    }
}

static void RequireOllamaDownload(string modelId)
{
    var viewModel = ModelView(modelId);
    if (!viewModel.UsesOllamaPull
        || viewModel.RequiresManualBackend
        || !viewModel.CanPull
        || !viewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should remain a one-click Ollama download.");
    }
}

static void RequireOllamaImagePlatformGate(string modelId)
{
    var viewModel = ModelView(modelId);
    if (!viewModel.IsImageGenerationModel
        || !viewModel.IsOllamaImageRoute
        || !viewModel.UsesOllamaPull)
    {
        throw new InvalidOperationException($"{modelId} should remain classified as an Ollama image-generation route.");
    }

    if (OperatingSystem.IsMacOS())
    {
        if (!viewModel.IsBackendPlatformSupported
            || !viewModel.CanPull
            || !viewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{modelId} should stay downloadable on macOS where Ollama image generation is supported.");
        }

        return;
    }

    if (viewModel.IsBackendPlatformSupported
        || viewModel.CanPull
        || !viewModel.InstallLabel.Equals("Mac only", StringComparison.OrdinalIgnoreCase)
        || !viewModel.PullButtonLabel.Equals("Mac only", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should be disabled on non-macOS hosts to avoid Ollama HTTP 500/EOF image-generation failures.");
    }

    viewModel.ApplyState(
        isInstalled: true,
        isAvailable: true,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: true);
    if (!viewModel.CanUninstall
        || !viewModel.PullButtonLabel.Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should still be removable when it was already pulled on an unsupported host.");
    }
}

static void RequireBackendDependenciesAutoinstallable()
{
    string[] dependencyIds =
    [
        "ollama",
        "llama_cpp_server",
        "lm_studio",
        "koboldcpp",
        "mlx_lm",
        "mlc_llm",
        "transformers",
        "diffusers",
        "stable_diffusion_cpp",
        "vllm",
        "sglang",
        "onnxruntime_genai",
        "openvino_genai",
        "tensorrt_llm",
        "exllamav2_tabbyapi",
        "bitnet_cpp",
        "rwkv_runner"
    ];

    var missing = dependencyIds
        .Select(CreateDependency)
        .Where(dependency => !dependency.HasSafeAutomaticInstaller)
        .Select(dependency => dependency.Id)
        .ToArray();

    if (missing.Length > 0)
    {
        throw new InvalidOperationException($"Dependencies should expose one-click installers on this platform: {string.Join(", ", missing)}");
    }
}

static LlmBackendDependencyViewModel CreateDependency(string id)
{
    return new LlmBackendDependencyViewModel(
        id,
        id,
        "test",
        "test",
        "test",
        "test",
        "test",
        isRequired: false,
        isRecommended: false);
}
