using Microsoft.AspNetCore.Mvc;
using ZtpLab03.SharedKernel.Results;

namespace ZtpLab03.Api.Controllers;

/// <summary>
/// Centralized mapper converting Application Layer <see cref="Result{T}"/> objects into HTTP responses (DRY principle).
/// </summary>
public static class ApiResultMapper
{
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(result.Value);
        }

        var payload = new { error = result.ErrorMessage, type = result.ErrorType.ToString() };

        return result.ErrorType switch
        {
            ErrorType.NotFound => controller.NotFound(payload),
            ErrorType.Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden, payload),
            ErrorType.Conflict => controller.Conflict(payload),
            _ => controller.BadRequest(payload)
        };
    }
}
