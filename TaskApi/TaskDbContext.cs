using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace TaskApi
{
    public class TaskDbContext : DbContext
    {
        public TaskDbContext(DbContextOptions<TaskDbContext> options) : base(options) { }

        public DbSet<DbTaskItem> Tasks => Set<DbTaskItem>();
    }

    [Table("tasks")]
    public class DbTaskItem
    {
        [Key]
        [Column("id")]
        public Guid Id { get; set; }

        [Column("title")]
        [NotNull]
        [MaxLength(200)]
        public string Title { get; set; }

        [Column("iscompleted")]
        public bool IsCompleted { get; set; } = false;

        [Column("createdat")]
        [NotNull]
        public DateTimeOffset CreatedAt { get; set; }

        [Column("completedat")]
        public DateTimeOffset? CompletedAt { get; set; }

        [Column("priority")]
        public Priority Priority { get; set; }

        [Column("xmin", TypeName = "xid")]
        [ConcurrencyCheck]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public uint Version { get; set; }
    }
}
