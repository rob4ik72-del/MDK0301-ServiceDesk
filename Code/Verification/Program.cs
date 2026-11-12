using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var profile = args.Length >= 2 && args[0] == "--pr" ? args[1].PadLeft(2, '0') : null;
if (profile is not null && profile is not ("04" or "05" or "06" or "07" or "08"))
    throw new ArgumentException("Выберите профиль ПР 4–8.");
var root = FindRoot(profile is null ? args.FirstOrDefault() : null);
var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
var output = Path.GetFullPath((profile is null ? args.ElementAtOrDefault(1) : null) ?? Path.Combine(root, "RunResults"));
Directory.CreateDirectory(output);
var staging = Path.Combine(Path.GetTempPath(), "ServiceDesk-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(staging);
var groups = new Dictionary<string, List<(string Label, string Output)>>();
var total = 0;
var port = GetFreePort();
var address = $"http://127.0.0.1:{port}";
Process? server = null;
try
{
    await Run("08", "Сохранение заявки без сети", "OfflineBuffer", 2, "Pending: 1",
        address, Path.Combine(staging, "offline.db"), "Офлайн заявка");
    server = Start("ServiceDeskApi", ["--urls", address, "--Logging:LogLevel:Default", "Warning"]);
    var discardOut = server.StandardOutput.ReadToEndAsync();
    var discardErr = server.StandardError.ReadToEndAsync();
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var ready = false;
    for (var i = 0; i < 60; i++)
    {
        if (server.HasExited) throw new Exception(await discardErr);
        try { ready = (await http.GetAsync(address + "/health")).IsSuccessStatusCode; } catch (HttpRequestException) { }
        if (ready) break;
        await Task.Delay(150);
    }
    if (!ready) throw new Exception("API did not start");
    await Run("04", "Обратная совместимость API", "ApiCompatibilityTests", 0, "duplicate event", address);
    File.WriteAllText(Path.Combine(output, "api-v1.json"), await http.GetStringAsync(address + "/api/v1/tickets"));
    File.WriteAllText(Path.Combine(output, "api-v2.json"), await http.GetStringAsync(address + "/api/v2/tickets"));
    await Run("08", "Отправка после восстановления соединения", "OfflineBuffer", 0, "Pending: 0",
        address, Path.Combine(staging, "offline.db"));
    if (!(await http.GetStringAsync(address + "/api/v2/tickets")).Contains("Офлайн заявка") &&
        !JsonDocument.Parse(await http.GetStringAsync(address + "/api/v2/tickets")).RootElement
            .EnumerateArray().Any(t => t.GetProperty("title").GetString() == "Офлайн заявка"))
        throw new Exception("Offline event missing on server");
    await Run("05", "Миграция SQLite", "Operations", 0, "keeps both rows",
        "migration", Path.Combine(root, "Code", "sample-data"), Path.Combine(staging, "migration.db"));
    await Run("07", "Стендовые проверки SAT", "Operations", 0, "4/4 PASS",
        "sat", address, Path.Combine(output, "SAT.md"));
    await Run("06", "Обмен CALS JSON/XML и состав изделия", "CalsExchange", 0, "CALS: 5/5 PASS", Path.Combine(staging, "cals"));
    var pack = Path.Combine(staging, "package.txt");
    var manifest = Path.Combine(staging, "manifest.json");
    var target = Path.Combine(staging, "installed.txt");
    File.WriteAllText(pack, "ServiceDesk release 1.1.0");
    await Run("08", "Формирование манифеста", "Operations", 0, "Manifest written", "manifest", "1.1.0", pack, manifest);
    await Run("08", "Установка версии 1.1.0", "Updater", 0, "Updated to 1.1.0", manifest, pack, target);
    await Run("08", "Повторный запуск без переустановки", "Updater", 0, "Already current", manifest, pack, target);
    await Run("08", "Манифест для версии 1.2.0", "Operations", 0, "Manifest written", "manifest", "1.2.0", pack, manifest);
    File.AppendAllText(pack, " modified");
    await Run("08", "Отклонение изменённого пакета", "Updater", 1, "SHA-256 mismatch", manifest, pack, target);
    if (File.ReadAllText(target) != "ServiceDesk release 1.1.0" || File.ReadAllText(target + ".version") != "1.1.0")
        throw new Exception("Rejected update modified installed files");
    await Run("08", "Проверки защиты", "SecurityDemo", 0,
        OperatingSystem.IsWindows() ? "PASS: DPAPI" : "SKIP: DPAPI");
    var archive = Path.Combine(output, "backup.zip");
    File.Delete(archive);
    await Run("08", "Резервное копирование", "Operations", 0, "SHA-256",
        "backup", Path.Combine(root, "Code", "sample-data"), archive);
    using var zip = ZipFile.OpenRead(archive);
    if (zip.GetEntry("schema_v1.sql") is null ||
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) != File.ReadAllText(archive + ".sha256"))
        throw new Exception("Backup validation failed");
    foreach (var (number, runs) in groups)
    {
        var body = string.Join("", runs.Select(x => $"<h2>{H(x.Label)}</h2><pre>{H(x.Output)}</pre>"));
        File.WriteAllText(Path.Combine(output, $"PR{number}-run.html"), $$"""
            <!doctype html><html lang="ru"><meta charset="utf-8"><title>ПР {{number}} — запуск</title>
            <style>body{margin:32px;font:18px Arial;color:#132a40;background:#f4f7fb}
            h1{font-size:30px} h2{font-size:19px;margin:14px 0 5px}
            pre{white-space:pre-wrap;overflow-wrap:anywhere;font:16px monospace;background:white;border-left:4px solid #2b6ea5;padding:10px;margin:0}</style>
            <h1>Служба заявок · ПР {{number}}</h1><p>Вывод проверочного запуска C#</p>{{body}}</html>
            """);
    }
    Console.WriteLine($"Completed {total} command scenarios; expected exit codes and outputs verified.");
    return 0;
}
finally
{
    if (server is not null)
    {
        if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
        server.Dispose();
    }
    Directory.Delete(staging, recursive: true);
}

