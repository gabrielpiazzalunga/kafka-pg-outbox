using Microsoft.EntityFrameworkCore;
using Interviewer.Api.Models;

namespace Interviewer.Api.Data
{
    public class InterviewerDbContext : DbContext
    {
        public InterviewerDbContext(DbContextOptions<InterviewerDbContext> options) : base(options) { }

        public DbSet<InterviewTemplate> InterviewTemplates { get; set; } = null!;
        public DbSet<InterviewSession> InterviewSessions { get; set; } = null!;
        public DbSet<BlockState> BlockStates { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InterviewSession>()
                .HasIndex(s => s.SessionCode)
                .IsUnique();
                
            modelBuilder.Entity<InterviewTemplate>()
                .HasIndex(t => t.Code)
                .IsUnique();
        }
    }
}
