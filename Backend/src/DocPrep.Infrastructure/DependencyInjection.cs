using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DocPrep.Application.Abstractions;
using DocPrep.Infrastructure.Persistence;
using DocPrep.Infrastructure.Services;

namespace DocPrep.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocPrepInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DocPrepDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Postgres connection string is missing.")));
        services.AddStackExchangeRedisCache(o => o.Configuration = configuration.GetConnectionString("Redis"));
        services.AddScoped<IDocPrepStore, DocPrepStore>(); services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPatientDataProtector, PatientDataProtector>(); services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IPatientSessionStore, RedisPatientSessionStore>(); services.AddSingleton<INotificationSender, DemoNotificationSender>();
        services.AddSingleton<IInterviewQuestionProvider, AdaptiveQuestionProvider>(); services.AddSingleton<IReportRenderer, QuestReportRenderer>();
        services.AddHttpClient<ITranscriptionService, HttpTranscriptionService>();
        services.Configure<ElevenLabsOptions>(configuration.GetSection(ElevenLabsOptions.Section));
        services.AddHttpClient<IElevenLabsClient, ElevenLabsClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ElevenLabsOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IElevenLabsWebhookVerifier, ElevenLabsWebhookVerifier>();
        return services;
    }
}
