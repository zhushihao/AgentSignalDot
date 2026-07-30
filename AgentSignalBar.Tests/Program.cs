using System.Text.Json.Nodes;
using AgentSignalBar.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("default state path uses LOCALAPPDATA AgentSignalBar status.json", StatePathTests.DefaultStatePathUsesLocalAppData),
            ("signal normalization accepts separators and aliases", SignalTests.SignalNormalizationAcceptsVariants),
            ("aggregation keeps blocked above permission and active", StateDocumentTests.AggregationKeepsBlockedAbovePermissionAndActive),
            ("Claude hooks map failures and max tokens", HookAdapterTests.ClaudeHooksMapFailuresAndMaxTokens),
            ("CodeBuddy hooks map events and explicit signals", HookAdapterTests.CodeBuddyHooksMapEventsAndExplicitSignals),
            ("Codex hook reads camelCase event and failure marker", HookAdapterTests.CodexHookReadsCamelCaseEventAndFailureMarker),
            ("Codex session log parser maps VS Code events", CodexSessionLogTests.CodexSessionLogParserMapsVsCodeEvents),
            ("Codex compacted log line does not start thinking", CodexSessionLogTests.CodexCompactedLogLineDoesNotStartThinking),
            ("Codex assistant message expires back to idle", CodexSessionLogTests.CodexAssistantMessageExpiresBackToIdle),
            ("Codex session log monitor reads appended lines after priming", CodexSessionLogTests.CodexSessionLogMonitorReadsAppendedLinesAfterPriming),
            ("WorkBuddy session monitor detects active session from file timestamp", WorkBuddySessionMonitorTests.WorkBuddySessionMonitorDetectsActiveSessionFromFileTimestamp),
            ("WorkBuddy session monitor falls back to file name when session id missing", WorkBuddySessionMonitorTests.WorkBuddySessionMonitorFallsBackToFileNameWhenSessionIdMissing),
            ("WorkBuddy session monitor ignores missing directory", WorkBuddySessionMonitorTests.WorkBuddySessionMonitorIgnoresMissingDirectory),
            // ── WorkBuddy session log monitor: 25 comprehensive tests ──
            ("WB log: Prime detects busy session", WorkBuddySessionLogMonitorTests.PrimeDetectsBusySession),
            ("WB log: Prime skips idle session", WorkBuddySessionLogMonitorTests.PrimeSkipsIdleSession),
            ("WB log: Prime last transition wins", WorkBuddySessionLogMonitorTests.PrimeLastTransitionWins),
            ("WB log: Prime detects blocked session", WorkBuddySessionLogMonitorTests.PrimeDetectsBlockedSession),
            ("WB log: Prime skips auto-approved permission", WorkBuddySessionLogMonitorTests.PrimeSkipsAutoApprovedPermission),
            ("WB log: ReadNewLines picks up appended data", WorkBuddySessionLogMonitorTests.ReadNewLinesPicksUpAppendedData),
            ("WB log: ToolEnded resolves blocked state", WorkBuddySessionLogMonitorTests.ToolEndedResolvesBlockedState),
            ("WB log: ToolStarted resolves blocked state", WorkBuddySessionLogMonitorTests.ToolStartedResolvesBlockedState),
            ("WB log: PermissionAutoApproved resolves blocked", WorkBuddySessionLogMonitorTests.PermissionAutoApprovedResolvesBlockedState),
            ("WB log: Busy transition resolves blocked state", WorkBuddySessionLogMonitorTests.BusyTransitionResolvesBlockedState),
            ("WB log: Busy transition within Prime resolves blocked", WorkBuddySessionLogMonitorTests.BusyTransitionWithinPrimeResolvesBlockedState),
            ("WB log: Busy session persists across short gap", WorkBuddySessionLogMonitorTests.BusySessionPersistsAcrossShortGap),
            ("WB log: Tool executing session not stale", WorkBuddySessionLogMonitorTests.ToolExecutingSessionNotStale),
            ("WB log: Blocked session not stale", WorkBuddySessionLogMonitorTests.BlockedSessionNotStale),
            ("WB log: Mixed busy and blocked initial state", WorkBuddySessionLogMonitorTests.MixedBusyAndBlockedInitialState),
            ("WB log: Cross-file sessions", WorkBuddySessionLogMonitorTests.CrossFileSessions),
            ("WB log: Session lifecycle across polls", WorkBuddySessionLogMonitorTests.SessionLifecycleAcrossPolls),
            ("WB log: Blocked lifecycle across polls", WorkBuddySessionLogMonitorTests.BlockedLifecycleAcrossPolls),
            ("WB log: Empty log directory no errors", WorkBuddySessionLogMonitorTests.EmptyLogDirectoryNoErrors),
            ("WB log: Blocked resolved within same Prime read", WorkBuddySessionLogMonitorTests.PrimeBlockedThenResolvedWithinSameRead),
            ("WB log: Pending permission resolved per file", WorkBuddySessionLogMonitorTests.PendingPermissionResolvedPerFile),
            ("WB log: Second poll no growth returns empty", WorkBuddySessionLogMonitorTests.SecondPollNoGrowthReturnsEmpty),
            ("WB log: WAITING_FOR_PERMISSION without ASK blocks", WorkBuddySessionLogMonitorTests.PermissionWaitingWithoutAskDefersBlock),
            ("WB log: Permission ask tracks last tool session", WorkBuddySessionLogMonitorTests.PermissionAskTracksLastToolSession),
            ("WB log: Ignores non-WorkBuddy lines", WorkBuddySessionLogMonitorTests.IgnoresNonWorkBuddyLines),
            ("WB log: Auto-approve clears without state transition", WorkBuddySessionLogMonitorTests.AutoApprovedClearsPendingWithoutStateTransition),
            ("WB log: Concurrent sessions in same file", WorkBuddySessionLogMonitorTests.ConcurrentSessionsInSameFile),
            ("WB log: Busy session persists across long gap", WorkBuddySessionLogMonitorTests.BusySessionPersistsAcrossLongGap),
            ("WB log: Busy session cleared only after hard-dead timeout", WorkBuddySessionLogMonitorTests.BusySessionClearedOnlyAfterHardDeadTimeout),
            ("WB log: Busy session hard-dead at prime", WorkBuddySessionLogMonitorTests.BusySessionHardDeadAtPrime),
            ("WB log: Explicit busy=false clears immediately", WorkBuddySessionLogMonitorTests.ExplicitBusyFalseClearsImmediately),
            ("WB log: Orphaned busy session idled when app alive elsewhere", WorkBuddySessionLogMonitorTests.OrphanedBusySessionIdledWhenAppAliveElsewhere),
            ("WB log: Solo busy session idled after silence timeout", WorkBuddySessionLogMonitorTests.SoloBusySessionIdledAfterSilenceTimeout),
            ("WB log: Dead blocked session not revived by Prime liveness", WorkBuddySessionLogMonitorTests.DeadBlockedSessionNotRevivedByPrimeLiveness),
            ("WB log: Live blocked session preserved by liveness", WorkBuddySessionLogMonitorTests.LiveBlockedSessionPreservedByLiveness),
            ("WB log: Liveness disabled by default revives blocked", WorkBuddySessionLogMonitorTests.LivenessDisabledByDefaultRevivesBlocked),
            ("WB log: CANCEL_REQUESTED clears block", WorkBuddySessionLogMonitorTests.CancelRequestedClearsBlock),
            ("WB log: Dead pid blocked session cleared regardless of log recency", WorkBuddySessionLogMonitorTests.DeadPidBlockedSessionClearedRegardlessOfLogRecency),
            ("CLI status JSON keeps macOS status schema fields", CliIntegrationTests.CliStatusJsonKeepsMacStatusSchemaFields),
            ("floating window default placement uses right middle of work area", FloatingWindowPlacementTests.DefaultPlacementUsesRightMiddleOfWorkArea),
            ("floating window restored placement is clamped into work area", FloatingWindowPlacementTests.RestoredPlacementIsClampedIntoWorkArea),
            ("manual signal tests cover every display state", ManualSignalTestCaseTests.ManualSignalTestsCoverEveryDisplayState),
            ("agent identity badge prefers highest priority active session", AgentIdentityBadgeTests.AgentIdentityBadgePrefersHighestPriorityActiveSession),
            ("agent identity badge recognizes Claude Code and Codex agents", AgentIdentityBadgeTests.AgentIdentityBadgeRecognizesClaudeCodeAndCodexAgents),
            ("floating signal text describes agent and current state", FloatingSignalTextTests.FloatingSignalTextDescribesAgentAndCurrentState),
            ("state store writes sessions atomically and reads status", StateStoreTests.StateStoreWritesSessionAndReadsSnapshot),
            ("completed sessions expire back to idle", StateStoreTests.CompletedSessionsExpireBackToIdle),
            ("thinking sessions expire back to idle", StateStoreTests.ThinkingSessionsExpireBackToIdle),
            ("Claude global stop clears ordinary active sessions", StateStoreTests.ClaudeGlobalStopClearsOrdinaryActiveSessions),
            ("Codex done clears same conversation across hook and log agents", StateStoreTests.CodexDoneClearsSameConversationAcrossHookAndLogAgents),
            ("Codex hook stop clears same conversation log agent", StateStoreTests.CodexHookStopClearsSameConversationLogAgent),
            ("Codex done preserves same conversation permission state", StateStoreTests.CodexDonePreservesSameConversationPermissionState),
            ("session start does not create active session rows", StateStoreTests.SessionStartDoesNotCreateActiveSessionRows),
            ("ready session rows are removed when reading snapshot", StateStoreTests.ReadySessionRowsAreRemovedWhenReadingSnapshot),
            ("corrupt status file reads as stale", StateStoreTests.CorruptStatusFileReadsAsStale),
            ("hook installer dry run builds Claude settings without writing", HookInstallerTests.DryRunBuildsClaudeSettingsWithoutWriting),
            ("hook installer emits PowerShell-safe Windows commands", HookInstallerTests.HookInstallerEmitsPowerShellSafeWindowsCommands),
            ("hook installer replaces duplicate Agent Signal Dot hook commands", HookInstallerTests.HookInstallerReplacesDuplicateAgentSignalHookCommands),
            ("hook installer keeps current Agent Signal Dot hook configuration idempotent", HookInstallerTests.HookInstallerKeepsCurrentAgentSignalHookConfigurationIdempotent),
            ("connection health check reports installed and duplicate hooks", ConnectionHealthCheckTests.ConnectionHealthCheckReportsInstalledAndDuplicateHooks),
            ("signal self test writes and verifies every manual status", SignalSelfTestRunnerTests.SignalSelfTestWritesAndVerifiesEveryManualStatus),
            ("CLI writes status and handles Claude hook stdin", CliIntegrationTests.CliWritesStatusAndHandlesClaudeHookStdin),
            ("CLI Claude stop hook tolerates trailing stdin text", CliIntegrationTests.CliClaudeStopHookToleratesTrailingStdinText),
            ("CLI install-hooks dry run previews Claude and Codex user configs", CliIntegrationTests.CliInstallHooksDryRunPreviewsClaudeAndCodexUserConfigs),
            ("CLI install-desktop-switch dry run previews shortcut", CliIntegrationTests.CliInstallDesktopSwitchDryRunPreviewsShortcut),
            ("CLI desktop-switch dry run plans start when app is stopped", CliIntegrationTests.CliDesktopSwitchDryRunPlansStartWhenAppIsStopped)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}");
                Console.Error.WriteLine(error.Message);
            }
        }

        if (failed > 0)
        {
            Console.Error.WriteLine($"{failed} test(s) failed.");
            return 1;
        }

        Console.WriteLine($"{tests.Length} test(s) passed.");
        return 0;
    }
}

