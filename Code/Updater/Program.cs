using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    if (args.Length != 3) throw new ArgumentException("Usage: Updater manifest.json package-file target-file");
    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(args[0]),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new FormatException("Empty manifest");
    var next = SemVersion.Parse(manifest.Version);
    var target = Path.GetFullPath(args[2]);
    var state = target + ".version";
    var current = SemVersion.Parse(File.Exists(state) ? File.ReadAllText(state).Trim() : "1.0.0");
    if (next.CompareTo(current) <= 0) { Console.WriteLine("Already current or newer"); return 0; }
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var temp = target + "." + Guid.NewGuid().ToString("N") + ".new";
    var stagedState = temp + ".version";
    try
    {
        File.Copy(args[1], temp);
        using (var input = File.OpenRead(temp))
        {
            var actual = Convert.ToHexString(SHA256.HashData(input));
            if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 mismatch; update rejected");
        }
        File.WriteAllText(stagedState, manifest.Version);
        File.Move(temp, target, overwrite: true);
        File.Move(stagedState, state, overwrite: true);
        Console.WriteLine($"Updated to {manifest.Version}; SHA-256 verified");
    }
    finally { File.Delete(temp); File.Delete(stagedState); }
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or JsonException or ArgumentException
    or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
record Manifest(string Version, string Sha256);

sealed class SemVersion(string[] core, string[] pre) : IComparable<SemVersion>
{
    private readonly string[] numbers = core;
    private readonly string[] qualifiers = pre;
    public static SemVersion Parse(string text)
    {
        var m = Regex.Match(text, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");
        if (!m.Success) throw new FormatException("Invalid SemVer");
        var pre = m.Groups[4].Success ? m.Groups[4].Value.Split('.') : [];
        if (pre.Any(x => Numeric(x) && x.Length > 1 && x[0] == '0')) throw new FormatException("Invalid SemVer prerelease");
        return new SemVersion([m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value], pre);
    }
    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < 3; i++)
        {
            var cmp = CompareNumeric(numbers[i], other.numbers[i]);
            if (cmp != 0) return cmp;
        }
        if (qualifiers.Length == 0 || other.qualifiers.Length == 0)
            return (qualifiers.Length == 0).CompareTo(other.qualifiers.Length == 0);
        for (var i = 0; i < Math.Min(qualifiers.Length, other.qualifiers.Length); i++)
        {
            var a = qualifiers[i]; var b = other.qualifiers[i];
            var cmp = Numeric(a) && Numeric(b) ? CompareNumeric(a, b) :
                Numeric(a) != Numeric(b) ? (Numeric(a) ? -1 : 1) : string.CompareOrdinal(a, b);
            if (cmp != 0) return cmp;
        }
        return qualifiers.Length.CompareTo(other.qualifiers.Length);
    }
    private static bool Numeric(string s) => s.All(char.IsAsciiDigit);
    private static int CompareNumeric(string a, string b) =>
        a.Length == b.Length ? string.CompareOrdinal(a, b) : a.Length.CompareTo(b.Length);
}
