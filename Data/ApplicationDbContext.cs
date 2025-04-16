using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;

namespace Sciencetopia.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets for messaging and user activity
        public DbSet<Message> Messages { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<VisitLog> VisitLogs { get; set; } // New VisitLog DbSet
        // Add the DailySummaries DbSet
        public DbSet<DailySummary> DailySummaries { get; set; }
        // Add the KnowledgeNodes DbSet
        public DbSet<KnowledgeNode> KnowledgeNodes { get; set; }
        public DbSet<KnowledgeNodeDraft> KnowledgeNodeDrafts { get; set; } // New KnowledgeNodeDraft DbSet
        public DbSet<KnowledgeNodeVersion> KnowledgeNodeVersions { get; set; } // New KnowledgeNodeVersion DbSet
        public DbSet<TypesOfTags> TypesOfTags { get; set; }
        public DbSet<TagTypes> TagTypes { get; set; }
        // Add the Tags DbSet
        public DbSet<Tags> Tags { get; set; }
        public DbSet<TagDraft> TagDrafts { get; set; } // New TagDraft DbSet
        public DbSet<TagVersion> TagVersions { get; set; } // New TagVersion DbSet
        public DbSet<Favorite> Favorites { get; set; } // New Favorite DbSet
        public DbSet<StudyGroupEntity> StudyGroups { get; set; } // New StudyGroupEntity DbSet
        public DbSet<Resource> Resources { get; set; } // New Resource DbSet

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Tags>().ToTable("Tags");
            builder.Entity<TypesOfTags>().ToTable("TypesOfTags");
            builder.Entity<TagTypes>().ToTable("TagTypes");
            builder.Entity<KnowledgeNode>().ToTable("KnowledgeNodes");
            builder.Entity<Resource>().ToTable("Resources");

            // builder.Ignore<KnowledgeNode>();
            // builder.Ignore<TagTypes>();
            // builder.Ignore<Tags>();
            // builder.Ignore<TypesOfTags>();

            // Configure relationships for the Message model
            builder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            builder.Entity<Message>()
                .HasOne(m => m.Receiver)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure VisitLog relationships (if needed)
            builder.Entity<VisitLog>()
                .HasKey(v => v.Id); // Primary Key

            builder.Entity<VisitLog>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Restrict); // If visits are linked to users

            // // Define Composite Primary Key for TagTypes (No Navigation Properties)
            // builder.Entity<TagTypes>()
            //     .HasKey(tt => new { tt.TagId, tt.TypeId });  // Define composite key

            // Configure Favorites table
            builder.Entity<Favorite>(entity =>
            {
                entity.HasKey(f => f.Id);

                entity.Property(f => f.UserId)
                    .IsRequired()
                    .HasColumnType("nvarchar(450)")
                    .UseCollation("SQL_Latin1_General_CP1_CI_AS"); // Ensure UserId is required and has the same type as IdentityUser's Id

                entity.Property(f => f.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(f => f.Type)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(f => f.CreatedAt)
                    .IsRequired();

                entity.HasOne(f => f.User)
                    .WithMany() // 可替换为 .WithMany(u => u.Favorites) 若你在 ApplicationUser 添加了导航属性
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TagDraft>()
                .HasOne<Tags>()
                .WithMany()
                .HasForeignKey(d => d.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<KnowledgeNodeDraft>()
                .HasOne<KnowledgeNode>()
                .WithMany()
                .HasForeignKey(d => d.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