static class CliIntegrationTests
{
    public static void CliWritesStatusAndHandlesClaudeHookStdin()
    {
        using var fixture = TempFixture.Create();
        var stateFile = Path.Combine(fixture.DirectoryPath, "status.json");
        var cliProject = FindCliProjectPath();

        var first = RunDotnet(
            ["run", "--project", cliProject, "--", "working", "--session", "job-1", "--agent", "script", "--event", "Started", "--json"],
            stateFile,
            stdin: null);

        Assert.Equal(0, first.ExitCode, first.Error);
        Assert.Equal(true, first.Output.Contains("\"aggregate\": \"working\"", StringComparison.Ordinal), first.Output);

        var payload = """
        {"hook_event_name":"PermissionRequest","session_id":"claude-main","tool_name":"Bash"}
        """;
        var hook = RunDotnet(
            ["run", "--project", cliProject, "--", "claude-hook"],
            stateFile,
            payload);

        Assert.Equal(0, hook.ExitCode, hook.Error);

        var status = RunDotnet(
            ["run", "--project", cliProject, "--", "status", "--json"],
            stateFile,
            stdin: null);

        Assert.Equal(0, status.ExitCode, status.Error);
        Assert.Equal(true, status.Output.Contains("\"aggregate\": \"permission\"", StringComparison.Ordinal), status.Output);
        Assert.Equal(true, status.Output.Contains("\"agent\": \"claude-code\"", StringComparison.Ordinal), status.Output);
    }

