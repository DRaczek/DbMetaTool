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

                string domainsDir = Path.Combine(outputDirectory, "domains");
                string tablesDir = Path.Combine(outputDirectory, "tables");
                string proceduresDir = Path.Combine(outputDirectory, "procedures");
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
                    RDB$FIELD_NAME,                 -- (0)
                    RDB$FIELD_LENGTH,               -- (1)
                    RDB$FIELD_TYPE,                 -- (2)
                    RDB$FIELD_SUB_TYPE,             -- (3)
                    RDB$FIELD_SCALE,                -- (4)
                    RDB$FIELD_PRECISION,            -- (5)
                    RDB$CHARACTER_LENGTH,           -- (6)
                    RDB$NULL_FLAG                   -- (7)
                FROM RDB$FIELDS
                WHERE RDB$SYSTEM_FLAG = 0 AND RDB$FIELD_NAME NOT LIKE 'RDB$%'";

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

                string dataType = Utils.GetSqlType(fieldType, subType, precision, scale, charLength);

                string script = $"CREATE DOMAIN {fieldName} AS {dataType}" + (isNullable ? "" : " NOT NULL") + ";";
                File.WriteAllText(Path.Combine(outputDir, $"{fieldName}.sql"), script);
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
            string query = @"
                SELECT RDB$PROCEDURE_NAME, RDB$PROCEDURE_SOURCE
                FROM RDB$PROCEDURES
                WHERE RDB$SYSTEM_FLAG = 0 AND RDB$PROCEDURE_SOURCE IS NOT NULL";

            using var command = new FbCommand(query, _connection);
            using var reader = command.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                string procName = reader.GetString(0).Trim();
                string procSource = reader.GetString(1);
                string script = $"SET TERM ^ ;\n{procSource}\nSET TERM ; ^";
                File.WriteAllText(Path.Combine(outputDir, $"{procName}.sql"), script);
                count++;
            }
            Console.WriteLine($"Wyeksportowano {count} procedur.");
        }


        private void AppendCreateTableScript(string tableName, StringBuilder sb)
        {
            sb.AppendLine($"CREATE TABLE {tableName} (");

            string queryColumnsFormat = @"
                    SELECT 
                        rf.RDB$FIELD_NAME,                                      -- Nazwa kolumny w tabeli (0)
                        f.RDB$FIELD_TYPE,                                       -- Typ pola (1)
                        f.RDB$FIELD_SUB_TYPE,                                   -- Podtyp (np. dla BLOB, NUMERIC) (2)
                        f.RDB$FIELD_LENGTH,                                     -- Długość (3)
                        f.RDB$FIELD_SCALE,                                      
                        rf.RDB$NULL_FLAG,                                       -- Czy może być NULL (5)
                        rf.RDB$FIELD_SOURCE,                                    -- Nazwa domeny lub pola globalnego (6)
                        f.RDB$FIELD_PRECISION,                                  -- Precyzja (dla NUMERIC) (7)
                        f.RDB$CHARACTER_LENGTH                                  -- Długość znakowa (dla CHAR, VARCHAR) (8)
                    FROM RDB$RELATION_FIELDS rf                                 -- FROM Kolumny tabeli
                    JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME -- JOIN Typy pól i ich właściwości
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

                if (fieldSource.StartsWith("RDB$"))
                {
                    short fieldType = readerCols.GetInt16(1);
                    short fieldLength = readerCols.GetInt16(3);
                    short subType = readerCols.IsDBNull(2) ? (short)0 : readerCols.GetInt16(2);
                    short scale = readerCols.IsDBNull(4) ? (short)0 : readerCols.GetInt16(4);
                    short precision = readerCols.IsDBNull(7) ? (short)0 : readerCols.GetInt16(7);
                    short charLength = readerCols.IsDBNull(8) ? (short)0 : readerCols.GetInt16(8);
                    colType = Utils.GetSqlType(fieldType, subType, precision, scale, charLength);
                }
                else
                {
                    colType = fieldSource;
                }

                bool isNullable = readerCols.IsDBNull(5) || readerCols.GetInt16(5) == 0;
                columnDefinitions.Add($"  {colName} {colType}" + (isNullable ? "" : " NOT NULL"));
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
