using Ledger.Service;
using Ledger.Service.Infrastructure.Database;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
var connectionString = builder.Configuration.GetSection("Postgres")["ConnectionString"] 
                       ?? throw new InvalidOperationException("Postgres connection string not found");

// Services
builder.Services.AddSingleton(new DatabaseMigrator(connectionString));
builder.Services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Run Migrations
var migrator = host.Services.GetRequiredService<DatabaseMigrator>();
migrator.MigrateUp();

await host.RunAsync();