    public static void CliStatusJsonKeepsMacStatusSchemaFields()
    {
        using var fixture = TempFixture.Create();
        var stateFile = Path.Combine(fixture.DirectoryPath, "status.json");
        var cliProject = FindCliProjectPath();

        var result = RunDotnet(
            ["run", "--project", cliProject, "--", "done", "--json"],
            stateFile,
            stdin: null);

        Assert.Equal(0, result.ExitCode, result.Error);
        Assert.Equal(true, result.Output.Contains("\"display_name\":", StringComparison.Ordinal), result.Output);
        Assert.Equal(true, result.Output.Contains("\"summary\":", StringComparison.Ordinal), result.Output);
        Assert.Equal(true, result.Output.Contains("\"action\":", StringComparison.Ordinal), result.Output);
    }

    public static void CliClaudeStopHookToleratesTrailingStdinText()
    {
        using var fixture = TempFixture.Create();
        var stateFile = Path.Combine(fixture.DirectoryPath, "status.json");
        var cliProject = FindCliProjectPath();

        var started = RunDotnet(
            ["run", "--project", cliProject, "--", "claude-hook"],
            stateFile,
            """
            {"hook_event_name":"UserPromptSubmit","session_id":"claude-main"}
            """);
        Assert.Equal(0, started.ExitCode, started.Error);

        var stopped = RunDotnet(
            ["run", "--project", cliProject, "--", "claude-hook"],
            stateFile,
            """
            {"hook_event_name":"Stop","session_id":"claude-main"}bash hook output
            """);

        Assert.Equal(0, stopped.ExitCode, stopped.Error);

        var status = RunDotnet(
            ["run", "--project", cliProject, "--", "status", "--json"],
            stateFile,
            stdin: null);

        Assert.Equal(0, status.ExitCode, status.Error);
        Assert.Equal(true, status.Output.Contains("\"aggregate\": \"done\"", StringComparison.Ordinal), status.Output);
    }

    public static void CliInstallHooksDryRunPreviewsClaudeAndCodexUserConfigs()
    {
        using var fixture = TempFixture.Create();
        var home = Path.Combine(fixture.DirectoryPath, "home");
        var cliProject = FindCliProjectPath();
        var fakeCli = Path.Combine(fixture.DirectoryPath, "agent-signal.exe");

        var result = RunDotnet(
            ["run", "--project", cliProject, "--", "install-hooks", "--dry-run", "--json", "--home", home, "--cli", fakeCli],
            Path.Combine(fixture.DirectoryPath, "status.json"),
            stdin: null);

        Assert.Equal(0, result.ExitCode, result.Error);
        Assert.Equal(true, result.Output.Contains("\"target_name\": \"Claude Code\"", StringComparison.Ordinal), result.Output);
        Assert.Equal(true, result.Output.Contains("\"target_name\": \"Codex\"", StringComparison.Ordinal), result.Output);
        Assert.Equal(true, result.Output.Contains(EscapeJson(Path.Combine(home, ".claude", "settings.json")), StringComparison.Ordinal), result.Output);
        Assert.Equal(true, result.Output.Contains(EscapeJson(Path.Combine(home, ".codex", "hooks.json")), StringComparison.Ordinal), result.Output);
        Assert.Equal(false, File.Exists(Path.Combine(home, ".claude", "settings.json")));
        Assert.Equal(false, File.Exists(Path.Combine(home, ".codex", "hooks.json")));
    }

    public static void CliInstallDesktopSwitchDryRunPreviewsShortcut()
    {
        using var fixture = TempFixture.Create();
        var desktop = Path.Combine(fixture.DirectoryPath, "Desktop");
        var cliProject = FindCliProjectPath();
        var fakeCli = Path.Combine(fixture.DirectoryPath, "agent-signal.exe");
        var fakeWindowsExe = Path.Combine(fixture.DirectoryPath, "AgentSignalBar.Windows.exe");

        var result = RunDotnet(
            ["run", "--project", cliProject, "--", "install-desktop-switch", "--dry-run", "--json", "--desktop", desktop, "--cli", fakeCli, "--windows-exe", fakeWindowsExe],
            Path.Combine(fixture.DirectoryPath, "status.json"),
            stdin: null);

        var shortcutPath = Path.Combine(desktop, "红绿灯开关.lnk");
        Assert.Equal(0, result.ExitCode, result.Error);
        var json = JsonNode.Parse(result.Output)!;
        Assert.Equal(shortcutPath, json["shortcut_path"]!.GetValue<string>());
        Assert.Equal(fakeCli, json["target_path"]!.GetValue<string>());
        Assert.Equal(fakeWindowsExe, json["windows_exe_path"]!.GetValue<string>());
        Assert.Equal(true, json["arguments"]!.GetValue<string>().Contains("desktop-switch", StringComparison.Ordinal), result.Output);
        Assert.Equal(false, File.Exists(shortcutPath));
    }

    public static void CliDesktopSwitchDryRunPlansStartWhenAppIsStopped()
    {
        using var fixture = TempFixture.Create();
        var cliProject = FindCliProjectPath();
        var fakeWindowsExe = Path.Combine(fixture.DirectoryPath, "AgentSignalBar.Windows.exe");
        File.WriteAllText(fakeWindowsExe, "");

        var result = RunDotnet(
            ["run", "--project", cliProject, "--", "desktop-switch", "--dry-run", "--json", "--windows-exe", fakeWindowsExe],
            Path.Combine(fixture.DirectoryPath, "status.json"),
            stdin: null);

        Assert.Equal(0, result.ExitCode, result.Error);
        // The toggle plans "stop" when AgentSignalBar.Windows is already running
        // on the host and "start" otherwise. Assert the action is consistent with
        // the detected running state so the test stays green regardless of the
        // ambient environment (e.g. the real tray app being open on a dev machine).
        var json = JsonNode.Parse(result.Output)!;
        var action = json["action"]!.GetValue<string>();
        var matched = json["matched_processes"]!.GetValue<int>();
        Assert.Equal(matched > 0 ? "stop" : "start", action);
        Assert.Equal(true, result.Output.Contains(EscapeJson(fakeWindowsExe), StringComparison.Ordinal), result.Output);
    }

    private static (int ExitCode, string Output, string Error) RunDotnet(string[] arguments, string stateFile, string? stdin)
    {
        var root = FindRepositoryRoot();
        var dotnet = ResolveDotnet(root);
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = root,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment["DOTNET_CLI_HOME"] = Path.Combine(root, ".tmp");
        process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["AGENT_SIGNAL_LIGHT_STATE_FILE"] = stateFile;

        process.Start();
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
        }
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    private static string ResolveDotnet(string root)
    {
        var local = Path.Combine(root, ".dotnet", "dotnet.exe");
        if (File.Exists(local))
        {
            return local;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var system = Path.Combine(programFiles, "dotnet", "dotnet.exe");
        if (File.Exists(system))
        {
            return system;
        }

        return "dotnet";
    }

    private static string EscapeJson(string value) =>
        System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.Default).ToString();

