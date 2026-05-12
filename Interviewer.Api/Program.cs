using Microsoft.EntityFrameworkCore;
using Interviewer.Api.Data;
using Interviewer.Api.Models;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure Postgres Database
builder.Services.AddDbContext<InterviewerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure CORS for local development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Auto-migrate and seed
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<InterviewerDbContext>();
    context.Database.Migrate();

    if (!context.InterviewTemplates.Any())
    {
        var mockConfig = new
        {
            id = "swe-senior-1",
            title = "Senior Software Engineer Interview (1 Hour)",
            blocks = new[]
            {
                new {
                    id = "intro",
                    title = "1. Intro & Background",
                    durationSeconds = 600,
                    questions = new[] {
                        new { id = "q-intro-1", text = "Walk me through your technical background and a recent complex system you designed or scaled.", imageUrl = (string?)null, checklist = new[] { "Mentioned specific scale/metrics", "Clear communication", "Owned the architecture" } }
                    }
                },
                new {
                    id = "scenarios",
                    title = "2. Architectural Scenarios",
                    durationSeconds = 1200,
                    questions = new[] {
                        new { id = "q-scenario-a", text = "Option A (Event-Driven): Our checkout service saves an order to the Postgres database, and then immediately publishes an OrderCreated event to Kafka. Looking at this dashboard over a 24-hour period, we have a discrepancy. What causes this, and how would you redesign the system to guarantee 100% consistency?", imageUrl = "http://localhost:5173/dual_write_discrepancy_animated.gif", checklist = new[] { "Identified Dual Write anti-pattern", "Suggested Transactional Outbox Pattern", "Mentioned CDC/Debezium" } },
                        new { id = "q-scenario-b", text = "Option B (Regular API): Looking at this dashboard from an unresponsive application, what do you think is happening? Why did the CPU drop if requests are still coming in?", imageUrl = "http://localhost:5173/thread_exhaustion_graph_animated.gif", checklist = new[] { "Identified Thread Exhaustion", "Understands CPU drop reason", "Suggested Timeouts/Async I/O" } },
                        new { id = "q-scenario-c", text = "Option C (Database): Our API latency suddenly spiked. We checked the DB dashboard and saw the active connections climbing and then flatlining at exactly 100. Is the database CPU/Disk the bottleneck here? What else could be causing this?", imageUrl = "http://localhost:5173/connection_pool_exhaustion_animated.gif", checklist = new[] { "Identified Connection Pool Exhaustion", "Mentioned Connection Leaks", "Mentioned N+1 queries" } },
                        new { id = "q-scenario-d", text = "Option D (Memory): This is the heap memory usage of our backend over a week. What does this pattern tell you, and what specific steps would you take to find the root cause?", imageUrl = "http://localhost:5173/staircase_memory_leak_animated.gif", checklist = new[] { "Identified Memory Leak", "Suggested taking a Heap Dump", "Mentioned GC Roots" } }
                    }
                },
                new {
                    id = "rapid-fire",
                    title = "3. Rapid Fire Knowledge",
                    durationSeconds = 300,
                    questions = new[] {
                        new { id = "q-rapid-1", text = "Stack vs. Heap: What's the difference? When would an allocation end up on the heap?", imageUrl = (string?)null, checklist = new[] { "Stack is fast/short-lived", "Heap is dynamic/GC managed" } },
                        new { id = "q-rapid-2", text = "Database Isolation Levels: Give an example of a problem that occurs at Read Committed that wouldn't happen at Serializable.", imageUrl = (string?)null, checklist = new[] { "Phantom reads", "Non-repeatable reads" } }
                    }
                },
                new {
                    id = "coding",
                    title = "4. Live Coding: Transactional KV Store",
                    durationSeconds = 1800,
                    questions = new[] {
                        new { id = "q-code-1", text = "Phase 1: Basic Operations (INSERT, UPDATE, GET, DELETE)", imageUrl = (string?)null, checklist = new[] { "Used Hash Map", "Handled existence checks" } },
                        new { id = "q-code-2", text = "Phase 2: Transactions (Non-Nested). BEGIN, COMMIT, ROLLBACK.", imageUrl = (string?)null, checklist = new[] { "Maintained separate transaction map", "Implemented DELETE tombstone", "Checked both maps on GET" } },
                        new { id = "q-code-3", text = "Phase 3: Nested Transactions (Follow-up)", imageUrl = (string?)null, checklist = new[] { "Refactored to Stack of Hash Maps", "Correctly popped/merged on COMMIT/ROLLBACK" } }
                    }
                }
            }
        };

        var configJson = JsonSerializer.Serialize(mockConfig);

        context.InterviewTemplates.Add(new InterviewTemplate
        {
            Id = Guid.NewGuid(),
            Code = "SWE-SENIOR",
            Title = "Senior Software Engineer Interview",
            ConfigJson = JsonDocument.Parse(configJson)
        });
        context.SaveChanges();
    }
}

app.Run();
