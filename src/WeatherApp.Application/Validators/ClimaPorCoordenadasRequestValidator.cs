using FluentValidation;
using WeatherApp.Application.DTOs;

namespace WeatherApp.Application.Validators;

public sealed class ClimaPorCoordenadasRequestValidator : AbstractValidator<ClimaPorCoordenadasRequest>
{
    public ClimaPorCoordenadasRequestValidator()
    {
        RuleFor(x => x.Latitude)
            .NotNull().WithMessage("A latitude é obrigatória.")
            .InclusiveBetween(-90m, 90m).WithMessage("A latitude deve estar entre -90 e 90.");

        RuleFor(x => x.Longitude)
            .NotNull().WithMessage("A longitude é obrigatória.")
            .InclusiveBetween(-180m, 180m).WithMessage("A longitude deve estar entre -180 e 180.");
    }
}