    private static string FindCliProjectPath()
    {
        return Path.Combine(FindRepositoryRoot(), "AgentSignalBar.Cli", "AgentSignalBar.Cli.csproj");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && (Directory.Exists(Path.Combine(directory.FullName, "AgentSignalBar.Cli"))
                    || File.Exists(Path.Combine(directory.FullName, "AgentSignalDot.sln"))))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Cannot locate repository root.");
    }
}

static class ManualSignalTestCaseTests
{
    public static void ManualSignalTestsCoverEveryDisplayState()
    {
        var coveredStates = ManualSignalTestCase.All
            .Select(testCase => testCase.ExpectedDisplayState)
            .OrderBy(state => state.ToString())
            .ToArray();
        var allStates = Enum.GetValues<DisplayState>()
            .OrderBy(state => state.ToString())
            .ToArray();

        Assert.Equal(allStates.Length, coveredStates.Length);
        for (var index = 0; index < allStates.Length; index++)
        {
            Assert.Equal(allStates[index], coveredStates[index]);
        }

        Assert.Equal(true, ManualSignalTestCase.All.All(testCase =>
            testCase.Signal.DisplayState() == testCase.ExpectedDisplayState));
    }
}

static class AgentIdentityBadgeTests
{
    public static void AgentIdentityBadgePrefersHighestPriorityActiveSession()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SignalSnapshot(
            AgentSignal.Permission,
            [
                new SessionStatus("codex-low", AgentSignal.Working, now.AddSeconds(10), "codex-cli", "PreToolUse"),
                new SessionStatus("claude-high", AgentSignal.PermissionRequest, now, "claude-code", "PermissionRequest")
            ],
            [],
            "status.json",
            now);

        Assert.Equal(AgentIdentity.ClaudeCode, AgentIdentityBadge.Resolve(snapshot));
    }

    public static void AgentIdentityBadgeRecognizesClaudeCodeAndCodexAgents()
    {
        Assert.Equal(AgentIdentity.Codex, AgentIdentityBadge.ResolveAgentName("codex-cli"));
        Assert.Equal(AgentIdentity.Codex, AgentIdentityBadge.ResolveAgentName("codex-vscode"));
        Assert.Equal(AgentIdentity.ClaudeCode, AgentIdentityBadge.ResolveAgentName("claude-code"));
        Assert.Equal(AgentIdentity.ClaudeCode, AgentIdentityBadge.ResolveAgentName("Claude Code"));
        Assert.Equal(AgentIdentity.Default, AgentIdentityBadge.ResolveAgentName("local-script"));
    }
}

static class FloatingWindowPlacementTests
{
    public static void DefaultPlacementUsesRightMiddleOfWorkArea()
    {
        var workArea = new SignalRectangle(0, 0, 1920, 1080);
        var placement = FloatingSignalWindowPlacement.DefaultPlacement(workArea);

        Assert.Equal(FloatingSignalWindowPlacement.DefaultWidth, placement.Width);
        Assert.Equal(FloatingSignalWindowPlacement.DefaultHeight, placement.Height);
        Assert.Equal(1870, placement.X);
        Assert.Equal(0, placement.Y);
    }

    public static void RestoredPlacementIsClampedIntoWorkArea()
    {
        var workArea = new SignalRectangle(100, 100, 800, 600);
        var placement = FloatingSignalWindowPlacement.Clamp(
            new SignalRectangle(20, 40, 276, 280),
            workArea);

        Assert.Equal(100, placement.X);
        Assert.Equal(100, placement.Y);
        Assert.Equal(276, placement.Width);
        Assert.Equal(280, placement.Height);
    }
}

static class FloatingSignalTextTests
{
    public static void FloatingSignalTextDescribesAgentAndCurrentState()
    {
        var now = DateTimeOffset.UtcNow;
        var codex = new SignalSnapshot(
            AgentSignal.Working,
            [
                new SessionStatus("codex-main", AgentSignal.Working, now, "codex-cli", "PreToolUse")
            ],
            [],
            "status.json",
            now);

        var codexText = FloatingSignalText.Resolve(codex);

        Assert.Equal("Codex", codexText.AgentName);
        Assert.Equal("工作中", codexText.StatusText);
        Assert.Equal("处理中", codexText.DetailText);

        var claude = new SignalSnapshot(
            AgentSignal.PermissionRequest,
            [
                new SessionStatus("claude-main", AgentSignal.PermissionRequest, now, "claude-code", "PermissionRequest")
            ],
            [],
            "status.json",
            now);

        var claudeText = FloatingSignalText.Resolve(claude);

        Assert.Equal("Claude", claudeText.AgentName);
        Assert.Equal("等授权", claudeText.StatusText);
        Assert.Equal("需确认", claudeText.DetailText);
    }
}

static class StatePathTests
{
    public static void DefaultStatePathUsesLocalAppData()
    {
        var env = new Dictionary<string, string?>
        {
            ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local"
        };

        var path = StatePath.DefaultStateFilePath(env);

        Assert.Equal(@"C:\Users\me\AppData\Local\AgentSignalBar\status.json", path);
    }
}

static class SignalTests
{
    public static void SignalNormalizationAcceptsVariants()
    {
        Assert.Equal(AgentSignal.Working, AgentSignalParser.Normalize(" pre-tool-use "));
        Assert.Equal(AgentSignal.PermissionRequest, AgentSignalParser.Normalize("permission request"));
        Assert.Equal(AgentSignal.MaxTokens, AgentSignalParser.Normalize("maxTokens"));
        Assert.Equal(AgentSignal.Failure, AgentSignalParser.Normalize("failed"));
    }
}

static class StateDocumentTests
{
    public static void AggregationKeepsBlockedAbovePermissionAndActive()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new SignalStateDocument
        {
            Sessions = new Dictionary<string, SessionRecord>
            {
                ["active"] = new("codex", AgentSignal.Working, "PreToolUse", now),
                ["permission"] = new("claude-code", AgentSignal.PermissionRequest, "PermissionRequest", now),
                ["blocked"] = new("script", AgentSignal.Blocked, "Failed", now)
            }
        };

        Assert.Equal(AgentSignal.Blocked, document.AggregateSignal());
    }
}

