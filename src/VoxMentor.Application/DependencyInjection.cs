using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Common.Behaviors;
using VoxMentor.Application.Common.Services;
using VoxMentor.Application.Features.MockInterviews;
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

        // #124: serializes the mock-interview daily-quota count+insert and
        // per-interview transcript appends (in-process; see class doc).
        services.AddSingleton<AsyncKeyedLock>();

        // #126: single dialogue implementation shared by the REST start/answer
        // handlers and InterviewHub.
        services.AddScoped<MockInterviewSessionService>();

        // #119: per-request Random for the JD-weighted ε-greedy selector. Scoped
        // rather than a shared instance — Random isn't thread-safe.
        services.AddScoped(_ => new Random());

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        return services;
    }
}
