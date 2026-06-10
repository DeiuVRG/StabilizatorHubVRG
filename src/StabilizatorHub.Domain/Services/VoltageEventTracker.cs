using StabilizatorHub.Domain.Entities;
using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Domain.Services;

/// <summary>Base type for the state transitions emitted by <see cref="VoltageEventTracker"/>.</summary>
public abstract record VoltageTransition;

/// <summary>A new abnormal-voltage episode just began.</summary>
public sealed record VoltageEventStarted(VoltageEventType Type, DateTime StartedAtUtc, double Voltage) : VoltageTransition;

/// <summary>The open episode is still in progress; extreme/sample counters were updated.</summary>
public sealed record VoltageEventProgressed(VoltageEventType Type, double ExtremeVoltage, int SampleCount) : VoltageTransition;

/// <summary>The open episode ended (voltage returned inside the normal band, or forced close).</summary>
public sealed record VoltageEventEnded(VoltageEventType Type, DateTime EndedAtUtc, double ExtremeVoltage, int SampleCount) : VoltageTransition;

/// <summary>
/// Per-device state machine that turns a stream of input-voltage samples into
/// undervoltage/overvoltage episodes. Pure logic (no I/O): the caller persists
/// the transitions. Hysteresis keeps an episode open until the voltage clearly
/// re-enters the normal band, avoiding open/close flapping around a limit.
/// </summary>
public sealed class VoltageEventTracker
{
    private readonly VoltageThresholds _thresholds;

    private VoltageEventType? _openType;
    private DateTime _openStartedAtUtc;
    private double _extremeVoltage;
    private int _sampleCount;

    public VoltageEventTracker(VoltageThresholds thresholds)
    {
        _thresholds = thresholds;
    }

    public bool HasOpenEvent => _openType is not null;

    /// <summary>Rehydrates the tracker from an episode left open in the database (e.g. after a backend restart).</summary>
    public void Restore(VoltageEventType type, DateTime startedAtUtc, double extremeVoltage, int sampleCount)
    {
        _openType = type;
        _openStartedAtUtc = startedAtUtc;
        _extremeVoltage = extremeVoltage;
        _sampleCount = sampleCount;
    }

    /// <summary>
    /// Processes one input-voltage sample and returns 0..2 transitions
    /// (an episode may end and a new one of the opposite type may start on the same sample).
    /// </summary>
    public IReadOnlyList<VoltageTransition> Process(double inputVoltage, DateTime timestampUtc)
    {
        var transitions = new List<VoltageTransition>(2);

        if (_openType is null)
        {
            TryStart(inputVoltage, timestampUtc, transitions);
            return transitions;
        }

        if (StillInsideOpenEpisode(inputVoltage))
        {
            _sampleCount++;
            _extremeVoltage = _openType == VoltageEventType.Undervoltage
                ? Math.Min(_extremeVoltage, inputVoltage)
                : Math.Max(_extremeVoltage, inputVoltage);

            transitions.Add(new VoltageEventProgressed(_openType.Value, _extremeVoltage, _sampleCount));
            return transitions;
        }

        transitions.Add(CloseOpenEpisode(timestampUtc));
        TryStart(inputVoltage, timestampUtc, transitions);
        return transitions;
    }

    /// <summary>
    /// Forcibly closes the open episode (device went offline, backend shutdown).
    /// Returns null when nothing was open.
    /// </summary>
    public VoltageEventEnded? CloseAt(DateTime timestampUtc) =>
        _openType is null ? null : CloseOpenEpisode(timestampUtc);

    private void TryStart(double voltage, DateTime timestampUtc, List<VoltageTransition> transitions)
    {
        VoltageEventType? type = null;

        if (_thresholds.IsUndervoltage(voltage))
        {
            type = VoltageEventType.Undervoltage;
        }
        else if (_thresholds.IsOvervoltage(voltage))
        {
            type = VoltageEventType.Overvoltage;
        }

        if (type is null)
        {
            return;
        }

        _openType = type;
        _openStartedAtUtc = timestampUtc;
        _extremeVoltage = voltage;
        _sampleCount = 1;

        transitions.Add(new VoltageEventStarted(type.Value, timestampUtc, voltage));
    }

    private bool StillInsideOpenEpisode(double voltage) => _openType switch
    {
        VoltageEventType.Undervoltage => voltage <= _thresholds.UndervoltageLimit + _thresholds.HysteresisVolts
                                         && !_thresholds.IsOvervoltage(voltage),
        VoltageEventType.Overvoltage => voltage >= _thresholds.OvervoltageLimit - _thresholds.HysteresisVolts
                                        && !_thresholds.IsUndervoltage(voltage),
        _ => false
    };

    private VoltageEventEnded CloseOpenEpisode(DateTime endedAtUtc)
    {
        var ended = new VoltageEventEnded(_openType!.Value, endedAtUtc, _extremeVoltage, _sampleCount);
        _openType = null;
        _sampleCount = 0;
        return ended;
    }
}
