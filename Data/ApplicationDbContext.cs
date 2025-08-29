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
        public DbSet<StudyPlanEntity> StudyPlans { get; set; }
        public DbSet<LessonEntity> Lessons { get; set; }
        public DbSet<StudyPlanVersion> StudyPlanVersions => Set<StudyPlanVersion>();
        public DbSet<StudyPlanDraft> StudyPlanDrafts => Set<StudyPlanDraft>();
        public DbSet<LessonVersion> LessonVersions => Set<LessonVersion>();
        public DbSet<LessonDraft> LessonDrafts => Set<LessonDraft>();
        public DbSet<StudyGroupStudyPlan> StudyGroupStudyPlans => Set<StudyGroupStudyPlan>();
        public DbSet<StudyPlanUserRole> StudyPlanUserRoles => Set<StudyPlanUserRole>();
        public DbSet<StudyGroupUserRole> StudyGroupUserRoles => Set<StudyGroupUserRole>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Tags>().ToTable("Tags");
            builder.Entity<TypesOfTags>().ToTable("TypesOfTags");
            builder.Entity<TagTypes>().ToTable("TagTypes");
            builder.Entity<KnowledgeNode>().ToTable("KnowledgeNodes");
            builder.Entity<Resource>().ToTable("Resources");

            // StudyPlans indexes & privacy (optional)
            builder.Entity<StudyPlanEntity>(eb =>
            {
                eb.HasIndex(x => x.CreatorId);
                eb.Property<string>("Privacy").HasMaxLength(16).HasDefaultValue("private");
                eb.HasIndex("Privacy");
            });

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

            // Versions & Drafts (StudyPlan/Lesson)
            builder.Entity<StudyPlanVersion>(eb =>
            {
                eb.ToTable("StudyPlanVersions");
                eb.HasKey(x => x.Id);

                eb.HasIndex(x => new { x.StudyPlanId, x.VersionNumber })
                  .IsUnique();

                eb.Property(x => x.CreatedDate)
                  .HasDefaultValueSql("SYSUTCDATETIME()");

                eb.Property(x => x.RowVersion).IsRowVersion();

                // 外键到 StudyPlanEntity
                eb.HasOne<StudyPlanEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyPlanId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<LessonVersion>(eb =>
            {
                eb.ToTable("LessonVersions");
                eb.HasKey(x => x.Id);

                eb.HasIndex(x => new { x.LessonId, x.VersionNumber })
                  .IsUnique();

                eb.Property(x => x.CreatedDate)
                  .HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.RowVersion).IsRowVersion();

                // 外键到 LessonEntity
                eb.HasOne<LessonEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.LessonId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<StudyPlanDraft>(eb =>
            {
                eb.ToTable("StudyPlanDrafts");
                eb.HasKey(x => x.Id);
                eb.HasIndex(x => new { x.StudyPlanId, x.DraftNumber }).IsUnique();

                eb.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.RowVersion).IsRowVersion();

                // 改为可空
                eb.Property(x => x.DraftStatus)
                  .HasConversion<string>()
                  .HasMaxLength(16)
                  .IsRequired(false);

                eb.HasOne<StudyPlanEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyPlanId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<LessonDraft>(eb =>
            {
                eb.ToTable("LessonDrafts");
                eb.HasKey(x => x.Id);
                eb.HasIndex(x => new { x.LessonId, x.DraftNumber }).IsUnique();

                eb.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.RowVersion).IsRowVersion();

                eb.Property(x => x.DraftStatus)
                  .HasConversion<string>()
                  .HasMaxLength(16)
                  .IsRequired(false); // 可空

                eb.HasOne<LessonEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.LessonId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            // StudyGroupStudyPlans
            builder.Entity<StudyGroupStudyPlan>(eb =>
            {
                eb.ToTable("StudyGroupStudyPlans");
                eb.HasKey(x => x.Id);
                eb.HasIndex(x => new { x.StudyGroupId, x.StudyPlanId }).IsUnique();
                eb.Property(x => x.Permission).HasMaxLength(16).IsRequired();
                eb.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasCheckConstraint("CK_SGSP_Permission", "Permission IN ('view','comment','edit','admin')");
                eb.HasOne<StudyGroupEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyGroupId)
                  .OnDelete(DeleteBehavior.Cascade);
                eb.HasOne<StudyPlanEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyPlanId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            // StudyPlanUserRoles
            builder.Entity<StudyPlanUserRole>(b =>
            {
                b.ToTable("StudyPlanUserRoles");
                b.HasKey(x => new { x.PlanId, x.UserId });

                b.Property(x => x.Role).HasConversion<byte>();
                b.Property(x => x.UserId)
                 .HasColumnType("nvarchar(450)")
                 .UseCollation("SQL_Latin1_General_CP1_CI_AS");

                b.HasOne(x => x.Plan)
                 .WithMany(p => p.UserRoles)
                 .HasForeignKey(x => x.PlanId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(x => x.UserId);
            });

            // StudyGroupUserRoles
            builder.Entity<StudyGroupUserRole>(b =>
            {
                b.ToTable("StudyGroupUserRoles");
                b.HasKey(x => new { x.GroupId, x.UserId });

                b.Property(x => x.Role).HasConversion<byte>();
                b.Property(x => x.UserId)
                 .HasColumnType("nvarchar(450)")
                 .UseCollation("SQL_Latin1_General_CP1_CI_AS");

                b.HasOne(x => x.Group)
                 .WithMany(g => g.UserRoles)
                 .HasForeignKey(x => x.GroupId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(x => x.UserId);
            });
        }
    }
}
