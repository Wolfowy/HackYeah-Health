using HealthPrep.Application.Abstractions;
using HealthPrep.Infrastructure.Persistence;
using HealthPrep.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthPrep.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<HealthPrepDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Postgres connection string is missing.")));
        services.AddStackExchangeRedisCache(options => options.Configuration = configuration.GetConnectionString("Redis"));
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IInterviewQuestionProvider, RuleBasedInterviewQuestionProvider>();
        services.AddSingleton<IReportPdfRenderer, SimplePdfRenderer>();
        return services;
    }
}
