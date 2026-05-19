using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Interviewer.Api.Data;
using Interviewer.Api.Models;

namespace Interviewer.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InterviewsController : ControllerBase
    {
        private readonly InterviewerDbContext _context;

        public InterviewsController(InterviewerDbContext context)
        {
            _context = context;
        }

        [HttpGet("templates")]
        public async Task<IActionResult> GetTemplates()
        {
            var templates = await _context.InterviewTemplates
                .Select(t => new { t.Id, t.Title, t.Code })
                .ToListAsync();
            return Ok(templates);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSessions()
        {
            var sessions = await _context.InterviewSessions
                .Include(s => s.Template)
                .OrderByDescending(s => s.StartedAt)
                .Select(s => new {
                    s.Id,
                    s.CandidateName,
                    TemplateTitle = s.Template.Title,
                    s.StartedAt,
                    s.EndedAt,
                    s.OverallRating
                })
                .ToListAsync();
            
            return Ok(sessions);
        }

        public class StartRequest { 
            public Guid TemplateId { get; set; }
            public string CandidateName { get; set; } = string.Empty; 
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartInterview([FromBody] StartRequest request)
        {
            var template = await _context.InterviewTemplates.FindAsync(request.TemplateId);
            if (template == null) return NotFound("Template not found.");

            var session = new InterviewSession
            {
                Id = Guid.NewGuid(),
                CandidateName = request.CandidateName,
                TemplateId = template.Id,
                StartedAt = DateTime.UtcNow
            };
            _context.InterviewSessions.Add(session);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                interviewId = session.Id,
                config = template.ConfigJson
            });
        }

        public class BlockStateDto
        {
            public int TimeSpentSeconds { get; set; }
            public JsonElement Notes { get; set; }
            public JsonElement CheckedItems { get; set; }
            public JsonElement Ratings { get; set; }
        }

        [HttpPut("{id}/blocks/{blockId}")]
        public async Task<IActionResult> SaveBlockState(Guid id, string blockId, [FromBody] BlockStateDto dto)
        {
            var blockState = await _context.BlockStates
                .FirstOrDefaultAsync(b => b.SessionId == id && b.BlockConfigId == blockId);

            if (blockState == null)
            {
                blockState = new BlockState
                {
                    Id = Guid.NewGuid(),
                    SessionId = id,
                    BlockConfigId = blockId
                };
                _context.BlockStates.Add(blockState);
            }

            blockState.TimeSpentSeconds = dto.TimeSpentSeconds;
            blockState.NotesJson = JsonDocument.Parse(dto.Notes.GetRawText() ?? "{}");
            blockState.CheckedItemsJson = JsonDocument.Parse(dto.CheckedItems.GetRawText() ?? "{}");
            blockState.RatingsJson = JsonDocument.Parse(dto.Ratings.GetRawText() ?? "{}");

            await _context.SaveChangesAsync();
            return Ok();
        }

        public class FinishRequest
        {
            public string SummaryNotes { get; set; } = string.Empty;
            public int OverallRating { get; set; }
        }

        [HttpPost("{id}/finish")]
        public async Task<IActionResult> FinishInterview(Guid id, [FromBody] FinishRequest request)
        {
            var session = await _context.InterviewSessions.FindAsync(id);
            if (session == null) return NotFound();

            session.EndedAt = DateTime.UtcNow;
            session.SummaryNotes = request.SummaryNotes;
            session.OverallRating = request.OverallRating;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("{id}/results")]
        public async Task<IActionResult> GetResults(Guid id)
        {
            var session = await _context.InterviewSessions
                .Include(s => s.Template)
                .FirstOrDefaultAsync(s => s.Id == id);
            
            if (session == null) return NotFound();

            var blocks = await _context.BlockStates
                .Where(b => b.SessionId == id)
                .ToListAsync();

            return Ok(new
            {
                session = new {
                    session.Id,
                    session.CandidateName,
                    session.StartedAt,
                    session.EndedAt,
                    session.OverallRating,
                    session.SummaryNotes
                },
                config = session.Template.ConfigJson,
                blockStates = blocks.Select(b => new {
                    b.BlockConfigId,
                    b.TimeSpentSeconds,
                    b.NotesJson,
                    b.CheckedItemsJson,
                    b.RatingsJson
                })
            });
        }
    }
}
