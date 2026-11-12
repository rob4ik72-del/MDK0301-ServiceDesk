using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Xml.Serialization;
using System.Xml;
using System.Security.Cryptography;
Console.OutputEncoding = Encoding.UTF8;
var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "CalsResults"));
Directory.CreateDirectory(output);
var card = new TicketCard { Id = 42, Title = "Принтер не печатает", Status = "Open", Priority = 3, Revision = 1 };
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var jsonPath = Path.Combine(output,"ticket.json");
var xmlPath = Path.Combine(output,"ticket.xml");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(card,jsonOptions));
var xml = new XmlSerializer(typeof(TicketCard));
using (var writer = XmlWriter.Create(xmlPath, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
    xml.Serialize(writer,card);
var fromJson = JsonSerializer.Deserialize<TicketCard>(File.ReadAllText(jsonPath))!;
using var reader = XmlReader.Create(xmlPath,new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
var fromXml = (TicketCard)xml.Deserialize(reader)!;
int checks = 0;
void Check(bool condition,string label) { if (!condition) throw new InvalidDataException(label); Console.WriteLine("PASS: "+label); checks++; }
Check(card == fromJson, "JSON round-trip: все поля сохранены");
Check(card == fromXml, "XML round-trip: все поля сохранены");
Console.WriteLine("JSON:\n"+File.ReadAllText(jsonPath));
Console.WriteLine("XML:\n"+File.ReadAllText(xmlPath));
var registry = new Registry(card);
var next = registry.Update(card with { Status = "Resolved" }, expectedRevision: 1);
Check(next.Id == card.Id && next.Revision == 2 && next.Status == "Resolved", "SSOT: постоянный ID, новая редакция 2");
bool conflict = false;
try { registry.Update(card with { Status = "Closed" }, expectedRevision:1); }
catch (InvalidOperationException) { conflict = true; }
Check(conflict && registry.Current == next, "Устаревшая редакция отклонена, данные не потеряны");
var manifest = new
{
    System = "ServiceDesk", Version = "1.1.0",
    Components = new [] {
        new { Name = "ServiceDeskApi", Technology = "ASP.NET Core 8", Role = "Единый источник статуса заявки" },
        new { Name = "OfflineBuffer", Technology = "EF Core SQLite, Polly", Role = "Доставка после восстановления сети" },
        new { Name = "CalsExchange", Technology = "System.Text.Json, XML", Role = "Обмен карточками с сохранением полей" }
    },
    Documents = new [] { "Reports/ПР06_МДК0301.docx", "ticket.json", "ticket.xml" },
    JsonSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jsonPath)))
};
File.WriteAllText(Path.Combine(output,"composition.json"),JsonSerializer.Serialize(manifest,jsonOptions));
using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"composition.json")));
Check(saved.RootElement.GetProperty("Components").GetArrayLength() == 3 &&
      saved.RootElement.GetProperty("JsonSha256").GetString() == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jsonPath))),
      "Электронный состав: 3 компонента, хеш карточки совпадает");
Console.WriteLine($"CALS: {checks}/5 PASS");
Console.WriteLine("Результаты: "+output);
public record TicketCard
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public int Priority { get; set; }
    public int Revision { get; set; }
}
public sealed class Registry(TicketCard initial)
{
    public TicketCard Current { get; private set; } = initial;
    public TicketCard Update(TicketCard proposed,int expectedRevision)
    {
        if (Current.Id != proposed.Id || Current.Revision != expectedRevision)
            throw new InvalidOperationException("Конфликт редакций");
        if (string.IsNullOrWhiteSpace(proposed.Title) || proposed.Priority is <1 or >4)
            throw new ArgumentException("Некорректные поля карточки");
        return Current = proposed with { Revision = checked(Current.Revision+1) };
    }
}
