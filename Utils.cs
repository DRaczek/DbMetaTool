namespace Sente
{
    public static class Utils
    {
        public static string GetSqlType(short type, short subType, short length, short scale, short charLength = 0)
        {
            //https://ib-aid.com/download/docs/firebird-language-reference-2.5/fblangref-appx04-fields.html
            return (type) switch
            {
                7 => "SMALLINT",
                8 => "INTEGER",
                10 => "FLOAT",
                12 => "DATE",
                13 => "TIME",
                14 => $"CHAR({charLength})",
                16 when subType == 1 => $"NUMERIC({length}, {-scale})", 
                16 when subType == 2 => $"DECIMAL({length}, {-scale})", 
                16 => "BIGINT",
                23 => "BOOLEAN",
                27 => "DOUBLE PRECISION",
                35 => "TIMESTAMP",
                37 => $"VARCHAR({charLength})",
                261 when subType == 0 => "BLOB SUB_TYPE BINARY",
                261 when subType == 1 => "BLOB SUB_TYPE TEXT",
                _ => "UNKNOWN"
            };
        }

        public static string FormatSqlValue(object value)
        {
            if (value is null || value is DBNull)
            {
                return "NULL";
            }
            if (value is string strValue)
            {
                // Ucieczka apostrofów i opakowanie w apostrofy
                return $"'{strValue.Replace("'", "''")}'";
            }
            if (value is DateTime dtValue)
            {
                // Standardowy format daty i czasu dla Firebird
                return $"'{dtValue:yyyy-MM-dd HH:mm:ss.fff}'";
            }
            if (value is bool boolValue)
            {
                return boolValue ? "TRUE" : "FALSE";
            }
            if (value is IFormattable formattable)
            {
                // Dla liczb (int, decimal, double, etc.) używamy formatu z kropką jako separatorem dziesiętnym
                return formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            }
            // Domyślna obsługa dla innych typów
            return value.ToString() ?? "NULL";
        }
    }
}
