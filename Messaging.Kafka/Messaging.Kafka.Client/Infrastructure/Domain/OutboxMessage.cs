using System;

namespace Messaging.Kafka.Client.Infrastructure.Domain;

/// <summary>
/// Represents a transactional outbox message to be captured by Debezium CDC.
/// </summary>
public sealed record OutboxMessage(
    Guid Id,
    string AggregateType,
    string AggregateId,
    string Type,
    byte[] Payload,
    DateTime? CreatedAt = null
);
