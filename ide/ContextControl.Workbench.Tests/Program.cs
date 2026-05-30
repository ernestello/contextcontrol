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
        || !tinySdViewModel.CanUseManualBackend
        || !tinySdViewModel.CanDownloadBackendModel
        || !tinySdViewModel.IsAvailable
        || !tinySdViewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diffusers-backed image models should expose a model download action once the Diffusers backend dependency is ready.");
    }

    tinySdViewModel.ApplyBackendModelState(true);
    if (!tinySdViewModel.IsBackendModelReady
        || tinySdViewModel.CanDownloadBackendModel
        || !tinySdViewModel.PullButtonLabel.Equals("Ready", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Diffusers-backed image models should become ready after their Hugging Face weights are cached.");
    }

    RequireImageDependency("nota-ai/bk-sdm-small", "diffusers");
    RequireImageDependency("SimianLuo/LCM_Dreamshaper_v7", "diffusers");
    RequireImageDependency("stabilityai/sd-turbo", "diffusers");
    RequireImageDependency("x/flux1-dev-q4", "stable_diffusion_cpp");
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
    if (!diffusersDependency.CanForceInstall
        || diffusersDependency.CanUninstall
        || !diffusersDependency.InstallActionLabel.Equals("Force install", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("External Python dependencies should expose a confirmed force-install action instead of a dead External action.");
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
