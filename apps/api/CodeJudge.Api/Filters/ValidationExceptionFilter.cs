using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CodeJudge.Api.Filters;

/// <summary>
/// Turns a FluentValidation failure from the MediatR pipeline into RFC 7807
/// ValidationProblemDetails, so a validation error looks the same whether it came from
/// model binding or from a handler's validator.
/// </summary>
public sealed class ValidationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ValidationException validationException)
        {
            return;
        }

        var errors = validationException.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Instance = context.HttpContext.Request.Path
        };

        context.Result = new BadRequestObjectResult(problem);
        context.ExceptionHandled = true;
    }
}
