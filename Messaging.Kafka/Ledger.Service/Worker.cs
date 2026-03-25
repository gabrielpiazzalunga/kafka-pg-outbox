using Dapper;
using Ledger.Service.Infrastructure.Database;

namespace Ledger.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly Random _random = new();

    public Worker(ILogger<Worker> logger, IDbConnectionFactory connectionFactory)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ledger Generation Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await GenerateSampleCaptureEventAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating sample ledger event");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task GenerateSampleCaptureEventAsync()
    {
        var txnId = Guid.NewGuid();
        var amount = 100.00m;
        
        // Visa ARN is 23 digits, starts with 7
        var arnPart1 = _random.NextInt64(100000000000L).ToString("D11");
        var arnPart2 = _random.NextInt64(100000000000L).ToString("D11");
        var arn = $"7{arnPart1}{arnPart2}";
        
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        _logger.LogInformation("Inserting sample balanced journal entry for Txn {txnId} (ARN {arn})", txnId, arn);

        // 1. Insert Journal Entry (Capture event)
        // This will be picked up by Debezium as the trigger for clearing Lake ingestion.
        var entryId = await connection.QuerySingleAsync<long>(@"
            INSERT INTO journal_entries (
                idempotency_key, event_type, event_timestamp, transaction_id, 
                merchant_id, network, arn, gross_amount, source_system
            ) VALUES (
                @IdempotencyKey, 'capture', NOW(), @TxnId, 
                'merchant_001', 'visa', @Arn, @Amount, 'sample_generator'
            ) RETURNING entry_id", 
            new { 
                IdempotencyKey = $"sample:capture:{txnId}", 
                TxnId = txnId, 
                Arn = arn, 
                Amount = amount 
            }, transaction);

        // 2. Insert Book Entries (Legs: Every debit MUST have a credit)
        
        // Leg 1: Debit charge_captured (Increasing confirmed receivables)
        // Account ID 2: charge_captured
        await connection.ExecuteAsync(@"
            INSERT INTO book_entries (entry_id, account_id, entry_type, amount, memo)
            VALUES (@EntryId, 2, 'debit', @Amount, 'Merchant captured confirmed funds')",
            new { EntryId = entryId, Amount = amount }, transaction);

        // Leg 2: Credit auth_holding (Decreasing temporary holds)
        // Account ID 1: auth_holding
        await connection.ExecuteAsync(@"
            INSERT INTO book_entries (entry_id, account_id, entry_type, amount, memo)
            VALUES (@EntryId, 1, 'credit', @Amount, 'Releasing authorization hold')",
            new { EntryId = entryId, Amount = amount }, transaction);

        await transaction.CommitAsync();
        
        _logger.LogInformation("Ledger Integrity Check: Journal Entry #{entryId} - Posted Debit/Credit legs successfully.", entryId);
    }
}
