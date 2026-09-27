using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portion.Server.Configuration;
using Portion.Server.Data;
using Portion.Server.Entities;
using Portion.Server.Infrastructure;
using Portion.Server.Models.Dtos;
using Portion.Server.Services.Abstractions;

namespace Portion.Server.Controllers
{
    [ApiController]
    [Route("api/recruitment")]
    [Produces("application/json")]
    public class RecruitmentController : ControllerBase
    {
        private readonly IngestionChannel _channel;
        private readonly IStorageSyncEngine _syncEngine;
        private readonly ApplicationDbContext _dbContext;
        private readonly IInteractiveScreeningEngine _screeningEngine;
        private readonly string _uploadDirectory;

        public RecruitmentController(
            IngestionChannel channel,
            IStorageSyncEngine syncEngine,
            ApplicationDbContext dbContext,
            IInteractiveScreeningEngine screeningEngine,
            IOptions<IngestionOptions> ingestionOptions)
        {
            _channel = channel;
            _syncEngine = syncEngine;
            _dbContext = dbContext;
            _screeningEngine = screeningEngine;
            _uploadDirectory = ingestionOptions.Value.UploadDirectory;
        }

        /// <summary>Returns all indexed resumes ordered by ingestion date.</summary>
        [HttpGet("resumes")]
        [ProducesResponseType(typeof(List<ResumeDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetResumes()
        {
            var resumes = await _dbContext.Resumes
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ResumeDto
                {
                    Id            = r.Id,
                    CandidateName = r.CandidateName,
                    FilePath      = r.FilePath,
                    Status        = r.Status.ToString(),
                    FailureReason = r.FailureReason,
                    CreatedAt     = r.CreatedAt,
                    LastSyncedAt  = r.LastSyncedAt,
                    ChunkCount    = r.Chunks.Count
                })
                .ToListAsync();

            return Ok(resumes);
        }

        /// <summary>Accepts a file upload, stores it, and enqueues it for ingestion.</summary>
        [HttpPost("upload-resume")]
        [ProducesResponseType(typeof(UploadResponseDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UploadResume(IFormFile file)
        {
            if (file is null || file.Length == 0)
                return BadRequest(ApiResponse.Fail("No file was provided."));

            var storageDirectory = Path.Combine(Directory.GetCurrentDirectory(), _uploadDirectory);
            Directory.CreateDirectory(storageDirectory);

            var filePath = Path.Combine(storageDirectory, $"{Guid.NewGuid()}_{file.FileName}");
            await using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var hash = _syncEngine.ComputeFileHash(filePath);
            var existing = await _dbContext.Resumes.FirstOrDefaultAsync(r => r.FileHash == hash);

            if (existing?.Status == IngestionStatus.Synced)
            {
                System.IO.File.Delete(filePath);
                return Ok(new UploadResponseDto("Resume is already indexed.", existing.Id));
            }

            var resume = new Resume
            {
                Id            = Guid.NewGuid(),
                CandidateName = Path.GetFileNameWithoutExtension(file.FileName),
                FilePath      = filePath,
                FileHash      = hash,
                Status        = IngestionStatus.Processing
            };

            _dbContext.Resumes.Add(resume);
            await _dbContext.SaveChangesAsync();
            await _channel.Writer.WriteAsync(new IngestionTask(resume.Id, filePath));

            return Accepted(new UploadResponseDto("Resume queued for ingestion.", resume.Id));
        }

        /// <summary>Schedules an async reconciliation scan over a local folder path.</summary>
        [HttpPost("sync-repository")]
        [ProducesResponseType(typeof(SyncResponseDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult SyncRepository([FromBody] FolderSyncRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.FolderPath))
                return BadRequest(ApiResponse.Fail("FolderPath is required."));

            _ = Task.Run(() => _syncEngine.ReconcileRepositoryAsync(request.FolderPath, HttpContext.RequestAborted));
            return Accepted(new SyncResponseDto("Repository reconciliation scheduled."));
        }

        /// <summary>Streams an AI-generated screening response as Server-Sent Events.</summary>
        [HttpGet("screen-chat")]
        public async Task StreamChat([FromQuery] string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control",  "no-cache");
            Response.Headers.Append("Connection",     "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            await foreach (var token in _screeningEngine.ExecuteStreamedScreeningAsync(prompt, HttpContext.RequestAborted))
            {
                var safe = token.Replace("\n", "\\n");
                await Response.WriteAsync($"data: {safe}\n\n");
                await Response.Body.FlushAsync();
            }
        }
    }
}
