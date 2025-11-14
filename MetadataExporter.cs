using FirebirdSql.Data.FirebirdClient;
using System.Text;

namespace Sente
{
    public class MetadataExporter
    {
        private readonly FbConnection _connection;

        public MetadataExporter(string connectionString)
        {
            _connection = new FbConnection(connectionString);
        }

        public void ExportTo(string outputDirectory)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);

                // 1) Połącz się z bazą danych przy użyciu connectionString.
                _connection.Open();

                string domainsDir = Path.Combine(outputDirectory, "1_domains");
                string tablesDir = Path.Combine(outputDirectory, "2_tables");
                string proceduresDir = Path.Combine(outputDirectory, "3_procedures");
                Directory.CreateDirectory(domainsDir);
                Directory.CreateDirectory(tablesDir);
                Directory.CreateDirectory(proceduresDir);

                // 2) Pobierz metadane domen, tabel (z kolumnami) i procedur.
                // 3) Wygeneruj pliki .sql / .json / .txt w outputDirectory.
                ExportDomains(domainsDir);
                ExportTables(tablesDir);
                ExportProcedures(proceduresDir);
            }
            finally
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                    Console.WriteLine("Zakończono eksport. Połączenie z bazą danych zostało zamknięte.");
                }
            }
        }

        private void ExportDomains(string outputDir)
        {
            string query = @"
                SELECT 
                    f.RDB$FIELD_NAME,                 -- (0)
                    f.RDB$FIELD_LENGTH,               -- (1)
                    f.RDB$FIELD_TYPE,                 -- (2)
                    f.RDB$FIELD_SUB_TYPE,             -- (3)
                    f.RDB$FIELD_SCALE,                -- (4)
                    f.RDB$FIELD_PRECISION,            -- (5)
                    f.RDB$CHARACTER_LENGTH,           -- (6)
                    f.RDB$NULL_FLAG,                  -- (7)
                    f.RDB$DEFAULT_SOURCE,             -- (8)
                    cs.RDB$CHARACTER_SET_NAME         -- (9) 
                FROM RDB$FIELDS f
                LEFT JOIN RDB$CHARACTER_SETS cs ON f.RDB$CHARACTER_SET_ID = cs.RDB$CHARACTER_SET_ID
                WHERE f.RDB$SYSTEM_FLAG = 0 AND f.RDB$FIELD_NAME NOT LIKE 'RDB$%'";

            using var command = new FbCommand(query, _connection);
            using var reader = command.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                string fieldName = reader.GetString(0).Trim();

                short fieldType = reader.GetInt16(2);
                short fieldLength = reader.GetInt16(1);
                short subType = reader.IsDBNull(3) ? (short)0 : reader.GetInt16(3);
                short scale = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4);
                short precision = reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5);
                short charLength = reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6);
                bool isNullable = reader.IsDBNull(7) || reader.GetInt16(7) == 0;
                string? defaultSource = reader.IsDBNull(8) ? null : reader.GetString(8);
                string? charSetName = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();

                string dataType = Utils.GetSqlType(fieldType, subType, precision, scale, charLength);

                var scriptBuilder = new StringBuilder();
                scriptBuilder.Append($"CREATE DOMAIN {fieldName} AS {dataType}");

                if (!string.IsNullOrEmpty(charSetName) 
                    && (dataType.StartsWith("CHAR") 
                    || dataType.StartsWith("VARCHAR") 
                    || dataType.Contains("BLOB SUB_TYPE TEXT")))
                {
                    scriptBuilder.Append($" CHARACTER SET {charSetName}");
                }
                if (!string.IsNullOrEmpty(defaultSource))
                {
                    scriptBuilder.Append($" {defaultSource.Trim()}");
                }
                if (!isNullable)
                {
                    scriptBuilder.Append(" NOT NULL");
                }
                scriptBuilder.Append(";");  

                File.WriteAllText(Path.Combine(outputDir, $"{fieldName}.sql"), scriptBuilder.ToString());
                count++;
            }
            Console.WriteLine($"Wyeksportowano {count} domen.");
        }   

        private void ExportTables(string outputDir)
        {
            string queryTables = @"
                SELECT RDB$RELATION_NAME
                FROM RDB$RELATIONS
                WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL";

            var tableNames = new List<string>();
            using (var cmd = new FbCommand(queryTables, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) tableNames.Add(reader.GetString(0).Trim());
            }

            int count = 0;
            foreach (var tableName in tableNames)
            {
                var sb = new StringBuilder();

                AppendCreateTableScript(tableName, sb);
                AppendInsertScripts(tableName, sb);

                File.WriteAllText(Path.Combine(outputDir, $"{tableName}.sql"), sb.ToString());
                count++;
            }
            Console.WriteLine($"Wyeksportowano {count} tabel.");
        }

        private void ExportProcedures(string outputDir)
        {
            // pobieranie procedury
            var procedures = new List<(string Name, string Source)>();
            string queryProcs = @"
                SELECT RDB$PROCEDURE_NAME,
                RDB$PROCEDURE_SOURCE
                FROM RDB$PROCEDURES
                WHERE RDB$SYSTEM_FLAG = 0 AND RDB$PROCEDURE_SOURCE IS NOT NULL";

            using (var cmdProcs = new FbCommand(queryProcs, _connection))
            using (var readerProcs = cmdProcs.ExecuteReader())
            {
                while (readerProcs.Read())
                {
                    procedures.Add((readerProcs.GetString(0).Trim(), readerProcs.GetString(1)));
                }
            }

            // pobieranie danych i/o parametrów
            string queryParams = @"
                SELECT
                    p.RDB$PARAMETER_NAME,                                   -- (0)
                    p.RDB$PARAMETER_TYPE, -- 0 = input, 1 = output          -- (1)
                    f.RDB$FIELD_TYPE,                                       -- (2)
                    f.RDB$FIELD_SUB_TYPE,                                   -- (3)
                    f.RDB$FIELD_LENGTH,                                     -- (4)
                    f.RDB$FIELD_SCALE,                                      -- (5)
                    f.RDB$FIELD_PRECISION,                                  -- (6)
                    f.RDB$CHARACTER_LENGTH,                                 -- (7)
                    p.RDB$FIELD_SOURCE                                      -- (8)
                FROM RDB$PROCEDURE_PARAMETERS p
                JOIN RDB$FIELDS f ON p.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                WHERE p.RDB$PROCEDURE_NAME = @ProcName AND p.RDB$SYSTEM_FLAG = 0
                ORDER BY p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER";

            int count = 0;
            foreach (var (procName, procSource) in procedures)
            {
                var inputParams = new List<string>();
                var outputParams = new List<string>();

                using (var cmdParams = new FbCommand(queryParams, _connection))
                {
                    cmdParams.Parameters.AddWithValue("@ProcName", procName);
                    using (var readerParams = cmdParams.ExecuteReader())
                    {
                        while (readerParams.Read())
                        {
                            string paramName = readerParams.GetString(0).Trim();
                            int paramType = readerParams.GetInt16(1);
                            string domainName = readerParams.GetString(8).Trim();
                            string dataType;

                            if (domainName.StartsWith("RDB$"))
                            {
                                short fieldType = readerParams.GetInt16(2);
                                short subType = readerParams.IsDBNull(3) ? (short)0 : readerParams.GetInt16(3);
                                short precision = readerParams.IsDBNull(6) ? (short)0 : readerParams.GetInt16(6);
                                short scale = readerParams.IsDBNull(5) ? (short)0 : readerParams.GetInt16(5);
                                short charLength = readerParams.IsDBNull(7) ? (short)0 : readerParams.GetInt16(7);
                                dataType = Utils.GetSqlType(fieldType, subType, precision, scale, charLength);
                            }
                            else
                            {
                                dataType = domainName;
                            }

                            string paramDefinition = $"{paramName} {dataType}";

                            if (paramType == 0)
                                inputParams.Add(paramDefinition);
                            else
                                outputParams.Add(paramDefinition);
                        }
                    }
                }

                // Generowanie skryptu procedury
                var sb = new StringBuilder();
                sb.AppendLine("SET TERM ^ ;");
                sb.Append($"CREATE PROCEDURE {procName}");

                if (inputParams.Any())
                    sb.Append($" ({string.Join(", ", inputParams)})");

                if (outputParams.Any())
                    sb.Append($" RETURNS ({string.Join(", ", outputParams)})");

                sb.AppendLine(" AS");
                sb.AppendLine(procSource);
                sb.AppendLine("^");
                sb.AppendLine("SET TERM ; ^");

                File.WriteAllText(Path.Combine(outputDir, $"{procName}.sql"), sb.ToString());
                count++;
            }

            Console.WriteLine($"Wyeksportowano {count} procedur.");
        }


        private void AppendCreateTableScript(string tableName, StringBuilder sb)
        {
            sb.AppendLine($"CREATE TABLE {tableName} (");

            string queryColumnsFormat = @"
                    SELECT 
                        rf.RDB$FIELD_NAME,                                          -- Nazwa kolumny w tabeli (0)
                        f.RDB$FIELD_TYPE,                                           -- Typ pola (1)
                        f.RDB$FIELD_SUB_TYPE,                                       -- Podtyp (np. dla BLOB, NUMERIC) (2)
                        f.RDB$FIELD_LENGTH,                                         -- Długość (3)
                        f.RDB$FIELD_SCALE,                                      
                        rf.RDB$NULL_FLAG,                                           -- Czy może być NULL (5)
                        rf.RDB$FIELD_SOURCE,                                        -- Nazwa domeny lub pola globalnego (6)
                        f.RDB$FIELD_PRECISION,                                      -- Precyzja (dla NUMERIC) (7)
                        f.RDB$CHARACTER_LENGTH,                                     -- Długość znakowa (dla CHAR, VARCHAR) (8)
                        rf.RDB$DEFAULT_SOURCE,                                      -- Wartość domyślna kolumny (9)
                        cs.RDB$CHARACTER_SET_NAME                                   -- Zestaw znaków (10)
                    FROM RDB$RELATION_FIELDS rf                                     -- FROM Kolumny tabeli
                    JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME     -- JOIN Typy pól i ich właściwości
                    LEFT JOIN RDB$CHARACTER_SETS cs ON f.RDB$CHARACTER_SET_ID = cs.RDB$CHARACTER_SET_ID
                    WHERE rf.RDB$RELATION_NAME = @TableName
                    ORDER BY rf.RDB$FIELD_POSITION";

            using var cmdCols = new FbCommand(queryColumnsFormat, _connection);
            cmdCols.Parameters.AddWithValue("@TableName", tableName);

            using var readerCols = cmdCols.ExecuteReader();
            var columnDefinitions = new List<string>();
            while (readerCols.Read())
            {
                string colName = readerCols.GetString(0).Trim();
                string fieldSource = readerCols.GetString(6).Trim();
                string colType;
                string? columnDefaultSource = readerCols.IsDBNull(9) ? null : readerCols.GetString(9).Trim();
                string? charSetName = readerCols.IsDBNull(10) ? null : readerCols.GetString(10).Trim();

                if (fieldSource.StartsWith("RDB$"))
                {
                    short fieldType = readerCols.GetInt16(1);
                    short fieldLength = readerCols.GetInt16(3);
                    short subType = readerCols.IsDBNull(2) ? (short)0 : readerCols.GetInt16(2);
                    short scale = readerCols.IsDBNull(4) ? (short)0 : readerCols.GetInt16(4);
                    short precision = readerCols.IsDBNull(7) ? (short)0 : readerCols.GetInt16(7);
                    short charLength = readerCols.IsDBNull(8) ? (short)0 : readerCols.GetInt16(8);
                    colType = Utils.GetSqlType(fieldType, subType, precision, scale, charLength);

                    if (!string.IsNullOrEmpty(charSetName) 
                        && (colType.StartsWith("CHAR") 
                        || colType.StartsWith("VARCHAR") 
                        || colType.Contains("BLOB SUB_TYPE TEXT")))
                    {
                        colType += $" CHARACTER SET {charSetName}";
                    }
                }
                else
                {
                    colType = fieldSource;
                }

                bool isNullable = readerCols.IsDBNull(5) || readerCols.GetInt16(5) == 0;
                var colDefBuilder = new StringBuilder($"  {colName} {colType}");
                if (!string.IsNullOrEmpty(columnDefaultSource))
                {
                    colDefBuilder.Append($" {columnDefaultSource}");
                }

                if (!isNullable)
                {
                    colDefBuilder.Append(" NOT NULL");
                }

                columnDefinitions.Add(colDefBuilder.ToString());
            }

            sb.AppendLine(string.Join(",\n", columnDefinitions));
            sb.AppendLine(");");
            sb.AppendLine();
        }

        private void AppendInsertScripts(string tableName, StringBuilder sb)
        {

            using var cmdData = new FbCommand($"SELECT * FROM \"{tableName}\"", _connection);
            using var readerData = cmdData.ExecuteReader();

            while (readerData.Read())
            {
                var values = Enumerable.Range(0, readerData.FieldCount)
                                       .Select(i => Utils.FormatSqlValue(readerData.GetValue(i)))
                                       .ToList();
                sb.AppendLine($"INSERT INTO \"{tableName}\" VALUES ({string.Join(", ", values)});");
            }
        }
    }
}
