namespace StabilizatorHub.Application.Common;

/// <summary>Result of a use-case operation, without exceptions for expected failures.</summary>
public sealed record OperationResult(bool Succeeded, string? Error = null)
{
    public static OperationResult Ok() => new(true);

    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>Result of a use-case operation carrying a value on success.</summary>
public sealed record OperationResult<T>(bool Succeeded, T? Value, string? Error = null)
{
    public static OperationResult<T> Ok(T value) => new(true, value);

    public static OperationResult<T> Fail(string error) => new(false, default, error);
}
