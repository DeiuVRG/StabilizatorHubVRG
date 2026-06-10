using StabilizatorHub.Domain.Entities;
using StabilizatorHub.Domain.Services;
using StabilizatorHub.Domain.ValueObjects;
using Xunit;

namespace StabilizatorHub.Tests;

public class VoltageEventTrackerTests
{
    private static readonly DateTime T0 = new(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);

    private static VoltageEventTracker NewTracker() => new(VoltageThresholds.Default);

    [Fact]
    public void NormalVoltage_ProducesNoTransitions()
    {
        var tracker = NewTracker();

        var transitions = tracker.Process(230, T0);

        Assert.Empty(transitions);
        Assert.False(tracker.HasOpenEvent);
    }

    [Theory]
    [InlineData(215, VoltageEventType.Undervoltage)]
    [InlineData(214.9, VoltageEventType.Undervoltage)]
    [InlineData(240, VoltageEventType.Overvoltage)]
    [InlineData(252, VoltageEventType.Overvoltage)]
    public void CrossingALimit_StartsAnEvent(double voltage, VoltageEventType expectedType)
    {
        var tracker = NewTracker();

        var transitions = tracker.Process(voltage, T0);

        var started = Assert.IsType<VoltageEventStarted>(Assert.Single(transitions));
        Assert.Equal(expectedType, started.Type);
        Assert.Equal(voltage, started.Voltage);
        Assert.True(tracker.HasOpenEvent);
    }

    [Fact]
    public void UndervoltageEvent_TracksMinimumAndSampleCount()
    {
        var tracker = NewTracker();
        tracker.Process(214, T0);

        tracker.Process(209, T0.AddMinutes(1));
        var transitions = tracker.Process(212, T0.AddMinutes(2));

        var progressed = Assert.IsType<VoltageEventProgressed>(Assert.Single(transitions));
        Assert.Equal(209, progressed.ExtremeVoltage); // minimum stays the worst value
        Assert.Equal(3, progressed.SampleCount);
    }

    [Fact]
    public void Hysteresis_KeepsEventOpenJustAboveTheLimit()
    {
        var tracker = NewTracker();
        tracker.Process(214, T0);

        // 216 V is above the 215 V limit but inside the 2 V hysteresis band.
        var transitions = tracker.Process(216, T0.AddMinutes(1));

        Assert.IsType<VoltageEventProgressed>(Assert.Single(transitions));
        Assert.True(tracker.HasOpenEvent);
    }

    [Fact]
    public void RecoveryBeyondHysteresis_EndsTheEvent()
    {
        var tracker = NewTracker();
        tracker.Process(214, T0);

        var transitions = tracker.Process(220, T0.AddMinutes(2));

        var ended = Assert.IsType<VoltageEventEnded>(Assert.Single(transitions));
        Assert.Equal(VoltageEventType.Undervoltage, ended.Type);
        Assert.Equal(T0.AddMinutes(2), ended.EndedAtUtc);
        Assert.False(tracker.HasOpenEvent);
    }

    [Fact]
    public void JumpFromUnderToOver_EndsAndStartsInOneSample()
    {
        var tracker = NewTracker();
        tracker.Process(210, T0);

        var transitions = tracker.Process(245, T0.AddMinutes(1));

        Assert.Equal(2, transitions.Count);
        Assert.Equal(VoltageEventType.Undervoltage, Assert.IsType<VoltageEventEnded>(transitions[0]).Type);
        Assert.Equal(VoltageEventType.Overvoltage, Assert.IsType<VoltageEventStarted>(transitions[1]).Type);
    }

    [Fact]
    public void Restore_ContinuesAPreviouslyOpenEvent()
    {
        var tracker = NewTracker();
        tracker.Restore(VoltageEventType.Overvoltage, T0, 246, 7);

        var transitions = tracker.Process(248, T0.AddMinutes(1));

        var progressed = Assert.IsType<VoltageEventProgressed>(Assert.Single(transitions));
        Assert.Equal(248, progressed.ExtremeVoltage);
        Assert.Equal(8, progressed.SampleCount);
    }

    [Fact]
    public void CloseAt_ForcesTheOpenEventToEnd()
    {
        var tracker = NewTracker();
        tracker.Process(210, T0);

        var ended = tracker.CloseAt(T0.AddMinutes(5));

        Assert.NotNull(ended);
        Assert.Equal(T0.AddMinutes(5), ended!.EndedAtUtc);
        Assert.False(tracker.HasOpenEvent);
        Assert.Null(tracker.CloseAt(T0.AddMinutes(6)));
    }
}
