// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static class ProjectStackScanner
{
    private const int MaxDepth = 20;
    private const int MaxDirectories = 8000;
    private const int MaxFiles = 50000;
    private const long MaxManifestReadBytes = 512 * 1024;
    private const long MaxTextSignalReadBytes = 160 * 1024;
    private const int MaxTextSignalFiles = 3000;

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly EnumerationOptions SafeEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false
    };
    private static readonly HashSet<string> AlwaysSkippedDirectories = new(NameComparer)
    {
        ".bundle",
        ".cache",
        ".ccReplace.versions",
        ".ccWorkbench.browser-data",
        ".angular",
        ".bloop",
        ".claude",
        ".conan",
        ".conan2",
        ".codex",
        ".cursor",
        ".dart_tool",
        ".expo",
        ".git",
        ".godot",
        ".gptReplace.versions",
        ".gradle",
        ".idea",
        ".import",
        ".metals",
        ".m2",
        ".mypy_cache",
        ".next",
        ".nox",
        ".opam",
        ".nuxt",
        ".nuget",
        ".parcel-cache",
        ".pnpm-store",
        ".pytest_cache",
        ".ruff_cache",
        ".serverless",
        ".svelte-kit",
        ".terraform",
        ".tmp",
        ".tox",
        ".turbo",
        ".venv",
        ".vs",
        ".vscode",
        ".yarn",
        ".zig-cache",
        "__pycache__",
        "_build",
        "_deps",
        "_opam",
        "bazel-bin",
        "bazel-out",
        "bazel-testlogs",
        "bin",
        "bower_components",
        "build",
        "build-debug",
        "build-release",
        "cmake-build-debug",
        "cmake-build-release",
        "CMakeFiles",
        "coverage",
        "DerivedData",
        "deps",
        "dist",
        "elm-stuff",
        "external",
        "extern",
        "node_modules",
        "obj",
        "out",
        "packages",
        "Pods",
        "target",
        "third_party",
        "thirdparty",
        "vendor",
        "vcpkg_installed",
        "venv",
        "zig-out"
    };
    private static readonly HashSet<string> TopLevelBuildConfigurationDirectories = new(NameComparer)
    {
        "Debug",
        "MinSizeRel",
        "Release",
        "RelWithDebInfo",
        "x64"
    };
    private static readonly string[] AutoSetupIgnoredFileNames =
    [
        ".ccFileRules.json",
        ".ccReplace.settings.auto.json",
        ".ccReplace.settings.json",
        ".ccWorkbench.settings.json",
        ".DS_Store",
        "desktop.ini",
        "Thumbs.db"
    ];
    private static readonly string[] AutoSetupIgnoredExtensions =
    [
        ".a",
        ".bak",
        ".bin",
        ".bmp",
        ".cache",
        ".collision",
        ".db",
        ".dds",
        ".dll",
        ".dylib",
        ".exe",
        ".exp",
        ".fc",
        ".fif",
        ".flac",
        ".gz",
        ".ico",
        ".ilk",
        ".import",
        ".ipch",
        ".jar",
        ".jpeg",
        ".jpg",
        ".lastbuildstate",
        ".lib",
        ".log",
        ".mp3",
        ".o",
        ".obj",
        ".ogg",
        ".opendb",
        ".pdb",
        ".pdf",
        ".png",
        ".pyc",
        ".pyo",
        ".rar",
        ".sdf",
        ".snapshot",
        ".so",
        ".spv",
        ".svo",
        ".tga",
        ".tlog",
        ".tmp",
        ".ttf",
        ".uid",
        ".unsuccessfulbuild",
        ".wasm",
        ".wav",
        ".webp",
        ".zip"
    ];

    private static readonly Dictionary<string, string> LanguageByExtension = new(NameComparer)
    {
        [".asm"] = "Assembly",
        [".astro"] = "Astro",
        [".axaml"] = "Avalonia XAML",
        [".bash"] = "Shell",
        [".bat"] = "Batch",
        [".bzl"] = "Starlark",
        [".c"] = "C",
        [".cc"] = "C++",
        [".cjs"] = "JavaScript",
        [".clj"] = "Clojure",
        [".cljc"] = "Clojure",
        [".cljs"] = "ClojureScript",
        [".coffee"] = "CoffeeScript",
        [".cpp"] = "C++",
        [".cs"] = "C#",
        [".cshtml"] = "Razor",
        [".cts"] = "TypeScript",
        [".cmd"] = "Batch",
        [".css"] = "CSS",
        [".cu"] = "CUDA",
        [".cuh"] = "CUDA",
        [".cxx"] = "C++",
        [".dart"] = "Dart",
        [".elm"] = "Elm",
        [".erl"] = "Erlang",
        [".ex"] = "Elixir",
        [".exs"] = "Elixir",
        [".fs"] = "F#",
        [".fsi"] = "F#",
        [".fsx"] = "F#",
        [".gd"] = "GDScript",
        [".go"] = "Go",
        [".gradle"] = "Gradle",
        [".groovy"] = "Groovy",
        [".h"] = "C/C++ header",
        [".hcl"] = "HCL",
        [".hh"] = "C++ header",
        [".hpp"] = "C++ header",
        [".hs"] = "Haskell",
        [".hrl"] = "Erlang",
        [".html"] = "HTML",
        [".hxx"] = "C++ header",
        [".java"] = "Java",
        [".jl"] = "Julia",
        [".js"] = "JavaScript",
        [".jsx"] = "React JSX",
        [".jsonnet"] = "Jsonnet",
        [".kt"] = "Kotlin",
        [".kts"] = "Kotlin",
        [".lhs"] = "Haskell",
        [".lua"] = "Lua",
        [".m"] = "Objective-C",
        [".mjs"] = "JavaScript",
        [".ml"] = "OCaml",
        [".mli"] = "OCaml",
        [".mm"] = "Objective-C++",
        [".mts"] = "TypeScript",
        [".nasm"] = "Assembly",
        [".nim"] = "Nim",
        [".pas"] = "Pascal",
        [".php"] = "PHP",
        [".pl"] = "Perl",
        [".pm"] = "Perl",
        [".ps1"] = "PowerShell",
        [".psd1"] = "PowerShell",
        [".psm1"] = "PowerShell",
        [".py"] = "Python",
        [".r"] = "R",
        [".raku"] = "Raku",
        [".razor"] = "Razor",
        [".rb"] = "Ruby",
        [".rmd"] = "R Markdown",
        [".rs"] = "Rust",
        [".s"] = "Assembly",
        [".sbt"] = "Scala",
        [".sc"] = "Scala",
        [".scala"] = "Scala",
        [".scss"] = "SCSS",
        [".sh"] = "Shell",
        [".sql"] = "SQL",
        [".svelte"] = "Svelte",
        [".swift"] = "Swift",
        [".tf"] = "Terraform",
        [".tfvars"] = "Terraform",
        [".ts"] = "TypeScript",
        [".tsx"] = "React TSX",
        [".vb"] = "VB.NET",
        [".vue"] = "Vue",
        [".xaml"] = "XAML",
        [".zig"] = "Zig",
        [".zon"] = "Zig",
        [".zsh"] = "Shell"
    };

    private static readonly Dictionary<string, string[]> SuggestedSupportedByStack = new(NameComparer)
    {
        [".NET"] = [".cs", ".csproj", ".fs", ".fsproj", ".props", ".sln", ".targets", ".vb", ".vbproj", ".json", ".xml"],
        [".NET MAUI"] = [".cs", ".csproj", ".json", ".props", ".sln", ".targets", ".xaml", ".xml"],
        ["Angular"] = [".ts", ".html", ".css", ".scss", ".json", ".yaml", ".yml"],
        ["Assembly"] = [".asm", ".s", ".nasm", ".inc"],
        ["ASP.NET Core"] = [".cs", ".cshtml", ".razor", ".csproj", ".json", ".css", ".html", ".js"],
        ["Astro"] = [".astro", ".ts", ".tsx", ".js", ".jsx", ".json", ".css", ".md"],
        ["Avalonia UI"] = [".axaml", ".cs", ".csproj", ".json"],
        ["Batch"] = [".bat", ".cmd"],
        ["Bazel"] = [".bzl"],
        ["C/C++"] = [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx", ".cmake", ".txt"],
        ["Clojure"] = [".clj", ".cljs", ".cljc", ".edn"],
        ["CoffeeScript"] = [".coffee", ".js", ".json"],
        ["CUDA"] = [".cu", ".cuh", ".c", ".cc", ".cpp", ".h", ".hpp", ".cmake"],
        ["Dart"] = [".dart", ".yaml", ".yml"],
        ["Deno"] = [".ts", ".tsx", ".js", ".jsx", ".json", ".jsonc", ".css", ".html", ".md"],
        ["Docker"] = [".dockerignore", ".yaml", ".yml"],
        ["Electron"] = [".ts", ".tsx", ".js", ".jsx", ".json", ".html", ".css"],
        ["Elixir"] = [".ex", ".exs"],
        ["Elm"] = [".elm", ".json"],
        ["Erlang"] = [".erl", ".hrl", ".config", ".lock"],
        ["F#"] = [".fs", ".fsi", ".fsx", ".fsproj", ".sln", ".props", ".targets", ".json"],
        ["Flutter"] = [".dart", ".yaml", ".yml"],
        ["Go"] = [".go", ".mod", ".sum"],
        ["Godot"] = [".gd", ".tscn", ".tres", ".godot", ".cfg"],
        ["Groovy"] = [".groovy", ".gradle", ".json", ".yaml", ".yml"],
        ["Haskell"] = [".hs", ".lhs", ".cabal", ".yaml", ".yml"],
        ["Java"] = [".java", ".gradle", ".kts", ".xml", ".properties"],
        ["JavaScript"] = [".js", ".jsx", ".mjs", ".cjs", ".json", ".css", ".html", ".md", ".yaml", ".yml"],
        ["Julia"] = [".jl", ".toml"],
        ["Kotlin"] = [".kt", ".kts", ".gradle", ".xml", ".properties"],
        ["Lua"] = [".lua", ".rockspec"],
        ["Next.js"] = [".ts", ".tsx", ".js", ".jsx", ".json", ".css", ".scss", ".html", ".md"],
        ["Nim"] = [".nim", ".nimble"],
        ["Node.js"] = [".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".json", ".css", ".html", ".md", ".yaml", ".yml"],
        ["Nuxt"] = [".vue", ".ts", ".js", ".json", ".css", ".scss", ".md"],
        ["Objective-C"] = [".m", ".mm", ".h", ".hpp", ".swift", ".json", ".plist"],
        ["OCaml"] = [".ml", ".mli", ".opam"],
        ["Pascal"] = [".pas"],
        ["Perl"] = [".pl", ".pm"],
        ["PHP"] = [".php", ".json"],
        ["Python"] = [".py", ".toml", ".txt", ".ini", ".cfg", ".yaml", ".yml"],
        ["R"] = [".r", ".rmd", ".rproj", ".lock"],
        ["Raku"] = [".raku"],
        ["React"] = [".tsx", ".ts", ".jsx", ".js", ".json", ".css", ".html", ".md"],
        ["Ruby"] = [".rb", ".gemspec", ".lock", ".yml", ".yaml"],
        ["Rust"] = [".rs", ".toml", ".lock"],
        ["Scala"] = [".scala", ".sc", ".sbt", ".xml", ".properties"],
        ["Shell"] = [".sh", ".bash", ".zsh"],
        ["SQL"] = [".sql"],
        ["Swift"] = [".swift", ".json"],
        ["Terraform"] = [".tf", ".tfvars", ".hcl"],
        ["TypeScript"] = [".ts", ".tsx", ".js", ".jsx", ".json"],
        ["Unity"] = [".cs", ".asmdef", ".shader", ".json"],
        ["Vite"] = [".ts", ".tsx", ".js", ".jsx", ".vue", ".svelte", ".json", ".css", ".html"],
        ["Vue"] = [".vue", ".ts", ".js", ".json", ".css"],
        ["Xamarin"] = [".cs", ".csproj", ".json", ".props", ".sln", ".targets", ".xaml", ".xml"],
        ["Svelte"] = [".svelte", ".ts", ".js", ".json", ".css"],
        ["PowerShell"] = [".ps1", ".psm1", ".psd1"],
        ["Zig"] = [".zig", ".zon"]
    };

    private static readonly Dictionary<string, string[]> SuggestedLocByStack = new(NameComparer)
    {
        [".NET"] = [".cs", ".fs", ".vb"],
        [".NET MAUI"] = [".cs", ".xaml"],
        ["Angular"] = [".ts"],
        ["Assembly"] = [".asm", ".s", ".nasm", ".inc"],
        ["ASP.NET Core"] = [".cs", ".cshtml", ".razor"],
        ["Astro"] = [".astro", ".ts", ".js"],
        ["Avalonia UI"] = [".axaml", ".cs"],
        ["Batch"] = [".bat", ".cmd"],
        ["Bazel"] = [".bzl"],
        ["C/C++"] = [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"],
        ["Clojure"] = [".clj", ".cljs", ".cljc"],
        ["CoffeeScript"] = [".coffee"],
        ["CUDA"] = [".cu", ".cuh"],
        ["Dart"] = [".dart"],
        ["Deno"] = [".ts", ".tsx", ".js", ".jsx"],
        ["Electron"] = [".ts", ".tsx", ".js", ".jsx"],
        ["Elixir"] = [".ex", ".exs"],
        ["Elm"] = [".elm"],
        ["Erlang"] = [".erl", ".hrl"],
        ["F#"] = [".fs", ".fsi", ".fsx"],
        ["Flutter"] = [".dart"],
        ["Go"] = [".go"],
        ["Godot"] = [".gd"],
        ["Groovy"] = [".groovy", ".gradle"],
        ["Haskell"] = [".hs", ".lhs"],
        ["Java"] = [".java"],
        ["JavaScript"] = [".js", ".jsx", ".mjs", ".cjs"],
        ["Julia"] = [".jl"],
        ["Kotlin"] = [".kt", ".kts"],
        ["Lua"] = [".lua"],
        ["Next.js"] = [".ts", ".tsx", ".js", ".jsx"],
        ["Nim"] = [".nim"],
        ["Node.js"] = [".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs"],
        ["Nuxt"] = [".vue", ".ts", ".js"],
        ["Objective-C"] = [".m", ".mm", ".h"],
        ["OCaml"] = [".ml", ".mli"],
        ["Pascal"] = [".pas"],
        ["Perl"] = [".pl", ".pm"],
        ["PHP"] = [".php"],
        ["Python"] = [".py"],
        ["R"] = [".r", ".rmd"],
        ["Raku"] = [".raku"],
        ["React"] = [".tsx", ".ts", ".jsx", ".js"],
        ["Ruby"] = [".rb"],
        ["Rust"] = [".rs"],
        ["Scala"] = [".scala", ".sc", ".sbt"],
        ["Shell"] = [".sh", ".bash", ".zsh"],
        ["SQL"] = [".sql"],
        ["Swift"] = [".swift"],
        ["Svelte"] = [".svelte", ".ts", ".js"],
        ["Terraform"] = [".tf", ".tfvars", ".hcl"],
        ["TypeScript"] = [".ts", ".tsx"],
        ["Unity"] = [".cs"],
        ["Vite"] = [".ts", ".tsx", ".js", ".jsx", ".vue", ".svelte"],
        ["Vue"] = [".vue", ".ts", ".js"],
        ["Xamarin"] = [".cs", ".xaml"],
        ["PowerShell"] = [".ps1", ".psm1", ".psd1"],
        ["Zig"] = [".zig"]
    };

    private static readonly Dictionary<string, string[]> SuggestedSkippedDirectoriesByStack = new(NameComparer)
    {
        [".NET"] = ["bin", "obj", ".vs", "TestResults"],
        [".NET MAUI"] = ["bin", "obj", ".vs", "TestResults"],
        ["Angular"] = ["node_modules", "dist", "build", "coverage", ".angular"],
        ["ASP.NET Core"] = ["bin", "obj", ".vs", "TestResults", "wwwroot/lib"],
        ["Astro"] = ["node_modules", "dist", "build", ".astro"],
        ["Bazel"] = ["bazel-bin", "bazel-out", "bazel-testlogs"],
        ["C/C++"] = ["build", "cmake-build-debug", "cmake-build-release", "CMakeFiles"],
        ["Clojure"] = ["target", ".cpcache"],
        ["CUDA"] = ["build", "cmake-build-debug", "cmake-build-release", "CMakeFiles"],
        ["Dart"] = [".dart_tool", "build"],
        ["Deno"] = [".deno", "dist", "coverage"],
        ["Docker"] = [],
        ["Electron"] = ["node_modules", "dist", "build", "out"],
        ["Elixir"] = ["_build", "deps"],
        ["Elm"] = ["elm-stuff"],
        ["Erlang"] = ["_build", "deps"],
        ["Flutter"] = [".dart_tool", "build"],
        ["Go"] = ["vendor"],
        ["Godot"] = [".godot", ".import"],
        ["Haskell"] = [".stack-work", "dist", "dist-newstyle"],
        ["Java"] = [".gradle", "build", "target", "out"],
        ["JavaScript"] = ["node_modules", "dist", "build", "coverage"],
        ["Julia"] = [".julia"],
        ["Kotlin"] = [".gradle", "build", "target", "out"],
        ["Next.js"] = ["node_modules", ".next", "out", "dist", "build", "coverage"],
        ["Nim"] = ["nimcache"],
        ["Node.js"] = ["node_modules", "dist", "build", "coverage", ".next", ".nuxt", ".svelte-kit", ".turbo"],
        ["Nuxt"] = ["node_modules", ".nuxt", "dist", "build", "coverage"],
        ["OCaml"] = ["_build", "_opam"],
        ["PHP"] = ["vendor"],
        ["Python"] = [".venv", "venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache"],
        ["R"] = ["renv/library", "packrat/lib"],
        ["React"] = ["node_modules", "dist", "build", "coverage"],
        ["Ruby"] = [".bundle", "vendor/bundle", "tmp"],
        ["Rust"] = ["target"],
        ["Scala"] = ["target", ".bloop", ".metals"],
        ["Swift"] = [".build", "DerivedData", "Pods"],
        ["Terraform"] = [".terraform"],
        ["Vite"] = ["node_modules", "dist", "build", "coverage"],
        ["Xamarin"] = ["bin", "obj", ".vs", "TestResults"],
        ["Zig"] = [".zig-cache", "zig-out"],
        ["Unity"] = ["Library", "Temp", "Obj", "Build", "Builds", "Logs"]
    };

    private static readonly HashSet<string> SourceOrConfigSuggestionExtensions = BuildSourceOrConfigSuggestionExtensions();
    private static readonly HashSet<string> TextSignalExtensions = new(NameComparer)
    {
        ".c",
        ".cc",
        ".cmake",
        ".cpp",
        ".cs",
        ".cxx",
        ".dart",
        ".bzl",
        ".ex",
        ".exs",
        ".fs",
        ".fsi",
        ".fsx",
        ".go",
        ".gradle",
        ".groovy",
        ".h",
        ".hh",
        ".hpp",
        ".hs",
        ".hxx",
        ".java",
        ".jl",
        ".js",
        ".jsx",
        ".kt",
        ".kts",
        ".lua",
        ".m",
        ".mjs",
        ".ml",
        ".mli",
        ".mm",
        ".mts",
        ".nim",
        ".php",
        ".pl",
        ".pm",
        ".ps1",
        ".py",
        ".r",
        ".rb",
        ".rs",
        ".scala",
        ".sh",
        ".toml",
        ".ts",
        ".tsx",
        ".tf",
        ".tfvars",
        ".vb",
        ".vue",
        ".zig",
        ".yaml",
        ".yml"
    };
    private static readonly TechnologyPattern[] TextUsePatterns =
    [
        new("Vulkan", ["<vulkan/vulkan.h>", "Vulkan::Vulkan", "VK_", "VkInstance", "VkDevice", "VkCommandBuffer"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx", ".m", ".mm"]),
        new("GLFW", ["<GLFW/glfw3.h>", "GLFWwindow", "glfwInit"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("GLM", ["<glm/", "glm::"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("Dear ImGui", ["<imgui.h>", "ImGui::", "imgui_impl", "imgui_impl_vulkan"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("Jolt Physics", ["<Jolt/", "JPH::"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("EnTT", ["<entt/", "entt::"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("STB", ["stb_image", "stb_image.h", "stb_truetype"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("spdlog", ["<spdlog/", "spdlog::"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("fmt", ["<fmt/", "fmt::"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("OpenGL", ["<GL/gl", "<glad/", "gladLoadGL", "glewInit"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("SDL", ["<SDL.h>", "<SDL2/", "SDL_Init", "SDL_Window"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("Direct3D 12", ["d3d12.h", "ID3D12"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("Direct3D 11", ["d3d11.h", "ID3D11"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"]),
        new("Metal", ["<Metal/Metal.h>", "MTLDevice", "CAMetalLayer"], [".m", ".mm", ".h", ".hpp"]),
        new("CUDA", ["cuda_runtime.h", "__global__", "cudaMalloc"], [".cu", ".cuh", ".c", ".cc", ".cpp", ".cxx", ".h", ".hpp"]),
        new("OpenCV", ["<opencv2/", "cv::Mat", "import cv2"], [".c", ".cc", ".cpp", ".cxx", ".h", ".hpp", ".py"]),
        new("PyTorch", ["import torch", "from torch", "torch::"], [".py", ".c", ".cc", ".cpp", ".cxx", ".h", ".hpp"]),
        new("TensorFlow", ["import tensorflow", "from tensorflow", "tensorflow/core"], [".py", ".c", ".cc", ".cpp", ".cxx", ".h", ".hpp"]),
        new("React", ["from 'react'", "from \"react\"", "ReactDOM"], [".js", ".jsx", ".ts", ".tsx"]),
        new("Vue", ["from 'vue'", "from \"vue\"", "createApp("], [".js", ".jsx", ".ts", ".tsx", ".vue"]),
        new("Svelte", ["from 'svelte", "@sveltejs/"], [".js", ".jsx", ".ts", ".tsx", ".svelte"])
    ];

    public static Task<ProjectStackScanResult> ScanAsync(string projectRoot, ProjectFileRules rules)
    {
        return Task.Run(() => Scan(projectRoot, rules));
    }

    public static ProjectStackScanResult Scan(string projectRoot, ProjectFileRules rules)
    {
        var root = new DirectoryInfo(projectRoot);
        if (!root.Exists)
        {
            return new ProjectStackScanResult(
                "Project folder is missing.",
                $"Project folder is missing: {projectRoot}",
                projectRoot,
                "Missing project",
                "No rules loaded",
                "No scan completed",
                [],
                [],
                ProjectStackRuleSet.Empty());
        }

        var state = new ScanState(root.FullName, rules);
        ScanDirectory(root, root.FullName, 0, rules, state, hiddenByCurrentRules: false);
        AddPostScanStackSignals(state);
        return BuildResult(state);
    }

    private static void ScanDirectory(
        DirectoryInfo directory,
        string rootPath,
        int depth,
        ProjectFileRules rules,
        ScanState state,
        bool hiddenByCurrentRules)
    {
        if (state.DirectoriesVisited >= MaxDirectories)
        {
            state.LimitHit = true;
            return;
        }

        state.DirectoriesVisited++;

        var relativePath = depth == 0
            ? ""
            : NormalizePath(Path.GetRelativePath(rootPath, directory.FullName));
        var childHiddenByCurrentRules = hiddenByCurrentRules;
        if (depth > 0 && TryGetExcludedDirectoryRule(directory, relativePath, out var excludedRule, out var excludedReason))
        {
            state.DirectoriesExcluded++;
            state.AutoSkippedDirectoryRules.Add(excludedRule);
            AddExcludedDirectoryUse(state, directory, relativePath, excludedReason);
            AddSample(state.SkippedDirectorySamples, $"{relativePath} ({excludedReason})");
            return;
        }

        if (depth > 0 && rules.ShouldSkipDirectory(directory.Name, relativePath))
        {
            childHiddenByCurrentRules = true;
            state.DirectoriesSkippedByRules++;
            AddSample(state.SkippedDirectorySamples, $"{relativePath} (current rules)");
        }

        if (depth >= MaxDepth)
        {
            state.LimitHit = true;
            return;
        }

        foreach (var childDirectory in EnumerateDirectories(directory))
        {
            ScanDirectory(childDirectory, rootPath, depth + 1, rules, state, childHiddenByCurrentRules);
            if (state.LimitHit)
            {
                return;
            }
        }

        foreach (var file in EnumerateFiles(directory))
        {
            ScanFile(file, rootPath, rules, state, childHiddenByCurrentRules);
            if (state.LimitHit)
            {
                return;
            }
        }
    }

    private static void ScanFile(
        FileInfo file,
        string rootPath,
        ProjectFileRules rules,
        ScanState state,
        bool hiddenByCurrentRules)
    {
        if (state.FilesSeen >= MaxFiles)
        {
            state.LimitHit = true;
            return;
        }

        state.FilesSeen++;
        var relativePath = NormalizePath(Path.GetRelativePath(rootPath, file.FullName));
        DetectManifestSignals(file, relativePath, state);

        var extension = NormalizeExtension(file.Extension);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            Increment(state.ExtensionCounts, extension);
            if (LanguageByExtension.TryGetValue(extension, out var language))
            {
                Increment(state.LanguageCounts, language);
            }
        }

        DetectFileSignals(file, relativePath, state);
        DetectTextUseSignals(file, relativePath, extension, state);

        var visibility = hiddenByCurrentRules
            ? ProjectFileVisibilityDecision.Skip("ignored directory")
            : rules.GetVisibilityDecision(relativePath, file.Name, file.Extension);
        if (!visibility.ShouldShow)
        {
            state.FilesSkippedByRules++;
            AddSample(state.SkippedFileSamples, $"{relativePath} ({visibility.IgnoredReason})");
            return;
        }

        state.VisibleFiles++;

        if (rules.ShouldTrackFile(relativePath, file.Name, file.Extension))
        {
            state.TrackedFiles++;
        }
        else
        {
            state.UnsupportedVisibleFiles++;
            Increment(state.UnsupportedExtensionCounts, string.IsNullOrWhiteSpace(extension) ? "(no extension)" : extension);
        }
    }

    private static void DetectManifestSignals(FileInfo file, string relativePath, ScanState state)
    {
        var lowerName = file.Name.ToLowerInvariant();
        var extension = NormalizeExtension(file.Extension);

        if (string.Equals(lowerName, "package.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Node.js", relativePath);
            AddUse(state, "Package manager: npm", relativePath);
            DetectPackageJson(file, state);
            return;
        }

        if (lowerName is "package-lock.json" or "npm-shrinkwrap.json")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: npm", relativePath);
            return;
        }

        if (lowerName is "pnpm-lock.yaml" or "pnpm-lock.yml")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: pnpm", relativePath);
            return;
        }

        if (lowerName is "yarn.lock")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: Yarn", relativePath);
            return;
        }

        if (lowerName is "bun.lockb" or "bun.lock")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: Bun", relativePath);
            return;
        }

        if (lowerName is "deno.json" or "deno.jsonc" or "deno.lock" or "jsr.json" or "jsr.jsonc")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Deno", relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddUse(state, lowerName.StartsWith("jsr.", StringComparison.Ordinal) ? "Package manager: JSR" : "Runtime: Deno", relativePath);
            DetectDenoManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "tsconfig.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddUse(state, "TypeScript", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "next.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Next.js", relativePath);
            AddStack(state, "React", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "vite.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Vite", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "svelte.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Svelte", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "astro.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Astro", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "nuxt.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Nuxt", relativePath);
            AddStack(state, "Vue", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (string.Equals(lowerName, "angular.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Angular", relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (extension is ".csproj" or ".fsproj" or ".vbproj")
        {
            AddManifest(state, relativePath);
            AddUse(state, "Package manager: NuGet", relativePath);
            DetectDotNetProject(file, relativePath, state);
            return;
        }

        if (extension is ".sln" or ".slnx"
            || string.Equals(lowerName, "packages.config", StringComparison.Ordinal)
            || string.Equals(lowerName, "global.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.props", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.targets", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.packages.props", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, ".NET", relativePath);
            AddUse(state, ".NET", relativePath);
            if (string.Equals(lowerName, "packages.config", StringComparison.Ordinal))
            {
                AddUse(state, "Package manager: NuGet", relativePath);
                DetectDotNetPackagesConfig(file, state);
            }

            DetectDotNetPackageProps(file, state);
            return;
        }

        if (string.Equals(lowerName, "pyproject.toml", StringComparison.Ordinal)
            || IsRequirementsFileName(lowerName)
            || string.Equals(lowerName, "poetry.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "pdm.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "uv.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "pipfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.py", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.cfg", StringComparison.Ordinal)
            || string.Equals(lowerName, "tox.ini", StringComparison.Ordinal)
            || string.Equals(lowerName, "environment.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "environment.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "conda.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "conda.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Python", relativePath);
            AddUse(state, "Package manager: Python", relativePath);
            DetectPythonManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "cargo.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "cargo.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Rust", relativePath);
            AddUse(state, "Package manager: Cargo", relativePath);
            DetectCargoManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "go.mod", StringComparison.Ordinal)
            || string.Equals(lowerName, "go.sum", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Go", relativePath);
            AddUse(state, "Package manager: Go modules", relativePath);
            DetectGoManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "pom.xml", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle.kts", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle.kts", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.sbt", StringComparison.Ordinal)
            || string.Equals(lowerName, "ivy.xml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            if (lowerName.EndsWith(".sbt", StringComparison.Ordinal))
            {
                AddStack(state, "Scala", relativePath);
                AddUse(state, "Build tool: sbt", relativePath);
                DetectScalaManifest(file, state);
                return;
            }

            if (lowerName == "ivy.xml")
            {
                AddStack(state, "Java", relativePath);
                AddUse(state, "Package manager: Ivy", relativePath);
                DetectIvyManifest(file, state);
                return;
            }

            AddStack(state, string.Equals(extension, ".kts", StringComparison.OrdinalIgnoreCase) ? "Kotlin" : "Java", relativePath);
            AddUse(state, lowerName == "pom.xml" ? "Build tool: Maven" : "Build tool: Gradle", relativePath);
            DetectJavaManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "composer.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "composer.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "PHP", relativePath);
            AddUse(state, "Package manager: Composer", relativePath);
            DetectComposerManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "gemfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "gemfile.lock", StringComparison.Ordinal)
            || extension == ".gemspec")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Ruby", relativePath);
            AddUse(state, "Package manager: Bundler", relativePath);
            DetectRubyManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "deps.edn", StringComparison.Ordinal)
            || string.Equals(lowerName, "project.clj", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.boot", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Clojure", relativePath);
            AddUse(state, "Package manager: Clojure", relativePath);
            DetectClojureManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "mix.exs", StringComparison.Ordinal)
            || string.Equals(lowerName, "mix.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Elixir", relativePath);
            AddUse(state, "Package manager: Hex", relativePath);
            DetectElixirManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "rebar.config", StringComparison.Ordinal)
            || string.Equals(lowerName, "rebar.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Erlang", relativePath);
            AddUse(state, "Package manager: rebar3", relativePath);
            DetectErlangManifest(file, state);
            return;
        }

        if (extension == ".rockspec")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Lua", relativePath);
            AddUse(state, "Package manager: LuaRocks", relativePath);
            DetectLuaRocksManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "cpanfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "makefile.pl", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.pl", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Perl", relativePath);
            AddUse(state, "Package manager: CPAN", relativePath);
            DetectPerlManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "package.swift", StringComparison.Ordinal)
            || string.Equals(lowerName, "package.resolved", StringComparison.Ordinal)
            || string.Equals(lowerName, "podfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "podfile.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "cartfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "cartfile.resolved", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Swift", relativePath);
            AddUse(state, lowerName.StartsWith("podfile", StringComparison.Ordinal) ? "Package manager: CocoaPods" : "Package manager: SwiftPM", relativePath);
            DetectAppleManifest(file, lowerName, state);
            return;
        }

        if ((string.Equals(lowerName, "description", StringComparison.Ordinal) && LooksLikeRDescription(file))
            || string.Equals(lowerName, "renv.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "packrat.lock", StringComparison.Ordinal)
            || extension == ".rproj")
        {
            AddManifest(state, relativePath);
            AddStack(state, "R", relativePath);
            AddUse(state, "Package manager: R", relativePath);
            DetectRManifest(file, lowerName, state);
            return;
        }

        if (string.Equals(lowerName, "project.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "manifest.toml", StringComparison.Ordinal))
        {
            if (LooksLikeJuliaManifest(file))
            {
                AddManifest(state, relativePath);
                AddStack(state, "Julia", relativePath);
                AddUse(state, "Package manager: Julia Pkg", relativePath);
                DetectJuliaManifest(file, state);
                return;
            }
        }

        if (extension == ".cabal"
            || string.Equals(lowerName, "cabal.project", StringComparison.Ordinal)
            || string.Equals(lowerName, "stack.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "package.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Haskell", relativePath);
            AddUse(state, lowerName == "stack.yaml" ? "Build tool: Stack" : "Build tool: Cabal", relativePath);
            DetectHaskellManifest(file, state);
            return;
        }

        if (extension == ".nimble")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Nim", relativePath);
            AddUse(state, "Package manager: Nimble", relativePath);
            DetectNimbleManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "build.zig", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.zig.zon", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Zig", relativePath);
            AddUse(state, "Build tool: Zig", relativePath);
            DetectZigManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "dune-project", StringComparison.Ordinal)
            || string.Equals(lowerName, "dune", StringComparison.Ordinal)
            || extension == ".opam")
        {
            AddManifest(state, relativePath);
            AddStack(state, "OCaml", relativePath);
            AddUse(state, "Package manager: OPAM", relativePath);
            DetectOcamlManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "elm.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Elm", relativePath);
            AddUse(state, "Package manager: Elm", relativePath);
            DetectElmManifest(file, state);
            return;
        }

        if (string.Equals(lowerName, "pubspec.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "pubspec.yml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Dart", relativePath);
            AddUse(state, "Package manager: pub", relativePath);
            DetectPubspecPackages(file, state);
            DetectPubspec(file, state);
            return;
        }

        if (string.Equals(lowerName, "project.godot", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Godot", relativePath);
            return;
        }

        if (string.Equals(lowerName, "cmakelists.txt", StringComparison.Ordinal)
            || extension == ".cmake"
            || string.Equals(lowerName, "makefile", StringComparison.Ordinal)
            || string.Equals(lowerName, "meson.build", StringComparison.Ordinal)
            || string.Equals(lowerName, "conanfile.txt", StringComparison.Ordinal)
            || string.Equals(lowerName, "conanfile.py", StringComparison.Ordinal)
            || string.Equals(lowerName, "vcpkg.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "xmake.lua", StringComparison.Ordinal)
            || string.Equals(lowerName, "premake5.lua", StringComparison.Ordinal)
            || string.Equals(lowerName, "build", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.bazel", StringComparison.Ordinal)
            || string.Equals(lowerName, "workspace", StringComparison.Ordinal)
            || string.Equals(lowerName, "workspace.bazel", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            if (string.Equals(lowerName, "makefile", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Make", relativePath);
            }
            else if (string.Equals(lowerName, "meson.build", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Meson", relativePath);
            }
            else if (string.Equals(lowerName, "conanfile.txt", StringComparison.Ordinal)
                || string.Equals(lowerName, "conanfile.py", StringComparison.Ordinal))
            {
                AddUse(state, "Package manager: Conan", relativePath);
                DetectConanManifest(file, state);
            }
            else if (string.Equals(lowerName, "vcpkg.json", StringComparison.Ordinal))
            {
                AddUse(state, "Package manager: vcpkg", relativePath);
                DetectVcpkgManifest(file, state);
            }
            else if (string.Equals(lowerName, "xmake.lua", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Xmake", relativePath);
            }
            else if (string.Equals(lowerName, "premake5.lua", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Premake", relativePath);
            }
            else if (string.Equals(lowerName, "build", StringComparison.Ordinal)
                || string.Equals(lowerName, "build.bazel", StringComparison.Ordinal)
                || string.Equals(lowerName, "workspace", StringComparison.Ordinal)
                || string.Equals(lowerName, "workspace.bazel", StringComparison.Ordinal))
            {
                AddUse(state, "Build tool: Bazel", relativePath);
                AddStack(state, "Bazel", relativePath);
                return;
            }
            else
            {
                AddUse(state, "Build tool: CMake", relativePath);
                DetectCMakeManifest(file, relativePath, state);
            }

            AddStack(state, "C/C++", relativePath);
            return;
        }

        if (string.Equals(lowerName, "dockerfile", StringComparison.Ordinal)
            || lowerName.EndsWith(".dockerfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "docker-compose.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "docker-compose.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "compose.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "compose.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Docker", relativePath);
            AddUse(state, "Container: Docker", relativePath);
            return;
        }

        if (extension == ".tf" || extension == ".tfvars")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Terraform", relativePath);
            AddUse(state, "Infrastructure: Terraform", relativePath);
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectories(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories("*", SafeEnumerationOptions)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SafeEnumerationOptions)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool TryGetExcludedDirectoryRule(
        DirectoryInfo directory,
        string relativePath,
        out string rule,
        out string reason)
    {
        rule = BuildDirectoryRule(directory, relativePath);
        reason = "generated/dependency";

        if (AlwaysSkippedDirectories.Contains(directory.Name))
        {
            rule = directory.Name;
            return true;
        }

        if (TopLevelBuildConfigurationDirectories.Contains(directory.Name)
            && IsTopLevelPath(relativePath))
        {
            reason = "build configuration";
            return true;
        }

        if (LooksLikeGeneratedBuildRoot(directory))
        {
            reason = "build output";
            return true;
        }

        if (LooksLikePackageManagerRoot(directory))
        {
            reason = "package manager root";
            return true;
        }

        if (LooksLikeNestedToolProject(directory))
        {
            reason = "tooling project";
            return true;
        }

        if (LooksLikeNestedRepository(directory))
        {
            reason = "nested repository";
            return true;
        }

        return false;
    }

    private static string BuildDirectoryRule(DirectoryInfo directory, string relativePath)
    {
        var normalizedPath = NormalizePath(relativePath);
        return string.IsNullOrWhiteSpace(normalizedPath) ? directory.Name : normalizedPath;
    }

    private static bool IsTopLevelPath(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        return !string.IsNullOrWhiteSpace(normalized)
            && !normalized.Contains('/', StringComparison.Ordinal);
    }

    private static bool LooksLikeGeneratedBuildRoot(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, "CMakeCache.txt"))
            || Directory.Exists(Path.Combine(directory.FullName, "CMakeFiles"));
    }

    private static bool LooksLikePackageManagerRoot(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, ".vcpkg-root"));
    }

    private static bool LooksLikeNestedToolProject(DirectoryInfo directory)
    {
        return LooksLikeContextControl(directory);
    }

    private static bool LooksLikeNestedRepository(DirectoryInfo directory)
    {
        return Directory.Exists(Path.Combine(directory.FullName, ".git"))
            || File.Exists(Path.Combine(directory.FullName, ".git"));
    }

    private static bool LooksLikeContextControl(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, "ccStart.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "ccDir.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "cc.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "ccReplace.ps1"));
    }

    private static void AddExcludedDirectoryUse(
        ScanState state,
        DirectoryInfo directory,
        string relativePath,
        string reason)
    {
        if (reason == "package manager root")
        {
            AddUse(state, "Package manager: " + DetectPackageManagerRootName(directory), relativePath);
        }
        else if (reason == "nested repository")
        {
            AddUse(state, "Nested repository", relativePath);
        }
        else if (reason == "tooling project")
        {
            AddUse(state, "Tooling project", relativePath);
        }
    }

    private static string DetectPackageManagerRootName(DirectoryInfo directory)
    {
        if (File.Exists(Path.Combine(directory.FullName, ".vcpkg-root")))
        {
            return "vcpkg";
        }

        return directory.Name;
    }

    private static void DetectFileSignals(FileInfo file, string relativePath, ScanState state)
    {
        var name = file.Name;
        var lowerName = name.ToLowerInvariant();
        var extension = NormalizeExtension(file.Extension);

        switch (extension)
        {
            case ".axaml":
                AddStack(state, "Avalonia UI", "*.axaml");
                break;
            case ".asm":
            case ".s":
            case ".nasm":
                AddStack(state, "Assembly", $"*{extension}");
                break;
            case ".bat":
            case ".cmd":
                AddStack(state, "Batch", $"*{extension}");
                break;
            case ".bzl":
                AddStack(state, "Bazel", "*.bzl");
                break;
            case ".cs":
            case ".fs":
            case ".fsi":
            case ".fsx":
                AddStack(state, "F#", $"*{extension}");
                AddStack(state, ".NET", $"*{extension}");
                break;
            case ".vb":
                AddStack(state, ".NET", $"*{extension}");
                break;
            case ".cpp":
            case ".cc":
            case ".cxx":
            case ".c":
            case ".hpp":
            case ".hh":
            case ".hxx":
            case ".h":
                AddStack(state, "C/C++", $"*{extension}");
                break;
            case ".cu":
            case ".cuh":
                AddStack(state, "CUDA", $"*{extension}");
                AddStack(state, "C/C++", $"*{extension}");
                break;
            case ".clj":
            case ".cljs":
            case ".cljc":
                AddStack(state, "Clojure", $"*{extension}");
                break;
            case ".coffee":
                AddStack(state, "CoffeeScript", "*.coffee");
                AddStack(state, "JavaScript", "*.coffee");
                break;
            case ".dart":
                AddStack(state, "Dart", "*.dart");
                break;
            case ".elm":
                AddStack(state, "Elm", "*.elm");
                break;
            case ".erl":
            case ".hrl":
                AddStack(state, "Erlang", $"*{extension}");
                break;
            case ".ex":
            case ".exs":
                AddStack(state, "Elixir", $"*{extension}");
                break;
            case ".go":
                AddStack(state, "Go", "*.go");
                break;
            case ".gradle":
            case ".groovy":
                AddStack(state, "Groovy", $"*{extension}");
                break;
            case ".hs":
            case ".lhs":
                AddStack(state, "Haskell", $"*{extension}");
                break;
            case ".java":
                AddStack(state, "Java", "*.java");
                break;
            case ".jl":
                AddStack(state, "Julia", "*.jl");
                break;
            case ".js":
            case ".mjs":
            case ".cjs":
                AddStack(state, "JavaScript", $"*{extension}");
                break;
            case ".jsx":
                AddStack(state, "JavaScript", "*.jsx");
                AddStack(state, "React", "*.jsx");
                break;
            case ".kt":
            case ".kts":
                AddStack(state, "Kotlin", $"*{extension}");
                break;
            case ".lua":
                AddStack(state, "Lua", "*.lua");
                break;
            case ".m":
            case ".mm":
                AddStack(state, "Objective-C", $"*{extension}");
                break;
            case ".ml":
            case ".mli":
                AddStack(state, "OCaml", $"*{extension}");
                break;
            case ".nim":
                AddStack(state, "Nim", "*.nim");
                break;
            case ".pas":
                AddStack(state, "Pascal", "*.pas");
                break;
            case ".php":
                AddStack(state, "PHP", "*.php");
                break;
            case ".pl":
            case ".pm":
                AddStack(state, "Perl", $"*{extension}");
                break;
            case ".ps1":
            case ".psm1":
            case ".psd1":
                AddStack(state, "PowerShell", $"*{extension}");
                break;
            case ".py":
                AddStack(state, "Python", "*.py");
                break;
            case ".r":
            case ".rmd":
                AddStack(state, "R", $"*{extension}");
                break;
            case ".raku":
                AddStack(state, "Raku", "*.raku");
                break;
            case ".rb":
                AddStack(state, "Ruby", "*.rb");
                break;
            case ".rs":
                AddStack(state, "Rust", "*.rs");
                break;
            case ".scala":
            case ".sc":
            case ".sbt":
                AddStack(state, "Scala", $"*{extension}");
                break;
            case ".sh":
            case ".bash":
            case ".zsh":
                AddStack(state, "Shell", $"*{extension}");
                break;
            case ".sql":
                AddStack(state, "SQL", "*.sql");
                break;
            case ".swift":
                AddStack(state, "Swift", "*.swift");
                break;
            case ".svelte":
                AddStack(state, "Svelte", "*.svelte");
                break;
            case ".tf":
            case ".tfvars":
            case ".hcl":
                AddStack(state, "Terraform", $"*{extension}");
                break;
            case ".ts":
            case ".tsx":
            case ".mts":
            case ".cts":
                AddStack(state, "TypeScript", $"*{extension}");
                break;
            case ".vue":
                AddStack(state, "Vue", "*.vue");
                break;
            case ".zig":
            case ".zon":
                AddStack(state, "Zig", $"*{extension}");
                break;
        }

        if (string.Equals(lowerName, "package.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Node.js", relativePath);
            DetectPackageJson(file, state);
            return;
        }

        if (string.Equals(lowerName, "tsconfig.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "TypeScript", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "next.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Next.js", relativePath);
            AddStack(state, "React", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "vite.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Vite", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "svelte.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Svelte", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "astro.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Astro", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (StartsWithConfigName(lowerName, "nuxt.config"))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Nuxt", relativePath);
            AddStack(state, "Vue", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (string.Equals(lowerName, "angular.json", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Angular", relativePath);
            AddStack(state, "TypeScript", relativePath);
            AddStack(state, "Node.js", relativePath);
            return;
        }

        if (extension is ".csproj" or ".fsproj" or ".vbproj")
        {
            AddManifest(state, relativePath);
            DetectDotNetProject(file, relativePath, state);
            return;
        }

        if (extension == ".sln"
            || string.Equals(lowerName, "global.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.props", StringComparison.Ordinal)
            || string.Equals(lowerName, "directory.build.targets", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, ".NET", relativePath);
            return;
        }

        if (string.Equals(lowerName, "pyproject.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "requirements.txt", StringComparison.Ordinal)
            || string.Equals(lowerName, "poetry.lock", StringComparison.Ordinal)
            || string.Equals(lowerName, "pipfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.py", StringComparison.Ordinal)
            || string.Equals(lowerName, "setup.cfg", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Python", relativePath);
            return;
        }

        if (string.Equals(lowerName, "cargo.toml", StringComparison.Ordinal)
            || string.Equals(lowerName, "cargo.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Rust", relativePath);
            return;
        }

        if (string.Equals(lowerName, "go.mod", StringComparison.Ordinal)
            || string.Equals(lowerName, "go.sum", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Go", relativePath);
            return;
        }

        if (string.Equals(lowerName, "pom.xml", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "build.gradle.kts", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle", StringComparison.Ordinal)
            || string.Equals(lowerName, "settings.gradle.kts", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, string.Equals(extension, ".kts", StringComparison.OrdinalIgnoreCase) ? "Kotlin" : "Java", relativePath);
            return;
        }

        if (string.Equals(lowerName, "composer.json", StringComparison.Ordinal)
            || string.Equals(lowerName, "composer.lock", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "PHP", relativePath);
            return;
        }

        if (string.Equals(lowerName, "gemfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "gemfile.lock", StringComparison.Ordinal)
            || extension == ".gemspec")
        {
            AddManifest(state, relativePath);
            AddStack(state, "Ruby", relativePath);
            return;
        }

        if (string.Equals(lowerName, "pubspec.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "pubspec.yml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Dart", relativePath);
            DetectPubspec(file, state);
            return;
        }

        if (string.Equals(lowerName, "project.godot", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Godot", relativePath);
            return;
        }

        if (string.Equals(lowerName, "cmakelists.txt", StringComparison.Ordinal)
            || extension == ".cmake"
            || string.Equals(lowerName, "makefile", StringComparison.Ordinal)
            || string.Equals(lowerName, "meson.build", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "C/C++", relativePath);
            return;
        }

        if (string.Equals(lowerName, "dockerfile", StringComparison.Ordinal)
            || lowerName.EndsWith(".dockerfile", StringComparison.Ordinal)
            || string.Equals(lowerName, "docker-compose.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "docker-compose.yaml", StringComparison.Ordinal)
            || string.Equals(lowerName, "compose.yml", StringComparison.Ordinal)
            || string.Equals(lowerName, "compose.yaml", StringComparison.Ordinal))
        {
            AddManifest(state, relativePath);
            AddStack(state, "Docker", relativePath);
        }
    }

    private static void DetectTextUseSignals(FileInfo file, string relativePath, string extension, ScanState state)
    {
        if (state.TextSignalFilesScanned >= MaxTextSignalFiles
            || file.Length > MaxTextSignalReadBytes
            || !ShouldReadTextSignals(file.Name, extension))
        {
            return;
        }

        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        state.TextSignalFilesScanned++;
        foreach (var pattern in TextUsePatterns)
        {
            if (pattern.Extensions.Contains(extension, NameComparer)
                && pattern.Needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                AddUse(state, pattern.Name, relativePath);
            }
        }
    }

    private static bool ShouldReadTextSignals(string fileName, string extension)
    {
        if (TextSignalExtensions.Contains(extension))
        {
            return true;
        }

        var lowerName = fileName.ToLowerInvariant();
        return lowerName is "cmakelists.txt"
            or "makefile"
            or "dockerfile"
            or "gemfile"
            or "pipfile";
    }

    private static void DetectPackageJson(FileInfo file, ScanState state)
    {
        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        var packages = new HashSet<string>(NameComparer);
        foreach (var propertyName in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
        {
            if (!root.TryGetProperty(propertyName, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                packages.Add(dependency.Name);
            }
        }

        if (root.TryGetProperty("packageManager", out var packageManager)
            && packageManager.ValueKind == JsonValueKind.String
            && packageManager.GetString() is { } packageManagerName)
        {
            AddStack(state, "Node.js", packageManagerName);
            AddUse(state, "Package manager: " + NormalizePackageManagerName(packageManagerName), "packageManager");
        }

        foreach (var package in packages.Take(80))
        {
            AddUse(state, "npm: " + package, "package.json dependency");
        }

        if (packages.Contains("next"))
        {
            AddStack(state, "Next.js", "next dependency");
            AddStack(state, "React", "next dependency");
        }

        if (packages.Contains("react") || packages.Contains("react-dom"))
        {
            AddStack(state, "React", "react dependency");
        }

        if (packages.Contains("vue"))
        {
            AddStack(state, "Vue", "vue dependency");
        }

        if (packages.Contains("svelte") || packages.Contains("@sveltejs/kit"))
        {
            AddStack(state, "Svelte", "svelte dependency");
        }

        if (packages.Contains("vite"))
        {
            AddStack(state, "Vite", "vite dependency");
        }

        if (packages.Contains("@angular/core"))
        {
            AddStack(state, "Angular", "@angular/core dependency");
        }

        if (packages.Contains("astro"))
        {
            AddStack(state, "Astro", "astro dependency");
        }

        if (packages.Contains("nuxt"))
        {
            AddStack(state, "Nuxt", "nuxt dependency");
            AddStack(state, "Vue", "nuxt dependency");
        }

        if (packages.Contains("typescript") || packages.Any(name => name.StartsWith("@types/", StringComparison.OrdinalIgnoreCase)))
        {
            AddStack(state, "TypeScript", "typescript dependency");
        }

        if (packages.Contains("electron"))
        {
            AddStack(state, "Electron", "electron dependency");
        }
    }

    private static void DetectDenoManifest(FileInfo file, string lowerName, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var match in Regex.Matches(text, @"[""']((?:npm|jsr):[^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, (lowerName.StartsWith("jsr.", StringComparison.Ordinal) ? "JSR: " : "Deno: ") + match.Groups[1].Value, file.Name);
        }

        foreach (var match in Regex.Matches(text, @"[""'](@?[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+|@?[A-Za-z0-9_.-]+)[""']\s*:", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            var package = match.Groups[1].Value;
            if (package is not "imports" and not "tasks" and not "compilerOptions" and not "lint" and not "fmt")
            {
                AddUse(state, (lowerName.StartsWith("jsr.", StringComparison.Ordinal) ? "JSR: " : "Deno import: ") + package, file.Name);
            }
        }
    }

    private static void DetectDotNetProject(FileInfo file, string relativePath, ScanState state)
    {
        AddStack(state, ".NET", relativePath);

        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        var sdk = document.Root?.Attribute("Sdk")?.Value ?? "";
        if (!string.IsNullOrWhiteSpace(sdk))
        {
            AddUse(state, ".NET SDK: " + sdk, "project sdk");
        }

        if (sdk.Contains("Web", StringComparison.OrdinalIgnoreCase))
        {
            AddStack(state, "ASP.NET Core", "web sdk");
        }

        foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
        {
            var include = reference.Attribute("Include")?.Value
                ?? reference.Attribute("Update")?.Value
                ?? "";
            if (!string.IsNullOrWhiteSpace(include))
            {
                AddUse(state, "NuGet: " + include, "PackageReference");
            }

            if (include.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, "Avalonia UI", include);
            }
            else if (include.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
                || include.Contains("Blazor", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, "ASP.NET Core", include);
            }
            else if (include.Contains("Maui", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, ".NET MAUI", include);
            }
            else if (include.Contains("Xamarin", StringComparison.OrdinalIgnoreCase))
            {
                AddStack(state, "Xamarin", include);
            }
        }
    }

    private static void DetectDotNetPackagesConfig(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var package in document.Descendants().Where(element => element.Name.LocalName == "package"))
        {
            var id = package.Attribute("id")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(id))
            {
                AddUse(state, "NuGet: " + id, "packages.config");
            }
        }
    }

    private static void DetectDotNetPackageProps(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var package in document.Descendants().Where(element => element.Name.LocalName == "PackageVersion"))
        {
            var include = package.Attribute("Include")?.Value
                ?? package.Attribute("Update")?.Value
                ?? "";
            if (!string.IsNullOrWhiteSpace(include))
            {
                AddUse(state, "NuGet: " + include, "PackageVersion");
            }
        }
    }

    private static void DetectCMakeManifest(FileInfo file, string relativePath, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in Regex.Matches(text, @"\bfind_package\s*\(\s*([A-Za-z0-9_.+-]+)", RegexOptions.IgnoreCase))
        {
            AddUse(state, NormalizeTechnologyName(match.Groups[1].Value), $"find_package in {relativePath}");
        }

        foreach (Match match in Regex.Matches(text, @"\bFetchContent_Declare\s*\(\s*([A-Za-z0-9_.+-]+)", RegexOptions.IgnoreCase))
        {
            AddUse(state, NormalizeTechnologyName(match.Groups[1].Value), $"FetchContent in {relativePath}");
        }

        foreach (Match match in Regex.Matches(text, @"\btarget_link_libraries\s*\((?<body>[^)]{1,3000})\)", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            foreach (var token in SplitCMakeTokens(match.Groups["body"].Value).Skip(1))
            {
                if (TryNormalizeLinkedLibrary(token, out var library))
                {
                    AddUse(state, library, $"target_link_libraries in {relativePath}");
                }
            }
        }

        if (text.Contains("vcpkg.cmake", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CMAKE_TOOLCHAIN_FILE", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Package manager: vcpkg", relativePath);
        }
    }

    private static void DetectConanManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var inRequires = false;
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                inRequires = trimmed.Equals("[requires]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inRequires && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var package = trimmed.Split(['#', ';'], 2)[0].Trim();
                if (!string.IsNullOrWhiteSpace(package))
                {
                    AddUse(state, "Conan: " + package, file.Name);
                }
            }
        }

        foreach (var match in Regex.Matches(text, @"(?:self\.)?requires\s*\(?\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "Conan: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectVcpkgManifest(FileInfo file, ScanState state)
    {
        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var dependency in dependencies.EnumerateArray().Take(80))
        {
            if (dependency.ValueKind == JsonValueKind.String && dependency.GetString() is { } package)
            {
                AddUse(state, "vcpkg: " + package, "vcpkg.json");
            }
            else if (dependency.ValueKind == JsonValueKind.Object
                && dependency.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && name.GetString() is { } namedPackage)
            {
                AddUse(state, "vcpkg: " + namedPackage, "vcpkg.json");
            }
        }
    }

    private static void DetectPythonManifest(FileInfo file, string lowerName, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (IsRequirementsFileName(lowerName))
        {
            AddUse(state, "Package manager: pip", file.Name);
            foreach (var package in ExtractRequirementsPackages(text).Take(80))
            {
                AddUse(state, "pip: " + package, "requirements.txt");
            }

            return;
        }

        if (lowerName == "pipfile")
        {
            AddUse(state, "Package manager: Pipenv", file.Name);
        }

        if (lowerName == "uv.lock")
        {
            AddUse(state, "Package manager: uv", file.Name);
        }

        if (lowerName == "pdm.lock" || text.Contains("[tool.pdm]", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Package manager: PDM", file.Name);
        }

        if (lowerName is "environment.yml" or "environment.yaml" or "conda.yml" or "conda.yaml")
        {
            AddUse(state, "Package manager: conda", file.Name);
            foreach (var package in ExtractYamlDependencyNames(text).Take(80))
            {
                AddUse(state, "conda: " + package, file.Name);
            }
        }

        if (lowerName == "poetry.lock" || text.Contains("[tool.poetry]", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Package manager: Poetry", file.Name);
        }

        if (text.Contains("[tool.hatch]", StringComparison.OrdinalIgnoreCase))
        {
            AddUse(state, "Build tool: Hatch", file.Name);
        }

        foreach (var tool in new[] { "pytest", "ruff", "black", "mypy", "isort" })
        {
            if (text.Contains("[tool." + tool, StringComparison.OrdinalIgnoreCase)
                || text.Contains(tool + ">=", StringComparison.OrdinalIgnoreCase))
            {
                AddUse(state, "Python tool: " + tool, file.Name);
            }
        }

        foreach (var package in ExtractTomlDependencyNames(text).Take(80))
        {
            AddUse(state, "Python package: " + package, file.Name);
        }

        foreach (var package in ExtractPythonLockPackageNames(text).Take(80))
        {
            AddUse(state, "Python package: " + package, file.Name);
        }
    }

    private static void DetectCargoManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "cargo.lock")
        {
            return;
        }

        var text = TryReadSmallText(file);
        foreach (var package in ExtractTomlSectionKeys(text, "dependencies", "dev-dependencies", "build-dependencies").Take(80))
        {
            AddUse(state, "Cargo: " + package, "Cargo.toml");
        }
    }

    private static void DetectGoManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "go.sum")
        {
            return;
        }

        var text = TryReadSmallText(file);
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("module ", StringComparison.Ordinal))
            {
                AddUse(state, "Go module: " + trimmed[7..].Trim(), "go.mod");
            }
            else if (trimmed.StartsWith("require ", StringComparison.Ordinal))
            {
                var package = trimmed[8..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(package) && package != "(")
                {
                    AddUse(state, "Go package: " + package, "go.mod");
                }
            }
            else if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+\s+v\d", RegexOptions.IgnoreCase))
            {
                AddUse(state, "Go package: " + trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], "go.mod");
            }
        }
    }

    private static void DetectJavaManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "pom.xml")
        {
            DetectMavenManifest(file, state);
            return;
        }

        var text = TryReadSmallText(file);
        foreach (Match match in Regex.Matches(text, @"(?:implementation|api|compileOnly|runtimeOnly|testImplementation)\s*\(?\s*[""']([^:""']+):([^:""']+)", RegexOptions.IgnoreCase))
        {
            AddUse(state, "Gradle: " + match.Groups[1].Value + ":" + match.Groups[2].Value, file.Name);
        }

        foreach (Match match in Regex.Matches(text, @"id\s*\(?\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
        {
            AddUse(state, "Gradle plugin: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectScalaManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"""([^""]+)""\s*%%?\s*""([^""]+)""\s*%", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "sbt: " + match.Groups[1].Value + ":" + match.Groups[2].Value, file.Name);
        }

        foreach (var match in Regex.Matches(text, @"addSbtPlugin\s*\(\s*""([^""]+)""\s*%%?\s*""([^""]+)""", RegexOptions.IgnoreCase).Cast<Match>().Take(40))
        {
            AddUse(state, "sbt plugin: " + match.Groups[1].Value + ":" + match.Groups[2].Value, file.Name);
        }
    }

    private static void DetectIvyManifest(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var dependency in document.Descendants().Where(element => element.Name.LocalName == "dependency"))
        {
            var org = dependency.Attribute("org")?.Value ?? "";
            var name = dependency.Attribute("name")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(name))
            {
                AddUse(state, "Ivy: " + org + ":" + name, "ivy.xml");
            }
        }
    }

    private static void DetectMavenManifest(FileInfo file, ScanState state)
    {
        XDocument? document;
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return;
            }

            document = XDocument.Load(file.FullName);
        }
        catch
        {
            return;
        }

        foreach (var dependency in document.Descendants().Where(element => element.Name.LocalName == "dependency"))
        {
            var groupId = dependency.Elements().FirstOrDefault(element => element.Name.LocalName == "groupId")?.Value ?? "";
            var artifactId = dependency.Elements().FirstOrDefault(element => element.Name.LocalName == "artifactId")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(groupId) && !string.IsNullOrWhiteSpace(artifactId))
            {
                AddUse(state, "Maven: " + groupId + ":" + artifactId, "pom.xml");
            }
        }
    }

    private static void DetectComposerManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName != "composer.json")
        {
            return;
        }

        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        foreach (var section in new[] { "require", "require-dev" })
        {
            if (!document.RootElement.TryGetProperty(section, out var dependencies)
                || dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject().Take(80))
            {
                AddUse(state, "Composer: " + dependency.Name, "composer.json");
            }
        }
    }

    private static void DetectRubyManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (Match match in Regex.Matches(text, @"\bgem\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase))
        {
            AddUse(state, "Ruby gem: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectClojureManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)").Cast<Match>().Take(80))
        {
            AddUse(state, "Clojure: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectElixirManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\{\s*:([A-Za-z0-9_]+)\s*,", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "Hex: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectErlangManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\{\s*([A-Za-z0-9_]+)\s*,\s*[""'{]", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "rebar: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectLuaRocksManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"[""']([A-Za-z0-9_.-]+)\s*[<>=~]", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "LuaRocks: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectPerlManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\b(?:requires|recommends|suggests|test_requires)\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "CPAN: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectAppleManifest(FileInfo file, string lowerName, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (lowerName.StartsWith("podfile", StringComparison.Ordinal))
        {
            foreach (var match in Regex.Matches(text, @"\bpod\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
            {
                AddUse(state, "CocoaPods: " + match.Groups[1].Value, file.Name);
            }

            return;
        }

        if (lowerName.StartsWith("cartfile", StringComparison.Ordinal))
        {
            foreach (var match in Regex.Matches(text, @"\b(?:github|git)\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
            {
                AddUse(state, "Carthage: " + match.Groups[1].Value, file.Name);
            }

            return;
        }

        foreach (var match in Regex.Matches(text, @"\.package\s*\([^)]+(?:url|name)\s*:\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "SwiftPM: " + ShortPackageName(match.Groups[1].Value), file.Name);
        }
    }

    private static void DetectRManifest(FileInfo file, string lowerName, ScanState state)
    {
        if (lowerName == "renv.lock")
        {
            using var document = TryReadJsonDocument(file);
            if (document?.RootElement.TryGetProperty("Packages", out var packages) == true
                && packages.ValueKind == JsonValueKind.Object)
            {
                foreach (var package in packages.EnumerateObject().Take(80))
                {
                    AddUse(state, "R package: " + package.Name, "renv.lock");
                }
            }

            return;
        }

        var text = TryReadSmallText(file);
        foreach (var package in ExtractDescriptionPackages(text).Take(80))
        {
            AddUse(state, "R package: " + package, file.Name);
        }
    }

    private static bool LooksLikeRDescription(FileInfo file)
    {
        var text = TryReadSmallText(file);
        return Regex.IsMatch(text, @"^Package\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            && Regex.IsMatch(text, @"^Version\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static bool LooksLikeJuliaManifest(FileInfo file)
    {
        var text = TryReadSmallText(file);
        return text.Contains("[deps]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[compat]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("julia_version", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"^\s*uuid\s*=", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static void DetectJuliaManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        var inDeps = false;
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[[deps.", StringComparison.OrdinalIgnoreCase))
            {
                var package = trimmed.Trim('[', ']').Replace("deps.", "", StringComparison.OrdinalIgnoreCase);
                AddUse(state, "Julia: " + package, file.Name);
                continue;
            }

            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                inDeps = trimmed.Equals("[deps]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inDeps)
            {
                var match = Regex.Match(trimmed, @"^([A-Za-z0-9_.-]+)\s*=");
                if (match.Success)
                {
                    AddUse(state, "Julia: " + match.Groups[1].Value, file.Name);
                }
            }
        }
    }

    private static void DetectHaskellManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (Match block in Regex.Matches(text, @"build-depends\s*:\s*(?<body>[^\r\n]+(?:\r?\n\s+[^:\r\n]+)*)", RegexOptions.IgnoreCase))
        {
            foreach (var package in SplitDependencyList(block.Groups["body"].Value).Take(80))
            {
                AddUse(state, "Hackage: " + package, file.Name);
            }
        }

        foreach (var package in ExtractYamlListValues(text, "extra-deps", "dependencies").Take(80))
        {
            AddUse(state, "Hackage: " + package, file.Name);
        }
    }

    private static void DetectNimbleManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\brequires\s+[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            foreach (var package in SplitDependencyList(match.Groups[1].Value))
            {
                AddUse(state, "Nimble: " + package, file.Name);
            }
        }
    }

    private static void DetectZigManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"\.name\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "Zig package: " + match.Groups[1].Value, file.Name);
        }
    }

    private static void DetectOcamlManifest(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        foreach (var match in Regex.Matches(text, @"[""']([A-Za-z0-9_.+-]+)[""']", RegexOptions.IgnoreCase).Cast<Match>().Take(80))
        {
            AddUse(state, "OPAM: " + match.Groups[1].Value, file.Name);
        }

        foreach (Match match in Regex.Matches(text, @"\(depends\s+([^)]{1,1000})\)", RegexOptions.IgnoreCase))
        {
            foreach (var package in SplitDependencyList(match.Groups[1].Value).Take(80))
            {
                AddUse(state, "Dune: " + package, file.Name);
            }
        }
    }

    private static void DetectElmManifest(FileInfo file, ScanState state)
    {
        using var document = TryReadJsonDocument(file);
        if (document is null)
        {
            return;
        }

        foreach (var section in new[] { "dependencies", "test-dependencies" })
        {
            if (!document.RootElement.TryGetProperty(section, out var dependencies))
            {
                continue;
            }

            if (dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in dependencies.EnumerateObject().Take(80))
                {
                    AddUse(state, "Elm: " + dependency.Name, "elm.json");
                }
            }
        }
    }

    private static void DetectPubspecPackages(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        var inDependencySection = false;
        foreach (var line in SplitTextLines(text))
        {
            if (Regex.IsMatch(line, @"^(dependencies|dev_dependencies):\s*$", RegexOptions.IgnoreCase))
            {
                inDependencySection = true;
                continue;
            }

            if (inDependencySection && Regex.IsMatch(line, @"^\S"))
            {
                inDependencySection = false;
            }

            if (!inDependencySection)
            {
                continue;
            }

            var match = Regex.Match(line, @"^\s{2}([A-Za-z0-9_][A-Za-z0-9_.-]*):");
            if (match.Success)
            {
                AddUse(state, "pub: " + match.Groups[1].Value, "pubspec");
            }
        }
    }

    private static void DetectPubspec(FileInfo file, ScanState state)
    {
        var text = TryReadSmallText(file);
        if (text.Contains("flutter:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sdk: flutter", StringComparison.OrdinalIgnoreCase))
        {
            AddStack(state, "Flutter", "pubspec flutter");
        }
    }

    private static void AddPostScanStackSignals(ScanState state)
    {
        if (state.LanguageCounts.ContainsKey("C#") && state.StackReasons.ContainsKey(".NET"))
        {
            AddStack(state, ".NET", "C# files");
        }

        if (state.ExtensionCounts.ContainsKey(".tsx") || state.ExtensionCounts.ContainsKey(".jsx"))
        {
            AddStack(state, "React", "*.tsx/*.jsx");
        }

        foreach (var extension in state.ExtensionCounts.Keys)
        {
            if (LanguageByExtension.TryGetValue(extension, out var language))
            {
                AddSuggestedExtension(state, extension, isLoc: language is not "CSS" and not "HTML" and not "XAML");
            }
        }
    }

    private static ProjectStackScanResult BuildResult(ScanState state)
    {
        var stacks = state.StackReasons
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var stackLabel = stacks.Length == 0
            ? "No stack manifests detected"
            : string.Join(", ", stacks.Take(4).Select(item => item.Key)) + (stacks.Length > 4 ? $", +{stacks.Length - 4}" : "");
        var summary = $"{stackLabel} | {state.TrackedFiles:N0}/{state.VisibleFiles:N0} visible files match current rules | {state.FilesSeen:N0} scanned for setup";

        var coveredSupported = new SortedSet<string>(NameComparer);
        var coveredLoc = new SortedSet<string>(NameComparer);
        var supportedSuggestions = new SortedSet<string>(NameComparer);
        var locSuggestions = new SortedSet<string>(NameComparer);
        var skippedDirectorySuggestions = new SortedSet<string>(NameComparer);
        foreach (var stack in stacks.Select(item => item.Key))
        {
            AddCoveredExtensions(coveredSupported, SuggestedSupportedByStack, stack, state.Rules.SupportedExtensions);
            AddCoveredExtensions(coveredLoc, SuggestedLocByStack, stack, state.Rules.LocExtensions);
            AddMissingExtensions(supportedSuggestions, SuggestedSupportedByStack, stack, state.Rules.SupportedExtensions);
            AddMissingExtensions(locSuggestions, SuggestedLocByStack, stack, state.Rules.LocExtensions);
            AddMissingNames(skippedDirectorySuggestions, SuggestedSkippedDirectoriesByStack, stack, state.Rules.IgnoredDirectories);
        }

        foreach (var rule in state.AutoSkippedDirectoryRules)
        {
            if (!state.Rules.IgnoredDirectories.Contains(rule, NameComparer))
            {
                skippedDirectorySuggestions.Add(rule);
            }
        }

        foreach (var extension in state.ExtensionCounts.Keys)
        {
            if (!ShouldSuggestObservedExtension(extension))
            {
                continue;
            }

            if (!state.Rules.SupportedExtensions.Contains(extension, NameComparer))
            {
                supportedSuggestions.Add(extension);
            }

            if (LanguageByExtension.ContainsKey(extension) && !state.Rules.LocExtensions.Contains(extension, NameComparer))
            {
                locSuggestions.Add(extension);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Project: {state.ProjectRoot}");
        var ruleSummary = $"{state.Rules.SupportedExtensions.Count:N0} allowed | {state.Rules.LocExtensions.Count:N0} LOC | {state.Rules.IgnoredExtensions.Count:N0} skipped types | {state.Rules.IgnoredDirectories.Count:N0} skipped folders";
        var scanSummary = $"{state.FilesSeen:N0} scanned | {state.VisibleFiles:N0} visible | {state.TrackedFiles:N0} matched | {state.UnsupportedVisibleFiles:N0} unsupported | {state.FilesSkippedByRules:N0} hidden files | {state.DirectoriesSkippedByRules:N0} hidden folders | {state.DirectoriesExcluded:N0} excluded folders";
        builder.AppendLine($"Rules: {ruleSummary}");
        builder.AppendLine($"Scan: {scanSummary}");
        if (state.LimitHit)
        {
            builder.AppendLine($"Limit: stopped at {MaxFiles:N0} files or {MaxDirectories:N0} folders");
        }

        var stackItems = stacks.Select(item =>
            $"{item.Key}: {string.Join(", ", item.Value.Take(4))}{(item.Value.Count > 4 ? ", ..." : "")}").ToArray();
        var useItems = BuildUseItems(state);
        var languageItems = state.LanguageCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(item => $"{item.Key}: {item.Value:N0} files")
            .ToArray();
        var topFileTypeItems = state.ExtensionCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(item => $"{item.Key}: {item.Value:N0}")
            .ToArray();
        var unsupportedItems = state.UnsupportedExtensionCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(item => $"{item.Key}: {item.Value:N0}")
            .ToArray();

        AppendSection(builder, "Stack", stackItems);
        AppendSection(builder, "Uses", useItems);
        AppendSection(builder, "Languages", languageItems);
        AppendSection(builder, "Manifests", state.ManifestSamples);
        AppendSection(builder, "Top file types", topFileTypeItems);
        AppendSection(builder, "Unsupported visible types", unsupportedItems);
        AppendSection(builder, "Already allowed stack file types", coveredSupported);
        AppendSection(builder, "Already counted LOC file types", coveredLoc);
        AppendSection(builder, "Suggested allowed types", supportedSuggestions);
        AppendSection(builder, "Suggested LOC types", locSuggestions);
        AppendSection(builder, "Suggested skipped folders", skippedDirectorySuggestions);
        AppendSection(builder, "Skipped samples", state.SkippedDirectorySamples.Concat(state.SkippedFileSamples).Take(8));

        var metrics = new[]
        {
            new ProjectStackMetric("Stack", stackLabel, stacks.Length == 0 ? "No manifests or language anchors found" : $"{stacks.Length:N0} detected signal(s)"),
            new ProjectStackMetric("Scanned", state.FilesSeen.ToString("N0"), "project files considered"),
            new ProjectStackMetric("Visible", state.VisibleFiles.ToString("N0"), "after skip rules"),
            new ProjectStackMetric("Matched", state.TrackedFiles.ToString("N0"), "allowed code/context files"),
            new ProjectStackMetric("Hidden", (state.FilesSkippedByRules + state.DirectoriesSkippedByRules).ToString("N0"), "current rules hide"),
            new ProjectStackMetric("Excluded", state.DirectoriesExcluded.ToString("N0"), "generated/dependency folders")
        };

        var sections = new List<ProjectStackSection>
        {
            new("Detected Stack", stackItems),
            new("Uses", useItems),
            new("Languages", languageItems),
            new("Manifests", state.ManifestSamples.ToArray()),
            new("Top File Types", topFileTypeItems),
            new("Unsupported Visible Types", unsupportedItems),
            new("Already Allowed", coveredSupported.ToArray()),
            new("Already Counted LOC", coveredLoc.ToArray()),
            new("Autosetup Plan", BuildAutosetupDeltaItems(supportedSuggestions, locSuggestions, skippedDirectorySuggestions)),
            new("Skipped Samples", state.SkippedDirectorySamples.Concat(state.SkippedFileSamples).Take(8).ToArray())
        };

        return new ProjectStackScanResult(
            summary,
            builder.ToString().TrimEnd(),
            state.ProjectRoot,
            stackLabel,
            ruleSummary,
            scanSummary,
            metrics,
            sections,
            BuildAutoSetupRuleSet(state, stacks.Select(item => item.Key), supportedSuggestions, locSuggestions, skippedDirectorySuggestions));
    }

    private static string[] BuildUseItems(ScanState state)
    {
        return state.UseReasons
            .OrderBy(item => GetUseSortGroup(item.Key))
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .Select(item => $"{item.Key}: {string.Join(", ", item.Value.Take(4))}{(item.Value.Count > 4 ? ", ..." : "")}")
            .ToArray();
    }

    private static int GetUseSortGroup(string value)
    {
        if (value.StartsWith("Build tool:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Package manager:", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (value.StartsWith("NuGet:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("npm:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("pip:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Cargo:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Carthage:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Clojure:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Conan:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("CPAN:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Deno:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Dune:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Elm:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Go package:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Composer:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("conda:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("CocoaPods:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Ruby gem:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Hackage:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Hex:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Ivy:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("JSR:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Julia:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("LuaRocks:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("pub:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Maven:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Gradle:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Nimble:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("OPAM:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("R package:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rebar:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("sbt:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("SwiftPM:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("vcpkg:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Zig package:", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static void AddCoveredExtensions(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack,
        IReadOnlyCollection<string> existing)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (existing.Contains(value, NameComparer))
            {
                target.Add(value);
            }
        }
    }

    private static void AddMissingExtensions(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack,
        IReadOnlyCollection<string> existing)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!existing.Contains(value, NameComparer))
            {
                target.Add(value);
            }
        }
    }

    private static void AddMissingNames(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack,
        IReadOnlyCollection<string> existing)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!existing.Contains(value, NameComparer))
            {
                target.Add(value);
            }
        }
    }

    private static IReadOnlyList<string> BuildAutosetupDeltaItems(
        IEnumerable<string> supportedSuggestions,
        IEnumerable<string> locSuggestions,
        IEnumerable<string> skippedDirectorySuggestions)
    {
        var items = new List<string>();
        AddDelta("Allowed types", supportedSuggestions);
        AddDelta("LOC types", locSuggestions);
        AddDelta("Skipped folders", skippedDirectorySuggestions);
        return items.Count == 0 ? ["No missing stack rules detected"] : items;

        void AddDelta(string label, IEnumerable<string> values)
        {
            var clean = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(14)
                .ToArray();
            if (clean.Length > 0)
            {
                items.Add($"{label}: {string.Join(", ", clean)}");
            }
        }
    }

    private static ProjectStackRuleSet BuildAutoSetupRuleSet(
        ScanState state,
        IEnumerable<string> stacks,
        IEnumerable<string> supportedSuggestions,
        IEnumerable<string> locSuggestions,
        IEnumerable<string> skippedDirectorySuggestions)
    {
        var ignoredDirectories = new SortedSet<string>(AlwaysSkippedDirectories, NameComparer);
        foreach (var value in state.AutoSkippedDirectoryRules)
        {
            ignoredDirectories.Add(value);
        }

        foreach (var stack in stacks)
        {
            AddKnownValues(ignoredDirectories, SuggestedSkippedDirectoriesByStack, stack);
        }

        foreach (var value in skippedDirectorySuggestions)
        {
            ignoredDirectories.Add(value);
        }

        var supportedExtensions = new SortedSet<string>(NameComparer);
        var locExtensions = new SortedSet<string>(NameComparer);
        foreach (var stack in stacks)
        {
            AddKnownExtensions(supportedExtensions, SuggestedSupportedByStack, stack);
            AddKnownExtensions(locExtensions, SuggestedLocByStack, stack);
        }

        foreach (var extension in state.ExtensionCounts.Keys.Where(ShouldSuggestObservedExtension))
        {
            supportedExtensions.Add(NormalizeExtension(extension));
            if (LanguageByExtension.ContainsKey(extension))
            {
                locExtensions.Add(NormalizeExtension(extension));
            }
        }

        foreach (var value in supportedSuggestions)
        {
            supportedExtensions.Add(NormalizeExtension(value));
        }

        foreach (var value in locSuggestions)
        {
            locExtensions.Add(NormalizeExtension(value));
        }

        var ignoredExtensions = new SortedSet<string>(
            AutoSetupIgnoredExtensions.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)),
            NameComparer);
        foreach (var extension in state.ExtensionCounts.Keys)
        {
            var normalized = NormalizeExtension(extension);
            if (!string.IsNullOrWhiteSpace(normalized) && !supportedExtensions.Contains(normalized))
            {
                ignoredExtensions.Add(normalized);
            }
        }

        foreach (var extension in supportedExtensions)
        {
            ignoredExtensions.Remove(extension);
        }

        return new ProjectStackRuleSet(
            ignoredDirectories.ToArray(),
            AutoSetupIgnoredFileNames.OrderBy(value => value, NameComparer).ToArray(),
            ignoredExtensions.ToArray(),
            supportedExtensions.ToArray(),
            locExtensions.Where(extension => supportedExtensions.Contains(extension)).ToArray());
    }

    private static void AddKnownExtensions(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Select(NormalizeExtension).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }

    private static void AddKnownValues(
        SortedSet<string> target,
        IReadOnlyDictionary<string, string[]> source,
        string stack)
    {
        if (!source.TryGetValue(stack, out var values))
        {
            return;
        }

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }

    private static void AppendSection(StringBuilder builder, string title, IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(title + ":");
        foreach (var item in items)
        {
            builder.AppendLine("  - " + item);
        }
    }

    private static bool StartsWithConfigName(string value, string configName)
    {
        return value.Equals(configName, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(configName + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument? TryReadJsonDocument(FileInfo file)
    {
        try
        {
            if (file.Length > MaxManifestReadBytes)
            {
                return null;
            }

            return JsonDocument.Parse(File.ReadAllText(file.FullName));
        }
        catch
        {
            return null;
        }
    }

    private static string TryReadSmallText(FileInfo file)
    {
        try
        {
            return file.Length > MaxManifestReadBytes ? "" : File.ReadAllText(file.FullName);
        }
        catch
        {
            return "";
        }
    }

    private static void AddManifest(ScanState state, string relativePath)
    {
        AddSample(state.ManifestSamples, relativePath, 18);
    }

    private static void AddStack(ScanState state, string stack, string reason)
    {
        if (!state.StackReasons.TryGetValue(stack, out var reasons))
        {
            reasons = new SortedSet<string>(NameComparer);
            state.StackReasons[stack] = reasons;
        }

        if (reasons.Count < 8)
        {
            reasons.Add(reason);
        }
    }

    private static void AddUse(ScanState state, string use, string reason)
    {
        var cleanUse = NormalizeUseLabel(use);
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "detected" : reason.Trim();
        if (string.IsNullOrWhiteSpace(cleanUse))
        {
            return;
        }

        if (!state.UseReasons.TryGetValue(cleanUse, out var reasons))
        {
            reasons = new SortedSet<string>(NameComparer);
            state.UseReasons[cleanUse] = reasons;
        }

        if (reasons.Count < 8)
        {
            reasons.Add(cleanReason);
        }
    }

    private static void AddSuggestedExtension(ScanState state, string extension, bool isLoc)
    {
        var normalized = NormalizeExtension(extension);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!state.Rules.SupportedExtensions.Contains(normalized, NameComparer))
        {
            state.ExtensionCounts.TryAdd(normalized, 0);
        }

        if (isLoc && !state.Rules.LocExtensions.Contains(normalized, NameComparer))
        {
            state.ExtensionCounts.TryAdd(normalized, 0);
        }
    }

    private static void AddSample(ICollection<string> samples, string value, int max = 10)
    {
        if (samples.Count < max
            && !string.IsNullOrWhiteSpace(value)
            && !samples.Any(sample => string.Equals(sample, value, StringComparison.OrdinalIgnoreCase)))
        {
            samples.Add(value);
        }
    }

    private static void Increment(IDictionary<string, int> map, string key)
    {
        map[key] = map.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static string NormalizeUseLabel(string value)
    {
        var clean = (value ?? string.Empty).Trim();
        while (clean.Contains("  ", StringComparison.Ordinal))
        {
            clean = clean.Replace("  ", " ", StringComparison.Ordinal);
        }

        return clean;
    }

    private static string NormalizePackageManagerName(string value)
    {
        var clean = value.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? value;
        return clean.ToLowerInvariant() switch
        {
            "npm" => "npm",
            "pnpm" => "pnpm",
            "yarn" => "Yarn",
            "bun" => "Bun",
            _ => clean
        };
    }

    private static string NormalizeTechnologyName(string value)
    {
        var clean = StripCMakeToken(value);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        return clean.ToLowerInvariant() switch
        {
            "glfw" or "glfw3" => "GLFW",
            "glm" => "GLM",
            "vulkan" => "Vulkan",
            "jolt" => "Jolt Physics",
            "imgui" or "dearimgui" => "Dear ImGui",
            "opengl" => "OpenGL",
            "sdl" or "sdl2" => "SDL",
            "fmt" => "fmt",
            "spdlog" => "spdlog",
            "entt" => "EnTT",
            "threads" => "Threads",
            _ => clean
        };
    }

    private static bool TryNormalizeLinkedLibrary(string token, out string library)
    {
        library = "";
        var clean = StripCMakeToken(token);
        if (string.IsNullOrWhiteSpace(clean)
            || IsCMakeKeyword(clean)
            || clean.StartsWith("${", StringComparison.Ordinal)
            || clean.StartsWith("$<", StringComparison.Ordinal))
        {
            return false;
        }

        if (clean.Contains("::", StringComparison.Ordinal))
        {
            library = NormalizeTechnologyName(clean.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? clean);
            return !string.IsNullOrWhiteSpace(library);
        }

        var normalized = NormalizeTechnologyName(clean);
        if (normalized != clean || IsKnownExternalLibraryName(normalized))
        {
            library = normalized;
            return true;
        }

        return false;
    }

    private static bool IsKnownExternalLibraryName(string value)
    {
        return value is "Vulkan" or "GLFW" or "GLM" or "Jolt Physics" or "Dear ImGui" or "OpenGL" or "SDL" or "fmt" or "spdlog" or "EnTT" or "Threads";
    }

    private static bool IsCMakeKeyword(string value)
    {
        return value.Equals("PRIVATE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase)
            || value.Equals("INTERFACE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || value.Equals("optimized", StringComparison.OrdinalIgnoreCase)
            || value.Equals("general", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripCMakeToken(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Trim('"', '\'', '(', ')', '[', ']', '{', '}', ',');
    }

    private static IEnumerable<string> SplitCMakeTokens(string value)
    {
        return Regex.Split(value, @"[\s\r\n]+")
            .Select(StripCMakeToken)
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static bool IsRequirementsFileName(string lowerName)
    {
        return lowerName.Equals("requirements.txt", StringComparison.Ordinal)
            || (lowerName.StartsWith("requirements", StringComparison.Ordinal)
                && lowerName.EndsWith(".txt", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ExtractRequirementsPackages(string text)
    {
        foreach (var line in SplitTextLines(text))
        {
            var clean = line.Split('#')[0].Trim();
            if (string.IsNullOrWhiteSpace(clean)
                || clean.StartsWith("-", StringComparison.Ordinal)
                || clean.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(clean, @"^([A-Za-z0-9_.-]+)");
            if (match.Success)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractPythonLockPackageNames(string text)
    {
        foreach (Match match in Regex.Matches(text, @"^\s*name\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static IEnumerable<string> ExtractYamlDependencyNames(string text)
    {
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var clean = trimmed[1..].Trim();
            if (clean.StartsWith("pip:", StringComparison.OrdinalIgnoreCase)
                || clean.StartsWith("python", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(clean, @"^([A-Za-z0-9_.-]+)");
            if (match.Success)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractTomlDependencyNames(string text)
    {
        foreach (var package in ExtractTomlSectionKeys(text, "tool.poetry.dependencies", "project.optional-dependencies"))
        {
            yield return package;
        }

        foreach (Match match in Regex.Matches(text, @"dependencies\s*=\s*\[(?<body>[^\]]{1,5000})\]", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            foreach (Match packageMatch in Regex.Matches(match.Groups["body"].Value, @"[""']([A-Za-z0-9_.-]+)"))
            {
                var package = packageMatch.Groups[1].Value;
                if (!package.Equals("python", StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractDescriptionPackages(string text)
    {
        foreach (Match match in Regex.Matches(text, @"^(?:Depends|Imports|Suggests|LinkingTo):\s*(?<body>[^\r\n]+(?:\r?\n\s+[^\r\n]+)*)", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            foreach (var package in SplitDependencyList(match.Groups["body"].Value))
            {
                if (!package.Equals("R", StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractYamlListValues(string text, params string[] sectionNames)
    {
        var active = false;
        var sections = sectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_-]+:\s*$"))
            {
                active = sections.Contains(trimmed.TrimEnd(':'));
                continue;
            }

            if (!active || !trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var clean = trimmed[1..].Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                yield return ShortPackageName(clean);
            }
        }
    }

    private static IEnumerable<string> SplitDependencyList(string value)
    {
        foreach (var token in Regex.Split(value ?? string.Empty, @"[,\s]+"))
        {
            var clean = token.Trim()
                .Trim('"', '\'', '(', ')', '[', ']', '{', '}', ',')
                .Split(['<', '>', '=', '~'], 2, StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "";
            if (LooksLikePackageToken(clean))
            {
                yield return ShortPackageName(clean);
            }
        }
    }

    private static bool LooksLikePackageToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || char.IsDigit(value[0])
            || value is "and" or "or" or "&&" or "||")
        {
            return false;
        }

        return Regex.IsMatch(value, @"^[A-Za-z_@][A-Za-z0-9_.+/@-]*$");
    }

    private static string ShortPackageName(string value)
    {
        var clean = (value ?? string.Empty).Trim().Trim('"', '\'', ',', ';');
        if (clean.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^4];
        }

        clean = clean.Replace('\\', '/');
        var last = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(last) ? clean : last;
    }

    private static IEnumerable<string> ExtractTomlSectionKeys(string text, params string[] sectionNames)
    {
        var active = false;
        var sections = sectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitTextLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                var section = trimmed.Trim('[', ']');
                active = sections.Contains(section);
                continue;
            }

            if (!active || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Regex.Match(trimmed, @"^([A-Za-z0-9_.-]+)\s*=");
            if (match.Success)
            {
                var package = match.Groups[1].Value;
                if (!package.Equals("python", StringComparison.OrdinalIgnoreCase))
                {
                    yield return package;
                }
            }
        }
    }

    private static IEnumerable<string> SplitTextLines(string text)
    {
        return (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeExtension(string extension)
    {
        var clean = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private static string NormalizePath(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.Trim('/');
    }

    private static bool ShouldSuggestObservedExtension(string extension)
    {
        return SourceOrConfigSuggestionExtensions.Contains(NormalizeExtension(extension));
    }

    private static HashSet<string> BuildSourceOrConfigSuggestionExtensions()
    {
        var extensions = new HashSet<string>(NameComparer);
        foreach (var extension in LanguageByExtension.Keys)
        {
            extensions.Add(extension);
        }

        foreach (var extension in new[]
        {
            ".asmdef",
            ".bat",
            ".cfg",
            ".cmake",
            ".cmd",
            ".config",
            ".cabal",
            ".dockerignore",
            ".editorconfig",
            ".edn",
            ".env",
            ".gemspec",
            ".gitignore",
            ".gradle",
            ".jsonc",
            ".json",
            ".lock",
            ".make",
            ".md",
            ".mod",
            ".nimble",
            ".opam",
            ".props",
            ".rproj",
            ".rockspec",
            ".sbt",
            ".sln",
            ".slnx",
            ".sum",
            ".targets",
            ".toml",
            ".xml",
            ".yaml",
            ".yml"
        })
        {
            extensions.Add(extension);
        }

        return extensions;
    }

    private sealed class ScanState(string projectRoot, ProjectFileRules rules)
    {
        public string ProjectRoot { get; } = projectRoot;
        public ProjectFileRules Rules { get; } = rules;
        public int DirectoriesVisited { get; set; }
        public int DirectoriesExcluded { get; set; }
        public int DirectoriesSkippedByRules { get; set; }
        public int FilesSeen { get; set; }
        public int FilesSkippedByRules { get; set; }
        public int VisibleFiles { get; set; }
        public int TrackedFiles { get; set; }
        public int UnsupportedVisibleFiles { get; set; }
        public int TextSignalFilesScanned { get; set; }
        public bool LimitHit { get; set; }
        public Dictionary<string, int> ExtensionCounts { get; } = new(NameComparer);
        public Dictionary<string, int> UnsupportedExtensionCounts { get; } = new(NameComparer);
        public Dictionary<string, int> LanguageCounts { get; } = new(NameComparer);
        public Dictionary<string, SortedSet<string>> StackReasons { get; } = new(NameComparer);
        public Dictionary<string, SortedSet<string>> UseReasons { get; } = new(NameComparer);
        public SortedSet<string> AutoSkippedDirectoryRules { get; } = new(NameComparer);
        public List<string> ManifestSamples { get; } = [];
        public List<string> SkippedDirectorySamples { get; } = [];
        public List<string> SkippedFileSamples { get; } = [];
    }
}

internal sealed record TechnologyPattern(
    string Name,
    IReadOnlyList<string> Needles,
    IReadOnlyList<string> Extensions);

public sealed record ProjectStackScanResult(
    string Summary,
    string DetailsText,
    string ProjectRoot,
    string StackLabel,
    string RuleSummary,
    string ScanSummary,
    IReadOnlyList<ProjectStackMetric> Metrics,
    IReadOnlyList<ProjectStackSection> Sections,
    ProjectStackRuleSet AutoSetupRules);

public sealed record ProjectStackMetric(string Key, string Value, string Detail);

public sealed record ProjectStackSection(string Title, IReadOnlyList<string> Items);

public sealed record ProjectStackRuleSet(
    IReadOnlyList<string> IgnoredDirectories,
    IReadOnlyList<string> IgnoredFileNames,
    IReadOnlyList<string> IgnoredExtensions,
    IReadOnlyList<string> SupportedExtensions,
    IReadOnlyList<string> LocExtensions)
{
    public static ProjectStackRuleSet Empty() => new([], [], [], [], []);
}
