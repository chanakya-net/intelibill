namespace HsnFinder.Api.Models.Responses;

public sealed record ApiResponse<T>(bool Success, T? Data, string? Error)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null);

    public static ApiResponse<T> Fail(string error) => new(false, default, error);
}
