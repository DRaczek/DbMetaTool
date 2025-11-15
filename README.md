# DbMetaTool - Narzędzie do Zarządzania Metadanymi Bazy Firebird

**DbMetaTool** to konsolowe narzędzie wiersza poleceń (CLI) napisane w języku C#, służące do zarządzania strukturą (metadanymi) i danymi baz danych Firebird 5.0. Umożliwia eksportowanie definicji obiektów bazodanowych do plików SQL, a następnie odtwarzanie lub aktualizowanie bazy danych na podstawie tych plików.

## Główne Funkcje

-   **Budowanie bazy od zera (`build-db`):** Tworzy nową, pustą bazę danych Firebird , a następnie wykonuje na niej zestaw skryptów SQL w celu zbudowania pełnej struktury i zaimportowania danych początkowych.
-   **Eksport metadanych (`export-scripts`):** Łączy się z istniejącą bazą danych Firebird i eksportuje definicje jej obiektów (domeny, tabele, procedury, wyzwalacze) do czytelnych plików `.sql`, zorganizowanych w logiczną strukturę katalogów. Eksportuje również dane z tabel jako polecenia `INSERT`.
-   **Aktualizacja istniejącej bazy (`update-db`):** Wykonuje zestaw skryptów SQL na istniejącej bazie danych, umożliwiając wprowadzanie zmian i migrację schematu.

### 1. Eksportowanie schematu z istniejącej bazy

```bash
dotnet run -- export-scripts --connection-string <connStr> --output-dir <ścieżka>
```
Przykład wykorzystywany w moim środowisku w celu testowania : 
```bash
dotnet run -- export-scripts --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\DB.FDB;DataSource=localhost;Port=3050;Dialect=3;" --output-dir "C:\workspace\Sente\db\scripts\test1"
```

### 2. Budowanie nowej bazy na podstawie skryptów

```bash
dotnet run -- build-db --db-dir <ścieżka> --scripts-dir <ścieżka>
```
Przykład wykorzystywany w moim środowisku w celu testowania : 
```bash
dotnet run -- build-db --db-dir "C:\workspace\Sente\db\new" --scripts-dir "C:\workspace\Sente\db\scripts\test1"
```

### 3. Aktualizowanie istniejącej bazy

```bash
dotnet run -- update-db --connection-string <connStr> --scripts-dir <ścieżka>
```
Przykład wykorzystywany w moim środowisku w celu testowania : 
```bash
dotnet run -- update-db --connection-string "User=SYSDBA;Password=ppp123;Database=C:\workspace\Sente\db\UPDATEDB.FDB;DataSource=localhost;Port=3050;Dialect=3;Charset=UTF8;" --scripts-dir "C:\workspace\Sente\db\scripts\test1"
```

## Struktura projektu
- Program.cs: Główny plik aplikacji, odpowiedzialny za parsowanie argumentów wiersza poleceň i wywoływanie odpowiednich poleceń.
- DatabaseBuilder.cs: Zawiera logikę odpowiedzialną za tworzenie nowej bazy danych (Build) oraz aktualizowanie istniejącej (Update) poprzez wykonywanie skryptów SQL.
- MetadataExporter.cs: Implementuje logikę eksportowania metadanych z bazy. Łączy się z bazą i wykonuje zapytania do tabel systemowych Firebird (RDB$*), aby odtworzyć definicje obiektów.
- Utils.cs: Klasa pomocnicza zawierająca funkcje narzędziowe, np. do formatowania typów danych SQL i wartości.

## Testowanie

Aplikacja została przeze mnie pomyślnie przetestowana na 2 różnych przykładowych bazach danych które zostały wygenerowane w tym celu.

> 💡 **Ważna uwaga dotycząca dostępu do bazy danych**
>  W pliku DatabaseBuilder.cs aby utworzyć bazę musiałem zdefiniować connection string który zawiera sztywno wpisane hasło i użytkownika. Aby aplikacja działała poprawnie zaleca się wpisać w Properties/launchSettings.json własne dane dostępowe do bazy danych
> ```bash string connectionString = @$"User={Environment.GetEnvironmentVariable("Username") ?? "SYSDBA"};Password={Environment.GetEnvironmentVariable("Password") ?? "ppp123"};Database={dbPath};DataSource=localhost;Port=3050;Dialect=3;Charset=UTF8";```
