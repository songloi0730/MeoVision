using System;
using MeoVision.Application.Alarms;
using MeoVision.Application.Archiving;
using MeoVision.Application.Auth;
using MeoVision.Application.Recipes;
using MeoVision.Application.Results;
using MeoVision.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MeoVision.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMeoVisionApplication(
        this IServiceCollection services, MeoVisionHostOptions options)
    {
        Guard.ThrowIfNull(services, nameof(services));
        Guard.ThrowIfNull(options, nameof(options));

        services.AddSingleton(options);
        services.AddSingleton<IRecipeRepository, JsonFileRecipeRepository>();
        services.AddSingleton<IInspectionResultStore, InMemoryInspectionResultStore>();
        services.AddSingleton<IAlarmService, AlarmService>();
        services.AddSingleton<INgImageArchiver, FileSystemNgImageArchiver>();
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<InspectionCycleService>();

        // IVisionEngine KHÔNG đăng ký ở đây — Application không được biết đó là Simulated hay
        // Cognex thật. Mỗi host tự thêm đúng 1 dòng đăng ký (xem MeoVision.WorkStation.Demo/Program.cs).
        return services;
    }
}