static class HookAdapterTests
{
    public static void ClaudeHooksMapFailuresAndMaxTokens()
    {
        Assert.Equal(AgentSignal.Working, ClaudeHookAdapter.ChooseSignal("PreToolUse", new Dictionary<string, object?>()));
        Assert.Equal(AgentSignal.Done, ClaudeHookAdapter.ChooseSignal("Stop", new Dictionary<string, object?>()));
        Assert.Equal(AgentSignal.Blocked, ClaudeHookAdapter.ChooseSignal("post_tool_use_failure", new Dictionary<string, object?>()));
        Assert.Equal(AgentSignal.MaxTokens, ClaudeHookAdapter.ChooseSignal("Stop", new Dictionary<string, object?> { ["stopReason"] = " max-tokens " }));
        Assert.Equal(AgentSignal.Error, ClaudeHookAdapter.ChooseSignal("Stop", new Dictionary<string, object?> { ["stop_reason"] = "tool error" }));
    }

    public static void CodexHookReadsCamelCaseEventAndFailureMarker()
    {
        Assert.Equal(AgentSignal.ToolDone, CodexHookAdapter.ChooseSignal(null, new Dictionary<string, object?> { ["hookEventName"] = "post tool use" }));
        Assert.Equal(AgentSignal.Blocked, CodexHookAdapter.ChooseSignal("PostToolUse", new Dictionary<string, object?> { ["exitStatus"] = 1 }));
    }

    public static void CodeBuddyHooksMapEventsAndExplicitSignals()
    {
        Assert.Equal(AgentSignal.Working, CodeBuddyHookAdapter.ChooseSignal("PreToolUse", new Dictionary<string, object?> { }));
        Assert.Equal(AgentSignal.Thinking, CodeBuddyHookAdapter.ChooseSignal("UserPromptSubmit", new Dictionary<string, object?> { }));
        Assert.Equal(AgentSignal.Done, CodeBuddyHookAdapter.ChooseSignal("Stop", new Dictionary<string, object?> { }));
        Assert.Equal(AgentSignal.Blocked, CodeBuddyHookAdapter.ChooseSignal("PostToolUseFailure", new Dictionary<string, object?> { }));
        Assert.Equal(AgentSignal.PermissionRequest, CodeBuddyHookAdapter.ChooseSignal(null, new Dictionary<string, object?> { ["hook_event_name"] = "PermissionRequest" }));
        Assert.Equal(AgentSignal.Working, CodeBuddyHookAdapter.ChooseSignal(null, new Dictionary<string, object?> { ["signal"] = "working" }));
    }
}

static class CodexSessionLogTests
{
    public static void CodexSessionLogParserMapsVsCodeEvents()
    {
        var metaLine = """
        {"timestamp":"2026-06-05T06:29:05.121Z","type":"session_meta","payload":{"id":"019e9677-5998-7693-9405-8ad6284386b5","originator":"codex_vscode","source":"vscode"}}
        """;
        var toolLine = """
        {"timestamp":"2026-06-05T08:43:52.894Z","type":"response_item","payload":{"type":"function_call","name":"shell_command"}}
        """;
        var doneLine = """
        {"timestamp":"2026-06-05T08:44:12Z","type":"event_msg","payload":{"type":"task_complete","turn_id":"turn-1"}}
        """;

        Assert.Equal("codex-vscode", CodexSessionLogParser.AgentNameFromSessionMetaLine(metaLine));

        var tool = CodexSessionLogParser.ActivityFromLine(toolLine, "codex-vscode:test", "codex-vscode");
        Assert.Equal(AgentSignal.Working, tool?.Signal);
        Assert.Equal("codex-vscode", tool?.Agent);
        Assert.Equal("DesktopToolCall:shell_command", tool?.Event);

        var done = CodexSessionLogParser.ActivityFromLine(doneLine, "codex-vscode:test", "codex-vscode");
        Assert.Equal(AgentSignal.Done, done?.Signal);
        Assert.Equal("DesktopTaskComplete", done?.Event);
    }

    public static void CodexCompactedLogLineDoesNotStartThinking()
    {
        var compactedLine = """
        {"timestamp":"2026-06-05T10:55:52.661Z","type":"compacted","payload":{"message":"summary"}}
        """;

        var activity = CodexSessionLogParser.ActivityFromLine(compactedLine, "codex-vscode:test", "codex-vscode");

        Assert.Equal<CodexSessionLogActivity?>(null, activity);
    }

    public static void CodexAssistantMessageExpiresBackToIdle()
    {
        using var fixture = TempFixture.Create();
        var messageLine = """
        {"timestamp":"2026-06-05T09:25:04.549Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"status update"}],"phase":"commentary"}}
        """;

        var activity = CodexSessionLogParser.ActivityFromLine(messageLine, "codex-vscode:test", "codex-vscode");

        Assert.Equal(AgentSignal.ToolDone, activity?.Signal);
        Assert.Equal("DesktopMessage", activity?.Event);

        var store = new SignalStateStore(
            Path.Combine(fixture.DirectoryPath, "status.json"),
            sessionTtl: TimeSpan.FromMinutes(30),
            completedTtl: TimeSpan.FromMilliseconds(1));
        store.ApplySessionSignal(
            activity!.Signal,
            activity.SessionId,
            activity.Agent,
            activity.Event,
            DateTimeOffset.UtcNow.AddSeconds(-2));

        var snapshot = store.ReadSnapshot(DateTimeOffset.UtcNow);

        Assert.Equal(AgentSignal.Idle, snapshot.Aggregate);
        Assert.Equal(0, snapshot.Sessions.Count);
    }

    public static void CodexSessionLogMonitorReadsAppendedLinesAfterPriming()
    {
        using var fixture = TempFixture.Create();
        var root = Path.Combine(fixture.DirectoryPath, ".codex", "sessions");
        var day = Path.Combine(root, "2026", "06", "05");
        Directory.CreateDirectory(day);
        var log = Path.Combine(day, "rollout-2026-06-05T14-27-44-019e9677-5998-7693-9405-8ad6284386b5.jsonl");
        File.WriteAllText(log, """
        {"timestamp":"2026-06-05T06:29:05.121Z","type":"session_meta","payload":{"id":"019e9677-5998-7693-9405-8ad6284386b5","originator":"codex_vscode","source":"vscode"}}

        """);

        var monitor = new CodexSessionLogMonitor([root]);

        Assert.Equal(0, monitor.Poll(DateTimeOffset.Parse("2026-06-05T08:00:00Z")).Count);

        File.AppendAllText(log, """
        {"timestamp":"2026-06-05T08:43:52.894Z","type":"response_item","payload":{"type":"function_call","name":"shell_command"}}

        """);

        var activities = monitor.Poll(DateTimeOffset.Parse("2026-06-05T08:43:53Z"));

        Assert.Equal(1, activities.Count);
        Assert.Equal(AgentSignal.Working, activities[0].Signal);
        Assert.Equal("codex-vscode", activities[0].Agent);
        Assert.Equal("codex-vscode:019e9677-5998-7693-9405-8ad6284386b5", activities[0].SessionId);
    }
}

