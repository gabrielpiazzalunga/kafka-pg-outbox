using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Interviewer.Api.Models
{
    public class InterviewTemplate
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Code { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "jsonb")]
        public JsonDocument ConfigJson { get; set; } = null!;
    }

    public class InterviewSession
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        [MaxLength(200)]
        public string CandidateName { get; set; } = string.Empty;

        public Guid TemplateId { get; set; }
        public InterviewTemplate Template { get; set; } = null!;

        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public int? OverallRating { get; set; }
        public string? SummaryNotes { get; set; }
    }

    public class BlockState
    {
        [Key]
        public Guid Id { get; set; }
        
        public Guid SessionId { get; set; }
        public InterviewSession Session { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string BlockConfigId { get; set; } = string.Empty;

        public int TimeSpentSeconds { get; set; }

        [Column(TypeName = "jsonb")]
        public JsonDocument NotesJson { get; set; } = null!;

        [Column(TypeName = "jsonb")]
        public JsonDocument CheckedItemsJson { get; set; } = null!;

        [Column(TypeName = "jsonb")]
        public JsonDocument RatingsJson { get; set; } = null!;
    }
}
