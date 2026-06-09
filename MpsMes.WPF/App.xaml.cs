using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MpsMes.Core.Interfaces;
using MpsMes.Infrastructure.Database;
using MpsMes.Infrastructure.Plc;
using MpsMes.Infrastructure.Vision;
using MpsMes.Services;
using MpsMes.ViewModels;
using MpsMes.WPF.Views;
using System.IO;
using System.Windows;

namespace MpsMes.WPF;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);

        // DB
        var connStr = config.GetConnectionString("MpsMesDB")!;
        services.AddSingleton(new DbConnectionFactory(connStr));
        services.AddSingleton<IProductionRepository, ProductionRepository>();
        services.AddSingleton<IVisionRepository,     VisionRepository>();
        services.AddSingleton<IEventLogRepository,   EventLogRepository>();

        // 인프라 서비스
        services.AddSingleton<IPlcService,    PlcService>();
        services.AddSingleton<IVisionService, VisionService>();

        // MES 데이터 저장 서비스
        services.AddSingleton<MesDataService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MonitorViewModel>();
        services.AddSingleton<VisionInspectionViewModel>();
        services.AddSingleton<ManualControlViewModel>();
        services.AddSingleton<QualityAnalysisViewModel>();
        services.AddSingleton<ProductionStatusViewModel>();
        services.AddSingleton<DbSearchViewModel>();
        services.AddSingleton<ProductionHistoryViewModel>();
        services.AddSingleton<EventLogViewModel>();

        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        // MesDataService 연결문자열 주입 및 시작
        var mesDataSvc = Services.GetRequiredService<MesDataService>();
        mesDataSvc.SetConnectionString(connStr);

        // 비전 검사 완료 시 MesDataService에 결과 전달
        var visionSvc = Services.GetRequiredService<IVisionService>();
        visionSvc.InspectionCompleted += result =>
            mesDataSvc.SetLastVisionResult(result);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is ServiceProvider sp)
            sp.Dispose();
        base.OnExit(e);
    }
}
