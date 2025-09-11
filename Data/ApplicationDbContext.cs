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
        public DbSet<StudyPlanCohort> Cohorts => Set<StudyPlanCohort>();
        // L10n domain
        public DbSet<Sciencetopia.Models.L10n.L10nSet> L10nSets => Set<Sciencetopia.Models.L10n.L10nSet>();
        public DbSet<Sciencetopia.Models.L10n.L10nItem> L10nItems => Set<Sciencetopia.Models.L10n.L10nItem>();
        public DbSet<Sciencetopia.Models.L10n.NodeL10nSet> NodeL10nSets => Set<Sciencetopia.Models.L10n.NodeL10nSet>();
        public DbSet<Sciencetopia.Models.L10n.L10nSetItem> L10nSetItems => Set<Sciencetopia.Models.L10n.L10nSetItem>();
        public DbSet<Sciencetopia.Models.L10n.TagL10nSet> TagL10nSets => Set<Sciencetopia.Models.L10n.TagL10nSet>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Tags>().ToTable("Tags");
            builder.Entity<TypesOfTags>().ToTable("TypesOfTags");
            builder.Entity<TagTypes>().ToTable("TagTypes");
            builder.Entity<KnowledgeNode>().ToTable("KnowledgeNodes");
            builder.Entity<Resource>().ToTable("Resources");
            
            // L10n tables
            builder.Entity<Sciencetopia.Models.L10n.L10nSet>().ToTable("L10nSets");
            builder.Entity<Sciencetopia.Models.L10n.L10nItem>().ToTable("L10nItems");
            builder.Entity<Sciencetopia.Models.L10n.NodeL10nSet>().ToTable("NodeL10nSets");
            builder.Entity<Sciencetopia.Models.L10n.L10nSetItem>().ToTable("L10nSetItems");
            builder.Entity<Sciencetopia.Models.L10n.TagL10nSet>().ToTable("TagL10nSets");

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

            // Translations removed; L10n domain replaces them

            // L10n Fluent mappings
            builder.Entity<Sciencetopia.Models.L10n.L10nSet>(eb =>
            {
                eb.HasKey(x => x.L10nSetId);
                eb.Property(x => x.Scope).HasMaxLength(50).IsRequired();
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => x.Scope).HasDatabaseName("IX_L10nSets_Scope");
            });

            builder.Entity<Sciencetopia.Models.L10n.L10nItem>(eb =>
            {
                eb.HasKey(x => x.L10nItemId);
                eb.Property(x => x.FieldKey).HasMaxLength(32).HasDefaultValue("name");
                eb.Property(x => x.LangCode).HasMaxLength(10);
                eb.Property(x => x.ScriptCode).HasMaxLength(10);
                eb.Property(x => x.Text).HasMaxLength(200);
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => new { x.FieldKey, x.LangCode }).HasDatabaseName("IX_L10nItems_Field_Lang");
                eb.HasIndex(x => x.Text).HasDatabaseName("IX_L10nItems_Text");
            });

            builder.Entity<Sciencetopia.Models.L10n.NodeL10nSet>(eb =>
            {
                eb.HasKey(x => new { x.NodeId, x.L10nSetId, x.Relation });
                eb.Property(x => x.Relation).HasDefaultValue((byte)0);
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => x.NodeId).HasDatabaseName("IX_NodeL10nSets_Node");
                eb.HasIndex(x => x.L10nSetId).HasDatabaseName("IX_NodeL10nSets_Set");
                eb.HasOne<KnowledgeNode>()
                    .WithMany()
                    .HasForeignKey(x => x.NodeId)
                    .OnDelete(DeleteBehavior.Cascade);
                eb.HasOne<Sciencetopia.Models.L10n.L10nSet>()
                    .WithMany()
                    .HasForeignKey(x => x.L10nSetId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Sciencetopia.Models.L10n.L10nSetItem>(eb =>
            {
                eb.HasKey(x => new { x.L10nSetId, x.L10nItemId });
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => x.L10nItemId).HasDatabaseName("IX_L10nSetItems_Item");
                eb.HasOne<Sciencetopia.Models.L10n.L10nSet>()
                    .WithMany()
                    .HasForeignKey(x => x.L10nSetId)
                    .OnDelete(DeleteBehavior.Cascade);
                eb.HasOne<Sciencetopia.Models.L10n.L10nItem>()
                    .WithMany()
                    .HasForeignKey(x => x.L10nItemId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // KnowledgeNodes: DefaultL10nSetId FK with filtered unique index
            builder.Entity<KnowledgeNode>(eb =>
            {
                eb.HasIndex(x => x.DefaultL10nSetId)
                  .HasDatabaseName("UX_KN_DefaultL10nSet")
                  .IsUnique()
                  .HasFilter("[DefaultL10nSetId] IS NOT NULL");
                eb.HasOne<Sciencetopia.Models.L10n.L10nSet>()
                  .WithMany()
                  .HasForeignKey(x => x.DefaultL10nSetId);
            });

            // TagL10nSets
            builder.Entity<Sciencetopia.Models.L10n.TagL10nSet>(eb =>
            {
                eb.HasKey(x => new { x.TagId, x.L10nSetId, x.Relation });
                eb.Property(x => x.Relation).HasDefaultValue((byte)0);
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => x.TagId).HasDatabaseName("IX_TagL10nSets_Tag");
                eb.HasIndex(x => x.L10nSetId).HasDatabaseName("IX_TagL10nSets_Set");
                eb.HasOne<Tags>()
                    .WithMany()
                    .HasForeignKey(x => x.TagId)
                    .OnDelete(DeleteBehavior.Cascade);
                eb.HasOne<Sciencetopia.Models.L10n.L10nSet>()
                    .WithMany()
                    .HasForeignKey(x => x.L10nSetId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Tags: DefaultL10nSetId
            builder.Entity<Tags>(eb =>
            {
                eb.HasIndex(x => x.DefaultL10nSetId)
                  .HasDatabaseName("UX_Tag_DefaultL10nSet")
                  .IsUnique()
                  .HasFilter("[DefaultL10nSetId] IS NOT NULL");
                eb.HasOne<Sciencetopia.Models.L10n.L10nSet>()
                  .WithMany()
                  .HasForeignKey(x => x.DefaultL10nSetId);
            });

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

            // Cohorts (StudyPlanCohort)
            builder.Entity<StudyPlanCohort>(b =>
            {
                b.ToTable("Cohorts");
                b.HasKey(x => x.Id);
                b.Property(x => x.Title).HasMaxLength(200);
                b.Property(x => x.Visibility).HasMaxLength(20).HasDefaultValue("private");
                b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                b.Property(x => x.CreatedBy)
                  .HasMaxLength(450)
                  .HasColumnType("nvarchar(450)")
                  .UseCollation("SQL_Latin1_General_CP1_CI_AS");

                b.HasIndex(x => x.StudyPlanId);
                b.HasIndex(x => x.StudyGroupId);
                b.Property(x => x.EnrollMode).HasConversion<string>().HasMaxLength(16).HasDefaultValue(Sciencetopia.Models.Enums.CohortEnrollMode.OptIn);
                b.Property(x => x.MembersCount).HasDefaultValue(0);

                b.HasOne<StudyPlanEntity>(x => x.Plan)
                 .WithMany()
                 .HasForeignKey(x => x.StudyPlanId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne<StudyGroupEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyGroupId)
                  .OnDelete(DeleteBehavior.SetNull);
            });

            // CohortMembers removed: membership tracked in graph (Neo4j)
        }
    }
}
