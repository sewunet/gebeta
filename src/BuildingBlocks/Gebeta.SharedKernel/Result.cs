namespace Gebeta.SharedKernel;

/// <summary>A failure that is expected and shown to the user (validation, not found, conflict).</summary>
public sealed record Error(string Code, string Message)
{
    public static Error NotFound(string what, object id) => new("not_found", $"{what} '{id}' was not found.");
    public static Error Validation(string message) => new("validation", message);
    public static Error Conflict(string message) => new("conflict", message);
}

/// <summary>The outcome of an operation that can fail in an expected way.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Fail(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    /// <summary>The value; reading it on a failed result is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result has no value: {Error!.Code} {Error.Message}");

    internal static Result<T> Ok(T value) => new(value, null);
    internal static Result<T> Fail(Error error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}
