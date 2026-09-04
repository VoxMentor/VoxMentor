using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Behaviors;
using VoxMentor.Application.Services;

namespace VoxMentor.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Application-layer services: FluentValidation validators from the
    /// assembly, the BKT engine, and the MediatR pipeline with validation behavior.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IBktEngine, BktEngine>();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        return services;
    }
}
