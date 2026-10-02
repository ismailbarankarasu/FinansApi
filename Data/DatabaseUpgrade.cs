using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Data;

public static class DatabaseUpgrade
{
    public static void Backup(string source, string destination)
    {
        if (!File.Exists(source))
        {
            throw new InvalidOperationException("Kaynak veritabanı bulunamadı.");
        }

        if (File.Exists(destination))
        {
            throw new InvalidOperationException("Hedef dosya zaten var; üzerine yazılmadı.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly }.ToString());
        using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        input.Open();
        output.Open();
        input.BackupDatabase(output);
        using var check = output.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        if ((string?)check.ExecuteScalar() != "ok")
        {
            throw new InvalidOperationException("Yedek bütünlük kontrolü başarısız.");
        }
    }

    public static async Task Import(string source, string destination)
    {
        if (File.Exists(destination))
        {
            throw new InvalidOperationException("Yeni hedef dosyası seçin; mevcut dosyanın üzerine yazılmaz.");
        }

        var snapshot = Path.GetFullPath(destination) + ".legacy-backup";
        Backup(source, snapshot);
        await using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = destination }.ToString()).Options);
        await database.Database.MigrateAsync();
        await database.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $path AS legacy";
            attach.Parameters.AddWithValue("$path", snapshot);
            await attach.ExecuteNonQueryAsync();
        }

        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            await database.Database.ExecuteSqlRawAsync("INSERT INTO Users (Id, FullName, Email, PasswordHash, CreatedAt) SELECT Id, FullName, Email, PasswordHash, CreatedAt FROM legacy.Users");
            await database.Database.ExecuteSqlRawAsync("INSERT INTO Categories (Id, UserId, Name, Type) SELECT Id, UserId, Name, Type FROM legacy.Categories");
            await database.Database.ExecuteSqlRawAsync("INSERT INTO Transactions (Id, UserId, CategoryId, Amount, Date, Description, Type) SELECT Id, UserId, CategoryId, Amount, Date, Description, Type FROM legacy.Transactions");
            foreach (var table in new[]
                {
                    "Users",
                    "Categories",
                    "Transactions"

                }

            )
            {
                using var check = connection.CreateCommand();
                check.Transaction = (SqliteTransaction)transaction.GetDbTransaction();
                check.CommandText = $"SELECT (SELECT COUNT(*) FROM main.{table}) = (SELECT COUNT(*) FROM legacy.{table})";
                if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 1)
                {
                    throw new InvalidOperationException($"{table}: kayıt sayısı eşleşmiyor.");
                }
            }

            await LegacyCompanyMapping.Apply(database);
            await transaction.CommitAsync();
        }

        using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM (SELECT Id, UserId, CategoryId, Amount, Date, Description, Type FROM main.Transactions EXCEPT SELECT Id, UserId, CategoryId, Amount, Date, Description, Type FROM legacy.Transactions)";
        if (Convert.ToInt64(await verify.ExecuteScalarAsync()) != 0)
        {
            throw new InvalidOperationException("Aktarılan işlem verileri kaynakla eşleşmiyor.");
        }

        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA foreign_key_check";
        using var reader = await integrity.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            throw new InvalidOperationException("İlişki bütünlüğü kontrolü başarısız.");
        }

        Console.WriteLine($"Aktarım doğrulandı. Kullanıcı: {await database.Users.CountAsync()}, kategori: {await database.Categories.CountAsync()}, işlem: {await database.Transactions.CountAsync()}. Kaynak korundu.");
    }

    public static async Task Initialize(AppDbContext database, bool demo)
    {
        await database.Database.OpenConnectionAsync();
        using var cmd = database.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users'";
        var hasUsers = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
        if (hasUsers && Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 0)
        {
            throw new InvalidOperationException("Eski EnsureCreated veritabanı korundu. --import-legacy kaynak.db yeni.db ile ayrı dosyaya aktarın; docs/database-upgrade.md belgesine bakın.");
        }

        await database.Database.MigrateAsync();
        if (demo)
        {
            DbInitializer.Seed(database);
        }

        await LegacyCompanyMapping.Apply(database);
    }

}
