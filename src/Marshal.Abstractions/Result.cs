namespace Marshal.Abstractions;

/// <summary>
/// Outcome of an operation that can fail in an expected way. Exceptions are reserved for bugs.
/// </summary>
public class Result
{
    private readonly Error? _error;

    /// <summary>Creates a result; <paramref name="error"/> is null for success.</summary>
    protected Result(Error? error) => _error = error;

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess => _error is null;

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => _error is not null;

    /// <summary>The failure. Throws when the result is a success.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(null);

    /// <summary>Creates a failed result.</summary>
    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Success<T>(T value) => Result<T>.FromValue(value);

    /// <summary>Creates a failed result of type <typeparamref name="T"/>.</summary>
    public static Result<T> Failure<T>(Error error) => Result<T>.FromError(error);

    /// <summary>Converts an error into a failed result.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}
