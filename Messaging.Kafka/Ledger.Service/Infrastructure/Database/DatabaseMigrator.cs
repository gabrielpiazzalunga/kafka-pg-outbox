using DbUp;
using DbUp.Engine;

namespace Ledger.Service.Infrastructure.Database;

public sealed class DatabaseMigrator(string connectionString)
{
    public void MigrateUp()
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        DatabaseUpgradeResult result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(DatabaseMigrator).Assembly,
                s => s.Contains("Migrations"))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
            throw new InvalidOperationException("DB migration failed", result.Error);
    }
}
