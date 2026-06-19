namespace Dollars2.Api.Models;

public class DollarsApiResponse<T>
{
    public T? Data { get; set; }
    public DollarsApiError? Error { get; set; }

    public static DollarsApiResponse<T> Success(T data) => new() { Data = data, Error = null };
    public static DollarsApiResponse<T> Fail(string message, string? code = null) =>
        new() { Data = default, Error = new DollarsApiError { Message = message, Code = code ?? "ERROR" } };
}

public class DollarsApiError
{
    public required string Message { get; set; }
    public required string Code { get; set; }
}
