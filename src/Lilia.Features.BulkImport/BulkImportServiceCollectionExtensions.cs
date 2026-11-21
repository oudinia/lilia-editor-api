using Lilia.Api.Features.BulkImport.Services;

namespace Lilia.Api.Features.BulkImport;

public static class BulkImportServiceCollectionExtensions
{
    /// <summary>
    /// The bulk import slice's services. Lilia.Api adds <c>IPdfImportPipeline</c> (the single PDF import's steps), maps
    /// the hub at <c>/hubs/bulk-import</c>, and gives Wolverine this assembly and <see cref="BulkImportMessaging"/>.
    /// </summary>
    public static IServiceCollection AddBulkImport(this IServiceCollection services)
    {
        // The BulkImport settings, read once from the app's configuration when first needed.
        services.AddSingleton(sp => BulkImportMessaging.Read(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IPdfSplitter, PdfSplitter>();
        services.AddSingleton<IBulkImportNotifier, BulkImportNotifier>();
        services.AddScoped<IBulkImportQueue, WolverineBulkImportQueue>();
        services.AddScoped<BulkImportProcessor>();
        services.AddScoped<BulkImportService>();
        services.AddHostedService<BulkImportSweeper>();
        return services;
    }
}
