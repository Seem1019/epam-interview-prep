using Microsoft.EntityFrameworkCore;

namespace EpamPrep.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<InterviewSession> InterviewSessions => Set<InterviewSession>();
    public DbSet<InterviewTurn> InterviewTurns => Set<InterviewTurn>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<PronunciationTerm> PronunciationTerms => Set<PronunciationTerm>();
    public DbSet<PlanTaskState> PlanTasks => Set<PlanTaskState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Profile>().Property(p => p.Id).ValueGeneratedNever();

        b.Entity<InterviewSession>(e =>
        {
            e.HasMany(s => s.Turns).WithOne().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(s => s.TopicList);
            e.Ignore(s => s.CurrentTopic);
        });
        b.Entity<InterviewTurn>().HasIndex(t => new { t.SessionId, t.Sequence }).IsUnique();

        b.Entity<QuizQuestion>().HasIndex(q => new { q.TopicKey, q.Level });
        b.Entity<QuizAttempt>().HasIndex(a => new { a.TopicKey, a.CreatedAt });
        b.Entity<ReviewItem>().HasIndex(r => r.QuestionId).IsUnique();
        b.Entity<ReviewItem>().HasIndex(r => r.DueAt);
        b.Entity<Lesson>().HasIndex(l => new { l.TopicKey, l.SubtopicKey }).IsUnique();
        b.Entity<PronunciationTerm>().HasIndex(t => t.Term).IsUnique();
        b.Entity<PlanTaskState>().HasKey(p => p.Key);
    }
}
