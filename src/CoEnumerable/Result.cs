using System;

namespace CoEnumerable;

public readonly struct Result<T>
{
    public T? Value { get; }
    public Exception? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(T value) { Value = value; }
    private Result(Exception error) { Error = error; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(Exception error) => new(error);
}
