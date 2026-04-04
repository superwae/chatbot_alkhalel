using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MunicipalityChatbot.Application.Abstractions;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Config;
using MunicipalityChatbot.Infrastructure.Db;
using MunicipalityChatbot.Infrastructure.Security;

namespace MunicipalityChatbot.Infrastructure.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger, bool isDevelopment, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();

        var dbInit = services.GetService<DatabaseInitOptions>() ?? new DatabaseInitOptions();
        var seedAdmin = services.GetService<AdminSeedOptions>() ?? new AdminSeedOptions();
        var faqSeed = services.GetService<FaqSeedOptions>() ?? new FaqSeedOptions();
        var apiSeed = services.GetService<ApiSeedOptions>() ?? new ApiSeedOptions();

        var shouldMigrate = dbInit.AutoMigrate || isDevelopment;
        if (shouldMigrate)
        {
            logger.LogInformation("Database init: running migrations (AutoMigrate={AutoMigrate}, IsDevelopment={IsDev})", dbInit.AutoMigrate, isDevelopment);
            await db.Database.MigrateAsync(ct);
        }

        await EnsureAdminAsync(db, services, logger, seedAdmin, isDevelopment, ct);
        await SeedFaqsAsync(db, services, logger, faqSeed, ct);
        await SeedApisAsync(db, services, logger, apiSeed, ct);
    }

    private static async Task EnsureAdminAsync(
        AppDbContext db,
        IServiceProvider services,
        ILogger logger,
        AdminSeedOptions seed,
        bool isDevelopment,
        CancellationToken ct
    )
    {
        if (!seed.Enabled)
        {
            logger.LogInformation("Admin seed: disabled.");
            return;
        }

        var username = (seed.Username ?? "admin").Trim();
        if (username.Length == 0) username = "admin";

        // Security guard: only use a default password in Development.
        var password = seed.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            if (!isDevelopment)
            {
                logger.LogWarning("Admin seed: enabled but SEED:ADMIN:PASSWORD is empty; skipping admin creation. Provide SEED__ADMIN__PASSWORD to seed.");
                return;
            }

            password = "ChangeMeNow!";
            logger.LogWarning("Admin seed: SEED:ADMIN:PASSWORD is empty; using Development default password. CHANGE IT before production.");
        }

        var exists = await db.Employees.AnyAsync(e => e.Username == username, ct);
        if (exists)
        {
            logger.LogInformation("Admin seed: user '{Username}' already exists; skipping.", username);
            return;
        }

        var hasher = services.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;

        var user = new EmployeeUser
        {
            EmployeeId = Guid.NewGuid(),
            Username = username,
            PasswordHash = hasher.Hash(password),
            Role = string.IsNullOrWhiteSpace(seed.Role) ? EmployeeRoles.EmployeeAdmin : seed.Role.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Employees.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Admin seed: created initial admin user '{Username}' with role '{Role}'.", user.Username, user.Role);
        }
        catch (DbUpdateException ex)
        {
            // If multiple instances race, unique constraint may fail. Treat as success.
            logger.LogWarning(ex, "Admin seed: could not create admin user (possible race).");
        }
    }

    private sealed record FaqSeedItem(
        string Title,
        string Question,
        string ShortDescription,
        string Answer,
        string Language,
        string Tags,
        string Department
    );

    private static async Task SeedFaqsAsync(
        AppDbContext db,
        IServiceProvider services,
        ILogger logger,
        FaqSeedOptions options,
        CancellationToken ct
    )
    {
        if (!options.Enabled)
        {
            logger.LogInformation("FAQ seed: disabled.");
            return;
        }

        // Check if any FAQs already exist
        var existingCount = await db.Faqs.CountAsync(ct);
        if (existingCount > 0)
        {
            logger.LogInformation("FAQ seed: {Count} FAQs already exist; skipping seed.", existingCount);
            return;
        }

        // Load FAQ data from external file or embedded resource
        string json;
        if (!string.IsNullOrWhiteSpace(options.ExternalFilePath) && File.Exists(options.ExternalFilePath))
        {
            logger.LogInformation("FAQ seed: loading from external file {Path}", options.ExternalFilePath);
            json = await File.ReadAllTextAsync(options.ExternalFilePath, ct);
        }
        else
        {
            logger.LogInformation("FAQ seed: loading from embedded resource");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MunicipalityChatbot.Infrastructure.Config.FaqSeedData.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                logger.LogWarning("FAQ seed: embedded resource not found; skipping.");
                return;
            }
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync(ct);
        }

        var seedItems = JsonSerializer.Deserialize<List<FaqSeedItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (seedItems == null || seedItems.Count == 0)
        {
            logger.LogWarning("FAQ seed: no items found in seed data; skipping.");
            return;
        }

        var faqRepo = services.GetRequiredService<IFaqRepository>();
        var embeddings = services.GetRequiredService<IEmbeddingService>();
        var qdrant = services.GetRequiredService<IQdrantService>();
        var qdrantOptions = services.GetService<QdrantOptions>() ?? new QdrantOptions();
        var now = DateTimeOffset.UtcNow;

        // Ensure Qdrant collection exists before seeding
        try
        {
            await qdrant.EnsureCollectionAsync(qdrantOptions.VectorSize, ct);
            logger.LogInformation("FAQ seed: Qdrant collection ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FAQ seed: could not ensure Qdrant collection; skipping FAQ embedding.");
        }

        var seededCount = 0;
        foreach (var item in seedItems)
        {
            try
            {
                var faq = new Faq
                {
                    FaqId = Guid.NewGuid(),
                    Title = item.Title?.Trim() ?? "",
                    Question = item.Question?.Trim() ?? "",
                    ShortDescription = item.ShortDescription?.Trim() ?? "",
                    Answer = item.Answer?.Trim() ?? "",
                    Language = (item.Language ?? "EN").Trim().ToUpperInvariant(),
                    TagsCsv = item.Tags?.Trim() ?? "",
                    Department = item.Department?.Trim() ?? "",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                // Validate required fields
                if (string.IsNullOrWhiteSpace(faq.Title) ||
                    string.IsNullOrWhiteSpace(faq.Question) ||
                    string.IsNullOrWhiteSpace(faq.Answer))
                {
                    logger.LogWarning("FAQ seed: skipping invalid item (missing required fields)");
                    continue;
                }

                await faqRepo.UpsertAsync(faq, ct);

                // Embed and upsert to Qdrant
                var embedText = $"{faq.Title}\n{faq.Question}\n{faq.ShortDescription}\n{faq.Answer}";
                var vec = await embeddings.EmbedAsync(embedText, ct);
                await qdrant.UpsertFaqAsync(faq, vec, ct);

                seededCount++;
                logger.LogDebug("FAQ seed: created FAQ '{Title}' ({Language})", faq.Title, faq.Language);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FAQ seed: failed to seed FAQ '{Title}'", item.Title);
            }
        }

        logger.LogInformation("FAQ seed: seeded {Count} FAQs successfully.", seededCount);
    }

    private sealed record ApiSeedItem(
        string ApiName,
        string Description,
        string BaseUrl,
        string Method,
        string PathTemplate,
        string AuthType,
        string? AuthConfigJson,
        string? HeadersTemplateJson,
        string? QueryParamsSchemaJson,
        string? BodySchemaJson,
        string? BodyTemplateJson,
        string? ResponseHandlingNotes
    );

    private static async Task SeedApisAsync(
        AppDbContext db,
        IServiceProvider services,
        ILogger logger,
        ApiSeedOptions options,
        CancellationToken ct
    )
    {
        if (!options.Enabled)
        {
            logger.LogInformation("API seed: disabled.");
            return;
        }

        // Check if any APIs already exist
        var existingCount = await db.ApiDefinitions.CountAsync(ct);
        if (existingCount > 0)
        {
            logger.LogInformation("API seed: {Count} APIs already exist; skipping seed.", existingCount);
            return;
        }

        // Load API data from external file or embedded resource
        string json;
        if (!string.IsNullOrWhiteSpace(options.ExternalFilePath) && File.Exists(options.ExternalFilePath))
        {
            logger.LogInformation("API seed: loading from external file {Path}", options.ExternalFilePath);
            json = await File.ReadAllTextAsync(options.ExternalFilePath, ct);
        }
        else
        {
            logger.LogInformation("API seed: loading from embedded resource");
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MunicipalityChatbot.Infrastructure.Config.ApiSeedData.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                logger.LogWarning("API seed: embedded resource not found; skipping.");
                return;
            }
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync(ct);
        }

        var seedItems = JsonSerializer.Deserialize<List<ApiSeedItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (seedItems == null || seedItems.Count == 0)
        {
            logger.LogWarning("API seed: no items found in seed data; skipping.");
            return;
        }

        var apiRepo = services.GetRequiredService<IApiDefinitionRepository>();
        var now = DateTimeOffset.UtcNow;

        var seededCount = 0;
        foreach (var item in seedItems)
        {
            try
            {
                var api = new ApiDefinition
                {
                    ApiId = Guid.NewGuid(),
                    ApiName = item.ApiName?.Trim() ?? "",
                    Description = item.Description?.Trim() ?? "",
                    BaseUrl = item.BaseUrl?.Trim() ?? "",
                    Method = (item.Method ?? "GET").Trim().ToUpperInvariant(),
                    PathTemplate = item.PathTemplate?.Trim() ?? "",
                    AuthType = item.AuthType?.Trim() ?? "None",
                    AuthConfigJson = item.AuthConfigJson ?? "{}",
                    HeadersTemplateJson = item.HeadersTemplateJson ?? "{}",
                    QueryParamsSchemaJson = item.QueryParamsSchemaJson ?? "{}",
                    BodySchemaJson = item.BodySchemaJson ?? "{}",
                    BodyTemplateJson = item.BodyTemplateJson,
                    ResponseHandlingNotes = item.ResponseHandlingNotes ?? "",
                    AllowInChat = true,
                    AllowlistedDomain = new Uri(item.BaseUrl ?? "http://localhost").Host,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                // Validate required fields
                if (string.IsNullOrWhiteSpace(api.ApiName) ||
                    string.IsNullOrWhiteSpace(api.BaseUrl))
                {
                    logger.LogWarning("API seed: skipping invalid item (missing required fields)");
                    continue;
                }

                await apiRepo.UpsertAsync(api, ct);

                seededCount++;
                logger.LogDebug("API seed: created API '{ApiName}'", api.ApiName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "API seed: failed to seed API '{ApiName}'", item.ApiName);
            }
        }

        logger.LogInformation("API seed: seeded {Count} APIs successfully.", seededCount);
    }
}

