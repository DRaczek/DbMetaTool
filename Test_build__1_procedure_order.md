# Problem
Procedury mogą wywoływać inne procedury - należy zaimportować je w odpowiedniej kolejności
# Rozwiązanie
Aby nie musieć szukać odwołań do procedur i obsłużyć również zależności cykliczne i hierarchiczne wykonuje:
- Pobieram nagłowek procedury - nazwe i parametry
- Tworze procedurę z uzupełnioną nazwą i parametrami ale bez ciała
- Wykonuje CREATE OR ALTER PROCEDURE uzupełniając istniejący już nagłówek procedury treścią

# Pierwszy krok - ręczny import poniższego skryptu do istniejącej bazy danych przez IBExpert -> Tools -> Script Executive
```sql

                            /* BAZA TESTOWA Z ZALEŻNOŚCIAMI HIERARCHICZNYMI I CYKLICZNYMI */

-- DOMENY
CREATE DOMAIN D_ID AS INTEGER;
CREATE DOMAIN D_TEXT AS VARCHAR(255) CHARACTER SET UTF8;
CREATE DOMAIN D_BOOL AS BOOLEAN;
COMMIT;

-- TABELE
CREATE TABLE PRODUCTS (ProductID D_ID, StockQuantity INTEGER);
CREATE TABLE USERS (UserID D_ID, UserName D_TEXT);
CREATE TABLE EVENT_LOGS (LogMessage D_TEXT);
COMMIT;

-- PROCEDURY
SET TERM ^ ;

/* --- SCENARIUSZ HIERARCHICZNY (A -> B) --- */

-- Procedura "podrzędna"
CREATE PROCEDURE B_CHECK_STOCK (
    P_PRODUCT_ID D_ID,
    P_QUANTITY INTEGER)
RETURNS (
    IS_AVAILABLE D_BOOL)
AS
DECLARE VARIABLE V_STOCK_QTY INTEGER;
BEGIN
    SELECT StockQuantity FROM PRODUCTS WHERE ProductID = :P_PRODUCT_ID INTO :V_STOCK_QTY;
    IS_AVAILABLE = (V_STOCK_QTY >= P_QUANTITY);
    SUSPEND;
END^

-- Procedura "nadrzędna", która wywołuje podrzędną
CREATE PROCEDURE A_VALIDATE_ORDER (
    P_PRODUCT_ID D_ID,
    P_QUANTITY INTEGER)
RETURNS (
    IS_VALID D_BOOL)
AS
DECLARE VARIABLE V_IS_AVAILABLE D_BOOL;
BEGIN
    -- Wywołanie procedury CHECK_STOCK
    EXECUTE PROCEDURE B_CHECK_STOCK(:P_PRODUCT_ID, :P_QUANTITY) RETURNING_VALUES :V_IS_AVAILABLE;
    
    IF (V_IS_AVAILABLE) THEN
        IS_VALID = TRUE;
    ELSE
        IS_VALID = FALSE;
        
    SUSPEND;
END^

```

# Drugi krok - wywołanie export-scripts komendą np.
```bash
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\DB.FDB;DataSource=localhost;Port=3050;Dialect=3;" --output-dir "C:\workspace\Sente\db\scripts\test1"
```

# Trzeci krok - wywołanie build-db komendą np.
```bash
dotnet run -- build-db --db-dir "C:\workspace\Sente\db\new" --scripts-dir "C:\workspace\Sente\db\scripts\test1"
```
Konsola : 
```bash
Tworzenie nowej bazy danych w: C:\workspace\Sente\db\new\db_20251123_195522.fdb
Pusta baza danych została utworzona pomyślnie.
Wykonywanie skryptów z katalogu: C:\workspace\Sente\db\scripts\test1
Połączono z bazą danych w celu wykonania skryptów.
Znaleziono 8 plików .sql do wykonania.

--- Faza 1: Wykonywanie skryptów podstawowych (domeny, tabele, dane) ---
-- Wykonywanie skryptu: D_BOOL.sql
-- Wykonywanie skryptu: D_ID.sql
-- Wykonywanie skryptu: D_TEXT.sql
-- Wykonywanie skryptu: EVENT_LOGS.sql
-- Wykonywanie skryptu: PRODUCTS.sql
-- Wykonywanie skryptu: USERS.sql
Pomyślnie wykonano 0 skryptów.

--- Faza 2a: Tworzenie 'zaślepek' procedur (Stubbing) ---
-- Tworzenie zaślepki dla: A_VALIDATE_ORDER.sql
-- Tworzenie zaślepki dla: B_CHECK_STOCK.sql
Faza 2a zakończona pomyślnie.

--- Faza 2b: Wypełnianie procedur właściwą logiką ---
-- Wykonywanie skryptu: A_VALIDATE_ORDER.sql
-- Wykonywanie skryptu: B_CHECK_STOCK.sql
Faza 2b zakończona pomyślnie. Wszystkie procedury zostały zaktualizowane.
Baza danych została zbudowana pomyślnie.
```

