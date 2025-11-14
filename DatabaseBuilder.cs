using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;

namespace Sente
{
    public class DatabaseBuilder
    {
        public void Build(string databaseDirectory, string scriptsDirectory)
        {
            if (!Directory.Exists(scriptsDirectory))
            {
                throw new DirectoryNotFoundException($"Katalog ze skryptami nie istnieje: {scriptsDirectory}");
            }

            string dbPath = CreateNewDatabaseFile(databaseDirectory);

            Console.WriteLine($"Wykonywanie skryptów z katalogu: {scriptsDirectory}");
            ExecuteScripts(dbPath, scriptsDirectory);
        }

        public void Update(string connectionString, string scriptsDirectory)
        {
            if (!Directory.Exists(scriptsDirectory))
            {
                throw new DirectoryNotFoundException($"Katalog ze skryptami nie istnieje: {scriptsDirectory}");
            }

            Console.WriteLine($"Rozpoczynanie aktualizacji bazy danych...");
            ExecuteScripts(connectionString, scriptsDirectory);
        }

        private string CreateNewDatabaseFile(string databaseDirectory)
        {
            Directory.CreateDirectory(databaseDirectory);
            string dbPath = Path.Combine(databaseDirectory, $"db_{DateTime.Now:yyyyMMdd_HHmmss}.fdb");

            Console.WriteLine($"Tworzenie nowej bazy danych w: {dbPath}");

            string connectionString = @$"User=SYSDBA;Password=ppp123;Database={dbPath};DataSource=localhost;Port=3050;Dialect=3;Charset=UTF8";

            try
            {
                FbConnection.CreateDatabase(connectionString);
                Console.WriteLine("Pusta baza danych została utworzona pomyślnie.");
                return connectionString;
            }
            catch (Exception ex)
            {
                throw new Exception($"Nie udało się utworzyć bazy danych w '{dbPath}'. Błąd: {ex.Message}", ex);
            }
        }

        private void ExecuteScripts(string connectionString, string scriptsDirectory)
        {
            using var connection = new FbConnection(connectionString);
            connection.Open();
            Console.WriteLine("Połączono z bazą danych w celu wykonania skryptów.");

            var sqlFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.AllDirectories)
                                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

            if (sqlFiles.Count == 0)
            {
                Console.WriteLine("Ostrzeżenie: Nie znaleziono żadnych plików .sql do wykonania.");
                return;
            }

            Console.WriteLine($"Znaleziono {sqlFiles.Count} plików .sql do wykonania.");

            int executedCount = 0;
            foreach (var file in sqlFiles)
            {
                try
                {
                    Console.WriteLine($"-- Wykonywanie skryptu: {Path.GetFileName(file)}");
                    string scriptContent = File.ReadAllText(file);

                    if (string.IsNullOrWhiteSpace(scriptContent))
                    {
                        Console.WriteLine("   -> Pominięto pusty plik.");
                        continue;
                    }

                    var script = new FbScript(scriptContent);
                    script.Parse();
                    var batch = new FbBatchExecution(connection);
                    batch.AppendSqlStatements(script);
                    batch.Execute();
                    executedCount++;
                }
                catch (Exception ex)
                {
                    string errorMessage = $"Krytyczny błąd podczas wykonywania skryptu '{Path.GetFileName(file)}'. Proces budowania przerwany.\nSzczegóły: {ex.Message}";
                    throw new Exception(errorMessage, ex);
                }
            }

            Console.WriteLine($"Pomyślnie wykonano {executedCount} skryptów.");
        }
    }
}