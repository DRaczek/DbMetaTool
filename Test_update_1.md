# Pierwszy krok - ręczny import poniższego skryptu do istniejącej bazy danych przez IBExpert -> Tools -> Script Executive
```sql
  
                         /* BAZA ŹRÓDŁOWA - zawiera wszystko, w tym obiekty "legacy" */

-- DOMENY
CREATE DOMAIN D_ID AS INTEGER;
CREATE DOMAIN D_TEXT AS VARCHAR(100) CHARACTER SET UTF8;
CREATE DOMAIN D_STATUS AS VARCHAR(20) CHARACTER SET UTF8; -- Ten typ zmienimy
CREATE DOMAIN D_LEGACY_CODE AS VARCHAR(10); -- Tę domenę "usuniemy" ze skryptów

COMMIT;

-- TABELE
CREATE TABLE EMPLOYEES (
    EmployeeID D_ID,
    EmployeeName D_TEXT,
    Status D_STATUS, -- Tę kolumnę zmodyfikujemy
    LegacyCode D_LEGACY_CODE -- Tę kolumnę "usuniemy" ze skryptów
);

CREATE TABLE DEPARTMENTS ( -- Tę tabelę "usuniemy" ze skryptów
    DepartmentID D_ID,
    DepartmentName D_TEXT
);
COMMIT;

-- DANE
INSERT INTO EMPLOYEES (EmployeeID, EmployeeName, Status, LegacyCode) VALUES (1, 'Anna Nowak', 'Active', 'A1');
INSERT INTO DEPARTMENTS (DepartmentID, DepartmentName) VALUES (10, 'IT');
COMMIT;

-- PROCEDURY
SET TERM ^ ;
CREATE PROCEDURE GET_EMPLOYEE_INFO (P_ID D_ID)
RETURNS (INFO D_TEXT)
AS
BEGIN
  /* Wersja 1 procedury */
  SELECT EmployeeName FROM EMPLOYEES WHERE EmployeeID = :P_ID INTO :INFO;
  SUSPEND;
END^

CREATE PROCEDURE ARCHIVE_OLD_DATA -- Tę procedurę "usuniemy" ze skryptów
AS
BEGIN
  -- Logika archiwizacji...
END^
SET TERM ; ^
COMMIT;
```

# Drugi krok - wywołanie export-scripts komendą np.
```bash
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\DB.FDB;DataSource=localhost;Port=3050;Dialect=3;" --output-dir "C:\workspace\Sente\db\scripts\test1"
```

Trzeci krok - ręczna modyfikacja niektórych skryptów:
- Usuń plik: D_LEGACY_CODE.sql.
- Edytuj plik D_STATUS.sql: Zmień jego zawartość na:
```sql
CREATE DOMAIN D_STATUS AS VARCHAR(2000) CHARACTER SET UTF8;
```
- Dodaj nowy plik D_EMAIL.sql z zawartością:
 ```sql
CREATE DOMAIN D_EMAIL AS VARCHAR(255) CHARACTER SET UTF8;
```
- Usuń plik: DEPARTMENTS.sql.
- Edytuj plik EMPLOYEES.sql:
```sql
CREATE TABLE EMPLOYEES (
    EmployeeID D_ID,
    EmployeeName D_TEXT,
    Status D_STATUS,
    Email D_EMAIL
);
```
- Dodaj nowy plik PROJECTS.sql
```sql
CREATE TABLE PROJECTS (
    ProjectID D_ID,
    ProjectName D_TEXT
);
```
- Usuń plik: ARCHIVE_OLD_DATA.sql.
- Edytuj plik GET_EMPLOYEE_INFO.sql
```sql
SET TERM ^ ;
CREATE OR ALTER PROCEDURE GET_EMPLOYEE_INFO (P_ID D_ID)
RETURNS (INFO D_TEXT)
AS
BEGIN
  /* Wersja 2 procedury - zmieniona logika */
  SELECT 'Employee: ' || EmployeeName FROM EMPLOYEES WHERE EmployeeID = :P_ID INTO :INFO;
  SUSPEND;
END^
SET TERM ; ^
```
- Dodaj nowy plik CALCULATE_BONUS.sql
```sql
SET TERM ^ ;
CREATE OR ALTER PROCEDURE CALCULATE_BONUS (P_ID D_ID)
RETURNS (BONUS_AMOUNT NUMERIC(10,2))
AS
BEGIN
  BONUS_AMOUNT = 1000.00;
  SUSPEND;
END^
SET TERM ; ^
```
# Trzeci krok - uruchom update-db przez np.
```bash
dotnet run -- update-db --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\DB.FDB;DataSource=localhost;Port=3050;Dialect=3;Charset=UTF8;" --scripts-dir "C:\workspace\Sente\db\scripts\test1"
```
# Czwarty Krok - uruchom ponownie export-scripts do innego folderu docelowego np.
```bash
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\DB.FDB;DataSource=localhost;Port=3050;Dialect=3;" --output-dir "C:\workspace\Sente\db\scripts\test2"
```
# Piąty krok - porównaj zmodyfikowane ręcznie skrypty z wyeksportowanymi z zaktualizowanej bazy danych

