using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WeatherApp.API.Filters;

/// <summary>Roda IValidator&lt;T&gt; registrado para os argumentos da action e devolve 400
/// (ValidationProblemDetails) se falhar. Escrito à mão em vez de FluentValidation.AspNetCore
/// (descontinuado) ou do AddValidation() nativo do .NET 10 (só cobre Minimal APIs).</summary>
public sealed class ValidacaoActionFilter(IServiceProvider provedor) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argumento in context.ActionArguments.Values)
        {
            if (argumento is null)
            {
                continue;
            }

            var tipoValidator = typeof(IValidator<>).MakeGenericType(argumento.GetType());
            if (provedor.GetService(tipoValidator) is not IValidator validator)
            {
                continue;
            }

            var resultado = await validator.ValidateAsync(
                new ValidationContext<object>(argumento), context.HttpContext.RequestAborted);

            if (resultado.IsValid)
            {
                continue;
            }

            foreach (var erro in resultado.Errors)
            {
                context.ModelState.AddModelError(erro.PropertyName, erro.ErrorMessage);
            }

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
            {
                Title = "Dados inválidos",
                Status = StatusCodes.Status400BadRequest,
                Instance = context.HttpContext.Request.Path
            });
            return;
        }

        await next();
    }
}
