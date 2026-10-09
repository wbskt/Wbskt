namespace Wbskt.Infrastructure;

// Appended-to only: Error.Type is serialized as its numeric value in error responses,
// so reordering these would silently change the wire format for existing clients.
public enum ErrorType { Failure, Validation, NotFound, Conflict, Unauthorized, Forbidden, Unavailable }

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public bool IsNone => this == None;

    // Factory methods — one per ErrorType, kept in sync with the enum
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>The caller could not be identified at all — no token, or a token we cannot parse. Maps to 401.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>The caller is known but lacks the required permission. Maps to 403 — never 401, or clients
    /// that redirect to login on 401 will sign the user out over a missing permission.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>A dependency the request needs (the message broker, the engine) is down, and nothing was
    /// done, so the same request can simply be retried. Maps to 503 with <c>Retry-After</c>.</summary>
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && !error.IsNone)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error.IsNone)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Runs <paramref name="action"/> only if the result failed.</summary>
    public Result OnFailure(Action<Error> action)
    {
        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    /// <summary>Runs <paramref name="action"/> only if the result succeeded.</summary>
    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Collapses the result to a value of <typeparamref name="T"/> for both branches.</summary>
    public T Match<T>(Func<T> onSuccess, Func<Error, T> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Error);

    public Result ThrowOnFailure(Func<Error, Exception>? exceptionFactory = null)
    {
        if (IsFailure)
        {
            throw exceptionFactory?.Invoke(Error) ?? new InvalidOperationException(Error.Message);
        }

        return this;
    }
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Failure results have no value.");

    public static Result<TValue> Success(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure(Error error) => new(default, false, error);

    /// <summary>Runs <paramref name="action"/> only if the result succeeded, passing the value.</summary>
    public Result<TValue> OnSuccess(Action<TValue> action)
    {
        if (IsSuccess)
        {
            action(Value);
        }

        return this;
    }

    /// <summary>Runs <paramref name="action"/> only if the result failed. Preserves the generic type for chaining.</summary>
    public new Result<TValue> OnFailure(Action<Error> action)
    {
        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    public new Result<TValue> ThrowOnFailure(Func<Error, Exception>? exceptionFactory = null)
    {
        base.ThrowOnFailure(exceptionFactory);
        return this;
    }

    /// <summary>Collapses the result to a value of <typeparamref name="T"/> for both branches.</summary>
    public T Match<T>(Func<TValue, T> onSuccess, Func<Error, T> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);

    /// <summary>Transforms the success value; passes failures through unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<TValue, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(Value)) : Result<TOut>.Failure(Error);

    /// <summary>Chains another Result-returning operation; short-circuits on failure.</summary>
    public Result<TOut> Bind<TOut>(Func<TValue, Result<TOut>> bind) =>
        IsSuccess ? bind(Value) : Result<TOut>.Failure(Error);

    /// <summary>Downgrades a success to a failure if <paramref name="predicate"/> is not satisfied.</summary>
    public Result<TValue> Ensure(Func<TValue, bool> predicate, Error error) =>
        IsSuccess && !predicate(Value) ? Result<TValue>.Failure(error) : this;

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}