using System.Text.Json;
using Clearing.IntegrationTests.Fixtures;
using Confluent.Kafka;
using Dapper;
using Npgsql;

namespace Clearing.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the Clearing Pipeline.
/// Validates: Postgres INSERT → Debezium CDC → Kafka Topic.
/// </summary>
public class ClearingPipelineTests : IClassFixture<ClearingPipelineFixture>
{
    private readonly ClearingPipelineFixture _fixture;

    public ClearingPipelineTests(ClearingPipelineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_JournalEntry_Should_Appear_In_Kafka_Via_Debezium()
    {
        // ─── ARRANGE ───
        // Insert a known journal entry into Postgres
        var txnId = Guid.NewGuid();
        var arn = $"7{Random.Shared.NextInt64(100000000000):D11}{Random.Shared.NextInt64(100000000000):D11}";
        var expectedAmount = 250.50m;

        await using var conn = new NpgsqlConnection(_fixture.PostgresConnectionString);
        await conn.OpenAsync();

        var entryId = await conn.QuerySingleAsync<long>("""
            INSERT INTO journal_entries (
                idempotency_key, event_type, event_timestamp, transaction_id, 
                merchant_id, network, arn, gross_amount, source_system
            ) VALUES (
                @IdempotencyKey, 'capture', NOW(), @TxnId, 
                'test_merchant_001', 'visa', @Arn, @Amount, 'integration_test'
            ) RETURNING entry_id
            """,
            new
            {
                IdempotencyKey = $"test:capture:{txnId}",
                TxnId = txnId,
                Arn = arn,
                Amount = expectedAmount
            });

        // Also insert the book entries (debit + credit legs) for integrity
        await conn.ExecuteAsync("""
            INSERT INTO book_entries (entry_id, account_id, entry_type, amount, memo)
            VALUES (@EntryId, 2, 'debit', @Amount, 'Test debit leg')
            """, new { EntryId = entryId, Amount = expectedAmount });

        await conn.ExecuteAsync("""
            INSERT INTO book_entries (entry_id, account_id, entry_type, amount, memo)
            VALUES (@EntryId, 1, 'credit', @Amount, 'Test credit leg')
            """, new { EntryId = entryId, Amount = expectedAmount });

        // ─── ACT ───
        // Consume from Kafka to verify that Debezium propagated the CDC event
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _fixture.KafkaBootstrapServers,
            GroupId = $"test-consumer-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe("ledger.journal.events");

        // Poll for up to 60 seconds looking for our specific transaction
        // (Debezium needs time to create replication slot, snapshot, and produce first CDC event)
        var deadline = DateTime.UtcNow.AddSeconds(60);
        bool found = false;
        string? receivedJson = null;

        while (DateTime.UtcNow < deadline && !found)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(2));
                if (result?.Message?.Value == null) continue;

                receivedJson = result.Message.Value;

                // Parse the JSON to find our specific transaction_id
                using var doc = JsonDocument.Parse(receivedJson);
                if (doc.RootElement.TryGetProperty("transaction_id", out var receivedTxnId) &&
                    receivedTxnId.GetString() == txnId.ToString())
                {
                    found = true;
                }
                else
                {
                    Console.WriteLine($"Test saw different Txn: {receivedTxnId.GetString() ?? "unknown"} (looking for {txnId})");
                }
            }
            catch (ConsumeException)
            {
                // Topic may not exist yet — Debezium hasn't produced the first message
                await Task.Delay(1000);
            }
        }

        consumer.Close();

        // Assertions
        Assert.True(found, $"Expected to find transaction {txnId} (ARN {arn}) in Kafka topic 'ledger.journal.events' within 60 seconds. Last record seen: {receivedJson ?? "NONE"}");
        Assert.NotNull(receivedJson);

        // Parse the received message and validate key fields
        var message = JsonDocument.Parse(receivedJson!);
        var root = message.RootElement;

        Assert.Equal("visa", root.GetProperty("network").GetString());
        Assert.Equal("capture", root.GetProperty("event_type").GetString());
        Assert.Equal("test_merchant_001", root.GetProperty("merchant_id").GetString());
        Assert.Equal(arn, root.GetProperty("arn").GetString());
        Assert.Equal(expectedAmount, decimal.Parse(root.GetProperty("gross_amount").GetString()!));
    }

    [Fact]
    public async Task Insert_Multiple_Networks_Should_All_Arrive_In_Kafka()
    {
        // ─── ARRANGE ───
        // Insert entries for 3 different networks
        var networks = new[] { "visa", "mastercard", "elo" };
        var insertedTxns = new Dictionary<string, Guid>();

        await using var conn = new NpgsqlConnection(_fixture.PostgresConnectionString);
        await conn.OpenAsync();

        foreach (var network in networks)
        {
            var txnId = Guid.NewGuid();
            insertedTxns[network] = txnId;

            await conn.ExecuteAsync("""
                INSERT INTO journal_entries (
                    idempotency_key, event_type, event_timestamp, transaction_id, 
                    merchant_id, network, arn, gross_amount, source_system
                ) VALUES (
                    @IdempotencyKey, 'capture', NOW(), @TxnId, 
                    'test_merchant_002', @Network, @Arn, 100.00, 'integration_test'
                )
                """,
                new
                {
                    IdempotencyKey = $"test:multi:{txnId}",
                    TxnId = txnId,
                    Network = network,
                    Arn = $"7{Random.Shared.NextInt64(100000000000):D11}{Random.Shared.NextInt64(100000000000):D11}"
                });
        }

        // ─── ACT & ASSERT ───
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _fixture.KafkaBootstrapServers,
            GroupId = $"test-consumer-multi-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe("ledger.journal.events");

        var foundNetworks = new HashSet<string>();
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline && foundNetworks.Count < 3)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(2));
                if (result?.Message?.Value == null) continue;

                var doc = JsonDocument.Parse(result.Message.Value);
                if (!doc.RootElement.TryGetProperty("transaction_id", out var txnIdProp)) continue;

                var txnIdStr = txnIdProp.GetString();
                foreach (var kvp in insertedTxns)
                {
                    if (kvp.Value.ToString() == txnIdStr)
                        foundNetworks.Add(kvp.Key);
                }
            }
            catch (ConsumeException)
            {
                await Task.Delay(1000);
            }
        }

        consumer.Close();

        Assert.Contains("visa", foundNetworks);
        Assert.Contains("mastercard", foundNetworks);
        Assert.Contains("elo", foundNetworks);
    }
}
