namespace Marshal.Abstractions;

/// <summary>
/// Outcome of an operation that returns a value or fails in an expected way.
/// </summary>
/// <typeparam name="T">Type of the value on success.</typeparam>
#pragma warning disable CA1000 // Static factories on the generic type keep call sites short.
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>The value. Throws when the result is a failure.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"A failed result has no value ({Error.Code}).");

    internal static Result<T> FromValue(T value) => new(value, null);

    internal static Result<T> FromError(Error error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Transforms the value of a successful result.</summary>
    public Result<TResult> Map<TResult>(Func<T, TResult> map) =>
        IsSuccess ? Result<TResult>.FromValue(map(Value)) : Result<TResult>.FromError(Error);

    /// <summary>Chains an operation that can fail.</summary>
    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> next) =>
        IsSuccess ? next(Value) : Result<TResult>.FromError(Error);

    /// <summary>Selects one of two branches.</summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    /// <summary>Wraps a value in a successful result.</summary>
    public static implicit operator Result<T>(T value) => FromValue(value);

    /// <summary>Wraps an error in a failed result.</summary>
    public static implicit operator Result<T>(Error error) => FromError(error);
}
#pragma warning restore CA1000
