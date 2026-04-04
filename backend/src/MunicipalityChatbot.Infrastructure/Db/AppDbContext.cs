using Microsoft.EntityFrameworkCore;
using MunicipalityChatbot.Domain.Entities;
using MunicipalityChatbot.Infrastructure.Config;

namespace MunicipalityChatbot.Infrastructure.Db;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<EmployeeUser> Employees => Set<EmployeeUser>();
    public DbSet<Faq> Faqs => Set<Faq>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ApiDefinition> ApiDefinitions => Set<ApiDefinition>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<RoutingDecision> RoutingDecisions => Set<RoutingDecision>();
    public DbSet<ApiCallAudit> ApiCalls => Set<ApiCallAudit>();
    public DbSet<CrawledPage> CrawledPages => Set<CrawledPage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<EmployeeUser>(e =>
        {
            e.ToTable("employees");
            e.HasKey(x => x.EmployeeId);
            e.Property(x => x.EmployeeId).HasColumnName("employee_id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(64).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        model.Entity<Faq>(e =>
        {
            e.ToTable("faqs");
            e.HasKey(x => x.FaqId);
            e.Property(x => x.FaqId).HasColumnName("faq_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
            e.Property(x => x.Question).HasColumnName("question").IsRequired();
            e.Property(x => x.ShortDescription).HasColumnName("short_description");
            e.Property(x => x.Answer).HasColumnName("answer").IsRequired();
            e.Property(x => x.Language).HasColumnName("language").HasMaxLength(2).IsRequired();
            e.Property(x => x.TagsCsv).HasColumnName("tags_csv").HasMaxLength(512);
            e.Property(x => x.Department).HasColumnName("department").HasMaxLength(128);
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.Language, x.IsActive });
        });

        model.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.HasKey(x => x.DocId);
            e.Property(x => x.DocId).HasColumnName("doc_id");
            e.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(512).IsRequired();
            e.Property(x => x.FileType).HasColumnName("filetype").HasMaxLength(16).IsRequired();
            e.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(x => x.DetectedLanguage).HasColumnName("detected_language").HasMaxLength(2);
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        model.Entity<DocumentChunk>(e =>
        {
            e.ToTable("document_chunks");
            e.HasKey(x => x.ChunkId);
            e.Property(x => x.ChunkId).HasColumnName("chunk_id");
            e.Property(x => x.DocId).HasColumnName("doc_id");
            e.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(512).IsRequired();
            e.Property(x => x.FileType).HasColumnName("filetype").HasMaxLength(16).IsRequired();
            e.Property(x => x.Language).HasColumnName("language").HasMaxLength(2);
            e.Property(x => x.PageNumber).HasColumnName("page_number");
            e.Property(x => x.SheetName).HasColumnName("sheet_name").HasMaxLength(256);
            e.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            e.Property(x => x.Text).HasColumnName("text").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.DocId);
        });

        model.Entity<ApiDefinition>(e =>
        {
            e.ToTable("api_definitions");
            e.HasKey(x => x.ApiId);
            e.Property(x => x.ApiId).HasColumnName("api_id");
            e.Property(x => x.ApiName).HasColumnName("api_name").HasMaxLength(256).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.BaseUrl).HasColumnName("base_url").HasMaxLength(512).IsRequired();
            e.Property(x => x.Method).HasColumnName("method").HasMaxLength(16).IsRequired();
            e.Property(x => x.PathTemplate).HasColumnName("path_template").HasMaxLength(512).IsRequired();
            e.Property(x => x.AuthType).HasColumnName("auth_type").HasMaxLength(32).IsRequired();
            e.Property(x => x.AuthConfigJson).HasColumnName("auth_config_json").IsRequired();
            e.Property(x => x.HeadersTemplateJson).HasColumnName("headers_template_json").IsRequired();
            e.Property(x => x.QueryParamsSchemaJson).HasColumnName("query_params_schema_json").IsRequired();
            e.Property(x => x.BodySchemaJson).HasColumnName("body_schema_json").IsRequired();
            e.Property(x => x.BodyTemplateJson).HasColumnName("body_template_json");
            e.Property(x => x.ResponseHandlingNotes).HasColumnName("response_handling_notes");
            e.Property(x => x.AllowInChat).HasColumnName("allow_in_chat").IsRequired();
            e.Property(x => x.AllowlistedDomain).HasColumnName("allowlisted_domain").HasMaxLength(256).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.AllowInChat);
        });

        model.Entity<ChatSession>(e =>
        {
            e.ToTable("chat_sessions");
            e.HasKey(x => x.SessionId);
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(32).IsRequired();
            e.Property(x => x.WidgetOrigin).HasColumnName("widget_origin").HasMaxLength(512);
            e.Property(x => x.UserLang).HasColumnName("user_lang").HasMaxLength(2);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        model.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
            e.Property(x => x.Text).HasColumnName("text").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.SessionId);
        });

        model.Entity<RoutingDecision>(e =>
        {
            e.ToTable("routing_decisions");
            e.HasKey(x => x.DecisionId);
            e.Property(x => x.DecisionId).HasColumnName("decision_id");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.Route).HasColumnName("route").HasMaxLength(16).IsRequired();
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.SelectedFaqId).HasColumnName("selected_faq_id");
            e.Property(x => x.SelectedChunkIdsCsv).HasColumnName("selected_chunk_ids_csv");
            e.Property(x => x.SelectedApiId).HasColumnName("selected_api_id");
            e.Property(x => x.PlannerJson).HasColumnName("planner_json").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.Route);
        });

        model.Entity<ApiCallAudit>(e =>
        {
            e.ToTable("api_calls");
            e.HasKey(x => x.ApiCallId);
            e.Property(x => x.ApiCallId).HasColumnName("api_call_id");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.ApiId).HasColumnName("api_id");
            e.Property(x => x.RequestSummaryJson).HasColumnName("request_summary_json").IsRequired();
            e.Property(x => x.ResponseStatusCode).HasColumnName("response_status_code");
            e.Property(x => x.ResponseSummaryJson).HasColumnName("response_summary_json").IsRequired();
            e.Property(x => x.Error).HasColumnName("error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        model.Entity<CrawledPage>(e =>
        {
            e.ToTable("crawled_pages");
            e.HasKey(x => x.PageId);
            e.Property(x => x.PageId).HasColumnName("page_id");
            e.Property(x => x.Url).HasColumnName("url").HasMaxLength(1024).IsRequired();
            e.HasIndex(x => x.Url).IsUnique();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(512);
            e.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
            e.Property(x => x.ChunkCount).HasColumnName("chunk_count");
            e.Property(x => x.LastCrawledAt).HasColumnName("last_crawled_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}

