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

        public class StartRequest { public string Code { get; set; } = string.Empty; }

        [HttpPost("start")]
        public async Task<IActionResult> StartInterview([FromBody] StartRequest request)
        {
            // Get the default template
            var template = await _context.InterviewTemplates.FirstOrDefaultAsync();
            if (template == null) return NotFound("No interview templates found in database.");

            // Create or resume session
            var session = await _context.InterviewSessions
                .FirstOrDefaultAsync(s => s.SessionCode == request.Code);

            if (session == null)
            {
                session = new InterviewSession
                {
                    Id = Guid.NewGuid(),
                    SessionCode = request.Code,
                    TemplateId = template.Id,
                    StartedAt = DateTime.UtcNow
                };
                _context.InterviewSessions.Add(session);
                await _context.SaveChangesAsync();
            }

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
    }
}
