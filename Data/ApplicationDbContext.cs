using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;

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
        public DbSet<TypesOfTags> TypesOfTags { get; set; }
        public DbSet<TagTypes> TagTypes { get; set; }
        // Add the Tags DbSet
        public DbSet<Tags> Tags { get; set; }
        public DbSet<Favorite> Favorites { get; set; } // New Favorite DbSet
        public DbSet<GroupEntity> Groups => Set<GroupEntity>(); // Groups ancestor
        public DbSet<StudyGroupEntity> StudyGroups { get; set; } // New StudyGroupEntity DbSet
        public DbSet<CohortEntity> Cohorts => Set<CohortEntity>();
        public DbSet<Resource> Resources { get; set; } // New Resource DbSet
        public DbSet<StudyPlanEntity> StudyPlans { get; set; }
        public DbSet<LessonEntity> Lessons { get; set; }
        public DbSet<StudyGroupStudyPlan> StudyGroupStudyPlans => Set<StudyGroupStudyPlan>();
        public DbSet<GroupPlanEnrollment> GroupPlanEnrollments => Set<GroupPlanEnrollment>();
        public DbSet<UserGroupEntity> UserGroups => Set<UserGroupEntity>();
        public DbSet<StudyPlanLessonSnapshot> StudyPlanLessonSnapshots => Set<StudyPlanLessonSnapshot>();
        public DbSet<GroupPlanSwitch> GroupPlanSwitches => Set<GroupPlanSwitch>();
        // L10n domain
        public DbSet<Sciencetopia.Models.L10n.L10nSet> L10nSets => Set<Sciencetopia.Models.L10n.L10nSet>();
        public DbSet<Sciencetopia.Models.L10n.L10nItem> L10nItems => Set<Sciencetopia.Models.L10n.L10nItem>();
        public DbSet<Sciencetopia.Models.L10n.NodeL10nSet> NodeL10nSets => Set<Sciencetopia.Models.L10n.NodeL10nSet>();
        public DbSet<Sciencetopia.Models.L10n.L10nSetItem> L10nSetItems => Set<Sciencetopia.Models.L10n.L10nSetItem>();
        public DbSet<Sciencetopia.Models.L10n.TagL10nSet> TagL10nSets => Set<Sciencetopia.Models.L10n.TagL10nSet>();
        public DbSet<TagRepresentativeNode> TagRepresentativeNodes { get; set; }  // Add DbSet for the new table

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Identity -> Users schema
            builder.Entity<ApplicationUser>().ToTable("AspNetUsers", schema: "Users");
            builder.Entity<IdentityRole>().ToTable("AspNetRoles", schema: "Users");
            builder.Entity<IdentityUserRole<string>>().ToTable("AspNetUserRoles", schema: "Users");
            builder.Entity<IdentityUserClaim<string>>().ToTable("AspNetUserClaims", schema: "Users");
            builder.Entity<IdentityUserLogin<string>>().ToTable("AspNetUserLogins", schema: "Users");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("AspNetRoleClaims", schema: "Users");
            builder.Entity<IdentityUserToken<string>>().ToTable("AspNetUserTokens", schema: "Users");

            builder.Entity<Tags>().ToTable("Tags", schema: "KnowledgeGraph");
            builder.Entity<TypesOfTags>().ToTable("TypesOfTags", schema: "KnowledgeGraph");
            builder.Entity<TagTypes>(entity =>
            {
                entity.ToTable("TagTypes", schema: "KnowledgeGraph");
                entity.HasKey(e => e.TagStableId);
                entity.Property(e => e.TagStableId)
                    .HasColumnName("TagStableId")
                    .ValueGeneratedNever();
            });
            builder.Entity<KnowledgeNode>().ToTable("KnowledgeNodes", schema: "KnowledgeGraph");
            builder.Entity<Resource>().ToTable("Resources", schema: "KnowledgeGraph");

            // Messages schema
            builder.Entity<Message>().ToTable("Messages", schema: "Messages");
            builder.Entity<Conversation>().ToTable("Conversations", schema: "Messages");
            builder.Entity<Notification>().ToTable("Notifications", schema: "Messages");

            // Logs schema
            builder.Entity<VisitLog>().ToTable("VisitLogs", schema: "Logs");
            builder.Entity<DailySummary>().ToTable("DailySummaries", schema: "Logs");

            // L10n tables
            builder.Entity<Sciencetopia.Models.L10n.L10nSet>().ToTable("L10nSets", schema: "L10n");
            builder.Entity<Sciencetopia.Models.L10n.L10nItem>().ToTable("L10nItems", schema: "L10n");
            builder.Entity<Sciencetopia.Models.L10n.NodeL10nSet>().ToTable("NodeL10nSets", schema: "L10n");
            builder.Entity<Sciencetopia.Models.L10n.L10nSetItem>().ToTable("L10nSetItems", schema: "L10n");
            builder.Entity<Sciencetopia.Models.L10n.TagL10nSet>().ToTable("TagL10nSets", schema: "L10n");

            // Group-first ancestor + StudyGroup flavor
            builder.Entity<GroupEntity>(eb =>
            {
                eb.ToTable("Groups", schema: "Groups");
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Kind).HasMaxLength(32).IsRequired();
                eb.Property(x => x.CreatedByUserId)
                  .HasMaxLength(450)
                  .HasColumnType("nvarchar(450)")
                  .UseCollation("SQL_Latin1_General_CP1_CI_AS");
                eb.Property(x => x.RowVersion).IsRowVersion();
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => x.Kind);
            });

            builder.Entity<StudyGroupEntity>(eb =>
            {
                eb.ToTable("StudyGroups", schema: "StudyGroups");
                eb.Property(x => x.Visibility).HasMaxLength(16).HasDefaultValue("Private");
                eb.Property(x => x.JoinPolicy).HasMaxLength(16).HasDefaultValue("Request");
                eb.Property(x => x.SettingsJson).HasColumnType("nvarchar(max)");
                eb.Property(x => x.Name).HasMaxLength(100);
                eb.HasOne(x => x.Group)
                  .WithOne()
                  .HasForeignKey<StudyGroupEntity>(x => x.Id)
                  .OnDelete(DeleteBehavior.Cascade);
                eb.HasIndex(x => x.Visibility);
            });

            builder.Entity<CohortEntity>(eb =>
            {
                eb.ToTable("Cohorts", schema: "StudyGroups");
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Title).HasMaxLength(200);
                eb.Property(x => x.Visibility).HasMaxLength(20).HasDefaultValue("private");
                eb.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("Active");
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.CreatedBy)
                  .HasMaxLength(450)
                  .HasColumnType("nvarchar(450)")
                  .UseCollation("SQL_Latin1_General_CP1_CI_AS");
                eb.Property(x => x.EnrollmentPolicy)
                  .HasConversion<string>()
                  .HasMaxLength(16)
                  .HasColumnName("EnrollMode")
                  .HasDefaultValue(Sciencetopia.Models.Enums.CohortEnrollMode.OptIn);
                eb.Property(x => x.SettingsJson).HasColumnType("nvarchar(max)");
                eb.HasIndex(x => x.StudyGroupStudyPlanId);
                eb.HasIndex(x => x.StudyPlanVersionId);
                eb.HasIndex(x => new { x.StudyGroupStudyPlanId, x.Status });
                eb.HasOne(x => x.Group)
                  .WithOne()
                  .HasForeignKey<CohortEntity>(x => x.Id)
                  .OnDelete(DeleteBehavior.Cascade);
                eb.HasOne(x => x.StudyGroupStudyPlan)
                  .WithMany()
                  .HasForeignKey(x => x.StudyGroupStudyPlanId)
                  .OnDelete(DeleteBehavior.Restrict);
                eb.HasOne<StudyPlanEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyPlanVersionId)
                  .OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<StudyPlanLessonSnapshot>(eb =>
            {
                eb.ToTable("StudyPlanLessonSnapshots", schema: "StudyPlans");
                eb.HasKey(x => new { x.StudyPlanStableId, x.StudyPlanVersionNumber, x.LessonStableId, x.LessonVersionNumber });
                eb.Property(x => x.StepType).HasMaxLength(32).IsRequired();
                eb.HasIndex(x => new { x.LessonStableId, x.LessonVersionNumber });
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
                entity.ToTable("Favorites", schema: "KnowledgeGraph");
                entity.HasKey(f => f.Id);

                entity.Property(f => f.GroupId)
                    .IsRequired()
                    .HasColumnType("uniqueidentifier");

                entity.Property(f => f.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(f => f.Type)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(f => f.CreatedAt)
                    .IsRequired();

                entity.HasOne(f => f.Group)
                    .WithMany()
                    .HasForeignKey(f => f.GroupId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(f => new { f.GroupId, f.Type });
            });

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

            // KnowledgeNodes: DefaultL10nSetId FK with filtered unique index + versioning metadata
            builder.Entity<KnowledgeNode>(eb =>
            {
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Status).HasMaxLength(32).IsRequired().HasDefaultValue("Draft");
                eb.Property(x => x.CreatedBy).HasMaxLength(128);
                eb.Property(x => x.ApprovedBy).HasMaxLength(128);
                eb.Property(x => x.RowVersion).IsRowVersion();

                eb.HasIndex(x => new { x.StableId, x.VersionNumber })
                  .HasDatabaseName("IX_KN_Stable_Version");
                eb.HasIndex(x => x.StableId)
                  .HasFilter("[IsCurrent] = 1")
                  .IsUnique()
                  .HasDatabaseName("UX_KN_Stable_Current");

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

            // Tags: DefaultL10nSetId + versioning metadata
            builder.Entity<Tags>(eb =>
            {
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Status).HasMaxLength(32).IsRequired().HasDefaultValue("Draft");
                eb.Property(x => x.CreatedBy).HasMaxLength(128);
                eb.Property(x => x.ApprovedBy).HasMaxLength(128);
                eb.Property(x => x.RowVersion).IsRowVersion();

                eb.HasIndex(x => new { x.StableId, x.VersionNumber })
                  .HasDatabaseName("IX_Tags_Stable_Version");
                eb.HasIndex(x => x.StableId)
                  .HasFilter("[IsCurrent] = 1")
                  .IsUnique()
                  .HasDatabaseName("UX_Tags_Stable_Current");

                eb.HasIndex(x => x.DefaultL10nSetId)
                  .HasDatabaseName("UX_Tag_DefaultL10nSet")
                  .IsUnique()
                  .HasFilter("[DefaultL10nSetId] IS NOT NULL");
                eb.HasOne<Sciencetopia.Models.L10n.L10nSet>()
                  .WithMany()
                  .HasForeignKey(x => x.DefaultL10nSetId);
            });

            // StudyGroupStudyPlans
            builder.Entity<StudyGroupStudyPlan>(eb =>
            {
                eb.ToTable("StudyGroupStudyPlans", schema: "StudyGroups");
                eb.HasKey(x => x.Id);
                eb.HasIndex(x => new { x.StudyGroupId, x.StudyPlanStableId }).IsUnique();
                eb.HasIndex(x => x.StudyPlanStableId);
                eb.Property(x => x.PlanVersionId).HasColumnName("ActivePlanVersionId");
                eb.Property(x => x.Permission).HasMaxLength(16).IsRequired();
                eb.Property(x => x.RelationType).HasMaxLength(16).HasDefaultValue("Adopt");
                eb.Property(x => x.SettingsJson).HasColumnType("nvarchar(max)");
                eb.Property(x => x.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasCheckConstraint("CK_SGSP_Permission", "Permission IN ('view','comment','edit','admin')");
                eb.HasCheckConstraint("CK_SGSP_RelationType", "RelationType IN ('Offer','Adopt','Default')");
                eb.HasOne<StudyGroupEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.StudyGroupId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<GroupPlanEnrollment>(eb =>
            {
                eb.ToTable("GroupPlanEnrollments", schema: "StudyPlans");
                eb.HasKey(x => x.Id);
                eb.Property(x => x.VersionPolicy).HasMaxLength(16).HasDefaultValue("Current");
                eb.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("Active");
                eb.Property(x => x.Role).HasConversion<byte>().HasDefaultValue(PlanRole.Viewer);
                eb.Property(x => x.CreatedBy)
                  .HasMaxLength(450)
                  .HasColumnType("nvarchar(450)")
                  .UseCollation("SQL_Latin1_General_CP1_CI_AS");
                eb.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.HasIndex(x => new { x.GroupId, x.StudyPlanStableId })
                  .IsUnique()
                  .HasFilter("[Status] = 'Active'");
                eb.HasIndex(x => x.StudyPlanStableId);
                eb.HasIndex(x => x.PlanVersionId);
                eb.HasOne(x => x.Group)
                  .WithMany()
                  .HasForeignKey(x => x.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
                eb.HasOne<StudyPlanEntity>()
                  .WithMany()
                  .HasForeignKey(x => x.PlanVersionId)
                  .OnDelete(DeleteBehavior.Restrict);
                eb.HasCheckConstraint("CK_GroupPlanEnrollments_VersionPolicy", "VersionPolicy IN ('Current','Pinned')");
                eb.HasCheckConstraint("CK_GroupPlanEnrollments_Status", "Status IN ('Active','Archived')");
            });

            // UserGroups (unified membership table)
            builder.Entity<UserGroupEntity>(b =>
            {
                b.ToTable("UserGroups", schema: "Groups");
                b.HasKey(x => new { x.UserId, x.GroupId });
                b.Property(x => x.Role).HasConversion<byte>();
                b.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("Active");
                b.Property(x => x.UserId)
                 .HasColumnType("nvarchar(450)")
                 .UseCollation("SQL_Latin1_General_CP1_CI_AS");
                b.Property(x => x.JoinedAt).HasDefaultValueSql("SYSUTCDATETIME()");

                b.HasOne(x => x.Group)
                 .WithMany()
                 .HasForeignKey(x => x.GroupId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(x => x.GroupId);
                b.HasIndex(x => x.UserId);
                b.HasIndex(x => new { x.GroupId, x.Status });
            });


            // 映射 TagRepresentativeNode 到 KnowledgeGraph 架构下的表
            builder.Entity<TagRepresentativeNode>()
                .ToTable("TagRepresentativeNode", "KnowledgeGraph");  // 确保使用正确的架构

            // 配置复合主键
            builder.Entity<TagRepresentativeNode>()
                .HasKey(tr => new { tr.TagId, tr.NodeId });  // TagId 和 NodeId 作为复合主键

            // 配置外键关系
            builder.Entity<TagRepresentativeNode>()
                .HasOne<Tags>()
                .WithMany()
                .HasForeignKey(tr => tr.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<TagRepresentativeNode>()
                .HasOne<KnowledgeNode>()
                .WithMany()
                .HasForeignKey(tr => tr.NodeId)
                .OnDelete(DeleteBehavior.Cascade);

            // GroupPlanSwitches (audit/scheduling)
            builder.Entity<GroupPlanSwitch>(b =>
            {
                b.ToTable("GroupPlanSwitches", schema: "StudyGroups");
                b.HasKey(x => x.Id);
                b.Property(x => x.Actor).HasMaxLength(450).HasColumnType("nvarchar(450)")
                  .UseCollation("SQL_Latin1_General_CP1_CI_AS");
                b.Property(x => x.Reason).HasMaxLength(1024);
                b.HasIndex(x => new { x.StudyGroupId, x.PlanStableId, x.EffectiveAt })
                  .HasDatabaseName("IX_GroupPlanSwitches_GroupPlan_Effective");
            });

            // StudyPlans: table mapping + versioning metadata + filtered unique head
            builder.Entity<StudyPlanEntity>(eb =>
            {
                eb.ToTable("StudyPlans", schema: "StudyPlans");
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Status).HasMaxLength(32).IsRequired().HasDefaultValue("Draft");
                eb.Property(x => x.CreatedBy).HasMaxLength(128);
                eb.Property(x => x.ApprovedBy).HasMaxLength(128);
                eb.Property(x => x.RowVersion).IsRowVersion();
                eb.Property(x => x.VersionNumber).HasDefaultValue(1);
                eb.Property(x => x.IsCurrent).HasDefaultValue(false);
                eb.Property(x => x.StableId).HasDefaultValueSql("NEWID()");
                eb.Property(x => x.Title).HasMaxLength(255).IsRequired();
                eb.Property<string>("Privacy").HasMaxLength(16).HasDefaultValue("private");
                eb.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
                eb.Property(x => x.LockfileJson).HasColumnType("nvarchar(max)");
                eb.Property(x => x.LastStudiedAt).HasColumnType("datetime2");

                eb.HasIndex(x => new { x.StableId, x.VersionNumber })
                  .HasDatabaseName("IX_SP_Stable_Version");

                eb.HasIndex(x => x.StableId)
                  .HasFilter("[IsCurrent] = 1")
                  .IsUnique()
                  .HasDatabaseName("UX_SP_Stable_Current");

                eb.HasIndex(x => x.CreatorId);
                eb.HasIndex("Privacy");
            });

            // Lessons: ensure versioned indexes (explicit mapping)
            builder.Entity<LessonEntity>(eb =>
            {
                eb.ToTable("Lessons", schema: "StudyPlans");
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Status).HasMaxLength(32).IsRequired().HasDefaultValue("Draft");
                eb.Property(x => x.CreatedBy).HasMaxLength(128);
                eb.Property(x => x.ApprovedBy).HasMaxLength(128);
                eb.Property(x => x.RowVersion).IsRowVersion();
                eb.Property(x => x.VersionNumber).HasDefaultValue(1);
                eb.Property(x => x.IsCurrent).HasDefaultValue(false);
                eb.Property(x => x.StableId).HasDefaultValueSql("NEWID()");
                eb.Property(x => x.Title).HasMaxLength(255).IsRequired();
                eb.Property(x => x.Kind).HasMaxLength(32).HasDefaultValue("Reading");
                eb.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
                eb.HasIndex(x => x.StudyPlanId);
                eb.HasIndex(x => new { x.StudyPlanId, x.StableId }).IsUnique().HasFilter("[StudyPlanId] IS NOT NULL");

                eb.HasIndex(x => new { x.StableId, x.VersionNumber })
                  .HasDatabaseName("IX_Lesson_Stable_Version");
                eb.HasIndex(x => x.StableId)
                  .HasFilter("[IsCurrent] = 1")
                  .IsUnique()
                  .HasDatabaseName("UX_Lesson_Stable_Current");
            });
        }
    }
}