static class StateStoreTests
{
    public static void StateStoreWritesSessionAndReadsSnapshot()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        var snapshot = store.ApplySessionSignal(AgentSignal.Working, "claude-main", "claude-code", "PreToolUse");

        Assert.Equal(AgentSignal.Working, snapshot.Aggregate);
        Assert.Equal("claude-main", snapshot.Sessions.Single().SessionId);
        Assert.Equal(AgentSignal.Working, store.ReadSnapshot().Aggregate);
    }

    public static void CompletedSessionsExpireBackToIdle()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(
            Path.Combine(fixture.DirectoryPath, "status.json"),
            sessionTtl: TimeSpan.FromMinutes(30),
            completedTtl: TimeSpan.FromMilliseconds(1));

        store.ApplySessionSignal(AgentSignal.Done, "codex-main", "codex", "Stop", DateTimeOffset.UtcNow.AddSeconds(-2));

        var snapshot = store.ReadSnapshot(DateTimeOffset.UtcNow);

        Assert.Equal(AgentSignal.Idle, snapshot.Aggregate);
        Assert.Equal(0, snapshot.Sessions.Count);
    }

    public static void ThinkingSessionsExpireBackToIdle()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(
            Path.Combine(fixture.DirectoryPath, "status.json"),
            sessionTtl: TimeSpan.FromMinutes(30),
            completedTtl: TimeSpan.FromSeconds(30));

        store.ApplySessionSignal(AgentSignal.Thinking, "claude-main", "claude-code", "UserPromptSubmit", DateTimeOffset.UtcNow.AddMinutes(-6));

        var snapshot = store.ReadSnapshot(DateTimeOffset.UtcNow);

        Assert.Equal(AgentSignal.Idle, snapshot.Aggregate);
        Assert.Equal(0, snapshot.Sessions.Count);
    }

    public static void ClaudeGlobalStopClearsOrdinaryActiveSessions()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        store.ApplySessionSignal(AgentSignal.Working, "claude-main", "claude-code", "PreToolUse");
        store.ApplySessionSignal(AgentSignal.PermissionRequest, "claude-permission", "claude-code", "PermissionRequest");

        var snapshot = store.ApplySessionSignal(AgentSignal.Done, "claude-global", "claude-code", "Stop");

        Assert.Equal(AgentSignal.Permission, snapshot.Aggregate);
        Assert.Equal(false, snapshot.Sessions.Any(session => session.SessionId == "claude-main"));
        Assert.Equal(true, snapshot.Sessions.Any(session => session.SessionId == "claude-permission"));
    }

    public static void CodexDoneClearsSameConversationAcrossHookAndLogAgents()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        store.ApplySessionSignal(AgentSignal.Thinking, "codex-cli:session-1", "codex-cli", "UserPromptSubmit");
        store.ApplySessionSignal(AgentSignal.Working, "codex-vscode:session-1", "codex-vscode", "DesktopToolCall:shell_command");

        var snapshot = store.ApplySessionSignal(AgentSignal.Done, "codex-vscode:session-1", "codex-vscode", "DesktopTaskComplete");

        Assert.Equal(AgentSignal.Done, snapshot.Aggregate);
        Assert.Equal(false, snapshot.Sessions.Any(session => session.SessionId == "codex-cli:session-1"));
        Assert.Equal(true, snapshot.Sessions.Any(session =>
            session.SessionId == "codex-vscode:session-1"
            && session.Signal == AgentSignal.Done));
    }

    public static void CodexHookStopClearsSameConversationLogAgent()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        store.ApplySessionSignal(AgentSignal.Working, "codex-vscode:session-1", "codex-vscode", "DesktopToolCall:shell_command");

        var snapshot = store.ApplySessionSignal(AgentSignal.Done, "codex-cli:session-1", "codex-cli", "Stop");

        Assert.Equal(AgentSignal.Done, snapshot.Aggregate);
        Assert.Equal(false, snapshot.Sessions.Any(session => session.SessionId == "codex-vscode:session-1"));
        Assert.Equal(true, snapshot.Sessions.Any(session =>
            session.SessionId == "codex-cli:session-1"
            && session.Signal == AgentSignal.Done));
    }

    public static void CodexDonePreservesSameConversationPermissionState()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        store.ApplySessionSignal(AgentSignal.PermissionRequest, "codex-cli:session-1", "codex-cli", "PermissionRequest");

        var snapshot = store.ApplySessionSignal(AgentSignal.Done, "codex-vscode:session-1", "codex-vscode", "DesktopTaskComplete");

        Assert.Equal(AgentSignal.Permission, snapshot.Aggregate);
        Assert.Equal(true, snapshot.Sessions.Any(session =>
            session.SessionId == "codex-cli:session-1"
            && session.Signal == AgentSignal.PermissionRequest));
    }

    public static void SessionStartDoesNotCreateActiveSessionRows()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        var snapshot = store.ApplySessionSignal(AgentSignal.SessionStart, "claude-main", "claude-code", "SessionStart");

        Assert.Equal(AgentSignal.Idle, snapshot.Aggregate);
        Assert.Equal(0, snapshot.Sessions.Count);
    }

    public static void ReadySessionRowsAreRemovedWhenReadingSnapshot()
    {
        using var fixture = TempFixture.Create();
        var path = Path.Combine(fixture.DirectoryPath, "status.json");
        File.WriteAllText(path, """
        {
          "schema_version": 1,
          "aggregate": "thinking",
          "updated_at": "2026-06-05T09:49:40Z",
          "sessions": {
            "claude-start": {
              "agent": "claude-code",
              "signal": "idle",
              "last_event": "SessionStart",
              "updated_at": "2026-06-05T09:49:38Z"
            },
            "claude-active": {
              "agent": "claude-code",
              "signal": "thinking",
              "last_event": "UserPromptSubmit",
              "updated_at": "2026-06-05T09:49:40Z"
            }
          },
          "events": []
        }
        """);

        var store = new SignalStateStore(path);
        var snapshot = store.ReadSnapshot(DateTimeOffset.Parse("2026-06-05T09:50:00Z"));

        Assert.Equal(1, snapshot.Sessions.Count);
        Assert.Equal("claude-active", snapshot.Sessions.Single().SessionId);
    }

    public static void CorruptStatusFileReadsAsStale()
    {
        using var fixture = TempFixture.Create();
        var path = Path.Combine(fixture.DirectoryPath, "status.json");
        File.WriteAllText(path, "{");
        var store = new SignalStateStore(path);

        var snapshot = store.ReadSnapshot();

        Assert.Equal(AgentSignal.Stale, snapshot.Aggregate);
        Assert.Equal(0, snapshot.Sessions.Count);
    }
}

