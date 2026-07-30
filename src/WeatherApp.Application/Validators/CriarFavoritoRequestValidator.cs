using FluentValidation;
using WeatherApp.Application.DTOs;

namespace WeatherApp.Application.Validators;

public sealed class CriarFavoritoRequestValidator : AbstractValidator<CriarFavoritoRequest>
{
    public CriarFavoritoRequestValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("O nome da cidade é obrigatório.")
            .MinimumLength(2).WithMessage("O nome da cidade deve ter ao menos 2 caracteres.")
            .MaximumLength(150).WithMessage("O nome da cidade deve ter no máximo 150 caracteres.");

        RuleFor(x => x.PaisCodigo)
            .Matches("^[A-Za-z]{2}$").WithMessage("O código do país deve ter exatamente 2 letras (ex.: BR).")
            .When(x => !string.IsNullOrWhiteSpace(x.PaisCodigo));
    }
}
