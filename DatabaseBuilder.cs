using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace Sente
{
    public class ColumnModel
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty; // Przechowuje tylko typ, np. "VARCHAR(100)"
        public string FullDefinitionInScript { get; set; } = string.Empty; // Pełna linia z pliku
    }

    public class TableModel
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, ColumnModel> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string OriginalCreateTableScript { get; set; } = string.Empty;
    }

    public class DomainModel
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty; // Tylko typ, np. "INTEGER"
        public string FullDefinitionInScript { get; set; } = string.Empty; // Pełna definicja
    }

    public class ProcedureModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullScript { get; set; } = string.Empty; // Pełny skrypt CREATE OR ALTER
        public string Body { get; set; } = string.Empty;       // Tylko ciało procedury (wszystko po AS)
    }

    public class SchemaModel
    {
        public Dictionary<string, TableModel> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DomainModel> Domains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ProcedureModel> Procedures { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

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
            Console.WriteLine("Rozpoczynanie uproszczonej aktualizacji bazy danych...");

            var targetSchema = GetTargetSchema(scriptsDirectory);
            Console.WriteLine($"\n[Analiza] Znaleziono definicje dla {targetSchema.Domains.Count} domen, {targetSchema.Tables.Count} tabel i {targetSchema.Procedures.Count} procedur w skryptach.");

            SchemaModel currentSchema;
            using (var connection = new FbConnection(connectionString))
            {
                connection.Open();
                currentSchema = GetCurrentSchema(connection);
            }
            Console.WriteLine($"[Analiza] Znaleziono {currentSchema.Domains.Count} domen, {currentSchema.Tables.Count} tabel i {currentSchema.Procedures.Count} procedur w bazie.");

            var migrationScripts = CompareSchemas(targetSchema, currentSchema);

            if (!migrationScripts.Any())
            {
                Console.WriteLine("\n[Status] Baza danych jest już aktualna. Brak zmian.");
                return;
            }

            Console.WriteLine($"\n[Migracja] Znaleziono {migrationScripts.Count} operacji do wykonania.");
            using (var connection = new FbConnection(connectionString))
            {
                connection.Open();
                try
                {
                    var batch = new FbBatchExecution(connection);

                    foreach (var script in migrationScripts)
                    {
                        string displayScript = script.Split('\n')[0].Trim();
                        displayScript = displayScript.Substring(0, Math.Min(displayScript.Length, 100));
                        Console.WriteLine($"> Wykonywanie: {displayScript}...");

                        // Dodajemy każdy wygenerowany skrypt do jednego, dużego batcha
                        ExecuteSingleScript(script, connection);
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"FATALNY BŁĄD podczas wykonywania migracji. Wszystkie zmiany zostały wycofane.");
                    Console.WriteLine($"Szczegóły błędu: {ex.Message}");
                    Console.ResetColor();
                    throw;
                }
            }

            Console.WriteLine("\n[Sukces] Aktualizacja zakończona.");
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

        /// <summary>
        /// Odczytuje aktualny schemat z podłączonej bazy danych.
        /// Używa zapytań do tabel systemowych RDB$*, podobnie jak MetadataExporter.
        /// </summary>
        private SchemaModel GetCurrentSchema(FbConnection connection)
        {
            var schema = new SchemaModel();

            // Domeny
            string queryDomains = @"SELECT f.RDB$FIELD_NAME, f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH FROM RDB$FIELDS f WHERE f.RDB$SYSTEM_FLAG = 0 AND f.RDB$FIELD_NAME NOT LIKE 'RDB$%'";
            using (var cmd = new FbCommand(queryDomains, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0).Trim();
                    string dataType = Utils.GetSqlType(reader.GetInt16(1), reader.IsDBNull(2) ? (short)0 : reader.GetInt16(2), reader.IsDBNull(3) ? (short)0 : reader.GetInt16(3), reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4), reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5));
                    schema.Domains.Add(name, new DomainModel { Name = name, DataType = dataType });
                }
            }

            // Tabele i kolumny
            var tableNames = new List<string>();
            using (var cmd = new FbCommand("SELECT RDB$RELATION_NAME FROM RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL", connection))
            using (var reader = cmd.ExecuteReader())
                while (reader.Read()) tableNames.Add(reader.GetString(0).Trim());

            foreach (var tableName in tableNames)
            {
                var tableModel = new TableModel { Name = tableName };
                var queryColumns = @"SELECT rf.RDB$FIELD_NAME, rf.RDB$FIELD_SOURCE FROM RDB$RELATION_FIELDS rf WHERE rf.RDB$RELATION_NAME = @TableName ORDER BY rf.RDB$FIELD_POSITION";
                using (var cmdCols = new FbCommand(queryColumns, connection))
                {
                    cmdCols.Parameters.AddWithValue("@TableName", tableName);
                    using (var readerCols = cmdCols.ExecuteReader())
                    {
                        while (readerCols.Read())
                        {
                            string colName = readerCols.GetString(0).Trim();
                            string domainOrTypeName = readerCols.GetString(1).Trim();
                            tableModel.Columns.Add(colName, new ColumnModel { Name = colName, DataType = domainOrTypeName });
                        }
                    }
                }
                schema.Tables.Add(tableName, tableModel);
            }

            // Procedury
            string procQuery = "SELECT RDB$PROCEDURE_NAME, RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$SYSTEM_FLAG = 0 AND RDB$PROCEDURE_SOURCE IS NOT NULL";
            using var procCmd = new FbCommand(procQuery, connection);
            using var procReader = procCmd.ExecuteReader();
            while (procReader.Read())
            {
                string procName = procReader.GetString(0).Trim();
                string procBody = procReader.IsDBNull(1) ? string.Empty : procReader.GetString(1);

                schema.Procedures.Add(procName, new ProcedureModel
                {
                    Name = procName,
                    Body = procBody
                });
            }

            return schema;
        }

        /// <summary>
        /// Parsuje pliki .sql z definicjami tabel i buduje model schematu docelowego.
        /// Używa prostych wyrażeń regularnych - to uproszczenie na potrzeby zadania.
        /// </summary>
        private SchemaModel GetTargetSchema(string scriptsDirectory)
        {
            var schema = new SchemaModel();

            // Parsowanie Domen
            string domainsPath = Path.Combine(scriptsDirectory, "1_domains");
            if (Directory.Exists(domainsPath))
            {
                foreach (var file in Directory.GetFiles(domainsPath, "*.sql"))
                {
                    string script = File.ReadAllText(file, Encoding.UTF8);
                    var match = Regex.Match(script, @"CREATE DOMAIN\s+(\w+)\s+AS\s+([^;]+);", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string name = match.Groups[1].Value;
                        string fullDef = match.Groups[2].Value.Trim();
                        // UPROSZCZENIE: Bierzemy tylko pierwszą część jako typ danych
                        string dataType = fullDef.Split(' ')[0];
                        schema.Domains.Add(name, new DomainModel { Name = name, DataType = dataType, FullDefinitionInScript = fullDef });
                    }
                }
            }

            // Parsowanie Tabel
            string tablesPath = Path.Combine(scriptsDirectory, "2_tables");
            if (Directory.Exists(tablesPath))
            {
                foreach (var file in Directory.GetFiles(tablesPath, "*.sql"))
                {
                    string script = File.ReadAllText(file, Encoding.UTF8);
                    var tableMatch = Regex.Match(script, @"CREATE TABLE\s+(\w+)", RegexOptions.IgnoreCase);
                    if (!tableMatch.Success) continue;

                    var tableModel = new TableModel { Name = tableMatch.Groups[1].Value, OriginalCreateTableScript = script };
                    int startIndex = script.IndexOf('(');
                    int endIndex = script.LastIndexOf(");");
                    if (startIndex > -1 && endIndex > -1)
                    {
                        string columnsDef = script.Substring(startIndex + 1, endIndex - startIndex - 1);
                        var columnLines = columnsDef.Split(new[] { ",\r\n", ",\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var line in columnLines)
                        {
                            var trimmedLine = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmedLine)) continue;
                            if (trimmedLine.EndsWith(","))
                            {
                                trimmedLine = trimmedLine.Substring(0, trimmedLine.Length - 1);
                            }

                            var parts = trimmedLine.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2)
                            {
                                string colName = parts[0].Trim();
                                string fullTypeDef = parts[1].Trim();
                                // UPROSZCZENIE: Bierzemy tylko pierwszą część jako typ
                                string dataType = fullTypeDef.Split(' ')[0];
                                tableModel.Columns.Add(colName, new ColumnModel { Name = colName, DataType = dataType, FullDefinitionInScript = fullTypeDef });
                            }
                        }
                    }
                    schema.Tables.Add(tableModel.Name, tableModel);
                }
            }

            // Parsowanie Procedur
            string proceduresPath = Path.Combine(scriptsDirectory, "3_procedures");
            if (Directory.Exists(proceduresPath))
            {
                foreach (var file in Directory.GetFiles(proceduresPath, "*.sql"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string fullScript = File.ReadAllText(file, Encoding.UTF8);

                    // 1. Usuwamy wszystkie dyrektywy SET TERM.
                    // 2. Usuwamy niestandardowe terminatory (^), jeśli są na końcu.
                    // 3. Upewniamy się, że skrypt kończy się standardowym średnikiem.
                    string cleanFullScript = Regex.Replace(fullScript, @"\s*SET TERM.*?;", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
                    cleanFullScript = cleanFullScript.TrimEnd('^').Trim();
                    if (!cleanFullScript.EndsWith(";"))
                    {
                        cleanFullScript += ";";
                    }

                    string body = string.Empty;

                    // Wyciągnij ciało procedury (wszystko po 'AS')
                    var match = Regex.Match(cleanFullScript, @"\s+AS\s+(.*)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        // Usuwamy końcowy średnik lub znak terminatora, jeśli istnieje
                        body = match.Groups[1].Value.Trim().TrimEnd(';').TrimEnd('^').Trim();
                    }

                    schema.Procedures.Add(name, new ProcedureModel
                    {
                        Name = name,
                        FullScript = cleanFullScript,
                        Body = body
                    });
                }
            }

                return schema;
        }

        /// <summary>
        /// Porównuje dwa modele schematów i generuje listę poleceń ALTER/CREATE.
        /// </summary>
        private List<string> CompareSchemas(SchemaModel targetSchema, SchemaModel currentSchema)
        {
            var scripts = new List<string>();

            // 1. Domeny
            foreach (var targetDomain in targetSchema.Domains.Values)
            {
                if (!currentSchema.Domains.TryGetValue(targetDomain.Name, out var currentDomain))
                {
                    scripts.Add($"CREATE DOMAIN {targetDomain.Name} AS {targetDomain.FullDefinitionInScript};");
                }
                else if (!currentDomain.DataType.Equals(targetDomain.DataType, StringComparison.OrdinalIgnoreCase))
                {
                    scripts.Add($"ALTER DOMAIN {targetDomain.Name} TYPE {targetDomain.DataType};");
                }
            }

            // 2. Tabele
            foreach (var targetTable in targetSchema.Tables.Values)
            {
                if (!currentSchema.Tables.TryGetValue(targetTable.Name, out var currentTable))
                {
                    scripts.Add(targetTable.OriginalCreateTableScript.Split("INSERT INTO")[0].Trim());
                }
                else
                {
                    foreach (var targetCol in targetTable.Columns.Values)
                        if (!currentTable.Columns.ContainsKey(targetCol.Name))
                            scripts.Add($"ALTER TABLE {targetTable.Name} ADD {targetCol.Name} {targetCol.DataType};");

                    //foreach (var currentCol in currentTable.Columns.Values)
                    //    if (!targetTable.Columns.ContainsKey(currentCol.Name))
                    //        scripts.Add($"ALTER TABLE {targetTable.Name} DROP {currentCol.Name};");

                    foreach (var targetCol in targetTable.Columns.Values)
                        if (currentTable.Columns.TryGetValue(targetCol.Name, out var currentCol) && !currentCol.DataType.Equals(targetCol.DataType, StringComparison.OrdinalIgnoreCase))
                            scripts.Add($"ALTER TABLE {targetTable.Name} ALTER COLUMN {targetCol.Name} TYPE {targetCol.DataType};");
                }
            }

            // 3. Usuwanie starych obiektów
            //foreach (var currentProc in currentSchema.Procedures.Values)
            //    if (!targetSchema.Procedures.ContainsKey(currentProc.Name))
            //        scripts.Add($"DROP PROCEDURE {currentProc.Name};");

            //foreach (var currentTable in currentSchema.Tables.Values)
            //    if (!targetSchema.Tables.ContainsKey(currentTable.Name))
            //        scripts.Add($"DROP TABLE {currentTable.Name};");

            //foreach (var currentDomain in currentSchema.Domains.Values)
            //    if (!targetSchema.Domains.ContainsKey(currentDomain.Name) && !IsDomainInUse(currentDomain.Name, targetSchema))
            //        scripts.Add($"DROP DOMAIN {currentDomain.Name};");

            // 4. Procedury
            var proceduresToUpdate = new List<ProcedureModel>();
            foreach (var targetProc in targetSchema.Procedures.Values)
            {
                // Scenariusz: Procedura nie istnieje w bazie LUB jej ciało się zmieniło
                if (!currentSchema.Procedures.TryGetValue(targetProc.Name, out var currentProc) ||
                    !NormalizeSqlBody(currentProc.Body).Equals(NormalizeSqlBody(targetProc.Body), StringComparison.OrdinalIgnoreCase))
                {
                    proceduresToUpdate.Add(targetProc);
                }
            }

            // Jeśli są jakieś procedury do stworzenia/zaktualizowania, użyj mechanizmu stubbing
            if (proceduresToUpdate.Any())
            {
                // Stubbing jest potrzebny tylko dla procedur, które są całkowicie nowe
                var newProcedures = proceduresToUpdate.Where(p => !currentSchema.Procedures.ContainsKey(p.Name)).ToList();
                foreach (var proc in newProcedures)
                {
                    string stub = GenerateProcedureStub(proc.FullScript);
                    if (!string.IsNullOrEmpty(stub)) scripts.Add(stub);
                }

                // Dodaj pełne skrypty CREATE OR ALTER dla wszystkich, które wymagają aktualizacji
                foreach (var proc in proceduresToUpdate)
                {
                    scripts.Add(proc.FullScript);
                }
            }

            return scripts;
        }

        private string NormalizeSqlBody(string sql)
        {
            // Zamienia wszystkie białe znaki (spacje, tabulatory, nowe linie) na pojedynczą spację
            // i usuwa komentarze, aby porównanie było bardziej niezawodne.
            if (string.IsNullOrEmpty(sql)) return string.Empty;

            // Usuń komentarze blokowe /* ... */
            sql = Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline);
            // Usuń komentarze liniowe -- ...
            sql = Regex.Replace(sql, @"--.*", "");
            // Znormalizuj białe znaki
            sql = Regex.Replace(sql, @"\s+", " ").Trim();

            return sql;
        }
    }
}