Process Start(string project, string[] command)
{
    var dll = Path.Combine(root, "Code", project, "bin", configuration, "net8.0", project + ".dll");
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    { WorkingDirectory = staging, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    start.ArgumentList.Add(dll);
    foreach (var argument in command) start.ArgumentList.Add(argument);
    return Process.Start(start)!;
}
async Task Run(string number, string label, string project, int exit, string required, params string[] command)
{
    using var p = Start(project, command);
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    try { await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(45)).Token); }
    catch (OperationCanceledException) { p.Kill(entireProcessTree: true); throw; }
    var log = ((await stdout) + (await stderr)).Replace(staging + Path.DirectorySeparatorChar, "")
        .Replace(output + Path.DirectorySeparatorChar, "").Trim();
    File.WriteAllText(Path.Combine(output, $"{++total:00}-{project}.log"), log);
    if (p.ExitCode != exit || !log.Contains(required, StringComparison.Ordinal))
        throw new Exception($"{label}: exit={p.ExitCode}, expected={exit}\n{log}");
    if (!groups.TryGetValue(number, out var reports)) groups.Add(number, reports = []);
    reports.Add((label, log));
    if (profile is null || profile == number)
    {
        Console.WriteLine($"\nПР {number}: {label}\n{log}");
        Console.WriteLine($"PASS {label}: exit={p.ExitCode}");
    }
}
static string H(string s) => WebUtility.HtmlEncode(s);
static int GetFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
    finally { listener.Stop(); }
}

static string FindRoot(string? supplied)
{
    if (supplied is not null)
    {
        var path = Path.GetFullPath(supplied);
        if (File.Exists(Path.Combine(path, "ServiceDesk.sln"))) return path;
        throw new DirectoryNotFoundException("Не найден ServiceDesk.sln: " + path);
    }
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ServiceDesk.sln"))) return dir.FullName;
    throw new DirectoryNotFoundException("Откройте решение ServiceDesk.sln.");
}