# Czwarty krok - ponowne wykonanie export-scripts, np.
```bash
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\new\DB_20251123_200208.FDB;DataSource=localhost;Port=3050;Dialect=3;" --output-dir "C:\workspace\Sente\db\scripts\test2"
```

# Piąty krok - sprawdzenie poprawności działania

Skrypty originalnej bazy danych
```sql
CREATE DOMAIN D_BOOL AS BOOLEAN;
CREATE DOMAIN D_ID AS INTEGER;
CREATE DOMAIN D_TEXT AS VARCHAR(255) CHARACTER SET UTF8;

CREATE TABLE EVENT_LOGS (
  LOGMESSAGE D_TEXT
);
CREATE TABLE PRODUCTS (
  PRODUCTID D_ID,
  STOCKQUANTITY INTEGER
);
CREATE TABLE USERS (
  USERID D_ID,
  USERNAME D_TEXT
);

SET TERM ^ ;
CREATE OR ALTER PROCEDURE A_VALIDATE_ORDER (P_PRODUCT_ID D_ID, P_QUANTITY INTEGER) RETURNS (IS_VALID D_BOOL) AS
DECLARE VARIABLE V_IS_AVAILABLE D_BOOL;
BEGIN
    -- Wywołanie procedury CHECK_STOCK
    EXECUTE PROCEDURE B_CHECK_STOCK(:P_PRODUCT_ID, :P_QUANTITY) RETURNING_VALUES :V_IS_AVAILABLE;
    
    IF (V_IS_AVAILABLE) THEN
        IS_VALID = TRUE;
    ELSE
        IS_VALID = FALSE;
        
    SUSPEND;
END
^
SET TERM ; ^

SET TERM ^ ;
CREATE OR ALTER PROCEDURE B_CHECK_STOCK (P_PRODUCT_ID D_ID, P_QUANTITY INTEGER) RETURNS (IS_AVAILABLE D_BOOL) AS
DECLARE VARIABLE V_STOCK_QTY INTEGER;
BEGIN
    SELECT StockQuantity FROM PRODUCTS WHERE ProductID = :P_PRODUCT_ID INTO :V_STOCK_QTY;
    IS_AVAILABLE = (V_STOCK_QTY >= P_QUANTITY);
    SUSPEND;
END
^
SET TERM ; ^
```

Skrypty wygenerowane na podstawie nowo stworzonej bazy danych:
```sql
CREATE DOMAIN D_BOOL AS BOOLEAN;
CREATE DOMAIN D_ID AS INTEGER;
CREATE DOMAIN D_TEXT AS VARCHAR(255) CHARACTER SET UTF8;

CREATE TABLE EVENT_LOGS (
  LOGMESSAGE D_TEXT
);
CREATE TABLE PRODUCTS (
  PRODUCTID D_ID,
  STOCKQUANTITY INTEGER
);
CREATE TABLE USERS (
  USERID D_ID,
  USERNAME D_TEXT
);

SET TERM ^ ;
CREATE OR ALTER PROCEDURE A_VALIDATE_ORDER (P_PRODUCT_ID D_ID, P_QUANTITY INTEGER) RETURNS (IS_VALID D_BOOL) AS
DECLARE VARIABLE V_IS_AVAILABLE D_BOOL;
BEGIN
    -- Wywołanie procedury CHECK_STOCK
    EXECUTE PROCEDURE B_CHECK_STOCK(:P_PRODUCT_ID, :P_QUANTITY) RETURNING_VALUES :V_IS_AVAILABLE;
    
    IF (V_IS_AVAILABLE) THEN
        IS_VALID = TRUE;
    ELSE
        IS_VALID = FALSE;
        
    SUSPEND;
END
^
SET TERM ; ^

SET TERM ^ ;
CREATE OR ALTER PROCEDURE B_CHECK_STOCK (P_PRODUCT_ID D_ID, P_QUANTITY INTEGER) RETURNS (IS_AVAILABLE D_BOOL) AS
DECLARE VARIABLE V_STOCK_QTY INTEGER;
BEGIN
    SELECT StockQuantity FROM PRODUCTS WHERE ProductID = :P_PRODUCT_ID INTO :V_STOCK_QTY;
    IS_AVAILABLE = (V_STOCK_QTY >= P_QUANTITY);
    SUSPEND;
END
^
SET TERM ; ^
```

# Wnioski
Baza zbudowana komendą ma taką samą strukturę jak oryginalna baza.








