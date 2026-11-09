using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Polly;

var address = args.FirstOrDefault() ?? "http://127.0.0.1:5000";
var file = args.ElementAtOrDefault(1) ?? "offline.db";
using var db = new LocalDb(file);
await db.Database.EnsureCreatedAsync();
if (args.ElementAtOrDefault(2) is { } title)
{
    var item = new PendingEvent { EventId = Guid.NewGuid(), Title = title };
    db.PendingEvents.Add(item);
    await db.SaveChangesAsync();
    Console.WriteLine($"Queued {item.EventId}");
}
using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
var retry = Policy.Handle<HttpRequestException>(e => e.StatusCode is null or >= HttpStatusCode.InternalServerError)
    .Or<TaskCanceledException>().WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(150 * i),
        (e, delay, i, _) => Console.WriteLine($"Retry {i}: {e.GetType().Name}"));
foreach (var item in await db.PendingEvents.Where(e => !e.Sent).ToListAsync())
{
    try
    {
        await retry.ExecuteAsync(async () =>
        {
            using var response = await http.PostAsJsonAsync("/api/v2/sync", new { item.EventId, item.Title });
            response.EnsureSuccessStatusCode();
        });
        item.Sent = true;
        await db.SaveChangesAsync();
        Console.WriteLine($"Synchronized {item.EventId}");
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"Retained {item.EventId}: {e.GetType().Name}");
    }
}
var pending = await db.PendingEvents.CountAsync(e => !e.Sent);
Console.WriteLine($"Pending: {pending}");
return pending == 0 ? 0 : 2;

class PendingEvent
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public string Title { get; set; } = "";
    public bool Sent { get; set; }
}
class LocalDb(string file) : DbContext
{
    public DbSet<PendingEvent> PendingEvents => Set<PendingEvent>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = file }.ToString());
    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.Entity<PendingEvent>().HasIndex(e => e.EventId).IsUnique();
}
