using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var sample = "local training value";
var hash = BCrypt.Net.BCrypt.HashPassword(sample, workFactor: 12);
Check(BCrypt.Net.BCrypt.Verify(sample, hash) && !BCrypt.Net.BCrypt.Verify("wrong", hash),
    "BCrypt: valid password accepted, wrong one rejected");
var rights = new Dictionary<string, string[]> {
    ["viewer"] = ["read"], ["operator"] = ["read", "create"], ["admin"] = ["read", "create", "delete"]
};
bool Allowed(string role, string action) => rights.TryGetValue(role, out var actions) && actions.Contains(action);
Check(Allowed("operator", "create") && !Allowed("operator", "delete") &&
    Allowed("admin", "delete") && !Allowed("unknown", "read"), "RBAC: allow and deny cases");

using var rootKey = RSA.Create(2048);
using var leafKey = RSA.Create(2048);
var now = DateTimeOffset.UtcNow;
var rootRequest = new CertificateRequest("CN=ServiceDesk Test CA", rootKey,
    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
using var root = rootRequest.CreateSelfSigned(now.AddDays(-3), now.AddDays(4));
var leafRequest = new CertificateRequest("CN=servicedesk.test", leafKey,
    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
var names = new SubjectAlternativeNameBuilder();
names.AddDnsName("servicedesk.test");
leafRequest.CertificateExtensions.Add(names.Build());
using var leaf = leafRequest.Create(root, now.AddHours(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
Check(Trusted(leaf, root, now) && leaf.MatchesHostname("servicedesk.test", false, false),
    "X.509: valid chain and hostname accepted");
Check(!leaf.MatchesHostname("other.test", false, false) && !Trusted(leaf, root, now.AddDays(2)),
    "X.509: wrong hostname and expired certificate rejected");

if (OperatingSystem.IsWindows())
{
    var bytes = Encoding.UTF8.GetBytes(sample);
    var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    var decoded = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
    Check(bytes.SequenceEqual(decoded) && !bytes.SequenceEqual(encrypted), "DPAPI: current-user roundtrip");
}
else Console.WriteLine("SKIP: DPAPI requires Windows");

static bool Trusted(X509Certificate2 leaf, X509Certificate2 root, DateTimeOffset at)
{
    using var chain = new X509Chain();
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(root);
    // This generated CA exists only for this test and has no revocation service.
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.VerificationTime = at.UtcDateTime;
    chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
    return chain.Build(leaf);
}
static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
