// CC-DESC: Coordinates project tabs, tree selection, history, and external-change queues.

using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    public const string RuleKindIgnoredDirectories = "ignoredDirectories";
    public const string RuleKindIgnoredFileNames = "ignoredFileNames";
    public const string RuleKindIgnoredFileTypes = "ignoredFileTypes";
    public const string RuleKindSupportedFileTypes = "supportedFileTypes";
    public const string RuleKindLocFileTypes = "locFileTypes";
    private const int DefaultExpandedDepth = 2;
    private const string FontAssets = "avares://ContextControl.Workbench/Assets/Fonts";

    private readonly Dictionary<string, ProjectWorkspaceState> _workspaceByProjectId = [];
    private readonly Dictionary<string, ExternalChangeTracker> _trackersByProjectId = [];
    private readonly WorkbenchSettings _workbenchSettings;
    private readonly SynchronizationContext? _uiContext;
    private readonly Timer _externalScanTimer;
    private int _projectSwitchVersion;
    private int _documentLoadVersion;
    private Dictionary<string, FileHistoryViewModel> _historyByPath;
    private ProjectTabViewModel? _currentProject;
    private ProjectNodeViewModel? _selectedNode;
    private TreeRowViewModel? _selectedTreeRow;
    private FileHistoryViewModel? _selectedHistory;
    private VersionEntryViewModel? _selectedVersion;
    private EditorDocumentViewModel? _activeDocument;
    private ThemeOptionViewModel _selectedTheme;
    private ThemeOptionViewModel _selectedSyntaxTheme;
    private ThemeOptionViewModel _selectedCodeFont;
    private ThemeOptionViewModel _selectedUiFont;
    private ThemeOptionViewModel _selectedSkin;
    private ThemeOptionViewModel _selectedFoldArrowPosition;
    private WorkbenchModeOptionViewModel _selectedWorkspaceMode;
    private bool _showFoldArrows;
    private bool _showSummaryArrowBorders;
    private bool _useParentChildArrowIndentation;
    private bool _showVerticalScopeLines;
    private bool _useColorfulFamilies;
    private bool _showAppearanceCodePreview;
    private bool _summarizeNamespace;
    private bool _summarizeClass;
    private bool _summarizeStruct;
    private bool _summarizeInterface;
    private bool _summarizeEnum;
    private bool _summarizeMethod;
    private bool _summarizeProperty;
    private bool _summarizeObject;
    private bool _summarizeBlock;
    private bool _summarizeArray;
    private bool _summarizeArguments;
    private bool _isHistoryOpen;
    private bool _showFileDetails = true;
    private bool _showSkippedFiles;
    private double _historyWidth;
    private double _historyOpacity;
    private double _historyGutter;
    private string _supportedFileTypesLabel = "";
    private string _ignoredFileTypesLabel = "";
    private string _fileRulesPath = "";
    private string _supportedFileTypesText = "";
    private string _ignoredFileTypesText = "";
    private string _ignoredFileNamesText = "";
    private string _ignoredDirectoriesText = "";
    private string _newIgnoredDirectoryRuleText = "";
    private string _newIgnoredFileNameRuleText = "";
    private string _newIgnoredFileTypeRuleText = "";
    private string _newSupportedFileTypeRuleText = "";
    private string _locFileTypesText = "";
    private string _newLocFileTypeRuleText = "";
    private string _fileRulesStatus = "";
    private string _projectScanSummary = "No scan yet.";
    private string _projectScanResultText = "No scan yet.";
    private string _projectScanRuleSummary = "";
    private string _projectScanAutoSetupStatus = "";
    private string _projectGraphSummary = "No project loaded.";
    private string _projectGraphTreeText = "No project loaded.";
    private ProjectNodeViewModel? _projectGraphSelectedNode;
    private int _projectGraphVersion;
    private int _projectGraphCenterVersion;
    private int _projectTreeFocusVersion;
    private string _projectGraphSearchText = "";
    private bool _isProjectGraphSearchOpen;
    private List<ProjectGraphSearchEntry>? _projectGraphSearchIndex;
    private bool _isProjectFilesPaneOpen;
    private bool _isBrowserRoutingPaneOpen;
    private bool _isProjectGraphTreePaneOpen;
    private bool _isProjectScanRunning;

    private WorkbenchViewModel(
        ObservableCollection<ProjectTabViewModel> projects,
        ObservableCollection<ProjectNodeViewModel> projectTree,
        Dictionary<string, FileHistoryViewModel> historyByPath,
        ProjectFileRules? fileRules = null,
        bool treeStatePrepared = false,
        WorkbenchSettings? workbenchSettings = null)
    {
        _uiContext = SynchronizationContext.Current;
        _workbenchSettings = workbenchSettings ?? WorkbenchSettings.Load();
        ContextControl = new ContextControlViewModel(_workbenchSettings);
        ContextControl.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ContextControlViewModel.IsPromptOpen))
            {
                OnPropertyChanged(nameof(PromptWindowViewLabel));
            }
        };
        BrowserPane = new BrowserPaneViewModel(
            _workbenchSettings.ContextControlRoot,
            ExternalBrowserService.DetectTargets(),
            _workbenchSettings.ExternalBrowserKey);
        BrowserPane.ExternalBrowserSelectionChanged += (_, key) =>
        {
            _workbenchSettings.ExternalBrowserKey = key;
            SaveAppearanceSettings();
        };
        Projects = projects;
        ProjectTree = projectTree;
        ExternalChanges = [];
        ProjectScanMetrics = [];
        ProjectScanSections = [];
        ProjectScanIdentitySections = [];
        ProjectScanFileSections = [];
        ProjectScanRuleSections = [];
        ProjectScanDiagnosticSections = [];
        IgnoredDirectoryRules = [];
        IgnoredFileNameRules = [];
        IgnoredFileTypeRules = [];
        SupportedFileTypeRules = [];
        LocFileTypeRules = [];
        Themes =
        [
            new ThemeOptionViewModel("empty", "Porcelain", "neutral light workbench with quiet blue-green state accents", null, "Light", "Experimental"),
            new ThemeOptionViewModel("alabaster", "Alabaster", "warm low-glare light palette with brass and teal accents for long sessions", null, "Light", "Professional"),
            new ThemeOptionViewModel("pearl", "Pearl", "cool soft-gray light theme with polished steel-blue accents", null, "Light", "Professional"),
            new ThemeOptionViewModel("opal", "Opal", "clean light workspace with quiet neutrals and muted jewel accents", null, "Light", "Professional"),
            new ThemeOptionViewModel("mist", "Mist", "whiteish cool-gray workspace with soft blue-green focus accents", null, "Light", "Calm"),
            new ThemeOptionViewModel("limestone", "Limestone", "whiteish stone-gray theme with muted sage and mineral blue accents", null, "Light", "Calm"),
            new ThemeOptionViewModel("dark", "Graphite", "low-glare industry-style dark theme with muted steel accents", null, "Dark", "Classic"),
            new ThemeOptionViewModel("nocturne", "Nocturne", "deep blue-black dark theme with low-glare surfaces and refined cyan contrast", null, "Dark", "Professional"),
            new ThemeOptionViewModel("onyx", "Onyx", "charcoal luxury dark theme with restrained brass accents", null, "Dark", "Professional"),
            new ThemeOptionViewModel("smoke", "Smoke", "neutral dark gray workbench with soft sage highlights for sustained focus", null, "Dark", "Professional"),
            new ThemeOptionViewModel("carbon", "Carbon", "black and graphite workbench with desaturated steel-blue accents", null, "Dark", "Calm"),
            new ThemeOptionViewModel("obsidian", "Obsidian", "near-black polished theme with quiet platinum and mineral-blue contrast", null, "Dark", "Calm"),
            new ThemeOptionViewModel("ash", "Ash", "soft smoky gray theme with muted sage accents for long focused work", null, "Dark", "Calm"),
            new ThemeOptionViewModel("graphene", "Graphene", "cool black-gray technical theme with subdued teal-blue highlights", null, "Dark", "Calm"),
            new ThemeOptionViewModel("solarized", "Solarized", "low-glare classic light palette with balanced yellow and blue", null, "Light", "Classic"),
            new ThemeOptionViewModel("cobalt", "Cobalt", "blue technical theme with crisp file-tree separation", null, "Dark", "Classic"),
            new ThemeOptionViewModel("contrast", "Contrast", "high-contrast dark workbench for maximum separation", null, "Dark", "Classic"),
            new ThemeOptionViewModel("verdant", "Verdant", "calm green light theme for long project scans", null, "Light", "Experimental"),
            new ThemeOptionViewModel("ember", "Ember", "orange dark theme with soft graphite panels", null, "Dark", "Experimental"),
            new ThemeOptionViewModel("ruby", "Ruby", "deep red command surface with clean neutral contrast", null, "Dark", "Colorful"),
            new ThemeOptionViewModel("amethyst", "Amethyst", "purple dark theme with measured violet highlights", null, "Dark", "Colorful"),
            new ThemeOptionViewModel("matrix", "Phosphor", "green phosphor variant with restrained IDE contrast", null, "Dark", "Colorful")
        ];
        SyntaxThemes =
        [
            new ThemeOptionViewModel("adaptive", "Adaptive", "matches the selected IDE theme"),
            new ThemeOptionViewModel("github-dark", "GitHub Dark", "familiar industry standard contrast"),
            new ThemeOptionViewModel("one-dark", "One Dark", "Atom-style warm dark palette"),
            new ThemeOptionViewModel("monokai", "Monokai", "classic high-separation editor colors"),
            new ThemeOptionViewModel("solarized-light", "Solarized Light", "low-glare light palette"),
            new ThemeOptionViewModel("solarized-dark", "Solarized Dark", "low-glare dark palette"),
            new ThemeOptionViewModel("high-contrast-dark", "High Contrast", "maximum token separation")
        ];
        CodeFonts =
        [
            new ThemeOptionViewModel("cascadia-code", "Cascadia Code", "modern Windows programming font with ligature support", $"{FontAssets}#Cascadia Code, Consolas"),
            new ThemeOptionViewModel("jetbrains-mono", "JetBrains Mono", "popular IDE font with tall x-height and crisp punctuation", $"{FontAssets}#JetBrains Mono, Consolas"),
            new ThemeOptionViewModel("fira-code", "Fira Code", "widely used ligature font for dense code views", $"{FontAssets}#Fira Code, Consolas"),
            new ThemeOptionViewModel("source-code-pro", "Source Code Pro", "Adobe's readable monospace for long editing sessions", $"{FontAssets}#Source Code Pro, Consolas"),
            new ThemeOptionViewModel("ibm-plex-mono", "IBM Plex Mono", "technical monospace with IBM terminal heritage", $"{FontAssets}#IBM Plex Mono, Consolas"),
            new ThemeOptionViewModel("hack", "Hack", "practical open-source coding font tuned for terminals", $"{FontAssets}#Hack, Consolas"),
            new ThemeOptionViewModel("iosevka", "Iosevka", "compact programming font for information-dense panes", $"{FontAssets}#Iosevka, Consolas"),
            new ThemeOptionViewModel("victor-mono", "Victor Mono", "distinctive coding font with cursive italic support", $"{FontAssets}#Victor Mono, Consolas"),
            new ThemeOptionViewModel("consolas", "Consolas", "classic ClearType Windows programming font", "Consolas, Cascadia Mono"),
            new ThemeOptionViewModel("lucida-console", "Lucida Console", "legacy Windows console face with sturdy glyphs", "Lucida Console, Consolas"),
            new ThemeOptionViewModel("courier-new", "Courier New", "1950s typewriter lineage for old-school code texture", "Courier New, Courier, Consolas"),
            new ThemeOptionViewModel("ocr-a", "OCR-A", "1960s machine-readable style with retro engineering character", "OCR A Extended, Courier New, Consolas"),
            new ThemeOptionViewModel("fixedsys", "Fixedsys", "bitmap-era Windows terminal feel", "Fixedsys, Terminal, Lucida Console, Consolas"),
            new ThemeOptionViewModel("ibm-3270", "IBM 3270", "mainframe terminal look with practical fallbacks", "IBM 3270, 3270, Fixedsys, Lucida Console, Consolas"),
            new ThemeOptionViewModel("vt-retro", "VT Terminal", "DEC-style terminal stack for retro command panes", "VT323, DEC Terminal, Terminal, Lucida Console, Consolas"),
            new ThemeOptionViewModel("space-mono", "Space Mono", "geometric monospace with a 1960s aerospace mood", "Space Mono, Cascadia Code, Consolas")
        ];
        UiFonts =
        [
            new ThemeOptionViewModel("aptos", "Aptos", "current Microsoft UI default with compact readable forms", "Aptos, Arial, Segoe UI"),
            new ThemeOptionViewModel("inter", "Inter", "polished app UI font with excellent small-size clarity", "fonts:Inter, Segoe UI"),
            new ThemeOptionViewModel("segoe-ui", "Segoe UI", "classic Windows interface font", "Segoe UI Variable Text, Segoe UI, Aptos"),
            new ThemeOptionViewModel("ibm-plex-sans", "IBM Plex Sans", "neutral technical UI face with strong numerals", $"{FontAssets}#IBM Plex Sans, Segoe UI"),
            new ThemeOptionViewModel("roboto", "Roboto", "familiar Material-style UI font", $"{FontAssets}#Roboto, Segoe UI"),
            new ThemeOptionViewModel("sf-pro", "SF Pro", "Apple-style system UI stack", "SF Pro Text, SF Pro Display, Trebuchet MS, Segoe UI"),
            new ThemeOptionViewModel("helvetica-neue", "Helvetica Neue", "clean Swiss-style app interface stack", "Helvetica Neue, Helvetica, Arial"),
            new ThemeOptionViewModel("arial", "Arial", "portable sans-serif fallback with broad coverage", "Arial, Helvetica"),
            new ThemeOptionViewModel("verdana", "Verdana", "wide screen-first UI font for small text", "Verdana, Segoe UI"),
            new ThemeOptionViewModel("tahoma", "Tahoma", "legacy Windows UI face with tight spacing", "Tahoma, Segoe UI"),
            new ThemeOptionViewModel("trebuchet", "Trebuchet MS", "older web UI face with warmer forms", "Trebuchet MS, Segoe UI")
        ];
        Skins = new ObservableCollection<ThemeOptionViewModel>(
            WorkbenchSkins.All.Select(skin => new ThemeOptionViewModel(skin.Key, skin.Name, skin.Description)));
        FoldArrowPositions =
        [
            new ThemeOptionViewModel("codeEditor", "Code Editor", "render summary arrows next to the code text"),
            new ThemeOptionViewModel("locBlock", "LOC block", "render summary arrows inside the line-number gutter")
        ];
        WorkspaceModes =
        [
            new WorkbenchModeOptionViewModel("code", "Code Editor"),
            new WorkbenchModeOptionViewModel("graph", "Graph"),
            new WorkbenchModeOptionViewModel("browser", "Browser"),
            new WorkbenchModeOptionViewModel("scanner", "Project scanner")
        ];
        _selectedTheme = FindOptionByKey(Themes, _workbenchSettings.ThemeKey, Themes[0]);
        _selectedSyntaxTheme = FindOptionByKey(SyntaxThemes, _workbenchSettings.SyntaxThemeKey, SyntaxThemes[0]);
        _selectedCodeFont = FindOptionByKey(CodeFonts, _workbenchSettings.CodeFontKey, CodeFonts[0]);
        _selectedUiFont = FindOptionByKey(UiFonts, _workbenchSettings.UiFontKey, UiFonts[0]);
        _selectedSkin = FindOptionByKey(Skins, _workbenchSettings.SkinKey, Skins[0]);
        _selectedFoldArrowPosition = FindOptionByKey(FoldArrowPositions, _workbenchSettings.FoldArrowPositionKey, FoldArrowPositions[0]);
        _selectedWorkspaceMode = FindModeByKey(_workbenchSettings.WorkspaceModeKey);
        _selectedWorkspaceMode.IsActive = true;
        _showFoldArrows = _workbenchSettings.ShowFoldArrows;
        _showSummaryArrowBorders = _workbenchSettings.ShowSummaryArrowBorders;
        _useParentChildArrowIndentation = _workbenchSettings.UseParentChildArrowIndentation;
        _showVerticalScopeLines = _workbenchSettings.ShowVerticalScopeLines;
        _useColorfulFamilies = _workbenchSettings.UseColorfulFamilies;
        _showAppearanceCodePreview = _workbenchSettings.ShowAppearanceCodePreview;
        _showSkippedFiles = _workbenchSettings.ShowSkippedFiles;
        _isProjectFilesPaneOpen = _workbenchSettings.ShowProjectFilesPane;
        _isBrowserRoutingPaneOpen = _workbenchSettings.ShowBrowserRoutingPane;
        _isProjectGraphTreePaneOpen = _workbenchSettings.ShowProjectGraphTreePane;
        ApplySummaryFoldKinds(_workbenchSettings.SummaryFoldKinds);
        SaveAppearanceSettings();
        _historyByPath = historyByPath;
        foreach (var project in projects)
        {
            var rules = fileRules ?? ProjectFileRules.Load(project.ProjectRoot);
            rules.Save();
            _workspaceByProjectId[project.Id] = new ProjectWorkspaceState(
                CloneTree(projectTree),
                new Dictionary<string, FileHistoryViewModel>(historyByPath),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new ObservableCollection<ExternalChangeItemViewModel>(),
                rules,
                treeStatePrepared);
        }

        SelectProjectCommand = new RelayCommand<ProjectTabViewModel>(SelectProject);
        ToggleNodeCommand = new RelayCommand<TreeRowViewModel>(ToggleNode, row => row?.HasChildren == true);
        IncludeExternalNodeCommand = new RelayCommand<ProjectNodeViewModel>(node => _ = IncludeExternalNodeAsync(node), node => node?.CanIncludeExternal == true);
        OpenVersionCommand = new RelayCommand<VersionEntryViewModel>(
            OpenVersion,
            version => !string.IsNullOrWhiteSpace(version?.SnapshotPath)
                || !string.IsNullOrWhiteSpace(version?.CurrentFilePath));
        OpenHistoryCommand = new RelayCommand<object>(_ => OpenHistory());
        CloseHistoryCommand = new RelayCommand<object>(_ => CloseHistory());
        ToggleFileDetailsCommand = new RelayCommand<object>(_ => ShowFileDetails = !ShowFileDetails);
        ToggleSkippedFilesCommand = new RelayCommand<object>(_ => _ = ToggleSkippedFilesAsync());
        ToggleProjectFilesPaneCommand = new RelayCommand<object>(_ => IsProjectFilesPaneOpen = !IsProjectFilesPaneOpen);
        ToggleBrowserRoutingPaneCommand = new RelayCommand<object>(_ => IsBrowserRoutingPaneOpen = !IsBrowserRoutingPaneOpen);
        ToggleProjectGraphTreePaneCommand = new RelayCommand<object>(_ => IsProjectGraphTreePaneOpen = !IsProjectGraphTreePaneOpen);
        TogglePromptWindowCommand = new RelayCommand<object>(_ => ContextControl.IsPromptOpen = !ContextControl.IsPromptOpen);
        OpenProjectGraphSearchCommand = new RelayCommand<object>(_ => OpenProjectGraphSearch());
        CloseProjectGraphSearchCommand = new RelayCommand<object>(_ => CloseProjectGraphSearch());
        SelectProjectGraphSearchSuggestionCommand = new RelayCommand<ProjectGraphSearchSuggestionViewModel>(SelectProjectGraphSearchSuggestion);
        SwitchWorkspaceModeCommand = new RelayCommand<WorkbenchModeOptionViewModel>(SwitchWorkspaceMode);
        AcceptAllExternalChangesCommand = new RelayCommand<object>(_ => AcceptAllExternalChanges());
        AcceptFinalExternalChangesCommand = new RelayCommand<object>(_ => AcceptFinalExternalChanges());
        AcceptSelectedExternalChangesCommand = new RelayCommand<object>(_ => AcceptSelectedExternalChanges());
        DismissSelectedExternalChangesCommand = new RelayCommand<object>(_ => DismissSelectedExternalChanges());
        ToggleExternalChangeCommand = new RelayCommand<ExternalChangeItemViewModel>(ToggleExternalChange);
        OpenExternalChangeCommand = new RelayCommand<ExternalChangeItemViewModel>(OpenExternalChange);
        OpenAttachmentCommand = new RelayCommand<string>(OpenAttachment);
        AddFileRuleEntryCommand = new RelayCommand<string>(AddFileRuleEntry);
        RemoveFileRuleEntryCommand = new RelayCommand<FileRuleEntryViewModel>(RemoveFileRuleEntry);
        SaveFileRulesCommand = new RelayCommand<object>(_ => _ = SaveFileRulesAsync());
        ResetFileRulesCommand = new RelayCommand<object>(_ => _ = ResetFileRulesAsync());
        ScanProjectRulesCommand = new RelayCommand<object>(_ => _ = ScanProjectRulesAsync());
        AutoSetupProjectRulesCommand = new RelayCommand<object>(_ => _ = AutoSetupProjectRulesAsync());

        if (!treeStatePrepared)
        {
            PrepareTree();
        }

        SelectProject(projects.FirstOrDefault());

        ActiveDocument = EditorDocumentViewModel.Empty();
        // FileSystemWatcher plus the tracker's own background poll handles changes.
        // A UI-thread full-scan timer made medium projects feel frozen.
        _externalScanTimer = new Timer(_ => PostToUi(ScanExternalChangesNow), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public ObservableCollection<ProjectTabViewModel> Projects { get; }
    public ObservableCollection<ProjectNodeViewModel> ProjectTree { get; }
    public List<ProjectNodeViewModel> VisibleProjectNodes { get; } = [];
    public AvaloniaList<TreeRowViewModel> VisibleTreeRows { get; } = [];
    public AvaloniaList<TreeRowViewModel> SelectedTreeRows { get; } = [];
    public ObservableCollection<ExternalChangeItemViewModel> ExternalChanges { get; }
    public ContextControlViewModel ContextControl { get; }
    public BrowserPaneViewModel BrowserPane { get; }
    public ObservableCollection<ProjectStackMetric> ProjectScanMetrics { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanSections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanIdentitySections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanFileSections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanRuleSections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanDiagnosticSections { get; }
    public ObservableCollection<FileRuleEntryViewModel> IgnoredDirectoryRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> IgnoredFileNameRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> IgnoredFileTypeRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> SupportedFileTypeRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> LocFileTypeRules { get; }
    public ObservableCollection<ProjectGraphSearchSuggestionViewModel> ProjectGraphSearchSuggestions { get; } = [];
    public ObservableCollection<ThemeOptionViewModel> Themes { get; }
    public ObservableCollection<ThemeOptionViewModel> SyntaxThemes { get; }
    public ObservableCollection<ThemeOptionViewModel> CodeFonts { get; }
    public ObservableCollection<ThemeOptionViewModel> UiFonts { get; }
    public ObservableCollection<ThemeOptionViewModel> Skins { get; }
    public ObservableCollection<ThemeOptionViewModel> FoldArrowPositions { get; }
    public ObservableCollection<WorkbenchModeOptionViewModel> WorkspaceModes { get; }
    public ICommand SelectProjectCommand { get; }
    public ICommand ToggleNodeCommand { get; }
    public ICommand IncludeExternalNodeCommand { get; }
    public ICommand OpenVersionCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand CloseHistoryCommand { get; }
    public ICommand ToggleFileDetailsCommand { get; }
    public ICommand ToggleSkippedFilesCommand { get; }
    public ICommand ToggleProjectFilesPaneCommand { get; }
    public ICommand ToggleBrowserRoutingPaneCommand { get; }
    public ICommand ToggleProjectGraphTreePaneCommand { get; }
    public ICommand TogglePromptWindowCommand { get; }
    public ICommand OpenProjectGraphSearchCommand { get; }
    public ICommand CloseProjectGraphSearchCommand { get; }
    public ICommand SelectProjectGraphSearchSuggestionCommand { get; }
    public ICommand SwitchWorkspaceModeCommand { get; }
    public ICommand AcceptAllExternalChangesCommand { get; }
    public ICommand AcceptFinalExternalChangesCommand { get; }
    public ICommand AcceptSelectedExternalChangesCommand { get; }
    public ICommand DismissSelectedExternalChangesCommand { get; }
    public ICommand ToggleExternalChangeCommand { get; }
    public ICommand OpenExternalChangeCommand { get; }
    public ICommand OpenAttachmentCommand { get; }
    public ICommand AddFileRuleEntryCommand { get; }
    public ICommand RemoveFileRuleEntryCommand { get; }
    public ICommand SaveFileRulesCommand { get; }
    public ICommand ResetFileRulesCommand { get; }
    public ICommand ScanProjectRulesCommand { get; }
    public ICommand AutoSetupProjectRulesCommand { get; }

    public ThemeOptionViewModel SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedTheme, value))
            {
                OnPropertyChanged(nameof(ThemeKey));
                OnPropertyChanged(nameof(SyntaxThemeKey));
                SaveAppearanceSettings();
            }
        }
    }

    public string ThemeKey => ActiveSkin.IsActive ? ActiveSkin.ThemeKey : SelectedTheme.Key;

    public ThemeOptionViewModel SelectedSyntaxTheme
    {
        get => _selectedSyntaxTheme;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedSyntaxTheme, value))
            {
                OnPropertyChanged(nameof(SyntaxThemeKey));
                SaveAppearanceSettings();
            }
        }
    }

    public string SyntaxThemeKey => ActiveSkin.IsActive ? ActiveSkin.SyntaxThemeKey : SelectedSyntaxTheme.Key;

    public ThemeOptionViewModel SelectedCodeFont
    {
        get => _selectedCodeFont;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedCodeFont, value))
            {
                OnPropertyChanged(nameof(CodeFontKey));
                OnPropertyChanged(nameof(CodeFontFamily));
                SaveAppearanceSettings();
            }
        }
    }

    public string CodeFontKey => SelectedCodeFont.Key;
    public string CodeFontFamily => ActiveSkin.IsActive ? ActiveSkin.CodeFontFamily : SelectedCodeFont.FontFamily;

    public ThemeOptionViewModel SelectedUiFont
    {
        get => _selectedUiFont;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedUiFont, value))
            {
                OnPropertyChanged(nameof(UiFontKey));
                OnPropertyChanged(nameof(UiFontFamily));
                SaveAppearanceSettings();
            }
        }
    }

    public string UiFontKey => SelectedUiFont.Key;
    public string UiFontFamily => ActiveSkin.IsActive ? ActiveSkin.UiFontFamily : SelectedUiFont.FontFamily;

    public ThemeOptionViewModel SelectedSkin
    {
        get => _selectedSkin;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedSkin, value))
            {
                NotifySkinAppearanceChanged();
                SaveAppearanceSettings();
            }
        }
    }

    public string SkinKey => SelectedSkin.Key;
    public bool IsSkinActive => ActiveSkin.IsActive;
    public bool AppearanceOptionsEnabled => !IsSkinActive;
    private WorkbenchSkinDefinition ActiveSkin => WorkbenchSkins.For(SelectedSkin.Key);

    private void NotifySkinAppearanceChanged()
    {
        OnPropertyChanged(nameof(SkinKey));
        OnPropertyChanged(nameof(IsSkinActive));
        OnPropertyChanged(nameof(AppearanceOptionsEnabled));
        OnPropertyChanged(nameof(ThemeKey));
        OnPropertyChanged(nameof(SyntaxThemeKey));
        OnPropertyChanged(nameof(CodeFontFamily));
        OnPropertyChanged(nameof(UiFontFamily));
    }

    public ThemeOptionViewModel SelectedFoldArrowPosition
    {
        get => _selectedFoldArrowPosition;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedFoldArrowPosition, value))
            {
                OnPropertyChanged(nameof(FoldArrowsInCodeEditor));
                OnPropertyChanged(nameof(CanUseParentChildArrowIndentation));
                OnPropertyChanged(nameof(EffectiveUseParentChildArrowIndentation));
                SaveAppearanceSettings();
            }
        }
    }

    public bool FoldArrowsInCodeEditor =>
        string.Equals(SelectedFoldArrowPosition.Key, "codeEditor", StringComparison.OrdinalIgnoreCase);

    public bool CanUseParentChildArrowIndentation => true;

    public bool EffectiveUseParentChildArrowIndentation =>
        UseParentChildArrowIndentation;

    public bool ShowFoldArrows
    {
        get => _showFoldArrows;
        set
        {
            if (SetProperty(ref _showFoldArrows, value))
            {
                OnPropertyChanged(nameof(CanUseParentChildArrowIndentation));
                OnPropertyChanged(nameof(EffectiveUseParentChildArrowIndentation));
                SaveAppearanceSettings();
            }
        }
    }

    public bool ShowSummaryArrowBorders
    {
        get => _showSummaryArrowBorders;
        set
        {
            if (SetProperty(ref _showSummaryArrowBorders, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public bool UseParentChildArrowIndentation
    {
        get => _useParentChildArrowIndentation;
        set
        {
            if (SetProperty(ref _useParentChildArrowIndentation, value))
            {
                OnPropertyChanged(nameof(EffectiveUseParentChildArrowIndentation));
                SaveAppearanceSettings();
            }
        }
    }

    public bool ShowVerticalScopeLines
    {
        get => _showVerticalScopeLines;
        set
        {
            if (SetProperty(ref _showVerticalScopeLines, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public string SummaryFoldKinds => BuildSummaryFoldKinds();

    public bool SummarizeNamespace
    {
        get => _summarizeNamespace;
        set => SetSummaryKind(ref _summarizeNamespace, value, nameof(SummarizeNamespace));
    }

    public bool SummarizeClass
    {
        get => _summarizeClass;
        set => SetSummaryKind(ref _summarizeClass, value, nameof(SummarizeClass));
    }

    public bool SummarizeStruct
    {
        get => _summarizeStruct;
        set => SetSummaryKind(ref _summarizeStruct, value, nameof(SummarizeStruct));
    }

    public bool SummarizeInterface
    {
        get => _summarizeInterface;
        set => SetSummaryKind(ref _summarizeInterface, value, nameof(SummarizeInterface));
    }

    public bool SummarizeEnum
    {
        get => _summarizeEnum;
        set => SetSummaryKind(ref _summarizeEnum, value, nameof(SummarizeEnum));
    }

    public bool SummarizeMethod
    {
        get => _summarizeMethod;
        set => SetSummaryKind(ref _summarizeMethod, value, nameof(SummarizeMethod));
    }

    public bool SummarizeProperty
    {
        get => _summarizeProperty;
        set => SetSummaryKind(ref _summarizeProperty, value, nameof(SummarizeProperty));
    }

    public bool SummarizeObject
    {
        get => _summarizeObject;
        set => SetSummaryKind(ref _summarizeObject, value, nameof(SummarizeObject));
    }

    public bool SummarizeBlock
    {
        get => _summarizeBlock;
        set => SetSummaryKind(ref _summarizeBlock, value, nameof(SummarizeBlock));
    }

    public bool SummarizeArray
    {
        get => _summarizeArray;
        set => SetSummaryKind(ref _summarizeArray, value, nameof(SummarizeArray));
    }

    public bool SummarizeArguments
    {
        get => _summarizeArguments;
        set => SetSummaryKind(ref _summarizeArguments, value, nameof(SummarizeArguments));
    }

    public bool UseColorfulFamilies
    {
        get => _useColorfulFamilies;
        set
        {
            if (SetProperty(ref _useColorfulFamilies, value))
            {
                if (!value && _showSummaryArrowBorders)
                {
                    _showSummaryArrowBorders = false;
                    OnPropertyChanged(nameof(ShowSummaryArrowBorders));
                }

                SaveAppearanceSettings();
            }
        }
    }

    public bool ShowAppearanceCodePreview
    {
        get => _showAppearanceCodePreview;
        set
        {
            if (SetProperty(ref _showAppearanceCodePreview, value))
            {
                SaveAppearanceSettings();
            }
        }
    }

    public WorkbenchModeOptionViewModel SelectedWorkspaceMode
    {
        get => _selectedWorkspaceMode;
        private set
        {
            if (ReferenceEquals(_selectedWorkspaceMode, value))
            {
                return;
            }

            _selectedWorkspaceMode.IsActive = false;
            value.IsActive = true;
            if (SetProperty(ref _selectedWorkspaceMode, value))
            {
                OnPropertyChanged(nameof(IsCodeEditorMode));
                OnPropertyChanged(nameof(IsProjectGraphMode));
                OnPropertyChanged(nameof(IsBrowserMode));
                OnPropertyChanged(nameof(IsProjectScannerMode));
                SaveAppearanceSettings();
            }
        }
    }

    public bool IsCodeEditorMode =>
        string.Equals(SelectedWorkspaceMode.Key, "code", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectGraphMode =>
        string.Equals(SelectedWorkspaceMode.Key, "graph", StringComparison.OrdinalIgnoreCase);

    public bool IsBrowserMode =>
        string.Equals(SelectedWorkspaceMode.Key, "browser", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectScannerMode =>
        string.Equals(SelectedWorkspaceMode.Key, "scanner", StringComparison.OrdinalIgnoreCase);

    public string AppearanceSettingsPath => _workbenchSettings.SettingsPath;

    public ProjectTabViewModel? CurrentProject
    {
        get => _currentProject;
        private set
        {
            if (SetProperty(ref _currentProject, value))
            {
                OnPropertyChanged(nameof(CanScanProjectRules));
                OnPropertyChanged(nameof(CanAutoSetupProjectRules));
            }
        }
    }

    public ProjectNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            var previous = _selectedNode;
            if (!SetProperty(ref _selectedNode, value))
            {
                if (value is { IsFile: true })
                {
                    SelectFileNode(value);
                }

                return;
            }

            if (previous is not null)
            {
                previous.IsCurrent = false;
            }

            if (value is not null)
            {
                value.IsCurrent = true;
            }

            RefreshCurrentRowHighlights(previous, value);

            if (value is null || value.IsFolder)
            {
                return;
            }

            SelectFileNode(value);
        }
    }

    public TreeRowViewModel? SelectedTreeRow
    {
        get => _selectedTreeRow;
        set
        {
            if (value?.Node is not { } node)
            {
                SetProperty(ref _selectedTreeRow, null);
                ReplaceSelectedTreeRows([]);
                return;
            }

            SelectTreeRow(value, false);
            if (!SelectedTreeRows.Contains(value))
            {
                ReplaceSelectedTreeRows([value]);
            }
        }
    }

    public FileHistoryViewModel? SelectedHistory
    {
        get => _selectedHistory;
        private set => SetProperty(ref _selectedHistory, value);
    }

    public EditorDocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        private set
        {
            if (SetProperty(ref _activeDocument, value))
            {
                OnPropertyChanged(nameof(HasActiveDocument));
            }
        }
    }

    public bool HasActiveDocument => ActiveDocument is not null;
    public bool HasExternalChanges => ExternalChanges.Count > 0;
    public string ExternalQueueTitle => HasExternalChanges ? $"External updates ({ExternalChanges.Count})" : "External updates";

    public string SupportedFileTypesLabel
    {
        get => _supportedFileTypesLabel;
        private set => SetProperty(ref _supportedFileTypesLabel, value);
    }

    public string IgnoredFileTypesLabel
    {
        get => _ignoredFileTypesLabel;
        private set => SetProperty(ref _ignoredFileTypesLabel, value);
    }

    public string FileRulesPath
    {
        get => _fileRulesPath;
        private set => SetProperty(ref _fileRulesPath, value);
    }

    public string SupportedFileTypesText
    {
        get => _supportedFileTypesText;
        set
        {
            if (SetProperty(ref _supportedFileTypesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string IgnoredFileTypesText
    {
        get => _ignoredFileTypesText;
        set
        {
            if (SetProperty(ref _ignoredFileTypesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string IgnoredFileNamesText
    {
        get => _ignoredFileNamesText;
        set
        {
            if (SetProperty(ref _ignoredFileNamesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string IgnoredDirectoriesText
    {
        get => _ignoredDirectoriesText;
        set
        {
            if (SetProperty(ref _ignoredDirectoriesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string LocFileTypesText
    {
        get => _locFileTypesText;
        set
        {
            if (SetProperty(ref _locFileTypesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string NewIgnoredDirectoryRuleText
    {
        get => _newIgnoredDirectoryRuleText;
        set => SetProperty(ref _newIgnoredDirectoryRuleText, value ?? "");
    }

    public string NewIgnoredFileNameRuleText
    {
        get => _newIgnoredFileNameRuleText;
        set => SetProperty(ref _newIgnoredFileNameRuleText, value ?? "");
    }

    public string NewIgnoredFileTypeRuleText
    {
        get => _newIgnoredFileTypeRuleText;
        set => SetProperty(ref _newIgnoredFileTypeRuleText, value ?? "");
    }

    public string NewSupportedFileTypeRuleText
    {
        get => _newSupportedFileTypeRuleText;
        set => SetProperty(ref _newSupportedFileTypeRuleText, value ?? "");
    }

    public string NewLocFileTypeRuleText
    {
        get => _newLocFileTypeRuleText;
        set => SetProperty(ref _newLocFileTypeRuleText, value ?? "");
    }

    public string FileRulesSummary =>
        $"{SupportedFileTypeRules.Count} allowed types | "
        + $"{LocFileTypeRules.Count} LOC types | "
        + $"{IgnoredFileTypeRules.Count} skipped types | "
        + $"{IgnoredFileNameRules.Count} skipped files | "
        + $"{IgnoredDirectoryRules.Count} skipped folders";

    public string FileRulesStatus
    {
        get => _fileRulesStatus;
        private set => SetProperty(ref _fileRulesStatus, value);
    }

    public string ProjectScanSummary
    {
        get => _projectScanSummary;
        private set => SetProperty(ref _projectScanSummary, value ?? "");
    }

    public string ProjectScanResultText
    {
        get => _projectScanResultText;
        private set => SetProperty(ref _projectScanResultText, value ?? "");
    }

    public string ProjectScanRuleSummary
    {
        get => _projectScanRuleSummary;
        private set => SetProperty(ref _projectScanRuleSummary, value ?? "");
    }

    public string ProjectScanAutoSetupStatus
    {
        get => _projectScanAutoSetupStatus;
        private set => SetProperty(ref _projectScanAutoSetupStatus, value ?? "");
    }

    public string ProjectGraphSummary
    {
        get => _projectGraphSummary;
        private set => SetProperty(ref _projectGraphSummary, value ?? "");
    }

    public string ProjectGraphTreeText
    {
        get => _projectGraphTreeText;
        private set => SetProperty(ref _projectGraphTreeText, value ?? "");
    }

    public ProjectNodeViewModel? ProjectGraphSelectedNode
    {
        get => _projectGraphSelectedNode;
        set
        {
            if (SetProperty(ref _projectGraphSelectedNode, value))
            {
                OnPropertyChanged(nameof(ProjectGraphSelectedLabel));
                if (value is { IsFile: true })
                {
                    FocusProjectTreeNode(value);
                }
            }
        }
    }

    public string ProjectGraphSelectedLabel => ProjectGraphSelectedNode is null
        ? "No graph node selected."
        : BuildProjectGraphSelectedLabel(ProjectGraphSelectedNode);

    public int ProjectGraphVersion
    {
        get => _projectGraphVersion;
        private set => SetProperty(ref _projectGraphVersion, value);
    }

    public int ProjectGraphCenterVersion
    {
        get => _projectGraphCenterVersion;
        private set => SetProperty(ref _projectGraphCenterVersion, value);
    }

    public int ProjectTreeFocusVersion
    {
        get => _projectTreeFocusVersion;
        private set => SetProperty(ref _projectTreeFocusVersion, value);
    }

    public bool IsProjectGraphSearchOpen
    {
        get => _isProjectGraphSearchOpen;
        set
        {
            if (SetProperty(ref _isProjectGraphSearchOpen, value))
            {
                if (!value)
                {
                    ProjectGraphSearchText = "";
                }

                OnPropertyChanged(nameof(HasProjectGraphSearchSuggestions));
            }
        }
    }

    public string ProjectGraphSearchText
    {
        get => _projectGraphSearchText;
        set
        {
            if (SetProperty(ref _projectGraphSearchText, value ?? ""))
            {
                UpdateProjectGraphSearchSuggestions();
            }
        }
    }

    public bool HasProjectGraphSearchSuggestions => ProjectGraphSearchSuggestions.Count > 0;

    public bool IsProjectScanRunning
    {
        get => _isProjectScanRunning;
        private set
        {
            if (SetProperty(ref _isProjectScanRunning, value))
            {
                OnPropertyChanged(nameof(CanScanProjectRules));
                OnPropertyChanged(nameof(CanAutoSetupProjectRules));
                OnPropertyChanged(nameof(ProjectScanButtonLabel));
            }
        }
    }

    public bool CanScanProjectRules => !IsProjectScanRunning && CurrentProject is not null;
    public bool CanAutoSetupProjectRules => !IsProjectScanRunning && CurrentProject is not null;
    public string ProjectScanButtonLabel => IsProjectScanRunning ? "Scanning" : "Scan";

    private void RefreshProjectGraph()
    {
        _projectGraphSearchIndex = null;
        ProjectGraphVersion++;
        ProjectGraphSummary = BuildProjectGraphSummary();
        ProjectGraphTreeText = BuildProjectGraphTreeText();

        if (ProjectGraphSelectedNode is not null && !ContainsProjectNode(ProjectGraphSelectedNode))
        {
            ProjectGraphSelectedNode = null;
        }
        else
        {
            OnPropertyChanged(nameof(ProjectGraphSelectedLabel));
        }

        UpdateProjectGraphSearchSuggestions();
    }

    public void OpenProjectGraphSearch()
    {
        IsProjectGraphSearchOpen = true;
        UpdateProjectGraphSearchSuggestions();
    }

    public void CloseProjectGraphSearch()
    {
        IsProjectGraphSearchOpen = false;
    }

    public void AcceptProjectGraphSearch()
    {
        SelectProjectGraphSearchSuggestion(ProjectGraphSearchSuggestions.FirstOrDefault());
    }

    private void FocusProjectTreeNode(ProjectNodeViewModel node)
    {
        if (!TryBuildProjectNodePath(ProjectTree, node, out var path) || path.Count == 0)
        {
            return;
        }

        for (var index = 0; index < path.Count - 1; index++)
        {
            if (path[index].IsFolder)
            {
                path[index].IsExpanded = true;
            }
        }

        RefreshVisibleProjectNodes();
        var rowIndex = FindNodeRowIndex(node);
        if (rowIndex < 0)
        {
            return;
        }

        var row = VisibleTreeRows[rowIndex];
        ReplaceSelectedTreeRows([row]);
        SetProperty(ref _selectedTreeRow, row, nameof(SelectedTreeRow));
        ProjectTreeFocusVersion++;
    }

    private void SelectProjectGraphSearchSuggestion(ProjectGraphSearchSuggestionViewModel? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        ProjectGraphSelectedNode = suggestion.Node;
        ProjectGraphCenterVersion++;
    }

    private void UpdateProjectGraphSearchSuggestions()
    {
        ProjectGraphSearchSuggestions.Clear();
        var query = ProjectGraphSearchText.Trim();
        if (query.Length == 0 || ProjectTree.Count == 0)
        {
            OnPropertyChanged(nameof(HasProjectGraphSearchSuggestions));
            return;
        }

        var index = GetProjectGraphSearchIndex();
        Span<int> bestScores = stackalloc int[5];
        Span<int> bestIndexes = stackalloc int[5];
        for (var slot = 0; slot < bestScores.Length; slot++)
        {
            bestScores[slot] = int.MaxValue;
            bestIndexes[slot] = -1;
        }

        for (var indexPosition = 0; indexPosition < index.Count; indexPosition++)
        {
            var score = ScoreProjectGraphSearch(index[indexPosition], query);
            if (score == int.MaxValue || score >= bestScores[^1])
            {
                continue;
            }

            var insertAt = bestScores.Length - 1;
            while (insertAt > 0 && score < bestScores[insertAt - 1])
            {
                bestScores[insertAt] = bestScores[insertAt - 1];
                bestIndexes[insertAt] = bestIndexes[insertAt - 1];
                insertAt--;
            }

            bestScores[insertAt] = score;
            bestIndexes[insertAt] = indexPosition;
        }

        for (var slot = 0; slot < bestIndexes.Length; slot++)
        {
            var entryIndex = bestIndexes[slot];
            if (entryIndex < 0)
            {
                continue;
            }

            var entry = index[entryIndex];
            ProjectGraphSearchSuggestions.Add(new ProjectGraphSearchSuggestionViewModel(
                entry.Node,
                entry.Title,
                entry.Detail,
                entry.Meta));
        }

        OnPropertyChanged(nameof(HasProjectGraphSearchSuggestions));
        if (ProjectGraphSearchSuggestions.FirstOrDefault() is { } first)
        {
            ProjectGraphSelectedNode = first.Node;
            ProjectGraphCenterVersion++;
        }
    }

    private List<ProjectGraphSearchEntry> GetProjectGraphSearchIndex()
    {
        if (_projectGraphSearchIndex is { } index)
        {
            return index;
        }

        index = new List<ProjectGraphSearchEntry>(Math.Max(256, ProjectTree.Count * 8));
        var stack = new Stack<ProjectNodeViewModel>();
        for (var indexRoot = ProjectTree.Count - 1; indexRoot >= 0; indexRoot--)
        {
            stack.Push(ProjectTree[indexRoot]);
        }

        var order = 0;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var title = string.IsNullOrWhiteSpace(node.DisplayName) ? node.Name : node.DisplayName;
            var detail = string.IsNullOrWhiteSpace(node.Path) ? node.Name : node.Path;
            index.Add(new ProjectGraphSearchEntry(
                node,
                title,
                detail,
                BuildProjectGraphNodeMeta(node),
                node.Depth,
                order++));

            for (var childIndex = node.Children.Count - 1; childIndex >= 0; childIndex--)
            {
                stack.Push(node.Children[childIndex]);
            }
        }

        _projectGraphSearchIndex = index;
        return index;
    }

    private static int ScoreProjectGraphSearch(ProjectGraphSearchEntry entry, string query)
    {
        var title = entry.Title;
        var detail = entry.Detail;

        if (string.Equals(title, query, StringComparison.OrdinalIgnoreCase))
        {
            return entry.Depth * 4;
        }

        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100 + entry.Depth * 4 + Math.Max(0, title.Length - query.Length);
        }

        var titleIndex = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (titleIndex >= 0)
        {
            return 400 + titleIndex * 8 + entry.Depth * 4 + Math.Max(0, title.Length - query.Length);
        }

        if (detail.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 900 + entry.Depth * 4 + Math.Max(0, detail.Length - query.Length);
        }

        var detailIndex = detail.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (detailIndex >= 0)
        {
            return 1300 + detailIndex * 4 + entry.Depth * 4 + Math.Min(entry.Order, 1000);
        }

        return int.MaxValue;
    }

    private static bool TryBuildProjectNodePath(
        IEnumerable<ProjectNodeViewModel> nodes,
        ProjectNodeViewModel target,
        out List<ProjectNodeViewModel> path)
    {
        path = [];
        foreach (var node in nodes)
        {
            if (TryBuildProjectNodePath(node, target, path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildProjectNodePath(
        ProjectNodeViewModel node,
        ProjectNodeViewModel target,
        List<ProjectNodeViewModel> path)
    {
        path.Add(node);
        if (ReferenceEquals(node, target))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryBuildProjectNodePath(child, target, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private string BuildProjectGraphSummary()
    {
        if (CurrentProject is null)
        {
            return "No project loaded.";
        }

        var skipState = ShowSkippedFiles ? "skipped shown" : "skipped hidden";
        return $"{CurrentProject.FileCount} files | {CurrentProject.DirectoryCount} dirs | {skipState} | current file rules";
    }

    private string BuildProjectGraphTreeText()
    {
        if (CurrentProject is null || ProjectTree.Count == 0)
        {
            return "No project loaded.";
        }

        const int maxLines = 6000;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(CurrentProject.Name);
        builder.AppendLine(CurrentProject.ProjectRoot);
        builder.AppendLine(BuildProjectGraphSummary());
        builder.AppendLine();

        var remainingLines = maxLines;
        var omitted = 0;
        foreach (var node in ProjectTree)
        {
            AppendProjectGraphTreeNode(builder, node, "", true, ref remainingLines, ref omitted);
            if (remainingLines <= 0)
            {
                break;
            }
        }

        if (omitted > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"... {omitted:N0}+ more nodes omitted from this text preview.");
        }

        return builder.ToString();
    }

    private static void AppendProjectGraphTreeNode(
        System.Text.StringBuilder builder,
        ProjectNodeViewModel node,
        string prefix,
        bool isLast,
        ref int remainingLines,
        ref int omitted)
    {
        if (remainingLines <= 0)
        {
            omitted += CountProjectGraphTextNodesCapped(node, 100000);
            return;
        }

        var connector = node.Depth <= 0 ? "" : isLast ? "`-- " : "|-- ";
        builder.Append(prefix);
        builder.Append(connector);
        builder.Append(node.DisplayName);

        var meta = BuildProjectGraphNodeMeta(node);
        if (!string.IsNullOrWhiteSpace(meta))
        {
            builder.Append("  ");
            builder.Append(meta);
        }

        builder.AppendLine();
        remainingLines--;

        if (remainingLines <= 0)
        {
            foreach (var child in node.Children)
            {
                omitted += CountProjectGraphTextNodesCapped(child, 100000 - Math.Min(omitted, 100000));
            }

            return;
        }

        var childPrefix = node.Depth <= 0
            ? ""
            : prefix + (isLast ? "    " : "|   ");
        for (var index = 0; index < node.Children.Count; index++)
        {
            AppendProjectGraphTreeNode(builder, node.Children[index], childPrefix, index == node.Children.Count - 1, ref remainingLines, ref omitted);
            if (remainingLines <= 0)
            {
                for (var rest = index + 1; rest < node.Children.Count; rest++)
                {
                    omitted += CountProjectGraphTextNodesCapped(node.Children[rest], 100000 - Math.Min(omitted, 100000));
                }

                break;
            }
        }
    }

    private static string BuildProjectGraphNodeMeta(ProjectNodeViewModel node)
    {
        if (node.IsExternal)
        {
            return "[skip]";
        }

        if (node.IsFolder)
        {
            return string.IsNullOrWhiteSpace(node.DirectoryStatsLabel)
                ? ""
                : $"[{node.DirectoryStatsLabel}]";
        }

        var parts = new[]
            {
                string.IsNullOrWhiteSpace(node.VersionLabel) ? "" : node.VersionLabel,
                string.IsNullOrWhiteSpace(node.LocMetricLabel) ? "" : node.LocMetricLabel
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? "" : $"[{string.Join(" ", parts)}]";
    }

    private static int CountProjectGraphTextNodesCapped(ProjectNodeViewModel node, int cap)
    {
        if (cap <= 0)
        {
            return 0;
        }

        var count = 0;
        var stack = new Stack<ProjectNodeViewModel>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            if (count >= cap)
            {
                return cap;
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return count;
    }

    private static string BuildProjectGraphSelectedLabel(ProjectNodeViewModel node)
    {
        var kind = node.IsFolder ? "Folder" : "File";
        var path = string.IsNullOrWhiteSpace(node.Path) ? node.Name : node.Path;
        var meta = BuildProjectGraphNodeMeta(node);
        return string.IsNullOrWhiteSpace(meta)
            ? $"{kind}: {path}"
            : $"{kind}: {path} {meta}";
    }

    private bool ContainsProjectNode(ProjectNodeViewModel target)
    {
        foreach (var node in ProjectTree)
        {
            if (ContainsProjectNode(node, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProjectNode(ProjectNodeViewModel current, ProjectNodeViewModel target)
    {
        if (ReferenceEquals(current, target))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            if (ContainsProjectNode(child, target))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsHistoryOpen
    {
        get => _isHistoryOpen;
        private set => SetProperty(ref _isHistoryOpen, value);
    }

    public bool ShowFileDetails
    {
        get => _showFileDetails;
        set
        {
            if (SetProperty(ref _showFileDetails, value))
            {
                OnPropertyChanged(nameof(FileDetailsToggleLabel));
            }
        }
    }

    public string FileDetailsToggleLabel => ShowFileDetails ? "File Details" : "Details Off";

    public bool ShowSkippedFiles
    {
        get => _showSkippedFiles;
        private set
        {
            if (SetProperty(ref _showSkippedFiles, value))
            {
                OnPropertyChanged(nameof(SkippedFilesToggleLabel));
            }
        }
    }

    public string SkippedFilesToggleLabel => ShowSkippedFiles ? "Hide Skip" : "Show Skip";

    public bool IsProjectFilesPaneOpen
    {
        get => _isProjectFilesPaneOpen;
        set
        {
            if (SetProperty(ref _isProjectFilesPaneOpen, value))
            {
                OnPropertyChanged(nameof(ProjectFilesPaneWidth));
                OnPropertyChanged(nameof(ProjectFilesTreeViewLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public GridLength ProjectFilesPaneWidth => IsProjectFilesPaneOpen
        ? new GridLength(252)
        : new GridLength(0);

    public string ProjectFilesTreeViewLabel => BuildViewToggleLabel(IsProjectFilesPaneOpen, "Project files tree");

    public bool IsBrowserRoutingPaneOpen
    {
        get => _isBrowserRoutingPaneOpen;
        set
        {
            if (SetProperty(ref _isBrowserRoutingPaneOpen, value))
            {
                OnPropertyChanged(nameof(BrowserRoutingPaneWidth));
                OnPropertyChanged(nameof(BrowserRoutingWindowViewLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public GridLength BrowserRoutingPaneWidth => IsBrowserRoutingPaneOpen
        ? new GridLength(282)
        : new GridLength(0);

    public string BrowserRoutingWindowViewLabel => BuildViewToggleLabel(IsBrowserRoutingPaneOpen, "Browser routing window");

    public bool IsProjectGraphTreePaneOpen
    {
        get => _isProjectGraphTreePaneOpen;
        set
        {
            if (SetProperty(ref _isProjectGraphTreePaneOpen, value))
            {
                OnPropertyChanged(nameof(ProjectGraphTreePaneWidth));
                OnPropertyChanged(nameof(ProjectGraphTreeColumnSpacing));
                OnPropertyChanged(nameof(ProjectGraphTreeToggleLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public GridLength ProjectGraphTreePaneWidth => IsProjectGraphTreePaneOpen
        ? new GridLength(260)
        : new GridLength(0);

    public double ProjectGraphTreeColumnSpacing => IsProjectGraphTreePaneOpen ? 6 : 0;

    public string ProjectGraphTreeToggleLabel => IsProjectGraphTreePaneOpen ? "Hide Tree" : "Show Tree";

    public string PromptWindowViewLabel => BuildViewToggleLabel(ContextControl.IsPromptOpen, "Prompt window");

    private static string BuildViewToggleLabel(bool isEnabled, string label)
    {
        return $"{(isEnabled ? "✓" : " ")} {label}";
    }

    public double HistoryWidth
    {
        get => _historyWidth;
        private set => SetProperty(ref _historyWidth, value);
    }

    public double HistoryOpacity
    {
        get => _historyOpacity;
        private set => SetProperty(ref _historyOpacity, value);
    }

    public double HistoryGutter
    {
        get => _historyGutter;
        private set => SetProperty(ref _historyGutter, value);
    }

    public static WorkbenchViewModel Create()
    {
        var settings = WorkbenchSettings.Load();
        var defaultRoot = FindDefaultContextControlRoot();
        if (defaultRoot is not null)
        {
            try
            {
                var loaded = ProjectLoader.Load(defaultRoot, showSkippedFiles: settings.ShowSkippedFiles);
                return new WorkbenchViewModel([loaded.Project], loaded.Tree, loaded.HistoryByPath, loaded.FileRules, loaded.IsTreePrepared, settings);
            }
            catch
            {
                // Fall back to the small design-time shell if local scanning fails.
            }
        }

        var projects = new ObservableCollection<ProjectTabViewModel>
        {
            new("cc", "CC", "Context Control", "18,284 LOC", "601", "239", "b9ef261", @"D:\Projects\vulkanas\contextcontrol"),
            new("vx", "VX", "VulkanVX", "open to scan", "project", "project", "linked", @"D:\Projects\vulkanas"),
            new("ide", "IDE", "Workbench", "native shell", "app", "native", "b9ef261", @"contextcontrol\ide"),
            new("ps", "PS", "PowerShell Core", "script core", "32", "3", "b9ef261", @"contextcontrol\lib")
        };

        return new WorkbenchViewModel(projects, BuildProjectTree(), BuildHistory(), workbenchSettings: settings);
    }

    private static ThemeOptionViewModel FindOptionByKey(
        IEnumerable<ThemeOptionViewModel> options,
        string? key,
        ThemeOptionViewModel fallback)
    {
        return options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    private WorkbenchModeOptionViewModel FindModeByKey(string? key)
    {
        return WorkspaceModes.FirstOrDefault(mode => string.Equals(mode.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? WorkspaceModes[0];
    }

    private void SwitchWorkspaceMode(WorkbenchModeOptionViewModel? mode)
    {
        if (mode is null)
        {
            return;
        }

        SelectedWorkspaceMode = mode;
        if (IsProjectScannerMode)
        {
            _ = ScanProjectRulesAsync();
        }
    }

    private void ApplySummaryFoldKinds(string? value)
    {
        var selected = ParseSummaryFoldKinds(value);
        _summarizeNamespace = selected.Contains("namespace");
        _summarizeClass = selected.Contains("class");
        _summarizeStruct = selected.Contains("struct");
        _summarizeInterface = selected.Contains("interface");
        _summarizeEnum = selected.Contains("enum");
        _summarizeMethod = selected.Contains("method");
        _summarizeProperty = selected.Contains("property");
        _summarizeObject = selected.Contains("object");
        _summarizeBlock = selected.Contains("block");
        _summarizeArray = selected.Contains("array");
        _summarizeArguments = selected.Contains("arguments");
    }

    private static HashSet<string> ParseSummaryFoldKinds(string? value)
    {
        const string defaultKinds = "namespace,class,struct,interface,enum,method,property,object,block,array,arguments";
        var source = value is null ? defaultKinds : value;
        return source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool SetSummaryKind(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(SummaryFoldKinds));
        SaveAppearanceSettings();
        return true;
    }

    private string BuildSummaryFoldKinds()
    {
        var kinds = new List<string>(11);
        AddSummaryKind(kinds, _summarizeNamespace, "namespace");
        AddSummaryKind(kinds, _summarizeClass, "class");
        AddSummaryKind(kinds, _summarizeStruct, "struct");
        AddSummaryKind(kinds, _summarizeInterface, "interface");
        AddSummaryKind(kinds, _summarizeEnum, "enum");
        AddSummaryKind(kinds, _summarizeMethod, "method");
        AddSummaryKind(kinds, _summarizeProperty, "property");
        AddSummaryKind(kinds, _summarizeObject, "object");
        AddSummaryKind(kinds, _summarizeBlock, "block");
        AddSummaryKind(kinds, _summarizeArray, "array");
        AddSummaryKind(kinds, _summarizeArguments, "arguments");
        return string.Join(",", kinds);
    }

    private static void AddSummaryKind(List<string> kinds, bool enabled, string key)
    {
        if (enabled)
        {
            kinds.Add(key);
        }
    }

    private void SaveAppearanceSettings()
    {
        _workbenchSettings.SkinKey = SkinKey;
        _workbenchSettings.ThemeKey = SelectedTheme.Key;
        _workbenchSettings.SyntaxThemeKey = SelectedSyntaxTheme.Key;
        _workbenchSettings.CodeFontKey = CodeFontKey;
        _workbenchSettings.UiFontKey = UiFontKey;
        _workbenchSettings.FoldArrowPositionKey = SelectedFoldArrowPosition.Key;
        _workbenchSettings.ShowFoldArrows = ShowFoldArrows;
        _workbenchSettings.ShowSummaryArrowBorders = ShowSummaryArrowBorders;
        _workbenchSettings.UseParentChildArrowIndentation = UseParentChildArrowIndentation;
        _workbenchSettings.ShowVerticalScopeLines = ShowVerticalScopeLines;
        _workbenchSettings.SummaryFoldKinds = SummaryFoldKinds;
        _workbenchSettings.UseColorfulFamilies = UseColorfulFamilies;
        _workbenchSettings.ShowAppearanceCodePreview = ShowAppearanceCodePreview;
        _workbenchSettings.WorkspaceModeKey = SelectedWorkspaceMode.Key;
        _workbenchSettings.ExternalBrowserKey = BrowserPane.SelectedExternalBrowser?.Key ?? "default";
        _workbenchSettings.ShowSkippedFiles = ShowSkippedFiles;
        _workbenchSettings.ShowProjectFilesPane = IsProjectFilesPaneOpen;
        _workbenchSettings.ShowBrowserRoutingPane = IsBrowserRoutingPaneOpen;
        _workbenchSettings.ShowProjectGraphTreePane = IsProjectGraphTreePaneOpen;

        try
        {
            _workbenchSettings.Save();
        }
        catch
        {
            // Appearance changes should never take the editor down if the settings
            // file is temporarily unavailable.
        }
    }

    public void Dispose()
    {
        _externalScanTimer.Dispose();

        foreach (var tracker in _trackersByProjectId.Values)
        {
            tracker.Dispose();
        }

        _trackersByProjectId.Clear();
    }

    private static string? FindDefaultContextControlRoot()
    {
        return FindContextRoot(Directory.GetCurrentDirectory())
            ?? FindContextRoot(AppContext.BaseDirectory);
    }

    private static string? FindContextRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ccStart.ps1"))
                && File.Exists(Path.Combine(directory.FullName, "ccDir.ps1"))
                && File.Exists(Path.Combine(directory.FullName, "cc.ps1"))
                && File.Exists(Path.Combine(directory.FullName, "ccReplace.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private void SelectProject(ProjectTabViewModel? project)
    {
        if (project is null)
        {
            return;
        }

        if (ReferenceEquals(CurrentProject, project) && project.IsActive)
        {
            return;
        }

        var switchVersion = Interlocked.Increment(ref _projectSwitchVersion);

        foreach (var item in Projects)
        {
            item.IsActive = ReferenceEquals(item, project);
        }

        CurrentProject = project;
        if (_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            // Let the active project tile render first; loading workspace content can be expensive.
            PostToUi(() =>
            {
                if (switchVersion != Volatile.Read(ref _projectSwitchVersion)
                    || CurrentProject?.Id != project.Id)
                {
                    return;
                }

                LoadWorkspace(workspace);
            });
        }

        QueueExternalTrackerStart(project, switchVersion);
    }

    private void OpenHistory()
    {
        if (SelectedNode is { IsFile: true })
        {
            OpenHistory(SelectedNode.Path);
            return;
        }

        OpenHistory("ide/ContextControl.Workbench/Views/MainWindow.axaml");
    }

    public void OpenHistory(string path)
    {
        SelectHistory(path);
        ShowHistory();
    }

    public void SelectTreeRow(TreeRowViewModel row, bool toggleHistory)
    {
        if (row.Node is not { } node)
        {
            return;
        }

        SetProperty(ref _selectedTreeRow, row, nameof(SelectedTreeRow));
        SelectedNode = node;

        if (toggleHistory)
        {
            ToggleHistoryForNode(node);
        }
    }

    public void SetSelectedTreeRows(IEnumerable<TreeRowViewModel> rows)
    {
        var selectedRows = rows
            .Where(row => row.Node is not null)
            .Distinct()
            .ToArray();

        ReplaceSelectedTreeRows(selectedRows);
        var primaryRow = selectedRows.LastOrDefault();
        if (primaryRow is not null)
        {
            SelectTreeRow(primaryRow, false);
            return;
        }

        SetProperty(ref _selectedTreeRow, null, nameof(SelectedTreeRow));
        SelectedNode = null;
    }

    public async Task CopyTreeContextAsync(IEnumerable<TreeRowViewModel> rows)
    {
        var selectedNodes = GetContextNodes(rows);
        var requestLines = selectedNodes
            .SelectMany(EnumerateContextFilePaths)
            .Select(NormalizeProjectPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestLines.Length == 0)
        {
            FileRulesStatus = "No shown files are available for the selected context.";
            return;
        }

        var label = selectedNodes.Count == 1
            ? selectedNodes[0].IsFolder ? "folder" : "file"
            : "selection";
        await ContextControl.CopyCodeContextAsync(requestLines, label);
    }

    public Task SkipTreeContextAsync(IEnumerable<TreeRowViewModel> rows)
    {
        return UpdateTreeContextRulesAsync(rows, skip: true);
    }

    public Task ShowTreeContextAsync(IEnumerable<TreeRowViewModel> rows)
    {
        return UpdateTreeContextRulesAsync(rows, skip: false);
    }

    public void ReportProjectTreeActionError(string message)
    {
        FileRulesStatus = string.IsNullOrWhiteSpace(message)
            ? "Project tree action failed."
            : $"Project tree action failed: {message}";
    }

    public bool CanToggleTreeFileExtension(ProjectNodeViewModel node)
    {
        return node.IsFile && !string.IsNullOrWhiteSpace(GetTreeFileExtension(node));
    }

    public string GetTreeFileExtensionRuleLabel(ProjectNodeViewModel node)
    {
        var extension = GetTreeFileExtension(node);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "File type";
        }

        return IsTreeFileExtensionSkipped(node)
            ? $"Show {extension} file types"
            : $"Hide {extension} file types";
    }

    public bool IsTreeFileExtensionSkipped(ProjectNodeViewModel node)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return false;
        }

        var extension = GetTreeFileExtension(node);
        return !string.IsNullOrWhiteSpace(extension) && workspace.FileRules.ShouldSkipExtension(extension);
    }

    public Task ToggleTreeFileExtensionAsync(ProjectNodeViewModel node)
    {
        if (!CanToggleTreeFileExtension(node))
        {
            FileRulesStatus = "This file has no extension to show or hide.";
            return Task.CompletedTask;
        }

        return UpdateTreeFileExtensionRuleAsync(GetTreeFileExtension(node), !IsTreeFileExtensionSkipped(node));
    }

    public string GetTreeFileLocRuleLabel(ProjectNodeViewModel node)
    {
        var extension = GetTreeFileExtension(node);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "File LOC";
        }

        return IsTreeFileLocExtensionShown(node)
            ? $"Hide LOC for {extension} file types"
            : $"Show LOC for {extension} file types";
    }

    public bool IsTreeFileLocExtensionShown(ProjectNodeViewModel node)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return false;
        }

        var extension = GetTreeFileExtension(node);
        return !string.IsNullOrWhiteSpace(extension) && workspace.FileRules.ShouldCountLocExtension(extension);
    }

    public Task ToggleTreeFileLocExtensionAsync(ProjectNodeViewModel node)
    {
        if (!CanToggleTreeFileExtension(node))
        {
            FileRulesStatus = "This file has no extension for LOC settings.";
            return Task.CompletedTask;
        }

        return UpdateTreeFileLocExtensionRuleAsync(GetTreeFileExtension(node), !IsTreeFileLocExtensionShown(node));
    }

    public void OpenVersionFromHistory(VersionEntryViewModel version)
    {
        OpenVersion(version);
    }

    public void OpenExternalChange(ExternalChangeItemViewModel? item)
    {
        if (item is null || CurrentProject is null)
        {
            return;
        }

        var node = FindProjectNodeByPath(ProjectTree, item.RelativePath);
        if (node is not null)
        {
            SelectedNode = node;
            SelectHistory(node.Path);
            OpenDocument(node);
            ShowHistory();
            return;
        }

        SelectHistory(item.RelativePath);
        ShowHistory();

        var fullPath = Path.Combine(CurrentProject.ProjectRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            ClearActiveVersion();
            OpenDocumentAsync(fullPath, item.RelativePath, $"v{item.Change.VersionAfter}", item.Change.Loc);
        }
    }

    public void OpenAttachment(string? path)
    {
        if (!TryResolveAttachment(path, out var fullPath, out var displayPath))
        {
            return;
        }

        ClearActiveVersion();
        OpenDocumentAsync(fullPath, displayPath, "live");
        SelectedWorkspaceMode = WorkspaceModes[0];
        if (IsHistoryOpen)
        {
            CloseHistory();
        }
    }

public void ToggleHistoryForNode(ProjectNodeViewModel node)
{
    if (!node.IsFile)
    {
        return;
    }

    var nodePath = NormalizeProjectPath(node.Path);
    var selectedHistoryPath = NormalizeProjectPath(SelectedHistory?.Path ?? "");

    if (IsHistoryOpen && string.Equals(selectedHistoryPath, nodePath, StringComparison.OrdinalIgnoreCase))
    {
        CloseHistory();
        return;
    }

    SelectHistory(node.Path);
    ShowHistory();
}

    private void ShowHistory()
    {
        IsHistoryOpen = true;
        HistoryWidth = 330;
        HistoryOpacity = 1;
        HistoryGutter = 8;
    }

    public void CloseHistory()
    {
        IsHistoryOpen = false;
        HistoryWidth = 0;
        HistoryOpacity = 0;
        HistoryGutter = 0;
    }

    private void ReplaceSelectedTreeRows(IReadOnlyList<TreeRowViewModel> rows)
    {
        var selected = rows.ToHashSet();
        foreach (var oldRow in SelectedTreeRows)
        {
            if (!selected.Contains(oldRow))
            {
                oldRow.IsSelected = false;
            }
        }

        SelectedTreeRows.Clear();
        SelectedTreeRows.AddRange(rows);
        foreach (var row in rows)
        {
            row.IsSelected = true;
        }
    }

    private IReadOnlyList<ProjectNodeViewModel> GetContextNodes(IEnumerable<TreeRowViewModel> rows)
    {
        return rows
            .Select(row => row.Node)
            .Where(node => node is not null)
            .Cast<ProjectNodeViewModel>()
            .Distinct()
            .ToArray();
    }

    private static IEnumerable<string> EnumerateContextFilePaths(ProjectNodeViewModel node)
    {
        if (node.IsExternal)
        {
            yield break;
        }

        if (node.IsFile)
        {
            if (!string.IsNullOrWhiteSpace(node.Path))
            {
                yield return node.Path;
            }

            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var path in EnumerateContextFilePaths(child))
            {
                yield return path;
            }
        }
    }

    private Task UpdateTreeContextRulesAsync(IEnumerable<TreeRowViewModel> rows, bool skip)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before changing file rules.";
            return Task.CompletedTask;
        }

        var nodes = GetContextNodes(rows)
            .Where(node => !string.IsNullOrWhiteSpace(node.Path))
            .ToArray();
        if (nodes.Length == 0)
        {
            FileRulesStatus = "Choose a file or folder before changing file rules.";
            return Task.CompletedTask;
        }

        var rules = workspace.FileRules;
        var changed = false;
        foreach (var node in nodes)
        {
            if (!skip && !node.IsExternal)
            {
                continue;
            }

            changed |= node.IsFolder
                ? skip ? rules.SkipDirectory(node.Path) : rules.ShowDirectory(node.Path)
                : skip ? rules.SkipFile(node.Path) : rules.ShowFile(node.Path);
        }

        if (!changed)
        {
            FileRulesStatus = skip
                ? "Selected paths were already skipped."
                : "Selected paths were already shown.";
            return Task.CompletedTask;
        }

        rules.Save();
        var status = skip
            ? $"Skipped {nodes.Length} selected path(s)."
            : $"Shown {nodes.Length} selected path(s).";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);
        ApplyTreePathRuleMutation(workspace, rules, nodes, skip);

        FileRulesStatus = status;
        return Task.CompletedTask;
    }

    private Task UpdateTreeFileExtensionRuleAsync(string extension, bool skip)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before changing file rules.";
            return Task.CompletedTask;
        }

        var cleanExtension = NormalizeFileExtension(extension);
        if (string.IsNullOrWhiteSpace(cleanExtension))
        {
            FileRulesStatus = "This file has no extension to show or hide.";
            return Task.CompletedTask;
        }

        var rules = workspace.FileRules;
        var changed = skip
            ? rules.SkipExtension(cleanExtension)
            : rules.ShowExtension(cleanExtension);

        if (!changed)
        {
            FileRulesStatus = skip
                ? $"{cleanExtension} file types were already skipped."
                : $"{cleanExtension} file types were already shown.";
            return Task.CompletedTask;
        }

        rules.Save();
        var status = skip
            ? $"Skipped {cleanExtension} file types."
            : $"Shown {cleanExtension} file types.";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);
        ApplyTreeExtensionRuleMutation(rules, cleanExtension, skip);

        FileRulesStatus = status;
        return Task.CompletedTask;
    }

    private Task UpdateTreeFileLocExtensionRuleAsync(string extension, bool showLoc)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before changing LOC rules.";
            return Task.CompletedTask;
        }

        var cleanExtension = NormalizeFileExtension(extension);
        if (string.IsNullOrWhiteSpace(cleanExtension))
        {
            FileRulesStatus = "This file has no extension for LOC settings.";
            return Task.CompletedTask;
        }

        var rules = workspace.FileRules;
        var changed = showLoc
            ? rules.ShowLocExtension(cleanExtension)
            : rules.HideLocExtension(cleanExtension);

        if (!changed)
        {
            FileRulesStatus = showLoc
                ? $"LOC was already shown for {cleanExtension} file types."
                : $"LOC was already hidden for {cleanExtension} file types.";
            return Task.CompletedTask;
        }

        rules.Save();
        var status = showLoc
            ? $"Showing LOC for {cleanExtension} file types."
            : $"Hiding LOC for {cleanExtension} file types.";
        ApplyFileRulesToEditor(rules, status);
        ApplyTreeLocExtensionRuleMutation(cleanExtension, showLoc);

        FileRulesStatus = status;
        return Task.CompletedTask;
    }

    private static string GetTreeFileExtension(ProjectNodeViewModel node)
    {
        var extension = Path.GetExtension(string.IsNullOrWhiteSpace(node.Name) ? node.Path : node.Name);
        return NormalizeFileExtension(extension);
    }

    private static string NormalizeFileExtension(string extension)
    {
        var clean = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private void ApplyTreePathRuleMutation(
        ProjectWorkspaceState workspace,
        ProjectFileRules rules,
        IReadOnlyList<ProjectNodeViewModel> nodes,
        bool skip)
    {
        var fallback = SelectedNode is null ? null : FindParentNode(SelectedNode);

        if (skip && !ShowSkippedFiles)
        {
            foreach (var node in nodes.OrderByDescending(node => node.Depth))
            {
                RemoveProjectNode(workspace, node);
            }

            FinishProjectTreeStructureMutation(fallback);
            return;
        }

        foreach (var node in nodes)
        {
            if (skip)
            {
                SetSubtreeExternal(node, true);
                continue;
            }

            if (FindParentNode(node)?.IsExternal == true)
            {
                continue;
            }

            ApplyNodeVisibilityFromRules(node, rules);
        }

        RecalculateProjectTreeMetrics();
    }

    private void ApplyTreeExtensionRuleMutation(ProjectFileRules rules, string extension, bool skip)
    {
        var fallback = SelectedNode is null ? null : FindParentNode(SelectedNode);
        var nodes = EnumerateProjectNodesWithParents()
            .Where(item => item.Node.IsFile && string.Equals(GetTreeFileExtension(item.Node), extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Node.Depth)
            .ToArray();

        if (skip && !ShowSkippedFiles)
        {
            if (CurrentProject is not null && _workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
            {
                foreach (var item in nodes)
                {
                    RemoveProjectNode(workspace, item.Node);
                }
            }

            FinishProjectTreeStructureMutation(fallback);
            return;
        }

        foreach (var item in nodes)
        {
            if (!skip && item.Parent?.IsExternal == true)
            {
                continue;
            }

            ApplyNodeVisibilityFromRules(item.Node, rules);
        }

        RecalculateProjectTreeMetrics();
    }

    private void ApplyTreeLocExtensionRuleMutation(string extension, bool showLoc)
    {
        foreach (var item in EnumerateProjectNodesWithParents())
        {
            var node = item.Node;
            if (node.IsExternal
                || !node.IsFile
                || !string.Equals(GetTreeFileExtension(node), extension, StringComparison.OrdinalIgnoreCase)
                || item.Parent?.IsExternal == true)
            {
                continue;
            }

            node.UpdateVersionAndLoc(node.VersionLabel, showLoc ? CountLocForNode(node) : 0);
        }

        RecalculateProjectTreeMetrics();
    }

    private void ApplyNodeVisibilityFromRules(ProjectNodeViewModel node, ProjectFileRules rules)
    {
        if (node.IsFolder)
        {
            var shouldSkip = !string.IsNullOrWhiteSpace(node.Path) && rules.ShouldSkipDirectory(node.Name, node.Path);
            node.SetExternalState(shouldSkip, shouldSkip && node.CanIncludeExternal);
            if (shouldSkip)
            {
                foreach (var child in node.Children)
                {
                    SetSubtreeExternal(child, true);
                }

                return;
            }

            foreach (var child in node.Children)
            {
                ApplyNodeVisibilityFromRules(child, rules);
            }

            return;
        }

        var extension = GetTreeFileExtension(node);
        var shouldShow = rules.ShouldShowFile(node.Path, node.Name, extension);
        node.SetExternalState(!shouldShow);
        node.UpdateVersionAndLoc(
            node.VersionLabel,
            shouldShow && rules.ShouldCountLocExtension(extension) ? CountLocForNode(node) : 0);
    }

    private static void SetSubtreeExternal(ProjectNodeViewModel node, bool isExternal)
    {
        node.SetExternalState(isExternal);
        if (isExternal)
        {
            node.UpdateVersionAndLoc(node.VersionLabel, 0);
        }

        foreach (var child in node.Children)
        {
            SetSubtreeExternal(child, isExternal);
        }
    }

    private long CountLocForNode(ProjectNodeViewModel node)
    {
        var path = ResolveNodePath(node);
        return path is not null && File.Exists(path)
            ? ProjectLoader.EstimateLoc(new FileInfo(path))
            : 0;
    }

    private void FinishProjectTreeStructureMutation(ProjectNodeViewModel? fallbackNode)
    {
        var selectedNodes = SelectedTreeRows
            .Select(row => row.Node)
            .Where(node => node is not null)
            .Cast<ProjectNodeViewModel>()
            .ToHashSet();

        RecalculateProjectTreeMetrics();
        PrepareTree();
        RefreshVisibleProjectNodes();

        var selectedRows = VisibleTreeRows
            .Where(row => row.Node is not null && selectedNodes.Contains(row.Node))
            .ToArray();

        if (selectedRows.Length == 0 && fallbackNode is not null)
        {
            selectedRows = VisibleTreeRows
                .Where(row => ReferenceEquals(row.Node, fallbackNode))
                .ToArray();
        }

        SetSelectedTreeRows(selectedRows);
    }

    private void RecalculateProjectTreeMetrics()
    {
        foreach (var node in ProjectTree)
        {
            node.RecalculateDirectoryLoc();
        }

        foreach (var row in VisibleTreeRows)
        {
            row.RefreshNodeMetrics();
            row.RefreshExpansionState();
            row.RefreshCurrentState();
        }

        RefreshProjectGraph();
    }

    private IEnumerable<(ProjectNodeViewModel Node, ProjectNodeViewModel? Parent)> EnumerateProjectNodesWithParents()
    {
        foreach (var node in ProjectTree)
        {
            foreach (var item in EnumerateProjectNodesWithParents(node, null))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<(ProjectNodeViewModel Node, ProjectNodeViewModel? Parent)> EnumerateProjectNodesWithParents(
        ProjectNodeViewModel node,
        ProjectNodeViewModel? parent)
    {
        yield return (node, parent);

        foreach (var child in node.Children)
        {
            foreach (var item in EnumerateProjectNodesWithParents(child, node))
            {
                yield return item;
            }
        }
    }

    private ProjectNodeViewModel? FindParentNode(ProjectNodeViewModel target)
    {
        foreach (var node in ProjectTree)
        {
            var parent = FindParentNode(node, target);
            if (parent is not null)
            {
                return parent;
            }
        }

        return null;
    }

    private static ProjectNodeViewModel? FindParentNode(ProjectNodeViewModel parent, ProjectNodeViewModel target)
    {
        foreach (var child in parent.Children)
        {
            if (ReferenceEquals(child, target))
            {
                return parent;
            }

            var nested = FindParentNode(child, target);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RemoveProjectNode(ProjectWorkspaceState workspace, ProjectNodeViewModel node)
    {
        RemoveProjectNode(ProjectTree, node);
        RemoveProjectNode(workspace.ProjectTree, node);
    }

    private static bool RemoveProjectNode(IList<ProjectNodeViewModel> nodes, ProjectNodeViewModel target)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (ReferenceEquals(node, target))
            {
                nodes.RemoveAt(index);
                return true;
            }

            if (RemoveProjectNode(node.Children, target))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ToggleSkippedFilesAsync()
    {
        ShowSkippedFiles = !ShowSkippedFiles;
        SaveAppearanceSettings();
        await RefreshCurrentProjectFromDiskAsync();
    }

    private async Task SaveFileRulesAsync()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before saving file rules.";
            return;
        }

        var rules = workspace.FileRules;
        SyncFileRuleTextsFromEntries();
        rules.UpdateRules(IgnoredDirectoriesText, IgnoredFileNamesText, IgnoredFileTypesText, SupportedFileTypesText, LocFileTypesText);
        rules.Save();
        var status = $"Saved file rules to {rules.RulesPath}";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);

        await RefreshCurrentProjectFromDiskAsync();
        FileRulesStatus = status;
    }

    private async Task ResetFileRulesAsync()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before resetting file rules.";
            return;
        }

        var rules = workspace.FileRules;
        rules.ResetToDefaults();
        rules.Save();
        var status = $"Reset file rules to defaults at {rules.RulesPath}";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);

        await RefreshCurrentProjectFromDiskAsync();
        FileRulesStatus = status;
    }

    private async Task ScanProjectRulesAsync()
    {
        if (IsProjectScanRunning)
        {
            return;
        }

        var project = CurrentProject;
        if (project is null || !_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            ProjectScanSummary = "No project open.";
            ProjectScanResultText = "Open a project before scanning.";
            FileRulesStatus = "Open a project before scanning.";
            return;
        }

        IsProjectScanRunning = true;
        ApplyProjectScanBusy(workspace);
        FileRulesStatus = "Scanning project rules...";

        try
        {
            var result = await ScanProjectRulesCoreAsync(project, workspace);
            workspace.ProjectScanResult = result;
            if (CurrentProject?.Id != project.Id)
            {
                return;
            }

            ApplyProjectScanResult(result, workspace.ProjectScanAutoSetupStatus);
            FileRulesStatus = "Project scan complete.";
        }
        catch (Exception ex)
        {
            workspace.ProjectScanResult = null;
            workspace.ProjectScanAutoSetupStatus = "";
            ApplyProjectScanError("Scan failed.", ex.Message);
            FileRulesStatus = "Project scan failed.";
        }
        finally
        {
            IsProjectScanRunning = false;
        }
    }

    private async Task<ProjectStackScanResult> ScanProjectRulesCoreAsync(ProjectTabViewModel project, ProjectWorkspaceState workspace)
    {
        SyncFileRuleTextsFromEntries();
        var rules = workspace.FileRules.CreateSnapshot(
            IgnoredDirectoriesText,
            IgnoredFileNamesText,
            IgnoredFileTypesText,
            SupportedFileTypesText,
            LocFileTypesText);

        return await ProjectStackScanner.ScanAsync(project.ProjectRoot, rules);
    }

    private async Task AutoSetupProjectRulesAsync()
    {
        if (IsProjectScanRunning)
        {
            return;
        }

        var project = CurrentProject;
        if (project is null || !_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            ProjectScanAutoSetupStatus = "Open a project before autosetup.";
            return;
        }

        IsProjectScanRunning = true;
        ApplyProjectScanBusy(workspace);
        FileRulesStatus = "Scanning project rules for autosetup...";

        try
        {
            var result = await ScanProjectRulesCoreAsync(project, workspace);
            workspace.ProjectScanResult = result;
            ApplyAutoSetupRules(workspace.FileRules, result.AutoSetupRules);
            workspace.FileRules.Save();
            workspace.ProjectScanAutoSetupStatus = $"Autosetup saved rules to {workspace.FileRules.RulesPath}";

            if (CurrentProject?.Id == project.Id)
            {
                ApplyFileRulesToEditor(workspace.FileRules, workspace.ProjectScanAutoSetupStatus);
                ApplyProjectScanResult(result, workspace.ProjectScanAutoSetupStatus);
                ReloadTrackerRules(project);
            }

            IsProjectScanRunning = false;
            await RefreshCurrentProjectFromDiskAsync();

            if (CurrentProject?.Id == project.Id)
            {
                PostToUi(() => _ = ScanProjectRulesAsync());
            }
        }
        catch (Exception ex)
        {
            workspace.ProjectScanAutoSetupStatus = "Autosetup failed.";
            if (CurrentProject?.Id == project.Id)
            {
                ProjectScanAutoSetupStatus = "Autosetup failed.";
                ProjectScanResultText = ex.Message;
                FileRulesStatus = "Autosetup failed.";
            }
        }
        finally
        {
            IsProjectScanRunning = false;
        }
    }

    private static void ApplyAutoSetupRules(ProjectFileRules rules, ProjectStackRuleSet ruleSet)
    {
        rules.ApplyCleanRules(
            ruleSet.IgnoredDirectories,
            ruleSet.IgnoredFileNames,
            ruleSet.IgnoredExtensions,
            ruleSet.SupportedExtensions,
            ruleSet.LocExtensions);
    }

    private void ApplyProjectScanBusy(ProjectWorkspaceState workspace)
    {
        ProjectScanSummary = "Scanning...";
        ProjectScanResultText = "";
        ProjectScanRuleSummary = "";
        ClearProjectScanCollections();
        ProjectScanAutoSetupStatus = workspace.ProjectScanAutoSetupStatus;
    }

    private void ApplyProjectScanError(string summary, string details)
    {
        ProjectScanSummary = summary;
        ProjectScanResultText = details;
        ProjectScanRuleSummary = "";
        ClearProjectScanCollections();
        ProjectScanAutoSetupStatus = "";
    }

    private void ApplyProjectScanResult(ProjectStackScanResult? result, string autoSetupStatus)
    {
        ClearProjectScanCollections();
        ProjectScanAutoSetupStatus = autoSetupStatus;

        if (result is null)
        {
            ProjectScanSummary = "No scan yet.";
            ProjectScanResultText = "Run Scan to inspect this project.";
            ProjectScanRuleSummary = "";
            return;
        }

        ProjectScanSummary = result.Summary;
        ProjectScanResultText = result.DetailsText;
        ProjectScanRuleSummary = result.RuleSummary;
        foreach (var metric in result.Metrics)
        {
            ProjectScanMetrics.Add(metric);
        }

        var sections = result.Sections.Where(section => section.Items.Count > 0).ToArray();
        foreach (var section in sections)
        {
            ProjectScanSections.Add(section);
        }

        AddScanSections(ProjectScanIdentitySections, sections, "Detected Stack", "Uses");
        AddScanSections(ProjectScanFileSections, sections, "Languages", "Top File Types", "Manifests");
        AddScanSections(ProjectScanRuleSections, sections, "Unsupported Visible Types", "Autosetup Plan", "Already Allowed", "Already Counted LOC");
        AddScanSections(ProjectScanDiagnosticSections, sections, "Skipped Samples");
    }

    private void ClearProjectScanCollections()
    {
        ProjectScanMetrics.Clear();
        ProjectScanSections.Clear();
        ProjectScanIdentitySections.Clear();
        ProjectScanFileSections.Clear();
        ProjectScanRuleSections.Clear();
        ProjectScanDiagnosticSections.Clear();
    }

    private static void AddScanSections(
        ICollection<ProjectStackSection> target,
        IReadOnlyCollection<ProjectStackSection> sections,
        params string[] titles)
    {
        foreach (var title in titles)
        {
            var section = sections.FirstOrDefault(item => string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase));
            if (section is not null)
            {
                target.Add(section);
            }
        }
    }

    private void ApplyFileRulesToEditor(ProjectFileRules rules, string status)
    {
        SupportedFileTypesLabel = rules.SupportedLabel;
        IgnoredFileTypesLabel = rules.IgnoredLabel;
        FileRulesPath = rules.RulesPath;
        SupportedFileTypesText = rules.SupportedExtensionsText;
        IgnoredFileTypesText = rules.IgnoredExtensionsText;
        LocFileTypesText = rules.LocExtensionsText;
        IgnoredFileNamesText = rules.IgnoredFileNamesText;
        IgnoredDirectoriesText = rules.IgnoredDirectoriesText;
        ReplaceRuleEntries(IgnoredDirectoryRules, RuleKindIgnoredDirectories, rules.IgnoredDirectories);
        ReplaceRuleEntries(IgnoredFileNameRules, RuleKindIgnoredFileNames, rules.IgnoredFileNames);
        ReplaceRuleEntries(IgnoredFileTypeRules, RuleKindIgnoredFileTypes, rules.IgnoredExtensions);
        ReplaceRuleEntries(SupportedFileTypeRules, RuleKindSupportedFileTypes, rules.SupportedExtensions);
        ReplaceRuleEntries(LocFileTypeRules, RuleKindLocFileTypes, rules.LocExtensions);
        FileRulesStatus = status;
        RefreshFileRuleSummary();
    }

    public IReadOnlyList<string> GetFileRuleEntries(string kind)
    {
        return GetRuleCollection(kind)
            .Select(entry => entry.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public void ReplaceFileRuleEntries(string kind, IEnumerable<string> values)
    {
        ReplaceRuleEntries(GetRuleCollection(kind), kind, NormalizeRuleEntries(values));
        SyncFileRuleTextsFromEntries();
        FileRulesStatus = "File rules changed. Save to apply.";
        RefreshFileRuleSummary();
    }

    public string GetFileRuleTitle(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => "Skipped folders",
            RuleKindIgnoredFileNames => "Skipped files",
            RuleKindIgnoredFileTypes => "Skipped file types",
            RuleKindSupportedFileTypes => "Allowed file types",
            RuleKindLocFileTypes => "LOC file types",
            _ => "File rules"
        };
    }

    public string GetFileRuleWatermark(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => "bin, obj, node_modules",
            RuleKindIgnoredFileNames => ".DS_Store, CMakeCache.txt",
            RuleKindIgnoredFileTypes => ".dll, .png, .tmp",
            RuleKindSupportedFileTypes => ".cs, .cpp, .ps1",
            RuleKindLocFileTypes => ".cs, .cpp, .md",
            _ => "new entry"
        };
    }

    private void AddFileRuleEntry(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        var text = GetNewRuleText(kind);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        AddRuleEntries(GetRuleCollection(kind), kind, SplitRuleText(text));
        SetNewRuleText(kind, "");
        SyncFileRuleTextsFromEntries();
        FileRulesStatus = "File rules changed. Save to apply.";
        RefreshFileRuleSummary();
    }

    private void RemoveFileRuleEntry(FileRuleEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        GetRuleCollection(entry.Kind).Remove(entry);
        SyncFileRuleTextsFromEntries();
        FileRulesStatus = "File rules changed. Save to apply.";
        RefreshFileRuleSummary();
    }

    private ObservableCollection<FileRuleEntryViewModel> GetRuleCollection(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => IgnoredDirectoryRules,
            RuleKindIgnoredFileNames => IgnoredFileNameRules,
            RuleKindIgnoredFileTypes => IgnoredFileTypeRules,
            RuleKindSupportedFileTypes => SupportedFileTypeRules,
            RuleKindLocFileTypes => LocFileTypeRules,
            _ => IgnoredDirectoryRules
        };
    }

    private string GetNewRuleText(string kind)
    {
        return kind switch
        {
            RuleKindIgnoredDirectories => NewIgnoredDirectoryRuleText,
            RuleKindIgnoredFileNames => NewIgnoredFileNameRuleText,
            RuleKindIgnoredFileTypes => NewIgnoredFileTypeRuleText,
            RuleKindSupportedFileTypes => NewSupportedFileTypeRuleText,
            RuleKindLocFileTypes => NewLocFileTypeRuleText,
            _ => ""
        };
    }

    private void SetNewRuleText(string kind, string value)
    {
        switch (kind)
        {
            case RuleKindIgnoredDirectories:
                NewIgnoredDirectoryRuleText = value;
                break;
            case RuleKindIgnoredFileNames:
                NewIgnoredFileNameRuleText = value;
                break;
            case RuleKindIgnoredFileTypes:
                NewIgnoredFileTypeRuleText = value;
                break;
            case RuleKindSupportedFileTypes:
                NewSupportedFileTypeRuleText = value;
                break;
            case RuleKindLocFileTypes:
                NewLocFileTypeRuleText = value;
                break;
        }
    }

    private static void ReplaceRuleEntries(
        ObservableCollection<FileRuleEntryViewModel> target,
        string kind,
        IEnumerable<string> values)
    {
        target.Clear();
        AddRuleEntries(target, kind, values);
    }

    private static void AddRuleEntries(
        ObservableCollection<FileRuleEntryViewModel> target,
        string kind,
        IEnumerable<string> values)
    {
        var existing = target
            .Select(entry => entry.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var value in NormalizeRuleEntries(values))
        {
            if (existing.Add(value))
            {
                target.Add(new FileRuleEntryViewModel(kind, value));
            }
        }
    }

    private void SyncFileRuleTextsFromEntries()
    {
        SupportedFileTypesText = JoinRuleEntries(SupportedFileTypeRules);
        IgnoredFileTypesText = JoinRuleEntries(IgnoredFileTypeRules);
        LocFileTypesText = JoinRuleEntries(LocFileTypeRules);
        IgnoredFileNamesText = JoinRuleEntries(IgnoredFileNameRules);
        IgnoredDirectoriesText = JoinRuleEntries(IgnoredDirectoryRules);
    }

    private void RefreshFileRuleSummary()
    {
        OnPropertyChanged(nameof(FileRulesSummary));
    }

    private static string JoinRuleEntries(IEnumerable<FileRuleEntryViewModel> entries)
    {
        return string.Join(Environment.NewLine, NormalizeRuleEntries(entries.Select(entry => entry.Value)));
    }

    private static IEnumerable<string> NormalizeRuleEntries(IEnumerable<string> values)
    {
        return values
            .SelectMany(SplitRuleText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitRuleText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private void ReloadTrackerRules(ProjectTabViewModel project)
    {
        if (_trackersByProjectId.TryGetValue(project.Id, out var tracker))
        {
            tracker.ReloadRules();
        }
    }

    public void ScanExternalChangesNow()
    {
        if (CurrentProject is null)
        {
            return;
        }

        StartExternalTracker(CurrentProject);
        if (_trackersByProjectId.TryGetValue(CurrentProject.Id, out var tracker))
        {
            tracker.ForceScanNow();
        }
    }

    public async Task LoadProjectAsync(string folderPath)
    {
        var loadedProject = await ProjectLoader.LoadAsync(folderPath, showSkippedFiles: ShowSkippedFiles);
        var existing = Projects.FirstOrDefault(project =>
            string.Equals(project.ProjectRoot, loadedProject.Project.ProjectRoot, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var existingIndex = Projects.IndexOf(existing);
            Projects[existingIndex] = loadedProject.Project;
            if (_trackersByProjectId.Remove(existing.Id, out var oldTracker))
            {
                oldTracker.Dispose();
            }
        }
        else
        {
            Projects.Add(loadedProject.Project);
        }

        var loadedWorkspace = new ProjectWorkspaceState(
            loadedProject.Tree,
            loadedProject.HistoryByPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new ObservableCollection<ExternalChangeItemViewModel>(),
            loadedProject.FileRules,
            loadedProject.IsTreePrepared);
        if (existing is not null && _workspaceByProjectId.TryGetValue(existing.Id, out var previousWorkspace))
        {
            loadedWorkspace.CopyScannerStateFrom(previousWorkspace);
        }

        _workspaceByProjectId[loadedProject.Project.Id] = loadedWorkspace;
        SelectProject(loadedProject.Project);
    }

    private async Task IncludeExternalNodeAsync(ProjectNodeViewModel? node)
    {
        if (CurrentProject is null
            || node is not { CanIncludeExternal: true }
            || string.IsNullOrWhiteSpace(CurrentProject.ProjectRoot)
            || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        var includedPaths = workspace.IncludedExternalPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        includedPaths.Add(node.Path);

        var loadedProject = await ProjectLoader.LoadAsync(CurrentProject.ProjectRoot, includedPaths, ShowSkippedFiles);
        var existingIndex = Projects.IndexOf(CurrentProject);
        if (existingIndex >= 0)
        {
            Projects[existingIndex] = loadedProject.Project;
        }

        var refreshedWorkspace = new ProjectWorkspaceState(
            loadedProject.Tree,
            loadedProject.HistoryByPath,
            includedPaths,
            workspace.ExternalChanges,
            loadedProject.FileRules,
            loadedProject.IsTreePrepared);
        refreshedWorkspace.CopyScannerStateFrom(workspace);
        _workspaceByProjectId[loadedProject.Project.Id] = refreshedWorkspace;
        SelectProject(loadedProject.Project);
    }

    private void ToggleNode(TreeRowViewModel? row)
    {
        if (row?.Node is not { HasChildren: true } node)
        {
            return;
        }

        var nodeRowIndex = VisibleTreeRows.IndexOf(row);
        var nodeIndex = nodeRowIndex;
        if (nodeRowIndex < 0
            || nodeIndex >= VisibleProjectNodes.Count
            || !ReferenceEquals(VisibleProjectNodes[nodeIndex], node))
        {
            nodeRowIndex = FindNodeRowIndex(node);
            nodeIndex = VisibleProjectNodes.IndexOf(node);
        }

        if (nodeIndex < 0 || nodeRowIndex < 0)
        {
            RefreshVisibleProjectNodes();
            return;
        }

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            row.RefreshExpansionState();
            CollapseVisibleNodeBranch(node, nodeIndex, nodeRowIndex);
            return;
        }

        node.IsExpanded = true;
        row.RefreshExpansionState();
        ExpandVisibleNodeBranch(node, nodeIndex, nodeRowIndex);
    }

    private void RefreshVisibleProjectNodes()
    {
        var visibleNodes = new List<ProjectNodeViewModel>();
        var visibleRows = new List<TreeRowViewModel>();

        foreach (var node in ProjectTree)
        {
            AddVisibleNode(node, visibleNodes, visibleRows);
        }

        VisibleProjectNodes.Clear();
        VisibleProjectNodes.AddRange(visibleNodes);
        VisibleTreeRows.Clear();
        VisibleTreeRows.AddRange(visibleRows);
    }

    private void ExpandVisibleNodeBranch(ProjectNodeViewModel node, int nodeIndex, int nodeRowIndex)
    {
        var expandedNodes = new List<ProjectNodeViewModel>();
        var expandedRows = new List<TreeRowViewModel>();
        CollectExpandedBranchRows(node, expandedNodes, expandedRows);
        if (expandedRows.Count == 0)
        {
            return;
        }

        VisibleProjectNodes.InsertRange(nodeIndex + 1, expandedNodes);
        VisibleTreeRows.InsertRange(nodeRowIndex + 1, expandedRows);
    }

    private void CollapseVisibleNodeBranch(ProjectNodeViewModel node, int nodeIndex, int nodeRowIndex)
    {
        var removeNodeCount = CountVisibleDescendantNodes(nodeIndex, node.Depth);
        var removeRowCount = CountVisibleDescendantRows(nodeRowIndex, node.Depth);
        if (removeNodeCount == 0 && removeRowCount == 0)
        {
            return;
        }

        VisibleProjectNodes.RemoveRange(nodeIndex + 1, removeNodeCount);
        VisibleTreeRows.RemoveRange(nodeRowIndex + 1, removeRowCount);

        var visibleRows = VisibleTreeRows.ToHashSet();
        var selectedRows = SelectedTreeRows
            .Where(visibleRows.Contains)
            .ToArray();
        if (selectedRows.Length == SelectedTreeRows.Count)
        {
            return;
        }

        SetSelectedTreeRows(selectedRows.Length > 0
            ? selectedRows
            : [VisibleTreeRows[nodeRowIndex]]);
    }

    private int CountVisibleDescendantNodes(int nodeIndex, int parentDepth)
    {
        var removeCount = 0;
        for (var index = nodeIndex + 1; index < VisibleProjectNodes.Count; index++)
        {
            if (VisibleProjectNodes[index].Depth <= parentDepth)
            {
                break;
            }

            removeCount++;
        }

        return removeCount;
    }

    private int CountVisibleDescendantRows(int nodeRowIndex, int parentDepth)
    {
        var removeCount = 0;
        for (var index = nodeRowIndex + 1; index < VisibleTreeRows.Count; index++)
        {
            if (VisibleTreeRows[index].Depth <= parentDepth)
            {
                break;
            }

            removeCount++;
        }

        return removeCount;
    }

    private int FindNodeRowIndex(ProjectNodeViewModel node)
    {
        for (var index = 0; index < VisibleTreeRows.Count; index++)
        {
            if (ReferenceEquals(VisibleTreeRows[index].Node, node))
            {
                return index;
            }
        }

        return -1;
    }

    private static void CollectExpandedBranchRows(
        ProjectNodeViewModel parent,
        ICollection<ProjectNodeViewModel> expandedNodes,
        ICollection<TreeRowViewModel> expandedRows)
    {
        foreach (var child in parent.Children)
        {
            expandedNodes.Add(child);
            expandedRows.Add(TreeRowViewModel.ForNode(child));

            if (child.IsExpanded && child.HasChildren)
            {
                CollectExpandedBranchRows(child, expandedNodes, expandedRows);
            }
        }
    }

    private void RefreshCurrentRowHighlights(ProjectNodeViewModel? previous, ProjectNodeViewModel? current)
    {
        foreach (var row in VisibleTreeRows)
        {
            if (ReferenceEquals(row.Node, previous) || ReferenceEquals(row.Node, current))
            {
                row.RefreshCurrentState();
            }
        }
    }

    private static void AddVisibleNode(
        ProjectNodeViewModel node,
        ICollection<ProjectNodeViewModel> visibleNodes,
        ICollection<TreeRowViewModel> visibleRows)
    {
        visibleNodes.Add(node);
        visibleRows.Add(TreeRowViewModel.ForNode(node));

        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisibleNode(child, visibleNodes, visibleRows);
        }
    }

    private void PrepareTree()
    {
        for (var index = 0; index < ProjectTree.Count; index++)
        {
            PrepareNode(ProjectTree[index], 0, index == ProjectTree.Count - 1, []);
        }
    }

    private static void PrepareNode(
        ProjectNodeViewModel node,
        int depth,
        bool isLast,
        IReadOnlyList<bool> ancestorContinues)
    {
        node.SetTreeState(depth, isLast, ancestorContinues);
        if (depth <= DefaultExpandedDepth && !node.IsExternal)
        {
            node.IsExpanded = true;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var childIsLast = index == node.Children.Count - 1;
            var childAncestors = ancestorContinues.Concat([!childIsLast]).ToArray();

            PrepareNode(child, depth + 1, childIsLast, childAncestors);
        }
    }

    private void LoadWorkspace(ProjectWorkspaceState workspace)
    {
        ProjectTree.Clear();
        foreach (var node in workspace.ProjectTree)
        {
            ProjectTree.Add(node);
        }

        _historyByPath = workspace.HistoryByPath;
        ExternalChanges.Clear();
        foreach (var change in workspace.ExternalChanges)
        {
            RegisterExternalChangeItem(change);
            ExternalChanges.Add(change);
        }

        SelectedNode = null;
        SelectedTreeRow = null;
        SelectedHistory = null;
        ClearActiveVersion();
        Interlocked.Increment(ref _documentLoadVersion);
        ActiveDocument = EditorDocumentViewModel.Empty();
        ApplyFileRulesToEditor(workspace.FileRules, "");
        ApplyProjectScanResult(workspace.ProjectScanResult, workspace.ProjectScanAutoSetupStatus);
        CloseHistory();
        if (!workspace.TreeStatePrepared)
        {
            PrepareTree();
            workspace.TreeStatePrepared = true;
        }

        RefreshVisibleProjectNodes();
        RefreshProjectGraph();
        RefreshExternalChangeLabels();
        RecalculateExternalChangeFlows(workspace.ExternalChanges);
    }

    private void OpenDocument(ProjectNodeViewModel node)
    {
        var path = ResolveNodePath(node);
        if (path is null)
        {
            Interlocked.Increment(ref _documentLoadVersion);
            ActiveDocument = EditorDocumentViewModel.Empty();
            SelectedWorkspaceMode = WorkspaceModes[0];
            return;
        }

        OpenDocumentAsync(path, node.Path, node.VersionLabel, node.Loc);
        SelectedWorkspaceMode = WorkspaceModes[0];
    }

    private void SelectFileNode(ProjectNodeViewModel node)
    {
        ClearActiveVersion();
        SelectHistory(node.Path);
        OpenDocument(node);
    }

    private void SelectHistory(string path)
    {
        SelectedHistory = _historyByPath.TryGetValue(path, out var history)
            ? history
            : new FileHistoryViewModel(
                Path.GetFileName(path),
                path,
                [new VersionEntryViewModel(
                    "v1",
                    "2026-05-10",
                    CurrentProject?.Commit ?? "local",
                    "tracked by Context Control",
                    Path.GetFileName(path),
                    path,
                    currentFilePath: SelectedNode is { } selectedNode
                        ? ResolveNodePath(selectedNode) ?? ""
                        : "")]);
        SelectedHistory.EnsureStatsLoaded();
    }

    private void OpenVersion(VersionEntryViewModel? version)
    {
        if (version is null)
        {
            return;
        }

        ClearActiveVersion();
        SelectedHistory?.EnsureStatsLoaded();
        version.IsActive = true;
        _selectedVersion = version;
        OpenVersionAsync(version);
        IsHistoryOpen = true;
        HistoryWidth = 330;
        HistoryOpacity = 1;
        HistoryGutter = 8;
    }

    private void OpenDocumentAsync(string absolutePath, string displayPath, string version = "", long loc = 0)
    {
        var loadVersion = Interlocked.Increment(ref _documentLoadVersion);
        ActiveDocument = EditorDocumentViewModel.Loading(displayPath);
        _ = LoadDocumentCoreAsync(loadVersion, absolutePath, displayPath, version, loc);
    }

    private async Task LoadDocumentCoreAsync(int loadVersion, string absolutePath, string displayPath, string version, long loc)
    {
        var document = await Task.Run(() => EditorDocumentViewModel.Load(absolutePath, displayPath, version, loc));
        PostDocument(loadVersion, document);
    }

    private void OpenVersionAsync(VersionEntryViewModel version)
    {
        var loadVersion = Interlocked.Increment(ref _documentLoadVersion);
        ActiveDocument = EditorDocumentViewModel.Loading(version.FilePath);
        _ = LoadVersionCoreAsync(loadVersion, version);
    }

    private async Task LoadVersionCoreAsync(int loadVersion, VersionEntryViewModel version)
    {
        var document = await Task.Run(() => EditorDocumentViewModel.LoadVersion(version));
        PostDocument(loadVersion, document);
    }

    private void PostDocument(int loadVersion, EditorDocumentViewModel document)
    {
        PostToUi(() =>
        {
            if (loadVersion == Volatile.Read(ref _documentLoadVersion))
            {
                ActiveDocument = document;
            }
        });
    }

    private void ClearActiveVersion()
    {
        if (_selectedVersion is not null)
        {
            _selectedVersion.IsActive = false;
            _selectedVersion = null;
        }
    }

    private string? ResolveNodePath(ProjectNodeViewModel node)
    {
        if (string.IsNullOrWhiteSpace(node.Path))
        {
            return null;
        }

        if (Path.IsPathRooted(node.Path))
        {
            return node.Path;
        }

        return string.IsNullOrWhiteSpace(CurrentProject?.ProjectRoot)
            ? null
            : Path.Combine(CurrentProject.ProjectRoot, node.Path);
    }

    private string GetAttachmentDisplayPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(CurrentProject?.ProjectRoot))
        {
            return fullPath;
        }

        var projectRoot = CurrentProject.ProjectRoot;
        if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(projectRoot, fullPath)
                .Replace('\\', '/');
            relative = NormalizeProjectPath(relative);
            return string.IsNullOrWhiteSpace(relative) ? fullPath : relative;
        }

        return fullPath;
    }

    private bool TryResolveAttachment(string? path, out string fullPath, out string displayPath)
    {
        fullPath = "";
        displayPath = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        displayPath = GetAttachmentDisplayPath(fullPath);
        return true;
    }

    private void StartExternalTracker(ProjectTabViewModel project)
    {
        if (_trackersByProjectId.ContainsKey(project.Id)
            || string.IsNullOrWhiteSpace(project.ProjectRoot)
            || !Directory.Exists(project.ProjectRoot))
        {
            return;
        }

        var tracker = new ExternalChangeTracker(project.ProjectRoot);
        tracker.ChangeCaptured += (_, change) => PostToUi(() => OnExternalChangeCaptured(project.Id, change));
        _trackersByProjectId[project.Id] = tracker;

        if (_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            workspace.FileRules = tracker.FileRules;
            if (ReferenceEquals(CurrentProject, project))
            {
                ApplyFileRulesToEditor(tracker.FileRules, FileRulesStatus);
            }
        }
    }

    private void QueueExternalTrackerStart(ProjectTabViewModel project, int switchVersion)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            PostToUi(() =>
            {
                if (switchVersion != Volatile.Read(ref _projectSwitchVersion)
                    || CurrentProject?.Id != project.Id)
                {
                    return;
                }

                StartExternalTracker(project);
            });
        });
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private void OnExternalChangeCaptured(string projectId, ExternalFileChange change)
    {
        if (!_workspaceByProjectId.TryGetValue(projectId, out var workspace))
        {
            return;
        }

        if (workspace.ExternalChanges.Any(item => item.QueueId.Equals($"{change.RelativePath}|{change.VersionAfter}", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var item = new ExternalChangeItemViewModel(change);
        RegisterExternalChangeItem(item);
        workspace.ExternalChanges.Add(item);

        if (CurrentProject?.Id == projectId)
        {
            ExternalChanges.Add(item);
            RefreshExternalChangeLabels();
            ReloadActiveDocumentIfChanged(change);
        }

        RecalculateExternalChangeFlows(workspace.ExternalChanges);
    }


    private void ReloadActiveDocumentIfChanged(ExternalFileChange change)
    {
        if (SelectedNode is not { IsFile: true })
        {
            return;
        }

        if (!string.Equals(NormalizeProjectPath(SelectedNode.Path), NormalizeProjectPath(change.RelativePath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ClearActiveVersion();
        SelectHistory(SelectedNode.Path);
        OpenDocument(SelectedNode);
    }

    private static string NormalizeProjectPath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private void RegisterExternalChangeItem(ExternalChangeItemViewModel item)
    {
        item.SelectionChanged -= OnExternalChangeSelectionChanged;
        item.SelectionChanged += OnExternalChangeSelectionChanged;
    }

    private void OnExternalChangeSelectionChanged(object? sender, EventArgs e)
    {
        if (CurrentProject is not null && _workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            RecalculateExternalChangeFlows(workspace.ExternalChanges);
        }
    }

    private void ToggleExternalChange(ExternalChangeItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
    }

    private void AcceptAllExternalChanges()
    {
        foreach (var item in ExternalChanges)
        {
            item.IsSelected = true;
        }

        AcceptSelectedExternalChanges();
    }

    private void AcceptFinalExternalChanges()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        var queued = workspace.ExternalChanges.ToArray();
        if (queued.Length == 0)
        {
            return;
        }

        ExternalVersionQueueStore.AcceptOnlyFinal(CurrentProject.ProjectRoot, queued.Select(item => item.Change));
        RemoveExternalChanges(workspace, item => queued.Contains(item));
        _ = RefreshCurrentProjectFromDiskAsync();
    }

    private void AcceptSelectedExternalChanges()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        RemoveExternalChanges(workspace, item => item.IsSelected);
        _ = RefreshCurrentProjectFromDiskAsync();
    }

    private void DismissSelectedExternalChanges()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        RemoveExternalChanges(workspace, item => item.IsSelected);
    }

    private void RemoveExternalChanges(ProjectWorkspaceState workspace, Func<ExternalChangeItemViewModel, bool> predicate)
    {
        var toRemove = workspace.ExternalChanges.Where(predicate).ToArray();
        foreach (var item in toRemove)
        {
            item.SelectionChanged -= OnExternalChangeSelectionChanged;
            workspace.ExternalChanges.Remove(item);
            ExternalChanges.Remove(item);
        }

        RecalculateExternalChangeFlows(workspace.ExternalChanges);
        RefreshExternalChangeLabels();
    }

    private async Task RefreshCurrentProjectFromDiskAsync()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var currentWorkspace))
        {
            return;
        }

        var projectRoot = CurrentProject.ProjectRoot;
        var projectId = CurrentProject.Id;
        var loadedProject = await ProjectLoader.LoadAsync(projectRoot, currentWorkspace.IncludedExternalPaths, ShowSkippedFiles);
        var existingIndex = Projects.IndexOf(CurrentProject);
        if (existingIndex >= 0)
        {
            Projects[existingIndex] = loadedProject.Project;
        }

        var refreshedWorkspace = new ProjectWorkspaceState(
            loadedProject.Tree,
            loadedProject.HistoryByPath,
            currentWorkspace.IncludedExternalPaths,
            currentWorkspace.ExternalChanges,
            loadedProject.FileRules,
            loadedProject.IsTreePrepared);
        refreshedWorkspace.CopyScannerStateFrom(currentWorkspace);
        _workspaceByProjectId[loadedProject.Project.Id] = refreshedWorkspace;

        if (loadedProject.Project.Id != projectId && _trackersByProjectId.Remove(projectId, out var tracker))
        {
            _trackersByProjectId[loadedProject.Project.Id] = tracker;
        }

        SelectProject(loadedProject.Project);
    }

    private void RecalculateExternalChangeFlows(IEnumerable<ExternalChangeItemViewModel> changes)
    {
        foreach (var group in changes.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(item => item.Change.VersionAfter).ToArray();
            var baseVersion = ordered.FirstOrDefault()?.Change.VersionBefore ?? 0;
            var baseSnapshot = ordered.FirstOrDefault()?.Change.PreviousSnapshotPath ?? "";

            foreach (var item in ordered)
            {
                item.SetEffectiveBase(baseVersion, baseSnapshot);
                if (item.IsSelected)
                {
                    baseVersion = item.Change.VersionAfter;
                    baseSnapshot = item.Change.SnapshotPath;
                }
            }
        }
    }

    private void RefreshExternalChangeLabels()
    {
        OnPropertyChanged(nameof(HasExternalChanges));
        OnPropertyChanged(nameof(ExternalQueueTitle));
    }

    private static ProjectNodeViewModel? FindProjectNodeByPath(IEnumerable<ProjectNodeViewModel> nodes, string path)
    {
        var normalized = NormalizeProjectPath(path);
        foreach (var node in nodes)
        {
            if (node.IsFile && string.Equals(NormalizeProjectPath(node.Path), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = FindProjectNodeByPath(node.Children, normalized);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static ObservableCollection<ProjectNodeViewModel> CloneTree(IEnumerable<ProjectNodeViewModel> nodes)
    {
        return new ObservableCollection<ProjectNodeViewModel>(nodes.Select(CloneNode));
    }

    private static ProjectNodeViewModel CloneNode(ProjectNodeViewModel node)
    {
        var clone = new ProjectNodeViewModel(
            node.Name,
            node.Path,
            node.IsFolder,
            node.VersionLabel,
            node.Children.Select(CloneNode),
            node.IsExternal,
            node.CanIncludeExternal,
            node.Loc,
            node.FileCount,
            node.DiskFileCount,
            node.DirectDiskFileCount);
        clone.IsExpanded = node.IsExpanded;
        clone.SetTreeState(node.Depth, node.IsLast, node.AncestorContinues);
        return clone;
    }

    private sealed class ProjectWorkspaceState(
        ObservableCollection<ProjectNodeViewModel> projectTree,
        Dictionary<string, FileHistoryViewModel> historyByPath,
        IReadOnlySet<string> includedExternalPaths,
        ObservableCollection<ExternalChangeItemViewModel> externalChanges,
        ProjectFileRules fileRules,
        bool treeStatePrepared = false)
    {
        public ObservableCollection<ProjectNodeViewModel> ProjectTree { get; } = projectTree;
        public Dictionary<string, FileHistoryViewModel> HistoryByPath { get; } = historyByPath;
        public IReadOnlySet<string> IncludedExternalPaths { get; } = includedExternalPaths;
        public ObservableCollection<ExternalChangeItemViewModel> ExternalChanges { get; } = externalChanges;
        public ProjectFileRules FileRules { get; set; } = fileRules;
        public bool TreeStatePrepared { get; set; } = treeStatePrepared;
        public ProjectStackScanResult? ProjectScanResult { get; set; }
        public string ProjectScanAutoSetupStatus { get; set; } = "";

        public void CopyScannerStateFrom(ProjectWorkspaceState other)
        {
            ProjectScanResult = other.ProjectScanResult;
            ProjectScanAutoSetupStatus = other.ProjectScanAutoSetupStatus;
        }
    }

    private readonly record struct ProjectGraphSearchEntry(
        ProjectNodeViewModel Node,
        string Title,
        string Detail,
        string Meta,
        int Depth,
        int Order);
}
