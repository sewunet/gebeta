using Gebeta.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Gebeta.Modularity;

/// <summary>Maps application results to HTTP: 200, or Problem Details with 400 / 404 / 409.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var error = result.Error!;
        var status = error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return TypedResults.Problem(title: error.Message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