static class HookInstallerTests
{
    public static void DryRunBuildsClaudeSettingsWithoutWriting()
    {
        using var fixture = TempFixture.Create();
        var home = Path.Combine(fixture.DirectoryPath, "home");
        var cliPath = Path.Combine(fixture.DirectoryPath, "agent-signal.exe");
        var result = HookConfigInstaller.PreviewClaudeInstall(home, cliPath);

        Assert.Equal(false, File.Exists(Path.Combine(home, ".claude", "settings.json")));
        Assert.Equal(true, result.Changed);
        Assert.Equal("Claude Code", result.TargetName);
        Assert.Equal(true, result.Json.Contains("PermissionRequest", StringComparison.Ordinal));
        Assert.Equal(true, result.Json.Contains(EscapeJson(cliPath), StringComparison.Ordinal));
        Assert.Equal(true, result.Json.Contains("claude-hook", StringComparison.Ordinal));
    }

    public static void HookInstallerEmitsPowerShellSafeWindowsCommands()
    {
        using var fixture = TempFixture.Create();
        var cliPath = Path.Combine(fixture.DirectoryPath, "path with spaces", "agent-signal.exe");
        var result = HookConfigInstaller.PreviewCodexInstall(Path.Combine(fixture.DirectoryPath, "hooks.json"), cliPath);
        var command = System.Text.Json.Nodes.JsonNode.Parse(result.Json)!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();

        Assert.Equal(true, command.Contains("powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command", StringComparison.Ordinal), command);
        Assert.Equal(true, command.Contains("& '" + EscapePowerShell(cliPath) + "' codex-hook PreToolUse", StringComparison.Ordinal), command);
    }

