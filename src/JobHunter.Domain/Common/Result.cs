namespace JobHunter.Domain.Common;

/// <summary>
/// A value carrying either a success payload or a failure <see cref="Error"/>, never both and never
/// neither. Expected business outcomes are values, not exceptions (coding-standards §4). The
/// constructor is private; the only ways in are <see cref="Success"/> and <see cref="Failure"/>,
/// which is what makes success-with-error and failure-without-reason unrepresentable.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    /// <summary>The payload. Throws if accessed on a failure — that is a programmer error, not a business outcome.</summary>
    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot read the value of a failed Result.");

    public static Result<T> Success(T value) => new(true, value, Error.None);

    public static Result<T> Failure(Error error)
    {
        if (error is null || error == Error.None)
        {
            throw new ArgumentException("A failed Result must carry a non-empty Error.", nameof(error));
        }

        return new Result<T>(false, default, error);
    }

    /// <summary>Transforms the success payload, propagating a failure unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(Error);

    /// <summary>Chains a further fallible operation, propagating a failure unchanged.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) =>
        IsSuccess ? bind(_value!) : Result<TOut>.Failure(Error);

    /// <summary>Collapses both branches to a single value.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
