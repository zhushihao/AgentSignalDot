namespace AgentSignalBar.Core;

public sealed record SignalSelfTestResult(
    string Name,
    AgentSignal Signal,
    DisplayState ExpectedDisplayState,
    DisplayState ActualDisplayState,
    bool Passed,
    string Detail);

public sealed record SignalSelfTestReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<SignalSelfTestResult> Results,
    SignalSnapshot FinalSnapshot)
{
    public bool Passed => Results.All(result => result.Passed);
}

public static class SignalSelfTestRunner
{
    private static readonly IReadOnlyList<ManualSignalTestCase> Cases =
    [
        new("测试思考中", AgentSignal.Thinking, DisplayState.Active),
        .. ManualSignalTestCase.All
    ];

    public static SignalSelfTestReport Run(SignalStateStore store)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<SignalSelfTestResult>();
        SignalSnapshot? snapshot = null;

        foreach (var testCase in Cases)
        {
            snapshot = Apply(store, testCase);
            var actual = snapshot.Aggregate.DisplayState();
            var passed = actual == testCase.ExpectedDisplayState;
            results.Add(new SignalSelfTestResult(
                testCase.ButtonText,
                testCase.Signal,
                testCase.ExpectedDisplayState,
                actual,
                passed,
                passed
                    ? AgentSignalParser.ToRawValue(snapshot.Aggregate)
                    : $"expected {testCase.ExpectedDisplayState}, got {actual}"));
        }

        snapshot ??= store.ReadSnapshot();
        return new SignalSelfTestReport(startedAt, DateTimeOffset.UtcNow, results, snapshot);
    }

    private static SignalSnapshot Apply(SignalStateStore store, ManualSignalTestCase testCase)
    {
        return testCase.Signal.DisplayState() is DisplayState.Ready or DisplayState.Paused
            ? store.SetManualSignal(testCase.Signal)
            : store.ApplySessionSignal(
                testCase.Signal,
                ManualSignalTestCase.SessionId,
                ManualSignalTestCase.Agent,
                ManualSignalTestCase.Event);
    }
}
