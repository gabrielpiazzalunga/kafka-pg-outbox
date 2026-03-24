# FeedIngestion.Service

A high-performance file ingestion worker utilizing .NET Channels (for backpressure) and Dapper (for fast, transaction-safe database bulk inserts) to read large settlement files and convert them into Outbox messages.

## Execution Requirements
* .NET 9.0 SDK
* PostgreSQL running locally (see `appsettings.json` for connection details; it defaults to `localhost:5432` with `postgres/postgres`).

## Commands to Run

From the `Messaging.Kafka` root solution directory, use the .NET CLI.

### 1. Generate a Mock Visas File (2GB)

To simulate a real-world scenario, you can generate a massive fixed-width log:
```bash
dotnet run --project FeedIngestion.Service -- --generate-mock /tmp/mock-visa.txt
```
*(This produces a ~2GB file with 20 million lines).*

### 2. Ingest the File

Run the ingestion job against the generated file. You will see progress logs every 10,000 records processed.
```bash
dotnet run --project FeedIngestion.Service -- --ingest-visa /tmp/mock-visa.txt
```

### 3. Test Resilience (Crashing)

While the ingestion is running (step 2), hit `CTRL+C` to terminate the process mid-way. 
Then, run the exact same `ingest-visa` command again. The system will print `[FeedIngestion] Resuming from record <X>` and instantly skip the lines it already processed!
