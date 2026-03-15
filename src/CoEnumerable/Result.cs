using System;

namespace CoEnumerable;

public readonly struct Result<T>
{
    private readonly T? value;
    private readonly Exception? error;

    public bool IsSuccess => error is null;

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public Exception Error => !IsSuccess
        ? error!
        : throw new InvalidOperationException("Cannot access Error on a successful Result.");

    private Result(T value) { this.value = value; }
    private Result(Exception error) { this.error = error; }

#pragma warning disable CA1000
    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }
#pragma warning restore CA1000
}
