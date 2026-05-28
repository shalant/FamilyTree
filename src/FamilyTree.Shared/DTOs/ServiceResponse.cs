namespace FamilyTree.Shared;

public class ServiceResponse<T>
{
    public T? Data { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";

    public static ServiceResponse<T> Ok(T data) =>
        new() { Success = true, Data = data };

    public static ServiceResponse<T> Fail(string message) =>
        new() { Success = false, Message = message };
}

// Non-generic version for operations that return no data (e.g. delete)
public class ServiceResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";

    public static ServiceResponse Ok() =>
        new() { Success = true };

    public static ServiceResponse Fail(string message) =>
        new() { Success = false, Message = message };
}