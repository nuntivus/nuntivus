using System;
using System.Collections.Generic;
using System.Linq;

namespace NuntivusServer.Common
{
    /// <summary>
    /// Represents the outcome of an operation, exposing success state and any associated errors.
    /// </summary>
    public class Result
    {
        protected Result(bool isSuccess, IReadOnlyList<Error> errors)
        {
            IsSuccess = isSuccess;
            Errors = errors ?? [];
        }

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        public IReadOnlyList<Error> Errors { get; }

        public Error? Error => Errors.Count > 0 ? Errors[0] : null;

        public static Result Success() => new(true, []);

        public static Result Failure(Error error) => new(false, [error]);

        public static Result Failure(IEnumerable<Error> errors) => new(false, errors.ToArray());

        public static Result Failure(string code, string message) => Failure(new Error(code, message));
        
        public static Result Failure(Exception ex) => Failure(new Error("unexpected", "An unexpected error occurred."));

        public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

        public static Result<T> Failure<T>(Error error) => Result<T>.Fail(error);

        public static Result<T> Failure<T>(Exception ex) => Result<T>.Fail(new Error("unexpected", "An unexpected error occurred."));

        public static Result<T> Failure<T>(string code, string message) => Result<T>.Fail(new Error(code, message));

        /// <summary>
        /// Throws a <see cref="ResultFailureException"/> when the result is a failure.
        /// </summary>
        public void EnsureSuccess()
        {
            if (IsFailure)
            {
                throw new ResultFailureException(Errors);
            }
        }
    }

    /// <summary>
    /// Represents the outcome of an operation that produces a value on success.
    /// </summary>
    public sealed class Result<T> : Result
    {
        private readonly T? _value;

        private Result(T? value, bool isSuccess, IReadOnlyList<Error> errors)
            : base(isSuccess, errors)
        {
            _value = value;
        }

        public T Value => IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed result.");

        /// <summary>
        /// Returns the value when successful; otherwise throws a <see cref="ResultFailureException"/>.
        /// </summary>
        public T EnsureValue()
        {
            EnsureSuccess();
            return _value!;
        }

        public static Result<T> Ok(T value) => new(value, true, []);

        public static Result<T> Fail(Error error) => new(default, false, [error]);

        public static Result<T> Fail(IEnumerable<Error> errors) => new(default, false, errors.ToArray());

        public static implicit operator Result<T>(T value) => Ok(value);

        public static implicit operator Result<T>(Error error) => Fail(error);
    }

    /// <summary>
    /// Describes a failure with a machine-readable code and human-readable message.
    /// </summary>
    public sealed record Error(string Code, string Message)
    {
        public static readonly Error None = new(string.Empty, string.Empty);

        public static Error NotFound(string message) => new("not_found", message);
        public static Error Validation(string message) => new("validation", message);
        public static Error Conflict(string message) => new("conflict", message);
        public static Error Unauthorized(string message) => new("unauthorized", message);
        public static Error Forbidden(string message) => new("forbidden", message);
        public static Error Unexpected(string message) => new("unexpected", message);

        public override string ToString() => string.IsNullOrEmpty(Code) ? Message : $"[{Code}] {Message}";
    }

    /// <summary>
    /// Exception thrown when a failed <see cref="Result"/> is asserted as successful.
    /// </summary>
    public sealed class ResultFailureException : Exception
    {
        public ResultFailureException(IReadOnlyList<Error> errors)
            : base(BuildMessage(errors))
        {
            Errors = errors ?? [];
        }

        public IReadOnlyList<Error> Errors { get; }

        public Error? Error => Errors.Count > 0 ? Errors[0] : null;

        private static string BuildMessage(IReadOnlyList<Error> errors)
        {
            if (errors is null || errors.Count == 0)
            {
                return "The operation failed.";
            }

            return string.Join("; ", errors.Select(e => e.ToString()));
        }
    }
}
