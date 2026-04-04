using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Api.BackgroundServices;

public sealed class WebsiteCrawlBackgroundService(
    IServiceProvider services,
    WebsiteCrawlOptions options,
    ILogger<WebsiteCrawlBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Website crawl background service is disabled");
            return;
        }

        logger.LogInformation("Website crawl background service started. Interval: {Hours} hours, URL: {Url}",
            options.IntervalHours, options.Url);

        // Wait a bit before first crawl to let the app start up
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Starting scheduled website crawl for {Url}", options.Url);

                using var scope = services.CreateScope();
                var crawler = scope.ServiceProvider.GetRequiredService<IWebsiteCrawlerService>();

                var result = await crawler.CrawlWebsiteAsync(options.Url, stoppingToken);

                logger.LogInformation(
                    "Scheduled crawl complete: {Processed} pages, {Skipped} skipped, {Chunks} chunks, {Errors} errors",
                    result.PagesProcessed, result.PagesSkipped, result.ChunksCreated, result.Errors.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled website crawl failed");
            }

            // Wait for next interval
            await Task.Delay(TimeSpan.FromHours(options.IntervalHours), stoppingToken);
        }

        logger.LogInformation("Website crawl background service stopped");
    }
}
