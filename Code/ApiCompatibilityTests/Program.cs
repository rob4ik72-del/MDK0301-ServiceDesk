using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

var address = args.FirstOrDefault() ?? "http://127.0.0.1:5000";
using var http = new HttpClient { BaseAddress = new Uri(address) };
var created = await http.PostAsJsonAsync("/api/v1/tickets", new { title = "Проверить принтер" });
Assert(created.StatusCode == HttpStatusCode.Created, "POST v1 returns 201");
var ticket = await created.Content.ReadFromJsonAsync<OldTicket>();
Assert(ticket is { Id: > 0, Title: "Проверить принтер", Status: "Open" }, "old client deserializes v1");
var id = ticket!.Id;
using var v1 = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/tickets/{id}"));
Assert(v1.RootElement.EnumerateObject().Select(p => p.Name).Order()
    .SequenceEqual(new[] { "id", "status", "title" }), "v1 field set is unchanged");
using var v2 = JsonDocument.Parse(await http.GetStringAsync($"/api/v2/tickets/{id}"));
Assert(v2.RootElement.GetProperty("priority").GetInt32() == 3, "v2 adds default priority");
Assert((await http.GetAsync("/api/v1/tickets/2147483647")).StatusCode == HttpStatusCode.NotFound, "missing ticket returns 404");
Assert((await http.PostAsJsonAsync("/api/v2/tickets", new { title = "test", priority = 9 })).StatusCode
    == HttpStatusCode.BadRequest, "v2 rejects invalid priority");
var key = Guid.NewGuid();
var item = new { eventId = key, title = "Тест повторной доставки" };
var first = await http.PostAsJsonAsync("/api/v2/sync", item);
var second = await http.PostAsJsonAsync("/api/v2/sync", item);
first.EnsureSuccessStatusCode(); second.EnsureSuccessStatusCode();
Assert((await first.Content.ReadFromJsonAsync<OldTicket>())!.Id ==
    (await second.Content.ReadFromJsonAsync<OldTicket>())!.Id, "duplicate event keeps the same ticket ID");
static void Assert(bool passed, string label)
{
    if (!passed) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
record OldTicket(int Id, string Title, string Status);