Konsola:
``` bash
Rozpoczynanie uproszczonej aktualizacji bazy danych...

[Analiza] Znaleziono definicje dla 4 domen, 2 tabel i 2 procedur w skryptach.
[Analiza] Znaleziono 4 domen, 2 tabel i 2 procedur w bazie.

[Migracja] Znaleziono 7 operacji do wykonania.
> Wykonywanie: CREATE DOMAIN D_EMAIL AS VARCHAR(255) CHARACTER SET UTF8;...
> Wykonywanie: ALTER DOMAIN D_STATUS TYPE VARCHAR(2000);...
> Wykonywanie: ALTER TABLE EMPLOYEES ADD Email D_EMAIL;...
> Wykonywanie: CREATE TABLE PROJECTS (...
> Wykonywanie: SET TERM ^ ;...
> Wykonywanie: SET TERM ^ ;...
> Wykonywanie: SET TERM ^ ;...

[Sukces] Aktualizacja zakończona.
Baza danych została zaktualizowana pomyślnie.
```


## Oryginalne ręcznie zmodyfikowane skrypty : 
```sql
CREATE DOMAIN D_EMAIL AS VARCHAR(255) CHARACTER SET UTF8;
CREATE DOMAIN D_ID AS INTEGER;
CREATE DOMAIN D_STATUS AS VARCHAR(2000) CHARACTER SET UTF8;
CREATE DOMAIN D_TEXT AS VARCHAR(100) CHARACTER SET UTF8;

CREATE TABLE EMPLOYEES (
    EmployeeID D_ID,
    EmployeeName D_TEXT,
    Status D_STATUS,
    Email D_EMAIL
);
CREATE TABLE PROJECTS (
    ProjectID D_ID,
    ProjectName D_TEXT
);

SET TERM ^ ;
CREATE OR ALTER PROCEDURE CALCULATE_BONUS (P_ID D_ID)
RETURNS (BONUS_AMOUNT NUMERIC(10,2))
AS
BEGIN
  BONUS_AMOUNT = 1000.00;
  SUSPEND;
END^
SET TERM ; ^

SET TERM ^ ;
CREATE OR ALTER PROCEDURE GET_EMPLOYEE_INFO (P_ID D_ID)
RETURNS (INFO D_TEXT)
AS
BEGIN
  /* Wersja 2 procedury - zmieniona logika */
  SELECT 'Employee: ' || EmployeeName FROM EMPLOYEES WHERE EmployeeID = :P_ID INTO :INFO;
  SUSPEND;
END^
SET TERM ; ^
```

## Skrypty skrypty wygenerowane po ponownym export-scripts : 
```sql
CREATE DOMAIN D_EMAIL AS VARCHAR(255) CHARACTER SET UTF8;
CREATE DOMAIN D_ID AS INTEGER;
CREATE DOMAIN D_LEGACY_CODE AS VARCHAR(10) CHARACTER SET UTF8;
CREATE DOMAIN D_STATUS AS VARCHAR(2000) CHARACTER SET UTF8;
CREATE DOMAIN D_TEXT AS VARCHAR(100) CHARACTER SET UTF8;

CREATE TABLE DEPARTMENTS (
  DEPARTMENTID D_ID,
  DEPARTMENTNAME D_TEXT
);
CREATE TABLE EMPLOYEES (
  EMPLOYEEID D_ID,
  EMPLOYEENAME D_TEXT,
  STATUS D_STATUS,
  LEGACYCODE D_LEGACY_CODE,
  EMAIL D_EMAIL
);
CREATE TABLE PROJECTS (
  PROJECTID D_ID,
  PROJECTNAME D_TEXT
);

SET TERM ^ ;
CREATE OR ALTER PROCEDURE ARCHIVE_OLD_DATA AS
BEGIN
  -- Logika archiwizacji...
END
^
SET TERM ; ^
SET TERM ^ ;
CREATE OR ALTER PROCEDURE CALCULATE_BONUS (P_ID D_ID) RETURNS (BONUS_AMOUNT NUMERIC(10, 2)) AS
BEGIN
  BONUS_AMOUNT = 1000.00;
  SUSPEND;
END
^
SET TERM ; ^
SET TERM ^ ;
CREATE OR ALTER PROCEDURE GET_EMPLOYEE_INFO (P_ID D_ID) RETURNS (INFO D_TEXT) AS
BEGIN
  /* Wersja 2 procedury - zmieniona logika */
  SELECT 'Employee: ' || EmployeeName FROM EMPLOYEES WHERE EmployeeID = :P_ID INTO :INFO;
  SUSPEND;
END
^
SET TERM ; ^
```
# Wnioski : 
- Baza danych została pomyślnie zaktualizowana
## Uwagi:
- Istniejące struktury danych nie zostały usunięte, nie byłem pewny czy założenia zakładały że baza ma być dokładnie wyrównana do skryptów czy ma dodawać/modyfikować nie usuwając istniejących danych więc założyłem że nie usunę istniejących domen/tabel/kolumn/procedur niezgodnych ze skryptami
- 
W pliku `DatabaseBuilder.cs` w funkcji `CompareSchemas(SchemaModel targetSchema, SchemaModel currentSchema)` istnieje zakomentowany zapis:
```csharp
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
```
oraz
```csharp
    //foreach (var currentCol in currentTable.Columns.Values)
    //    if (!targetTable.Columns.ContainsKey(currentCol.Name))
    //        scripts.Add($"ALTER TABLE {targetTable.Name} DROP {currentCol.Name};");
```
Które odpowiadają za usuwanie niezgodnych ze skryptami domen/tabel/kolumn/procedur

Odkomentowanie tych lini kodu powinno usunąć niezgodne struktury bazy danych i dokładnie wyrównać baze danych do skryptów.











