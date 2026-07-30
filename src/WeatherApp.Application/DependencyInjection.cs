using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using WeatherApp.Application.DTOs;
using WeatherApp.Application.Services;
using WeatherApp.Application.Validators;
using WeatherApp.Domain.Entities;

namespace WeatherApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // TimeProvider.System injetado explicitamente em vez de DateTime.UtcNow espalhado.
        services.TryAddSingletonTimeProvider();
        services.AddScoped<ClimaService>();
        services.AddScoped<FavoritosService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IValidator<CriarFavoritoRequest>, CriarFavoritoRequestValidator>();
        services.AddScoped<IValidator<RegistrarRequest>, RegistrarRequestValidator>();
        services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
    }
}
