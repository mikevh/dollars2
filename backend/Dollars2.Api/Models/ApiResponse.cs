namespace Dollars2.Api.Models;

public class ApiResponse<T>
{
    public T? Data { get; set; }
    public ApiError? Error { get; set; }

    public static ApiResponse<T> Success(T data) => new() { Data = data, Error = null };
    public static ApiResponse<T> Fail(string message, string? code = null) =>
        new() { Data = default, Error = new ApiError { Message = message, Code = code ?? "ERROR" } };
}

public class ApiError
{
    public required string Message { get; set; }
    public required string Code { get; set; }
}
