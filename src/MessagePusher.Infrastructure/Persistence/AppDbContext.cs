using MessagePusher.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(12);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Password).HasColumnName("password").IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(20);
            e.HasIndex(x => x.DisplayName);
            e.Property(x => x.Role).HasColumnName("role").HasDefaultValue(1);
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue(1);
            e.Property(x => x.Token).HasColumnName("token");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(50);
            e.HasIndex(x => x.Email);
            e.Property(x => x.GitHubId).HasColumnName("github_id");
            e.HasIndex(x => x.GitHubId);
            e.Property(x => x.WeChatId).HasColumnName("wechat_id");
            e.HasIndex(x => x.WeChatId);
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.SendEmailToOthers).HasColumnName("send_email_to_others").HasDefaultValue(0);
            e.Property(x => x.SaveMessageToDatabase).HasColumnName("save_message_to_database").HasDefaultValue(0);
            e.Ignore(x => x.VerificationCode);
        });

        b.Entity<Channel>(e =>
        {
            e.ToTable("channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Type).HasColumnName("type").HasColumnType("varchar(32)");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Name).HasColumnName("name").HasColumnType("varchar(32)");
            e.HasIndex(x => new { x.Name, x.UserId }).IsUnique().HasDatabaseName("name_user_id");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue(1);
            e.Property(x => x.Secret).HasColumnName("secret");
            e.HasIndex(x => x.Secret);
            e.Property(x => x.AppId).HasColumnName("app_id");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.Other).HasColumnName("other");
            e.Property(x => x.CreatedTime).HasColumnName("created_time").HasColumnType("bigint");
            e.Property(x => x.Token).HasColumnName("token");
        });

        b.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.Url).HasColumnName("url");
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.Timestamp).HasColumnName("timestamp").HasColumnType("bigint");
            e.Property(x => x.Link).HasColumnName("link");
            e.HasIndex(x => x.Link).IsUnique();
            e.Property(x => x.To).HasColumnName("to");
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue(0);
            e.HasIndex(x => x.Status);
            e.Property(x => x.RenderMode).HasColumnName("render_mode");
            e.Ignore(x => x.Token);
            e.Ignore(x => x.HtmlContent);
            e.Ignore(x => x.Async);
            e.Ignore(x => x.OpenId);
            e.Ignore(x => x.Desp);
            e.Ignore(x => x.Short);
        });

        b.Entity<Option>(e =>
        {
            e.ToTable("options");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Value).HasColumnName("value");
        });

        b.Entity<Webhook>(e =>
        {
            e.ToTable("webhooks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Name).HasColumnName("name").HasColumnType("varchar(32)");
            e.HasIndex(x => x.Name);
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue(1);
            e.Property(x => x.Link).HasColumnName("link").HasColumnType("char(32)");
            e.HasIndex(x => x.Link).IsUnique();
            e.Property(x => x.CreatedTime).HasColumnName("created_time").HasColumnType("bigint");
            e.Property(x => x.ExtractRule).HasColumnName("extract_rule").IsRequired();
            e.Property(x => x.ConstructRule).HasColumnName("construct_rule").IsRequired();
            e.Property(x => x.Channel).HasColumnName("channel").HasColumnType("varchar(32)").IsRequired();
        });
    }
}
