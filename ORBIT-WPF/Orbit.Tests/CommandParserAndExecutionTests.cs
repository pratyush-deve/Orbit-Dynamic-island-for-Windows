using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Orbit.Models;
using Orbit.Services.Commands;
using Orbit.Services.Commands.Handlers;
using Orbit.Services.Notes;
using Xunit;

namespace Orbit.Tests;

public class CommandParserAndExecutionTests
{
    private readonly AppRegistry _appRegistry = new();
    private readonly CommandParser _parser;

    public CommandParserAndExecutionTests()
    {
        _parser = new CommandParser(_appRegistry);
    }

    [Fact]
    public void CommandParser_RecognizesOpenAndroidStudio()
    {
        var intent = _parser.Parse("Open Android Studio");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("Android Studio", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesLaunchGoogleChrome()
    {
        var intent = _parser.Parse("Launch Google Chrome");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("Google Chrome", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesStartNotepad()
    {
        var intent = _parser.Parse("Start Notepad");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("Notepad", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesOpenCalculator()
    {
        var intent = _parser.Parse("Open Calculator");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("Calculator", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesWebSearch_SearchGoogleForKotlinJetpackCompose()
    {
        var intent = _parser.Parse("Search Google for Kotlin Jetpack Compose");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("Kotlin Jetpack Compose", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesWebSearch_SearchForWifiSpeedTest()
    {
        var intent = _parser.Parse("Search for Wi-Fi speed test");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("Wi-Fi speed test", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesWebSearch_OpenGoogleAndSearchForWifiSpeedTest()
    {
        var intent = _parser.Parse("Open Google and search for wifi speed test");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("wifi speed test", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesAppLaunch_OpenGoogle()
    {
        var intent = _parser.Parse("Open Google");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("Google", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var target = _appRegistry.ResolveApp(intent.Target!);
        Assert.NotNull(target);
        Assert.Contains("google", target.ExecutableOrUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandParser_RecognizesWebSearch_LookUpWhisperCpp()
    {
        var intent = _parser.Parse("Look up Whisper.cpp");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("Whisper.cpp", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesFileSearch_FindAndroidStudioFiles()
    {
        var intent = _parser.Parse("Find Android Studio files");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("Android Studio", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesFileSearch_SearchMyLaptopForPDFFiles()
    {
        var intent = _parser.Parse("Search my laptop for PDF files");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("PDF files", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesFileSearch_FindFilesNamedProjectReport()
    {
        var intent = _parser.Parse("Find files named project report");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("project report", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var searchForNamed = _parser.Parse("Search for files named project report");
        Assert.Equal(CommandType.FileSearch, searchForNamed.Type);
        Assert.Equal("project report", searchForNamed.Target);
    }

    [Fact]
    public void CommandParser_RecognizesFileSearch_FindSingleFile()
    {
        var intent = _parser.Parse("Find the file resume.docx");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("resume.docx", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesFileSearch_SearchThisPcForBudget()
    {
        var intent = _parser.Parse("Search this PC for budget");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("budget", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesFileSearch_FindFilesInSpecificFolder()
    {
        var intent = _parser.Parse("Find random college files in my desktop");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("random college", intent.Target);
        Assert.Equal("desktop", intent.Scope);
        Assert.True(intent.Confidence >= 0.60f);

        var intentSearch = _parser.Parse("Search desktop for random college files");
        Assert.Equal(CommandType.FileSearch, intentSearch.Type);
        Assert.Equal("random college", intentSearch.Target);
        Assert.Equal("desktop", intentSearch.Scope);

        var intentDownloads = _parser.Parse("Find invoice files in downloads");
        Assert.Equal(CommandType.FileSearch, intentDownloads.Type);
        Assert.Equal("invoice", intentDownloads.Target);
        Assert.Equal("downloads", intentDownloads.Scope);
    }

    [Fact]
    public void CommandParser_RecognizesVolumePercentage_NumberWords()
    {
        var intent = _parser.Parse("Set volume to fifty percent");
        Assert.Equal(CommandType.SystemControl, intent.Type);
        Assert.Equal(SystemControlAction.SetVolume, intent.ControlAction);
        Assert.Equal(50, intent.Value);
    }

    [Fact]
    public void CommandParser_RecognizesVolumePercentage_Numeric()
    {
        var intent = _parser.Parse("Set volume to 50%");
        Assert.Equal(CommandType.SystemControl, intent.Type);
        Assert.Equal(SystemControlAction.SetVolume, intent.ControlAction);
        Assert.Equal(50, intent.Value);
    }

    [Fact]
    public void CommandParser_RecognizesVolumeIncreaseAndDecrease()
    {
        var inc = _parser.Parse("Increase volume");
        Assert.Equal(CommandType.SystemControl, inc.Type);
        Assert.Equal(SystemControlAction.IncreaseVolume, inc.ControlAction);

        var dec = _parser.Parse("Decrease volume");
        Assert.Equal(CommandType.SystemControl, dec.Type);
        Assert.Equal(SystemControlAction.DecreaseVolume, dec.ControlAction);
    }

    [Fact]
    public void CommandParser_RecognizesMuteAndUnmute()
    {
        var mute = _parser.Parse("Mute");
        Assert.Equal(CommandType.SystemControl, mute.Type);
        Assert.Equal(SystemControlAction.Mute, mute.ControlAction);

        var unmute = _parser.Parse("Unmute");
        Assert.Equal(CommandType.SystemControl, unmute.Type);
        Assert.Equal(SystemControlAction.Unmute, unmute.ControlAction);
    }

    [Fact]
    public void CommandParser_RecognizesTakeScreenshot()
    {
        var shot1 = _parser.Parse("Take a screenshot");
        Assert.Equal(CommandType.Screenshot, shot1.Type);

        var shot2 = _parser.Parse("Capture my screen");
        Assert.Equal(CommandType.Screenshot, shot2.Type);
    }

    [Fact]
    public void CommandParser_RecognizesCreateNote()
    {
        var intent = _parser.Parse("Create a note saying finish my project");
        Assert.Equal(CommandType.TakeNote, intent.Type);
        Assert.Equal("finish my project", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var intentThat = _parser.Parse("Take a note that I have a meeting at 930 today");
        Assert.Equal(CommandType.TakeNote, intentThat.Type);
        Assert.Equal("I have a meeting at 930 today", intentThat.Target);
    }

    [Fact]
    public void CommandParser_RejectsUnknownPhrase()
    {
        var intent = _parser.Parse("What is the meaning of life");
        Assert.Equal(CommandType.Unknown, intent.Type);
        Assert.Equal(0.0f, intent.Confidence);
    }

    [Fact]
    public void CommandParser_RejectsCommandInjectionCharacters()
    {
        var intent = _parser.Parse("open notepad.exe; rm -rf /");
        Assert.Equal(CommandType.Unknown, intent.Type);
    }

    private class FakeNotesService : INotesService
    {
        public string Content { get; set; } = string.Empty;
        public event Action<string>? NotesUpdated;

        public string LoadNotes() => Content;
        public void SaveNotes(string content)
        {
            Content = content;
            NotesUpdated?.Invoke(content);
        }
        public Task SaveNotesAsync(string content)
        {
            Content = content;
            NotesUpdated?.Invoke(content);
            return Task.CompletedTask;
        }
        public bool ExportNotes(string destinationFilePath, string content) => true;
        public void ClearNotes()
        {
            Content = string.Empty;
            NotesUpdated?.Invoke(string.Empty);
        }
    }

    [Fact]
    public async Task NotesHandler_AlwaysAppendsToExistingNotesWithNewLine_NeverOverwrites()
    {
        var fakeNotes = new FakeNotesService { Content = "Existing Task 1\nExisting Task 2" };
        var handler = new NotesHandler(fakeNotes);

        var intent = new CommandIntent(CommandType.TakeNote, "Create a note saying finish my project", 0.95f, Target: "finish my project");
        var result = await handler.ExecuteAsync(intent);

        Assert.True(result.Success);
        Assert.Equal("Existing Task 1\nExisting Task 2\nfinish my project", fakeNotes.Content);
    }

    [Fact]
    public void AppRegistry_ResolvesAliasesSafely()
    {
        Assert.True(_appRegistry.IsRecognizedApp("chrome"));
        Assert.True(_appRegistry.IsRecognizedApp("android studio"));
        Assert.True(_appRegistry.IsRecognizedApp("notepad"));
        Assert.True(_appRegistry.IsRecognizedApp("calculator"));

        var targetCalc = _appRegistry.ResolveApp("calc");
        Assert.NotNull(targetCalc);
        Assert.Equal("Calculator", targetCalc.DisplayName);
        Assert.Equal("calc.exe", targetCalc.ExecutableOrUri);

        var targetNotepad = _appRegistry.ResolveApp("notepad");
        Assert.NotNull(targetNotepad);
        Assert.Equal("Notepad", targetNotepad.DisplayName);
        Assert.Equal("notepad.exe", targetNotepad.ExecutableOrUri);
    }

    [Fact]
    public async Task CommandExecutor_DispatchesRegisteredHandlersSuccessfully()
    {
        var fakeNotes = new FakeNotesService();
        var notesHandler = new NotesHandler(fakeNotes);
        var executor = new CommandExecutionService([notesHandler]);

        var intent = new CommandIntent(CommandType.TakeNote, "Take a note buy milk", 0.95f, Target: "buy milk");
        var result = await executor.ExecuteAsync(intent);

        Assert.True(result.Success);
        Assert.Equal("Note added to Orbit Notes", result.Message);
        Assert.Equal("buy milk", fakeNotes.Content);
    }

    [Fact]
    public void CommandParser_RecognizesConversationalLaunch_CanYouOpenGoogleChromeForMe()
    {
        var intent = _parser.Parse("Can you open google chrome for me");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("google chrome", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var target = _appRegistry.ResolveApp(intent.Target!);
        Assert.NotNull(target);
        Assert.Contains("chrome.exe", target.ExecutableOrUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandParser_RecognizesVoiceCorrection_OperateStudio()
    {
        var intent = _parser.Parse("Operate studio");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("studio", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var target = _appRegistry.ResolveApp(intent.Target!);
        Assert.NotNull(target);
        Assert.Equal("Android Studio", target.DisplayName);
    }

    [Fact]
    public void CommandParser_RecognizesVoiceCorrection_OpenTheAndroidThing()
    {
        var intent = _parser.Parse("Open the android thing");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("android thing", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var target = _appRegistry.ResolveApp(intent.Target!);
        Assert.NotNull(target);
        Assert.Equal("Android Studio", target.DisplayName);
    }

    [Fact]
    public void CommandParser_RecognizesVoiceCorrection_OpenMyMales()
    {
        var intent = _parser.Parse("Open my males");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("males", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);

        var target = _appRegistry.ResolveApp(intent.Target!);
        Assert.NotNull(target);
        Assert.Equal("Gmail", target.DisplayName);
    }

    [Fact]
    public void CommandParser_RecognizesAmazonSearch_SearchForMensSocksInAmazon()
    {
        var intent = _parser.Parse("Search for men's socks of size XL in amazon");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("amazon", intent.Scope);
        Assert.Equal("men's socks of size XL", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    [Fact]
    public void CommandParser_RecognizesAmazonSearch_OpenChromeAndSearchAmazonForMensSocks()
    {
        var intent = _parser.Parse("Open chrome and search amazon for men's socks excel size");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("amazon", intent.Scope);
        Assert.Equal("men's socks excel size", intent.Target);
        Assert.True(intent.Confidence >= 0.60f);
    }

    // ==========================================
    // Unified Routing & Precedence Tests (14 Required Scenarios)
    // ==========================================

    [Fact]
    public void Scenario01_FindCollegeFilesOnMyDesktop_RoutesToLocalFileSearch()
    {
        var intent = _parser.Parse("Find college files on my desktop");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("desktop", intent.Scope);
        Assert.True(intent.Target == "college" || intent.Target == "college files");
    }

    [Fact]
    public void Scenario02_OpenFileExplorerAndSearchCollegeFilesOnDesktop_RoutesToLocalFileSearch()
    {
        var intent = _parser.Parse("Open File Explorer and search for college files on my desktop");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("desktop", intent.Scope);
        Assert.True(intent.Target == "college" || intent.Target == "college files");
    }

    [Fact]
    public void Scenario02b_OpenFileExplorerAndSearchRandomCollegeFilesFolder_RoutesToLocalFileSearch()
    {
        var intent = _parser.Parse("Open File Explorer and search for random college files folder in my desktop");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("desktop", intent.Scope);
        Assert.Equal("random college files folder", intent.Target);
    }

    [Fact]
    public void Scenario03_SearchDownloadsForPDFs_RoutesToLocalFileSearch()
    {
        var intent = _parser.Parse("Search Downloads for PDFs");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("downloads", intent.Scope);
        Assert.Equal("PDFs", intent.Target);
    }

    [Fact]
    public void Scenario04_SearchMyntraForOversizedHoodies_RoutesToWebsiteSearch()
    {
        var intent = _parser.Parse("Search Myntra for oversized hoodies");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("myntra", intent.Scope);
        Assert.Equal("oversized hoodies", intent.Target);

        string searchUrl = SiteSearchRegistry.BuildSearchUrl(intent.Scope!, intent.Target!);
        Assert.Contains("myntra.com", searchUrl);
        Assert.Contains("oversized%20hoodies", searchUrl);
    }

    [Fact]
    public void Scenario05_SearchAmazonIndiaForMensSocksXL_RoutesToWebsiteSearch()
    {
        var intent = _parser.Parse("Search Amazon India for men's socks XL");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("amazon india", intent.Scope);
        Assert.Equal("men's socks XL", intent.Target);

        string searchUrl = SiteSearchRegistry.BuildSearchUrl(intent.Scope!, intent.Target!);
        Assert.Contains("amazon.in/s?k=", searchUrl);
    }

    [Fact]
    public void Scenario06_SearchYouTubeForKotlinTutorials_RoutesToWebsiteSearch()
    {
        var intent = _parser.Parse("Search YouTube for Kotlin tutorials");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("youtube", intent.Scope);
        Assert.Equal("Kotlin tutorials", intent.Target);

        string searchUrl = SiteSearchRegistry.BuildSearchUrl(intent.Scope!, intent.Target!);
        Assert.Contains("youtube.com/results?search_query=", searchUrl);
    }

    [Fact]
    public void Scenario07_SearchGitHubForJetpackComposeExamples_RoutesToWebsiteSearch()
    {
        var intent = _parser.Parse("Search GitHub for Jetpack Compose examples");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("github", intent.Scope);
        Assert.Equal("Jetpack Compose examples", intent.Target);

        string searchUrl = SiteSearchRegistry.BuildSearchUrl(intent.Scope!, intent.Target!);
        Assert.Contains("github.com/search?q=", searchUrl);
    }

    [Fact]
    public void Scenario08_GoogleSearchForAndroidStudioErrors_RoutesToWebSearch()
    {
        var intent = _parser.Parse("Google search for Android Studio errors");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("Android Studio errors", intent.Target);
    }

    [Fact]
    public void Scenario09_SearchForAndroidStudioErrors_RoutesToWebSearch()
    {
        var intent = _parser.Parse("Search for Android Studio errors");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("Android Studio errors", intent.Target);
    }

    [Fact]
    public void Scenario10_OpenAndroidStudio_RoutesToAppLaunch()
    {
        var intent = _parser.Parse("Open Android Studio");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("Android Studio", intent.Target);
    }

    [Fact]
    public void Scenario11_OpenMyMails_RoutesToAppLaunch_Gmail()
    {
        var intent = _parser.Parse("Open my mails");
        Assert.Equal(CommandType.LaunchApp, intent.Type);
        Assert.Equal("mails", intent.Target);

        var target = _appRegistry.ResolveApp(intent.Target!);
        Assert.NotNull(target);
        Assert.Equal("Gmail", target.DisplayName);

        var phonetic = _parser.Parse("Open my males");
        Assert.Equal(CommandType.LaunchApp, phonetic.Type);
        Assert.Equal("males", phonetic.Target);
        Assert.Equal("Gmail", _appRegistry.ResolveApp(phonetic.Target!)?.DisplayName);
    }

    [Fact]
    public void Scenario12_CheckMyLaptopRAM_RoutesToSystemMetrics()
    {
        var intent = _parser.Parse("Check my laptop RAM");
        Assert.Equal(CommandType.SystemMetrics, intent.Type);
        Assert.Equal(SystemMetricsSubtype.Ram, intent.MetricsSubtype);
    }

    [Fact]
    public void Scenario13_ExplainBinarySearch_RoutesToConversationalAI()
    {
        var intent = _parser.Parse("Explain binary search");
        Assert.Equal(CommandType.Unknown, intent.Type);
    }

    [Fact]
    public void Scenario14_SearchMyntraAndThenOpenChrome_RoutesToClarification()
    {
        var intent = _parser.Parse("Search Myntra and then open Chrome");
        Assert.Equal(CommandType.Clarification, intent.Type);
        Assert.NotNull(intent.ClarificationPrompt);
    }

    // ==========================================
    // Guardrails, Edge Cases & Clarification
    // ==========================================

    [Fact]
    public void Guardrail_SearchWebForDesktopWallpapers_DoesNotRouteToLocalSearch()
    {
        var intent = _parser.Parse("Search the web for desktop wallpapers");
        Assert.Equal(CommandType.WebSearch, intent.Type);
        Assert.Equal("desktop wallpapers", intent.Target);
    }

    [Fact]
    public void Guardrail_FindDesktopWallpaperFilesOnMyLaptop_RoutesToLocalFileSearch()
    {
        var intent = _parser.Parse("Find desktop wallpaper files on my laptop");
        Assert.Equal(CommandType.FileSearch, intent.Type);
        Assert.Equal("desktop wallpaper", intent.Target);
    }

    [Fact]
    public void UnknownSite_SearchAjioForSneakers_UsesGoogleSiteSearchFallback()
    {
        var intent = _parser.Parse("Search Ajio for sneakers");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("ajio", intent.Scope);
        Assert.Equal("sneakers", intent.Target);

        string url = SiteSearchRegistry.BuildSearchUrl(intent.Scope!, intent.Target!);
        Assert.Contains("site%3Aajio.com", url);
        Assert.Contains("sneakers", url);
    }

    [Fact]
    public void UnknownSite_SearchStackOverflowForNullPointerExceptions_UsesGoogleSiteSearchFallback()
    {
        var intent = _parser.Parse("Search Stack Overflow for null pointer exceptions");
        Assert.Equal(CommandType.WebsiteSearch, intent.Type);
        Assert.Equal("stack overflow", intent.Scope);
        Assert.Equal("null pointer exceptions", intent.Target);

        string url = SiteSearchRegistry.BuildSearchUrl(intent.Scope!, intent.Target!);
        Assert.Contains("site%3Astackoverflow.com", url);
        Assert.Contains("null%20pointer%20exceptions", url);
    }

    [Fact]
    public void Clarification_AmbiguousStudio_PromptsForAndroidStudio()
    {
        var intent = _parser.Parse("Studio");
        Assert.Equal(CommandType.Clarification, intent.Type);
        Assert.Equal("Android Studio", intent.Target);
        Assert.NotNull(intent.PendingAction);
        Assert.Equal("Did you mean Android Studio?", intent.ClarificationPrompt);
    }

    [Fact]
    public void Clarification_PhoneticOperateStaff_PromptsForAndroidStudio()
    {
        var intent = _parser.Parse("Operate staff");
        Assert.Equal(CommandType.Clarification, intent.Type);
        Assert.Equal("Android Studio", intent.Target);
        Assert.NotNull(intent.PendingAction);
        Assert.Equal("Did you mean Android Studio?", intent.ClarificationPrompt);
    }

    [Fact]
    public void Clarification_OpenTheMessengerApp_DynamicallySuggestsWhatsApp()
    {
        var intent = _parser.Parse("Open the messenger app");
        Assert.Equal(CommandType.Clarification, intent.Type);
        Assert.Equal("WhatsApp", intent.Target);
        Assert.NotNull(intent.PendingAction);
        Assert.Equal("Did you mean WhatsApp?", intent.ClarificationPrompt);
    }

    [Fact]
    public void Clarification_OpenChatApp_DynamicallySuggestsWhatsApp()
    {
        var intent = _parser.Parse("Open chat app");
        Assert.Equal(CommandType.Clarification, intent.Type);
        Assert.Equal("WhatsApp", intent.Target);
        Assert.NotNull(intent.PendingAction);
        Assert.Equal("Did you mean WhatsApp?", intent.ClarificationPrompt);
    }

    [Fact]
    public void FuzzyMatching_SuggestCandidate_CorrectsSpellingAndRoles()
    {
        var candidateMessenger = _appRegistry.SuggestCandidate("messenger app");
        Assert.NotNull(candidateMessenger);
        Assert.Equal("WhatsApp", candidateMessenger.DisplayName);

        var candidateFuzzy = _appRegistry.SuggestCandidate("whatsap");
        Assert.NotNull(candidateFuzzy);
        Assert.Equal("WhatsApp", candidateFuzzy.DisplayName);

        var candidateChrome = _appRegistry.SuggestCandidate("crome");
        Assert.NotNull(candidateChrome);
        Assert.Equal("Google Chrome", candidateChrome.DisplayName);
    }

    [Fact]
    public void UnrecognizedUtterance_OpenSameData_DoesNotHaveHighConfidence()
    {
        var intent = _parser.Parse("Open same data");
        // Completely unrecognized app without candidates should not pass deterministic execution threshold (>= 0.60)
        Assert.True(intent.Confidence < 0.60f);
    }

    [Fact]
    public void SystemControl_NaturalVolumePhrases_RouteToSystemControl()
    {
        var intent1 = _parser.Parse("Set the volume to 50 percent");
        Assert.Equal(CommandType.SystemControl, intent1.Type);
        Assert.Equal(SystemControlAction.SetVolume, intent1.ControlAction);
        Assert.Equal(50, intent1.Value);

        var intent2 = _parser.Parse("Turn up the volume");
        Assert.Equal(CommandType.SystemControl, intent2.Type);
        Assert.Equal(SystemControlAction.IncreaseVolume, intent2.ControlAction);

        var intent3 = _parser.Parse("Turn down the volume");
        Assert.Equal(CommandType.SystemControl, intent3.Type);
        Assert.Equal(SystemControlAction.DecreaseVolume, intent3.ControlAction);

        var intent4 = _parser.Parse("Mute the volume");
        Assert.Equal(CommandType.SystemControl, intent4.Type);
        Assert.Equal(SystemControlAction.Mute, intent4.ControlAction);
    }

    [Fact]
    public void SystemMetrics_LaptopHealthAndRamQueries_RouteToSystemMetrics()
    {
        var intentRam = _parser.Parse("Check my lappy ram");
        Assert.Equal(CommandType.SystemMetrics, intentRam.Type);
        Assert.Equal(SystemMetricsSubtype.Ram, intentRam.MetricsSubtype);

        var intentBatt = _parser.Parse("Check battery");
        Assert.Equal(CommandType.SystemMetrics, intentBatt.Type);
        Assert.Equal(SystemMetricsSubtype.Battery, intentBatt.MetricsSubtype);

        var intentCpu = _parser.Parse("Check cpu usage");
        Assert.Equal(CommandType.SystemMetrics, intentCpu.Type);
        Assert.Equal(SystemMetricsSubtype.Cpu, intentCpu.MetricsSubtype);

        var intentHealth = _parser.Parse("Check my laptop health");
        Assert.Equal(CommandType.SystemMetrics, intentHealth.Type);
        Assert.Equal(SystemMetricsSubtype.All, intentHealth.MetricsSubtype);
    }

    [Fact]
    public void SiteSearch_OverGreedyPrepositionalPhrase_DoesNotRouteToTheMiddleDotCom()
    {
        var intent = _parser.Parse("Search in the middle for a girl");
        // Should NOT route to WebsiteSearch with scope="the middle"
        Assert.NotEqual(CommandType.WebsiteSearch, intent.Type);
    }
}

