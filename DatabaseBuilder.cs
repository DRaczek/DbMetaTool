using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;
using System.Text;
using System.Text.RegularExpressions;

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

            string connectionString = @$"User={Environment.GetEnvironmentVariable("Username") ?? "SYSDBA"};Password={Environment.GetEnvironmentVariable("Password") ?? "masterkey"};Database={dbPath};DataSource=localhost;Port=3050;Dialect=3;Charset=UTF8";

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

            var procedureFiles = sqlFiles.Where(s => s.Contains(Path.Combine(scriptsDirectory, "3_procedures"))).ToList();
            var otherFiles = sqlFiles.Except(procedureFiles).ToList();

            Console.WriteLine("\n--- Faza 1: Wykonywanie skryptów podstawowych (domeny, tabele, dane) ---");
            int executedCount = 0;
            foreach (var file in otherFiles)
            {
                 ExecuteSingleScriptFromFile(file, connection);
            }
            Console.WriteLine($"Pomyślnie wykonano {executedCount} skryptów.");

            if (procedureFiles.Any())
            {
                Console.WriteLine("\n--- Faza 2a: Tworzenie 'zaślepek' procedur (Stubbing) ---");
                foreach (var file in procedureFiles)
                {
                    try
                    {
                        Console.WriteLine($"-- Tworzenie zaślepki dla: {Path.GetFileName(file)}");
                        string originalContent = File.ReadAllText(file, System.Text.Encoding.UTF8);
                        string stubScript = GenerateProcedureStub(originalContent);

                        if (string.IsNullOrEmpty(stubScript))
                        {
                            Console.WriteLine("   -> Nie udało się wygenerować zaślepki, pominięto.");
                            continue;
                        }

                        ExecuteSingleScript(stubScript, connection);
                    }
                    catch (Exception ex)
                    {
                        string errorMessage = $"Krytyczny błąd podczas tworzenia zaślepki dla '{Path.GetFileName(file)}'.\nSzczegóły: {ex.Message}";
                        throw new Exception(errorMessage, ex);
                    }
                }
                Console.WriteLine("Faza 2a zakończona pomyślnie.");

                Console.WriteLine("\n--- Faza 2b: Wypełnianie procedur właściwą logiką ---");
                foreach (var file in procedureFiles)
                {
                    ExecuteSingleScriptFromFile(file, connection);
                }
                Console.WriteLine("Faza 2b zakończona pomyślnie. Wszystkie procedury zostały zaktualizowane.");
            }
        }

        private string GenerateProcedureStub(string fullScript)
        {
            var match = Regex.Match(fullScript, @"\s+AS\s+", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return string.Empty;
            }

            string header = fullScript.Substring(0, match.Index + match.Length);
            
            var stubBuilder = new StringBuilder();
            stubBuilder.AppendLine(header);
            stubBuilder.AppendLine("BEGIN");
            stubBuilder.AppendLine("  -- Stub");
            stubBuilder.AppendLine("END;");

            return stubBuilder.ToString();
        }

        private void ExecuteSingleScriptFromFile(string file, FbConnection connection)
        {
            try
            {
                Console.WriteLine($"-- Wykonywanie skryptu: {Path.GetFileName(file)}");
                string scriptContent = File.ReadAllText(file, System.Text.Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(scriptContent))
                {
                    Console.WriteLine("   -> Pominięto pusty plik.");
                    return;
                }

                ExecuteSingleScript(scriptContent, connection);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Krytyczny błąd podczas wykonywania skryptu '{Path.GetFileName(file)}'.\nSzczegóły: {ex.Message}";
                throw new Exception(errorMessage, ex);
            }
        }

        private void ExecuteSingleScript(string scriptContent, FbConnection connection)
        {
            var script = new FbScript(scriptContent);
            script.Parse();
            var batch = new FbBatchExecution(connection);
            batch.AppendSqlStatements(script);
            batch.Execute();
        }
    }
}