    public static void HookInstallerReplacesDuplicateAgentSignalHookCommands()
    {
        using var fixture = TempFixture.Create();
        var home = Path.Combine(fixture.DirectoryPath, "home");
        var claudeDirectory = Path.Combine(home, ".claude");
        Directory.CreateDirectory(claudeDirectory);
        File.WriteAllText(Path.Combine(claudeDirectory, "settings.json"), """
        {
          "hooks": {
            "Stop": [
              {
                "hooks": [
                  {
                    "type": "command",
                    "command": "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"& 'C:\\old\\AgentSignalBar.Cli\\agent-signal.exe' claude-hook\"",
                    "timeout": 5
                  }
                ],
                "matcher": ""
              },
              {
                "hooks": [
                  {
                    "type": "command",
                    "command": "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"& 'C:\\old\\AgentSignalBar.Windows\\agent-signal.exe' claude-hook\"",
                    "timeout": 5
                  }
                ],
                "matcher": ""
              },
              {
                "hooks": [
                  {
                    "type": "command",
                    "command": "python old-traffic-light.py idle",
                    "timeout": 10
                  }
                ]
              }
            ]
          }
        }
        """);
        var cliPath = Path.Combine(fixture.DirectoryPath, "agent-signal.exe");

        var result = HookConfigInstaller.PreviewClaudeInstall(home, cliPath);
        var stopCommands = CommandsForEvent(result.Json, "Stop");

        Assert.Equal(1, stopCommands.Count(command =>
            command.Contains("agent-signal.exe", StringComparison.OrdinalIgnoreCase)
            && command.Contains("claude-hook", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(true, stopCommands.Any(command =>
            command.Contains("claude-hook Stop", StringComparison.Ordinal)));
        Assert.Equal(true, stopCommands.Any(command =>
            command.Contains("python old-traffic-light.py idle", StringComparison.Ordinal)));
    }

    public static void HookInstallerKeepsCurrentAgentSignalHookConfigurationIdempotent()
    {
        using var fixture = TempFixture.Create();
        var home = Path.Combine(fixture.DirectoryPath, "home");
        var settingsPath = Path.Combine(home, ".claude", "settings.json");
        var cliPath = Path.Combine(fixture.DirectoryPath, "agent-signal.exe");
        var first = HookConfigInstaller.PreviewClaudeInstall(home, cliPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, first.Json);

        var second = HookConfigInstaller.PreviewClaudeInstall(home, cliPath);
        var stopCommands = CommandsForEvent(second.Json, "Stop");

        Assert.Equal(false, second.Changed);
        Assert.Equal(1, stopCommands.Count(command =>
            command.Contains("agent-signal.exe", StringComparison.OrdinalIgnoreCase)
            && command.Contains("claude-hook Stop", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> CommandsForEvent(string json, string eventName)
    {
        var commands = new List<string>();
        var blocks = JsonNode.Parse(json)!["hooks"]![eventName]!.AsArray();
        foreach (var block in blocks)
        {
            if (block?["hooks"] is not JsonArray hooks)
            {
                continue;
            }

            foreach (var hook in hooks)
            {
                var command = hook?["command"]?.GetValue<string>();
                if (command is not null)
                {
                    commands.Add(command);
                }
            }
        }

        return commands;
    }

    private static string EscapeJson(string value) =>
        System.Text.Json.JsonEncodedText.Encode(value, System.Text.Encodings.Web.JavaScriptEncoder.Default).ToString();

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

static class ConnectionHealthCheckTests
{
    public static void ConnectionHealthCheckReportsInstalledAndDuplicateHooks()
    {
        using var fixture = TempFixture.Create();
        var home = Path.Combine(fixture.DirectoryPath, "home");
        var stateFile = Path.Combine(fixture.DirectoryPath, "status.json");
        var cliPath = Path.Combine(fixture.DirectoryPath, "agent-signal.exe");
        var codexHooksPath = Path.Combine(home, ".codex", "hooks.json");
        var codexSessionsDirectory = Path.Combine(home, ".codex", "sessions");
        Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
        File.WriteAllText(cliPath, "");

        var store = new SignalStateStore(stateFile);
        store.ApplySessionSignal(AgentSignal.Working, "codex-main", "codex-cli", "PreToolUse");
        Directory.CreateDirectory(codexSessionsDirectory);
        var codexLogPath = Path.Combine(codexSessionsDirectory, "rollout-2026-06-05T14-27-44-019e9677-5998-7693-9405-8ad6284386b5.jsonl");
        File.WriteAllText(codexLogPath, "{}" + Environment.NewLine);

        var claudePreview = HookConfigInstaller.PreviewClaudeInstall(home, cliPath);
        Directory.CreateDirectory(Path.GetDirectoryName(claudePreview.Path)!);
        File.WriteAllText(claudePreview.Path, claudePreview.Json);

        var codexPreview = HookConfigInstaller.PreviewCodexInstall(codexHooksPath, cliPath);
        Directory.CreateDirectory(Path.GetDirectoryName(codexPreview.Path)!);
        var codexRoot = JsonNode.Parse(codexPreview.Json)!;
        codexRoot["hooks"]!["Stop"]!.AsArray().Add(new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"& 'C:\\old\\agent-signal.exe' codex-hook Stop\""
                }
            }
        });
        File.WriteAllText(codexPreview.Path, codexRoot.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var report = ConnectionHealthCheck.Run(new ConnectionHealthCheckOptions(
            HomeDirectory: home,
            AgentSignalCliPath: cliPath,
            StateFilePath: stateFile,
            CodexHooksPath: codexHooksPath,
            CodexSessionsDirectory: codexSessionsDirectory));

        Assert.Equal(true, report.Items.Any(item =>
            item.Id == ConnectionHealthItemId.ClaudeHooks
            && item.Severity == ConnectionHealthSeverity.Ok));
        Assert.Equal(true, report.Items.Any(item =>
            item.Id == ConnectionHealthItemId.CodexHooks
            && item.Severity == ConnectionHealthSeverity.Warning
            && item.Detail.Contains("duplicate", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(true, report.Items.Any(item =>
            item.Id == ConnectionHealthItemId.StateFile
            && item.Severity == ConnectionHealthSeverity.Ok
            && item.Detail.Contains("working", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(true, report.Items.Any(item =>
            item.Id == ConnectionHealthItemId.CodexSessionLogs
            && item.Severity == ConnectionHealthSeverity.Ok
            && item.Detail.Contains(Path.GetFileName(codexLogPath), StringComparison.OrdinalIgnoreCase)));
    }
}

static class SignalSelfTestRunnerTests
{
    public static void SignalSelfTestWritesAndVerifiesEveryManualStatus()
    {
        using var fixture = TempFixture.Create();
        var store = new SignalStateStore(Path.Combine(fixture.DirectoryPath, "status.json"));

        var report = SignalSelfTestRunner.Run(store);

        Assert.Equal(ManualSignalTestCase.All.Count + 1, report.Results.Count);
        Assert.Equal(true, report.Passed);
        Assert.Equal(true, report.Results.Any(result =>
            result.Signal == AgentSignal.Thinking
            && result.ExpectedDisplayState == DisplayState.Active
            && result.ActualDisplayState == DisplayState.Active
            && result.Passed));
        foreach (var testCase in ManualSignalTestCase.All)
        {
            Assert.Equal(true, report.Results.Any(result =>
                result.Signal == testCase.Signal
                && result.ExpectedDisplayState == testCase.ExpectedDisplayState
                && result.ActualDisplayState == testCase.ExpectedDisplayState
                && result.Passed));
        }

        var finalSnapshot = store.ReadSnapshot();
        Assert.Equal(report.FinalSnapshot.Aggregate, finalSnapshot.Aggregate);
    }
}

sealed class TempFixture : IDisposable
{
    public string DirectoryPath { get; }

    private TempFixture(string directoryPath)
    {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(directoryPath);
    }

    public static TempFixture Create()
    {
        return new TempFixture(Path.Combine(Path.GetTempPath(), "agent-signal-tests", Guid.NewGuid().ToString("N")));
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

static class WorkBuddySessionMonitorTests
{
    public static void WorkBuddySessionMonitorDetectsActiveSessionFromFileTimestamp()
    {
        using var fixture = TempFixture.Create();
        var sessionsDir = Path.Combine(fixture.DirectoryPath, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var sessionFile = Path.Combine(sessionsDir, "11744.json");
        File.WriteAllText(sessionFile, """
        {
          "pid": 11744,
          "lastHeartbeat": 1783137836422,
          "sessionId": "test-session-1",
          "cwd": "D:\\\\iQuant",
          "startedAt": 1783137206030,
          "kind": "interactive",
          "url": "http://127.0.0.1:62190",
          "endpoint": "http://127.0.0.1:62190",
          "mode": "local",
          "version": "2.103.3",
          "os": "win32",
          "arch": "x64",
          "hostname": "test",
          "updatedAt": 1783137836422
        }
        """);

        var monitor = new WorkBuddySessionMonitor(sessionsDir, TimeSpan.FromSeconds(30));
        var now = DateTimeOffset.UtcNow;
        var activities = monitor.Poll(now).ToArray();

        Assert.Equal(1, activities.Length);
        Assert.Equal("workbuddy:test-session-1", activities[0].SessionId);
        Assert.Equal("workbuddy", activities[0].Agent);
        Assert.Equal(AgentSignal.Thinking, activities[0].Signal);

        // Second poll with no file change should not re-emit.
        var second = monitor.Poll(now.AddSeconds(1)).ToArray();
        Assert.Equal(0, second.Length);

        // After threshold passes, session should emit SessionEnd.
        File.SetLastWriteTimeUtc(sessionFile, now.AddSeconds(-31).UtcDateTime);
        var third = monitor.Poll(now.AddSeconds(31)).ToArray();
        Assert.Equal(1, third.Length);
        Assert.Equal(AgentSignal.SessionEnd, third[0].Signal);
        Assert.Equal("workbuddy:test-session-1", third[0].SessionId);
    }

    public static void WorkBuddySessionMonitorFallsBackToFileNameWhenSessionIdMissing()
    {
        using var fixture = TempFixture.Create();
        var sessionsDir = Path.Combine(fixture.DirectoryPath, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var sessionFile = Path.Combine(sessionsDir, "4484.json");
        File.WriteAllText(sessionFile, "{ \"pid\": 4484 }");

        var monitor = new WorkBuddySessionMonitor(sessionsDir, TimeSpan.FromSeconds(30));
        var activities = monitor.Poll(DateTimeOffset.UtcNow).ToArray();

        Assert.Equal(1, activities.Length);
        Assert.Equal("workbuddy:4484", activities[0].SessionId);
    }

    public static void WorkBuddySessionMonitorIgnoresMissingDirectory()
    {
        using var fixture = TempFixture.Create();
        var sessionsDir = Path.Combine(fixture.DirectoryPath, "missing");
        var monitor = new WorkBuddySessionMonitor(sessionsDir, TimeSpan.FromSeconds(10));
        var activities = monitor.Poll(DateTimeOffset.UtcNow).ToArray();
        Assert.Equal(0, activities.Length);
    }
}

static class Assert
{
    public static void Equal<T>(T expected, T actual, string? detail = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}." + (detail is null ? "" : Environment.NewLine + detail));
        }
    }

    public static void NotNull(object? value, string? detail = null)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected non-null value." + (detail is null ? "" : Environment.NewLine + detail));
        }
    }
}
