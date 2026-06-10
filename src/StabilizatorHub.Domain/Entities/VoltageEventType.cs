namespace StabilizatorHub.Domain.Entities;

/// <summary>Kind of abnormal input-voltage episode.</summary>
public enum VoltageEventType
{
    /// <summary>Input voltage dropped to or below the undervoltage limit (default 215 V).</summary>
    Undervoltage = 1,

    /// <summary>Input voltage rose to or above the overvoltage limit (default 240 V).</summary>
    Overvoltage = 2
}
