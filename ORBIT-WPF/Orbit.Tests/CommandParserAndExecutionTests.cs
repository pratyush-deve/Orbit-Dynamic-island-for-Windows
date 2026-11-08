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
}

