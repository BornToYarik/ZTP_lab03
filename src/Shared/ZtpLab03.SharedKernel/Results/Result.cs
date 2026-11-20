namespace ZtpLab03.SharedKernel.Results;

/// <summary>
/// Standardized result wrapper used across all Use Cases to avoid repetitive error-handling boilerplate (DRY).
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }
    public ErrorType ErrorType { get; }

    private Result(bool isSuccess, T? value, string? errorMessage, ErrorType errorType)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        ErrorType = errorType;
    }

    public static Result<T> Success(T value) =>
        new(true, value, null, ErrorType.None);

    public static Result<T> Failure(string errorMessage, ErrorType errorType = ErrorType.Validation) =>
        new(false, default, errorMessage, errorType);
}

public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4
}
