using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Messaging.Kafka.Client.Infrastructure.Database;

/// <summary>
/// Registers the Debezium outbox connector via the Kafka Connect REST API at application startup.
/// Uses PUT (idempotent) — safe to call on every startup from multiple replicas.
/// Intended for local development; production environments should use the K8s Job instead.
/// </summary>
public sealed class DebeziumConnectorRegistrar(
    HttpClient httpClient,
    ILogger<DebeziumConnectorRegistrar> logger)
{
    private const string ConnectorName = "outbox-connector";

    public async Task EnsureConnectorAsync(string kafkaConnectUrl, string connectorConfigPath, CancellationToken ct = default)
    {
        var configJson = await File.ReadAllTextAsync(connectorConfigPath, ct);
        var fullConfig = JsonSerializer.Deserialize<JsonElement>(configJson);

        // PUT /connectors/{name}/config expects just the config object, not the wrapper
        JsonElement configOnly = fullConfig.TryGetProperty("config", out var cfg) ? cfg : fullConfig;

        var url = $"{kafkaConnectUrl.TrimEnd('/')}/connectors/{ConnectorName}/config";

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                var response = await httpClient.PutAsJsonAsync(url, configOnly, ct);
                response.EnsureSuccessStatusCode();
                logger.LogInformation("Debezium connector '{Connector}' registered successfully", ConnectorName);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Attempt {Attempt}/10 — Kafka Connect not ready: {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        logger.LogError("Failed to register Debezium connector after 10 attempts — continuing without it");
    }
}
