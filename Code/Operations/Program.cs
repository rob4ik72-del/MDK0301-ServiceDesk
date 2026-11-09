using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

try
{
    switch (args)
    {
        case ["manifest", var version, var file, var output]:
            File.WriteAllText(output, JsonSerializer.Serialize(new { Version = version, Sha256 = Hash(file) }));
            Console.WriteLine("Manifest written: " + output);
            return 0;
        case ["backup", var source, var destination]:
            if (Path.GetFullPath(destination).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Archive must be outside source directory");
            ZipFile.CreateFromDirectory(source, destination);
            File.WriteAllText(destination + ".sha256", Hash(destination));
            using (var zip = ZipFile.OpenRead(destination))
                Console.WriteLine($"Backup: {zip.Entries.Count} entries; SHA-256 {Hash(destination)}");
            return 0;
        case ["sat", var address, var output]:
            return await Sat(address, output);
        case ["migration", var directory, var database]:
            return Migration(directory, database);
        default:
            Console.Error.WriteLine("Operations: manifest VERSION FILE JSON | backup SOURCE ZIP | sat URL MARKDOWN | migration SQL_DIR DB");
            return 1;
    }
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}
static async Task<int> Sat(string address, string output)
{
    using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
    var checks = new List<(string Scenario, bool Passed, string Detail)>();
    foreach (var (route, expected) in new[]
    {
        ("/health", HttpStatusCode.OK),
        ("/api/v1/tickets", HttpStatusCode.OK),
        ("/api/v2/tickets", HttpStatusCode.OK),
        ("/api/v1/tickets/2147483647", HttpStatusCode.NotFound)
    })
    {
        try
        {
            using var response = await http.GetAsync(route);
            var correctBody = true;
            if (expected != HttpStatusCode.NotFound)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                correctBody = route == "/health" ? json.RootElement.GetProperty("status").GetString() == "ok"
                    : json.RootElement.ValueKind == JsonValueKind.Array;
            }
            checks.Add((route, response.StatusCode == expected && correctBody, $"HTTP {(int)response.StatusCode}"));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        { checks.Add((route, false, e.GetType().Name)); }
    }
    var passed = checks.Count(x => x.Passed);
    var lines = new List<string> { "# Протокол SAT — стенд", "", $"Дата: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", "",
        "| Сценарий | Результат | Ответ |", "| --- | --- | --- |" };
    foreach (var x in checks) lines.Add($"| GET {x.Scenario} | {(x.Passed ? "PASS" : "FAIL")} | {x.Detail} |");
    lines.Add($"\nПройдено: {passed}/{checks.Count}. Согласование заказчиком проводится отдельно.");
    File.WriteAllLines(output, lines);
    Console.WriteLine($"SAT: {passed}/{checks.Count} PASS");
    return passed == checks.Count ? 0 : 2;
}
static int Migration(string directory, string database)
{
    if (File.Exists(database)) throw new ArgumentException("Use a new database file for the migration demo");
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString());
    connection.Open();
    void Exec(string sql) { using var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    long Read(string sql) { using var cmd = connection.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
    Exec(File.ReadAllText(Path.Combine(directory, "schema_v1.sql")));
    Exec("INSERT INTO tickets (title,status,legacy_category) VALUES ('Existing ticket','Open','Hardware')");
    Exec(File.ReadAllText(Path.Combine(directory, "migrate_expand.sql")));
    Exec("INSERT INTO tickets (title,status) VALUES ('Old client ticket','Open')");
    if (Read("SELECT COUNT(*) FROM tickets WHERE priority=3") != 2) throw new InvalidOperationException("Expand failed");
    Console.WriteLine("PASS: original row preserved and v1 insert works after Expand");
    Exec(File.ReadAllText(Path.Combine(directory, "migrate_contract.sql")));
    if (Read("SELECT COUNT(*) FROM tickets WHERE priority=3") != 2 ||
        Read("SELECT COUNT(*) FROM pragma_table_info('tickets') WHERE name='legacy_category'") != 0)
        throw new InvalidOperationException("Contract failed");
    Console.WriteLine("PASS: Contract removes the legacy column and keeps both rows");
    return 0;
}
