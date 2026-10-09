namespace GymShop.Application.Common;

public enum AppErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Unavailable
}

public sealed record AppError(AppErrorType Type, string Message, string? Code = null);

public class AppResult
{
    protected AppResult(bool isSuccess, AppError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public AppError? Error { get; }

    public static AppResult Success() => new(true, null);
    public static AppResult Failure(AppErrorType type, string message, string? code = null) => new(false, new AppError(type, message, code));
}

public sealed class AppResult<T> : AppResult
{
    private AppResult(bool isSuccess, T? value, AppError? error) : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static AppResult<T> Success(T value) => new(true, value, null);
    public new static AppResult<T> Failure(AppErrorType type, string message, string? code = null) => new(false, default, new AppError(type, message, code));
}
