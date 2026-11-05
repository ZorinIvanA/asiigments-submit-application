using LabsApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabsApp.Infrastructure.Ef;

/// <summary>
/// DbContext приложения (итерация «БД»): маппинг доменных сущностей на готовую
/// схему PostgreSQL (src/api/db/02_schema.sql — источник истины; миграции EF
/// не используются). Таблицы и колонки — snake_case,FK-каскады выполняет БД,
/// поэтому связи в модели не объявляются: агрегаты загружаются независимо.
/// Чтения выполняются AsNoTracking — репозитории отдают копии-снимки,
/// как и in-memory реализация.
/// </summary>
public sealed class LabsAppDbContext(DbContextOptions<LabsAppDbContext> options) : DbContext(options)
{
    public DbSet<Group> Groups => Set<Group>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Lab> Labs => Set<Lab>();

    public DbSet<Submission> Submissions => Set<Submission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Group>(entity =>
        {
            entity.ToTable("groups");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Id).HasColumnName("id");
            entity.Property(g => g.Name).HasColumnName("name");
            entity.Property(g => g.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.Login).HasColumnName("login");
            entity.Property(u => u.Email).HasColumnName("email");
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash");
            entity.Property(u => u.FullName).HasColumnName("full_name");
            entity.Property(u => u.Role).HasColumnName("role");
            entity.Property(u => u.GroupId).HasColumnName("group_id");
            entity.Property(u => u.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Lab>(entity =>
        {
            entity.ToTable("labs");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).HasColumnName("id");
            entity.Property(l => l.Semester).HasColumnName("semester");
            entity.Property(l => l.Number).HasColumnName("number");
            entity.Property(l => l.Content).HasColumnName("content");
            entity.Property(l => l.AssignmentUrl).HasColumnName("assignment_url");
            entity.Property(l => l.DefenseRequired).HasColumnName("defense_required");
            entity.Property(l => l.CreatedAt).HasColumnName("created_at");
            entity.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Submission>(entity =>
        {
            entity.ToTable("submissions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("id");
            entity.Property(s => s.StudentId).HasColumnName("student_id");
            entity.Property(s => s.LabId).HasColumnName("lab_id");
            entity.Property(s => s.SubmitDate).HasColumnName("submit_date");
            entity.Property(s => s.DefenseDate).HasColumnName("defense_date");
            entity.Property(s => s.UpdatedBy).HasColumnName("updated_by");
            entity.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        });

    }
}
