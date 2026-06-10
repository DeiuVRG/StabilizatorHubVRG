namespace StabilizatorHub.Domain.Abstractions;

/// <summary>
/// Abstraction over the system clock so time-dependent logic stays testable
/// (Dependency Inversion: services depend on this interface, not on DateTime.UtcNow).
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
