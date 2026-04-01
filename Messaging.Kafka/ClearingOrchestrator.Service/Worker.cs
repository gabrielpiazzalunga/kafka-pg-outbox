using System.Data;
using Dapper;
using Npgsql;
using System.Diagnostics;

namespace ClearingOrchestrator.Service;

public class ClearingRun
{
    public int id { get; init; }
    public string network { get; init; } = "";
    public DateOnly clearing_date { get; init; }
    public string status { get; init; } = "";
    public DateTime cutoff_timestamp { get; init; }
    public long? expected_count { get; init; }
    public long? actual_count { get; init; }
    public decimal? expected_amount { get; init; }
    public decimal? actual_amount { get; init; }
    public string? spark_app_id { get; init; }
    public string? s3_file_path { get; init; }
    public string? error_message { get; init; }
    public int? retry_count { get; init; }
    public DateTime created_at { get; init; }
    public DateTime? completed_at { get; init; }
    public DateTime previous_cutoff_timestamp { get; init; }
}

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private readonly string _connectionString;

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _connectionString = _config.GetSection("Postgres:ConnectionString").Value!;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Clearing Orchestrator started at: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessClearingStateMachineAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Orchestrator Tick");
            }

            await Task.Delay(10000, stoppingToken); // Tick every 10 seconds
        }
    }

    private async Task ProcessClearingStateMachineAsync(CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // 1. Fetch active run or create one if none exists for today
        var activeRun = await connection.QueryFirstOrDefaultAsync<ClearingRun>(
            "SELECT * FROM clearing_runs WHERE status NOT IN ('COMPLETED', 'FAILED') ORDER BY id DESC LIMIT 1");

        if (activeRun == null)
        {
            // Check if we already completed today's run
            var network = _config.GetValue<string>("Clearing:Network") ?? "visa";
            var today = DateTime.UtcNow.Date;
            
            var completedToday = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT id FROM clearing_runs WHERE network = @Network AND clearing_date = @Date AND status = 'COMPLETED'",
                new { Network = network, Date = today });

            if (completedToday == null)
            {
                _logger.LogInformation("Triggering Soft-Cutoff for {Date}", today.ToString("yyyy-MM-dd"));
                await TriggerNewRunAsync(connection, network, today);
                return; // State machine will pick it up next tick
            }
            
            return; // All good, waiting for tomorrow
        }

        // --- STATE MACHINE ROUTING ---
        string status = activeRun.status;
        int runId = activeRun.id;
        _logger.LogDebug("Processing RunId: {Id} in state {Status}", runId, status);

        switch (status)
        {
            case "LOCKING":
                await HandleLockingPhase(connection, activeRun);
                break;
            case "DRAINING":
                await HandleDrainingPhase(connection, activeRun);
                break;
            case "VALIDATING":
                await HandleValidatingPhase(connection, activeRun);
                break;
            case "GENERATING":
                await HandleGeneratingPhase(connection, activeRun);
                break;
            case "VERIFYING":
                await HandleVerifyingPhase(connection, activeRun);
                break;
            case "DELIVERING":
                await HandleDeliveringPhase(connection, activeRun);
                break;
        }
    }

    private async Task TriggerNewRunAsync(IDbConnection connection, string network, DateTime date)
    {
        var sql = @"
            INSERT INTO clearing_runs 
            (network, clearing_date, status, cutoff_timestamp, previous_cutoff_timestamp) 
            VALUES (@Network, @Date, 'LOCKING', @Cutoff, @PrevCutoff)";
            
        // We find the last cutoff. If none, Jan 1 1970
        var prevCutoff = await connection.QueryFirstOrDefaultAsync<DateTime?>(
            "SELECT cutoff_timestamp FROM clearing_runs WHERE network = @N AND status = 'COMPLETED' ORDER BY id DESC LIMIT 1",
            new { N = network }) ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await connection.ExecuteAsync(sql, new { 
            Network = network, 
            Date = date, 
            Cutoff = DateTime.UtcNow,
            PrevCutoff = prevCutoff
        });
    }

    private async Task HandleLockingPhase(IDbConnection connection, ClearingRun run)
    {
        _logger.LogInformation("[LOCKING] Calculating expected bounds and counts from Ledger for Run {Id}", run.id);
        
        // Count how many records exist between previous cutoff and current cutoff
        var sql = @"
            SELECT COUNT(*), COALESCE(SUM(gross_amount), 0)
            FROM journal_entries 
            WHERE event_type = 'capture' AND network = @Network
              AND event_timestamp > @Prev AND event_timestamp <= @Curr";

        var stats = await connection.QuerySingleAsync<dynamic>(sql, new {
            Network = run.network,
            Prev = run.previous_cutoff_timestamp,
            Curr = run.cutoff_timestamp
        });

        long expectedCount = stats.count;
        decimal expectedAmount = stats.coalesce;

        _logger.LogInformation("Locked Ledger Boundary. Expected Count: {Count}, Expected Amount: {Amt}", expectedCount, expectedAmount);

        // Update run and progress to DRAINING
        await connection.ExecuteAsync(
            "UPDATE clearing_runs SET expected_count = @Count, expected_amount = @Amt, status = 'DRAINING' WHERE id = @Id",
            new { Count = expectedCount, Amt = expectedAmount, Id = run.id });
    }

    private async Task HandleDrainingPhase(IDbConnection connection, ClearingRun run)
    {
        int drainDelayMins = _config.GetValue<int>("Clearing:DrainDelayMinutes", 1);
        DateTime cutoff = run.cutoff_timestamp;
        DateTime waitUntil = cutoff.AddMinutes(drainDelayMins);

        if (DateTime.UtcNow >= waitUntil)
        {
            _logger.LogInformation("[DRAINING] Drain window elapsed for Run {Id}. Transitioning to VALIDATING.", run.id);
            await connection.ExecuteAsync("UPDATE clearing_runs SET status = 'VALIDATING' WHERE id = @Id", new { Id = run.id });
        }
        else
        {
            _logger.LogDebug("Waiting for drain window. Resumes at {WaitUntil}", waitUntil);
        }
    }

    private async Task HandleValidatingPhase(IDbConnection connection, ClearingRun run)
    {
        _logger.LogInformation("[VALIDATING] Iceberg sync verification layer is deferred to Spark Job runtime (pending Trino catalog integration). Proceeding to GENERATING.");
        await connection.ExecuteAsync("UPDATE clearing_runs SET status = 'GENERATING' WHERE id = @Id", new { Id = run.id });
    }

    private async Task HandleGeneratingPhase(IDbConnection connection, ClearingRun run)
    {
        _logger.LogInformation("[GENERATING] Submitting Spark K8s Job for boundary [{Prev}, {Curr}]", run.previous_cutoff_timestamp, run.cutoff_timestamp);

        try
        {
            // Formatting dates for the spark job env vars
            string prev = run.previous_cutoff_timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            string curr = run.cutoff_timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            string date = run.clearing_date.ToString("yyyy-MM-dd");

            // Path to manifest (absolute for reliability in different runtime environments)
            string manifestPath = "/Users/gabrielpiazzalunga/projects/Messaging.Kafka/eng/manifests-kraft/spark-clearing-job.yaml";

            // We use a single bash script to:
            // 1. Delete old job if exists
            // 2. Create a modified manifest with the correct env vars and apply it
            string bashCommand = $@"
                kubectl delete job spark-clearing-job --ignore-not-found;
                sed 's/value: ""visa""/value: ""{run.network}""/' {manifestPath} | \
                sed 's/value: ""1970-01-01T00:00:00Z""/value: ""{prev}""/' | \
                sed 's/value: ""2099-12-31T23:59:59Z""/value: ""{curr}""/' | \
                sed 's/value: ""2026-03-26""/value: ""{date}""/' | \
                kubectl apply -f -";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{bashCommand}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to apply Spark Job. ExitCode: {Code}, Error: {Error}", process.ExitCode, error);
                return;
            }

            _logger.LogInformation("Spark job applied to Kubernetes successfully.");
            await connection.ExecuteAsync("UPDATE clearing_runs SET status = 'VERIFYING' WHERE id = @Id", new { Id = run.id });
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Failed to submit spark job");
        }
    }

    private async Task HandleVerifyingPhase(IDbConnection connection, ClearingRun run)
    {
        // Polling K8s job status
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"kubectl get job spark-clearing-job -o jsonpath='{.status.conditions[?(@.type==\\\"Complete\\\")].status}'\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (output.Contains("True"))
        {
            _logger.LogInformation("[VERIFYING] Spark Job complete. S3 File is ready. Moving to DELIVERING.");
            await connection.ExecuteAsync("UPDATE clearing_runs SET status = 'DELIVERING' WHERE id = @Id", new { Id = run.id });
        }
        else
        {
            _logger.LogDebug("Spark Job still running...");
        }
    }

    private async Task HandleDeliveringPhase(IDbConnection connection, ClearingRun run)
    {
        _logger.LogInformation("[DELIVERING] Simulating SFTP delivery to Card Network...");
        await Task.Delay(2000); // simulate network call
        _logger.LogInformation("SFTP Delivery Successful.");

        await connection.ExecuteAsync("UPDATE clearing_runs SET status = 'COMPLETED', completed_at = @Time WHERE id = @Id", 
            new { Time = DateTime.UtcNow, Id = run.id });

        _logger.LogInformation("=== RUN {Id} FULLY COMPLETED ===", run.id);
    }
}
