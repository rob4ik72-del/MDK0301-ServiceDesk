using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TicketStore>();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "1.0.0" }));
// V1 retains original response contract for legacy clients.
app.MapGet("/api/v1/tickets", (TicketStore db) => Results.Ok(db.All().Select(t => new TicketV1(t.Id, t.Title, t.Status))));
app.MapGet("/api/v1/tickets/{id:int}", (int id, TicketStore db) => db.Get(id) is { } t ? Results.Ok(new TicketV1(t.Id,t.Title,t.Status)) : Results.NotFound());
app.MapPost("/api/v1/tickets", (NewTicket request, TicketStore db) =>
{
    if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Title is required");
    var t = db.Add(request.Title.Trim());
    return Results.Created($"/api/v1/tickets/{t.Id}", new TicketV1(t.Id,t.Title,t.Status));
});
// V2 extends response with Priority; V1 endpoints are unchanged.
app.MapGet("/api/v2/tickets", (TicketStore db) => Results.Ok(db.All().Select(t => new TicketV2(t.Id,t.Title,t.Status,t.Priority))));
app.MapGet("/api/v2/tickets/{id:int}", (int id, TicketStore db) => db.Get(id) is { } t ? Results.Ok(new TicketV2(t.Id,t.Title,t.Status,t.Priority)) : Results.NotFound());
app.MapPost("/api/v2/tickets", (NewTicketV2 request, TicketStore db) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || request.Priority is < 1 or > 4)
        return Results.BadRequest("Title and priority 1..4 are required");
    var t = db.Add(request.Title.Trim(), request.Priority);
    return Results.Created($"/api/v2/tickets/{t.Id}", new TicketV2(t.Id,t.Title,t.Status,t.Priority));
});
// The idempotency key identifies a client event within this running service.
app.MapPost("/api/v2/sync", (OfflineTicket request, TicketStore db) =>
{
    if (request.EventId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title))
        return Results.BadRequest("EventId and title are required");
    return Results.Ok(db.Sync(request.EventId, request.Title.Trim()));
});
app.MapGet("/", () => Results.Content("""
<!doctype html><html lang="ru"><meta charset="utf-8">
<title>Служба заявок — API</title>
<style>body{font:20px Arial;margin:45px;color:#19314a;background:#f6f8fa}
pre{background:white;padding:16px;border:1px solid #ccd6de;font:17px monospace}
h1{font-size:33px}h2{font-size:22px}</style>
<h1>Служба заявок</h1><p>МДК 03.01 · ПР 4 · Ответы работающего REST API</p>
<div id="results"></div>
<script>
(async () => {
 for (const path of ['/health','/api/v1/tickets','/api/v2/tickets']) {
   const response = await fetch(path);
   const title = document.createElement('h2');
   title.textContent = 'GET ' + path + ' — HTTP ' + response.status;
   const result = document.createElement('pre');
   result.textContent = JSON.stringify(await response.json(),null,2);
   document.querySelector('#results').append(title,result);
 }
})();
</script></html>
""", "text/html; charset=utf-8"));
app.Run();
public partial class Program { }
record OfflineTicket(Guid EventId, string Title);
record NewTicket(string Title);
record NewTicketV2(string Title, int Priority);
record TicketV1(int Id, string Title, string Status);
record TicketV2(int Id, string Title, string Status, int Priority);
record Ticket(int Id, string Title, string Status, int Priority);
class TicketStore
{
    private readonly ConcurrentDictionary<int,Ticket> _items=new();
    private int _next=0;
    private readonly Dictionary<Guid, Ticket> _synced = new();
    public Ticket Sync(Guid eventId, string title)
    {
        lock (_synced)
        {
            if (!_synced.TryGetValue(eventId, out var ticket))
                _synced.Add(eventId, ticket = Add(title));
            return ticket;
        }
    }
    public IEnumerable<Ticket> All()=>_items.Values.OrderBy(x=>x.Id);
    public Ticket? Get(int id)=>_items.TryGetValue(id,out var t)?t:null;
    public Ticket Add(string title,int priority=3)
    {
        var t=new Ticket(Interlocked.Increment(ref _next),title,"Open",priority);
        _items[t.Id]=t; return t;
    }
}
